using System.Diagnostics;
using System.Globalization;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public class NvidiaSmiFallback : IHardwareMonitor
{
    public string ProviderName => "NVIDIA-SMI Utility Fallback";
    public bool IsAvailable => _isAvailable;

    private readonly string _smiPath;
    private readonly bool _isAvailable;
    private DateTime _lastQueryTime = DateTime.MinValue;
    private HardwareMetrics? _cachedMetrics;
    private readonly TimeSpan _throttleInterval = TimeSpan.FromSeconds(2);

    public NvidiaSmiFallback()
    {
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");

        if (File.Exists(defaultPath))
        {
            _smiPath = defaultPath;
            _isAvailable = true;
        }
        else if (File.Exists(programFiles))
        {
            _smiPath = programFiles;
            _isAvailable = true;
        }
        else
        {
            // Probe PATH
            _smiPath = "nvidia-smi.exe";
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = _smiPath,
                    Arguments = "--help",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });
                p?.WaitForExit(500);
                _isAvailable = p != null && p.ExitCode == 0;
            }
            catch
            {
                _isAvailable = false;
            }
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<HardwareMetrics> GetMetricsAsync()
    {
        if (!_isAvailable || string.IsNullOrEmpty(_smiPath))
        {
            return new HardwareMetrics { Timestamp = DateTime.Now };
        }

        var now = DateTime.Now;
        if (_cachedMetrics != null && (now - _lastQueryTime) < _throttleInterval)
        {
            return _cachedMetrics;
        }

        try
        {
            // Allow 2500ms timeout for laptop discrete GPUs waking up from sleep
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
            var startInfo = new ProcessStartInfo
            {
                FileName = _smiPath,
                Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu,memory.total,memory.free,memory.used,clocks.current.graphics,power.draw --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Trim().Split(',');
                if (parts.Length >= 8)
                {
                    var metrics = new HardwareMetrics
                    {
                        Timestamp = now,
                        GpuName = parts[0].Trim(),
                        SensorSourceDetails = ProviderName
                    };

                    if (double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double temp))
                        metrics.GpuTemperatureC = temp;

                    if (double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double util))
                        metrics.GpuUsagePercent = util;

                    if (double.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double totalMb))
                        metrics.VramTotalGb = Math.Round(totalMb / 1024.0, 1);

                    if (double.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double freeMb))
                        metrics.VramAvailableGb = Math.Round(freeMb / 1024.0, 1);

                    if (double.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double usedMb))
                        metrics.VramUsedGb = Math.Round(usedMb / 1024.0, 1);

                    if (metrics.VramTotalGb.HasValue && metrics.VramUsedGb.HasValue && metrics.VramTotalGb > 0)
                        metrics.VramUsagePercent = Math.Round((metrics.VramUsedGb.Value / metrics.VramTotalGb.Value) * 100.0, 1);

                    if (double.TryParse(parts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double clock))
                        metrics.GpuClockMhz = clock;

                    if (double.TryParse(parts[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double power))
                    {
                        if ((power >= 450.0 && power <= 620.0) ||
                            (power > 300.0 && (metrics.GpuUsagePercent.GetValueOrDefault(0) < 15 || metrics.GpuTemperatureC.GetValueOrDefault(0) < 55)))
                        {
                            metrics.GpuPowerWatts = 0.0;
                        }
                        else if (power > 0.0 && power <= 650.0)
                        {
                            metrics.GpuPowerWatts = power;
                        }
                        else
                        {
                            metrics.GpuPowerWatts = 0.0;
                        }
                    }

                    _cachedMetrics = metrics;
                    _lastQueryTime = now;
                    return metrics;
                }
            }
        }
        catch
        {
            // Timeout or error: return last cached or empty
        }

        return _cachedMetrics ?? new HardwareMetrics { Timestamp = now };
    }

    public void Dispose() { }
}

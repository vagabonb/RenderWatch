using System.Collections.ObjectModel;
using System.Diagnostics;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public sealed class NotchTelemetry : ViewModelBase
{
    private static readonly TimeSpan MaxSampleGap = TimeSpan.FromSeconds(6);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SustainedSignal _cpu = new();
    private readonly SustainedSignal _gpu = new();
    private readonly SustainedSignal _memory = new();
    private TimeSpan? _lastSample;

    public bool CpuHot => _cpu.Active;
    public bool GpuHot => _gpu.Active;
    public bool MemoryHigh => _memory.Active;
    public ObservableCollection<PeakReading> Peaks { get; } = new();

    internal void Observe(HardwareMetrics metrics, AppSettings settings) =>
        Observe(metrics, settings, _clock.Elapsed);

    internal void Observe(HardwareMetrics metrics, AppSettings settings, TimeSpan elapsed)
    {
        if (_lastSample is { } last && elapsed - last > MaxSampleGap) ResetAlerts();
        _lastSample = elapsed;
        var cpuTemp = Reading(metrics.CpuTemperatureC, 5);
        var gpuTemp = Reading(metrics.GpuTemperatureC, 5);
        var cpuPeak = Check(_cpu, cpuTemp, settings.CpuTempThresholdC, nameof(CpuHot));
        var gpuPeak = Check(_gpu, gpuTemp, settings.GpuTempThresholdC, nameof(GpuHot));
        var ramPeak = Check(_memory, Reading(metrics.RamUsagePercent), 95, nameof(MemoryHigh));
        if (!cpuPeak && !gpuPeak && !ramPeak) return;

        var reasons = new List<string>(3);
        if (cpuPeak) reasons.Add($"CPU {cpuTemp:0}°C");
        if (gpuPeak) reasons.Add($"GPU {gpuTemp:0}°C");
        if (ramPeak) reasons.Add($"RAM {metrics.RamUsagePercent:0}%");
        string values = $"CPU {Percent(metrics.CpuUsagePercent)} · GPU {Percent(metrics.GpuUsagePercent)} · RAM {Percent(metrics.RamUsagePercent)}";
        string disks = $"Диски ↓{Rate(metrics.DiskReadBytesPerSecond)} ↑{Rate(metrics.DiskWriteBytesPerSecond)} МБ/с";
        Peaks.Insert(0, new PeakReading(metrics.Timestamp.ToString("HH:mm:ss"), string.Join(" · ", reasons), values, disks));
        if (Peaks.Count > 8) Peaks.RemoveAt(Peaks.Count - 1);

        bool Check(SustainedSignal signal, double value, double limit, string property)
        {
            bool wasActive = signal.Active;
            signal.Update(value, limit, elapsed);
            if (signal.Active != wasActive) OnPropertyChanged(property);
            return signal.Active && !wasActive;
        }
    }

    internal void ExpireAlerts()
    {
        if (_lastSample is not { } last || _clock.Elapsed - last <= MaxSampleGap) return;
        ResetAlerts();
        _lastSample = null;
    }

    private void ResetAlerts()
    {
        Reset(_cpu, nameof(CpuHot));
        Reset(_gpu, nameof(GpuHot));
        Reset(_memory, nameof(MemoryHigh));

        void Reset(SustainedSignal signal, string property)
        {
            bool wasActive = signal.Active;
            signal.Reset();
            if (wasActive) OnPropertyChanged(property);
        }
    }

    private static double Reading(double? value, double minimum = 0) =>
        value is double number && double.IsFinite(number) && number >= minimum ? number : double.NaN;
    private static string Percent(double? value) => value.HasValue ? $"{value:0}%" : "—";
    private static string Rate(double? value) => value.HasValue ? $"{value / 1048576:0.##}" : "—";

    public sealed record PeakReading(string Time, string Title, string Load, string Disk);

    private sealed class SustainedSignal
    {
        private TimeSpan? _since;
        public bool Active { get; private set; }

        public void Update(double value, double limit, TimeSpan now)
        {
            if (!double.IsFinite(value) || value < (Active ? limit - 4 : limit))
            {
                Reset();
                return;
            }
            _since ??= now;
            Active = now - _since.Value >= TimeSpan.FromSeconds(4);
        }

        public void Reset()
        {
            _since = null;
            Active = false;
        }
    }
}

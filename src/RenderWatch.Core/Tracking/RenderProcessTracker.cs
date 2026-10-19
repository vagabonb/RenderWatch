using System.Diagnostics;
using System.Text.RegularExpressions;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Tracking;

public class RenderProcessTracker : IRenderTracker
{
    private RenderProfile _currentProfile = new() { Id = "auto", Name = "Автоопределение" };
    private RenderStatus _currentStatus = RenderStatus.Idle;
    private readonly HangDetector _hangDetector = new();

    private DateTime? _renderStartTime;
    private string _activeAppName = "Не обнаружено";
    private string _activeSceneName = "Безымянная сцена";
    private string _lastDetectionSource = "Нет активных процессов";
    private int _trackedProcessId = -1;

    // Session stats accumulator
    private double _cpuSum;
    private int _cpuCount;
    private double _peakCpu;
    private double _gpuSum;
    private int _gpuCount;
    private double _peakGpu;
    private double _maxCpuTemp;
    private double _maxGpuTemp;
    private double _peakRam;
    private double _peakVram;

    // Log tracking
    private long _lastLogFilePosition;
    private string? _lastMonitoredLogPath;

    public RenderProfile CurrentProfile
    {
        get => _currentProfile;
        set
        {
            _currentProfile = value;
            _hangDetector.Reset();
        }
    }

    public RenderStatus CurrentStatus => _currentStatus;

    public TrackerUpdateResult Update(HardwareMetrics currentMetrics, int hangTimeoutSec)
    {
        var result = new TrackerUpdateResult();
        var prevStatus = _currentStatus;

        // 1. Find active target processes
        Process? mainProc = null;
        Process? assocProc = null;

        var mainNames = _currentProfile.MainProcessNames.Count > 0
            ? _currentProfile.MainProcessNames
            : new List<string> { "blender", "blender-app", "3dsmax", "revit", "cinema 4d" };

        foreach (var name in mainNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                mainProc = procs[0];
                // Clean up remaining handles
                for (int i = 1; i < procs.Length; i++) procs[i].Dispose();
                break;
            }
        }

        foreach (var name in _currentProfile.AssociatedProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                assocProc = procs[0];
                for (int i = 1; i < procs.Length; i++) procs[i].Dispose();
                break;
            }
        }

        Process? activeProc = assocProc ?? mainProc;

        if (activeProc != null)
        {
            _activeAppName = FormatProcessName(mainProc?.ProcessName ?? activeProc.ProcessName);
            _trackedProcessId = activeProc.Id;

            // Extract scene name from window title if available
            if (mainProc != null)
            {
                string title = GetProcessWindowTitle(mainProc);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    _activeSceneName = ExtractSceneName(title, _activeAppName);
                }
            }

            // Check for log file completion events if configured
            bool logCompleted = CheckLogFileForCompletion(_currentProfile.LogFilePathPattern, _currentProfile.LogCompletionPatterns);

            // Check hang detection
            bool isFrozen = _hangDetector.CheckIfProcessFrozen(activeProc, hangTimeoutSec);

            if (isFrozen)
            {
                _currentStatus = RenderStatus.PossiblyFrozen;
                _lastDetectionSource = $"Сигнал Windows 'Не отвечает' ({activeProc.ProcessName}.exe)";
            }
            else if (logCompleted)
            {
                _currentStatus = RenderStatus.Completed;
                _lastDetectionSource = "Событие завершения из журнала логов";
            }
            else
            {
                // Active process exists and is healthy
                bool hasHeavyLoad = (currentMetrics.CpuUsagePercent ?? 0) >= _currentProfile.CpuThresholdPercent
                                 || (currentMetrics.GpuUsagePercent ?? 0) >= _currentProfile.GpuThresholdPercent
                                 || assocProc != null;

                if (hasHeavyLoad)
                {
                    if (_currentStatus != RenderStatus.Rendering)
                    {
                        _renderStartTime ??= DateTime.Now;
                        _currentStatus = RenderStatus.Rendering;
                        _lastDetectionSource = assocProc != null
                            ? $"Выделенный процесс рендера ({assocProc.ProcessName}.exe)"
                            : $"Нагрузка оборудования ({activeProc.ProcessName}.exe)";
                    }

                    // Accumulate stats
                    AccumulateStats(currentMetrics);
                }
                else
                {
                    // Process exists, but load is low
                    if (_currentStatus == RenderStatus.Rendering)
                    {
                        // Was rendering, now load dropped: could be possibly completed or waiting for next frame
                        _currentStatus = RenderStatus.PossiblyCompleted;
                        _lastDetectionSource = "Снижение нагрузки рендер-процесса";
                    }
                    else if (_currentStatus != RenderStatus.PossiblyCompleted)
                    {
                        _currentStatus = RenderStatus.Idle;
                        _lastDetectionSource = $"Процесс ожидает ({activeProc.ProcessName}.exe)";
                    }
                }
            }
        }
        else
        {
            // No process found
            if (_currentStatus == RenderStatus.Rendering || _currentStatus == RenderStatus.PossiblyCompleted)
            {
                // Process terminated after rendering
                _currentStatus = RenderStatus.Completed;
                _lastDetectionSource = "Рендер-процесс завершил работу";

                // Generate completed session
                result.CompletedSession = CreateCompletedSession(RenderStatus.Completed);
                ResetSession();
            }
            else
            {
                _currentStatus = RenderStatus.Idle;
                _lastDetectionSource = "Нет активных процессов рендера";
                _activeAppName = "Не обнаружено";
                _activeSceneName = "Безымянная сцена";
                _hangDetector.Reset();
            }
        }

        // Clean up process handles
        mainProc?.Dispose();
        assocProc?.Dispose();

        result.Status = _currentStatus;
        result.AppName = _activeAppName;
        result.SceneName = _activeSceneName;
        result.StatusDetectionSource = _lastDetectionSource;
        result.StartTime = _renderStartTime;
        result.ElapsedDuration = _renderStartTime.HasValue ? DateTime.Now - _renderStartTime.Value : TimeSpan.Zero;
        result.StatusChanged = prevStatus != _currentStatus;

        if (result.StatusChanged)
        {
            result.StatusChangeReason = $"{prevStatus.ToDisplayName()} ➔ {_currentStatus.ToDisplayName()} ({_lastDetectionSource})";
        }

        return result;
    }

    private void AccumulateStats(HardwareMetrics metrics)
    {
        if (metrics.CpuUsagePercent.HasValue)
        {
            _cpuSum += metrics.CpuUsagePercent.Value;
            _cpuCount++;
            if (metrics.CpuUsagePercent.Value > _peakCpu) _peakCpu = metrics.CpuUsagePercent.Value;
        }

        if (metrics.GpuUsagePercent.HasValue)
        {
            _gpuSum += metrics.GpuUsagePercent.Value;
            _gpuCount++;
            if (metrics.GpuUsagePercent.Value > _peakGpu) _peakGpu = metrics.GpuUsagePercent.Value;
        }

        if (metrics.CpuTemperatureC.HasValue && metrics.CpuTemperatureC.Value > _maxCpuTemp)
            _maxCpuTemp = metrics.CpuTemperatureC.Value;

        if (metrics.GpuTemperatureC.HasValue && metrics.GpuTemperatureC.Value > _maxGpuTemp)
            _maxGpuTemp = metrics.GpuTemperatureC.Value;

        if (metrics.RamUsedGb.HasValue && metrics.RamUsedGb.Value > _peakRam)
            _peakRam = metrics.RamUsedGb.Value;

        if (metrics.VramUsedGb.HasValue && metrics.VramUsedGb.Value > _peakVram)
            _peakVram = metrics.VramUsedGb.Value;
    }

    public void ForceCompleteCurrentSession(string reason)
    {
        if (_renderStartTime.HasValue)
        {
            ResetSession();
            _currentStatus = RenderStatus.Idle;
            _lastDetectionSource = reason;
        }
    }

    private RenderSession CreateCompletedSession(RenderStatus finalStatus)
    {
        var duration = _renderStartTime.HasValue ? DateTime.Now - _renderStartTime.Value : TimeSpan.Zero;
        return new RenderSession
        {
            SceneName = _activeSceneName,
            AppName = _activeAppName,
            ProfileName = _currentProfile.Name,
            StartTime = _renderStartTime ?? DateTime.Now.Subtract(duration),
            EndTime = DateTime.Now,
            Duration = duration,
            FinalStatus = finalStatus,
            StatusDetectionSource = _lastDetectionSource,
            AvgCpuUsage = _cpuCount > 0 ? Math.Round(_cpuSum / _cpuCount, 1) : null,
            PeakCpuUsage = _peakCpu > 0 ? Math.Round(_peakCpu, 1) : null,
            AvgGpuUsage = _gpuCount > 0 ? Math.Round(_gpuSum / _gpuCount, 1) : null,
            PeakGpuUsage = _peakGpu > 0 ? Math.Round(_peakGpu, 1) : null,
            MaxCpuTemp = _maxCpuTemp > 0 ? Math.Round(_maxCpuTemp, 0) : null,
            MaxGpuTemp = _maxGpuTemp > 0 ? Math.Round(_maxGpuTemp, 0) : null,
            PeakRamGb = _peakRam > 0 ? Math.Round(_peakRam, 1) : null,
            PeakVramGb = _peakVram > 0 ? Math.Round(_peakVram, 1) : null
        };
    }

    private void ResetSession()
    {
        _renderStartTime = null;
        _cpuSum = 0;
        _cpuCount = 0;
        _peakCpu = 0;
        _gpuSum = 0;
        _gpuCount = 0;
        _peakGpu = 0;
        _maxCpuTemp = 0;
        _maxGpuTemp = 0;
        _peakRam = 0;
        _peakVram = 0;
        _hangDetector.Reset();
    }

    private static string FormatProcessName(string procName)
    {
        return procName.ToLowerInvariant() switch
        {
            "blender" or "blender-app" => "Blender 3D",
            "3dsmax" => "Autodesk 3ds Max",
            "vray" or "vrayneomodel" => "Chaos V-Ray",
            "revit" => "Autodesk Revit",
            "coronarenderer" => "Chaos Corona",
            "cinema 4d" or "cinema4d" => "Maxon Cinema 4D",
            _ => procName
        };
    }

    private static string GetProcessWindowTitle(Process p)
    {
        try
        {
            return p.MainWindowTitle;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractSceneName(string title, string appName)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Безымянная сцена";

        // Blender format: "Blender* - [C:\path\scene.blend]" or "scene.blend - Blender"
        var match = Regex.Match(title, @"\[(.*?\.(?:blend|max|c4d|rvt|vrscene|fbx))\]", RegexOptions.IgnoreCase);
        if (match.Success) return Path.GetFileName(match.Groups[1].Value);

        match = Regex.Match(title, @"([a-zA-Z0-9_\-\.\s]+\.(?:blend|max|c4d|rvt|vrscene))", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.Trim();

        // Fallback: strip app name
        string clean = title.Replace(appName, "", StringComparison.OrdinalIgnoreCase).Trim('-', ' ', '|', ':');
        return string.IsNullOrWhiteSpace(clean) ? "Безымянная сцена" : clean;
    }

    private bool CheckLogFileForCompletion(string? logPathPattern, List<string> completionPatterns)
    {
        if (string.IsNullOrWhiteSpace(logPathPattern) || completionPatterns.Count == 0) return false;

        try
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(logPathPattern);
            if (!File.Exists(expandedPath)) return false;

            var fileInfo = new FileInfo(expandedPath);
            if (_lastMonitoredLogPath != expandedPath)
            {
                _lastMonitoredLogPath = expandedPath;
                _lastLogFilePosition = Math.Max(0, fileInfo.Length - 4096);
            }

            if (fileInfo.Length <= _lastLogFilePosition) return false;

            using var fs = new FileStream(expandedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_lastLogFilePosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string newContent = reader.ReadToEnd();
            _lastLogFilePosition = fs.Position;

            foreach (var pattern in completionPatterns)
            {
                if (newContent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Log read error
        }

        return false;
    }
}
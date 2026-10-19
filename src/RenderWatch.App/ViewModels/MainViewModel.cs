using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using RenderWatch.App.Services;
using RenderWatch.App.Tray;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.History;
using RenderWatch.Core.Launcher;
using RenderWatch.Core.Models;
using RenderWatch.Core.Notifications;
using RenderWatch.Core.Settings;
using RenderWatch.Core.Tracking;

namespace RenderWatch.App.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IHardwareMonitor _hardwareMonitor;
    private readonly IRenderTracker _tracker;
    private readonly IHistoryRepository _historyRepository;
    private readonly ISettingsManager _settingsManager;
    private readonly INotificationService _notificationService;
    private readonly IAppLauncher _launcher;

    private ViewModelBase _currentView;
    private int _activeTabIndex;
    private string _currentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");
    private string _activeProfileName = "Автоопределение";
    private bool _isMonitoring = true;
    private bool _isMinimized;
    private string _toastTitle = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private bool _isCompactMode;
    private bool _isCompactTopmost = true;
    private int _trimCycleCounter;

    private double _currentPollingIntervalSec = 0.5;

    private AppSettings _settings = new();
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    public DashboardViewModel Dashboard { get; }
    public ChartsViewModel Charts { get; }
    public ConfigurationViewModel Configuration { get; }
    public HistoryViewModel History { get; }
    public LauncherViewModel Launcher { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<RenderProfile> AvailableProfiles { get; } = new();

    private RenderProfile? _selectedProfile;
    public RenderProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value) && value != null)
            {
                SelectProfile(value);
            }
        }
    }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetField(ref _currentView, value);
    }

    public int ActiveTabIndex
    {
        get => _activeTabIndex;
        set
        {
            if (SetField(ref _activeTabIndex, value))
            {
                CurrentView = value switch
                {
                    0 => Dashboard,
                    1 => Charts,
                    2 => Configuration,
                    _ => Dashboard
                };
                OnPropertyChanged(nameof(IsDashboardActive));
                OnPropertyChanged(nameof(IsChartsActive));
                OnPropertyChanged(nameof(IsConfigActive));
            }
        }
    }

    public bool IsDashboardActive => ActiveTabIndex == 0;
    public bool IsChartsActive => ActiveTabIndex == 1;
    public bool IsConfigActive => ActiveTabIndex == 2;

    public double CurrentPollingIntervalSec
    {
        get => _currentPollingIntervalSec;
        set
        {
            if (SetField(ref _currentPollingIntervalSec, value))
            {
                OnPropertyChanged(nameof(IsIntervalTenthSec));
                OnPropertyChanged(nameof(IsIntervalHalfSec));
                OnPropertyChanged(nameof(IsInterval1Sec));
                OnPropertyChanged(nameof(IsInterval3Sec));
                OnPropertyChanged(nameof(IsInterval5Sec));
                _settings.PollingIntervalIdleSec = value;
                _settings.PollingIntervalRenderingSec = value;
                _ = _settingsManager.SaveSettingsAsync(_settings);
            }
        }
    }

    public bool IsIntervalTenthSec => Math.Abs(CurrentPollingIntervalSec - 0.1) < 0.05;
    public bool IsIntervalHalfSec => Math.Abs(CurrentPollingIntervalSec - 0.5) < 0.1;
    public bool IsInterval1Sec => Math.Abs(CurrentPollingIntervalSec - 1.0) < 0.1;
    public bool IsInterval3Sec => Math.Abs(CurrentPollingIntervalSec - 3.0) < 0.1;
    public bool IsInterval5Sec => Math.Abs(CurrentPollingIntervalSec - 5.0) < 0.1;

    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        set => SetField(ref _currentTimeDisplay, value);
    }

    public string ActiveProfileName
    {
        get => _activeProfileName;
        set => SetField(ref _activeProfileName, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set => SetField(ref _isMonitoring, value);
    }

    public bool IsMinimized
    {
        get => _isMinimized;
        set => SetField(ref _isMinimized, value);
    }

    public string ToastTitle
    {
        get => _toastTitle;
        set => SetField(ref _toastTitle, value);
    }

    public bool IsCompactTopmost
    {
        get => _isCompactTopmost;
        set => SetField(ref _isCompactTopmost, value);
    }

    public bool IsCompactMode
    {
        get => _isCompactMode;
        set => SetField(ref _isCompactMode, value);
    }

    private bool _isTaskbarStripLightMode;
    public bool IsTaskbarStripLightMode
    {
        get => _isTaskbarStripLightMode;
        set
        {
            if (SetField(ref _isTaskbarStripLightMode, value))
            {
                OnPropertyChanged(nameof(IsTaskbarStripDarkMode));
                if (Dashboard != null && Dashboard.IsTaskbarStripLightMode != value)
                {
                    Dashboard.IsTaskbarStripLightMode = value;
                }
                _settings.IsTaskbarStripLightMode = value;
                _ = _settingsManager.SaveSettingsAsync(_settings);
            }
        }
    }

    public bool IsTaskbarStripDarkMode => !IsTaskbarStripLightMode;

    public string ToastMessage { get => _toastMessage; set => SetField(ref _toastMessage, value); }
    public bool IsToastVisible { get => _isToastVisible; set => SetField(ref _isToastVisible, value); }

    public ICommand SelectTabCommand { get; }
    public ICommand ToggleMonitoringCommand { get; }
    public ICommand ToggleCompactModeCommand { get; }
    public ICommand ToggleCompactTopmostCommand { get; }
    public ICommand DismissToastCommand { get; }
    public ICommand ForceCompleteCommand { get; }
    public ICommand SetIntervalCommand { get; }
    public ICommand SetTaskbarStripThemeCommand { get; }
    public ICommand ApplyUpdateCommand { get; }

    public MainViewModel(
        IHardwareMonitor hardwareMonitor,
        IRenderTracker tracker,
        IHistoryRepository historyRepository,
        ISettingsManager settingsManager,
        INotificationService notificationService,
        IAppLauncher launcher)
    {
        _hardwareMonitor = hardwareMonitor;
        _tracker = tracker;
        _historyRepository = historyRepository;
        _settingsManager = settingsManager;
        _notificationService = notificationService;
        _launcher = launcher;

        Dashboard = new DashboardViewModel();
        Dashboard.TaskbarStripThemeChanged += mode =>
        {
            if (IsTaskbarStripLightMode != mode)
            {
                IsTaskbarStripLightMode = mode;
            }
        };
        Charts = new ChartsViewModel();
        Configuration = new ConfigurationViewModel();
        History = new HistoryViewModel(historyRepository);
        Launcher = new LauncherViewModel(launcher, settingsManager);
        Settings = new SettingsViewModel(settingsManager);

        _currentView = Dashboard;
        _activeTabIndex = 0;

        SelectTabCommand = new RelayCommand(p =>
        {
            if (p != null && int.TryParse(p.ToString(), out int idx))
            {
                ActiveTabIndex = idx;
            }
        });

        SetIntervalCommand = new RelayCommand(p =>
        {
            if (p != null && double.TryParse(p.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sec))
            {
                CurrentPollingIntervalSec = sec;
            }
        });

        SetTaskbarStripThemeCommand = new RelayCommand(p =>
        {
            if (p != null)
            {
                string theme = p.ToString()!.ToLowerInvariant();
                IsTaskbarStripLightMode = (theme == "light" || theme == "true" || theme == "1");
            }
        });

        ToggleMonitoringCommand = new RelayCommand(() => IsMonitoring = !IsMonitoring);
        ToggleCompactModeCommand = new RelayCommand(() => IsCompactMode = !IsCompactMode);
        ToggleCompactTopmostCommand = new RelayCommand(() => IsCompactTopmost = !IsCompactTopmost);
        DismissToastCommand = new RelayCommand(() => IsToastVisible = false);

        ForceCompleteCommand = new RelayCommand(() =>
        {
            _tracker.ForceCompleteCurrentSession("Принудительное завершение пользователем");
        });

        ApplyUpdateCommand = new RelayCommand(() => AutoUpdateService.ApplyUpdateAndRestart());

        _notificationService.OnNotification += HandleNotification;
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsManager.LoadSettingsAsync();
        IsTaskbarStripLightMode = _settings.IsTaskbarStripLightMode;
        Settings.Load(_settings);
        Charts.Initialize(_settings, _settingsManager);

        CurrentPollingIntervalSec = 0.5;
        _settings.PollingIntervalIdleSec = 0.5;
        _settings.PollingIntervalRenderingSec = 0.5;

        AvailableProfiles.Clear();
        foreach (var p in _settings.Profiles) AvailableProfiles.Add(p);

        var active = AvailableProfiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId) ?? AvailableProfiles.FirstOrDefault();
        if (active != null)
        {
            _selectedProfile = active;
            SelectProfile(active);
        }

        Launcher.LoadProfiles(_settings.LaunchProfiles);
        await History.LoadHistoryAsync();

        await _hardwareMonitor.InitializeAsync();

        // Perform immediate first reading so UI is instantly populated
        try
        {
            var initialMetrics = await _hardwareMonitor.GetMetricsAsync();
            var initialTracker = _tracker.Update(initialMetrics, _settings.HangTimeoutSec);
            Dashboard.Update(initialMetrics, initialTracker);
            Charts.AddPoints(
                initialMetrics.CpuUsagePercent,
                initialMetrics.GpuUsagePercent,
                initialMetrics.CpuTemperatureC,
                initialMetrics.GpuTemperatureC,
                initialMetrics.RamUsagePercent,
                initialMetrics.VramUsagePercent
            );
        }
        catch { }

        StartMonitoringLoop();

        AutoUpdateService.UpdateReady += tag =>
        {
            ToastTitle = "Обновление готово";
            ToastMessage = $"Версия {tag} загружена. Приложение обновится при следующем запуске или клике на уведомление.";
            IsToastVisible = true;
        };
        AutoUpdateService.StartBackgroundUpdateCheck();
    }

    public void SelectProfile(RenderProfile profile)
    {
        _tracker.CurrentProfile = profile;
        ActiveProfileName = profile.Name;
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void StartMonitoringLoop()
    {
        _monitoringCts = new CancellationTokenSource();
        _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token));
    }

    private async Task MonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (IsMonitoring)
                {
                    var metrics = await _hardwareMonitor.GetMetricsAsync();
                    var trackerResult = _tracker.Update(metrics, _settings.HangTimeoutSec);

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CurrentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");
                        Dashboard.Update(metrics, trackerResult);

                        Charts.AddPoints(
                            metrics.CpuUsagePercent,
                            metrics.GpuUsagePercent,
                            metrics.CpuTemperatureC,
                            metrics.GpuTemperatureC,
                            metrics.RamUsagePercent,
                            metrics.VramUsagePercent
                        );
                    });

                    if (trackerResult.CompletedSession != null)
                    {
                        await _historyRepository.AddSessionAsync(trackerResult.CompletedSession);
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            _ = History.LoadHistoryAsync();
                        });

                        if (_settings.EnableNotifications && _settings.NotifyOnCompleted)
                        {
                            _notificationService.NotifyRenderCompleted(
                                trackerResult.CompletedSession.AppName,
                                trackerResult.CompletedSession.SceneName,
                                trackerResult.CompletedSession.Duration
                            );
                        }
                    }

                    if (_settings.EnableNotifications)
                    {
                        if (_settings.NotifyOnHang && trackerResult.Status == RenderStatus.PossiblyFrozen)
                        {
                            _notificationService.NotifyHangDetected(trackerResult.AppName, _settings.HangTimeoutSec);
                        }

                        if (metrics.CpuTemperatureC >= _settings.CpuTempThresholdC)
                        {
                            _notificationService.NotifyThresholdExceeded("CPU Temp", metrics.CpuTemperatureC.Value, _settings.CpuTempThresholdC, "°C");
                        }

                        if (metrics.GpuTemperatureC >= _settings.GpuTempThresholdC)
                        {
                            _notificationService.NotifyThresholdExceeded("GPU Temp", metrics.GpuTemperatureC.Value, _settings.GpuTempThresholdC, "°C");
                        }

                        if (metrics.RamUsagePercent >= _settings.RamThresholdPercent)
                        {
                            _notificationService.NotifyThresholdExceeded("RAM", metrics.RamUsagePercent.Value, _settings.RamThresholdPercent, "%");
                        }

                        if (metrics.VramUsagePercent >= _settings.VramThresholdPercent)
                        {
                            _notificationService.NotifyThresholdExceeded("VRAM", metrics.VramUsagePercent.Value, _settings.VramThresholdPercent, "%");
                        }
                    }
                }

                double intervalSec = _isMinimized
                    ? _settings.PollingIntervalBackgroundSec
                    : _currentPollingIntervalSec;

                int trimThreshold = intervalSec <= 0.2 ? 200 : (intervalSec <= 0.5 ? 60 : 30);
                if (++_trimCycleCounter >= trimThreshold)
                {
                    _trimCycleCounter = 0;
                    RenderWatch.App.Utils.MemoryOptimizer.Trim();
                }

                int delayMs = (int)(intervalSec * 1000);
                await Task.Delay(Math.Clamp(delayMs, 100, 30000), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(2000, token);
            }
        }
    }

    private void HandleNotification(NotificationEvent evt)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            ToastTitle = evt.Title;
            ToastMessage = evt.Message;
            IsToastVisible = true;

            Task.Delay(5000).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (ToastTitle == evt.Title) IsToastVisible = false;
                });
            });
        });
    }

    public void Dispose()
    {
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _hardwareMonitor.Dispose();
    }
}

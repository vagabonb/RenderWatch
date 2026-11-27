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
    private readonly BatteryMonitor _batteryMonitor = new();

    private ViewModelBase _currentView;
    private int _activeTabIndex;
    private string _currentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");
    private string _activeProfileName = "Автоопределение";
    private bool _isMonitoring = true;
    private bool _isMinimized;
    private bool _isNotchExpanded;
    private readonly SemaphoreSlim _monitoringWake = new(0, 1);
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

    public SessionMetricsViewModel SessionMetrics { get; } = new();
    public DashboardViewModel Dashboard { get; }
    public ChartsViewModel Charts { get; }
    public NotchTelemetry Notch { get; } = new();
    private HardwareMetrics _notchMetrics = new();
    private NotchReading _primaryNotchReading = NotchReading.Empty;
    private NotchReading _secondaryNotchReading = NotchReading.Empty;
    public NotchReading PrimaryNotchReading => _primaryNotchReading;
    public NotchReading SecondaryNotchReading => _secondaryNotchReading;
    public string NotchHint => SecondaryNotchReading.IsSet
        ? $"{PrimaryNotchReading.Hint}\n{SecondaryNotchReading.Hint}"
        : PrimaryNotchReading.Hint;

    private void RefreshNotchReadings()
    {
        var shape = Constructor.NotchShape;
        var battery = Dashboard.Battery;
        var primary = shape switch
        {
            NotchLayout.Island => Constructor.IslandFirstMetric,
            NotchLayout.MemoryBar => Constructor.BarMetric,
            _ when Constructor.RingMetric == NotchRingMetric.Battery && !battery.IsPresent => NotchRingMetric.Ram,
            _ => Constructor.RingMetric
        };
        var secondary = shape == NotchLayout.Island ? Constructor.IslandSecondMetric : NotchRingMetric.None;
        bool primaryChanged = SetField(ref _primaryNotchReading,
            NotchReading.Create(primary, _notchMetrics, battery, SessionMetrics), nameof(PrimaryNotchReading));
        bool secondaryChanged = SetField(ref _secondaryNotchReading,
            NotchReading.Create(secondary, _notchMetrics, battery, SessionMetrics), nameof(SecondaryNotchReading));
        if (primaryChanged || secondaryChanged) OnPropertyChanged(nameof(NotchHint));
    }

    internal void UpdateDashboard(HardwareMetrics metrics, TrackerUpdateResult tracker)
    {
        Dashboard.Update(metrics, tracker);
        _notchMetrics = metrics;
        RefreshNotchReadings();
    }

    internal void UpdateBatteryReading(BatteryReading? battery)
    {
        Dashboard.Battery.Update(battery);
        if (PrimaryNotchReading.Metric == NotchRingMetric.Battery || SecondaryNotchReading.Metric == NotchRingMetric.Battery ||
            (Constructor.NotchShape == NotchLayout.Ring && Constructor.RingMetric == NotchRingMetric.Battery))
            RefreshNotchReadings();
    }

    internal void SetNotchExpanded(bool expanded)
    {
        if (_isNotchExpanded == expanded) return;
        Volatile.Write(ref _isNotchExpanded, expanded);
        if (_monitoringWake.CurrentCount == 0) _monitoringWake.Release();
    }
    public ConfigurationViewModel Configuration { get; }
    public DisplayConstructorViewModel Constructor { get; }
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
                    3 => Constructor,
                    _ => Dashboard
                };
                OnPropertyChanged(nameof(IsDashboardActive));
                OnPropertyChanged(nameof(IsChartsActive));
                OnPropertyChanged(nameof(IsConfigActive));
                OnPropertyChanged(nameof(IsConstructorActive));
            }
        }
    }

    public bool IsDashboardActive => ActiveTabIndex == 0;
    public bool IsChartsActive => ActiveTabIndex == 1;
    public bool IsConfigActive => ActiveTabIndex == 2;
    public bool IsConstructorActive => ActiveTabIndex == 3;

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

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetField(ref _startWithWindows, value))
            {
                OnPropertyChanged(nameof(IsAutostartDisabled));
                if (Dashboard != null && Dashboard.StartWithWindows != value)
                {
                    Dashboard.StartWithWindows = value;
                }
                if (Settings != null && Settings.StartWithWindows != value)
                {
                    Settings.StartWithWindows = value;
                }
                _settings.StartWithWindows = value;
                _ = UpdateAutostartAsync(value);
                _ = _settingsManager.SaveSettingsAsync(_settings);
            }
        }
    }

    private readonly SemaphoreSlim _autostartLock = new(1, 1);

    private async Task UpdateAutostartAsync(bool enable)
    {
        await _autostartLock.WaitAsync();
        bool success;
        try
        {
            if (enable != StartWithWindows) return;
            success = await Task.Run(() => StartupManager.SetAutostart(enable));
        }
        finally
        {
            _autostartLock.Release();
        }
        if (!success)
        {
            ToastTitle = "Автозапуск";
            ToastMessage = "Не удалось изменить автозапуск Windows. Проверьте права и настройки планировщика задач.";
            IsToastVisible = true;
        }
    }
    public bool IsAutostartDisabled => !StartWithWindows;

    public string ToastMessage { get => _toastMessage; set => SetField(ref _toastMessage, value); }
    public bool IsToastVisible { get => _isToastVisible && !IsUpdatePromptVisible; set => SetField(ref _isToastVisible, value); }

    public string Appearance
    {
        get => IsColorStyle ? "Color" : IsGraphiteStyle ? "Graphite" : "Black";
        private set
        {
            if (Appearance == value) return;
            GetWindowLayout();
            _settings.IsNavigationCollapsed ??= IsSidebarCollapsed;
            _settings.Appearance = value;
            NotifyAppearanceChanged();
            _ = _settingsManager.SaveSettingsAsync(_settings);
        }
    }

    public bool UsesBlackLayout => !IsGraphiteStyle;
    public bool IsBlackStyle => UsesBlackLayout && !IsColorStyle;
    public bool IsGraphiteStyle => string.Equals(_settings.Appearance, "Graphite", StringComparison.OrdinalIgnoreCase);
    public bool IsColorStyle => string.Equals(_settings.Appearance, "Color", StringComparison.OrdinalIgnoreCase);
    public bool AreSettingsLoaded { get; private set; }
    public ICommand SetAppearanceCommand { get; }

    internal AppearanceLayout GetWindowLayout() => _settings.SharedWindowLayout ??= MigrateWindowLayout();

    private AppearanceLayout MigrateWindowLayout()
    {
        var current = Appearance switch
        {
            "Graphite" => _settings.GraphiteLayout,
            "Color" => _settings.ColorLayout,
            _ => _settings.BlackLayout
        };
        AppearanceLayout?[] layouts = [current, _settings.BlackLayout, _settings.GraphiteLayout, _settings.ColorLayout];
        var mainLayout = layouts.FirstOrDefault(layout => layout?.Main != null);
        return new AppearanceLayout
        {
            Main = mainLayout?.Main,
            IsMainMaximized = (mainLayout ?? current)?.IsMainMaximized == true,
            Compact = layouts.FirstOrDefault(layout => layout?.Compact != null)?.Compact,
            Micro = layouts.FirstOrDefault(layout => layout?.Micro != null)?.Micro
        };
    }

    internal Task SaveWindowLayoutAsync() => _settingsManager.SaveSettingsAsync(_settings);

    private void NotifyAppearanceChanged()
    {
        OnPropertyChanged(nameof(IsBlackStyle));
        OnPropertyChanged(nameof(UsesBlackLayout));
        OnPropertyChanged(nameof(IsGraphiteStyle));
        OnPropertyChanged(nameof(IsColorStyle));
        NotifySidebarChanged();
        OnPropertyChanged(nameof(Appearance));
    }

    public bool IsSidebarCollapsed
    {
        get => _settings.IsNavigationCollapsed ?? (IsColorStyle ? _settings.IsColorSidebarCollapsed :
            IsGraphiteStyle ? _settings.IsSidebarCollapsed : _settings.IsBlackSidebarCollapsed);
        set
        {
            if (IsSidebarCollapsed == value) return;
            _settings.IsNavigationCollapsed = value;
            NotifySidebarChanged();
            _ = _settingsManager.SaveSettingsAsync(_settings);
        }
    }

    public GridLength SidebarWidth => new(IsSidebarCollapsed ? 56 : 194);
    public ICommand ToggleSidebarCommand { get; }

    private void NotifySidebarChanged()
    {
        OnPropertyChanged(nameof(IsSidebarCollapsed));
        OnPropertyChanged(nameof(SidebarWidth));
    }

    public ICommand SelectTabCommand { get; }
    public ICommand ToggleMonitoringCommand { get; }
    public ICommand ToggleCompactModeCommand { get; }
    public ICommand ToggleCompactTopmostCommand { get; }
    public ICommand DismissToastCommand { get; }
    public ICommand ApplyUpdateCommand { get; }
    public ICommand PostponeUpdateCommand { get; }

    private readonly CancellationTokenSource _lifetime = new();
    private AutoUpdateService.StableRelease? _updateOffer;
    private bool _isUpdating;
    private bool _isUpdatePromptVisible;
    private string _updatePromptText = "";

    public bool IsUpdatePromptVisible
    {
        get => _isUpdatePromptVisible;
        private set
        {
            if (!SetField(ref _isUpdatePromptVisible, value)) return;
            OnPropertyChanged(nameof(IsToastVisible));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (SetField(ref _isUpdating, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public string UpdatePromptText
    {
        get => _updatePromptText;
        private set => SetField(ref _updatePromptText, value);
    }
    public ICommand ForceCompleteCommand { get; }
    public ICommand SetIntervalCommand { get; }
    public ICommand SetTaskbarStripThemeCommand { get; }
    public ICommand SetAutostartCommand { get; }

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
        Dashboard.AutostartChanged += enabled =>
        {
            if (StartWithWindows != enabled)
            {
                StartWithWindows = enabled;
            }
        };
        Charts = new ChartsViewModel();
        Configuration = new ConfigurationViewModel();
        Constructor = new DisplayConstructorViewModel(SessionMetrics);
        Constructor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "" or nameof(Constructor.NotchShape) or nameof(Constructor.RingMetric) or
                nameof(Constructor.BarMetric) or nameof(Constructor.IslandFirstMetric) or nameof(Constructor.IslandSecondMetric))
                RefreshNotchReadings();
        };
        RefreshNotchReadings();
        History = new HistoryViewModel(historyRepository);
        Launcher = new LauncherViewModel(launcher, settingsManager);
        Settings = new SettingsViewModel(settingsManager);

        _currentView = Dashboard;
        _activeTabIndex = 0;

        SetAppearanceCommand = new RelayCommand(value =>
        {
            if (value is "Black" or "Graphite" or "Color") Appearance = (string)value;
        });
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
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

        SetAutostartCommand = new RelayCommand(p =>
        {
            if (p != null)
            {
                string s = p.ToString()!.ToLowerInvariant();
                StartWithWindows = (s == "true" || s == "1" || s == "enable" || s == "enabled");
            }
            else
            {
                StartWithWindows = !StartWithWindows;
            }
        });

        ToggleMonitoringCommand = new RelayCommand(() => IsMonitoring = !IsMonitoring);
        ToggleCompactModeCommand = new RelayCommand(() => IsCompactMode = !IsCompactMode);
        ToggleCompactTopmostCommand = new RelayCommand(() => IsCompactTopmost = !IsCompactTopmost);
        DismissToastCommand = new RelayCommand(() => IsToastVisible = false);
        ApplyUpdateCommand = new RelayCommand(async () => await AcceptUpdateAsync(), () => IsUpdatePromptVisible && !IsUpdating);
        PostponeUpdateCommand = new RelayCommand(() =>
        {
            if (IsUpdating) return;
            _updateOffer = null;
            IsUpdatePromptVisible = false;
        }, () => IsUpdatePromptVisible && !IsUpdating);

        ForceCompleteCommand = new RelayCommand(() =>
        {
            _tracker.ForceCompleteCurrentSession("Принудительное завершение пользователем");
        });

        _notificationService.OnNotification += HandleNotification;
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsManager.LoadSettingsAsync();
        _settings.Appearance = Appearance;
        AreSettingsLoaded = true;
        var token = _lifetime.Token;
        _ = Task.Run(() => MonitorBatteryAsync(_batteryMonitor.Read, token), token);
        OnPropertyChanged(nameof(AreSettingsLoaded));
        NotifyAppearanceChanged();
        NotifySidebarChanged();
        Constructor.Initialize(_settings, _settingsManager);
        IsTaskbarStripLightMode = _settings.IsTaskbarStripLightMode;

        // Synchronize autostart status with actual OS Task Scheduler / Registry state
        bool isOsAutostart = StartupManager.IsAutostartEnabled();
        _startWithWindows = _settings.StartWithWindows || isOsAutostart;
        if (_startWithWindows != _settings.StartWithWindows)
        {
            _settings.StartWithWindows = _startWithWindows;
            _ = _settingsManager.SaveSettingsAsync(_settings);
        }
        if (_startWithWindows) _ = UpdateAutostartAsync(true);
        Dashboard.StartWithWindows = _startWithWindows;
        Settings.StartWithWindows = _startWithWindows;

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
            UpdateDashboard(initialMetrics, initialTracker);
            SessionMetrics.Update(initialMetrics);
            Notch.Observe(initialMetrics, _settings);
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

        _ = CheckForUpdateAsync(token);
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            var offer = await Task.Run(() => AutoUpdateService.CheckForUpdateAsync(cancellationToken), cancellationToken);
            if (offer == null || cancellationToken.IsCancellationRequested) return;
            ShowUpdateOffer(offer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal void ShowUpdateOffer(AutoUpdateService.StableRelease offer)
    {
        _updateOffer = offer;
        UpdatePromptText = $"Обновить до {offer.Tag.TrimStart('v')}?";
        IsUpdatePromptVisible = true;
    }

    private async Task AcceptUpdateAsync()
    {
        if (IsUpdating || _updateOffer is not { } offer || Application.Current?.MainWindow is not { } window) return;
        var token = _lifetime.Token;
        IsUpdating = true;
        UpdatePromptText = "Скачивание…";
        try
        {
            await Task.Run(() => AutoUpdateService.DownloadAndStartAsync(offer, token), token);
            window.Close();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            UpdatePromptText = "Повторить обновление?";
        }
        finally
        {
            IsUpdating = false;
        }
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

    internal async Task MonitorBatteryAsync(Func<BatteryReading?> read, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                var reading = read();
                if (Application.Current?.Dispatcher is not { } dispatcher) return;
                await dispatcher.InvokeAsync(() => UpdateBatteryReading(reading),
                    System.Windows.Threading.DispatcherPriority.Background, token);
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
                        UpdateDashboard(metrics, trackerResult);
                        SessionMetrics.Update(metrics);
                        Notch.Observe(metrics, _settings);

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

                double intervalSec = _isMinimized && !Volatile.Read(ref _isNotchExpanded)
                    ? _settings.PollingIntervalBackgroundSec
                    : _currentPollingIntervalSec;

                int trimThreshold = intervalSec <= 0.2 ? 200 : (intervalSec <= 0.5 ? 60 : 30);
                if (++_trimCycleCounter >= trimThreshold)
                {
                    _trimCycleCounter = 0;
                    RenderWatch.App.Utils.MemoryOptimizer.Trim();
                }

                int delayMs = (int)(intervalSec * 1000);
                await _monitoringWake.WaitAsync(Math.Clamp(delayMs, 100, 30000), token);
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
        _lifetime.Cancel();
        _lifetime.Dispose();
        _monitoringCts?.Dispose();
        _hardwareMonitor.Dispose();
    }
}

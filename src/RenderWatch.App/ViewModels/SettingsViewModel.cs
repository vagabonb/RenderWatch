using System.Windows.Input;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.Models;
using RenderWatch.Core.Settings;

namespace RenderWatch.App.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsManager _settingsManager;
    private AppSettings _settings = new();
    private string _statusMessage = string.Empty;

    public double PollingIntervalIdleSec
    {
        get => _settings.PollingIntervalIdleSec;
        set { _settings.PollingIntervalIdleSec = value; OnPropertyChanged(); }
    }

    public double PollingIntervalRenderingSec
    {
        get => _settings.PollingIntervalRenderingSec;
        set { _settings.PollingIntervalRenderingSec = value; OnPropertyChanged(); }
    }

    public double PollingIntervalBackgroundSec
    {
        get => _settings.PollingIntervalBackgroundSec;
        set { _settings.PollingIntervalBackgroundSec = value; OnPropertyChanged(); }
    }

    public double CpuTempThresholdC
    {
        get => _settings.CpuTempThresholdC;
        set { _settings.CpuTempThresholdC = value; OnPropertyChanged(); }
    }

    public double GpuTempThresholdC
    {
        get => _settings.GpuTempThresholdC;
        set { _settings.GpuTempThresholdC = value; OnPropertyChanged(); }
    }

    public double RamThresholdPercent
    {
        get => _settings.RamThresholdPercent;
        set { _settings.RamThresholdPercent = value; OnPropertyChanged(); }
    }

    public double VramThresholdPercent
    {
        get => _settings.VramThresholdPercent;
        set { _settings.VramThresholdPercent = value; OnPropertyChanged(); }
    }

    public int HangTimeoutSec
    {
        get => _settings.HangTimeoutSec;
        set { _settings.HangTimeoutSec = value; OnPropertyChanged(); }
    }

    public bool EnableNotifications
    {
        get => _settings.EnableNotifications;
        set { _settings.EnableNotifications = value; OnPropertyChanged(); }
    }

    public bool NotifyOnCompleted
    {
        get => _settings.NotifyOnCompleted;
        set { _settings.NotifyOnCompleted = value; OnPropertyChanged(); }
    }

    public bool NotifyOnHang
    {
        get => _settings.NotifyOnHang;
        set { _settings.NotifyOnHang = value; OnPropertyChanged(); }
    }

    public bool NotifyOnCrash
    {
        get => _settings.NotifyOnCrash;
        set { _settings.NotifyOnCrash = value; OnPropertyChanged(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { _settings.MinimizeToTray = value; OnPropertyChanged(); }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { _settings.CloseToTray = value; OnPropertyChanged(); }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set { _settings.StartWithWindows = value; OnPropertyChanged(); }
    }

    public string CoolingType
    {
        get => _settings.CoolingType;
        set
        {
            _settings.CoolingType = value;
            CompositeHardwareMonitor.UserCoolingPreference = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCoolingAuto));
            OnPropertyChanged(nameof(IsCoolingLiquid));
            OnPropertyChanged(nameof(IsCoolingAir));
        }
    }

    public bool IsCoolingAuto
    {
        get => string.Equals(_settings.CoolingType, "auto", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_settings.CoolingType);
        set { if (value) CoolingType = "auto"; }
    }

    public bool IsCoolingLiquid
    {
        get => string.Equals(_settings.CoolingType, "liquid", StringComparison.OrdinalIgnoreCase);
        set { if (value) CoolingType = "liquid"; }
    }

    public bool IsCoolingAir
    {
        get => string.Equals(_settings.CoolingType, "air", StringComparison.OrdinalIgnoreCase);
        set { if (value) CoolingType = "air"; }
    }

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public ICommand SaveCommand { get; }

    public SettingsViewModel(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        SaveCommand = new RelayCommand(async () => await SaveAsync());
    }

    public void Load(AppSettings settings)
    {
        _settings = settings;
        CompositeHardwareMonitor.UserCoolingPreference = settings.CoolingType;
        OnPropertyChanged(string.Empty);
    }

    public async Task SaveAsync()
    {
        CompositeHardwareMonitor.UserCoolingPreference = _settings.CoolingType;
        await _settingsManager.SaveSettingsAsync(_settings);
        StatusMessage = "Настройки успешно сохранены.";
    }
}
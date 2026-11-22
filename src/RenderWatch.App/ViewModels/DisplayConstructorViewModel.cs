using System.Windows.Input;
using RenderWatch.Core.Models;
using RenderWatch.Core.Settings;

namespace RenderWatch.App.ViewModels;

public class DisplayConstructorViewModel : ViewModelBase
{
    private AppSettings? _settings;
    private ISettingsManager? _settingsManager;

    public void Initialize(AppSettings settings, ISettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;
        _metricOrder = NormalizeOrder(settings.DisplayMetricOrder);
        OnPropertyChanged(string.Empty);
    }

    private string _metricOrder = "CPU,GPU,RAM,VRAM";
    public string MetricOrder
    {
        get => _metricOrder;
        private set
        {
            if (SetField(ref _metricOrder, value))
            {
                if (_settings != null && _settingsManager != null)
                {
                    _settings.DisplayMetricOrder = value;
                    _ = _settingsManager.SaveSettingsAsync(_settings);
                }
            }
        }
    }

    public ICommand MoveMetricLeftCommand { get; }
    public ICommand MoveMetricRightCommand { get; }

    public DisplayConstructorViewModel()
    {
        MoveMetricLeftCommand = new RelayCommand(p => MoveMetric(p as string, -1));
        MoveMetricRightCommand = new RelayCommand(p => MoveMetric(p as string, 1));
    }

    private void MoveMetric(string? name, int direction)
    {
        if (name == null) return;
        var items = _metricOrder.Split(',').ToList();
        int from = items.IndexOf(name);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= items.Count) return;
        (items[from], items[to]) = (items[to], items[from]);
        MetricOrder = string.Join(",", items);
    }

    private static string NormalizeOrder(string? saved)
    {
        string[] valid = ["CPU", "GPU", "RAM", "VRAM", "DISK", "NET", "UPTIME"];
        var items = (saved ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().ToUpperInvariant())
            .Where(item => valid.Contains(item))
            .Distinct().ToList();
        foreach (string item in valid)
            if (!items.Contains(item)) items.Add(item);
        return string.Join(",", items);
    }

    public bool ShowTaskbarDisk
    {
        get => _settings?.ShowTaskbarDisk ?? false;
        set
        {
            if (_settings == null || _settings.ShowTaskbarDisk == value) return;
            _settings.ShowTaskbarDisk = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarNetwork
    {
        get => _settings?.ShowTaskbarNetwork ?? false;
        set
        {
            if (_settings == null || _settings.ShowTaskbarNetwork == value) return;
            _settings.ShowTaskbarNetwork = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarUptime
    {
        get => _settings?.ShowTaskbarUptime ?? false;
        set
        {
            if (_settings == null || _settings.ShowTaskbarUptime == value) return;
            _settings.ShowTaskbarUptime = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroDisk
    {
        get => _settings?.ShowMicroDisk ?? false;
        set
        {
            if (_settings == null || _settings.ShowMicroDisk == value) return;
            _settings.ShowMicroDisk = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroNetwork
    {
        get => _settings?.ShowMicroNetwork ?? false;
        set
        {
            if (_settings == null || _settings.ShowMicroNetwork == value) return;
            _settings.ShowMicroNetwork = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroUptime
    {
        get => _settings?.ShowMicroUptime ?? false;
        set
        {
            if (_settings == null || _settings.ShowMicroUptime == value) return;
            _settings.ShowMicroUptime = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactDisk
    {
        get => _settings?.ShowCompactDisk ?? false;
        set
        {
            if (_settings == null || _settings.ShowCompactDisk == value) return;
            _settings.ShowCompactDisk = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactNetwork
    {
        get => _settings?.ShowCompactNetwork ?? false;
        set
        {
            if (_settings == null || _settings.ShowCompactNetwork == value) return;
            _settings.ShowCompactNetwork = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactUptime
    {
        get => _settings?.ShowCompactUptime ?? false;
        set
        {
            if (_settings == null || _settings.ShowCompactUptime == value) return;
            _settings.ShowCompactUptime = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarCpu
    {
        get => _settings?.ShowTaskbarCpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowTaskbarCpu == value) return;
            _settings.ShowTaskbarCpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarGpu
    {
        get => _settings?.ShowTaskbarGpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowTaskbarGpu == value) return;
            _settings.ShowTaskbarGpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarRam
    {
        get => _settings?.ShowTaskbarRam ?? true;
        set
        {
            if (_settings == null || _settings.ShowTaskbarRam == value) return;
            _settings.ShowTaskbarRam = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarVram
    {
        get => _settings?.ShowTaskbarVram ?? false;
        set
        {
            if (_settings == null || _settings.ShowTaskbarVram == value) return;
            _settings.ShowTaskbarVram = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarCpuTemp
    {
        get => _settings?.ShowTaskbarCpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowTaskbarCpuTemp == value) return;
            _settings.ShowTaskbarCpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowTaskbarGpuTemp
    {
        get => _settings?.ShowTaskbarGpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowTaskbarGpuTemp == value) return;
            _settings.ShowTaskbarGpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroCpu
    {
        get => _settings?.ShowMicroCpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowMicroCpu == value) return;
            _settings.ShowMicroCpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroGpu
    {
        get => _settings?.ShowMicroGpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowMicroGpu == value) return;
            _settings.ShowMicroGpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroRam
    {
        get => _settings?.ShowMicroRam ?? true;
        set
        {
            if (_settings == null || _settings.ShowMicroRam == value) return;
            _settings.ShowMicroRam = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroVram
    {
        get => _settings?.ShowMicroVram ?? false;
        set
        {
            if (_settings == null || _settings.ShowMicroVram == value) return;
            _settings.ShowMicroVram = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroCpuTemp
    {
        get => _settings?.ShowMicroCpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowMicroCpuTemp == value) return;
            _settings.ShowMicroCpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowMicroGpuTemp
    {
        get => _settings?.ShowMicroGpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowMicroGpuTemp == value) return;
            _settings.ShowMicroGpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactCpu
    {
        get => _settings?.ShowCompactCpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactCpu == value) return;
            _settings.ShowCompactCpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactGpu
    {
        get => _settings?.ShowCompactGpu ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactGpu == value) return;
            _settings.ShowCompactGpu = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactRam
    {
        get => _settings?.ShowCompactRam ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactRam == value) return;
            _settings.ShowCompactRam = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactVram
    {
        get => _settings?.ShowCompactVram ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactVram == value) return;
            _settings.ShowCompactVram = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactCpuTemp
    {
        get => _settings?.ShowCompactCpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactCpuTemp == value) return;
            _settings.ShowCompactCpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }

    public bool ShowCompactGpuTemp
    {
        get => _settings?.ShowCompactGpuTemp ?? true;
        set
        {
            if (_settings == null || _settings.ShowCompactGpuTemp == value) return;
            _settings.ShowCompactGpuTemp = value;
            OnPropertyChanged();
            _ = _settingsManager!.SaveSettingsAsync(_settings);
        }
    }
}

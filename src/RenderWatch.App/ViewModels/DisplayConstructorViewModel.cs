using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using RenderWatch.Core.Models;
using RenderWatch.Core.Settings;

namespace RenderWatch.App.ViewModels;

public class DisplayConstructorViewModel : ViewModelBase
{
    private AppSettings? _settings;
    private ISettingsManager? _settingsManager;
    private readonly DispatcherTimer _saveTimer;
    private IReadOnlyList<CompactGraphOption> _activeCompactGraphs = [];
    private CompactGraphOption? _selectedCompactGraph;
    private int _selectedPage;

    public TaskbarAppearanceViewModel Taskbar { get; }
    public int SelectedPage { get => _selectedPage; set => SetField(ref _selectedPage, value); }
    public ObservableCollection<CompactGraphOption> CompactOptions { get; } = new();
    public IReadOnlyList<CompactGraphOption> ActiveCompactGraphs
    {
        get => _activeCompactGraphs;
        private set => SetField(ref _activeCompactGraphs, value);
    }
    public CompactGraphOption? SelectedCompactGraph
    {
        get => _selectedCompactGraph;
        set
        {
            if (SetField(ref _selectedCompactGraph, value)) CommandManager.InvalidateRequerySuggested();
        }
    }
    public ICommand MoveCompactUpCommand { get; }
    public ICommand MoveCompactDownCommand { get; }

    public void Initialize(AppSettings settings, ISettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;
        _metricOrder = NormalizeOrder(settings.DisplayMetricOrder);
        Taskbar.Initialize(settings.TaskbarAppearance ??= new(), _metricOrder);
        InitializeCompact(settings);
        OnPropertyChanged(string.Empty);
    }

    private string _metricOrder = "CPU,GPU,RAM,VRAM,DISK,NET,UPTIME";
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

    public DisplayConstructorViewModel(SessionMetricsViewModel metrics)
    {
        MoveMetricLeftCommand = new RelayCommand(p => MoveMetric(p as string, -1));
        MoveMetricRightCommand = new RelayCommand(p => MoveMetric(p as string, 1));
        MoveCompactUpCommand = new RelayCommand(() => MoveCompact(-1), () => CanMoveCompact(-1));
        MoveCompactDownCommand = new RelayCommand(() => MoveCompact(1), () => CanMoveCompact(1));
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_settings != null && _settingsManager != null)
                _ = _settingsManager.SaveSettingsAsync(_settings);
        };
        Taskbar = new TaskbarAppearanceViewModel(QueueSave);

        CompactGraphOption[] options =
        [
            new("CPU", "CPU", "%", metrics.CpuLoad, 100, SaveCompact),
            new("CPU-TEMP", "CPU °C", "°C", metrics.CpuTemp, 105, SaveCompact),
            new("GPU", "GPU", "%", metrics.GpuLoad, 100, SaveCompact),
            new("GPU-TEMP", "GPU °C", "°C", metrics.GpuTemp, 105, SaveCompact),
            new("RAM", "RAM", "%", metrics.Ram, 100, SaveCompact),
            new("VRAM", "VRAM", "%", metrics.Vram, 100, SaveCompact),
            new("NET-RX", "Сеть · Приём", "Б/с", metrics.NetworkReceive, TransferHistory.InitialScale, SaveCompact),
            new("NET-TX", "Сеть · Отправка", "Б/с", metrics.NetworkSend, TransferHistory.InitialScale, SaveCompact),
            new("DISK-READ", "Диски · Чтение", "Б/с", metrics.DiskRead.Points, TransferHistory.InitialScale, SaveCompact),
            new("DISK-WRITE", "Диски · Запись", "Б/с", metrics.DiskWrite.Points, TransferHistory.InitialScale, SaveCompact)
        ];
        foreach (var option in options) CompactOptions.Add(option);
        InitializeCompact(new AppSettings());
    }

    private void InitializeCompact(AppSettings settings)
    {
        var saved = settings.CompactGraphs?.OfType<CompactGraphSetting>().ToArray();
        var legacyOrder = _metricOrder.Split(',');
        var byId = CompactOptions.ToDictionary(option => option.Id, StringComparer.Ordinal);
        var order = saved is null
            ? byId.Keys.OrderBy(id => Array.IndexOf(legacyOrder, id.Split('-')[0])).ToArray()
            : saved.Select(item => item.Id).Where(id => id != null && byId.ContainsKey(id))
                .Concat(byId.Keys).Distinct().ToArray();

        for (int index = 0; index < order.Length; index++)
        {
            var option = byId[order[index]];
            CompactOptions.Move(CompactOptions.IndexOf(option), index);
            option.SetInitialVisibility(saved is null ? IsLegacyGraphEnabled(settings, option.Id) :
                saved.FirstOrDefault(item => item.Id == option.Id)?.Enabled == true);
        }
        SelectedCompactGraph = CompactOptions[0];
        RefreshCompactGraphs();
    }

    private static bool IsLegacyGraphEnabled(AppSettings settings, string id) => id switch
    {
        "CPU" => settings.ShowCompactCpu,
        "CPU-TEMP" => settings.ShowCompactCpuTemp,
        "GPU" => settings.ShowCompactGpu,
        "GPU-TEMP" => settings.ShowCompactGpuTemp,
        "RAM" => settings.ShowCompactRam,
        "VRAM" => settings.ShowCompactVram,
        "NET-RX" or "NET-TX" => settings.ShowCompactNetwork,
        "DISK-READ" or "DISK-WRITE" => settings.ShowCompactDisk,
        _ => false
    };

    private bool CanMoveCompact(int offset)
    {
        int index = SelectedCompactGraph is null ? -1 : CompactOptions.IndexOf(SelectedCompactGraph);
        return index >= 0 && index + offset >= 0 && index + offset < CompactOptions.Count;
    }

    private void MoveCompact(int offset)
    {
        if (!CanMoveCompact(offset)) return;
        int index = CompactOptions.IndexOf(SelectedCompactGraph!);
        CompactOptions.Move(index, index + offset);
        CommandManager.InvalidateRequerySuggested();
        SaveCompact();
    }

    private void RefreshCompactGraphs() => ActiveCompactGraphs = CompactOptions.Where(option => option.IsEnabled).ToArray();

    private void SaveCompact()
    {
        RefreshCompactGraphs();
        if (_settings == null) return;
        _settings.CompactGraphs = CompactOptions.Select(option => new CompactGraphSetting
        {
            Id = option.Id,
            Enabled = option.IsEnabled
        }).ToList();
        QueueSave();
    }

    private void QueueSave()
    {
        if (_settings == null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
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

}

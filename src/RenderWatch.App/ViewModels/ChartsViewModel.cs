using System.Collections.ObjectModel;
using System.Windows.Input;
using RenderWatch.Core.Models;
using RenderWatch.Core.Settings;

namespace RenderWatch.App.ViewModels;

public class ChartsViewModel : ViewModelBase
{
    private int _timeRangeMinutes = 5;
    private int _graphCapacity = 60;
    private bool _showCpu = true;
    private bool _showGpu = true;
    private bool _showCpuTemp = true;
    private bool _showGpuTemp = true;
    private bool _showRam = true;
    private bool _showVram = true;

    private AppSettings? _settings;
    private ISettingsManager? _settingsManager;

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> CpuTempHistory { get; } = new();
    public ObservableCollection<double> GpuTempHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> VramHistory { get; } = new();

    public int GraphCapacity
    {
        get => _graphCapacity;
        set
        {
            if (SetField(ref _graphCapacity, value))
            {
                TrimToMaxCapacity();
            }
        }
    }

    public int TimeRangeMinutes
    {
        get => _timeRangeMinutes;
        set
        {
            if (SetField(ref _timeRangeMinutes, value))
            {
                GraphCapacity = value switch
                {
                    1 => 60,
                    5 => 120,
                    15 => 240,
                    _ => 60
                };
                if (_settings != null)
                {
                    _settings.GraphHistoryMinutes = value;
                    SaveSettings();
                }
            }
        }
    }

    public bool ShowCpu
    {
        get => _showCpu;
        set
        {
            if (SetField(ref _showCpu, value))
            {
                if (_settings != null) { _settings.ShowCpuGraph = value; SaveSettings(); }
            }
        }
    }

    public bool ShowGpu
    {
        get => _showGpu;
        set
        {
            if (SetField(ref _showGpu, value))
            {
                if (_settings != null) { _settings.ShowGpuGraph = value; SaveSettings(); }
            }
        }
    }

    public bool ShowCpuTemp
    {
        get => _showCpuTemp;
        set
        {
            if (SetField(ref _showCpuTemp, value))
            {
                if (_settings != null) { _settings.ShowCpuTempGraph = value; SaveSettings(); }
            }
        }
    }

    public bool ShowGpuTemp
    {
        get => _showGpuTemp;
        set
        {
            if (SetField(ref _showGpuTemp, value))
            {
                if (_settings != null) { _settings.ShowGpuTempGraph = value; SaveSettings(); }
            }
        }
    }

    public bool ShowRam
    {
        get => _showRam;
        set
        {
            if (SetField(ref _showRam, value))
            {
                if (_settings != null) { _settings.ShowRamGraph = value; SaveSettings(); }
            }
        }
    }

    public bool ShowVram
    {
        get => _showVram;
        set
        {
            if (SetField(ref _showVram, value))
            {
                if (_settings != null) { _settings.ShowVramGraph = value; SaveSettings(); }
            }
        }
    }

    public ICommand SetRange1MinCommand { get; }
    public ICommand SetRange5MinCommand { get; }
    public ICommand SetRange15MinCommand { get; }

    public ChartsViewModel()
    {
        SetRange1MinCommand = new RelayCommand(() => TimeRangeMinutes = 1);
        SetRange5MinCommand = new RelayCommand(() => TimeRangeMinutes = 5);
        SetRange15MinCommand = new RelayCommand(() => TimeRangeMinutes = 15);
    }

    public void Initialize(AppSettings settings, ISettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;

        _showCpu = settings.ShowCpuGraph;
        _showGpu = settings.ShowGpuGraph;
        _showCpuTemp = settings.ShowCpuTempGraph;
        _showGpuTemp = settings.ShowGpuTempGraph;
        _showRam = settings.ShowRamGraph;
        _showVram = settings.ShowVramGraph;

        OnPropertyChanged(nameof(ShowCpu));
        OnPropertyChanged(nameof(ShowGpu));
        OnPropertyChanged(nameof(ShowCpuTemp));
        OnPropertyChanged(nameof(ShowGpuTemp));
        OnPropertyChanged(nameof(ShowRam));
        OnPropertyChanged(nameof(ShowVram));

        if (settings.GraphHistoryMinutes > 0)
        {
            _timeRangeMinutes = settings.GraphHistoryMinutes;
            _graphCapacity = _timeRangeMinutes switch
            {
                1 => 60,
                5 => 120,
                15 => 240,
                _ => 60
            };
            OnPropertyChanged(nameof(TimeRangeMinutes));
            OnPropertyChanged(nameof(GraphCapacity));
        }
    }

    private void SaveSettings()
    {
        if (_settings != null && _settingsManager != null)
        {
            _ = _settingsManager.SaveSettingsAsync(_settings);
        }
    }

    public void AddPoints(double? cpu, double? gpu, double? cpuTemp, double? gpuTemp, double? ram, double? vram)
    {
        AddPoint(CpuHistory, cpu ?? 0);
        AddPoint(GpuHistory, gpu ?? 0);
        AddPoint(CpuTempHistory, cpuTemp ?? 0);
        AddPoint(GpuTempHistory, gpuTemp ?? 0);
        AddPoint(RamHistory, ram ?? 0);
        AddPoint(VramHistory, vram ?? 0);
    }

    private void AddPoint(ObservableCollection<double> collection, double val)
    {
        collection.Add(val);
        while (collection.Count > _graphCapacity && collection.Count > 0)
        {
            collection.RemoveAt(0);
        }
    }

    private void TrimToMaxCapacity()
    {
        Trim(CpuHistory, _graphCapacity);
        Trim(GpuHistory, _graphCapacity);
        Trim(CpuTempHistory, _graphCapacity);
        Trim(GpuTempHistory, _graphCapacity);
        Trim(RamHistory, _graphCapacity);
        Trim(VramHistory, _graphCapacity);
    }

    private static void Trim(ObservableCollection<double> collection, int maxPoints)
    {
        while (collection.Count > maxPoints && collection.Count > 0)
        {
            collection.RemoveAt(0);
        }
    }
}
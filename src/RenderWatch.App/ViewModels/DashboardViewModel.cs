using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.Models;
using RenderWatch.Core.Tracking;

namespace RenderWatch.App.ViewModels;

public class FanMetricViewModel : ViewModelBase
{
    private string _id = string.Empty;
    private string _displayName = string.Empty;
    private string _role = string.Empty;
    private string _description = string.Empty;
    private string _hardwareDevice = string.Empty;
    private string _rpmDisplay = "—";
    private string _statusBadge = "Остановлен";
    private string _statusColorHex = "#808080";
    private double _percentValue;
    private bool _isActive;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string Role { get => _role; set => SetField(ref _role, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    public string HardwareDevice { get => _hardwareDevice; set => SetField(ref _hardwareDevice, value); }
    public string RpmDisplay { get => _rpmDisplay; set => SetField(ref _rpmDisplay, value); }
    public string StatusBadge { get => _statusBadge; set => SetField(ref _statusBadge, value); }
    public string StatusColorHex { get => _statusColorHex; set => SetField(ref _statusColorHex, value); }
    public double PercentValue { get => _percentValue; set => SetField(ref _percentValue, value); }
    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }

    public void UpdateFrom(FanMetric metric)
    {
        Id = metric.Id;
        DisplayName = metric.DisplayName;
        Role = metric.Role;
        Description = metric.Description;
        HardwareDevice = metric.HardwareDevice;
        RpmDisplay = metric.RpmDisplay;
        PercentValue = metric.Percent ?? (metric.IsActive ? 50.0 : 0.0);
        IsActive = metric.IsActive;
        StatusBadge = metric.StatusText;
        StatusColorHex = metric.IsActive ? "#A0A0A0" : "#606060";
    }
}

public class FanGroupViewModel : ViewModelBase
{
    private string _groupKey = string.Empty;
    private string _groupName = string.Empty;
    private string _countText = string.Empty;
    private int _order;

    public string GroupKey { get => _groupKey; set => SetField(ref _groupKey, value); }
    public string GroupName { get => _groupName; set => SetField(ref _groupName, value); }
    public string CountText { get => _countText; set => SetField(ref _countText, value); }
    public int Order { get => _order; set => SetField(ref _order, value); }
    public ObservableCollection<FanMetricViewModel> Fans { get; } = new();
}

public class DashboardViewModel : ViewModelBase
{
    private readonly ObservableCollection<FanMetricViewModel> _fans = new();
    public ReadOnlyObservableCollection<FanMetricViewModel> Fans { get; }

    private readonly ObservableCollection<FanGroupViewModel> _fanGroups = new();
    public ReadOnlyObservableCollection<FanGroupViewModel> FanGroups { get; }

    private string _fansSummaryText = "Вентиляторы: опрос...";
    private bool _hasFans = false;
    private int _totalFansCount = 0;
    private int _activeFansCount = 0;
    private string _appName = "Ожидание";
    private string _sceneName = "Нет активных сессий";
    private string _statusText = "Ожидание";
    private string _statusColorHex = "#8A909D";
    private string _durationText = "00:00:00";
    private string _detectionSourceText = "Система в ожидании";

    private string _cpuUsageDisplay = "0%";
    private string _cpuTempDisplay = "Недоступно";
    private string _cpuClockDisplay = "Недоступно";
    private string _cpuPowerDisplay = "Недоступно";
    private double _cpuPercentValue;

    private string _gpuName = "GPU";
    private string _gpuUsageDisplay = "0%";
    private string _gpuTempDisplay = "Недоступно";
    private string _gpuClockDisplay = "Недоступно";
    private string _gpuPowerDisplay = "Недоступно";
    private double _gpuPercentValue;

    private string _ramDisplay = "Недоступно";
    private string _ramCompactDisplay = "Недоступно";
    private string _ramUsedDisplay = "Недоступно";
    private string _ramAvailableDisplay = "Недоступно";
    private string _ramTotalDisplay = "Недоступно";
    private double _ramPercentValue;

    private string _cpuTempMicro = "—";
    private string _gpuTempMicro = "—";
    private string _cpuUsageMicro = "0%";
    private string _gpuUsageMicro = "0%";
    private string _ramPercentDisplay = "0%";

    private string _vramDisplay = "Недоступно";
    private string _vramUsedDisplay = "Недоступно";
    private string _vramAvailableDisplay = "Недоступно";
    private string _vramTotalDisplay = "Недоступно";
    private double _vramPercentValue;

    private string _sensorSources = string.Empty;
    private bool _isTaskbarStripLightMode;

    public bool IsTaskbarStripLightMode
    {
        get => _isTaskbarStripLightMode;
        set
        {
            if (SetField(ref _isTaskbarStripLightMode, value))
            {
                OnPropertyChanged(nameof(IsTaskbarStripDarkMode));
                TaskbarStripThemeChanged?.Invoke(value);
            }
        }
    }

    public bool IsTaskbarStripDarkMode => !IsTaskbarStripLightMode;

    public ICommand SetTaskbarStripThemeCommand { get; }
    public event Action<bool>? TaskbarStripThemeChanged;

    public DashboardViewModel()
    {
        Fans = new ReadOnlyObservableCollection<FanMetricViewModel>(_fans);
        FanGroups = new ReadOnlyObservableCollection<FanGroupViewModel>(_fanGroups);

        SetTaskbarStripThemeCommand = new RelayCommand(p =>
        {
            if (p != null)
            {
                string theme = p.ToString()!.ToLowerInvariant();
                IsTaskbarStripLightMode = (theme == "light" || theme == "true" || theme == "1");
            }
        });
    }

    public string FansSummaryText { get => _fansSummaryText; set => SetField(ref _fansSummaryText, value); }
    public bool HasFans { get => _hasFans; set => SetField(ref _hasFans, value); }
    public int TotalFansCount { get => _totalFansCount; set => SetField(ref _totalFansCount, value); }
    public int ActiveFansCount { get => _activeFansCount; set => SetField(ref _activeFansCount, value); }

    public string AppName { get => _appName; set => SetField(ref _appName, value); }
    public string SceneName { get => _sceneName; set => SetField(ref _sceneName, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public string StatusColorHex { get => _statusColorHex; set => SetField(ref _statusColorHex, value); }
    public string DurationText { get => _durationText; set => SetField(ref _durationText, value); }
    public string DetectionSourceText { get => _detectionSourceText; set => SetField(ref _detectionSourceText, value); }

    public Brush StatusBrush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(StatusColorHex));
            }
            catch
            {
                return Brushes.White;
            }
        }
    }

    public string CpuUsageDisplay { get => _cpuUsageDisplay; set => SetField(ref _cpuUsageDisplay, value); }
    public string CpuTempDisplay { get => _cpuTempDisplay; set => SetField(ref _cpuTempDisplay, value); }
    public string CpuClockDisplay { get => _cpuClockDisplay; set => SetField(ref _cpuClockDisplay, value); }
    public string CpuPowerDisplay { get => _cpuPowerDisplay; set => SetField(ref _cpuPowerDisplay, value); }
    public double CpuPercentValue { get => _cpuPercentValue; set => SetField(ref _cpuPercentValue, value); }

    public string GpuName { get => _gpuName; set => SetField(ref _gpuName, value); }
    public string GpuUsageDisplay { get => _gpuUsageDisplay; set => SetField(ref _gpuUsageDisplay, value); }
    public string GpuTempDisplay { get => _gpuTempDisplay; set => SetField(ref _gpuTempDisplay, value); }
    public string GpuClockDisplay { get => _gpuClockDisplay; set => SetField(ref _gpuClockDisplay, value); }
    public string GpuPowerDisplay { get => _gpuPowerDisplay; set => SetField(ref _gpuPowerDisplay, value); }
    public double GpuPercentValue { get => _gpuPercentValue; set => SetField(ref _gpuPercentValue, value); }

    public string RamDisplay { get => _ramDisplay; set => SetField(ref _ramDisplay, value); }
    public string RamCompactDisplay { get => _ramCompactDisplay; set => SetField(ref _ramCompactDisplay, value); }
    public string RamUsedDisplay { get => _ramUsedDisplay; set => SetField(ref _ramUsedDisplay, value); }
    public string RamAvailableDisplay { get => _ramAvailableDisplay; set => SetField(ref _ramAvailableDisplay, value); }
    public string RamTotalDisplay { get => _ramTotalDisplay; set => SetField(ref _ramTotalDisplay, value); }
    public double RamPercentValue { get => _ramPercentValue; set => SetField(ref _ramPercentValue, value); }

    public string CpuTempMicro { get => _cpuTempMicro; set => SetField(ref _cpuTempMicro, value); }
    public string GpuTempMicro { get => _gpuTempMicro; set => SetField(ref _gpuTempMicro, value); }
    public string CpuUsageMicro { get => _cpuUsageMicro; set => SetField(ref _cpuUsageMicro, value); }
    public string GpuUsageMicro { get => _gpuUsageMicro; set => SetField(ref _gpuUsageMicro, value); }
    public string RamPercentDisplay { get => _ramPercentDisplay; set => SetField(ref _ramPercentDisplay, value); }

    public string VramDisplay { get => _vramDisplay; set => SetField(ref _vramDisplay, value); }
    public string VramUsedDisplay { get => _vramUsedDisplay; set => SetField(ref _vramUsedDisplay, value); }
    public string VramAvailableDisplay { get => _vramAvailableDisplay; set => SetField(ref _vramAvailableDisplay, value); }
    public string VramTotalDisplay { get => _vramTotalDisplay; set => SetField(ref _vramTotalDisplay, value); }
    public double VramPercentValue { get => _vramPercentValue; set => SetField(ref _vramPercentValue, value); }

    public string SensorSources { get => _sensorSources; set => SetField(ref _sensorSources, value); }

    public void Update(HardwareMetrics metrics, TrackerUpdateResult tracker)
    {
        AppName = tracker.AppName;
        SceneName = tracker.SceneName;
        StatusText = tracker.Status.ToDisplayName();
        StatusColorHex = tracker.Status.GetStatusColorHex();
        OnPropertyChanged(nameof(StatusBrush));

        DurationText = tracker.ElapsedDuration.ToString(@"hh\:mm\:ss");
        DetectionSourceText = tracker.StatusDetectionSource;

        CpuUsageDisplay = metrics.CpuUsageDisplay;
        CpuTempDisplay = metrics.CpuTempDisplay;
        CpuClockDisplay = metrics.CpuClockDisplay;
        CpuPowerDisplay = metrics.CpuPowerDisplay;
        CpuPercentValue = metrics.CpuUsagePercent ?? 0.0;

        if (!string.IsNullOrEmpty(metrics.GpuName)) GpuName = metrics.GpuName;
        GpuUsageDisplay = metrics.GpuUsageDisplay;
        GpuTempDisplay = metrics.GpuTempDisplay;
        GpuClockDisplay = metrics.GpuClockDisplay;
        GpuPowerDisplay = metrics.GpuPowerDisplay;
        GpuPercentValue = metrics.GpuUsagePercent ?? 0.0;

        RamDisplay = metrics.RamDisplay;
        RamCompactDisplay = metrics.RamCompactDisplay;
        RamPercentValue = metrics.RamUsagePercent ?? 0.0;

        if (metrics.RamUsedGb.HasValue)
            RamUsedDisplay = $"{metrics.RamUsedGb.Value:F1} ГБ";
        if (metrics.RamAvailableGb.HasValue)
            RamAvailableDisplay = $"{metrics.RamAvailableGb.Value:F1} ГБ";
        if (metrics.RamTotalGb.HasValue)
            RamTotalDisplay = $"{metrics.RamTotalGb.Value:F1} ГБ";

        CpuTempMicro = metrics.CpuTempMicro;
        GpuTempMicro = metrics.GpuTempMicro;
        CpuUsageMicro = metrics.CpuUsageMicro;
        GpuUsageMicro = metrics.GpuUsageMicro;
        RamPercentDisplay = metrics.RamPercentDisplay;

        VramDisplay = metrics.VramDisplay;
        VramPercentValue = metrics.VramUsagePercent ?? 0.0;

        if (metrics.VramUsedGb.HasValue)
            VramUsedDisplay = $"{metrics.VramUsedGb.Value:F1} ГБ";
        if (metrics.VramAvailableGb.HasValue)
            VramAvailableDisplay = $"{metrics.VramAvailableGb.Value:F1} ГБ";
        if (metrics.VramTotalGb.HasValue)
            VramTotalDisplay = $"{metrics.VramTotalGb.Value:F1} ГБ";

        SensorSources = metrics.SensorSourceDetails;

        // Update Fans
        if (metrics.Fans != null && metrics.Fans.Count > 0)
        {
            HasFans = true;
            TotalFansCount = metrics.TotalFansCount;
            ActiveFansCount = metrics.ActiveFansCount;
            FansSummaryText = metrics.FansSummaryDisplay;

            var existingMap = _fans.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
            var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in metrics.Fans)
            {
                currentIds.Add(m.Id);
                if (existingMap.TryGetValue(m.Id, out var vm))
                {
                    vm.UpdateFrom(m);
                }
                else
                {
                    var newVm = new FanMetricViewModel();
                    newVm.UpdateFrom(m);
                    _fans.Add(newVm);
                }
            }

            for (int i = _fans.Count - 1; i >= 0; i--)
            {
                if (!currentIds.Contains(_fans[i].Id))
                {
                    _fans.RemoveAt(i);
                }
            }

            for (int targetIndex = 0; targetIndex < metrics.Fans.Count; targetIndex++)
            {
                var targetId = metrics.Fans[targetIndex].Id;
                int currentIndex = -1;
                for (int i = 0; i < _fans.Count; i++)
                {
                    if (string.Equals(_fans[i].Id, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex >= 0 && currentIndex != targetIndex && targetIndex < _fans.Count)
                {
                    _fans.Move(currentIndex, targetIndex);
                }
            }

            // Sync categorized hardware groups
            UpdateFanGroups(metrics.Fans, metrics.IsLiquidCooling);
        }
        else
        {
            HasFans = false;
            TotalFansCount = 0;
            ActiveFansCount = 0;
            FansSummaryText = "Определение...";
            _fans.Clear();
            _fanGroups.Clear();
        }
    }

    private static (string GroupKey, string GroupName, int Order) DetermineFanGroup(FanMetric m, bool hasLiquidCoolingInSystem = false)
    {
        string id = (m.Id ?? string.Empty).ToLowerInvariant();
        string role = (m.Role ?? string.Empty).ToLowerInvariant();
        string name = (m.DisplayName ?? string.Empty).ToLowerInvariant();

        // 1. Laptop fans
        if (id.StartsWith("laptop-") || name.Contains("ноутбук") || role.Contains("ноутбук"))
        {
            return ("laptop", "ОХЛАЖДЕНИЕ НОУТБУКА", 1);
        }

        // 2. Liquid Cooling (AIO Pump / Radiator) & CPU
        if (id.Contains("pump") || id.Contains("aio") || id.Contains("rad") || id.Contains("cpu") ||
            role.Contains("помпа") || role.Contains("сжо") || role.Contains("радиатор") || role.Contains("cpu") || role.Contains("процессор") ||
            name.Contains("помпа") || name.Contains("сжо") || name.Contains("радиатор") || name.Contains("cpu"))
        {
            if (hasLiquidCoolingInSystem)
            {
                return ("aio", "ПРОЦЕССОР И СЖО", 1);
            }
            else
            {
                return ("cpu", "ОХЛАЖДЕНИЕ ПРОЦЕССОРА", 1);
            }
        }

        // 3. GPU / Graphics
        if (id.Contains("gpu") || role.Contains("gpu") || role.Contains("видеокарт") || name.Contains("gpu") || name.Contains("видеокарт"))
        {
            return ("gpu", "ВИДЕОКАРТА (GPU)", 2);
        }

        // 4. Chassis / Case fans
        if (id.Contains("chassis") || id.Contains("case") || role.Contains("корпус") || role.Contains("chassis") || name.Contains("корпус"))
        {
            return ("chassis", "КОРПУСНЫЕ ВЕНТИЛЯТОРЫ", 3);
        }

        // 5. Power Supply
        if (id.Contains("psu") || role.Contains("блок питания") || role.Contains("psu") || name.Contains("блок питания") || name.Contains("psu"))
        {
            return ("psu", "БЛОК ПИТАНИЯ (PSU)", 4);
        }

        return ("other", "ДОПОЛНИТЕЛЬНЫЕ КОНТУРЫ", 5);
    }

    private void UpdateFanGroups(IList<FanMetric> rawFans, bool isLiquidCooling = false)
    {
        bool isUserAirForced = string.Equals(CompositeHardwareMonitor.UserCoolingPreference, "air", StringComparison.OrdinalIgnoreCase);
        bool isUserLiquidForced = string.Equals(CompositeHardwareMonitor.UserCoolingPreference, "liquid", StringComparison.OrdinalIgnoreCase);

        bool hasLiquidCoolingInSystem;
        if (isUserAirForced)
        {
            hasLiquidCoolingInSystem = false;
        }
        else if (isUserLiquidForced)
        {
            hasLiquidCoolingInSystem = true;
        }
        else
        {
            hasLiquidCoolingInSystem = isLiquidCooling;
        }

        // Sanitize any remaining liquid cooling tags if air cooled
        if (!hasLiquidCoolingInSystem)
        {
            foreach (var f in rawFans)
            {
                if ((f.Role ?? "").Contains("Помпа", StringComparison.OrdinalIgnoreCase) ||
                    (f.DisplayName ?? "").Contains("Помпа", StringComparison.OrdinalIgnoreCase) ||
                    (f.DisplayName ?? "").Contains("СЖО", StringComparison.OrdinalIgnoreCase))
                {
                    f.Role = "Корпус";
                    f.DisplayName = $"Вентилятор ({f.Name})";
                    f.Description = "Вентилятор, подключенный к разъему материнской платы";
                }
            }
        }

        var rawGrouped = rawFans
            .Select(f => (Fan: f, Group: DetermineFanGroup(f, hasLiquidCoolingInSystem)))
            .GroupBy(x => x.Group.GroupKey)
            .ToList();

        var existingGroupMap = _fanGroups.ToDictionary(g => g.GroupKey, StringComparer.OrdinalIgnoreCase);
        var activeGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grp in rawGrouped)
        {
            string key = grp.Key;
            activeGroupKeys.Add(key);
            var firstInfo = grp.First().Group;

            if (!existingGroupMap.TryGetValue(key, out var groupVm))
            {
                groupVm = new FanGroupViewModel
                {
                    GroupKey = key,
                    GroupName = firstInfo.GroupName,
                    Order = firstInfo.Order
                };
                _fanGroups.Add(groupVm);
                existingGroupMap[key] = groupVm;
            }

            groupVm.GroupName = firstInfo.GroupName;
            groupVm.Order = firstInfo.Order;

            // Sync fans in this group
            var targetFanIds = grp.Select(x => x.Fan.Id).ToList();
            var targetFanIdSet = targetFanIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groupFanMap = groupVm.Fans.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var item in grp)
            {
                var m = item.Fan;
                var fanVm = _fans.FirstOrDefault(f => string.Equals(f.Id, m.Id, StringComparison.OrdinalIgnoreCase));
                if (fanVm != null && !groupFanMap.ContainsKey(fanVm.Id))
                {
                    groupVm.Fans.Add(fanVm);
                }
            }

            for (int i = groupVm.Fans.Count - 1; i >= 0; i--)
            {
                if (!targetFanIdSet.Contains(groupVm.Fans[i].Id))
                {
                    groupVm.Fans.RemoveAt(i);
                }
            }

            for (int targetIndex = 0; targetIndex < targetFanIds.Count; targetIndex++)
            {
                var targetId = targetFanIds[targetIndex];
                int curIdx = -1;
                for (int i = 0; i < groupVm.Fans.Count; i++)
                {
                    if (string.Equals(groupVm.Fans[i].Id, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        curIdx = i;
                        break;
                    }
                }
                if (curIdx >= 0 && curIdx != targetIndex && targetIndex < groupVm.Fans.Count)
                {
                    groupVm.Fans.Move(curIdx, targetIndex);
                }
            }

            int total = groupVm.Fans.Count;
            int active = groupVm.Fans.Count(f => f.IsActive);
            int passive = total - active;
            if (passive > 0)
            {
                groupVm.CountText = $"{total} шт. ({active} активны, {passive} пассивны)";
            }
            else
            {
                groupVm.CountText = $"{total} шт. ({active} активны)";
            }
        }

        for (int i = _fanGroups.Count - 1; i >= 0; i--)
        {
            if (!activeGroupKeys.Contains(_fanGroups[i].GroupKey))
            {
                _fanGroups.RemoveAt(i);
            }
        }

        var sorted = _fanGroups.OrderBy(g => g.Order).ToList();
        for (int targetIdx = 0; targetIdx < sorted.Count; targetIdx++)
        {
            int curIdx = _fanGroups.IndexOf(sorted[targetIdx]);
            if (curIdx >= 0 && curIdx != targetIdx)
            {
                _fanGroups.Move(curIdx, targetIdx);
            }
        }
    }
}
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RenderWatch.Core.Models;

public class SystemConfigurationInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // CPU
    public string CpuName { get; set; } = "Определение...";
    public string CpuCoresThreads { get; set; } = string.Empty;
    public string CpuFrequency { get; set; } = string.Empty;
    public string CpuMaxFrequency { get; set; } = string.Empty;
    public string CpuSocket { get; set; } = string.Empty;
    public string CpuCache { get; set; } = string.Empty;
    public string CpuArchitecture { get; set; } = "x64 (64-bit)";

    // RAM
    public string RamTotalCapacity { get; set; } = string.Empty;
    public string RamType { get; set; } = string.Empty;
    public string RamSpeed { get; set; } = string.Empty;
    public string RamChannelMode { get; set; } = "Dual-Channel (2-канальный режим)";
    public string RamChannelBadge { get; set; } = "2-канальный";
    public string RamSlotsUsed { get; set; } = string.Empty;
    public List<RamModuleInfo> RamModules { get; set; } = new();

    // Motherboard
    public string MotherboardManufacturer { get; set; } = string.Empty;
    public string MotherboardProduct { get; set; } = string.Empty;
    public string MotherboardVersion { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosDate { get; set; } = string.Empty;
    public string BiosVendor { get; set; } = string.Empty;

    // GPU
    public string PrimaryGpuName { get; set; } = string.Empty;
    public string PrimaryGpuVram { get; set; } = string.Empty;
    public string PrimaryGpuDriver { get; set; } = string.Empty;
    public string SecondaryGpuName { get; set; } = string.Empty;
    public string SecondaryGpuVram { get; set; } = string.Empty;
    public bool HasSecondaryGpu => !string.IsNullOrWhiteSpace(SecondaryGpuName);

    // Storage / Disks
    public string TotalStorageCapacity { get; set; } = string.Empty;
    public string PhysicalDisksSummary { get; set; } = string.Empty;
    public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = new();
    public List<DiskDriveInfo> Disks { get; set; } = new();

    // Power & Battery / PSU
    public bool IsLaptop { get; set; }
    public string SystemType { get; set; } = "Стационарный ПК";

    private string _powerSource = "Питание от электросети";
    public string PowerSource
    {
        get => _powerSource;
        set
        {
            if (_powerSource != value)
            {
                _powerSource = value;
                OnPropertyChanged();
            }
        }
    }
    public string ActivePowerPlan { get; set; } = "Сбалансированная";
    public string BatteryStatus { get; set; } = "Не применимо (Стационарный ПК)";
    public string BatteryHealth { get; set; } = "Отсутствует (Питание от электросети)";
    public string SystemEstimatedTdp { get; set; } = "~400 Вт (Пиковая нагрузка)";
    public string RecommendedPsu { get; set; } = "от 650–750 Вт";
}

public class RamModuleInfo
{
    public string Slot { get; set; } = string.Empty;
    public string Capacity { get; set; } = string.Empty;
    public string Speed { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string ChannelBank { get; set; } = string.Empty;
}

public class DiskDriveInfo
{
    public string Name { get; set; } = string.Empty;
    public string TotalSpace { get; set; } = string.Empty;
    public string FreeSpace { get; set; } = string.Empty;
    public string UsedSpace { get; set; } = string.Empty;
    public double UsedPercent { get; set; }
    public string FileSystem { get; set; } = "NTFS";
    public string DiskType { get; set; } = "Локальный накопитель";
}

public class PhysicalDiskInfo
{
    public int Index { get; set; }
    public string DisplayIndex => $"Накопитель #{Index + 1}";
    public string Model { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = "NVMe / SSD";
    public string Capacity { get; set; } = string.Empty;
    public string PartitionsCount { get; set; } = string.Empty;
}

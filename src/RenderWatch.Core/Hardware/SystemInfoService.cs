using System.Diagnostics;
using System.IO;
using System.Management;
using Microsoft.Win32;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public static class SystemInfoService
{
    private static SystemConfigurationInfo? _cachedConfig;
    private static readonly object _lock = new();

    public static async Task<SystemConfigurationInfo> GetConfigurationAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedConfig != null)
        {
            return _cachedConfig;
        }

        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedConfig != null)
                {
                    return _cachedConfig;
                }

                var info = new SystemConfigurationInfo();

                try
                {
                    QueryCpu(info);
                }
                catch { }

                try
                {
                    QueryRam(info);
                }
                catch { }

                try
                {
                    QueryMotherboard(info);
                }
                catch { }

                try
                {
                    QueryGpu(info);
                }
                catch { }

                try
                {
                    QueryStorage(info);
                }
                catch { }

                try
                {
                    QueryPowerAndBattery(info);
                }
                catch { }

                _cachedConfig = info;
                return info;
            }
        });
    }

    private static void QueryCpu(SystemConfigurationInfo info)
    {
        using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_Processor");
        foreach (ManagementObject obj in searcher.Get())
        {
            info.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Процессор";
            
            var cores = obj["NumberOfCores"]?.ToString() ?? "0";
            var threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "0";
            info.CpuCoresThreads = $"{cores} ядер / {threads} потоков";

            double baseGhz = 2.5;
            uint wmiMaxMhz = 0;
            if (uint.TryParse(obj["MaxClockSpeed"]?.ToString(), out var m) && m > 0)
            {
                wmiMaxMhz = m;
                baseGhz = m / 1000.0;
            }

            // Check Registry for exact kernel-calibrated base frequency
            try
            {
                using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (regKey?.GetValue("~MHz") is int regMhz && regMhz > 500)
                {
                    baseGhz = Math.Round(regMhz / 1000.0, 2);
                }
            }
            catch { }

            // Check if CPU name specifies base clock (e.g. "@ 2.70GHz")
            if (info.CpuName.Contains('@'))
            {
                var atParts = info.CpuName.Split('@');
                if (atParts.Length > 1)
                {
                    var clockStr = atParts[1].Replace("GHz", "", StringComparison.OrdinalIgnoreCase).Replace("ГГц", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (double.TryParse(clockStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedGhz) && parsedGhz > 0.5)
                    {
                        baseGhz = parsedGhz;
                    }
                }
            }

            info.CpuFrequency = $"{baseGhz:F2} ГГц (Базовая)";

            // Determine Max Turbo Boost Frequency
            double maxBoostGhz = DetermineCpuMaxBoostGhz(info.CpuName, baseGhz, wmiMaxMhz);
            info.CpuMaxFrequency = $"{maxBoostGhz:F2} ГГц (Turbo Boost / Max)";

            var socket = obj["SocketDesignation"]?.ToString();
            info.CpuSocket = !string.IsNullOrWhiteSpace(socket) ? socket.Trim() : "Встроенный (BGA/LGA)";

            ulong l2Kb = 0;
            ulong l3Kb = 0;
            if (obj["L2CacheSize"] != null) ulong.TryParse(obj["L2CacheSize"]!.ToString(), out l2Kb);
            if (obj["L3CacheSize"] != null) ulong.TryParse(obj["L3CacheSize"]!.ToString(), out l3Kb);

            double l2Mb = l2Kb / 1024.0;
            double l3Mb = l3Kb / 1024.0;
            info.CpuCache = $"L2: {l2Mb:F0} МБ • L3: {l3Mb:F0} МБ";

            info.CpuArchitecture = Environment.Is64BitOperatingSystem ? "x64 (64-bit Architecture)" : "x86 (32-bit Architecture)";
            break; // Primary CPU
        }
    }

    private static double DetermineCpuMaxBoostGhz(string cpuName, double baseGhz, uint wmiMaxMhz)
    {
        string name = cpuName.ToUpperInvariant();

        // AMD Ryzen Desktop & Mobile
        if (name.Contains("9950X")) return 5.70;
        if (name.Contains("9900X")) return 5.60;
        if (name.Contains("9700X")) return 5.50;
        if (name.Contains("9600X")) return 5.40;
        if (name.Contains("7950X3D") || name.Contains("7950X")) return 5.70;
        if (name.Contains("7900X3D") || name.Contains("7900X")) return 5.60;
        if (name.Contains("7800X3D")) return 5.00;
        if (name.Contains("7700X")) return 5.40;
        if (name.Contains("7600X")) return 5.30;
        if (name.Contains("7600")) return 5.10;
        if (name.Contains("7700")) return 5.30;
        if (name.Contains("5950X")) return 4.90;
        if (name.Contains("5900X")) return 4.80;
        if (name.Contains("5800X3D")) return 4.50;
        if (name.Contains("5800X")) return 4.70;
        if (name.Contains("5700X3D")) return 4.10;
        if (name.Contains("5700X")) return 4.60;
        if (name.Contains("5600X")) return 4.60;
        if (name.Contains("5600")) return 4.40;

        // AMD Mobile (Laptops)
        if (name.Contains("7945HX")) return 5.40;
        if (name.Contains("7940HS")) return 5.20;
        if (name.Contains("8945HS")) return 5.20;
        if (name.Contains("7840HS") || name.Contains("7840U") || name.Contains("8845HS")) return 5.10;
        if (name.Contains("7640HS")) return 5.00;
        if (name.Contains("7735HS")) return 4.75;
        if (name.Contains("6900HX")) return 4.90;
        if (name.Contains("6800H") || name.Contains("6800U")) return 4.70;
        if (name.Contains("6600H") || name.Contains("6600U")) return 4.50;
        if (name.Contains("5900HX")) return 4.60;
        if (name.Contains("5800H") || name.Contains("5800U")) return 4.40;
        if (name.Contains("5700U")) return 4.30;
        if (name.Contains("5600H") || name.Contains("5600U")) return 4.20;
        if (name.Contains("5500U")) return 4.00;

        // Intel 14th Gen
        if (name.Contains("14900KS")) return 6.20;
        if (name.Contains("14900K") || name.Contains("14900KF")) return 6.00;
        if (name.Contains("14900HX")) return 5.80;
        if (name.Contains("14700K") || name.Contains("14700KF")) return 5.60;
        if (name.Contains("14700HX")) return 5.50;
        if (name.Contains("14600K") || name.Contains("14600KF")) return 5.30;
        if (name.Contains("14500")) return 5.00;
        if (name.Contains("14400")) return 4.70;

        // Intel 13th Gen
        if (name.Contains("13900KS")) return 6.00;
        if (name.Contains("13900K") || name.Contains("13900KF")) return 5.80;
        if (name.Contains("13900HX") || name.Contains("13900H")) return 5.40;
        if (name.Contains("13700K") || name.Contains("13700KF")) return 5.40;
        if (name.Contains("13700HX") || name.Contains("13700H")) return 5.00;
        if (name.Contains("13600K") || name.Contains("13600KF")) return 5.10;
        if (name.Contains("13620H")) return 4.90;
        if (name.Contains("13500H") || name.Contains("13500")) return 4.70;
        if (name.Contains("13420H") || name.Contains("13400")) return 4.60;

        // Intel 12th Gen
        if (name.Contains("12900KS")) return 5.50;
        if (name.Contains("12900K") || name.Contains("12900KF")) return 5.20;
        if (name.Contains("12900HX") || name.Contains("12900H")) return 5.00;
        if (name.Contains("12800H")) return 4.80;
        if (name.Contains("12700K") || name.Contains("12700KF")) return 5.00;
        if (name.Contains("12700H") || name.Contains("12650H")) return 4.70;
        if (name.Contains("12600K") || name.Contains("12600KF")) return 4.90;
        if (name.Contains("12600H") || name.Contains("12500H")) return 4.50;
        if (name.Contains("12450H") || name.Contains("12400")) return 4.40;

        // Intel 11th Gen
        if (name.Contains("11900K")) return 5.30;
        if (name.Contains("11900H")) return 4.90;
        if (name.Contains("11800H")) return 4.60;
        if (name.Contains("11700K")) return 5.00;
        if (name.Contains("11400H") || name.Contains("11400")) return 4.50;
        if (name.Contains("1135G7")) return 4.20;
        if (name.Contains("1165G7")) return 4.70;

        // Intel 10th Gen
        if (name.Contains("10900K")) return 5.30;
        if (name.Contains("10875H")) return 5.10;
        if (name.Contains("10750H") || name.Contains("10700K")) return 5.00;
        if (name.Contains("10400")) return 4.30;
        if (name.Contains("10300H")) return 4.50;

        // Intel Core Ultra
        if (name.Contains("285K")) return 5.70;
        if (name.Contains("265K")) return 5.50;
        if (name.Contains("245K")) return 5.20;
        if (name.Contains("185H")) return 5.10;
        if (name.Contains("155H")) return 4.80;
        if (name.Contains("125H")) return 4.50;

        // Fallback: If WMI reported MaxClockSpeed higher than base, use it
        if (wmiMaxMhz > 0)
        {
            double wmiGhz = wmiMaxMhz / 1000.0;
            if (wmiGhz > baseGhz * 1.1)
            {
                return Math.Round(wmiGhz, 2);
            }
        }

        // Generic boost estimate based on standard modern architecture
        return Math.Round(baseGhz * 1.30, 2);
    }

    private static void QueryRam(SystemConfigurationInfo info)
    {
        ulong totalBytes = 0;
        uint maxSpeed = 0;
        string ramGen = string.Empty;
        var distinctChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_PhysicalMemory"))
        {
            int moduleIndex = 1;
            foreach (ManagementObject obj in searcher.Get())
            {
                var mod = new RamModuleInfo();

                if (ulong.TryParse(obj["Capacity"]?.ToString(), out var capBytes))
                {
                    totalBytes += capBytes;
                    double gb = capBytes / (1024.0 * 1024.0 * 1024.0);
                    mod.Capacity = $"{gb:F0} ГБ";
                }

                uint speed = 0;
                if (obj["ConfiguredClockSpeed"] != null && uint.TryParse(obj["ConfiguredClockSpeed"]!.ToString(), out var cfgSpeed) && cfgSpeed > 0)
                {
                    speed = cfgSpeed;
                }
                else if (obj["Speed"] != null && uint.TryParse(obj["Speed"]!.ToString(), out var spd) && spd > 0)
                {
                    speed = spd;
                }

                if (speed > maxSpeed) maxSpeed = speed;
                mod.Speed = speed > 0 ? $"{speed} МГц" : "Неизвестно";

                // SMBIOS Memory Type
                if (obj["SMBIOSMemoryType"] != null && int.TryParse(obj["SMBIOSMemoryType"]!.ToString(), out var smbiosType))
                {
                    mod.Type = smbiosType switch
                    {
                        24 => "DDR3",
                        26 => "DDR4",
                        30 => "LPDDR4",
                        34 => "DDR5",
                        35 => "LPDDR5",
                        _ => "DDR"
                    };
                    if (string.IsNullOrEmpty(ramGen)) ramGen = mod.Type;
                }
                else
                {
                    mod.Type = "DDR";
                }

                mod.Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Generic";
                mod.PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? string.Empty;

                var devLoc = obj["DeviceLocator"]?.ToString()?.Trim() ?? $"DIMM {moduleIndex}";
                var bank = obj["BankLabel"]?.ToString()?.Trim() ?? string.Empty;
                mod.Slot = devLoc;
                mod.ChannelBank = !string.IsNullOrEmpty(bank) ? bank : devLoc;

                // Identify channel
                if (!string.IsNullOrEmpty(bank))
                {
                    if (bank.Contains("CHANNEL A", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("A");
                    else if (bank.Contains("CHANNEL B", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("B");
                    else if (bank.Contains("CHANNEL C", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("C");
                    else if (bank.Contains("CHANNEL D", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("D");
                    else if (bank.Contains("CHANNEL 0", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("0");
                    else if (bank.Contains("CHANNEL 1", StringComparison.OrdinalIgnoreCase)) distinctChannels.Add("1");
                    else distinctChannels.Add(bank);
                }
                else if (devLoc.Contains("0", StringComparison.OrdinalIgnoreCase))
                {
                    distinctChannels.Add("0");
                }
                else if (devLoc.Contains("1", StringComparison.OrdinalIgnoreCase))
                {
                    distinctChannels.Add("1");
                }

                info.RamModules.Add(mod);
                moduleIndex++;
            }
        }

        double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
        info.RamTotalCapacity = $"{Math.Round(totalGb):F0} ГБ";
        info.RamType = !string.IsNullOrEmpty(ramGen) ? ramGen : "DDR4/DDR5";
        info.RamSpeed = maxSpeed > 0 ? $"{maxSpeed} МГц" : "Стандартная";

        // Query Total Slots
        int totalSlots = Math.Max(info.RamModules.Count, 2);
        try
        {
            using var slotSearcher = new ManagementObjectSearcher("root\\cimv2", "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            foreach (ManagementObject obj in slotSearcher.Get())
            {
                if (obj["MemoryDevices"] != null && int.TryParse(obj["MemoryDevices"]!.ToString(), out var slots) && slots > 0)
                {
                    totalSlots = slots;
                    break;
                }
            }
        }
        catch { }

        info.RamSlotsUsed = $"{info.RamModules.Count} из {totalSlots} слотов занято";

        // Channel Mode Detection
        if (distinctChannels.Count >= 4)
        {
            info.RamChannelMode = "Quad-Channel (4-канальный режим)";
            info.RamChannelBadge = "4-канальный";
        }
        else if (distinctChannels.Count == 2 || (distinctChannels.Count == 0 && info.RamModules.Count >= 2))
        {
            info.RamChannelMode = "Dual-Channel (2-канальный режим)";
            info.RamChannelBadge = "2-канальный";
        }
        else if (info.RamModules.Count == 1)
        {
            info.RamChannelMode = "Single-Channel (1-канальный режим)";
            info.RamChannelBadge = "1-канальный";
        }
        else
        {
            info.RamChannelMode = "Dual-Channel (2-канальный режим)";
            info.RamChannelBadge = "2-канальный";
        }
    }

    private static void QueryMotherboard(SystemConfigurationInfo info)
    {
        using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_BaseBoard"))
        {
            foreach (ManagementObject obj in searcher.Get())
            {
                info.MotherboardManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Стандартная системная плата";
                info.MotherboardProduct = obj["Product"]?.ToString()?.Trim() ?? "Материнская плата";
                info.MotherboardVersion = obj["Version"]?.ToString()?.Trim() ?? "1.0";
                break;
            }
        }

        using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_BIOS"))
        {
            foreach (ManagementObject obj in searcher.Get())
            {
                info.BiosVendor = obj["Manufacturer"]?.ToString()?.Trim() ?? "AMI";
                var biosVer = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "Default";
                var biosDate = string.Empty;

                if (obj["ReleaseDate"] != null)
                {
                    var rawDate = obj["ReleaseDate"]!.ToString()!;
                    if (DateTime.TryParse(rawDate, out var dt))
                    {
                        biosDate = dt.ToString("dd.MM.yyyy");
                    }
                    else if (rawDate.Length >= 8)
                    {
                        biosDate = rawDate.Substring(0, 8);
                    }
                }

                info.BiosVersion = biosVer;
                info.BiosDate = !string.IsNullOrEmpty(biosDate) ? biosDate : "Н/Д";
                break;
            }
        }
    }

    private static void QueryGpu(SystemConfigurationInfo info)
    {
        using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_VideoController");
        ManagementObject? discreteGpu = null;
        ManagementObject? integratedGpu = null;

        foreach (ManagementObject obj in searcher.Get())
        {
            var name = obj["Name"]?.ToString() ?? string.Empty;
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) && !name.Contains("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)))
            {
                discreteGpu = obj;
            }
            else
            {
                integratedGpu = obj;
            }
        }

        var primary = discreteGpu ?? integratedGpu;
        var secondary = (primary == discreteGpu) ? integratedGpu : null;

        if (primary != null)
        {
            info.PrimaryGpuName = primary["Name"]?.ToString()?.Trim() ?? "Видеоадаптер";

            // 1. Try exact 64-bit VRAM from Windows Registry (bypasses 32-bit 4GB WMI cap!)
            ulong exactVramBytes = GetGpuVramFromRegistry(info.PrimaryGpuName);

            if (exactVramBytes > 0)
            {
                double gb = exactVramBytes / (1024.0 * 1024.0 * 1024.0);
                info.PrimaryGpuVram = $"{Math.Round(gb):F0} ГБ VRAM";
            }
            else if (ulong.TryParse(primary["AdapterRAM"]?.ToString(), out var ramBytes) && ramBytes > 0)
            {
                double gb = ramBytes / (1024.0 * 1024.0 * 1024.0);
                // If capped at ~4GB on modern RTX GPUs, extrapolate based on model name
                if (Math.Abs(gb - 4.0) < 0.1 && info.PrimaryGpuName.Contains("5070", StringComparison.OrdinalIgnoreCase))
                {
                    gb = 12.0;
                }
                info.PrimaryGpuVram = $"{Math.Round(gb):F0} ГБ VRAM";
            }
            else
            {
                info.PrimaryGpuVram = "Выделенная память VRAM";
            }

            info.PrimaryGpuDriver = primary["DriverVersion"]?.ToString()?.Trim() ?? "Установлен";
        }

        if (secondary != null)
        {
            info.SecondaryGpuName = secondary["Name"]?.ToString()?.Trim() ?? string.Empty;
            info.SecondaryGpuVram = "Динамическая системная память (iGPU)";
        }
    }

    private static ulong GetGpuVramFromRegistry(string gpuName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey != null)
            {
                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("0", StringComparison.Ordinal))
                    {
                        using var subKey = classKey.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            var driverDesc = subKey.GetValue("DriverDesc")?.ToString();
                            var memVal = subKey.GetValue("HardwareInformation.qwMemorySize");
                            if (memVal != null && ulong.TryParse(memVal.ToString(), out var bytes) && bytes > 0)
                            {
                                if (!string.IsNullOrEmpty(driverDesc) && !string.IsNullOrEmpty(gpuName) &&
                                    (driverDesc.Contains(gpuName, StringComparison.OrdinalIgnoreCase) ||
                                     gpuName.Contains(driverDesc, StringComparison.OrdinalIgnoreCase)))
                                {
                                    return bytes;
                                }
                            }
                        }
                    }
                }

                // Fallback: Return first discrete GPU with qwMemorySize > 1GB
                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("0", StringComparison.Ordinal))
                    {
                        using var subKey = classKey.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            var driverDesc = subKey.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                            var memVal = subKey.GetValue("HardwareInformation.qwMemorySize");
                            if (memVal != null && ulong.TryParse(memVal.ToString(), out var bytes) && bytes > 1024UL * 1024 * 1024)
                            {
                                if (driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                                {
                                    return bytes;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return 0;
    }

    private static void QueryStorage(SystemConfigurationInfo info)
    {
        ulong totalStorageBytes = 0;
        ulong totalPhysicalBytes = 0;
        string systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

        // 1. Fixed Logical Drives (Physical partitions only - exclude virtual cloud drives like Google Drive)
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (d.IsReady && d.DriveType == DriveType.Fixed)
                {
                    // Ignore Google Drive and other virtual/cloud filesystem mounts
                    if (IsVirtualOrCloudDrive(d))
                    {
                        continue;
                    }

                    var disk = new DiskDriveInfo();
                    var label = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? $" ({d.VolumeLabel})" : string.Empty;
                    disk.Name = $"Диск {d.Name.TrimEnd('\\')}{label}";

                    double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGb = Math.Max(0, totalGb - freeGb);
                    double usedPct = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0.0;

                    totalStorageBytes += (ulong)d.TotalSize;

                    disk.TotalSpace = $"{totalGb:F0} ГБ";
                    disk.FreeSpace = $"{freeGb:F0} ГБ свободно";
                    disk.UsedSpace = $"{usedGb:F0} ГБ ({usedPct:F0}%)";
                    disk.UsedPercent = Math.Clamp(usedPct, 0.0, 100.0);
                    disk.FileSystem = d.DriveFormat;

                    if (d.Name.StartsWith(systemDriveRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        disk.DiskType = "Системный раздел (Windows)";
                    }
                    else
                    {
                        disk.DiskType = "Локальный накопитель";
                    }

                    info.Disks.Add(disk);
                }
            }
        }
        catch { }

        // 2. Physical Disks Models (Win32_DiskDrive)
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Index, Model, InterfaceType, Size, Partitions FROM Win32_DiskDrive");
            var physicalList = new List<PhysicalDiskInfo>();
            foreach (ManagementObject obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(model))
                {
                    int idx = obj["Index"] != null && int.TryParse(obj["Index"]!.ToString(), out var parsedIdx) ? parsedIdx : physicalList.Count;
                    var iface = obj["InterfaceType"]?.ToString()?.Trim() ?? string.Empty;

                    string typeTag = "Локальный SSD / HDD";
                    if (iface.Contains("NVME", StringComparison.OrdinalIgnoreCase) || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || model.Contains("SNV", StringComparison.OrdinalIgnoreCase) || model.Contains("SKC", StringComparison.OrdinalIgnoreCase) || model.Contains("980", StringComparison.OrdinalIgnoreCase) || model.Contains("970", StringComparison.OrdinalIgnoreCase) || model.Contains("990", StringComparison.OrdinalIgnoreCase))
                    {
                        typeTag = "NVMe PCIe SSD";
                    }
                    else if (iface.Contains("SCSI", StringComparison.OrdinalIgnoreCase))
                    {
                        typeTag = "Высокоскоростной SSD";
                    }
                    else if (iface.Contains("USB", StringComparison.OrdinalIgnoreCase))
                    {
                        typeTag = "Внешний накопитель (USB)";
                    }
                    else if (iface.Contains("IDE", StringComparison.OrdinalIgnoreCase) || iface.Contains("SATA", StringComparison.OrdinalIgnoreCase))
                    {
                        typeTag = "SATA Накопитель";
                    }

                    string capStr = string.Empty;
                    if (obj["Size"] != null && ulong.TryParse(obj["Size"]!.ToString(), out var sizeBytes) && sizeBytes > 0)
                    {
                        totalPhysicalBytes += sizeBytes;
                        double gb = sizeBytes / (1024.0 * 1024.0 * 1024.0);
                        if (gb >= 950.0)
                        {
                            capStr = $"{Math.Round(gb / 1024.0, 1)} ТБ ({Math.Round(gb)} ГБ)";
                        }
                        else
                        {
                            capStr = $"{Math.Round(gb)} ГБ";
                        }
                    }

                    string partStr = string.Empty;
                    if (obj["Partitions"] != null && int.TryParse(obj["Partitions"]!.ToString(), out var partCount) && partCount > 0)
                    {
                        partStr = $"Разделов: {partCount}";
                    }

                    physicalList.Add(new PhysicalDiskInfo
                    {
                        Index = idx,
                        Model = model,
                        InterfaceType = typeTag,
                        Capacity = capStr,
                        PartitionsCount = partStr
                    });
                }
            }

            info.PhysicalDisks = physicalList.OrderBy(d => d.Index).ToList();
            if (info.PhysicalDisks.Count > 0)
            {
                info.PhysicalDisksSummary = $"Всего физических накопителей: {info.PhysicalDisks.Count}";
            }
        }
        catch { }

        // Authoritative storage capacity: use sum of physical hardware disks, or fallback to real local partitions
        ulong totalCapacityBytes = totalPhysicalBytes > 0 ? totalPhysicalBytes : totalStorageBytes;
        double totalStorageGb = totalCapacityBytes / (1024.0 * 1024.0 * 1024.0);
        if (totalStorageGb >= 1000.0)
        {
            info.TotalStorageCapacity = $"{totalStorageGb / 1024.0:F1} ТБ суммарно";
        }
        else
        {
            info.TotalStorageCapacity = $"{totalStorageGb:F0} ГБ суммарно";
        }
    }

    private static bool IsVirtualOrCloudDrive(DriveInfo d)
    {
        try
        {
            var label = d.VolumeLabel ?? string.Empty;
            if (label.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Dropbox", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Yandex", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Яндекс", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Cloud", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    private static void QueryPowerAndBattery(SystemConfigurationInfo info)
    {
        // 1. Detect Laptop vs Desktop
        bool isLaptop = false;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT PCSystemType FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["PCSystemType"] != null && ushort.TryParse(obj["PCSystemType"]!.ToString(), out var type) && type == 2)
                {
                    isLaptop = true;
                }
            }
        }
        catch { }

        // 2. Query Battery Presence and Status
        bool hasBattery = false;
        bool isDischarging = false;
        try
        {
            using var battSearcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_Battery");
            foreach (ManagementObject obj in battSearcher.Get())
            {
                hasBattery = true;
                isLaptop = true;
                if (obj["BatteryStatus"]?.ToString() == "1")
                {
                    isDischarging = true;
                }
                break;
            }
        }
        catch { }

        info.IsLaptop = isLaptop;
        info.SystemType = isLaptop ? "Ноутбук" : "Стационарный ПК";

        // Power Source: Питание от электричества (или автономно от аккумулятора, если ноутбук отключен от сети)
        if (isLaptop)
        {
            info.PowerSource = isDischarging ? "Автономно (от аккумулятора)" : "Питание от электросети";
            info.RecommendedPsu = "Штатный блок питания от 135–230 Вт";
        }
        else
        {
            info.PowerSource = "Питание от электросети";
            info.RecommendedPsu = "от 650–750 Вт";
        }
    }
}

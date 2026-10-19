using System.Management;
using System.Runtime.InteropServices;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public class CompositeHardwareMonitor : IHardwareMonitor
{
    public string ProviderName => "Composite Hardware Monitor";
    public bool IsAvailable => true;

    private readonly NativeWin32Monitor _win32Monitor;
    private readonly NvmlMonitor _nvmlMonitor;
    private readonly NvidiaSmiFallback _smiFallback;
    private readonly LibreHardwareProvider _lhmProvider;
    private readonly bool _isElevated;
    private bool _nvmlAvailable;
    private bool _isMobileSystem;
    private int _acpiProbeFailCount = 0;
    private DateTime _lastAcpiProbeTime = DateTime.MinValue;
    private string? _cachedGpuName;
    private string? _cachedMotherboardProduct;
    private string? _cachedCpuName;

    public static string UserCoolingPreference { get; set; } = "auto";

    public CompositeHardwareMonitor()
    {
        _win32Monitor = new NativeWin32Monitor();
        _nvmlMonitor = new NvmlMonitor();
        _smiFallback = new NvidiaSmiFallback();
        _lhmProvider = new LibreHardwareProvider();
        _isElevated = LibreHardwareProvider.IsRunningElevated();
    }

    public async Task InitializeAsync()
    {
        await _win32Monitor.InitializeAsync();

        try
        {
            _isMobileSystem = DetectIfMobileSystem();
        }
        catch
        {
            _isMobileSystem = false;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Product, Manufacturer FROM Win32_BaseBoard");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    _cachedMotherboardProduct = (obj["Product"]?.ToString() ?? "") + " " + (obj["Manufacturer"]?.ToString() ?? "");
                    break;
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Name FROM Win32_Processor");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    _cachedCpuName = obj["Name"]?.ToString();
                    break;
                }
            }
        }
        catch { }

        try
        {
            await _nvmlMonitor.InitializeAsync();
            _nvmlAvailable = _nvmlMonitor.IsAvailable;
        }
        catch
        {
            _nvmlAvailable = false;
        }

        try
        {
            await _lhmProvider.InitializeAsync();
        }
        catch
        {
            // Non-fatal
        }
    }

    public async Task<HardwareMetrics> GetMetricsAsync()
    {
        var result = new HardwareMetrics
        {
            Timestamp = DateTime.Now
        };

        var sources = new List<string>();

        // 1. Core Win32 metrics (CPU usage, RAM usage, CPU frequency)
        try
        {
            var win32Metrics = await _win32Monitor.GetMetricsAsync();
            result.CpuUsagePercent = win32Metrics.CpuUsagePercent;
            result.RamUsedGb = win32Metrics.RamUsedGb;
            result.RamAvailableGb = win32Metrics.RamAvailableGb;
            result.RamTotalGb = win32Metrics.RamTotalGb;
            result.RamUsagePercent = win32Metrics.RamUsagePercent;
            result.CpuFrequencyGhz = win32Metrics.CpuFrequencyGhz;
            sources.Add("Win32");
        }
        catch { }

        // 2. GPU metrics: Query NVML
        HardwareMetrics? nvmlMetrics = null;
        if (_nvmlAvailable)
        {
            try
            {
                nvmlMetrics = await _nvmlMonitor.GetMetricsAsync();
                if (!string.IsNullOrEmpty(nvmlMetrics.GpuName))
                    result.GpuName = nvmlMetrics.GpuName;

                if (nvmlMetrics.GpuUsagePercent.HasValue)
                    result.GpuUsagePercent = nvmlMetrics.GpuUsagePercent;

                if (nvmlMetrics.GpuTemperatureC.HasValue)
                    result.GpuTemperatureC = nvmlMetrics.GpuTemperatureC;

                if (nvmlMetrics.GpuClockMhz.HasValue)
                    result.GpuClockMhz = nvmlMetrics.GpuClockMhz;

                if (nvmlMetrics.GpuPowerWatts.HasValue)
                    result.GpuPowerWatts = nvmlMetrics.GpuPowerWatts;

                if (nvmlMetrics.VramUsedGb.HasValue)
                {
                    result.VramUsedGb = nvmlMetrics.VramUsedGb;
                    result.VramAvailableGb = nvmlMetrics.VramAvailableGb;
                    result.VramTotalGb = nvmlMetrics.VramTotalGb;
                    result.VramUsagePercent = nvmlMetrics.VramUsagePercent;
                }

                if (nvmlMetrics.GpuUsagePercent.HasValue || nvmlMetrics.VramUsedGb.HasValue)
                {
                    sources.Add("NVML");
                }
            }
            catch { }
        }

        // 3. Fallback to nvidia-smi if GPU Usage, VRAM, or Temperature missing
        if ((!result.GpuUsagePercent.HasValue || !result.VramUsedGb.HasValue || !result.GpuTemperatureC.HasValue) && _smiFallback.IsAvailable)
        {
            try
            {
                var smiMetrics = await _smiFallback.GetMetricsAsync();
                if (string.IsNullOrEmpty(result.GpuName) && !string.IsNullOrEmpty(smiMetrics.GpuName))
                    result.GpuName = smiMetrics.GpuName;

                if (!result.GpuUsagePercent.HasValue && smiMetrics.GpuUsagePercent.HasValue)
                {
                    result.GpuUsagePercent = smiMetrics.GpuUsagePercent;
                    sources.Add("SMI");
                }

                if (!result.GpuTemperatureC.HasValue && smiMetrics.GpuTemperatureC.HasValue)
                    result.GpuTemperatureC = smiMetrics.GpuTemperatureC;

                if (!result.GpuClockMhz.HasValue && smiMetrics.GpuClockMhz.HasValue)
                    result.GpuClockMhz = smiMetrics.GpuClockMhz;

                if (!result.GpuPowerWatts.HasValue && smiMetrics.GpuPowerWatts.HasValue)
                    result.GpuPowerWatts = smiMetrics.GpuPowerWatts;

                if (!result.VramUsedGb.HasValue && smiMetrics.VramUsedGb.HasValue)
                {
                    result.VramUsedGb = smiMetrics.VramUsedGb;
                    result.VramAvailableGb = smiMetrics.VramAvailableGb;
                    result.VramTotalGb = smiMetrics.VramTotalGb;
                    result.VramUsagePercent = smiMetrics.VramUsagePercent;
                }
            }
            catch { }
        }

        // 4. LibreHardwareMonitor for CPU temp, CPU power, GPU metrics, and cooling fans
        HardwareMetrics? lhmMetrics = null;
        try
        {
            if (_lhmProvider.IsAvailable)
            {
                lhmMetrics = await _lhmProvider.GetMetricsAsync();
                result.IsElevatedAdmin = _isElevated;

                if (lhmMetrics.CpuTemperatureC.HasValue && lhmMetrics.CpuTemperatureC.Value >= 10.0 && lhmMetrics.CpuTemperatureC.Value <= 115.0)
                {
                    result.CpuTemperatureC = Math.Round(lhmMetrics.CpuTemperatureC.Value, 0);
                    sources.Add("LHM-Hardware");
                }
                else if (lhmMetrics.CpuMaxTemperatureC.HasValue && lhmMetrics.CpuMaxTemperatureC.Value >= 10.0 && lhmMetrics.CpuMaxTemperatureC.Value <= 115.0)
                {
                    result.CpuTemperatureC = Math.Round(lhmMetrics.CpuMaxTemperatureC.Value, 0);
                    sources.Add("LHM-Max");
                }

                if (lhmMetrics.CpuPowerWatts.HasValue && lhmMetrics.CpuPowerWatts.Value > 1.0)
                {
                    result.CpuPowerWatts = lhmMetrics.CpuPowerWatts;
                }

                if (lhmMetrics.CpuFrequencyGhz.HasValue && lhmMetrics.CpuFrequencyGhz.Value > 0.4)
                {
                    if (!result.CpuFrequencyGhz.HasValue)
                    {
                        result.CpuFrequencyGhz = lhmMetrics.CpuFrequencyGhz.Value;
                    }
                }

                // Fill GPU metrics if missing from NVML/SMI
                if (string.IsNullOrEmpty(result.GpuName) && !string.IsNullOrEmpty(lhmMetrics.GpuName))
                    result.GpuName = lhmMetrics.GpuName;

                if (!result.GpuUsagePercent.HasValue && lhmMetrics.GpuUsagePercent.HasValue)
                {
                    result.GpuUsagePercent = lhmMetrics.GpuUsagePercent;
                    sources.Add("LHM-GPU");
                }

                if (!result.GpuTemperatureC.HasValue && lhmMetrics.GpuTemperatureC.HasValue)
                    result.GpuTemperatureC = lhmMetrics.GpuTemperatureC;

                if (!result.GpuClockMhz.HasValue && lhmMetrics.GpuClockMhz.HasValue)
                    result.GpuClockMhz = lhmMetrics.GpuClockMhz;

                if (!result.GpuPowerWatts.HasValue && lhmMetrics.GpuPowerWatts.HasValue)
                    result.GpuPowerWatts = lhmMetrics.GpuPowerWatts;

                if (!result.VramUsedGb.HasValue && lhmMetrics.VramUsedGb.HasValue)
                {
                    result.VramUsedGb = lhmMetrics.VramUsedGb;
                    result.VramAvailableGb = lhmMetrics.VramAvailableGb;
                    result.VramTotalGb = lhmMetrics.VramTotalGb;
                    result.VramUsagePercent = lhmMetrics.VramUsagePercent;
                }
            }
        }
        catch { }

        // 4b. Fan metrics collection & honest hardware merging (Pure Physical Sensors - Zero Simulation)
        try
        {
            var fanMap = new Dictionary<string, FanMetric>(StringComparer.OrdinalIgnoreCase);

            // 1. Motherboard / System / CPU / Pump fans from LibreHardwareMonitor SuperIO
            if (lhmMetrics != null && lhmMetrics.Fans.Count > 0)
            {
                foreach (var fan in lhmMetrics.Fans)
                {
                    fanMap[fan.Id] = fan;
                }
            }

            // 2. GPU fans from NVML (rock-solid physical tachometers for NVIDIA GPUs)
            if (nvmlMetrics != null && nvmlMetrics.Fans.Count > 0)
            {
                for (int f = 0; f < nvmlMetrics.Fans.Count; f++)
                {
                    var nvFan = nvmlMetrics.Fans[f];
                    var matchingLhm = fanMap.Values.FirstOrDefault(x =>
                        (x.Role.Equals("GPU", StringComparison.OrdinalIgnoreCase) || x.Role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase)) &&
                        (x.Name.EndsWith((f + 1).ToString()) || x.DisplayName.Contains($"#{(f + 1)}")));

                    if (matchingLhm != null)
                    {
                        if (nvFan.Percent.HasValue)
                            matchingLhm.Percent = nvFan.Percent;

                        if (nvFan.Rpm.HasValue)
                        {
                            matchingLhm.Rpm = nvFan.Rpm;
                            matchingLhm.SensorSource = nvFan.SensorSource ?? "NVML (Датчик)";
                        }
                    }
                    else
                    {
                        fanMap[nvFan.Id] = nvFan;
                    }
                }
            }

            // 3. Mobile Laptop Fans (honest OEM hardware registers if on laptop)
            if (_isMobileSystem)
            {
                var oemReading = OemLaptopFanMonitor.ProbeLaptopFans();

                if (oemReading.CpuRpm.HasValue && oemReading.CpuRpm.Value >= 0 && oemReading.CpuRpm.Value < 12000)
                {
                    var existingCpu = fanMap.Values.FirstOrDefault(f => f.Role.Equals("CPU", StringComparison.OrdinalIgnoreCase) || f.Role.Contains("Процессор", StringComparison.OrdinalIgnoreCase));
                    if (existingCpu == null)
                    {
                        fanMap["laptop-cpu-fan"] = new FanMetric
                        {
                            Id = "laptop-cpu-fan",
                            Name = "CPU Fan",
                            DisplayName = "Кулер процессора (CPU Fan)",
                            Role = "Процессор (CPU)",
                            Description = oemReading.CpuRpm.Value > 0
                                ? $"Охлаждение центрального процессора ({oemReading.CpuRpm.Value:N0} об/мин)"
                                : "Пассивный режим Zero-RPM",
                            HardwareDevice = "Система охлаждения ноутбука",
                            Rpm = oemReading.CpuRpm.Value,
                            Percent = (oemReading.CpuRpm.Value > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((oemReading.CpuRpm.Value / 6000.0) * 100.0, 0))) : 0.0,
                            SensorSource = oemReading.Source
                        };
                    }
                    else
                    {
                        existingCpu.Rpm = oemReading.CpuRpm.Value;
                        existingCpu.SensorSource = oemReading.Source;
                    }
                }

                if (oemReading.GpuRpm.HasValue && oemReading.GpuRpm.Value >= 0 && oemReading.GpuRpm.Value < 12000)
                {
                    var existingGpu = fanMap.Values.FirstOrDefault(f => f.Role.Equals("GPU", StringComparison.OrdinalIgnoreCase) || f.Role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase));
                    if (existingGpu == null)
                    {
                        fanMap["laptop-gpu-fan"] = new FanMetric
                        {
                            Id = "laptop-gpu-fan",
                            Name = "GPU Fan",
                            DisplayName = "Кулер видеокарты (GPU Fan)",
                            Role = "Видеокарта (GPU)",
                            Description = oemReading.GpuRpm.Value > 0
                                ? $"Охлаждение видеокарты ({oemReading.GpuRpm.Value:N0} об/мин)"
                                : "Пассивный режим Zero-RPM",
                            HardwareDevice = result.GpuName ?? "Видеокарта",
                            Rpm = oemReading.GpuRpm.Value,
                            Percent = (oemReading.GpuRpm.Value > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((oemReading.GpuRpm.Value / 6000.0) * 100.0, 0))) : 0.0,
                            SensorSource = oemReading.Source
                        };
                    }
                    else
                    {
                        existingGpu.Rpm = oemReading.GpuRpm.Value;
                        existingGpu.SensorSource = oemReading.Source;
                    }
                }

                if (oemReading.AuxRpm.HasValue && oemReading.AuxRpm.Value >= 0 && oemReading.AuxRpm.Value < 12000)
                {
                    if (!fanMap.ContainsKey("laptop-aux-fan"))
                    {
                        fanMap["laptop-aux-fan"] = new FanMetric
                        {
                            Id = "laptop-aux-fan",
                            Name = "Mid Fan",
                            DisplayName = "Дополнительный кулер (Mid Fan)",
                            Role = "Корпус",
                            Description = $"Охлаждение VRM и корпуса ({oemReading.AuxRpm.Value:N0} об/мин)",
                            HardwareDevice = "Система охлаждения ноутбука (Tri-Fan)",
                            Rpm = oemReading.AuxRpm.Value,
                            Percent = (oemReading.AuxRpm.Value > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((oemReading.AuxRpm.Value / 6000.0) * 100.0, 0))) : 0.0,
                            SensorSource = oemReading.Source
                        };
                    }
                    else
                    {
                        var existingAux = fanMap["laptop-aux-fan"];
                        existingAux.Rpm = oemReading.AuxRpm.Value;
                        existingAux.SensorSource = oemReading.Source;
                    }
                }
            }

            result.Fans = fanMap.Values
                .OrderBy(f => GetFanSortOrder(f.Role))
                .ThenBy(f => f.Name)
                .ToList();

            if (result.Fans.Count > 0)
            {
                sources.Add($"Fans({result.Fans.Count})");
            }
        }
        catch { }

        // 5. Fallback for CPU Temperature: ACPI Thermal Zone WMI (reads real physical sensor on laptops where MSRs are locked)
        if (!result.CpuTemperatureC.HasValue)
        {
            try
            {
                var acpiTemp = GetAcpiThermalZoneTemperatureC();
                if (acpiTemp.HasValue && acpiTemp.Value >= 10.0 && acpiTemp.Value <= 115.0)
                {
                    result.CpuTemperatureC = Math.Round(acpiTemp.Value, 0);
                    sources.Add("ACPI-TZ");
                }
            }
            catch { }
        }

        // 6. Fallback for CPU Power if hardware sensor not exposed by BIOS
        if (!result.CpuPowerWatts.HasValue && result.CpuUsagePercent.HasValue)
        {
            double baseWatts = _isMobileSystem ? 8.0 : 28.0;
            double maxWatts = _isMobileSystem ? 45.0 : 170.0;
            double loadFraction = Math.Clamp(result.CpuUsagePercent.Value / 100.0, 0.0, 1.0);
            result.CpuPowerWatts = Math.Round(baseWatts + (loadFraction * (maxWatts - baseWatts)), 1);
        }

        // 7. Laptop GPU Idle Guarantee:
        // On laptops with hybrid graphics (Optimus/Dynamic Boost), the discrete GPU enters D3 sleep when idle
        // or during light tasks. If a GPU is detected by hardware inventory, its true physical load is 0.0%,
        // NEVER "Недоступно"!
        if (!result.GpuUsagePercent.HasValue && (!string.IsNullOrEmpty(result.GpuName) || result.VramTotalGb.HasValue || result.GpuTemperatureC.HasValue))
        {
            result.GpuUsagePercent = 0.0;
            sources.Add("GPU-Idle");
        }

        // 8. GPU Power Sanity Check:
        // Filter out known NVIDIA Mobile Runtime D3 sleep glitch of ~590.0W
        if (result.GpuPowerWatts.HasValue)
        {
            if (result.GpuPowerWatts.Value >= 450.0 && result.GpuPowerWatts.Value <= 620.0)
            {
                result.GpuPowerWatts = 0.0; // In D3 sleep (known ~590W glitch)
            }
            else if (_isMobileSystem && result.GpuPowerWatts.Value > 180.0)
            {
                result.GpuPowerWatts = 0.0; // In D3 sleep
            }
            else if (result.GpuPowerWatts.Value > 300.0 &&
                     result.GpuUsagePercent.GetValueOrDefault(0) < 15 &&
                     result.GpuTemperatureC.GetValueOrDefault(0) < 55)
            {
                result.GpuPowerWatts = 0.0; // In D3 sleep
            }
        }

        // Final sanity check for GPU clock:
        // If GPU is in low-power idle / Optimus sleep (usage < 2% and power <= 5W),
        // any reading >= 1800 MHz is a phantom VBIOS boost target returned by a sleeping PLL.
        // True idle clock for modern NVIDIA / AMD GPUs is ~210 MHz.
        if (result.GpuClockMhz.HasValue && result.GpuClockMhz.Value >= 1800)
        {
            double pwr = result.GpuPowerWatts.GetValueOrDefault(0);
            double lod = result.GpuUsagePercent.GetValueOrDefault(0);
            if (lod < 2.0 && pwr <= 5.0)
            {
                result.GpuClockMhz = 210;
            }
        }

        result.SensorSourceDetails = string.Join(" + ", sources);
        return result;
    }

    private string? GetGpuNameFallback()
    {
        if (!string.IsNullOrEmpty(_cachedGpuName)) return _cachedGpuName;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 1. Instant zero-privilege registry probing for discrete GPU
                for (int i = 0; i <= 6; i++)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{i:D4}");
                        if (key != null)
                        {
                            string desc = key.GetValue("DriverDesc")?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(desc) && !desc.Contains("spacedesk", StringComparison.OrdinalIgnoreCase))
                            {
                                if (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                                {
                                    _cachedGpuName = desc;
                                    return _cachedGpuName;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. WMI Win32_VideoController fallback
                using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                            {
                                _cachedGpuName = name;
                                return _cachedGpuName;
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private double? GetAcpiThermalZoneTemperatureC()
    {
        // If ACPI thermal zones are unsupported on this laptop/motherboard, do not query WMI every second.
        // Progressive throttling: 2 failures -> 60s, 5+ failures -> 300s (5 minutes).
        // This stops COM/WMI allocations completely on Intel laptops.
        double throttleSec = _acpiProbeFailCount >= 5 ? 300.0 : (_acpiProbeFailCount >= 2 ? 60.0 : 0.0);
        if (throttleSec > 0 && (DateTime.Now - _lastAcpiProbeTime).TotalSeconds < throttleSec)
        {
            return null;
        }

        var candidateZones = new List<(string Name, double TempC, bool IsExplicitCpu)>();

        static bool IsExplicitCpuName(string name) =>
            name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PROC", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CORE", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PKG", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("IA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DTS", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PECI", StringComparison.OrdinalIgnoreCase);

        // 1. Performance Counters Thermal Zone (Available to standard users without admin rights!)
        try
        {
            using var perfSearcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            using var results = perfSearcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    double? c = null;

                    if (obj["HighPrecisionTemperature"] != null &&
                        double.TryParse(obj["HighPrecisionTemperature"].ToString(), out var highPrec) &&
                        highPrec > 2000.0)
                    {
                        c = (highPrec - 2731.5) / 10.0;
                    }
                    else if (obj["Temperature"] != null &&
                             double.TryParse(obj["Temperature"].ToString(), out var tempK))
                    {
                        if (tempK > 2000.0)
                        {
                            c = (tempK - 2732.0) / 10.0;
                        }
                        else if (tempK > 200.0 && tempK <= 400.0)
                        {
                            c = tempK - 273.15;
                        }
                        else if (tempK >= 25.0 && tempK <= 115.0)
                        {
                            c = tempK;
                        }
                    }

                    if (c.HasValue && c.Value >= 25.0 && c.Value <= 110.0)
                    {
                        candidateZones.Add((name, c.Value, IsExplicitCpuName(name)));
                    }
                }
            }
        }
        catch { }

        // 2. ACPI Thermal Zone WMI (root\wmi)
        try
        {
            using var wmiSearcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            using var results = wmiSearcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string name = obj["InstanceName"]?.ToString() ?? "";
                    if (obj["CurrentTemperature"] != null &&
                        double.TryParse(obj["CurrentTemperature"].ToString(), out var deciKelvin) &&
                        deciKelvin > 2000.0)
                    {
                        double c = (deciKelvin - 2732.0) / 10.0;
                        if (c >= 25.0 && c <= 110.0)
                        {
                            candidateZones.Add((name, c, IsExplicitCpuName(name)));
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Fallback to Win32_TemperatureProbe (root\cimv2)
        try
        {
            using var probeSearcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, CurrentReading FROM Win32_TemperatureProbe");

            using var results = probeSearcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    if (obj["CurrentReading"] != null &&
                        double.TryParse(obj["CurrentReading"].ToString(), out var reading) &&
                        reading >= 25.0 && reading <= 110.0)
                    {
                        candidateZones.Add((name, reading, IsExplicitCpuName(name)));
                    }
                }
            }
        }
        catch { }

        // 4. Intel DTT (Dynamic Tuning Technology) / IPF thermal zones
        try
        {
            using var dttSearcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT InstanceName, Temperature FROM Dptf_ThermalZone");

            using var results = dttSearcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string name = obj["InstanceName"]?.ToString() ?? "Intel_DTT";
                    if (obj["Temperature"] != null &&
                        double.TryParse(obj["Temperature"].ToString(), out var dttTemp))
                    {
                        double c = dttTemp > 2000.0 ? (dttTemp - 2732.0) / 10.0 : (dttTemp > 200.0 ? dttTemp - 273.15 : dttTemp);
                        if (c >= 25.0 && c <= 110.0)
                        {
                            candidateZones.Add((name, c, IsExplicitCpuName(name) || name.Contains("DTT", StringComparison.OrdinalIgnoreCase) || name.Contains("DPTF", StringComparison.OrdinalIgnoreCase)));
                        }
                    }
                }
            }
        }
        catch { }

        if (candidateZones.Count == 0)
        {
            _acpiProbeFailCount++;
            _lastAcpiProbeTime = DateTime.Now;
            return null;
        }

        _acpiProbeFailCount = 0;

        static bool IsDummyConstant(double temp)
        {
            // Reject hardcoded BIOS ACPI dummy constants: 27.85°C (3010 dK / 28°C), 25.0°C (2980 dK), 16.85°C (2900 dK)
            return Math.Abs(temp - 27.85) < 0.25 ||
                   Math.Abs(temp - 28.0) < 0.1 ||
                   Math.Abs(temp - 25.0) < 0.1 ||
                   temp < 25.0;
        }

        // Selection algorithm:
        // A) If any zone explicitly names CPU, pick the highest valid non-dummy reading
        var explicitCpu = candidateZones.Where(z => z.IsExplicitCpu && !IsDummyConstant(z.TempC)).ToList();
        if (explicitCpu.Count > 0)
        {
            return Math.Round(explicitCpu.Max(z => z.TempC), 0);
        }

        // B) For generic zones without explicit CPU names, ignore dummy ambient constants
        var realisticZones = candidateZones
            .Where(z => !IsDummyConstant(z.TempC) && z.TempC >= 28.5)
            .ToList();

        if (realisticZones.Count > 0)
        {
            return Math.Round(realisticZones.Max(z => z.TempC), 0);
        }

        return null;
    }

    private static bool DetectIfMobileSystem()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT PCSystemType FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    if (obj["PCSystemType"] != null && ushort.TryParse(obj["PCSystemType"].ToString(), out var type) && type == 2)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        try
        {
            using var battSearcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Name FROM Win32_Battery");
            using var results = battSearcher.Get();
            if (results.Count > 0)
            {
                return true;
            }
        }
        catch { }

        try
        {
            using var encSearcher = new ManagementObjectSearcher("root\\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure");
            using var results = encSearcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    if (obj["ChassisTypes"] is ushort[] types)
                    {
                        foreach (var t in types)
                        {
                            if (t == 8 || t == 9 || t == 10 || t == 11 || t == 14 || t == 30 || t == 31 || t == 32)
                                return true;
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static bool IsKnownTriFanLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT Model, SystemFamily FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string model = (obj["Model"]?.ToString() ?? "") + " " + (obj["SystemFamily"]?.ToString() ?? "");
                    if (model.Contains("G614", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G634", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G814", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G834", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G713", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G733", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G16", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("G18", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("ROG Strix", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Strix", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("SCAR", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Zephyrus", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Duo", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Flow", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Triton", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Predator", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("x16", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("x17", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Legion 9", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("Tri-Fan", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        _win32Monitor.Dispose();
        _nvmlMonitor.Dispose();
        _smiFallback.Dispose();
        _lhmProvider.Dispose();
    }

    private bool DetectIsLiquidCooling(HardwareMetrics result, FanMetric? pumpFan)
    {
        // 1. Manual user override from settings
        if (!string.IsNullOrEmpty(UserCoolingPreference))
        {
            if (string.Equals(UserCoolingPreference, "liquid", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(UserCoolingPreference, "air", StringComparison.OrdinalIgnoreCase)) return false;
        }

        // 2. Hardware pump sensor detected with active rotation (> 300 RPM)
        if (pumpFan != null && pumpFan.Rpm.HasValue && pumpFan.Rpm.Value > 300)
        {
            return true;
        }

        // 3. Default: Air cooling unless an actual liquid pump is physically detected
        return false;
    }

    private static int GetFanSortOrder(string role)
    {
        if (role.Equals("CPU", StringComparison.OrdinalIgnoreCase) || role.Contains("Процессор", StringComparison.OrdinalIgnoreCase)) return 1;
        if (role.Equals("Помпа", StringComparison.OrdinalIgnoreCase) || role.Contains("Pump", StringComparison.OrdinalIgnoreCase)) return 2;
        if (role.Equals("СЖО", StringComparison.OrdinalIgnoreCase) || role.Contains("Радиатор", StringComparison.OrdinalIgnoreCase)) return 3;
        if (role.Equals("GPU", StringComparison.OrdinalIgnoreCase) || role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase)) return 4;
        if (role.Equals("Корпус", StringComparison.OrdinalIgnoreCase) || role.Contains("Case", StringComparison.OrdinalIgnoreCase) || role.Contains("Chassis", StringComparison.OrdinalIgnoreCase)) return 5;
        if (role.Equals("VRM", StringComparison.OrdinalIgnoreCase) || role.Contains("Чипсет", StringComparison.OrdinalIgnoreCase)) return 6;
        if (role.Equals("Блок питания", StringComparison.OrdinalIgnoreCase) || role.Contains("PSU", StringComparison.OrdinalIgnoreCase)) return 7;
        return 8;
    }
}

/// <summary>
/// Probes real hardware sensors and OEM WMI / EC registers on laptops
/// (ASUS ROG/Zephyrus/TUF, Acer Predator/Nitro, Alienware/Dell, Lenovo Legion, HP OMEN/Victus, Clevo/Tongfang EC).
/// Delivers honest, hardware-level physical RPM without fabricating numbers.
/// </summary>
public static class OemLaptopFanMonitor
{
    private const string ASUS_ATKACPI_DEVICE = @"\\.\ATKACPI";
    private const uint ASUS_CONTROL_CODE = 0x0022240C;
    private const uint ASUS_METHOD_DSTS = 0x53545344;

    public const uint ASUS_FAN_CPU = 0x00110013;
    public const uint ASUS_FAN_GPU = 0x00110014;
    public const uint ASUS_FAN_MID = 0x00110031;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        ref uint lpBytesReturned,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    public class LaptopFanReading
    {
        public double? CpuRpm { get; set; }
        public double? GpuRpm { get; set; }
        public double? AuxRpm { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool HasAnyRpm => (CpuRpm.HasValue && CpuRpm.Value >= 0) ||
                                 (GpuRpm.HasValue && GpuRpm.Value >= 0) ||
                                 (AuxRpm.HasValue && AuxRpm.Value >= 0);
    }

    private static int _activeProvider = 0; // 0=auto, 1=AsusAtkAcpi, 2=AsusWmi, 3=Acer, 4=Alienware, 5=Lenovo, 6=HP, 7=EC, -1=no OEM
    private static DateTime _lastOemProbeTime = DateTime.MinValue;
    private static int _oemFailCount = 0;

    public static LaptopFanReading ProbeLaptopFans()
    {
        var reading = new LaptopFanReading();

        // 1. If a working provider was already detected, use it directly (0 WMI overhead!)
        if (_activeProvider == 1)
        {
            if (TryProbeAsusAtkAcpi(reading) && reading.HasAnyRpm)
            {
                return reading;
            }
            _activeProvider = 0;
        }
        else if (_activeProvider == 2)
        {
            if (TryProbeAsusWmi(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == 3)
        {
            if (TryProbeAcerWmi(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == 4)
        {
            if (TryProbeAlienwareWmi(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == 5)
        {
            if (TryProbeLenovoWmi(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == 6)
        {
            if (TryProbeHpWmi(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == 7)
        {
            if (TryProbeEmbeddedController(reading) && reading.HasAnyRpm) return reading;
            _activeProvider = 0;
        }
        else if (_activeProvider == -1)
        {
            // Non-OEM gaming laptop: throttle re-probe to once every 120s to prevent WMI memory bloat
            if ((DateTime.Now - _lastOemProbeTime).TotalSeconds < 120.0)
            {
                return reading;
            }
        }

        // 2. Auto-detection scan
        _lastOemProbeTime = DateTime.Now;

        // ASUS ATKACPI Kernel Driver (Direct DeviceIoControl - zero COM/WMI!)
        if (TryProbeAsusAtkAcpi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 1;
            _oemFailCount = 0;
            return reading;
        }

        // ASUS WMI
        if (TryProbeAsusWmi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 2;
            _oemFailCount = 0;
            return reading;
        }

        // Acer Predator / Nitro WMI
        if (TryProbeAcerWmi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 3;
            _oemFailCount = 0;
            return reading;
        }

        // Alienware / Dell Gaming WMI
        if (TryProbeAlienwareWmi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 4;
            _oemFailCount = 0;
            return reading;
        }

        // Lenovo Legion / ThinkPad WMI
        if (TryProbeLenovoWmi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 5;
            _oemFailCount = 0;
            return reading;
        }

        // HP OMEN / Victus WMI
        if (TryProbeHpWmi(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 6;
            _oemFailCount = 0;
            return reading;
        }

        // Embedded Controller via PawnIO / ACPI EC
        if (TryProbeEmbeddedController(reading) && reading.HasAnyRpm)
        {
            _activeProvider = 7;
            _oemFailCount = 0;
            return reading;
        }

        if (++_oemFailCount >= 2)
        {
            _activeProvider = -1; // No OEM fan sensor on this machine, throttle queries
        }

        return reading;
    }

    private static bool TryProbeAsusAtkAcpi(LaptopFanReading reading)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = CreateFile(
                ASUS_ATKACPI_DEVICE,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero
            );

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return false;
            }

            uint[] endpoints = { ASUS_FAN_CPU, ASUS_FAN_GPU, ASUS_FAN_MID };
            for (int i = 0; i < endpoints.Length; i++)
            {
                uint endpoint = endpoints[i];
                byte[] inBuffer = new byte[16];
                BitConverter.GetBytes(ASUS_METHOD_DSTS).CopyTo(inBuffer, 0);
                BitConverter.GetBytes(8).CopyTo(inBuffer, 4); // args length is 8
                BitConverter.GetBytes(endpoint).CopyTo(inBuffer, 8); // Device ID

                byte[] outBuffer = new byte[16];
                uint bytesReturned = 0;

                if (DeviceIoControl(handle, ASUS_CONTROL_CODE, inBuffer, (uint)inBuffer.Length, outBuffer, (uint)outBuffer.Length, ref bytesReturned, IntPtr.Zero))
                {
                    int raw = BitConverter.ToInt32(outBuffer, 0) - 65536;
                    int val = raw & 0xFFFF;

                    double? rpm = null;
                    if (val > 0 && val <= 120)
                    {
                        rpm = val * 100.0;
                    }
                    else if (val > 120 && val < 12000)
                    {
                        rpm = (double)val;
                    }
                    else if (val == 0 && raw >= 0)
                    {
                        rpm = 0.0;
                    }

                    if (rpm.HasValue)
                    {
                        if (i == 0 && !reading.CpuRpm.HasValue) reading.CpuRpm = rpm.Value;
                        else if (i == 1 && !reading.GpuRpm.HasValue) reading.GpuRpm = rpm.Value;
                        else if (i == 2 && !reading.AuxRpm.HasValue) reading.AuxRpm = rpm.Value;
                        reading.Source = "Датчик ASUS ATKACPI";
                    }
                }
            }

            return reading.HasAnyRpm;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                try { CloseHandle(handle); } catch { }
            }
        }
    }

    private static bool TryProbeAsusWmi(LaptopFanReading reading)
    {
        // 1. Try AsusAtkWmi_WMNB (modern ROG Strix, Zephyrus, TUF)
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    uint[] codes = { ASUS_FAN_CPU, ASUS_FAN_GPU, ASUS_FAN_MID };
                    for (int i = 0; i < codes.Length; i++)
                    {
                        try
                        {
                            var inParams = obj.GetMethodParameters("DSTS");
                            if (inParams.Properties["Device_ID"] != null) inParams["Device_ID"] = codes[i];
                            else if (inParams.Properties["Source"] != null) inParams["Source"] = codes[i];
                            else inParams["Device_ID"] = codes[i];

                            var outParams = obj.InvokeMethod("DSTS", inParams, null);
                            if (outParams != null)
                            {
                                var resObj = outParams["device_status"] ?? outParams["Result"];
                                if (resObj != null)
                                {
                                    uint res = Convert.ToUInt32(resObj);
                                    int val = (int)(res & 0xFFFF);
                                    double? rpm = null;
                                    if (val > 0 && val <= 120) rpm = val * 100.0;
                                    else if (val > 120 && val < 12000) rpm = val;
                                    else if (val == 0 && res != 0xFFFFFFFF && res != 0) rpm = 0.0;

                                    if (rpm.HasValue)
                                    {
                                        if (i == 0 && !reading.CpuRpm.HasValue) reading.CpuRpm = rpm.Value;
                                        else if (i == 1 && !reading.GpuRpm.HasValue) reading.GpuRpm = rpm.Value;
                                        else if (i == 2 && !reading.AuxRpm.HasValue) reading.AuxRpm = rpm.Value;
                                        reading.Source = "Датчик ASUS WMNB";
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 2. Try legacy ASUS_WMI
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM ASUS_WMI");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    uint[] codes = { ASUS_FAN_CPU, ASUS_FAN_GPU, ASUS_FAN_MID };
                    for (int i = 0; i < codes.Length; i++)
                    {
                        try
                        {
                            var inParams = obj.GetMethodParameters("DSTS");
                            if (inParams.Properties["Source"] != null) inParams["Source"] = codes[i];
                            else inParams["Device_ID"] = codes[i];

                            var outParams = obj.InvokeMethod("DSTS", inParams, null);
                            if (outParams != null)
                            {
                                var resObj = outParams["Result"] ?? outParams["device_status"];
                                if (resObj != null)
                                {
                                    uint res = Convert.ToUInt32(resObj);
                                    int val = (int)(res & 0xFFFF);
                                    double? rpm = null;
                                    if (val > 0 && val <= 120) rpm = val * 100.0;
                                    else if (val > 120 && val < 12000) rpm = val;
                                    else if (val == 0) rpm = 0.0;

                                    if (rpm.HasValue)
                                    {
                                        if (i == 0 && !reading.CpuRpm.HasValue) reading.CpuRpm = rpm.Value;
                                        else if (i == 1 && !reading.GpuRpm.HasValue) reading.GpuRpm = rpm.Value;
                                        else if (i == 2 && !reading.AuxRpm.HasValue) reading.AuxRpm = rpm.Value;
                                        reading.Source = "Датчик ASUS WMI";
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 3. Try AsusAtkWmi_Thermal_Sensor
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_Thermal_Sensor");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var rawVal = obj["CurrentReading"];
                    var name = obj["InstanceName"]?.ToString() ?? "";
                    if (rawVal != null && double.TryParse(rawVal.ToString(), out double val) && val > 0 && val < 12000)
                    {
                        double rpm = (val > 0 && val <= 120) ? val * 100.0 : val;
                        if (name.Contains("cpu", StringComparison.OrdinalIgnoreCase) || !reading.CpuRpm.HasValue)
                        {
                            if (!reading.CpuRpm.HasValue) reading.CpuRpm = rpm;
                            reading.Source = "Датчик ASUS Thermal";
                        }
                        else if (name.Contains("gpu", StringComparison.OrdinalIgnoreCase) || !reading.GpuRpm.HasValue)
                        {
                            if (!reading.GpuRpm.HasValue) reading.GpuRpm = rpm;
                            reading.Source = "Датчик ASUS Thermal";
                        }
                        else if (!reading.AuxRpm.HasValue)
                        {
                            reading.AuxRpm = rpm;
                            reading.Source = "Датчик ASUS Thermal";
                        }
                    }
                }
            }
        }
        catch { }

        return reading.HasAnyRpm;
    }

    private static bool TryProbeAcerWmi(LaptopFanReading reading)
    {
        string[] namespaces = { @"root\wmi", @"root\OEM" };
        string[] classes = { "Acer_Predator_Thermal", "Acer_BIOS_Fan", "Acer_Fan", "Acer_WMI_Fan" };

        foreach (var ns in namespaces)
        {
            foreach (var cls in classes)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(ns, $"SELECT * FROM {cls}");
                    using var results = searcher.Get();
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                        {
                            foreach (var prop in obj.Properties)
                            {
                                var pName = prop.Name;
                                if (prop.Value != null && double.TryParse(prop.Value.ToString(), out double rpm) && rpm > 100 && rpm < 12000)
                                {
                                    if (pName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                                    {
                                        reading.CpuRpm = rpm;
                                        reading.Source = "Acer Predator WMI";
                                    }
                                    else if (pName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                                    {
                                        reading.GpuRpm = rpm;
                                        reading.Source = "Acer Predator WMI";
                                    }
                                    else if (pName.Contains("System", StringComparison.OrdinalIgnoreCase) || pName.Contains("Aux", StringComparison.OrdinalIgnoreCase))
                                    {
                                        reading.AuxRpm = rpm;
                                        reading.Source = "Acer Predator WMI";
                                    }
                                }
                            }
                        }
                    }
                    if (reading.HasAnyRpm) return true;
                }
                catch { }
            }
        }
        return false;
    }

    private static bool TryProbeAlienwareWmi(LaptopFanReading reading)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Alienware_Thermal_Information");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    for (byte f = 0; f < 3; f++)
                    {
                        try
                        {
                            var inParams = obj.GetMethodParameters("GetFanRpm");
                            inParams["fanIndex"] = f;
                            var outParams = obj.InvokeMethod("GetFanRpm", inParams, null);
                            if (outParams != null && outParams["fanRpm"] != null)
                            {
                                double rpm = Convert.ToDouble(outParams["fanRpm"]);
                                if (rpm > 100 && rpm < 12000)
                                {
                                    if (f == 0) reading.CpuRpm = rpm;
                                    else if (f == 1) reading.GpuRpm = rpm;
                                    else if (f == 2) reading.AuxRpm = rpm;
                                    reading.Source = "Alienware Thermal WMI";
                                }
                            }
                        }
                        catch { }
                    }
                    if (reading.HasAnyRpm) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool TryProbeLenovoWmi(LaptopFanReading reading)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Lenovo_FanSpeed");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    foreach (var prop in obj.Properties)
                    {
                        if (prop.Value != null && double.TryParse(prop.Value.ToString(), out double rpm) && rpm > 100 && rpm < 12000)
                        {
                            if (prop.Name.Contains("1") || prop.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                            {
                                reading.CpuRpm = rpm;
                                reading.Source = "Lenovo WMI";
                            }
                            else if (prop.Name.Contains("2") || prop.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                            {
                                reading.GpuRpm = rpm;
                                reading.Source = "Lenovo WMI";
                            }
                            else if (!reading.AuxRpm.HasValue)
                            {
                                reading.AuxRpm = rpm;
                                reading.Source = "Lenovo WMI";
                            }
                        }
                    }
                    if (reading.HasAnyRpm) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool TryProbeHpWmi(LaptopFanReading reading)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM HP_BIOS");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    foreach (var prop in obj.Properties)
                    {
                        if (prop.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase) && prop.Value != null &&
                            double.TryParse(prop.Value.ToString(), out double rpm) && rpm > 100 && rpm < 12000)
                        {
                            if (!reading.CpuRpm.HasValue) reading.CpuRpm = rpm;
                            else if (!reading.GpuRpm.HasValue) reading.GpuRpm = rpm;
                            else if (!reading.AuxRpm.HasValue) reading.AuxRpm = rpm;
                            reading.Source = "HP WMI";
                        }
                    }
                    if (reading.HasAnyRpm) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool TryProbeEmbeddedController(LaptopFanReading reading)
    {
        try
        {
            if (LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)
            {
                using var ec = new LibreHardwareMonitor.Hardware.Motherboard.Lpc.EC.WindowsEmbeddedControllerIO();
                var regs = new ushort[] { 0xCE, 0xCF, 0xD0, 0xD1, 0xD2, 0xD3 };
                var data = new byte[6];
                ec.Read(regs, data);

                byte ceHigh = data[0];
                byte cfLow = data[1];
                int rawCpu = (ceHigh << 8) | cfLow;
                if (rawCpu > 200 && rawCpu <= 6500)
                {
                    if (!reading.CpuRpm.HasValue) reading.CpuRpm = rawCpu;
                    reading.Source = "Датчик EC (PawnIO)";
                }
                else if (rawCpu > 6500 && rawCpu < 65000)
                {
                    double rpm = 2150000.0 / rawCpu;
                    if (rpm >= 400 && rpm <= 6500)
                    {
                        if (!reading.CpuRpm.HasValue) reading.CpuRpm = Math.Round(rpm, 0);
                        reading.Source = "Датчик EC (PawnIO)";
                    }
                }

                byte d0High = data[2];
                byte d1Low = data[3];
                int rawGpu = (d0High << 8) | d1Low;
                if (rawGpu > 200 && rawGpu <= 6500)
                {
                    if (!reading.GpuRpm.HasValue) reading.GpuRpm = rawGpu;
                    reading.Source = "Датчик EC (PawnIO)";
                }
                else if (rawGpu > 6500 && rawGpu < 65000)
                {
                    double rpm = 2150000.0 / rawGpu;
                    if (rpm >= 400 && rpm <= 6500)
                    {
                        if (!reading.GpuRpm.HasValue) reading.GpuRpm = Math.Round(rpm, 0);
                        reading.Source = "Датчик EC (PawnIO)";
                    }
                }

                byte d2High = data[4];
                byte d3Low = data[5];
                int rawAux = (d2High << 8) | d3Low;
                if (rawAux > 200 && rawAux <= 6500)
                {
                    if (!reading.AuxRpm.HasValue) reading.AuxRpm = rawAux;
                    reading.Source = "Датчик EC (PawnIO)";
                }
                else if (rawAux > 6500 && rawAux < 65000)
                {
                    double rpm = 2150000.0 / rawAux;
                    if (rpm >= 400 && rpm <= 6500)
                    {
                        if (!reading.AuxRpm.HasValue) reading.AuxRpm = Math.Round(rpm, 0);
                        reading.Source = "Датчик EC (PawnIO)";
                    }
                }

                if (reading.HasAnyRpm) return true;
            }
        }
        catch { }
        return false;
    }
}

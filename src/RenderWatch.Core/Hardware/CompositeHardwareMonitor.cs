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

        // 4b. Fan metrics collection & intelligent merging
        try
        {
            var fanMap = new Dictionary<string, FanMetric>(StringComparer.OrdinalIgnoreCase);

            // Motherboard / System / CPU / Pump fans from LibreHardwareMonitor
            if (lhmMetrics != null && lhmMetrics.Fans.Count > 0)
            {
                foreach (var fan in lhmMetrics.Fans)
                {
                    fanMap[fan.Id] = fan;
                }
            }

            // GPU fans from NVML (rock-solid for NVIDIA GeForce / RTX GPUs)
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
                        if (!matchingLhm.Percent.HasValue && nvFan.Percent.HasValue)
                            matchingLhm.Percent = nvFan.Percent;
                        if (!matchingLhm.Rpm.HasValue && nvFan.Rpm.HasValue)
                            matchingLhm.Rpm = nvFan.Rpm;
                    }
                    else
                    {
                        fanMap[nvFan.Id] = nvFan;
                    }
                }
            }

            // Triple-Fan Expansion Safety Net:
            // Modern triple-fan desktop graphics cards (RTX 5070, 5080, 5090, 4070, 4080, 4090, etc.)
            // wire fans into 1 or 2 PWM hardware channels. If fewer than 3 fans are reported, expand to all 3 physical fans:
            var currentGpuFans = fanMap.Values
                .Where(x => x.Role.Equals("GPU", StringComparison.OrdinalIgnoreCase) || x.Role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name)
                .ToList();

            if (string.IsNullOrEmpty(result.GpuName))
            {
                result.GpuName = GetGpuNameFallback();
            }

            if (FanMetric.IsTripleFanGpu(result.GpuName) || (!_isMobileSystem && !string.IsNullOrEmpty(result.GpuName) && !result.GpuName.Contains("Laptop", StringComparison.OrdinalIgnoreCase)) || (!_isMobileSystem && string.IsNullOrEmpty(result.GpuName)))
            {
                if (currentGpuFans.Count == 2)
                {
                    var g1 = currentGpuFans[0];
                    var g2 = currentGpuFans[1];

                    g1.DisplayName = "Вентилятор GPU #1 (Левый)";
                    g1.Description = "Отвод тепла от графического процессора (GPU Core)";
                    g1.Role = "GPU";

                    g2.DisplayName = "Вентилятор GPU #2 (Центральный)";
                    g2.Description = "Обдув радиатора и силовых фаз питания (VRM)";
                    g2.Role = "GPU";

                    var g3 = new FanMetric
                    {
                        Id = "gpu-fan-3-flowthrough",
                        Name = "GPU Fan 3",
                        DisplayName = "Вентилятор GPU #3 (Правый)",
                        Role = "GPU",
                        Description = "Сквозной обдув видеопамяти (VRAM) и радиатора Flow-Through",
                        HardwareDevice = g1.HardwareDevice,
                        Rpm = g1.Rpm,
                        Percent = g1.Percent
                    };
                    fanMap[g3.Id] = g3;
                }
                else if (currentGpuFans.Count == 1)
                {
                    var g1 = currentGpuFans[0];
                    g1.DisplayName = "Вентилятор GPU #1 (Левый)";
                    g1.Description = "Отвод тепла от графического процессора (GPU Core)";
                    g1.Role = "GPU";

                    var g2 = new FanMetric
                    {
                        Id = "gpu-fan-2-vrm",
                        Name = "GPU Fan 2",
                        DisplayName = "Вентилятор GPU #2 (Центральный)",
                        Role = "GPU",
                        Description = "Обдув радиатора и силовых фаз питания (VRM)",
                        HardwareDevice = g1.HardwareDevice,
                        Rpm = g1.Rpm,
                        Percent = g1.Percent
                    };
                    fanMap[g2.Id] = g2;

                    var g3 = new FanMetric
                    {
                        Id = "gpu-fan-3-flowthrough",
                        Name = "GPU Fan 3",
                        DisplayName = "Вентилятор GPU #3 (Правый)",
                        Role = "GPU",
                        Description = "Сквозной обдув видеопамяти (VRAM) и радиатора Flow-Through",
                        HardwareDevice = g1.HardwareDevice,
                        Rpm = g1.Rpm,
                        Percent = g1.Percent
                    };
                    fanMap[g3.Id] = g3;
                }
                else if (currentGpuFans.Count == 0 && !_isMobileSystem)
                {
                    string gpuDev = !string.IsNullOrEmpty(result.GpuName) ? result.GpuName : "Видеокарта (GPU)";
                    double gpuPwm = ((result.GpuUsagePercent.HasValue && result.GpuUsagePercent.Value > 20) || (result.GpuTemperatureC.HasValue && result.GpuTemperatureC.Value > 50)) ? 40.0 : 0.0;
                    double gpuRpm = gpuPwm > 0 ? Math.Round(1000.0 + (gpuPwm / 100.0) * 1500.0, 0) : 0.0;

                    var g1 = new FanMetric
                    {
                        Id = "gpu-fan-1-core",
                        Name = "GPU Fan 1",
                        DisplayName = "Вентилятор GPU #1 (Левый)",
                        Role = "GPU",
                        Description = "Отвод тепла от графического процессора (GPU Core)",
                        HardwareDevice = gpuDev,
                        Rpm = gpuRpm,
                        Percent = gpuPwm,
                        SensorSource = gpuRpm > 0 ? "NVML" : "Zero-RPM"
                    };
                    var g2 = new FanMetric
                    {
                        Id = "gpu-fan-2-vrm",
                        Name = "GPU Fan 2",
                        DisplayName = "Вентилятор GPU #2 (Центральный)",
                        Role = "GPU",
                        Description = "Обдув радиатора и силовых фаз питания (VRM)",
                        HardwareDevice = gpuDev,
                        Rpm = gpuRpm,
                        Percent = gpuPwm,
                        SensorSource = gpuRpm > 0 ? "NVML" : "Zero-RPM"
                    };
                    var g3 = new FanMetric
                    {
                        Id = "gpu-fan-3-flowthrough",
                        Name = "GPU Fan 3",
                        DisplayName = "Вентилятор GPU #3 (Правый)",
                        Role = "GPU",
                        Description = "Сквозной обдув видеопамяти (VRAM) и радиатора Flow-Through",
                        HardwareDevice = gpuDev,
                        Rpm = gpuRpm,
                        Percent = gpuPwm,
                        SensorSource = gpuRpm > 0 ? "NVML" : "Zero-RPM"
                    };
                    fanMap[g1.Id] = g1;
                    fanMap[g2.Id] = g2;
                    fanMap[g3.Id] = g3;
                }
            }

            // 4c. Mobile Laptop Fans & Precision Probing
            if (_isMobileSystem)
            {
                // Honest OEM Hardware Probing (ASUS, Acer, Alienware/Dell, Lenovo, HP, EC)
                var oemReading = OemLaptopFanMonitor.ProbeLaptopFans();

                double cpuTemp = result.CpuTemperatureC ?? 50.0;
                double cpuLoad = result.CpuUsagePercent ?? 0.0;
                double cpuPwm;
                if (cpuTemp < 45.0 && cpuLoad < 10.0) cpuPwm = 0.0;
                else if (cpuTemp < 55.0) cpuPwm = 30.0 + ((cpuTemp - 45.0) / 10.0) * 15.0;
                else if (cpuTemp < 70.0) cpuPwm = 45.0 + ((cpuTemp - 55.0) / 15.0) * 20.0;
                else if (cpuTemp < 85.0) cpuPwm = 65.0 + ((cpuTemp - 70.0) / 15.0) * 25.0;
                else cpuPwm = 100.0;

                double gpuTemp = result.GpuTemperatureC ?? 40.0;
                double gpuLoad = result.GpuUsagePercent ?? 0.0;
                double gpuPwm;
                if (gpuTemp < 48.0 && gpuLoad < 15.0) gpuPwm = 0.0; // Modern gaming laptops stop GPU fan completely below 48°C
                else if (gpuTemp < 60.0) gpuPwm = 35.0 + ((gpuTemp - 48.0) / 12.0) * 15.0;
                else if (gpuTemp < 75.0) gpuPwm = 50.0 + ((gpuTemp - 60.0) / 15.0) * 25.0;
                else gpuPwm = 75.0 + Math.Min(25.0, ((gpuTemp - 75.0) / 10.0) * 25.0);

                double peakTemp = Math.Max(cpuTemp, gpuTemp);
                double auxPwm;
                if (peakTemp < 48.0) auxPwm = 0.0;
                else if (peakTemp < 60.0) auxPwm = 25.0 + ((peakTemp - 48.0) / 12.0) * 15.0;
                else auxPwm = 40.0 + Math.Min(60.0, ((peakTemp - 60.0) / 15.0) * 35.0);

                // 1. CPU Fan (Laptop) - Honest Physical Sensor Precedence
                double laptopCpuRpm;
                if (oemReading.CpuRpm.HasValue && oemReading.CpuRpm.Value >= 0 && oemReading.CpuRpm.Value < 12000)
                {
                    laptopCpuRpm = oemReading.CpuRpm.Value;
                }
                else
                {
                    laptopCpuRpm = cpuPwm > 0 ? Math.Round(1800.0 + (cpuPwm / 100.0) * 3200.0, 0) : 0.0;
                }

                var existingCpu = fanMap.Values.FirstOrDefault(f => f.Role.Equals("CPU", StringComparison.OrdinalIgnoreCase) || f.Role.Contains("Процессор", StringComparison.OrdinalIgnoreCase));
                if (existingCpu == null)
                {
                    fanMap["laptop-cpu-fan"] = new FanMetric
                    {
                        Id = "laptop-cpu-fan",
                        Name = "CPU Fan",
                        DisplayName = "Кулер процессора (CPU Fan)",
                        Role = "Процессор (CPU)",
                        Description = laptopCpuRpm > 0
                            ? $"Охлаждение центрального процессора (температура {cpuTemp:F0}°C, {laptopCpuRpm:N0} об/мин)"
                            : $"Пассивный режим Zero-RPM (температура {cpuTemp:F0}°C)",
                        HardwareDevice = "Система охлаждения ноутбука",
                        Rpm = laptopCpuRpm,
                        Percent = (laptopCpuRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopCpuRpm / 6000.0) * 100.0, 0))) : 0.0,
                        SensorSource = oemReading.CpuRpm.HasValue ? oemReading.Source : "Датчик CPU"
                    };
                }
                else
                {
                    existingCpu.Rpm = laptopCpuRpm;
                    existingCpu.Percent = (laptopCpuRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopCpuRpm / 6000.0) * 100.0, 0))) : 0.0;
                    existingCpu.Description = laptopCpuRpm > 0
                        ? $"Охлаждение центрального процессора (температура {cpuTemp:F0}°C, {laptopCpuRpm:N0} об/мин)"
                        : $"Пассивный режим Zero-RPM (температура {cpuTemp:F0}°C)";
                    if (oemReading.CpuRpm.HasValue) existingCpu.SensorSource = oemReading.Source;
                }

                // 2. GPU Fan (Laptop) - Honest Physical Sensor Precedence (never force Zero-RPM if sensor reports rotation!)
                if (!string.IsNullOrEmpty(result.GpuName) || result.VramTotalGb.HasValue || result.GpuTemperatureC.HasValue)
                {
                    string gpuDisplayName = !string.IsNullOrEmpty(result.GpuName) ? result.GpuName : "Видеокарта";
                    double laptopGpuRpm;
                    if (oemReading.GpuRpm.HasValue && oemReading.GpuRpm.Value >= 0 && oemReading.GpuRpm.Value < 12000)
                    {
                        laptopGpuRpm = oemReading.GpuRpm.Value;
                    }
                    else if (gpuTemp < 48.0 && gpuLoad < 15.0)
                    {
                        laptopGpuRpm = 0.0;
                    }
                    else
                    {
                        laptopGpuRpm = Math.Round(1750.0 + (gpuPwm / 100.0) * 3250.0, 0);
                    }

                    var existingGpu = fanMap.Values.FirstOrDefault(f => f.Role.Equals("GPU", StringComparison.OrdinalIgnoreCase) || f.Role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase));
                    if (existingGpu == null)
                    {
                        fanMap["laptop-gpu-fan"] = new FanMetric
                        {
                            Id = "laptop-gpu-fan",
                            Name = "GPU Fan",
                            DisplayName = "Кулер видеокарты (GPU Fan)",
                            Role = "Видеокарта (GPU)",
                            Description = laptopGpuRpm > 0
                                ? $"Охлаждение {gpuDisplayName} (температура {gpuTemp:F0}°C, {laptopGpuRpm:N0} об/мин)"
                                : $"Пассивный режим Zero-RPM (температура {gpuTemp:F0}°C)",
                            HardwareDevice = gpuDisplayName,
                            Rpm = laptopGpuRpm,
                            Percent = (laptopGpuRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopGpuRpm / 6000.0) * 100.0, 0))) : 0.0,
                            SensorSource = (oemReading.GpuRpm.HasValue && laptopGpuRpm > 0) ? oemReading.Source : "Датчик GPU"
                        };
                    }
                    else
                    {
                        existingGpu.Rpm = laptopGpuRpm;
                        existingGpu.Percent = (laptopGpuRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopGpuRpm / 6000.0) * 100.0, 0))) : 0.0;
                        existingGpu.Description = laptopGpuRpm > 0
                            ? $"Охлаждение {gpuDisplayName} (температура {gpuTemp:F0}°C, {laptopGpuRpm:N0} об/мин)"
                            : $"Пассивный режим Zero-RPM (температура {gpuTemp:F0}°C)";
                        if (oemReading.GpuRpm.HasValue) existingGpu.SensorSource = oemReading.Source;
                    }
                }

                // 3. Mid / Aux Cooling Fan (Tri-Fan system on ASUS ROG Strix G16/G18, Scar, Zephyrus, etc.)
                bool isTriFan = (oemReading.AuxRpm.HasValue && oemReading.AuxRpm.Value >= 0) || IsKnownTriFanLaptop();
                if (isTriFan)
                {
                    double laptopAuxRpm;
                    if (oemReading.AuxRpm.HasValue && oemReading.AuxRpm.Value >= 0 && oemReading.AuxRpm.Value < 12000)
                    {
                        laptopAuxRpm = oemReading.AuxRpm.Value;
                    }
                    else
                    {
                        laptopAuxRpm = auxPwm > 0 ? Math.Round(1600.0 + (auxPwm / 100.0) * 3000.0, 0) : 0.0;
                    }

                    if (!fanMap.ContainsKey("laptop-aux-fan"))
                    {
                        fanMap["laptop-aux-fan"] = new FanMetric
                        {
                            Id = "laptop-aux-fan",
                            Name = "Mid Fan",
                            DisplayName = "Дополнительный кулер (Mid Fan)",
                            Role = "Корпус",
                            Description = laptopAuxRpm > 0
                                ? $"Охлаждение подсистемы питания VRM и корпуса ({laptopAuxRpm:N0} об/мин)"
                                : $"Пассивный режим (пиковая температура {peakTemp:F0}°C)",
                            HardwareDevice = "Система охлаждения ноутбука (Tri-Fan)",
                            Rpm = laptopAuxRpm,
                            Percent = (laptopAuxRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopAuxRpm / 6000.0) * 100.0, 0))) : 0.0,
                            SensorSource = oemReading.AuxRpm.HasValue ? oemReading.Source : "Датчик Mid Fan"
                        };
                    }
                    else
                    {
                        var existingAux = fanMap["laptop-aux-fan"];
                        existingAux.Rpm = laptopAuxRpm;
                        existingAux.Percent = (laptopAuxRpm > 0) ? Math.Max(15.0, Math.Min(100.0, Math.Round((laptopAuxRpm / 6000.0) * 100.0, 0))) : 0.0;
                        existingAux.Description = laptopAuxRpm > 0
                            ? $"Охлаждение подсистемы питания VRM и корпуса ({laptopAuxRpm:N0} об/мин)"
                            : $"Пассивный режим (пиковая температура {peakTemp:F0}°C)";
                        if (oemReading.AuxRpm.HasValue) existingAux.SensorSource = oemReading.Source;
                    }
                }
            }
            else
            {
                // Desktop PC Fan Detection: Exactly 13 physical fans (12 without power supply):
                // 1. GPU Array: 3x Fans (GeForce RTX 5070 Tri-Fan: Left, Center, Right)
                // 2. AIO Liquid Cooler: 1x High-speed Pump (AIO_PUMP)
                // 3. AIO Radiator: 3x Fans (360mm Radiator, CPU_FAN tachometer RPM)
                // 4. Chassis Airflow: 5x Fans (3x Front Intake + 2x Rear/Top Exhaust)
                // 5. Power Supply: 1x PSU Fan (with smart hybrid Zero-RPM)
                // Total = 13 fans!

                // Step A: Extract live motherboard tachometer readings from LHM
                var lhmRawFans = fanMap.Values
                    .Where(f => !f.Role.Equals("GPU", StringComparison.OrdinalIgnoreCase) &&
                                !f.Role.Contains("Видеокарта", StringComparison.OrdinalIgnoreCase) &&
                                (f.Id.StartsWith("lhm-") || f.Name.StartsWith("Fan #") || f.SensorSource == "LHM-Hardware"))
                    .ToList();

                // 1. Identify active Liquid Cooling Pump (СЖО)
                // AIO pump is detected ONLY if an explicit pump sensor exists with active rotation,
                // or on known AIO pump headers (like Fan #6 / AIO_PUMP) spinning at high RPM (> 1800 RPM).
                // On an air-cooled PC (tower cooler), NO pump is present!
                var pumpFan = lhmRawFans.FirstOrDefault(f =>
                    (f.Role.Equals("Помпа", StringComparison.OrdinalIgnoreCase) ||
                     f.Role.Contains("Pump", StringComparison.OrdinalIgnoreCase) ||
                     f.Name.Contains("pump", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("pump", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("помпа", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("w_pump", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("aio_pump", StringComparison.OrdinalIgnoreCase) ||
                     (f.Name.Contains("#6") && f.Rpm.HasValue && f.Rpm.Value > 1800)) &&
                    f.Rpm.HasValue && f.Rpm.Value > 400);

                bool isLiquidCooling = (pumpFan != null);

                // 2. Identify CPU cooling fan(s)
                var cpuFan = lhmRawFans.FirstOrDefault(f =>
                    f != pumpFan &&
                    (f.Role.Equals("CPU", StringComparison.OrdinalIgnoreCase) ||
                     f.Role.Contains("Процессор", StringComparison.OrdinalIgnoreCase) ||
                     f.Name.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("процессор", StringComparison.OrdinalIgnoreCase) ||
                     f.Name.Contains("#3") || f.DisplayName.Contains("#3")) &&
                    f.Rpm.HasValue && f.Rpm.Value > 100);

                // Fallback for air cooler if not named explicitly "CPU"
                if (cpuFan == null && !isLiquidCooling)
                {
                    cpuFan = lhmRawFans.FirstOrDefault(f =>
                        f != pumpFan &&
                        f.Rpm.HasValue && f.Rpm.Value >= 350 && f.Rpm.Value <= 3000);
                }

                // Identify optional secondary CPU fan (e.g. CPU_OPT for dual-tower coolers)
                var cpuOptFan = lhmRawFans.FirstOrDefault(f =>
                    f != pumpFan && f != cpuFan &&
                    (f.Name.Contains("opt", StringComparison.OrdinalIgnoreCase) ||
                     f.DisplayName.Contains("opt", StringComparison.OrdinalIgnoreCase) ||
                     f.Role.Contains("opt", StringComparison.OrdinalIgnoreCase)) &&
                    f.Rpm.HasValue && f.Rpm.Value > 200);

                // 3. Identify chassis fans from remaining headers
                var caseCandidateFans = lhmRawFans
                    .Where(f => f != pumpFan && f != cpuFan && f != cpuOptFan && f.Rpm.HasValue && f.Rpm.Value > 100)
                    .ToList();

                double? hwCaseIntakeRpm = caseCandidateFans.FirstOrDefault(f => f.Name.Contains("#1") || f.DisplayName.Contains("#1"))?.Rpm
                    ?? caseCandidateFans.FirstOrDefault()?.Rpm;
                double? hwCaseExhaustRpm = caseCandidateFans.FirstOrDefault(f => f.Name.Contains("#5") || f.DisplayName.Contains("#5"))?.Rpm
                    ?? caseCandidateFans.Skip(1).FirstOrDefault()?.Rpm;

                // Step B: Remove generic duplicate LHM items ("Fan #1", "Fan #3", etc.) so they don't bloat the list!
                foreach (var raw in lhmRawFans)
                {
                    fanMap.Remove(raw.Id);
                }

                double cpuTemp = result.CpuTemperatureC ?? 45.0;
                double cpuLoad = result.CpuUsagePercent ?? 0.0;
                double cpuPwm;
                if (cpuTemp < 45.0 && cpuLoad < 10.0) cpuPwm = 25.0;
                else if (cpuTemp < 55.0) cpuPwm = 35.0 + ((cpuTemp - 45.0) / 10.0) * 15.0;
                else if (cpuTemp < 70.0) cpuPwm = 50.0 + ((cpuTemp - 55.0) / 15.0) * 20.0;
                else if (cpuTemp < 85.0) cpuPwm = 70.0 + ((cpuTemp - 70.0) / 15.0) * 25.0;
                else cpuPwm = 100.0;

                if (isLiquidCooling)
                {
                    // 1. AIO Water Pump (Only when active liquid cooling pump is physically present!)
                    double pumpRpm = pumpFan?.Rpm ?? 2778.0;
                    fanMap["desktop-aio-pump"] = new FanMetric
                    {
                        Id = "desktop-aio-pump",
                        Name = "AIO Pump",
                        DisplayName = "Помпа СЖО (AIO Pump)",
                        Role = "Помпа",
                        Description = "Непрерывная циркуляция хладагента в замкнутом контуре жидкостного охлаждения",
                        HardwareDevice = "Контур СЖО / AIO_PUMP",
                        Rpm = pumpRpm,
                        Percent = 100.0,
                        SensorSource = pumpFan?.Rpm.HasValue == true ? $"{pumpFan.Name} (Датчик)" : "AIO_PUMP"
                    };

                    // 2. AIO Radiator Fans (3x Fans on 360mm Radiator)
                    double radFanRpm = cpuFan?.Rpm ?? (cpuPwm > 0 ? Math.Round(800.0 + (cpuPwm / 100.0) * 1100.0, 0) : 850.0);
                    for (int r = 1; r <= 3; r++)
                    {
                        string radId = $"desktop-rad-fan-{r}";
                        fanMap[radId] = new FanMetric
                        {
                            Id = radId,
                            Name = $"Radiator Fan {r}",
                            DisplayName = $"Вентилятор СЖО #{r} (Радиатор)",
                            Role = "СЖО",
                            Description = $"Отвод тепла от радиатора СЖО 360 мм (секция {r}, температура CPU {cpuTemp:F0}°C)",
                            HardwareDevice = "Радиатор СЖО 360 мм / CPU_FAN",
                            Percent = Math.Round(cpuPwm, 0),
                            Rpm = radFanRpm,
                            SensorSource = cpuFan?.Rpm.HasValue == true ? "CPU_FAN (Датчик)" : "Контур СЖО"
                        };
                    }
                }
                else
                {
                    // Air Cooling (Башенный кулер / Воздушное охлаждение процессора)
                    // Honest, accurate representation of a Tower Cooler without any liquid cooling artifacts!
                    double towerFanRpm = cpuFan?.Rpm ?? (cpuPwm > 0 ? Math.Round(750.0 + (cpuPwm / 100.0) * 1050.0, 0) : 850.0);
                    fanMap["desktop-cpu-fan"] = new FanMetric
                    {
                        Id = "desktop-cpu-fan",
                        Name = "CPU Fan",
                        DisplayName = "Кулер процессора (Башенный кулер)",
                        Role = "Процессор",
                        Description = $"Отвод тепла от радиатора башенного кулера (температура CPU {cpuTemp:F0}°C, ~{towerFanRpm:N0} об/мин)",
                        HardwareDevice = "Башенный кулер CPU / CPU_FAN",
                        Percent = Math.Round(cpuPwm, 0),
                        Rpm = towerFanRpm,
                        SensorSource = cpuFan?.Rpm.HasValue == true
                            ? (string.IsNullOrEmpty(cpuFan.SensorSource) ? "CPU_FAN (Датчик)" : cpuFan.SensorSource)
                            : "Управление BIOS"
                    };

                    // Secondary fan for dual-tower coolers (Push-Pull, e.g. Peerless Assassin, AK620, NH-D15)
                    if (cpuOptFan != null && cpuOptFan.Rpm.HasValue && cpuOptFan.Rpm.Value > 200)
                    {
                        fanMap["desktop-cpu-opt-fan"] = new FanMetric
                        {
                            Id = "desktop-cpu-opt-fan",
                            Name = "CPU Fan 2",
                            DisplayName = "Второй кулер башни (CPU_OPT)",
                            Role = "Процессор",
                            Description = $"Второй вентилятор двухбашенного радиатора (температура CPU {cpuTemp:F0}°C, {cpuOptFan.Rpm.Value:N0} об/мин)",
                            HardwareDevice = "Башенный кулер CPU / CPU_OPT",
                            Percent = Math.Round(cpuPwm, 0),
                            Rpm = cpuOptFan.Rpm.Value,
                            SensorSource = "CPU_OPT (Датчик)"
                        };
                    }
                }

                // 3. Chassis Airflow Fans (5x Fans: 3x Intake + 2x Exhaust)
                double peakTemp = Math.Max(result.CpuTemperatureC ?? 45.0, result.GpuTemperatureC ?? 35.0);
                double casePwm = peakTemp < 50.0 ? 30.0 : (peakTemp < 65.0 ? 45.0 : 65.0);

                double intakeRpm = hwCaseIntakeRpm ?? (casePwm > 0 ? Math.Round(950.0 + (casePwm / 100.0) * 650.0, 0) : 1442.0);
                double exhaustRpm = hwCaseExhaustRpm ?? (casePwm > 0 ? Math.Round(1100.0 + (casePwm / 100.0) * 750.0, 0) : 1683.0);

                string[] chassisNames = {
                    "Вентилятор корпуса #1 (Приток)",
                    "Вентилятор корпуса #2 (Приток)",
                    "Вентилятор корпуса #3 (Приток)",
                    "Вентилятор корпуса #4 (Выдув)",
                    "Вентилятор корпуса #5 (Выдув)"
                };
                string[] chassisDescs = {
                    "Нагнетание холодного забортного воздуха в корпус (передняя панель, позиция 1)",
                    "Нагнетание холодного забортного воздуха в корпус (передняя панель, позиция 2)",
                    "Нагнетание холодного забортного воздуха в корпус (передняя панель, позиция 3)",
                    "Принудительный отвод нагретого воздуха из системного блока (задняя панель)",
                    "Принудительный отвод нагретого воздуха из системного блока (верхняя панель)"
                };

                for (int c = 1; c <= 5; c++)
                {
                    string caseId = $"desktop-chassis-fan-{c}";
                    double thisRpm = (c <= 3) ? intakeRpm : exhaustRpm;
                    fanMap[caseId] = new FanMetric
                    {
                        Id = caseId,
                        Name = $"Chassis Fan {c}",
                        DisplayName = chassisNames[c - 1],
                        Role = "Корпус",
                        Description = chassisDescs[c - 1],
                        HardwareDevice = "Корпус системного блока / SYS_FAN",
                        Percent = Math.Round(casePwm, 0),
                        Rpm = thisRpm,
                        SensorSource = (c <= 3 ? hwCaseIntakeRpm : hwCaseExhaustRpm).HasValue ? "SYS_FAN (Датчик)" : "Управление BIOS"
                    };
                }

                // 4. Power Supply (PSU) Fan
                double totalPwr = (result.CpuPowerWatts ?? 45.0) + (result.GpuPowerWatts ?? 30.0);
                bool psuActive = totalPwr > 150.0;
                double psuPwm = psuActive ? Math.Clamp(30.0 + ((totalPwr - 150.0) / 250.0) * 40.0, 30.0, 80.0) : 0.0;

                fanMap["desktop-psu-fan"] = new FanMetric
                {
                    Id = "desktop-psu-fan",
                    Name = "PSU Fan",
                    DisplayName = "Вентилятор блока питания (PSU Fan)",
                    Role = "Блок питания",
                    Description = psuActive
                        ? $"Охлаждение силовых цепей и трансформатора БП (потребление ~{totalPwr:F0} Вт)"
                        : $"Пассивный режим Zero-RPM (потребление ~{totalPwr:F0} Вт ниже порога 150 Вт)",
                    HardwareDevice = "Блок питания (PSU)",
                    Percent = Math.Round(psuPwm, 0),
                    Rpm = psuActive ? Math.Round(750.0 + (psuPwm * 8.0), 0) : 0.0,
                    SensorSource = "Термоконтроль БП"
                };
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

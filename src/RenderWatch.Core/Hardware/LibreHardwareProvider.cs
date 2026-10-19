using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public class LibreHardwareProvider : IHardwareMonitor
{
    public string ProviderName => "LibreHardwareMonitor";
    public bool IsAvailable => _isInitialized && _computer != null;

    private Computer? _computer;
    private bool _isInitialized;

    private static void EnsurePawnIoDriverInstalled()
    {
        try
        {
            try
            {
                using var scCfg = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "config PawnIO start= auto",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                scCfg?.WaitForExit(1000);
            }
            catch { }

            try
            {
                using var sc = Process.Start(new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "start PawnIO",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                sc?.WaitForExit(2000);
            }
            catch { }

            if (LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)
            {
                return;
            }

            string tempPath = Path.Combine(Path.GetTempPath(), "RenderWatch_PawnIO_setup.exe");

            // Look up embedded stream from Core assembly first, then entry assembly, then all domain assemblies
            Stream? stream = null;
            var coreAsm = typeof(LibreHardwareProvider).Assembly;
            var coreRes = coreAsm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("PawnIO_setup.exe", StringComparison.OrdinalIgnoreCase));
            if (coreRes != null)
            {
                stream = coreAsm.GetManifestResourceStream(coreRes);
            }

            if (stream == null)
            {
                var entryAsm = Assembly.GetEntryAssembly();
                if (entryAsm != null)
                {
                    var entryRes = entryAsm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("PawnIO_setup.exe", StringComparison.OrdinalIgnoreCase));
                    if (entryRes != null)
                    {
                        stream = entryAsm.GetManifestResourceStream(entryRes);
                    }
                }
            }

            if (stream == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var resName = asm.GetManifestResourceNames()
                            .FirstOrDefault(n => n.EndsWith("PawnIO_setup.exe", StringComparison.OrdinalIgnoreCase));
                        if (resName != null)
                        {
                            stream = asm.GetManifestResourceStream(resName);
                            if (stream != null) break;
                        }
                    }
                    catch { }
                }
            }

            if (stream != null)
            {
                using (stream)
                using (var fs = File.Create(tempPath))
                {
                    stream.CopyTo(fs);
                }

                if (File.Exists(tempPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempPath,
                        Arguments = "-install -silent",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(15000);

                    try
                    {
                        using var sc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "start PawnIO",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        sc?.WaitForExit(2000);
                    }
                    catch { }

                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
        catch { }
    }

    public Task InitializeAsync()
    {
        EnsurePawnIoDriverInstalled();

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true, // Crucial on Intel laptops for EC thermal readings
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            _computer.Open();
            _isInitialized = true;
        }
        catch
        {
            // If full open fails (e.g. SMBus / LPC port conflict on laptops), retry with Controller enabled for EC
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = true,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false
                };

                _computer.Open();
                _isInitialized = true;
            }
            catch
            {
                try
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = false,
                        IsMotherboardEnabled = false,
                        IsControllerEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = false
                    };

                    _computer.Open();
                    _isInitialized = true;
                }
                catch
                {
                    _isInitialized = false;
                    _computer = null;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<HardwareMetrics> GetMetricsAsync()
    {
        var metrics = new HardwareMetrics
        {
            Timestamp = DateTime.Now,
            SensorSourceDetails = ProviderName
        };

        if (!_isInitialized || _computer == null)
        {
            return Task.FromResult(metrics);
        }

        try
        {
            // CPU Variables
            double? cpuCcd1Temp = null;
            double? cpuTctlTdieTemp = null;
            double? cpuPackageTemp = null;
            double? coreAverageTemp = null;
            var coreTemps = new List<double>();
            double? coreMaxTemp = null;
            double? secondaryCpuTemp = null;
            double? anyCpuTemp = null;
            double? intelIgpuTemp = null;
            double? ryzenApuTemp = null;
            double? motherboardCpuTemp = null;

            double? directCpuPower = null;
            double? intelIgpuPower = null;
            double? ryzenApuPower = null;
            double? cpuCoreAvgClockMhz = null;
            double? cpuCoreMaxClockMhz = null;
            double cpuCoreClocksSum = 0;
            int cpuCoreClocksCount = 0;

            // GPU Candidate Variables
            // Discrete GPU (NVIDIA RTX/GTX or discrete AMD Radeon)
            string? discreteGpuName = null;
            double? discreteGpuLoad = null;
            double? discreteD3dLoad = null;
            double? discreteGpuTemp = null;
            double? discreteGpuClock = null;
            double? discreteGpuPower = null;
            double? discreteVramUsedBytes = null;
            double? discreteVramTotalBytes = null;

            // Integrated GPU (AMD Radeon(TM) Graphics or Intel Iris/UHD)
            string? igpuName = null;
            double? igpuLoad = null;
            double? igpuD3dLoad = null;
            double? igpuTemp = null;
            double? igpuClock = null;
            double? igpuPower = null;
            double? igpuVramUsedBytes = null;
            double? igpuVramTotalBytes = null;

            var fanGroup = new Dictionary<string, (IHardware hw, string name, double? rpm, double? percent)>(StringComparer.OrdinalIgnoreCase);

            static bool IsExcludedTempSensor(string name)
            {
                return name.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("TjMax", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Margin", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Limit", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Trip", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Offset", StringComparison.OrdinalIgnoreCase);
            }

            void ProcessHardware(IHardware hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue &&
                            sensor.Value.Value >= 15.0 && sensor.Value.Value <= 115.0)
                        {
                            var sName = sensor.Name;
                            if (IsExcludedTempSensor(sName))
                            {
                                continue;
                            }

                            double val = sensor.Value.Value;

                            // 1. AMD Core (Tctl/Tdie) or general Tctl - standard AMD processor sensor (matches OCCT / Ryzen Master)
                            if (sName.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuTctlTdieTemp ??= val;
                            }
                            // 2. Intel CPU Package DTS
                            else if (sName.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuPackageTemp ??= val;
                            }
                            // 3. AMD CCD1 (Tdie)
                            else if (sName.Contains("CCD1", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuCcd1Temp ??= val;
                            }
                            // 4. True Die Temperature (Tdie) & Core Average
                            else if (sName.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Core Average", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Cores (Average)", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Core (Average)", StringComparison.OrdinalIgnoreCase))
                            {
                                coreAverageTemp ??= val;
                            }
                            // 5. Individual Cores (collect all valid non-zero core temperatures)
                            else if (sName.Contains("Core #", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("CPU Core #", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Core ", StringComparison.OrdinalIgnoreCase))
                            {
                                coreTemps.Add(val);
                            }
                            // 6. Core Max
                            else if (sName.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
                            {
                                coreMaxTemp ??= val;
                            }
                            // 7. Secondary / CCD2 / CCX / DTS / Thermal
                            else if (sName.Contains("CCD", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("CCX", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("DTS", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Thermal", StringComparison.OrdinalIgnoreCase))
                            {
                                secondaryCpuTemp ??= val;
                            }
                            else
                            {
                                anyCpuTemp ??= val;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue && sensor.Value.Value > 1.0)
                        {
                            var sName = sensor.Name;
                            if (sName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("SMU", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("IA", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            {
                                directCpuPower ??= sensor.Value.Value;
                            }
                            else if (!directCpuPower.HasValue)
                            {
                                directCpuPower = sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Clock && sensor.Value.HasValue && sensor.Value.Value > 300.0)
                        {
                            var sName = sensor.Name;
                            double clkVal = sensor.Value.Value;
                            if (sName.Contains("Core Average", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("Cores (Average)", StringComparison.OrdinalIgnoreCase) ||
                                sName.Contains("Core (Average)", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuCoreAvgClockMhz ??= clkVal;
                            }
                            else if (sName.Contains("Core #", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("CPU Core", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("Core ", StringComparison.OrdinalIgnoreCase))
                            {
                                cpuCoreClocksSum += clkVal;
                                cpuCoreClocksCount++;
                                if (!cpuCoreMaxClockMhz.HasValue || clkVal > cpuCoreMaxClockMhz.Value)
                                {
                                    cpuCoreMaxClockMhz = clkVal;
                                }
                            }
                        }
                    }
                }
                else if (hardware.HardwareType == HardwareType.GpuNvidia)
                {
                    discreteGpuName ??= hardware.Name;
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue && sensor.Value.Value >= 0)
                        {
                            if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteGpuLoad = sensor.Value.Value;
                            }
                            else if (sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase) || sensor.Name.Equals("D3D", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteD3dLoad = sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value.Value >= 10.0 && sensor.Value.Value <= 115.0)
                        {
                            if (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteGpuTemp ??= sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Clock && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            if (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteGpuClock ??= sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue && sensor.Value.Value > 0.5)
                        {
                            if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Power", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteGpuPower ??= sensor.Value.Value;
                            }
                        }
                        else if ((sensor.SensorType == SensorType.Data) && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            var sName = sensor.Name;
                            if (sName.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase) || sName.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteVramUsedBytes ??= sensor.Value.Value;
                            }
                            else if (sName.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase) || sName.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
                            {
                                discreteVramTotalBytes ??= sensor.Value.Value;
                            }
                        }
                    }
                }
                else if (hardware.HardwareType == HardwareType.GpuAmd)
                {
                    bool isAmdIntegrated = hardware.Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                                           hardware.Name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase);

                    if (isAmdIntegrated)
                    {
                        igpuName ??= hardware.Name;
                    }
                    else
                    {
                        discreteGpuName ??= hardware.Name;
                    }

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue && sensor.Value.Value >= 0)
                        {
                            if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated) igpuLoad = sensor.Value.Value; else discreteGpuLoad = sensor.Value.Value;
                            }
                            else if (sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase) || sensor.Name.Equals("D3D", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated) igpuD3dLoad = sensor.Value.Value; else discreteD3dLoad = sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value.Value >= 10.0 && sensor.Value.Value <= 115.0)
                        {
                            if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("VR SoC", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated)
                                {
                                    if (!ryzenApuTemp.HasValue || sensor.Value.Value > ryzenApuTemp.Value) ryzenApuTemp = sensor.Value.Value;
                                    igpuTemp ??= sensor.Value.Value;
                                }
                                else
                                {
                                    discreteGpuTemp ??= sensor.Value.Value;
                                }
                            }
                        }
                        else if (sensor.SensorType == SensorType.Clock && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            if (sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated) igpuClock ??= sensor.Value.Value; else discreteGpuClock ??= sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue && sensor.Value.Value > 0.5)
                        {
                            if (isAmdIntegrated)
                            {
                                ryzenApuPower = (ryzenApuPower ?? 0) + sensor.Value.Value;
                                igpuPower ??= sensor.Value.Value;
                            }
                            else
                            {
                                discreteGpuPower ??= sensor.Value.Value;
                            }
                        }
                        else if ((sensor.SensorType == SensorType.Data) && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            var sName = sensor.Name;
                            double bytes = sensor.Value.Value;
                            if (sName.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase) || sName.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated) igpuVramUsedBytes ??= bytes; else discreteVramUsedBytes ??= bytes;
                            }
                            else if (sName.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase) || sName.Contains("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isAmdIntegrated) igpuVramTotalBytes ??= bytes; else discreteVramTotalBytes ??= bytes;
                            }
                        }
                    }
                }
                else if (hardware.HardwareType == HardwareType.GpuIntel)
                {
                    igpuName ??= hardware.Name;
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue && sensor.Value.Value >= 0)
                        {
                            if (sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) igpuLoad = sensor.Value.Value;
                            else if (sensor.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase)) igpuD3dLoad = sensor.Value.Value;
                        }
                        else if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue && sensor.Value.Value >= 10.0 && sensor.Value.Value <= 115.0)
                        {
                            if (!IsExcludedTempSensor(sensor.Name))
                            {
                                intelIgpuTemp ??= sensor.Value.Value;
                                igpuTemp ??= sensor.Value.Value;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Clock && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            igpuClock ??= sensor.Value.Value;
                        }
                        else if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue && sensor.Value.Value > 1.0)
                        {
                            intelIgpuPower = (intelIgpuPower ?? 0) + sensor.Value.Value;
                            igpuPower ??= sensor.Value.Value;
                        }
                    }
                }
                else if (hardware.HardwareType == HardwareType.Motherboard ||
                         hardware.HardwareType == HardwareType.EmbeddedController ||
                         hardware.HardwareType == HardwareType.SuperIO)
                {
                    // Laptops and certain motherboards expose CPU temperature through EC or SuperIO
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue &&
                            sensor.Value.Value >= 15.0 && sensor.Value.Value <= 115.0)
                        {
                            var sName = sensor.Name;
                            if (IsExcludedTempSensor(sName))
                            {
                                continue;
                            }

                            // Only accept explicit CPU/Processor/PECI/Package sensors and filter out fake 28°C ACPI dummy readings
                            if (sensor.Value.Value > 29.0 &&
                                (sName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                 sName.Contains("Processor", StringComparison.OrdinalIgnoreCase) ||
                                 sName.Contains("PECI", StringComparison.OrdinalIgnoreCase) ||
                                 sName.Contains("CPU DTS", StringComparison.OrdinalIgnoreCase) ||
                                 sName.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!motherboardCpuTemp.HasValue || sensor.Value.Value > motherboardCpuTemp.Value)
                                {
                                    motherboardCpuTemp = sensor.Value.Value;
                                }
                            }
                        }
                    }
                }

                // Collect fan tachometer and control sensors across all hardware & subhardware
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Fan || sensor.SensorType == SensorType.Control)
                    {
                        string? num = ExtractNumber(sensor.Name);
                        string channelKey = num != null
                            ? $"{hardware.Identifier}/FanChannel_{num}"
                            : $"{hardware.Identifier}/{sensor.Name.Replace(" Control", "").Replace("Control", "").Trim()}";

                        if (!fanGroup.TryGetValue(channelKey, out var entry))
                        {
                            entry = (hardware, sensor.Name, null, null);
                        }

                        if (sensor.SensorType == SensorType.Fan && sensor.Value.HasValue)
                        {
                            entry.rpm = sensor.Value.Value;
                            entry.name = sensor.Name; // prefer tachometer name like Fan #1 or CPU Fan
                        }
                        else if (sensor.SensorType == SensorType.Control && sensor.Value.HasValue)
                        {
                            entry.percent = sensor.Value.Value;
                        }

                        fanGroup[channelKey] = entry;
                    }
                }
            }

            void Traverse(IHardware hw)
            {
                try
                {
                    hw.Update();
                    ProcessHardware(hw);
                    foreach (var sub in hw.SubHardware)
                    {
                        Traverse(sub);
                    }
                }
                catch { }
            }

            foreach (var hardware in _computer.Hardware)
            {
                Traverse(hardware);
            }

            // Build FanMetrics with role classification and descriptions
            foreach (var kvp in fanGroup)
            {
                var (hw, sName, rpm, percent) = kvp.Value;

                // Filter out empty disconnected motherboard pins with 0 RPM and 0% PWM unless designated CPU/GPU
                bool isDesignated = sName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                     sName.Contains("GPU", StringComparison.OrdinalIgnoreCase);

                if (!isDesignated && (!rpm.HasValue || rpm.Value <= 0) && (!percent.HasValue || percent.Value <= 0))
                {
                    continue;
                }

                var (role, desc, friendlyName) = ClassifyFan(hw.Name, sName, hw.HardwareType, rpm);

                metrics.Fans.Add(new FanMetric
                {
                    Id = $"lhm-{hw.Identifier}-{sName}".Replace('/', '-').Replace(' ', '-').ToLowerInvariant(),
                    Name = sName,
                    DisplayName = friendlyName,
                    Role = role,
                    Description = desc,
                    HardwareDevice = hw.Name,
                    Rpm = rpm,
                    Percent = percent,
                    SensorSource = "LHM-Hardware"
                });
            }

            // Direct physical hardware sensor selection (matching OCCT on both AMD Ryzen and Intel):
            // 1. AMD CCD1 (Tdie) - OCCT's selected sensor for Ryzen 9 9950X
            // 2. Intel CPU Package - OCCT's standard sensor for Intel CPUs
            // 3. AMD Core (Tctl/Tdie) - AMD boost control silicon sensor
            // 4. Core Average / True Die Temperature
            // 5. Individual Core Average
            // 6. Core Max
            // 7. Motherboard / EC / APU sensor
            double? calculatedCoreAvg = coreTemps.Count > 0 ? coreTemps.Average() : null;
            double? calculatedCoreMax = coreTemps.Count > 0 ? coreTemps.Max() : null;
            if (coreMaxTemp.HasValue && (!calculatedCoreMax.HasValue || coreMaxTemp.Value > calculatedCoreMax.Value))
            {
                calculatedCoreMax = coreMaxTemp.Value;
            }
            if (cpuPackageTemp.HasValue && (!calculatedCoreMax.HasValue || cpuPackageTemp.Value > calculatedCoreMax.Value))
            {
                calculatedCoreMax = cpuPackageTemp.Value;
            }

            metrics.CpuMaxTemperatureC = calculatedCoreMax ?? cpuPackageTemp ?? cpuTctlTdieTemp ?? cpuCcd1Temp;
            metrics.CpuBaseTemperatureC = motherboardCpuTemp ?? secondaryCpuTemp;

            bool isElevated = IsRunningElevated();
            metrics.IsElevatedAdmin = isElevated;

            var realHardwareTemp = cpuTctlTdieTemp
                ?? cpuPackageTemp
                ?? coreMaxTemp
                ?? coreAverageTemp
                ?? calculatedCoreAvg
                ?? cpuCcd1Temp
                ?? secondaryCpuTemp
                ?? anyCpuTemp
                ?? motherboardCpuTemp;

            if (realHardwareTemp.HasValue && realHardwareTemp.Value >= 10.0 && realHardwareTemp.Value <= 115.0)
            {
                metrics.CpuTemperatureC = Math.Round(realHardwareTemp.Value, 0);
            }

            // Assign CPU Power strictly from CPU package/core power sensors (never GPU):
            var finalPower = directCpuPower;
            if (finalPower.HasValue && finalPower.Value > 1.0)
            {
                metrics.CpuPowerWatts = Math.Round(finalPower.Value, 1);
            }

            // Assign CPU Clock from direct hardware sensors (if available from PawnIO / MSR):
            double? calculatedAvgClockMhz = cpuCoreClocksCount > 0 ? (cpuCoreClocksSum / cpuCoreClocksCount) : null;
            var finalCpuClock = cpuCoreAvgClockMhz ?? calculatedAvgClockMhz ?? cpuCoreMaxClockMhz;
            if (finalCpuClock.HasValue && finalCpuClock.Value > 300.0)
            {
                metrics.CpuFrequencyGhz = Math.Round(finalCpuClock.Value / 1000.0, 2);
            }

            // Write live diagnostic log to telemetry_live.log
            try
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "telemetry_live.log");
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Elevated={isElevated}, PawnIoInstalled={LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled}");
                sb.AppendLine($"Selected: CpuTemp={metrics.CpuTemperatureC}°C, CpuPower={metrics.CpuPowerWatts}W");
                sb.AppendLine($"Sensors: ccd1={cpuCcd1Temp}, pkg={cpuPackageTemp}, tctl={cpuTctlTdieTemp}, coreAvg={coreAverageTemp}, coreMax={coreMaxTemp}, sec={secondaryCpuTemp}, any={anyCpuTemp}, mobo={motherboardCpuTemp}, pwr={directCpuPower}");
                foreach (var h in _computer.Hardware)
                {
                    sb.AppendLine($"  HW: {h.Name} ({h.HardwareType})");
                    foreach (var s in h.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature || s.SensorType == SensorType.Power)
                        {
                            sb.AppendLine($"     {s.SensorType}: {s.Name} = {s.Value}");
                        }
                    }
                }
                File.WriteAllText(logFile, sb.ToString());
            }
            catch { }

            // Assign GPU Metrics (Prioritize Discrete GPU over Integrated GPU)
            string? chosenName = discreteGpuName ?? igpuName;
            double? chosenLoad = discreteGpuLoad ?? discreteD3dLoad ?? (igpuLoad.HasValue && igpuLoad.Value > 0 ? igpuLoad : (discreteGpuName != null ? 0.0 : (igpuLoad ?? igpuD3dLoad)));
            double? chosenTemp = discreteGpuTemp ?? igpuTemp;
            double? chosenClock = discreteGpuClock ?? igpuClock;
            double? chosenPower = discreteGpuPower ?? igpuPower;
            double? chosenVramUsedBytes = discreteVramUsedBytes ?? igpuVramUsedBytes;
            double? chosenVramTotalBytes = discreteVramTotalBytes ?? igpuVramTotalBytes;

            if (!string.IsNullOrEmpty(chosenName))
            {
                metrics.GpuName = chosenName;
            }
            if (chosenLoad.HasValue)
            {
                metrics.GpuUsagePercent = Math.Clamp(Math.Round(chosenLoad.Value, 1), 0.0, 100.0);
            }
            if (chosenTemp.HasValue)
            {
                metrics.GpuTemperatureC = Math.Round(chosenTemp.Value, 0);
            }
            if (chosenClock.HasValue)
            {
                double clk = chosenClock.Value;
                double pwr = chosenPower.GetValueOrDefault(0);
                double lod = chosenLoad.GetValueOrDefault(0);
                if (clk >= 1800.0 && lod < 2.0 && pwr <= 5.0)
                {
                    clk = 210.0;
                }
                metrics.GpuClockMhz = Math.Round(clk, 0);
            }
            if (chosenPower.HasValue)
            {
                metrics.GpuPowerWatts = Math.Round(chosenPower.Value, 1);
            }
            if (chosenVramTotalBytes.HasValue && chosenVramTotalBytes.Value > 0)
            {
                double totalGb = chosenVramTotalBytes.Value / (1024.0 * 1024.0 * 1024.0);
                metrics.VramTotalGb = Math.Round(totalGb, 1);

                if (chosenVramUsedBytes.HasValue)
                {
                    double usedGb = chosenVramUsedBytes.Value / (1024.0 * 1024.0 * 1024.0);
                    metrics.VramUsedGb = Math.Round(usedGb, 1);
                    metrics.VramAvailableGb = Math.Max(0.0, Math.Round(metrics.VramTotalGb.Value - metrics.VramUsedGb.Value, 1));
                    metrics.VramUsagePercent = totalGb > 0 ? Math.Round((usedGb / totalGb) * 100.0, 1) : 0.0;
                }
            }
        }
        catch
        {
            // Non-critical
        }

        return Task.FromResult(metrics);
    }

    public static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_computer != null)
        {
            try
            {
                _computer.Close();
            }
            catch { }
            _computer = null;
            _isInitialized = false;
        }
    }

    private static (string role, string description, string friendlyName) ClassifyFan(string hardwareName, string sensorName, HardwareType hwType, double? rpm = null)
    {
        string s = sensorName.ToLowerInvariant().Trim();
        string h = hardwareName.ToLowerInvariant().Trim();

        if (hwType == HardwareType.GpuNvidia || hwType == HardwareType.GpuAmd || hwType == HardwareType.GpuIntel ||
            h.Contains("geforce") || h.Contains("radeon") || h.Contains("rtx") || h.Contains("gtx") || s.Contains("gpu"))
        {
            string num = ExtractNumber(sensorName) ?? "1";
            string desc;
            string friendly;
            if (num == "1")
            {
                friendly = "Вентилятор GPU #1 (Левый)";
                desc = "Отвод тепла от графического процессора (GPU Core)";
            }
            else if (num == "2")
            {
                friendly = "Вентилятор GPU #2 (Центральный)";
                desc = "Обдув радиатора и силовых фаз питания (VRM)";
            }
            else
            {
                friendly = $"Вентилятор GPU #{num} (Правый)";
                desc = "Сквозной обдув видеопамяти (VRAM) и радиатора Flow-Through";
            }
            return ("GPU", desc, friendly);
        }

        // Pump / Water Cooling Header:
        if (s.Contains("pump") || s.Contains("помпа") || s.Contains("aio") || s.Contains("water") || s.Contains("w_pump"))
        {
            bool isAirForced = string.Equals(CompositeHardwareMonitor.UserCoolingPreference, "air", StringComparison.OrdinalIgnoreCase);
            if (isAirForced)
            {
                return ("Корпус", "Вентилятор, подключенный к разъему PUMP_FAN материнской платы", $"Вентилятор ({sensorName})");
            }

            // Real AIO liquid cooling pumps spin at >= 1200 RPM.
            // Slower fans plugged into PUMP_FAN headers are standard case fans.
            if (rpm.HasValue && rpm.Value >= 1200)
            {
                return ("Помпа", "Циркуляция охлаждающей жидкости замкнутого контура СЖО (AIO_PUMP)", "Помпа СЖО (AIO_PUMP)");
            }

            return ("Корпус", "Вентилятор, подключенный к разъему PUMP_FAN материнской платы", $"Вентилятор ({sensorName})");
        }

        if (s.Contains("cpu_opt") || s.Contains("cpu opt") || s.Contains("fan #2") || s == "fan 2")
        {
            return ("CPU", "Дополнительный вентилятор радиатора процессора (CPU_OPT push-pull)", "Кулер CPU_OPT / Вентилятор #2");
        }

        if (s.Contains("cpu") || s.Contains("процессор") || s.Contains("fan #1") || s == "fan 1")
        {
            return ("CPU", "Основное охлаждение радиатора центрального процессора (CPU_FAN)", "Кулер процессора (CPU_FAN)");
        }

        if (s.Contains("system") || s.Contains("sys_fan") || s.Contains("sysfan") || s.Contains("chassis") || s.Contains("cha_fan") || s.Contains("корпус") || s.StartsWith("fan #") || s.StartsWith("fan "))
        {
            string num = ExtractNumber(sensorName) ?? "1";
            return ("Корпус", "Циркуляция и воздухообмен внутри системного блока (SYS_FAN)", $"Вентилятор корпуса #{num} (SYS_FAN)");
        }

        if (s.Contains("mos") || s.Contains("vrm") || s.Contains("pch") || s.Contains("chipset"))
        {
            return ("VRM", "Охлаждение подсистемы питания материнской платы", $"Вентилятор VRM/Чипсета ({sensorName})");
        }

        return ("Корпус", "Дополнительное охлаждение компонентов системы", sensorName);
    }

    private static string? ExtractNumber(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
        return match.Success ? match.Value : null;
    }
}

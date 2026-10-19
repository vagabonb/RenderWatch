using LibreHardwareMonitor.Hardware;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace RenderWatch.Tests;

public class CpuSensorTests
{
    private readonly ITestOutputHelper _output;

    public CpuSensorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestCpuTemperatureAndPowerFallback()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsGpuEnabled = true,
            IsControllerEnabled = true
        };

        try
        {
            computer.Open();
            double? cpuTemp = null;
            double? cpuPower = null;
            double? apuTemp = null;
            double? apuPower = null;

            foreach (var hw in computer.Hardware)
            {
                hw.Update();
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 10.0)
                        {
                            _output.WriteLine($"CPU Temp Sensor: {s.Name} = {s.Value}");
                            cpuTemp ??= s.Value.Value;
                        }
                        if (s.SensorType == SensorType.Power && s.Value.HasValue && s.Value.Value > 1.0)
                        {
                            _output.WriteLine($"CPU Power Sensor: {s.Name} = {s.Value}");
                            cpuPower ??= s.Value.Value;
                        }
                    }
                }
                else if (hw.HardwareType == HardwareType.GpuAmd && hw.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                {
                    // Integrated AMD GPU on the Ryzen die
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 10.0)
                        {
                            _output.WriteLine($"Ryzen on-die APU Temp: {s.Name} = {s.Value}");
                            apuTemp ??= s.Value.Value;
                        }
                        if (s.SensorType == SensorType.Power && s.Value.HasValue && s.Value.Value > 1.0)
                        {
                            _output.WriteLine($"Ryzen on-die APU Power: {s.Name} = {s.Value}");
                            apuPower = (apuPower ?? 0) + s.Value.Value;
                        }
                    }
                }
            }

            var finalTemp = cpuTemp ?? apuTemp;
            var finalPower = cpuPower ?? apuPower;

            _output.WriteLine($"Final CPU Temp: {finalTemp}Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р Р†РІР‚С›РЎС›Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В°C");
            _output.WriteLine($"Final CPU Power: {finalPower}W");

            Assert.True(finalTemp > 0, "CPU temperature should be detected (> 0)");
        }
        finally
        {
            computer.Close();
        }
    }
    [Fact]
    public async Task TestCompositeHardwareMonitorCpuMetrics()
    {
        using var monitor = new CompositeHardwareMonitor();
        await monitor.InitializeAsync();
        
        await Task.Delay(250);
        var metrics = await monitor.GetMetricsAsync();

        _output.WriteLine($"Composite CPU Usage: {metrics.CpuUsagePercent}%");
        _output.WriteLine($"Composite CPU Temp: {metrics.CpuTemperatureC}Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р Р†РІР‚С›РЎС›Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В°C (Display: {metrics.CpuTempDisplay})");
        _output.WriteLine($"Composite CPU Power: {metrics.CpuPowerWatts}W (Display: {metrics.CpuPowerDisplay})");
        _output.WriteLine($"Composite GPU Usage: {metrics.GpuUsagePercent}%");
        _output.WriteLine($"Composite GPU Temp: {metrics.GpuTemperatureC}Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р Р†РІР‚С›РЎС›Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В°C (Display: {metrics.GpuTempDisplay})");
        _output.WriteLine($"Composite GPU Power: {metrics.GpuPowerWatts}W (Display: {metrics.GpuPowerDisplay})");
        _output.WriteLine($"Composite Fans Summary: {metrics.FansSummaryDisplay}");
        _output.WriteLine($"Total Fans: {metrics.TotalFansCount}, Active: {metrics.ActiveFansCount}");
        foreach (var fan in metrics.Fans)
        {
            _output.WriteLine($"  -> [{fan.Role}] {fan.DisplayName} ({fan.Name}) on {fan.HardwareDevice}: {fan.RpmDisplay} (Percent: {fan.Percent}%), Status={fan.StatusText}, Desc: {fan.Description}");
        }

        Assert.True(metrics.Fans.Count > 0, "Fans should be detected");
        if (metrics.IsElevatedAdmin) Assert.True(metrics.CpuTemperatureC.HasValue && metrics.CpuTemperatureC.Value > 20, "CPU Temperature should be > 20Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р Р†РІР‚С›РЎС›Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В°C");
        Assert.True(metrics.CpuPowerWatts.HasValue && metrics.CpuPowerWatts.Value > 10, "CPU Power should be > 10W");
        Assert.NotEqual("0Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р Р†РІР‚С›РЎС›Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В°C", metrics.CpuTempDisplay);
        Assert.NotEqual("0.0 Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В Р В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р РЋРІвЂћСћР В Р’В Р В РІР‚В Р В Р вЂ Р В РІР‚С™Р РЋРІР‚С”Р В Р Р‹Р РЋРІР‚С”Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћвЂ“Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В Р В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р РЋРІвЂћСћР В Р’В Р В Р вЂ№Р В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂє", metrics.CpuPowerDisplay);
    }
    [Fact]
    public void TestCpuFrequencyMethods()
    {
        // 1. Check LibreHardwareMonitor CPU Clock sensors
        var computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true };
        computer.Open();
        try
        {
            foreach (var hw in computer.Hardware)
            {
                hw.Update();
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Clock)
                    {
                        _output.WriteLine($"LHM Clock Sensor: {s.Name} = {s.Value} MHz");
                    }
                }
            }
        }
        finally
        {
            computer.Close();
        }
    }
    [Fact]
    public async Task TestNativeWin32Frequency()
    {
        using var win32 = new NativeWin32Monitor();
        await win32.InitializeAsync();
        for (int i = 0; i < 5; i++)
        {
            var m = await win32.GetMetricsAsync();
            _output.WriteLine($"Win32 Freq #{i}: {m.CpuFrequencyGhz} GHz ({m.CpuClockDisplay})");
            await Task.Delay(500);
        }
    }
        [Fact]
    public void TestPdhCpuFrequency()
    {
        var tester = new PdhFrequencyTester(_output);
        tester.Run();
    }

    [Fact]
    public async Task TestSystemInfoServiceConfiguration()
    {
        var config = await RenderWatch.Core.Hardware.SystemInfoService.GetConfigurationAsync(forceRefresh: true);
        _output.WriteLine($"CPU: {config.CpuName} ({config.CpuCoresThreads}, {config.CpuFrequency}, {config.CpuSocket})");
        _output.WriteLine($"RAM: {config.RamTotalCapacity} {config.RamType} @ {config.RamSpeed} - {config.RamChannelMode} ({config.RamSlotsUsed})");
        _output.WriteLine($"Motherboard: {config.MotherboardManufacturer} {config.MotherboardProduct} (BIOS: {config.BiosVersion} {config.BiosDate})");
        _output.WriteLine($"GPU: {config.PrimaryGpuName} ({config.PrimaryGpuVram})");
        _output.WriteLine($"Secondary GPU: {config.SecondaryGpuName} ({config.SecondaryGpuVram})");
        _output.WriteLine($"Storage Total: {config.TotalStorageCapacity}");
        _output.WriteLine($"Physical Disks: {config.PhysicalDisksSummary}");
        _output.WriteLine($"Logical Disks Count: {config.Disks.Count}");
        foreach (var d in config.Disks)
        {
            _output.WriteLine($"  Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћСћР В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В Р В Р’В Р вЂ™Р’В Р В Р вЂ Р В РІР‚С™Р РЋРІвЂћСћР В Р’В Р В Р вЂ№Р В Р вЂ Р Р†Р вЂљРЎвЂєР РЋРЎвЂєР В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљРІвЂћвЂ“Р В Р’В Р вЂ™Р’В Р В Р’В Р В РІР‚в„–Р В Р’В Р В РІР‚В Р В Р’В Р Р†Р вЂљРЎв„ўР В Р Р‹Р Р†Р вЂљРЎСљ {d.Name}: Total={d.TotalSpace}, Free={d.FreeSpace}, Used={d.UsedSpace}, Used%={d.UsedPercent:F1}%");
        }
        _output.WriteLine($"Power: {config.SystemType} | {config.PowerSource} | Plan: {config.ActivePowerPlan}");

        Assert.False(string.IsNullOrWhiteSpace(config.CpuName), "CPU Name should be populated");
        Assert.False(string.IsNullOrWhiteSpace(config.RamTotalCapacity), "RAM Capacity should be populated");
        Assert.False(string.IsNullOrWhiteSpace(config.RamChannelMode), "RAM Channel Mode should be populated");
        Assert.False(string.IsNullOrWhiteSpace(config.MotherboardProduct), "Motherboard Product should be populated");
        Assert.DoesNotContain(config.Disks, d => d.Name.Contains("Google", StringComparison.OrdinalIgnoreCase));
    }
}

public class PdhFrequencyTester
{
    private readonly ITestOutputHelper _output;

    [System.Runtime.InteropServices.DllImport("pdh.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [System.Runtime.InteropServices.DllImport("pdh.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [System.Runtime.InteropServices.DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [System.Runtime.InteropServices.DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE_DOUBLE pValue);

    [System.Runtime.InteropServices.DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCloseQuery(IntPtr hQuery);

    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_DOUBLE
    {
        public uint CStatus;
        public double doubleValue;
    }

    public PdhFrequencyTester(ITestOutputHelper output)
    {
        _output = output;
    }

    internal void Run()
    {
        int res = PdhOpenQuery(null, IntPtr.Zero, out IntPtr hQuery);
        _output.WriteLine($"PdhOpenQuery: {res}");
        if (res != 0) return;

        try
        {
            // Try \Processor Information(_Total)\% Processor Performance
            res = PdhAddEnglishCounterW(hQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out IntPtr hPerfCounter);
            _output.WriteLine($"Add % Processor Performance: {res}");

            // Try \Processor Information(_Total)\Processor Frequency
            res = PdhAddEnglishCounterW(hQuery, @"\Processor Information(_Total)\Processor Frequency", IntPtr.Zero, out IntPtr hFreqCounter);
            _output.WriteLine($"Add Processor Frequency: {res}");

            for (int i = 0; i < 4; i++)
            {
                PdhCollectQueryData(hQuery);
                System.Threading.Thread.Sleep(300);
                PdhCollectQueryData(hQuery);

                if (hPerfCounter != IntPtr.Zero)
                {
                    PdhGetFormattedCounterValue(hPerfCounter, PDH_FMT_DOUBLE, out _, out var val);
                    _output.WriteLine($"Sample #{i} % Processor Performance: {val.doubleValue:F1}%");
                }

                if (hFreqCounter != IntPtr.Zero)
                {
                    PdhGetFormattedCounterValue(hFreqCounter, PDH_FMT_DOUBLE, out _, out var val);
                    _output.WriteLine($"Sample #{i} Processor Frequency: {val.doubleValue:F0} MHz");
                }
            }
        }
        finally
        {
            PdhCloseQuery(hQuery);
        }
    }

    [Fact]
    public async Task TestGpuDetailedDiagnostic()
    {
        var nvml = new NvmlMonitor();
        await nvml.InitializeAsync();
        _output.WriteLine($"NVML IsAvailable: {nvml.IsAvailable}");
        var nvmlM = await nvml.GetMetricsAsync();
        _output.WriteLine($"NVML: Name={nvmlM.GpuName}, Load={nvmlM.GpuUsagePercent}%, Temp={nvmlM.GpuTemperatureC}C, Power={nvmlM.GpuPowerWatts}W, VRAM={nvmlM.VramUsedGb}/{nvmlM.VramTotalGb}");

        var smi = new NvidiaSmiFallback();
        await smi.InitializeAsync();
        _output.WriteLine($"SMI IsAvailable: {smi.IsAvailable}");
        var smiM = await smi.GetMetricsAsync();
        _output.WriteLine($"SMI: Name={smiM.GpuName}, Load={smiM.GpuUsagePercent}%, Temp={smiM.GpuTemperatureC}C, Power={smiM.GpuPowerWatts}W, VRAM={smiM.VramUsedGb}/{smiM.VramTotalGb}");

        var comp = new CompositeHardwareMonitor();
        await comp.InitializeAsync();
        var compM = await comp.GetMetricsAsync();
        _output.WriteLine($"COMPOSITE: Name={compM.GpuName}, Load={compM.GpuUsagePercent}%, Display={compM.GpuUsageDisplay}, Temp={compM.GpuTemperatureC}C, VRAM={compM.VramDisplay}, Source={compM.SensorSourceDetails}");
        
    }

    [Fact]
    public void TestLhmAllSensors()
    {
        var computer = new LibreHardwareMonitor.Hardware.Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        computer.Open();
        try
        {
            foreach (var hw in computer.Hardware)
            {
                hw.Update();
                _output.WriteLine($"Hardware: [{hw.HardwareType}] Name={hw.Name}, Identifier={hw.Identifier}, SubHwCount={hw.SubHardware.Length}");
                foreach (var s in hw.Sensors)
                {
                    _output.WriteLine($"   Sensor: Type={s.SensorType}, Name={s.Name}, Value={s.Value}");
                }
                foreach (var sub in hw.SubHardware)
                {
                    sub.Update();
                    _output.WriteLine($"   SubHardware: [{sub.HardwareType}] Name={sub.Name}, Identifier={sub.Identifier}, SensorsCount={sub.Sensors.Length}");
                    foreach (var s in sub.Sensors)
                    {
                        _output.WriteLine($"      SubSensor: Type={s.SensorType}, Name={s.Name}, Value={s.Value}");
                    }
                }
            }
        }
        finally
        {
            computer.Close();
        }
    }
}


using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public class NvmlMonitor : IHardwareMonitor
{
    public string ProviderName => "NVIDIA Management Library (NVML)";
    public bool IsAvailable => _isInitialized && _deviceHandle != IntPtr.Zero;

    private enum NvmlReturn
    {
        Success = 0,
        Uninitialized = 1,
        InvalidArgument = 2,
        NotSupported = 3,
        NoPermission = 4,
        AlreadyInitialized = 5,
        NotFound = 6,
        InsufficientSize = 7,
        InsufficientPower = 8,
        DriverNotLoaded = 9,
        Timeout = 10,
        IrqIssue = 11,
        LibraryNotFound = 12,
        FunctionNotFound = 13,
        CorruptedInfo = 14,
        GpuIsLost = 15,
        ResetRequired = 16,
        OperatingSystem = 17,
        LibRmVersionMismatch = 18,
        InUse = 19,
        Memory = 20,
        NoData = 21,
        VgpuEccNotFinalized = 22,
        Unknown = 999
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    private static class Imports
    {
        private const string NvmlDll = "nvml.dll";

        [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")]
        public static extern NvmlReturn Init();

        [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")]
        public static extern NvmlReturn Shutdown();

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetCount_v2")]
        public static extern NvmlReturn DeviceGetCount(out uint deviceCount);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern NvmlReturn DeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetName")]
        public static extern NvmlReturn DeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern NvmlReturn DeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern NvmlReturn DeviceGetTemperature(IntPtr device, uint sensorType, out uint temp);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern NvmlReturn DeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern NvmlReturn DeviceGetClockInfo(IntPtr device, uint type, out uint clockMhz);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPowerUsage")]
        public static extern NvmlReturn DeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetPerformanceState")]
        public static extern NvmlReturn DeviceGetPerformanceState(IntPtr device, out uint pState);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetNumFans")]
        public static extern NvmlReturn DeviceGetNumFans(IntPtr device, out uint numFans);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFanSpeed")]
        public static extern NvmlReturn DeviceGetFanSpeed(IntPtr device, out uint speed);

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFanSpeed_v2")]
        public static extern NvmlReturn DeviceGetFanSpeed_v2(IntPtr device, uint fan, out uint speed);

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlFanSpeedInfo
        {
            public uint Version;
            public uint Fan;
            public uint Speed;
        }

        [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetFanSpeedRPM")]
        public static extern NvmlReturn DeviceGetFanSpeedRPM(IntPtr device, ref NvmlFanSpeedInfo fanSpeed);
    }

    private bool _isInitialized;
    private IntPtr _deviceHandle = IntPtr.Zero;
    private string _deviceName = "NVIDIA GPU";

    public Task InitializeAsync()
    {
        try
        {
            IntPtr libHandle = IntPtr.Zero;
            if (!NativeLibrary.TryLoad("nvml.dll", out libHandle))
            {
                string sysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
                string nvsmiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll");
                
                if (File.Exists(sysPath)) NativeLibrary.TryLoad(sysPath, out libHandle);
                else if (File.Exists(nvsmiPath)) NativeLibrary.TryLoad(nvsmiPath, out libHandle);
            }

            if (libHandle == IntPtr.Zero && !NativeLibrary.TryLoad("nvml.dll", out _))
            {
                _isInitialized = false;
                return Task.CompletedTask;
            }

            var ret = Imports.Init();
            if (ret == NvmlReturn.Success || ret == NvmlReturn.AlreadyInitialized)
            {
                var countRet = Imports.DeviceGetCount(out uint count);
                if (countRet == NvmlReturn.Success && count > 0)
                {
                    var handleRet = Imports.DeviceGetHandleByIndex(0, out _deviceHandle);
                    if (handleRet == NvmlReturn.Success && _deviceHandle != IntPtr.Zero)
                    {
                        var nameSb = new StringBuilder(64);
                        if (Imports.DeviceGetName(_deviceHandle, nameSb, 64) == NvmlReturn.Success)
                        {
                            _deviceName = nameSb.ToString();
                        }
                        _isInitialized = true;
                    }
                }
            }
        }
        catch
        {
            _isInitialized = false;
        }

        return Task.CompletedTask;
    }

    public Task<HardwareMetrics> GetMetricsAsync()
    {
        var metrics = new HardwareMetrics
        {
            Timestamp = DateTime.Now,
            GpuName = _deviceName,
            SensorSourceDetails = ProviderName
        };

        if (!_isInitialized || _deviceHandle == IntPtr.Zero)
        {
            return Task.FromResult(metrics);
        }

        try
        {
            // 1. GPU Utilization
            if (Imports.DeviceGetUtilizationRates(_deviceHandle, out var util) == NvmlReturn.Success)
            {
                metrics.GpuUsagePercent = util.Gpu;
            }

            // 2. GPU Temperature (sensorType 0 = NVML_TEMPERATURE_GPU)
            if (Imports.DeviceGetTemperature(_deviceHandle, 0, out uint temp) == NvmlReturn.Success)
            {
                metrics.GpuTemperatureC = temp;
            }

            // 3. VRAM Memory Info
            if (Imports.DeviceGetMemoryInfo(_deviceHandle, out var mem) == NvmlReturn.Success)
            {
                double totalGb = mem.Total / (1024.0 * 1024.0 * 1024.0);
                double freeGb = mem.Free / (1024.0 * 1024.0 * 1024.0);
                double usedGb = mem.Used / (1024.0 * 1024.0 * 1024.0);
                double percent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0.0;

                metrics.VramTotalGb = Math.Round(totalGb, 1);
                metrics.VramAvailableGb = Math.Round(freeGb, 1);
                metrics.VramUsedGb = Math.Round(usedGb, 1);
                metrics.VramUsagePercent = Math.Round(percent, 1);
            }

            // 4. Power Usage (in milliwatts)
            if (Imports.DeviceGetPowerUsage(_deviceHandle, out uint powerMilliWatts) == NvmlReturn.Success)
            {
                double watts = powerMilliWatts / 1000.0;
                // Guard against known NVIDIA mobile Runtime D3 sleep glitch (reports ~590,000 mW / 590W in D3 state)
                if ((watts >= 450.0 && watts <= 620.0) ||
                    (watts > 300.0 && (metrics.GpuUsagePercent.GetValueOrDefault(0) < 15 || metrics.GpuTemperatureC.GetValueOrDefault(0) < 55)))
                {
                    metrics.GpuPowerWatts = 0.0;
                }
                else if (watts > 0.0 && watts <= 650.0)
                {
                    metrics.GpuPowerWatts = Math.Round(watts, 1);
                }
                else
                {
                    metrics.GpuPowerWatts = 0.0;
                }
            }

            // 5. Graphics Clock (type 0 = NVML_CLOCK_GRAPHICS, type 1 = NVML_CLOCK_SM)
            uint clockMhz = 0;
            bool clockRead = (Imports.DeviceGetClockInfo(_deviceHandle, 0, out clockMhz) == NvmlReturn.Success);
            if (!clockRead || clockMhz == 0)
            {
                clockRead = (Imports.DeviceGetClockInfo(_deviceHandle, 1, out clockMhz) == NvmlReturn.Success);
            }

            if (clockRead && clockMhz > 0)
            {
                // Sanity check for mobile Optimus / low-power P-State sleep:
                // On NVIDIA laptops (e.g. RTX 4070 Laptop), when the GPU is in deep idle / RTD3 sleep
                // drawing ~1.2W with 0% load, the hardware PLL is powered off, and nvmlDeviceGetClockInfo
                // frequently returns the static VBIOS Boost clock target (2175 MHz).
                // If the GPU is in idle P-State (P8/P5/P15) or drawing <= 5W with 0% load,
                // the true operating clock is the base idle clock (~210 MHz).
                uint pState = 0;
                bool hasPState = Imports.DeviceGetPerformanceState(_deviceHandle, out pState) == NvmlReturn.Success;

                double watts = metrics.GpuPowerWatts.GetValueOrDefault(0);
                double usage = metrics.GpuUsagePercent.GetValueOrDefault(0);

                if (clockMhz >= 1800 && (usage < 2.0 && watts <= 5.0 || (hasPState && pState >= 8)))
                {
                    metrics.GpuClockMhz = 210;
                }
                else
                {
                    metrics.GpuClockMhz = clockMhz;
                }
            }

            // 6. Fan Speed & Fans Count (Real physical hardware tachometers)
            if (Imports.DeviceGetNumFans(_deviceHandle, out uint numFans) == NvmlReturn.Success && numFans > 0)
            {
                uint overallSpeed = 0;
                Imports.DeviceGetFanSpeed(_deviceHandle, out overallSpeed);

                for (uint f = 0; f < numFans; f++)
                {
                    uint fanSpeed = overallSpeed;
                    if (Imports.DeviceGetFanSpeed_v2(_deviceHandle, f, out uint indSpeed) == NvmlReturn.Success)
                    {
                        fanSpeed = indSpeed;
                    }

                    var info = new Imports.NvmlFanSpeedInfo
                    {
                        Version = (uint)(Marshal.SizeOf<Imports.NvmlFanSpeedInfo>() | (1 << 24)),
                        Fan = f,
                        Speed = 0
                    };

                    double? fanRpm = null;
                    if (Imports.DeviceGetFanSpeedRPM(_deviceHandle, ref info) == NvmlReturn.Success)
                    {
                        fanRpm = info.Speed;
                    }
                    else if (fanSpeed == 0 && metrics.GpuTemperatureC.GetValueOrDefault(0) < 55)
                    {
                        fanRpm = 0.0;
                    }

                    string fanNum = (f + 1).ToString();
                    string fanName = $"GPU Fan {fanNum}";
                    string fanDisplayName = numFans == 1 ? "Вентилятор видеокарты" : $"Вентилятор GPU #{fanNum}";
                    string fanDesc = f == 0
                        ? "Отвод тепла от графического процессора (GPU Core)"
                        : (f == 1 ? "Обдув видеопамяти и подсистемы питания (VRM)" : "Сквозной обдув радиатора видеокарты");

                    metrics.Fans.Add(new FanMetric
                    {
                        Id = $"nvml-gpu-fan-{f}",
                        Name = fanName,
                        DisplayName = fanDisplayName,
                        Role = "GPU",
                        Description = fanDesc,
                        HardwareDevice = _deviceName,
                        Percent = fanSpeed,
                        Rpm = fanRpm,
                        SensorSource = (fanRpm.HasValue && fanRpm.Value > 0) ? "NVML (Датчик)" : (fanSpeed == 0 ? "Zero-RPM" : "NVML")
                    });
                }
            }
        }
        catch
        {
            // Non-critical: leaves missing metrics as null
        }

        return Task.FromResult(metrics);
    }

    public void Dispose()
    {
        if (_isInitialized)
        {
            try
            {
                Imports.Shutdown();
            }
            catch
            {
                // Ignore shutdown error
            }
            _isInitialized = false;
            _deviceHandle = IntPtr.Zero;
        }
    }
}

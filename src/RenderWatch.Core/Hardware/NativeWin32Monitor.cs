using System.Runtime.InteropServices;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public class NativeWin32Monitor : IHardwareMonitor
{
    public string ProviderName => "Windows Native (Win32 API)";
    public bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_DOUBLE
    {
        public uint CStatus;
        public double doubleValue;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr lpInputBuffer,
        uint nInputBufferSize,
        IntPtr lpOutputBuffer,
        uint nOutputBufferSize);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE_DOUBLE pValue);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCloseQuery(IntPtr hQuery);

    private const int ProcessorInformation = 11;
    private const uint PDH_FMT_DOUBLE = 0x00000200;

    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private bool _hasPrevTimes;
    private readonly int _processorCount = Environment.ProcessorCount;

    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhPerfCounter = IntPtr.Zero;
    private bool _pdhInitialized;
    private double _baseCpuFrequencyGhz = 2.5;

    public Task InitializeAsync()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = idle.ToUInt64();
            _prevKernelTime = kernel.ToUInt64();
            _prevUserTime = user.ToUInt64();
            _hasPrevTimes = true;
        }

        // Initialize True Base CPU Frequency with multi-stage verification (Registry ~MHz, NtPower MaxMhz)
        _baseCpuFrequencyGhz = DetectTrueBaseCpuFrequencyGhz();

        // Initialize PDH for Dynamic Real-time CPU Frequency
        try
        {
            int res = PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery);
            if (res == 0)
            {
                res = PdhAddEnglishCounterW(_pdhQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _pdhPerfCounter);
                if (res == 0)
                {
                    PdhCollectQueryData(_pdhQuery);
                    _pdhInitialized = true;
                }
            }
        }
        catch
        {
            _pdhInitialized = false;
        }

        return Task.CompletedTask;
    }

    private static double DetectTrueBaseCpuFrequencyGhz()
    {
        // 1. Registry HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\~MHz (Kernel-calibrated exact base clock)
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("~MHz") is int mhz && mhz > 500)
            {
                return Math.Round(mhz / 1000.0, 2);
            }
        }
        catch { }

        // 2. CallNtPowerInformation info.MaxMhz (ACPI Hardware Base Rating across cores)
        try
        {
            int structSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            int count = Environment.ProcessorCount;
            int bufferSize = structSize * count;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, (uint)bufferSize);
                if (status == 0)
                {
                    double totalMaxMhz = 0;
                    int validCount = 0;
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr currentPtr = IntPtr.Add(buffer, i * structSize);
                        var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(currentPtr);
                        if (info.MaxMhz > 500)
                        {
                            totalMaxMhz += info.MaxMhz;
                            validCount++;
                        }
                    }

                    if (validCount > 0)
                    {
                        return Math.Round((totalMaxMhz / validCount) / 1000.0, 2);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch { }

        // 3. Fallback to reasonable 2.5 GHz
        return 2.5;
    }

    public Task<HardwareMetrics> GetMetricsAsync()
    {
        var metrics = new HardwareMetrics
        {
            Timestamp = DateTime.Now,
            SensorSourceDetails = ProviderName
        };

        // 1. CPU Usage
        if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            ulong currentIdle = idleTime.ToUInt64();
            ulong currentKernel = kernelTime.ToUInt64();
            ulong currentUser = userTime.ToUInt64();

            if (_hasPrevTimes)
            {
                ulong usrDelta = currentUser - _prevUserTime;
                ulong kerDelta = currentKernel - _prevKernelTime;
                ulong idlDelta = currentIdle - _prevIdleTime;

                ulong sysDelta = kerDelta + usrDelta;
                if (sysDelta > 0)
                {
                    double cpuLoad = (double)(sysDelta - idlDelta) * 100.0 / sysDelta;
                    metrics.CpuUsagePercent = Math.Clamp(cpuLoad, 0.0, 100.0);
                }
            }

            _prevIdleTime = currentIdle;
            _prevKernelTime = currentKernel;
            _prevUserTime = currentUser;
            _hasPrevTimes = true;
        }

        // 2. RAM Usage
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double availGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = totalGb - availGb;
            double percent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : memStatus.dwMemoryLoad;

            metrics.RamTotalGb = Math.Round(totalGb, 1);
            metrics.RamAvailableGb = Math.Round(availGb, 1);
            metrics.RamUsedGb = Math.Round(usedGb, 1);
            metrics.RamUsagePercent = Math.Round(percent, 1);
        }

        // 3. Dynamic Real-time CPU Frequency (PDH % Processor Performance * Base Clock)
        metrics.CpuFrequencyGhz = GetCpuFrequencyGhz();

        return Task.FromResult(metrics);
    }

    private double? GetCpuFrequencyGhz()
    {
        if (_pdhInitialized && _pdhPerfCounter != IntPtr.Zero)
        {
            try
            {
                int res = PdhCollectQueryData(_pdhQuery);
                if (res == 0)
                {
                    res = PdhGetFormattedCounterValue(_pdhPerfCounter, PDH_FMT_DOUBLE, out _, out var val);
                    if (res == 0 && val.doubleValue > 5.0)
                    {
                        // Real-time instantaneous operating clock (% Processor Performance * Base Clock)
                        double dynamicGhz = _baseCpuFrequencyGhz * (val.doubleValue / 100.0);
                        return Math.Round(Math.Clamp(dynamicGhz, 0.4, 7.5), 2);
                    }
                }
            }
            catch
            {
                // Fall through to nominal
            }
        }

        return GetNominalCpuFrequencyGhz();
    }

    private double? GetNominalCpuFrequencyGhz()
    {
        try
        {
            int structSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            int bufferSize = structSize * _processorCount;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                int status = CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, (uint)bufferSize);
                if (status == 0) // STATUS_SUCCESS
                {
                    double totalMhz = 0;
                    int count = 0;

                    for (int i = 0; i < _processorCount; i++)
                    {
                        IntPtr currentPtr = IntPtr.Add(buffer, i * structSize);
                        var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(currentPtr);
                        uint mhz = info.CurrentMhz > 500 ? info.CurrentMhz : info.MaxMhz;
                        if (mhz > 500)
                        {
                            totalMhz += mhz;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        return Math.Round((totalMhz / count) / 1000.0, 2);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Non-critical
        }

        return _baseCpuFrequencyGhz > 0.5 ? _baseCpuFrequencyGhz : null;
    }

    public void Dispose()
    {
        if (_pdhQuery != IntPtr.Zero)
        {
            try
            {
                PdhCloseQuery(_pdhQuery);
            }
            catch { }
            _pdhQuery = IntPtr.Zero;
            _pdhPerfCounter = IntPtr.Zero;
            _pdhInitialized = false;
        }
    }
}
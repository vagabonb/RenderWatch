using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public sealed class SystemActivityMonitor : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private IntPtr _query;
    private IntPtr _readCounter;
    private IntPtr _writeCounter;
    private IntPtr _diskReadArray;
    private IntPtr _diskWriteArray;
    private IReadOnlyList<DiskActivity> _disks = Array.Empty<DiskActivity>();
    private long _lastSampleTimestamp;
    private Dictionary<string, (long Received, long Sent)> _previousAdapters = new();
    private (double? DiskRead, double? DiskWrite, double? NetworkReceive, double? NetworkSend, bool NetworkOnline, bool NetworkAvailable) _last;

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    public SystemActivityMonitor()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
            if (PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _readCounter) != 0 ||
                PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _writeCounter) != 0)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            if (_query != IntPtr.Zero)
            {
                PdhAddEnglishCounter(_query, @"\PhysicalDisk(*)\Disk Read Bytes/sec", IntPtr.Zero, out _diskReadArray);
                PdhAddEnglishCounter(_query, @"\PhysicalDisk(*)\Disk Write Bytes/sec", IntPtr.Zero, out _diskWriteArray);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    public void Update(HardwareMetrics result)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = _lastSampleTimestamp == 0 ? 0 : (now - _lastSampleTimestamp) / (double)Stopwatch.Frequency;
        if (_lastSampleTimestamp == 0 || elapsed >= 1)
        {
            double? diskRead = null, diskWrite = null;
            _disks = Array.Empty<DiskActivity>();
            if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0 && elapsed > 0)
            {
                diskRead = ReadCounter(_readCounter);
                diskWrite = ReadCounter(_writeCounter);
                var reads = ReadInstances(_diskReadArray);
                var writes = ReadInstances(_diskWriteArray);
                _disks = reads.Keys.Union(writes.Keys)
                    .Where(name => !string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => new DiskActivity(name,
                        reads.TryGetValue(name, out var read) ? read : null,
                        writes.TryGetValue(name, out var write) ? write : null))
                    .ToArray();
            }

            double? receive = null, send = null;
            bool online = false;
            bool networkAvailable = false;
            try
            {
                var current = new Dictionary<string, (long Received, long Sent)>();
                networkAvailable = true;
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        continue;
                    try
                    {
                        var stats = adapter.GetIPStatistics();
                        current[adapter.Id] = (stats.BytesReceived, stats.BytesSent);
                    }
                    catch (NetworkInformationException) { }
                }
                online = current.Count > 0;
                if (elapsed > 0 && online)
                {
                    long receivedDelta = 0, sentDelta = 0;
                    bool haveBaseline = false;
                    foreach (var item in current)
                    {
                        if (!_previousAdapters.TryGetValue(item.Key, out var previous)) continue;
                        if (item.Value.Received < previous.Received || item.Value.Sent < previous.Sent) continue;
                        receivedDelta += item.Value.Received - previous.Received;
                        sentDelta += item.Value.Sent - previous.Sent;
                        haveBaseline = true;
                    }
                    if (haveBaseline)
                    {
                        receive = receivedDelta / elapsed;
                        send = sentDelta / elapsed;
                    }
                }
                _previousAdapters = current;
            }
            catch (NetworkInformationException)
            {
                networkAvailable = false;
                _previousAdapters.Clear();
            }
            _last = (diskRead, diskWrite, receive, send, online, networkAvailable);
            _lastSampleTimestamp = now;
        }

        result.PhysicalDisks = _disks;
        result.DiskReadBytesPerSecond = _last.DiskRead;
        result.DiskWriteBytesPerSecond = _last.DiskWrite;
        result.NetworkReceiveBytesPerSecond = _last.NetworkReceive;
        result.NetworkSendBytesPerSecond = _last.NetworkSend;
        result.NetworkOnline = _last.NetworkOnline;
        result.NetworkAvailable = _last.NetworkAvailable;
        result.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private static Dictionary<string, double?> ReadInstances(IntPtr counter)
    {
        var result = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        if (counter == IntPtr.Zero) return result;
        uint size = 0, count = 0;
        const uint moreData = 0x800007D2;
        if (PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, ref count, IntPtr.Zero) != moreData || size == 0)
            return result;
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, ref count, buffer) != 0)
                return result;
            int stride = Marshal.SizeOf<CounterItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, checked(i * stride)));
                string? name = Marshal.PtrToStringUni(item.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                double number = item.Value.Value;
                result[name] = item.Value.Status <= 1 && double.IsFinite(number) && number >= 0 ? number : null;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static double? ReadCounter(IntPtr counter)
    {
        if (PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out CounterValue value) != 0 ||
            value.Status > 1 || !double.IsFinite(value.Value) || value.Value < 0)
            return null;
        return value.Value;
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }
}
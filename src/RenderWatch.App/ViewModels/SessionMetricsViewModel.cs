using System.Collections.ObjectModel;
using System.Globalization;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public class SessionMetricsViewModel : ViewModelBase
{
    public ObservableCollection<double> CpuLoad { get; } = new();
    public ObservableCollection<double> GpuLoad { get; } = new();
    public ObservableCollection<double> CpuTemp { get; } = new();
    public ObservableCollection<double> GpuTemp { get; } = new();
    public ObservableCollection<double> Ram { get; } = new();
    public ObservableCollection<double> Vram { get; } = new();
    public ObservableCollection<double> NetworkReceive { get; } = new();
    public ObservableCollection<double> NetworkSend { get; } = new();
    public TransferHistory DiskRead { get; } = new();
    public TransferHistory DiskWrite { get; } = new();
    private double _networkReceiveScale = TransferHistory.InitialScale;
    private double _networkSendScale = TransferHistory.InitialScale;
    public double NetworkReceiveScale
    {
        get => _networkReceiveScale;
        private set { if (SetField(ref _networkReceiveScale, value)) OnPropertyChanged(nameof(NetworkReceiveScaleDisplay)); }
    }
    public double NetworkSendScale
    {
        get => _networkSendScale;
        private set { if (SetField(ref _networkSendScale, value)) OnPropertyChanged(nameof(NetworkSendScaleDisplay)); }
    }
    public string NetworkReceiveScaleDisplay => FormatNetworkScale(NetworkReceiveScale);
    public string NetworkSendScaleDisplay => FormatNetworkScale(NetworkSendScale);

    private static string FormatNetworkScale(double scale) => scale >= 1024 * 1024
        ? $"Шкала до {scale / (1024 * 1024):N0} МБ/с"
        : $"Шкала до {scale / 1024:N0} КБ/с";

    private double? _cpuLoadMax, _gpuLoadMax, _cpuTempMax, _gpuTempMax, _ramMax, _vramMax;
    public string CpuLoadMax => Format(_cpuLoadMax, "%");
    public string GpuLoadMax => Format(_gpuLoadMax, "%");
    public string CpuTempMax => Format(_cpuTempMax, "°C");
    public string GpuTempMax => Format(_gpuTempMax, "°C");
    public string RamMax => Format(_ramMax, "%");
    public string VramMax => Format(_vramMax, "%");

    private static string Format(double? value, string unit) =>
        value.HasValue ? value.Value.ToString("F0", CultureInfo.InvariantCulture) + unit : "—";

    private void Add(ObservableCollection<double> points, double? value, ref double? maximum, string name)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return;
        AppendPoint(points, value.Value);
        if (!maximum.HasValue || value.Value > maximum.Value)
        {
            maximum = value;
            OnPropertyChanged(name);
        }
    }

    private static void AppendPoint(ObservableCollection<double> points, double value)
    {
        points.Add(value);
        if (points.Count > 120) points.RemoveAt(0);
    }

    public void Update(HardwareMetrics metrics)
    {
        Add(CpuLoad, metrics.CpuUsagePercent, ref _cpuLoadMax, nameof(CpuLoadMax));
        Add(GpuLoad, metrics.GpuUsagePercent, ref _gpuLoadMax, nameof(GpuLoadMax));
        Add(CpuTemp, metrics.CpuTemperatureC > 5 ? metrics.CpuTemperatureC : null, ref _cpuTempMax, nameof(CpuTempMax));
        Add(GpuTemp, metrics.GpuTemperatureC > 5 ? metrics.GpuTemperatureC : null, ref _gpuTempMax, nameof(GpuTempMax));
        Add(Ram, metrics.RamUsagePercent, ref _ramMax, nameof(RamMax));
        Add(Vram, metrics.VramUsagePercent, ref _vramMax, nameof(VramMax));
        NetworkReceiveScale = TransferHistory.Append(NetworkReceive, metrics.NetworkReceiveBytesPerSecond, NetworkReceiveScale);
        NetworkSendScale = TransferHistory.Append(NetworkSend, metrics.NetworkSendBytesPerSecond, NetworkSendScale);
        DiskRead.Add(metrics.DiskReadBytesPerSecond);
        DiskWrite.Add(metrics.DiskWriteBytesPerSecond);
    }
}

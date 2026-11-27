using System.Windows.Media;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public sealed record NotchReading(NotchRingMetric Metric, string Text, double Fill, string Hint, SolidColorBrush? Accent = null)
{
    internal static readonly NotchReading Empty = new(NotchRingMetric.None, "", 0, "");
    private static readonly string[] RateUnits = ["Б", "К", "М", "Г", "Т"];
    public bool IsSet => Metric != NotchRingMetric.None;

    internal static NotchReading Create(NotchRingMetric metric, HardwareMetrics metrics, BatteryViewModel battery, SessionMetricsViewModel history)
    {
        if (metric == NotchRingMetric.None) return Empty;

        string title = DisplayConstructorViewModel.GetNotchMetricTitle(metric);
        return metric switch
        {
            NotchRingMetric.CpuLoad => Display(metric, title, metrics.CpuUsagePercent),
            NotchRingMetric.GpuLoad => Display(metric, title, metrics.GpuUsagePercent),
            NotchRingMetric.Ram => Display(metric, title, metrics.RamUsagePercent),
            NotchRingMetric.Vram => Display(metric, title, metrics.VramUsagePercent),
            NotchRingMetric.CpuTemperature => Display(metric, title, metrics.CpuTemperatureC, temperature: true),
            NotchRingMetric.GpuTemperature => Display(metric, title, metrics.GpuTemperatureC, temperature: true),
            NotchRingMetric.Battery => new(metric, battery.ChargeText, battery.RingFill, $"{title} · {battery.ChargeText}", battery.AccentBrush),
            NotchRingMetric.DiskRead => Transfer(metric, title, metrics.DiskReadBytesPerSecond, history.DiskRead.Scale),
            NotchRingMetric.DiskWrite => Transfer(metric, title, metrics.DiskWriteBytesPerSecond, history.DiskWrite.Scale),
            NotchRingMetric.NetworkReceive when metrics.NetworkOnline =>
                Transfer(metric, title, metrics.NetworkReceiveBytesPerSecond, history.NetworkReceiveScale),
            NotchRingMetric.NetworkSend when metrics.NetworkOnline =>
                Transfer(metric, title, metrics.NetworkSendBytesPerSecond, history.NetworkSendScale),
            _ => Unavailable(metric, title)
        };
    }

    private static NotchReading Display(NotchRingMetric metric, string title, double? number, bool temperature = false)
    {
        if (number is not double value || !double.IsFinite(value) || value < 0 || (temperature && value <= 5))
            return Unavailable(metric, title);
        double fill = Math.Min(value, 100);
        string text = temperature ? $"{value:0}°" : $"{fill:0}%";
        return new(metric, text, fill, $"{title} · {text}{(temperature ? "C" : "")}");
    }

    private static NotchReading Transfer(NotchRingMetric metric, string title, double? bytesPerSecond, double scale)
    {
        if (bytesPerSecond is not double rate || !double.IsFinite(rate) || rate < 0)
            return Unavailable(metric, title);
        scale = TransferHistory.ExpandScale(scale, rate);
        return new(metric, FormatRate(rate, compact: true), Math.Min(100, rate / scale * 100),
            $"{title} · {FormatRate(rate)} · до {FormatRate(scale)}");
    }

    private static NotchReading Unavailable(NotchRingMetric metric, string title) => new(metric, "—", 0, $"{title} · нет данных");

    private static string FormatRate(double rate, bool compact = false)
    {
        int unit = 0;
        double promotionThreshold = compact ? 999.5 : 1024;
        while (rate >= promotionThreshold && unit < RateUnits.Length - 1)
        {
            rate /= 1024;
            unit++;
        }

        if (compact) return $"{rate.ToString(unit > 0 && rate < 9.95 ? "0.#" : "0")}{RateUnits[unit]}";
        string suffix = unit == 0 ? "Б" : RateUnits[unit] + "Б";
        return $"{rate:0.##} {suffix}/с";
    }
}

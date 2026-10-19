using System.Globalization;

namespace RenderWatch.Core.Models;

public class FanMetric
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HardwareDevice { get; set; } = string.Empty;
    public double? Rpm { get; set; }
    public double? Percent { get; set; }

    public string? SensorSource { get; set; }

    public bool IsActive => (Rpm.HasValue && Rpm.Value > 0) || (Percent.HasValue && Percent.Value > 0);

    public string RpmDisplay
    {
        get
        {
            if (Rpm.HasValue)
            {
                if (Rpm.Value > 0)
                {
                    return $"{Math.Round(Rpm.Value):N0} об/мин";
                }
                return "0 об/мин";
            }
            if (Percent.HasValue)
            {
                return $"{Math.Round(Percent.Value):F0}%";
            }
            return "0 об/мин";
        }
    }

    public string StatusText => IsActive
        ? ((!string.IsNullOrEmpty(SensorSource) && SensorSource.Contains("Датчик")) ? "Активен (Датчик)" : "Активен")
        : "Пассивный (Zero-RPM)";

    public static bool IsTripleFanGpu(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        string g = gpuName.ToLowerInvariant();

        if (g.Contains("laptop") || g.Contains("mobile") || g.Contains("max-q"))
            return false;

        if (g.Contains("5070") || g.Contains("5080") || g.Contains("5090") ||
            g.Contains("4070") || g.Contains("4080") || g.Contains("4090") ||
            g.Contains("3080") || g.Contains("3090") || g.Contains("3070 ti") ||
            g.Contains("7900") || g.Contains("7800 xt") || g.Contains("6950") || g.Contains("6900"))
        {
            return true;
        }

        if (g.Contains("3x") || g.Contains("trio") || g.Contains("trinity") ||
            g.Contains("strix") || g.Contains("tuf") || g.Contains("aorus") ||
            g.Contains("suprim") || g.Contains("phantom") || g.Contains("gamerock") ||
            g.Contains("amp extreme") || g.Contains("master"))
        {
            return true;
        }

        return false;
    }
}

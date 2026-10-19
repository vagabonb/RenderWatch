using System.Globalization;

namespace RenderWatch.Core.Models;

public class HardwareMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public double? CpuUsagePercent { get; set; }
    public double? CpuTemperatureC { get; set; }
    public double? CpuMaxTemperatureC { get; set; }
    public double? CpuBaseTemperatureC { get; set; }
    public bool IsElevatedAdmin { get; set; }
    public double? CpuFrequencyGhz { get; set; }
    public double? CpuPowerWatts { get; set; }

    public string? GpuName { get; set; }
    public double? GpuUsagePercent { get; set; }
    public double? GpuTemperatureC { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuPowerWatts { get; set; }

    public double? RamUsedGb { get; set; }
    public double? RamAvailableGb { get; set; }
    public double? RamTotalGb { get; set; }
    public double? RamUsagePercent { get; set; }

    public double? VramUsedGb { get; set; }
    public double? VramAvailableGb { get; set; }
    public double? VramTotalGb { get; set; }
    public double? VramUsagePercent { get; set; }

    public string SensorSourceDetails { get; set; } = string.Empty;

    public List<FanMetric> Fans { get; set; } = new();
    public bool IsLiquidCooling { get; set; }
    public int TotalFansCount => Fans.Count;
    public int ActiveFansCount => Fans.Count(f => f.IsActive);
    public string FansSummaryDisplay
    {
        get
        {
            if (TotalFansCount == 0) return "Определение...";
            int passiveCount = TotalFansCount - ActiveFansCount;
            if (passiveCount > 0)
                return $"{TotalFansCount} шт. ({ActiveFansCount} активны, {passiveCount} пассивны)";
            return $"{TotalFansCount} шт. ({ActiveFansCount} активны)";
        }
    }

    public string CpuUsageDisplay => CpuUsagePercent.HasValue ? $"{Math.Round(CpuUsagePercent.Value).ToString("F0", CultureInfo.InvariantCulture)}%" : "Недоступно";
    public string CpuTempDisplay => CpuTemperatureC.HasValue && CpuTemperatureC.Value > 5.0 ? $"{CpuTemperatureC.Value.ToString("F0", CultureInfo.InvariantCulture)}°C" : "Недоступно";
    public string CpuClockDisplay => CpuFrequencyGhz.HasValue ? $"{CpuFrequencyGhz.Value.ToString("F2", CultureInfo.InvariantCulture)} ГГц" : "Недоступно";
    public string CpuPowerDisplay => CpuPowerWatts.HasValue && CpuPowerWatts.Value > 1.0 ? $"{CpuPowerWatts.Value.ToString("F1", CultureInfo.InvariantCulture)} Вт" : "Недоступно";

    public string GpuUsageDisplay => GpuUsagePercent.HasValue ? $"{Math.Round(GpuUsagePercent.Value).ToString("F0", CultureInfo.InvariantCulture)}%" : "Недоступно";
    public string GpuTempDisplay => GpuTemperatureC.HasValue ? $"{GpuTemperatureC.Value.ToString("F0", CultureInfo.InvariantCulture)}°C" : "Недоступно";
    public string GpuClockDisplay => GpuClockMhz.HasValue ? $"{GpuClockMhz.Value.ToString("F0", CultureInfo.InvariantCulture)} МГц" : "Недоступно";
    public string GpuPowerDisplay => GpuPowerWatts.HasValue ? $"{GpuPowerWatts.Value.ToString("F1", CultureInfo.InvariantCulture)} Вт" : "Недоступно";

    public string RamDisplay => RamUsedGb.HasValue && RamTotalGb.HasValue
        ? $"{RamUsedGb.Value.ToString("F1", CultureInfo.InvariantCulture)} / {RamTotalGb.Value.ToString("F1", CultureInfo.InvariantCulture)} ГБ ({RamUsagePercent.GetValueOrDefault().ToString("F0", CultureInfo.InvariantCulture)}%)"
        : "Недоступно";

    public string RamCompactDisplay => RamUsedGb.HasValue
        ? $"{RamUsedGb.Value.ToString("F1", CultureInfo.InvariantCulture)} ГБ ({RamUsagePercent.GetValueOrDefault().ToString("F0", CultureInfo.InvariantCulture)}%)"
        : "Недоступно";

    public string VramDisplay => VramUsedGb.HasValue && VramTotalGb.HasValue
        ? $"{VramUsedGb.Value.ToString("F1", CultureInfo.InvariantCulture)} / {VramTotalGb.Value.ToString("F1", CultureInfo.InvariantCulture)} ГБ ({VramUsagePercent.GetValueOrDefault().ToString("F0", CultureInfo.InvariantCulture)}%)"
        : "Недоступно";

    public string CpuTempMicro => CpuTemperatureC.HasValue && CpuTemperatureC.Value > 5.0
        ? $"{CpuTemperatureC.Value.ToString("F0", CultureInfo.InvariantCulture)}°"
        : "—";

    public string CpuUsageMicro => CpuUsagePercent.HasValue
        ? $"{Math.Round(CpuUsagePercent.Value).ToString("F0", CultureInfo.InvariantCulture)}%"
        : "0%";

    public string GpuTempMicro => GpuTemperatureC.HasValue && GpuTemperatureC.Value > 5.0
        ? $"{GpuTemperatureC.Value.ToString("F0", CultureInfo.InvariantCulture)}°"
        : "—";

    public string GpuUsageMicro => GpuUsagePercent.HasValue
        ? $"{Math.Round(GpuUsagePercent.Value).ToString("F0", CultureInfo.InvariantCulture)}%"
        : "0%";

    public string RamPercentDisplay => RamUsagePercent.HasValue
        ? $"{Math.Round(RamUsagePercent.Value).ToString("F0", CultureInfo.InvariantCulture)}%"
        : "0%";
}
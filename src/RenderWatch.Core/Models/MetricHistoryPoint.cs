namespace RenderWatch.Core.Models;

public record MetricHistoryPoint(
    DateTime Timestamp,
    double? CpuUsage,
    double? GpuUsage,
    double? CpuTemp,
    double? GpuTemp,
    double? RamUsage,
    double? VramUsage
);
namespace RenderWatch.Core.Models;

public class RenderSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SceneName { get; set; } = "Безымянная сцена";
    public string AppName { get; set; } = "Неизвестно";
    public string ProfileName { get; set; } = "По умолчанию";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public RenderStatus FinalStatus { get; set; } = RenderStatus.Idle;
    public string StatusDetectionSource { get; set; } = string.Empty;

    public double? AvgCpuUsage { get; set; }
    public double? PeakCpuUsage { get; set; }
    public double? AvgGpuUsage { get; set; }
    public double? PeakGpuUsage { get; set; }
    public double? MaxCpuTemp { get; set; }
    public double? MaxGpuTemp { get; set; }
    public double? PeakRamGb { get; set; }
    public double? PeakVramGb { get; set; }

    public string UserNotes { get; set; } = string.Empty;

    public string FormattedDuration => Duration.ToString(@"hh\:mm\:ss");
    public string FormattedDate => StartTime.ToString("yyyy-MM-dd HH:mm:ss");
}
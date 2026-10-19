using RenderWatch.Core.Models;

namespace RenderWatch.Core.Tracking;

public class TrackerUpdateResult
{
    public RenderStatus Status { get; set; } = RenderStatus.Idle;
    public string AppName { get; set; } = "Не обнаружено";
    public string SceneName { get; set; } = "Безымянная сцена";
    public string StatusDetectionSource { get; set; } = "Нет активных процессов";
    public DateTime? StartTime { get; set; }
    public TimeSpan ElapsedDuration { get; set; } = TimeSpan.Zero;
    public RenderSession? CompletedSession { get; set; }
    public bool StatusChanged { get; set; }
    public string? StatusChangeReason { get; set; }
}

public interface IRenderTracker
{
    RenderProfile CurrentProfile { get; set; }
    RenderStatus CurrentStatus { get; }
    TrackerUpdateResult Update(HardwareMetrics currentMetrics, int hangTimeoutSec);
    void ForceCompleteCurrentSession(string reason);
}
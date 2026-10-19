namespace RenderWatch.Core.Models;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Alert
}

public class NotificationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public NotificationType Type { get; set; } = NotificationType.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
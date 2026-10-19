using RenderWatch.Core.Models;

namespace RenderWatch.Core.Notifications;

public interface INotificationService
{
    event Action<NotificationEvent>? OnNotification;

    void Notify(NotificationType type, string title, string message, string? debounceKey = null, int debounceSeconds = 60);
    void NotifyRenderCompleted(string appName, string sceneName, TimeSpan duration);
    void NotifyHangDetected(string appName, int hangTimeoutSec);
    void NotifyProcessCrashed(string appName);
    void NotifyThresholdExceeded(string metricName, double currentValue, double threshold, string unit);
    void NotifySensorUnavailable(string sensorName);
}

public class NotificationService : INotificationService
{
    public event Action<NotificationEvent>? OnNotification;

    private readonly Dictionary<string, DateTime> _debounceMap = new();
    private readonly object _lock = new();

    public void Notify(NotificationType type, string title, string message, string? debounceKey = null, int debounceSeconds = 60)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            if (!string.IsNullOrEmpty(debounceKey))
            {
                if (_debounceMap.TryGetValue(debounceKey, out var lastTime))
                {
                    if ((now - lastTime).TotalSeconds < debounceSeconds)
                    {
                        return; // Suppressed by cooldown
                    }
                }
                _debounceMap[debounceKey] = now;
            }

            var evt = new NotificationEvent
            {
                Type = type,
                Title = title,
                Message = message,
                Timestamp = now
            };

            OnNotification?.Invoke(evt);
        }
    }

    public void NotifyRenderCompleted(string appName, string sceneName, TimeSpan duration)
    {
        Notify(
            NotificationType.Success,
            "Рендер завершён",
            $"{appName}: сцена \"{sceneName}\" завершена за {duration:hh\\:mm\\:ss}",
            debounceKey: "render_completed_" + sceneName,
            debounceSeconds: 30
        );
    }

    public void NotifyHangDetected(string appName, int hangTimeoutSec)
    {
        Notify(
            NotificationType.Warning,
            "Подозрение на зависание",
            $"Приложение {appName} не отвечает более {hangTimeoutSec} сек. Проверьте состояние процесса.",
            debounceKey: "hang_" + appName,
            debounceSeconds: 120
        );
    }

    public void NotifyProcessCrashed(string appName)
    {
        Notify(
            NotificationType.Alert,
            "Процесс неожиданно завершён",
            $"Рендер-процесс {appName} завершился некорректно.",
            debounceKey: "crash_" + appName,
            debounceSeconds: 30
        );
    }

    public void NotifyThresholdExceeded(string metricName, double currentValue, double threshold, string unit)
    {
        Notify(
            NotificationType.Alert,
            $"Превышен порог {metricName}",
            $"Текущее значение: {currentValue:F1}{unit} (порог: {threshold:F1}{unit})",
            debounceKey: "threshold_" + metricName,
            debounceSeconds: 90
        );
    }

    public void NotifySensorUnavailable(string sensorName)
    {
        Notify(
            NotificationType.Info,
            "Датчик недоступен",
            $"Источник показателей {sensorName} не отвечает или требует повышенных прав доступа.",
            debounceKey: "sensor_na_" + sensorName,
            debounceSeconds: 300
        );
    }
}
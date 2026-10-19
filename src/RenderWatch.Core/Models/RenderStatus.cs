namespace RenderWatch.Core.Models;

public enum RenderStatus
{
    Idle,
    Rendering,
    PossiblyCompleted,
    Completed,
    PossiblyFrozen,
    InsufficientData
}

public static class RenderStatusExtensions
{
    public static string ToDisplayName(this RenderStatus status) => status switch
    {
        RenderStatus.Idle => "Ожидание",
        RenderStatus.Rendering => "Идёт рендер",
        RenderStatus.PossiblyCompleted => "Возможно завершен",
        RenderStatus.Completed => "Завершен",
        RenderStatus.PossiblyFrozen => "Возможно завис",
        RenderStatus.InsufficientData => "Недостаточно данных",
        _ => "Неизвестно"
    };

    public static string GetStatusColorHex(this RenderStatus status) => status switch
    {
        RenderStatus.Idle => "#8A909D",              // Slate gray
        RenderStatus.Rendering => "#FFFFFF",         // Crisp white
        RenderStatus.PossiblyCompleted => "#D1D5DB", // Light silver
        RenderStatus.Completed => "#F3F4F6",         // Bright white
        RenderStatus.PossiblyFrozen => "#9CA3AF",     // Silver warning
        RenderStatus.InsufficientData => "#6B7280",  // Dark gray
        _ => "#8A909D"
    };
}
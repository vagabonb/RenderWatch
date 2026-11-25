namespace RenderWatch.Core.Models;

public sealed class TaskbarAppearanceSettings
{
    public string? Order { get; set; }
    public int BlockGap { get; set; } = 6;
    public int LabelGap { get; set; } = 4;
    public int ValueGap { get; set; } = 2;
    public int SidePadding { get; set; }
    public Dictionary<string, TaskbarMetricSettings> Metrics { get; set; } = new();
}

public sealed class TaskbarMetricSettings
{
    public bool UseText { get; set; }
    public string LabelColor { get; set; } = string.Empty;
    public string ValueColor { get; set; } = string.Empty;
    public string TemperatureColor { get; set; } = string.Empty;
}

public sealed class CompactGraphSetting
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

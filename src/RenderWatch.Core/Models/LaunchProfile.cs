namespace RenderWatch.Core.Models;

public class LaunchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AssociatedProfileId { get; set; } = "auto";
    public DateTime? LastLaunchedAt { get; set; }
}
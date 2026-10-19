namespace RenderWatch.Core.Models;

public class AppSettings
{
    public double PollingIntervalIdleSec { get; set; } = 0.5;
    public double PollingIntervalRenderingSec { get; set; } = 0.5;
    public double PollingIntervalBackgroundSec { get; set; } = 8.0;

    public double CpuTempThresholdC { get; set; } = 88.0;
    public double GpuTempThresholdC { get; set; } = 83.0;
    public double RamThresholdPercent { get; set; } = 92.0;
    public double VramThresholdPercent { get; set; } = 92.0;
    public int HangTimeoutSec { get; set; } = 60;

    public int GraphHistoryMinutes { get; set; } = 5;
    public bool ShowCpuGraph { get; set; } = true;
    public bool ShowGpuGraph { get; set; } = true;
    public bool ShowCpuTempGraph { get; set; } = true;
    public bool ShowGpuTempGraph { get; set; } = true;
    public bool ShowRamGraph { get; set; } = true;
    public bool ShowVramGraph { get; set; } = true;

    public bool EnableNotifications { get; set; } = true;
    public bool NotifyOnCompleted { get; set; } = true;
    public bool NotifyOnHang { get; set; } = true;
    public bool NotifyOnCrash { get; set; } = true;
    public bool NotifyOnThresholdExceeded { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool IsTaskbarStripLightMode { get; set; } = false;

    public string ActiveProfileId { get; set; } = "auto";
    public string CoolingType { get; set; } = "auto";
    public List<RenderProfile> Profiles { get; set; } = RenderProfile.GetDefaultProfiles();
    public List<LaunchProfile> LaunchProfiles { get; set; } = new();
}
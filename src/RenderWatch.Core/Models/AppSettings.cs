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
    public string Appearance { get; set; } = "Black";
    public AppearanceLayout BlackLayout { get; set; } = new();
    public AppearanceLayout GraphiteLayout { get; set; } = new();
    public AppearanceLayout ColorLayout { get; set; } = new();

    public bool IsSidebarCollapsed { get; set; }
    public bool IsBlackSidebarCollapsed { get; set; }
    public bool IsColorSidebarCollapsed { get; set; }

    public bool IsTaskbarStripLightMode { get; set; } = false;

    public bool ShowTaskbarCpu { get; set; } = true;
    public bool ShowTaskbarGpu { get; set; } = true;
    public bool ShowTaskbarRam { get; set; } = true;
    public bool ShowTaskbarVram { get; set; } = false;
    public bool ShowTaskbarCpuTemp { get; set; } = true;
    public bool ShowTaskbarGpuTemp { get; set; } = true;
    public bool ShowMicroCpu { get; set; } = true;
    public bool ShowMicroGpu { get; set; } = true;
    public bool ShowMicroRam { get; set; } = true;
    public bool ShowMicroVram { get; set; } = false;
    public bool ShowMicroCpuTemp { get; set; } = true;
    public bool ShowMicroGpuTemp { get; set; } = true;
    public bool ShowCompactCpu { get; set; } = true;
    public bool ShowCompactGpu { get; set; } = true;
    public bool ShowCompactRam { get; set; } = true;
    public bool ShowCompactVram { get; set; } = true;
    public bool ShowCompactCpuTemp { get; set; } = true;
    public bool ShowCompactGpuTemp { get; set; } = true;

    public bool ShowTaskbarDisk { get; set; }
    public bool ShowTaskbarNetwork { get; set; }
    public bool ShowTaskbarUptime { get; set; }
    public bool ShowMicroDisk { get; set; }
    public bool ShowMicroNetwork { get; set; }
    public bool ShowMicroUptime { get; set; }
    public bool ShowCompactDisk { get; set; }
    public bool ShowCompactNetwork { get; set; }
    public bool ShowCompactUptime { get; set; }

    public string DisplayMetricOrder { get; set; } = "CPU,GPU,RAM,VRAM";
    public TaskbarAppearanceSettings TaskbarAppearance { get; set; } = new();
    public List<CompactGraphSetting>? CompactGraphs { get; set; }

    public string ActiveProfileId { get; set; } = "auto";
    public string CoolingType { get; set; } = "auto";
    public List<RenderProfile> Profiles { get; set; } = RenderProfile.GetDefaultProfiles();
    public List<LaunchProfile> LaunchProfiles { get; set; } = new();
}

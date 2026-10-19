using RenderWatch.Core.Models;
using RenderWatch.Core.Tracking;
using Xunit;

namespace RenderWatch.Tests;

public class ProcessTrackerTests
{
    [Fact]
    public void Tracker_InitialStateIsIdle()
    {
        var tracker = new RenderProcessTracker();
        Assert.Equal(RenderStatus.Idle, tracker.CurrentStatus);

        var metrics = new HardwareMetrics { CpuUsagePercent = 5.0, GpuUsagePercent = 2.0 };
        var result = tracker.Update(metrics, 60);

        Assert.Equal(RenderStatus.Idle, result.Status);
        Assert.Equal("Не обнаружено", result.AppName);
    }

    [Fact]
    public void Tracker_ProfileChangeResetsHangState()
    {
        var tracker = new RenderProcessTracker();
        var customProfile = new RenderProfile
        {
            Id = "custom",
            Name = "Custom Test Profile",
            MainProcessNames = new() { "notepad" }
        };

        tracker.CurrentProfile = customProfile;
        Assert.Equal("Custom Test Profile", tracker.CurrentProfile.Name);
    }

    [Fact]
    public void Tracker_ForceCompleteResetsSession()
    {
        var tracker = new RenderProcessTracker();
        tracker.ForceCompleteCurrentSession("Ручная отмена пользователем");
        Assert.Equal(RenderStatus.Idle, tracker.CurrentStatus);
    }

    [Fact]
    public void RenderSession_CalculatesFormattedDurationProperly()
    {
        var session = new RenderSession
        {
            Duration = new TimeSpan(1, 45, 23)
        };

        Assert.Equal("01:45:23", session.FormattedDuration);
    }
}
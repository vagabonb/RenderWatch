using RenderWatch.Core.History;
using RenderWatch.Core.Launcher;
using RenderWatch.Core.Models;
using RenderWatch.Core.Notifications;
using RenderWatch.Core.Settings;
using RenderWatch.Core.Tracking;
using Xunit;

namespace RenderWatch.Tests;

public class CoreTests
{
    [Fact]
    public void HardwareMetrics_FormatsUnavailableSensorsAsText_NeverZero()
    {
        var metrics = new HardwareMetrics
        {
            CpuUsagePercent = null,
            CpuTemperatureC = null,
            CpuFrequencyGhz = null,
            CpuPowerWatts = null,
            GpuUsagePercent = null,
            GpuTemperatureC = null,
            RamUsedGb = null,
            VramUsedGb = null
        };

        Assert.Equal("Недоступно", metrics.CpuUsageDisplay);
        Assert.Equal("Недоступно", metrics.CpuTempDisplay);
        Assert.Equal("Недоступно", metrics.CpuClockDisplay);
        Assert.Equal("Недоступно", metrics.CpuPowerDisplay);
        Assert.Equal("Недоступно", metrics.GpuUsageDisplay);
        Assert.Equal("Недоступно", metrics.GpuTempDisplay);
        Assert.Equal("Недоступно", metrics.RamDisplay);
        Assert.Equal("Недоступно", metrics.VramDisplay);
    }

    [Fact]
    public void HardwareMetrics_FormatsAvailableSensorsCorrectly()
    {
        var metrics = new HardwareMetrics
        {
            CpuUsagePercent = 42.56,
            CpuTemperatureC = 68.2,
            CpuFrequencyGhz = 4.85,
            CpuPowerWatts = 120.4,
            GpuUsagePercent = 99.0,
            GpuTemperatureC = 72.0,
            GpuClockMhz = 2550,
            GpuPowerWatts = 220.5,
            RamUsedGb = 24.5,
            RamTotalGb = 64.0,
            RamUsagePercent = 38.2,
            VramUsedGb = 10.2,
            VramTotalGb = 16.0,
            VramUsagePercent = 63.8
        };

        Assert.Equal("43%", metrics.CpuUsageDisplay);
        Assert.Equal("68°C", metrics.CpuTempDisplay);
        Assert.Equal("4.85 ГГц", metrics.CpuClockDisplay);
        Assert.Equal("120.4 Вт", metrics.CpuPowerDisplay);
        Assert.Equal("99%", metrics.GpuUsageDisplay);
        Assert.Equal("72°C", metrics.GpuTempDisplay);
        Assert.Equal("24.5 / 64.0 ГБ (38%)", metrics.RamDisplay);
        Assert.Equal("10.2 / 16.0 ГБ (64%)", metrics.VramDisplay);
    }

    [Fact]
    public void RenderStatusExtensions_ReturnsProperNamesAndColors()
    {
        Assert.Equal("Ожидание", RenderStatus.Idle.ToDisplayName());
        Assert.Equal("Идёт рендер", RenderStatus.Rendering.ToDisplayName());
        Assert.Equal("Возможно завершен", RenderStatus.PossiblyCompleted.ToDisplayName());
        Assert.Equal("Завершен", RenderStatus.Completed.ToDisplayName());
        Assert.Equal("Возможно завис", RenderStatus.PossiblyFrozen.ToDisplayName());
        Assert.Equal("Недостаточно данных", RenderStatus.InsufficientData.ToDisplayName());

        Assert.StartsWith("#", RenderStatus.Rendering.GetStatusColorHex());
        Assert.StartsWith("#", RenderStatus.Completed.GetStatusColorHex());
        Assert.StartsWith("#", RenderStatus.PossiblyFrozen.GetStatusColorHex());
    }

    [Fact]
    public void NotificationService_DebounceSuppressesDuplicates()
    {
        var service = new NotificationService();
        int receivedCount = 0;
        service.OnNotification += evt => receivedCount++;

        // Send 5 notifications with same debounceKey
        for (int i = 0; i < 5; i++)
        {
            service.Notify(NotificationType.Warning, "Test", "Msg", debounceKey: "test_key", debounceSeconds: 60);
        }

        Assert.Equal(1, receivedCount);

        // Different debounceKey triggers new notification
        service.Notify(NotificationType.Alert, "Other", "Msg2", debounceKey: "other_key", debounceSeconds: 60);
        Assert.Equal(2, receivedCount);
    }

    [Fact]
    public async Task JsonHistoryRepository_AddsUpdatesDeletesAndExports()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RenderWatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string jsonPath = Path.Combine(tempDir, "history.json");
        string csvPath = Path.Combine(tempDir, "export.csv");

        try
        {
            var repo = new JsonHistoryRepository(jsonPath);

            var session1 = new RenderSession
            {
                Id = "s1",
                SceneName = "Office_Interior.blend",
                AppName = "Blender",
                ProfileName = "Blender",
                StartTime = DateTime.Now.AddMinutes(-30),
                EndTime = DateTime.Now,
                Duration = TimeSpan.FromMinutes(30),
                FinalStatus = RenderStatus.Completed,
                AvgGpuUsage = 95.5,
                MaxGpuTemp = 74.0,
                UserNotes = "OptiX denoiser test"
            };

            await repo.AddSessionAsync(session1);
            var sessions = await repo.GetAllSessionsAsync();
            Assert.Single(sessions);
            Assert.Equal("Office_Interior.blend", sessions[0].SceneName);

            // Update
            session1.UserNotes = "Updated note";
            await repo.UpdateSessionAsync(session1);
            sessions = await repo.GetAllSessionsAsync();
            Assert.Equal("Updated note", sessions[0].UserNotes);

            // Export CSV
            await repo.ExportToCsvAsync(csvPath);
            Assert.True(File.Exists(csvPath));
            string csvContent = await File.ReadAllTextAsync(csvPath);
            Assert.Contains("Office_Interior.blend", csvContent);
            Assert.Contains("Updated note", csvContent);
            Assert.Contains("Duration", csvContent);

            // Delete
            await repo.DeleteSessionAsync("s1");
            sessions = await repo.GetAllSessionsAsync();
            Assert.Empty(sessions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task JsonSettingsManager_HandlesCorruptedFileGracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RenderWatchSettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string jsonPath = Path.Combine(tempDir, "settings.json");

        try
        {
            // Write corrupt JSON
            await File.WriteAllTextAsync(jsonPath, "{ corrupt json ::: 123 }");

            var manager = new JsonSettingsManager(jsonPath);
            var settings = await manager.LoadSettingsAsync();

            Assert.NotNull(settings);
            Assert.Equal(0.5, settings.PollingIntervalIdleSec);
            Assert.True(settings.Profiles.Count >= 3);
            Assert.True(File.Exists(jsonPath + ".corrupt.bak"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void AppLauncher_ValidatesPathCorrectly()
    {
        var launcher = new AppLauncher();

        // Empty path
        var res1 = launcher.Launch(new LaunchProfile { ExecutablePath = "" });
        Assert.False(res1.Success);

        // Non-existent path
        var res2 = launcher.Launch(new LaunchProfile { ExecutablePath = @"C:\NonExistentApp12345\fake.exe" });
        Assert.False(res2.Success);
        Assert.False(string.IsNullOrWhiteSpace(res2.Message));
    }
}
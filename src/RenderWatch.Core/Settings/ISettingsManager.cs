using RenderWatch.Core.Models;

namespace RenderWatch.Core.Settings;

public interface ISettingsManager
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    void ApplyStartupRegistry(bool enable);
}
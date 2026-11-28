using System.Text;
using System.Text.Json;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Settings;

public class JsonSettingsManager : ISettingsManager
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings? _cachedSettings;

    public JsonSettingsManager(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenderWatch");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "settings.json");
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null) return _cachedSettings;

            if (File.Exists(_filePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_filePath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        // Ensure built-in default profiles are present
                        EnsureDefaultProfiles(settings);
                        _cachedSettings = settings;
                        return settings;
                    }
                }
                catch
                {
                    try
                    {
                        File.Copy(_filePath, _filePath + ".corrupt.bak", overwrite: true);
                    }
                    catch { }
                }
            }

            _cachedSettings = new AppSettings();
            await SaveInternalAsync(_cachedSettings);
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _cachedSettings = settings;
            await SaveInternalAsync(settings);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(AppSettings settings)
    {
        string dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(settings, options);
        await File.WriteAllTextAsync(_filePath, json, Encoding.UTF8);
    }

    private static void EnsureDefaultProfiles(AppSettings settings)
    {
        var defaults = RenderProfile.GetDefaultProfiles();
        foreach (var def in defaults)
        {
            if (!settings.Profiles.Any(p => p.Id == def.Id))
            {
                settings.Profiles.Insert(0, def);
            }
        }
    }
}

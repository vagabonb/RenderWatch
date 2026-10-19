using System.Text;
using System.Text.Json;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.History;

public class JsonHistoryRepository : IHistoryRepository
{
    private readonly string _filePath;
    private readonly List<RenderSession> _sessions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isLoaded;

    public JsonHistoryRepository(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenderWatch");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "history.json");
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        if (File.Exists(_filePath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(_filePath, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<RenderSession>>(json);
                if (items != null)
                {
                    _sessions.Clear();
                    _sessions.AddRange(items);
                }
            }
            catch
            {
                // Backup corrupt file
                try
                {
                    File.Copy(_filePath, _filePath + ".corrupt.bak", overwrite: true);
                }
                catch { }
            }
        }

        _isLoaded = true;
    }

    private async Task SaveInternalAsync()
    {
        string dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(_sessions, options);
        await File.WriteAllTextAsync(_filePath, json, Encoding.UTF8);
    }

    public async Task<IReadOnlyList<RenderSession>> GetAllSessionsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return _sessions.OrderByDescending(s => s.StartTime).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddSessionAsync(RenderSession session)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            _sessions.Add(session);
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateSessionAsync(RenderSession session)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            int index = _sessions.FindIndex(s => s.Id == session.Id);
            if (index >= 0)
            {
                _sessions[index] = session;
                await SaveInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteSessionAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            int removed = _sessions.RemoveAll(s => s.Id == id);
            if (removed > 0)
            {
                await SaveInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            _sessions.Clear();
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExportToCsvAsync(string filePath, IEnumerable<RenderSession>? sessions = null)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            var targetList = sessions?.ToList() ?? _sessions.OrderByDescending(s => s.StartTime).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("ID,SceneName,AppName,ProfileName,StartTime,EndTime,Duration,FinalStatus,AvgCpuUsage,PeakCpuUsage,AvgGpuUsage,PeakGpuUsage,MaxCpuTemp,MaxGpuTemp,PeakRamGb,PeakVramGb,UserNotes");

            foreach (var s in targetList)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(s.Id),
                    EscapeCsv(s.SceneName),
                    EscapeCsv(s.AppName),
                    EscapeCsv(s.ProfileName),
                    EscapeCsv(s.StartTime.ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeCsv(s.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                    EscapeCsv(s.Duration.ToString(@"hh\:mm\:ss")),
                    EscapeCsv(s.FinalStatus.ToDisplayName()),
                    s.AvgCpuUsage?.ToString("F1") ?? "",
                    s.PeakCpuUsage?.ToString("F1") ?? "",
                    s.AvgGpuUsage?.ToString("F1") ?? "",
                    s.PeakGpuUsage?.ToString("F1") ?? "",
                    s.MaxCpuTemp?.ToString("F0") ?? "",
                    s.MaxGpuTemp?.ToString("F0") ?? "",
                    s.PeakRamGb?.ToString("F1") ?? "",
                    s.PeakVramGb?.ToString("F1") ?? "",
                    EscapeCsv(s.UserNotes)
                ));
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return $"\"{value}\"";
    }
}
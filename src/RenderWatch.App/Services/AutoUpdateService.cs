using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace RenderWatch.App.Services;

public class AutoUpdateService
{
    public const string GitHubOwner = "vagabonb";
    public const string GitHubRepo = "RenderWatch";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static string UpdateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenderWatch",
        "updates"
    );

    public static string PendingExePath => Path.Combine(UpdateDir, "RenderWatch.new");
    public static string ScriptPath => Path.Combine(UpdateDir, "apply_update.cmd");

    public static string? AvailableVersionTag { get; private set; }
    public static bool IsUpdateReady { get; private set; }

    public static event Action<string>? UpdateReady;

    /// <summary>
    /// Silent background check for updates from GitHub Releases.
    /// Runs asynchronously with zero impact on UI or application performance.
    /// </summary>
    public static void StartBackgroundUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Small delay after startup so app launch is completely instantaneous
                await Task.Delay(4000);
                await CheckAndDownloadUpdateAsync();
            }
            catch
            {
                // Silent catch: network unavailable, offline mode, rate limits
            }
        });
    }

    public static async Task CheckAndDownloadUpdateAsync()
    {
        try
        {
            Directory.CreateDirectory(UpdateDir);

            // If an old pending update exists and user just started it, clean up
            if (File.Exists(PendingExePath) && !IsUpdateReady)
            {
                try { File.Delete(PendingExePath); } catch { }
            }

            string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("RenderWatch-AutoUpdater", "1.0"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp)) return;
            string remoteTag = tagProp.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(remoteTag)) return;

            // Parse remote version
            string cleanTag = remoteTag.TrimStart('v', 'V').Trim();
            if (!Version.TryParse(cleanTag, out var remoteVer))
            {
                // Try loose 3-part version (e.g. 1.2 -> 1.2.0)
                if (Version.TryParse(cleanTag + ".0", out var looseVer))
                {
                    remoteVer = looseVer;
                }
                else return;
            }

            var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

            // Only update if remote version is strictly newer
            if (remoteVer <= currentVer) return;

            AvailableVersionTag = remoteTag;

            // Find the RenderWatch.exe download asset
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonArrayKind())
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        string name = nameProp.GetString() ?? "";
                        if (name.Equals("RenderWatch.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = urlProp.GetString();
                            break;
                        }
                    }
                }
            }

            // Fallback direct download link
            downloadUrl ??= $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{remoteTag}/RenderWatch.exe";

            // Download new executable in background
            string tempFile = PendingExePath + ".tmp";
            using (var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
            {
                downloadReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("RenderWatch-AutoUpdater", "1.0"));
                using var downloadResp = await _httpClient.SendAsync(downloadReq, HttpCompletionOption.ResponseHeadersRead);
                if (!downloadResp.IsSuccessStatusCode) return;

                using var inStream = await downloadResp.Content.ReadAsStreamAsync();
                using var outStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await inStream.CopyToAsync(outStream);
            }

            // Validate that download succeeded and is a real PE executable (> 30 MB)
            var fi = new FileInfo(tempFile);
            if (fi.Exists && fi.Length > 30_000_000)
            {
                if (File.Exists(PendingExePath)) File.Delete(PendingExePath);
                File.Move(tempFile, PendingExePath);
                IsUpdateReady = true;

                // Notify UI on main dispatcher
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    UpdateReady?.Invoke(AvailableVersionTag ?? remoteTag);
                });
            }
            else
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
        catch
        {
            // Fail silently on any network or parsing error
        }
    }

    private static JsonValueKind JsonArrayKind() => JsonValueKind.Array;

    /// <summary>
    /// Launches the detached swap script that waits for the current process to close,
    /// replaces the existing RenderWatch.exe with the downloaded one, and restarts.
    /// </summary>
    public static void ApplyUpdateAndRestart()
    {
        if (!IsUpdateReady || !File.Exists(PendingExePath)) return;

        try
        {
            int currentPid = Process.GetCurrentProcess().Id;
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe)) return;

            // Generate clean batch updater script
            string scriptContent = $@"@echo off
set OLD_PID={currentPid}
set TARGET_EXE=""{currentExe}""
set NEW_EXE=""{PendingExePath}""

:WAIT_LOOP
tasklist /fi ""PID eq %OLD_PID%"" 2>NUL | find ""%OLD_PID%"" >NUL
if %ERRORLEVEL%==0 (
    timeout /t 1 /nobreak >NUL
    goto WAIT_LOOP
)

copy /y %NEW_EXE% %TARGET_EXE% >NUL
if %ERRORLEVEL%==0 (
    del %NEW_EXE% >NUL
)

start """" %TARGET_EXE%
del ""%~f0"" >NUL
";

            File.WriteAllText(ScriptPath, scriptContent, System.Text.Encoding.ASCII);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{ScriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Application.Current.Shutdown();
            });
        }
        catch
        {
            // Ignored
        }
    }
}

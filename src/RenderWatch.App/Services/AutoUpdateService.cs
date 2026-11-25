using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace RenderWatch.App.Services;

internal static class AutoUpdateService
{
    private const string Repository = "vagabonb/RenderWatch";
    private const string AssetName = "RenderWatch.exe";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    public static async Task<StableRelease?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        if (Environment.ProcessPath is not { } target) return null;
        string cache = CachePath(target);
        try
        {
            PortableUpdateWorker.CleanOldWorkers(cache);
            return await FindOfferAsync(Client, UpdatePackage.ReadVersion(target), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            PortableUpdateWorker.Log(cache, ex);
            return null;
        }
    }

    internal static async Task<StableRelease?> FindOfferAsync(HttpClient client, Version current, CancellationToken cancellationToken = default)
    {
        using var request = Request(new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=20"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return releases.RootElement.EnumerateArray()
            .Select(ReadStableRelease)
            .OfType<StableRelease>()
            .Where(candidate => candidate.Package.Version > current)
            .MaxBy(candidate => candidate.Package.Version);
    }

    public static async Task DownloadAndStartAsync(StableRelease release, CancellationToken cancellationToken)
    {
        string target = Environment.ProcessPath ?? throw new IOException("Could not locate the running executable.");
        string cache = CachePath(target);
        try
        {
            await DownloadAsync(Client, release, cache, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var args = Environment.GetCommandLineArgs();
            string mode = args.Contains("--tray") ? "--tray" :
                args.Any(arg => arg is "--taskbar" or "--panel" or "--minimized") ? "--taskbar" : "";
            PortableUpdateWorker.Start(target, cache, release.Package, mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            PortableUpdateWorker.Log(cache, ex);
            throw;
        }
    }

    internal static async Task DownloadAsync(HttpClient client, StableRelease release, string cache, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cache);
        string pending = PortableUpdateWorker.PendingPath(cache);
        if (release.Package.Matches(pending)) return;

        string temporary = pending + ".download";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var download = Request(release.Download);
            using var asset = await client.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            asset.EnsureSuccessStatusCode();
            if (asset.Content.Headers.ContentLength is long length && length != release.Package.Size)
                throw new InvalidDataException("GitHub returned an unexpected update size.");

            await using (var source = await asset.Content.ReadAsStreamAsync(timeout.Token))
            await using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, timeout.Token);

            if (!release.Package.Matches(temporary))
                throw new InvalidDataException("The download does not match the release version or SHA-256.");

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, pending, overwrite: true);
        }
        finally
        {
            PortableUpdateWorker.TryDelete(temporary);
        }
    }

    private static StableRelease? ReadStableRelease(JsonElement release)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;

        var tag = release.GetProperty("tag_name").GetString();
        if (tag == null || !tag.StartsWith('v')) return null;
        if (!Version.TryParse(tag[1..], out var number) || number.Build < 0 || number.Revision >= 0 ||
            tag != $"v{number.ToString(3)}")
            return null;
        var version = new Version(number.Major, number.Minor, number.Build, 0);

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            if (!asset.TryGetProperty("digest", out var digestProperty)) continue;
            var digest = digestProperty.GetString();
            if (digest == null || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)) continue;
            if (!asset.GetProperty("size").TryGetInt64(out long size) || size <= 0) continue;

            var download = new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{AssetName}");
            return new StableRelease(tag, download, new UpdatePackage(version, size, digest[7..]));
        }

        return null;
    }

    private static string CachePath(string target)
    {
        string path = Path.GetFullPath(target).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return Path.Combine(App.DataDirectory, "updates", hash);
    }

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("RenderWatch/1.0");
        return request;
    }

    internal sealed record StableRelease(string Tag, Uri Download, UpdatePackage Package);
}

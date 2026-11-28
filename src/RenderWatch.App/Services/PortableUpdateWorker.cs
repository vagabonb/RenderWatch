using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace RenderWatch.App.Services;

internal sealed record UpdatePackage(Version Version, long Size, string Sha256)
{
    public static Version ReadVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    public bool Matches(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Size || ReadVersion(path) != Version)
            return false;

        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class PortableUpdateWorker
{
    internal const string Command = "--install-portable-update";
    private const string WorkerPrefix = "RenderWatch.UpdateWorker-";

    internal static string PendingPath(string cache) => Path.Combine(cache, "RenderWatch.pending");

    internal static void Start(string target, string cache, UpdatePackage package, string restartMode)
    {
        Directory.CreateDirectory(cache);
        var helper = Path.Combine(cache, WorkerPrefix + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(target, helper);

        using var parent = Process.GetCurrentProcess();
        var startInfo = new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(target)!
        };
        foreach (var arg in new[]
        {
            Command, target, parent.Id.ToString(CultureInfo.InvariantCulture),
            parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            package.Version.ToString(), package.Size.ToString(CultureInfo.InvariantCulture), package.Sha256, restartMode
        })
            startInfo.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(startInfo) ?? throw new IOException("Could not start the update helper.");
        }
        catch
        {
            TryDelete(helper);
            throw;
        }
    }

    internal static int Run(string[] args)
    {
        var helper = Environment.ProcessPath;
        if (helper == null || !Path.GetFileName(helper).StartsWith(WorkerPrefix, StringComparison.Ordinal) ||
            args.Length != 8 || !Path.IsPathFullyQualified(args[1]) ||
            !int.TryParse(args[2], out int parentId) || !long.TryParse(args[3], out long parentStart) ||
            !Version.TryParse(args[4], out var version) || !long.TryParse(args[5], out long size) || size <= 0 ||
            args[6].Length != 64 || !args[6].All(Uri.IsHexDigit) || args[7] is not ("" or "--taskbar" or "--tray" or "--notch"))
            return 1;

        string cache = Path.GetDirectoryName(helper)!;
        string target = Path.GetFullPath(args[1]);
        var package = new UpdatePackage(version, size, args[6]);
        try
        {
            WaitForParent(parentId, parentStart, target);
            if (UpdatePackage.ReadVersion(target) >= package.Version) return 0;

            Install(target, cache, package, args[7]);
            return 0;
        }
        catch (Exception ex)
        {
            Log(cache, ex);
            return 1;
        }
    }

    private static void WaitForParent(int id, long started, string target)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (parent)
        {
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != started) return;
            if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The target is not the executable that requested the update.");
            if (!parent.WaitForExit(60_000))
                throw new TimeoutException("PC Watch did not finish closing within one minute.");
        }
    }

    private static void Install(string target, string cache, UpdatePackage package, string restartMode)
    {
        string pending = PendingPath(cache);
        string staged = target + ".update-" + Guid.NewGuid().ToString("N");
        string backup = target + ".previous";
        try
        {
            File.Copy(pending, staged);
            if (!package.Matches(staged))
                throw new InvalidDataException("The downloaded file does not match the release version or SHA-256.");

            // File.Replace requires both files on the same volume.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Replace(staged, target, backup);
                    break;
                }
                catch (IOException ex) when (attempt < 19 && (ex.HResult & 0xFFFF) is 32 or 33)
                {
                    Thread.Sleep(250);
                }
            }

            try
            {
                var startInfo = new ProcessStartInfo(target)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(target)!
                };
                if (restartMode.Length != 0) startInfo.ArgumentList.Add(restartMode);
                using var app = Process.Start(startInfo) ?? throw new IOException("Could not start the updated version.");
            }
            catch
            {
                File.Replace(backup, target, null);
                throw;
            }

            TryDelete(pending);
        }
        catch
        {
            if (!File.Exists(target) && File.Exists(backup)) File.Move(backup, target);
            throw;
        }
        finally
        {
            TryDelete(staged);
        }
    }

    internal static void CleanOldWorkers(string cache)
    {
        if (!Directory.Exists(cache)) return;
        foreach (var helper in Directory.EnumerateFiles(cache, WorkerPrefix + "*.exe"))
            TryDelete(helper);
    }

    internal static void Log(string cache, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(cache);
            File.AppendAllText(Path.Combine(cache, "update-error.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

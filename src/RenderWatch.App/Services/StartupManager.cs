using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RenderWatch.App.Services;

/// <summary>
/// Manages Windows startup registration for RENDER WATCH.
/// Utilizes Windows Task Scheduler with Highest Privileges (/RL HIGHEST)
/// so that the application starts elevated upon user logon without triggering a UAC prompt.
/// Also mirrors to the CurrentVersion\Run registry key as a fallback.
/// </summary>
public static class StartupManager
{
    public const string TaskName = "RenderWatch";
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RenderWatch";

    public static string GetExecutablePath()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RenderWatch.exe");
        }
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Checks whether autostart is currently enabled via Task Scheduler or Registry.
    /// </summary>
    public static bool IsAutostartEnabled()
    {
        // 1. Check Task Scheduler
        if (IsTaskScheduled())
        {
            return true;
        }

        // 2. Check Registry Run key
        return IsRegistryRunSet();
    }

    /// <summary>
    /// Enables or disables Windows autostart.
    /// </summary>
    public static bool SetAutostart(bool enable)
    {
        string exePath = GetExecutablePath();
        bool success = true;

        if (enable)
        {
            // 1. Register with Windows Task Scheduler (Elevated, on logon, zero UAC prompt)
            bool taskResult = CreateScheduledTask(exePath);
            if (!taskResult)
            {
                success = false;
            }

            // 2. Mirror into HKCU Registry Run
            SetRegistryRun(true, exePath);
        }
        else
        {
            // 1. Remove from Task Scheduler
            DeleteScheduledTask();

            // 2. Remove from Registry Run
            SetRegistryRun(false, exePath);
        }

        return success;
    }

    private static bool IsTaskScheduled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(2000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CreateScheduledTask(string exePath)
    {
        try
        {
            // Arguments format: /create /tn "RenderWatch" /tr "\"C:\Path\RenderWatch.exe\" --taskbar" /sc onlogon /rl highest /f
            string taskAction = $"\\\"{exePath}\\\" --taskbar";
            string args = $"/create /tn \"{TaskName}\" /tr \"{taskAction}\" /sc onlogon /rl highest /f";

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{TaskName}\" /f",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRegistryRunSet()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetRegistryRun(bool enable, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null) return;

            if (enable)
            {
                key.SetValue(AppName, $"\"{exePath}\" --taskbar");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // Suppress registry exceptions
        }
    }
}

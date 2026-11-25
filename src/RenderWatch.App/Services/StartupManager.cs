using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RenderWatch.App.Services;

/// <summary>
/// Manages Windows startup registration for RENDER WATCH.
/// Utilizes Windows Task Scheduler with Highest Privileges (/RL HIGHEST)
/// so that the application starts elevated upon user logon without triggering a UAC prompt.
/// Allows startup while the computer is running on battery power.
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

        // A Run key cannot launch an application that requires elevation at logon.
        return false;
    }

    /// <summary>
    /// Enables or disables Windows autostart.
    /// </summary>
    public static bool SetAutostart(bool enable)
    {
        if (!enable)
        {
            bool scheduled = IsTaskScheduled();
            bool removed = !scheduled || DeleteScheduledTask();
            SetRegistryRun(false, GetExecutablePath());
            return removed;
        }

        bool created = CreateScheduledTask(GetExecutablePath());
        if (created) SetRegistryRun(false, GetExecutablePath());
        return created;
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
            // Interactive-token tasks can start elevated at logon without a UAC prompt.
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Cannot identify the current user.");
            var task = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XDeclaration("1.0", "UTF-16", null),
                new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Task", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                    new System.Xml.Linq.XAttribute("version", "1.2"),
                    new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Triggers", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("LogonTrigger", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Enabled", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "true"),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("UserId", "http://schemas.microsoft.com/windows/2004/02/mit/task"), sid))),
                    new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Principals", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Principal", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                            new System.Xml.Linq.XAttribute("id", "Author"),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("UserId", "http://schemas.microsoft.com/windows/2004/02/mit/task"), sid),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("LogonType", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "InteractiveToken"),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("RunLevel", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "HighestAvailable"))),
                    new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Settings", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("MultipleInstancesPolicy", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "IgnoreNew"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("DisallowStartIfOnBatteries", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "false"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("StopIfGoingOnBatteries", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "false"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("ExecutionTimeLimit", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "PT0S")),
                    new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Actions", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                        new System.Xml.Linq.XAttribute("Context", "Author"),
                        new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Exec", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Command", "http://schemas.microsoft.com/windows/2004/02/mit/task"), exePath),
                            new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("Arguments", "http://schemas.microsoft.com/windows/2004/02/mit/task"), "--taskbar")))));
            string xmlPath = Path.Combine(Path.GetTempPath(), $"RenderWatch-startup-{Guid.NewGuid():N}.xml");
            try
            {
                task.Save(xmlPath);
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("/create");
                psi.ArgumentList.Add("/tn");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/xml");
                psi.ArgumentList.Add(xmlPath);
                psi.ArgumentList.Add("/f");
                using var process = Process.Start(psi);
                if (process == null || !process.WaitForExit(10000)) return false;
                return process.ExitCode == 0;
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
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

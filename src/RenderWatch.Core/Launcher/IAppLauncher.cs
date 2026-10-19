using System.Diagnostics;
using RenderWatch.Core.Models;

namespace RenderWatch.Core.Launcher;

public class LaunchResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
}

public interface IAppLauncher
{
    LaunchResult Launch(LaunchProfile profile);
}

public class AppLauncher : IAppLauncher
{
    public LaunchResult Launch(LaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return new LaunchResult { Success = false, Message = "Путь к исполняемому файлу не указан." };
        }

        string expandedPath = Environment.ExpandEnvironmentVariables(profile.ExecutablePath);
        if (!File.Exists(expandedPath))
        {
            return new LaunchResult { Success = false, Message = $"Файл не найден: {expandedPath}" };
        }

        string workDir = !string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Environment.ExpandEnvironmentVariables(profile.WorkingDirectory)
            : Path.GetDirectoryName(expandedPath) ?? string.Empty;

        if (!string.IsNullOrEmpty(workDir) && !Directory.Exists(workDir))
        {
            try
            {
                Directory.CreateDirectory(workDir);
            }
            catch
            {
                workDir = Path.GetDirectoryName(expandedPath) ?? string.Empty;
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = expandedPath,
                Arguments = profile.Arguments ?? string.Empty,
                WorkingDirectory = workDir,
                UseShellExecute = false
            };

            var proc = Process.Start(startInfo);
            if (proc != null)
            {
                profile.LastLaunchedAt = DateTime.Now;
                return new LaunchResult
                {
                    Success = true,
                    Message = $"Приложение успешно запущено (PID: {proc.Id})",
                    ProcessId = proc.Id
                };
            }

            return new LaunchResult { Success = false, Message = "Не удалось запустить процесс." };
        }
        catch (Exception ex)
        {
            return new LaunchResult { Success = false, Message = $"Ошибка запуска: {ex.Message}" };
        }
    }
}
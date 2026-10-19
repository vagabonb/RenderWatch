using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using RenderWatch.Core.Launcher;
using RenderWatch.Core.Models;
using RenderWatch.Core.Settings;

namespace RenderWatch.App.ViewModels;

public class LauncherViewModel : ViewModelBase
{
    private readonly IAppLauncher _launcher;
    private readonly ISettingsManager _settingsManager;
    private LaunchProfile? _selectedProfile;
    private string _statusMessage = string.Empty;

    // Edit fields
    private string _editName = string.Empty;
    private string _editExePath = string.Empty;
    private string _editArgs = string.Empty;
    private string _editWorkDir = string.Empty;
    private string _editDesc = string.Empty;
    private bool _isEditing;

    public ObservableCollection<LaunchProfile> Profiles { get; } = new();

    public LaunchProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value) && value != null)
            {
                EditName = value.Name;
                EditExePath = value.ExecutablePath;
                EditArgs = value.Arguments;
                EditWorkDir = value.WorkingDirectory;
                EditDesc = value.Description;
                IsEditing = true;
            }
        }
    }

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public string EditName { get => _editName; set => SetField(ref _editName, value); }
    public string EditExePath { get => _editExePath; set => SetField(ref _editExePath, value); }
    public string EditArgs { get => _editArgs; set => SetField(ref _editArgs, value); }
    public string EditWorkDir { get => _editWorkDir; set => SetField(ref _editWorkDir, value); }
    public string EditDesc { get => _editDesc; set => SetField(ref _editDesc, value); }
    public bool IsEditing { get => _isEditing; set => SetField(ref _isEditing, value); }

    public ICommand LaunchCommand { get; }
    public ICommand AddNewCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand BrowseExeCommand { get; }

    public LauncherViewModel(IAppLauncher launcher, ISettingsManager settingsManager)
    {
        _launcher = launcher;
        _settingsManager = settingsManager;

        LaunchCommand = new RelayCommand(ExecuteLaunch);
        AddNewCommand = new RelayCommand(StartNewProfile);
        SaveProfileCommand = new RelayCommand(async () => await SaveProfileAsync());
        DeleteProfileCommand = new RelayCommand(async () => await DeleteProfileAsync());
        BrowseExeCommand = new RelayCommand(BrowseExecutable);
    }

    public void LoadProfiles(IEnumerable<LaunchProfile> profiles)
    {
        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);

        if (Profiles.Count == 0)
        {
            // Default Blender profile
            var def = new LaunchProfile
            {
                Name = "Blender 3D",
                Description = "Запуск рабочего проекта Blender",
                ExecutablePath = @"C:\Program Files\Blender Foundation\Blender\blender.exe",
                Arguments = ""
            };
            Profiles.Add(def);
        }
    }

    private void ExecuteLaunch()
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "Выберите профиль для запуска.";
            return;
        }

        var result = _launcher.Launch(SelectedProfile);
        StatusMessage = result.Message;
    }

    private void StartNewProfile()
    {
        SelectedProfile = null;
        EditName = "Новое приложение";
        EditExePath = string.Empty;
        EditArgs = string.Empty;
        EditWorkDir = string.Empty;
        EditDesc = string.Empty;
        IsEditing = true;
    }

    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditExePath))
        {
            StatusMessage = "Укажите название и путь к исполняемому файлу.";
            return;
        }

        if (SelectedProfile == null)
        {
            var p = new LaunchProfile
            {
                Name = EditName,
                ExecutablePath = EditExePath,
                Arguments = EditArgs,
                WorkingDirectory = EditWorkDir,
                Description = EditDesc
            };
            Profiles.Add(p);
            SelectedProfile = p;
        }
        else
        {
            SelectedProfile.Name = EditName;
            SelectedProfile.ExecutablePath = EditExePath;
            SelectedProfile.Arguments = EditArgs;
            SelectedProfile.WorkingDirectory = EditWorkDir;
            SelectedProfile.Description = EditDesc;
        }

        var settings = await _settingsManager.LoadSettingsAsync();
        settings.LaunchProfiles = Profiles.ToList();
        await _settingsManager.SaveSettingsAsync(settings);

        StatusMessage = "Профиль сохранён.";
    }

    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile != null)
        {
            Profiles.Remove(SelectedProfile);
            SelectedProfile = null;
            IsEditing = false;

            var settings = await _settingsManager.LoadSettingsAsync();
            settings.LaunchProfiles = Profiles.ToList();
            await _settingsManager.SaveSettingsAsync(settings);

            StatusMessage = "Профиль удалён.";
        }
    }

    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            Title = "Выберите исполняемый файл приложения"
        };

        if (dialog.ShowDialog() == true)
        {
            EditExePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(EditWorkDir))
            {
                EditWorkDir = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }
}
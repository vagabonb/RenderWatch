using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using RenderWatch.App.Services;
using RenderWatch.App.Tray;
using RenderWatch.App.ViewModels;
using RenderWatch.App.Views;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.History;
using RenderWatch.Core.Launcher;
using RenderWatch.Core.Notifications;
using RenderWatch.Core.Settings;
using RenderWatch.Core.Tracking;

namespace RenderWatch.App;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    private const int SW_RESTORE = 9;

    private TrayIconManager? _trayManager;
    private MainWindow? _mainWindow;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                File.AppendAllText("error.log", $"[{DateTime.Now}] Unhandled Exception: {e.Exception}\n");
            }
            catch { }
            MessageBox.Show($"Произошла ошибка:\n{e.Exception.Message}\n\nПодробности записаны в error.log", "RENDER WATCH Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                File.AppendAllText("error.log", $"[{DateTime.Now}] AppDomain Exception: {e.ExceptionObject}\n");
            }
            catch { }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Explicit Application ID ensures Windows Taskbar associates the window strictly with this exact version
            // and purges any stale/cached icons from earlier builds on laptops or other systems
            SetCurrentProcessExplicitAppUserModelID("RenderWatch.App.v2.FinalIcon");
        }
        catch { }

        // Ensure Single Instance: if an active instance with a visible window exists, focus it.
        // If stale ghost processes exist without a window, terminate them cleanly.
        try
        {
            var currentProc = Process.GetCurrentProcess();
            var existingProcs = Process.GetProcessesByName("RenderWatch")
                .Where(p => p.Id != currentProc.Id)
                .ToList();

            if (existingProcs.Count > 0)
            {
                IntPtr activeHwnd = IntPtr.Zero;
                foreach (var p in existingProcs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        activeHwnd = p.MainWindowHandle;
                        break;
                    }
                }

                if (activeHwnd != IntPtr.Zero)
                {
                    ShowWindow(activeHwnd, SW_RESTORE);
                    SetForegroundWindow(activeHwnd);
                    Shutdown();
                    return;
                }

                // If existing instances have no window (stale or hung), terminate them to let the new instance take over
                foreach (var p in existingProcs)
                {
                    try { p.Kill(); } catch { }
                }
            }
        }
        catch { }

        try
        {
            // Initialize Core Dependencies
            var hardwareMonitor = new CompositeHardwareMonitor();
            var tracker = new RenderProcessTracker();
            var historyRepo = new JsonHistoryRepository();
            var settingsManager = new JsonSettingsManager();
            var notificationService = new NotificationService();
            var launcher = new AppLauncher();

            // Initialize MainViewModel
            var mainViewModel = new MainViewModel(
                hardwareMonitor,
                tracker,
                historyRepo,
                settingsManager,
                notificationService,
                launcher);

            // Create Main Window immediately so UI appears with zero lag
            _mainWindow = new MainWindow(mainViewModel);

            // Create Native Tray Icon Manager
            _trayManager = new TrayIconManager(
                mainViewModel,
                _mainWindow,
                showWindowAction: () => _mainWindow.RestoreFromTray(),
                exitAction: () =>
                {
                    _mainWindow.Close();
                    Shutdown();
                });

            _mainWindow.AttachTrayManager(_trayManager);

            bool startMinimized = e.Args.Contains("--minimized");
            if (startMinimized)
            {
                _mainWindow.HideToTray();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.Activate();
                _mainWindow.Focus();
                RenderWatch.App.Utils.MemoryOptimizer.Trim();
            }

            // Asynchronously load settings, profiles, and start hardware telemetry loop
            _ = mainViewModel.InitializeAsync();

            // Periodic memory trimming every 45s keeps RAM minimal (especially on Intel laptops)
            var memTrimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            memTrimTimer.Tick += (_, _) =>
            {
                RenderWatch.App.Utils.MemoryOptimizer.Trim();
            };
            memTrimTimer.Start();

            // Initial post-startup trim after WPF themes and monitoring are loaded
            Task.Delay(3000).ContinueWith(_ =>
            {
                try
                {
                    Current?.Dispatcher.Invoke(() => RenderWatch.App.Utils.MemoryOptimizer.Trim());
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText("error.log", $"[{DateTime.Now}] OnStartup Exception: {ex}\n");
            }
            catch { }
            MessageBox.Show($"Ошибка инициализации RENDER WATCH:\n{ex.Message}\n\nПодробности в error.log", "RENDER WATCH", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayManager?.Dispose();
        }
        catch { }

        if (AutoUpdateService.IsUpdateReady)
        {
            AutoUpdateService.ApplyUpdateAndRestart();
            return;
        }

        base.OnExit(e);
        Environment.Exit(0);
    }
}

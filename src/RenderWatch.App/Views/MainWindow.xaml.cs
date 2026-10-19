using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RenderWatch.App.Tray;
using RenderWatch.App.ViewModels;

namespace RenderWatch.App.Views;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetClassLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetClassLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    private const uint WM_SETICON = 0x0080;
    private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
    private static readonly IntPtr ICON_BIG = new IntPtr(1);

    private readonly MainViewModel _viewModel;
    private TrayIconManager? _trayManager;
    private System.Drawing.Icon? _hIconBig;
    private System.Drawing.Icon? _hIconSmall;

    private double _normalWidth  = 1180;
    private double _normalHeight = 780;
    private double _normalLeft   = 100;
    private double _normalTop    = 100;

    private const double CompactTargetWidth  = 660;
    private const double CompactTargetHeight = 150;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompactMode))
            {
                ApplyCompactMode(_viewModel.IsCompactMode);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsCompactTopmost))
            {
                if (_viewModel.IsCompactMode)
                {
                    Topmost = _viewModel.IsCompactTopmost;
                }
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var uri = new Uri("pack://application:,,,/RenderWatch;component/Assets/app.ico", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(uri) ?? Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);

                    // 48x48 icon for Windows Taskbar on high-DPI laptop displays (125% - 150% scaling)
                    ms.Position = 0;
                    _hIconBig = new System.Drawing.Icon(ms, 48, 48);
                    SendMessage(hwnd, WM_SETICON, ICON_BIG, _hIconBig.Handle);
                    try { SetClassLongPtr(hwnd, GCLP_HICON, _hIconBig.Handle); } catch { }

                    ms.Position = 0;
                    _hIconSmall = new System.Drawing.Icon(ms, 16, 16);
                    SendMessage(hwnd, WM_SETICON, ICON_SMALL, _hIconSmall.Handle);
                    try { SetClassLongPtr(hwnd, GCLP_HICONSM, _hIconSmall.Handle); } catch { }
                }
                else
                {
                    var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        _hIconBig = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (_hIconBig != null)
                        {
                            SendMessage(hwnd, WM_SETICON, ICON_BIG, _hIconBig.Handle);
                            SendMessage(hwnd, WM_SETICON, ICON_SMALL, _hIconBig.Handle);
                            try { SetClassLongPtr(hwnd, GCLP_HICON, _hIconBig.Handle); } catch { }
                            try { SetClassLongPtr(hwnd, GCLP_HICONSM, _hIconBig.Handle); } catch { }
                        }
                    }
                }

                try
                {
                    this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(uri);
                }
                catch { }
            }
        }
        catch { }
    }

    public void AttachTrayManager(TrayIconManager trayManager)
    {
        _trayManager = trayManager;
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Settings.CloseToTray)
        {
            HideToTray();
        }
        else
        {
            Close();
        }
    }

    private void OnCompactModeClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCompactMode = !_viewModel.IsCompactMode;
        ApplyCompactMode(_viewModel.IsCompactMode);
    }

    private void OnCompactHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void ApplyCompactMode(bool compact)
    {
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);

        if (compact)
        {
            if (chrome != null)
            {
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            _normalWidth  = Width;
            _normalHeight = Height;
            _normalLeft   = Left;
            _normalTop    = Top;

            NormalModeGrid.Visibility  = Visibility.Collapsed;
            CompactModeGrid.Visibility = Visibility.Visible;

            ResizeMode = ResizeMode.NoResize;
            Topmost    = _viewModel.IsCompactTopmost;
            MinWidth   = 400;
            MinHeight  = 120;
            Width      = CompactTargetWidth;
            Height     = CompactTargetHeight;
        }
        else
        {
            if (chrome != null)
            {
                chrome.CaptionHeight = 38;
                chrome.ResizeBorderThickness = new Thickness(6);
            }

            NormalModeGrid.Visibility  = Visibility.Visible;
            CompactModeGrid.Visibility = Visibility.Collapsed;

            Topmost    = false;
            ResizeMode = ResizeMode.CanResize;
            MinWidth   = 900;
            MinHeight  = 600;
            Width      = Math.Max(_normalWidth, 900);
            Height     = Math.Max(_normalHeight, 600);
            Left       = _normalLeft;
            Top        = _normalTop;
        }
    }

    private MicroWindow? _microWindow;
    private TaskbarStripWindow? _taskbarStripWindow;

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void OnMicroModeClicked(object sender, RoutedEventArgs e)
    {
        MinimizeToMicroWindow();
    }

    private void OnMinimizeToPanelClicked(object sender, RoutedEventArgs e)
    {
        MinimizeToTaskbarStrip();
    }

    public void MinimizeToMicroWindow()
    {
        _taskbarStripWindow?.Hide();
        Hide();
        if (_microWindow == null)
        {
            _microWindow = new MicroWindow(
                _viewModel,
                restoreAction: () => RestoreFromMicroWindow(),
                hideToTrayAction: () => HideToTray(),
                exitAction: () =>
                {
                    _viewModel.Settings.CloseToTray = false;
                    Close();
                }
            );
        }

        _microWindow.PositionNearTray();
        _microWindow.Show();
        _microWindow.Topmost = true;
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
    }

    public void RestoreFromMicroWindow()
    {
        _microWindow?.Hide();
        _viewModel.IsMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        BringIntoView();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
    }

    public void MinimizeToTaskbarStrip()
    {
        _microWindow?.Hide();
        Hide();
        if (_taskbarStripWindow == null)
        {
            _taskbarStripWindow = new TaskbarStripWindow(
                _viewModel,
                restoreAction: () => RestoreFromTaskbarStrip(),
                hideToTrayAction: () => HideToTray(),
                exitAction: () =>
                {
                    _viewModel.Settings.CloseToTray = false;
                    Close();
                }
            );
        }

        _taskbarStripWindow.PositionOnTaskbar();
        _taskbarStripWindow.Show();
        _taskbarStripWindow.EnsureTopmost();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
    }

    public void RestoreFromTaskbarStrip()
    {
        _taskbarStripWindow?.Hide();
        _viewModel.IsMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        BringIntoView();
    }

    private void OnToggleTopmostClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCompactTopmost = !_viewModel.IsCompactTopmost;
        if (_viewModel.IsCompactMode)
        {
            Topmost = _viewModel.IsCompactTopmost;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsCompactMode)
        {
            _viewModel.IsCompactMode = false;
            ApplyCompactMode(false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    public void HideToTray()
    {
        _microWindow?.Hide();
        _taskbarStripWindow?.Hide();
        _viewModel.IsMinimized = true;
        Hide();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
        _trayManager?.ShowNotification("RENDER WATCH", "Приложение свёрнуто в системный трей и продолжает работу в фоне.");
    }

    public void RestoreFromTray()
    {
        _microWindow?.Hide();
        _taskbarStripWindow?.Hide();
        _viewModel.IsMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _microWindow?.Close();
            _taskbarStripWindow?.Close();
        }
        catch { }
        try
        {
            _hIconBig?.Dispose();
            _hIconSmall?.Dispose();
        }
        catch { }
        try
        {
            _trayManager?.Dispose();
        }
        catch { }
        try
        {
            _viewModel.Dispose();
        }
        catch { }
        base.OnClosed(e);

        // Terminate process completely to release any file/folder locks immediately
        try
        {
            Application.Current?.Shutdown();
        }
        catch { }
        Environment.Exit(0);
    }
}
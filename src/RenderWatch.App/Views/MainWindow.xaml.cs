using System;
using System.IO;
using System.ComponentModel;
using RenderWatch.Core.Models;
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

    private bool _closingAfterSave;
    private bool _savingBeforeClose;
    private const double CompactWindowWidth = 350;
    private Point _compactDragStart;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) => RestoreWindowLayout();
        _viewModel.Constructor.PropertyChanged += (_, e) =>
        {
            if (_viewModel.IsCompactMode &&
                (string.IsNullOrEmpty(e.PropertyName) ||
                 e.PropertyName == nameof(DisplayConstructorViewModel.ActiveCompactGraphs)))
            {
                UpdateCompactSize();
            }
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AreSettingsLoaded))
            {
                RestoreWindowLayout();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsCompactMode))
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
    }

    private void OnCompactHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            _viewModel.IsCompactMode = false;
            e.Handled = true;
            return;
        }
        _compactDragStart = e.GetPosition(this);
    }

    private void OnCompactHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _compactDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _compactDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void OnCompactContentMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            _viewModel.IsCompactMode = false;
            e.Handled = true;
        }
    }

    private void UpdateCompactSize()
    {
        int count = _viewModel.Constructor.ActiveCompactGraphs.Count;
        Width = Math.Clamp(CompactWindowWidth, MinWidth,
            Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(count * CompactChartStrip.TileHeight + 38, MinHeight,
            Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        if (double.IsFinite(Top))
            Top = ClampCompactTop(Top);
    }

    private double ClampCompactTop(double top) => Math.Clamp(top, SystemParameters.VirtualScreenTop,
        Math.Max(SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));

    private WindowPlacement CapturePlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        return new WindowPlacement
        {
            Left = double.IsFinite(bounds.Left) ? bounds.Left : 100,
            Top = double.IsFinite(bounds.Top) ? bounds.Top : 100,
            Width = double.IsFinite(bounds.Width) && bounds.Width > 0 ? bounds.Width : Width,
            Height = double.IsFinite(bounds.Height) && bounds.Height > 0 ? bounds.Height : Height
        };
    }

    private void StoreWindowLayout(bool compact)
    {
        var layout = _viewModel.GetWindowLayout();
        if (compact)
        {
            layout.Compact = CapturePlacement();
        }
        else
        {
            layout.Main = CapturePlacement();
            layout.IsMainMaximized = WindowState == WindowState.Maximized;
        }
    }

    private void RestoreWindowLayout()
    {
        var layout = _viewModel.GetWindowLayout();
        var compact = _viewModel.IsCompactMode;
        var placement = compact ? layout.Compact : layout.Main;
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            chrome.CaptionHeight = compact ? 0 : 38;
            chrome.ResizeBorderThickness = new Thickness(compact ? 0 : 6);
        }

        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        ResizeMode = compact ? ResizeMode.NoResize : ResizeMode.CanResize;
        Topmost = compact && _viewModel.IsCompactTopmost;
        MinWidth = compact ? 180 : 900;
        MinHeight = compact ? 110 : 600;

        if (compact)
        {
            UpdateCompactSize();
        }
        else
        {
            Width = placement != null && double.IsFinite(placement.Width)
                ? Math.Max(MinWidth, placement.Width) : (_viewModel.UsesBlackLayout ? 1180 : 1220);
            Height = placement != null && double.IsFinite(placement.Height)
                ? Math.Max(MinHeight, placement.Height) : (_viewModel.UsesBlackLayout ? 780 : 800);
        }

        if (placement != null && double.IsFinite(placement.Left) && double.IsFinite(placement.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(placement.Left, SystemParameters.VirtualScreenLeft - Width + 80,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80);
            Top = compact ? ClampCompactTop(placement.Top) : Math.Clamp(placement.Top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 38);
        }

        if (!compact && layout.IsMainMaximized) WindowState = WindowState.Maximized;
    }

    private void ApplyCompactMode(bool compact)
    {
        StoreWindowLayout(!compact);
        RestoreWindowLayout();
        _ = _viewModel.SaveWindowLayoutAsync();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closingAfterSave) return;
        e.Cancel = true;
        if (_savingBeforeClose) return;
        _savingBeforeClose = true;

        StoreWindowLayout(_viewModel.IsCompactMode);
        try
        {
            await _viewModel.SaveWindowLayoutAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        _closingAfterSave = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    private MicroWindow? _microWindow;
    private TaskbarStripWindow? _taskbarStripWindow;
    private NotchWindow? _notchWindow;

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

    private void OnNotchModeClicked(object sender, RoutedEventArgs e) => MinimizeToNotch();

    public void MinimizeToNotch()
    {
        _microWindow?.Hide();
        _taskbarStripWindow?.Stop();
        _viewModel.IsMinimized = true;
        Hide();
        _notchWindow ??= new NotchWindow(_viewModel, RestoreFromTray, HideToTray);
        _notchWindow.ShowOnMonitorOf(this);
    }

    public void MinimizeToMicroWindow()
    {
        _notchWindow?.Stop();
        _taskbarStripWindow?.Stop();
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
        _viewModel.IsCompactMode = false;
        _viewModel.IsMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        BringIntoView();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
    }

    public void MinimizeToTaskbarStrip()
    {
        _notchWindow?.Stop();
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

        _taskbarStripWindow.Start();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
    }

    public void RestoreFromTaskbarStrip()
    {
        _taskbarStripWindow?.Stop();
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
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    public void HideToTray()
    {
        _notchWindow?.Stop();
        _microWindow?.Hide();
        _taskbarStripWindow?.Stop();
        _viewModel.IsMinimized = true;
        Hide();
        RenderWatch.App.Utils.MemoryOptimizer.Trim();
        _trayManager?.ShowNotification("RENDER WATCH", "Приложение свёрнуто в системный трей и продолжает работу в фоне.");
    }

    public void RestoreFromTray()
    {
        _notchWindow?.Stop();
        _microWindow?.Hide();
        _taskbarStripWindow?.Stop();
        _viewModel.IsMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _notchWindow?.Close();
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

        Application.Current?.Shutdown();
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RenderWatch.App.ViewModels;
using RenderWatch.App.Views;

namespace RenderWatch.App.Tray;

public class TrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly MainViewModel _mainViewModel;
    private readonly Action _showWindowAction;
    private readonly Action _exitAction;
    private readonly Window _ownerWindow;
    private HwndSource? _hwndSource;
    private NOTIFYICONDATA _notifyData;
    private bool _isAdded;
    private ContextMenu? _contextMenu;
    private Icon? _trayIconRef;
    private TrayHoverFlyout? _hoverFlyout;

    public TrayIconManager(MainViewModel mainViewModel, Window ownerWindow, Action showWindowAction, Action exitAction)
    {
        _mainViewModel = mainViewModel;
        _ownerWindow = ownerWindow;
        _showWindowAction = showWindowAction;
        _exitAction = exitAction;

        BuildContextMenu();
        InitializeNativeTray();
    }

    private IntPtr GetApplicationIconHandle()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/RenderWatch;component/Assets/app.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri) ?? Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                _trayIconRef = new Icon(ms, 16, 16);
                return _trayIconRef.Handle;
            }
        }
        catch { }

        try
        {
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                _trayIconRef = Icon.ExtractAssociatedIcon(exePath);
                if (_trayIconRef != null)
                {
                    return _trayIconRef.Handle;
                }
            }
        }
        catch { }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION fallback
    }

    private void InitializeNativeTray()
    {
        var helper = new WindowInteropHelper(_ownerWindow);
        IntPtr hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        IntPtr hIcon = GetApplicationIconHandle();

        _notifyData = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "RENDER WATCH - Пульт рендер-станции"
        };

        _isAdded = Shell_NotifyIcon(NIM_ADD, ref _notifyData);
    }

    private void BuildContextMenu()
    {
        _contextMenu = new ContextMenu
        {
            Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#14171C")),
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252B34")),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6"))
        };

        var titleItem = new MenuItem
        {
            Header = "RENDER WATCH (Открыть)",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))
        };
        titleItem.Click += (_, _) =>
        {
            _hoverFlyout?.HideFlyout();
            _showWindowAction();
        };
        _contextMenu.Items.Add(titleItem);

        var microItem = new MenuItem { Header = "Микро-окно" };
        microItem.Click += (_, _) =>
        {
            _hoverFlyout?.HideFlyout();
            if (_ownerWindow is MainWindow mw)
            {
                mw.MinimizeToMicroWindow();
            }
        };
        _contextMenu.Items.Add(microItem);

        var panelItem = new MenuItem { Header = "На панель (индикатор)" };
        panelItem.Click += (_, _) =>
        {
            _hoverFlyout?.HideFlyout();
            if (_ownerWindow is MainWindow mw)
            {
                mw.MinimizeToTaskbarStrip();
            }
        };
        _contextMenu.Items.Add(panelItem);

        _contextMenu.Items.Add(new Separator());

        var pauseItem = new MenuItem { Header = "Приостановить мониторинг" };
        pauseItem.Click += (_, _) => _mainViewModel.IsMonitoring = !_mainViewModel.IsMonitoring;
        _contextMenu.Items.Add(pauseItem);

        _contextMenu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Выход из приложения", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9CA3AF")) };
        exitItem.Click += (_, _) =>
        {
            _hoverFlyout?.HideFlyout();
            _exitAction();
        };
        _contextMenu.Items.Add(exitItem);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int action = lParam.ToInt32();
            if (action == WM_MOUSEMOVE)
            {
                if (_hoverFlyout == null)
                {
                    _hoverFlyout = new TrayHoverFlyout(_mainViewModel, _showWindowAction);
                }
                _hoverFlyout.KeepAlive();
            }
            else if (action == 0x0202) // WM_LBUTTONUP: single click also opens/holds flyout
            {
                if (_hoverFlyout == null)
                {
                    _hoverFlyout = new TrayHoverFlyout(_mainViewModel, _showWindowAction);
                }
                _hoverFlyout.ShowAtCursor();
                handled = true;
            }
            else if (action == WM_LBUTTONDBLCLK)
            {
                _hoverFlyout?.HideFlyout();
                _showWindowAction();
                handled = true;
            }
            else if (action == WM_RBUTTONUP)
            {
                _hoverFlyout?.HideFlyout();
                SetForegroundWindow(hwnd);
                if (_contextMenu != null)
                {
                    _contextMenu.IsOpen = true;
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isAdded) return;

        _notifyData.uFlags |= NIF_INFO;
        _notifyData.szInfoTitle = title;
        _notifyData.szInfo = message;
        _notifyData.dwInfoFlags = 1; // NIIF_INFO
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _notifyData);
            _isAdded = false;
        }
        _hwndSource?.RemoveHook(WndProc);
        _trayIconRef?.Dispose();
        _hoverFlyout?.Close();
        _hoverFlyout = null;
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RenderWatch.App.ViewModels;

namespace RenderWatch.App.Views;

public partial class TaskbarStripWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_HWNDPARENT = -8;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, int dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    private static bool IsLaptopWithBattery()
    {
        try
        {
            if (GetSystemPowerStatus(out var sps))
            {
                // BatteryFlag 128 = No system battery (Desktop PC), 255 = Unknown
                return sps.BatteryFlag != 128 && sps.BatteryFlag != 255;
            }
        }
        catch { }
        return false;
    }

    private static double GetDpiScale(IntPtr hWnd)
    {
        try
        {
            if (hWnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hWnd);
                if (dpi > 0)
                {
                    return dpi / 96.0;
                }
            }
        }
        catch { }

        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            if (g.DpiX > 10)
            {
                return g.DpiX / 96.0;
            }
        }
        catch { }

        return 1.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private readonly MainViewModel _viewModel;
    private readonly Action _restoreAction;
    private readonly Action? _hideToTrayAction;
    private readonly Action? _exitAction;

    // Persisted coordinates: user-dragged position
    private static double? _savedLeft;
    private static double? _savedTop;

    // Cached auto-position to prevent drift/jumping between minimize-restore cycles
    private static double? _autoPlacedLeft;
    private static double? _autoPlacedTop;
    private static double? _targetRightDip;
    private static double _lastScreenWidth;

    private Point _dragStartPoint;
    private bool _isDragging;
    private readonly DispatcherTimer _zOrderTimer;

    public TaskbarStripWindow(MainViewModel viewModel, Action restoreAction, Action? hideToTrayAction = null, Action? exitAction = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _restoreAction = restoreAction;
        _hideToTrayAction = hideToTrayAction;
        _exitAction = exitAction;
        DataContext = _viewModel;

        // CRITICAL: Anchor the RIGHT EDGE to prevent any expansion over the tray chevron ^
        // Whenever content width changes (e.g. telemetry metrics load or values change),
        // shift Left to the left so the right edge stays rigidly 14 DIPs left of the chevron ^
        SizeChanged += (_, e) =>
        {
            if (!_isDragging && !_savedLeft.HasValue && _targetRightDip.HasValue && e.NewSize.Width > 40)
            {
                Left = Math.Max(20, _targetRightDip.Value - e.NewSize.Width);
                _autoPlacedLeft = Left;
            }
        };

        Loaded += (_, _) =>
        {
            PositionOnTaskbar();
            EnsureTopmost();
        };

        // Periodic timer to maintain Topmost Z-order above taskbar (even when windows maximize and taskbar becomes grey/opaque)
        _zOrderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _zOrderTimer.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                EnsureTopmost();
            }
        };
        _zOrderTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Attach as an owned window of Shell_TrayWnd
                // In Win32, an owned window is ALWAYS drawn above its owner in Z-order!
                IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
                if (hTaskbar != IntPtr.Zero)
                {
                    try
                    {
                        SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, hTaskbar);
                    }
                    catch { }
                }

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

                EnsureTopmost();
            }
        }
        catch { }
    }

    public void EnsureTopmost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch { }
    }

    public void PositionOnTaskbar(bool forceAuto = false)
    {
        MicroPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pillWidth = MicroPill.DesiredSize.Width;
        double pillHeight = MicroPill.DesiredSize.Height > 10 ? MicroPill.DesiredSize.Height : (MicroPill.Height > 10 ? MicroPill.Height : 36.0);
        double widgetHeight = ActualHeight > 10 ? ActualHeight : (DesiredSize.Height > 10 ? DesiredSize.Height : pillHeight);

        // 1. If user previously manually dragged the widget, preserve custom position (unless forceAuto requested)
        if (!forceAuto && _savedLeft.HasValue && _savedTop.HasValue)
        {
            Left = _savedLeft.Value;
            Top = _savedTop.Value;
            EnsureTopmost();
            return;
        }

        // Attach to user interactive desktop "Default" to ensure cross-station visibility
        try
        {
            IntPtr hDefault = OpenDesktop("Default", 0, false, 0x1FF);
            if (hDefault != IntPtr.Zero)
            {
                SetThreadDesktop(hDefault);
            }
        }
        catch { }

        // Find Shell_TrayWnd reliably via FindWindow or EnumWindows
        IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);
        if (hTaskbar == IntPtr.Zero)
        {
            EnumWindows((hwnd, _) =>
            {
                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, 256);
                if (cls.ToString() == "Shell_TrayWnd")
                {
                    hTaskbar = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        // Compute accurate DPI scale for the taskbar (handles 100%, 125%, 150%, 175%, 200% displays)
        double dpiScale = GetDpiScale(hTaskbar);

        try
        {
            if (hTaskbar != IntPtr.Zero && GetWindowRect(hTaskbar, out RECT tbRect))
            {
                double taskbarLeftDip = tbRect.Left / dpiScale;
                double taskbarRightDip = tbRect.Right / dpiScale;
                double taskbarTopDip = tbRect.Top / dpiScale;
                double taskbarHeightDip = (tbRect.Bottom - tbRect.Top) / dpiScale;

                double trayLeftDip = 0.0;
                string detectionSource = "None";

                // A. Instant, rock-solid Win32 detection via TrayNotifyWnd (Standard across Windows 10 & 11)
                // In Windows 11 & 10, TrayNotifyWnd.Left marks the EXACT physical start of the system tray!
                try
                {
                    IntPtr hTray = FindWindowEx(hTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (hTray != IntPtr.Zero && GetWindowRect(hTray, out RECT trRect))
                    {
                        int trW = trRect.Right - trRect.Left;
                        if (trRect.Left > 100 && trW > 15)
                        {
                            trayLeftDip = trRect.Left / dpiScale;
                            detectionSource = $"Win32_TrayNotifyWnd (Width={trW}px)";
                        }
                    }
                }
                catch { }

                // B. Targeted UI Automation Search across Shell_TrayWnd and XAML Island child windows
                if (trayLeftDip <= 0.0)
                {
                    try
                    {
                        var searchRoots = new List<System.Windows.Automation.AutomationElement>();
                        try
                        {
                            var trayRoot = System.Windows.Automation.AutomationElement.FromHandle(hTaskbar);
                            if (trayRoot != null) searchRoots.Add(trayRoot);
                        }
                        catch { }

                        EnumChildWindows(hTaskbar, (childHwnd, _) =>
                        {
                            try
                            {
                                var el = System.Windows.Automation.AutomationElement.FromHandle(childHwnd);
                                if (el != null) searchRoots.Add(el);
                            }
                            catch { }
                            return true;
                        }, IntPtr.Zero);

                        int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;

                        foreach (var root in searchRoots)
                        {
                            if (trayLeftDip > 100) break;

                            // Direct search for chevron button or known IDs
                            string[] chevronIds = new[]
                            {
                                "NotificationAreaOverflowButton",
                                "SystemTrayIconOverflowButton",
                                "OverflowButton"
                            };

                            System.Windows.Automation.AutomationElement? chevron = null;
                            try
                            {
                                chevron = root.FindFirst(
                                    System.Windows.Automation.TreeScope.Descendants,
                                    new System.Windows.Automation.PropertyCondition(
                                        System.Windows.Automation.AutomationElement.ClassNameProperty,
                                        "SystemTray.ChevronIconView"));
                            }
                            catch { }

                            if (chevron == null)
                            {
                                foreach (var id in chevronIds)
                                {
                                    try
                                    {
                                        chevron = root.FindFirst(
                                            System.Windows.Automation.TreeScope.Descendants,
                                            new System.Windows.Automation.PropertyCondition(
                                                System.Windows.Automation.AutomationElement.AutomationIdProperty, id));
                                        if (chevron != null) break;
                                    }
                                    catch { }
                                }
                            }

                            if (chevron != null)
                            {
                                try
                                {
                                    var r = chevron.Current.BoundingRectangle;
                                    if (r.Width > 5 && r.Left > 100)
                                    {
                                        trayLeftDip = r.Left / dpiScale;
                                        detectionSource = "UIA_Chevron";
                                        break;
                                    }
                                }
                                catch { }
                            }

                            // Search for leftmost button in the right tray zone
                            try
                            {
                                var btnCond = new System.Windows.Automation.PropertyCondition(
                                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                                    System.Windows.Automation.ControlType.Button);
                                var btns = root.FindAll(System.Windows.Automation.TreeScope.Descendants, btnCond);
                                double minLeftInZone = double.MaxValue;

                                foreach (System.Windows.Automation.AutomationElement b in btns)
                                {
                                    try
                                    {
                                        var cur = b.Current;
                                        if (cur.ProcessId == myPid) continue;
                                        var r = cur.BoundingRectangle;
                                        if (r.Width > 8 && r.Height > 8 &&
                                            r.Left >= (tbRect.Right - 700 * dpiScale) &&
                                            r.Left <= (tbRect.Right - 30 * dpiScale))
                                        {
                                            if (r.Left < minLeftInZone)
                                            {
                                                minLeftInZone = r.Left;
                                            }
                                        }
                                    }
                                    catch { }
                                }

                                if (minLeftInZone < double.MaxValue && minLeftInZone > 100)
                                {
                                    trayLeftDip = minLeftInZone / dpiScale;
                                    detectionSource = "UIA_MinTrayButton";
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // C. Fallback: Calibrated geometric distance from right edge
                // Average Windows tray width: PC ~150 DIPs, Laptop with battery ~220 DIPs
                if (trayLeftDip <= 0.0)
                {
                    bool isLaptop = IsLaptopWithBattery();
                    double fallbackTrayWidth = isLaptop ? 220.0 : 150.0;
                    trayLeftDip = taskbarRightDip - fallbackTrayWidth;
                    detectionSource = isLaptop ? "Fallback_Laptop_220" : "Fallback_PC_150";
                }

                // Place widget with a neat, compact gap of 10 DIPs to the left of the chevron ^
                double targetRight = trayLeftDip - 10.0;
                _targetRightDip = targetRight;

                double currentWidth = ActualWidth > 50 ? ActualWidth : (pillWidth > 50 ? pillWidth : 250.0);
                Left = Math.Max(20, targetRight - currentWidth);
                Top = taskbarTopDip + Math.Max(2, (taskbarHeightDip - widgetHeight) / 2.0);

                try
                {
                    System.IO.File.WriteAllText(@"c:\Users\PC FOR GAMER\Documents\VIOD\tray_live.log",
                        $"[{DateTime.Now:HH:mm:ss.fff}] Source={detectionSource}, trayLeftDip={trayLeftDip}, targetRight={targetRight}, Left={Left}, Width={currentWidth}, tbRight={taskbarRightDip}\r\n");
                }
                catch { }

                _autoPlacedLeft = Left;
                _autoPlacedTop = Top;
                _lastScreenWidth = SystemParameters.PrimaryScreenWidth;

                EnsureTopmost();
                return;
            }
        }
        catch { }

        // Ultimate WorkArea Fallback
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        double fallbackTaskbarTop = workArea.Bottom;
        double fallbackTaskbarHeight = screenHeight - workArea.Bottom;
        if (fallbackTaskbarHeight < 25) fallbackTaskbarHeight = 48;

        bool isLap = IsLaptopWithBattery();
        double trayW = isLap ? 220.0 : 150.0;

        double targetRightFallback = screenWidth - trayW - 10.0;
        _targetRightDip = targetRightFallback;
        double currentW = ActualWidth > 50 ? ActualWidth : (pillWidth > 50 ? pillWidth : 250.0);

        Top = fallbackTaskbarTop + Math.Max(2, (fallbackTaskbarHeight - widgetHeight) / 2.0);
        Left = Math.Max(20, targetRightFallback - currentW);

        _autoPlacedLeft = Left;
        _autoPlacedTop = Top;
        _lastScreenWidth = screenWidth;

        EnsureTopmost();
    }

    private void OnPillMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
    }

    private void OnPillMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
        {
            Point current = e.GetPosition(this);
            double dx = current.X - _dragStartPoint.X;
            double dy = current.Y - _dragStartPoint.Y;
            // Strict drag threshold: prevent normal clicks from triggering drag move
            if (Math.Sqrt(dx * dx + dy * dy) > 14)
            {
                _isDragging = true;
                _targetRightDip = null; // User manually positioned, disable auto right-edge anchor
                try
                {
                    DragMove();
                    _savedLeft = Left;
                    _savedTop = Top;
                }
                catch { }
            }
        }
    }

    private void OnPillMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            _restoreAction();
        }
        _isDragging = false;
    }

    private void OnContextMenuRestoreClicked(object sender, RoutedEventArgs e)
    {
        _restoreAction();
    }

    private void OnContextMenuTrayClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        _hideToTrayAction?.Invoke();
    }

    private void OnContextMenuResetPosClicked(object sender, RoutedEventArgs e)
    {
        _savedLeft = null;
        _savedTop = null;
        _autoPlacedLeft = null;
        _autoPlacedTop = null;
        _targetRightDip = null;
        PositionOnTaskbar(forceAuto: true);
    }

    private void OnContextMenuExitClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        if (_exitAction != null)
        {
            _exitAction();
        }
        else
        {
            Application.Current?.Shutdown();
            Environment.Exit(0);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _zOrderTimer.Stop();
        }
        catch { }
        base.OnClosed(e);
    }
}

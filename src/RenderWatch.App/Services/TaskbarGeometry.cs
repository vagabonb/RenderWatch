using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace RenderWatch.App.Services;

internal static class TaskbarGeometry
{
    private const double TrayGap = 10;

    internal readonly record struct Frame(nint Handle, Rect Bounds, double DpiScale)
    {
        internal Placement PlaceBeforeTray(double trayLeftPixels) => new(
            Bounds.Left / DpiScale, Bounds.Top / DpiScale, Bounds.Height / DpiScale,
            trayLeftPixels / DpiScale);
    }

    internal readonly record struct Placement(double TaskbarLeft, double TaskbarTop, double TaskbarHeight, double TrayLeft)
    {
        internal Point PositionWidget(Size size) => new(
            Math.Max(TaskbarLeft, TrayLeft - TrayGap - size.Width),
            TaskbarTop + Math.Max(0, (TaskbarHeight - size.Height) / 2));
    }

    internal static nint FindTaskbar() => FindWindow("Shell_TrayWnd", null);

    internal static Frame? ReadFrame()
    {
        nint handle = FindTaskbar();
        if (handle == 0 || !TryGetBounds(handle, out var bounds)) return null;
        uint dpi = GetDpiForWindow(handle);
        return new Frame(handle, bounds, dpi == 0 ? 1 : dpi / 96.0);
    }

    internal static Placement? ReadNativeTray(Frame frame)
    {
        nint tray = FindWindowEx(frame.Handle, 0, "TrayNotifyWnd", null);
        return tray != 0 && TryGetBounds(tray, out var bounds) && IsTrayBounds(frame, bounds)
            ? frame.PlaceBeforeTray(bounds.Left)
            : null;
    }

    internal static (Frame? Frame, Placement? Placement) ReadNativeGeometry()
    {
        var frame = ReadFrame();
        return (frame, frame is { } current ? ReadNativeTray(current) : null);
    }

    internal static Placement EstimatePlacement(Frame? frame)
    {
        double trayWidth = GetSystemPowerStatus(out var power) && (power.BatteryFlag & 128) == 0 && power.BatteryFlag != 255
            ? 220 : 150;
        if (frame is { } taskbar)
            return taskbar.PlaceBeforeTray(taskbar.Bounds.Right - trayWidth * taskbar.DpiScale);

        var workArea = SystemParameters.WorkArea;
        double height = SystemParameters.PrimaryScreenHeight - workArea.Bottom;
        return new Placement(workArea.Left, workArea.Bottom, height >= 25 ? height : 48,
            SystemParameters.PrimaryScreenWidth - trayWidth);
    }

    // UI Automation is a fallback for taskbars that do not expose a usable TrayNotifyWnd.
    // Call this on a background thread and keep the search inside the taskbar.
    internal static Placement? ReadAccessibleTray(Frame frame)
    {
        var roots = new List<nint> { frame.Handle };
        EnumChildWindows(frame.Handle, (handle, _) =>
        {
            var className = new StringBuilder(128);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Windows.UI.Composition.DesktopWindowContentBridge" or "TrayNotifyWnd")
                roots.Add(handle);
            return true;
        }, 0);

        var filter = new OrCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.ChevronIconView"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NotifyIconView"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.OmniButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayFrame"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NotificationAreaOverflowButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIconOverflowButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIconStack"));
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);

        double left = double.PositiveInfinity;
        foreach (nint handle in roots)
        {
            try
            {
                var root = AutomationElement.FromHandle(handle);
                using (cache.Activate())
                {
                    foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, filter))
                    {
                        var bounds = element.Cached.BoundingRectangle;
                        if (!element.Cached.IsOffscreen && IsTrayBounds(frame, bounds) &&
                            bounds.Left >= frame.Bounds.Right - 800 * frame.DpiScale)
                            left = Math.Min(left, bounds.Left);
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }

        return double.IsFinite(left) ? frame.PlaceBeforeTray(left) : null;
    }

    private static bool IsTrayBounds(Frame frame, Rect bounds) =>
        !bounds.IsEmpty && bounds.Width >= 8 && bounds.Height >= 8 &&
        bounds.Left >= frame.Bounds.Left &&
        bounds.Right <= frame.Bounds.Right + 4 * frame.DpiScale &&
        bounds.Bottom > frame.Bounds.Top && bounds.Top < frame.Bounds.Bottom;

    private static bool TryGetBounds(nint handle, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (!GetWindowRect(handle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
        bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    private delegate bool EnumWindow(nint window, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint window, EnumWindow callback, nint data);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}

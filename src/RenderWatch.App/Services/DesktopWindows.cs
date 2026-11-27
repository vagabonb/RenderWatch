using System.Runtime.InteropServices;
using System.Text;

namespace RenderWatch.App.Services;

internal static class DesktopWindows
{
    private const uint NearestMonitor = 2;
    private const uint RootOwner = 3;
    private const int WindowStyle = -16;
    private const int Caption = 0x00C00000;
    private const uint ExtendedFrameBounds = 9;
    private const uint Cloaked = 14;

    internal static Display? ReadDisplay(nint anchor)
    {
        nint monitor = MonitorFromWindow(anchor, NearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? new Display(monitor, info.Bounds, info.WorkArea) : null;
    }

    internal static bool IsForegroundFullscreen(Display display)
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return false;
        GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == 0 || processId == Environment.ProcessId) return false;
        if (IsWindowFullscreen(foreground, display)) return true;

        nint owner = GetAncestor(foreground, RootOwner);
        return owner != 0 && owner != foreground && IsWindowFullscreen(owner, display);
    }

    internal static bool IsWindowFullscreen(nint window, Display display)
    {
        if (!IsWindowVisible(window) || IsIconic(window) || MonitorFromWindow(window, NearestMonitor) != display.Handle)
            return false;

        bool hasVisibleBounds = DwmGetWindowAttribute(window, ExtendedFrameBounds, out PixelRect bounds, Marshal.SizeOf<PixelRect>()) == 0
            && bounds.Width > 0 && bounds.Height > 0;
        if (!hasVisibleBounds && !GetWindowRect(window, out bounds)) return false;
        bool maximizedWithCaption = IsZoomed(window) && (GetWindowLong(window, WindowStyle) & Caption) == Caption;
        if (!IsFullscreen(bounds, display, maximizedWithCaption, hasVisibleBounds)) return false;
        if (DwmGetWindowAttribute(window, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    internal static bool IsFullscreen(PixelRect window, Display display, bool maximizedWithCaption, bool hasVisibleBounds) =>
        Covers(window, display.Bounds) &&
        (!maximizedWithCaption || (hasVisibleBounds && !Covers(display.WorkArea, display.Bounds)));

    private static bool Covers(PixelRect outer, PixelRect inner) =>
        outer.Left <= inner.Left + 1 && outer.Top <= inner.Top + 1 &&
        outer.Right >= inner.Right - 1 && outer.Bottom >= inner.Bottom - 1;

    internal static void Align(nint window, PixelRect workArea, int topInset)
    {
        if (!GetWindowRect(window, out var bounds)) return;
        int x = workArea.Left + (workArea.Width - bounds.Width) / 2;
        int y = workArea.Top + topInset;
        if (bounds.Left != x || bounds.Top != y)
            SetWindowPos(window, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // Keep size, stacking order and focus.
    }

    internal readonly record struct Display(nint Handle, PixelRect Bounds, PixelRect WorkArea);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Bounds;
        public PixelRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out PixelRect bounds);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out PixelRect value, int size);
}

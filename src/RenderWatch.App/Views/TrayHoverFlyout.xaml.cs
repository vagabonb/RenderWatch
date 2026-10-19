using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RenderWatch.App.Views;

public partial class TrayHoverFlyout : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly Action _onRestoreAction;
    private readonly DispatcherTimer _closeTimer;
    private bool _isMouseInside;
    private DateTime _openedTime;

    public TrayHoverFlyout(object dataContext, Action onRestoreAction)
    {
        InitializeComponent();
        DataContext = dataContext;
        _onRestoreAction = onRestoreAction;

        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _closeTimer.Tick += CloseTimer_Tick;

        MouseEnter += (_, _) =>
        {
            _isMouseInside = true;
            _closeTimer.Stop(); // Never close while mouse is inside
        };

        MouseLeave += (_, _) =>
        {
            _isMouseInside = false;
            // Generous 700ms grace period before closing after leaving window
            _closeTimer.Interval = TimeSpan.FromMilliseconds(700);
            _closeTimer.Start();
        };

        Deactivated += (_, _) =>
        {
            if (!_isMouseInside)
            {
                HideFlyout();
            }
        };
    }

    public void ShowAtCursor()
    {
        GetCursorPos(out POINT ptPhysical);

        // Convert physical screen pixels to WPF device-independent units (DIPs)
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        double cursorX = ptPhysical.X / dpiScaleX;
        double cursorY = ptPhysical.Y / dpiScaleY;

        double screenW = SystemParameters.WorkArea.Right;
        double screenH = SystemParameters.WorkArea.Bottom;

        // Position directly above taskbar with minimal 6px gap
        Left = Math.Clamp(cursorX - (Width / 2.0), 10, screenW - Width - 10);
        Top  = Math.Clamp(cursorY - Height - 6, 10, screenH - Height - 6);

        _openedTime = DateTime.Now;
        _isMouseInside = false;

        Show();
        Activate();

        // 1.5 seconds initial grace timer for user to move mouse from tray into the window
        _closeTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _closeTimer.Start();
    }

    public void KeepAlive()
    {
        if (!IsVisible)
        {
            ShowAtCursor();
        }
        else if (!_isMouseInside)
        {
            // Reset initial grace timer while user is moving near tray
            _openedTime = DateTime.Now;
            _closeTimer.Interval = TimeSpan.FromMilliseconds(1200);
        }
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        // If mouse is inside or hovering the window, NEVER close
        if (_isMouseInside || IsMouseOver)
        {
            _closeTimer.Stop();
            return;
        }

        // Check if cursor is currently within the window or near the bottom gap
        GetCursorPos(out POINT ptPhysical);
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        double curX = ptPhysical.X / dpiScaleX;
        double curY = ptPhysical.Y / dpiScaleY;

        // Tolerant bounding box (includes 30px buffer below window toward tray icon)
        bool inTolerantArea = (curX >= Left - 20 && curX <= Left + Width + 20) &&
                             (curY >= Top - 10 && curY <= Top + Height + 40);

        if (inTolerantArea)
        {
            // User is moving towards or over the window - do not close
            return;
        }

        HideFlyout();
    }

    public void HideFlyout()
    {
        _closeTimer.Stop();
        _isMouseInside = false;
        Hide();
    }

    private void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _onRestoreAction();
    }
}
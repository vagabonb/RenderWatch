using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RenderWatch.App.Services;
using RenderWatch.App.ViewModels;

namespace RenderWatch.App.Views;

public partial class TaskbarStripWindow : Window
{
    private const int GwlExStyle = -20;
    private const int GwlHwndParent = -8;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = new(-1);
    private const uint KeepSizeAndPosition = 0x0001 | 0x0002;
    private const uint NoActivate = 0x0010;

    private readonly Action _restoreAction;
    private readonly Action? _hideToTrayAction;
    private readonly Action? _exitAction;
    private readonly Func<(TaskbarGeometry.Frame? Frame, TaskbarGeometry.Placement? Placement)> _readNativeGeometry;
    private readonly DispatcherTimer _taskbarTimer;
    private TaskbarGeometry.Frame? _frame;
    private TaskbarGeometry.Placement? _placement;
    private bool _readingAccessibleTray;
    private DateTime _nextAccessibleReadUtc;

    public TaskbarStripWindow(MainViewModel viewModel, Action restoreAction, Action? hideToTrayAction = null, Action? exitAction = null)
        : this(viewModel, restoreAction, TaskbarGeometry.ReadNativeGeometry, hideToTrayAction, exitAction)
    {
    }

    internal TaskbarStripWindow(MainViewModel viewModel, Action restoreAction,
        Func<(TaskbarGeometry.Frame? Frame, TaskbarGeometry.Placement? Placement)> readNativeGeometry,
        Action? hideToTrayAction = null, Action? exitAction = null)
    {
        InitializeComponent();
        _restoreAction = restoreAction;
        _hideToTrayAction = hideToTrayAction;
        _exitAction = exitAction;
        _readNativeGeometry = readNativeGeometry;
        DataContext = viewModel;

        // Keep the right edge anchored when a readout changes width (for example, from 99% to 100%).
        SizeChanged += (_, e) => AlignWidget(e.NewSize);

        _taskbarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _taskbarTimer.Tick += (_, _) =>
        {
            PositionOnTaskbar();
            EnsureTopmost();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                PositionOnTaskbar();
                _taskbarTimer.Start();
            }
            else
            {
                _taskbarTimer.Stop();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        nint owner = TaskbarGeometry.FindTaskbar();
        if (owner != 0)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hwnd, GwlHwndParent, owner);
            else
                SetWindowLong(hwnd, GwlHwndParent, owner.ToInt32());
        }

        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExToolWindow | WsExNoActivate);
        EnsureTopmost();
    }

    public void EnsureTopmost()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, KeepSizeAndPosition | NoActivate);
    }

    public void PositionOnTaskbar()
    {
        var (frame, native) = _readNativeGeometry();
        bool frameChanged = _frame != frame;
        _frame = frame;
        if (native is { } placement)
        {
            SetPlacement(placement);
            return;
        }

        // Keep the last known position if Explorer is briefly rebuilding the notification area.
        if (_placement is null || (frameChanged && frame.HasValue))
            SetPlacement(TaskbarGeometry.EstimatePlacement(frame));

        if (IsVisible && frame is { } taskbar && !_readingAccessibleTray && DateTime.UtcNow >= _nextAccessibleReadUtc)
            _ = ReadAccessibleTrayAsync(taskbar);
    }

    private async Task ReadAccessibleTrayAsync(TaskbarGeometry.Frame frame)
    {
        _readingAccessibleTray = true;
        try
        {
            var placement = await Task.Run(() => TaskbarGeometry.ReadAccessibleTray(frame));
            if (IsVisible && _frame == frame && placement is { } accessible &&
                TaskbarGeometry.ReadNativeTray(frame) is null)
            {
                SetPlacement(accessible);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray accessibility probe failed: {ex.Message}");
        }
        finally
        {
            _readingAccessibleTray = false;
            _nextAccessibleReadUtc = DateTime.UtcNow.AddSeconds(1);
        }
    }

    private void SetPlacement(TaskbarGeometry.Placement placement)
    {
        if (_placement == placement) return;
        _placement = placement;

        MicroPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = ActualWidth > 0 ? ActualWidth : MicroPill.DesiredSize.Width;
        double height = ActualHeight > 0 ? ActualHeight : MicroPill.DesiredSize.Height;
        AlignWidget(new Size(width, height));
    }

    private void AlignWidget(Size size)
    {
        if (_placement is not { } placement || size.IsEmpty || size.Width <= 0 || size.Height <= 0) return;
        var position = placement.PositionWidget(size);
        if (!double.IsFinite(Left) || Math.Abs(Left - position.X) >= 0.25) Left = position.X;
        if (!double.IsFinite(Top) || Math.Abs(Top - position.Y) >= 0.25) Top = position.Y;
    }

    private void OnPillMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _restoreAction();

    private void OnContextMenuRestoreClicked(object sender, RoutedEventArgs e) => _restoreAction();

    private void OnContextMenuTrayClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        _hideToTrayAction?.Invoke();
    }

    private void OnContextMenuResetPosClicked(object sender, RoutedEventArgs e)
    {
        _placement = null;
        _nextAccessibleReadUtc = DateTime.MinValue;
        PositionOnTaskbar();
    }

    private void OnContextMenuExitClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        if (_exitAction != null)
            _exitAction();
        else
            Application.Current?.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _taskbarTimer.Stop();
        base.OnClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int width, int height, uint flags);
}

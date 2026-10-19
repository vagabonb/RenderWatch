using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RenderWatch.App.ViewModels;

namespace RenderWatch.App.Views;

public partial class MicroWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly MainViewModel _viewModel;
    private readonly Action _restoreAction;
    private readonly Action? _hideToTrayAction;
    private readonly Action? _exitAction;

    private static double? _savedLeft;
    private static double? _savedTop;

    public MicroWindow(MainViewModel viewModel, Action restoreAction, Action? hideToTrayAction = null, Action? exitAction = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _restoreAction = restoreAction;
        _hideToTrayAction = hideToTrayAction;
        _exitAction = exitAction;
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
        }
        catch { }
    }

    public void PositionNearTray()
    {
        if (_savedLeft.HasValue && _savedTop.HasValue)
        {
            Left = _savedLeft.Value;
            Top = _savedTop.Value;
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double widgetWidth = DesiredSize.Width > 50 ? DesiredSize.Width : 320;
        double widgetHeight = DesiredSize.Height > 10 ? DesiredSize.Height : 38;

        Left = Math.Max(20, workArea.Right - widgetWidth - 24);
        Top = Math.Max(20, workArea.Bottom - widgetHeight - 16);
    }

    private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _restoreAction();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
                _savedLeft = Left;
                _savedTop = Top;
            }
            catch { }
        }
    }

    private void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        _restoreAction();
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

    private void OnCloseClicked(object sender, RoutedEventArgs e)
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
}

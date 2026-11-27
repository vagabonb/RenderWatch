using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RenderWatch.App.Services;
using RenderWatch.App.ViewModels;
using RenderWatch.Core.Models;

namespace RenderWatch.App.Views;

public partial class NotchWindow : Window
{
    internal const double ExpandedWidth = 468;
    private readonly DispatcherTimer _desktopTimer;
    private readonly NotchTelemetry _telemetry;
    private readonly MainViewModel _viewModel;
    private readonly Action _restore;
    private readonly Action _hideToTray;
    private readonly Func<DesktopWindows.Display, bool> _isFullscreen;
    private nint _anchor;
    private bool _expanded;

    public NotchWindow(MainViewModel viewModel, Action restore, Action hideToTray)
        : this(viewModel, restore, hideToTray, DesktopWindows.IsForegroundFullscreen)
    {
    }

    internal NotchWindow(MainViewModel viewModel, Action restore, Action hideToTray,
        Func<DesktopWindows.Display, bool> isFullscreen)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _telemetry = viewModel.Notch;
        _restore = restore;
        _hideToTray = hideToTray;
        _isFullscreen = isFullscreen;
        int index = 0;
        foreach (var option in viewModel.Constructor.NotchMetrics)
        {
            var item = new MenuItem { Header = option.Title, Tag = option.Metric, IsCheckable = true };
            item.Click += OnRingMetricClicked;
            HeaderButton.ContextMenu.Items.Insert(index++, item);
        }
        _desktopTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _desktopTimer.Tick += (_, _) => UpdateDesktop();
        SizeChanged += (_, _) => Align();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Align));
        Deactivated += (_, _) => SetExpanded(false);
    }

    public void ShowOnMonitorOf(Window anchor)
    {
        _anchor = new WindowInteropHelper(anchor).EnsureHandle();
        new WindowInteropHelper(this).EnsureHandle();
        _desktopTimer.Start();
        UpdateDesktop();
    }

    public void Stop()
    {
        _desktopTimer.Stop();
        SetExpanded(false);
        CloseHeaderMenu();
        Hide();
    }

    private void UpdateDesktop()
    {
        _telemetry.ExpireAlerts();
        if (DesktopWindows.ReadDisplay(_anchor) is not { } display) return;
        if (_isFullscreen(display))
        {
            if (IsVisible)
            {
                SetExpanded(false);
                CloseHeaderMenu();
                Hide();
            }
            return;
        }

        if (!IsVisible)
        {
            Align();
            Show();
        }
        Align();
    }

    private void Align()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || DesktopWindows.ReadDisplay(_anchor) is not { } display) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var layout = _viewModel.Constructor.NotchShape;
        double topInset = NotchHeader.TopInsetFor(layout);
        double width = Math.Min(_expanded ? ExpandedWidth : NotchHeader.WidthFor(layout),
            Math.Max(1, display.WorkArea.Width / dpi.DpiScaleX - 12));
        double maxHeight = Math.Max(1, display.WorkArea.Height / dpi.DpiScaleY - 12);
        if (Width != width) Width = width;
        if (MaxHeight != maxHeight) MaxHeight = maxHeight;
        DesktopWindows.Align(handle, display.WorkArea, (int)Math.Round(topInset * dpi.DpiScaleY));
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        _viewModel.SetNotchExpanded(expanded);
        Details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (!expanded) JournalToggle.IsChecked = false;
        Align();
    }

    private void OnOverviewCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var cards = (UniformGrid)sender;
        cards.Columns = e.NewSize.Width < 376 ? 2 : 4;
    }

    private void OnTransferCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var cards = (UniformGrid)sender;
        cards.Columns = e.NewSize.Width < 370 ? 1 : 2;
    }

    private void OnTogglePanel(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

    private void OnHeaderMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        bool ringMode = _viewModel.Constructor.NotchShape == NotchLayout.Ring;
        foreach (var entry in menu.Items)
        {
            if (entry is Separator separator) separator.Visibility = ringMode ? Visibility.Visible : Visibility.Collapsed;
            if (entry is not MenuItem { Tag: NotchRingMetric metric } item) continue;
            item.IsChecked = metric == _viewModel.PrimaryNotchReading.Metric;
            item.Visibility = ringMode && (metric != NotchRingMetric.Battery || _viewModel.Dashboard.Battery.IsPresent)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnRingMetricClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: NotchRingMetric metric }) _viewModel.Constructor.RingMetric = metric;
    }

    private void CloseHeaderMenu()
    {
        if (HeaderButton.ContextMenu is { IsOpen: true } menu) menu.IsOpen = false;
    }

    private void OnOpenMain(object sender, RoutedEventArgs e) => _restore();
    private void OnHideToTray(object sender, RoutedEventArgs e) => _hideToTray();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _expanded)
        {
            SetExpanded(false);
            HeaderButton.Focus();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _desktopTimer.Stop();
        base.OnClosed(e);
    }
}

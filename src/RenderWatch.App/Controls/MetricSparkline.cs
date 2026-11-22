using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace RenderWatch.App.Controls;

public class MetricSparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable<double>), typeof(MetricSparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public IEnumerable<double>? Points
    {
        get => (IEnumerable<double>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(MetricSparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double maximum && double.IsFinite(maximum) && maximum >= 0);

    // Zero selects an automatic scale for rates such as network traffic.
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(MetricSparkline),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is int capacity && (capacity == 0 || capacity >= 2));

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    private static readonly Pen LinePen = new(Brushes.WhiteSmoke, 1.35);
    private static readonly Brush FillBrush = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(85, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(26, 255, 255, 255), 0.65),
            new GradientStop(Colors.Transparent, 1)
        }
    };

    static MetricSparkline()
    {
        LinePen.Freeze();
        FillBrush.Freeze();
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var chart = (MetricSparkline)target;
        if (e.OldValue is INotifyCollectionChanged oldPoints) CollectionChangedEventManager.RemoveHandler(oldPoints, chart.OnCollectionChanged);
        if (e.NewValue is INotifyCollectionChanged newPoints) CollectionChangedEventManager.AddHandler(newPoints, chart.OnCollectionChanged);
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var points = Points?.ToArray();
        if (points == null || points.Length < 2 || ActualWidth < 2 || ActualHeight < 2) return;
        var maximum = MaxValue > 0 ? MaxValue : Math.Max(1, points.Max() * 1.12);
        var bottom = ActualHeight - 1;
        var plotHeight = ActualHeight - 2;
        var plotWidth = ActualWidth - 2;
        var startIndex = Capacity > 0 ? Math.Max(0, points.Length - Capacity) : 0;
        var intervals = Capacity > 0 ? Capacity - 1 : points.Length - 1;
        var firstX = 1 + plotWidth - (points.Length - 1 - startIndex) * plotWidth / intervals;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var fillContext = fill.Open())
        {
            var first = new Point(firstX, bottom - Math.Clamp(points[startIndex] / maximum, 0, 1) * plotHeight);
            lineContext.BeginFigure(first, false, false);
            fillContext.BeginFigure(new Point(firstX, bottom), true, true);
            fillContext.LineTo(first, false, false);

            for (int i = startIndex + 1; i < points.Length; i++)
            {
                var point = new Point(1 + plotWidth - (points.Length - 1 - i) * plotWidth / intervals,
                    bottom - Math.Clamp(points[i] / maximum, 0, 1) * plotHeight);
                lineContext.LineTo(point, true, false);
                fillContext.LineTo(point, false, false);
            }

            fillContext.LineTo(new Point(ActualWidth - 1, bottom), false, false);
        }
        line.Freeze();
        fill.Freeze();
        dc.DrawGeometry(FillBrush, null, fill);
        dc.DrawGeometry(null, LinePen, line);
    }
}

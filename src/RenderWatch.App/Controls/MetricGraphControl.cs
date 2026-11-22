using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RenderWatch.App.Controls;

public class MetricGraphControl : FrameworkElement
{
    public static readonly DependencyProperty PointsSourceProperty =
        DependencyProperty.Register(
            nameof(PointsSource),
            typeof(IEnumerable<double>),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsSourceChanged));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush),
            typeof(Brush),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentValueTextProperty =
        DependencyProperty.Register(
            nameof(CurrentValueText),
            typeof(string),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(
            nameof(MaxValue),
            typeof(double),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsFlatProperty = DependencyProperty.Register(
        nameof(IsFlat), typeof(bool), typeof(MetricGraphControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsFlat
    {
        get => (bool)GetValue(IsFlatProperty);
        set => SetValue(IsFlatProperty, value);
    }

    public IEnumerable<double>? PointsSource
    {
        get => (IEnumerable<double>?)GetValue(PointsSourceProperty);
        set => SetValue(PointsSourceProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string CurrentValueText
    {
        get => (string)GetValue(CurrentValueTextProperty);
        set => SetValue(CurrentValueTextProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public static readonly DependencyProperty CapacityProperty =
        DependencyProperty.Register(
            nameof(Capacity),
            typeof(int),
            typeof(MetricGraphControl),
            new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    private static void OnPointsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricGraphControl ctrl)
        {
            if (e.OldValue is INotifyCollectionChanged oldCol)
            {
                CollectionChangedEventManager.RemoveHandler(oldCol, ctrl.OnCollectionChanged);
            }
            if (e.NewValue is INotifyCollectionChanged newCol)
            {
                CollectionChangedEventManager.AddHandler(newCol, ctrl.OnCollectionChanged);
            }
            ctrl.InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    // Grid & Axis Boundary Pens
    private static readonly Brush CardBrush = new SolidColorBrush(Color.FromRgb(22, 22, 22));
    private static readonly Brush PlotBrush = new SolidColorBrush(Color.FromRgb(15, 15, 15));
    private static readonly Pen BlackTracePen = new(Brushes.WhiteSmoke, 1.7);
    private static readonly Brush EndpointHaloBrush = new SolidColorBrush(Color.FromArgb(44, 255, 255, 255));
    private static readonly Typeface BlackValueTypeface = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Pen TopBoundaryPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 1.0);
    private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1.0);
    private static readonly Pen BasePen = new Pen(new SolidColorBrush(Color.FromArgb(43, 255, 255, 255)), 1.0);
    private static readonly Pen AxisLinePen = new Pen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), 1.0);

    private static readonly Pen LinePen = new Pen(Brushes.WhiteSmoke, 1.15);
    private static readonly Brush FillBrush = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(64, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(22, 255, 255, 255), 0.65),
            new GradientStop(Colors.Transparent, 1)
        }
    };

    private static readonly Typeface TitleTypeface = new(new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface ValueTypeface = new(new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Light, FontStretches.Normal);
    private static readonly Typeface AxisTypeface = new(new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush TextMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8B7B1"));

    private static readonly Brush TextWhiteBrush = new SolidColorBrush(Colors.White);
    private static readonly Brush TextSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0CEC6"));
    private static readonly Brush AxisLabelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0"));
    static MetricGraphControl()
    {
        CardBrush.Freeze();
        PlotBrush.Freeze();
        BlackTracePen.Freeze();
        EndpointHaloBrush.Freeze();
        TopBoundaryPen.Freeze();
        GridPen.Freeze();
        BasePen.Freeze();
        TextMutedBrush.Freeze();
        AxisLinePen.Freeze();
        LinePen.Freeze();
        FillBrush.Freeze();
        TextWhiteBrush.Freeze();
        TextSecondaryBrush.Freeze();
        AxisLabelBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 60 || h < 60) return;

        if (!IsFlat)
            dc.DrawRoundedRectangle(CardBrush, null, new Rect(0, 0, w, h), 8, 8);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var pointsList = PointsSource as IList<double> ?? PointsSource?.ToList();
        int count = pointsList?.Count ?? 0;
        bool hasData = count > 0;
        double paddingH = IsFlat ? 0 : 14;
        double paddingTop = IsFlat ? 4 : 12;

        FormattedText FormatText(string value, double size, Brush brush, Typeface typeface) =>
            new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);

        var title = FormatText(Title.ToUpperInvariant(), 11, TextSecondaryBrush, TitleTypeface);
        dc.DrawText(title, new Point(paddingH, paddingTop));

        string stats = "МОНИТОРИНГ...";
        if (hasData)
        {
            double minimum = pointsList![0];
            double maximum = minimum;
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                double sample = pointsList[i];
                minimum = Math.Min(minimum, sample);
                maximum = Math.Max(maximum, sample);
                total += sample;
            }
            stats = $"МИН: {minimum:F0}{Unit}   МАКС: {maximum:F0}{Unit}   СРЕДН: {total / count:F0}{Unit}";
        }
        dc.DrawText(FormatText(stats, 9.5, TextMutedBrush, AxisTypeface), new Point(paddingH, paddingTop + 16));

        string reading = hasData ? $"{pointsList![count - 1]:F1} {Unit}" : "—";
        var value = FormatText(reading, 20, TextWhiteBrush, IsFlat ? ValueTypeface : BlackValueTypeface);
        dc.DrawText(value, new Point(w - paddingH - value.Width, paddingTop + 2));

        double graphTop = paddingTop + 36;
        double graphBottom = h - 14;
        double graphLeft = paddingH + 38;
        double graphRight = w - paddingH - 4;
        double graphWidth = graphRight - graphLeft;
        double graphHeight = graphBottom - graphTop;
        if (graphWidth <= 20 || graphHeight <= 20) return;

        if (!IsFlat)
        {
            dc.DrawRectangle(PlotBrush, null, new Rect(graphLeft, graphTop, graphWidth, graphHeight));
            dc.DrawLine(AxisLinePen, new Point(graphRight, graphTop), new Point(graphRight, graphBottom));
        }

        double scaleMax = Math.Max(1.0, MaxValue);
        dc.DrawLine(AxisLinePen, new Point(graphLeft, graphTop), new Point(graphLeft, graphBottom));

        for (int i = 1; i <= 3; i++)
        {
            double x = graphLeft + graphWidth * i / 4;
            dc.DrawLine(GridPen, new Point(x, graphTop), new Point(x, graphBottom));
        }

        for (int tick = 0; tick <= 2; tick++)
        {
            double fraction = tick / 2.0;
            double y = graphBottom - fraction * graphHeight;
            dc.DrawLine(AxisLinePen, new Point(graphLeft - 3, y), new Point(graphLeft, y));
            var pen = tick == 0 ? BasePen : tick == 2 ? TopBoundaryPen : GridPen;
            dc.DrawLine(pen, new Point(graphLeft, y), new Point(graphRight, y));
            var label = new FormattedText($"{scaleMax * fraction:F0}{Unit}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, AxisTypeface, tick == 1 ? 9 : 9.5,
                tick == 1 ? TextMutedBrush : AxisLabelBrush, dpi);
            dc.DrawText(label, new Point(graphLeft - label.Width - 6, y - 6));
        }

        if (!hasData) return;

        int cap = Math.Max(2, Capacity);
        double stepX = graphWidth / (cap - 1);
        int startIndex = Math.Max(0, count - cap);


        var lineGeom = new StreamGeometry();
        var fillGeom = new StreamGeometry();

        double firstPtX = graphRight - ((count - 1 - startIndex) * stepX);
        double firstPtY = graphBottom - Math.Clamp(pointsList![startIndex] / scaleMax, 0.0, 1.0) * graphHeight;

        double lastX = firstPtX;
        double lastY = firstPtY;

        using (var lineCtx = lineGeom.Open())
        using (var fillCtx = fillGeom.Open())
        {
            lineCtx.BeginFigure(new Point(firstPtX, firstPtY), isFilled: false, isClosed: false);
            fillCtx.BeginFigure(new Point(firstPtX, graphBottom), isFilled: true, isClosed: true);
            fillCtx.LineTo(new Point(firstPtX, firstPtY), false, false);
            for (int i = startIndex + 1; i < count; i++)
            {
                double px = graphRight - ((count - 1 - i) * stepX);
                double py = graphBottom - Math.Clamp(pointsList[i] / scaleMax, 0.0, 1.0) * graphHeight;
                lineCtx.LineTo(new Point(px, py), isStroked: true, isSmoothJoin: true);
                fillCtx.LineTo(new Point(px, py), false, true);

                lastX = px;
                lastY = py;
            }
            fillCtx.LineTo(new Point(lastX, graphBottom), false, false);
        }

        lineGeom.Freeze();
        fillGeom.Freeze();

        // Clip to graph area to avoid drawing outside boundaries
        dc.PushClip(new RectangleGeometry(new Rect(graphLeft - 1, graphTop - 2, graphWidth + 6, graphHeight + 4)));

        dc.DrawGeometry(FillBrush, null, fillGeom);
        dc.DrawGeometry(null, IsFlat ? LinePen : BlackTracePen, lineGeom);
        dc.Pop();

        if (!IsFlat)
            dc.DrawEllipse(EndpointHaloBrush, null, new Point(lastX, lastY), 4.6, 4.6);

        dc.DrawEllipse(TextWhiteBrush, null, new Point(lastX, lastY), 1.8, 1.8);
    }
}

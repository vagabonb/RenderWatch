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
                oldCol.CollectionChanged -= ctrl.OnCollectionChanged;
            }
            if (e.NewValue is INotifyCollectionChanged newCol)
            {
                newCol.CollectionChanged += ctrl.OnCollectionChanged;
            }
            ctrl.InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private static readonly Brush BgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141414"));
    private static readonly Pen BorderPen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")), 1.0);
    private static readonly Brush PlotBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D0D0D"));
    private static readonly Pen PlotBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1.0);
    
    // Grid & Axis Boundary Pens
    private static readonly Pen TopBoundaryPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 1.0);
    private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1.0);
    private static readonly Pen BasePen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#383838")), 1.2);
    private static readonly Pen AxisLinePen = new Pen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), 1.0);

    // Reusable line & fill brushes (frozen once, zero heap allocation during render!)
    private static readonly Brush FillBrush = new LinearGradientBrush(
        Color.FromArgb(38, 255, 255, 255),
        Color.FromArgb(2, 255, 255, 255),
        new Point(0, 0),
        new Point(0, 1));
    private static readonly Pen LinePen = new Pen(Brushes.White, 1.8);
    
    private static readonly Typeface TitleTypeface = new(new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface ValueTypeface = new(new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface StatsTypeface = new(new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface AxisTypeface  = new(new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Brush TextWhiteBrush = new SolidColorBrush(Colors.White);
    private static readonly Brush TextSecondaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E"));
    private static readonly Brush TextMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#707070"));
    private static readonly Brush AxisLabelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0"));
    private static readonly Brush DotHaloBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    static MetricGraphControl()
    {
        BgBrush.Freeze();
        BorderPen.Freeze();
        PlotBgBrush.Freeze();
        PlotBorderPen.Freeze();
        TopBoundaryPen.Freeze();
        GridPen.Freeze();
        BasePen.Freeze();
        AxisLinePen.Freeze();
        FillBrush.Freeze();
        LinePen.Freeze();
        TextWhiteBrush.Freeze();
        TextSecondaryBrush.Freeze();
        TextMutedBrush.Freeze();
        AxisLabelBrush.Freeze();
        DotHaloBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 60 || h < 60) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 1. Sleek Background Card with Rounded Corners (Pure Monochrome)
        dc.DrawRoundedRectangle(BgBrush, BorderPen, new Rect(0, 0, w, h), 8, 8);

        // 2. Extract Points List and Compute Statistics with ZERO Allocations
        IList<double>? pointsList = PointsSource as IList<double>;
        if (pointsList == null && PointsSource != null)
        {
            pointsList = PointsSource.ToList();
        }

        int count = pointsList?.Count ?? 0;
        bool hasData = count > 0;
        double currentVal = 0;
        double minVal = 0;
        double maxVal = 0;
        double avgVal = 0;

        if (hasData)
        {
            currentVal = pointsList![count - 1];
            minVal = pointsList[0];
            maxVal = pointsList[0];
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                double v = pointsList[i];
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
                sum += v;
            }
            avgVal = sum / count;
        }

        // 3. Card Header: Title & Big Readable Value
        double paddingH = 14;
        double paddingTop = 12;

        // Title
        var titleFt = new FormattedText(
            Title.ToUpperInvariant(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TitleTypeface,
            11,
            TextSecondaryBrush,
            dpi);
        dc.DrawText(titleFt, new Point(paddingH, paddingTop));

        // Stats summary line: "МИН: 12%  МАКС: 98%  СРЕДН: 45%"
        string statsText = hasData 
            ? $"МИН: {minVal:F0}{Unit}   МАКС: {maxVal:F0}{Unit}   СРЕДН: {avgVal:F0}{Unit}"
            : "МОНИТОРИНГ...";
        
        var statsFt = new FormattedText(
            statsText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            StatsTypeface,
            9.5,
            TextMutedBrush,
            dpi);
        dc.DrawText(statsFt, new Point(paddingH, paddingTop + 16));

        // Big prominent current value on top-right
        string displayValStr = hasData ? $"{currentVal:F1} {Unit}" : $"0.0 {Unit}";
        var valFt = new FormattedText(
            displayValStr,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            ValueTypeface,
            20,
            TextWhiteBrush,
            dpi);
        dc.DrawText(valFt, new Point(w - paddingH - valFt.Width, paddingTop + 2));

        // 4. Graph Layout Bounds
        double graphTop = paddingTop + 36;
        double graphBottom = h - 14;
        double axisWidth = 38; // accommodates "100%" or "105°C" cleanly
        double graphLeft = paddingH + axisWidth;
        double graphRight = w - paddingH - 4;
        double graphWidth = graphRight - graphLeft;
        double graphHeight = graphBottom - graphTop;

        if (graphWidth <= 20 || graphHeight <= 20) return;

        double scaleMax = Math.Max(1.0, MaxValue);

        // Plot Canvas Background with distinct subtle boundary
        dc.DrawRectangle(PlotBgBrush, PlotBorderPen, new Rect(graphLeft, graphTop, graphWidth, graphHeight));

        // Y-Axis Vertical Guide line & Ticks at graphLeft
        dc.DrawLine(AxisLinePen, new Point(graphLeft, graphTop), new Point(graphLeft, graphBottom));
        dc.DrawLine(AxisLinePen, new Point(graphLeft - 3, graphTop), new Point(graphLeft, graphTop));
        dc.DrawLine(AxisLinePen, new Point(graphLeft - 3, graphTop + (graphHeight * 0.5)), new Point(graphLeft, graphTop + (graphHeight * 0.5)));
        dc.DrawLine(AxisLinePen, new Point(graphLeft - 3, graphBottom), new Point(graphLeft, graphBottom));

        // Vertical Grid lines (25%, 50%, 75%) like Windows Task Manager
        for (int g = 1; g <= 3; g++)
        {
            double gx = graphLeft + (graphWidth * (g / 4.0));
            dc.DrawLine(GridPen, new Point(gx, graphTop), new Point(gx, graphBottom));
        }

        // 5. Y-Axis Grid Lines & Labels (100%, 50%, 0%) with Units and Clear Contrast
        // 100% Boundary line (Ceiling) & label with Unit
        dc.DrawLine(TopBoundaryPen, new Point(graphLeft, graphTop), new Point(graphRight, graphTop));
        var topLabel = new FormattedText($"{scaleMax:F0}{Unit}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 9.5, AxisLabelBrush, dpi);
        dc.DrawText(topLabel, new Point(graphLeft - topLabel.Width - 6, graphTop - 6));

        // 50% line & label
        double midY = graphTop + (graphHeight * 0.5);
        dc.DrawLine(GridPen, new Point(graphLeft, midY), new Point(graphRight, midY));
        var midLabel = new FormattedText($"{scaleMax * 0.5:F0}{Unit}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 9.0, TextMutedBrush, dpi);
        dc.DrawText(midLabel, new Point(graphLeft - midLabel.Width - 6, midY - 6));

        // 0% Baseline & label with Unit
        dc.DrawLine(BasePen, new Point(graphLeft, graphBottom), new Point(graphRight, graphBottom));
        var botLabel = new FormattedText($"0{Unit}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 9.5, AxisLabelBrush, dpi);
        dc.DrawText(botLabel, new Point(graphLeft - botLabel.Width - 6, graphBottom - 6));

        // 6. Draw Chart Curve & Gradient Fill (Tape / ribbon scrolling left, Task Manager style)
        if (!hasData)
        {
            return;
        }

        int cap = Math.Max(2, Capacity);
        double stepX = graphWidth / (cap - 1);
        int startIndex = count > cap ? count - cap : 0;

        // Build Line and Fill Geometries
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
            fillCtx.LineTo(new Point(firstPtX, firstPtY), isStroked: false, isSmoothJoin: false);

            for (int i = startIndex + 1; i < count; i++)
            {
                double px = graphRight - ((count - 1 - i) * stepX);
                double py = graphBottom - Math.Clamp(pointsList[i] / scaleMax, 0.0, 1.0) * graphHeight;
                lineCtx.LineTo(new Point(px, py), isStroked: true, isSmoothJoin: true);
                fillCtx.LineTo(new Point(px, py), isStroked: false, isSmoothJoin: false);

                lastX = px;
                lastY = py;
            }

            fillCtx.LineTo(new Point(lastX, graphBottom), isStroked: false, isSmoothJoin: false);
        }

        lineGeom.Freeze();
        fillGeom.Freeze();

        // Clip to graph area to avoid drawing outside boundaries
        dc.PushClip(new RectangleGeometry(new Rect(graphLeft - 1, graphTop - 2, graphWidth + 6, graphHeight + 4)));

        // Fill under curve (smooth monochrome fade)
        dc.DrawGeometry(FillBrush, null, fillGeom);

        // Stroke line
        dc.DrawGeometry(null, LinePen, lineGeom);

        // 7. Active Point Indicator (Marker Dot on current reading at right edge)
        dc.DrawEllipse(DotHaloBrush, null, new Point(lastX, lastY), 5.5, 5.5);
        dc.DrawEllipse(TextWhiteBrush, null, new Point(lastX, lastY), 2.5, 2.5);

        dc.Pop();
    }
}
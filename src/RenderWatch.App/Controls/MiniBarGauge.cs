using System.Windows;
using System.Windows.Media;

namespace RenderWatch.App.Controls;

public class MiniBarGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(MiniBarGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(MiniBarGauge),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Brush), typeof(MiniBarGauge),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public Brush? AccentColor
    {
        get => (Brush?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private static readonly Brush TrackBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222731"));
    private static readonly Brush WhiteBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
    private static readonly Brush AmberBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly Brush RedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

    static MiniBarGauge()
    {
        TrackBrush.Freeze();
        WhiteBrush.Freeze();
        AmberBrush.Freeze();
        RedBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double radius = h / 2.0;

        // Background track
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), radius, radius);

        // Filled portion
        double pct = Math.Clamp(Value / Math.Max(1.0, MaxValue), 0.0, 1.0);
        double fillWidth = w * pct;

        if (fillWidth > 0)
        {
            Brush barBrush = AccentColor ?? WhiteBrush;
            dc.DrawRoundedRectangle(barBrush, null, new Rect(0, 0, fillWidth, h), radius, radius);
        }
    }
}
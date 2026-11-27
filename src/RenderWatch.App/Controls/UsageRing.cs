using System.Windows;
using System.Windows.Media;

namespace RenderWatch.App.Controls;

public sealed class UsageRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double thickness && double.IsFinite(thickness) && thickness > 0);

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(SolidColorBrush), typeof(UsageRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SolidColorBrush? AccentBrush
    {
        get => (SolidColorBrush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var thickness = StrokeThickness;
        var radius = (Math.Min(ActualWidth, ActualHeight) - thickness) / 2 - 1;
        if (radius <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var percent = double.IsFinite(Value) ? Math.Clamp(Value, 0, 100) : 0;
        var color = AccentBrush ?? TelemetryPalette.ForLoad(Math.Round(percent));
        drawing.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(
            55, color.Color.R, color.Color.G, color.Color.B)), thickness), center, radius, radius);
        if (percent == 0) return;

        var pen = new Pen(color, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (percent == 100)
        {
            drawing.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var end = Math.Tau * percent / 100;
        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            geometry.ArcTo(new Point(center.X + radius * Math.Sin(end), center.Y - radius * Math.Cos(end)),
                new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true, false);
        }
        drawing.DrawGeometry(null, pen, arc);
    }
}

using System.Windows.Media;

namespace RenderWatch.App.Controls;

public static class TelemetryPalette
{
    public static SolidColorBrush Green { get; } = Ink(92, 217, 163);
    public static SolidColorBrush Blue { get; } = Ink(126, 177, 255);
    public static SolidColorBrush Orange { get; } = Ink(255, 176, 110);
    public static SolidColorBrush Red { get; } = Ink(255, 111, 130);
    public static SolidColorBrush Violet { get; } = Ink(192, 158, 255);
    public static SolidColorBrush Cyan { get; } = Ink(103, 212, 235);

    internal static SolidColorBrush ForLoad(double percent) => percent switch
    {
        >= 100 => Red,
        >= 80 => Orange,
        >= 50 => Blue,
        _ => Green
    };

    internal static SolidColorBrush ForBattery(int percent) => percent switch
    {
        <= 10 => Red,
        <= 25 => Orange,
        <= 50 => Blue,
        _ => Green
    };

    private static SolidColorBrush Ink(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

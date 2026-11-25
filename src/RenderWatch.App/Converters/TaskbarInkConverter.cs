using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RenderWatch.App.Converters;

public sealed class TaskbarInkConverter : IMultiValueConverter
{
    private static readonly Brush DarkLabel = Create(245, 242, 234);
    private static readonly Brush LightLabel = Create(23, 23, 23);
    private static readonly Brush LightValue = Create(10, 10, 10);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is Brush custom) return custom;
        bool light = values[1] is true;
        return Equals(parameter, "Label") ? (light ? LightLabel : DarkLabel) :
            (light ? LightValue : Brushes.White);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Create(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

using System.Windows;
using System.Windows.Controls;

namespace RenderWatch.App.Controls;

public static class MetricOrder
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(string), typeof(MetricOrder), new PropertyMetadata(null, OnOrderChanged));

    public static string? GetValue(DependencyObject target) => (string?)target.GetValue(ValueProperty);
    public static void SetValue(DependencyObject target, string? value) => target.SetValue(ValueProperty, value);

    private static void OnOrderChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Panel panel) return;
        void Reorder()
        {
            var order = (GetValue(panel) ?? "CPU,GPU,RAM,VRAM").Split(',');
            var tagged = panel.Children.OfType<FrameworkElement>()
                .Where(child => child.Tag is string tag && order.Contains(tag)).ToList();
            foreach (var child in tagged) panel.Children.Remove(child);
            int position = 0;
            foreach (string name in order)
            {
                var child = tagged.FirstOrDefault(item => Equals(item.Tag, name));
                if (child != null) panel.Children.Insert(position++, child);
            }
        }
        if (panel.IsLoaded) Reorder();
        else
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) => { panel.Loaded -= loaded; Reorder(); };
            panel.Loaded += loaded;
        }
    }
}

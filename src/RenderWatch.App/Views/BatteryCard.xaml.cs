using System.Windows;
using System.Windows.Controls;

namespace RenderWatch.App.Views;

public partial class BatteryCard : UserControl
{
    public static readonly DependencyProperty IsMonochromeProperty = DependencyProperty.Register(
        nameof(IsMonochrome), typeof(bool), typeof(BatteryCard), new PropertyMetadata(false));

    public bool IsMonochrome
    {
        get => (bool)GetValue(IsMonochromeProperty);
        set => SetValue(IsMonochromeProperty, value);
    }

    public BatteryCard() => InitializeComponent();
}

using System.Windows;
using System.Windows.Controls;

namespace RenderWatch.App.Views;

public partial class TaskbarStripContent : UserControl
{
    public static readonly DependencyProperty UseDarkInkProperty =
        DependencyProperty.Register(nameof(UseDarkInk), typeof(bool), typeof(TaskbarStripContent));

    public bool UseDarkInk
    {
        get => (bool)GetValue(UseDarkInkProperty);
        set => SetValue(UseDarkInkProperty, value);
    }

    public TaskbarStripContent()
    {
        InitializeComponent();
    }
}

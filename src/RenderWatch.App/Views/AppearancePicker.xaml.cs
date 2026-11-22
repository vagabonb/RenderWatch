using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RenderWatch.App.Views;

public partial class AppearancePicker : UserControl
{
    public AppearancePicker()
    {
        InitializeComponent();
    }

    private void OnOpenModes(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = button.ContextMenu!;
        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

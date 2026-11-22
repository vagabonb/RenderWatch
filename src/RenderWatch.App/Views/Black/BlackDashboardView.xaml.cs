using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RenderWatch.App.Views.Black;

public partial class BlackDashboardView : UserControl
{
    public BlackDashboardView()
    {
        InitializeComponent();
    }

    private void OnHardwareCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var cards = (UniformGrid)sender;
        cards.Columns = e.NewSize.Width < 760 ? 2 : 4;
    }
}

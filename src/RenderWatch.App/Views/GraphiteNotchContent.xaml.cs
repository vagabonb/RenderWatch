using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RenderWatch.App.Views;

public partial class GraphiteNotchContent : UserControl
{
    public GraphiteNotchContent() => InitializeComponent();

    private void OnMetricGroupsSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateColumns((UniformGrid)sender, e.NewSize.Width, 260);

    private void OnTransferGroupsSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateColumns((UniformGrid)sender, e.NewSize.Width, 315);

    private static void UpdateColumns(UniformGrid groups, double width, double minimumTwoColumnWidth)
    {
        double contentWidth = width + groups.Margin.Left + groups.Margin.Right;
        groups.Columns = contentWidth < minimumTwoColumnWidth ? 1 : 2;
    }
}

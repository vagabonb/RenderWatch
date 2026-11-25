using System.Windows.Controls;

namespace RenderWatch.App.Views;

public partial class CompactChartStrip : UserControl
{
    public const double ChartHeight = 94;
    internal const double TileHeight = ChartHeight + 5;

    public CompactChartStrip()
    {
        InitializeComponent();
    }
}

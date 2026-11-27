using System.Windows.Controls;
using RenderWatch.Core.Models;

namespace RenderWatch.App.Views;

public partial class NotchHeader : UserControl
{
    public const double RingWidth = 34;
    public const double IslandWidth = 128;
    public const double MemoryBarWidth = 108;

    public NotchHeader() => InitializeComponent();

    internal static double WidthFor(NotchLayout layout) => layout switch
    {
        NotchLayout.Island => IslandWidth,
        NotchLayout.MemoryBar => MemoryBarWidth,
        _ => RingWidth
    };

    internal static double TopInsetFor(NotchLayout layout) => layout switch
    {
        NotchLayout.Island => 0,
        NotchLayout.MemoryBar => 2,
        _ => 6
    };
}

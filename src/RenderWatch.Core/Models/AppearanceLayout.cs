namespace RenderWatch.Core.Models;

public class AppearanceLayout
{
    public WindowPlacement? Main { get; set; }
    public bool IsMainMaximized { get; set; }
    public WindowPlacement? Compact { get; set; }
    public WindowPlacement? Micro { get; set; }
}

public class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

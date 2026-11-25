using RenderWatch.App.Services;

namespace RenderWatch.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == PortableUpdateWorker.Command)
            return PortableUpdateWorker.Run(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

using RenderWatch.App.Services;
using System.Threading;
using System.Windows.Threading;

namespace RenderWatch.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == PortableUpdateWorker.Command)
            return PortableUpdateWorker.Run(args);

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PCWatch.Activate");
        using var instance = new Mutex(false, @"Local\PCWatch.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            if (args.Length == 0) activate.Set();
            return 0;
        }
        if (App.TryActivateLegacyInstance()) return 0;

        var app = new App();
        app.InitializeComponent();
        var registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
        {
            app.Dispatcher.InvokeAsync(app.ActivateFromSecondLaunch, DispatcherPriority.ApplicationIdle);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            return app.Run();
        }
        finally
        {
            registration.Unregister(null);
        }
    }
}

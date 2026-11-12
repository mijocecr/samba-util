using Avalonia;
using System;
using System.Threading.Tasks;
using SAMBA_Util.Helpers;

namespace SAMBA_Util;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
   
    public static async Task Main(string[] args)
    {
        bool hasDisplay = Environment.GetEnvironmentVariable("DISPLAY") != null ||
                          Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null;
        bool isTerminal = !Console.IsInputRedirected && !Console.IsOutputRedirected;

        if (!hasDisplay && isTerminal)
        {
            await CliApp.RunAsync();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }


    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
using Avalonia;
using Avalonia.Media;
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
            // Use the bundled Inter font as the default family. Without this,
            // Avalonia asks fontconfig for the system default and aborts when
            // the system has no fonts at all (e.g. minimal containers).
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .LogToTrace();
}
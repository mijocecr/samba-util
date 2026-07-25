using Avalonia;
using System;
using SAMBA_Util.Helpers;

namespace SAMBA_Util;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
   
    public static void Main(string[] args)
    {
        bool hasDisplay = Environment.GetEnvironmentVariable("DISPLAY") != null;
        bool isTerminal = !Console.IsInputRedirected && !Console.IsOutputRedirected;

        if (!hasDisplay && isTerminal)
        {
            CliApp.Run();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }


    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            
#endif
            .WithInterFont()
            .LogToTrace();
}
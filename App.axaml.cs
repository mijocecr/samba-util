using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SAMBA_Util.Helpers;
using SAMBA_Util.Views;

namespace SAMBA_Util;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Antes de abrir la ventana principal comprobamos que Samba esté
            // instalado. Si no lo está, mostramos un aviso con instrucciones y
            // cerramos la aplicación de forma limpia al cerrar esa ventana.
            if (SambaEnvironment.IsSambaInstalled())
            {
                desktop.MainWindow = new MainWindow();
            }
            else
            {
                desktop.MainWindow = new SambaMissingWindow();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

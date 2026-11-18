namespace SAMBA_Util.Helpers;

using System;

// Interpreta el estado de un servicio a partir de `systemctl show`.
//
// Se usa `systemctl show -p LoadState,ActiveState,UnitFileState` en lugar de
// `systemctl status` porque su salida es "clave=valor" y NO se traduce: los
// estados de systemd (loaded, active, inactive, failed…) son identificadores
// estables. `systemctl status` imprime etiquetas como "Loaded:"/"Active:" que
// systemd traduce al idioma del sistema, lo que produce falsos "Not installed"
// en equipos con el idioma distinto del inglés.
public static class ServiceInspector
{
    // Comprueba si systemd (systemctl) está disponible. Fuera de systemd la
    // detección de servicios no aplica y conviene decirlo en lugar de mostrar
    // "Not installed".
    public static bool HasSystemctl()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var result = ShellHelper.Ejecutar("command -v systemctl 2>/dev/null");
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout);
    }

    // Hechos de una unidad de systemd. Estados tal cual los devuelve systemd.
    public readonly record struct UnitState(string LoadState, string ActiveState, string UnitFileState);

    // Analiza la salida de `systemctl show`. Devuelve null si la unidad no
    // existe (LoadState=not-found) o si la salida no es reconocible.
    public static UnitState? ParseShow(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        string? load = null;
        string? active = null;
        string? file = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "LoadState": load = value; break;
                case "ActiveState": active = value; break;
                case "UnitFileState": file = value; break;
            }
        }

        // Sin ninguna de las claves esperadas no hay nada que interpretar.
        if (load == null && active == null)
            return null;

        // Una unidad inexistente responde LoadState=not-found.
        if (string.Equals(load, "not-found", StringComparison.OrdinalIgnoreCase))
            return null;

        return new UnitState(load ?? "", active ?? "", file ?? "");
    }

    // Texto para la interfaz a partir del estado activo. Se mantiene en inglés
    // como el resto de mensajes de la aplicación.
    public static string Describe(UnitState state)
    {
        return state.ActiveState.ToLowerInvariant() switch
        {
            "active" => "Active (running)",
            "reloading" => "Active (reloading)",
            "activating" => "Activating",
            "deactivating" => "Deactivating",
            "failed" => "Failed",
            "inactive" => "Inactive",
            "" => "Inactive",
            _ => state.ActiveState
        };
    }
}

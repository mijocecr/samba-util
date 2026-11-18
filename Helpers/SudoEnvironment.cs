namespace SAMBA_Util.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

// Comprueba si el usuario que ejecuta la sesión puede usar sudo.
//
// Sin privilegios de administrador SAMBA-Util no puede gestionar Samba, así que
// conviene detectarlo al arrancar y avisar, en lugar de pedir una contraseña que
// nunca va a funcionar. La detección es independiente de la distribución y del
// idioma: se apoya en la pertenencia a los grupos que conceden sudo y en la
// clasificación de `sudo -n -l` con los mensajes forzados al inglés (LC_ALL=C).
public static class SudoEnvironment
{
    // Grupos que conceden sudo en las principales distribuciones:
    // Debian/Ubuntu usan "sudo"; Fedora/RHEL/Arch/openSUSE usan "wheel";
    // algunas instalaciones antiguas de Debian usaban "admin".
    private static readonly string[] SudoGroups = { "sudo", "wheel", "admin" };

    // Hechos recogidos del sistema, separados de la decisión para poder
    // probarlos sin tocar el sistema real.
    public readonly record struct Probe(
        bool IsRoot,
        bool SudoBinaryPresent,
        IReadOnlyList<string> Groups,
        int NonInteractiveExitCode,
        string NonInteractiveOutput);

    // ¿El usuario pertenece a algún grupo que concede sudo?
    public static bool IsInSudoGroup(IReadOnlyList<string> groups) =>
        groups.Any(group => SudoGroups.Contains(group, StringComparer.OrdinalIgnoreCase));

    // Decide si el usuario puede usar sudo a partir de los hechos.
    public static bool HasSudoAccess(in Probe probe)
    {
        // root no necesita sudo.
        if (probe.IsRoot)
            return true;

        // Sin el binario sudo no hay privilegios que valgan.
        if (!probe.SudoBinaryPresent)
            return false;

        // Pertenecer a un grupo sudo/wheel/admin es la vía habitual.
        if (IsInSudoGroup(probe.Groups))
            return true;

        // `sudo -n -l` sin contraseña solo completa con NOPASSWD (código 0), pero
        // también lo ejecuta un sudoer normal: en ese caso responde "a password
        // is required". Se fuerza LC_ALL=C para que el mensaje esté en inglés.
        if (probe.NonInteractiveExitCode == 0)
            return true;

        if (probe.NonInteractiveOutput.Contains("password is required", StringComparison.OrdinalIgnoreCase))
            return true;

        // Cualquier otro caso ("is not in the sudoers file",
        // "is not allowed to run sudo", ...) indica que no puede.
        return false;
    }

    // Recoge los hechos del sistema actual. Es la única parte que ejecuta
    // comandos; la decisión vive en la función pura de arriba.
    public static Probe Inspect()
    {
        if (!OperatingSystem.IsLinux())
        {
            // Fuera de Linux no se bloquea: la aplicación está pensada para Linux,
            // pero no debe impedir su ejecución en otras plataformas.
            return new Probe(true, true, Array.Empty<string>(), 0, "");
        }

        var uid = ShellHelper.Ejecutar("id -u 2>/dev/null");
        bool isRoot = uid.ExitCode == 0 && uid.Stdout.Trim() == "0";

        var sudo = ShellHelper.Ejecutar("command -v sudo 2>/dev/null");
        bool sudoPresent = sudo.ExitCode == 0 && !string.IsNullOrWhiteSpace(sudo.Stdout);

        var groupsOut = ShellHelper.Ejecutar("id -nG 2>/dev/null");
        var groups = groupsOut.Stdout
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int exit = -1;
        string output = "";

        if (sudoPresent && !isRoot)
        {
            // -n evita cualquier petición interactiva; LC_ALL=C fija el idioma de
            // los mensajes para poder clasificarlos sin depender del locale.
            var list = ShellHelper.Ejecutar("LC_ALL=C sudo -n -l 2>&1");
            exit = list.ExitCode;
            output = (list.Stdout + "\n" + list.Stderr).Trim();
        }

        return new Probe(isRoot, sudoPresent, groups, exit, output);
    }

    public static bool CurrentUserHasSudo() => HasSudoAccess(Inspect());
}

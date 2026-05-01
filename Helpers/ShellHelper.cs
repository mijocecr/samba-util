using System;
using System.Diagnostics;
using System.Text;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
    public static (int ExitCode, string Output, string Error) EjecutarComoRoot(string args, string? extraInput = null)
    {
        Console.WriteLine("=== EjecutarComoRoot ===");
        Console.WriteLine($"Comando: sudo -S {args}");
        Console.WriteLine($"ExtraInput: {(extraInput != null ? extraInput.Replace("\n", "\\n") : "NULL")}");

        if (string.IsNullOrEmpty(Credenciales.AdminPassword))
        {
            Console.WriteLine("ERROR: AdminPassword está vacío");
            return (1, "", "Admin password is empty");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = "-S " + args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine("[STDOUT] " + e.Data);
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                Console.WriteLine("[STDERR] " + e.Data);
                errorBuilder.AppendLine(e.Data);
            }
        };

        Console.WriteLine("Iniciando proceso...");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 1) Contraseña sudo
        Console.WriteLine("Enviando contraseña sudo...");
        process.StandardInput.WriteLine(Credenciales.AdminPassword);

        // 2) Entrada adicional (smbpasswd)
        if (!string.IsNullOrEmpty(extraInput))
        {
            Console.WriteLine("Enviando extraInput...");
            process.StandardInput.Write(extraInput);
        }

        Console.WriteLine("Cerrando stdin...");
        process.StandardInput.Flush();
        process.StandardInput.Close();

        Console.WriteLine("Esperando fin de proceso...");
        process.WaitForExit();

        Console.WriteLine($"ExitCode: {process.ExitCode}");
        Console.WriteLine("=========================\n");

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}

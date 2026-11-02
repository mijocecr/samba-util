using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
    private static long _callCount = 0;

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command) =>
        EjecutarComoRoot(command, null);

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRootConEntrada(string command, string input) =>
        EjecutarComoRoot(command, input);

    private static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command, string? commandInput)
    {
        var callId = ++_callCount;
        var sw = Stopwatch.StartNew();

        var password = Credenciales.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine($"[SHELL] #{callId} Cancelado: no hay contraseña de administrador.");
            return (1002, "", "ADMIN_PASSWORD_REQUIRED");
        }

        var pass = password.TrimEnd('\r', '\n');
        if (pass.Contains('\r') || pass.Contains('\n'))
            throw new InvalidOperationException("La contraseña no puede contener saltos de línea.");

        Console.WriteLine($"[SHELL] #{callId} → EjecutarComoRoot('{command}')");

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-S");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("/bin/bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        Console.WriteLine($"[SHELL] #{callId} Iniciando proceso…");
        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            process.StandardInput.WriteLine(pass);
            if (commandInput != null)
                process.StandardInput.Write(commandInput);
        }
        catch (System.IO.IOException)
        {
            Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: el proceso cerró la entrada antes de recibir todos los datos.");
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (System.IO.IOException)
            {
                Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: no se pudo cerrar la entrada de sudo.");
            }
        }

        const int timeoutMs = 15000;
        Console.WriteLine($"[SHELL] #{callId} Esperando hasta {timeoutMs} ms…");

        if (!process.WaitForExit(timeoutMs))
        {
            Console.WriteLine($"[SHELL] #{callId} TIMEOUT tras {timeoutMs} ms. Matando proceso…");
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
            }
            return (1, "", "Timeout");
        }

        sw.Stop();

        string stdout = outputBuilder.ToString();
        string stderr = errorBuilder.ToString();

        Console.WriteLine($"[SHELL] #{callId} ← Finalizado en {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"[SHELL] #{callId} ExitCode={process.ExitCode}");
        Console.WriteLine($"[SHELL] #{callId} STDOUT='{stdout.Trim()}'");
        Console.WriteLine($"[SHELL] #{callId} STDERR='{stderr.Trim()}'");

        // 🔥 DETECCIÓN DE CONTRASEÑA INCORRECTA
        if (stderr.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Sorry, try again", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("no password was provided", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[SHELL] #{callId} → CONTRASEÑA INCORRECTA DETECTADA");
            return (1001, stdout, "PASSWORD_INCORRECT");
        }

        return (process.ExitCode, stdout, stderr);
    }
    
    public static (int ExitCode, string Stdout, string Stderr) Ejecutar(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    // ---------------------------------------------------------
    // EJECUCIÓN NORMAL (SIN ROOT)
    // ---------------------------------------------------------
    public static async Task<string> RunAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return stdoutBuilder.ToString() + stderrBuilder.ToString();
    }
}
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
    private static long _callCount = 0;

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command)
    {
        var callId = ++_callCount;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[SHELL] #{callId} → EjecutarComoRoot('{command}')");

        // 🔥 SIEMPRE usar bash -c para soportar redirecciones, sed, systemctl, etc.
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"-S bash -c \"{command.Replace("\"", "\\\"")}\"",
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

        // Enviar contraseña
        if (!string.IsNullOrEmpty(Credenciales.AdminPassword))
        {
            Console.WriteLine($"[SHELL] #{callId} Enviando contraseña…");
            var pass = Credenciales.AdminPassword.TrimEnd('\r', '\n');
            process.StandardInput.WriteLine(pass);
            process.StandardInput.Flush();
        }
        else
        {
            Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: No hay contraseña configurada");
        }

        process.StandardInput.Close();

        const int timeoutMs = 15000;
        Console.WriteLine($"[SHELL] #{callId} Esperando hasta {timeoutMs} ms…");

        if (!process.WaitForExit(timeoutMs))
        {
            Console.WriteLine($"[SHELL] #{callId} TIMEOUT tras {timeoutMs} ms. Matando proceso…");
            try { process.Kill(); } catch { }
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
    // Tiempos de espera. Se pueden sobreescribir por llamada.
    public const int DefaultTimeoutMs = 15_000;
    public const int LongTimeoutMs = 60_000;
    public const int NetworkTimeoutMs = 60_000;

    // Tiempo máximo para validar/renovar la credencial de sudo.
    private const int AuthTimeoutMs = 30_000;

    // Códigos de error propios. No se lanzan excepciones operativas.
    public const int ExitOk = 0;
    public const int ExitPasswordIncorrect = 1001;
    public const int ExitAdminPasswordRequired = 1002;
    public const int ExitInvalidPassword = 1003;
    public const int ExitTimeout = 1004;
    public const int ExitStartFailure = 1005;

    private static long _callCount;

    // La autenticación sudo es la fase frágil: dos procesos "sudo -S -v"
    // concurrentes pueden competir por el fichero de timestamp y fallar de
    // forma transitoria. Serializamos solo esta fase; una vez autenticada la
    // credencial, los comandos pueden seguir ejecutándose en paralelo.
    private static readonly SemaphoreSlim AuthLock = new(1, 1);

    // ---------------------------------------------------------
    // EJECUCIÓN COMO ROOT
    // ---------------------------------------------------------

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command) =>
        EjecutarComoRoot(command, null, DefaultTimeoutMs);

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command, int timeoutMs) =>
        EjecutarComoRoot(command, null, timeoutMs);

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRootConEntrada(string command, string input) =>
        EjecutarComoRoot(command, input, DefaultTimeoutMs);

    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRootConEntrada(string command, string input, int timeoutMs) =>
        EjecutarComoRoot(command, input, timeoutMs);

    private static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(
        string command, string? commandInput, int timeoutMs)
    {
        var callId = Interlocked.Increment(ref _callCount);
        var sw = Stopwatch.StartNew();

        if (!TryGetPassword(out var password, out var failure))
        {
            Console.WriteLine($"[SHELL] #{callId} Cancelado: {failure.Stderr}");
            return failure;
        }

        var effectiveTimeout = timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs;
        Console.WriteLine($"[SHELL] #{callId} → EjecutarComoRoot('{command}')");

        // 1-2) Autenticar/renovar la credencial de sudo de forma serializada.
        //      La contraseña nunca se mezcla con la entrada del comando.
        var authTimeout = Math.Min(effectiveTimeout, AuthTimeoutMs);
        var authFailure = Authenticate(password, authTimeout, callId);
        if (authFailure.HasValue)
            return authFailure.Value;

        // 3) Ejecutar el comando. commandInput va SOLO al comando, nunca a sudo.
        var result = RunProcess(
            "sudo",
            new[] { "-n", "--", "/bin/bash", "-c", command },
            commandInput,
            effectiveTimeout,
            callId,
            "comando root");

        sw.Stop();
        Console.WriteLine($"[SHELL] #{callId} ← Finalizado en {sw.ElapsedMilliseconds} ms (ExitCode={result.ExitCode})");
        return result;
    }

    // Devuelve null si la credencial quedó autenticada; en caso contrario,
    // el resultado de error que debe devolverse al llamador.
    private static (int ExitCode, string Stdout, string Stderr)? Authenticate(
        string password, int authTimeout, long callId)
    {
        // Espera acotada para no bloquear indefinidamente si otra
        // autenticación se quedó colgada.
        var lockTimeout = Math.Min(authTimeout, 10_000);
        if (!AuthLock.Wait(lockTimeout))
        {
            Console.WriteLine($"[SHELL] #{callId} TIMEOUT esperando el turno de autenticación sudo.");
            return (ExitTimeout, "", "TIMEOUT");
        }

        try
        {
            var auth = RunProcess(
                "sudo",
                new[] { "-S", "-p", "", "-v" },
                password + "\n",
                authTimeout,
                callId,
                "autenticación sudo");

            if (auth.ExitCode != 0)
            {
                // Un timeout o un fallo de arranque no son una contraseña incorrecta.
                if (auth.ExitCode is ExitTimeout or ExitStartFailure)
                    return auth;

                // sudo devuelve un código distinto de cero si la autenticación falla,
                // independientemente del idioma de sus mensajes.
                Console.WriteLine($"[SHELL] #{callId} → AUTENTICACIÓN FALLIDA");
                return (ExitPasswordIncorrect, auth.Stdout, FormatAuthError(auth.Stderr));
            }

            // Confirmar que la credencial cacheada se puede usar sin contraseña.
            var cacheCheck = RunProcess(
                "sudo",
                new[] { "-n", "-v" },
                null,
                authTimeout,
                callId,
                "verificación de credencial sudo");

            if (cacheCheck.ExitCode != 0)
            {
                if (cacheCheck.ExitCode is ExitTimeout or ExitStartFailure)
                    return cacheCheck;

                Console.WriteLine($"[SHELL] #{callId} → SUDO NO PUEDE USAR LA CREDENCIAL CACHEADA");
                return (ExitPasswordIncorrect, cacheCheck.Stdout, FormatAuthError(cacheCheck.Stderr));
            }

            return null;
        }
        finally
        {
            AuthLock.Release();
        }
    }

    private static bool TryGetPassword(
        out string password,
        out (int ExitCode, string Stdout, string Stderr) failure)
    {
        var raw = Credenciales.AdminPassword;

        if (string.IsNullOrWhiteSpace(raw))
        {
            password = "";
            failure = (ExitAdminPasswordRequired, "", "ADMIN_PASSWORD_REQUIRED");
            return false;
        }

        password = raw.TrimEnd('\r', '\n');
        if (password.Length == 0)
        {
            failure = (ExitAdminPasswordRequired, "", "ADMIN_PASSWORD_REQUIRED");
            return false;
        }

        if (password.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            failure = (ExitInvalidPassword, "", "INVALID_PASSWORD");
            return false;
        }

        failure = default;
        return true;
    }

    private static string FormatAuthError(string stderr)
    {
        var detail = stderr.Trim();
        return string.IsNullOrWhiteSpace(detail) ? "PASSWORD_INCORRECT" : detail;
    }

    // ---------------------------------------------------------
    // EJECUCIÓN NORMAL (SIN ROOT)
    // ---------------------------------------------------------

    public static (int ExitCode, string Stdout, string Stderr) Ejecutar(string command) =>
        Ejecutar(command, DefaultTimeoutMs);

    public static (int ExitCode, string Stdout, string Stderr) Ejecutar(string command, int timeoutMs)
    {
        var callId = ++_callCount;
        Console.WriteLine($"[SHELL] #{callId} → Ejecutar('{command}')");
        return RunProcess(
            "/bin/bash",
            new[] { "-c", command },
            null,
            timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs,
            callId,
            "comando");
    }

    public static Task<string> RunAsync(string command) =>
        RunAsync(command, NetworkTimeoutMs);

    public static async Task<string> RunAsync(string command, int timeoutMs)
    {
        var callId = ++_callCount;
        Console.WriteLine($"[SHELL] #{callId} → RunAsync('{command}')");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                Console.WriteLine($"[SHELL] #{callId} ERROR: no se pudo iniciar el proceso.");
                return stdout.ToString() + stderr.ToString() + "\nPROCESS_START_FAILED";
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            CloseInput(process, callId);

            var effectiveTimeout = timeoutMs <= 0 ? Timeout.Infinite : timeoutMs;
            using var cts = new CancellationTokenSource(effectiveTimeout);

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[SHELL] #{callId} TIMEOUT tras {timeoutMs} ms. Matando proceso…");
                KillProcess(process, callId);
                return stdout.ToString() + stderr.ToString() + "\nTIMEOUT";
            }

            // Garantiza que los lectores asíncronos hayan volcado.
            process.WaitForExit();
            return stdout.ToString() + stderr.ToString();
        }
        catch (Exception ex)
        {
            // Un fallo de shell nunca debe tumbar la aplicación.
            Console.WriteLine($"[SHELL] #{callId} ERROR en RunAsync: {ex.Message}");
            if (process != null)
                KillProcess(process, callId);
            return stdout.ToString() + stderr.ToString() + $"\nPROCESS_ERROR: {ex.Message}";
        }
        finally
        {
            process?.Dispose();
        }
    }

    // ---------------------------------------------------------
    // MOTOR COMÚN
    // ---------------------------------------------------------

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string? input,
        int timeoutMs,
        long callId,
        string phase)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };

            Console.WriteLine($"[SHELL] #{callId} Iniciando {phase}…");
            if (!process.Start())
            {
                Console.WriteLine($"[SHELL] #{callId} ERROR: no se pudo iniciar {phase}.");
                return (ExitStartFailure, stdout.ToString(), "PROCESS_START_FAILED");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (input != null)
            {
                try
                {
                    process.StandardInput.Write(input);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: el proceso cerró la entrada antes de recibir todos los datos.");
                }
            }

            CloseInput(process, callId);

            var effectiveTimeout = timeoutMs <= 0 ? Timeout.Infinite : timeoutMs;
            Console.WriteLine($"[SHELL] #{callId} Esperando hasta {DescribeTimeout(effectiveTimeout)}…");

            if (!process.WaitForExit(effectiveTimeout))
            {
                Console.WriteLine($"[SHELL] #{callId} TIMEOUT. Matando proceso…");
                KillProcess(process, callId);
                return (ExitTimeout, stdout.ToString(), "TIMEOUT");
            }

            // Garantiza que los lectores asíncronos hayan volcado el contenido.
            process.WaitForExit();

            return (process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            // Process.Start, WaitForExit, Kill… nunca deben propagarse al llamador.
            Console.WriteLine($"[SHELL] #{callId} ERROR al ejecutar {phase}: {ex.Message}");
            if (process != null)
                KillProcess(process, callId);
            return (ExitStartFailure, stdout.ToString(), $"PROCESS_START_FAILED: {ex.Message}");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void CloseInput(Process process, long callId)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: no se pudo cerrar la entrada del proceso.");
        }
    }

    private static void KillProcess(Process process, long callId)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(5_000))
                process.WaitForExit(); // vacía los lectores asíncronos antes de devolver
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SHELL] #{callId} ADVERTENCIA: no se pudo matar el proceso: {ex.Message}");
        }
    }

    private static string DescribeTimeout(int timeoutMs) =>
        timeoutMs == Timeout.Infinite ? "siempre" : $"{timeoutMs} ms";
}

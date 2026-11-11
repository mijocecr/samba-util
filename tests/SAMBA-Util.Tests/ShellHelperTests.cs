using System.Diagnostics;
using System.Threading.Tasks;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class ShellHelperTests
{
    [Fact]
    public void Ejecutar_CapturaStdoutYExitCode()
    {
        var result = ShellHelper.Ejecutar("printf 'hello'");

        Assert.Equal(ShellHelper.ExitOk, result.ExitCode);
        Assert.Equal("hello", result.Stdout.Trim());
        Assert.Equal(string.Empty, result.Stderr.Trim());
    }

    [Fact]
    public void Ejecutar_SeparaStdoutYStderr()
    {
        var result = ShellHelper.Ejecutar("printf 'out'; printf 'err' >&2");

        Assert.Equal(ShellHelper.ExitOk, result.ExitCode);
        Assert.Equal("out", result.Stdout.Trim());
        Assert.Equal("err", result.Stderr.Trim());
    }

    [Fact]
    public void Ejecutar_DevuelveCodigoDeSalidaDelComando()
    {
        var result = ShellHelper.Ejecutar("exit 7");

        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public void Ejecutar_DevuelveCodigoDeTimeout()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = ShellHelper.Ejecutar("sleep 5", 250);
        stopwatch.Stop();

        Assert.Equal(ShellHelper.ExitTimeout, result.ExitCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 4_000,
            $"El timeout tardó demasiado: {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task RunAsync_CapturaLaSalida()
    {
        var output = await ShellHelper.RunAsync("printf 'async'");

        Assert.Equal("async", output.Trim());
    }
}

using SAMBA_Util.Views;
using Xunit;

namespace SAMBA_Util.Tests;

public class StatusViewTests
{
    [Theory]
    [InlineData("Active (running)")]
    [InlineData("Active (reloading)")]
    [InlineData("Activating")]
    [InlineData("Deactivating")]
    [InlineData("Reloading")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ServiceHintFor_NoMuestraNadaCuandoElServicioEstaActivoOTranquilo(string? smbdText)
    {
        Assert.Null(StatusView.ServiceHintFor(smbdText));
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Failed")]
    [InlineData("Masked")]
    public void ServiceHintFor_IndicaElComandoCuandoElServicioEstaParado(string smbdText)
    {
        var hint = StatusView.ServiceHintFor(smbdText);

        Assert.NotNull(hint);
        Assert.Contains("systemctl enable --now smbd", hint);
        Assert.Contains("Start Samba", hint);
    }

    [Fact]
    public void ServiceHintFor_AvisaCuandoNoHaySystemd()
    {
        var hint = StatusView.ServiceHintFor("Unknown (systemd not available)");

        Assert.NotNull(hint);
        Assert.Contains("systemd is not available", hint);
    }

    [Fact]
    public void ServiceHintFor_AvisaCuandoFaltaLaUnidad()
    {
        var hint = StatusView.ServiceHintFor("Not installed");

        Assert.NotNull(hint);
        Assert.Contains("not found", hint);
    }
}

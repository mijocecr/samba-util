using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class SambaHelpersTests
{
    [Theory]
    [InlineData(null, "SMB2")]
    [InlineData("", "SMB2")]
    [InlineData("smb3", "SMB3")]
    [InlineData("NT1", "NT1")]
    [InlineData("SMB1", "NT1")]
    [InlineData("unknown", "SMB2")]
    public void NormalizeProtocol_DevuelveValorAdmitido(string? input, string expected)
    {
        Assert.Equal(expected, ConfigManager.NormalizeProtocol(input));
    }

    [Theory]
    [InlineData("homes", true)]
    [InlineData("HOMES", true)]
    [InlineData("printers", true)]
    [InlineData("print$", true)]
    [InlineData("IPC$", true)]
    [InlineData("myshare", false)]
    [InlineData("global", false)]
    public void IsSpecial_IdentificaSeccionesReservadas(string name, bool expected)
    {
        Assert.Equal(expected, SambaSections.IsSpecial(name));
    }

    [Fact]
    public void ParseList_ExtraeNombresDePdbedit()
    {
        var output = "alice:1000:Alice Smith\nbob:1001:Bob Jones\n";

        var users = SambaUsers.ParseList(output);

        Assert.Equal(new[] { "alice", "bob" }, users);
    }

    [Fact]
    public void ParseList_IgnoraLineasVaciasYDuplicados()
    {
        var output = "alice:1000:Alice\n\nalice:1000:Alice\n\r\n";

        var users = SambaUsers.ParseList(output);

        Assert.Equal(new[] { "alice" }, users);
    }

    [Fact]
    public void ParseList_OrdenaSinDistinguirMayusculas()
    {
        var output = "Zoe:1002:Zoe\nana:1000:Ana\nBob:1001:Bob\n";

        var users = SambaUsers.ParseList(output);

        Assert.Equal(new[] { "ana", "Bob", "Zoe" }, users);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseList_DevuelveListaVaciaSinSalida(string? output)
    {
        Assert.Empty(SambaUsers.ParseList(output));
    }
}

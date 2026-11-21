using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class NetworkScannerTests
{
    // La clasificación decide si la interfaz debe pedir credenciales. Solo un
    // rechazo de autenticación explícito debe abrir el diálogo; un error de red
    // o de otra índole no.
    [Theory]
    [InlineData("session setup failed: NT_STATUS_ACCESS_DENIED", ShareEnumerationOutcome.RequiresCredentials)]
    [InlineData("NT_STATUS_LOGON_FAILURE", ShareEnumerationOutcome.RequiresCredentials)]
    [InlineData("NT_STATUS_ACCOUNT_DISABLED", ShareEnumerationOutcome.RequiresCredentials)]
    [InlineData("NT_STATUS_ACCOUNT_LOCKED_OUT", ShareEnumerationOutcome.AccountLockedOut)]
    [InlineData("NT_STATUS_PASSWORD_MUST_CHANGE", ShareEnumerationOutcome.RequiresCredentials)]
    [InlineData("NT_STATUS_NO_LOGON_SERVERS", ShareEnumerationOutcome.RequiresCredentials)]
    [InlineData("do_connect: Connection to 10.0.0.1 failed (Error NT_STATUS_HOST_UNREACHABLE)", ShareEnumerationOutcome.Failed)]
    [InlineData("NT_STATUS_CONNECTION_REFUSED", ShareEnumerationOutcome.Failed)]
    [InlineData("NT_STATUS_BAD_NETWORK_NAME", ShareEnumerationOutcome.Failed)]
    [InlineData("", ShareEnumerationOutcome.Failed)]
    public void ClassifyEnumerationFailure_MapeaElMotivo(string output, ShareEnumerationOutcome expected)
    {
        Assert.Equal(expected, NetworkScanner.ClassifyEnumerationFailure(output));
    }

    // Windows (y algunos NAS) imprimen un NT_STATUS por una operación
    // secundaria mientras enumeran los recursos. Esos shares no se deben
    // descartar: antes se perdían y la interfase mostraba "Unable to
    // enumerate SMB shares" aunque la autenticación hubiera funcionado.
    [Fact]
    public void EvaluateSmbClientOutput_ConSharesYNtStatusDevuelveLosShares()
    {
        const string output = "tree connect failed: NT_STATUS_ACCESS_DENIED\n" +
                              "Disk|Docs|Shared documents\n" +
                              "IPC|IPC$|IPC Service";

        var result = NetworkScanner.EvaluateSmbClientOutput("192.168.10.58", output, authenticated: true);

        Assert.Equal(ShareEnumerationOutcome.Success, result.Outcome);
        Assert.Single(result.Shares);
        Assert.Equal("Docs", result.Shares[0].Name);
        Assert.Equal("Authenticated", result.Shares[0].Access);
    }

    [Fact]
    public void EvaluateSmbClientOutput_SoloErrorDeAutenticacionPideCredenciales()
    {
        const string output = "session setup failed: NT_STATUS_LOGON_FAILURE";

        var result = NetworkScanner.EvaluateSmbClientOutput("192.168.10.58", output, authenticated: true);

        Assert.Equal(ShareEnumerationOutcome.RequiresCredentials, result.Outcome);
        Assert.Empty(result.Shares);
    }

    // macOS con guest activado anuncia `nobody`, que no es un share real.
    // Debe pedir credenciales antes de mostrarlo en la interfaz.
    [Fact]
    public void EvaluateSmbClientOutput_GuestSoloNobodyPideCredenciales()
    {
        const string output = "IPC|IPC$|\n" +
                              "Disk|nobody|\n" +
                              "SMB1 disabled -- no workgroup available";

        var result = NetworkScanner.EvaluateSmbClientOutput("192.168.10.65", output, authenticated: false);

        Assert.Equal(ShareEnumerationOutcome.RequiresCredentials, result.Outcome);
        Assert.Empty(result.Shares);
    }

    [Fact]
    public void EvaluateSmbClientOutput_GuestConShareRealIgnoraNobody()
    {
        const string output = "IPC|IPC$|\n" +
                              "Disk|nobody|\n" +
                              "Disk|Public|Shared files";

        var result = NetworkScanner.EvaluateSmbClientOutput("192.168.10.65", output, authenticated: false);

        Assert.Equal(ShareEnumerationOutcome.Success, result.Outcome);
        Assert.Single(result.Shares);
        Assert.Equal("Public", result.Shares[0].Name);
    }

    // En macOS `-U %` solo sirve para IPC$: ante LOGON_FAILURE hay que
    // reintentar con el usuario `guest`. No queremos reintentar otros errores
    // (por ejemplo una cuenta bloqueada) para no generar intentos extra.
    [Theory]
    [InlineData("session setup failed: NT_STATUS_LOGON_FAILURE", true)]
    [InlineData("NT_STATUS_ACCOUNT_LOCKED_OUT", false)]
    [InlineData("NT_STATUS_ACCESS_DENIED", false)]
    [InlineData("Disk|Docs|Shared documents", false)]
    [InlineData("", false)]
    public void ShouldRetryAnonymousAsGuest_SoloAnteLogonFailure(string output, bool expected)
    {
        Assert.Equal(expected, NetworkScanner.ShouldRetryAnonymousAsGuest(output));
    }

    // Con credenciales explícitas no se debe caer al invitado: en macOS el
    // invitado puede devolver un share `nobody` que ocultaría los reales.
    [Fact]
    public void ShouldReturnAuthenticatedResult_PrefiereCredencialesConShares()
    {
        var result = new ShareEnumerationResult
        {
            Outcome = ShareEnumerationOutcome.Success,
            Shares = [new NetworkShare("Docs")]
        };

        Assert.True(NetworkScanner.ShouldReturnAuthenticatedResult(result));
    }

    [Theory]
    [InlineData(ShareEnumerationOutcome.RequiresCredentials, true)]
    [InlineData(ShareEnumerationOutcome.AccountLockedOut, true)]
    [InlineData(ShareEnumerationOutcome.Success, false)]
    [InlineData(ShareEnumerationOutcome.Failed, false)]
    [InlineData(ShareEnumerationOutcome.ClientMissing, false)]
    public void ShouldReturnAuthenticatedResult_PropagaSoloRechazos(
        ShareEnumerationOutcome outcome, bool expected)
    {
        var result = new ShareEnumerationResult { Outcome = outcome };

        Assert.Equal(expected, NetworkScanner.ShouldReturnAuthenticatedResult(result));
    }

    [Fact]
    public void ParseRpcShares_IgnoraRuidoNT_STATUSyRecursosDelSistema()
    {
        const string output = "Cannot connect: NT_STATUS_ACCESS_DENIED\n" +
                              "netname: ADMIN$\n" +
                              "netname: Datos\n" +
                              "netname: IPC$";

        var shares = NetworkScanner.ParseRpcShares("192.168.10.58", output);

        Assert.Single(shares);
        Assert.Equal("Datos", shares[0].Name);
        Assert.Equal("Windows RPC", shares[0].Comment);
    }
}

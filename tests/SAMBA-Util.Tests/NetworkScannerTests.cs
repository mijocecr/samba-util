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
    [InlineData("NT_STATUS_ACCOUNT_LOCKED_OUT", ShareEnumerationOutcome.RequiresCredentials)]
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
}

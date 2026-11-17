using System;
using System.IO;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class UsersharesIncludeTests
{
    [Fact]
    public void LoadShares_ResuelveIncludeAUsersharesConf()
    {
        var dir = CreateTempDir();
        try
        {
            var usershares = Path.Combine(dir, "usershares.conf");
            File.WriteAllText(usershares,
                "[global]\n" +
                "   usershare path = /var/lib/samba/usershares\n" +
                "   usershare max shares = 100\n" +
                "   usershare allow guests = yes\n");

            var smbConf = Path.Combine(dir, "smb.conf");
            File.WriteAllText(smbConf,
                "[global]\n" +
                "   workgroup = WORKGROUP\n" +
                $"   include = {usershares}\n" +
                "\n" +
                "[datos]\n" +
                "   path = /srv/datos\n" +
                "   read only = no\n");

            var shares = SambaConfigReader.LoadShares(smbConf);

            var share = Assert.Single(shares);
            Assert.Equal("datos", share.Name);
            Assert.Equal("/srv/datos", share.Path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Documenta el motivo de crear usershares.conf por defecto en
    // SmbInitializer: un include que apunta a un archivo inexistente aborta la
    // carga de la configuración.
    [Fact]
    public void LoadShares_FallaSiElIncludeNoExiste()
    {
        var dir = CreateTempDir();
        try
        {
            var smbConf = Path.Combine(dir, "smb.conf");
            File.WriteAllText(smbConf,
                "[global]\n" +
                $"   include = {Path.Combine(dir, "usershares.conf")}\n");

            Assert.Throws<FileNotFoundException>(() => SambaConfigReader.LoadShares(smbConf));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"samba-util-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

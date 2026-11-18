using System;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class SudoEnvironmentTests
{
    private static SudoEnvironment.Probe Probe(
        bool isRoot, bool sudoBinary, string groups, int exit, string output) =>
        new(isRoot, sudoBinary, groups.Split(',', StringSplitOptions.RemoveEmptyEntries), exit, output);

    // La decisión debe reconocer como sudoer tanto a quien está en los grupos
    // habituales como a quien aparece en sudoers (lo delata la respuesta en
    // inglés de `sudo -n -l`), y no confundir un fallo de red u otro mensaje.
    [Theory]
    [InlineData(true, false, "", 1, "", true)]                                                    // root
    [InlineData(false, false, "sudo", 1, "", false)]                                              // sin binario sudo
    [InlineData(false, true, "sudo", 1, "is not in the sudoers file.", true)]                     // grupo sudo
    [InlineData(false, true, "wheel", 1, "is not in the sudoers file.", true)]                    // grupo wheel
    [InlineData(false, true, "wheel,audio,video", 1, "x", true)]                                  // grupo entre otros
    [InlineData(false, true, "users", 0, "", true)]                                               // NOPASSWD (sudo -n -l ok)
    [InlineData(false, true, "users", 1, "sudo: a password is required", true)]                   // sudoer con contraseña
    [InlineData(false, true, "users", 1, "miguel is not in the sudoers file.", false)]            // no sudoer
    [InlineData(false, true, "users", 1, "Sorry, user miguel is not allowed to run sudo on h.", false)]
    [InlineData(false, true, "users", 1, "", false)]
    public void HasSudoAccess_Decide(bool isRoot, bool sudoBinary, string groups, int exit, string output, bool expected)
    {
        Assert.Equal(expected, SudoEnvironment.HasSudoAccess(Probe(isRoot, sudoBinary, groups, exit, output)));
    }

    [Theory]
    [InlineData("sudo", true)]
    [InlineData("wheel", true)]
    [InlineData("admin", true)]
    [InlineData("WHEEL", true)]
    [InlineData("users,audio", false)]
    [InlineData("", false)]
    public void IsInSudoGroup_DetectaGrupos(string groups, bool expected)
    {
        Assert.Equal(expected, SudoEnvironment.IsInSudoGroup(groups.Split(',', StringSplitOptions.RemoveEmptyEntries)));
    }
}

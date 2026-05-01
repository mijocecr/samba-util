using System;

namespace SAMBA_Util.ViewModels;

public class PasswordDialogViewModel
{
    private readonly Action<string> _onPassword;

    public PasswordDialogViewModel(Action<string> onPassword)
    {
        _onPassword = onPassword;
    }

    public void OnPasswordEntered(string password)
    {
        _onPassword(password);
    }
}
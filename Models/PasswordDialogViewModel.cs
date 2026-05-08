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
        // Aquí NO se valida nada.
        // Aquí NO se cierra el diálogo.
        // Solo se pasa el password a MainWindow.
        _onPassword(password);
    }
}
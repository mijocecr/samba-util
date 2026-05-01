using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Models;
using Avalonia.Platform.Storage;

namespace SAMBA_Util.Views;

public partial class ShareEditorWindow : Window
{
    private Share _original;

    public bool Saved { get; private set; } = false;

    
    
    // ⭐ Constructor vacío (solo para el diseñador)
    public ShareEditorWindow()
    {
        InitializeComponent();

        // Evita nulls si el diseñador intenta guardar
        _original = new Share();
    }

    // ⭐ Constructor real (editar o agregar)
    public ShareEditorWindow(Share share)
    {
        InitializeComponent();
        _original = share;

        TxtName.Text = share.Name;
        TxtPath.Text = share.Path;
        TgRO.IsChecked = share.ReadOnly;
        TgGuests.IsChecked = share.AllowGuests;
    }

    // ⭐ Folder Picker moderno (Avalonia 11)
    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for the share",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0)
        {
            TxtPath.Text = folders[0].Path.LocalPath;
        }
    }

    // ⭐ Guardar cambios
   
    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _original.Name = TxtName.Text;
        _original.Path = TxtPath.Text;
        _original.ReadOnly = TgRO.IsChecked ?? false;
        _original.AllowGuests = TgGuests.IsChecked ?? false;

        Saved = true;
        Close(_original);   // ⭐ DEVUELVE EL SHARE EDITADO
    }

    
}
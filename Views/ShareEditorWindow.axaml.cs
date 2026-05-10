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
        _original = new Share();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // ⭐ Constructor real (editar o agregar)
    public ShareEditorWindow(Share share)
    {
        InitializeComponent();
        _original = share;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // BASIC
        TxtName.Text = share.Name;
        TxtPath.Text = share.Path;
        TgRO.IsChecked = share.ReadOnly;
        TgGuests.IsChecked = share.AllowGuests;
        TgBrowseable.IsChecked = share.Browseable;
        TxtComment.Text = share.Comment;

        // USERS & PERMISSIONS
        TxtValidUsers.Text = share.ValidUsers;
        TxtWriteList.Text = share.WriteList;
        TxtReadList.Text = share.ReadList;

        // FORCE USER/GROUP
        TxtForceUser.Text = share.ForceUser;
        TxtForceGroup.Text = share.ForceGroup;

        // MASKS (seleccionar en ComboBox)
        SelectComboByTag(CmbCreateMask, share.CreateMask);
        SelectComboByTag(CmbDirectoryMask, share.DirectoryMask);
    }

    // ⭐ Helper para seleccionar ComboBoxItem por Tag
    private void SelectComboByTag(ComboBox combo, string? tag)
    {
        if (tag == null)
            return;

        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                combo.SelectedItem = cbi;
                break;
            }
        }
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
        // BASIC
        _original.Name = TxtName.Text ?? "";
        _original.Path = TxtPath.Text ?? "";
        _original.ReadOnly = TgRO.IsChecked ?? false;
        _original.AllowGuests = TgGuests.IsChecked ?? false;
        _original.Browseable = TgBrowseable.IsChecked ?? true;
        _original.Comment = TxtComment.Text ?? "";

        // USERS & PERMISSIONS
        _original.ValidUsers = TxtValidUsers.Text ?? "";
        _original.WriteList = TxtWriteList.Text ?? "";
        _original.ReadList = TxtReadList.Text ?? "";

        // FORCE USER/GROUP
        _original.ForceUser = TxtForceUser.Text ?? "";
        _original.ForceGroup = TxtForceGroup.Text ?? "";

        // MASKS (desde ComboBox)
        _original.CreateMask = GetSelectedTag(CmbCreateMask, "755");
        _original.DirectoryMask = GetSelectedTag(CmbDirectoryMask, "755");

        Saved = true;
        Close(_original);   // ⭐ DEVUELVE EL SHARE EDITADO
    }

    // ⭐ Helper para obtener Tag del ComboBox
    private string GetSelectedTag(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag)
        {
            return tag;
        }

        return fallback;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Models;
using Avalonia.Platform.Storage;

namespace SAMBA_Util.Views;

public partial class ShareEditorWindow : Window
{
    private Share _original;

    public bool Saved { get; private set; } = false;

    public ShareEditorWindow()
    {
        InitializeComponent();
        _original = new Share();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

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

        // SYMLINKS
        TgFollowSymlinks.IsChecked = share.FollowSymlinks;
        TgWideLinks.IsChecked = share.WideLinks;

        // USERS & PERMISSIONS
        TxtValidUsers.Text = share.ValidUsers;
        TxtWriteList.Text = share.WriteList;
        TxtReadList.Text = share.ReadList;

        // FORCE USER/GROUP
        TxtForceUser.Text = share.ForceUser;
        TxtForceGroup.Text = share.ForceGroup;

        // MASKS
        SelectComboByTag(CmbCreateMask, share.CreateMask);
        SelectComboByTag(CmbDirectoryMask, share.DirectoryMask);
    }

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

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _original.Name = TxtName.Text ?? "";
        _original.Path = TxtPath.Text ?? "";
        _original.ReadOnly = TgRO.IsChecked ?? false;
        _original.AllowGuests = TgGuests.IsChecked ?? false;
        _original.Browseable = TgBrowseable.IsChecked ?? true;
        _original.Comment = TxtComment.Text ?? "";

        _original.FollowSymlinks = TgFollowSymlinks.IsChecked ?? true;
        _original.WideLinks = TgWideLinks.IsChecked ?? false;

        _original.ValidUsers = TxtValidUsers.Text ?? "";
        _original.WriteList = TxtWriteList.Text ?? "";
        _original.ReadList = TxtReadList.Text ?? "";

        _original.ForceUser = TxtForceUser.Text ?? "";
        _original.ForceGroup = TxtForceGroup.Text ?? "";

        _original.CreateMask = GetSelectedTag(CmbCreateMask, "755");
        _original.DirectoryMask = GetSelectedTag(CmbDirectoryMask, "755");

        Saved = true;
        Close(_original);
    }

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
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using SAMBA_Util.Helpers;
using SAMBA_Util.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace SAMBA_Util.Views;

public partial class SharesView : UserControl
{
    public ObservableCollection<Share> Shares { get; } = new();

    public SharesView()
    {
        InitializeComponent();

        ListShares.ItemsSource = Shares;
        LoadShares();
    }

    public int LoadShares()
    {
        Shares.Clear();

        var sharesFromFile = SambaConfigReader.LoadShares();

        foreach (var s in sharesFromFile)
        {
            Shares.Add(s);
        }
            return Shares.Count;
    }

    public void OnDeleteShare(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            SambaConfigWriter.DeleteShare(share.Name);
            LoadShares();
            var main = TopLevel.GetTopLevel(this) as MainWindow;
            main?.UpdateStatus($"Share '{share.Name}' deleted successfully.");

        }
    }



  
    public async void OnEditShare(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            var win = new ShareEditorWindow(share);

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null)
            {
                Console.WriteLine("ERROR: No se pudo obtener la ventana padre.");
                return;
            }

            var result = await win.ShowDialog<Share?>(owner);

            if (result != null)
            {
                SambaConfigWriter.UpdateShare(result);
                LoadShares();
                var main = TopLevel.GetTopLevel(this) as MainWindow;
                main?.UpdateStatus($"Share '{result.Name}' updated successfully.");

            }
        }
    }

    // ⭐ AGREGAR SHARE (versión correcta)
    private async void OnAddShare(object? sender, RoutedEventArgs e)
    {
        var newShare = new Share
        {
            Name = "",
            Path = "",
            ReadOnly = false,
            AllowGuests = false
        };

        var win = new ShareEditorWindow(newShare);

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            Console.WriteLine("ERROR: No se pudo obtener la ventana padre.");
            return;
        }

        await win.ShowDialog(owner);

        if (win.Saved)
        {
            try
            {
               
                SambaConfigWriter.AddShare(newShare);
                LoadShares();

                var main = TopLevel.GetTopLevel(this) as MainWindow;
                main?.UpdateStatus($"Share '{newShare.Name}' added successfully.");

                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al agregar share: {ex.Message}");
            }
        }
    }
}

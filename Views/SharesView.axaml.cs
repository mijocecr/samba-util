using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using SAMBA_Util.Helpers;
using SAMBA_Util.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace SAMBA_Util.Views;

public partial class SharesView : UserControl
{
    public ObservableCollection<Share> Shares { get; } = new();

    public SharesView()
    {
        InitializeComponent();
        ListShares.ItemsSource = Shares;
    }

    // ---------------------------------------------------------
    //  Recibir shares desde MainWindow (arranque rápido)
    // ---------------------------------------------------------
    public void SetShares(List<Share> shares)
    {
        Shares.Clear();

        foreach (var s in shares)
            Shares.Add(s);

        // Validar permisos aquí también
        foreach (var s in Shares)
            s.Warning = ShareValidator.ValidateShare(s);
    }

    // ---------------------------------------------------------
    //  Carga tradicional (si el usuario lo pide)
    // ---------------------------------------------------------
    public int LoadShares()
    {
        Shares.Clear();

        var sharesFromFile = SambaConfigReader.LoadShares();

        foreach (var s in sharesFromFile)
            Shares.Add(s);

        // Validar permisos SOLO cuando el usuario abre la pestaña
        foreach (var s in Shares)
            s.Warning = ShareValidator.ValidateShare(s);

        return Shares.Count;
    }

    // ---------------------------------------------------------
    //  ELIMINAR SHARE
    // ---------------------------------------------------------
    public void OnDeleteShare(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            SambaConfigWriter.DeleteShare(share.Name);
            LoadShares();

            if (TopLevel.GetTopLevel(this) is MainWindow main)
                main.UpdateStatus($"Share '{share.Name}' deleted successfully.");
        }
    }

    // ---------------------------------------------------------
    //  EDITAR SHARE
    // ---------------------------------------------------------
    public async void OnEditShare(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            var win = new ShareEditorWindow(share);

            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                Console.WriteLine("ERROR: No se pudo obtener la ventana padre.");
                return;
            }

            var result = await win.ShowDialog<Share?>(owner);

            if (result != null)
            {
                SambaConfigWriter.UpdateShare(result);
                LoadShares();

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Share '{result.Name}' updated successfully.");
            }
        }
    }

    // ---------------------------------------------------------
    //  AGREGAR SHARE
    // ---------------------------------------------------------
    private async void OnAddShare(object? sender, RoutedEventArgs e)
    {
        var newShare = new Share
        {
            Name = "",
            Path = "",
            ReadOnly = true,
            AllowGuests = false,
            Browseable = true,
            CreateMask = "0744",
            DirectoryMask = "0755"
        };

        var win = new ShareEditorWindow(newShare);

        if (TopLevel.GetTopLevel(this) is not Window owner)
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

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Share '{newShare.Name}' added successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al agregar share: {ex.Message}");
            }
        }
    }
}

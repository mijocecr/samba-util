using System;
using System.Diagnostics;
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
        ApplyWarnings();
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
        ApplyWarnings();

        return Shares.Count;
    }

    private void ApplyWarnings()
    {
        foreach (var s in Shares)
        {
            s.Warning = SambaConfigWriter.IsFromInclude(s)
                ? $"Defined in {s.SourceFile} (included from smb.conf). Edit that file directly."
                : ShareValidator.ValidateShare(s);
        }
    }

    // ---------------------------------------------------------
    //  ELIMINAR SHARE
    // ---------------------------------------------------------
    public void OnDeleteShare(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            try
            {
                SambaConfigWriter.DeleteShare(share.Name);
                LoadShares();

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Share '{share.Name}' deleted successfully.");
            }
            catch (Exception ex)
            {
                share.Warning = ex.Message;

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Could not delete '{share.Name}': {ex.Message}");
            }
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
                Console.WriteLine("ERROR: Could not get parent window.");
                return;
            }

            var result = await win.ShowDialog<Share?>(owner);

            if (result != null && !string.IsNullOrWhiteSpace(result.Name))
            {
                try
                {
                    SambaConfigWriter.UpdateShare(result);
                    LoadShares();

                    if (TopLevel.GetTopLevel(this) is MainWindow main)
                        main.UpdateStatus($"Share '{result.Name}' updated successfully.");
                }
                catch (Exception ex)
                {
                    result.Warning = ex.Message;

                    if (TopLevel.GetTopLevel(this) is MainWindow main)
                        main.UpdateStatus($"Could not update '{result.Name}': {ex.Message}");
                }
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
            Console.WriteLine("ERROR: Could not get parent window.");
            return;
        }

        var result = await win.ShowDialog<Share?>(owner);

        // Validar que se haya guardado y que el nombre no esté vacío
        if (win.Saved && result != null && !string.IsNullOrWhiteSpace(result.Name))
        {
            try
            {
                SambaConfigWriter.AddShare(result);
                LoadShares();

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Share '{result.Name}' added successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding share: {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------
    //  FIX PERMISSIONS
    // ---------------------------------------------------------
   
    private async void OnFixPermissions(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Share share)
        {
            var config = ConfigManager.Load();
            string perms = config.DefaultPermissions ?? "755";

            try
            {
                // Ejecutar chmod recursivo (-R) como root para corregir el subárbol
                var result = ShellHelper.EjecutarComoRoot(
                    $"chmod -R {perms} \"{share.Path}\"", ShellHelper.LongTimeoutMs);

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(result.Stderr);
                }

                share.Warning = ShareValidator.ValidateShare(share);

                int index = Shares.IndexOf(share);
                if (index != -1)
                {
                    Shares[index] = share;
                }

                if (TopLevel.GetTopLevel(this) is MainWindow main)
                    main.UpdateStatus($"Recursive permissions fixed for '{share.Name}'");
            }
            catch (Exception ex)
            {
                share.Warning = $"Error fixing permissions: {ex.Message}";
            }
        }
    }
    
    
}
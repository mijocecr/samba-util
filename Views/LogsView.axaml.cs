using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;
using System;
using System.IO;
using System.Text;
using Avalonia.Media;

namespace SAMBA_Util.Views;

public partial class LogsView : UserControl
{
    private readonly string[] LogCandidates =
    {
        "/var/log/samba/log.smbd",
        "/var/log/samba/log.nmbd",
        "/var/log/samba/log.winbindd",
        "/var/log/samba/log.samba"
    };

    private string? DetectedLog;

    public LogsView()
    {
        InitializeComponent();
        DetectLogFile();
        LoadLogs();
    }

    private void DetectLogFile()
    {
        foreach (var path in LogCandidates)
        {
            if (File.Exists(path))
            {
                DetectedLog = path;
                return;
            }
        }
    }

    private void LoadLogs()
    {
        if (DetectedLog == null)
        {
            TxtLogs.Text = "No Samba logs found.";
            return;
        }

        string text;

        try
        {
            // Evitar bloquear la UI con logs enormes
            var lines = File.ReadAllLines(DetectedLog);
            var sb = new StringBuilder();

            foreach (var line in lines)
            {
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"[ERROR] {line}");
                else if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"[WARNING] {line}");
                else
                    sb.AppendLine(line);
            }

            text = sb.ToString();
        }
        catch (Exception ex)
        {
            TxtLogs.Text = $"Error reading log: {ex.Message}";
            return;
        }

        TxtLogs.Text = text;

        // Scroll automático al final
        Scroll.Offset = new Avalonia.Vector(0, Scroll.Extent.Height);
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void OnOpenFull(object? sender, RoutedEventArgs e)
    {
        if (DetectedLog == null)
            return;

        // Abrir como usuario normal (no root)
        try
        {
            System.Diagnostics.Process.Start("xdg-open", DetectedLog);
        }
        catch
        {
            // Fallback root
            ShellHelper.EjecutarComoRoot($"xdg-open {DetectedLog}");
        }
    }
}

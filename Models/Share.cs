using System.Collections.Generic;

namespace SAMBA_Util.Models;

public class Share
{
    // Básico
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool ReadOnly { get; set; } = true;
    public bool AllowGuests { get; set; } = false;
    public bool Browseable { get; set; } = true;
    public string Comment { get; set; } = "";
    public List<string> UnknownParameters { get; set; } = new();

    // Enlaces Simbólicos (NUEVO)
    public bool FollowSymlinks { get; set; } = true;
    public bool WideLinks { get; set; } = false;

    // Usuarios y permisos
    public string ValidUsers { get; set; } = "";
    public string WriteList { get; set; } = "";
    public string ReadList { get; set; } = "";

    // Forzar usuario/grupo
    public string ForceUser { get; set; } = "";
    public string ForceGroup { get; set; } = "";

    // Máscaras de creación
    public string CreateMask { get; set; } = "0744";
    public string DirectoryMask { get; set; } = "0755";
    public string? Warning { get; set; }
}
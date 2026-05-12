using System.Collections.Generic;

namespace SAMBA_Util.Models;

public class Share
{
    // Básico
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool ReadOnly { get; set; } = true;       // default Samba
    public bool AllowGuests { get; set; } = false;   // guest ok
    public bool Browseable { get; set; } = true;     // default Samba
    public string Comment { get; set; } = "";
    public List<string> UnknownParameters { get; set; }


    // Usuarios y permisos
    public string ValidUsers { get; set; } = "";     // "miguel juan"
    public string WriteList { get; set; } = "";      // "miguel"
    public string ReadList { get; set; } = "";       // "juan"

    // Forzar usuario/grupo
    public string ForceUser { get; set; } = "";
    public string ForceGroup { get; set; } = "";

    // Máscaras de creación
    public string CreateMask { get; set; } = "0744";     // default Samba
    public string DirectoryMask { get; set; } = "0755";  // default Samba
    public string? Warning { get; set; }

}
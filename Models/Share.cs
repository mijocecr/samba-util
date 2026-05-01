namespace SAMBA_Util.Models;

public class Share
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool ReadOnly { get; set; }
    public bool AllowGuests { get; set; }
}
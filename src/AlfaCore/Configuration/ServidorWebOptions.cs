namespace AlfaCore.Configuration;

public sealed class ServidorWebOptions
{
    public const string SectionName = "ServidorWeb";

    public int Puerto { get; set; } = 5055;
    public int? PuertoHttpsDesarrollo { get; set; }
    public bool EscucharEnRed { get; set; } = true;
    public bool AbrirNavegadorAlIniciar { get; set; } = true;
    public string Protocolo { get; set; } = "http";
    public string? UrlBasePublica { get; set; }
    public string NombreAplicacion { get; set; } = "AlfaCore - Alfa Gestión";
}

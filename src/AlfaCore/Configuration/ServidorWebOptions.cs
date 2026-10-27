namespace AlfaCore.Configuration;

public sealed class ServidorWebOptions
{
    public const string SectionName = "ServidorWeb";

    public int Puerto { get; set; } = 5055;
    public int? PuertoHttpsDesarrollo { get; set; }

    // Redirección HTTP -> HTTPS. Apagada por defecto a propósito: la mayoría de las
    // instalaciones corren como servicio de Windows sobre Kestrel en la LAN, sin HTTPS,
    // y ahí redirigir dejaría la app inaccesible. Se enciende sólo en los servidores
    // publicados a Internet detrás de IIS con certificado.
    public bool RedirigirAHttps { get; set; }
    public int PuertoHttpsPublico { get; set; } = 443;

    public bool EscucharEnRed { get; set; } = true;
    public bool AbrirNavegadorAlIniciar { get; set; } = true;
    public string Protocolo { get; set; } = "http";
    public string? UrlBasePublica { get; set; }
    public string NombreAplicacion { get; set; } = "AlfaCore - Alfa Gestión";
}

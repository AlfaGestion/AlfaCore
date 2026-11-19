namespace AlfaCore.Models;

public static class TicketInformeEstados
{
    public const string Borrador = "BORRADOR";
    public const string Publicado = "PUBLICADO";
}

/// <summary>Informe al cliente de un ticket (TICK_INFORMES).</summary>
public sealed class TicketInformeDto
{
    public int IdInforme { get; set; }
    public long IdTicket { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Estado { get; set; } = TicketInformeEstados.Borrador;
    public bool GeneradoIA { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public string UsuarioModificacion { get; set; } = string.Empty;
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraPublicacion { get; set; }
    public int Vistas { get; set; }
    public DateTime? UltimaVista { get; set; }

    public bool Publicado => Estado == TicketInformeEstados.Publicado;
}

public sealed class TicketInformeGuardarRequest
{
    public int? IdInforme { get; set; }
    public long IdTicket { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public bool GeneradoIA { get; set; }
    public string Usuario { get; set; } = string.Empty;
}

/// <summary>Dónde se publica el link: la base y la empresa de la sesión, y la raíz del sitio.</summary>
public sealed record TicketInformeDestino(string IdWeb, int IdBase, string BaseUrl);

/// <summary>Lo que ve el cliente en la página pública.</summary>
public sealed class TicketInformePublicoDto
{
    public int IdInforme { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public int NumeroTicket { get; set; }
    public string TituloTicket { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public DateTime? FechaHoraPublicacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
}

/// <summary>Datos del ticket que usan la plantilla y la IA para armar el informe.</summary>
public sealed class TicketInformeContexto
{
    public long IdTicket { get; set; }
    public int Numero { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public List<(DateTime Fecha, string Tipo, string Texto, string Usuario)> Actividad { get; set; } = [];
    public List<(DateTime Fecha, string Direccion, string Texto)> Mensajes { get; set; } = [];
    public List<(DateTime Fecha, int Minutos, string Texto)> Partes { get; set; } = [];
}

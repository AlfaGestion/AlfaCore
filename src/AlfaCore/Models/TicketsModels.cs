namespace AlfaCore.Models;

public sealed class TicketsFilters
{
    public string Texto { get; set; } = string.Empty;
    public string? CodigoEstado { get; set; }
    public string? IdTecnico { get; set; }
    public int? Prioridad { get; set; }
    public bool IncluirCerrados { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class TicketGridItemDto
{
    public long IdTicket { get; set; }
    public int Numero { get; set; }
    public string CodigoVisible { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string CodigoEstado { get; set; } = string.Empty;
    public string EstadoNombre { get; set; } = string.Empty;
    public string EstadoColor { get; set; } = string.Empty;
    public int EstadoOrden { get; set; }
    public int Prioridad { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public long? IdConversacion { get; set; }
    public int MensajesOrigen { get; set; }
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraCierre { get; set; }
    public List<TicketEtiquetaDto> Etiquetas { get; set; } = [];
}

public sealed class TicketDetailDto : TicketGridItemDto
{
    public string Descripcion { get; set; } = string.Empty;
    public string UsuarioAlta { get; set; } = string.Empty;
    public List<TicketMensajeOrigenDto> MensajesOrigenDetalle { get; set; } = [];
    public List<TicketActividadDto> Actividad { get; set; } = [];
}

public sealed class TicketEstadoDto
{
    public string CodigoEstado { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool EsCerrado { get; set; }
    public int Orden { get; set; }
}

public sealed class TicketEtiquetaDto
{
    public int IdEtiqueta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class TicketActividadDto
{
    public long IdActividad { get; set; }
    public string TipoActividad { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class TicketMensajeOrigenDto
{
    public long IdMensaje { get; set; }
    public long IdConversacion { get; set; }
    public string Autor { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string TipoMensaje { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public List<TicketAdjuntoOrigenDto> Adjuntos { get; set; } = [];
}

public sealed class TicketAdjuntoOrigenDto
{
    public long IdAdjunto { get; set; }
    public long IdMensaje { get; set; }
    public string TipoArchivo { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
}

public sealed class TicketCreateRequest
{
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string? CodigoEstado { get; set; }
    public int Prioridad { get; set; } = 1;
    public string? IdTecnico { get; set; }
    public string? ClienteCodigo { get; set; }
    public int? IdContacto { get; set; }
    public long? IdConversacion { get; set; }
    public List<long> IdMensajes { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketUpdateRequest
{
    public long IdTicket { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string CodigoEstado { get; set; } = TicketEstadoKeys.Nuevo;
    public int Prioridad { get; set; } = 1;
    public string? IdTecnico { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketQuickUpdateRequest
{
    public long IdTicket { get; set; }
    public string? CodigoEstado { get; set; }
    public string? IdTecnico { get; set; }
    public int? Prioridad { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketNotaRequest
{
    public long IdTicket { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketLookupDto
{
    public List<TicketEstadoDto> Estados { get; set; } = [];
    public List<ConversacionTecnicoOptionDto> Tecnicos { get; set; } = [];
    public List<TicketEtiquetaDto> Etiquetas { get; set; } = [];
}

public sealed class TicketViewSettingsDto
{
    public string AgruparPor { get; set; } = TicketViewGroupKeys.Estado;
    public List<TicketViewColumnDto> Columnas { get; set; } = [];
}

public sealed class TicketViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class TicketEstadoKeys
{
    public const string Nuevo = "NUEVO";
    public const string EnProceso = "EN_PROCESO";
    public const string EnEspera = "EN_ESPERA";
    public const string Escalado = "ESCALADO";
    public const string Cerrado = "CERRADO";
}

public static class TicketViewColumnKeys
{
    public const string Numero = "numero";
    public const string Titulo = "titulo";
    public const string Prioridad = "prioridad";
    public const string Estado = "estado";
    public const string Asignado = "asignado";
    public const string Cliente = "cliente";
    public const string Contacto = "contacto";
    public const string Fecha = "fecha";
    public const string Mensajes = "mensajes";
}

public static class TicketViewGroupKeys
{
    public const string None = "none";
    public const string Estado = "estado";
    public const string Prioridad = "prioridad";
    public const string Tecnico = "tecnico";
}

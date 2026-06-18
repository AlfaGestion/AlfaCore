namespace AlfaCore.Models;

public static class ParteHoraEstadoKeys
{
    public const string Borrador = "BORRADOR";
    public const string Presentado = "PRESENTADO";
    public const string Aprobado = "APROBADO";
    public const string Rechazado = "RECHAZADO";

    public static readonly string[] All = [Borrador, Presentado, Aprobado, Rechazado];
}

public static class ParteHoraTipoTrabajoKeys
{
    public const string Soporte = "SOPORTE";
    public const string Desarrollo = "DESARROLLO";
    public const string Implementacion = "IMPLEMENTACION";
    public const string Capacitacion = "CAPACITACION";
    public const string Reunion = "REUNION";
    public const string Viaje = "VIAJE";
    public const string Interno = "INTERNO";

    public static readonly string[] All = [Soporte, Desarrollo, Implementacion, Capacitacion, Reunion, Viaje, Interno];
}

public sealed class PartesHorasFilters
{
    public string Texto { get; set; } = string.Empty;
    public DateTime? FechaDesde { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? FechaHasta { get; set; } = DateTime.Today;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string TipoTrabajo { get; set; } = string.Empty;
    public long? IdTicket { get; set; }
    public bool SoloFacturables { get; set; }
    public bool SoloExcedentes { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class ParteHoraGridItemDto
{
    public long IdParteHora { get; set; }
    public DateTime Fecha { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public long? IdTicket { get; set; }
    public string TicketCodigo { get; set; } = string.Empty;
    public string TicketTitulo { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string TipoTrabajo { get; set; } = string.Empty;
    public int Minutos { get; set; }
    public decimal Horas => Minutos / 60m;
    public bool Facturable { get; set; }
    public bool Excedente { get; set; }
    public string Estado { get; set; } = ParteHoraEstadoKeys.Borrador;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
}

public sealed class ParteHoraDetailDto : ParteHoraGridItemDto
{
    public int? IdContacto { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public decimal? CostoHora { get; set; }
    public string UsuarioAprobacion { get; set; } = string.Empty;
    public DateTime? FechaHoraAprobacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
}

public sealed class ParteHoraSaveRequest
{
    public long? IdParteHora { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string ClienteCodigo { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public long? IdTicket { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string TipoTrabajo { get; set; } = ParteHoraTipoTrabajoKeys.Soporte;
    public int Minutos { get; set; } = 60;
    public bool Facturable { get; set; } = true;
    public bool Excedente { get; set; }
    public string Estado { get; set; } = ParteHoraEstadoKeys.Borrador;
    public string Descripcion { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class ParteHoraClienteConfigDto
{
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int HorasIncluidasMes { get; set; }
    public int ToleranciaMinutos { get; set; }
    public bool Activa { get; set; } = true;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class ParteHoraResumenClienteDto
{
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int MinutosTrabajados { get; set; }
    public int MinutosFacturables { get; set; }
    public int MinutosExcedentes { get; set; }
    public int MinutosIncluidos { get; set; }
    public int ToleranciaMinutos { get; set; }
    public decimal CostoInterno { get; set; }
    public int Tickets { get; set; }
}

public sealed class ParteHoraResumenTecnicoDto
{
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public int MinutosTrabajados { get; set; }
    public int MinutosFacturables { get; set; }
    public decimal CostoInterno { get; set; }
}

public sealed class ParteHoraDashboardDto
{
    public int MinutosTrabajados { get; set; }
    public int MinutosFacturables { get; set; }
    public int MinutosExcedentes { get; set; }
    public int PartesPendientes { get; set; }
    public List<ParteHoraResumenClienteDto> Clientes { get; set; } = [];
    public List<ParteHoraResumenTecnicoDto> Tecnicos { get; set; } = [];
}

public sealed class ParteHoraTicketOptionDto
{
    public long IdTicket { get; set; }
    public int Numero { get; set; }
    public string CodigoVisible { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
}

public sealed class ParteHoraPersonaOptionDto
{
    public string Tipo { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
}

public sealed class ParteHoraLookupDto
{
    public List<ParteHoraLookupOptionDto> Tecnicos { get; set; } = [];
    public List<string> Estados { get; set; } = ParteHoraEstadoKeys.All.ToList();
    public List<string> TiposTrabajo { get; set; } = ParteHoraTipoTrabajoKeys.All.ToList();
}

public sealed class ParteHoraLookupOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Subtitulo { get; set; } = string.Empty;
}

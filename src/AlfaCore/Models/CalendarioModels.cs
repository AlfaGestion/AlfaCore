namespace AlfaCore.Models;

public sealed class CalendarioMonthRequest
{
    public int Year { get; set; } = DateTime.Today.Year;
    public int Month { get; set; } = DateTime.Today.Month;
    public string Search { get; set; } = string.Empty;
    public string? IdTecnico { get; set; }
    public string Tipo { get; set; } = CalendarioEventoTipos.Todos;
}

public sealed class CalendarioMonthDto
{
    public DateTime MonthStart { get; set; }
    public DateTime GridStart { get; set; }
    public DateTime GridEnd { get; set; }
    public List<CalendarioEventoDto> Eventos { get; set; } = [];
    public List<ConversacionTecnicoOptionDto> Tecnicos { get; set; } = [];
    public List<ConversacionPlantillaDto> PlantillasWhatsApp { get; set; } = [];
}

public sealed class CalendarioEventoDto
{
    public long IdEvento { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Tipo { get; set; } = CalendarioEventoTipos.Guardia;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool TodoElDia { get; set; } = true;
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string Estado { get; set; } = CalendarioEventoEstados.Confirmado;
    public string Color { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public CalendarioRecordatorioDto? Recordatorio { get; set; }
}

public sealed class CalendarioRecordatorioDto
{
    public long IdRecordatorio { get; set; }
    public string Tipo { get; set; } = CalendarioRecordatorioTipos.WhatsApp;
    public int MinutosAntes { get; set; } = 1440;
    public long? IdPlantillaWhatsApp { get; set; }
    public bool NotificarResponsable { get; set; } = true;
    public DateTime? FechaHoraProgramada { get; set; }
    public DateTime? FechaHoraEnvio { get; set; }
    public string EstadoEnvio { get; set; } = CalendarioRecordatorioEstados.Pendiente;
    public string UltimoError { get; set; } = string.Empty;
}

public sealed class CalendarioEventoSaveRequest
{
    public long IdEvento { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Tipo { get; set; } = CalendarioEventoTipos.Guardia;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool TodoElDia { get; set; } = true;
    public string? IdTecnico { get; set; }
    public string? TecnicoNombre { get; set; }
    public string? TelefonoWhatsApp { get; set; }
    public string Estado { get; set; } = CalendarioEventoEstados.Confirmado;
    public string? Color { get; set; }
    public string? Descripcion { get; set; }
    public bool CrearRecordatorioWhatsApp { get; set; } = true;
    public int MinutosAntesRecordatorio { get; set; } = 1440;
    public long? IdPlantillaWhatsApp { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class CalendarioRecordatorioSendResult
{
    public long IdRecordatorio { get; set; }
    public long IdConversacion { get; set; }
    public long IdMensaje { get; set; }
    public string EstadoEnvio { get; set; } = string.Empty;
}

public static class CalendarioEventoTipos
{
    public const string Todos = "TODOS";
    public const string Guardia = "GUARDIA";
    public const string Vacaciones = "VACACIONES";
    public const string Ausencia = "AUSENCIA";
    public const string Feriado = "FERIADO";
    public const string Reunion = "REUNION";
    public const string Capacitacion = "CAPACITACION";
    public const string Otro = "OTRO";
}

public static class CalendarioEventoEstados
{
    public const string Borrador = "BORRADOR";
    public const string Confirmado = "CONFIRMADO";
    public const string Cancelado = "CANCELADO";
    public const string Finalizado = "FINALIZADO";
}

public static class CalendarioRecordatorioTipos
{
    public const string WhatsApp = "WHATSAPP";
}

public static class CalendarioRecordatorioEstados
{
    public const string Pendiente = "PENDIENTE";
    public const string Enviado = "ENVIADO";
    public const string Error = "ERROR";
    public const string Omitido = "OMITIDO";
}

public sealed class ReunionPublicaTipoDto
{
    public long IdTipoReunion { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int DuracionMinutos { get; set; } = 60;
    public string Modalidad { get; set; } = "En linea";
    public string EnlaceVideollamada { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string EmailOperador { get; set; } = string.Empty;
    public string DiasDisponibles { get; set; } = "1,2,3,4,5";
    public TimeSpan HoraDesde { get; set; } = TimeSpan.FromHours(10);
    public TimeSpan HoraHasta { get; set; } = TimeSpan.FromHours(16);
    public int ReservasHastaDias { get; set; } = 30;
    public int AnticipacionMinHoras { get; set; } = 24;
    public bool Activo { get; set; } = true;
    public string Color { get; set; } = "#22d3ee";
}

public sealed class ReunionPublicaMesRequest
{
    public string Slug { get; set; } = "capacitacion-online";
    public int Year { get; set; } = DateTime.Today.Year;
    public int Month { get; set; } = DateTime.Today.Month;
}

public sealed class ReunionPublicaMesDto
{
    public ReunionPublicaTipoDto? Tipo { get; set; }
    public DateTime MonthStart { get; set; }
    public DateTime GridStart { get; set; }
    public DateTime GridEnd { get; set; }
    public List<ReunionPublicaDiaDto> Dias { get; set; } = [];
}

public sealed class ReunionPublicaDiaDto
{
    public DateTime Fecha { get; set; }
    public bool Disponible { get; set; }
    public List<ReunionPublicaHorarioDto> Horarios { get; set; } = [];
}

public sealed class ReunionPublicaHorarioDto
{
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public bool Disponible { get; set; }
}

public sealed class ReunionPublicaReservaRequest
{
    public long IdTipoReunion { get; set; }
    public DateTime FechaInicio { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string TipoCapacitacion { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public sealed class ReunionPublicaReservaResult
{
    public long IdReserva { get; set; }
    public long IdEvento { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string Modalidad { get; set; } = string.Empty;
}

public sealed class ReunionPublicaAdminDto
{
    public List<ReunionPublicaTipoDto> Tipos { get; set; } = [];
    public List<ReunionPublicaReservaAdminDto> Reservas { get; set; } = [];
}

public sealed class ReunionPublicaReservaAdminDto
{
    public long IdReserva { get; set; }
    public long IdEvento { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string TipoCapacitacion { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
}

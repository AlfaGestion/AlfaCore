namespace AlfaCore.Models;

public sealed class TicketsFilters
{
    public string Texto { get; set; } = string.Empty;
    public string? CodigoEstado { get; set; }
    public string? IdTecnico { get; set; }
    public int? Prioridad { get; set; }
    public bool IncluirCerrados { get; set; } = true;
    public bool? TieneAsignado { get; set; }
    public bool? TieneMensajes { get; set; }
    public string? ClienteCodigo { get; set; }
    public int? IdContacto { get; set; }
    public int? IdEtiqueta { get; set; }
    public string FechaRapida { get; set; } = string.Empty;
    /// <summary>Bandeja de "Atender primero" (ver <see cref="TicketBandejaKeys"/>); vacía = sin bandeja.</summary>
    public string Bandeja { get; set; } = string.Empty;
    /// <summary>Técnico del usuario logueado, para la bandeja "Para mí".</summary>
    public string? IdTecnicoActual { get; set; }
    public List<SearchRuleDto> Reglas { get; set; } = [];
    public string OrdenarPor { get; set; } = TicketViewColumnKeys.Fecha;
    public bool OrdenDescendente { get; set; } = true;
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

    // "Atender primero" (TicketsAtencionCalculo): solo se completa para tickets abiertos.
    public string UsuarioAltaTicket { get; set; } = string.Empty;
    public string EsperandoA { get; set; } = string.Empty;
    public DateTime? EsperandoDesde { get; set; }
    public double HorasHabilesEsperando { get; set; }
    public bool ClientePrioritario { get; set; }
    public string ClienteClasificacion { get; set; } = string.Empty;
    public string EstadoAbono { get; set; } = string.Empty;
    public int CasosAbiertosCliente { get; set; }
    public DateTime? FechaSeguimiento { get; set; }
    public bool SeguimientoVencido { get; set; }
    public int PrioridadEfectiva { get; set; }
    public string PlazoTipo { get; set; } = string.Empty;
    public string PlazoEstado { get; set; } = string.Empty;
    public DateTime? PlazoVence { get; set; }
    public double PlazoHorasRestantes { get; set; }
    public double Puntaje { get; set; }
    /// <summary>"2 h", "3 días hábiles": cuánto hace que espera (a nosotros o al cliente).</summary>
    public string EsperandoTexto { get; set; } = string.Empty;
    /// <summary>"Vencido hace 2 h", "Quedan 5 h", "Frenado".</summary>
    public string PlazoTexto { get; set; } = string.Empty;
    public List<string> Motivos { get; set; } = [];
}

public static class TicketEsperandoA
{
    public const string Nosotros = "NOSOTROS";
    public const string Cliente = "CLIENTE";
    public const string Seguimiento = "SEGUIMIENTO";
}

public static class TicketPlazoEstados
{
    public const string EnTermino = "EN_TERMINO";
    public const string PorVencer = "POR_VENCER";
    public const string Vencido = "VENCIDO";
    public const string Frenado = "FRENADO";
}

public static class TicketPlazoTipos
{
    public const string Respuesta = "RESPUESTA";
    public const string Resolucion = "RESOLUCION";
}

public static class TicketBandejaKeys
{
    public const string Mios = "mios";
    public const string Nosotros = "nosotros";
    public const string Plazo = "plazo";
    public const string Prioritarios = "prioritarios";
    public const string SinAsignar = "sin-asignar";
    public const string Seguimientos = "seguimientos";
    public const string Cliente = "cliente";
    public const string Bot = "bot";
    public const string Abiertos = "abiertos";
}

public sealed class TicketBandejaDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    /// <summary>Hay casos que piden atención (vencidos, seguimientos vencidos): el contador se resalta.</summary>
    public bool Alerta { get; set; }
}

public sealed class TicketPlazoDto
{
    public int Prioridad { get; set; }
    public int HorasRespuesta { get; set; }
    public int HorasResolucion { get; set; }
}

/// <summary>Pesos y umbrales del orden "Atender primero" (TA_CONFIGURACION grupo TICKETS) y plazos (TICK_PLAZOS).</summary>
public sealed class TicketsAtencionConfigDto
{
    public List<TicketPlazoDto> Plazos { get; set; } = [];
    public int PesoVencido { get; set; } = 100;
    public int PesoPorVencer { get; set; } = 50;
    public int PesoPrioridad { get; set; } = 15;
    public int PesoClientePrioritario { get; set; } = 20;
    public int PesoVariosCasos { get; set; } = 10;
    public int PesoHoraEspera { get; set; } = 1;
    public int PesoHoraEsperaTope { get; set; } = 40;
    public int PesoSeguimientoVencido { get; set; } = 40;
    public int PorVencerPorcentaje { get; set; } = 75;
    public int VariosCasosDesde { get; set; } = 3;
    /// <summary>Estados en los que se espera al cliente (separados por coma): el plazo se frena.</summary>
    public string EstadosEsperaCliente { get; set; } = TicketEstadoKeys.EnEspera;
}

public sealed class TicketSeguimientoRequest
{
    public long IdTicket { get; set; }
    /// <summary>Fecha y hora en que el ticket vuelve a la cola; null quita el seguimiento.</summary>
    public DateTime? Fecha { get; set; }
    public string? UsuarioAccion { get; set; }
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
    public bool ArchivoDisponible { get; set; }
    public bool PuedeRecuperarse { get; set; }
    public string EstadoAlmacenamiento { get; set; } = string.Empty;
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
    public List<int> IdEtiquetas { get; set; } = [];
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
    public string? ClienteCodigo { get; set; }
    public int? IdContacto { get; set; }
    public List<int> IdEtiquetas { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketEtiquetaSaveRequest
{
    public int IdEtiqueta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketEtiquetaDeleteRequest
{
    public int IdEtiqueta { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketClienteOptionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
}

public sealed class TicketContactoOptionDto
{
    public int IdContacto { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
}

public sealed class TicketQuickUpdateRequest
{
    public long IdTicket { get; set; }
    public string? CodigoEstado { get; set; }
    public string? IdTecnico { get; set; }
    public int? Prioridad { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class TicketBulkUpdateRequest
{
    public List<long> IdTickets { get; set; } = [];
    public string? CodigoEstado { get; set; }
    public bool ActualizarTecnico { get; set; }
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

public sealed class TicketRelatedSearchRequest
{
    public string? ClienteCodigo { get; set; }
    public int? IdContacto { get; set; }
    public long? IdConversacion { get; set; }
    public string Texto { get; set; } = string.Empty;
}

public sealed class TicketRelatedMatchDto
{
    public TicketGridItemDto Ticket { get; set; } = new();
    public int Score { get; set; }
    public List<string> Terminos { get; set; } = [];
    public string PrimerMensajeTexto { get; set; } = string.Empty;
    public bool MismaConversacion { get; set; }
    public bool MismoContacto { get; set; }
    public bool MismoCliente { get; set; }
}

public sealed class TicketViewSettingsDto
{
    public string AgruparPor { get; set; } = TicketViewGroupKeys.Estado;
    public List<TicketViewColumnDto> Columnas { get; set; } = [];
}

/// <summary>Vista por defecto de la lista de Tickets (una sola definición para el servicio y la pantalla).</summary>
public static class TicketViewDefaults
{
    public static TicketViewSettingsDto Crear()
        => new()
        {
            AgruparPor = TicketViewGroupKeys.Estado,
            Columnas =
            [
                new() { Key = TicketViewColumnKeys.Numero, Label = "Nro.", Visible = true, Order = 0, WidthPx = 84 },
                new() { Key = TicketViewColumnKeys.Titulo, Label = "Ticket", Visible = true, Order = 1, WidthPx = 320 },
                new() { Key = TicketViewColumnKeys.Plazo, Label = "Plazo", Visible = true, Order = 2, WidthPx = 200 },
                new() { Key = TicketViewColumnKeys.Esperando, Label = "Esperando a", Visible = true, Order = 3, WidthPx = 200 },
                new() { Key = TicketViewColumnKeys.Prioridad, Label = "Prioridad", Visible = true, Order = 4, WidthPx = 118 },
                new() { Key = TicketViewColumnKeys.Estado, Label = "Etapa", Visible = true, Order = 5, WidthPx = 128 },
                new() { Key = TicketViewColumnKeys.Asignado, Label = "Asignado", Visible = true, Order = 6, WidthPx = 150 },
                new() { Key = TicketViewColumnKeys.Cliente, Label = "Cliente", Visible = true, Order = 7, WidthPx = 210 },
                new() { Key = TicketViewColumnKeys.TipoCliente, Label = "Tipo de cliente", Visible = true, Order = 8, WidthPx = 140 },
                new() { Key = TicketViewColumnKeys.Antiguedad, Label = "Antigüedad", Visible = true, Order = 9, WidthPx = 120 },
                new() { Key = TicketViewColumnKeys.Contacto, Label = "Contacto", Visible = false, Order = 10, WidthPx = 190 },
                new() { Key = TicketViewColumnKeys.Etiquetas, Label = "Etiquetas", Visible = true, Order = 11, WidthPx = 180 },
                new() { Key = TicketViewColumnKeys.Mensajes, Label = "Mensajes", Visible = false, Order = 12, WidthPx = 112 },
                new() { Key = TicketViewColumnKeys.Fecha, Label = "Fecha", Visible = true, Order = 13, WidthPx = 160 },
                new() { Key = TicketViewColumnKeys.CasosCliente, Label = "Casos del cliente", Visible = false, Order = 14, WidthPx = 130 },
                new() { Key = TicketViewColumnKeys.Seguimiento, Label = "Seguimiento", Visible = false, Order = 15, WidthPx = 140 }
            ]
        };

    public static int Ancho(string key)
        => Crear().Columnas.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.WidthPx ?? 150;
}

public sealed class TicketViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
    public int WidthPx { get; set; }
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
    public const string Etiquetas = "etiquetas";
    public const string Fecha = "fecha";
    public const string Mensajes = "mensajes";
    public const string Esperando = "esperando";
    public const string Plazo = "plazo";
    public const string TipoCliente = "tipocliente";
    public const string CasosCliente = "casoscliente";
    public const string Antiguedad = "antiguedad";
    public const string Seguimiento = "seguimiento";
    /// <summary>Orden "Atender primero" (no es una columna).</summary>
    public const string Atencion = "atencion";
}

public static class TicketViewGroupKeys
{
    public const string None = "none";
    public const string Estado = "estado";
    public const string Prioridad = "prioridad";
    public const string Tecnico = "tecnico";
}

/// <summary>Panel de seguimiento de Tickets (/tickets/seguimiento).</summary>
public sealed class TicketsSeguimientoDto
{
    public int Dias { get; set; }
    public int Abiertos { get; set; }
    public int EsperanNuestra { get; set; }
    public int Vencidos { get; set; }
    public int PorVencer { get; set; }
    public int EsperandoCliente { get; set; }
    public int SeguimientosVencidos { get; set; }
    public int SinAsignar { get; set; }
    public int Prioritarios { get; set; }
    /// <summary>Semanas (lunes a domingo): Periodo = "dd/MM" del lunes, Ventas = entradas, Compras = cierres.</summary>
    public List<ComparativoMensualDto> Semanas { get; set; } = [];
    public int EntradasPeriodo { get; set; }
    public int CierresPeriodo { get; set; }
    public List<TicketsAntiguedadTramoDto> Antiguedad { get; set; } = [];
    public int CerradosMedidos { get; set; }
    public int RespuestaMedidos { get; set; }
    public double? CumplimientoRespuesta { get; set; }
    public double? CumplimientoResolucion { get; set; }
    public double? HorasResolucionPromedio { get; set; }
    public List<TicketsEtapaTiempoDto> TiempoPorEtapa { get; set; } = [];
    public DateTime? HistorialDesde { get; set; }
    public List<TicketsTecnicoCargaDto> Tecnicos { get; set; } = [];
    public List<TicketsClienteCasosDto> Clientes { get; set; } = [];
    public List<TicketGridItemDto> MasAtrasados { get; set; } = [];
    /// <summary>Hay más abiertos que el tope que se analiza (los números son de los más recientes).</summary>
    public bool Truncado { get; set; }
}

public sealed class TicketsAntiguedadTramoDto
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Nosotros { get; set; }
    public int Cliente { get; set; }
}

public sealed class TicketsEtapaTiempoDto
{
    public string CodigoEstado { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public double HorasPromedio { get; set; }
    public string Texto { get; set; } = string.Empty;
    public int Tramos { get; set; }
}

public sealed class TicketsTecnicoCargaDto
{
    public string IdTecnico { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Abiertos { get; set; }
    public int EsperanNuestra { get; set; }
    public int Vencidos { get; set; }
    public int? MasViejoNumero { get; set; }
    public long? MasViejoId { get; set; }
    public string MasViejoTexto { get; set; } = string.Empty;
}

public sealed class TicketsClienteCasosDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Abiertos { get; set; }
    public int Vencidos { get; set; }
    public int CreadosPeriodo { get; set; }
    public bool Prioritario { get; set; }
    public string EstadoAbono { get; set; } = string.Empty;
}

// ── Avisos automáticos y prioridad sugerida por IA (2026-11-24) ─────────────────────────────

public static class TicketAvisoTipos
{
    public const string PorVencer = "PLAZO_POR_VENCER";
    public const string Vencido = "PLAZO_VENCIDO";
    public const string Seguimiento = "SEGUIMIENTO";
    public const string Prioritario = "PRIORITARIO_ESPERA";
    public const string Resumen = "RESUMEN_DIARIO";
    public const string IaPrioridad = "IA_PRIORIDAD";
}

/// <summary>Qué avisos se mandan y a quién (TA_CONFIGURACION grupo TICKETS).</summary>
public sealed class TicketsAvisosConfigDto
{
    public bool Activo { get; set; } = true;
    public bool AvisarPorVencer { get; set; } = true;
    public bool AvisarVencido { get; set; } = true;
    public bool AvisarSeguimiento { get; set; } = true;
    /// <summary>Usuarios del sistema separados por coma: reciben lo que no tiene técnico y los clientes prioritarios que esperan.</summary>
    public string Coordinadores { get; set; } = string.Empty;
    /// <summary>Horas hábiles que espera un cliente prioritario antes de avisar a los coordinadores (0 = no avisar).</summary>
    public int PrioritarioHoras { get; set; } = 4;
    /// <summary>Hora del resumen diario por técnico (null = sin resumen).</summary>
    public int? ResumenHora { get; set; } = 9;
    public bool IaPrioridadBot { get; set; } = true;

    public IReadOnlyList<string> CoordinadoresLista()
        => (Coordinadores ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Un aviso a mandar. Va al técnico; si no hay técnico (o es para coordinación), a los coordinadores.</summary>
public sealed record TicketAviso(
    long IdTicket,
    string Tipo,
    string Clave,
    string IdTecnico,
    bool ParaCoordinadores,
    string Titulo,
    string Cuerpo,
    string Url,
    string Detalle);

public sealed class TicketAvisoEnviadoDto
{
    public long IdTicket { get; set; }
    public int Numero { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class TicketsAvisosResultadoDto
{
    public bool Ejecutado { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public int Enviados { get; set; }
    public int PrioridadesIa { get; set; }
    public List<string> Detalle { get; set; } = [];
}

public sealed class TicketSugerenciaIaRequest
{
    public long? IdTicket { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Nombre del cliente tal como se ve en pantalla (solo para dar contexto).</summary>
    public string? Cliente { get; set; }
}

public sealed class TicketSugerenciaIaDto
{
    public bool Disponible { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public int Prioridad { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
}

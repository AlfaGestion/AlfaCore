using System.Text.Json;

namespace AlfaCore.Models;

public sealed class ConversacionesInboxFilters
{
    public const string EstadoSinFinalizar = "__SIN_FINALIZAR__";
    public string Modo { get; set; } = "pendientes";
    public string Search { get; set; } = string.Empty;
    public string? IdTecnicoActual { get; set; }
    public string? CodigoEstado { get; set; }
    public string? Canal { get; set; }
    public string? UsuarioActual { get; set; }
    public string? SistemaActual { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public string? Auditoria { get; set; }
    public string? TipoMensaje { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public sealed class ConversacionesEstadisticasFilters
{
    public DateTime Desde { get; set; } = DateTime.Today;
    public DateTime Hasta { get; set; } = DateTime.Today;
    public string? IdTecnico { get; set; }
}

public sealed class ConversacionesEstadisticasDto
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int ConversacionesTotales { get; set; }
    public int ConversacionesAbiertas { get; set; }
    public int ConversacionesPendientes { get; set; }
    public int ConversacionesEnGestion { get; set; }
    public int ConversacionesCerradas { get; set; }
    public int ConversacionesArchivadas { get; set; }
    public int ConversacionesSinAsignar { get; set; }
    public int ConversacionesAsignadas { get; set; }
    public int ConversacionesNuevas { get; set; }
    public int ConversacionesActivasEnRango { get; set; }
    public int ClientesUnicos { get; set; }
    public int ConversacionesReabiertas { get; set; }
    public int MensajesEntrantes { get; set; }
    public int MensajesSalientes { get; set; }
    public int MensajesInternos { get; set; }
    public int MensajesConAdjuntos { get; set; }
    public int ConversacionesConRespuesta { get; set; }
    public int ConversacionesSinRespuesta { get; set; }
    public int ConversacionesCerradasEnRango { get; set; }
    public int TicketsCreados { get; set; }
    public int Asignaciones { get; set; }
    public int CambiosEstado { get; set; }
    public TimeSpan? PromedioPrimeraRespuesta { get; set; }
    public TimeSpan? PromedioCierre { get; set; }
    public List<ConversacionesEstadisticaEstadoDto> PorEstado { get; set; } = [];
    public List<ConversacionesEstadisticaTecnicoDto> PorTecnico { get; set; } = [];
    public List<ConversacionesEstadisticaDiaDto> PorDia { get; set; } = [];
    public List<ConversacionesEstadisticaActividadDto> Actividad { get; set; } = [];
    public List<ConversacionesEstadisticaTipoMensajeDto> PorTipoMensaje { get; set; } = [];
    public List<ConversacionesEstadisticaClienteDto> PorCliente { get; set; } = [];
    public List<ConversacionesEstadisticaConversacionDto> TopConversaciones { get; set; } = [];
    public List<ConversacionesEstadisticaTemaDto> TemasFrecuentes { get; set; } = [];
    public List<ConversacionesEstadisticaTemaDto> FrasesFrecuentes { get; set; } = [];
}

public sealed class ConversacionesEstadisticaEstadoDto
{
    public string CodigoEstado { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public bool EsCerrado { get; set; }
}

public sealed class ConversacionesEstadisticaTecnicoDto
{
    public string IdTecnico { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int ConversacionesAsignadas { get; set; }
    public int MensajesEnviados { get; set; }
    public int Cierres { get; set; }
    public int TicketsCreados { get; set; }
}

public sealed class ConversacionesEstadisticaDiaDto
{
    public DateTime Fecha { get; set; }
    public int Entrantes { get; set; }
    public int Salientes { get; set; }
    public int Cerradas { get; set; }
    public int Tickets { get; set; }
}

public sealed class ConversacionesEstadisticaActividadDto
{
    public string Tipo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public sealed class ConversacionesEstadisticaTipoMensajeDto
{
    public string Tipo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public sealed class ConversacionesEstadisticaTemaDto
{
    public string Texto { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public sealed class ConversacionesEstadisticaClienteDto
{
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int Conversaciones { get; set; }
    public int Contactos { get; set; }
    public int Entrantes { get; set; }
    public int Salientes { get; set; }
    public int Internos { get; set; }
    public int TotalMensajes { get; set; }
    public int PendientesRespuesta { get; set; }
    public int Cerradas { get; set; }
    public int Tickets { get; set; }
    public int Reabiertas { get; set; }
    public TimeSpan? PromedioPrimeraRespuesta { get; set; }
    public DateTime UltimoMovimiento { get; set; }
}

public sealed class ConversacionesEstadisticaConversacionDto
{
    public long IdConversacion { get; set; }
    public int? IdContacto { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Tecnico { get; set; } = string.Empty;
    public int Entrantes { get; set; }
    public int Salientes { get; set; }
    public int Total { get; set; }
    public DateTime UltimoMovimiento { get; set; }
}

public sealed class ConversacionInboxItemDto
{
    public long IdConversacion { get; set; }
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string NombreVisible { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public string CodigoEstado { get; set; } = string.Empty;
    public string EstadoDescripcion { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string ResumenUltimoMensaje { get; set; } = string.Empty;
    public DateTime FechaHoraUltimoMensaje { get; set; }
    public DateTime? FechaHoraUltimoMensajeCliente { get; set; }
    public long? IdUltimoMensajeCliente { get; set; }
    public int CantidadMensajesCliente { get; set; }
    public int MensajesNoLeidosUsuario { get; set; }
    public bool VentanaWhatsAppActiva { get; set; }
    public DateTime? FechaHoraVencimientoVentanaWhatsApp { get; set; }
    public bool Archivada { get; set; }
    public bool Bloqueada { get; set; }
    public bool FijadaPorUsuario { get; set; }
    public DateTime? FechaHoraFijada { get; set; }
}

public sealed class ConversacionDetalleDto
{
    public long IdConversacion { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string NombreVisible { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public string ContactoTelefono { get; set; } = string.Empty;
    public string ContactoCelular { get; set; } = string.Empty;
    public string ContactoEmail { get; set; } = string.Empty;
    public string ContactoCargo { get; set; } = string.Empty;
    public string CodigoEstado { get; set; } = string.Empty;
    public string EstadoDescripcion { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string ResumenUltimoMensaje { get; set; } = string.Empty;
    public string Prioridad { get; set; } = string.Empty;
    public bool Archivada { get; set; }
    public bool Bloqueada { get; set; }
    public DateTime? FechaHoraPrimerMensaje { get; set; }
    public DateTime FechaHoraUltimoMensaje { get; set; }
    public DateTime? FechaHoraUltimoMensajeCliente { get; set; }
    public bool VentanaWhatsAppActiva { get; set; }
    public DateTime? FechaHoraVencimientoVentanaWhatsApp { get; set; }
    public DateTime? FechaHoraCierre { get; set; }
}

public sealed class ConversacionClienteCandidateDto
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Localidad { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
}

public sealed class ConversacionRelacionarClienteRequest
{
    public long IdConversacion { get; set; }
    public string ClienteCodigo { get; set; } = string.Empty;
}

public sealed class ConversacionMensajeDto
{
    public long IdMensaje { get; set; }
    public long IdConversacion { get; set; }
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string WhatsAppMessageId { get; set; } = string.Empty;
    public string WhatsAppReplyToMessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string EstadoEnvio { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public string UsuarioAutor { get; set; } = string.Empty;
    public string SistemaAutor { get; set; } = string.Empty;
    public string IdTecnicoAutor { get; set; } = string.Empty;
    public string TecnicoAutorNombre { get; set; } = string.Empty;
    public bool TieneAdjuntos { get; set; }
}

public sealed class ConversacionAuditoriaMensajeDto
{
    public long IdMensaje { get; set; }
    public long IdConversacion { get; set; }
    public string NombreConversacion { get; set; } = string.Empty;
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string EstadoDescripcion { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
    public bool TieneAdjuntos { get; set; }
}

public sealed class ConversacionTypingRequest
{
    public long IdConversacion { get; set; }
    public string? ClienteId { get; set; }
    public string? IdTecnico { get; set; }
    public string? NombreTecnico { get; set; }
    public string? Usuario { get; set; }
    public string? Sistema { get; set; }
    public bool Escribiendo { get; set; }
}

public sealed class ConversacionTypingDto
{
    public string ClienteId { get; set; } = string.Empty;
    public string IdTecnico { get; set; } = string.Empty;
    public string NombreTecnico { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Sistema { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class ConversacionAdjuntoDto
{
    public long IdAdjunto { get; set; }
    public long IdMensaje { get; set; }
    public string TipoArchivo { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string UrlArchivo { get; set; } = string.Empty;
    public string RutaLocal { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
}

public sealed class ConversacionStickerFavoritoDto
{
    public long IdFavorito { get; set; }
    public long IdAdjunto { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
}

public sealed class ConversacionSendMessageRequest
{
    public long IdConversacion { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string MessageType { get; set; } = "TEXT";
    public string? WhatsAppReplyToMessageId { get; set; }
    public string? IdTecnicoAutor { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionReaccionRequest
{
    public long IdConversacion { get; set; }
    public long IdMensaje { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public string? IdTecnicoAutor { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionNotaInternaRequest
{
    public long IdConversacion { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? IdTecnicoAutor { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionEventoInternoRequest
{
    public long IdConversacion { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? IdTecnicoAutor { get; set; }
    public string? NombreTecnicoAccion { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionAsignacionRequest
{
    public long IdConversacion { get; set; }
    public string? IdTecnico { get; set; }
    public string? IdTecnicoAutor { get; set; }
    public string? NombreTecnicoAccion { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ConversacionEstadoRequest
{
    public long IdConversacion { get; set; }
    public string CodigoEstado { get; set; } = string.Empty;
    public string? IdTecnicoAutor { get; set; }
    public string? NombreTecnicoAccion { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ConversacionMessageResultDto
{
    public long IdMensaje { get; set; }
    public string EstadoEnvio { get; set; } = string.Empty;
    public string WhatsAppMessageId { get; set; } = string.Empty;
}

public sealed class ConversacionWebhookResultDto
{
    public long IdWebhookLog { get; set; }
    public int MensajesDetectados { get; set; }
    public int MensajesProcesados { get; set; }
}

public sealed class ConversacionUploadAdjuntoRequest
{
    public long IdConversacion { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string TipoArchivo { get; set; } = string.Empty;
    public Stream Contenido { get; set; } = Stream.Null;
    public long TamanoBytes { get; set; }
    public string? IdTecnicoAutor { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
    public bool PermitirEnvioConVentanaVencida { get; set; }
}

public sealed class ConversacionAdjuntoServeDto
{
    public string RutaLocal { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public string NombreDescarga { get; set; } = string.Empty;
}

public sealed class ConversacionCrearHiloInternoRequest
{
    public string NombreHilo { get; set; } = string.Empty;
    public string? IdTecnico { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionRenameRequest
{
    public long IdConversacion { get; set; }
    public string NombreVisible { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionCrearWhatsAppRequest
{
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string? IdTecnico { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionCrearWhatsAppResultDto
{
    public long IdConversacion { get; set; }
    public bool Creada { get; set; }
    public bool ContactoAsociado { get; set; }
    public string TelefonoWhatsApp { get; set; } = string.Empty;
    public string NombreVisible { get; set; } = string.Empty;
}

public sealed class ConversacionWebhookRequest
{
    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConversacionTecnicoOptionDto
{
    public string IdTecnico { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string UsuarioAsociado { get; set; } = string.Empty;
    public string SistemaAsociado { get; set; } = string.Empty;
}

public sealed class ConversacionEstadoOptionDto
{
    public string CodigoEstado { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool EsCerrado { get; set; }
    public int Orden { get; set; }
}

public sealed class ConversacionPlantillaFilters
{
    public string Search { get; set; } = string.Empty;
    public string? EstadoMeta { get; set; }
    public bool IncluirInactivas { get; set; }
}

public sealed class ConversacionPlantillaDto
{
    public long IdPlantilla { get; set; }
    public string NombreVisible { get; set; } = string.Empty;
    public string NombreMeta { get; set; } = string.Empty;
    public string Categoria { get; set; } = ConversacionPlantillaCategorias.Marketing;
    public string Idioma { get; set; } = "es_AR";
    public string EncabezadoTexto { get; set; } = string.Empty;
    public string CuerpoTexto { get; set; } = string.Empty;
    public string PieTexto { get; set; } = string.Empty;
    public string EjemplosVariablesJson { get; set; } = string.Empty;
    public string EstadoLocal { get; set; } = ConversacionPlantillaEstadosLocales.Borrador;
    public string EstadoMeta { get; set; } = string.Empty;
    public string MetaTemplateId { get; set; } = string.Empty;
    public string MetaRechazoMotivo { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    public DateTime FechaHoraGrabacion { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraSincronizacion { get; set; }
}

public sealed class ConversacionPlantillaSaveRequest
{
    public long IdPlantilla { get; set; }
    public string NombreVisible { get; set; } = string.Empty;
    public string NombreMeta { get; set; } = string.Empty;
    public string Categoria { get; set; } = ConversacionPlantillaCategorias.Marketing;
    public string Idioma { get; set; } = "es_AR";
    public string EncabezadoTexto { get; set; } = string.Empty;
    public string CuerpoTexto { get; set; } = string.Empty;
    public string PieTexto { get; set; } = string.Empty;
    public string EjemplosVariablesJson { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionPlantillaSubmitRequest
{
    public long IdPlantilla { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionPlantillaSendRequest
{
    public long IdConversacion { get; set; }
    public long IdPlantilla { get; set; }
    public List<string> ValoresVariables { get; set; } = [];
    public string? IdTecnicoAutor { get; set; }
    public string? UsuarioAccion { get; set; }
    public string? SistemaAccion { get; set; }
}

public sealed class ConversacionPlantillaMessageResultDto
{
    public long IdMensaje { get; set; }
    public string EstadoEnvio { get; set; } = string.Empty;
    public string WhatsAppMessageId { get; set; } = string.Empty;
}

public sealed class ConversacionPlantillaAutoValuesDto
{
    public List<string> Valores { get; set; } = [];
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public static class ConversacionPlantillaCategorias
{
    public const string Marketing = "MARKETING";
    public const string Utility = "UTILITY";
}

public static class ConversacionPlantillaEstadosLocales
{
    public const string Borrador = "BORRADOR";
    public const string Enviada = "ENVIADA";
    public const string Sincronizada = "SINCRONIZADA";
}

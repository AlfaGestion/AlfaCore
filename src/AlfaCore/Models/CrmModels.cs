namespace AlfaCore.Models;

public sealed class CrmFilters
{
    public string Texto { get; set; } = string.Empty;
    public int? IdEtapa { get; set; }
    public string? IdTecnico { get; set; }
    public bool SoloSinAsignar { get; set; }
    public string? ClienteCodigo { get; set; }
    public int? IdEtiqueta { get; set; }
    public bool IncluirCerradas { get; set; }
    public string OrdenarPor { get; set; } = CrmViewColumnKeys.Fecha;
    public bool OrdenDescendente { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CrmOpportunityDto
{
    public long IdOportunidad { get; set; }
    public int Numero { get; set; }
    public string CodigoVisible { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int IdEtapa { get; set; }
    public string EtapaNombre { get; set; } = string.Empty;
    public string EtapaColor { get; set; } = string.Empty;
    public int EtapaOrden { get; set; }
    public bool EtapaEsGanada { get; set; }
    public bool EtapaEsPerdida { get; set; }
    public int Prioridad { get; set; }
    public int Probabilidad { get; set; }
    public decimal ImporteEstimado { get; set; }
    public DateTime? FechaCierreEstimada { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public string CanalOrigen { get; set; } = string.Empty;
    public long? IdConversacion { get; set; }
    public int MensajesOrigen { get; set; }
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraCierre { get; set; }
    public List<CrmEtiquetaDto> Etiquetas { get; set; } = [];

    // Próxima acción = tarea pendiente más próxima por vencimiento (null si no tiene ninguna).
    public long? ProximaAccionId { get; set; }
    public string ProximaAccionTipo { get; set; } = string.Empty;
    public string ProximaAccionTitulo { get; set; } = string.Empty;
    public DateTime? ProximaAccionVencimiento { get; set; }
    public bool ProximaAccionVencida { get; set; }
}

public sealed class CrmOpportunityDetailDto : CrmOpportunityDto
{
    public List<CrmActividadDto> Actividad { get; set; } = [];
    public List<CrmMensajeOrigenDto> MensajesOrigenDetalle { get; set; } = [];
    public List<CrmTareaDto> Tareas { get; set; } = [];
}

public sealed class CrmTareaDto
{
    public long IdTarea { get; set; }
    public long IdOportunidad { get; set; }
    public string TipoAccion { get; set; } = CrmTareaTipos.Tarea;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public DateTime Vencimiento { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string TecnicoNombre { get; set; } = string.Empty;
    public bool Completada { get; set; }
    public DateTime? FechaHoraCompletada { get; set; }
    public bool Vencida { get; set; }
}

public sealed class CrmTareaSaveRequest
{
    public long IdTarea { get; set; }
    public long IdOportunidad { get; set; }
    public string TipoAccion { get; set; } = CrmTareaTipos.Tarea;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public DateTime Vencimiento { get; set; }
    public string? IdTecnico { get; set; }
    public string? UsuarioAccion { get; set; }
}

public static class CrmTareaTipos
{
    public const string Llamada = "LLAMADA";
    public const string Email = "EMAIL";
    public const string Reunion = "REUNION";
    public const string WhatsApp = "WHATSAPP";
    public const string Tarea = "TAREA";
}

public sealed class CrmEtapaDto
{
    public int IdEtapa { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool EsGanada { get; set; }
    public bool EsPerdida { get; set; }
    public int Orden { get; set; }
}

public sealed class CrmEtiquetaDto
{
    public int IdEtiqueta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class CrmActividadDto
{
    public long IdActividad { get; set; }
    public string TipoActividad { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class CrmMensajeOrigenDto
{
    public long IdMensaje { get; set; }
    public long IdConversacion { get; set; }
    public string Autor { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string TipoMensaje { get; set; } = string.Empty;
    public string Texto { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class CrmLookupDto
{
    public List<CrmEtapaDto> Etapas { get; set; } = [];
    public List<ConversacionTecnicoOptionDto> Tecnicos { get; set; } = [];
    public List<CrmEtiquetaDto> Etiquetas { get; set; } = [];
}

public sealed class CrmOpportunitySaveRequest
{
    public long IdOportunidad { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int? IdEtapa { get; set; }
    public int Prioridad { get; set; } = 1;
    public int Probabilidad { get; set; } = 10;
    public decimal ImporteEstimado { get; set; }
    public DateTime? FechaCierreEstimada { get; set; }
    public string? IdTecnico { get; set; }
    public string? ClienteCodigo { get; set; }
    public int? IdContacto { get; set; }
    public string? CanalOrigen { get; set; }
    public long? IdConversacion { get; set; }
    public List<long> IdMensajes { get; set; } = [];
    public List<int> IdEtiquetas { get; set; } = [];
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmQuickUpdateRequest
{
    public long IdOportunidad { get; set; }
    public int? IdEtapa { get; set; }
    public string? IdTecnico { get; set; }
    public int? Prioridad { get; set; }
    public int? Probabilidad { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmEtapaSaveRequest
{
    public int IdEtapa { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool EsGanada { get; set; }
    public bool EsPerdida { get; set; }
    public int Orden { get; set; }
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmEtiquetaSaveRequest
{
    public int IdEtiqueta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmNotaRequest
{
    public long IdOportunidad { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? UsuarioAccion { get; set; }
}

public sealed class CrmViewSettingsDto
{
    public string AgruparPor { get; set; } = CrmViewGroupKeys.Etapa;
    public List<CrmViewColumnDto> Columnas { get; set; } = [];
}

public sealed class CrmViewColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
}

public static class CrmViewColumnKeys
{
    public const string Numero = "numero";
    public const string Titulo = "titulo";
    public const string Etapa = "etapa";
    public const string Importe = "importe";
    public const string Probabilidad = "probabilidad";
    public const string Asignado = "asignado";
    public const string Cliente = "cliente";
    public const string Contacto = "contacto";
    public const string Etiquetas = "etiquetas";
    public const string Fecha = "fecha";
}

public static class CrmViewGroupKeys
{
    public const string None = "none";
    public const string Etapa = "etapa";
    public const string Tecnico = "tecnico";
    public const string Probabilidad = "probabilidad";
}

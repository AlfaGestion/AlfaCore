namespace AlfaCore.Models;

public static class TareaEstadoKeys
{
    public const string Pendiente = "PENDIENTE";
    public const string EnCurso = "EN_CURSO";
    public const string Completada = "COMPLETADA";

    public static readonly string[] All = [Pendiente, EnCurso, Completada];
}

public static class TareaPrioridadKeys
{
    public const string Alta = "ALTA";
    public const string Media = "MEDIA";
    public const string Baja = "BAJA";

    public static readonly string[] All = [Alta, Media, Baja];
}

public sealed class TareasPageDto
{
    public List<TareaListaDto> Listas { get; set; } = [];
    public List<TareaItemDto> Tareas { get; set; } = [];
    public List<TareaItemDto> Completadas { get; set; } = [];
    public List<TareaNotaRapidaDto> NotasRapidas { get; set; } = [];
    public List<TareaUsuarioDto> Usuarios { get; set; } = [];
    public List<TareaGrupoDto> Grupos { get; set; } = [];
}

public sealed class TareaListaDto
{
    public int IdLista { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool EsDefault { get; set; }
    public string UsuarioPropietario { get; set; } = string.Empty;
    public bool EsPropia { get; set; }
    public bool CompartidaConTodos { get; set; }
    public List<string> UsuariosCompartidos { get; set; } = [];
    public int Pendientes { get; set; }
    public int Completadas { get; set; }
}

public sealed class TareaItemDto
{
    public long IdTarea { get; set; }
    public int IdLista { get; set; }
    public string ListaNombre { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime? FechaVencimiento { get; set; }
    public string UsuarioAsignado { get; set; } = string.Empty;
    public string Estado { get; set; } = TareaEstadoKeys.Pendiente;
    public string Prioridad { get; set; } = TareaPrioridadKeys.Media;
    public string UsuarioAlta { get; set; } = string.Empty;
    public bool EsPropia { get; set; }
    public bool CompartidaConTodos { get; set; }
    public List<string> UsuariosCompartidos { get; set; } = [];
    public int Adjuntos { get; set; }
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraCompletada { get; set; }
}

public sealed class TareaAdjuntoDto
{
    public long IdAdjunto { get; set; }
    public long IdTarea { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string ContenidoBase64 { get; set; } = string.Empty;
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
}

public sealed class TareaAdjuntoUploadDto
{
    public string NombreArchivo { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public byte[] Contenido { get; set; } = [];
}

public sealed class TareaNotaRapidaDto
{
    public long IdNota { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public bool EsPropia { get; set; }
    public bool CompartidaConTodos { get; set; }
    public List<string> UsuariosCompartidos { get; set; } = [];
    public DateTime Fecha { get; set; }
    public bool Completada { get; set; }
    public int Orden { get; set; }
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraCompletada { get; set; }
}

public sealed class TareaNotaRapidaSaveRequest
{
    public long? IdNota { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class TareaUsuarioDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class TareaGrupoDto
{
    public string Grupo { get; set; } = string.Empty;
    public List<string> Usuarios { get; set; } = [];
}

public sealed class TareaSaveRequest
{
    public long? IdTarea { get; set; }
    public int IdLista { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime? FechaVencimiento { get; set; }
    public string UsuarioAsignado { get; set; } = string.Empty;
    public string Estado { get; set; } = TareaEstadoKeys.Pendiente;
    public string Prioridad { get; set; } = TareaPrioridadKeys.Media;
    public bool CompartirConTodos { get; set; }
    public List<string> UsuariosCompartidos { get; set; } = [];
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class TareaListaSaveRequest
{
    public int? IdLista { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class TareaCompartirRequest
{
    public TareaObjetoCompartidoTipo TipoObjeto { get; set; }
    public long IdObjeto { get; set; }
    public bool CompartirConTodos { get; set; }
    public List<string> Usuarios { get; set; } = [];
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class TareaGruposSaveRequest
{
    public List<TareaGrupoDto> Grupos { get; set; } = [];
    public string UsuarioAccion { get; set; } = string.Empty;
}

public enum TareaObjetoCompartidoTipo
{
    Lista,
    Nota,
    Tarea
}

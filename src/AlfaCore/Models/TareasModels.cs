namespace AlfaCore.Models;

public static class TareaEstadoKeys
{
    public const string Pendiente = "PENDIENTE";
    public const string EnCurso = "EN_CURSO";
    public const string Completada = "COMPLETADA";

    public static readonly string[] All = [Pendiente, EnCurso, Completada];
}

public sealed class TareasPageDto
{
    public List<TareaListaDto> Listas { get; set; } = [];
    public List<TareaItemDto> Tareas { get; set; } = [];
    public List<TareaItemDto> Completadas { get; set; } = [];
    public List<TareaNotaRapidaDto> NotasRapidas { get; set; } = [];
    public List<TareaUsuarioDto> Usuarios { get; set; } = [];
}

public sealed class TareaListaDto
{
    public int IdLista { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool EsDefault { get; set; }
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
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraModificacion { get; set; }
    public DateTime? FechaHoraCompletada { get; set; }
}

public sealed class TareaNotaRapidaDto
{
    public long IdNota { get; set; }
    public string Texto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public bool Completada { get; set; }
    public DateTime FechaHoraAlta { get; set; }
    public DateTime? FechaHoraCompletada { get; set; }
}

public sealed class TareaUsuarioDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class TareaListaSaveRequest
{
    public int? IdLista { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

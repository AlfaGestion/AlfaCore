namespace AlfaCore.Models;

public sealed class InformesPageDto
{
    public List<InformesArticuloResumenDto> Articulos { get; set; } = [];
    public List<InformesPlantillaDto> Plantillas { get; set; } = [];
    public InformesArticuloDetalleDto? Seleccionado { get; set; }
    public List<InformesComentarioDto> Comentarios { get; set; } = [];
    public List<InformesVersionDto> Versiones { get; set; } = [];
}

public class InformesArticuloResumenDto
{
    public long IdArticulo { get; set; }
    public long? IdPadre { get; set; }
    public string Categoria { get; set; } = InformesCategorias.EspacioTrabajo;
    public string Titulo { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
    public bool EsFavorito { get; set; }
    public bool EsCompartido { get; set; }
    public bool Bloqueado { get; set; }
    public bool Archivado { get; set; }
    public int Orden { get; set; }
    public DateTime FechaHoraModificacion { get; set; }
}

public sealed class InformesArticuloDetalleDto : InformesArticuloResumenDto
{
    public string Contenido { get; set; } = string.Empty;
    public string PortadaTipo { get; set; } = InformesPortadas.Ninguna;
    public string PortadaValor { get; set; } = string.Empty;
    public string UsuarioPropietario { get; set; } = string.Empty;
    public string UsuarioAlta { get; set; } = string.Empty;
    public string UsuarioModificacion { get; set; } = string.Empty;
    public Guid? TokenPublico { get; set; }
}

public sealed class InformesArticuloSaveRequest
{
    public long IdArticulo { get; set; }
    public long? IdPadre { get; set; }
    public string Categoria { get; set; } = InformesCategorias.EspacioTrabajo;
    public string Titulo { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public string PortadaTipo { get; set; } = InformesPortadas.Ninguna;
    public string PortadaValor { get; set; } = string.Empty;
    public bool Bloqueado { get; set; }
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class InformesArticuloCreateRequest
{
    public long? IdPadre { get; set; }
    public string Categoria { get; set; } = InformesCategorias.EspacioTrabajo;
    public string PlantillaKey { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class InformesComentarioDto
{
    public long IdComentario { get; set; }
    public long IdArticulo { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime FechaHoraAlta { get; set; }
    public bool Resuelto { get; set; }
}

public sealed class InformesComentarioRequest
{
    public long IdArticulo { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class InformesVersionDto
{
    public long IdVersion { get; set; }
    public long IdArticulo { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; }
}

public sealed class InformesPlantillaDto
{
    public string Key { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
}

public static class InformesCategorias
{
    public const string EspacioTrabajo = "WORKSPACE";
    public const string Privado = "PRIVATE";
}

public static class InformesPortadas
{
    public const string Ninguna = "NONE";
    public const string Gradiente = "GRADIENT";
    public const string Url = "URL";
}

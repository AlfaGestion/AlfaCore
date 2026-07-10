namespace AlfaCore.Models;

public sealed class NovedadesPageDto
{
    public List<NovedadResumenDto> Novedades { get; set; } = [];
    public NovedadDetalleDto? Seleccionada { get; set; }
    public NovedadesMetricasDto Metricas { get; set; } = new();
}

public sealed class NovedadesMetricasDto
{
    public int Borradores { get; set; }
    public int Programadas { get; set; }
    public int Publicadas { get; set; }
    public int LecturasPendientes { get; set; }
}

public class NovedadResumenDto
{
    public long IdNovedad { get; set; }
    public string Tipo { get; set; } = NovedadTipos.Manual;
    public string Estado { get; set; } = NovedadEstados.Borrador;
    public string Prioridad { get; set; } = NovedadPrioridades.Info;
    public string Version { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Bajada { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
    public string Icono { get; set; } = "bi-megaphone";
    public string Acento { get; set; } = NovedadAcentos.Azul;
    public DateTime? FechaProgramada { get; set; }
    public DateTime? FechaPublicacion { get; set; }
    public DateTime? VigenteHasta { get; set; }
    public bool MostrarPopup { get; set; }
    public bool LecturaObligatoria { get; set; }
    public bool RepetirHastaLeer { get; set; }
    public int Lecturas { get; set; }
    public int Vistas { get; set; }
    public DateTime FechaHoraModificacion { get; set; }
}

public sealed class NovedadDetalleDto : NovedadResumenDto
{
    public string Contenido { get; set; } = string.Empty;
    public string PortadaTipo { get; set; } = NovedadPortadas.Ninguna;
    public string PortadaValor { get; set; } = string.Empty;
    public string Footer { get; set; } = string.Empty;
    public string Segmento { get; set; } = NovedadSegmentos.Todos;
    public string UsuarioAlta { get; set; } = string.Empty;
    public string UsuarioModificacion { get; set; } = string.Empty;
    public bool LeidaPorUsuario { get; set; }
    public DateTime? FechaHoraLeida { get; set; }
}

public sealed class NovedadSaveRequest
{
    public long IdNovedad { get; set; }
    public string Tipo { get; set; } = NovedadTipos.Manual;
    public string Estado { get; set; } = NovedadEstados.Borrador;
    public string Prioridad { get; set; } = NovedadPrioridades.Info;
    public string Version { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Bajada { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public string Icono { get; set; } = "bi-megaphone";
    public string Acento { get; set; } = NovedadAcentos.Azul;
    public string PortadaTipo { get; set; } = NovedadPortadas.Ninguna;
    public string PortadaValor { get; set; } = string.Empty;
    public string Footer { get; set; } = string.Empty;
    public DateTime? FechaProgramada { get; set; }
    public DateTime? VigenteHasta { get; set; }
    public bool MostrarPopup { get; set; } = true;
    public bool LecturaObligatoria { get; set; }
    public bool RepetirHastaLeer { get; set; } = true;
    public bool ReiniciarSeguimiento { get; set; }
    public string Segmento { get; set; } = NovedadSegmentos.Todos;
    public string UsuarioAccion { get; set; } = string.Empty;
}

public sealed class NovedadesFilterDto
{
    public string Estado { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Busqueda { get; set; } = string.Empty;
}

public static class NovedadTipos
{
    public const string Manual = "MANUAL";
    public const string BoletinPatch = "BOLETIN_PATCH";
}

public static class NovedadEstados
{
    public const string Borrador = "BORRADOR";
    public const string Programado = "PROGRAMADO";
    public const string Publicado = "PUBLICADO";
    public const string Archivado = "ARCHIVADO";
}

public static class NovedadPrioridades
{
    public const string Info = "INFO";
    public const string Importante = "IMPORTANTE";
    public const string Critico = "CRITICO";
}

public static class NovedadSegmentos
{
    public const string Todos = "TODOS";
}

public static class NovedadPortadas
{
    public const string Ninguna = "NONE";
    public const string Gradiente = "GRADIENT";
    public const string Url = "URL";
}

public static class NovedadAcentos
{
    public const string Azul = "azul";
    public const string Verde = "verde";
    public const string Cian = "cian";
    public const string Ambar = "ambar";
    public const string Rojo = "rojo";
    public const string Grafito = "grafito";
}

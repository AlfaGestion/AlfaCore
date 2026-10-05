namespace AlfaCore.Models;

/// <summary>Aviso de la campana de la barra superior (recordatorios y novedades del usuario).</summary>
public sealed class AvisoDto
{
    /// <summary>Identifica el aviso para marcarlo como leído (ej. "cal:123:aviso", "res:45", "nov:7").</summary>
    public string Clave { get; set; } = string.Empty;
    public string Tipo { get; set; } = AvisoTipos.Evento;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    /// <summary>Momento al que se refiere el aviso (inicio del evento, alta de la reserva, publicación).</summary>
    public DateTime FechaHora { get; set; }
    /// <summary>Ruta interna (sin base) a la que lleva el aviso; vacía si se lee en el mismo panel.</summary>
    public string Ruta { get; set; } = string.Empty;
    public bool Leido { get; set; }
    /// <summary>Contenido ampliado que se muestra en el panel (novedades).</summary>
    public string Contenido { get; set; } = string.Empty;
}

public static class AvisoTipos
{
    public const string Guardia = "GUARDIA";
    public const string Reunion = "REUNION";
    public const string Capacitacion = "CAPACITACION";
    public const string Evento = "EVENTO";
    public const string Reserva = "RESERVA";
    public const string Asignado = "ASIGNADO";
    public const string Novedad = "NOVEDAD";
}

/// <summary>Evento de calendario con los datos que necesita el centro de avisos.</summary>
public sealed class AvisoEventoFuente
{
    public long IdEvento { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool TodoElDia { get; set; }
    public string IdTecnico { get; set; } = string.Empty;
    public string UsuarioAlta { get; set; } = string.Empty;
    public DateTime? FechaAlta { get; set; }
    public int? MinutosAntes { get; set; }
    public long? IdReserva { get; set; }
    public string ReservaCliente { get; set; } = string.Empty;
    public string ReservaRazonSocial { get; set; } = string.Empty;
    public string ReservaTipoCapacitacion { get; set; } = string.Empty;
    public DateTime? ReservaFechaAlta { get; set; }
}

/// <summary>Novedad publicada que el usuario todavía no leyó.</summary>
public sealed class AvisoNovedadFuente
{
    public long IdNovedad { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Bajada { get; set; } = string.Empty;
    public string Resumen { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime FechaPublicacion { get; set; }
}

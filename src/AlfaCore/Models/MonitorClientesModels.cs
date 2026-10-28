namespace AlfaCore.Models;

/// <summary>Estados del Monitor de Clientes, de peor a mejor (el orden se usa para ordenar).</summary>
public enum MonitorEstado
{
    Critico = 0,
    Aviso = 1,
    SinDatos = 2,
    Ok = 3
}

/// <summary>Reporte que manda el sistema legacy (POST /api/vb6/monitor-estado).</summary>
public sealed class MonitorReporteRequest
{
    public string LicenciaPrincipal { get; init; } = string.Empty;
    public string DbServidor { get; init; } = string.Empty;
    public string DbNombre { get; init; } = string.Empty;
    public string? HostCliente { get; init; }
    public string? VersionSistema { get; init; }

    /// <summary>Hora del equipo del cliente al armar el reporte. Con ella se corrige el desfase de
    /// reloj: todas las fechas del reporte se pasan a la hora del servidor.</summary>
    public DateTime? FechaHoraCliente { get; init; }

    public IReadOnlyList<MonitorReporteItem> Items { get; init; } = [];
}

public sealed class MonitorReporteItem
{
    public string Tipo { get; init; } = string.Empty;
    public string Destino { get; init; } = string.Empty;
    public DateTime? UltimoOk { get; init; }
    public DateTime? UltimoIntento { get; init; }
    public string? Resultado { get; init; }
    public string? Mensaje { get; init; }
}

public sealed class MonitorReporteResultado
{
    public string ClienteCodigo { get; init; } = string.Empty;
    public int IdOrigen { get; init; }
    public int ItemsGrabados { get; init; }
    public int ItemsIgnorados { get; init; }
}

public sealed class MonitorTipoControlDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public string Medida { get; init; } = "EDAD";
    public int? AvisoMinutos { get; init; }
    public int? CriticoMinutos { get; init; }
    public int Orden { get; init; }
    public bool Activo { get; init; }
}

/// <summary>Una fila del tablero: un control de un sistema de un cliente, ya evaluado.</summary>
public sealed class MonitorFilaDto
{
    public int IdOrigen { get; init; }
    public string ClienteCodigo { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string PrioridadCodigo { get; init; } = string.Empty;
    public string PrioridadDescripcion { get; init; } = string.Empty;
    public string DbServidor { get; init; } = string.Empty;
    public string DbNombre { get; init; } = string.Empty;
    public string? HostCliente { get; init; }
    public string? VersionSistema { get; init; }
    public DateTime UltimoReporte { get; init; }
    public string TipoCodigo { get; init; } = string.Empty;
    public string TipoNombre { get; init; } = string.Empty;
    public int TipoOrden { get; init; }
    public string Destino { get; init; } = string.Empty;
    public DateTime? UltimoOk { get; init; }
    public DateTime? UltimoIntento { get; init; }
    public string? Resultado { get; init; }
    public string? Mensaje { get; init; }
    public MonitorEstado Estado { get; init; }

    /// <summary>Por qué quedó en ese estado, en palabras (ej. "Sin envío hace 3 h 20 min; límite 3 h").</summary>
    public string Motivo { get; init; } = string.Empty;
}

public sealed class MonitorTableroDto
{
    public DateTime FechaHora { get; init; }
    public IReadOnlyList<MonitorFilaDto> Filas { get; init; } = [];
    public IReadOnlyList<MonitorTipoControlDto> Tipos { get; init; } = [];
}

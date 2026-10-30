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

    /// <summary>EDAD, RESULTADO o VENCE. En VENCE, <see cref="UltimoOk"/> es la fecha en que vence.</summary>
    public string Medida { get; init; } = "EDAD";

    public bool EsVencimiento => string.Equals(Medida, "VENCE", StringComparison.OrdinalIgnoreCase);
    public string Destino { get; init; } = string.Empty;
    public DateTime? UltimoOk { get; init; }
    public DateTime? UltimoIntento { get; init; }
    public string? Resultado { get; init; }
    public string? Mensaje { get; init; }
    public MonitorEstado Estado { get; init; }

    /// <summary>Por qué quedó en ese estado, en palabras (ej. "Sin envío hace 3 h 20 min; límite 3 h").</summary>
    public string Motivo { get; init; } = string.Empty;

    // Seguimiento (MON_SEGUIMIENTO). Se completan después de evaluar.
    public string? TomadoPor { get; set; }
    public DateTime? TomadoFecha { get; set; }
    public DateTime? SilenciadoHasta { get; set; }
    public string? SilenciadoPor { get; set; }
    public string? Nota { get; set; }

    /// <summary>True si está silenciada en el momento en que se armó el tablero.</summary>
    public bool Silenciado { get; set; }

    public bool Tomado => !string.IsNullOrWhiteSpace(TomadoPor);

    /// <summary>Identifica la fila entre reportes: origen + tipo + destino.</summary>
    public string Clave => MonitorClave.Armar(IdOrigen, TipoCodigo, Destino);
}

public static class MonitorClave
{
    public static string Armar(int idOrigen, string tipoCodigo, string? destino)
        => $"{idOrigen}|{(tipoCodigo ?? string.Empty).Trim().ToUpperInvariant()}|{(destino ?? string.Empty).Trim().ToUpperInvariant()}";
}

public sealed class MonitorTableroDto
{
    public DateTime FechaHora { get; init; }
    public IReadOnlyList<MonitorFilaDto> Filas { get; init; } = [];
    public IReadOnlyList<MonitorTipoControlDto> Tipos { get; init; } = [];
}

/// <summary>Cliente activo del padrón central sin ningún origen registrado en el monitor.</summary>
public sealed class MonitorClienteSinReporteDto
{
    public string ClienteCodigo { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string Clasificacion { get; init; } = string.Empty;
    public string ClasificacionDescripcion { get; init; } = string.Empty;
}

/// <summary>Cambio de seguimiento de una fila desde el tablero.</summary>
public sealed class MonitorSeguimientoRequest
{
    public int IdOrigen { get; init; }
    public string TipoCodigo { get; init; } = string.Empty;
    public string Destino { get; init; } = string.Empty;

    /// <summary>Usuario que hace el cambio.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>True = lo toma el usuario; false = queda libre; null = no cambia.</summary>
    public bool? Tomar { get; init; }

    /// <summary>Si <see cref="CambiarSilencio"/> es true: hasta cuándo queda silenciada (null = quita el silencio).</summary>
    public DateTime? SilenciarHasta { get; init; }
    public bool CambiarSilencio { get; init; }

    /// <summary>Si <see cref="CambiarNota"/> es true: la nota nueva (vacía = la borra).</summary>
    public string? Nota { get; init; }
    public bool CambiarNota { get; init; }
}

/// <summary>Configuración de los avisos al chat de soporte (MON_CONFIG).</summary>
public sealed class MonitorAvisoConfigDto
{
    public bool Activo { get; set; } = true;

    /// <summary>Códigos de prioridad que avisan, separados por coma (ej. "1,2"). Vacío = todas.</summary>
    public string Prioridades { get; set; } = "1,2";

    /// <summary>Cada cuánto se repite el aviso si siguen las mismas alertas. 0 = solo cuando aparece una nueva.</summary>
    public int RepetirMinutos { get; set; } = 120;

    /// <summary>Usuarios que además reciben un push, separados por coma.</summary>
    public string UsuariosPush { get; set; } = string.Empty;

    /// <summary>Hilo interno de Conversaciones (ALFANET2007) donde se publican los avisos. Se crea solo.</summary>
    public long? IdHilo { get; set; }

    public DateTime? UltimoEnvio { get; set; }

    /// <summary>Claves de las alertas que había en el último ciclo, ordenadas y separadas por ";".</summary>
    public string UltimaFirma { get; set; } = string.Empty;

    /// <summary>Pedido de "Enviar aviso ahora" desde el tablero; el proceso lo atiende en el próximo minuto.</summary>
    public bool Forzar { get; set; }

    public IReadOnlyList<string> PrioridadesLista
        => Prioridades.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> UsuariosPushLista
        => UsuariosPush.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Una versión de AlfaActualizacion_Monitor (MON_ACTUALIZACION).</summary>
public sealed class MonitorActualizacionDto
{
    public int IdVersion { get; init; }
    public string Version { get; init; } = string.Empty;

    /// <summary>Ruta del setup dentro de AlfaArchivos (ej. ACTUALIZACIONES/20261029-01/AlfaActualizacion_Monitor_20261029-01.exe).</summary>
    public string ArchivoRemoto { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long TamanioBytes { get; init; }
    public string? Notas { get; init; }

    /// <summary>NADIE (pausada), PRUEBA, P1P2 o TODOS.</summary>
    public string Liberacion { get; set; } = MonitorLiberacion.Nadie;

    /// <summary>Códigos de cliente separados por coma que la reciben en cualquier liberación salvo NADIE.</summary>
    public string? ClientesPrueba { get; set; }
    public DateTime FechaAlta { get; init; }
    public string? UsuarioAlta { get; init; }
    public DateTime? FechaLiberacion { get; init; }
    public string? UsuarioLiberacion { get; init; }

    /// <summary>Sistemas que informan esta versión instalada (con la última actualización OK).</summary>
    public int Instalaciones { get; init; }

    public IReadOnlyList<string> ClientesPruebaLista
        => (ClientesPrueba ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Un sistema (base) que informa una versión instalada, para ver quién actualizó.</summary>
public sealed class MonitorInstalacionDto
{
    public string ClienteCodigo { get; init; } = string.Empty;
    public string RazonSocial { get; init; } = string.Empty;
    public string? LicenciaPrincipal { get; init; }
    public string DbServidor { get; init; } = string.Empty;
    public string DbNombre { get; init; } = string.Empty;
    public string? HostCliente { get; init; }
    public string? Resultado { get; init; }
    public DateTime FechaReporte { get; init; }
}

public static class MonitorLiberacion
{
    public const string Nadie = "NADIE";
    public const string Prueba = "PRUEBA";
    public const string P1P2 = "P1P2";
    public const string Todos = "TODOS";

    public static readonly IReadOnlyList<string> Valores = [Nadie, Prueba, P1P2, Todos];
}

/// <summary>Versión que le toca instalar a un sistema (GET /api/vb6/actualizacion-pendiente).</summary>
public sealed class MonitorActualizacionPendienteDto
{
    public string Version { get; init; } = string.Empty;
    public string ArchivoRemoto { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long TamanioBytes { get; init; }
}

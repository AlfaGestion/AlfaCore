namespace AlfaCore.Models;

/// <summary>Un caso de soporte (conversación) abierto, tal como lo ve el resumen diario.</summary>
public sealed class BriefSoportePendienteDto
{
    public long Id { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Contacto { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string? FechaApertura { get; set; }
    public string? FechaUltimoMensaje { get; set; }

    /// <summary>"cliente" o "nosotros". Las notas internas no cuentan.</summary>
    public string UltimoEscribio { get; set; } = string.Empty;

    public string Tecnico { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;

    /// <summary>
    /// Horas desde el último mensaje del cliente si el cliente escribió último; 0 si respondimos nosotros.
    /// </summary>
    public decimal HorasSinRespuesta { get; set; }

    public string UltimoMensajeCliente { get; set; } = string.Empty;
}

public sealed class BriefSoportePendientesResponse
{
    public string Generado { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Maximo { get; set; }
    public List<BriefSoportePendienteDto> Items { get; set; } = [];
}

/// <summary>Ítem de actividad: parte de horas o ticket.</summary>
public sealed class BriefActividadItemDto
{
    /// <summary>PARTE_HORAS o TICKET.</summary>
    public string Tipo { get; set; } = string.Empty;

    public long Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string? Fecha { get; set; }
    public string? Vencimiento { get; set; }

    /// <summary>Solo para partes de horas.</summary>
    public int? Minutos { get; set; }

    /// <summary>Solo para tickets: 0 (baja) a 3 (urgente).</summary>
    public int? Prioridad { get; set; }
}

public sealed class BriefActividadResponse
{
    public string Generado { get; set; } = string.Empty;
    public string Desde { get; set; } = string.Empty;
    public string Tecnico { get; set; } = string.Empty;
    public int Maximo { get; set; }
    public List<BriefActividadItemDto> PartesHoras { get; set; } = [];
    public List<BriefActividadItemDto> TicketsAbiertosAsignados { get; set; } = [];
    public List<BriefActividadItemDto> TicketsSinAsignar { get; set; } = [];
    public List<BriefActividadItemDto> TicketsCerradosDesde { get; set; } = [];
}

/// <summary>Oportunidad abierta del CRM con su próxima acción, para el seguimiento.</summary>
public sealed class BriefOportunidadDto
{
    public long Id { get; set; }
    public int Numero { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Etapa { get; set; } = string.Empty;
    public decimal ImporteEstimado { get; set; }
    public int Probabilidad { get; set; }
    public int Prioridad { get; set; }
    public string Tecnico { get; set; } = string.Empty;
    public string? FechaAlta { get; set; }
    public string? FechaCierreEstimada { get; set; }
    public string? UltimoMovimiento { get; set; }
    public int DiasSinMovimiento { get; set; }
    public string ProximaAccion { get; set; } = string.Empty;
    public string ProximaAccionTipo { get; set; } = string.Empty;
    public string? ProximaAccionVencimiento { get; set; }

    /// <summary>VENCIDA, PROGRAMADA o SIN_ACCION.</summary>
    public string ProximaAccionEstado { get; set; } = string.Empty;
}

public sealed class BriefOportunidadesResponse
{
    public string Generado { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Maximo { get; set; }
    public bool SoloMias { get; set; }
    public List<BriefOportunidadDto> Items { get; set; } = [];
}

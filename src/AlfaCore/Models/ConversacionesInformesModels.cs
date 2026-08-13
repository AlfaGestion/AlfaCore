namespace AlfaCore.Models;

/// <summary>
/// Cabecera de un informe mensual de atención de Conversaciones (un período por informe),
/// con sus filas (una por cliente, o por contacto si no tiene cliente asociado).
/// </summary>
public sealed class ConversacionInformeMensualDto
{
    public int IdInforme { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public DateTime FechaGeneracion { get; set; }
    public string UsuarioGeneracion { get; set; } = string.Empty;
    public string Estado { get; set; } = "GENERADO";
    public List<ConversacionInformeFilaDto> Filas { get; set; } = [];

    public string PeriodoLabel => $"{Mes:00}/{Anio}";
}

/// <summary>Una fila del informe: un cliente (agrupando sus contactos) o un contacto sin cliente.</summary>
public sealed class ConversacionInformeFilaDto
{
    public int IdDetalle { get; set; }
    public int IdInforme { get; set; }

    /// <summary>CLIENTE | CONTACTO | SININDENT.</summary>
    public string TipoFila { get; set; } = "CLIENTE";
    public string ClienteCodigo { get; set; } = string.Empty;
    public int? IdContacto { get; set; }
    public string NombreMostrar { get; set; } = string.Empty;

    public string ClasificacionCodigo { get; set; } = string.Empty;
    public string ClasificacionDesc { get; set; } = string.Empty;
    public string CategoriaDesc { get; set; } = string.Empty;
    /// <summary>Abonado | Suspendido | SinAbono.</summary>
    public string EstadoAbono { get; set; } = "SinAbono";

    public int CantConversaciones { get; set; }
    public int CantDias { get; set; }
    public int CantMensajes { get; set; }
    public int CantMensajesEntrantes { get; set; }
    public int CantMensajesSalientes { get; set; }
    public int MinutosTotales { get; set; }
    public int MinutosFacturables { get; set; }

    // Fase 2
    public string ResumenBorrador { get; set; } = string.Empty;
    public string ResumenEditado { get; set; } = string.Empty;
    public bool ResumenGeneradoIA { get; set; }
    public string EstadoEnvio { get; set; } = "PENDIENTE";
    public DateTime? FechaEnvio { get; set; }
    public string CanalEnvio { get; set; } = string.Empty;

    public bool EsAbonado => string.Equals(EstadoAbono, "Abonado", StringComparison.OrdinalIgnoreCase);
    public string HorasTotalesLabel => FormatHoras(MinutosTotales);
    public string HorasFacturablesLabel => FormatHoras(MinutosFacturables);

    private static string FormatHoras(int minutos)
    {
        if (minutos <= 0)
            return "0h";
        var h = minutos / 60;
        var m = minutos % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }
}

/// <summary>Ítem del listado de informes ya generados (para elegir cuál abrir).</summary>
public sealed class ConversacionInformeListItemDto
{
    public int IdInforme { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public DateTime FechaGeneracion { get; set; }
    public int CantFilas { get; set; }

    public string PeriodoLabel => $"{Mes:00}/{Anio}";
}

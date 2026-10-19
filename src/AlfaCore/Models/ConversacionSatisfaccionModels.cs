namespace AlfaCore.Models;

/// <summary>Filtros del informe de satisfacción (Conversaciones → Informes → Satisfacción).</summary>
public sealed class SatisfaccionFiltros
{
    public DateTime Desde { get; set; } = DateTime.Today.AddDays(-29);
    public DateTime Hasta { get; set; } = DateTime.Today;

    /// <summary>Persona del equipo (IdTecnico); "ASISTENTE" = atendidas sólo por el asistente; vacío = todas.</summary>
    public string? Atendio { get; set; }

    public int? IdNumeroWhatsApp { get; set; }

    public const string AtendioAsistente = "ASISTENTE";
}

public sealed class SatisfaccionInformeDto
{
    public int Enviadas { get; set; }
    public int Respondidas { get; set; }
    public int MuyBien { get; set; }
    public int Regular { get; set; }
    public int Mal { get; set; }
    public int ConComentario { get; set; }

    public double PorcentajeRespuesta => Enviadas == 0 ? 0 : Math.Round(Respondidas * 100.0 / Enviadas, 1);

    /// <summary>% de "Muy bien" sobre las respondidas.</summary>
    public double PorcentajeSatisfechos => Respondidas == 0 ? 0 : Math.Round(MuyBien * 100.0 / Respondidas, 1);

    public double PorcentajeInsatisfechos => Respondidas == 0 ? 0 : Math.Round(Mal * 100.0 / Respondidas, 1);

    /// <summary>Promedio en escala 1 (Mal) a 3 (Muy bien).</summary>
    public double Promedio => Respondidas == 0 ? 0 : Math.Round((MuyBien * 3.0 + Regular * 2.0 + Mal) / Respondidas, 2);

    public List<SatisfaccionPorPersonaDto> PorPersona { get; set; } = [];
    public List<SatisfaccionSemanaDto> PorSemana { get; set; } = [];
    public List<SatisfaccionRespuestaDto> Respuestas { get; set; } = [];
}

public sealed class SatisfaccionPorPersonaDto
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Enviadas { get; set; }
    public int Respondidas { get; set; }
    public int MuyBien { get; set; }
    public int Regular { get; set; }
    public int Mal { get; set; }
    public double PorcentajeSatisfechos => Respondidas == 0 ? 0 : Math.Round(MuyBien * 100.0 / Respondidas, 1);
}

public sealed class SatisfaccionSemanaDto
{
    public DateTime Desde { get; set; }
    public int Respondidas { get; set; }
    public int MuyBien { get; set; }
    public int Mal { get; set; }
    public double PorcentajeSatisfechos => Respondidas == 0 ? 0 : Math.Round(MuyBien * 100.0 / Respondidas, 1);
}

public sealed record SatisfaccionOpcion(string Valor, string Texto);

public sealed class SatisfaccionOpcionesDto
{
    public List<SatisfaccionOpcion> Personas { get; set; } = [];
    public List<SatisfaccionOpcion> Numeros { get; set; } = [];
}

public sealed class SatisfaccionRespuestaDto
{
    public long IdEncuesta { get; set; }
    public long IdConversacion { get; set; }
    public DateTime FechaRespuesta { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Atendio { get; set; } = string.Empty;
    public int Calificacion { get; set; }
    public string Comentario { get; set; } = string.Empty;
}

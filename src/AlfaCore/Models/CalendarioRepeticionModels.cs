namespace AlfaCore.Models;

/// <summary>
/// Regla de repetición de un evento del Calendario (estilo Google Calendar). Se guarda como JSON en
/// CAL_EVENTOS.ReglaRepeticion de cada evento de la serie; las repeticiones se graban como eventos
/// reales unidos por CAL_EVENTOS.IdSerie (así recordatorios, avisos e indicador funcionan igual).
/// </summary>
public sealed class CalendarioRepeticionDto
{
    /// <summary>NINGUNA, DIARIA, SEMANAL, MENSUAL o ANUAL.</summary>
    public string Frecuencia { get; set; } = CalendarioFrecuencias.Ninguna;

    /// <summary>Cada cuántos días/semanas/meses/años (1 = todos).</summary>
    public int Intervalo { get; set; } = 1;

    /// <summary>Solo SEMANAL: días en que se repite (vacío = el día de la semana del inicio).</summary>
    public List<DayOfWeek> DiasSemana { get; set; } = [];

    /// <summary>Solo MENSUAL: DIA_MES ("el día 6") o DIA_SEMANA ("el primer martes").</summary>
    public string ModoMensual { get; set; } = CalendarioModosMensuales.DiaMes;

    /// <summary>NUNCA (hasta un año), FECHA o VECES.</summary>
    public string Fin { get; set; } = CalendarioFinesRepeticion.Nunca;

    public DateTime? FinFecha { get; set; }

    public int FinVeces { get; set; } = 10;

    public bool SeRepite => !string.Equals(Frecuencia, CalendarioFrecuencias.Ninguna, StringComparison.OrdinalIgnoreCase);

    public CalendarioRepeticionDto Clonar() => new()
    {
        Frecuencia = Frecuencia,
        Intervalo = Intervalo,
        DiasSemana = [.. DiasSemana],
        ModoMensual = ModoMensual,
        Fin = Fin,
        FinFecha = FinFecha,
        FinVeces = FinVeces
    };
}

public static class CalendarioFrecuencias
{
    public const string Ninguna = "NINGUNA";
    public const string Diaria = "DIARIA";
    public const string Semanal = "SEMANAL";
    public const string Mensual = "MENSUAL";
    public const string Anual = "ANUAL";
}

public static class CalendarioModosMensuales
{
    public const string DiaMes = "DIA_MES";
    public const string DiaSemana = "DIA_SEMANA";
}

public static class CalendarioFinesRepeticion
{
    public const string Nunca = "NUNCA";
    public const string Fecha = "FECHA";
    public const string Veces = "VECES";
}

/// <summary>A qué eventos de una serie se aplica una edición o una baja.</summary>
public static class CalendarioAlcancesSerie
{
    public const string SoloEste = "SOLO_ESTE";
    public const string EsteYSiguientes = "SIGUIENTES";
    public const string Todos = "TODOS";
}

/// <summary>Indicador fijo de la barra superior (antes atado a las guardias).</summary>
public sealed class CalendarioIndicadorConfigDto
{
    /// <summary>Tipo especial: el indicador muestra solo los eventos marcados con la casilla.</summary>
    public const string SoloMarcados = "MARCADOS";

    public bool Activo { get; set; }

    /// <summary>
    /// <see cref="SoloMarcados"/> o un tipo de evento (todos los de ese tipo). Los eventos marcados
    /// con "Mostrar en la barra superior" se muestran siempre.
    /// </summary>
    public string Tipo { get; set; } = SoloMarcados;
    public string Etiqueta { get; set; } = "A cargo";
}

/// <summary>
/// Responsables del Calendario: técnicos y usuarios del sistema (para las empresas sin técnicos).
/// Un usuario se identifica como "U:nombre" y se graba con IdTecnico vacío y su nombre en
/// TecnicoNombre.
/// </summary>
public static class CalendarioResponsables
{
    public const string PrefijoUsuario = "U:";

    public static bool EsUsuario(string? id, out string usuario)
    {
        var valor = (id ?? string.Empty).Trim();
        if (valor.StartsWith(PrefijoUsuario, StringComparison.OrdinalIgnoreCase) && valor.Length > PrefijoUsuario.Length)
        {
            usuario = valor[PrefijoUsuario.Length..].Trim();
            return usuario.Length > 0;
        }

        usuario = string.Empty;
        return false;
    }

    public static string IdUsuario(string usuario) => PrefijoUsuario + usuario.Trim();
}

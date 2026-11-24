using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Horario de atención de la empresa (Conversaciones → Automatizaciones → Horario): qué días y en qué franja se
/// trabaja. Única implementación para "¿es horario laboral?" y para contar horas hábiles (plazos de Tickets).
/// Si la franja no se puede leer, el día laborable cuenta completo (mismo criterio que tenía el bot).
/// </summary>
public sealed class HorarioLaboral
{
    private readonly bool[] _dias = new bool[7];
    private readonly TimeSpan? _desde;
    private readonly TimeSpan? _hasta;

    public HorarioLaboral(IReadOnlyList<DayOfWeek> dias, TimeSpan? desde, TimeSpan? hasta)
    {
        foreach (var d in dias)
            _dias[(int)d] = true;
        _desde = desde;
        _hasta = hasta;
    }

    public static HorarioLaboral Desde(ConversacionAutomatizacionesConfigDto config)
    {
        var dias = new List<DayOfWeek>();
        if (config.Lunes) dias.Add(DayOfWeek.Monday);
        if (config.Martes) dias.Add(DayOfWeek.Tuesday);
        if (config.Miercoles) dias.Add(DayOfWeek.Wednesday);
        if (config.Jueves) dias.Add(DayOfWeek.Thursday);
        if (config.Viernes) dias.Add(DayOfWeek.Friday);
        if (config.Sabado) dias.Add(DayOfWeek.Saturday);
        if (config.Domingo) dias.Add(DayOfWeek.Sunday);

        if (TimeSpan.TryParse(config.HoraDesde, out var desde) && TimeSpan.TryParse(config.HoraHasta, out var hasta))
            return new HorarioLaboral(dias, desde, hasta);
        return new HorarioLaboral(dias, null, null);
    }

    /// <summary>Horas de una jornada (para expresar plazos en días hábiles). Sin franja: 24.</summary>
    public double HorasPorJornada
        => _desde is TimeSpan d && _hasta is TimeSpan h
            ? (d <= h ? (h - d).TotalHours : 24 - (d - h).TotalHours) is var x && x > 0 ? x : 24
            : 24;

    public bool EsHabil(DateTime momento)
    {
        if (!_dias[(int)momento.DayOfWeek])
            return false;
        if (_desde is not TimeSpan desde || _hasta is not TimeSpan hasta)
            return true;

        var hora = momento.TimeOfDay;
        return desde <= hasta
            ? hora >= desde && hora <= hasta
            : hora >= desde || hora <= hasta;
    }

    /// <summary>Horas hábiles entre dos momentos (0 si hasta ≤ desde).</summary>
    public double HorasHabilesEntre(DateTime desde, DateTime hasta)
    {
        if (hasta <= desde)
            return 0;

        double total = 0;
        for (var dia = desde.Date; dia <= hasta.Date; dia = dia.AddDays(1))
        {
            foreach (var (ini, fin) in Franjas(dia))
            {
                var a = ini > desde ? ini : desde;
                var b = fin < hasta ? fin : hasta;
                if (b > a)
                    total += (b - a).TotalHours;
            }
        }
        return total;
    }

    /// <summary>Momento en que se cumplen <paramref name="horas"/> hábiles contadas desde <paramref name="desde"/>.</summary>
    public DateTime SumarHorasHabiles(DateTime desde, double horas)
    {
        if (horas <= 0)
            return desde;
        if (!_dias.Any(x => x))
            return desde.AddHours(horas);

        var restante = horas;
        // Tope de seguridad: 3 años de calendario.
        for (var dia = desde.Date; dia <= desde.Date.AddYears(3); dia = dia.AddDays(1))
        {
            foreach (var (ini, fin) in Franjas(dia))
            {
                var a = ini > desde ? ini : desde;
                if (fin <= a)
                    continue;
                var disponible = (fin - a).TotalHours;
                if (disponible >= restante)
                    return a.AddHours(restante);
                restante -= disponible;
            }
        }
        return desde.AddHours(horas);
    }

    private IEnumerable<(DateTime Inicio, DateTime Fin)> Franjas(DateTime dia)
    {
        if (!_dias[(int)dia.DayOfWeek])
            yield break;

        if (_desde is not TimeSpan desde || _hasta is not TimeSpan hasta)
        {
            yield return (dia, dia.AddDays(1));
            yield break;
        }

        if (desde <= hasta)
        {
            yield return (dia + desde, dia + hasta);
        }
        else
        {
            // Franja nocturna (ej. 20:00 a 06:00): la madrugada y la noche del mismo día.
            yield return (dia, dia + hasta);
            yield return (dia + desde, dia.AddDays(1));
        }
    }
}

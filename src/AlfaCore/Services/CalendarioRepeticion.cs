using System.Globalization;
using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Cálculo de las fechas de una serie de eventos repetidos y su descripción en castellano. Sin IO:
/// lo usan el servicio (para grabar los eventos) y la ventana de evento (para la vista previa).
/// </summary>
public static class CalendarioRepeticion
{
    /// <summary>Tope de repeticiones por serie (un año de eventos diarios).</summary>
    public const int MaximoOcurrencias = 366;

    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly DayOfWeek[] OrdenSemana =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// Inicios de cada evento de la serie, empezando por <paramref name="inicio"/> (siempre incluido).
    /// "No finaliza" genera hasta un año desde el inicio; nunca más de <see cref="MaximoOcurrencias"/>.
    /// </summary>
    public static List<DateTime> GenerarInicios(DateTime inicio, CalendarioRepeticionDto? regla)
    {
        var resultado = new List<DateTime> { inicio };
        if (regla is null || !regla.SeRepite)
            return resultado;

        var intervalo = Math.Clamp(regla.Intervalo, 1, 99);
        var limiteFecha = regla.Fin switch
        {
            CalendarioFinesRepeticion.Fecha when regla.FinFecha is DateTime fin => fin.Date.AddDays(1).AddTicks(-1),
            CalendarioFinesRepeticion.Veces => DateTime.MaxValue,
            _ => inicio.AddYears(1)
        };
        var limiteCantidad = regla.Fin == CalendarioFinesRepeticion.Veces
            ? Math.Clamp(regla.FinVeces, 1, MaximoOcurrencias)
            : MaximoOcurrencias;

        bool Agregar(DateTime fecha)
        {
            if (fecha <= inicio)
                return true;
            if (fecha > limiteFecha || resultado.Count >= limiteCantidad)
                return false;
            resultado.Add(fecha);
            return true;
        }

        var hora = inicio.TimeOfDay;
        switch (regla.Frecuencia.ToUpperInvariant())
        {
            case CalendarioFrecuencias.Diaria:
                for (var k = 1; ; k++)
                    if (!Agregar(inicio.AddDays((double)k * intervalo))) break;
                break;

            case CalendarioFrecuencias.Semanal:
            {
                var dias = regla.DiasSemana.Count > 0 ? regla.DiasSemana.ToHashSet() : [inicio.DayOfWeek];
                var lunes = inicio.Date.AddDays(-(((int)inicio.DayOfWeek + 6) % 7));
                var seguir = true;
                for (var semana = 0; seguir && semana < 52 * 99; semana += intervalo)
                {
                    for (var i = 0; i < 7 && seguir; i++)
                    {
                        if (!dias.Contains(OrdenSemana[i]))
                            continue;
                        seguir = Agregar(lunes.AddDays(semana * 7 + i) + hora);
                    }
                }
                break;
            }

            case CalendarioFrecuencias.Mensual:
            {
                var porDiaSemana = regla.ModoMensual == CalendarioModosMensuales.DiaSemana;
                var ordinal = OrdinalEnMes(inicio);
                for (var k = 1; ; k++)
                {
                    var mes = new DateTime(inicio.Year, inicio.Month, 1).AddMonths(k * intervalo);
                    var fecha = porDiaSemana
                        ? DiaSemanaOrdinal(mes.Year, mes.Month, inicio.DayOfWeek, ordinal)
                        : new DateTime(mes.Year, mes.Month, Math.Min(inicio.Day, DateTime.DaysInMonth(mes.Year, mes.Month)));
                    if (!Agregar(fecha + hora)) break;
                }
                break;
            }

            case CalendarioFrecuencias.Anual:
                for (var k = 1; ; k++)
                    if (!Agregar(inicio.AddYears(k * intervalo))) break;
                break;
        }

        return resultado;
    }

    /// <summary>1 a 4 = primer..cuarto; -1 = último (la quinta aparición o la última del mes).</summary>
    public static int OrdinalEnMes(DateTime fecha)
    {
        var n = (fecha.Day - 1) / 7 + 1;
        return n >= 5 ? -1 : n;
    }

    private static DateTime DiaSemanaOrdinal(int anio, int mes, DayOfWeek dia, int ordinal)
    {
        if (ordinal == -1)
        {
            var ultimo = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
            while (ultimo.DayOfWeek != dia)
                ultimo = ultimo.AddDays(-1);
            return ultimo;
        }

        var primero = new DateTime(anio, mes, 1);
        while (primero.DayOfWeek != dia)
            primero = primero.AddDays(1);
        return primero.AddDays((ordinal - 1) * 7);
    }

    public static string NombreDia(DayOfWeek dia) => EsAr.DateTimeFormat.GetDayName(dia);

    public static string NombreOrdinal(int ordinal) => ordinal switch
    {
        1 => "primer",
        2 => "segundo",
        3 => "tercer",
        4 => "cuarto",
        _ => "último"
    };

    /// <summary>Descripción corta: "Cada 2 semanas el martes, hasta el 31/12/2026".</summary>
    /// <summary>
    /// Fin más tardío para que las repeticiones no se pisen (null = no se pisan). Ej.: un evento de
    /// una semana que se repite todos los días se superpone; con todo el día se cuenta por días.
    /// </summary>
    public static DateTime? FinSinSuperponer(DateTime inicio, DateTime fin, bool todoElDia, CalendarioRepeticionDto? regla)
    {
        var inicios = GenerarInicios(inicio, regla);
        if (inicios.Count < 2)
            return null;

        var hueco = Enumerable.Range(1, inicios.Count - 1).Min(i => inicios[i] - inicios[i - 1]);
        if (todoElDia)
        {
            var diasHueco = (int)Math.Round(hueco.TotalDays);
            var diasEvento = (fin.Date - inicio.Date).Days + 1;
            return diasEvento <= diasHueco ? null : inicio.Date.AddDays(diasHueco - 1) + fin.TimeOfDay;
        }

        return fin - inicio <= hueco ? null : inicio + hueco;
    }

    /// <summary>
    /// Secuencia de responsables que se repite en una serie (vacía si no hay rotación). Si la
    /// secuencia no se repite exacta (eventos cambiados a mano), las personas en orden de aparición.
    /// </summary>
    public static List<string> DetectarRotacion(IReadOnlyList<string> responsables)
    {
        var ids = responsables.Select(x => (x ?? string.Empty).Trim()).ToList();
        var distintos = ids.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distintos.Count < 2)
            return [];

        for (var periodo = distintos.Count; periodo <= ids.Count / 2; periodo++)
        {
            var repite = true;
            for (var i = periodo; i < ids.Count && repite; i++)
                repite = string.Equals(ids[i], ids[i % periodo], StringComparison.OrdinalIgnoreCase);
            if (repite)
                return ids.Take(periodo).Where(x => x.Length > 0).ToList();
        }

        return distintos;
    }

    /// <summary>La misma rotación, empezando por <paramref name="primero"/> (si está en la lista).</summary>
    public static List<string> EmpezarDesde(IReadOnlyList<string> rotacion, string? primero)
    {
        var indice = rotacion.ToList().FindIndex(x => string.Equals(x?.Trim(), primero?.Trim(), StringComparison.OrdinalIgnoreCase));
        return indice <= 0 ? [.. rotacion] : [.. rotacion.Skip(indice), .. rotacion.Take(indice)];
    }

    public static string Describir(DateTime inicio, CalendarioRepeticionDto? regla)
    {
        if (regla is null || !regla.SeRepite)
            return "No se repite";

        var n = Math.Clamp(regla.Intervalo, 1, 99);
        string texto;
        switch (regla.Frecuencia.ToUpperInvariant())
        {
            case CalendarioFrecuencias.Diaria:
                texto = n == 1 ? "Todos los días" : $"Cada {n} días";
                break;
            case CalendarioFrecuencias.Semanal:
            {
                var dias = (regla.DiasSemana.Count > 0 ? regla.DiasSemana : [inicio.DayOfWeek])
                    .OrderBy(d => Array.IndexOf(OrdenSemana, d)).ToList();
                var esHabiles = n == 1 && dias.Count == 5 && !dias.Contains(DayOfWeek.Saturday) && !dias.Contains(DayOfWeek.Sunday);
                texto = esHabiles
                    ? "Días hábiles (lunes a viernes)"
                    : $"{(n == 1 ? "Todas las semanas" : $"Cada {n} semanas")} el {UnirConY(dias.Select(NombreDia).ToList())}";
                break;
            }
            case CalendarioFrecuencias.Mensual:
                var cada = n == 1 ? "Todos los meses" : $"Cada {n} meses";
                texto = regla.ModoMensual == CalendarioModosMensuales.DiaSemana
                    ? $"{cada} el {NombreOrdinal(OrdinalEnMes(inicio))} {NombreDia(inicio.DayOfWeek)}"
                    : $"{cada} el día {inicio.Day}";
                break;
            case CalendarioFrecuencias.Anual:
                texto = $"{(n == 1 ? "Todos los años" : $"Cada {n} años")} el {inicio.ToString("d 'de' MMMM", EsAr)}";
                break;
            default:
                return "No se repite";
        }

        return regla.Fin switch
        {
            CalendarioFinesRepeticion.Fecha when regla.FinFecha is DateTime fin => $"{texto}, hasta el {fin:dd/MM/yyyy}",
            CalendarioFinesRepeticion.Veces => $"{texto}, {Math.Max(1, regla.FinVeces)} veces",
            _ => texto
        };
    }

    private static string UnirConY(IReadOnlyList<string> partes)
        => partes.Count <= 1 ? string.Join(string.Empty, partes) : $"{string.Join(", ", partes.Take(partes.Count - 1))} y {partes[^1]}";

    public static string Serializar(CalendarioRepeticionDto regla) => JsonSerializer.Serialize(regla, JsonOptions);

    public static CalendarioRepeticionDto? Deserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CalendarioRepeticionDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

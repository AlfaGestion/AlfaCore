namespace AlfaCore.Components.Shared;

/// <summary>
/// Rangos rápidos de fecha de los tableros de gestión (Ventas, Compras, Stock, etc.). El rango
/// activo se deduce de las fechas del filtro —no se guarda aparte— para que quede bien marcado al
/// volver a una pantalla que comparte el estado de filtros (GestionFilterStateService).
/// </summary>
public static class GestionRangosFecha
{
    public sealed record Rango(string Key, string Label);

    public static readonly IReadOnlyList<Rango> Todos =
    [
        new("hoy", "Hoy"),
        new("semana", "Esta semana"),
        new("mes", "Mes actual"),
        new("mes-anterior", "Mes anterior"),
        new("ultimos-30", "Últimos 30 días"),
        new("ultimos-3-meses", "Últimos 3 meses"),
        new("anio", "Año actual"),
        new("todo", "Todo")
    ];

    public static (DateTime? Desde, DateTime? Hasta) Calcular(string key, DateTime? hoy = null)
    {
        var today = (hoy ?? DateTime.Today).Date;
        var primeroDelMes = new DateTime(today.Year, today.Month, 1);
        return key switch
        {
            "hoy" => (today, today),
            "semana" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
            "mes" => (primeroDelMes, today),
            "mes-anterior" => (primeroDelMes.AddMonths(-1), primeroDelMes.AddDays(-1)),
            "ultimos-30" => (today.AddDays(-29), today),
            "ultimos-3-meses" => (today.AddMonths(-3), today),
            "anio" => (new DateTime(today.Year, 1, 1), today),
            "todo" => (null, null),
            _ => (primeroDelMes, today)
        };
    }

    /// <summary>Key del rango rápido que coincide con las fechas, o null si es un período personalizado.</summary>
    public static string? Detectar(DateTime? desde, DateTime? hasta, DateTime? hoy = null)
    {
        // "Mes actual" primero: es el período por defecto y el 1° de cada mes coincide con "Hoy".
        foreach (var rango in Todos.OrderBy(r => r.Key == "mes" ? 0 : 1))
        {
            var (d, h) = Calcular(rango.Key, hoy);
            if (d?.Date == desde?.Date && h?.Date == hasta?.Date)
                return rango.Key;
        }

        return null;
    }

    /// <summary>Texto corto del período para el chip del Smart Search.</summary>
    public static string Etiqueta(DateTime? desde, DateTime? hasta)
    {
        var key = Detectar(desde, hasta);
        if (key is not null)
            return Todos.First(r => r.Key == key).Label;

        return (desde, hasta) switch
        {
            (not null, not null) => $"{desde:dd/MM/yy} a {hasta:dd/MM/yy}",
            (not null, null) => $"Desde {desde:dd/MM/yy}",
            (null, not null) => $"Hasta {hasta:dd/MM/yy}",
            _ => "Todo"
        };
    }
}

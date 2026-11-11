namespace AlfaCore.Services;

/// <summary>Reglas puras del reporte de stock (sin acceso a datos), probadas en StockReportesTests.</summary>
public static class StockReportesCalculo
{
    public sealed record Ingreso(DateTime Fecha, string? Lote, decimal Cantidad);
    public sealed record Parte(DateTime? Ingreso, DateTime? Vencimiento, decimal Cantidad, bool Estimado);

    /// <summary>
    /// Perecederos, como CargaSaldosDetPerecedero de V_Cons_Ficha_Stock: el stock actual se asigna a los ingresos más
    /// recientes (se supone que sale primero lo más viejo). El vencimiento es el NroLote yyyymmdd del ingreso; si no tiene,
    /// la fecha de ingreso más los días del artículo (estimado). Lo que no alcanza a cubrir ningún ingreso queda sin vencimiento.
    /// </summary>
    public static List<Parte> Repartir(decimal stock, IEnumerable<Ingreso> ingresos, int diasArticulo)
    {
        var partes = new List<Parte>();
        if (stock <= 0) return partes;
        var ordenados = ingresos.Where(i => i.Cantidad > 0)
            .Select(i => (i.Fecha, Vto: StockMovimientosCalculo.Vencimiento(i.Lote), i.Cantidad))
            .Select(i => (i.Fecha, Vencimiento: i.Vto ?? StockMovimientosCalculo.VencimientoPropuesto(i.Fecha, diasArticulo), Estimado: i.Vto is null, i.Cantidad))
            .OrderByDescending(i => i.Fecha).ThenByDescending(i => i.Vencimiento).ToList();
        var resto = stock;
        foreach (var i in ordenados)
        {
            if (resto <= 0) break;
            var toma = Math.Min(resto, i.Cantidad);
            resto -= toma;
            var existente = partes.FindIndex(p => p.Vencimiento == i.Vencimiento && p.Estimado == i.Estimado);
            if (existente >= 0)
            {
                var p = partes[existente];
                partes[existente] = p with { Cantidad = p.Cantidad + toma, Ingreso = p.Ingreso < i.Fecha ? p.Ingreso : i.Fecha };
            }
            else partes.Add(new(i.Fecha, i.Vencimiento, toma, i.Estimado));
        }
        if (resto > 0) partes.Add(new(null, null, resto, false));
        return partes.OrderBy(p => p.Vencimiento ?? DateTime.MaxValue).ToList();
    }

    /// <summary>VENCIDO (antes de hoy), PORVENCER (hoy o dentro de N días), VIGENTE, o SINVTO.</summary>
    public static string Estado(DateTime? vencimiento, DateTime hoy, int diasPorVencer)
        => vencimiento is not { } v ? "SINVTO" : v.Date < hoy.Date ? "VENCIDO" : v.Date <= hoy.Date.AddDays(Math.Max(0, diasPorVencer)) ? "PORVENCER" : "VIGENTE";

    public static string EstadoTexto(string estado) => estado switch
    {
        "VENCIDO" => "Vencido",
        "PORVENCER" => "Por vencer",
        "VIGENTE" => "Vigente",
        _ => "Sin vencimiento"
    };

    /// <summary>Saldo corrido de la ficha: entrada si suma, salida si resta (en unidad base).</summary>
    public static decimal Acumular(decimal saldo, decimal cantidad) => decimal.Round(saldo + cantidad, 4);
}

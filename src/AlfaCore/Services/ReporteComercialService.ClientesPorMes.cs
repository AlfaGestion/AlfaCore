using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ReporteComercialService
{
    public const int MaximoMesesClientesPorMes = 36;

    /// <summary>
    /// Total vendido por cliente y mes del período (mismo libro y filtros que el Resumen de ventas).
    /// Los clientes con facturación automática activa aparecen aunque no hayan tenido ventas, y se
    /// marcan los meses que quedaron sin facturar según su periodicidad.
    /// </summary>
    public Task<ResumenClientesMesDto> GetClientesPorMesAsync(FiltrosReporteCompras f, CancellationToken ct = default)
        => EjecutarAsync(true, "ClientesPorMes", async cn =>
        {
            if (f.FechaDesde is null || f.FechaHasta is null)
                throw new ReglaReporteException("Elegí el período (desde y hasta) para ver las ventas por mes.");
            var meses = MesesDelPeriodo(f.FechaDesde.Value, f.FechaHasta.Value);
            if (meses.Count > MaximoMesesClientesPorMes)
                throw new ReglaReporteException($"El período puede tener hasta {MaximoMesesClientesPorMes} meses.");

            // Se leen hasta 11 meses antes del período: un abono trimestral o anual facturado antes
            // del primer mes visible no debe marcarse como faltante.
            var desdeExtendido = meses[0].AddMonths(-11);
            var p = Parametros(new FiltrosReporteCompras
            {
                FechaDesde = desdeExtendido, FechaHasta = f.FechaHasta.Value.Date, Proveedor = f.Proveedor, TipoComprobante = f.TipoComprobante,
                CuentaExacta = f.CuentaExacta, Provincia = f.Provincia, IncluirProforma = f.IncluirProforma, Vendedor = f.Vendedor,
                SinVendedor = f.SinVendedor, Zona = f.Zona, SinZona = f.SinZona, UnidadesNegocio = [.. f.UnidadesNegocio]
            });

            var ventas = (await cn.QueryAsync<(string Cuenta, string RazonSocial, int Anio, int Mes, decimal Total)>(new CommandDefinition(Libro(true) + $"""
                SELECT l.Cuenta, MAX(l.RazonSocial), YEAR(l.FECHA), MONTH(l.FECHA), ISNULL(SUM(l.IMPORTE),0)
                FROM Libro l {Filtro}
                GROUP BY l.Cuenta, YEAR(l.FECHA), MONTH(l.FECHA);
                """, p, commandTimeout: 90, cancellationToken: ct))).ToList();

            var hayFa = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT CASE WHEN OBJECT_ID('dbo.MA_CUENTAS_AUTOCPTES') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 1;
            var fa = new Dictionary<string, (string RazonSocial, string Periodicidad)>(StringComparer.OrdinalIgnoreCase);
            var primeraVenta = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (hayFa)
            {
                // Mismos filtros de cartera que el libro, aplicados al cliente y a la unidad del abono.
                foreach (var r in await cn.QueryAsync<(string Cuenta, string RazonSocial, string Periodicidad)>(new CommandDefinition($"""
                    SELECT LTRIM(RTRIM(cl.CTACODIGO)), LTRIM(RTRIM(ISNULL(cl.RAZON_SOCIAL,''))), UPPER(LTRIM(RTRIM(ISNULL(a.Periodicidad,''))))
                    FROM dbo.MA_CUENTAS_AUTOCPTES a
                    JOIN dbo.VT_CLIENTES cl ON cl.CODIGO = a.Cuenta
                    {FiltroCarteraComercial(true, "cl.CTACODIGO", "a.UNEGOCIO")}
                      AND a.Activo = 1 AND ISNULL(cl.Dada_De_Baja,0) = 0
                      AND (@Proveedor IS NULL OR cl.CTACODIGO LIKE @Busqueda ESCAPE '\' OR cl.RAZON_SOCIAL LIKE @Busqueda ESCAPE '\');
                    """, p, cancellationToken: ct)))
                {
                    var meses1 = MesesPeriodicidad(r.Periodicidad);
                    if (meses1 == 0) continue;
                    // Con varios abonos activos manda el de período más corto.
                    if (!fa.TryGetValue(r.Cuenta, out var actual) || meses1 < MesesPeriodicidad(actual.Periodicidad))
                        fa[r.Cuenta] = (r.RazonSocial, r.Periodicidad);
                }
                // Antes de su primera venta un cliente no tenía abono que controlar.
                foreach (var r in await cn.QueryAsync<(string Cuenta, DateTime Fecha)>(new CommandDefinition(Libro(true) + """
                    SELECT l.Cuenta, MIN(l.FECHA) FROM Libro l
                    WHERE ISNULL(l.IMPORTE,0) > 0 AND EXISTS (SELECT 1 FROM dbo.MA_CUENTAS_AUTOCPTES a JOIN dbo.VT_CLIENTES cl ON cl.CODIGO = a.Cuenta
                                                             WHERE a.Activo = 1 AND LTRIM(RTRIM(cl.CTACODIGO)) = l.Cuenta)
                    GROUP BY l.Cuenta;
                    """, p, commandTimeout: 90, cancellationToken: ct)))
                    primeraVenta[r.Cuenta] = r.Fecha;
            }

            return Armar(meses, ventas, fa, primeraVenta, DateTime.Today);
        }, ct, f);

    internal static List<DateTime> MesesDelPeriodo(DateTime desde, DateTime hasta)
    {
        var lista = new List<DateTime>();
        for (var m = new DateTime(desde.Year, desde.Month, 1); m <= hasta.Date && lista.Count <= MaximoMesesClientesPorMes; m = m.AddMonths(1))
            lista.Add(m);
        return lista;
    }

    internal static int MesesPeriodicidad(string periodicidad) => periodicidad.Trim().ToUpperInvariant() switch
    {
        "DIARIA" or "SEMANAL" or "QUINCENAL" or "MENSUAL" => 1,
        "BIMESTRAL" => 2,
        "TRIMESTRAL" => 3,
        "SEMESTRAL" => 6,
        "ANUAL" => 12,
        _ => 0
    };

    /// <summary>Arma la grilla y marca faltantes: un mes de un cliente con abono queda «sin facturar» si
    /// en ese mes y los anteriores de su período (bimestral = 2, trimestral = 3...) no hubo ventas.</summary>
    internal static ResumenClientesMesDto Armar(IReadOnlyList<DateTime> meses,
        IEnumerable<(string Cuenta, string RazonSocial, int Anio, int Mes, decimal Total)> ventas,
        IReadOnlyDictionary<string, (string RazonSocial, string Periodicidad)> fa,
        IReadOnlyDictionary<string, DateTime> primeraVenta, DateTime hoy)
    {
        var porCuenta = ventas.GroupBy(v => v.Cuenta, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Nombre: g.Select(x => x.RazonSocial).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? g.Key,
                Meses: g.GroupBy(x => new DateTime(x.Anio, x.Mes, 1)).ToDictionary(x => x.Key, x => x.Sum(y => y.Total))), StringComparer.OrdinalIgnoreCase);
        var mesActual = new DateTime(hoy.Year, hoy.Month, 1);
        var filas = new List<ResumenClienteMesFilaDto>();
        foreach (var cuenta in porCuenta.Keys.Union(fa.Keys, StringComparer.OrdinalIgnoreCase))
        {
            porCuenta.TryGetValue(cuenta, out var datos);
            var ventasMes = datos.Meses ?? new Dictionary<DateTime, decimal>();
            var tieneFa = fa.TryGetValue(cuenta, out var abono);
            var periodo = tieneFa ? MesesPeriodicidad(abono.Periodicidad) : 0;
            DateTime? inicio = primeraVenta.TryGetValue(cuenta, out var pv) ? new DateTime(pv.Year, pv.Month, 1) : null;
            var importes = meses.Select(m => ventasMes.GetValueOrDefault(m)).ToList();
            var estados = new List<EstadoMesCliente>();
            foreach (var m in meses)
            {
                if (ventasMes.GetValueOrDefault(m) > 0) { estados.Add(EstadoMesCliente.Facturado); continue; }
                if (!tieneFa || m > mesActual || (inicio is { } i && m < i)) { estados.Add(EstadoMesCliente.Vacio); continue; }
                var cubierto = Enumerable.Range(1, periodo - 1).Any(k => ventasMes.GetValueOrDefault(m.AddMonths(-k)) > 0);
                estados.Add(cubierto ? EstadoMesCliente.Vacio : m == mesActual ? EstadoMesCliente.Pendiente : EstadoMesCliente.SinFacturar);
            }
            filas.Add(new ResumenClienteMesFilaDto
            {
                Cuenta = cuenta,
                RazonSocial = datos.Nombre ?? (tieneFa ? abono.RazonSocial : cuenta),
                Periodicidad = tieneFa ? abono.Periodicidad : string.Empty,
                Importes = importes,
                Estados = estados,
                Total = importes.Sum()
            });
        }
        // Clientes sin ventas ni nada que controlar en el período no aportan.
        filas.RemoveAll(x => x.Total == 0 && x.Estados.All(e => e == EstadoMesCliente.Vacio));
        filas = filas.OrderByDescending(x => x.MesesSinFacturar).ThenBy(x => x.RazonSocial, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new ResumenClientesMesDto
        {
            Meses = meses,
            Filas = filas,
            TotalesPorMes = meses.Select((_, i) => filas.Sum(x => x.Importes[i])).ToList(),
            Total = filas.Sum(x => x.Total)
        };
    }
}

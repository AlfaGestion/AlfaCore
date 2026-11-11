using System.Globalization;
using AlfaCore.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IStockReportesService
{
    Task<StockReporteOpciones> GetOpcionesAsync(CancellationToken ct = default);
    Task<StockReportePagina<StockSaldoFila>> GetSaldosAsync(StockReporteFiltro filtro, CancellationToken ct = default);
    Task<StockReportePagina<StockPeriodoFila>> GetPeriodoAsync(StockReporteFiltro filtro, CancellationToken ct = default);
    Task<StockFicha> GetFichaAsync(StockReporteFiltro filtro, CancellationToken ct = default);
    Task<StockReportePagina<StockPerecederoFila>> GetPerecederosAsync(StockReporteFiltro filtro, CancellationToken ct = default);
    /// <summary>F5 de la ficha: artículos por código, descripción o código de barras, con el stock actual del depósito.</summary>
    Task<IReadOnlyList<StockMovimientoArticulo>> BuscarArticulosAsync(string texto, string deposito, CancellationToken ct = default);
    /// <summary>La solapa completa (sin paginar) en Excel.</summary>
    Task<byte[]> ExportarAsync(StockReporteTipo tipo, StockReporteFiltro filtro, CancellationToken ct = default);
}

/// <summary>
/// Reporte de stock web (/stock/reportes), port de V_Cons_Ficha_Stock.exe: saldos a una fecha (en unidades y bultos,
/// por depósito o con los depósitos en columnas), movimientos de un período, ficha de stock con saldo inicial y
/// acumulado, y perecederos por vencimiento. Fuente: V_MV_STOCK no anulado, SUM(CantidadUD) como Stk_Stock_UDKG.
/// Solo lectura.
/// </summary>
public sealed class StockReportesService(
    ISessionService sessionService, IAppUserSessionService appUserSession,
    IPermissionService permissions, IAppEventService events) : IStockReportesService
{
    public const string MenuKey = "D80-RPTSTOCK";
    private const string Modulo = "StockReportes";
    private const int MaxExportacion = 50000;
    private const int MaxFicha = 10000;

    public Task<StockReporteOpciones> GetOpcionesAsync(CancellationToken ct = default)
        => EjecutarAsync("Opciones", async (cn, token) =>
        {
            using var multi = await cn.QueryMultipleAsync(Cmd("""
                SELECT LTRIM(RTRIM(IdDeposito)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_DEPOSITO
                WHERE LTRIM(RTRIM(ISNULL(IdDeposito,'')))<>'' ORDER BY IdDeposito;
                SELECT LTRIM(RTRIM(IdRubro)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_RUBROS
                WHERE LTRIM(RTRIM(ISNULL(IdRubro,'')))<>'' ORDER BY Descripcion;
                SELECT LTRIM(RTRIM(IdFamilia)) Codigo, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_FAMILIAS
                WHERE LTRIM(RTRIM(ISNULL(IdFamilia,'')))<>'' ORDER BY Descripcion;
                """, null, token));
            var opciones = new StockReporteOpciones
            {
                Depositos = (await multi.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList(),
                Rubros = (await multi.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList(),
                Familias = (await multi.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList()
            };
            var cfg = await ConfigAsync(cn, token);
            opciones.UnidadBultos = cfg.UnidadBultos;
            opciones.UsaPerecederos = cfg.UsaPerecederos;
            return opciones;
        }, ct);

    public Task<IReadOnlyList<StockMovimientoArticulo>> BuscarArticulosAsync(string texto, string deposito, CancellationToken ct = default)
        => EjecutarAsync<IReadOnlyList<StockMovimientoArticulo>>("BuscarArticulos", async (cn, token) =>
        {
            var f = Normalizar(new StockReporteFiltro { Texto = texto, Deposito = deposito });
            var p = Parametros(f);
            var filas = (await cn.QueryAsync<StockMovimientoArticulo>(Cmd($"""
                SELECT TOP(100) LTRIM(RTRIM(a.IDARTICULO)) Codigo, LTRIM(RTRIM(ISNULL(a.DESCRIPCION,''))) Descripcion, LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad,
                    CONVERT(decimal(18,4),ISNULL((SELECT SUM(m.CantidadUD) FROM dbo.V_MV_STOCK m WHERE m.IDArticulo=a.IDARTICULO AND ISNULL(m.Anulado,0)=0
                        AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito)),0)) Stock
                FROM dbo.V_MA_ARTICULOS a
                WHERE ISNULL(a.SUSPENDIDO,0)=0 AND (@Texto='' OR LTRIM(RTRIM(a.IDARTICULO)) LIKE @Patron ESCAPE '\'
                    OR a.DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @Patron ESCAPE '\' OR a.CODIGOBARRA LIKE @Patron ESCAPE '\')
                ORDER BY CASE WHEN LTRIM(RTRIM(a.IDARTICULO))=@Texto OR LTRIM(RTRIM(ISNULL(a.CODIGOBARRA,'')))=@Texto THEN 0 ELSE 1 END, a.DESCRIPCION, a.IDARTICULO;
                """, p, token))).ToList();
            return filas;
        }, ct);

    // ---------------------------------------------------------------- Saldos

    public Task<StockReportePagina<StockSaldoFila>> GetSaldosAsync(StockReporteFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Saldos", (cn, token) => SaldosAsync(cn, Normalizar(filtro), paginar: true, token), ct);

    private async Task<StockReportePagina<StockSaldoFila>> SaldosAsync(SqlConnection cn, StockReporteFiltro f, bool paginar, CancellationToken ct)
    {
        var hasta = (f.Hasta ?? DateTime.Today).Date;
        var p = Parametros(f, hasta: hasta);
        var desde = paginar ? (f.Pagina - 1) * f.TamanoPagina : 0;
        var tamano = paginar ? f.TamanoPagina : MaxExportacion;
        var pagina = new StockReportePagina<StockSaldoFila> { Pagina = f.Pagina, TamanoPagina = f.TamanoPagina };
        // Saldo por artículo y depósito a la fecha (movimientos hasta ese día inclusive), como la vista Stk_Stock_UDKG.
        var cte = $"""
            WITH s AS (
                SELECT m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) Deposito, SUM(ISNULL(m.CantidadUD,0)) Saldo, SUM(ISNULL(m.CANT_BL,0)) BultosMov
                FROM dbo.V_MV_STOCK m
                WHERE ISNULL(m.Anulado,0)=0 AND m.FECHA < DATEADD(day,1,@Hasta) AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito)
                GROUP BY m.IDArticulo, LTRIM(RTRIM(m.IdDeposito))
            ), f AS (
                SELECT s.*, LTRIM(RTRIM(a.IDARTICULO)) Articulo, ISNULL(a.DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad,
                    ISNULL(r.Descripcion, LTRIM(RTRIM(ISNULL(a.IDRUBRO,'')))) Rubro
                FROM s JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=s.IDArticulo
                LEFT JOIN dbo.V_TA_RUBROS r ON LTRIM(RTRIM(r.IdRubro))=LTRIM(RTRIM(a.IDRUBRO))
                WHERE {FiltroArticulo("a")} {(f.SoloConStock ? "AND ROUND(s.Saldo,4)<>0" : "")}
            )
            """;
        if (!f.DepositosEnColumnas)
        {
            var sql = cte + """
                SELECT Articulo, Descripcion, Unidad, Rubro, Deposito, CONVERT(decimal(18,4),Saldo) Saldo, CONVERT(decimal(18,4),BultosMov) BultosMov
                FROM (SELECT f.*, ROW_NUMBER() OVER (ORDER BY f.Descripcion, f.Articulo, f.Deposito) rn FROM f) x
                WHERE rn > @Desde AND rn <= @Desde + @Tamano ORDER BY rn;
                """ + cte + "SELECT COUNT(*), CONVERT(decimal(18,4),ISNULL(SUM(Saldo),0)) FROM f;";
            using var multi = await cn.QueryMultipleAsync(Cmd(sql, Con(p, desde, tamano), ct));
            var filas = (await multi.ReadAsync<FilaSaldo>()).ToList();
            var (total, unidades) = await multi.ReadSingleAsync<(int, decimal)>();
            pagina.Total = total; pagina.TotalUnidades = unidades;
            var equivalencias = await EquivalenciasAsync(cn, filas.Select(x => x.Articulo), ct);
            pagina.Filas = filas.Select(x => new StockSaldoFila
            {
                Articulo = x.Articulo, Descripcion = x.Descripcion.Trim(), Unidad = x.Unidad, Rubro = x.Rubro, Deposito = x.Deposito, Saldo = x.Saldo,
                Bultos = equivalencias.Bultos(x.Articulo, x.Unidad, x.Saldo, x.BultosMov)
            }).ToList();
            pagina.TotalBultos = pagina.Filas.Sum(x => x.Bultos ?? 0);
        }
        else
        {
            // Un renglón por artículo y una columna por depósito (opción "Saldos por depósitos" del legacy, con pr_crosstab).
            var sql = cte + """
                , g AS (SELECT Articulo, MIN(Descripcion) Descripcion, MIN(Unidad) Unidad, MIN(Rubro) Rubro, SUM(Saldo) Saldo, SUM(BultosMov) BultosMov FROM f GROUP BY Articulo)
                SELECT Articulo, Descripcion, Unidad, Rubro, '' Deposito, CONVERT(decimal(18,4),Saldo) Saldo, CONVERT(decimal(18,4),BultosMov) BultosMov
                FROM (SELECT g.*, ROW_NUMBER() OVER (ORDER BY g.Descripcion, g.Articulo) rn FROM g) x
                WHERE rn > @Desde AND rn <= @Desde + @Tamano ORDER BY rn;
                """ + cte + "SELECT COUNT(DISTINCT Articulo), CONVERT(decimal(18,4),ISNULL(SUM(Saldo),0)) FROM f;";
            using var multi = await cn.QueryMultipleAsync(Cmd(sql, Con(p, desde, tamano), ct));
            var filas = (await multi.ReadAsync<FilaSaldo>()).ToList();
            var (total, unidades) = await multi.ReadSingleAsync<(int, decimal)>();
            pagina.Total = total; pagina.TotalUnidades = unidades;
            if (filas.Count > 0)
            {
                var porDeposito = await cn.QueryAsync<(string Articulo, string Deposito, decimal Saldo)>(Cmd("""
                    SELECT LTRIM(RTRIM(m.IDArticulo)), LTRIM(RTRIM(m.IdDeposito)), CONVERT(decimal(18,4),SUM(ISNULL(m.CantidadUD,0)))
                    FROM dbo.V_MV_STOCK m
                    WHERE ISNULL(m.Anulado,0)=0 AND m.FECHA < DATEADD(day,1,@Hasta) AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito)
                      AND LTRIM(RTRIM(m.IDArticulo)) IN @Ids
                    GROUP BY LTRIM(RTRIM(m.IDArticulo)), LTRIM(RTRIM(m.IdDeposito));
                    """, new { Hasta = hasta, Deposito = f.Deposito, Ids = filas.Select(x => x.Articulo).ToArray() }, ct));
                var mapa = porDeposito.GroupBy(x => x.Articulo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var equivalencias = await EquivalenciasAsync(cn, filas.Select(x => x.Articulo), ct);
                pagina.Filas = filas.Select(x =>
                {
                    var fila = new StockSaldoFila { Articulo = x.Articulo, Descripcion = x.Descripcion.Trim(), Unidad = x.Unidad, Rubro = x.Rubro, Saldo = x.Saldo,
                        Bultos = equivalencias.Bultos(x.Articulo, x.Unidad, x.Saldo, x.BultosMov) };
                    foreach (var d in mapa.GetValueOrDefault(x.Articulo) ?? []) if (d.Saldo != 0 || !f.SoloConStock) fila.PorDeposito[d.Deposito] = d.Saldo;
                    return fila;
                }).ToList();
                pagina.Depositos = pagina.Filas.SelectMany(x => x.PorDeposito.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                pagina.TotalBultos = pagina.Filas.Sum(x => x.Bultos ?? 0);
            }
        }
        if (hasta < DateTime.Today) pagina.Avisos.Add($"Saldos al {hasta:dd/MM/yyyy}: incluye los movimientos hasta ese día.");
        return pagina;
    }

    // ---------------------------------------------------------------- Período

    public Task<StockReportePagina<StockPeriodoFila>> GetPeriodoAsync(StockReporteFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Periodo", (cn, token) => PeriodoAsync(cn, Normalizar(filtro), paginar: true, token), ct);

    private async Task<StockReportePagina<StockPeriodoFila>> PeriodoAsync(SqlConnection cn, StockReporteFiltro f, bool paginar, CancellationToken ct)
    {
        var (desdeFecha, hasta) = Periodo(f);
        var p = Parametros(f, desdeFecha, hasta);
        var desde = paginar ? (f.Pagina - 1) * f.TamanoPagina : 0;
        var tamano = paginar ? f.TamanoPagina : MaxExportacion;
        var cte = $"""
            WITH s AS (
                SELECT m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) Deposito,
                    SUM(CASE WHEN m.FECHA < @DesdeFecha THEN ISNULL(m.CantidadUD,0) ELSE 0 END) Inicial,
                    SUM(CASE WHEN m.FECHA >= @DesdeFecha AND ISNULL(m.CantidadUD,0) > 0 THEN m.CantidadUD ELSE 0 END) Entradas,
                    SUM(CASE WHEN m.FECHA >= @DesdeFecha AND ISNULL(m.CantidadUD,0) < 0 THEN -m.CantidadUD ELSE 0 END) Salidas,
                    SUM(ISNULL(m.CantidadUD,0)) Final, SUM(ISNULL(m.CANT_BL,0)) BultosMov,
                    SUM(CASE WHEN m.FECHA >= @DesdeFecha THEN 1 ELSE 0 END) Movimientos
                FROM dbo.V_MV_STOCK m
                WHERE ISNULL(m.Anulado,0)=0 AND m.FECHA < DATEADD(day,1,@Hasta) AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito)
                GROUP BY m.IDArticulo, LTRIM(RTRIM(m.IdDeposito))
            ), f AS (
                SELECT s.*, LTRIM(RTRIM(a.IDARTICULO)) Articulo, ISNULL(a.DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad
                FROM s JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=s.IDArticulo
                WHERE {FiltroArticulo("a")} {(f.SoloConStock ? "AND s.Movimientos > 0" : "AND (s.Movimientos > 0 OR ROUND(s.Final,4)<>0)")}
            )
            """;
        var sql = cte + """
            SELECT Articulo, Descripcion, Unidad, Deposito, CONVERT(decimal(18,4),Inicial) SaldoInicial, CONVERT(decimal(18,4),Entradas) Entradas,
                CONVERT(decimal(18,4),Salidas) Salidas, CONVERT(decimal(18,4),Final) SaldoFinal, CONVERT(decimal(18,4),BultosMov) BultosMov
            FROM (SELECT f.*, ROW_NUMBER() OVER (ORDER BY f.Descripcion, f.Articulo, f.Deposito) rn FROM f) x
            WHERE rn > @Desde AND rn <= @Desde + @Tamano ORDER BY rn;
            """ + cte + "SELECT COUNT(*), CONVERT(decimal(18,4),ISNULL(SUM(Final),0)) FROM f;";
        using var multi = await cn.QueryMultipleAsync(Cmd(sql, Con(p, desde, tamano), ct));
        var filas = (await multi.ReadAsync<FilaPeriodo>()).ToList();
        var (total, unidades) = await multi.ReadSingleAsync<(int, decimal)>();
        var equivalencias = await EquivalenciasAsync(cn, filas.Select(x => x.Articulo), ct);
        var pagina = new StockReportePagina<StockPeriodoFila>
        {
            Pagina = f.Pagina, TamanoPagina = f.TamanoPagina, Total = total, TotalUnidades = unidades,
            Filas = filas.Select(x => new StockPeriodoFila
            {
                Articulo = x.Articulo, Descripcion = x.Descripcion.Trim(), Unidad = x.Unidad, Deposito = x.Deposito, SaldoInicial = x.SaldoInicial,
                Entradas = x.Entradas, Salidas = x.Salidas, SaldoFinal = x.SaldoFinal,
                BultosFinal = equivalencias.Bultos(x.Articulo, x.Unidad, x.SaldoFinal, x.BultosMov)
            }).ToList()
        };
        pagina.TotalBultos = pagina.Filas.Sum(x => x.BultosFinal ?? 0);
        return pagina;
    }

    // ---------------------------------------------------------------- Ficha

    public Task<StockFicha> GetFichaAsync(StockReporteFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Ficha", (cn, token) => FichaAsync(cn, Normalizar(filtro), token), ct);

    private async Task<StockFicha> FichaAsync(SqlConnection cn, StockReporteFiltro f, CancellationToken ct)
    {
        var codigo = f.Articulo.Trim();
        if (codigo.Length == 0) throw new CargaComprobantesReglaException("Elegí el artículo de la ficha.");
        var articulo = await cn.QueryFirstOrDefaultAsync<FilaArticulo>(Cmd("""
            SELECT TOP(1) IDARTICULO IdCrudo, LTRIM(RTRIM(IDARTICULO)) Codigo, ISNULL(DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(IDUNIDAD,''))) Unidad
            FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO))=@Codigo OR LTRIM(RTRIM(ISNULL(CODIGOBARRA,'')))=@Codigo
            ORDER BY CASE WHEN LTRIM(RTRIM(IDARTICULO))=@Codigo THEN 0 ELSE 1 END;
            """, new { Codigo = codigo }, ct)) ?? throw new CargaComprobantesReglaException($"No existe el artículo {codigo}.");
        var (desde, hasta) = Periodo(f);
        var p = new { articulo.IdCrudo, Deposito = f.Deposito, Desde = desde, Hasta = hasta, Max = MaxFicha + 1 };
        // Como CargaMov de V_Cons_Ficha_Stock: saldo inicial (antes del período) y movimientos con saldo acumulado en unidad base.
        var inicial = await cn.ExecuteScalarAsync<decimal>(Cmd("""
            SELECT CONVERT(decimal(18,4),ISNULL(SUM(CantidadUD),0)) FROM dbo.V_MV_STOCK
            WHERE IDArticulo=@IdCrudo AND ISNULL(Anulado,0)=0 AND FECHA < @Desde AND (@Deposito='' OR LTRIM(RTRIM(IdDeposito))=@Deposito);
            """, p, ct));
        var movimientos = (await cn.QueryAsync<FilaFicha>(Cmd("""
            SELECT TOP(@Max) s.FECHA Fecha, LTRIM(RTRIM(s.TC)) Tc, s.IDCOMPROBANTE Comprobante, LTRIM(RTRIM(ISNULL(s.IdDeposito,''))) Deposito,
                LTRIM(RTRIM(ISNULL(s.IDDEPOSITO_DESTINO,''))) OrigenDestino,
                LTRIM(RTRIM(ISNULL(ms.Descripcion, ISNULL(s.IdMotivoStock,'')))) Motivo,
                COALESCE(NULLIF(LTRIM(RTRIM(c.CUENTA)),''), NULLIF(NULLIF(LTRIM(RTRIM(s.CuentaProveedor)),'*'),''), '') Cuenta,
                COALESCE(NULLIF(LTRIM(RTRIM(c.NOMBRE)),''), LTRIM(RTRIM(mc.DESCRIPCION)), '') Nombre,
                CONVERT(decimal(18,4),ISNULL(s.CantidadUD,0)) Cantidad, CONVERT(decimal(18,4),ISNULL(s.CANT_BL,0)) Bultos,
                LTRIM(RTRIM(ISNULL(s.NroLote,''))) Lote, LTRIM(RTRIM(ISNULL(s.ControladoPor,''))) Observaciones
            FROM dbo.V_MV_STOCK s
            LEFT JOIN dbo.V_TA_MotivoStock ms ON ms.IdMotivoStock=s.IdMotivoStock
            LEFT JOIN dbo.V_MV_CPTE c ON c.TC=s.TC AND c.IDCOMPROBANTE=s.IDCOMPROBANTE AND c.IDCOMPLEMENTO=s.IDCOMPLEMENTO
            LEFT JOIN dbo.MA_CUENTAS mc ON LTRIM(RTRIM(mc.CODIGO))=COALESCE(NULLIF(LTRIM(RTRIM(c.CUENTA)),''), NULLIF(LTRIM(RTRIM(s.CuentaProveedor)),'*'))
            WHERE s.IDArticulo=@IdCrudo AND ISNULL(s.Anulado,0)=0 AND s.FECHA >= @Desde AND s.FECHA < DATEADD(day,1,@Hasta)
              AND (@Deposito='' OR LTRIM(RTRIM(s.IdDeposito))=@Deposito)
            ORDER BY s.FECHA, s.ID;
            """, p, ct))).ToList();
        var ficha = new StockFicha
        {
            Articulo = articulo.Codigo, Descripcion = articulo.Descripcion.Trim(), Unidad = articulo.Unidad, Desde = desde, Hasta = hasta,
            SaldoInicial = inicial, Truncada = movimientos.Count > MaxFicha
        };
        var saldo = inicial;
        foreach (var m in movimientos.Take(MaxFicha))
        {
            saldo = StockReportesCalculo.Acumular(saldo, m.Cantidad);
            ficha.Movimientos.Add(new()
            {
                Fecha = m.Fecha, Tc = m.Tc, Comprobante = m.Comprobante.Trim(), Deposito = m.Deposito, OrigenDestino = m.OrigenDestino, Motivo = m.Motivo,
                Cuenta = m.Cuenta, Nombre = m.Nombre, Entrada = m.Cantidad > 0 ? m.Cantidad : 0, Salida = m.Cantidad < 0 ? -m.Cantidad : 0, Saldo = saldo,
                Bultos = m.Bultos, Lote = m.Lote, Vencimiento = StockMovimientosCalculo.Vencimiento(m.Lote), Observaciones = m.Observaciones
            });
        }
        ficha.Entradas = ficha.Movimientos.Sum(x => x.Entrada);
        ficha.Salidas = ficha.Movimientos.Sum(x => x.Salida);
        ficha.SaldoFinal = ficha.Truncada ? inicial + movimientos.Sum(x => x.Cantidad) : saldo;
        var equivalencias = await EquivalenciasAsync(cn, [articulo.Codigo], ct);
        ficha.SaldosDeposito = (await cn.QueryAsync<(string Deposito, decimal Saldo, decimal Bultos)>(Cmd("""
            SELECT LTRIM(RTRIM(IdDeposito)), CONVERT(decimal(18,4),SUM(ISNULL(CantidadUD,0))), CONVERT(decimal(18,4),SUM(ISNULL(CANT_BL,0)))
            FROM dbo.V_MV_STOCK WHERE IDArticulo=@IdCrudo AND ISNULL(Anulado,0)=0 AND FECHA < DATEADD(day,1,@Hasta)
            GROUP BY LTRIM(RTRIM(IdDeposito)) HAVING ROUND(SUM(ISNULL(CantidadUD,0)),4)<>0 ORDER BY 1;
            """, p, ct))).Select(x => new StockSaldoFila { Articulo = articulo.Codigo, Descripcion = ficha.Descripcion, Unidad = articulo.Unidad,
                Deposito = x.Deposito, Saldo = x.Saldo, Bultos = equivalencias.Bultos(articulo.Codigo, articulo.Unidad, x.Saldo, x.Bultos) }).ToList();
        return ficha;
    }

    // ---------------------------------------------------------------- Perecederos

    public Task<StockReportePagina<StockPerecederoFila>> GetPerecederosAsync(StockReporteFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Perecederos", async (cn, token) =>
        {
            var f = Normalizar(filtro);
            var todas = await PerecederosAsync(cn, f, token);
            return new StockReportePagina<StockPerecederoFila>
            {
                Pagina = f.Pagina, TamanoPagina = f.TamanoPagina, Total = todas.Count, TotalUnidades = todas.Sum(x => x.Cantidad),
                TotalBultos = todas.Sum(x => x.Bultos ?? 0),
                Filas = todas.Skip((f.Pagina - 1) * f.TamanoPagina).Take(f.TamanoPagina).ToList(),
                Avisos = todas.Any(x => x.Estimado) ? ["Los vencimientos marcados como estimados salen de la fecha de ingreso más los días del artículo."] : []
            };
        }, ct);

    private async Task<List<StockPerecederoFila>> PerecederosAsync(SqlConnection cn, StockReporteFiltro f, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var p = Parametros(f, hasta: hoy);
        // Artículos perecederos con stock hoy por depósito, y sus ingresos (CantidadUD > 0) para asignarles el stock.
        var sql = $"""
            WITH s AS (
                SELECT m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) Deposito, SUM(ISNULL(m.CantidadUD,0)) Saldo
                FROM dbo.V_MV_STOCK m JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=m.IDArticulo
                WHERE ISNULL(m.Anulado,0)=0 AND ISNULL(a.Perecedero,0)=1 AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito) AND {FiltroArticulo("a")}
                GROUP BY m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) HAVING SUM(ISNULL(m.CantidadUD,0)) > 0
            )
            SELECT LTRIM(RTRIM(a.IDARTICULO)) Articulo, ISNULL(a.DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(a.IDUNIDAD,''))) Unidad,
                ISNULL(r.Descripcion, LTRIM(RTRIM(ISNULL(a.IDRUBRO,'')))) Rubro, s.Deposito, CONVERT(decimal(18,4),s.Saldo) Saldo,
                CASE WHEN ISNULL(a.PideMedidas,0)=1 THEN 0 ELSE CONVERT(int,ROUND(ISNULL(a.Largo,0),0)) END Dias
            FROM s JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=s.IDArticulo
            LEFT JOIN dbo.V_TA_RUBROS r ON LTRIM(RTRIM(r.IdRubro))=LTRIM(RTRIM(a.IDRUBRO))
            ORDER BY a.DESCRIPCION, a.IDARTICULO, s.Deposito;

            WITH s AS (
                SELECT m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) Deposito
                FROM dbo.V_MV_STOCK m JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO=m.IDArticulo
                WHERE ISNULL(m.Anulado,0)=0 AND ISNULL(a.Perecedero,0)=1 AND (@Deposito='' OR LTRIM(RTRIM(m.IdDeposito))=@Deposito) AND {FiltroArticulo("a")}
                GROUP BY m.IDArticulo, LTRIM(RTRIM(m.IdDeposito)) HAVING SUM(ISNULL(m.CantidadUD,0)) > 0
            )
            SELECT LTRIM(RTRIM(m.IDArticulo)) Articulo, LTRIM(RTRIM(m.IdDeposito)) Deposito, CONVERT(date,m.FECHA) Fecha,
                LTRIM(RTRIM(ISNULL(m.NroLote,''))) Lote, CONVERT(decimal(18,4),SUM(m.CantidadUD)) Cantidad
            FROM dbo.V_MV_STOCK m JOIN s ON s.IDArticulo=m.IDArticulo AND s.Deposito=LTRIM(RTRIM(m.IdDeposito))
            WHERE ISNULL(m.Anulado,0)=0 AND ISNULL(m.CantidadUD,0) > 0
            GROUP BY LTRIM(RTRIM(m.IDArticulo)), LTRIM(RTRIM(m.IdDeposito)), CONVERT(date,m.FECHA), LTRIM(RTRIM(ISNULL(m.NroLote,'')));
            """;
        using var multi = await cn.QueryMultipleAsync(Cmd(sql, p, ct));
        var saldos = (await multi.ReadAsync<FilaPerecedero>()).ToList();
        var ingresos = (await multi.ReadAsync<FilaIngreso>()).GroupBy(x => (x.Articulo.ToUpperInvariant(), x.Deposito.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(x => new StockReportesCalculo.Ingreso(x.Fecha, x.Lote, x.Cantidad)).ToList());
        var equivalencias = await EquivalenciasAsync(cn, saldos.Select(x => x.Articulo), ct);
        var filas = new List<StockPerecederoFila>();
        foreach (var s in saldos)
            foreach (var parte in StockReportesCalculo.Repartir(s.Saldo, ingresos.GetValueOrDefault((s.Articulo.ToUpperInvariant(), s.Deposito.ToUpperInvariant())) ?? [], s.Dias))
            {
                var estado = StockReportesCalculo.Estado(parte.Vencimiento, hoy, f.DiasPorVencer);
                if (f.Estado.Length > 0 && estado != f.Estado) continue;
                filas.Add(new()
                {
                    Articulo = s.Articulo, Descripcion = s.Descripcion.Trim(), Unidad = s.Unidad, Rubro = s.Rubro, Deposito = s.Deposito,
                    Ingreso = parte.Ingreso, Vencimiento = parte.Vencimiento, Cantidad = parte.Cantidad,
                    Bultos = equivalencias.Bultos(s.Articulo, s.Unidad, parte.Cantidad, null),
                    DiasRestantes = parte.Vencimiento is { } v ? (int)(v.Date - hoy).TotalDays : null, Estado = estado, Estimado = parte.Estimado
                });
            }
        return filas.OrderBy(x => x.Vencimiento ?? DateTime.MaxValue).ThenBy(x => x.Descripcion).ThenBy(x => x.Deposito).ToList();
    }

    // ---------------------------------------------------------------- Excel

    public Task<byte[]> ExportarAsync(StockReporteTipo tipo, StockReporteFiltro filtro, CancellationToken ct = default)
        => EjecutarAsync("Exportar", async (cn, token) =>
        {
            var f = Normalizar(filtro);
            var cfg = await ConfigAsync(cn, token);
            var bultos = cfg.UnidadBultos.Length > 0;
            using var libro = new XLWorkbook();
            switch (tipo)
            {
                case StockReporteTipo.Saldos:
                {
                    var datos = await SaldosAsync(cn, f, paginar: false, token);
                    var titulo = $"Saldos al {(f.Hasta ?? DateTime.Today):dd/MM/yyyy}";
                    if (f.DepositosEnColumnas)
                        Hoja(libro, "Saldos", titulo, ["Código", "Descripción", "Unidad", "Rubro", .. datos.Depositos.Select(d => $"Dep. {d}"), "Total", .. (bultos ? new[] { "Bultos" } : [])],
                            datos.Filas.Select(x => (object?[])[x.Articulo, x.Descripcion, x.Unidad, x.Rubro, .. datos.Depositos.Select(d => (object?)x.PorDeposito.GetValueOrDefault(d)), x.Saldo, .. (bultos ? new object?[] { x.Bultos } : [])]));
                    else
                        Hoja(libro, "Saldos", titulo, ["Código", "Descripción", "Unidad", "Rubro", "Depósito", "Saldo", .. (bultos ? new[] { "Bultos" } : [])],
                            datos.Filas.Select(x => (object?[])[x.Articulo, x.Descripcion, x.Unidad, x.Rubro, x.Deposito, x.Saldo, .. (bultos ? new object?[] { x.Bultos } : [])]));
                    break;
                }
                case StockReporteTipo.Periodo:
                {
                    var (desde, hasta) = Periodo(f);
                    var datos = await PeriodoAsync(cn, f, paginar: false, token);
                    Hoja(libro, "Período", $"Movimientos del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}",
                        ["Código", "Descripción", "Unidad", "Depósito", "Saldo inicial", "Entradas", "Salidas", "Saldo final", .. (bultos ? new[] { "Bultos" } : [])],
                        datos.Filas.Select(x => (object?[])[x.Articulo, x.Descripcion, x.Unidad, x.Deposito, x.SaldoInicial, x.Entradas, x.Salidas, x.SaldoFinal, .. (bultos ? new object?[] { x.BultosFinal } : [])]));
                    break;
                }
                case StockReporteTipo.Ficha:
                {
                    var ficha = await FichaAsync(cn, f, token);
                    var filas = new List<object?[]> { new object?[] { ficha.Desde, "", "Saldo inicial", "", "", "", "", "", null, null, ficha.SaldoInicial, null, "", null, "" } };
                    filas.AddRange(ficha.Movimientos.Select(m => (object?[])[m.Fecha, m.Tc, m.Comprobante, m.Deposito, m.OrigenDestino, m.Motivo, m.Cuenta, m.Nombre,
                        m.Entrada == 0 ? null : m.Entrada, m.Salida == 0 ? null : m.Salida, m.Saldo, m.Bultos == 0 ? null : m.Bultos, m.Lote, m.Vencimiento, m.Observaciones]));
                    Hoja(libro, "Ficha", $"Ficha de stock {ficha.Articulo} {ficha.Descripcion} ({ficha.Unidad}) del {ficha.Desde:dd/MM/yyyy} al {ficha.Hasta:dd/MM/yyyy}",
                        ["Fecha", "TC", "Comprobante", "Depósito", "Org./Dest.", "Motivo", "Cuenta", "Nombre", "Entrada", "Salida", "Saldo", "Bultos", "Lote", "Vence", "Observaciones"], filas);
                    break;
                }
                default:
                {
                    var datos = await PerecederosAsync(cn, f, token);
                    Hoja(libro, "Perecederos", $"Productos perecederos al {DateTime.Today:dd/MM/yyyy} (por vencer: {f.DiasPorVencer} días)",
                        ["Código", "Descripción", "Unidad", "Rubro", "Depósito", "Ingreso", "Vence", "Días", "Estado", "Cantidad", .. (bultos ? new[] { "Bultos" } : []), "Vencimiento"],
                        datos.Select(x => (object?[])[x.Articulo, x.Descripcion, x.Unidad, x.Rubro, x.Deposito, x.Ingreso, x.Vencimiento, x.DiasRestantes,
                            StockReportesCalculo.EstadoTexto(x.Estado), x.Cantidad, .. (bultos ? new object?[] { x.Bultos } : []), x.Estimado ? "Estimado" : x.Vencimiento is null ? "" : "Registrado"]));
                    break;
                }
            }
            using var ms = new MemoryStream();
            libro.SaveAs(ms);
            return ms.ToArray();
        }, ct);

    private static void Hoja(XLWorkbook libro, string nombre, string titulo, IReadOnlyList<string> columnas, IEnumerable<object?[]> filas)
    {
        var hoja = libro.Worksheets.Add(nombre);
        hoja.Cell(1, 1).Value = titulo;
        hoja.Cell(1, 1).Style.Font.Bold = true;
        for (var c = 0; c < columnas.Count; c++)
        {
            var celda = hoja.Cell(3, c + 1);
            celda.Value = columnas[c];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3A5F");
            celda.Style.Font.FontColor = XLColor.White;
        }
        var r = 4;
        foreach (var fila in filas)
        {
            for (var c = 0; c < fila.Length; c++)
            {
                var celda = hoja.Cell(r, c + 1);
                switch (fila[c])
                {
                    case null: break;
                    case decimal d: celda.Value = d; celda.Style.NumberFormat.Format = "#,##0.####"; break;
                    case int i: celda.Value = i; break;
                    case DateTime dt: celda.Value = dt; celda.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                    default: celda.Value = fila[c]!.ToString(); break;
                }
            }
            r++;
        }
        hoja.SheetView.FreezeRows(3);
        hoja.Columns().AdjustToContents(3, Math.Min(r, 500), 8, 60);
    }

    // ---------------------------------------------------------------- Comunes

    private static StockReporteFiltro Normalizar(StockReporteFiltro? f)
    {
        f ??= new();
        static string T(string? s, int max) { var t = (s ?? "").Trim(); return t.Length > max ? t[..max] : t; }
        return new StockReporteFiltro
        {
            Deposito = T(f.Deposito, 4), Rubro = T(f.Rubro, 4), Familia = T(f.Familia, 10), Texto = T(f.Texto, 100), Articulo = T(f.Articulo, 50),
            Desde = f.Desde?.Date, Hasta = f.Hasta?.Date, SoloConStock = f.SoloConStock, DepositosEnColumnas = f.DepositosEnColumnas,
            Estado = f.Estado is "VENCIDO" or "PORVENCER" or "VIGENTE" or "SINVTO" ? f.Estado : "",
            DiasPorVencer = Math.Clamp(f.DiasPorVencer, 0, 3650),
            Pagina = Math.Max(1, f.Pagina), TamanoPagina = f.TamanoPagina is 25 or 50 or 100 or 200 ? f.TamanoPagina : 50
        };
    }

    /// <summary>Período de la ficha y de movimientos: por defecto, los últimos 3 meses (como V_Cons_Ficha_Stock).</summary>
    private static (DateTime Desde, DateTime Hasta) Periodo(StockReporteFiltro f)
    {
        var hasta = (f.Hasta ?? DateTime.Today).Date;
        var desde = (f.Desde ?? hasta.AddMonths(-3)).Date;
        if (desde > hasta) throw new CargaComprobantesReglaException("La fecha desde no puede ser posterior a la fecha hasta.");
        return (desde, hasta);
    }

    // Filtros de artículo: no suspendidos, que controlan stock, rubro, familia y texto (código, descripción o código de barras).
    private static string FiltroArticulo(string a) => $"""
        ISNULL({a}.SUSPENDIDO,0)=0 AND ISNULL({a}.NO_CONTROLA_STOCK,0)=0
        AND (@Rubro='' OR LTRIM(RTRIM({a}.IDRUBRO))=@Rubro) AND (@Familia='' OR LTRIM(RTRIM({a}.IdFamilia))=@Familia)
        AND (@Texto='' OR LTRIM(RTRIM({a}.IDARTICULO)) LIKE @Patron ESCAPE '\' OR {a}.DESCRIPCION COLLATE Latin1_General_CI_AI LIKE @Patron ESCAPE '\'
             OR {a}.CODIGOBARRA LIKE @Patron ESCAPE '\')
        """;

    private static Dictionary<string, object?> Parametros(StockReporteFiltro f, DateTime? desde = null, DateTime? hasta = null)
    {
        var literal = f.Texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        return new()
        {
            ["Deposito"] = f.Deposito, ["Rubro"] = f.Rubro, ["Familia"] = f.Familia, ["Texto"] = f.Texto, ["Patron"] = "%" + literal + "%",
            ["DesdeFecha"] = desde, ["Hasta"] = hasta
        };
    }
    private static DynamicParameters Con(Dictionary<string, object?> p, int desde, int tamano)
    {
        var d = new DynamicParameters(p);
        d.Add("Desde", desde); d.Add("Tamano", tamano);
        return d;
    }

    private sealed record Config(string UnidadBultos, bool UsaPerecederos);
    private static async Task<Config> ConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var cfg = (await cn.QueryAsync<(string Clave, string Valor)>(Cmd("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR,''))) FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('UNIDADDEMEDIDA_BL','USAPERECEDEROS');
            """, null, ct))).GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Valor, StringComparer.OrdinalIgnoreCase);
        return new(cfg.GetValueOrDefault("UNIDADDEMEDIDA_BL", ""), string.Equals(cfg.GetValueOrDefault("USAPERECEDEROS", ""), "SI", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Equivalencias a bultos (S_TA_EQUIV con UNIDADDEMEDIDA_BL), como CargaComprobantesUnidades.</summary>
    private static async Task<Equivalencias> EquivalenciasAsync(SqlConnection cn, IEnumerable<string> articulos, CancellationToken ct)
    {
        var ids = articulos.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var unidad = (await ConfigAsync(cn, ct)).UnidadBultos;
        if (unidad.Length == 0 || ids.Length == 0) return new(unidad, []);
        var lista = (await cn.QueryAsync<CargaComprobantesUnidades.Equivalencia>(Cmd("""
            SELECT LTRIM(RTRIM(IDARTICULO)) Articulo, LTRIM(RTRIM(IDUNIDAD)) Unidad, LTRIM(RTRIM(IDUNIDAD_EQUIV)) Equivalente, CONVERT(decimal(28,8),COEFICIENTE) Coeficiente
            FROM dbo.S_TA_EQUIV WHERE LTRIM(RTRIM(IDARTICULO)) IN @Ids;
            """, new { Ids = ids }, ct))).ToList();
        return new(unidad, lista);
    }

    private sealed class Equivalencias(string unidadBultos, List<CargaComprobantesUnidades.Equivalencia> lista)
    {
        /// <summary>Saldo en bultos con la equivalencia del artículo; sin equivalencia, los bultos de los movimientos (CANT_BL); null sin unidad de bultos.</summary>
        public decimal? Bultos(string articulo, string unidad, decimal cantidad, decimal? bultosMovimientos)
        {
            if (unidadBultos.Length == 0) return null;
            if (string.Equals(unidad, unidadBultos, StringComparison.OrdinalIgnoreCase)) return cantidad;
            try
            {
                var convertido = CargaComprobantesUnidades.Convertir(articulo, unidad, unidadBultos, Math.Abs(cantidad), lista);
                if (convertido is { } c) return Math.Sign(cantidad) * c;
            }
            catch (CargaComprobantesReglaException) { /* equivalencia duplicada o inválida: se usan los bultos de los movimientos */ }
            return bultosMovimientos is { } b && b != 0 ? decimal.Round(b, 4) : null;
        }
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, CancellationToken, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessionService, appUserSession, out _))
            throw new CargaComprobantesReglaException("Ingresá con un usuario autorizado en la base activa.");
        var user = appUserSession.GetCurrentUserName().Trim();
        var system = appUserSession.CurrentUser?.SystemCode ?? string.Empty;
        try
        {
            await using var cn = new SqlConnection(sessionService.GetConnectionString());
            await cn.OpenAsync(ct);
            if (!await permissions.HasAccessAsync(MenuKey, ct))
            {
                var restringido = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TA_TAREAS
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario) AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(TAREA,'')<>'')
                        OR EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB
                        WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario) AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(Clave,'')<>'')
                        THEN 1 ELSE 0 END;
                    """, new { Usuario = user, Sistema = system }, commandTimeout: 5, cancellationToken: ct));
                if (restringido != 0) throw new CargaComprobantesReglaException("Tu usuario no tiene acceso a los reportes de stock.");
            }
            return await operacion(cn, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CargaComprobantesReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo generar el reporte de stock. Probá con menos datos (un depósito o un rubro) o volvé a intentar.";
            var code = await events.LogErrorAsync(Modulo, accion, ex, mensaje, ct: ct);
            throw new AppUserFacingException(mensaje, code, ex);
        }
    }

    private static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) => new(sql, p, commandTimeout: 120, cancellationToken: ct);

    private sealed class FilaSaldo { public string Articulo { get; set; } = ""; public string Descripcion { get; set; } = ""; public string Unidad { get; set; } = ""; public string Rubro { get; set; } = ""; public string Deposito { get; set; } = ""; public decimal Saldo { get; set; } public decimal BultosMov { get; set; } }
    private sealed class FilaPeriodo { public string Articulo { get; set; } = ""; public string Descripcion { get; set; } = ""; public string Unidad { get; set; } = ""; public string Deposito { get; set; } = ""; public decimal SaldoInicial { get; set; } public decimal Entradas { get; set; } public decimal Salidas { get; set; } public decimal SaldoFinal { get; set; } public decimal BultosMov { get; set; } }
    private sealed class FilaArticulo { public string IdCrudo { get; set; } = ""; public string Codigo { get; set; } = ""; public string Descripcion { get; set; } = ""; public string Unidad { get; set; } = ""; }
    private sealed class FilaFicha { public DateTime Fecha { get; set; } public string Tc { get; set; } = ""; public string Comprobante { get; set; } = ""; public string Deposito { get; set; } = ""; public string OrigenDestino { get; set; } = ""; public string Motivo { get; set; } = ""; public string Cuenta { get; set; } = ""; public string Nombre { get; set; } = ""; public decimal Cantidad { get; set; } public decimal Bultos { get; set; } public string Lote { get; set; } = ""; public string Observaciones { get; set; } = ""; }
    private sealed class FilaPerecedero { public string Articulo { get; set; } = ""; public string Descripcion { get; set; } = ""; public string Unidad { get; set; } = ""; public string Rubro { get; set; } = ""; public string Deposito { get; set; } = ""; public decimal Saldo { get; set; } public int Dias { get; set; } }
    private sealed class FilaIngreso { public string Articulo { get; set; } = ""; public string Deposito { get; set; } = ""; public DateTime Fecha { get; set; } public string Lote { get; set; } = ""; public decimal Cantidad { get; set; } }
}

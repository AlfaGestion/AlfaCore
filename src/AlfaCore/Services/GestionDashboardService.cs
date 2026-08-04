using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

public sealed class GestionDashboardService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IGestionDashboardService
{
    private const string VentasDocumentosWhere = """
        AND (
            c.TC LIKE 'FC%'
            OR c.TC LIKE 'NC%'
            OR c.TC LIKE 'ND%'
            OR c.TC LIKE 'FP%'
        )
        """;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<VentasFilterOptionsDto> GetVentasFilterOptionsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Ventas", "GetFilterOptions", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return new VentasFilterOptionsDto
            {
                Usuarios = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) LTRIM(RTRIM(c.Usuario))
                    FROM dbo.V_MV_Cpte c
                    WHERE (
                          c.TC LIKE 'FC%'
                          OR c.TC LIKE 'NC%'
                          OR c.TC LIKE 'ND%'
                          OR c.TC LIKE 'FP%'
                      )
                      AND ISNULL(LTRIM(RTRIM(c.Usuario)), '') <> ''
                    ORDER BY LTRIM(RTRIM(c.Usuario))
                    """, cn, token),
                UnidadesNegocio = await QueryUnidadesNegocioAsync("""
                    SELECT DISTINCT TOP (100)
                        LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO))) AS Codigo,
                        ISNULL(NULLIF(LTRIM(RTRIM(u.Descripcion)), ''), LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))) AS Descripcion
                    FROM dbo.V_MV_Cpte c
                    LEFT JOIN dbo.V_TA_UnidadNegocio u ON LTRIM(RTRIM(u.Codigo)) = LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO)))
                    WHERE (
                          c.TC LIKE 'FC%'
                          OR c.TC LIKE 'NC%'
                          OR c.TC LIKE 'ND%'
                          OR c.TC LIKE 'FP%'
                      )
                      AND c.UNEGOCIO IS NOT NULL
                    ORDER BY Descripcion
                    """, cn, token),
                Depositos = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), c.IdDeposito)
                    FROM dbo.V_MV_Cpte c
                    WHERE (
                          c.TC LIKE 'FC%'
                          OR c.TC LIKE 'NC%'
                          OR c.TC LIKE 'ND%'
                          OR c.TC LIKE 'FP%'
                      )
                      AND c.IdDeposito IS NOT NULL
                    ORDER BY CONVERT(varchar(50), c.IdDeposito)
                    """, cn, token),
                TiposComprobante = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) c.TC
                    FROM dbo.V_MV_Cpte c
                    WHERE (
                          c.TC LIKE 'FC%'
                          OR c.TC LIKE 'NC%'
                          OR c.TC LIKE 'ND%'
                          OR c.TC LIKE 'FP%'
                      )
                      AND ISNULL(LTRIM(RTRIM(c.TC)), '') <> ''
                    ORDER BY c.TC
                    """, cn, token)
            };
        }, "No se pudieron cargar los filtros de ventas.", ct);

    public Task<StockFilterOptionsDto> GetStockFilterOptionsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Stock", "GetFilterOptions", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return new StockFilterOptionsDto
            {
                Rubros = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), a.IDRUBRO)
                    FROM dbo.V_MA_ARTICULOS a
                    WHERE a.IDRUBRO IS NOT NULL
                    ORDER BY CONVERT(varchar(50), a.IDRUBRO)
                    """, cn, token),
                Familias = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), a.IdFamilia)
                    FROM dbo.V_MA_ARTICULOS a
                    WHERE a.IdFamilia IS NOT NULL
                    ORDER BY CONVERT(varchar(50), a.IdFamilia)
                    """, cn, token),
                Depositos = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), s.IdDeposito)
                    FROM dbo.V_MV_STOCK s
                    WHERE s.IdDeposito IS NOT NULL
                    ORDER BY CONVERT(varchar(50), s.IdDeposito)
                    """, cn, token),
                Sucursales = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), s.UNEGOCIO)
                    FROM dbo.V_MV_STOCK s
                    WHERE s.UNEGOCIO IS NOT NULL
                    ORDER BY CONVERT(varchar(50), s.UNEGOCIO)
                    """, cn, token),
                Estados = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) LTRIM(RTRIM(s.Estado))
                    FROM dbo.V_MV_STOCK s
                    WHERE ISNULL(LTRIM(RTRIM(s.Estado)), '') <> ''
                    ORDER BY LTRIM(RTRIM(s.Estado))
                    """, cn, token)
            };
        }, "No se pudieron cargar los filtros de stock.", ct);

    public Task<CajaBancosFilterOptionsDto> GetCajaBancosFilterOptionsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("CajaBancos", "GetFilterOptions", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return new CajaBancosFilterOptionsDto
            {
                Cajas = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas))
                    FROM dbo.VT_CONSOLIDADO_CAJA c
                    ORDER BY ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas))
                    """, cn, token),
                Bancos = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) LTRIM(RTRIM(b.CUENTA))
                    FROM dbo.V_EstadoBancario b
                    WHERE UPPER(LTRIM(RTRIM(b.TipoVista))) = 'BC'
                      AND ISNULL(LTRIM(RTRIM(b.CUENTA)), '') <> ''
                    ORDER BY LTRIM(RTRIM(b.CUENTA))
                    """, cn, token)
            };
        }, "No se pudieron cargar los filtros de caja y bancos.", ct);

    public Task<ContabilidadFilterOptionsDto> GetContabilidadFilterOptionsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Contabilidad", "GetFilterOptions", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return new ContabilidadFilterOptionsDto
            {
                Usuarios = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) LTRIM(RTRIM(a.USUARIO_LOGEADO))
                    FROM dbo.MV_ASIENTOS a
                    WHERE ISNULL(LTRIM(RTRIM(a.USUARIO_LOGEADO)), '') <> ''
                    ORDER BY LTRIM(RTRIM(a.USUARIO_LOGEADO))
                    """, cn, token),
                Sucursales = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (100) CONVERT(varchar(50), a.UNEGOCIO)
                    FROM dbo.MV_ASIENTOS a
                    WHERE a.UNEGOCIO IS NOT NULL
                    ORDER BY CONVERT(varchar(50), a.UNEGOCIO)
                    """, cn, token),
                Tipos = await QueryDistinctAsync("""
                    SELECT DISTINCT TOP (10) LTRIM(RTRIM(a.[DEBE-HABER]))
                    FROM dbo.MV_ASIENTOS a
                    WHERE ISNULL(LTRIM(RTRIM(a.[DEBE-HABER])), '') <> ''
                    ORDER BY LTRIM(RTRIM(a.[DEBE-HABER]))
                    """, cn, token)
            };
        }, "No se pudieron cargar los filtros de contabilidad.", ct);

    public async Task<VentasDashboardDto> GetVentasAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetDashboard", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string ventasWhere = """
                WHERE (
                      c.TC LIKE 'FC%'
                      OR c.TC LIKE 'NC%'
                      OR c.TC LIKE 'ND%'
                      OR c.TC LIKE 'FP%'
                  )
                  AND (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR c.CUENTA LIKE @ClienteLike OR c.NOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR c.Usuario = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), c.UNEGOCIO))) + ',%')
                  AND (@Deposito IS NULL OR CONVERT(varchar(50), c.IdDeposito) = @Deposito)
                  AND (@TipoComprobante IS NULL OR c.TC = @TipoComprobante)
                """;

            var kpis = await QuerySingleAsync("""
                SELECT
                    ISNULL(SUM(l.IMPORTE), 0) AS TotalFacturado,
                    ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                                ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0) AS TicketPromedio,
                    COUNT(DISTINCT l.TC + l.IdComprobante) AS Comprobantes,
                    COUNT(DISTINCT l.CUENTA) AS ClientesActivos
                FROM dbo.Libro_VentasConFP l
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                """, cn, r => new VentasDashboardDto
                {
                    TotalFacturadoMes = GetDecimal(r, 0),
                    TicketPromedioMes = GetDecimal(r, 1),
                    ComprobantesMes = GetInt(r, 2),
                    ClientesActivos = GetInt(r, 3)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasDashboardDto();

            var evolucion = await QueryMonthlyAsync("""
                WITH Meses AS (
                    SELECT DATEADD(month, -11, DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date))) AS MesInicio
                    UNION ALL
                    SELECT DATEADD(month, 1, MesInicio)
                    FROM Meses
                    WHERE MesInicio < DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date))
                ),
                VentasMensuales AS (
                    SELECT
                        DATEADD(day, 1 - DAY(l.FECHA), CAST(l.FECHA AS date)) AS MesInicio,
                        SUM(l.IMPORTE) AS Total
                    FROM dbo.Libro_VentasConFP l
                    WHERE l.FECHA >= DATEADD(month, -11, DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date)))
                      AND l.FECHA < ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))
                      AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                      AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                      AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                      AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                    GROUP BY DATEADD(day, 1 - DAY(l.FECHA), CAST(l.FECHA AS date))
                )
                SELECT
                    CONVERT(varchar(7), m.MesInicio, 120) AS Periodo,
                    ISNULL(v.Total, 0) AS Total
                FROM Meses m
                LEFT JOIN VentasMensuales v ON v.MesInicio = m.MesInicio
                ORDER BY m.MesInicio
                OPTION (MAXRECURSION 12)
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var topClientes = await QueryCategoryAsync("""
                SELECT TOP (8)
                    ISNULL(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)), ''), l.CUENTA) AS Categoria,
                    l.CUENTA AS Codigo,
                    SUM(l.IMPORTE) AS Total
                FROM dbo.Libro_VentasConFP l
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                GROUP BY l.CUENTA, l.CABNOMBRE
                ORDER BY SUM(l.IMPORTE) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var topArticulos = await QueryCategoryAsync($"""
                SELECT TOP (8)
                    i.DESCRIPCION AS Categoria,
                    CONVERT(varchar(50), i.IDARTICULO) AS Codigo,
                    SUM(i.TOTAL) AS Total
                FROM dbo.V_MV_CpteInsumos i
                INNER JOIN dbo.V_MV_Cpte c
                    ON c.TC = i.TC
                   AND c.IDCOMPROBANTE = i.IDCOMPROBANTE
                   AND c.IDCOMPLEMENTO = i.IDCOMPLEMENTO
                {ventasWhere}
                GROUP BY i.IDARTICULO, i.DESCRIPCION
                ORDER BY SUM(i.TOTAL) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var ultimos = await QueryMovementsAsync($"""
                SELECT TOP (10)
                    c.FECHA,
                    c.TC,
                    ISNULL(NULLIF(LTRIM(RTRIM(c.NOMBRE)), ''), c.CUENTA) AS Nombre,
                    CONVERT(varchar(50), c.IDCOMPROBANTE),
                    c.IMPORTE
                FROM dbo.V_MV_Cpte c
                {ventasWhere}
                ORDER BY c.FECHA DESC, c.ID DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            return new VentasDashboardDto
            {
                TotalFacturadoMes = kpis.TotalFacturadoMes,
                TicketPromedioMes = kpis.TicketPromedioMes,
                ComprobantesMes = kpis.ComprobantesMes,
                ClientesActivos = kpis.ClientesActivos,
                EvolucionMensual = evolucion,
                TopClientes = topClientes,
                TopArticulos = topArticulos,
                UltimosComprobantes = ultimos
            };
        }, "No se pudo cargar el dashboard de ventas.", ct);
    }

    public async Task<ComparativoVentasComprasDto> GetComparativoAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetComparativo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // KPI — total ventas del período (misma fuente que el dashboard de ventas)
            decimal totalVentas = 0;
            await using (var cmd = new SqlCommand("""
                SELECT ISNULL(SUM(l.IMPORTE), 0)
                FROM dbo.Libro_VentasConFP l
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                """, cn))
            {
                BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token)) totalVentas = GetDecimal(rd, 0);
            }

            // KPI — total compras del período (libro IVA compras: base gravada + IVA por alícuota)
            decimal totalCompras = 0;
            await using (var cmd = new SqlCommand("""
                SELECT ISNULL(SUM(
                           ISNULL(c.NETO_GRAVADO, 0)
                         + ISNULL(c.IVA_21,       0)
                         + ISNULL(c.IVA_105,      0)
                         + ISNULL(c.IVA_27,       0)
                         + ISNULL(c.IVA_1735,     0)
                       ), 0)
                FROM dbo.LibroIvaCompras_Contadores c
                WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                """, cn))
            {
                BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token)) totalCompras = GetDecimal(rd, 0);
            }

            // Evolución mensual — ventas (últimos 12 meses respecto a FechaHasta)
            var meses = BuildLast12Months(filters.FechaHasta);
            var fechaHastaExcl = filters.FechaHasta.HasValue
                ? (object)filters.FechaHasta.Value.Date.AddDays(1)
                : DBNull.Value;

            var ventasMes = new Dictionary<string, decimal>();
            await using (var cmd = new SqlCommand("""
                SELECT CONVERT(char(7), l.FECHA, 126), ISNULL(SUM(l.IMPORTE), 0)
                FROM dbo.Libro_VentasConFP l
                WHERE l.FECHA >= @EvolucionDesde
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                GROUP BY CONVERT(char(7), l.FECHA, 126)
                """, cn))
            {
                cmd.Parameters.AddWithValue("@EvolucionDesde", meses[0]);
                cmd.Parameters.AddWithValue("@FechaHastaExclusive", fechaHastaExcl);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    ventasMes[rd.GetString(0)] = GetDecimal(rd, 1);
            }

            // Evolución mensual — compras (últimos 12 meses)
            var comprasMes = new Dictionary<string, decimal>();
            await using (var cmd = new SqlCommand("""
                SELECT CONVERT(char(7), c.FECHA, 126),
                       ISNULL(SUM(
                           ISNULL(c.NETO_GRAVADO, 0)
                         + ISNULL(c.IVA_21,       0)
                         + ISNULL(c.IVA_105,      0)
                         + ISNULL(c.IVA_27,       0)
                         + ISNULL(c.IVA_1735,     0)
                       ), 0)
                FROM dbo.LibroIvaCompras_Contadores c
                WHERE c.FECHA >= @EvolucionDesde
                  AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                GROUP BY CONVERT(char(7), c.FECHA, 126)
                """, cn))
            {
                cmd.Parameters.AddWithValue("@EvolucionDesde", meses[0]);
                cmd.Parameters.AddWithValue("@FechaHastaExclusive", fechaHastaExcl);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    comprasMes[rd.GetString(0)] = GetDecimal(rd, 1);
            }

            var evolucion = meses
                .Select(m =>
                {
                    var key = m.ToString("yyyy-MM");
                    return new ComparativoMensualDto
                    {
                        Periodo = key,
                        Ventas  = ventasMes.TryGetValue(key,  out var v) ? v : 0,
                        Compras = comprasMes.TryGetValue(key, out var c) ? c : 0
                    };
                })
                .ToList();

            return new ComparativoVentasComprasDto
            {
                TotalVentas      = totalVentas,
                TotalCompras     = totalCompras,
                EvolucionMensual = evolucion
            };
        }, "No se pudo cargar el comparativo de ventas y compras.", ct);
    }

    private static List<DateTime> BuildLast12Months(DateTime? referencia)
    {
        var hasta   = referencia ?? DateTime.Today;
        var inicio  = new DateTime(hasta.Year, hasta.Month, 1).AddMonths(-11);
        return Enumerable.Range(0, 12).Select(i => inicio.AddMonths(i)).ToList();
    }

    public async Task<VentasClientesPageDto> GetVentasClientesAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetClientesPage", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string libWhere = """
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(l.IMPORTE), 0),
                    COUNT(DISTINCT l.CUENTA),
                    ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                                ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0)
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                """, cn, r => new VentasClientesPageDto
                {
                    TotalFacturado = GetDecimal(r, 0),
                    ClientesActivos = GetInt(r, 1),
                    TicketPromedio = GetDecimal(r, 2)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasClientesPageDto();

            var top = await QueryCategoryAsync($"""
                SELECT TOP (10)
                    ISNULL(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)), ''), l.CUENTA) AS Categoria,
                    l.CUENTA AS Codigo,
                    SUM(l.IMPORTE) AS Total
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                GROUP BY l.CUENTA, l.CABNOMBRE
                ORDER BY SUM(l.IMPORTE) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var clientes = await QueryVentasClientesAsync($"""
                SELECT
                    l.CUENTA AS Cuenta,
                    ISNULL(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)), ''), l.CUENTA) AS Cliente,
                    SUM(l.IMPORTE) AS TotalFacturado,
                    COUNT(DISTINCT l.TC + l.IdComprobante) AS CantidadComprobantes,
                    ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                                ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0) AS TicketPromedio,
                    MAX(l.FECHA) AS UltimaVenta
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                GROUP BY l.CUENTA, l.CABNOMBRE
                ORDER BY SUM(l.IMPORTE) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var total = clientes.Sum(x => x.TotalFacturado);
            var normalized = clientes.Select(x => WithParticipacion(x, total)).ToList();

            return new VentasClientesPageDto
            {
                TotalFacturado = kpis.TotalFacturado,
                ClientesActivos = kpis.ClientesActivos,
                TicketPromedio = kpis.TicketPromedio,
                TopClientes = top,
                Clientes = normalized
            };
        }, "No se pudo cargar la página de clientes de ventas.", ct);
    }

    public async Task<VentasRubrosPageDto> GetVentasRubrosAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetRubrosPage", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string detWhere = """
                WHERE EXISTS (
                    SELECT 1 FROM dbo.Libro_VentasConFP l
                    WHERE l.TC = d.TC
                      AND l.IdComprobante = d.IDCOMPROBANTE
                      AND (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                      AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                      AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                      AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                      AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                      AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                )
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(d.ValorVenta), 0),
                    COUNT(DISTINCT d.IDRUBRO)
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhere}
                """, cn, r => new VentasRubrosPageDto
                {
                    TotalVendido = GetDecimal(r, 0),
                    RubrosActivos = GetInt(r, 1)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasRubrosPageDto();

            var top = await QueryCategoryAsync($"""
                SELECT TOP (10)
                    ISNULL(NULLIF(LTRIM(RTRIM(d.DescRubro)), ''), CONVERT(varchar(50), d.IDRUBRO)) AS Categoria,
                    CONVERT(varchar(50), d.IDRUBRO) AS Codigo,
                    SUM(d.ValorVenta) AS Total
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhere}
                GROUP BY d.IDRUBRO, d.DescRubro
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var rubros = await QueryVentasRubrosAsync($"""
                SELECT
                    ISNULL(NULLIF(LTRIM(RTRIM(d.DescRubro)), ''), CONVERT(varchar(50), d.IDRUBRO)) AS Rubro,
                    SUM(d.ValorVenta) AS TotalVendido,
                    COUNT(DISTINCT d.Articulo) AS CantidadArticulos,
                    COUNT(DISTINCT d.TC + d.IDCOMPROBANTE) AS CantidadComprobantes
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhere}
                GROUP BY d.IDRUBRO, d.DescRubro
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var total = rubros.Sum(x => x.TotalVendido);
            var normalized = rubros.Select(x => WithParticipacion(x, total)).ToList();

            return new VentasRubrosPageDto
            {
                TotalVendido = kpis.TotalVendido,
                RubrosActivos = kpis.RubrosActivos,
                TopRubros = top,
                Rubros = normalized
            };
        }, "No se pudo cargar la página de rubros de ventas.", ct);
    }

    public async Task<VentasFamiliasPageDto> GetVentasFamiliasAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetFamiliasPage", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string detWhereFam = """
                WHERE EXISTS (
                    SELECT 1 FROM dbo.Libro_VentasConFP l
                    WHERE l.TC = d.TC
                      AND l.IdComprobante = d.IDCOMPROBANTE
                      AND (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                      AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                      AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                      AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                      AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                      AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                )
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(d.ValorVenta), 0),
                    COUNT(DISTINCT a.IdFamilia)
                FROM dbo.VT_DETALLEIVAPROFORMA d
                INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = d.Articulo
                {detWhereFam}
                """, cn, r => new VentasFamiliasPageDto
                {
                    TotalVendido = GetDecimal(r, 0),
                    FamiliasActivas = GetInt(r, 1)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasFamiliasPageDto();

            var top = await QueryCategoryAsync($"""
                SELECT TOP (10)
                    COALESCE(NULLIF(fj.Descripcion, ''), CONVERT(varchar(50), a.IdFamilia)) AS Categoria,
                    CONVERT(varchar(50), a.IdFamilia) AS Codigo,
                    SUM(d.ValorVenta) AS Total
                FROM dbo.VT_DETALLEIVAPROFORMA d
                INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = d.Articulo
                LEFT JOIN dbo.vw_familias_jerarquia fj ON fj.IdFamilia = CONVERT(varchar(50), a.IdFamilia)
                {detWhereFam}
                GROUP BY a.IdFamilia, fj.Descripcion
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var familias = await QueryVentasFamiliasAsync($"""
                SELECT
                    CONVERT(varchar(50), a.IdFamilia) AS Familia,
                    SUM(d.ValorVenta) AS TotalVendido,
                    COUNT(DISTINCT d.Articulo) AS CantidadArticulos,
                    COUNT(DISTINCT d.TC + d.IDCOMPROBANTE) AS CantidadComprobantes,
                    ISNULL(fj.Descripcion, '') AS DescripcionFamilia
                FROM dbo.VT_DETALLEIVAPROFORMA d
                INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = d.Articulo
                LEFT JOIN dbo.vw_familias_jerarquia fj ON fj.IdFamilia = CONVERT(varchar(50), a.IdFamilia)
                {detWhereFam}
                GROUP BY a.IdFamilia, fj.Descripcion
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var total = familias.Sum(x => x.TotalVendido);
            var normalized = familias.Select(x => WithParticipacion(x, total)).ToList();

            return new VentasFamiliasPageDto
            {
                TotalVendido = kpis.TotalVendido,
                FamiliasActivas = kpis.FamiliasActivas,
                TopFamilias = top,
                Familias = normalized
            };
        }, "No se pudo cargar la página de familias de ventas.", ct);
    }

    public async Task<VentasArticulosPageDto> GetVentasArticulosAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetArticulosPage", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string detWhereArt = """
                WHERE EXISTS (
                    SELECT 1 FROM dbo.Libro_VentasConFP l
                    WHERE l.TC = d.TC
                      AND l.IdComprobante = d.IDCOMPROBANTE
                      AND (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                      AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                      AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                      AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                      AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                      AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                )
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(d.ValorVenta), 0),
                    COUNT(DISTINCT d.Articulo),
                    ISNULL(SUM(d.Consumo), 0)
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhereArt}
                """, cn, r => new VentasArticulosPageDto
                {
                    TotalVendido = GetDecimal(r, 0),
                    ArticulosActivos = GetInt(r, 1),
                    CantidadVendida = GetDecimal(r, 2)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasArticulosPageDto();

            var topTotal = await QueryCategoryAsync($"""
                SELECT TOP (10)
                    d.DESCRIPCION AS Categoria,
                    CONVERT(varchar(50), d.Articulo) AS Codigo,
                    SUM(d.ValorVenta) AS Total
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhereArt}
                GROUP BY d.Articulo, d.DESCRIPCION
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var topCantidad = await QueryCategoryAsync($"""
                SELECT TOP (10)
                    d.DESCRIPCION AS Categoria,
                    CONVERT(varchar(50), d.Articulo) AS Codigo,
                    SUM(d.Consumo) AS Total
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhereArt}
                GROUP BY d.Articulo, d.DESCRIPCION
                ORDER BY SUM(d.Consumo) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var articulos = await QueryVentasArticulosAsync($"""
                SELECT
                    CONVERT(varchar(50), d.Articulo) AS IdArticulo,
                    d.DESCRIPCION,
                    SUM(d.Consumo) AS CantidadVendida,
                    SUM(d.ValorVenta) AS TotalVendido,
                    COUNT(DISTINCT d.TC + d.IDCOMPROBANTE) AS CantidadComprobantes,
                    MAX(d.FECHA) AS UltimaVenta
                FROM dbo.VT_DETALLEIVAPROFORMA d
                {detWhereArt}
                GROUP BY d.Articulo, d.DESCRIPCION
                ORDER BY SUM(d.ValorVenta) DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            return new VentasArticulosPageDto
            {
                TotalVendido = kpis.TotalVendido,
                ArticulosActivos = kpis.ArticulosActivos,
                CantidadVendida = kpis.CantidadVendida,
                TopPorTotal = topTotal,
                TopPorCantidad = topCantidad,
                Articulos = articulos
            };
        }, "No se pudo cargar la página de artículos de ventas.", ct);
    }

    public async Task<VentasComprobantesPageDto> GetVentasComprobantesAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetComprobantes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string libWhere = """
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(l.IMPORTE), 0) AS TotalImporte,
                    COUNT(DISTINCT l.TC + l.IdComprobante) AS TotalComprobantes,
                    COUNT(DISTINCT l.CUENTA) AS ClientesActivos,
                    ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                                ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0) AS TicketPromedio
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                """, cn, r => new VentasComprobantesPageDto
                {
                    TotalImporte = GetDecimal(r, 0),
                    TotalComprobantes = GetInt(r, 1),
                    ClientesActivos = GetInt(r, 2),
                    TicketPromedio = GetDecimal(r, 3)
                }, cmd => BindVentasFilters(cmd, filters), token) ?? new VentasComprobantesPageDto();

            var rows = await QueryVentasComprobantesAsync($"""
                SELECT TOP (501)
                    l.TC,
                    l.IdComprobante,
                    l.FECHA,
                    l.CUENTA,
                    ISNULL(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)), ''), l.CUENTA) AS Cliente,
                    l.IMPORTE,
                    LTRIM(RTRIM(ISNULL(l.USUARIO_LOGEADO, ''))) AS Usuario
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                ORDER BY l.FECHA DESC, l.IdComprobante DESC
                """, cn, cmd => BindVentasFilters(cmd, filters), token);

            var hayMas = rows.Count > 500;

            return new VentasComprobantesPageDto
            {
                TotalImporte = kpis.TotalImporte,
                TotalComprobantes = kpis.TotalComprobantes,
                ClientesActivos = kpis.ClientesActivos,
                TicketPromedio = kpis.TicketPromedio,
                HayMasResultados = hayMas,
                Comprobantes = hayMas ? rows.Take(500).ToList() : rows
            };
        }, "No se pudo cargar el listado de comprobantes.", ct);
    }

    public Task<IReadOnlyList<VentasComprobanteItemDto>> GetVentasComprobanteItemsAsync(string tc, string idComprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync("Ventas", "GetComprobanteItems", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return await QueryVentasComprobanteItemsAsync("""
                SELECT
                    CONVERT(varchar(50), d.Articulo) AS IdArticulo,
                    LTRIM(RTRIM(ISNULL(d.DESCRIPCION, ''))) AS Descripcion,
                    CAST(CASE WHEN d.TC IN ('NC', 'NCFP') THEN d.Consumo * -1 ELSE d.Consumo END AS decimal(10,2)) AS Cantidad,
                    CAST(CASE WHEN d.TC IN ('FP', 'NCFP') THEN d.ValorVtaCIVA ELSE (d.ValorVenta + d.Impuestos) END AS decimal(10,2)) AS PrecioNeto,
                    ISNULL(d.ValorVtaCIVA, 0) AS TotalConIVA,
                    ISNULL(d.Costo / CASE WHEN ISNULL(d.Consumo, 0) = 0 THEN 1 ELSE d.Consumo END, 0) AS CostoUnit,
                    ISNULL(d.DescRubro, '') AS Rubro
                FROM dbo.VT_DETALLEIVAPROFORMA_COMPLETO d
                WHERE d.TC = @TC
                  AND d.IDCOMPROBANTE = @IDCOMPROBANTE
                ORDER BY d.Articulo
                """, cn, cmd =>
            {
                cmd.Parameters.AddWithValue("@TC", tc);
                cmd.Parameters.AddWithValue("@IDCOMPROBANTE", idComprobante);
            }, token);
        }, "No se pudo cargar el detalle del comprobante.", ct);

    public Task<VentasResumenTcPageDto> GetVentasResumenPorTcAsync(VentasDashboardFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Ventas", "GetResumenPorTc", async token =>
        {
            filters ??= new VentasDashboardFilters();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string libWhere = """
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                """;

            const string ivaSlots = """
                CASE WHEN ISNULL(l.LIVA_AlicIVA,  0) = {0} THEN ISNULL(l.LIVA_ImpIVA,  0) ELSE 0 END +
                CASE WHEN ISNULL(l.LIVA_AlicIva2, 0) = {0} THEN ISNULL(l.LIVA_ImpIva2, 0) ELSE 0 END +
                CASE WHEN ISNULL(l.LIVA_AlicIVA3, 0) = {0} THEN ISNULL(l.LIVA_ImpIVA3, 0) ELSE 0 END +
                CASE WHEN ISNULL(l.LIVA_AlicIVA4, 0) = {0} THEN ISNULL(l.LIVA_ImpIVA4, 0) ELSE 0 END
                """;

            var sql = $"""
                SELECT
                    l.TC,
                    COUNT(DISTINCT l.IdComprobante)                                    AS Cantidad,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_ImpNetoGrav, 0) ELSE 0 END)                                                AS NetoGravado,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_ImpNetoNGrav, 0) + ISNULL(l.LIVA_EXENTO, 0) ELSE 0 END)                    AS NetoNoGravado,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN {string.Format(ivaSlots, "21")} ELSE 0 END)   AS Iva21,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN {string.Format(ivaSlots, "10.5")} ELSE 0 END) AS Iva105,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_ImpIVARec, 0) ELSE 0 END)                                                  AS IvaRec,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_Ret_IBtos, 0) ELSE 0 END)                                                  AS RetIIBB,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_Ret_Ganancias, 0) ELSE 0 END)                                              AS RetGanancias,
                    SUM(CASE WHEN l.TC NOT IN ('FP','NCFP') THEN ISNULL(l.LIVA_Ret_Perc, 0) ELSE 0 END)                                                   AS RetIVA,
                    SUM(CASE WHEN l.TC IN ('FP','NCFP') THEN l.IMPORTE ELSE ISNULL(l.LIVA_TOTAL, 0) END)                                                  AS Total
                FROM dbo.Libro_VentasConFP l
                {libWhere}
                GROUP BY l.TC
                ORDER BY l.TC
                """;

            var filas = new List<VentasResumenTcDto>();
            await using var cmd = new SqlCommand(sql, cn);
            BindVentasFilters(cmd, filters);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                filas.Add(new VentasResumenTcDto
                {
                    Tc           = GetStringValue(rd, 0),
                    Cantidad     = GetInt(rd, 1),
                    NetoGravado  = GetDecimal(rd, 2),
                    NetoNoGravado= GetDecimal(rd, 3),
                    Iva21        = GetDecimal(rd, 4),
                    Iva105       = GetDecimal(rd, 5),
                    IvaRec       = GetDecimal(rd, 6),
                    RetIIBB      = GetDecimal(rd, 7),
                    RetGanancias = GetDecimal(rd, 8),
                    RetIVA       = GetDecimal(rd, 9),
                    Total        = GetDecimal(rd, 10)
                });
            }

            return new VentasResumenTcPageDto
            {
                TotalComprobantes = filas.Sum(x => x.Cantidad),
                TotalGeneral      = filas.Sum(x => x.Total),
                Filas             = filas
            };
        }, "No se pudo cargar el resumen por tipo de comprobante.", ct);

    public async Task<VentasComparativoPeriodoDto> GetVentasComparativoPeriodoAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetComparativoPeriodo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var desde = filters.FechaDesde ?? DateTime.Today;
            var hasta = filters.FechaHasta ?? DateTime.Today;
            var dias = Math.Max(0, (hasta.Date - desde.Date).Days);

            var periodoAnteriorHasta = desde.Date.AddDays(-1);
            var periodoAnteriorDesde = periodoAnteriorHasta.AddDays(-dias);

            var anioAnteriorDesde = desde.Date.AddYears(-1);
            var anioAnteriorHasta = hasta.Date.AddYears(-1);

            var actual = await GetPeriodoKpiAsync(cn, filters, desde.Date, hasta.Date, token);
            var periodoAnterior = await GetPeriodoKpiAsync(cn, filters, periodoAnteriorDesde, periodoAnteriorHasta, token);
            var anioAnterior = await GetPeriodoKpiAsync(cn, filters, anioAnteriorDesde, anioAnteriorHasta, token);

            return new VentasComparativoPeriodoDto
            {
                Actual = actual,
                PeriodoAnterior = periodoAnterior,
                AnioAnterior = anioAnterior
            };
        }, "No se pudo cargar el comparativo por período.", ct);
    }

    private async Task<VentasPeriodoKpiDto> GetPeriodoKpiAsync(SqlConnection cn, VentasDashboardFilters filters, DateTime fechaDesde, DateTime fechaHasta, CancellationToken ct)
    {
        const string sql = """
            SELECT
                ISNULL(SUM(l.IMPORTE), 0) AS TotalFacturado,
                COUNT(DISTINCT l.TC + l.IdComprobante) AS Comprobantes,
                ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                            ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0) AS TicketPromedio
            FROM dbo.Libro_VentasConFP l
            WHERE l.FECHA >= @FechaDesde
              AND l.FECHA < @FechaHastaExclusive
              AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
              AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
              AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
              AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@FechaDesde", fechaDesde);
        cmd.Parameters.AddWithValue("@FechaHastaExclusive", fechaHasta.Date.AddDays(1));
        AddNullableString(cmd, "@ClienteLike", Like(filters.Cliente));
        AddNullableString(cmd, "@Usuario", filters.Usuario);
        AddNullableString(cmd, "@Sucursales", JoinCodes(filters.UnidadesNegocio));
        AddNullableString(cmd, "@TipoComprobante", filters.TipoComprobante);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (await rd.ReadAsync(ct))
        {
            return new VentasPeriodoKpiDto
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                TotalFacturado = GetDecimal(rd, 0),
                Comprobantes = GetInt(rd, 1),
                TicketPromedio = GetDecimal(rd, 2)
            };
        }

        return new VentasPeriodoKpiDto { FechaDesde = fechaDesde, FechaHasta = fechaHasta };
    }

    public async Task<IReadOnlyList<VentasUnidadNegocioResumenDto>> GetVentasPorUnidadNegocioAsync(VentasDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new VentasDashboardFilters();

        return await ExecuteLoggedAsync("Ventas", "GetPorUnidadNegocio", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                SELECT
                    LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) AS Codigo,
                    ISNULL(NULLIF(LTRIM(RTRIM(u.Descripcion)), ''), LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO)))) AS Descripcion,
                    ISNULL(SUM(l.IMPORTE), 0) AS TotalFacturado,
                    COUNT(DISTINCT l.TC + l.IdComprobante) AS Comprobantes,
                    ISNULL(CASE WHEN COUNT(DISTINCT l.TC + l.IdComprobante) = 0 THEN 0
                                ELSE SUM(l.IMPORTE) / COUNT(DISTINCT l.TC + l.IdComprobante) END, 0) AS TicketPromedio,
                    COUNT(DISTINCT l.CUENTA) AS ClientesActivos
                FROM dbo.Libro_VentasConFP l
                LEFT JOIN dbo.V_TA_UnidadNegocio u ON LTRIM(RTRIM(u.Codigo)) = LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO)))
                WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR l.FECHA < @FechaHastaExclusive)
                  AND (@ClienteLike IS NULL OR l.CUENTA LIKE @ClienteLike OR l.CABNOMBRE LIKE @ClienteLike)
                  AND (@Usuario IS NULL OR l.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursales IS NULL OR ',' + @Sucursales + ',' LIKE '%,' + LTRIM(RTRIM(CONVERT(varchar(50), l.UNEGOCIO))) + ',%')
                  AND (@TipoComprobante IS NULL OR l.TC = @TipoComprobante)
                  AND l.UNEGOCIO IS NOT NULL
                GROUP BY l.UNEGOCIO, u.Descripcion
                ORDER BY SUM(l.IMPORTE) DESC
                """;

            var items = new List<VentasUnidadNegocioResumenDto>();
            await using var cmd = new SqlCommand(sql, cn);
            BindVentasFilters(cmd, filters);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new VentasUnidadNegocioResumenDto
                {
                    Codigo = GetStringValue(rd, 0),
                    Descripcion = GetStringValue(rd, 1),
                    TotalFacturado = GetDecimal(rd, 2),
                    Comprobantes = GetInt(rd, 3),
                    TicketPromedio = GetDecimal(rd, 4),
                    ClientesActivos = GetInt(rd, 5)
                });
            }

            return (IReadOnlyList<VentasUnidadNegocioResumenDto>)items;
        }, "No se pudo cargar la comparativa por unidad de negocio.", ct);
    }

    public async Task<StockDashboardDto> GetStockAsync(StockDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new StockDashboardFilters();

        return await ExecuteLoggedAsync("Stock", "GetDashboard", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string stockCurrentWhere = """
                WHERE (@FechaHastaExclusive IS NULL OR s.FECHA < @FechaHastaExclusive)
                  AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), s.IDArticulo) LIKE @ArticuloCodigoLike)
                  AND (@Deposito IS NULL OR CONVERT(varchar(50), s.IdDeposito) = @Deposito)
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), s.UNEGOCIO) = @Sucursal)
                  AND (@Estado IS NULL OR s.Estado = @Estado)
                """;

            const string stockArticleWhere = """
                WHERE ISNULL(a.SUSPENDIDO, 0) = 0
                  AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), a.IDARTICULO) LIKE @ArticuloCodigoLike)
                  AND (@ArticuloDescripcionLike IS NULL OR a.DESCRIPCION LIKE @ArticuloDescripcionLike)
                  AND (@Rubro IS NULL OR CONVERT(varchar(50), a.IDRUBRO) = @Rubro)
                  AND (@Familia IS NULL OR CONVERT(varchar(50), a.IdFamilia) = @Familia)
                """;

            var kpis = await QuerySingleAsync($"""
                WITH StockActual AS (
                    SELECT s.IDArticulo, SUM(s.Cantidad) AS StockActual
                    FROM dbo.V_MV_STOCK s
                    {stockCurrentWhere}
                    GROUP BY s.IDArticulo
                )
                SELECT
                    ISNULL(SUM(CASE WHEN ISNULL(st.StockActual, 0) > 0 THEN ISNULL(st.StockActual, 0) * ISNULL(a.COSTO, 0) ELSE 0 END), 0) AS StockValorizado,
                    SUM(CASE WHEN ISNULL(st.StockActual, 0) > 0 THEN 1 ELSE 0 END) AS ArticulosConStock,
                    SUM(CASE WHEN ISNULL(st.StockActual, 0) <= ISNULL(a.PUNTOPEDIDO, 0) AND ISNULL(a.PUNTOPEDIDO, 0) > 0 THEN 1 ELSE 0 END) AS BajoPuntoPedido,
                    SUM(CASE WHEN ISNULL(st.StockActual, 0) <= 0 THEN 1 ELSE 0 END) AS SinStock
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN StockActual st ON st.IDArticulo = a.IDARTICULO
                {stockArticleWhere}
                """, cn, r => new StockDashboardDto
                {
                    StockValorizado = GetDecimal(r, 0),
                    ArticulosConStock = GetInt(r, 1),
                    BajoPuntoPedido = GetInt(r, 2),
                    SinStock = GetInt(r, 3)
                }, cmd => BindStockFilters(cmd, filters), token) ?? new StockDashboardDto();

            var evolucion = await QueryMonthlyAsync("""
                WITH Meses AS (
                    SELECT DATEADD(month, -11, DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date))) AS MesInicio
                    UNION ALL
                    SELECT DATEADD(month, 1, MesInicio)
                    FROM Meses
                    WHERE MesInicio < DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date))
                ),
                ArticulosFiltrados AS (
                    SELECT
                        a.IDARTICULO,
                        a.COSTO
                    FROM dbo.V_MA_ARTICULOS a
                    WHERE ISNULL(a.SUSPENDIDO, 0) = 0
                      AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), a.IDARTICULO) LIKE @ArticuloCodigoLike)
                      AND (@ArticuloDescripcionLike IS NULL OR a.DESCRIPCION LIKE @ArticuloDescripcionLike)
                      AND (@Rubro IS NULL OR CONVERT(varchar(50), a.IDRUBRO) = @Rubro)
                      AND (@Familia IS NULL OR CONVERT(varchar(50), a.IdFamilia) = @Familia)
                ),
                StockBase AS (
                    SELECT
                        s.IDArticulo,
                        SUM(s.Cantidad) AS CantidadBase
                    FROM dbo.V_MV_STOCK s
                    INNER JOIN ArticulosFiltrados a ON a.IDARTICULO = s.IDArticulo
                    WHERE s.FECHA < DATEADD(month, -11, DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date)))
                      AND (@Deposito IS NULL OR CONVERT(varchar(50), s.IdDeposito) = @Deposito)
                      AND (@Sucursal IS NULL OR CONVERT(varchar(50), s.UNEGOCIO) = @Sucursal)
                      AND (@Estado IS NULL OR s.Estado = @Estado)
                    GROUP BY s.IDArticulo
                ),
                MovMes AS (
                    SELECT
                        DATEADD(day, 1 - DAY(s.FECHA), CAST(s.FECHA AS date)) AS MesInicio,
                        s.IDArticulo,
                        SUM(s.Cantidad) AS CantidadMes
                    FROM dbo.V_MV_STOCK s
                    INNER JOIN ArticulosFiltrados a ON a.IDARTICULO = s.IDArticulo
                    WHERE s.FECHA >= DATEADD(month, -11, DATEADD(day, 1 - DAY(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE())))), CAST(DATEADD(day, -1, ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))) AS date)))
                      AND s.FECHA < ISNULL(@FechaHastaExclusive, DATEADD(day, 1, GETDATE()))
                      AND (@Deposito IS NULL OR CONVERT(varchar(50), s.IdDeposito) = @Deposito)
                      AND (@Sucursal IS NULL OR CONVERT(varchar(50), s.UNEGOCIO) = @Sucursal)
                      AND (@Estado IS NULL OR s.Estado = @Estado)
                    GROUP BY DATEADD(day, 1 - DAY(s.FECHA), CAST(s.FECHA AS date)), s.IDArticulo
                ),
                MesArticulo AS (
                    SELECT
                        m.MesInicio,
                        a.IDARTICULO,
                        CASE
                            WHEN ISNULL(sb.CantidadBase, 0)
                                + ISNULL((
                                    SELECT SUM(ISNULL(mm2.CantidadMes, 0))
                                    FROM MovMes mm2
                                    WHERE mm2.IDArticulo = a.IDARTICULO
                                      AND mm2.MesInicio <= m.MesInicio
                                ), 0) < 0
                            THEN 0
                            ELSE ISNULL(sb.CantidadBase, 0)
                            + ISNULL((
                                SELECT SUM(ISNULL(mm2.CantidadMes, 0))
                                FROM MovMes mm2
                                WHERE mm2.IDArticulo = a.IDARTICULO
                                  AND mm2.MesInicio <= m.MesInicio
                            ), 0)
                        END AS StockMes
                    FROM Meses m
                    CROSS JOIN ArticulosFiltrados a
                    LEFT JOIN StockBase sb ON sb.IDArticulo = a.IDARTICULO
                )
                SELECT
                    CONVERT(varchar(7), ma.MesInicio, 120) AS Periodo,
                    SUM(CASE WHEN ma.StockMes > 0 THEN ma.StockMes * ISNULL(a.COSTO, 0) ELSE 0 END) AS Total
                FROM MesArticulo ma
                INNER JOIN ArticulosFiltrados a ON a.IDARTICULO = ma.IDARTICULO
                GROUP BY ma.MesInicio
                ORDER BY ma.MesInicio
                OPTION (MAXRECURSION 12)
                """, cn, cmd => BindStockFilters(cmd, filters), token);

            var topMov = await QueryCategoryAsync("""
                SELECT TOP (8)
                    ISNULL(NULLIF(LTRIM(RTRIM(a.DESCRIPCION)), ''), CONVERT(varchar(50), s.IDArticulo)) AS Categoria,
                    CONVERT(varchar(50), s.IDArticulo) AS Codigo,
                    SUM(ABS(CAST(s.Cantidad AS decimal(18, 2))) * ISNULL(s.COSTO, 0)) AS Total
                FROM dbo.V_MV_STOCK s
                INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = s.IDArticulo
                WHERE (@FechaDesde IS NULL OR s.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR s.FECHA < @FechaHastaExclusive)
                  AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), s.IDArticulo) LIKE @ArticuloCodigoLike)
                  AND (@ArticuloDescripcionLike IS NULL OR a.DESCRIPCION LIKE @ArticuloDescripcionLike OR s.Descripcion LIKE @ArticuloDescripcionLike)
                  AND (@Rubro IS NULL OR CONVERT(varchar(50), a.IDRUBRO) = @Rubro)
                  AND (@Familia IS NULL OR CONVERT(varchar(50), a.IdFamilia) = @Familia)
                  AND (@Deposito IS NULL OR CONVERT(varchar(50), s.IdDeposito) = @Deposito)
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), s.UNEGOCIO) = @Sucursal)
                  AND (@Estado IS NULL OR s.Estado = @Estado)
                GROUP BY s.IDArticulo, a.DESCRIPCION
                ORDER BY SUM(ABS(CAST(s.Cantidad AS decimal(18, 2))) * ISNULL(s.COSTO, 0)) DESC
                """, cn, cmd => BindStockFilters(cmd, filters), token);

            var criticos = await QueryStockCriticosAsync(cn, cmd => BindStockFilters(cmd, filters), token);

            return new StockDashboardDto
            {
                StockValorizado = kpis.StockValorizado,
                ArticulosConStock = kpis.ArticulosConStock,
                BajoPuntoPedido = kpis.BajoPuntoPedido,
                SinStock = kpis.SinStock,
                EvolucionMensual = evolucion,
                TopMovimientos = topMov,
                Criticos = criticos
            };
        }, "No se pudo cargar el dashboard de stock.", ct);
    }

    public async Task<CajaBancosDashboardDto> GetCajaBancosAsync(CajaBancosDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new CajaBancosDashboardFilters();

        return await ExecuteLoggedAsync("CajaBancos", "GetDashboard", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var kpis = await QuerySingleAsync("""
                WITH CajasActuales AS (
                    SELECT
                        ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) AS CajaNombre,
                        c.SALDO,
                        ROW_NUMBER() OVER (
                            PARTITION BY ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas))
                            ORDER BY c.FECHA DESC
                        ) AS rn
                    FROM dbo.VT_CONSOLIDADO_CAJA c
                    WHERE (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                      AND (@Caja IS NULL OR ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) = @Caja)
                      AND (@TextoLike IS NULL OR c.Descripcion LIKE @TextoLike)
                )
                SELECT
                    ISNULL((SELECT SUM(SALDO) FROM CajasActuales WHERE rn = 1), 0) AS SaldoCajas,
                    ISNULL((
                        SELECT SUM(CASE WHEN b.[DEBE-HABER] = 'D' THEN b.IMPORTE ELSE -b.IMPORTE END)
                        FROM dbo.V_EstadoBancario b
                        WHERE UPPER(LTRIM(RTRIM(b.TipoVista))) = 'BC'
                          AND (@FechaHastaExclusive IS NULL OR b.FECHA < @FechaHastaExclusive)
                          AND (@BancoCuenta IS NULL OR b.CUENTA = @BancoCuenta)
                          AND (@TextoLike IS NULL OR b.DETALLE LIKE @TextoLike)
                    ), 0) AS SaldoBancos,
                    ISNULL((
                        SELECT SUM(c.INGRESOS)
                        FROM dbo.VT_CONSOLIDADO_CAJA c
                        WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                          AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                          AND (@Caja IS NULL OR ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) = @Caja)
                          AND (@TextoLike IS NULL OR c.Descripcion LIKE @TextoLike)
                    ),0) AS IngresosPeriodo,
                    ISNULL((
                        SELECT SUM(c.EGRESOS)
                        FROM dbo.VT_CONSOLIDADO_CAJA c
                        WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                          AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                          AND (@Caja IS NULL OR ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) = @Caja)
                          AND (@TextoLike IS NULL OR c.Descripcion LIKE @TextoLike)
                    ),0) AS EgresosPeriodo,
                    ISNULL((SELECT SUM(SALDO) FROM dbo.VE_CPTES_IMPAGOS),0) AS PendienteCobro,
                    ISNULL((SELECT SUM(SALDO) FROM dbo.CO_CPTES_IMPAGOS),0) AS PendientePago
                """, cn, r => new CajaBancosDashboardDto
                {
                    SaldoCajas = GetDecimal(r, 0),
                    SaldoBancos = GetDecimal(r, 1),
                    Ingresos7Dias = GetDecimal(r, 2),
                    Egresos7Dias = GetDecimal(r, 3),
                    PendienteCobro = GetDecimal(r, 4),
                    PendientePago = GetDecimal(r, 5)
                }, cmd => BindCajaBancosFilters(cmd, filters), token) ?? new CajaBancosDashboardDto();

            var evolucion = await QueryMonthlyAsync("""
                SELECT
                    CONVERT(varchar(10), c.FECHA, 103) AS Periodo,
                    SUM(c.INGRESOS - c.EGRESOS) AS Total
                FROM dbo.VT_CONSOLIDADO_CAJA c
                WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                  AND (@Caja IS NULL OR ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) = @Caja)
                  AND (@TextoLike IS NULL OR c.Descripcion LIKE @TextoLike)
                GROUP BY CONVERT(varchar(10), c.FECHA, 103), CAST(c.FECHA AS date)
                ORDER BY CAST(c.FECHA AS date)
                """, cn, cmd => BindCajaBancosFilters(cmd, filters), token);

            var topCajas = await QueryCategoryAsync("""
                SELECT TOP (8)
                    ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) AS Categoria,
                    CONVERT(varchar(50), c.IdCajas) AS Codigo,
                    MAX(c.SALDO) AS Total
                FROM dbo.VT_CONSOLIDADO_CAJA c
                WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR c.FECHA < @FechaHastaExclusive)
                  AND (@Caja IS NULL OR ISNULL(NULLIF(LTRIM(RTRIM(c.Descripcion)), ''), CONVERT(varchar(50), c.IdCajas)) = @Caja)
                  AND (@TextoLike IS NULL OR c.Descripcion LIKE @TextoLike)
                GROUP BY c.IdCajas, c.Descripcion
                ORDER BY MAX(c.SALDO) DESC
                """, cn, cmd => BindCajaBancosFilters(cmd, filters), token);

            var topBancos = await QueryCategoryAsync("""
                SELECT TOP (8)
                    b.CUENTA AS Categoria,
                    b.CUENTA AS Codigo,
                    SUM(CASE WHEN b.[DEBE-HABER] = 'D' THEN b.IMPORTE ELSE -b.IMPORTE END) AS Total
                FROM dbo.V_EstadoBancario b
                WHERE UPPER(LTRIM(RTRIM(b.TipoVista))) = 'BC'
                  AND (@FechaDesde IS NULL OR b.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR b.FECHA < @FechaHastaExclusive)
                  AND (@BancoCuenta IS NULL OR b.CUENTA = @BancoCuenta)
                  AND (@TextoLike IS NULL OR b.DETALLE LIKE @TextoLike)
                GROUP BY b.CUENTA
                ORDER BY SUM(CASE WHEN b.[DEBE-HABER] = 'D' THEN b.IMPORTE ELSE -b.IMPORTE END) DESC
                """, cn, cmd => BindCajaBancosFilters(cmd, filters), token);

            return new CajaBancosDashboardDto
            {
                SaldoCajas = kpis.SaldoCajas,
                SaldoBancos = kpis.SaldoBancos,
                Ingresos7Dias = kpis.Ingresos7Dias,
                Egresos7Dias = kpis.Egresos7Dias,
                PendienteCobro = kpis.PendienteCobro,
                PendientePago = kpis.PendientePago,
                EvolucionDiaria = evolucion,
                TopCajas = topCajas,
                TopBancos = topBancos
            };
        }, "No se pudo cargar el dashboard de caja y bancos.", ct);
    }

    public async Task<ContabilidadDashboardDto> GetContabilidadAsync(ContabilidadDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new ContabilidadDashboardFilters();

        return await ExecuteLoggedAsync("Contabilidad", "GetDashboard", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string contabilidadWhere = """
                WHERE (@FechaDesde IS NULL OR a.FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR a.FECHA < @FechaHastaExclusive)
                  AND (@CuentaLike IS NULL OR a.CUENTA LIKE @CuentaLike)
                  AND (@DetalleLike IS NULL OR a.DETALLE LIKE @DetalleLike)
                  AND (@Usuario IS NULL OR a.USUARIO_LOGEADO = @Usuario)
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), a.UNEGOCIO) = @Sucursal)
                  AND (@Tipo IS NULL OR a.[DEBE-HABER] = @Tipo)
                """;

            var kpis = await QuerySingleAsync($"""
                SELECT
                    ISNULL(SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE 0 END),0) AS DebePeriodo,
                    ISNULL(SUM(CASE WHEN a.[DEBE-HABER] = 'H' THEN a.IMPORTE ELSE 0 END),0) AS HaberPeriodo,
                    ISNULL(SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END),0) AS SaldoNetoPeriodo,
                    COUNT(DISTINCT a.[NUMERO ASIENTO]) AS AsientosPeriodo
                FROM dbo.MV_ASIENTOS a
                {contabilidadWhere}
                """, cn, r => new ContabilidadDashboardDto
                {
                    DebeMes = GetDecimal(r, 0),
                    HaberMes = GetDecimal(r, 1),
                    SaldoNetoMes = GetDecimal(r, 2),
                    AsientosMes = GetInt(r, 3)
                }, cmd => BindContabilidadFilters(cmd, filters), token) ?? new ContabilidadDashboardDto();

            var evolucion = await QueryMonthlyAsync($"""
                SELECT
                    CONVERT(varchar(7), a.FECHA, 120) AS Periodo,
                    SUM(CASE WHEN a.[DEBE-HABER] = 'D' THEN a.IMPORTE ELSE -a.IMPORTE END) AS Total
                FROM dbo.MV_ASIENTOS a
                {contabilidadWhere}
                GROUP BY CONVERT(varchar(7), a.FECHA, 120)
                ORDER BY Periodo
                """, cn, cmd => BindContabilidadFilters(cmd, filters), token);

            var topCuentas = await QueryCategoryAsync($"""
                SELECT TOP (8)
                    a.CUENTA AS Categoria,
                    a.CUENTA AS Codigo,
                    SUM(ABS(a.IMPORTE)) AS Total
                FROM dbo.MV_ASIENTOS a
                {contabilidadWhere}
                GROUP BY a.CUENTA
                ORDER BY SUM(ABS(a.IMPORTE)) DESC
                """, cn, cmd => BindContabilidadFilters(cmd, filters), token);

            var ultimos = await QueryAsientosAsync($"""
                SELECT TOP (12)
                    a.FECHA,
                    a.CUENTA,
                    a.DETALLE,
                    a.[DEBE-HABER],
                    a.IMPORTE
                FROM dbo.MV_ASIENTOS a
                {contabilidadWhere}
                ORDER BY a.FECHA DESC, a.[NUMERO ASIENTO] DESC, a.SECUENCIA DESC
                """, cn, cmd => BindContabilidadFilters(cmd, filters), token);

            return new ContabilidadDashboardDto
            {
                DebeMes = kpis.DebeMes,
                HaberMes = kpis.HaberMes,
                SaldoNetoMes = kpis.SaldoNetoMes,
                AsientosMes = kpis.AsientosMes,
                EvolucionMensual = evolucion,
                TopCuentas = topCuentas,
                UltimosAsientos = ultimos
            };
        }, "No se pudo cargar el dashboard de contabilidad.", ct);
    }

    public async Task<PosicionIvaDto> GetPosicionIvaAsync(ContabilidadDashboardFilters filters, CancellationToken ct = default)
    {
        filters ??= new ContabilidadDashboardFilters();

        return await ExecuteLoggedAsync("Contabilidad", "GetPosicionIva", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string ivaWhere = """
                WHERE (@FechaDesde IS NULL OR FECHA >= @FechaDesde)
                  AND (@FechaHastaExclusive IS NULL OR FECHA < @FechaHastaExclusive)
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), UNEGOCIO) = @Sucursal)
                """;

            // cols: 0=IVA_21, 1=IVA_105, 2=IVA_27, 3=IVA_1735, 4=IVA_RESP_INSC, 5=IVA_MONOTRIBUTO, 6=IVA_CONS_FINAL, 7=NETO_GRAVADO
            const string selectIva = """
                SELECT
                    ISNULL(SUM(IVA_21),          0),
                    ISNULL(SUM(IVA_105),         0),
                    ISNULL(SUM(IVA_27),          0),
                    ISNULL(SUM(IVA_1735),        0),
                    ISNULL(SUM(IVA_RESP_INSC),   0),
                    ISNULL(SUM(IVA_MONOTRIBUTO), 0),
                    ISNULL(SUM(IVA_CONS_FINAL),  0),
                    ISNULL(SUM(NETO_GRAVADO),    0)
                """;

            var v = new decimal[8];
            var c = new decimal[8];

            await using (var cmd = new SqlCommand($"{selectIva} FROM dbo.LibroIvaVentas_Contadores {ivaWhere}", cn))
            {
                BindIvaFilters(cmd, filters);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token))
                    for (var i = 0; i < 8; i++) v[i] = GetDecimal(rd, i);
            }

            await using (var cmd = new SqlCommand($"{selectIva} FROM dbo.LibroIvaCompras_Contadores {ivaWhere}", cn))
            {
                BindIvaFilters(cmd, filters);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token))
                    for (var i = 0; i < 8; i++) c[i] = GetDecimal(rd, i);
            }

            // EX. = registros donde ninguna columna de condición tiene IVA pero sí hay
            // IVA por alícuota (IVA_21 + IVA_105 + IVA_27 + IVA_1735). El resumen por
            // alícuota los muestra como "EX." — el total debe coincidir con ese resumen.
            var exV = (v[0] + v[1] + v[2] + v[3]) - (v[4] + v[5] + v[6]);
            var exC = (c[0] + c[1] + c[2] + c[3]) - (c[4] + c[5] + c[6]);
            var totalV = v[4] + v[5] + v[6] + exV;
            var totalC = c[4] + c[5] + c[6] + exC;

            var filas = new List<PosicionIvaFilaDto>
            {
                new() { Concepto = "IVA Resp. Inscripto", Ventas = v[4], Compras = c[4] },
                new() { Concepto = "IVA Monotributo",     Ventas = v[5], Compras = c[5] },
                new() { Concepto = "IVA Cons. Final",     Ventas = v[6], Compras = c[6] },
            };
            if (exV != 0 || exC != 0)
                filas.Add(new() { Concepto = "EX. / Sin condición", Ventas = exV, Compras = exC });

            const string monthlyWhere = """
                WHERE FECHA >= DATEADD(MONTH, -11, DATEADD(day, 1 - DAY(GETDATE()), CAST(GETDATE() AS date)))
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), UNEGOCIO) = @Sucursal)
                GROUP BY CONVERT(char(7), FECHA, 126)
                ORDER BY 1
                """;
            const string monthlySelect = """
                SELECT CONVERT(char(7), FECHA, 126),
                       ISNULL(SUM(IVA_21 + IVA_105 + IVA_27 + IVA_1735), 0)
                FROM dbo.
                """;

            var vm = new Dictionary<string, decimal>();
            await using (var cmd = new SqlCommand(monthlySelect + "LibroIvaVentas_Contadores " + monthlyWhere, cn))
            {
                AddNullableString(cmd, "@Sucursal", filters.Sucursal);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    vm[GetStringValue(rd, 0)] = GetDecimal(rd, 1);
            }

            var cm2 = new Dictionary<string, decimal>();
            await using (var cmd = new SqlCommand(monthlySelect + "LibroIvaCompras_Contadores " + monthlyWhere, cn))
            {
                AddNullableString(cmd, "@Sucursal", filters.Sucursal);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    cm2[GetStringValue(rd, 0)] = GetDecimal(rd, 1);
            }

            var periods = vm.Keys.Union(cm2.Keys).OrderBy(x => x).ToList();
            var evolucionSaldo = periods
                .Select(p => new MonthlyPointDto { Periodo = p, Total = vm.GetValueOrDefault(p) - cm2.GetValueOrDefault(p) })
                .ToList();
            var evolucionIvaMensual = periods
                .Select(p => new ComparativoMensualDto { Periodo = p, Ventas = vm.GetValueOrDefault(p), Compras = cm2.GetValueOrDefault(p) })
                .ToList();

            // Resumen por condición IVA y alícuota
            const string condIva =
                "CASE WHEN IVA_RESP_INSC<>0 THEN 'R.I.' WHEN IVA_MONOTRIBUTO<>0 THEN 'MONO.' WHEN IVA_CONS_FINAL<>0 THEN 'C.F.' ELSE 'EX.' END";

            async Task<List<ResumenAlicuotaFilaDto>> QueryResumenAsync(string tabla)
            {
                var sql = $"""
                    WITH base AS (
                        SELECT TC, {condIva} AS CondicionIVA,
                            IVA_21, IVA_105, IVA_27, IVA_1735
                        FROM dbo.{tabla}
                        WHERE (@FechaDesde IS NULL OR FECHA >= @FechaDesde)
                          AND (@FechaHastaExclusive IS NULL OR FECHA < @FechaHastaExclusive)
                          AND (@Sucursal IS NULL OR CONVERT(varchar(50), UNEGOCIO) = @Sucursal)
                    ),
                    unpivoted AS (
                        SELECT TC, CondicionIVA, CAST(21.00 AS decimal(5,2)) AS Alicuota, IVA_21 AS MontoIVA FROM base WHERE IVA_21<>0
                        UNION ALL
                        SELECT TC, CondicionIVA, CAST(10.50 AS decimal(5,2)), IVA_105 FROM base WHERE IVA_105<>0
                        UNION ALL
                        SELECT TC, CondicionIVA, CAST(27.00 AS decimal(5,2)), IVA_27 FROM base WHERE IVA_27<>0
                        UNION ALL
                        SELECT TC, CondicionIVA, CAST(17.35 AS decimal(5,2)), IVA_1735 FROM base WHERE IVA_1735<>0
                    )
                    SELECT CondicionIVA, Alicuota,
                        ISNULL(SUM(CASE WHEN COALESCE(LEFT(TC,2),'FC') NOT IN ('NC','ND') THEN MontoIVA ELSE 0 END),0),
                        ISNULL(SUM(CASE WHEN LEFT(TC,2)='NC' THEN MontoIVA ELSE 0 END),0),
                        ISNULL(SUM(CASE WHEN LEFT(TC,2)='ND' THEN MontoIVA ELSE 0 END),0)
                    FROM unpivoted
                    GROUP BY CondicionIVA, Alicuota
                    ORDER BY
                        CASE CondicionIVA WHEN 'C.F.' THEN 1 WHEN 'EX.' THEN 2 WHEN 'MONO.' THEN 3 WHEN 'R.I.' THEN 4 ELSE 5 END,
                        Alicuota
                    """;
                var result = new List<ResumenAlicuotaFilaDto>();
                await using var cmd = new SqlCommand(sql, cn);
                BindIvaFilters(cmd, filters);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    result.Add(new ResumenAlicuotaFilaDto
                    {
                        CondicionIVA = GetStringValue(rd, 0),
                        Alicuota     = GetDecimal(rd, 1),
                        FC           = GetDecimal(rd, 2),
                        NC           = GetDecimal(rd, 3),
                        ND           = GetDecimal(rd, 4),
                    });
                return result;
            }

            var resumenVentas  = await QueryResumenAsync("LibroIvaVentas_Contadores");
            var resumenCompras = await QueryResumenAsync("LibroIvaCompras_Contadores");

            return new PosicionIvaDto
            {
                TotalIvaVentas     = totalV,
                TotalIvaCompras    = totalC,
                NetoGravadoVentas  = v[7],
                NetoGravadoCompras = c[7],
                Filas              = filas,
                EvolucionSaldo     = evolucionSaldo,
                EvolucionIvaMensual = evolucionIvaMensual,
                ResumenVentas      = resumenVentas,
                ResumenCompras     = resumenCompras,
            };
        }, "No se pudo calcular la posición de IVA.", ct);
    }

    public async Task<BalanceSaldosDto> GetBalanceSaldosAsync(ContabilidadDashboardFilters filters, int nivel = 2, CancellationToken ct = default)
    {
        filters ??= new ContabilidadDashboardFilters();
        nivel = Math.Clamp(nivel, 1, 4);

        return await ExecuteLoggedAsync("Contabilidad", "GetBalanceSaldos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // La cantidad de dígitos por nivel viene de TA_CONFIGURACION, no se hardcodea
            var digitos = await LeerDigitosPlanCuentasAsync(cn, token);
            var tdigitos = CalcTdigitos(digitos, nivel);

            // El SP espera fechas como NVARCHAR(10) en formato dd/MM/yyyy
            var fechaD = (filters.FechaDesde ?? new DateTime(DateTime.Today.Year, 1, 1)).ToString("dd/MM/yyyy");
            var fechaH = (filters.FechaHasta ?? DateTime.Today).ToString("dd/MM/yyyy");
            var whereClause = BuildSaldosWhereClause(filters);

            var filas = new List<BalanceSaldoFilaDto>();
            await using var cmd = new SqlCommand("dbo.NW_SALDOSCUENTAS", cn);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.CommandTimeout = 60;
            cmd.Parameters.AddWithValue("@FECHAD",   fechaD);
            cmd.Parameters.AddWithValue("@FECHAH",   fechaH);
            cmd.Parameters.AddWithValue("@TDIGITOS", tdigitos.ToString());
            cmd.Parameters.AddWithValue("@WHERE",    whereClause);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
                filas.Add(new BalanceSaldoFilaDto
                {
                    Codigo      = GetStringValue(rd, 0),
                    Descripcion = GetStringValue(rd, 1),
                    Saldo       = GetDecimal(rd, 2),
                });

            // Ordenar por código alfabético: da el orden jerárquico correcto del plan de cuentas
            // ("1" < "11" < "12" < "2" < "21" ...)
            filas.Sort((a, b) => string.Compare(a.Codigo, b.Codigo, StringComparison.Ordinal));

            // Dígitos acumulados por nivel; la UI los usa para mapear cada fila a su columna
            var cumulativos = new List<int>
            {
                digitos.Capitulo,
                digitos.Capitulo + digitos.Subcapitulo,
                digitos.Capitulo + digitos.Subcapitulo + digitos.Rubro,
                digitos.Capitulo + digitos.Subcapitulo + digitos.Rubro + digitos.Subrubro,
            };

            // KPIs: busca las cuentas de capítulo (código de longitud exactamente DIGITOS_CAPITULO)
            decimal SaldoCap(string codigo) =>
                filas.FirstOrDefault(f => f.Codigo.Length == digitos.Capitulo && f.Codigo == codigo)?.Saldo ?? 0m;

            return new BalanceSaldosDto
            {
                Filas              = filas,
                NivelAplicado      = nivel,
                TdigitosAplicados  = tdigitos,
                DigitosCapitulo    = digitos.Capitulo,
                DigitosCumulativos = cumulativos,
                TotalActivo        = SaldoCap("1"),
                TotalPasivo        = SaldoCap("2"),
                PatrimonioNeto     = SaldoCap("3"),
                TotalResultados    = SaldoCap("4"),
            };
        }, "No se pudo obtener el balance de saldos.", ct);
    }

    // Lee los dígitos de cada nivel del plan de cuentas desde TA_CONFIGURACION
    private static async Task<DigitosPlanCuentas> LeerDigitosPlanCuentasAsync(SqlConnection cn, CancellationToken ct)
    {
        var result = new DigitosPlanCuentas();
        await using var cmd = new SqlCommand("""
            SELECT
                UPPER(LTRIM(RTRIM(clave))),
                CASE
                    WHEN ISNUMERIC(LTRIM(RTRIM(valor))) = 1 THEN CAST(LTRIM(RTRIM(valor)) AS int)
                    ELSE NULL
                END
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(clave))) LIKE 'DIGITOS_%'
            """, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var clave = GetStringValue(rd, 0);
            var valor = GetInt(rd, 1);
            switch (clave)
            {
                case "DIGITOS_CAPITULO":    result.Capitulo    = valor; break;
                case "DIGITOS_SUBCAPITULO": result.Subcapitulo = valor; break;
                case "DIGITOS_RUBRO":       result.Rubro       = valor; break;
                case "DIGITOS_SUBRUBRO":    result.Subrubro    = valor; break;
            }
        }
        return result;
    }

    // Calcula dígitos acumulados hasta el nivel solicitado (1=capítulo, 2=subcapítulo, 3=rubro, 4=subrubro)
    private static int CalcTdigitos(DigitosPlanCuentas d, int nivel) => nivel switch
    {
        1 => d.Capitulo,
        2 => d.Capitulo + d.Subcapitulo,
        3 => d.Capitulo + d.Subcapitulo + d.Rubro,
        _ => d.Capitulo + d.Subcapitulo + d.Rubro + d.Subrubro,
    };

    // Construye fragmento WHERE para el SP; UNEGOCIO es numérico, solo se acepta valor entero
    private static string BuildSaldosWhereClause(ContabilidadDashboardFilters filters)
    {
        if (string.IsNullOrWhiteSpace(filters.Sucursal)) return string.Empty;
        var suc = filters.Sucursal.Trim();
        return int.TryParse(suc, out var sucInt) ? $" AND UNEGOCIO = {sucInt}" : string.Empty;
    }

    private sealed class DigitosPlanCuentas
    {
        public int Capitulo    { get; set; } = 1;
        public int Subcapitulo { get; set; } = 1;
        public int Rubro       { get; set; } = 1;
        public int Subrubro    { get; set; } = 2;
    }

    private async Task<IReadOnlyList<string>> QueryDistinctAsync(string sql, SqlConnection cn, CancellationToken ct)
    {
        var items = new List<string>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var value = GetStringValue(rd, 0);
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value);
            }
        }

        return items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<UnidadNegocioOptionDto>> QueryUnidadesNegocioAsync(string sql, SqlConnection cn, CancellationToken ct)
    {
        var items = new List<UnidadNegocioOptionDto>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var codigo = GetStringValue(rd, 0);
            if (string.IsNullOrWhiteSpace(codigo))
                continue;

            items.Add(new UnidadNegocioOptionDto
            {
                Codigo = codigo,
                Descripcion = GetStringValue(rd, 1)
            });
        }

        return items
            .GroupBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<IReadOnlyList<MonthlyPointDto>> QueryMonthlyAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<MonthlyPointDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new MonthlyPointDto
            {
                Periodo = GetStringValue(rd, 0),
                Total = GetDecimal(rd, 1)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<CategoryTotalDto>> QueryCategoryAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<CategoryTotalDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new CategoryTotalDto
            {
                Categoria = GetStringValue(rd, 0),
                Codigo = rd.IsDBNull(1) ? null : GetStringValue(rd, 1),
                Total = GetDecimal(rd, 2)
            });
        }

        var total = items.Sum(i => i.Total);
        return items.Select(i => new CategoryTotalDto
        {
            Categoria = i.Categoria,
            Codigo = i.Codigo,
            Total = i.Total,
            Participacion = total == 0 ? 0 : i.Total / total
        }).ToList();
    }

    private async Task<IReadOnlyList<GestionMovimientoDto>> QueryMovementsAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<GestionMovimientoDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new GestionMovimientoDto
            {
                Fecha = rd.GetDateTime(0),
                Codigo = GetStringValue(rd, 1),
                Descripcion = GetStringValue(rd, 2),
                Referencia = GetStringValue(rd, 3),
                Total = GetDecimal(rd, 4)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<VentasClienteResumenDto>> QueryVentasClientesAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasClienteResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasClienteResumenDto
            {
                Cuenta = GetStringValue(rd, 0),
                Cliente = GetStringValue(rd, 1),
                TotalFacturado = GetDecimal(rd, 2),
                CantidadComprobantes = GetInt(rd, 3),
                TicketPromedio = GetDecimal(rd, 4),
                UltimaVenta = rd.IsDBNull(5) ? null : rd.GetDateTime(5)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<VentasRubroResumenDto>> QueryVentasRubrosAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasRubroResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasRubroResumenDto
            {
                Rubro = GetStringValue(rd, 0),
                TotalVendido = GetDecimal(rd, 1),
                CantidadArticulos = GetInt(rd, 2),
                CantidadComprobantes = GetInt(rd, 3)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<VentasFamiliaResumenDto>> QueryVentasFamiliasAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasFamiliaResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasFamiliaResumenDto
            {
                Familia = GetStringValue(rd, 0),
                TotalVendido = GetDecimal(rd, 1),
                CantidadArticulos = GetInt(rd, 2),
                CantidadComprobantes = GetInt(rd, 3),
                DescripcionFamilia = GetStringValue(rd, 4)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<VentasArticuloResumenDto>> QueryVentasArticulosAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasArticuloResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasArticuloResumenDto
            {
                IdArticulo = GetStringValue(rd, 0),
                Descripcion = GetStringValue(rd, 1),
                CantidadVendida = GetDecimal(rd, 2),
                TotalVendido = GetDecimal(rd, 3),
                CantidadComprobantes = GetInt(rd, 4),
                UltimaVenta = rd.IsDBNull(5) ? null : rd.GetDateTime(5)
            });
        }

        return items;
    }

    private async Task<List<VentasComprobanteResumenDto>> QueryVentasComprobantesAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasComprobanteResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasComprobanteResumenDto
            {
                Tc = GetStringValue(rd, 0),
                IdComprobante = GetStringValue(rd, 1),
                Fecha = rd.IsDBNull(2) ? DateTime.MinValue : rd.GetDateTime(2),
                Cuenta = GetStringValue(rd, 3),
                Cliente = GetStringValue(rd, 4),
                Importe = GetDecimal(rd, 5),
                Usuario = GetStringValue(rd, 6)
            });
        }
        return items;
    }

    private async Task<IReadOnlyList<VentasComprobanteItemDto>> QueryVentasComprobanteItemsAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<VentasComprobanteItemDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new VentasComprobanteItemDto
            {
                IdArticulo = GetStringValue(rd, 0),
                Descripcion = GetStringValue(rd, 1),
                Cantidad = GetDecimal(rd, 2),
                PrecioNeto = GetDecimal(rd, 3),
                TotalConIVA = GetDecimal(rd, 4),
                CostoUnit = GetDecimal(rd, 5),
                Rubro = GetStringValue(rd, 6)
            });
        }
        return items;
    }

    private async Task<IReadOnlyList<StockCriticoDto>> QueryStockCriticosAsync(SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<StockCriticoDto>();
        await using var cmd = new SqlCommand("""
            WITH StockActual AS (
                SELECT s.IDArticulo, SUM(s.Cantidad) AS StockActual
                FROM dbo.V_MV_STOCK s
                WHERE (@FechaHastaExclusive IS NULL OR s.FECHA < @FechaHastaExclusive)
                  AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), s.IDArticulo) LIKE @ArticuloCodigoLike)
                  AND (@Deposito IS NULL OR CONVERT(varchar(50), s.IdDeposito) = @Deposito)
                  AND (@Sucursal IS NULL OR CONVERT(varchar(50), s.UNEGOCIO) = @Sucursal)
                  AND (@Estado IS NULL OR s.Estado = @Estado)
                GROUP BY s.IDArticulo
            )
            SELECT TOP (12)
                CONVERT(varchar(50), a.IDARTICULO),
                a.DESCRIPCION,
                ISNULL(st.StockActual, 0) AS StockActual,
                ISNULL(a.PUNTOPEDIDO, 0) AS PuntoPedido,
                ISNULL(st.StockActual, 0) * ISNULL(a.COSTO, 0) AS Valorizado
            FROM dbo.V_MA_ARTICULOS a
            LEFT JOIN StockActual st ON st.IDArticulo = a.IDARTICULO
            WHERE ISNULL(a.PUNTOPEDIDO, 0) > 0
              AND ISNULL(st.StockActual, 0) <= ISNULL(a.PUNTOPEDIDO, 0)
              AND (@ArticuloCodigoLike IS NULL OR CONVERT(varchar(50), a.IDARTICULO) LIKE @ArticuloCodigoLike)
              AND (@ArticuloDescripcionLike IS NULL OR a.DESCRIPCION LIKE @ArticuloDescripcionLike)
              AND (@Rubro IS NULL OR CONVERT(varchar(50), a.IDRUBRO) = @Rubro)
              AND (@Familia IS NULL OR CONVERT(varchar(50), a.IdFamilia) = @Familia)
            ORDER BY (ISNULL(st.StockActual, 0) - ISNULL(a.PUNTOPEDIDO, 0)) ASC, a.DESCRIPCION
            """, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new StockCriticoDto
            {
                IdArticulo = GetStringValue(rd, 0),
                Descripcion = GetStringValue(rd, 1),
                StockActual = GetDecimal(rd, 2),
                PuntoPedido = GetDecimal(rd, 3),
                Valorizado = GetDecimal(rd, 4)
            });
        }

        return items;
    }

    private async Task<IReadOnlyList<ContabilidadAsientoResumenDto>> QueryAsientosAsync(string sql, SqlConnection cn, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var items = new List<ContabilidadAsientoResumenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new ContabilidadAsientoResumenDto
            {
                Fecha = rd.GetDateTime(0),
                Cuenta = GetStringValue(rd, 1),
                Detalle = GetStringValue(rd, 2),
                Tipo = GetStringValue(rd, 3),
                Importe = GetDecimal(rd, 4)
            });
        }

        return items;
    }

    private async Task<T?> QuerySingleAsync<T>(string sql, SqlConnection cn, Func<SqlDataReader, T> map, Action<SqlCommand>? bind, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? map(rd) : default;
    }

    private static void BindVentasFilters(SqlCommand cmd, VentasDashboardFilters filters)
    {
        BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
        AddNullableString(cmd, "@ClienteLike", Like(filters.Cliente));
        AddNullableString(cmd, "@Usuario", filters.Usuario);
        AddNullableString(cmd, "@Sucursales", JoinCodes(filters.UnidadesNegocio));
        AddNullableString(cmd, "@Deposito", filters.Deposito);
        AddNullableString(cmd, "@TipoComprobante", filters.TipoComprobante);
    }

    /// <summary>
    /// Junta códigos de unidad de negocio en un string separado por comas. No se usa STRING_SPLIT porque
    /// la base activa (ALFANET) tiene compatibility_level 100 (SQL 2008) aunque el motor sea más nuevo;
    /// el filtro compara con el patrón ',@Sucursales,' LIKE '%,codigo,%' en cada WHERE, portable a 2008+.
    /// </summary>
    private static string? JoinCodes(List<string>? codigos)
        => codigos is { Count: > 0 } ? string.Join(",", codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())) : null;

    private static void BindStockFilters(SqlCommand cmd, StockDashboardFilters filters)
    {
        BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
        AddNullableString(cmd, "@ArticuloCodigoLike", Like(filters.ArticuloCodigo));
        AddNullableString(cmd, "@ArticuloDescripcionLike", Like(filters.ArticuloDescripcion));
        AddNullableString(cmd, "@Rubro", filters.Rubro);
        AddNullableString(cmd, "@Familia", filters.Familia);
        AddNullableString(cmd, "@Deposito", filters.Deposito);
        AddNullableString(cmd, "@Sucursal", filters.Sucursal);
        AddNullableString(cmd, "@Estado", filters.Estado);
    }

    private static void BindCajaBancosFilters(SqlCommand cmd, CajaBancosDashboardFilters filters)
    {
        BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
        AddNullableString(cmd, "@Caja", filters.Caja);
        AddNullableString(cmd, "@BancoCuenta", filters.BancoCuenta);
        AddNullableString(cmd, "@TextoLike", Like(filters.Texto));
    }

    private static void BindContabilidadFilters(SqlCommand cmd, ContabilidadDashboardFilters filters)
    {
        BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
        AddNullableString(cmd, "@CuentaLike", Like(filters.CuentaContable));
        AddNullableString(cmd, "@DetalleLike", Like(filters.Detalle));
        AddNullableString(cmd, "@Usuario", filters.Usuario);
        AddNullableString(cmd, "@Sucursal", filters.Sucursal);
        AddNullableString(cmd, "@Tipo", filters.Tipo);
    }

    private static void BindIvaFilters(SqlCommand cmd, ContabilidadDashboardFilters filters)
    {
        BindDateRange(cmd, filters.FechaDesde, filters.FechaHasta);
        AddNullableString(cmd, "@Sucursal", filters.Sucursal);
    }

    private static void BindDateRange(SqlCommand cmd, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        cmd.Parameters.AddWithValue("@FechaDesde", (object?)fechaDesde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaHastaExclusive", fechaHasta.HasValue ? fechaHasta.Value.Date.AddDays(1) : DBNull.Value);
    }

    private static void AddNullableString(SqlCommand cmd, string parameterName, string? value)
        => cmd.Parameters.AddWithValue(parameterName, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static string? Like(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";

    private static string GetStringValue(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;

    private static decimal GetDecimal(SqlDataReader rd, int index) =>
        rd.IsDBNull(index) ? 0 : Convert.ToDecimal(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static int GetInt(SqlDataReader rd, int index) =>
        rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index), CultureInfo.InvariantCulture);

    private async Task<T> ExecuteLoggedAsync<T>(string module, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private static VentasClienteResumenDto WithParticipacion(VentasClienteResumenDto item, decimal total)
        => new()
        {
            Cuenta = item.Cuenta,
            Cliente = item.Cliente,
            TotalFacturado = item.TotalFacturado,
            Participacion = total == 0 ? 0 : item.TotalFacturado / total,
            CantidadComprobantes = item.CantidadComprobantes,
            TicketPromedio = item.TicketPromedio,
            UltimaVenta = item.UltimaVenta
        };

    private static VentasRubroResumenDto WithParticipacion(VentasRubroResumenDto item, decimal total)
        => new()
        {
            Rubro = item.Rubro,
            TotalVendido = item.TotalVendido,
            Participacion = total == 0 ? 0 : item.TotalVendido / total,
            CantidadArticulos = item.CantidadArticulos,
            CantidadComprobantes = item.CantidadComprobantes
        };

    private static VentasFamiliaResumenDto WithParticipacion(VentasFamiliaResumenDto item, decimal total)
        => new()
        {
            Familia = item.Familia,
            DescripcionFamilia = item.DescripcionFamilia,
            TotalVendido = item.TotalVendido,
            Participacion = total == 0 ? 0 : item.TotalVendido / total,
            CantidadArticulos = item.CantidadArticulos,
            CantidadComprobantes = item.CantidadComprobantes
        };
}

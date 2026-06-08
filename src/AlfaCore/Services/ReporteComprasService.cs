using AlfaCore.Configuration;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ReporteComprasService(
    IConfiguration configuration,
    ILogger<ReporteComprasService> logger,
    ISessionService sessionService) : IReporteComprasService
{
    private readonly ILogger<ReporteComprasService> _logger = logger;
    private readonly DatosSqlOptions _sqlOptions =
        configuration.GetSection(DatosSqlOptions.SectionName).Get<DatosSqlOptions>() ?? new();

    private string _connectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // Cláusula WHERE compartida para las queries sobre vw_compras_cabecera_dashboard
    private const string CabeceraWhere = """
        WHERE (@FechaDesde IS NULL OR c.FECHA >= @FechaDesde)
          AND (@FechaHasta IS NULL OR c.FECHA < DATEADD(day, 1, @FechaHasta))
          AND (@Proveedor IS NULL OR c.CUENTA LIKE '%' + @Proveedor + '%' OR c.RAZON_SOCIAL LIKE '%' + @Proveedor + '%')
          AND (@TipoComprobante IS NULL OR c.TC = @TipoComprobante)
        """;

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private SqlCommand CreateCommand(SqlConnection conn, string sql)
    {
        var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = Math.Max(5, _sqlOptions.CommandTimeoutSegundos);
        return cmd;
    }

    private static object ToDb(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private void AddFiltrosParams(SqlCommand cmd, FiltrosReporteCompras f)
    {
        cmd.Parameters.AddWithValue("@FechaDesde", (object?)f.FechaDesde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaHasta", (object?)f.FechaHasta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Proveedor", ToDb(f.Proveedor));
        cmd.Parameters.AddWithValue("@TipoComprobante", ToDb(f.TipoComprobante));
    }

    private static string SafeString(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? string.Empty : r.GetString(ord);
    }

    private static decimal SafeDecimal(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    private static int SafeInt(SqlDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? 0 : r.GetInt32(ord);
    }

    // ── Opciones ─────────────────────────────────────────────────────────────

    public async Task<ReporteComprasOptionsDto> GetOptionsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await OpenConnectionAsync(ct);
            var tcs = new List<string>();
            await using var cmd = CreateCommand(conn,
                "SELECT DISTINCT TC FROM vw_compras_cabecera_dashboard WHERE TC IS NOT NULL AND TC <> '' ORDER BY TC");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var v = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(v)) tcs.Add(v);
            }
            return new ReporteComprasOptionsDto { TiposComprobante = tcs };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar opciones de ReporteCompras");
            return new ReporteComprasOptionsDto();
        }
    }

    // ── Resumen de Compras ────────────────────────────────────────────────────

    public async Task<ResumenComprasDto> GetResumenAsync(FiltrosReporteCompras filtros, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);

        decimal totalComprado = 0, netoTotal = 0, ivaTotal = 0;
        int cantComprobantes = 0, cantProveedores = 0;

        await using (var cmdTot = CreateCommand(conn, $"""
            SELECT
                ISNULL(SUM(c.ImporteDashboard), 0) AS TotalComprado,
                ISNULL(SUM(c.NetoDashboard), 0)    AS NetoTotal,
                ISNULL(SUM(c.IvaDashboard), 0)     AS IvaTotal,
                COUNT(*)                            AS CantidadComprobantes,
                COUNT(DISTINCT c.CUENTA)            AS CantidadProveedores
            FROM vw_compras_cabecera_dashboard c
            {CabeceraWhere}
            """))
        {
            AddFiltrosParams(cmdTot, filtros);
            await using var r = await cmdTot.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                totalComprado    = SafeDecimal(r, "TotalComprado");
                netoTotal        = SafeDecimal(r, "NetoTotal");
                ivaTotal         = SafeDecimal(r, "IvaTotal");
                cantComprobantes = SafeInt(r, "CantidadComprobantes");
                cantProveedores  = SafeInt(r, "CantidadProveedores");
            }
        }

        var divisor = totalComprado == 0 ? 1 : totalComprado;
        var filas = new List<ResumenComprasFilaDto>();

        await using (var cmdFilas = CreateCommand(conn, $"""
            SELECT
                c.CUENTA AS Cuenta,
                COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS RazonSocial,
                COUNT(*)                           AS CantidadComprobantes,
                ISNULL(SUM(c.NetoDashboard), 0)   AS Neto,
                ISNULL(SUM(c.IvaDashboard), 0)    AS Iva,
                ISNULL(SUM(c.ImporteDashboard), 0) AS Total
            FROM vw_compras_cabecera_dashboard c
            {CabeceraWhere}
            GROUP BY c.CUENTA, c.RAZON_SOCIAL
            ORDER BY SUM(c.ImporteDashboard) DESC
            """))
        {
            AddFiltrosParams(cmdFilas, filtros);
            await using var r = await cmdFilas.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var total = SafeDecimal(r, "Total");
                filas.Add(new ResumenComprasFilaDto
                {
                    Cuenta               = SafeString(r, "Cuenta"),
                    RazonSocial          = SafeString(r, "RazonSocial"),
                    CantidadComprobantes = SafeInt(r, "CantidadComprobantes"),
                    Neto                 = SafeDecimal(r, "Neto"),
                    Iva                  = SafeDecimal(r, "Iva"),
                    Total                = total,
                    Participacion        = total / divisor
                });
            }
        }

        return new ResumenComprasDto
        {
            TotalComprado        = totalComprado,
            NetoTotal            = netoTotal,
            IvaTotal             = ivaTotal,
            CantidadComprobantes = cantComprobantes,
            CantidadProveedores  = cantProveedores,
            Filas                = filas
        };
    }

    // ── Detalle de Compras ────────────────────────────────────────────────────

    public async Task<DetalleComprasResultDto> GetDetalleComprasAsync(FiltrosReporteCompras filtros, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        int skip = (filtros.Pagina - 1) * filtros.TamanioPagina;

        int totalRegistros = 0;
        await using (var cmdCount = CreateCommand(conn, $"""
            SELECT COUNT(*)
            FROM vw_compras_cabecera_dashboard c
            {CabeceraWhere}
            """))
        {
            AddFiltrosParams(cmdCount, filtros);
            var result = await cmdCount.ExecuteScalarAsync(ct);
            totalRegistros = Convert.ToInt32(result);
        }

        var items = new List<DetalleComprasFilaDto>();
        await using (var cmdItems = CreateCommand(conn, $"""
            SELECT
                c.FECHA                                         AS Fecha,
                c.TC                                           AS Tc,
                c.IDCOMPROBANTE                                AS IdComprobante,
                c.CUENTA                                       AS Cuenta,
                COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS RazonSocial,
                ISNULL(c.NetoDashboard, 0)                     AS Neto,
                ISNULL(c.IvaDashboard, 0)                      AS Iva,
                ISNULL(c.ImporteDashboard, 0)                  AS Total,
                ISNULL(c.USUARIO, '')                          AS Usuario,
                ISNULL(c.EstadoComprobante, '')                AS EstadoComprobante
            FROM vw_compras_cabecera_dashboard c
            {CabeceraWhere}
            ORDER BY c.FECHA DESC, c.IDCOMPROBANTE DESC
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY
            """))
        {
            AddFiltrosParams(cmdItems, filtros);
            cmdItems.Parameters.AddWithValue("@Skip",     skip);
            cmdItems.Parameters.AddWithValue("@PageSize", filtros.TamanioPagina);
            await using var r = await cmdItems.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                items.Add(new DetalleComprasFilaDto
                {
                    Fecha               = r.GetDateTime(r.GetOrdinal("Fecha")),
                    Tc                  = SafeString(r, "Tc"),
                    IdComprobante       = SafeString(r, "IdComprobante"),
                    Cuenta              = SafeString(r, "Cuenta"),
                    RazonSocial         = SafeString(r, "RazonSocial"),
                    Neto                = SafeDecimal(r, "Neto"),
                    Iva                 = SafeDecimal(r, "Iva"),
                    Total               = SafeDecimal(r, "Total"),
                    Usuario             = SafeString(r, "Usuario"),
                    EstadoComprobante   = SafeString(r, "EstadoComprobante")
                });
            }
        }

        return new DetalleComprasResultDto
        {
            Items          = items,
            TotalRegistros = totalRegistros,
            PaginaActual   = filtros.Pagina,
            TamanioPagina  = filtros.TamanioPagina
        };
    }

    // ── Cuenta Corriente (desde MV_ASIENTOS) ─────────────────────────────────
    //
    // Convención de signo para proveedor:
    //   DEBEHABER = 'H' → el proveedor nos facturó  → suma (positivo / deuda)
    //   DEBEHABER = 'D' → pagamos / nota de crédito → resta (negativo)
    //
    // Nota: los nombres de columna de MV_ASIENTOS pueden variar según la versión
    // de la base. Se asumen: Cuenta, Fecha, TC, IDCOMPROBANTE, DEBEHABER, Importe.
    // La razón social se obtiene de MA_CUENTAS (campo NOMBRE o RAZON_SOCIAL).

    public async Task<CuentaCorrienteResultDto> GetCuentaCorrienteAsync(FiltrosReporteCompras filtros, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(ct);
        int skip = (filtros.Pagina - 1) * filtros.TamanioPagina;

        const string ccWhere = """
            WHERE (@FechaDesde IS NULL OR a.Fecha >= @FechaDesde)
              AND (@FechaHasta IS NULL OR a.Fecha < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR a.Cuenta LIKE '%' + @Proveedor + '%'
                                     OR mc.NOMBRE LIKE '%' + @Proveedor + '%')
              AND (@TipoComprobante IS NULL OR a.TC = @TipoComprobante)
            """;

        int totalRegistros = 0;
        decimal saldoTotal = 0;

        await using (var cmdResumen = CreateCommand(conn, $"""
            SELECT
                COUNT(*) AS Total,
                ISNULL(SUM(CASE WHEN a.DEBEHABER = 'H'
                                THEN ISNULL(a.Importe, 0)
                                ELSE -ISNULL(a.Importe, 0) END), 0) AS SaldoTotal
            FROM MV_ASIENTOS a
            LEFT JOIN MA_CUENTAS mc ON mc.CUENTA = a.Cuenta
            {ccWhere}
            """))
        {
            AddFiltrosParams(cmdResumen, filtros);
            await using var r = await cmdResumen.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                totalRegistros = SafeInt(r, "Total");
                saldoTotal     = SafeDecimal(r, "SaldoTotal");
            }
        }

        var items = new List<CuentaCorrienteFilaDto>();
        await using (var cmdItems = CreateCommand(conn, $"""
            SELECT
                a.Cuenta,
                COALESCE(NULLIF(mc.NOMBRE, ''), a.Cuenta)            AS RazonSocial,
                a.Fecha,
                ISNULL(a.TC, '')                                     AS Tc,
                ISNULL(a.IDCOMPROBANTE, '')                          AS IdComprobante,
                ISNULL(a.DEBEHABER, '')                              AS DebeHaber,
                ISNULL(a.Importe, 0)                                 AS Importe,
                CASE WHEN a.DEBEHABER = 'H'
                     THEN  ISNULL(a.Importe, 0)
                     ELSE -ISNULL(a.Importe, 0) END                  AS ImporteSignado
            FROM MV_ASIENTOS a
            LEFT JOIN MA_CUENTAS mc ON mc.CUENTA = a.Cuenta
            {ccWhere}
            ORDER BY a.Cuenta, a.Fecha, a.IDCOMPROBANTE
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY
            """))
        {
            AddFiltrosParams(cmdItems, filtros);
            cmdItems.Parameters.AddWithValue("@Skip",     skip);
            cmdItems.Parameters.AddWithValue("@PageSize", filtros.TamanioPagina);
            await using var r = await cmdItems.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                items.Add(new CuentaCorrienteFilaDto
                {
                    Cuenta         = SafeString(r, "Cuenta"),
                    RazonSocial    = SafeString(r, "RazonSocial"),
                    Fecha          = r.GetDateTime(r.GetOrdinal("Fecha")),
                    Tc             = SafeString(r, "Tc"),
                    IdComprobante  = SafeString(r, "IdComprobante"),
                    DebeHaber      = SafeString(r, "DebeHaber"),
                    Importe        = SafeDecimal(r, "Importe"),
                    ImporteSignado = SafeDecimal(r, "ImporteSignado")
                });
            }
        }

        return new CuentaCorrienteResultDto
        {
            Items          = items,
            TotalRegistros = totalRegistros,
            PaginaActual   = filtros.Pagina,
            TamanioPagina  = filtros.TamanioPagina,
            SaldoTotal     = saldoTotal
        };
    }
}

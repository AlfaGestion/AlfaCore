using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed partial class ComprasDashboardService
{
    public async Task<ComprobantesOverviewDto> GetComprobantesOverviewAsync(ComprobantesFilter filter, CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("GetComprobantesOverview", filter, async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var resumen = await ReadSingleAsync(connection, $"""
                SELECT
                    ISNULL(SUM(c.ImporteDashboard), 0) AS TotalComprado,
                    COUNT(*) AS CantidadComprobantes,
                    COUNT(DISTINCT c.CUENTA) AS ProveedoresActivos,
                    ISNULL(AVG(CAST(c.ImporteDashboard AS decimal(18,2))), 0) AS TicketPromedio,
                    SUM(CASE WHEN c.ImporteDashboard = 0 THEN 1 ELSE 0 END) AS ComprobantesEnCero,
                    SUM(CASE WHEN ISNULL(det.CantidadItems, 0) = 0 THEN 1 ELSE 0 END) AS ComprobantesSinDetalle,
                    SUM(CASE WHEN c.IvaDashboard = 0 AND c.ImporteDashboard > 0 THEN 1 ELSE 0 END) AS ComprobantesIvaCero,
                    ISNULL(MAX(c.ImporteDashboard), 0) AS ComprobanteMaximo
                {HeaderWithDetailFromClause};
                """, filter, reader => new
            {
                TotalComprado = reader.GetDecimal("TotalComprado"),
                CantidadComprobantes = reader.GetInt32("CantidadComprobantes"),
                ProveedoresActivos = reader.GetInt32("ProveedoresActivos"),
                TicketPromedio = reader.GetDecimal("TicketPromedio"),
                ComprobantesEnCero = reader.GetInt32("ComprobantesEnCero"),
                ComprobantesSinDetalle = reader.GetInt32("ComprobantesSinDetalle"),
                ComprobantesIvaCero = reader.GetInt32("ComprobantesIvaCero"),
                ComprobanteMaximo = reader.GetDecimal("ComprobanteMaximo")
            }, cancellationToken);

            var totalImporte = resumen?.TotalComprado ?? 0m;
            var totalComprobantes = resumen?.CantidadComprobantes ?? 0;

            var composicionTipos = await GetCategoryTotalsAsync(connection, filter, $"""
                SELECT TOP (6)
                    c.TC AS Categoria,
                    c.TC AS Codigo,
                    SUM(c.ImporteDashboard) AS Total
                {HeaderFromClause}
                GROUP BY c.TC
                ORDER BY SUM(c.ImporteDashboard) DESC;
                """, totalImporte == 0 ? 1 : totalImporte, cancellationToken);

            var predominante = await ReadSingleAsync(connection, $"""
                SELECT TOP (1)
                    c.TC AS Categoria,
                    COUNT(*) AS Cantidad
                {HeaderFromClause}
                GROUP BY c.TC
                ORDER BY COUNT(*) DESC, SUM(c.ImporteDashboard) DESC;
                """, filter, reader => new
            {
                Categoria = reader.SafeGetString("Categoria"),
                Cantidad = reader.GetInt32("Cantidad")
            }, cancellationToken);

            var evolucionSemanal = await ReadListAsync(connection, $"""
                SELECT TOP (12)
                    CONVERT(varchar(4), YEAR(c.FECHA)) + '-S' + RIGHT('00' + CONVERT(varchar(2), DATEPART(ISO_WEEK, c.FECHA)), 2) AS Periodo,
                    SUM(c.ImporteDashboard) AS Total
                {HeaderFromClause}
                GROUP BY YEAR(c.FECHA), DATEPART(ISO_WEEK, c.FECHA)
                ORDER BY YEAR(c.FECHA) DESC, DATEPART(ISO_WEEK, c.FECHA) DESC;
                """, filter, reader => new MonthlyPointDto
            {
                Periodo = reader.SafeGetString("Periodo"),
                Total = reader.GetDecimal("Total")
            }, cancellationToken);

            var topComprobantes = await GetCategoryTotalsAsync(connection, filter, $"""
                SELECT TOP (8)
                    ISNULL(c.TC, '') + ' ' + ISNULL(c.IDCOMPROBANTE, '') + ' · ' + ISNULL(COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA), '') AS Categoria,
                    ISNULL(c.TC, '') + '|' + ISNULL(c.IDCOMPROBANTE, '') + '|' + ISNULL(c.CUENTA, '') AS Codigo,
                    c.ImporteDashboard AS Total
                {HeaderFromClause}
                ORDER BY c.ImporteDashboard DESC, c.FECHA DESC;
                """, totalImporte == 0 ? 1 : totalImporte, cancellationToken);

            var alertas = BuildComprobantesAlerts(
                resumen?.ComprobantesEnCero ?? 0,
                resumen?.ComprobantesSinDetalle ?? 0,
                resumen?.ComprobantesIvaCero ?? 0,
                resumen?.ComprobanteMaximo ?? 0m,
                totalImporte,
                predominante?.Categoria,
                predominante?.Cantidad ?? 0,
                totalComprobantes);

            return new ComprobantesOverviewDto
            {
                TotalComprado = resumen?.TotalComprado ?? 0m,
                CantidadComprobantes = totalComprobantes,
                ProveedoresActivos = resumen?.ProveedoresActivos ?? 0,
                TicketPromedio = resumen?.TicketPromedio ?? 0m,
                ComprobantesEnCero = resumen?.ComprobantesEnCero ?? 0,
                ComprobantesSinDetalle = resumen?.ComprobantesSinDetalle ?? 0,
                ComprobantesIvaCero = resumen?.ComprobantesIvaCero ?? 0,
                ComprobanteMaximo = resumen?.ComprobanteMaximo ?? 0m,
                TcPredominante = predominante?.Categoria ?? "Sin datos",
                TcPredominanteParticipacion = totalComprobantes > 0 ? (predominante?.Cantidad ?? 0) / (decimal)totalComprobantes : 0m,
                EvolucionSemanal = evolucionSemanal.Reverse<MonthlyPointDto>().ToList(),
                ComposicionTipos = composicionTipos,
                TopComprobantes = topComprobantes,
                Alertas = alertas
            };
        });
    }

    public async Task<ComprobantesResultDto> GetComprobantesAsync(ComprobantesFilter filter, CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("GetComprobantes", filter, async () =>
        {
            filter.Pagina = Math.Max(1, filter.Pagina);
            filter.TamanioPagina = filter.TamanioPagina is <= 0 or > 100 ? 20 : filter.TamanioPagina;

            await using var connection = await OpenConnectionAsync(cancellationToken);
            var sql = $"""
                SELECT COUNT(*)
                {HeaderFromClause};

                WITH comprobantes_paginados AS (
                    SELECT
                        c.TC, c.IDCOMPROBANTE, c.NUMERO, c.FECHA, c.CUENTA, c.RAZON_SOCIAL,
                        c.SUCURSAL, CONVERT(varchar(20), c.IdDeposito) AS Deposito, c.USUARIO,
                        c.NetoDashboard, c.IvaDashboard, c.ImporteDashboard, c.EstadoComprobante,
                        ISNULL(det.CantidadItems, 0) AS CantidadItems,
                        CAST(CASE WHEN ISNULL(det.CantidadItems, 0) > 0 THEN 1 ELSE 0 END AS bit) AS TieneDetalle,
                        CAST(CASE WHEN ISNULL(det.CantidadItems, 0) = 0 THEN 1 ELSE 0 END AS bit) AS EsContable,
                        CAST(CASE WHEN c.IvaDashboard = 0 AND c.ImporteDashboard > 0 THEN 1 ELSE 0 END AS bit) AS IvaEnCero,
                        CASE
                            WHEN c.ImporteDashboard = 0 THEN 'En cero'
                            WHEN ISNULL(det.CantidadItems, 0) = 0 THEN 'Sin detalle'
                            WHEN c.IvaDashboard = 0 AND c.ImporteDashboard > 0 THEN 'IVA 0'
                            ELSE ''
                        END AS AlertaOperativa,
                        ROW_NUMBER() OVER (ORDER BY c.FECHA DESC, c.IDCOMPROBANTE DESC) AS rn
                    {HeaderWithDetailFromClause}
                )
                SELECT
                    TC, IDCOMPROBANTE, NUMERO, FECHA, CUENTA, RAZON_SOCIAL,
                    SUCURSAL, Deposito, USUARIO,
                    NetoDashboard, IvaDashboard, ImporteDashboard, EstadoComprobante,
                    CantidadItems, TieneDetalle, EsContable, IvaEnCero, AlertaOperativa
                FROM comprobantes_paginados
                WHERE rn BETWEEN @Offset + 1 AND @Offset + @PageSize
                ORDER BY rn;
                """;

            await using var command = BuildCommand(connection, sql, filter);
            command.Parameters.AddWithValue("@Offset", (filter.Pagina - 1) * filter.TamanioPagina);
            command.Parameters.AddWithValue("@PageSize", filter.TamanioPagina);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var total = 0;
            if (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt32(0);
            }

            await reader.NextResultAsync(cancellationToken);
            var items = new List<ComprobanteDto>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapComprobante(reader));
            }

            _logger.LogInformation(
                "Comprobantes: {TotalRegistros} registros totales, página {Pagina}, tamaño {TamanioPagina}.",
                total,
                filter.Pagina,
                filter.TamanioPagina);

            return new ComprobantesResultDto
            {
                Items = items,
                TotalRegistros = total,
                PaginaActual = filter.Pagina,
                TamanioPagina = filter.TamanioPagina
            };
        });
    }

    public async Task<ComprobanteDetalleDto?> GetComprobanteDetalleAsync(string tc, string idComprobante, string cuenta, CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("GetComprobanteDetalle", null, async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var cabecera = await ReadSingleAsync(connection, """
                SELECT
                    TC, IDCOMPROBANTE, NUMERO, FECHA, CUENTA, RAZON_SOCIAL,
                    SUCURSAL, CONVERT(varchar(20), IdDeposito) AS Deposito, USUARIO,
                    NetoDashboard, IvaDashboard, ImporteDashboard, EstadoComprobante,
                    CAST(0 AS int) AS CantidadItems,
                    CAST(0 AS bit) AS TieneDetalle,
                    CAST(0 AS bit) AS EsContable,
                    CAST(0 AS bit) AS IvaEnCero,
                    CAST('' AS varchar(50)) AS AlertaOperativa
                FROM vw_compras_cabecera_dashboard
                WHERE TC = @TC AND IDCOMPROBANTE = @IdComprobante AND CUENTA = @Cuenta;
                """, cmd =>
            {
                cmd.Parameters.AddWithValue("@TC", tc);
                cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
                cmd.Parameters.AddWithValue("@Cuenta", cuenta);
            }, MapComprobante, cancellationToken);

            if (cabecera is null)
            {
                _logger.LogWarning(
                    "No se encontró detalle para comprobante TC={TC}, ID={IdComprobante}, Cuenta={Cuenta}.",
                    tc,
                    idComprobante,
                    cuenta);
                return null;
            }

            var items = await ReadListAsync(connection, """
                SELECT
                    IDARTICULO,
                    COALESCE(NULLIF(DESCRIPCION_ARTICULO, ''), DESCRIPCION_ITEM, IDARTICULO) AS DescripcionArticulo,
                    COALESCE(RUBRO, 'Sin rubro') AS Rubro,
                    COALESCE(FAMILIA, 'Sin familia') AS Familia,
                    CantidadDashboard,
                    COSTO,
                    TotalDashboard
                FROM vw_compras_detalle_dashboard
                WHERE TC = @TC AND IDCOMPROBANTE = @IdComprobante AND CUENTA = @Cuenta
                ORDER BY DescripcionArticulo;
                """, cmd =>
            {
                cmd.Parameters.AddWithValue("@TC", tc);
                cmd.Parameters.AddWithValue("@IdComprobante", idComprobante);
                cmd.Parameters.AddWithValue("@Cuenta", cuenta);
            }, reader => new ComprobanteItemDto
            {
                IdArticulo = reader.SafeGetString("IDARTICULO"),
                DescripcionArticulo = reader.SafeGetString("DescripcionArticulo"),
                Rubro = reader.SafeGetString("Rubro"),
                Familia = reader.SafeGetString("Familia"),
                Cantidad = reader.GetDecimal("CantidadDashboard"),
                Costo = reader.GetDecimal("COSTO"),
                Total = reader.GetDecimal("TotalDashboard")
            }, cancellationToken);

            _logger.LogInformation(
                "Detalle de comprobante cargado. TC={TC}, ID={IdComprobante}, Cuenta={Cuenta}, Ítems={CantidadItems}.",
                tc,
                idComprobante,
                cuenta,
                items.Count);

            var cabeceraConIndicadores = new ComprobanteDto
            {
                Tc = cabecera.Tc,
                IdComprobante = cabecera.IdComprobante,
                Numero = cabecera.IdComprobante,
                Fecha = cabecera.Fecha,
                Cuenta = cabecera.Cuenta,
                RazonSocial = cabecera.RazonSocial,
                Sucursal = cabecera.Sucursal,
                Deposito = cabecera.Deposito,
                Usuario = cabecera.Usuario,
                NetoDashboard = cabecera.NetoDashboard,
                IvaDashboard = cabecera.IvaDashboard,
                ImporteDashboard = cabecera.ImporteDashboard,
                EstadoComprobante = cabecera.EstadoComprobante,
                CantidadItems = items.Count,
                TieneDetalle = items.Count > 0,
                EsContable = items.Count == 0,
                IvaEnCero = cabecera.IvaDashboard == 0 && cabecera.ImporteDashboard > 0,
                AlertaOperativa = cabecera.ImporteDashboard == 0
                    ? "En cero"
                    : items.Count == 0
                        ? "Sin detalle"
                        : cabecera.IvaDashboard == 0 && cabecera.ImporteDashboard > 0
                            ? "IVA 0"
                            : string.Empty
            };

            return new ComprobanteDetalleDto
            {
                Cabecera = cabeceraConIndicadores,
                Items = items
            };
        });
    }

    private static IReadOnlyList<ComprobantesAlertDto> BuildComprobantesAlerts(
        int comprobantesEnCero,
        int comprobantesSinDetalle,
        int comprobantesIvaCero,
        decimal comprobanteMaximo,
        decimal totalImporte,
        string? tcPredominante,
        int cantidadPredominante,
        int totalComprobantes)
    {
        var items = new List<ComprobantesAlertDto>();

        if (comprobantesEnCero > 0)
        {
            items.Add(new ComprobantesAlertDto
            {
                Tipo = "warn",
                Titulo = "Comprobantes en cero",
                Descripcion = $"{comprobantesEnCero} registros con importe total 0."
            });
        }

        if (comprobantesSinDetalle > 0)
        {
            items.Add(new ComprobantesAlertDto
            {
                Tipo = "info",
                Titulo = "Sin detalle",
                Descripcion = $"{comprobantesSinDetalle} comprobantes no tienen artículos asociados."
            });
        }

        if (comprobantesIvaCero > 0)
        {
            items.Add(new ComprobantesAlertDto
            {
                Tipo = "danger",
                Titulo = "IVA en cero",
                Descripcion = $"{comprobantesIvaCero} comprobantes con total positivo e IVA 0."
            });
        }

        if (totalImporte > 0 && comprobanteMaximo > 0 && (comprobanteMaximo / totalImporte) >= 0.20m)
        {
            items.Add(new ComprobantesAlertDto
            {
                Tipo = "success",
                Titulo = "Importe concentrado",
                Descripcion = $"El comprobante más alto representa {(comprobanteMaximo / totalImporte):P1} del total."
            });
        }

        if (!string.IsNullOrWhiteSpace(tcPredominante) && totalComprobantes > 0)
        {
            items.Add(new ComprobantesAlertDto
            {
                Tipo = "info",
                Titulo = "TC predominante",
                Descripcion = $"{tcPredominante} concentra {(cantidadPredominante / (decimal)totalComprobantes):P1} de los comprobantes."
            });
        }

        return items.Take(4).ToList();
    }

}

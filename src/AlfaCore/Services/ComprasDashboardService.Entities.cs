using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ComprasDashboardService
{
    public async Task<ProveedoresPageDto> GetProveedoresPageDataAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("GetProveedoresPage", filters, async () =>
        {
            var proveedoresTask = GetProveedoresAsync(filters, cancellationToken);
            var anteriorTask   = GetProveedoresTotalesAsync(ComputePriorPeriodFilters(filters), cancellationToken);
            await Task.WhenAll(proveedoresTask, anteriorTask);

            var proveedores    = proveedoresTask.Result;
            var totalesAnterior = anteriorTask.Result;

            if (proveedores.Count == 0)
                return new ProveedoresPageDto();

            var totalGeneral  = proveedores.Sum(p => p.TotalComprado);
            var totalAnterior = totalesAnterior.Values.Sum();

            var enriched = proveedores.Select(p =>
            {
                var participacion = totalGeneral > 0 ? p.TotalComprado / totalGeneral : 0m;
                var esNuevo       = !totalesAnterior.ContainsKey(p.Cuenta);
                decimal? variacion = null;
                if (!esNuevo && totalesAnterior.TryGetValue(p.Cuenta, out var ant) && ant > 0)
                    variacion = (p.TotalComprado - ant) / ant;

                return new ProveedorResumenDto
                {
                    Cuenta               = p.Cuenta,
                    RazonSocial          = p.RazonSocial,
                    TotalComprado        = p.TotalComprado,
                    Participacion        = participacion,
                    CantidadComprobantes = p.CantidadComprobantes,
                    TicketPromedio       = p.TicketPromedio,
                    UltimaCompra         = p.UltimaCompra,
                    PrimeraCompra        = p.PrimeraCompra,
                    VariacionVsAnterior  = variacion,
                    EsNuevo              = esNuevo
                };
            }).ToList();

            var top5Total    = enriched.Take(5).Sum(p => p.TotalComprado);
            var varTotal     = totalAnterior > 0 ? (totalGeneral - totalAnterior) / totalAnterior : (decimal?)null;
            var conVariacion = enriched.Where(p => p.VariacionVsAnterior.HasValue).ToList();
            var mayorCaida   = conVariacion.Count > 0 ? conVariacion.MinBy(p => p.VariacionVsAnterior) : null;
            var mayorCrec    = conVariacion.Count > 0 ? conVariacion.MaxBy(p => p.VariacionVsAnterior) : null;

            return new ProveedoresPageDto
            {
                Kpis = new ProveedoresKpiDto
                {
                    TotalComprado            = totalGeneral,
                    ProveedoresActivos       = enriched.Count,
                    TopProveedorNombre       = enriched[0].RazonSocial,
                    TopProveedorTotal        = enriched[0].TotalComprado,
                    ConcentracionTop5        = totalGeneral > 0 ? top5Total / totalGeneral : 0m,
                    VariacionTotalVsAnterior = varTotal,
                    MayorCaidaNombre         = mayorCaida?.VariacionVsAnterior < -0.05m ? mayorCaida.RazonSocial : null,
                    MayorCaidaVariacion      = mayorCaida?.VariacionVsAnterior < -0.05m ? mayorCaida.VariacionVsAnterior : null,
                    MayorCrecimientoNombre   = mayorCrec?.VariacionVsAnterior > 0.05m ? mayorCrec.RazonSocial : null,
                    MayorCrecimientoVariacion = mayorCrec?.VariacionVsAnterior > 0.05m ? mayorCrec.VariacionVsAnterior : null,
                },
                Proveedores = enriched
            };
        });
    }

    public async Task<IReadOnlyList<ProveedorResumenDto>> GetProveedoresAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var sql = $"""
            SELECT
                c.CUENTA,
                COALESCE(NULLIF(c.RAZON_SOCIAL, ''), c.CUENTA) AS RAZON_SOCIAL,
                SUM(c.ImporteDashboard) AS TotalComprado,
                COUNT(*) AS CantidadComprobantes,
                AVG(CAST(c.ImporteDashboard AS decimal(18,2))) AS TicketPromedio,
                MAX(c.FECHA) AS UltimaCompra,
                MIN(c.FECHA) AS PrimeraCompra
            {HeaderFromClause}
            GROUP BY c.CUENTA, c.RAZON_SOCIAL
            ORDER BY SUM(c.ImporteDashboard) DESC;
            """;

        return await ReadListAsync(connection, sql, filters, reader => new ProveedorResumenDto
        {
            Cuenta               = reader.SafeGetString("CUENTA"),
            RazonSocial          = reader.SafeGetString("RAZON_SOCIAL"),
            TotalComprado        = reader.GetDecimal("TotalComprado"),
            CantidadComprobantes = reader.GetInt32("CantidadComprobantes"),
            TicketPromedio       = reader.GetDecimal("TicketPromedio"),
            UltimaCompra         = reader.GetNullableDateTime("UltimaCompra"),
            PrimeraCompra        = reader.GetNullableDateTime("PrimeraCompra")
        }, cancellationToken);
    }

    private static DashboardFilters ComputePriorPeriodFilters(DashboardFilters filters)
    {
        var today  = DateTime.Today;
        var desde  = filters.FechaDesde ?? new DateTime(today.Year, today.Month, 1);
        var hasta  = filters.FechaHasta ?? today;
        var span   = hasta - desde;
        var antHasta = desde.AddDays(-1);
        var antDesde = antHasta - span;
        var prior    = filters.Clone();
        prior.FechaDesde = antDesde;
        prior.FechaHasta = antHasta;
        return prior;
    }

    private async Task<Dictionary<string, decimal>> GetProveedoresTotalesAsync(DashboardFilters filters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var sql = $"""
            SELECT c.CUENTA, SUM(c.ImporteDashboard) AS Total
            {HeaderFromClause}
            GROUP BY c.CUENTA;
            """;
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using var command = BuildCommand(connection, sql, filters);
        await using var reader  = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var cuenta = reader.SafeGetString("CUENTA");
            if (!string.IsNullOrEmpty(cuenta))
                result[cuenta] = reader.GetDecimal("Total");
        }
        return result;
    }

    public async Task<ProveedorDetalleDto?> GetProveedorDetalleAsync(string cuenta, DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        var scoped = filters.Clone();
        scoped.Proveedor = cuenta;
        var resumen = (await GetProveedoresAsync(scoped, cancellationToken)).FirstOrDefault();
        if (resumen is null)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var total = resumen.TotalComprado == 0 ? 1 : resumen.TotalComprado;
        var evolutionFilters = scoped.WithoutDates();
        evolutionFilters.FechaDesde = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-11);
        evolutionFilters.FechaHasta = DateTime.Today;
        var evolucionMensual = await GetMonthlySeriesAsync(connection, evolutionFilters, "SUM(c.ImporteDashboard)", HeaderFromClause, "c.FECHA", cancellationToken);

        return new ProveedorDetalleDto
        {
            Resumen = resumen,
            TopArticulos = await GetCategoryTotalsAsync(connection, scoped, $"""
                SELECT TOP (8)
                    COALESCE(NULLIF(d.DESCRIPCION_ARTICULO, ''), d.IDARTICULO) AS Categoria,
                    d.IDARTICULO AS Codigo,
                    SUM(d.TotalDashboard) AS Total
                {DetailFromClause}
                GROUP BY d.IDARTICULO, d.DESCRIPCION_ARTICULO
                ORDER BY SUM(d.TotalDashboard) DESC;
                """, total, cancellationToken),
            UltimosComprobantes = await ReadListAsync(connection, $"""
                SELECT TOP (8)
                    c.TC, c.IDCOMPROBANTE, c.NUMERO, c.FECHA, c.CUENTA, c.RAZON_SOCIAL,
                    c.SUCURSAL, CONVERT(varchar(20), c.IdDeposito) AS Deposito, c.USUARIO,
                    c.NetoDashboard, c.IvaDashboard, c.ImporteDashboard, c.EstadoComprobante
                {HeaderFromClause}
                ORDER BY c.FECHA DESC, c.IDCOMPROBANTE DESC;
                """, scoped, MapComprobante, cancellationToken),
            EvolucionMensual = NormalizeLast12Months(evolucionMensual)
        };
    }

    public async Task<RubrosPageDto> GetRubrosPageDataAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var totalGeneral = await GetTotalFromDetailAsync(connection, filters, cancellationToken);
        var priorFilters = ComputePriorPeriodFilters(filters);
        var totalAnterior = await GetTotalFromDetailAsync(connection, priorFilters, cancellationToken);
        var divisor = totalGeneral == 0 ? 1 : totalGeneral;

        var currentRows = await ReadListAsync(connection, $"""
            SELECT
                COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro') AS Rubro,
                SUM(d.TotalDashboard) AS TotalComprado,
                COUNT(DISTINCT d.IDARTICULO) AS CantidadArticulos,
                COUNT(DISTINCT (ISNULL(d.TC, '') + '|' + ISNULL(d.IDCOMPROBANTE, '') + '|' + ISNULL(d.CUENTA, ''))) AS CantidadComprobantes,
                MAX(d.FECHA) AS UltimaCompra
            {DetailFromClause}
            GROUP BY COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro')
            ORDER BY SUM(d.TotalDashboard) DESC;
            """, filters, reader => new RubroResumenDto
        {
            Rubro = reader.SafeGetString("Rubro"),
            TotalComprado = reader.GetDecimal("TotalComprado"),
            CantidadArticulos = reader.GetInt32("CantidadArticulos"),
            CantidadComprobantes = reader.GetInt32("CantidadComprobantes"),
            UltimaCompra = reader.GetNullableDateTime("UltimaCompra")
        }, cancellationToken);

        var priorRows = await ReadListAsync(connection, $"""
            SELECT
                COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro') AS Rubro,
                SUM(d.TotalDashboard) AS TotalAnterior
            {DetailFromClause}
            GROUP BY COALESCE(NULLIF(d.RUBRO, ''), 'Sin rubro');
            """, priorFilters, reader => new
        {
            Rubro = reader.SafeGetString("Rubro"),
            TotalAnterior = reader.GetDecimal("TotalAnterior")
        }, cancellationToken);

        var priorLookup = priorRows.ToDictionary(x => x.Rubro, x => x.TotalAnterior, StringComparer.OrdinalIgnoreCase);

        var rubros = currentRows.Select(item =>
        {
            priorLookup.TryGetValue(item.Rubro, out var totalAnteriorRubro);
            decimal? variacion = null;
            if (totalAnteriorRubro > 0)
            {
                variacion = (item.TotalComprado - totalAnteriorRubro) / totalAnteriorRubro;
            }

            return new RubroResumenDto
            {
                Rubro = item.Rubro,
                TotalComprado = item.TotalComprado,
                Participacion = item.TotalComprado / divisor,
                CantidadArticulos = item.CantidadArticulos,
                CantidadComprobantes = item.CantidadComprobantes,
                TotalAnterior = totalAnteriorRubro > 0 ? totalAnteriorRubro : null,
                VariacionVsAnterior = variacion,
                TicketPromedio = item.CantidadComprobantes > 0 ? item.TotalComprado / item.CantidadComprobantes : 0m,
                UltimaCompra = item.UltimaCompra
            };
        }).OrderByDescending(x => x.TotalComprado).ToList();

        var conVariacion = rubros.Where(x => x.VariacionVsAnterior.HasValue).ToList();
        var conCrecimiento = conVariacion.Where(x => x.VariacionVsAnterior > 0).OrderByDescending(x => x.VariacionVsAnterior).ToList();
        var conCaida = conVariacion.Where(x => x.VariacionVsAnterior < 0).OrderBy(x => x.VariacionVsAnterior).ToList();
        var rubroPrincipal = rubros.FirstOrDefault();
        var concentracionTop3 = rubros.Take(3).Sum(x => x.Participacion);
        var variacionTotal = totalAnterior > 0 ? (totalGeneral - totalAnterior) / totalAnterior : (decimal?)null;

        return new RubrosPageDto
        {
            Kpis = new RubrosKpiDto
            {
                TotalComprado = totalGeneral,
                CantidadRubrosActivos = rubros.Count,
                RubroPrincipal = rubroPrincipal?.Rubro ?? "Sin datos",
                ParticipacionRubroPrincipal = rubroPrincipal?.Participacion ?? 0m,
                VariacionTotalVsAnterior = variacionTotal,
                RubroMayorCrecimiento = conCrecimiento.FirstOrDefault()?.Rubro ?? "Sin datos",
                RubroMayorCrecimientoVariacion = conCrecimiento.FirstOrDefault()?.VariacionVsAnterior,
                RubroMayorCaida = conCaida.FirstOrDefault()?.Rubro ?? "Sin datos",
                RubroMayorCaidaVariacion = conCaida.FirstOrDefault()?.VariacionVsAnterior,
                ConcentracionTop3 = concentracionTop3
            },
            Rubros = rubros,
            DistribucionGasto = rubros.Take(8).Select(x => new CategoryTotalDto
            {
                Categoria = x.Rubro,
                Codigo = x.Rubro,
                Total = x.Participacion
            }).ToList(),
            TopRubros = rubros.Take(8).Select(x => new CategoryTotalDto
            {
                Categoria = x.Rubro,
                Codigo = x.Rubro,
                Total = x.TotalComprado
            }).ToList(),
            VariacionesPositivas = conCrecimiento.Take(8).Select(x => new CategoryTotalDto
            {
                Categoria = x.Rubro,
                Codigo = x.Rubro,
                Total = x.VariacionVsAnterior ?? 0m
            }).ToList(),
            VariacionesNegativas = conCaida.Take(8).Select(x => new CategoryTotalDto
            {
                Categoria = x.Rubro,
                Codigo = x.Rubro,
                Total = Math.Abs(x.VariacionVsAnterior ?? 0m)
            }).ToList(),
            ConcentracionTop3VsResto =
            [
                new CategoryTotalDto { Categoria = "Top 3 rubros", Codigo = "TOP3", Total = concentracionTop3 },
                new CategoryTotalDto { Categoria = "Resto", Codigo = "RESTO", Total = Math.Max(0m, 1m - concentracionTop3) }
            ],
            Insights = BuildRubrosInsights(rubros, rubroPrincipal, concentracionTop3)
        };
    }

    public async Task<IReadOnlyList<RubroResumenDto>> GetRubrosAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
        => (await GetRubrosPageDataAsync(filters, cancellationToken)).Rubros;

    public async Task<RubroDetalleDto?> GetRubroDetalleAsync(string rubro, DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        var scoped = filters.Clone();
        scoped.Rubro = rubro;
        var resumen = (await GetRubrosPageDataAsync(scoped, cancellationToken)).Rubros.FirstOrDefault();
        if (resumen is null)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var totalRubro = resumen.TotalComprado == 0 ? 1 : resumen.TotalComprado;

        var ultimosComprobantes = await ReadListAsync(connection, $"""
            SELECT TOP (10)
                d.TC,
                d.IDCOMPROBANTE,
                MAX(d.FECHA) AS Fecha,
                d.CUENTA,
                MAX(COALESCE(NULLIF(d.RAZON_SOCIAL, ''), d.CUENTA)) AS RazonSocial,
                SUM(d.TotalDashboard) AS ImporteDashboard
            {DetailFromClause}
            GROUP BY d.TC, d.IDCOMPROBANTE, d.CUENTA
            ORDER BY MAX(d.FECHA) DESC, SUM(d.TotalDashboard) DESC;
            """, scoped, reader => new ComprobanteDto
        {
            Tc = reader.SafeGetString("TC"),
            IdComprobante = reader.SafeGetString("IDCOMPROBANTE"),
            Numero = reader.SafeGetString("IDCOMPROBANTE"),
            Fecha = reader.GetDateTime("Fecha"),
            Cuenta = reader.SafeGetString("CUENTA"),
            RazonSocial = reader.SafeGetString("RazonSocial"),
            ImporteDashboard = reader.GetDecimal("ImporteDashboard")
        }, cancellationToken);

        return new RubroDetalleDto
        {
            Resumen = resumen,
            EvolucionMensual = await GetMonthlySeriesAsync(connection, scoped, "SUM(d.TotalDashboard)", DetailFromClause, "d.FECHA", cancellationToken),
            TopArticulos = await GetCategoryTotalsAsync(connection, scoped, $"""
                SELECT TOP (10)
                    COALESCE(NULLIF(d.DESCRIPCION_ARTICULO, ''), d.IDARTICULO) AS Categoria,
                    d.IDARTICULO AS Codigo,
                    SUM(d.TotalDashboard) AS Total
                {DetailFromClause}
                GROUP BY d.IDARTICULO, d.DESCRIPCION_ARTICULO
                ORDER BY SUM(d.TotalDashboard) DESC;
                """, totalRubro, cancellationToken),
            UltimosComprobantes = ultimosComprobantes
        };
    }

    private static IReadOnlyList<RubrosInsightDto> BuildRubrosInsights(
        IReadOnlyList<RubroResumenDto> rubros,
        RubroResumenDto? rubroPrincipal,
        decimal concentracionTop3)
    {
        var insights = new List<RubrosInsightDto>();

        if (rubroPrincipal is not null && rubroPrincipal.Participacion >= 0.20m)
        {
            insights.Add(new RubrosInsightDto
            {
                Tipo = "info",
                Mensaje = $"{rubroPrincipal.Rubro} concentra {(rubroPrincipal.Participacion * 100m):N1}% del gasto del período."
            });
        }

        if (concentracionTop3 >= 0.50m)
        {
            insights.Add(new RubrosInsightDto
            {
                Tipo = "warning",
                Mensaje = $"Los 3 rubros principales concentran {(concentracionTop3 * 100m):N1}% del gasto."
            });
        }

        var mayorCrecimiento = rubros.Where(x => x.VariacionVsAnterior.HasValue).OrderByDescending(x => x.VariacionVsAnterior).FirstOrDefault();
        if (mayorCrecimiento is not null && mayorCrecimiento.VariacionVsAnterior >= 0.10m)
        {
            insights.Add(new RubrosInsightDto
            {
                Tipo = "danger",
                Mensaje = $"{mayorCrecimiento.Rubro} creció {(mayorCrecimiento.VariacionVsAnterior.Value * 100m):N1}% vs período anterior."
            });
        }

        var mayorCaida = rubros.Where(x => x.VariacionVsAnterior.HasValue).OrderBy(x => x.VariacionVsAnterior).FirstOrDefault();
        if (mayorCaida is not null && mayorCaida.VariacionVsAnterior <= -0.10m)
        {
            insights.Add(new RubrosInsightDto
            {
                Tipo = "success",
                Mensaje = $"{mayorCaida.Rubro} cayó {Math.Abs(mayorCaida.VariacionVsAnterior.Value * 100m):N1}% vs período anterior."
            });
        }

        var sinMovimiento = rubros.Where(x => !x.UltimaCompra.HasValue || x.UltimaCompra.Value.Date < DateTime.Today.AddDays(-30)).ToList();
        if (sinMovimiento.Count > 0)
        {
            insights.Add(new RubrosInsightDto
            {
                Tipo = "warning",
                Mensaje = $"Hay {sinMovimiento.Count} rubros sin movimiento reciente."
            });
        }

        return insights;
    }

    public async Task<FamiliasPageDto> GetFamiliasPageDataAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var totalGeneral = await GetTotalFromDetailAsync(connection, filters, cancellationToken);
        var totalAnterior = await GetTotalFromDetailAsync(connection, ComputePriorPeriodFilters(filters), cancellationToken);
        var divisor = totalGeneral == 0 ? 1 : totalGeneral;

        var currentRows = await ReadListAsync(connection, $"""
            SELECT
                COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia') AS Familia,
                COALESCE(NULLIF(fj.Descripcion, ''), COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia')) AS Descripcion,
                COALESCE(fj.PadreIdFamilia, '') AS PadreIdFamilia,
                COALESCE(fj.NivelJerarquico, 0) AS NivelJerarquico,
                COALESCE(fj.TieneHijos, 0) AS TieneHijos,
                SUM(d.TotalDashboard) AS TotalComprado,
                COUNT(DISTINCT d.IDARTICULO) AS CantidadArticulos,
                COUNT(DISTINCT d.CUENTA) AS CantidadProveedores,
                COUNT(DISTINCT (ISNULL(d.TC, '') + '|' + ISNULL(d.IDCOMPROBANTE, '') + '|' + ISNULL(d.CUENTA, ''))) AS CantidadComprobantes,
                MAX(d.FECHA) AS UltimaCompra
            FROM vw_compras_detalle_dashboard d
            LEFT JOIN vw_familias_jerarquia fj ON fj.IdFamilia = d.FAMILIA
            WHERE (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Familia IS NULL OR d.FAMILIA = @Familia)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            GROUP BY
                COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia'),
                COALESCE(NULLIF(fj.Descripcion, ''), COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia')),
                COALESCE(fj.PadreIdFamilia, ''),
                COALESCE(fj.NivelJerarquico, 0),
                COALESCE(fj.TieneHijos, 0)
            ORDER BY SUM(d.TotalDashboard) DESC;
            """, filters, reader => new FamiliaResumenDto
        {
            Familia = reader.SafeGetString("Familia"),
            Descripcion = reader.SafeGetString("Descripcion"),
            PadreIdFamilia = reader.SafeGetString("PadreIdFamilia"),
            NivelJerarquico = reader.GetInt32("NivelJerarquico"),
            TieneHijos = reader.GetBoolean("TieneHijos"),
            TotalComprado = reader.GetDecimal("TotalComprado"),
            CantidadArticulos = reader.GetInt32("CantidadArticulos"),
            CantidadProveedores = reader.GetInt32("CantidadProveedores"),
            CantidadComprobantes = reader.GetInt32("CantidadComprobantes"),
            UltimaCompra = reader.GetNullableDateTime("UltimaCompra")
        }, cancellationToken);

        var priorFilters = ComputePriorPeriodFilters(filters);
        var priorRows = await ReadListAsync(connection, $"""
            SELECT
                COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia') AS Familia,
                SUM(d.TotalDashboard) AS TotalAnterior
            FROM vw_compras_detalle_dashboard d
            WHERE (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Familia IS NULL OR d.FAMILIA = @Familia)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            GROUP BY COALESCE(NULLIF(d.FAMILIA, ''), 'Sin familia');
            """, priorFilters, reader => new
        {
            Familia = reader.SafeGetString("Familia"),
            TotalAnterior = reader.GetDecimal("TotalAnterior")
        }, cancellationToken);

        var priorLookup = priorRows.ToDictionary(x => x.Familia, x => x.TotalAnterior, StringComparer.OrdinalIgnoreCase);

        var familias = currentRows.Select(item =>
        {
            priorLookup.TryGetValue(item.Familia, out var totalAnteriorFamilia);
            decimal? variacion = null;
            if (totalAnteriorFamilia > 0)
            {
                variacion = (item.TotalComprado - totalAnteriorFamilia) / totalAnteriorFamilia;
            }

            return new FamiliaResumenDto
            {
                Familia = item.Familia,
                Descripcion = item.Descripcion,
                PadreIdFamilia = item.PadreIdFamilia,
                NivelJerarquico = item.NivelJerarquico,
                TieneHijos = item.TieneHijos,
                TotalComprado = item.TotalComprado,
                Participacion = item.TotalComprado / divisor,
                CantidadArticulos = item.CantidadArticulos,
                CantidadProveedores = item.CantidadProveedores,
                CantidadComprobantes = item.CantidadComprobantes,
                TotalAnterior = totalAnteriorFamilia > 0 ? totalAnteriorFamilia : null,
                VariacionVsAnterior = variacion,
                TicketPromedio = item.CantidadComprobantes > 0 ? item.TotalComprado / item.CantidadComprobantes : 0m,
                UltimaCompra = item.UltimaCompra
            };
        }).OrderByDescending(x => x.TotalComprado).ToList();

        var conVariacion = familias.Where(x => x.VariacionVsAnterior.HasValue).ToList();
        var conCrecimiento = conVariacion.Where(x => x.VariacionVsAnterior > 0).OrderByDescending(x => x.VariacionVsAnterior).ToList();
        var conCaida = conVariacion.Where(x => x.VariacionVsAnterior < 0).OrderBy(x => x.VariacionVsAnterior).ToList();
        var principal = familias.FirstOrDefault();
        var concentracionTop5 = familias.Take(5).Sum(x => x.Participacion);
        var variacionTotal = totalAnterior > 0 ? (totalGeneral - totalAnterior) / totalAnterior : (decimal?)null;

        return new FamiliasPageDto
        {
            Kpis = new FamiliasKpiDto
            {
                TotalComprado = totalGeneral,
                CantidadFamiliasActivas = familias.Count,
                FamiliaPrincipal = principal?.Descripcion ?? principal?.Familia ?? "Sin datos",
                ParticipacionFamiliaPrincipal = principal?.Participacion ?? 0m,
                VariacionTotalVsAnterior = variacionTotal,
                FamiliaMayorCrecimiento = conCrecimiento.FirstOrDefault()?.Descripcion ?? conCrecimiento.FirstOrDefault()?.Familia ?? "Sin datos",
                FamiliaMayorCrecimientoVariacion = conCrecimiento.FirstOrDefault()?.VariacionVsAnterior,
                FamiliaMayorCaida = conCaida.FirstOrDefault()?.Descripcion ?? conCaida.FirstOrDefault()?.Familia ?? "Sin datos",
                FamiliaMayorCaidaVariacion = conCaida.FirstOrDefault()?.VariacionVsAnterior,
                ConcentracionTop5 = concentracionTop5
            },
            Familias = familias,
            TopFamilias = familias.Take(10).Select(x => new CategoryTotalDto
            {
                Categoria = string.IsNullOrWhiteSpace(x.Descripcion) ? x.Familia : x.Descripcion,
                Codigo = x.Familia,
                Total = x.TotalComprado
            }).ToList(),
            DistribucionGasto = familias.Take(10).Select(x => new CategoryTotalDto
            {
                Categoria = string.IsNullOrWhiteSpace(x.Descripcion) ? x.Familia : x.Descripcion,
                Codigo = x.Familia,
                Total = x.Participacion
            }).ToList(),
            VariacionesPositivas = conCrecimiento.Take(10).Select(x => new CategoryTotalDto
            {
                Categoria = string.IsNullOrWhiteSpace(x.Descripcion) ? x.Familia : x.Descripcion,
                Codigo = x.Familia,
                Total = x.VariacionVsAnterior ?? 0m
            }).ToList(),
            VariacionesNegativas = conCaida.Take(10).Select(x => new CategoryTotalDto
            {
                Categoria = string.IsNullOrWhiteSpace(x.Descripcion) ? x.Familia : x.Descripcion,
                Codigo = x.Familia,
                Total = Math.Abs(x.VariacionVsAnterior ?? 0m)
            }).ToList(),
            ConcentracionTop5VsResto =
            [
                new CategoryTotalDto { Categoria = "Top 5 familias", Codigo = "TOP5", Total = concentracionTop5 },
                new CategoryTotalDto { Categoria = "Resto", Codigo = "RESTO", Total = Math.Max(0m, 1m - concentracionTop5) }
            ],
            Insights = BuildFamiliasInsights(familias, principal, concentracionTop5)
        };
    }

    public async Task<IReadOnlyList<FamiliaResumenDto>> GetFamiliasAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
        => (await GetFamiliasPageDataAsync(filters, cancellationToken)).Familias;

    public async Task<FamiliaDetalleDto?> GetFamiliaDetalleAsync(string familia, DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        var baseFilters = filters.Clone();
        baseFilters.Familia = null;

        await using var connection = await OpenConnectionAsync(cancellationToken);

        var resumen = await ReadSingleAsync(connection, $"""
            SELECT
                @FamiliaSeleccionada AS Familia,
                COALESCE(NULLIF(fj.Descripcion, ''), @FamiliaSeleccionada) AS Descripcion,
                COALESCE(fj.PadreIdFamilia, '') AS PadreIdFamilia,
                COALESCE(fj.NivelJerarquico, 0) AS NivelJerarquico,
                COALESCE(fj.TieneHijos, 0) AS TieneHijos,
                ISNULL(SUM(d.TotalDashboard), 0) AS TotalComprado,
                COUNT(DISTINCT d.IDARTICULO) AS CantidadArticulos,
                COUNT(DISTINCT d.CUENTA) AS CantidadProveedores,
                COUNT(DISTINCT (ISNULL(d.TC, '') + '|' + ISNULL(d.IDCOMPROBANTE, '') + '|' + ISNULL(d.CUENTA, ''))) AS CantidadComprobantes,
                MAX(d.FECHA) AS UltimaCompra
            FROM vw_familias_jerarquia fj
            LEFT JOIN vw_compras_detalle_dashboard d
                ON d.FAMILIA LIKE @FamiliaSeleccionada + '%'
               AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
               AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
               AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
               AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
               AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
               AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
               AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
               AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
               AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
               AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
               AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            WHERE fj.IdFamilia = @FamiliaSeleccionada
            GROUP BY fj.Descripcion, fj.PadreIdFamilia, fj.NivelJerarquico, fj.TieneHijos;
            """,
            cmd =>
            {
                AddCommonParameters(cmd, baseFilters);
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => new FamiliaResumenDto
            {
                Familia = reader.SafeGetString("Familia"),
                Descripcion = reader.SafeGetString("Descripcion"),
                PadreIdFamilia = reader.SafeGetString("PadreIdFamilia"),
                NivelJerarquico = reader.GetInt32("NivelJerarquico"),
                TieneHijos = reader.GetBoolean("TieneHijos"),
                TotalComprado = reader.GetDecimal("TotalComprado"),
                CantidadArticulos = reader.GetInt32("CantidadArticulos"),
                CantidadProveedores = reader.GetInt32("CantidadProveedores"),
                CantidadComprobantes = reader.GetInt32("CantidadComprobantes"),
                TicketPromedio = reader.GetInt32("CantidadComprobantes") > 0 ? reader.GetDecimal("TotalComprado") / reader.GetInt32("CantidadComprobantes") : 0m,
                UltimaCompra = reader.GetNullableDateTime("UltimaCompra")
            },
            cancellationToken);

        if (resumen is null)
        {
            return null;
        }

        var priorResumen = await ReadSingleAsync(connection, $"""
            SELECT
                ISNULL(SUM(d.TotalDashboard), 0) AS TotalAnterior
            FROM vw_compras_detalle_dashboard d
            WHERE d.FAMILIA LIKE @FamiliaSeleccionada + '%'
              AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante);
            """,
            cmd =>
            {
                AddCommonParameters(cmd, ComputePriorPeriodFilters(baseFilters));
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => reader.GetDecimal("TotalAnterior"),
            cancellationToken);

        var resumenFinal = new FamiliaResumenDto
        {
            Familia = resumen.Familia,
            Descripcion = resumen.Descripcion,
            PadreIdFamilia = resumen.PadreIdFamilia,
            NivelJerarquico = resumen.NivelJerarquico,
            TieneHijos = resumen.TieneHijos,
            TotalComprado = resumen.TotalComprado,
            Participacion = (await GetTotalFromDetailAsync(connection, baseFilters, cancellationToken)) is var totalGeneral && totalGeneral > 0 ? resumen.TotalComprado / totalGeneral : 0m,
            CantidadArticulos = resumen.CantidadArticulos,
            CantidadProveedores = resumen.CantidadProveedores,
            CantidadComprobantes = resumen.CantidadComprobantes,
            TotalAnterior = priorResumen > 0 ? priorResumen : null,
            VariacionVsAnterior = priorResumen > 0 ? (resumen.TotalComprado - priorResumen) / priorResumen : null,
            TicketPromedio = resumen.TicketPromedio,
            UltimaCompra = resumen.UltimaCompra
        };

        var totalRama = resumenFinal.TotalComprado == 0 ? 1 : resumenFinal.TotalComprado;

        var composicionInterna = await ReadListAsync(connection, $"""
            SELECT
                child.IdFamilia AS Codigo,
                COALESCE(NULLIF(child.Descripcion, ''), child.IdFamilia) AS Categoria,
                ISNULL(SUM(d.TotalDashboard), 0) AS Total
            FROM vw_familias_jerarquia child
            LEFT JOIN vw_compras_detalle_dashboard d
                ON d.FAMILIA LIKE child.IdFamilia + '%'
               AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
               AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
               AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
               AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
               AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
               AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
               AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
               AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
               AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
               AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
               AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            WHERE child.PadreIdFamilia = @FamiliaSeleccionada
            GROUP BY child.IdFamilia, child.Descripcion
            HAVING ISNULL(SUM(d.TotalDashboard), 0) > 0
            ORDER BY ISNULL(SUM(d.TotalDashboard), 0) DESC;
            """,
            cmd =>
            {
                AddCommonParameters(cmd, baseFilters);
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => new CategoryTotalDto
            {
                Codigo = reader.SafeGetString("Codigo"),
                Categoria = reader.SafeGetString("Categoria"),
                Total = reader.GetDecimal("Total")
            },
            cancellationToken);

        var composicionNormalizada = composicionInterna.Select(x => new CategoryTotalDto
        {
            Codigo = x.Codigo,
            Categoria = x.Categoria,
            Total = x.Total,
            Participacion = x.Total / totalRama
        }).ToList();

        var articulos = await ReadListAsync(connection, $"""
            SELECT TOP (10)
                d.IDARTICULO AS Codigo,
                COALESCE(NULLIF(d.DESCRIPCION_ARTICULO, ''), d.IDARTICULO) AS Categoria,
                SUM(d.TotalDashboard) AS Total
            FROM vw_compras_detalle_dashboard d
            WHERE d.FAMILIA LIKE @FamiliaSeleccionada + '%'
              AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            GROUP BY d.IDARTICULO, d.DESCRIPCION_ARTICULO
            ORDER BY SUM(d.TotalDashboard) DESC;
            """,
            cmd =>
            {
                AddCommonParameters(cmd, baseFilters);
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => new CategoryTotalDto
            {
                Codigo = reader.SafeGetString("Codigo"),
                Categoria = reader.SafeGetString("Categoria"),
                Total = reader.GetDecimal("Total")
            },
            cancellationToken);

        var articulosNormalizados = articulos.Select(x => new CategoryTotalDto
        {
            Codigo = x.Codigo,
            Categoria = x.Categoria,
            Total = x.Total,
            Participacion = x.Total / totalRama
        }).ToList();

        var ultimosComprobantes = await ReadListAsync(connection, $"""
            SELECT TOP (10)
                d.TC,
                d.IDCOMPROBANTE,
                MAX(d.FECHA) AS Fecha,
                d.CUENTA,
                MAX(COALESCE(NULLIF(d.RAZON_SOCIAL, ''), d.CUENTA)) AS RazonSocial,
                SUM(d.TotalDashboard) AS ImporteDashboard
            FROM vw_compras_detalle_dashboard d
            WHERE d.FAMILIA LIKE @FamiliaSeleccionada + '%'
              AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            GROUP BY d.TC, d.IDCOMPROBANTE, d.CUENTA
            ORDER BY MAX(d.FECHA) DESC, SUM(d.TotalDashboard) DESC;
            """,
            cmd =>
            {
                AddCommonParameters(cmd, baseFilters);
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => new ComprobanteDto
            {
                Tc = reader.SafeGetString("TC"),
                IdComprobante = reader.SafeGetString("IDCOMPROBANTE"),
                Numero = reader.SafeGetString("IDCOMPROBANTE"),
                Fecha = reader.GetDateTime("Fecha"),
                Cuenta = reader.SafeGetString("CUENTA"),
                RazonSocial = reader.SafeGetString("RazonSocial"),
                ImporteDashboard = reader.GetDecimal("ImporteDashboard")
            },
            cancellationToken);

        var evolucion = await ReadListAsync(connection, $"""
            SELECT TOP (12)
                RIGHT('0' + CONVERT(varchar(2), MONTH(d.FECHA)), 2) + '/' + CONVERT(varchar(4), YEAR(d.FECHA)) AS Periodo,
                SUM(d.TotalDashboard) AS Total
            FROM vw_compras_detalle_dashboard d
            WHERE d.FAMILIA LIKE @FamiliaSeleccionada + '%'
              AND (@FechaDesde IS NULL OR d.FECHA >= @FechaDesde)
              AND (@FechaHasta IS NULL OR d.FECHA < DATEADD(day, 1, @FechaHasta))
              AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
              AND (@Articulo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@Articulo)))
              AND (@ArticuloCodigo IS NULL OR LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@ArticuloCodigo)))
              AND (@ArticuloDescripcion IS NULL OR d.DESCRIPCION_ARTICULO LIKE '%' + @ArticuloDescripcion + '%')
              AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
              AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
              AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
              AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito)
              AND (@TipoComprobante IS NULL OR d.TC = @TipoComprobante)
            GROUP BY YEAR(d.FECHA), MONTH(d.FECHA)
            ORDER BY YEAR(d.FECHA) DESC, MONTH(d.FECHA) DESC;
            """,
            cmd =>
            {
                AddCommonParameters(cmd, baseFilters);
                cmd.Parameters.AddWithValue("@FamiliaSeleccionada", familia);
            },
            reader => new MonthlyPointDto
            {
                Periodo = reader.SafeGetString("Periodo"),
                Total = reader.GetDecimal("Total")
            },
            cancellationToken);

        return new FamiliaDetalleDto
        {
            Resumen = resumenFinal,
            EvolucionMensual = evolucion.Reverse<MonthlyPointDto>().ToList(),
            ComposicionInterna = composicionNormalizada,
            Articulos = articulosNormalizados,
            Proveedores = [],
            UltimosComprobantes = ultimosComprobantes
        };
    }

    private static IReadOnlyList<FamiliasInsightDto> BuildFamiliasInsights(
        IReadOnlyList<FamiliaResumenDto> familias,
        FamiliaResumenDto? principal,
        decimal concentracionTop5)
    {
        var insights = new List<FamiliasInsightDto>();

        if (principal is not null && principal.Participacion >= 0.15m)
        {
            insights.Add(new FamiliasInsightDto
            {
                Tipo = "info",
                Mensaje = $"{(string.IsNullOrWhiteSpace(principal.Descripcion) ? principal.Familia : principal.Descripcion)} concentra {(principal.Participacion * 100m):N1}% del gasto."
            });
        }

        if (concentracionTop5 >= 0.50m)
        {
            insights.Add(new FamiliasInsightDto
            {
                Tipo = "warning",
                Mensaje = $"Las 5 familias principales concentran {(concentracionTop5 * 100m):N1}% del total."
            });
        }

        var mayorCrecimiento = familias.Where(x => x.VariacionVsAnterior.HasValue).OrderByDescending(x => x.VariacionVsAnterior).FirstOrDefault();
        if (mayorCrecimiento is not null && mayorCrecimiento.VariacionVsAnterior >= 0.10m)
        {
            insights.Add(new FamiliasInsightDto
            {
                Tipo = "danger",
                Mensaje = $"{(string.IsNullOrWhiteSpace(mayorCrecimiento.Descripcion) ? mayorCrecimiento.Familia : mayorCrecimiento.Descripcion)} creció {(mayorCrecimiento.VariacionVsAnterior.Value * 100m):N1}%."
            });
        }

        var mayorCaida = familias.Where(x => x.VariacionVsAnterior.HasValue).OrderBy(x => x.VariacionVsAnterior).FirstOrDefault();
        if (mayorCaida is not null && mayorCaida.VariacionVsAnterior <= -0.10m)
        {
            insights.Add(new FamiliasInsightDto
            {
                Tipo = "success",
                Mensaje = $"{(string.IsNullOrWhiteSpace(mayorCaida.Descripcion) ? mayorCaida.Familia : mayorCaida.Descripcion)} cayó {Math.Abs(mayorCaida.VariacionVsAnterior.Value * 100m):N1}%."
            });
        }

        var sinMovimiento = familias.Where(x => !x.UltimaCompra.HasValue || x.UltimaCompra.Value.Date < DateTime.Today.AddDays(-30)).Count();
        if (sinMovimiento > 0)
        {
            insights.Add(new FamiliasInsightDto
            {
                Tipo = "warning",
                Mensaje = $"Hay {sinMovimiento} familias sin movimiento reciente."
            });
        }

        return insights;
    }

    public async Task<ArticulosPageDto> GetArticulosPageDataAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        return await MeasureAsync("GetArticulosPage", filters, async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var fechaAltaExpr = await GetArticuloFechaAltaExpressionAsync(connection, cancellationToken);

            var currentRows = await ReadListAsync(connection, $"""
                SELECT
                    d.IDARTICULO,
                    COALESCE(NULLIF(d.DESCRIPCION_ARTICULO, ''), d.IDARTICULO) AS DescripcionArticulo,
                    SUM(d.CantidadDashboard) AS CantidadComprada,
                    SUM(d.TotalDashboard) AS TotalComprado,
                    AVG(CAST(d.COSTO AS decimal(18,4))) AS CostoPromedio,
                    MAX(CAST(d.COSTO AS decimal(18,4))) AS PrecioActual,
                    MAX(d.FECHA) AS UltimaCompra,
                    {fechaAltaExpr} AS FechaAlta,
                    COUNT(DISTINCT (ISNULL(d.TC, '') + '|' + ISNULL(d.IDCOMPROBANTE, '') + '|' + ISNULL(d.CUENTA, ''))) AS CantidadCompras
                {DetailFromClause}
                GROUP BY d.IDARTICULO, d.DESCRIPCION_ARTICULO
                ORDER BY SUM(d.TotalDashboard) DESC;
                """, filters, reader => new ArticuloResumenDto
            {
                IdArticulo = reader.SafeGetString("IDARTICULO"),
                DescripcionArticulo = reader.SafeGetString("DescripcionArticulo"),
                CantidadComprada = reader.GetDecimal("CantidadComprada"),
                TotalComprado = reader.GetDecimal("TotalComprado"),
                CostoPromedio = reader.GetDecimal("CostoPromedio"),
                PrecioActual = reader.GetDecimal("PrecioActual"),
                UltimaCompra = reader.GetNullableDateTime("UltimaCompra"),
                FechaAlta = reader.GetNullableDateTime("FechaAlta"),
                CantidadCompras = reader.GetInt32("CantidadCompras")
            }, cancellationToken);

            var priorFilters = ComputePriorPeriodFilters(filters);
            var priorCosts = await ReadListAsync(connection, $"""
                SELECT
                    d.IDARTICULO,
                    AVG(CAST(d.COSTO AS decimal(18,4))) AS PrecioAnterior
                {DetailFromClause}
                GROUP BY d.IDARTICULO;
                """, priorFilters, reader => new
            {
                IdArticulo = reader.SafeGetString("IDARTICULO"),
                PrecioAnterior = reader.GetDecimal("PrecioAnterior")
            }, cancellationToken);

            var proveedoresPrincipales = await ReadListAsync(connection, $"""
                WITH ranked AS (
                    SELECT
                        d.IDARTICULO,
                        COALESCE(NULLIF(d.RAZON_SOCIAL, ''), d.CUENTA) AS ProveedorPrincipal,
                        d.CUENTA AS ProveedorPrincipalCuenta,
                        SUM(d.TotalDashboard) AS TotalProveedor,
                        ROW_NUMBER() OVER (
                            PARTITION BY d.IDARTICULO
                            ORDER BY SUM(d.TotalDashboard) DESC, COUNT(*) DESC, d.CUENTA
                        ) AS rn
                    {DetailFromClause}
                    GROUP BY d.IDARTICULO, d.CUENTA, d.RAZON_SOCIAL
                )
                SELECT
                    IDARTICULO,
                    ProveedorPrincipal,
                    ProveedorPrincipalCuenta,
                    TotalProveedor
                FROM ranked
                WHERE rn = 1;
                """, filters, reader => new
            {
                IdArticulo = reader.SafeGetString("IDARTICULO"),
                ProveedorPrincipal = reader.SafeGetString("ProveedorPrincipal"),
                ProveedorPrincipalCuenta = reader.SafeGetString("ProveedorPrincipalCuenta"),
                TotalProveedor = reader.GetDecimal("TotalProveedor")
            }, cancellationToken);

            // Artículos que tienen ALGUNA compra anterior al inicio del período actual → no son nuevos
            var fechaDesdeActual = filters.FechaDesde;
            var articulosConHistorial = fechaDesdeActual.HasValue
                ? await ReadListAsync(connection, $"""
                    SELECT DISTINCT LTRIM(RTRIM(d.IDARTICULO)) AS IDARTICULO
                    FROM vw_compras_detalle_dashboard d
                    WHERE d.FECHA < @FechaDesde
                      AND (@Proveedor IS NULL OR d.CUENTA = @Proveedor)
                      AND (@Rubro IS NULL OR d.RUBRO = @Rubro)
                      AND (@Familia IS NULL OR d.FAMILIA = @Familia)
                      AND (@Usuario IS NULL OR d.USUARIO = @Usuario)
                      AND (@Sucursal IS NULL OR d.SUCURSAL = @Sucursal)
                      AND (@Deposito IS NULL OR CONVERT(varchar(20), d.IdDeposito) = @Deposito);
                    """,
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@FechaDesde", fechaDesdeActual.Value);
                        cmd.Parameters.AddWithValue("@Proveedor",  ToDbValue(filters.Proveedor));
                        cmd.Parameters.AddWithValue("@Rubro",      ToDbValue(filters.Rubro));
                        cmd.Parameters.AddWithValue("@Familia",    ToDbValue(filters.Familia));
                        cmd.Parameters.AddWithValue("@Usuario",    ToDbValue(filters.Usuario));
                        cmd.Parameters.AddWithValue("@Sucursal",   ToDbValue(filters.Sucursal));
                        cmd.Parameters.AddWithValue("@Deposito",   ToDbValue(filters.Deposito));
                    },
                    reader => reader.SafeGetString("IDARTICULO"),
                    cancellationToken)
                : (IReadOnlyList<string>)[];

            var conHistorialSet = new HashSet<string>(articulosConHistorial, StringComparer.OrdinalIgnoreCase);

            var priorLookup = priorCosts.ToDictionary(x => x.IdArticulo, x => x.PrecioAnterior, StringComparer.OrdinalIgnoreCase);
            var proveedorLookup = proveedoresPrincipales.ToDictionary(x => x.IdArticulo, x => x, StringComparer.OrdinalIgnoreCase);

            var articulos = currentRows.Select(item =>
            {
                priorLookup.TryGetValue(item.IdArticulo, out var precioAnterior);
                proveedorLookup.TryGetValue(item.IdArticulo, out var proveedor);
                decimal? variacion = null;
                if (precioAnterior > 0)
                {
                    variacion = (item.PrecioActual - precioAnterior) / precioAnterior;
                }

                var esNuevo = !conHistorialSet.Contains(item.IdArticulo.Trim());

                return new ArticuloResumenDto
                {
                    IdArticulo = item.IdArticulo,
                    DescripcionArticulo = item.DescripcionArticulo,
                    CantidadComprada = item.CantidadComprada,
                    TotalComprado = item.TotalComprado,
                    CostoPromedio = item.CostoPromedio,
                    PrecioActual = item.PrecioActual,
                    PrecioAnterior = precioAnterior,
                    VariacionPrecio = variacion,
                    ProveedorPrincipal = proveedor?.ProveedorPrincipal ?? string.Empty,
                    ProveedorPrincipalCuenta = proveedor?.ProveedorPrincipalCuenta ?? string.Empty,
                    ParticipacionProveedorPrincipal = item.TotalComprado > 0 ? (proveedor?.TotalProveedor ?? 0m) / item.TotalComprado : 0m,
                    UltimaCompra = item.UltimaCompra,
                    FechaAlta = item.FechaAlta,
                    CantidadCompras = item.CantidadCompras,
                    EsNuevo = esNuevo
                };
            }).ToList();

            var conVariacion = articulos.Where(x => x.VariacionPrecio.HasValue).ToList();
            var conAumento = conVariacion.Where(x => x.VariacionPrecio > 0).OrderByDescending(x => x.VariacionPrecio).ToList();
            var conBaja = conVariacion.Where(x => x.VariacionPrecio < 0).OrderBy(x => x.VariacionPrecio).ToList();

            return new ArticulosPageDto
            {
                Kpis = new ArticulosKpiDto
                {
                    TotalComprado = articulos.Sum(x => x.TotalComprado),
                    CantidadArticulosDistintos = articulos.Count,
                    CantidadItems = articulos.Sum(x => x.CantidadCompras),
                    CostoPromedioGeneral = articulos.Count > 0 ? articulos.Average(x => x.CostoPromedio) : 0m,
                    ArticulosConAumento = conAumento.Count,
                    ArticulosConBaja = conBaja.Count,
                    ArticulosNuevos = articulos.Count(x => x.EsNuevo),
                    MayorAumentoArticulo = conAumento.FirstOrDefault()?.DescripcionArticulo ?? "Sin datos",
                    MayorAumentoVariacion = conAumento.FirstOrDefault()?.VariacionPrecio,
                    MayorBajaArticulo = conBaja.FirstOrDefault()?.DescripcionArticulo ?? "Sin datos",
                    MayorBajaVariacion = conBaja.FirstOrDefault()?.VariacionPrecio
                },
                Articulos = articulos,
                TopPorTotal = articulos.OrderByDescending(x => x.TotalComprado).Take(8).Select(x => new CategoryTotalDto
                {
                    Categoria = x.DescripcionArticulo,
                    Codigo = x.IdArticulo,
                    Total = x.TotalComprado
                }).ToList(),
                TopPorCantidad = articulos.OrderByDescending(x => x.CantidadComprada).Take(8).Select(x => new CategoryTotalDto
                {
                    Categoria = x.DescripcionArticulo,
                    Codigo = x.IdArticulo,
                    Total = x.CantidadComprada
                }).ToList(),
                TopAumentos = conAumento.Take(8).Select(x => new CategoryTotalDto
                {
                    Categoria = x.DescripcionArticulo,
                    Codigo = x.IdArticulo,
                    Total = x.VariacionPrecio ?? 0m
                }).ToList(),
                TopBajas = conBaja.Take(8).Select(x => new CategoryTotalDto
                {
                    Categoria = x.DescripcionArticulo,
                    Codigo = x.IdArticulo,
                    Total = Math.Abs(x.VariacionPrecio ?? 0m)
                }).ToList(),
                Insights = BuildArticulosInsights(articulos, conAumento, conBaja)
            };
        });
    }

    public async Task<IReadOnlyList<ArticuloResumenDto>> GetArticulosAsync(DashboardFilters filters, CancellationToken cancellationToken = default)
        => (await GetArticulosPageDataAsync(filters, cancellationToken)).Articulos;

    private async Task<string> GetArticuloFechaAltaExpressionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var exists = await ReadSingleAsync(connection, """
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = 'dbo'
                      AND TABLE_NAME = 'V_MA_ARTICULOS'
                      AND COLUMN_NAME = 'FHALTA'
                ) THEN 1
                ELSE 0
            END AS Existe;
            """,
            _ => { },
            reader => reader.GetInt32("Existe"),
            cancellationToken);

        return exists == 1
            ? "(SELECT MIN(a.FHALTA) FROM dbo.V_MA_ARTICULOS a WHERE LTRIM(RTRIM(a.IDARTICULO)) = LTRIM(RTRIM(d.IDARTICULO)))"
            : "CAST(NULL AS datetime)";
    }

    public async Task<ArticuloDetalleDto?> GetArticuloDetalleAsync(string idArticulo, DashboardFilters filters, CancellationToken cancellationToken = default)
    {
        var scoped = filters.Clone();
        scoped.Articulo = idArticulo;
        var resumen = (await GetArticulosPageDataAsync(scoped, cancellationToken)).Articulos.FirstOrDefault();
        if (resumen is null)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var proveedores = await GetCategoryTotalsAsync(connection, scoped, $"""
            SELECT TOP (10)
                COALESCE(NULLIF(d.RAZON_SOCIAL, ''), d.CUENTA) AS Categoria,
                d.CUENTA AS Codigo,
                SUM(d.TotalDashboard) AS Total
            {DetailFromClause}
            GROUP BY d.CUENTA, d.RAZON_SOCIAL
            ORDER BY SUM(d.TotalDashboard) DESC;
            """, resumen.TotalComprado == 0 ? 1 : resumen.TotalComprado, cancellationToken);

        var historial = await ReadListAsync(connection, $"""
            SELECT TOP (12)
                c.TC, c.IDCOMPROBANTE, c.NUMERO, c.FECHA, c.CUENTA, c.RAZON_SOCIAL,
                c.SUCURSAL, CONVERT(varchar(20), c.IdDeposito) AS Deposito, c.USUARIO,
                c.NetoDashboard, c.IvaDashboard, c.ImporteDashboard, c.EstadoComprobante
            FROM vw_compras_cabecera_dashboard c
            WHERE EXISTS (
                SELECT 1
                FROM vw_compras_detalle_dashboard d
                WHERE d.TC = c.TC AND d.IDCOMPROBANTE = c.IDCOMPROBANTE AND d.CUENTA = c.CUENTA AND LTRIM(RTRIM(d.IDARTICULO)) = LTRIM(RTRIM(@IdArticulo))
            )
            ORDER BY c.FECHA DESC, c.IDCOMPROBANTE DESC;
            """, cmd => cmd.Parameters.AddWithValue("@IdArticulo", idArticulo), MapComprobante, cancellationToken);

        var evolucion = await ReadListAsync(connection, """
            SELECT TOP (12)
                RIGHT('0' + CONVERT(varchar(2), MONTH(FECHA)), 2) + '/' + CONVERT(varchar(4), YEAR(FECHA)) AS Periodo,
                AVG(CAST(COSTO AS decimal(18,4))) AS Total
            FROM vw_compras_detalle_dashboard
            WHERE LTRIM(RTRIM(IDARTICULO)) = LTRIM(RTRIM(@IdArticulo))
            GROUP BY YEAR(FECHA), MONTH(FECHA)
            ORDER BY YEAR(FECHA) DESC, MONTH(FECHA) DESC;
            """, cmd => cmd.Parameters.AddWithValue("@IdArticulo", idArticulo), reader => new MonthlyPointDto
        {
            Periodo = reader.SafeGetString("Periodo"),
            Total = reader.GetDecimal("Total")
        }, cancellationToken);

        return new ArticuloDetalleDto
        {
            Resumen = resumen,
            Proveedores = proveedores,
            Historial = historial,
            EvolucionCosto = evolucion.Reverse<MonthlyPointDto>().ToList()
        };
    }

    private static IReadOnlyList<ArticulosInsightDto> BuildArticulosInsights(
        IReadOnlyList<ArticuloResumenDto> articulos,
        IReadOnlyList<ArticuloResumenDto> conAumento,
        IReadOnlyList<ArticuloResumenDto> conBaja)
    {
        var insights = new List<ArticulosInsightDto>();

        var mayorAumento = conAumento.FirstOrDefault();
        if (mayorAumento is not null && mayorAumento.VariacionPrecio >= 0.10m)
        {
            insights.Add(new ArticulosInsightDto
            {
                Tipo = "danger",
                Mensaje = $"{mayorAumento.DescripcionArticulo} aumentó {(mayorAumento.VariacionPrecio ?? 0m):P1} vs período anterior."
            });
        }

        var mayorBaja = conBaja.FirstOrDefault();
        if (mayorBaja is not null && mayorBaja.VariacionPrecio <= -0.10m)
        {
            insights.Add(new ArticulosInsightDto
            {
                Tipo = "success",
                Mensaje = $"{mayorBaja.DescripcionArticulo} bajó {Math.Abs(mayorBaja.VariacionPrecio ?? 0m):P1} vs período anterior."
            });
        }

        var impacto = articulos.OrderByDescending(x => x.TotalComprado).FirstOrDefault();
        if (impacto is not null)
        {
            insights.Add(new ArticulosInsightDto
            {
                Tipo = "info",
                Mensaje = $"{impacto.DescripcionArticulo} es el artículo de mayor impacto económico del período."
            });
        }

        var dominante = articulos
            .Where(x => x.ParticipacionProveedorPrincipal >= 0.70m && !string.IsNullOrWhiteSpace(x.ProveedorPrincipal))
            .OrderByDescending(x => x.ParticipacionProveedorPrincipal)
            .FirstOrDefault();
        if (dominante is not null)
        {
            insights.Add(new ArticulosInsightDto
            {
                Tipo = "warn",
                Mensaje = $"{dominante.DescripcionArticulo} depende de {dominante.ProveedorPrincipal} en {(dominante.ParticipacionProveedorPrincipal):P0} del gasto."
            });
        }

        return insights.Take(4).ToList();
    }

    private async Task<decimal> GetTotalFromDetailAsync(SqlConnection connection, DashboardFilters filters, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT ISNULL(SUM(d.TotalDashboard), 0) AS Total
            {DetailFromClause};
            """;

        return await ReadSingleAsync(connection, sql, filters, reader => reader.GetDecimal("Total"), cancellationToken);
    }
}

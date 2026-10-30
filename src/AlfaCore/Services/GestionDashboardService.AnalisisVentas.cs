using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class GestionDashboardService
{
    public Task<VentasAnalisisDto> GetVentasAnalisisAsync(VentasDashboardFilters filters, bool porVendedor,
        string? codigo = null, int pagina = 1, CancellationToken ct = default)
        => ExecuteLoggedAsync("Ventas", "AnalisisComercial", async token =>
        {
            if (filters.FechaDesde > filters.FechaHasta || filters.FechaHasta?.Date == DateTime.MaxValue.Date || pagina < 1)
                throw new AppUserFacingException("Revisá el período y la página seleccionada.", "");

            // El vendedor pertenece al movimiento histórico, no al maestro actual del cliente.
            var dimension = porVendedor ? "l.IdVendedor" : "CONVERT(nvarchar(50), l.UNEGOCIO)";
            var nombre = porVendedor ? "l.NOMBRE_VENDEDOR" : "u.Descripcion";
            var sinAsignar = porVendedor ? "Sin vendedor" : "Sin unidad de negocio";
            var join = porVendedor ? "" : """
                LEFT JOIN (SELECT LTRIM(RTRIM(Codigo)) Codigo, MAX(Descripcion) Descripcion
                    FROM dbo.V_TA_UnidadNegocio GROUP BY LTRIM(RTRIM(Codigo))) u
                    ON u.Codigo = LTRIM(RTRIM(CONVERT(nvarchar(50),l.UNEGOCIO)))
                """;
            await using var cn = new SqlConnection(ConnectionString);
            // Materializar una sola vez evita que ranking y detalle usen universos distintos.
            var sql = $"""
                SELECT ISNULL(LTRIM(RTRIM({dimension})), '') Codigo,
                    COALESCE(NULLIF(LTRIM(RTRIM({nombre})), ''), NULLIF(LTRIM(RTRIM({dimension})), ''), @SinAsignar) Nombre,
                    l.FECHA Fecha, LTRIM(RTRIM(l.TC)) Tc, LTRIM(RTRIM(l.IdComprobante)) IdComprobante,
                    LTRIM(RTRIM(l.CUENTA)) Cuenta, l.CABNOMBRE Cliente, l.IMPORTE Total
                INTO #VentasAnalisis
                FROM dbo.Libro_VentasConFP l
                {join}
                WHERE (@Desde IS NULL OR l.FECHA >= @Desde)
                  AND (@Hasta IS NULL OR l.FECHA < @Hasta)
                  AND (@Cliente IS NULL OR l.CUENTA LIKE @Cliente OR l.CABNOMBRE LIKE @Cliente)
                  AND (@Usuario IS NULL OR LTRIM(RTRIM(l.USUARIO_LOGEADO)) = @Usuario)
                  AND (@SinUnidades = 1 OR LTRIM(RTRIM(CONVERT(nvarchar(50),l.UNEGOCIO))) IN @Unidades)
                  AND (@Tc IS NULL OR LTRIM(RTRIM(l.TC)) = @Tc)
                  AND (@Deposito IS NULL OR EXISTS (SELECT 1 FROM dbo.V_MV_Cpte c
                      WHERE LTRIM(RTRIM(c.TC))=LTRIM(RTRIM(l.TC))
                        AND LTRIM(RTRIM(c.IDCOMPROBANTE))=LTRIM(RTRIM(l.IdComprobante))
                        AND LTRIM(RTRIM(CONVERT(nvarchar(50),c.IdDeposito)))=@Deposito));

                SELECT Codigo, MAX(Nombre) Nombre, SUM(Total) Total,
                    (SELECT COUNT(*) FROM (SELECT DISTINCT Tc,IdComprobante FROM #VentasAnalisis d WHERE d.Codigo=v.Codigo) c) Comprobantes,
                    COUNT(DISTINCT Cuenta) Clientes
                FROM #VentasAnalisis v GROUP BY Codigo ORDER BY SUM(Total) DESC, Codigo;

                SELECT CONVERT(varchar(7),Fecha,120) Periodo, SUM(Total) Total
                FROM #VentasAnalisis WHERE @Codigo IS NOT NULL AND Codigo=@Codigo
                GROUP BY CONVERT(varchar(7),Fecha,120) ORDER BY Periodo;

                SELECT COUNT(*) FROM (SELECT Tc,IdComprobante FROM #VentasAnalisis
                    WHERE @Codigo IS NOT NULL AND Codigo=@Codigo GROUP BY Tc,IdComprobante) c;

                -- ROW_NUMBER conserva la paginación en servidores SQL Server 2008.
                ;WITH Comprobantes AS (
                    SELECT MAX(Fecha) Fecha,Tc,IdComprobante,MAX(Cuenta) Cuenta,MAX(Cliente) Cliente,SUM(Total) Total
                    FROM #VentasAnalisis WHERE @Codigo IS NOT NULL AND Codigo=@Codigo
                    GROUP BY Tc,IdComprobante
                ), Pagina AS (
                    SELECT *,ROW_NUMBER() OVER (ORDER BY Fecha DESC,Tc,IdComprobante) Fila
                    FROM Comprobantes
                )
                SELECT Fecha,Tc,IdComprobante,Cuenta,Cliente,Total
                FROM Pagina WHERE Fila > @Skip AND Fila <= CONVERT(bigint,@Skip) + 50
                ORDER BY Fila;
                DROP TABLE #VentasAnalisis;
                """;
            static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            using var result = await cn.QueryMultipleAsync(new CommandDefinition(sql, new
            {
                Desde = filters.FechaDesde?.Date, Hasta = filters.FechaHasta?.Date.AddDays(1),
                Cliente = Like(filters.Cliente), Usuario = Clean(filters.Usuario),
                SinUnidades = filters.UnidadesNegocio.Count == 0, Unidades = filters.UnidadesNegocio.Select(x => x.Trim()).ToArray(),
                Tc = Clean(filters.TipoComprobante), Deposito = Clean(filters.Deposito),
                Codigo = codigo?.Trim(), SinAsignar = sinAsignar, Skip = checked((pagina - 1) * 50)
            }, commandTimeout: 60, cancellationToken: token));
            return new VentasAnalisisDto
            {
                Filas = (await result.ReadAsync<VentasAnalisisFilaDto>()).ToList(),
                Evolucion = (await result.ReadAsync<MonthlyPointDto>()).ToList(),
                CantidadComprobantes = await result.ReadSingleAsync<int>(),
                Comprobantes = (await result.ReadAsync<VentasAnalisisComprobanteDto>()).ToList()
            };
        }, "No se pudo cargar el análisis de ventas. Reintentá la consulta.", ct);
}

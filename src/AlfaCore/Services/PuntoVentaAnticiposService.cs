using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaAnticiposService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ILogger<PuntoVentaAnticiposService> logger) : IPuntoVentaAnticiposService
{
    private const string ModuleName = "PuntoVentaAnticipos";

    public Task<PuntoVentaNpPaginaDto> BuscarNotasPedidoAsync(PuntoVentaNpListadoFiltro filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync("BuscarNotasPedido", async token =>
        {
            var buscar = (filtro.Buscar ?? string.Empty).Trim();
            var estado = (filtro.Estado ?? string.Empty).Trim().ToUpperInvariant();
            var pagina = Math.Max(1, filtro.Pagina);
            var tamano = Math.Clamp(filtro.TamanoPagina <= 0 ? 20 : filtro.TamanoPagina, 1, 50);
            var offset = checked((pagina - 1) * tamano);
            var sucursal = (filtro.Sucursal ?? string.Empty).Trim();
            var unidad = (filtro.Unidad ?? string.Empty).Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                WITH np AS
                (
                    SELECT c.ID,
                        LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                        LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                        LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) AS Cliente,
                        c.FECHA AS Fecha,
                        CONVERT(decimal(18,2), ISNULL(c.IMPORTE, 0)) AS ImporteComercial,
                        CONVERT(bit, ISNULL(c.ANULADA, 0)) AS Anulada,
                        CONVERT(bit, ISNULL(c.BLOQUEADA, 0)) AS Bloqueada,
                        CONVERT(bit, ISNULL(c.FINALIZADA, 0)) AS Finalizada,
                        CONVERT(bit, ISNULL(c.CERRADA, 0)) AS Cerrada
                    FROM dbo.V_MV_CPTE c
                    WHERE LTRIM(RTRIM(c.TC)) = 'NP'
                      AND (@IncluirAnuladas = 1 OR ISNULL(c.ANULADA, 0) = 0)
                      AND (@Buscar = '' OR LTRIM(RTRIM(c.IDCOMPROBANTE)) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) LIKE @BuscarLike)
                      AND (@Desde IS NULL OR c.FECHA >= @Desde)
                      AND (@Hasta IS NULL OR c.FECHA < DATEADD(day, 1, @Hasta))
                      AND (@Sucursal = '' OR LTRIM(RTRIM(ISNULL(c.SUCURSAL, ''))) = @Sucursal)
                      AND (@Unidad = '' OR LTRIM(RTRIM(ISNULL(c.UNEGOCIO, ''))) = @Unidad)
                      AND (@Estado = '' OR @Estado = CASE
                            WHEN ISNULL(c.ANULADA, 0) = 1 THEN 'ANULADA'
                            WHEN ISNULL(c.BLOQUEADA, 0) = 1 THEN 'BLOQUEADA'
                            WHEN ISNULL(c.FINALIZADA, 0) = 1 THEN 'FINALIZADA'
                            WHEN ISNULL(c.CERRADA, 0) = 1 THEN 'CERRADA'
                            ELSE 'PENDIENTE' END)
                )
                SELECT n.ID, n.IdComprobante, n.Cuenta, n.Cliente, n.Fecha, n.ImporteComercial,
                    CONVERT(decimal(18,2), COALESCE(ant.Total, 0)) AS TotalAnticiposVigentes,
                    CONVERT(bit, CASE WHEN COALESCE(ant.Inconsistente, 0) = 0 THEN 1 ELSE 0 END) AS TotalAnticiposVerificado,
                    CONVERT(varchar(20), CASE WHEN COALESCE(ant.Inconsistente, 0) = 0 THEN 'VERIFICADO' ELSE 'NO_VERIFICADO' END) AS EstadoAnticipos,
                    CONVERT(varchar(20), CASE WHEN n.Anulada = 1 THEN 'ANULADA' WHEN n.Bloqueada = 1 THEN 'BLOQUEADA'
                        WHEN n.Finalizada = 1 THEN 'FINALIZADA' WHEN n.Cerrada = 1 THEN 'CERRADA' ELSE 'PENDIENTE' END) AS EstadoComprobante,
                    COUNT(*) OVER() AS Total
                FROM np n
                OUTER APPLY
                (
                    SELECT SUM(CASE WHEN ISNULL(cob.ANULADA, 0) = 0
                        AND COALESCE(ap.Aplicado, 0) <= CONVERT(decimal(18,2), ISNULL(cob.IMPORTE, 0))
                        THEN CONVERT(decimal(18,2), ISNULL(cob.IMPORTE, 0)) - COALESCE(ap.Aplicado, 0) ELSE 0 END) AS Total,
                        MAX(CASE WHEN ISNULL(cob.ANULADA, 0) = 0
                            AND COALESCE(ap.Aplicado, 0) > CONVERT(decimal(18,2), ISNULL(cob.IMPORTE, 0)) THEN 1 ELSE 0 END) AS Inconsistente
                    FROM dbo.V_MV_CPTE cob
                    OUTER APPLY
                    (
                        SELECT SUM(CONVERT(decimal(18,2), ISNULL(a.IMPORTE, 0))) AS Aplicado
                        FROM dbo.MV_APLICACION a
                        WHERE LTRIM(RTRIM(a.CUENTA)) = n.Cuenta
                          AND LTRIM(RTRIM(a.TC)) = LTRIM(RTRIM(cob.TC))
                          AND LTRIM(RTRIM(a.IDCOMPROBANTE)) = LTRIM(RTRIM(cob.IDCOMPROBANTE))
                    ) ap
                    WHERE LTRIM(RTRIM(cob.CUENTA)) = n.Cuenta
                      AND LTRIM(RTRIM(cob.TC)) IN ('CB', 'CBCT', 'CBFP')
                      AND (LTRIM(RTRIM(ISNULL(cob.PRVENTA, ''))) = n.IdComprobante
                           OR LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(cob.OBSERVACIONES, N'')))) = N'Cob.NP ' + n.IdComprobante)
                ) ant
                ORDER BY n.Fecha DESC, n.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;
                """;

            var items = (await cn.QueryAsync<PuntoVentaNpResumenRow>(new CommandDefinition(sql, new
            {
                Buscar = buscar,
                BuscarLike = $"%{buscar.Replace("%", "[%]").Replace("_", "[_]")}%",
                Estado = estado,
                Desde = filtro.Desde?.Date,
                Hasta = filtro.Hasta?.Date,
                Sucursal = sucursal,
                Unidad = unidad,
                IncluirAnuladas = filtro.IncluirAnuladas,
                Offset = offset,
                Tamano = tamano
            }, cancellationToken: token))).AsList();

            return new PuntoVentaNpPaginaDto
            {
                Items = items.Select(x => new PuntoVentaNpResumenDto
                {
                    Id = x.Id, IdComprobante = x.IdComprobante, Cuenta = x.Cuenta, Cliente = x.Cliente,
                    Fecha = x.Fecha, ImporteComercial = x.ImporteComercial,
                    TotalAnticiposVigentes = x.TotalAnticiposVigentes,
                    TotalAnticiposVerificado = x.TotalAnticiposVerificado,
                    EstadoAnticipos = x.EstadoAnticipos, EstadoComprobante = x.EstadoComprobante
                }).ToList(),
                Total = items.FirstOrDefault()?.Total ?? 0,
                Pagina = pagina, TamanoPagina = tamano
            };
        }, "No se pudieron consultar los encargos.", ct);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PuntoVentaNpAnticiposDto?> GetNotaPedidoAsync(string idComprobante, string? cuenta = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetNotaPedido", async token =>
        {
            var comprobante = (idComprobante ?? string.Empty).Trim();
            var cuentaNormalizada = (cuenta ?? string.Empty).Trim();
            if (comprobante.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string cabeceraSql = """
                SELECT TOP (1)
                    c.ID,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                    LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) AS Cliente,
                    c.FECHA AS Fecha,
                    CONVERT(decimal(18,2), ISNULL(c.IMPORTE, 0)) AS ImporteComercial,
                    CONVERT(bit, ISNULL(c.ANULADA, 0)) AS Anulada,
                    CONVERT(bit, ISNULL(c.BLOQUEADA, 0)) AS Bloqueada,
                    CONVERT(bit, ISNULL(c.FINALIZADA, 0)) AS Finalizada,
                    CONVERT(bit, ISNULL(c.CERRADA, 0)) AS Cerrada
                FROM dbo.V_MV_CPTE c
                WHERE LTRIM(RTRIM(c.TC)) = 'NP'
                  AND LTRIM(RTRIM(c.IDCOMPROBANTE)) = @Comprobante
                  AND (@Cuenta = '' OR LTRIM(RTRIM(c.CUENTA)) = @Cuenta)
                ORDER BY c.ID DESC;
                """;

            var cabecera = await cn.QuerySingleOrDefaultAsync<NpCabecera>(new CommandDefinition(
                cabeceraSql,
                new { Comprobante = comprobante, Cuenta = cuentaNormalizada },
                cancellationToken: token));
            if (cabecera is null)
                return null;

            const string cobranzasSql = """
                SELECT
                    c.ID,
                    CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(c.PRVENTA, ''))) AS PrVenta,
                    LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(c.OBSERVACIONES, N'')))) AS Observaciones,
                    LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                    c.FECHA AS Fecha,
                    CONVERT(decimal(18,2), ISNULL(c.IMPORTE, 0)) AS ImporteOriginal,
                    CONVERT(bit, ISNULL(c.ANULADA, 0)) AS Anulada
                FROM dbo.V_MV_CPTE c
                WHERE LTRIM(RTRIM(c.CUENTA)) = @Cuenta
                  AND LTRIM(RTRIM(c.TC)) IN ('CB', 'CBCT', 'CBFP')
                  AND (
                      LTRIM(RTRIM(ISNULL(c.PRVENTA, ''))) = @PrVenta
                      OR LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(c.OBSERVACIONES, N'')))) = @LegacyObservation
                  )
                ORDER BY c.FECHA, c.ID;
                """;

            var cobranzas = (await cn.QueryAsync<NpCobranza>(new CommandDefinition(
                cobranzasSql,
                new
                {
                    PrVenta = cabecera.IdComprobante,
                    LegacyObservation = $"Cob.NP {cabecera.IdComprobante}",
                    Cuenta = cabecera.Cuenta
                },
                cancellationToken: token))).AsList();

            cobranzas = cobranzas
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .OrderBy(x => x.Fecha)
                .ThenBy(x => x.Id)
                .ToList();

            IReadOnlyList<NpAplicacion> aplicaciones = cobranzas.Count == 0
                ? Array.Empty<NpAplicacion>()
                : (await cn.QueryAsync<NpAplicacion>(new CommandDefinition("""
                    SELECT
                        LTRIM(RTRIM(a.TC)) AS TcCobranza,
                        LTRIM(RTRIM(a.IDCOMPROBANTE)) AS IdCobranza,
                        LTRIM(RTRIM(ISNULL(a.TCO_ORIGEN, ''))) AS TcDestino,
                        LTRIM(RTRIM(ISNULL(a.IDCOMPROBANTE_ORIGEN, ''))) AS IdDestino,
                        CONVERT(decimal(18,2), ISNULL(a.IMPORTE, 0)) AS Importe
                    FROM dbo.MV_APLICACION a
                    WHERE LTRIM(RTRIM(a.CUENTA)) = @Cuenta
                      AND LTRIM(RTRIM(a.TC)) IN @Tipos
                      AND LTRIM(RTRIM(a.IDCOMPROBANTE)) IN @Comprobantes;
                    """, new
                {
                    Cuenta = cabecera.Cuenta,
                    Tipos = cobranzas.Select(x => x.Tc.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Comprobantes = cobranzas.Select(x => x.IdComprobante.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                }, cancellationToken: token))).AsList();

            var acciones = (await cn.QueryAsync<ComprobanteAccionDto>(new CommandDefinition("""
                SELECT
                    LTRIM(RTRIM(ISNULL(a.TIPO_ACCION, ''))) AS TipoAccion,
                    CAST(ISNULL(a.SYSTEMUSER, '') AS nvarchar(max)) AS Comentario,
                    a.FECHAHORA AS FechaHora,
                    LTRIM(RTRIM(ISNULL(a.USUARIO, ''))) AS Usuario,
                    LTRIM(RTRIM(ISNULL(a.PC, ''))) AS Pc,
                    LTRIM(RTRIM(ISNULL(a.PROCESO, ''))) AS Proceso
                FROM dbo.V_MV_CPTEACCIONES a
                WHERE LTRIM(RTRIM(a.TC)) = 'NP'
                  AND LTRIM(RTRIM(a.IDCOMPROBANTE)) = @Comprobante
                  AND (@Cuenta = '' OR LTRIM(RTRIM(ISNULL(a.CUENTA, ''))) = @Cuenta)
                ORDER BY a.FECHAHORA DESC, a.ID DESC;
                """, new { Comprobante = cabecera.IdComprobante, Cuenta = cabecera.Cuenta }, cancellationToken: token))).AsList();

            var aplicacionesPorCobranza = aplicaciones
                .GroupBy(x => (Tc: x.TcCobranza, Id: x.IdCobranza), StringTupleComparer.Instance)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<NpAplicacion>)x.ToList(), StringTupleComparer.Instance);

            var resultadoCobranzas = cobranzas.Select(cobranza =>
            {
                var key = (Tc: cobranza.Tc, Id: cobranza.IdComprobante);
                var relacionadas = aplicacionesPorCobranza.TryGetValue(key, out var filas)
                    ? filas
                    : Array.Empty<NpAplicacion>();
                var calculo = PuntoVentaAnticiposCalculador.Calcular(cobranza.ImporteOriginal, cobranza.Anulada, relacionadas
                    .Select(x => new PuntoVentaNpAplicacionDto
                    {
                        TcCobranza = x.TcCobranza,
                        IdCobranza = x.IdCobranza,
                        TcDestino = x.TcDestino,
                        IdDestino = x.IdDestino,
                        Importe = x.Importe
                    }).ToList());
                var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion(
                    cobranza.PrVenta, cobranza.Observaciones, cabecera.IdComprobante);

                return new PuntoVentaNpCobranzaDto
                {
                    Id = cobranza.Id,
                    IdComplemento = cobranza.IdComplemento,
                    Tc = cobranza.Tc,
                    IdComprobante = cobranza.IdComprobante,
                    PrVenta = cobranza.PrVenta,
                    Observaciones = cobranza.Observaciones,
                    Cuenta = cobranza.Cuenta,
                    Fecha = cobranza.Fecha,
                    ImporteOriginal = cobranza.ImporteOriginal,
                    Anulada = cobranza.Anulada,
                    ImporteAplicado = calculo.Aplicado,
                    ImporteDisponible = calculo.Disponible,
                    ImporteDisponibleVerificado = calculo.Verificado,
                    EstadoDisponible = calculo.Estado,
                    TipoRelacion = relacion.Tipo,
                    RelacionVerificada = relacion.Verificada,
                    AdvertenciaRelacion = relacion.Advertencia,
                    Aplicaciones = relacionadas.Select(x => new PuntoVentaNpAplicacionDto
                    {
                        TcCobranza = x.TcCobranza,
                        IdCobranza = x.IdCobranza,
                        TcDestino = x.TcDestino,
                        IdDestino = x.IdDestino,
                        Importe = x.Importe
                    }).ToList()
                };
            }).ToList();

            var total = PuntoVentaAnticiposCalculador.Totalizar(resultadoCobranzas);
            return new PuntoVentaNpAnticiposDto
            {
                Id = cabecera.Id,
                Tc = cabecera.Tc,
                IdComprobante = cabecera.IdComprobante,
                Cuenta = cabecera.Cuenta,
                Cliente = cabecera.Cliente,
                Fecha = cabecera.Fecha,
                ImporteComercial = cabecera.ImporteComercial,
                EstadoComprobante = Estado(cabecera),
                Cobranzas = resultadoCobranzas,
                TotalAnticiposVigentes = total.Total,
                TotalAnticiposVerificado = total.Verificado,
                EstadoAnticipos = total.Estado,
                Acciones = acciones
            };
        }, "No se pudieron consultar los anticipos de la nota de pedido.", ct);

    private static string Estado(NpCabecera cabecera)
        => cabecera.Anulada ? "ANULADA"
            : cabecera.Bloqueada ? "BLOQUEADA"
            : cabecera.Finalizada ? "FINALIZADA"
            : cabecera.Cerrada ? "CERRADA"
            : "PENDIENTE";

    private sealed class PuntoVentaNpResumenRow
    {
        public int Id { get; init; }
        public string IdComprobante { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Cliente { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public decimal ImporteComercial { get; init; }
        public decimal? TotalAnticiposVigentes { get; init; }
        public bool TotalAnticiposVerificado { get; init; }
        public string EstadoAnticipos { get; init; } = string.Empty;
        public string EstadoComprobante { get; init; } = string.Empty;
        public int Total { get; init; }
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", ModuleName, action);
            throw;
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed class NpCabecera
    {
        public int Id { get; init; }
        public string Tc { get; init; } = string.Empty;
        public string IdComprobante { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Cliente { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public decimal ImporteComercial { get; init; }
        public bool Anulada { get; init; }
        public bool Bloqueada { get; init; }
        public bool Finalizada { get; init; }
        public bool Cerrada { get; init; }
    }

    private sealed class NpCobranza
    {
        public int Id { get; init; }
        public int IdComplemento { get; init; }
        public string Tc { get; init; } = string.Empty;
        public string IdComprobante { get; init; } = string.Empty;
        public string PrVenta { get; init; } = string.Empty;
        public string Observaciones { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public decimal ImporteOriginal { get; init; }
        public bool Anulada { get; init; }
    }

    private sealed class NpAplicacion
    {
        public string TcCobranza { get; init; } = string.Empty;
        public string IdCobranza { get; init; } = string.Empty;
        public string TcDestino { get; init; } = string.Empty;
        public string IdDestino { get; init; } = string.Empty;
        public decimal Importe { get; init; }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Tc, string Id)>
    {
        public static StringTupleComparer Instance { get; } = new();
        public bool Equals((string Tc, string Id) x, (string Tc, string Id) y)
            => string.Equals(x.Tc, y.Tc, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Tc, string Id) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tc), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id));
    }
}

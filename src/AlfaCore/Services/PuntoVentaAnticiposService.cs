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
                WITH np_base AS
                (
                    SELECT c.ID,
                        CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                        LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                        LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                        LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) AS Cliente,
                        LTRIM(RTRIM(ISNULL(c.TELEFONO, ''))) AS Telefono,
                        c.FECHA AS Fecha,
                        c.FECHAENTREGA AS FechaEntrega,
                        CONVERT(decimal(18,2), ISNULL(c.IMPORTE, 0)) AS ImporteComercial,
                        LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(c.COMENTARIOS, N'')))) AS Comentarios,
                        CONVERT(bit, ISNULL(c.ANULADA, 0)) AS Anulada,
                        CONVERT(bit, ISNULL(c.BLOQUEADA, 0)) AS Bloqueada,
                        CONVERT(bit, ISNULL(c.FINALIZADA, 0)) AS Finalizada,
                        CONVERT(bit, ISNULL(c.CERRADA, 0)) AS Cerrada
                    FROM dbo.V_MV_CPTE c
                    WHERE LTRIM(RTRIM(c.TC)) = 'NP'
                      AND (@IncluirAnuladas = 1 OR ISNULL(c.ANULADA, 0) = 0)
                      AND (@Buscar = '' OR LTRIM(RTRIM(c.IDCOMPROBANTE)) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.TELEFONO, ''))) LIKE @BuscarLike
                           OR LTRIM(RTRIM(ISNULL(c.DOCUMENTONUMERO, ''))) LIKE @BuscarLike
                           OR CONVERT(nvarchar(max), ISNULL(c.COMENTARIOS, N'')) LIKE @BuscarLike
                           OR CONVERT(nvarchar(max), ISNULL(c.OBSERVACIONES, N'')) LIKE @BuscarLike)
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
                , np AS
                (
                    SELECT n.*
                    FROM np_base n
                    ORDER BY n.Fecha DESC, n.ID DESC
                    OFFSET @Offset ROWS FETCH NEXT @TamanoConSiguiente ROWS ONLY
                )
                SELECT n.ID, n.IdComprobante, n.Cuenta, n.Cliente, n.Telefono, n.Fecha, n.FechaEntrega, n.ImporteComercial,
                    n.Comentarios,
                    CAST(NULL AS decimal(18,2)) AS TotalAnticiposVigentes,
                    CAST(0 AS bit) AS TotalAnticiposVerificado,
                    CONVERT(varchar(20), 'NO_VERIFICADO') AS EstadoAnticipos,
                    CONVERT(varchar(20), CASE WHEN n.Anulada = 1 THEN 'ANULADA' WHEN n.Bloqueada = 1 THEN 'BLOQUEADA'
                        WHEN n.Finalizada = 1 THEN 'FINALIZADA' WHEN n.Cerrada = 1 THEN 'CERRADA' ELSE 'PENDIENTE' END) AS EstadoComprobante,
                    CONVERT(varchar(20), CASE WHEN n.Finalizada = 1 THEN 'LISTO' ELSE 'PENDIENTE' END) AS EstadoProduccion,
                    CONVERT(varchar(20), CASE WHEN n.Anulada = 1 THEN 'ANULADA' ELSE 'PENDIENTE' END) AS EstadoEntrega
                FROM np n
                ORDER BY n.Fecha DESC, n.ID DESC
                OPTION (RECOMPILE);
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
                TamanoConSiguiente = tamano + 1
            }, cancellationToken: token))).AsList();

            var tieneSiguiente = items.Count > tamano;
            if (tieneSiguiente)
                items.RemoveAt(items.Count - 1);

            var anticiposPorNp = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var idsSinResumenLegacy = new List<string>();
            foreach (var item in items)
            {
                if (PuntoVentaAnticiposCalculador.TryLeerResumenLegacy(item.Comentarios, out var sena, out _))
                    anticiposPorNp[item.IdComprobante.Trim()] = sena;
                else
                    idsSinResumenLegacy.Add(item.IdComprobante.Trim());
            }

            // La consulta rápida de F9 no debe abrir una segunda búsqueda pesada
            // sobre los movimientos para cada página. El detalle del pedido sigue
            // verificando las cobranzas; para búsquedas acotadas conservamos el
            // fallback legacy para mostrar la seña histórica en el listado.
            if (idsSinResumenLegacy.Count > 0 && buscar.Length > 0)
            {
                var referencias = idsSinResumenLegacy.Select(x => $"Cob.NP {x}").ToArray();
                var anticipos = await cn.QueryAsync<AnticipoListadoRow>(new CommandDefinition("""
                    SELECT CASE
                               WHEN LTRIM(RTRIM(ISNULL(PRVENTA, ''))) <> '' THEN LTRIM(RTRIM(PRVENTA))
                               ELSE REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(OBSERVACIONES, N'')))), N'Cob.NP ', N'')
                           END AS IdNotaPedido,
                           SUM(CONVERT(decimal(18,2), ISNULL(IMPORTE, 0))) AS Importe
                    FROM dbo.V_MV_CPTE
                    WHERE LTRIM(RTRIM(TC)) IN ('CB', 'CBCT', 'CBFP')
                      AND ISNULL(ANULADA, 0) = 0
                      AND (LTRIM(RTRIM(ISNULL(PRVENTA, ''))) IN @Ids
                           OR LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(OBSERVACIONES, N'')))) IN @Referencias)
                    GROUP BY CASE
                               WHEN LTRIM(RTRIM(ISNULL(PRVENTA, ''))) <> '' THEN LTRIM(RTRIM(PRVENTA))
                               ELSE REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(max), ISNULL(OBSERVACIONES, N'')))), N'Cob.NP ', N'')
                           END;
                    """, new { Ids = idsSinResumenLegacy, Referencias = referencias }, cancellationToken: token));
                foreach (var anticipo in anticipos)
                    anticiposPorNp[anticipo.IdNotaPedido.Trim()] = anticipo.Importe;
            }

            // F9 de VB6 devuelve primero solo las NP. La preparación y las
            // cobranzas se consultan al abrir el comprobante seleccionado.
            // Evitamos hacer un APPLY por fila sobre tablas de movimientos.
            return new PuntoVentaNpPaginaDto
            {
                Items = items.Select(x => new PuntoVentaNpResumenDto
                {
                    Id = x.Id, IdComprobante = x.IdComprobante, Cuenta = x.Cuenta, Cliente = x.Cliente,
                    Fecha = x.Fecha, FechaEntrega = x.FechaEntrega, Telefono = x.Telefono, ImporteComercial = x.ImporteComercial,
                    TotalAnticiposVigentes = anticiposPorNp.TryGetValue(x.IdComprobante.Trim(), out var anticipo) ? anticipo : null,
                    TotalAnticiposVerificado = anticiposPorNp.ContainsKey(x.IdComprobante.Trim()),
                    EstadoAnticipos = anticiposPorNp.ContainsKey(x.IdComprobante.Trim()) ? "VERIFICADO" : "SIN_SEÑA", EstadoComprobante = x.EstadoComprobante,
                    EstadoProduccion = x.EstadoProduccion, EstadoEntrega = x.EstadoEntrega
                }).ToList(),
                // No se ejecuta COUNT(*) OVER(): F9 debe traer la primera página sin recorrer
                // todas las NP. El total es suficiente para habilitar la navegación y se ajusta
                // al llegar a la última página.
                Total = offset + items.Count + (tieneSiguiente ? 1 : 0),
                TieneSiguiente = tieneSiguiente,
                Pagina = pagina, TamanoPagina = tamano
            };
        }, "No se pudieron consultar los encargos.", ct);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private static async Task<bool> ExisteColumnaAsync(
        SqlConnection cn,
        string objeto,
        string columna,
        CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN COL_LENGTH(@Objeto, @Columna) IS NULL THEN 0 ELSE 1 END;
            """;

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Objeto = $"dbo.{objeto}", Columna = columna },
            cancellationToken: ct)) == 1;
    }

    public Task<PuntoVentaNpAnticiposDto?> GetNotaPedidoAsync(string idComprobante, string? cuenta = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetNotaPedido", async token =>
        {
            var comprobante = (idComprobante ?? string.Empty).Trim();
            var cuentaNormalizada = (cuenta ?? string.Empty).Trim();
            if (comprobante.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            // Algunas instalaciones legacy no exponen todavía estos campos en las
            // tablas de movimientos. La consulta del detalle no debe fallar por una
            // columna opcional: los anticipos se leen de V_MV_CPTE y no dependen de
            // la información de preparación ni de la nota de una línea.
            var tieneFechaPreparacion = await ExisteColumnaAsync(cn, "V_MV_CPTE", "FechaPreparacion", token);
            var tieneCantidadPreparada = await ExisteColumnaAsync(cn, "V_MV_CPTEINSUMOS", "CANTIDADPREPARADA", token);
            var tieneCantidadEntregada = await ExisteColumnaAsync(cn, "V_MV_CPTEINSUMOS", "EntregadoMostrador", token);
            var tienePresentacion = await ExisteColumnaAsync(cn, "V_MV_CPTEINSUMOS", "PRESENTACION", token);
            var tieneObservacionesLinea = await ExisteColumnaAsync(cn, "V_MV_CPTEINSUMOS", "OBSERVACIONES", token);
            var fechaPreparacionSql = tieneFechaPreparacion
                ? "c.FECHAPREPARACION"
                : "CONVERT(datetime, NULL)";
            var observacionesLineaSql = tieneObservacionesLinea
                ? "LTRIM(RTRIM(ISNULL(i.OBSERVACIONES, '')))"
                : "CONVERT(nvarchar(4000), N'')";
            var cantidadPreparadaSql = tieneCantidadPreparada
                ? "CONVERT(decimal(18,4), ISNULL(i.CANTIDADPREPARADA, 0))"
                : "CONVERT(decimal(18,4), 0)";
            var cantidadEntregadaSql = tieneCantidadEntregada
                ? "CONVERT(decimal(18,4), ISNULL(i.EntregadoMostrador, 0))"
                : "CONVERT(decimal(18,4), 0)";
            var presentacionSql = tienePresentacion
                ? "LTRIM(RTRIM(ISNULL(i.PRESENTACION, '')))"
                : "CONVERT(nvarchar(50), N'')";

            var cabeceraSql = $"""
                SELECT TOP (1)
                    c.ID,
                    CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                    LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) AS Cliente,
                    LTRIM(RTRIM(ISNULL(c.TELEFONO, ''))) AS Telefono,
                    LTRIM(RTRIM(CONVERT(nvarchar(max),
                        COALESCE(NULLIF(CONVERT(nvarchar(max), c.OBSERVACIONES), N''),
                                 CONVERT(nvarchar(max), ISNULL(c.COMENTARIOS, N'')))))) AS Observaciones,
                    c.FECHA AS Fecha,
                    c.FECHAENTREGA AS FechaEntrega,
                    {fechaPreparacionSql} AS FechaPreparacion,
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

            var items = (await cn.QueryAsync<PuntoVentaNpItemDto>(new CommandDefinition($"""
                SELECT
                    i.ID AS LineaId,
                    LTRIM(RTRIM(ISNULL(i.IDARTICULO, ''))) AS IdArticulo,
                    LTRIM(RTRIM(ISNULL(i.DESCRIPCION, ''))) AS Descripcion,
                    CONVERT(decimal(18,4), ISNULL(i.CANTIDAD, 0)) AS Cantidad,
                    CONVERT(decimal(18,2), ISNULL(i.IMPORTE_UD, i.COSTO)) AS Precio,
                    CONVERT(decimal(18,2), ISNULL(i.TOTAL, i.IMPORTE)) AS Importe,
                    {cantidadPreparadaSql} AS CantidadPreparada,
                    {cantidadEntregadaSql} AS CantidadEntregada,
                    {presentacionSql} AS Presentacion,
                    {observacionesLineaSql} AS Observaciones
                FROM dbo.V_MV_CPTEINSUMOS i
                WHERE LTRIM(RTRIM(i.TC)) = 'NP'
                  AND LTRIM(RTRIM(i.IDCOMPROBANTE)) = @Comprobante
                  AND ISNULL(i.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY i.SECUENCIA, i.ID;
                """, new { Comprobante = cabecera.IdComprobante, IdComplemento = cabecera.IdComplemento }, cancellationToken: token))).AsList();

            const string cobranzasSql = """
                SELECT
                    c.ID,
                    CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(c.PRVENTA, ''))) AS PrVenta,
                    LTRIM(RTRIM(CONVERT(nvarchar(max),
                        COALESCE(NULLIF(CONVERT(nvarchar(max), c.COMENTARIOS), N''),
                                 CONVERT(nvarchar(max), ISNULL(c.OBSERVACIONES, N'')))))) AS Observaciones,
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

            List<NpCobranza> cobranzas;
            try
            {
                cobranzas = (await cn.QueryAsync<NpCobranza>(new CommandDefinition(
                    cobranzasSql,
                    new
                    {
                        PrVenta = cabecera.IdComprobante,
                        LegacyObservation = $"Cob.NP {cabecera.IdComprobante}",
                        Cuenta = cabecera.Cuenta
                    },
                    cancellationToken: token))).AsList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudieron leer las cobranzas relacionadas de la NP {Comprobante}; se continúa con el detalle comercial.", cabecera.IdComprobante);
                cobranzas = [];
            }

            cobranzas = cobranzas
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .OrderBy(x => x.Fecha)
                .ThenBy(x => x.Id)
                .ToList();

            IReadOnlyList<NpAplicacion> aplicaciones;
            try
            {
                aplicaciones = cobranzas.Count == 0
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudieron leer las aplicaciones de anticipos de la NP {Comprobante}; se continúa sin aplicaciones.", cabecera.IdComprobante);
                aplicaciones = Array.Empty<NpAplicacion>();
            }

            IReadOnlyList<ComprobanteAccionDto> acciones;
            try
            {
                acciones = (await cn.QueryAsync<ComprobanteAccionDto>(new CommandDefinition("""
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudieron leer las acciones de la NP {Comprobante}; se continúa sin historial.", cabecera.IdComprobante);
                acciones = Array.Empty<ComprobanteAccionDto>();
            }

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
                FechaEntrega = cabecera.FechaEntrega,
                FechaPreparacion = cabecera.FechaPreparacion,
                Telefono = cabecera.Telefono,
                Observaciones = cabecera.Observaciones,
                ImporteComercial = cabecera.ImporteComercial,
                EstadoComprobante = Estado(cabecera),
                Cobranzas = resultadoCobranzas,
                TotalAnticiposVigentes = total.Total,
                TotalAnticiposVerificado = total.Verificado,
                EstadoAnticipos = total.Estado,
                EstadoProduccion = EstadoProduccion(cabecera, items),
                Acciones = acciones,
                Items = items
            };
        }, "No se pudieron consultar los anticipos de la nota de pedido.", ct);

    public Task<PuntoVentaNpAnticiposDto?> GetNotaPedidoParaFacturarAsync(string idComprobante, string? cuenta = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetNotaPedidoParaFacturar", async token =>
        {
            var comprobante = (idComprobante ?? string.Empty).Trim();
            var cuentaNormalizada = (cuenta ?? string.Empty).Trim();
            if (comprobante.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var tieneObservacionesLinea = await ExisteColumnaAsync(cn, "V_MV_CPTEINSUMOS", "OBSERVACIONES", token);
            var observacionesLineaSql = tieneObservacionesLinea
                ? "LTRIM(RTRIM(ISNULL(i.OBSERVACIONES, N'')))"
                : "CONVERT(nvarchar(4000), N'')";

            const string cabeceraSql = """
                SELECT TOP (1)
                    c.ID,
                    CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    LTRIM(RTRIM(ISNULL(c.CUENTA, ''))) AS Cuenta,
                    LTRIM(RTRIM(ISNULL(c.NOMBRE, ''))) AS Cliente,
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
                cabeceraSql, new { Comprobante = comprobante, Cuenta = cuentaNormalizada }, cancellationToken: token));
            if (cabecera is null)
                return null;

            var items = (await cn.QueryAsync<PuntoVentaNpItemDto>(new CommandDefinition($"""
                SELECT
                    i.ID AS LineaId,
                    LTRIM(RTRIM(ISNULL(i.IDARTICULO, ''))) AS IdArticulo,
                    LTRIM(RTRIM(ISNULL(i.DESCRIPCION, ''))) AS Descripcion,
                    CONVERT(decimal(18,4), ISNULL(i.CANTIDAD, 0)) AS Cantidad,
                    CONVERT(decimal(18,2), CASE WHEN ISNULL(i.CANTIDAD, 0) = 0 THEN ISNULL(i.IMPORTE, 0)
                        ELSE ISNULL(i.TOTAL, i.IMPORTE) / NULLIF(i.CANTIDAD, 0) END) AS Precio,
                    CONVERT(decimal(18,2), ISNULL(i.TOTAL, i.IMPORTE)) AS Importe,
                    CONVERT(decimal(18,4), 0) AS CantidadEntregada,
                    CONVERT(nvarchar(100), N'') AS Presentacion,
                    {observacionesLineaSql} AS Observaciones
                FROM dbo.V_MV_CPTEINSUMOS i
                WHERE LTRIM(RTRIM(i.TC)) = 'NP'
                  AND LTRIM(RTRIM(i.IDCOMPROBANTE)) = @Comprobante
                  AND ISNULL(i.IDCOMPLEMENTO, 0) = @IdComplemento
                ORDER BY i.SECUENCIA, i.ID;
                """, new { Comprobante = cabecera.IdComprobante, IdComplemento = cabecera.IdComplemento }, cancellationToken: token))).AsList();

            var anticipo = await cn.QuerySingleOrDefaultAsync<PuntoVentaNpCobranzaDto>(new CommandDefinition("""
                SELECT TOP (1)
                    c.ID AS Id,
                    CONVERT(int, ISNULL(c.IDCOMPLEMENTO, 0)) AS IdComplemento,
                    LTRIM(RTRIM(c.TC)) AS Tc,
                    LTRIM(RTRIM(c.IDCOMPROBANTE)) AS IdComprobante,
                    CONVERT(decimal(18,2), ISNULL(c.IMPORTE, 0)) AS ImporteOriginal,
                    CONVERT(bit, ISNULL(c.ANULADA, 0)) AS Anulada
                FROM dbo.V_MV_CPTE c
                WHERE LTRIM(RTRIM(c.TC)) IN ('CB', 'CBCT', 'CBFP')
                  AND LTRIM(RTRIM(ISNULL(c.PRVENTA, ''))) = @Comprobante
                  AND ISNULL(c.ANULADA, 0) = 0
                ORDER BY c.FECHA, c.ID;
                """, new { Comprobante = cabecera.IdComprobante }, cancellationToken: token));

            return new PuntoVentaNpAnticiposDto
            {
                Id = cabecera.Id,
                Tc = cabecera.Tc,
                IdComprobante = cabecera.IdComprobante,
                Cuenta = cabecera.Cuenta,
                Cliente = cabecera.Cliente,
                Fecha = cabecera.Fecha,
                FechaEntrega = cabecera.FechaEntrega,
                ImporteComercial = cabecera.ImporteComercial,
                EstadoComprobante = Estado(cabecera),
                AnticipoAplicable = anticipo,
                Items = items
            };
        }, "No se pudo cargar la nota de pedido para facturar.", ct);

    private static string Estado(NpCabecera cabecera)
        => cabecera.Anulada ? "ANULADA"
            : cabecera.Bloqueada ? "BLOQUEADA"
            : cabecera.Finalizada ? "FINALIZADA"
            : cabecera.Cerrada ? "CERRADA"
            : "PENDIENTE";

    private sealed class PuntoVentaNpResumenRow
    {
        public int Id { get; init; }
        public int IdComplemento { get; init; }
        public string IdComprobante { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Cliente { get; init; } = string.Empty;
        public string Telefono { get; init; } = string.Empty;
        public string Comentarios { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public DateTime? FechaEntrega { get; init; }
        public decimal ImporteComercial { get; init; }
        public decimal? TotalAnticiposVigentes { get; init; }
        public bool TotalAnticiposVerificado { get; init; }
        public string EstadoAnticipos { get; init; } = string.Empty;
        public string EstadoComprobante { get; init; } = string.Empty;
        public string EstadoProduccion { get; init; } = string.Empty;
        public string EstadoEntrega { get; init; } = string.Empty;
        public int Total { get; init; }
    }

    private sealed class AnticipoListadoRow
    {
        public string IdNotaPedido { get; init; } = string.Empty;
        public decimal Importe { get; init; }
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
            var mensaje = ex is SqlException { Number: -2 }
                ? $"{userMessage} La consulta tardó más de lo permitido; probá nuevamente o acotá los filtros."
                : userMessage;
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, mensaje, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(mensaje, incidentId, ex);
        }
    }

    private sealed class NpCabecera
    {
        public int Id { get; init; }
        public int IdComplemento { get; init; }
        public string Tc { get; init; } = string.Empty;
        public string IdComprobante { get; init; } = string.Empty;
        public string Cuenta { get; init; } = string.Empty;
        public string Cliente { get; init; } = string.Empty;
        public string Telefono { get; init; } = string.Empty;
        public DateTime? Fecha { get; init; }
        public DateTime? FechaEntrega { get; init; }
        public DateTime? FechaPreparacion { get; init; }
        public string Observaciones { get; init; } = string.Empty;
        public decimal ImporteComercial { get; init; }
        public bool Anulada { get; init; }
        public bool Bloqueada { get; init; }
        public bool Finalizada { get; init; }
        public bool Cerrada { get; init; }
    }

    private static string EstadoProduccion(NpCabecera cabecera, IReadOnlyList<PuntoVentaNpItemDto> items)
    {
        if (cabecera.Anulada || cabecera.Bloqueada || items.Count == 0)
            return "PENDIENTE";

        var cantidadTotal = items.Sum(x => x.Cantidad);
        var cantidadPreparada = items.Sum(x => Math.Max(x.CantidadPreparada, 0m));
        if (cabecera.Finalizada || cantidadTotal > 0m && cantidadPreparada >= cantidadTotal)
            return "LISTO";
        return cantidadPreparada > 0m ? "EN_PREPARACION" : "PENDIENTE";
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

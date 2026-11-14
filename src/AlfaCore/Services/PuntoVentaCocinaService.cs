using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaCocinaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ILogger<PuntoVentaCocinaService> logger) : IPuntoVentaCocinaService
{
    private const string ModuleName = "PuntoVentaCocina";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private const string ComandaSelect = """
        SELECT c.ID AS IdComanda, c.IDPEDIDO AS IdPedido, c.DESTINO AS Destino, c.FECHAHORA AS FechaHora,
            ISNULL(m.NOMBRE, ISNULL(p.NOMBRECLIENTE, '')) AS NombreMesa, ISNULL(s.NOMBRE, '') AS NombreSector,
            ISNULL(LTRIM(RTRIM(v.Nombre)), ISNULL(LTRIM(RTRIM(p.MOZO)), '')) AS Mozo,
            p.CUBIERTOS AS Cubiertos, c.IMPRESA AS Impresa
        FROM dbo.POS_COMANDA c
        INNER JOIN dbo.POS_PEDIDO p ON p.ID = c.IDPEDIDO
        LEFT JOIN dbo.POS_MESA m ON m.ID = p.IDMESA
        LEFT JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
        LEFT JOIN dbo.V_TA_VENDEDORES v ON LTRIM(RTRIM(v.IdVendedor)) = LTRIM(RTRIM(p.MOZO))
        """;

    private const string LineasSelect = """
        SELECT ID AS Id, ISNULL(IDCOMANDA, 0) AS IdComanda, LTRIM(RTRIM(IDARTICULO)) AS IdArticulo,
            ISNULL(DESCRIPCION, '') AS Descripcion, CAST(CANTIDAD AS decimal(18, 4)) AS Cantidad,
            ISNULL(OBSERVACIONES, '') AS Observaciones, ISNULL(ESTADO, 'ENVIADO') AS Estado, ISNULL(ORIGEN, '') AS Origen
        FROM dbo.POS_PEDIDO_DET
        WHERE IDCOMANDA IN @Ids
        ORDER BY ID;
        """;

    public Task<IReadOnlyList<PuntoVentaCocinaComandaDto>> GetComandasAsync(int idPuntoVenta, string? destino, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetComandas", async token =>
        {
            await using var cn = await OpenAsync(token);
            var comandas = (await cn.QueryAsync<PuntoVentaCocinaComandaDto>(new CommandDefinition($"""
                {ComandaSelect}
                WHERE c.IDPUNTOVENTA = @IdPuntoVenta
                  AND (@Destino = '' OR c.DESTINO = @Destino)
                  AND c.FECHAHORA >= DATEADD(HOUR, -16, GETDATE())
                  AND EXISTS (SELECT 1 FROM dbo.POS_PEDIDO_DET d
                              WHERE d.IDCOMANDA = c.ID AND d.ESTADO IN ('ENVIADO', 'EN_PREPARACION', 'LISTO'))
                ORDER BY c.FECHAHORA, c.ID;
                """, new { IdPuntoVenta = idPuntoVenta, Destino = NormalizeDestino(destino) }, cancellationToken: token))).ToList();

            await CargarLineasAsync(cn, comandas, soloActivas: true, token);
            return (IReadOnlyList<PuntoVentaCocinaComandaDto>)comandas.Where(c => c.Lineas.Count > 0).ToList();
        }, "No se pudieron cargar las comandas.", ct);

    public Task<PuntoVentaCocinaComandaDto?> GetComandaAsync(int idComanda, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetComanda", async token =>
        {
            await using var cn = await OpenAsync(token);
            var comanda = await cn.QuerySingleOrDefaultAsync<PuntoVentaCocinaComandaDto>(new CommandDefinition(
                $"{ComandaSelect} WHERE c.ID = @Id;", new { Id = idComanda }, cancellationToken: token));
            if (comanda is null)
                return null;
            await CargarLineasAsync(cn, [comanda], soloActivas: false, token);
            return comanda;
        }, "No se pudo cargar la comanda.", ct);

    public Task AvanzarLineaAsync(int idLinea, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("AvanzarLinea", async token =>
        {
            await using var cn = await OpenAsync(token);
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO_DET
                SET ESTADO = CASE ESTADO WHEN 'ENVIADO' THEN 'EN_PREPARACION'
                                         WHEN 'EN_PREPARACION' THEN 'LISTO'
                                         ELSE 'EN_PREPARACION' END,
                    FECHAHORA_LISTO = CASE ESTADO WHEN 'EN_PREPARACION' THEN GETDATE() ELSE FECHAHORA_LISTO END
                WHERE ID = @Id AND ESTADO IN ('ENVIADO', 'EN_PREPARACION', 'LISTO');
                """, new { Id = idLinea }, cancellationToken: token));
            if (affected == 0)
                throw new InvalidOperationException("El plato ya no está en preparación (lo sirvieron o lo anularon).");
            return true;
        }, "No se pudo actualizar el plato.", ct);

    public Task MarcarComandaAsync(int idComanda, string estado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("MarcarComanda", async token =>
        {
            var nuevo = (estado ?? string.Empty).Trim().ToUpperInvariant();
            if (nuevo is not (PuntoVentaSalonLineaEstadoKeys.EnPreparacion or PuntoVentaSalonLineaEstadoKeys.Listo or PuntoVentaSalonLineaEstadoKeys.Servido))
                throw new InvalidOperationException("El estado indicado no es válido.");

            await using var cn = await OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO_DET
                SET ESTADO = @Estado,
                    FECHAHORA_LISTO = CASE WHEN @Estado = 'LISTO' AND FECHAHORA_LISTO IS NULL THEN GETDATE() ELSE FECHAHORA_LISTO END
                WHERE IDCOMANDA = @Id AND ESTADO IN ('ENVIADO', 'EN_PREPARACION', 'LISTO');
                """, new { Id = idComanda, Estado = nuevo }, cancellationToken: token));
            return true;
        }, "No se pudo actualizar la comanda.", ct);

    public Task<IReadOnlyList<PuntoVentaCocinaComandaDto>> ReclamarParaImprimirAsync(int idPuntoVenta, string? destino, string estacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("ReclamarParaImprimir", async token =>
        {
            await using var cn = await OpenAsync(token);

            // READPAST: si otra estacion esta reclamando la misma comanda, esta la saltea.
            var ids = (await cn.QueryAsync<int>(new CommandDefinition("""
                UPDATE c
                SET IMPRESA = 1, FECHAHORA_IMPRESION = GETDATE(), ESTACION = @Estacion
                OUTPUT INSERTED.ID
                FROM dbo.POS_COMANDA c WITH (UPDLOCK, READPAST)
                WHERE c.IDPUNTOVENTA = @IdPuntoVenta
                  AND c.IMPRESA = 0
                  AND (@Destino = '' OR c.DESTINO = @Destino)
                  AND c.FECHAHORA >= DATEADD(MINUTE, -60, GETDATE());
                """, new
            {
                IdPuntoVenta = idPuntoVenta,
                Destino = NormalizeDestino(destino),
                Estacion = Truncate((estacion ?? string.Empty).Trim(), 50)
            }, cancellationToken: token))).ToList();

            if (ids.Count == 0)
                return (IReadOnlyList<PuntoVentaCocinaComandaDto>)[];

            var comandas = (await cn.QueryAsync<PuntoVentaCocinaComandaDto>(new CommandDefinition(
                $"{ComandaSelect} WHERE c.ID IN @Ids ORDER BY c.ID;", new { Ids = ids }, cancellationToken: token))).ToList();
            await CargarLineasAsync(cn, comandas, soloActivas: false, token);
            return (IReadOnlyList<PuntoVentaCocinaComandaDto>)comandas;
        }, "No se pudieron leer las comandas para imprimir.", ct);

    public Task<IReadOnlyList<PuntoVentaAgotadoDto>> GetAgotadosAsync(int idPuntoVenta, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetAgotados", async token =>
        {
            await using var cn = await OpenAsync(token);
            var rows = await cn.QueryAsync<PuntoVentaAgotadoDto>(new CommandDefinition("""
                SELECT x.IDARTICULO AS IdArticulo, ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), x.IDARTICULO) AS Descripcion,
                    x.FECHAHORA AS FechaHora, ISNULL(x.USUARIO, '') AS Usuario
                FROM dbo.POS_ARTICULO_AGOTADO x
                LEFT JOIN dbo.V_MA_ARTICULOS a ON LTRIM(RTRIM(a.IdArticulo)) = x.IDARTICULO
                WHERE x.IDPUNTOVENTA = @IdPuntoVenta
                ORDER BY x.FECHAHORA DESC;
                """, new { IdPuntoVenta = idPuntoVenta }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaAgotadoDto>)rows.ToList();
        }, "No se pudieron cargar los agotados.", ct);

    public Task SetAgotadoAsync(int idPuntoVenta, string idArticulo, bool agotado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("SetAgotado", async token =>
        {
            var id = (idArticulo ?? string.Empty).Trim();
            if (id.Length == 0)
                throw new InvalidOperationException("Elegí el artículo.");

            await using var cn = await OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(agotado
                ? """
                  IF NOT EXISTS (SELECT 1 FROM dbo.POS_ARTICULO_AGOTADO WHERE IDPUNTOVENTA = @IdPuntoVenta AND IDARTICULO = @IdArticulo)
                      INSERT INTO dbo.POS_ARTICULO_AGOTADO (IDPUNTOVENTA, IDARTICULO, FECHAHORA, USUARIO)
                      VALUES (@IdPuntoVenta, @IdArticulo, GETDATE(), @Usuario);
                  """
                : "DELETE FROM dbo.POS_ARTICULO_AGOTADO WHERE IDPUNTOVENTA = @IdPuntoVenta AND IDARTICULO = @IdArticulo;",
                new { IdPuntoVenta = idPuntoVenta, IdArticulo = id, Usuario = NormalizeUser(usuarioAccion) },
                cancellationToken: token));

            try
            {
                await appEvents.LogAuditAsync(ModuleName, agotado ? "Agotar" : "Reponer", "POS_ARTICULO_AGOTADO", $"{idPuntoVenta}/{id}",
                    agotado ? $"Artículo {id} marcado como agotado." : $"Artículo {id} disponible otra vez.", null, token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo auditar el agotado {IdArticulo}.", id);
            }
            return true;
        }, "No se pudo actualizar el artículo agotado.", ct);

    // ------------------------------------------------------------------ helpers

    private static async Task CargarLineasAsync(SqlConnection cn, List<PuntoVentaCocinaComandaDto> comandas, bool soloActivas, CancellationToken ct)
    {
        if (comandas.Count == 0)
            return;

        var lineas = (await cn.QueryAsync<PuntoVentaCocinaLineaDto>(new CommandDefinition(
            LineasSelect, new { Ids = comandas.Select(c => c.IdComanda).ToList() }, cancellationToken: ct))).ToList();

        foreach (var comanda in comandas)
        {
            comanda.Lineas = lineas
                .Where(l => l.IdComanda == comanda.IdComanda)
                .Where(l => soloActivas
                    ? l.Estado is PuntoVentaSalonLineaEstadoKeys.Enviado or PuntoVentaSalonLineaEstadoKeys.EnPreparacion or PuntoVentaSalonLineaEstadoKeys.Listo
                    : l.Estado != PuntoVentaSalonLineaEstadoKeys.Anulado)
                .ToList();
        }
    }

    private static string NormalizeDestino(string? destino)
        => (destino ?? string.Empty).Trim().ToUpperInvariant();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}

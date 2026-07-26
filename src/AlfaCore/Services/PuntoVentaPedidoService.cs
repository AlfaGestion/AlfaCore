using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaPedidoService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaPedidoValidator validator,
    ILogger<PuntoVentaPedidoService> logger) : IPuntoVentaPedidoService
{
    private const string ModuleName = "PuntoVentaPedido";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<PuntoVentaPedidoDto>> GetPedidosAsync(int idPuntoVenta, string? estado = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPedidos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var estadoNormalizado = string.IsNullOrWhiteSpace(estado) ? null : estado.Trim().ToUpperInvariant();

            const string sql = """
                SELECT
                    p.ID, ISNULL(p.TC, '') AS Tc, ISNULL(p.IDCOMPROBANTE, '') AS IdComprobante,
                    p.IDPUNTOVENTA, p.MODALIDAD,
                    ISNULL(p.IDCLIENTE, '') AS IdCliente, p.NOMBRECLIENTE,
                    ISNULL(p.TELEFONO, '') AS Telefono, ISNULL(p.DIRECCION, '') AS Direccion,
                    p.FECHA, p.HORARIOPROGRAMADO, p.ESTADO,
                    ISNULL(p.OBSERVACIONES, '') AS Observaciones,
                    ISNULL(p.USUARIO, '') AS Usuario,
                    p.FECHAHORA_ALTA, p.FECHAHORA_MODIFICACION,
                    ISNULL((SELECT SUM(d.CANTIDAD * d.PRECIO) FROM dbo.POS_PEDIDO_DET d WHERE d.IDPEDIDO = p.ID), 0) AS Total
                FROM dbo.POS_PEDIDO p
                WHERE p.IDPUNTOVENTA = @IdPuntoVenta
                  AND (@Estado IS NULL OR p.ESTADO = @Estado)
                ORDER BY p.FECHA DESC, p.ID DESC;
                """;

            var rows = await cn.QueryAsync<PuntoVentaPedidoDto>(new CommandDefinition(
                sql, new { IdPuntoVenta = idPuntoVenta, Estado = estadoNormalizado }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaPedidoDto>)rows.ToList();
        }, "No se pudieron cargar los pedidos.", ct);

    public Task<PuntoVentaPedidoDto?> GetPedidoByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPedidoById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            return await LoadPedidoAsync(cn, id, token);
        }, "No se pudo cargar el pedido seleccionado.", ct);

    private static async Task<PuntoVentaPedidoDto?> LoadPedidoAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string headerSql = """
            SELECT
                p.ID, ISNULL(p.TC, '') AS Tc, ISNULL(p.IDCOMPROBANTE, '') AS IdComprobante,
                p.IDPUNTOVENTA, p.MODALIDAD,
                ISNULL(p.IDCLIENTE, '') AS IdCliente, p.NOMBRECLIENTE,
                ISNULL(p.TELEFONO, '') AS Telefono, ISNULL(p.DIRECCION, '') AS Direccion,
                p.FECHA, p.HORARIOPROGRAMADO, p.ESTADO,
                ISNULL(p.OBSERVACIONES, '') AS Observaciones,
                ISNULL(p.USUARIO, '') AS Usuario,
                p.FECHAHORA_ALTA, p.FECHAHORA_MODIFICACION
            FROM dbo.POS_PEDIDO p
            WHERE p.ID = @Id;
            """;

        var pedido = await cn.QuerySingleOrDefaultAsync<PuntoVentaPedidoDto>(new CommandDefinition(headerSql, new { Id = id }, cancellationToken: ct));
        if (pedido is null)
            return null;

        const string itemsSql = """
            SELECT
                ID, IDARTICULO AS IdArticulo, ISNULL(DESCRIPCION, '') AS Descripcion,
                ISNULL(IDUNIDAD, '') AS IdUnidad, ISNULL(PRESENTACION, '') AS Presentacion,
                CANTIDAD AS Cantidad, PRECIO AS Precio, TASAIVA AS TasaIva, EXENTO AS Exento,
                ISNULL(FAMILIA, '') AS Familia, ISNULL(OBSERVACIONES, '') AS Observaciones
            FROM dbo.POS_PEDIDO_DET
            WHERE IDPEDIDO = @Id
            ORDER BY ID;
            """;

        var items = await cn.QueryAsync<PuntoVentaPedidoItemDto>(new CommandDefinition(itemsSql, new { Id = id }, cancellationToken: ct));
        pedido.Items = items.ToList();
        pedido.Total = pedido.Items.Sum(i => i.Subtotal);
        return pedido;
    }

    public Task<int> SavePedidoAsync(PuntoVentaPedidoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePedido", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.NombreCliente = (request.NombreCliente ?? string.Empty).Trim();
            request.Modalidad = (request.Modalidad ?? string.Empty).Trim().ToUpperInvariant();
            request.IdCliente = (request.IdCliente ?? string.Empty).Trim();
            request.Telefono = (request.Telefono ?? string.Empty).Trim();
            request.Direccion = (request.Direccion ?? string.Empty).Trim();
            request.Observaciones = (request.Observaciones ?? string.Empty).Trim();

            var validation = await validator.ValidatePedidoForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            try
            {
                var usuario = NormalizeUser(request.UsuarioAccion);
                var isNew = !request.Id.HasValue || request.Id.Value <= 0;
                int idPedido;

                if (isNew)
                {
                    const string insertSql = """
                        INSERT INTO dbo.POS_PEDIDO
                            (IDPUNTOVENTA, MODALIDAD, IDCLIENTE, NOMBRECLIENTE, TELEFONO, DIRECCION,
                             FECHA, HORARIOPROGRAMADO, ESTADO, OBSERVACIONES, USUARIO, FECHAHORA_ALTA)
                        VALUES
                            (@IdPuntoVenta, @Modalidad, @IdCliente, @NombreCliente, @Telefono, @Direccion,
                             @Fecha, @HorarioProgramado, @Estado, @Observaciones, @Usuario, GETDATE());
                        SELECT CAST(SCOPE_IDENTITY() AS int);
                        """;
                    idPedido = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                    {
                        request.IdPuntoVenta,
                        request.Modalidad,
                        IdCliente = request.IdCliente,
                        request.NombreCliente,
                        request.Telefono,
                        request.Direccion,
                        request.Fecha,
                        request.HorarioProgramado,
                        Estado = PuntoVentaPedidoEstadoKeys.Pendiente,
                        request.Observaciones,
                        Usuario = usuario
                    }, transaction: (SqlTransaction)tx, cancellationToken: token));
                }
                else
                {
                    idPedido = request.Id!.Value;
                    const string updateSql = """
                        UPDATE dbo.POS_PEDIDO
                        SET IDPUNTOVENTA = @IdPuntoVenta,
                            MODALIDAD = @Modalidad,
                            IDCLIENTE = @IdCliente,
                            NOMBRECLIENTE = @NombreCliente,
                            TELEFONO = @Telefono,
                            DIRECCION = @Direccion,
                            FECHA = @Fecha,
                            HORARIOPROGRAMADO = @HorarioProgramado,
                            OBSERVACIONES = @Observaciones,
                            USUARIO = @Usuario,
                            FECHAHORA_MODIFICACION = GETDATE()
                        WHERE ID = @Id;
                        """;
                    var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
                    {
                        Id = idPedido,
                        request.IdPuntoVenta,
                        request.Modalidad,
                        IdCliente = request.IdCliente,
                        request.NombreCliente,
                        request.Telefono,
                        request.Direccion,
                        request.Fecha,
                        request.HorarioProgramado,
                        request.Observaciones,
                        Usuario = usuario
                    }, transaction: (SqlTransaction)tx, cancellationToken: token));

                    if (affected <= 0)
                        throw new InvalidOperationException("No se encontró el pedido para actualizar.");

                    await cn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM dbo.POS_PEDIDO_DET WHERE IDPEDIDO = @Id;",
                        new { Id = idPedido }, transaction: (SqlTransaction)tx, cancellationToken: token));
                }

                const string insertItemSql = """
                    INSERT INTO dbo.POS_PEDIDO_DET
                        (IDPEDIDO, IDARTICULO, DESCRIPCION, IDUNIDAD, PRESENTACION, CANTIDAD, PRECIO, TASAIVA, EXENTO, FAMILIA, OBSERVACIONES)
                    VALUES
                        (@IdPedido, @IdArticulo, @Descripcion, @IdUnidad, @Presentacion, @Cantidad, @Precio, @TasaIva, @Exento, @Familia, @Observaciones);
                    """;

                foreach (var item in request.Items)
                {
                    await cn.ExecuteAsync(new CommandDefinition(insertItemSql, new
                    {
                        IdPedido = idPedido,
                        item.IdArticulo,
                        item.Descripcion,
                        item.IdUnidad,
                        item.Presentacion,
                        item.Cantidad,
                        item.Precio,
                        item.TasaIva,
                        item.Exento,
                        item.Familia,
                        item.Observaciones
                    }, transaction: (SqlTransaction)tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
                return idPedido;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar el pedido.", ct);

    public Task CambiarEstadoAsync(int id, string nuevoEstado, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CambiarEstado", async token =>
        {
            var estado = (nuevoEstado ?? string.Empty).Trim().ToUpperInvariant();
            if (!PuntoVentaPedidoEstadoKeys.All.Contains(estado))
                throw new InvalidOperationException("El estado indicado no es válido.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO
                SET ESTADO = @Estado, USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """,
                new { Id = id, Estado = estado, Usuario = NormalizeUser(usuarioAccion) },
                cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el pedido indicado.");

            return true;
        }, "No se pudo actualizar el estado del pedido.", ct);

    public Task MarcarFacturadoAsync(int idPedido, string tc, string idComprobante, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MarcarFacturado", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO
                SET TC = @Tc, IDCOMPROBANTE = @IdComprobante, ESTADO = @Estado,
                    USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """,
                new
                {
                    Id = idPedido,
                    Tc = tc,
                    IdComprobante = idComprobante,
                    Estado = PuntoVentaPedidoEstadoKeys.Entregado,
                    Usuario = NormalizeUser(usuarioAccion)
                },
                cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el pedido para marcar como facturado.");

            return true;
        }, "No se pudo marcar el pedido como facturado.", ct);

    public Task AnularPedidoAsync(int id, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AnularPedido", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.POS_PEDIDO
                SET ESTADO = @Estado, USUARIO = @Usuario, FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """,
                new { Id = id, Estado = PuntoVentaPedidoEstadoKeys.Anulado, Usuario = NormalizeUser(usuarioAccion) },
                cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el pedido indicado.");

            return true;
        }, "No se pudo anular el pedido.", ct);

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string BuildValidationMessage(ValidationResult validation)
    {
        var issues = validation.Issues
            .Select(issue => (issue.Message ?? string.Empty).Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return issues.Count == 0
            ? "Revisá los campos marcados antes de guardar."
            : string.Join(" ", issues);
    }

    private async Task<T> ExecuteLoggedAsync<T>(string module, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", module, action);
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
}

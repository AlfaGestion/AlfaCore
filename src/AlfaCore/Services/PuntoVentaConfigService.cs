using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaConfigService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaConfigValidator validator,
    ILogger<PuntoVentaConfigService> logger) : IPuntoVentaConfigService
{
    private const string ModuleName = "PuntoVentaConfig";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<PuntoVentaEntidadDto>> GetPuntosVentaAsync(bool soloActivos = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPuntosVenta", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, NOMBRE, MODO,
                    ISNULL(UNEGOCIO, '') AS Unegocio,
                    ISNULL(IDCAJA, '') AS IdCaja,
                    ISNULL(SUCURSAL, '') AS Sucursal,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                {(soloActivos ? "WHERE ACTIVO = 1" : string.Empty)}
                ORDER BY NOMBRE, CODIGO;
                """;

            var rows = await cn.QueryAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaEntidadDto>)rows.ToList();
        }, "No se pudieron cargar los puntos de venta.", ct);

    public Task<PuntoVentaEntidadDto?> GetPuntoVentaByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPuntoVentaById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                SELECT
                    ID, CODIGO, NOMBRE, MODO,
                    ISNULL(UNEGOCIO, '') AS Unegocio,
                    ISNULL(IDCAJA, '') AS IdCaja,
                    ISNULL(SUCURSAL, '') AS Sucursal,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                WHERE ID = @Id;
                """;

            return await cn.QuerySingleOrDefaultAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, new { Id = id }, cancellationToken: token));
        }, "No se pudo cargar el punto de venta seleccionado.", ct);

    public Task<int> SavePuntoVentaAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePuntoVenta", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Modo = (request.Modo ?? string.Empty).Trim().ToUpperInvariant();
            request.Unegocio = (request.Unegocio ?? string.Empty).Trim();
            request.IdCaja = (request.IdCaja ?? string.Empty).Trim();
            request.Sucursal = (request.Sucursal ?? string.Empty).Trim();

            var validation = await validator.ValidatePuntoVentaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;
            var usuario = (request.UsuarioAccion ?? string.Empty).Trim();

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_PUNTOVENTA
                        (CODIGO, NOMBRE, MODO, UNEGOCIO, IDCAJA, SUCURSAL, ACTIVO, USUARIO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @Nombre, @Modo, @Unegocio, @IdCaja, @Sucursal, @Activo, @Usuario, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.Nombre,
                    request.Modo,
                    request.Unegocio,
                    request.IdCaja,
                    request.Sucursal,
                    request.Activo,
                    Usuario = usuario
                }, cancellationToken: token));
            }

            const string updateSql = """
                UPDATE dbo.POS_PUNTOVENTA
                SET CODIGO = @Codigo,
                    NOMBRE = @Nombre,
                    MODO = @Modo,
                    UNEGOCIO = @Unegocio,
                    IDCAJA = @IdCaja,
                    SUCURSAL = @Sucursal,
                    ACTIVO = @Activo,
                    USUARIO = @Usuario,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.Nombre,
                request.Modo,
                request.Unegocio,
                request.IdCaja,
                request.Sucursal,
                request.Activo,
                Usuario = usuario
            }, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el punto de venta para actualizar.");

            return request.Id!.Value;
        }, "No se pudo guardar el punto de venta.", ct);

    public Task<IReadOnlyList<PuntoVentaSectorDto>> GetSectoresAsync(int idPuntoVenta, bool soloActivos = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSectores", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, IDPUNTOVENTA, NOMBRE,
                    ISNULL(ICONO, '') AS Icono,
                    ISNULL(IMAGEN, '') AS Imagen,
                    ORDEN, ACTIVO
                FROM dbo.POS_SECTOR
                WHERE IDPUNTOVENTA = @IdPuntoVenta
                {(soloActivos ? "AND ACTIVO = 1" : string.Empty)}
                ORDER BY ORDEN, NOMBRE;
                """;

            var rows = await cn.QueryAsync<PuntoVentaSectorDto>(new CommandDefinition(sql, new { idPuntoVenta }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaSectorDto>)rows.ToList();
        }, "No se pudieron cargar los sectores del punto de venta.", ct);

    public Task<int> SaveSectorAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveSector", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Icono = (request.Icono ?? string.Empty).Trim();
            request.Imagen = (request.Imagen ?? string.Empty).Trim();

            var validation = await validator.ValidateSectorForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_SECTOR
                        (CODIGO, IDPUNTOVENTA, NOMBRE, ICONO, IMAGEN, ORDEN, ACTIVO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @IdPuntoVenta, @Nombre, @Icono, @Imagen, @Orden, @Activo, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.IdPuntoVenta,
                    request.Nombre,
                    request.Icono,
                    request.Imagen,
                    request.Orden,
                    request.Activo
                }, cancellationToken: token));
            }

            const string updateSql = """
                UPDATE dbo.POS_SECTOR
                SET CODIGO = @Codigo,
                    IDPUNTOVENTA = @IdPuntoVenta,
                    NOMBRE = @Nombre,
                    ICONO = @Icono,
                    IMAGEN = @Imagen,
                    ORDEN = @Orden,
                    ACTIVO = @Activo,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.IdPuntoVenta,
                request.Nombre,
                request.Icono,
                request.Imagen,
                request.Orden,
                request.Activo
            }, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el sector para actualizar.");

            return request.Id!.Value;
        }, "No se pudo guardar el sector.", ct);

    public Task<IReadOnlyList<PuntoVentaMesaDto>> GetMesasAsync(int idSector, bool soloActivas = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetMesas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, IDSECTOR, NOMBRE, CAPACIDAD,
                    POSX AS PosX, POSY AS PosY, WIDTH, HEIGHT,
                    ISNULL(ICONO, '') AS Icono,
                    ISNULL(IMAGEN, '') AS Imagen,
                    ACTIVO
                FROM dbo.POS_MESA
                WHERE IDSECTOR = @IdSector
                {(soloActivas ? "AND ACTIVO = 1" : string.Empty)}
                ORDER BY NOMBRE, CODIGO;
                """;

            var rows = await cn.QueryAsync<PuntoVentaMesaDto>(new CommandDefinition(sql, new { idSector }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaMesaDto>)rows.ToList();
        }, "No se pudieron cargar las mesas del sector.", ct);

    public Task<int> SaveMesaAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveMesa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Icono = (request.Icono ?? string.Empty).Trim();
            request.Imagen = (request.Imagen ?? string.Empty).Trim();

            var validation = await validator.ValidateMesaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_MESA
                        (CODIGO, IDSECTOR, NOMBRE, CAPACIDAD, POSX, POSY, WIDTH, HEIGHT, ICONO, IMAGEN, ACTIVO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @IdSector, @Nombre, @Capacidad, @PosX, @PosY, @Width, @Height, @Icono, @Imagen, @Activo, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.IdSector,
                    request.Nombre,
                    request.Capacidad,
                    request.PosX,
                    request.PosY,
                    request.Width,
                    request.Height,
                    request.Icono,
                    request.Imagen,
                    request.Activo
                }, cancellationToken: token));
            }

            const string updateSql = """
                UPDATE dbo.POS_MESA
                SET CODIGO = @Codigo,
                    IDSECTOR = @IdSector,
                    NOMBRE = @Nombre,
                    CAPACIDAD = @Capacidad,
                    POSX = @PosX,
                    POSY = @PosY,
                    WIDTH = @Width,
                    HEIGHT = @Height,
                    ICONO = @Icono,
                    IMAGEN = @Imagen,
                    ACTIVO = @Activo,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.IdSector,
                request.Nombre,
                request.Capacidad,
                request.PosX,
                request.PosY,
                request.Width,
                request.Height,
                request.Icono,
                request.Imagen,
                request.Activo
            }, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró la mesa para actualizar.");

            return request.Id!.Value;
        }, "No se pudo guardar la mesa.", ct);

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

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

    public Task<PuntoVentaCatalogosDto> GetCatalogosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCatalogos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var unidades = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) AS Valor, LTRIM(RTRIM(Descripcion)) AS Descripcion
                FROM dbo.V_TA_UnidadNegocio
                WHERE LTRIM(RTRIM(ISNULL(Codigo, ''))) <> ''
                ORDER BY Codigo;
                """, cancellationToken: token))).ToList();

            var cajas = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdCajas)) AS Valor,
                       LTRIM(RTRIM(IdCajas)) + CASE WHEN LTRIM(RTRIM(ISNULL(Descripcion, ''))) = '' THEN '' ELSE ' · ' + LTRIM(RTRIM(Descripcion)) END AS Descripcion
                FROM dbo.V_Ta_Cajas
                WHERE LTRIM(RTRIM(ISNULL(IdCajas, ''))) <> ''
                ORDER BY IdCajas;
                """, cancellationToken: token))).ToList();

            var sucursales = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT DISTINCT RIGHT('0000' + CONVERT(varchar(20), Sucursal), 4) AS Valor,
                       RIGHT('0000' + CONVERT(varchar(20), Sucursal), 4) AS Descripcion
                FROM (
                    SELECT A_SUC_DEFAULT AS Sucursal FROM dbo.V_TA_CPTE
                    UNION ALL SELECT B_SUC_DEFAULT FROM dbo.V_TA_CPTE
                    UNION ALL SELECT C_SUC_DEFAULT FROM dbo.V_TA_CPTE
                    UNION ALL SELECT X_SUC_DEFAULT FROM dbo.V_TA_CPTE
                ) AS sucursales
                WHERE ISNULL(Sucursal, 0) > 0
                ORDER BY Valor;
                """, cancellationToken: token))).ToList();

            var sucursalConfigurada = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(VALOR, '')))
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'TPV_SUCURSAL';
                """, cancellationToken: token));
            AgregarOpcion(sucursales, sucursalConfigurada, NormalizarCodigo(sucursalConfigurada));

            var sucursalesGuardadas = await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT LTRIM(RTRIM(ISNULL(SUCURSAL, '')))
                FROM dbo.POS_PUNTOVENTA
                WHERE LTRIM(RTRIM(ISNULL(SUCURSAL, ''))) <> '';
                """, cancellationToken: token));
            foreach (var sucursal in sucursalesGuardadas)
                AgregarOpcion(sucursales, sucursal, NormalizarCodigo(sucursal));

            var listas = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.V_MA_PreciosCab') IS NOT NULL
                    SELECT LTRIM(RTRIM(IdLista)) AS Valor,
                           LTRIM(RTRIM(IdLista)) + CASE WHEN LTRIM(RTRIM(ISNULL(Nombre, ''))) = '' THEN '' ELSE ' · ' + LTRIM(RTRIM(Nombre)) END AS Descripcion
                    FROM dbo.V_MA_PreciosCab
                    WHERE TipoLista = 'V' AND LTRIM(RTRIM(ISNULL(IdLista, ''))) <> ''
                    ORDER BY IdLista;
                """, cancellationToken: token))).ToList();

            var rubros = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.V_TA_Rubros') IS NOT NULL
                    SELECT LTRIM(RTRIM(IdRubro)) AS Valor,
                           LTRIM(RTRIM(ISNULL(Descripcion, IdRubro))) AS Descripcion
                    FROM dbo.V_TA_Rubros
                    WHERE LTRIM(RTRIM(ISNULL(IdRubro, ''))) <> ''
                    ORDER BY Descripcion, IdRubro;
                """, cancellationToken: token))).ToList();

            return new PuntoVentaCatalogosDto
            {
                Sucursales = sucursales.OrderBy(x => x.Valor).ToList(),
                Cajas = cajas,
                UnidadesNegocio = unidades,
                ListasPrecios = listas,
                Rubros = rubros
            };
        }, "No se pudieron cargar los catálogos de puntos de venta.", ct);

    private static void AgregarOpcion(List<PuntoVentaCatalogoOpcionDto> opciones, string? valor, string? descripcion)
    {
        var normalizado = NormalizarCodigo(valor);
        if (normalizado.Length == 0 || opciones.Any(x => string.Equals(x.Valor, normalizado, StringComparison.OrdinalIgnoreCase)))
            return;

        opciones.Add(new PuntoVentaCatalogoOpcionDto { Valor = normalizado, Descripcion = descripcion?.Trim() is { Length: > 0 } texto ? texto : normalizado });
    }

    private static string NormalizarCodigo(string? valor)
    {
        var texto = (valor ?? string.Empty).Trim();
        return int.TryParse(texto, out var numero) && numero >= 0 && numero <= 9999
            ? numero.ToString("0000")
            : texto;
    }

    public Task<string> GetComprobanteHabitualAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetComprobanteHabitual", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var clave = BuildComprobanteHabitualKey();
            var valor = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(VALOR)), '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """, new { Clave = clave }, cancellationToken: token));

            return NormalizeComprobanteHabitual(valor);
        }, "No se pudo leer el comprobante habitual del punto de venta.", ct);

    public Task SaveComprobanteHabitualAsync(string comprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveComprobanteHabitual", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var clave = BuildComprobanteHabitualKey();
            var valor = NormalizeComprobanteHabitual(comprobante) switch
            {
                "FP" => "FP",
                "NCFP" => "NCFP",
                "NC" => "NC",
                _ => "eFC"
            };
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION
                SET VALOR = @Valor, FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion)
                    VALUES (N'DATOS', @Clave, @Valor, GETDATE());
                END;
                """, new { Clave = clave, Valor = valor }, cancellationToken: token));

            return affected;
        }, "No se pudo guardar el comprobante habitual del punto de venta.", ct);

    private static string BuildComprobanteHabitualKey()
        => $"{Environment.MachineName.Trim()}_GOUR_CPTE_PC".ToUpperInvariant();

    private static string NormalizeComprobanteHabitual(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "FP" => "FP",
            "NCFP" => "NCFP",
            "NC" => "NC",
            _ => "FC"
        };

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
                    LTRIM(RTRIM(ISNULL(IDLISTA, ''))) AS IdLista,
                    CLASEPRECIO AS ClasePrecio,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                {(soloActivos ? "WHERE ACTIVO = 1" : string.Empty)}
                ORDER BY NOMBRE, CODIGO;
                """;

            var rows = (await cn.QueryAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, cancellationToken: token))).ToList();
            var rubros = await GetRubrosPorPuntoVentaAsync(cn, null, token);
            foreach (var row in rows)
                row.Rubros = rubros.TryGetValue(row.Id, out var lista) ? lista : [];
            return (IReadOnlyList<PuntoVentaEntidadDto>)rows;
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
                    LTRIM(RTRIM(ISNULL(IDLISTA, ''))) AS IdLista,
                    CLASEPRECIO AS ClasePrecio,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                WHERE ID = @Id;
                """;

            var pv = await cn.QuerySingleOrDefaultAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, new { Id = id }, cancellationToken: token));
            if (pv is not null)
                pv.Rubros = (await GetRubrosPorPuntoVentaAsync(cn, id, token)).TryGetValue(id, out var lista) ? lista : [];
            return pv;
        }, "No se pudo cargar el punto de venta seleccionado.", ct);

    private static async Task<Dictionary<int, List<string>>> GetRubrosPorPuntoVentaAsync(SqlConnection cn, int? id, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<(int IdPuntoVenta, string IdRubro)>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.POS_PUNTOVENTA_RUBRO', N'U') IS NOT NULL
                SELECT IDPUNTOVENTA, LTRIM(RTRIM(IDRUBRO))
                FROM dbo.POS_PUNTOVENTA_RUBRO
                WHERE @Id IS NULL OR IDPUNTOVENTA = @Id
                ORDER BY IDPUNTOVENTA, IDRUBRO;
            """, new { Id = id }, cancellationToken: ct));
        return rows.GroupBy(r => r.IdPuntoVenta).ToDictionary(g => g.Key, g => g.Select(r => r.IdRubro).ToList());
    }

    private static async Task GuardarRubrosAsync(SqlConnection cn, SqlTransaction tx, int id, IEnumerable<string> rubros, CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.POS_PUNTOVENTA_RUBRO WHERE IDPUNTOVENTA = @Id;", new { Id = id }, tx, cancellationToken: ct));
        foreach (var rubro in rubros.Select(r => (r ?? string.Empty).Trim()).Where(r => r.Length is > 0 and <= 10).Distinct(StringComparer.OrdinalIgnoreCase))
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.POS_PUNTOVENTA_RUBRO (IDPUNTOVENTA, IDRUBRO) VALUES (@Id, @Rubro);",
                new { Id = id, Rubro = rubro }, tx, cancellationToken: ct));
    }

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
            request.IdLista = (request.IdLista ?? string.Empty).Trim();
            if (request.ClasePrecio is < 1 or > 8)
                request.ClasePrecio = null;
            request.Rubros ??= [];

            var validation = await validator.ValidatePuntoVentaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;
            var usuario = (request.UsuarioAccion ?? string.Empty).Trim();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_PUNTOVENTA
                        (CODIGO, NOMBRE, MODO, UNEGOCIO, IDCAJA, SUCURSAL, IDLISTA, CLASEPRECIO, ACTIVO, USUARIO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @Nombre, @Modo, @Unegocio, @IdCaja, @Sucursal, @IdLista, @ClasePrecio, @Activo, @Usuario, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                var nuevoId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.Nombre,
                    request.Modo,
                    request.Unegocio,
                    request.IdCaja,
                    request.Sucursal,
                    request.IdLista,
                    request.ClasePrecio,
                    request.Activo,
                    Usuario = usuario
                }, tx, cancellationToken: token));
                await GuardarRubrosAsync(cn, tx, nuevoId, request.Rubros, token);
                await tx.CommitAsync(token);
                return nuevoId;
            }

            const string updateSql = """
                UPDATE dbo.POS_PUNTOVENTA
                SET CODIGO = @Codigo,
                    NOMBRE = @Nombre,
                    MODO = @Modo,
                    UNEGOCIO = @Unegocio,
                    IDCAJA = @IdCaja,
                    SUCURSAL = @Sucursal,
                    IDLISTA = @IdLista,
                    CLASEPRECIO = @ClasePrecio,
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
                request.IdLista,
                request.ClasePrecio,
                request.Activo,
                Usuario = usuario
            }, tx, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el punto de venta para actualizar.");

            await GuardarRubrosAsync(cn, tx, request.Id!.Value, request.Rubros, token);
            await tx.CommitAsync(token);
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
                    ISNULL(FORMATO, 'CUADRADA') AS Formato,
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
            request.Formato = PuntoVentaMesaFormatoKeys.Normalizar(request.Formato);

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
                        (CODIGO, IDSECTOR, NOMBRE, CAPACIDAD, POSX, POSY, WIDTH, HEIGHT, FORMATO, ICONO, IMAGEN, ACTIVO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @IdSector, @Nombre, @Capacidad, @PosX, @PosY, @Width, @Height, @Formato, @Icono, @Imagen, @Activo, GETDATE());
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
                    request.Formato,
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
                    FORMATO = @Formato,
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
                request.Formato,
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

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class AutorizacionTareasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IAutorizacionTareasService
{
    private const string ModuleName = "AutorizacionTareas";
    private const string DefaultUnidadNegocioVisual = "1";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosAsync(string sistemaPermisos, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<UsuarioSistemaDto>>(ModuleName, "GetUsuarios", async token =>
        {
            if (string.IsNullOrWhiteSpace(sistemaPermisos))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_USUARIOS", token))
                return [];

            var hasActivo = await ColumnExistsAsync(cn, "TA_USUARIOS", "Activo", token);
            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, '') AS Nombre,
                    ISNULL(SISTEMA, '') AS Sistema,
                    {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")} AS Activo,
                    ISNULL(Administrador, 0) AS Administrador,
                    ISNULL(EsGrupo, 0) AS EsGrupo
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND ISNULL(EsGrupo, 0) = 0
                  {(hasActivo ? "AND ISNULL(Activo, 1) = 1" : string.Empty)}
                ORDER BY NOMBRE;
                """;

            var rows = await cn.QueryAsync<UsuarioSistemaDto>(new CommandDefinition(sql, new
            {
                Sistema = sistemaPermisos.Trim().ToUpperInvariant()
            }, cancellationToken: token));

            return rows.ToArray();
        }, "No se pudieron cargar los usuarios del sistema.", ct);

    public Task<AutorizacionTareasDto> GetAutorizacionAsync(string menuSistema, string sistemaPermisos, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetAutorizacion", async token =>
        {
            if (string.IsNullOrWhiteSpace(menuSistema))
                throw new InvalidOperationException("No se recibió el menú del sistema.");
            if (string.IsNullOrWhiteSpace(sistemaPermisos))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");
            if (string.IsNullOrWhiteSpace(usuario))
                throw new InvalidOperationException("Seleccioná un usuario antes de consultar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_MENU", token) || !await TableExistsAsync(cn, "TA_TAREAS", token))
                throw new InvalidOperationException("La base activa no tiene completas las tablas de menú o permisos.");

            var userData = await LoadUserDataAsync(cn, sistemaPermisos, usuario, token);
            var unidadesNegocio = await LoadUnidadesNegocioAsync(cn, token);
            var cajas = await LoadCajasAsync(cn, token);
            var depositos = await LoadDepositosAsync(cn, token);
            var unidadNegocio = ResolveUnidadNegocio(userData.UNegocio, unidadesNegocio);
            var idCaja = ResolveOptionalCatalogValue(userData.IdCaja, cajas);
            var idDeposito = ResolveOptionalCatalogValue(userData.IdDeposito, depositos);

            var hasDescripcionMenu = await ColumnExistsAsync(cn, "TA_MENU", "Descripcion", token);
            var sql = $"""
                SELECT
                    m.Menu,
                    ISNULL(m.Titulo, '') AS Titulo,
                    ISNULL(m.Clave, '') AS Clave,
                    ISNULL(m.Nombre, '') AS Nombre,
                    {(hasDescripcionMenu ? "ISNULL(CAST(m.Descripcion AS nvarchar(max)), '')" : "''")} AS Descripcion,
                    ISNULL(m.Proceso, '') AS Proceso,
                    ISNULL(m.Habilitado, 1) AS Habilitado,
                    ISNULL(m.Orden, m.Clave) AS OrdenMenu,
                    ISNULL(w.RutaWeb, '') AS RutaWeb,
                    ISNULL(w.HabilitadoWeb, 0) AS HabilitadoWeb,
                    CASE WHEN t.TAREA IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS Autorizado
                FROM dbo.TA_MENU m
                LEFT JOIN dbo.ALFACORE_MENU_WEB w
                    ON w.Menu = m.Menu
                   AND w.Clave = m.Clave
                LEFT JOIN dbo.TA_TAREAS t
                    ON UPPER(LTRIM(RTRIM(t.USUARIO))) = @Usuario
                   AND UPPER(LTRIM(RTRIM(t.SISTEMA))) = @SistemaPermisos
                   AND UPPER(LTRIM(RTRIM(t.TAREA))) = UPPER(LTRIM(RTRIM(m.Clave)))
                WHERE UPPER(LTRIM(RTRIM(m.Menu))) = @MenuSistema
                ORDER BY m.Orden, m.Clave, m.Nombre;
                """;

            var rows = (await cn.QueryAsync<MenuAutorizacionRow>(new CommandDefinition(sql, new
            {
                Usuario = usuario.Trim().ToUpperInvariant(),
                SistemaPermisos = sistemaPermisos.Trim().ToUpperInvariant(),
                MenuSistema = menuSistema.Trim().ToUpperInvariant()
            }, cancellationToken: token))).ToList();

            var nodeByKey = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
                .GroupBy(x => x.Clave!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new MenuAutorizacionItemDto
                {
                    Menu = g.First().Menu?.Trim() ?? string.Empty,
                    Clave = g.Key,
                    Titulo = g.First().Titulo?.Trim() ?? string.Empty,
                    Nombre = g.First().Nombre?.Trim() ?? g.Key,
                    Descripcion = g.First().Descripcion?.Trim() ?? string.Empty,
                    Proceso = string.IsNullOrWhiteSpace(g.First().Proceso) ? null : g.First().Proceso?.Trim(),
                    Habilitado = g.First().Habilitado,
                    RutaWeb = string.IsNullOrWhiteSpace(g.First().RutaWeb) ? null : g.First().RutaWeb?.Trim(),
                    HabilitadoWeb = g.First().HabilitadoWeb,
                    Autorizado = g.First().Autorizado
                }, StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodeByKey.Values)
                node.Hijos.Clear();

            var roots = new List<MenuAutorizacionItemDto>();
            foreach (var node in nodeByKey.Values.OrderBy(x => ParseOrder(rows, x.Clave)).ThenBy(x => x.Nombre))
            {
                if (string.Equals(node.Clave, "D", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(node.Titulo) && nodeByKey.TryGetValue(node.Titulo, out var parent))
                {
                    if (string.Equals(parent.Clave, "D", StringComparison.OrdinalIgnoreCase))
                    {
                        roots.Add(node);
                        continue;
                    }

                    parent.Hijos.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            SortTree(roots, rows);

            return new AutorizacionTareasDto
            {
                MenuSistema = menuSistema.Trim().ToUpperInvariant(),
                PermisosSistema = sistemaPermisos.Trim().ToUpperInvariant(),
                Usuario = usuario.Trim(),
                Administrador = userData.Administrador,
                UNegocio = unidadNegocio,
                IdCaja = idCaja,
                IdDeposito = idDeposito,
                UnidadesNegocio = unidadesNegocio,
                Cajas = cajas,
                Depositos = depositos,
                Items = roots
            };
        }, "No se pudo cargar la autorización de tareas.", ct);

    public Task GuardarAutorizacionAsync(GuardarAutorizacionTareasRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GuardarAutorizacion", async token =>
        {
            if (request is null)
                throw new InvalidOperationException("No se recibió la autorización a guardar.");
            if (string.IsNullOrWhiteSpace(request.MenuSistema))
                throw new InvalidOperationException("No se recibió el menú del sistema.");
            if (string.IsNullOrWhiteSpace(request.PermisosSistema))
                throw new InvalidOperationException("No se recibió el sistema de permisos.");
            if (string.IsNullOrWhiteSpace(request.Usuario))
                throw new InvalidOperationException("Seleccioná un usuario antes de grabar.");
            if (string.IsNullOrWhiteSpace(request.UNegocio))
                throw new InvalidOperationException("La unidad de negocio es obligatoria.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_MENU", token) || !await TableExistsAsync(cn, "TA_TAREAS", token))
                throw new InvalidOperationException("La base activa no tiene completas las tablas de menú o permisos.");

            var unidadesNegocio = await LoadUnidadesNegocioAsync(cn, token);
            var cajas = await LoadCajasAsync(cn, token);
            var depositos = await LoadDepositosAsync(cn, token);
            var unidadNegocio = ResolveUnidadNegocio(request.UNegocio, unidadesNegocio);
            var idCaja = ResolveOptionalCatalogValue(request.IdCaja, cajas);
            var idDeposito = ResolveOptionalCatalogValue(request.IdDeposito, depositos);
            var hasAdministrador = await ColumnExistsAsync(cn, "TA_USUARIOS", "Administrador", token);
            var hasIdCaja = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDCAJA", token);
            var hasUNegocio = await ColumnExistsAsync(cn, "TA_USUARIOS", "UNegocio", token);
            var hasIdDeposito = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDDEPOSITO", token);

            var managedKeys = (await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT UPPER(LTRIM(RTRIM(Clave)))
                FROM dbo.TA_MENU
                WHERE UPPER(LTRIM(RTRIM(Menu))) = @MenuSistema
                  AND ISNULL(Habilitado, 1) = 1
                  AND ISNULL(Clave, '') <> '';
                """, new
            {
                MenuSistema = request.MenuSistema.Trim().ToUpperInvariant()
            }, cancellationToken: token)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var parentRows = (await cn.QueryAsync<(string Clave, string Titulo)>(new CommandDefinition("""
                SELECT
                    UPPER(LTRIM(RTRIM(ISNULL(Clave, '')))) AS Clave,
                    UPPER(LTRIM(RTRIM(ISNULL(Titulo, '')))) AS Titulo
                FROM dbo.TA_MENU
                WHERE UPPER(LTRIM(RTRIM(Menu))) = @MenuSistema
                  AND ISNULL(Clave, '') <> '';
                """, new
            {
                MenuSistema = request.MenuSistema.Trim().ToUpperInvariant()
            }, cancellationToken: token))).ToList();

            var parentByKey = parentRows
                .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
                .ToDictionary(x => x.Clave, x => x.Titulo, StringComparer.OrdinalIgnoreCase);

            var selected = new HashSet<string>(
                request.TareasSeleccionadas
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Where(x => managedKeys.Contains(x, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

            var expandedSelection = ExpandSelectionWithAncestors(selected, parentByKey, managedKeys);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            try
            {
                await UpdateUserAssignmentsAsync(
                    cn,
                    tx,
                    request.PermisosSistema,
                    request.Usuario,
                    request.Administrador,
                    unidadNegocio,
                    idCaja,
                    idDeposito,
                    hasAdministrador,
                    hasIdCaja,
                    hasUNegocio,
                    hasIdDeposito,
                    token);

                var deleteBase = """
                    DELETE FROM dbo.TA_TAREAS
                    WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                      AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(TAREA))) IN @Managed;
                    """;

                var deleteFiltered = """
                    DELETE FROM dbo.TA_TAREAS
                    WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                      AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(TAREA))) IN @Managed
                      AND UPPER(LTRIM(RTRIM(TAREA))) NOT IN @Selected;
                    """;

                if (expandedSelection.Count == 0)
                {
                    await cn.ExecuteAsync(new CommandDefinition(deleteBase, new
                    {
                        Usuario = request.Usuario.Trim().ToUpperInvariant(),
                        Sistema = request.PermisosSistema.Trim().ToUpperInvariant(),
                        Managed = managedKeys
                    }, tx, cancellationToken: token));
                }
                else
                {
                    await cn.ExecuteAsync(new CommandDefinition(deleteFiltered, new
                    {
                        Usuario = request.Usuario.Trim().ToUpperInvariant(),
                        Sistema = request.PermisosSistema.Trim().ToUpperInvariant(),
                        Managed = managedKeys,
                        Selected = expandedSelection.ToArray()
                    }, tx, cancellationToken: token));

                    const string insertSql = """
                        INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
                        SELECT @Usuario, @Sistema, @Tarea
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM dbo.TA_TAREAS
                            WHERE UPPER(LTRIM(RTRIM(USUARIO))) = @Usuario
                              AND UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                              AND UPPER(LTRIM(RTRIM(TAREA))) = @Tarea
                        );
                        """;

                    foreach (var tarea in expandedSelection.OrderBy(x => x))
                    {
                        await cn.ExecuteAsync(new CommandDefinition(insertSql, new
                        {
                            Usuario = request.Usuario.Trim().ToUpperInvariant(),
                            Sistema = request.PermisosSistema.Trim().ToUpperInvariant(),
                            Tarea = tarea
                        }, tx, cancellationToken: token));
                    }
                }

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar la autorización de tareas.", ct);

    private static string ResolveUnidadNegocio(string? currentValue, IReadOnlyList<AutorizacionCatalogoItemDto> unidadesNegocio)
    {
        var resolved = ResolveOptionalCatalogValue(currentValue, unidadesNegocio);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var preferred = unidadesNegocio.FirstOrDefault(x =>
            string.Equals(x.CodigoVisual, DefaultUnidadNegocioVisual, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
            return preferred.Codigo;

        return unidadesNegocio.FirstOrDefault()?.Codigo ?? string.Empty;
    }

    private static string ResolveOptionalCatalogValue(string? currentValue, IReadOnlyList<AutorizacionCatalogoItemDto> options)
    {
        var normalized = currentValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var match = options.FirstOrDefault(x =>
            string.Equals(x.Codigo.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        return match?.Codigo ?? string.Empty;
    }

    private static async Task<UsuarioAutorizacionRow> LoadUserDataAsync(
        SqlConnection cn,
        string sistemaPermisos,
        string usuario,
        CancellationToken ct)
    {
        var hasAdministrador = await ColumnExistsAsync(cn, "TA_USUARIOS", "Administrador", ct);
        var hasIdCaja = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDCAJA", ct);
        var hasUNegocio = await ColumnExistsAsync(cn, "TA_USUARIOS", "UNegocio", ct);
        var hasIdDeposito = await ColumnExistsAsync(cn, "TA_USUARIOS", "IDDEPOSITO", ct);

        var sql = $"""
            SELECT TOP (1)
                ISNULL(NOMBRE, '') AS Nombre,
                {(hasAdministrador ? "CASE WHEN ISNULL(Administrador, 0) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END" : "CAST(0 AS bit)")} AS Administrador,
                {(hasIdCaja ? "ISNULL(IDCAJA, '')" : "''")} AS IdCaja,
                {(hasUNegocio ? "ISNULL(UNegocio, '')" : "''")} AS UNegocio,
                {(hasIdDeposito ? "ISNULL(IDDEPOSITO, '')" : "''")} AS IdDeposito
            FROM dbo.TA_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
              AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Usuario;
            """;

        var user = await cn.QueryFirstOrDefaultAsync<UsuarioAutorizacionRow>(new CommandDefinition(sql, new
        {
            Sistema = sistemaPermisos.Trim().ToUpperInvariant(),
            Usuario = usuario.Trim().ToUpperInvariant()
        }, cancellationToken: ct));

        if (user is null)
            throw new InvalidOperationException("El usuario seleccionado ya no existe en TA_USUARIOS.");

        return user;
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadUnidadesNegocioAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_UnidadNegocio", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(Codigo, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                '' AS UnidadNegocio,
                CAST(0 AS bit) AS Compartido
            FROM dbo.V_TA_UnidadNegocio
            ORDER BY Codigo;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadCajasAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_CAJAS", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(IDCAJAS, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                ISNULL(UnidadNegocio, '') AS UnidadNegocio,
                CAST(0 AS bit) AS Compartido
            FROM dbo.V_TA_CAJAS
            ORDER BY IDCAJAS;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task<IReadOnlyList<AutorizacionCatalogoItemDto>> LoadDepositosAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await ObjectExistsAsync(cn, "V_TA_DEPOSITO", ct))
            return [];

        var rows = await cn.QueryAsync<AutorizacionCatalogoItemDto>(new CommandDefinition("""
            SELECT
                ISNULL(IDDEPOSITO, '') AS Codigo,
                ISNULL(Descripcion, '') AS Descripcion,
                ISNULL(UnidadNegocio, '') AS UnidadNegocio,
                ISNULL(Compartido, 0) AS Compartido
            FROM dbo.V_TA_DEPOSITO
            ORDER BY IDDEPOSITO;
            """, cancellationToken: ct));

        return rows.ToArray();
    }

    private static async Task UpdateUserAssignmentsAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string sistemaPermisos,
        string usuario,
        bool administrador,
        string unidadNegocio,
        string idCaja,
        string idDeposito,
        bool hasAdministrador,
        bool hasIdCaja,
        bool hasUNegocio,
        bool hasIdDeposito,
        CancellationToken ct)
    {
        var assignments = new List<string>();
        if (hasAdministrador)
            assignments.Add("Administrador = @Administrador");
        if (hasIdCaja)
            assignments.Add("IDCAJA = @IdCaja");
        if (hasUNegocio)
            assignments.Add("UNegocio = @UNegocio");
        if (hasIdDeposito)
            assignments.Add("IDDEPOSITO = @IdDeposito");

        if (assignments.Count == 0)
            return;

        var sql = $"""
            UPDATE dbo.TA_USUARIOS
            SET {string.Join("," + Environment.NewLine + "    ", assignments)}
            WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
              AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Usuario;
            """;

        var affected = await cn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Administrador = administrador,
            IdCaja = DbNullable(idCaja),
            UNegocio = unidadNegocio,
            IdDeposito = DbNullable(idDeposito),
            Sistema = sistemaPermisos.Trim().ToUpperInvariant(),
            Usuario = usuario.Trim().ToUpperInvariant()
        }, tx, cancellationToken: ct));

        if (affected == 0)
            throw new InvalidOperationException("No se encontró el usuario a actualizar en TA_USUARIOS.");
    }

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }

    private async Task ExecuteLoggedAsync(
        string module,
        string action,
        Func<CancellationToken, Task> operation,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{tableName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.objects
            WHERE object_id = OBJECT_ID(@FullName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { FullName = $"dbo.{objectName}" }, cancellationToken: ct));
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@FullName)
              AND LOWER(name) = LOWER(@ColumnName);
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            FullName = $"dbo.{tableName}",
            ColumnName = columnName
        }, cancellationToken: ct));
        return count > 0;
    }

    private static int ParseOrder(IEnumerable<MenuAutorizacionRow> rows, string clave)
    {
        var row = rows.FirstOrDefault(x => string.Equals(x.Clave?.Trim(), clave, StringComparison.OrdinalIgnoreCase));
        var order = row?.OrdenMenu?.Trim();

        if (!string.IsNullOrWhiteSpace(order))
        {
            var digits = new string(order.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed))
                return parsed;
        }

        var fallbackDigits = new string(clave.Where(char.IsDigit).ToArray());
        return int.TryParse(fallbackDigits, out var fallback) ? fallback : int.MaxValue;
    }

    private static void SortTree(List<MenuAutorizacionItemDto> nodes, IReadOnlyList<MenuAutorizacionRow> rows)
    {
        nodes.Sort((a, b) =>
        {
            var left = ParseOrder(rows, a.Clave);
            var right = ParseOrder(rows, b.Clave);
            var compare = left.CompareTo(right);
            return compare != 0 ? compare : string.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes)
            SortTree(node.Hijos, rows);
    }

    private static HashSet<string> ExpandSelectionWithAncestors(
        HashSet<string> selected,
        IReadOnlyDictionary<string, string> parentByKey,
        IReadOnlyCollection<string> managedKeys)
    {
        var expanded = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var managed = new HashSet<string>(managedKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var key in selected.ToArray())
        {
            var current = key;
            while (parentByKey.TryGetValue(current, out var parent) && !string.IsNullOrWhiteSpace(parent))
            {
                if (!managed.Contains(parent))
                    break;

                expanded.Add(parent);
                current = parent;
            }
        }

        return expanded;
    }

    private sealed class MenuAutorizacionRow
    {
        public string? Menu { get; init; }
        public string? Titulo { get; init; }
        public string? Clave { get; init; }
        public string? Nombre { get; init; }
        public string? Descripcion { get; init; }
        public string? Proceso { get; init; }
        public bool Habilitado { get; init; }
        public string? OrdenMenu { get; init; }
        public string? RutaWeb { get; init; }
        public bool HabilitadoWeb { get; init; }
        public bool Autorizado { get; init; }
    }

    private sealed class UsuarioAutorizacionRow
    {
        public string Nombre { get; init; } = string.Empty;
        public bool Administrador { get; init; }
        public string IdCaja { get; init; } = string.Empty;
        public string UNegocio { get; init; } = string.Empty;
        public string IdDeposito { get; init; } = string.Empty;
    }
}

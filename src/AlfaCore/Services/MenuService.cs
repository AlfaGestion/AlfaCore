using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class MenuService(
    IConfiguration configuration,
    ISessionService sessionService,
    IPermissionService permissionService,
    IAppEventService appEvents,
    IActualizacionesService actualizacionesService,
    IAppUserSessionService appUserSession) : IMenuService
{
    private const string CrmModuleKey = "D010185";
    private const string CrmSectionKey = "D010185WEB";
    private const string CrmSectionName = "CRM";
    private const string CrmTicketsKey = "D010185-WEB-TICKETS";
    private const string CrmConversacionesKey = "D010185-WEB-CONVERSACIONES";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<ShellModuleDto>> GetModulesAsync(CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        return menu.Modules;
    }

    public async Task<IReadOnlyList<ShellMenuSearchItemDto>> GetSearchItemsAsync(CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        return BuildSearchItems(menu);
    }

    public async Task<ShellMenuNodeDto?> GetNodeByRouteAsync(string route, CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        var normalized = NormalizeRoute(route);
        return menu.Nodes.FirstOrDefault(x => NormalizeRoute(x.RutaWeb) == normalized);
    }

    public async Task<IReadOnlyList<ShellWorkspaceSectionDto>> GetModuleSectionsAsync(string moduleKey, CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        return BuildSections(menu.Nodes, menu.ParentByKey, menu.NameByKey, moduleKey).ToArray();
    }

    public async Task<ShellModuleDto?> GetModuleByKeyAsync(string moduleKey, CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        return menu.Modules.FirstOrDefault(x => string.Equals(x.Clave, moduleKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ShellRouteContextDto> GetRouteContextAsync(string route, CancellationToken ct = default)
    {
        var menu = await LoadVisibleMenuAsync(ct);
        var node = menu.Nodes.FirstOrDefault(x => NormalizeRoute(x.RutaWeb) == NormalizeRoute(route));
        if (node is null)
            return new ShellRouteContextDto { Modules = menu.Modules };

        var module = ResolveModule(menu.Modules, menu.ParentByKey, node.Clave);
        return new ShellRouteContextDto
        {
            ActiveNode = node,
            Module = module,
            Modules = menu.Modules
        };
    }

    private Task<MenuSnapshot> LoadVisibleMenuAsync(CancellationToken ct)
        => LoadVisibleMenuAsync(ct, allowAutoRepair: true);

    private async Task<MenuSnapshot> LoadVisibleMenuAsync(CancellationToken ct, bool allowAutoRepair)
    {
        return await ExecuteLoggedAsync("Shell", "LoadVisibleMenu", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "TA_MENU", token) || !await TableExistsAsync(cn, "ALFACORE_MENU_WEB", token))
            {
                await cn.CloseAsync();

                if (allowAutoRepair && await TryAutoRepairShellMenuAsync(token))
                    return await LoadVisibleMenuAsync(token, allowAutoRepair: false);

                return EmptySnapshot();
            }

            var hasNombreWeb = await ColumnExistsAsync(cn, "ALFACORE_MENU_WEB", "NombreWeb", token);
            var hasDescripcion = await ColumnExistsAsync(cn, "TA_MENU", "Descripcion", token);
            var nombreExpression = hasNombreWeb
                ? "ISNULL(NULLIF(w.NombreWeb, ''), ISNULL(m.Nombre, ''))"
                : "ISNULL(m.Nombre, '')";
            var descripcionExpression = hasDescripcion
                ? "ISNULL(CAST(m.Descripcion AS nvarchar(max)), '')"
                : "''";

            var sql = $"""
                SELECT
                    m.Menu,
                    ISNULL(m.Titulo, '') AS Titulo,
                    m.Clave,
                    {nombreExpression} AS Nombre,
                    {descripcionExpression} AS Descripcion,
                    ISNULL(m.Proceso, '') AS Proceso,
                    ISNULL(m.Habilitado, 1) AS Habilitado,
                    ISNULL(m.Orden, m.Clave) AS OrdenMenu,
                    ISNULL(w.RutaWeb, '') AS RutaWeb,
                    ISNULL(w.Componente, '') AS Componente,
                    ISNULL(w.Icono, '') AS Icono,
                    ISNULL(w.HabilitadoWeb, 1) AS HabilitadoWeb,
                    ISNULL(w.OrdenWeb, 0) AS OrdenWeb,
                    ISNULL(w.EsFavoritoDefault, 0) AS EsFavoritoDefault,
                    ISNULL(w.Observacion, '') AS Observacion
                FROM dbo.TA_MENU m
                LEFT JOIN dbo.ALFACORE_MENU_WEB w
                    ON w.Menu = m.Menu
                   AND w.Clave = m.Clave
                WHERE ISNULL(m.Habilitado, 1) = 1;
                """;

            var rows = (await cn.QueryAsync<MenuRow>(new CommandDefinition(sql, cancellationToken: token))).ToList();
            var permissionSet = await permissionService.GetAllowedTaskKeysAsync(token);

            var parentByKey = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
                .GroupBy(x => x.Clave.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Titulo?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var nameByKey = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Clave))
                .GroupBy(x => x.Clave.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim() ?? g.Key, StringComparer.OrdinalIgnoreCase);

            var mapped = rows.Where(x => x.HabilitadoWeb && !string.IsNullOrWhiteSpace(x.RutaWeb)).ToList();
            var includeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in mapped)
            {
                var key = row.Clave.Trim();
                if (permissionSet is not null && permissionSet.Count > 0 && !permissionSet.Contains(key))
                    continue;

                includeKeys.Add(key);
                AddAncestors(includeKeys, parentByKey, key);
            }

            if (permissionSet is null || permissionSet.Count == 0)
            {
                foreach (var row in mapped)
                {
                    var key = row.Clave.Trim();
                    includeKeys.Add(key);
                    AddAncestors(includeKeys, parentByKey, key);
                }
            }

            var visibleRows = rows
                .Where(x => includeKeys.Contains(x.Clave.Trim()))
                .ToList();

            var nodes = visibleRows
                .Where(x => x.HabilitadoWeb && !string.IsNullOrWhiteSpace(x.RutaWeb))
                .Select(x => new ShellMenuNodeDto
                {
                    Menu = x.Menu,
                    Clave = x.Clave.Trim(),
                    Titulo = x.Titulo?.Trim() ?? string.Empty,
                    Nombre = x.Nombre?.Trim() ?? string.Empty,
                    Descripcion = x.Descripcion?.Trim() ?? string.Empty,
                    Proceso = x.Proceso?.Trim() ?? string.Empty,
                    RutaWeb = x.RutaWeb?.Trim() ?? string.Empty,
                    Componente = x.Componente?.Trim() ?? string.Empty,
                    Icono = x.Icono?.Trim() ?? "bi-grid",
                    OrdenWeb = x.OrdenWeb > 0 ? x.OrdenWeb : ParseOrder(x.OrdenMenu, x.Clave),
                    EsFavoritoDefault = x.EsFavoritoDefault,
                    Observacion = x.Observacion?.Trim() ?? string.Empty
                })
                .OrderBy(x => x.OrdenWeb)
                .ThenBy(x => x.Nombre)
                .ToList();

            AddSyntheticCrmNodes(nodes, includeKeys, parentByKey, nameByKey);

            var rootModules = visibleRows
                .Where(x => includeKeys.Contains(x.Clave.Trim()))
                .Where(IsShellModuleRow)
                .Select(x => new ShellModuleDto
                {
                    Menu = x.Menu,
                    Clave = x.Clave.Trim(),
                    Nombre = x.Nombre?.Trim() ?? x.Clave.Trim(),
                    RutaWeb = rows.FirstOrDefault(r => string.Equals(r.Clave, x.Clave, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(r.RutaWeb))?.RutaWeb?.Trim()
                              ?? $"/shell/{Uri.EscapeDataString(x.Clave.Trim())}",
                    Icono = rows.FirstOrDefault(r => string.Equals(r.Clave, x.Clave, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(r.Icono))?.Icono?.Trim()
                            ?? "bi-grid",
                    OrdenWeb = ParseOrder(x.OrdenMenu, x.Clave)
                })
                .OrderBy(x => x.OrdenWeb)
                .ThenBy(x => x.Nombre)
                .ToList();

            var crmNode = nodes.FirstOrDefault(x => string.Equals(x.Clave, CrmModuleKey, StringComparison.OrdinalIgnoreCase));
            if (crmNode is not null && rootModules.All(x => !string.Equals(x.Clave, CrmModuleKey, StringComparison.OrdinalIgnoreCase)))
            {
                rootModules.Add(new ShellModuleDto
                {
                    Menu = crmNode.Menu,
                    Clave = crmNode.Clave,
                    Nombre = crmNode.Nombre,
                    RutaWeb = crmNode.RutaWeb,
                    Icono = crmNode.Icono,
                    OrdenWeb = crmNode.OrdenWeb > 0 ? crmNode.OrdenWeb : 18
                });
            }

            nodes = nodes
                .OrderBy(x => x.OrdenWeb)
                .ThenBy(x => x.Nombre)
                .ToList();

            rootModules = rootModules
                .OrderBy(x => x.OrdenWeb)
                .ThenBy(x => x.Nombre)
                .ToList();

            return new MenuSnapshot(nodes, rootModules, parentByKey, nameByKey);
        }, "No se pudo construir el menú web dinámico.", ct);
    }

    private async Task<bool> TryAutoRepairShellMenuAsync(CancellationToken ct)
    {
        try
        {
            await actualizacionesService.ExecutePendingAsync(new ActualizacionesRunRequest
            {
                UsuarioAccion = appUserSession.GetCurrentUserName("SYSTEM"),
                PcAccion = Environment.MachineName,
                EsEjecucionAutomatica = true
            }, ct);

            return true;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                "Shell",
                "AutoRepairMenuUpdates",
                ex,
                "No se pudieron ejecutar automáticamente las actualizaciones del shell web.",
                ct: ct);

            return false;
        }
    }

    private static MenuSnapshot EmptySnapshot()
        => new(
            [],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<ShellWorkspaceSectionDto> BuildSections(
        IReadOnlyList<ShellMenuNodeDto> nodes,
        IReadOnlyDictionary<string, string> parentByKey,
        IReadOnlyDictionary<string, string> nameByKey,
        string moduleKey)
    {
        var items = nodes
            .Where(x => IsDescendantOf(x, moduleKey, parentByKey))
            .Where(x => !string.Equals(x.Clave, moduleKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sections = items
            .GroupBy(x => ResolveSectionKey(x, moduleKey, parentByKey), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                return new ShellWorkspaceSectionDto
                {
                    Clave = g.Key,
                    Nombre = nameByKey.TryGetValue(g.Key, out var sectionName) ? sectionName : "Aplicaciones",
                    Items = g.OrderBy(x => x.OrdenWeb).ThenBy(x => x.Nombre).ToArray()
                };
            })
            .OrderBy(x => items.First(i => string.Equals(ResolveSectionKey(i, moduleKey, parentByKey), x.Clave, StringComparison.OrdinalIgnoreCase)).OrdenWeb)
            .ToArray();

        return sections;
    }

    private static bool IsDescendantOf(ShellMenuNodeDto node, string moduleKey, IReadOnlyDictionary<string, string> parentByKey)
    {
        if (string.Equals(node.Clave, moduleKey, StringComparison.OrdinalIgnoreCase))
            return true;

        var parent = node.Titulo;
        while (!string.IsNullOrWhiteSpace(parent))
        {
            if (string.Equals(parent, moduleKey, StringComparison.OrdinalIgnoreCase))
                return true;

            parent = parentByKey.TryGetValue(parent, out var next) ? next : string.Empty;
        }

        return false;
    }

    private static string ResolveSectionKey(ShellMenuNodeDto node, string moduleKey, IReadOnlyDictionary<string, string> parentByKey)
    {
        if (string.Equals(node.Titulo, moduleKey, StringComparison.OrdinalIgnoreCase))
            return node.Clave;

        var currentKey = node.Clave;
        while (parentByKey.TryGetValue(currentKey, out var parent) && !string.IsNullOrWhiteSpace(parent))
        {
            if (string.Equals(parent, moduleKey, StringComparison.OrdinalIgnoreCase))
                return currentKey;

            if (!parentByKey.TryGetValue(parent, out var parentOfParent))
                return currentKey;

            if (string.Equals(parentOfParent, moduleKey, StringComparison.OrdinalIgnoreCase))
                return parent;

            currentKey = parent;
        }

        return node.Clave;
    }

    private static ShellModuleDto? ResolveModule(IReadOnlyList<ShellModuleDto> modules, IReadOnlyDictionary<string, string> parentByKey, string key)
    {
        var current = key.Trim();
        while (!string.IsNullOrWhiteSpace(current))
        {
            var module = modules.FirstOrDefault(x => string.Equals(x.Clave, current, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
                return module;

            if (!parentByKey.TryGetValue(current, out var parent))
                break;

            current = parent;
        }

        return null;
    }

    private static IReadOnlyList<ShellMenuSearchItemDto> BuildSearchItems(MenuSnapshot menu)
    {
        var items = new List<ShellMenuSearchItemDto>();

        foreach (var module in menu.Modules)
        {
            items.Add(new ShellMenuSearchItemDto
            {
                Menu = module.Menu,
                Clave = module.Clave,
                Nombre = module.Nombre,
                RutaWeb = module.RutaWeb,
                Icono = string.IsNullOrWhiteSpace(module.Icono) ? "bi-grid-3x3-gap-fill" : module.Icono,
                ModuloNombre = module.Nombre,
                SeccionNombre = "Módulo principal",
                Descripcion = "Módulo principal",
                EsModulo = true
            });
        }

        foreach (var node in menu.Nodes)
        {
            var module = ResolveModule(menu.Modules, menu.ParentByKey, node.Clave);
            var sectionName = ResolveSearchSectionName(node, module, menu.ParentByKey, menu.NameByKey);

            items.Add(new ShellMenuSearchItemDto
            {
                Menu = node.Menu,
                Clave = node.Clave,
                Nombre = node.Nombre,
                RutaWeb = node.RutaWeb,
                Icono = string.IsNullOrWhiteSpace(node.Icono) ? "bi-grid" : node.Icono,
                ModuloNombre = module?.Nombre ?? "Aplicaciones",
                SeccionNombre = sectionName,
                Descripcion = BuildSearchDescription(module?.Nombre, sectionName, node.Observacion),
                Observacion = string.IsNullOrWhiteSpace(node.Observacion) ? node.Descripcion : node.Observacion,
                EsModulo = false
            });
        }

        return items
            .GroupBy(x => $"{NormalizeRoute(x.RutaWeb)}|{x.Clave}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.EsModulo ? 0 : 1)
            .ThenBy(x => x.ModuloNombre)
            .ThenBy(x => x.Nombre)
            .ToArray();
    }

    private static string ResolveSearchSectionName(
        ShellMenuNodeDto node,
        ShellModuleDto? module,
        IReadOnlyDictionary<string, string> parentByKey,
        IReadOnlyDictionary<string, string> nameByKey)
    {
        if (module is null)
            return "Aplicaciones";

        var sectionKey = ResolveSectionKey(node, module.Clave, parentByKey);
        if (string.Equals(sectionKey, node.Clave, StringComparison.OrdinalIgnoreCase)
            && string.Equals(node.Titulo, module.Clave, StringComparison.OrdinalIgnoreCase))
        {
            return "Acceso directo";
        }

        return nameByKey.TryGetValue(sectionKey, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "Aplicaciones";
    }

    private static string BuildSearchDescription(string? moduleName, string? sectionName, string? description)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(moduleName))
            parts.Add(moduleName.Trim());

        if (!string.IsNullOrWhiteSpace(sectionName) && !string.Equals(sectionName, "Acceso directo", StringComparison.OrdinalIgnoreCase))
            parts.Add(sectionName.Trim());

        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description.Trim());

        return string.Join(" · ", parts.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsShellModuleRow(MenuRow row)
    {
        var route = NormalizeRoute(row.RutaWeb);
        return string.Equals(row.Titulo?.Trim(), "D", StringComparison.OrdinalIgnoreCase)
               || string.Equals(route, $"/shell/{CrmModuleKey}", StringComparison.OrdinalIgnoreCase)
               || (string.Equals(row.Componente?.Trim(), "ShellWorkspacePage", StringComparison.OrdinalIgnoreCase)
                   && route.StartsWith("/shell/", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddSyntheticCrmNodes(
        List<ShellMenuNodeDto> nodes,
        HashSet<string> includeKeys,
        Dictionary<string, string> parentByKey,
        Dictionary<string, string> nameByKey)
    {
        if (!includeKeys.Contains(CrmModuleKey))
            return;

        parentByKey[CrmSectionKey] = CrmModuleKey;
        nameByKey[CrmSectionKey] = CrmSectionName;
        parentByKey[CrmConversacionesKey] = CrmSectionKey;
        parentByKey[CrmTicketsKey] = CrmSectionKey;
        nameByKey[CrmConversacionesKey] = "Conversaciones";
        nameByKey[CrmTicketsKey] = "Tickets";

        if (nodes.All(x => !string.Equals(x.Clave, CrmConversacionesKey, StringComparison.OrdinalIgnoreCase)))
        {
            var crmModule = nodes.FirstOrDefault(x => string.Equals(x.Clave, CrmModuleKey, StringComparison.OrdinalIgnoreCase));
            nodes.Add(new ShellMenuNodeDto
            {
                Menu = crmModule?.Menu ?? "ALFA",
                Clave = CrmConversacionesKey,
                Titulo = CrmSectionKey,
                Nombre = "Conversaciones",
                Descripcion = "Inbox operativo de conversaciones, seguimiento y atención comercial.",
                RutaWeb = "/conversaciones",
                Componente = "Conversaciones",
                Icono = "bi-chat-left-text-fill",
                OrdenWeb = 18501,
                Observacion = "Atención omnicanal, seguimiento comercial y derivación operativa."
            });
        }

        if (nodes.All(x => !string.Equals(x.Clave, CrmTicketsKey, StringComparison.OrdinalIgnoreCase)))
        {
            var crmModule = nodes.FirstOrDefault(x => string.Equals(x.Clave, CrmModuleKey, StringComparison.OrdinalIgnoreCase));
            nodes.Add(new ShellMenuNodeDto
            {
                Menu = crmModule?.Menu ?? "ALFA",
                Clave = CrmTicketsKey,
                Titulo = CrmSectionKey,
                Nombre = "Tickets",
                Descripcion = "Mesa de ayuda, soporte y seguimiento de incidencias.",
                RutaWeb = "/tickets",
                Componente = "Tickets",
                Icono = "bi-life-preserver",
                OrdenWeb = 18502,
                Observacion = "Gestión de tickets de asistencia, prioridades y resolución."
            });
        }
    }

    private static void AddAncestors(HashSet<string> includeKeys, IReadOnlyDictionary<string, string> parentByKey, string key)
    {
        var current = key;
        while (!string.IsNullOrWhiteSpace(current) && parentByKey.TryGetValue(current, out var parent))
        {
            if (string.IsNullOrWhiteSpace(parent))
                break;

            includeKeys.Add(parent);
            current = parent;
        }
    }

    private static int ParseOrder(string? order, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(order))
        {
            var digits = new string(order.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed))
                return parsed;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            var digits = new string(fallback.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed))
                return parsed;
        }

        return int.MaxValue;
    }

    private static string NormalizeRoute(string? route)
    {
        var value = (route ?? string.Empty).Trim();
        if (value.Length == 0)
            return "/";

        if (!value.StartsWith('/'))
            value = "/" + value;

        return value.TrimEnd('/').Length == 0 ? "/" : value.TrimEnd('/');
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string process,
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
            var incidentId = await appEvents.LogErrorAsync(
                process,
                action,
                ex,
                userMessage,
                ct: ct);

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

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@FullName)
              AND name = @ColumnName;
            """;

        var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            FullName = $"dbo.{tableName}",
            ColumnName = columnName
        }, cancellationToken: ct));
        return count > 0;
    }

    private sealed class MenuRow
    {
        public string Menu { get; init; } = string.Empty;
        public string Titulo { get; init; } = string.Empty;
        public string Clave { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public string Descripcion { get; init; } = string.Empty;
        public string Proceso { get; init; } = string.Empty;
        public bool Habilitado { get; init; }
        public string OrdenMenu { get; init; } = string.Empty;
        public string RutaWeb { get; init; } = string.Empty;
        public string Componente { get; init; } = string.Empty;
        public string Icono { get; init; } = string.Empty;
        public bool HabilitadoWeb { get; init; }
        public int OrdenWeb { get; init; }
        public bool EsFavoritoDefault { get; init; }
        public string Observacion { get; init; } = string.Empty;
    }

    private sealed record MenuSnapshot(
        IReadOnlyList<ShellMenuNodeDto> Nodes,
        IReadOnlyList<ShellModuleDto> Modules,
        IReadOnlyDictionary<string, string> ParentByKey,
        IReadOnlyDictionary<string, string> NameByKey);
}

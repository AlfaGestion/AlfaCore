using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

/// <summary>
/// Aísla las tablas del sistema legacy (Alfa Gestion VB6) mientras se corren los scripts de
/// App_Data/updates. El menu y los permisos del legacy (TA_MENU, TA_TAREAS) y los de AlfaCore
/// (ALFACORE_MENU_WEB, ALFACORE_TAREAS_WEB) son independientes: AlfaCore NUNCA escribe en
/// TA_MENU ni en TA_TAREAS.
///
/// Muchos scripts viejos insertan la opcion web tambien en TA_MENU y se la dan a todos los usuarios
/// en TA_TAREAS. En vez de corregir script por script:
///   1) al empezar la tanda se copian TA_MENU y TA_TAREAS a tablas propias de AlfaCore
///      (ALFACORE_SANDBOX_TA_MENU / ALFACORE_SANDBOX_TA_TAREAS);
///   2) antes de ejecutar cada lote, <see cref="Rewrite"/> cambia en el texto del script las
///      referencias a TA_MENU / TA_TAREAS por esas copias: los scripts leen y escriben la copia
///      (los que insertan en ALFACORE_MENU_WEB leyendo la fila que crearon en TA_MENU siguen andando);
///   3) al terminar, los permisos que los scripts quisieron dar en TA_TAREAS pasan a
///      ALFACORE_TAREAS_WEB (que es lo que lee el menu web) y las copias se borran.
///
/// Las tablas reales no se leen para restaurar ni se escriben nunca, asi que tampoco se pisan los
/// cambios que haga el legacy (ControlBase) mientras corren los scripts.
/// Todo corre en la misma conexion que los scripts (la temporal de permisos es de esa sesion).
/// </summary>
internal sealed class LegacyTablesUpdateGuard
{
    private const string MenuTable = "TA_MENU";
    private const string TareasTable = "TA_TAREAS";
    internal const string SandboxPrefix = "ALFACORE_SANDBOX_";
    private const string WebPendientes = "#AC_GuardWebPendientes";
    private const string WebGrantsUser = "ALFACORE-UPDATES";

    // TA_MENU / TA_TAREAS como nombre propio: no V_TA_TAREAS, ALFACORE_TAREAS_WEB, @TA_MENU, #TA_MENU, etc.
    private static readonly Regex LegacyTableName = new(
        @"(?<![\w#@$])(TA_MENU|TA_TAREAS)(?![\w$])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SqlConnection _cn;
    private readonly bool _hasMenu;
    private readonly bool _hasTareas;

    private LegacyTablesUpdateGuard(SqlConnection cn, bool hasMenu, bool hasTareas)
    {
        _cn = cn;
        _hasMenu = hasMenu;
        _hasTareas = hasTareas;
    }

    public static async Task<LegacyTablesUpdateGuard> BeginAsync(SqlConnection cn, CancellationToken ct)
    {
        var hasMenu = await TableExistsAsync(cn, MenuTable, ct);
        var hasTareas = await TableExistsAsync(cn, TareasTable, ct);
        var guard = new LegacyTablesUpdateGuard(cn, hasMenu, hasTareas);

        var sb = new StringBuilder();
        sb.AppendLine(DropSandboxSql());
        sb.AppendLine($"IF OBJECT_ID('tempdb..{WebPendientes}') IS NOT NULL DROP TABLE {WebPendientes};");
        sb.AppendLine($"CREATE TABLE {WebPendientes} (Usuario nvarchar(100) NOT NULL, Sistema nvarchar(100) NOT NULL, Clave nvarchar(100) NOT NULL);");
        // Solo lectura de las reales: la copia es la que van a usar los scripts.
        if (hasMenu)
            sb.AppendLine($"SELECT * INTO dbo.{SandboxPrefix}{MenuTable} FROM dbo.{MenuTable};");
        if (hasTareas)
            sb.AppendLine($"SELECT * INTO dbo.{SandboxPrefix}{TareasTable} FROM dbo.{TareasTable};");

        await ExecAsync(cn, sb.ToString(), ct);
        return guard;
    }

    /// <summary>Cambia en el lote las referencias a TA_MENU / TA_TAREAS por las copias de AlfaCore.</summary>
    public static string Rewrite(string batch)
        => string.IsNullOrEmpty(batch)
            ? batch
            : LegacyTableName.Replace(batch, m => SandboxPrefix + m.Value.ToUpperInvariant());

    /// <summary>
    /// Pasa a ALFACORE_TAREAS_WEB los permisos que los scripts quisieron dar en TA_TAREAS y devuelve
    /// un resumen de lo que los scripts intentaron cambiar en el legacy ("" si nada). No escribe en
    /// TA_MENU ni en TA_TAREAS.
    /// </summary>
    public async Task<string> RestoreAsync(CancellationToken ct)
    {
        var partes = new List<string>();

        if (_hasMenu && await TableExistsAsync(_cn, SandboxPrefix + MenuTable, ct))
        {
            var (agregadas, quitadas) = await DiffAsync(MenuTable, ct);
            if (agregadas + quitadas > 0)
                partes.Add($"TA_MENU: {agregadas} fila(s) agregada(s)/modificada(s) y {quitadas} quitada(s) por los scripts, no aplicadas");
        }

        if (_hasTareas && await TableExistsAsync(_cn, SandboxPrefix + TareasTable, ct))
        {
            var nuevos = await CollectNewGrantsAsync(ct);
            if (nuevos > 0)
                partes.Add($"TA_TAREAS: {nuevos} permiso(s) pasado(s) a ALFACORE_TAREAS_WEB en vez del legacy");
        }

        await FlushWebGrantsAsync(ct);

        return partes.Count == 0
            ? string.Empty
            : "Legacy aislado (" + string.Join("; ", partes) + ").";
    }

    public async Task EndAsync(CancellationToken ct)
    {
        try
        {
            await FlushWebGrantsAsync(ct);
        }
        catch
        {
            // informativo
        }

        try
        {
            await ExecAsync(_cn,
                DropSandboxSql() +
                $"IF OBJECT_ID('tempdb..{WebPendientes}') IS NOT NULL DROP TABLE {WebPendientes};", ct);
        }
        catch
        {
            // La temporal se borra sola al cerrar la conexion; las copias se borran en la proxima tanda.
        }
    }

    private static string DropSandboxSql()
        => $"IF OBJECT_ID('dbo.{SandboxPrefix}{MenuTable}', 'U') IS NOT NULL DROP TABLE dbo.{SandboxPrefix}{MenuTable};" +
           $"IF OBJECT_ID('dbo.{SandboxPrefix}{TareasTable}', 'U') IS NOT NULL DROP TABLE dbo.{SandboxPrefix}{TareasTable};";

    /// <summary>Filas distintas entre la copia y la real (solo lectura de la real).</summary>
    private async Task<(int Agregadas, int Quitadas)> DiffAsync(string table, CancellationToken ct)
    {
        var cols = await ReadComparableColumnsAsync(_cn, table, ct);
        if (cols.Count == 0)
            return (0, 0);
        var list = string.Join(", ", cols);
        var real = $"dbo.{Q(table)}";
        var copia = $"dbo.{Q(SandboxPrefix + table)}";
        var sql = $"""
            SELECT (SELECT COUNT(*) FROM (SELECT {list} FROM {copia} EXCEPT SELECT {list} FROM {real}) a),
                   (SELECT COUNT(*) FROM (SELECT {list} FROM {real} EXCEPT SELECT {list} FROM {copia}) q);
            """;
        await using var cmd = new SqlCommand(sql, _cn) { CommandTimeout = 300 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return (reader.GetInt32(0), reader.GetInt32(1));
        return (0, 0);
    }

    /// <summary>Permisos que estan en la copia y no en TA_TAREAS: quedan pendientes para ALFACORE_TAREAS_WEB.</summary>
    private async Task<int> CollectNewGrantsAsync(CancellationToken ct)
    {
        var sql = $"""
            INSERT INTO {WebPendientes} (Usuario, Sistema, Clave)
            SELECT n.USUARIO, n.SISTEMA, n.TAREA
              FROM (SELECT CAST(USUARIO AS nvarchar(100)) AS USUARIO, CAST(SISTEMA AS nvarchar(100)) AS SISTEMA, CAST(TAREA AS nvarchar(100)) AS TAREA
                      FROM dbo.{SandboxPrefix}{TareasTable}
                    EXCEPT
                    SELECT CAST(USUARIO AS nvarchar(100)), CAST(SISTEMA AS nvarchar(100)), CAST(TAREA AS nvarchar(100))
                      FROM dbo.{TareasTable}) n
             WHERE ISNULL(n.TAREA, '') <> '' AND n.USUARIO IS NOT NULL AND n.SISTEMA IS NOT NULL;
            SELECT @@ROWCOUNT;
            """;
        await using var cmd = new SqlCommand(sql, _cn) { CommandTimeout = 300 };
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int n ? n : 0;
    }

    /// <summary>
    /// Pasa a ALFACORE_TAREAS_WEB los permisos que los scripts quisieron dar en TA_TAREAS. Si la
    /// tabla todavia no existe (la crea un script mas adelante), quedan pendientes para el proximo intento.
    /// </summary>
    private async Task FlushWebGrantsAsync(CancellationToken ct)
    {
        var webCols = await ReadColumnNamesAsync(_cn, "ALFACORE_TAREAS_WEB", ct);
        if (webCols.Count == 0 || !webCols.Contains("Usuario") || !webCols.Contains("Sistema") || !webCols.Contains("Clave"))
            return;

        var extraCols = new StringBuilder();
        var extraVals = new StringBuilder();
        if (webCols.Contains("FechaHoraGrabacion")) { extraCols.Append(", FechaHoraGrabacion"); extraVals.Append(", GETDATE()"); }
        if (webCols.Contains("UsuarioGrabacion")) { extraCols.Append(", UsuarioGrabacion"); extraVals.Append($", N'{WebGrantsUser}'"); }

        // Solo para usuarios que ya tienen permisos web explicitos: sin filas en ALFACORE_TAREAS_WEB
        // el usuario ve todo el menu web, y agregarle una sola fila lo dejaria restringido a esa opcion.
        // Los que no califican quedan pendientes por si un script posterior les carga permisos web.
        var sql = $"""
            IF OBJECT_ID('tempdb..{WebPendientes}') IS NOT NULL
            BEGIN
                DECLARE @Pasar TABLE (Usuario nvarchar(100), Sistema nvarchar(100), Clave nvarchar(100));
                INSERT INTO @Pasar (Usuario, Sistema, Clave)
                SELECT DISTINCT LTRIM(RTRIM(p.Usuario)), LTRIM(RTRIM(p.Sistema)), LTRIM(RTRIM(p.Clave))
                  FROM {WebPendientes} p
                 WHERE EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB u
                                WHERE UPPER(LTRIM(RTRIM(u.Usuario))) = UPPER(LTRIM(RTRIM(p.Usuario)))
                                  AND UPPER(LTRIM(RTRIM(u.Sistema))) = UPPER(LTRIM(RTRIM(p.Sistema)))
                                  AND ISNULL(u.Clave, '') <> '');

                INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave{extraCols})
                SELECT x.Usuario, x.Sistema, x.Clave{extraVals}
                  FROM @Pasar x
                 WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB w
                                    WHERE UPPER(LTRIM(RTRIM(w.Usuario))) = UPPER(x.Usuario)
                                      AND UPPER(LTRIM(RTRIM(w.Sistema))) = UPPER(x.Sistema)
                                      AND UPPER(LTRIM(RTRIM(w.Clave)))   = UPPER(x.Clave));

                DELETE p FROM {WebPendientes} p
                 WHERE EXISTS (SELECT 1 FROM @Pasar x
                                WHERE x.Usuario = LTRIM(RTRIM(p.Usuario))
                                  AND x.Sistema = LTRIM(RTRIM(p.Sistema))
                                  AND x.Clave   = LTRIM(RTRIM(p.Clave)));
            END
            """;
        await ExecAsync(_cn, sql, ct);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection cn, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NULL THEN 0 ELSE 1 END;", cn);
        cmd.Parameters.AddWithValue("@t", "dbo." + table);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int n && n == 1;
    }

    /// <summary>Columnas comparables con EXCEPT (los ntext/text/image/xml se pasan a nvarchar(max)).</summary>
    private static async Task<List<string>> ReadComparableColumnsAsync(SqlConnection cn, string table, CancellationToken ct)
    {
        const string sql = """
            SELECT c.name, CASE WHEN ty.name IN ('ntext', 'text', 'image', 'xml') THEN 1 ELSE 0 END AS is_lob
              FROM sys.columns c
                   INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
             WHERE c.object_id = OBJECT_ID(@t, 'U')
               AND c.is_computed = 0 AND c.is_identity = 0
               AND ty.name NOT IN ('timestamp', 'rowversion')
             ORDER BY c.column_id;
            """;
        var cols = new List<string>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@t", "dbo." + table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = Q(reader.GetString(0));
            cols.Add(reader.GetInt32(1) == 1 ? $"CAST({name} AS nvarchar(max)) AS {name}" : name);
        }
        return cols;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(SqlConnection cn, string table, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@t, 'U');", cn);
        cmd.Parameters.AddWithValue("@t", "dbo." + table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    private static async Task ExecAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Q(string name) => "[" + name.Replace("]", "]]") + "]";
}

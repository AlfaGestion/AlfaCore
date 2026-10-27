using Microsoft.Data.SqlClient;
using System.Text;

namespace AlfaCore.Services;

/// <summary>
/// Protege las tablas del sistema legacy (Alfa Gestion VB6) mientras se corren los scripts de
/// App_Data/updates. El menu y los permisos del legacy (TA_MENU, TA_TAREAS) y los de AlfaCore
/// (ALFACORE_MENU_WEB, ALFACORE_TAREAS_WEB) son independientes: ningun script de AlfaCore puede
/// agregar, cambiar ni borrar opciones o permisos del legacy.
///
/// Muchos scripts viejos (y alguno publicado sin pasar por el repo) insertan la opcion web tambien
/// en TA_MENU y se la dan a todos los usuarios en TA_TAREAS; eso aparecia en el menu y en la
/// Autorizacion de Tareas del legacy sin que nadie lo pidiera. En vez de corregir script por script,
/// el motor saca una foto de esas tablas antes de correr los scripts y al terminar la tanda las deja
/// como estaban (no script por script: algunos leen filas que creo uno anterior). Los permisos que el script queria dar en TA_TAREAS pasan a ALFACORE_TAREAS_WEB
/// (que es lo que lee el menu web), asi el comportamiento de la web no cambia.
///
/// Todo corre en la misma conexion que los scripts (las tablas temporales son de esa sesion).
/// </summary>
internal sealed class LegacyTablesUpdateGuard
{
    private const string MenuTable = "TA_MENU";
    private const string TareasTable = "TA_TAREAS";
    private const string MenuSnap = "#AC_GuardTaMenu";
    private const string TareasSnap = "#AC_GuardTaTareas";
    private const string WebPendientes = "#AC_GuardWebPendientes";
    private const string WebGrantsUser = "ALFACORE-UPDATES";

    private readonly SqlConnection _cn;
    private readonly TableInfo? _menu;
    private readonly TableInfo? _tareas;

    private LegacyTablesUpdateGuard(SqlConnection cn, TableInfo? menu, TableInfo? tareas)
    {
        _cn = cn;
        _menu = menu;
        _tareas = tareas;
    }

    public static async Task<LegacyTablesUpdateGuard> BeginAsync(SqlConnection cn, CancellationToken ct)
    {
        var menu = await ReadTableInfoAsync(cn, MenuTable, ct);
        var tareas = await ReadTableInfoAsync(cn, TareasTable, ct);
        var guard = new LegacyTablesUpdateGuard(cn, menu, tareas);

        var sb = new StringBuilder();
        sb.AppendLine($"IF OBJECT_ID('tempdb..{MenuSnap}') IS NOT NULL DROP TABLE {MenuSnap};");
        sb.AppendLine($"IF OBJECT_ID('tempdb..{TareasSnap}') IS NOT NULL DROP TABLE {TareasSnap};");
        sb.AppendLine($"IF OBJECT_ID('tempdb..{WebPendientes}') IS NOT NULL DROP TABLE {WebPendientes};");
        sb.AppendLine($"CREATE TABLE {WebPendientes} (Usuario nvarchar(100) NOT NULL, Sistema nvarchar(100) NOT NULL, Clave nvarchar(100) NOT NULL);");
        if (menu is not null)
            sb.AppendLine($"SELECT * INTO {MenuSnap} FROM dbo.{MenuTable};");
        if (tareas is not null)
            sb.AppendLine($"SELECT * INTO {TareasSnap} FROM dbo.{TareasTable};");

        await ExecAsync(cn, sb.ToString(), ct);
        return guard;
    }

    /// <summary>Deja TA_MENU y TA_TAREAS como estaban al empezar. Devuelve un resumen ("" si no hubo cambios).</summary>
    public async Task<string> RestoreAsync(CancellationToken ct)
    {
        var partes = new List<string>();

        if (_menu is not null)
        {
            var (nuevas, cambiadas, repuestas) = await RestoreTableAsync(_menu, MenuSnap, moveGrantsToWeb: false, ct);
            if (nuevas + cambiadas + repuestas > 0)
                partes.Add($"TA_MENU: {nuevas} agregada(s) quitada(s), {cambiadas} modificada(s) restaurada(s), {repuestas} borrada(s) repuesta(s)");
        }

        if (_tareas is not null)
        {
            var (nuevas, cambiadas, repuestas) = await RestoreTableAsync(_tareas, TareasSnap, moveGrantsToWeb: true, ct);
            if (nuevas + cambiadas + repuestas > 0)
                partes.Add($"TA_TAREAS: {nuevas} permiso(s) agregado(s) pasado(s) a ALFACORE_TAREAS_WEB, {repuestas} borrado(s) repuesto(s)");
        }

        await FlushWebGrantsAsync(ct);

        return partes.Count == 0
            ? string.Empty
            : "Legacy protegido (" + string.Join("; ", partes) + ").";
    }

    public async Task EndAsync(CancellationToken ct)
    {
        try
        {
            await FlushWebGrantsAsync(ct);
            await ExecAsync(_cn,
                $"IF OBJECT_ID('tempdb..{MenuSnap}') IS NOT NULL DROP TABLE {MenuSnap};" +
                $"IF OBJECT_ID('tempdb..{TareasSnap}') IS NOT NULL DROP TABLE {TareasSnap};" +
                $"IF OBJECT_ID('tempdb..{WebPendientes}') IS NOT NULL DROP TABLE {WebPendientes};", ct);
        }
        catch
        {
            // Las temporales se borran solas al cerrar la conexion.
        }
    }

    private async Task<(int Nuevas, int Cambiadas, int Repuestas)> RestoreTableAsync(
        TableInfo table, string snap, bool moveGrantsToWeb, CancellationToken ct)
    {
        var cols = table.Columns;
        var colList = string.Join(", ", cols.Select(c => Q(c.Name)));
        string Cmp(string alias) => string.Join(", ", cols.Select(c => c.IsLob
            ? $"CAST({alias}.{Q(c.Name)} AS nvarchar(max)) AS {Q(c.Name)}"
            : $"{alias}.{Q(c.Name)}"));
        var t = $"dbo.{Q(table.Name)}";

        var sb = new StringBuilder();
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("DECLARE @nuevas int = 0, @cambiadas int = 0, @repuestas int = 0;");
        sb.AppendLine("BEGIN TRAN;");

        if (moveGrantsToWeb)
        {
            // permisos que el script agrego: se guardan para ALFACORE_TAREAS_WEB antes de sacarlos
            sb.AppendLine($"""
                INSERT INTO {WebPendientes} (Usuario, Sistema, Clave)
                SELECT n.USUARIO, n.SISTEMA, n.TAREA
                  FROM (SELECT {Cmp("m")} FROM {t} m EXCEPT SELECT {Cmp("s")} FROM {snap} s) n
                 WHERE ISNULL(n.TAREA, '') <> '';
                """);
        }

        if (table.Identity is not null)
        {
            var id = Q(table.Identity);
            // filas nuevas
            sb.AppendLine($"DELETE m FROM {t} m WHERE NOT EXISTS (SELECT 1 FROM {snap} s WHERE s.{id} = m.{id}); SET @nuevas = @@ROWCOUNT;");
            // filas modificadas
            var setList = string.Join(", ", cols.Select(c => $"m.{Q(c.Name)} = s.{Q(c.Name)}"));
            sb.AppendLine($"""
                UPDATE m SET {setList}
                  FROM {t} m INNER JOIN {snap} s ON s.{id} = m.{id}
                 WHERE NOT EXISTS (SELECT {Cmp("m")} INTERSECT SELECT {Cmp("s")});
                SET @cambiadas = @@ROWCOUNT;
                """);
            // filas borradas (con su id original)
            sb.AppendLine($"""
                IF EXISTS (SELECT 1 FROM {snap} s WHERE NOT EXISTS (SELECT 1 FROM {t} m WHERE m.{id} = s.{id}))
                BEGIN
                    SET IDENTITY_INSERT {t} ON;
                    INSERT INTO {t} ({id}, {colList})
                    SELECT s.{id}, {string.Join(", ", cols.Select(c => $"s.{Q(c.Name)}"))}
                      FROM {snap} s WHERE NOT EXISTS (SELECT 1 FROM {t} m WHERE m.{id} = s.{id});
                    SET @repuestas = @@ROWCOUNT;
                    SET IDENTITY_INSERT {t} OFF;
                END
                """);
        }
        else
        {
            // sin clave: se compara la fila completa (una modificacion cuenta como borrada + agregada)
            sb.AppendLine($"""
                DELETE m FROM {t} m
                 WHERE NOT EXISTS (SELECT 1 FROM {snap} s WHERE EXISTS (SELECT {Cmp("m")} INTERSECT SELECT {Cmp("s")}));
                SET @nuevas = @@ROWCOUNT;
                INSERT INTO {t} ({colList})
                SELECT {colList} FROM (SELECT {Cmp("s")} FROM {snap} s EXCEPT SELECT {Cmp("m")} FROM {t} m) x;
                SET @repuestas = @@ROWCOUNT;
                """);
        }

        sb.AppendLine("COMMIT;");
        sb.AppendLine("SELECT @nuevas, @cambiadas, @repuestas;");

        await using var cmd = new SqlCommand(sb.ToString(), _cn) { CommandTimeout = 300 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        return (0, 0, 0);
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

    private sealed record ColumnInfo(string Name, bool IsLob);

    private sealed record TableInfo(string Name, string? Identity, IReadOnlyList<ColumnInfo> Columns);

    private static async Task<TableInfo?> ReadTableInfoAsync(SqlConnection cn, string table, CancellationToken ct)
    {
        const string sql = """
            SELECT c.name, c.is_identity,
                   CASE WHEN ty.name IN ('ntext', 'text', 'image', 'xml') THEN 1 ELSE 0 END AS is_lob
              FROM sys.columns c
                   INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
             WHERE c.object_id = OBJECT_ID(@t, 'U')
               AND c.is_computed = 0
               AND ty.name NOT IN ('timestamp', 'rowversion')
             ORDER BY c.column_id;
            """;
        string? identity = null;
        var cols = new List<ColumnInfo>();
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@t", "dbo." + table);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (reader.GetBoolean(1))
                    identity = name;
                else
                    cols.Add(new ColumnInfo(name, reader.GetInt32(2) == 1));
            }
        }
        return cols.Count == 0 ? null : new TableInfo(table, identity, cols);
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

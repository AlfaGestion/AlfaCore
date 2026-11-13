using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Resultado de una pasada de <see cref="IConversacionesService.CompactarAdjuntosAsync"/>.</summary>
public sealed record ConversacionAdjuntosCompactacionResultDto
{
    public bool Habilitado { get; init; }
    public int HashesCalculados { get; init; }
    public int FilasCompartidas { get; init; }
    public long BytesLiberadosBase { get; init; }
    public int ArchivosMovidos { get; init; }
    public long BytesLiberadosDisco { get; init; }
    public bool Terminado { get; init; }
    public int Huerfanos { get; init; }
    public long BytesHuerfanos { get; init; }
    public bool HuerfanosBorrados { get; init; }

    public bool HuboCambios => HashesCalculados + FilasCompartidas + ArchivosMovidos > 0 || HuerfanosBorrados;
}

/// <summary>
/// Compactación de los adjuntos grabados antes del almacén por contenido (2026-11-13). Cada pasada procesa un lote
/// chico, así no compite con la atención: (1) calcula el hash de las filas viejas que no lo tienen, (2) deja una
/// sola copia del contenido en la base (las demás filas quedan SQL_COMPARTIDO), (3) pasa los archivos de disco a
/// <see cref="ConversacionesAdjuntosAlmacen"/> y borra la copia vieja, y (4) cuando no queda nada por mover,
/// informa los archivos que ninguna fila referencia (huérfanos) y los borra solo si
/// CONVERSACIONES-ADJUNTOS-BORRAR-HUERFANOS = SI. Nunca borra un archivo sin haber actualizado antes la fila.
/// </summary>
public sealed partial class ConversacionesService
{
    private const string CompactarClave = "CONVERSACIONES-ADJUNTOS-COMPACTAR";
    private const string CompactarLoteClave = "CONVERSACIONES-ADJUNTOS-COMPACTAR-LOTE";
    private const string BorrarHuerfanosClave = "CONVERSACIONES-ADJUNTOS-BORRAR-HUERFANOS";
    private const int CompactarLoteInicial = 50;
    // Un huérfano reciente puede ser un archivo cuya fila todavía se está grabando.
    private static readonly TimeSpan AntiguedadMinimaHuerfano = TimeSpan.FromDays(2);

    // Último IdAdjunto revisado en disco por base, para no volver a mirar filas cuyo archivo no está.
    // Se pierde al reiniciar: entonces se repasa desde el principio, lo ya movido no vuelve a entrar.
    private static readonly ConcurrentDictionary<string, long> CursorCompactacionDisco = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> HuerfanosRevisados = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ConversacionAdjuntosCompactacionResultDto> CompactarAdjuntosAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (!await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "RutaLocal", ct))
            return new ConversacionAdjuntosCompactacionResultDto();

        await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, ct);
        var config = await LeerConfigCompactacionAsync(cn, ct);
        if (!config.Habilitado)
            return new ConversacionAdjuntosCompactacionResultDto();

        var claveBase = $"{cn.DataSource}|{cn.Database}";
        var conContenido = await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "ArchivoContenido", ct);

        var hashes = conContenido ? await CalcularHashesFaltantesAsync(cn, Math.Min(config.Lote, 10), ct) : 0;
        var (compartidas, bytesBase) = conContenido ? await CompartirContenidoSqlAsync(cn, config.Lote, ct) : (0, 0L);
        var (movidos, bytesDisco, discoTerminado) = await MoverArchivosAlAlmacenAsync(cn, claveBase, config.Lote, ct);

        var terminado = discoTerminado && hashes == 0 && compartidas == 0;
        var resultado = new ConversacionAdjuntosCompactacionResultDto
        {
            Habilitado = true,
            HashesCalculados = hashes,
            FilasCompartidas = compartidas,
            BytesLiberadosBase = bytesBase,
            ArchivosMovidos = movidos,
            BytesLiberadosDisco = bytesDisco,
            Terminado = terminado
        };

        if (terminado && HuerfanosRevisados.TryAdd(claveBase, true))
        {
            var (huerfanos, bytesHuerfanos, borrados) = await RevisarHuerfanosAsync(cn, config.BorrarHuerfanos, ct);
            resultado = resultado with { Huerfanos = huerfanos, BytesHuerfanos = bytesHuerfanos, HuerfanosBorrados = borrados };
            if (huerfanos > 0)
            {
                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "CompactarAdjuntosHuerfanos",
                    "CONV_ADJUNTOS",
                    string.Empty,
                    borrados
                        ? $"Se borraron {huerfanos} archivos de adjuntos que ninguna conversación usaba."
                        : $"Hay {huerfanos} archivos de adjuntos que ninguna conversación usa. Para borrarlos: {BorrarHuerfanosClave} = SI.",
                    new { Archivos = huerfanos, Bytes = bytesHuerfanos, Borrados = borrados },
                    ct);
            }
        }

        if (resultado.HuboCambios)
        {
            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "CompactarAdjuntos",
                "CONV_ADJUNTOS",
                string.Empty,
                "Adjuntos repetidos compactados.",
                new { hashes, compartidas, bytesBase, movidos, bytesDisco },
                ct);
        }

        return resultado;
    }

    private sealed record ConfigCompactacion(bool Habilitado, int Lote, bool BorrarHuerfanos);

    private static async Task<ConfigCompactacion> LeerConfigCompactacionAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
                SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR, N'')))
                FROM dbo.TA_CONFIGURACION
                WHERE CLAVE IN (@Compactar, @Lote, @Huerfanos);
            """;
        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@Compactar", CompactarClave);
            cmd.Parameters.AddWithValue("@Lote", CompactarLoteClave);
            cmd.Parameters.AddWithValue("@Huerfanos", BorrarHuerfanosClave);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                valores[rd.GetString(0)] = rd.GetString(1);
        }

        static bool Si(string? valor, bool inicial)
            => string.IsNullOrWhiteSpace(valor) ? inicial : valor.Trim().ToUpperInvariant() is "SI" or "S" or "1";

        var lote = valores.TryGetValue(CompactarLoteClave, out var loteTexto)
                   && int.TryParse(loteTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, 500)
            : CompactarLoteInicial;
        return new ConfigCompactacion(
            Si(valores.GetValueOrDefault(CompactarClave), true),
            lote,
            Si(valores.GetValueOrDefault(BorrarHuerfanosClave), false));
    }

    // Filas grabadas antes de que existiera ArchivoHashSha256. Se calcula en .NET (HASHBYTES tiene tope de
    // 8000 bytes antes de SQL 2016) y de a una fila, para no cargar varios archivos grandes juntos.
    private static async Task<int> CalcularHashesFaltantesAsync(SqlConnection cn, int lote, CancellationToken ct)
    {
        var ids = new List<long>();
        await using (var cmd = new SqlCommand("""
            SELECT TOP (@Lote) IdAdjunto FROM dbo.CONV_ADJUNTOS
            WHERE ArchivoHashSha256 IS NULL AND ArchivoContenido IS NOT NULL
            ORDER BY IdAdjunto;
            """, cn))
        {
            cmd.Parameters.AddWithValue("@Lote", lote);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                ids.Add(rd.GetInt64(0));
        }

        foreach (var id in ids)
        {
            byte[]? contenido;
            await using (var cmd = new SqlCommand("SELECT ArchivoContenido FROM dbo.CONV_ADJUNTOS WHERE IdAdjunto = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                contenido = await cmd.ExecuteScalarAsync(ct) as byte[];
            }

            if (contenido is not { Length: > 0 })
                continue;

            await using var update = new SqlCommand("""
                UPDATE dbo.CONV_ADJUNTOS SET ArchivoHashSha256 = @Hash
                WHERE IdAdjunto = @Id AND ArchivoHashSha256 IS NULL;
                """, cn);
            update.Parameters.AddWithValue("@Id", id);
            update.Parameters.Add("@Hash", System.Data.SqlDbType.NVarChar, 64).Value = ContentHash(contenido);
            await update.ExecuteNonQueryAsync(ct);
        }

        return ids.Count;
    }

    // Conserva el contenido en la fila más antigua de cada hash y lo saca de las demás. Exige también el mismo
    // largo, por si algún hash quedó mal grabado. La más antigua nunca pierde el suyo (no hay una anterior), así
    // que siempre queda una copia.
    private static async Task<(int Filas, long Bytes)> CompartirContenidoSqlAsync(SqlConnection cn, int lote, CancellationToken ct)
    {
        var sql = $"""
            SET NOCOUNT ON;
            DECLARE @Liberado TABLE (Bytes bigint NULL);
            UPDATE TOP (@Lote) a
            SET ArchivoContenido = NULL,
                AlmacenamientoEstado = N'{SharedSqlContentState}'
            OUTPUT CONVERT(bigint, DATALENGTH(deleted.ArchivoContenido)) INTO @Liberado (Bytes)
            FROM dbo.CONV_ADJUNTOS a
            WHERE a.ArchivoContenido IS NOT NULL
              AND a.ArchivoHashSha256 IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM dbo.CONV_ADJUNTOS k
                  WHERE k.ArchivoHashSha256 = a.ArchivoHashSha256
                    AND k.IdAdjunto < a.IdAdjunto
                    AND k.ArchivoContenido IS NOT NULL
                    AND DATALENGTH(k.ArchivoContenido) = DATALENGTH(a.ArchivoContenido));
            SELECT COUNT(*), ISNULL(SUM(Bytes), 0) FROM @Liberado;
            """;
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 };
        cmd.Parameters.AddWithValue("@Lote", lote);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? (rd.GetInt32(0), rd.GetInt64(1)) : (0, 0L);
    }

    private async Task<(int Movidos, long Bytes, bool Terminado)> MoverArchivosAlAlmacenAsync(
        SqlConnection cn, string claveBase, int lote, CancellationToken ct)
    {
        var raiz = UploadsBasePath;
        var desde = CursorCompactacionDisco.GetValueOrDefault(claveBase);
        var filas = new List<(long Id, long IdConversacion, string Ruta)>();
        await using (var cmd = new SqlCommand($"""
            SELECT TOP (@Lote) a.IdAdjunto, m.IdConversacion, a.RutaLocal
            FROM dbo.CONV_ADJUNTOS a
            INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = a.IdMensaje
            WHERE a.IdAdjunto > @Desde
              AND NULLIF(LTRIM(RTRIM(ISNULL(a.RutaLocal, N''))), N'') IS NOT NULL
              AND REPLACE(a.RutaLocal, N'/', N'\') NOT LIKE N'%\{ConversacionesAdjuntosAlmacen.CarpetaContenido}\%'
            ORDER BY a.IdAdjunto;
            """, cn))
        {
            cmd.Parameters.AddWithValue("@Lote", lote);
            cmd.Parameters.AddWithValue("@Desde", desde);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                filas.Add((rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2)));
        }

        var movidos = 0;
        long liberados = 0;
        foreach (var fila in filas)
        {
            ct.ThrowIfCancellationRequested();
            CursorCompactacionDisco[claveBase] = fila.Id;

            var actual = UbicarArchivoAdjunto(fila.Ruta, fila.IdConversacion, raiz);
            if (actual.Length == 0)
                continue; // no está en disco: lo resuelven la copia en SQL o la recuperación desde Meta.

            var contenido = await File.ReadAllBytesAsync(actual, ct);
            var (destino, yaEstaba) = await ConversacionesAdjuntosAlmacen.GuardarConDetalleAsync(raiz, contenido, actual, ct);

            await using (var update = new SqlCommand("""
                UPDATE dbo.CONV_ADJUNTOS SET RutaLocal = @Nueva
                WHERE IdAdjunto = @Id AND RutaLocal = @Anterior;
                SELECT @@ROWCOUNT;
                """, cn))
            {
                update.Parameters.AddWithValue("@Id", fila.Id);
                update.Parameters.AddWithValue("@Nueva", ToStoredAttachmentPath(destino));
                update.Parameters.AddWithValue("@Anterior", fila.Ruta);
                if (Convert.ToInt32(await update.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                    continue; // la fila cambió mientras tanto: se deja el archivo viejo como estaba.
            }

            // El archivo viejo se borra solo si ninguna otra fila lo sigue usando.
            await using (var refs = new SqlCommand("SELECT COUNT(*) FROM dbo.CONV_ADJUNTOS WHERE RutaLocal = @Anterior;", cn))
            {
                refs.Parameters.AddWithValue("@Anterior", fila.Ruta);
                if (Convert.ToInt32(await refs.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                    continue;
            }

            if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(actual);
                    liberados += yaEstaba ? contenido.LongLength : 0;
                }
                catch (IOException)
                {
                    // Abierto en este momento (se está sirviendo): queda como huérfano y lo informa la revisión final.
                }
            }

            movidos++;
        }

        return (movidos, liberados, filas.Count < lote);
    }

    private string UbicarArchivoAdjunto(string rutaGuardada, long idConversacion, string raiz)
    {
        var directa = ToAbsoluteAttachmentPath(rutaGuardada);
        if (IsAttachmentPathAllowedForRoot(directa, raiz) && File.Exists(directa))
            return directa;

        var enConversacion = Path.Combine(raiz, idConversacion.ToString(CultureInfo.InvariantCulture), Path.GetFileName(rutaGuardada));
        return IsAttachmentPathAllowedForRoot(enConversacion, raiz) && File.Exists(enConversacion) ? enConversacion : string.Empty;
    }

    // Solo mira las carpetas de conversación (nombre numérico): otras carpetas de la base, como importaciones,
    // no son adjuntos de CONV_ADJUNTOS.
    private async Task<(int Archivos, long Bytes, bool Borrados)> RevisarHuerfanosAsync(SqlConnection cn, bool borrar, CancellationToken ct)
    {
        var raiz = UploadsBasePath;
        if (!Directory.Exists(raiz))
            return (0, 0, false);

        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("SELECT RutaLocal FROM dbo.CONV_ADJUNTOS WHERE NULLIF(LTRIM(RTRIM(ISNULL(RutaLocal, N''))), N'') IS NOT NULL;", cn) { CommandTimeout = 300 })
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var ruta = ToAbsoluteAttachmentPath(rd.GetString(0));
                usados.Add(Path.GetFullPath(ruta));
                usados.Add(Path.GetFileName(ruta)); // la resolución también encuentra un archivo por nombre en la carpeta de la conversación
            }
        }

        var limite = DateTime.UtcNow - AntiguedadMinimaHuerfano;
        var huerfanos = Directory.EnumerateDirectories(raiz)
            .Where(d => Path.GetFileName(d).All(char.IsAsciiDigit))
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc < limite && !usados.Contains(f.FullName) && !usados.Contains(f.Name))
            .ToList();

        var bytes = huerfanos.Sum(f => f.Length);
        if (!borrar || huerfanos.Count == 0)
            return (huerfanos.Count, bytes, false);

        foreach (var archivo in huerfanos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                archivo.Delete();
            }
            catch (IOException)
            {
            }
        }

        return (huerfanos.Count, bytes, true);
    }
}

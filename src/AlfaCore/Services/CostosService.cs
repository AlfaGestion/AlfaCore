using ClosedXML.Excel;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class CostosService(IConfiguration configuration, ISessionService sessionService, IWebHostEnvironment env, IAppEventService appEvents) : ICostosService
{
    private static readonly string[] CodeCandidates = ["codigo", "cod", "sku", "codigo proveedor", "idarticuloproveedor"];
    private static readonly string[] DescriptionCandidates = ["descripcion", "descripción", "detalle", "producto", "articulo"];
    private static readonly string[] PriceCandidates = ["precio s/iva", "precio sin iva", "precio costo", "costo", "precio", "importe", "precio c/iva", "precio con iva"];

    private readonly string _importsPath = Path.Combine(env.ContentRootPath, "App_Data", "CostosImports");
    private readonly IAppEventService _appEvents = appEvents;
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<CostosProfileDto>> GetProfilesAsync(CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Costos", "GetProfiles", async token =>
        {
            const string sql = """
            SELECT
                Id,
                ISNULL(Proveedor, ''),
                ISNULL(CuentaProveedor, ''),
                ISNULL(PoliticaPrecios, ''),
                ISNULL(LISTA, ''),
                ISNULL(Hoja, ''),
                ISNULL(RangoDesde, ''),
                ISNULL(RangoHasta, ''),
                ISNULL(CamposClave, ''),
                ISNULL(Notas, ''),
                ISNULL(SoloAlta, 0),
                ISNULL(SoloModificacion, 0)
            FROM dbo.V_Ta_InterODBC
            ORDER BY Proveedor
            """;

            var items = new List<CostosProfileDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new CostosProfileDto
                {
                    Id = rd.GetInt32(0),
                    ProviderName = rd.GetString(1),
                    ProviderAccount = rd.GetString(2),
                    PricePolicy = rd.GetString(3),
                    ListCode = rd.GetString(4),
                    SheetName = rd.GetString(5),
                    RangeFrom = rd.GetString(6),
                    RangeTo = rd.GetString(7),
                    KeyFields = rd.GetString(8),
                    Notes = rd.GetString(9),
                    OnlyAdd = rd.GetBoolean(10),
                    OnlyModify = rd.GetBoolean(11)
                });
            }

            return (IReadOnlyList<CostosProfileDto>)items;
        }, "No se pudieron cargar los perfiles de importación.", ct);
    }

    public async Task<IReadOnlyList<CostosProviderLookupDto>> SearchProvidersAsync(string term, int limit = 15, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return [];

        return await ExecuteLoggedAsync("Costos", "SearchProviders", async token =>
        {
            var sql = $"""
                SELECT TOP ({Math.Max(1, limit)})
                    LTRIM(RTRIM(ISNULL(p.CODIGO, ''))) AS Code,
                    LTRIM(RTRIM(COALESCE(NULLIF(p.RAZON_SOCIAL, ''), p.CODIGO))) AS Name
                FROM dbo.VT_PROVEEDORES p
                WHERE p.CODIGO LIKE '%' + @Term + '%'
                   OR p.RAZON_SOCIAL LIKE '%' + @Term + '%'
                GROUP BY p.CODIGO, p.RAZON_SOCIAL
                ORDER BY Name, Code
                """;

            var items = new List<CostosProviderLookupDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Term", term.Trim());
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new CostosProviderLookupDto
                {
                    Code = rd.GetString(0),
                    Name = rd.GetString(1)
                });
            }

            return (IReadOnlyList<CostosProviderLookupDto>)items;
        }, "No se pudieron buscar proveedores.", ct);
    }

    public async Task<int> CreateProfileAsync(CostosProfileDto profile, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Costos", "CreateProfile", async token =>
        {
            ValidateProfile(profile);
            await ValidateProviderCodeAsync(profile.ProviderAccount, token);

            const string sql = """
            INSERT INTO dbo.V_Ta_InterODBC
            (
                Proveedor,
                Odbc,
                CuentaProveedor,
                PoliticaPrecios,
                Hoja,
                LISTA,
                RangoDesde,
                RangoHasta,
                CamposClave,
                Notas,
                SoloAlta,
                SoloModificacion
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @Proveedor,
                '',
                @CuentaProveedor,
                @PoliticaPrecios,
                @Hoja,
                @Lista,
                @RangoDesde,
                @RangoHasta,
                @CamposClave,
                @Notas,
                @SoloAlta,
                @SoloModificacion
            )
            """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            AddProfileParameters(cmd, profile);
            var result = await cmd.ExecuteScalarAsync(token);
            var newId = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            await _appEvents.LogAuditAsync("Costos", "CreateProfile", "V_Ta_InterODBC", newId.ToString(CultureInfo.InvariantCulture), "Perfil de importación creado.",
                new { profile.ProviderName, profile.ProviderAccount }, token);
            return newId;
        }, "No se pudo crear el perfil de importación.", ct);
    }

    public async Task UpdateProfileAsync(CostosProfileDto profile, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Costos", "UpdateProfile", async token =>
        {
            if (profile.Id <= 0)
                throw new InvalidOperationException("Perfil inválido para actualizar.");

            ValidateProfile(profile);
            await ValidateProviderCodeAsync(profile.ProviderAccount, token);

            const string sql = """
            UPDATE dbo.V_Ta_InterODBC
            SET
                Proveedor = @Proveedor,
                CuentaProveedor = @CuentaProveedor,
                PoliticaPrecios = @PoliticaPrecios,
                Hoja = @Hoja,
                LISTA = @Lista,
                RangoDesde = @RangoDesde,
                RangoHasta = @RangoHasta,
                CamposClave = @CamposClave,
                Notas = @Notas,
                SoloAlta = @SoloAlta,
                SoloModificacion = @SoloModificacion
            WHERE Id = @Id
            """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            AddProfileParameters(cmd, profile);
            cmd.Parameters.AddWithValue("@Id", profile.Id);
            await cmd.ExecuteNonQueryAsync(token);
            await _appEvents.LogAuditAsync("Costos", "UpdateProfile", "V_Ta_InterODBC", profile.Id.ToString(CultureInfo.InvariantCulture), "Perfil de importación actualizado.",
                new { profile.ProviderName, profile.ProviderAccount }, token);
            return true;
        }, "No se pudo actualizar el perfil de importación.", ct);
    }

    public async Task DeleteProfileAsync(int profileId, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Costos", "DeleteProfile", async token =>
        {
            if (profileId <= 0)
                throw new InvalidOperationException("Perfil inválido para eliminar.");

            const string sql = "DELETE FROM dbo.V_Ta_InterODBC WHERE Id = @Id";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", profileId);
            await cmd.ExecuteNonQueryAsync(token);
            await _appEvents.LogAuditAsync("Costos", "DeleteProfile", "V_Ta_InterODBC", profileId.ToString(CultureInfo.InvariantCulture), "Perfil de importación eliminado.", null, token);
            return true;
        }, "No se pudo eliminar el perfil de importación.", ct);
    }

    public async Task<IReadOnlyList<CostosBatchSummaryDto>> GetRecentBatchesAsync(int limit = 25, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT TOP ({Math.Max(1, limit)})
                ID,
                FechaHora_Alta,
                ISNULL(Estado, ''),
                ISNULL(Usuario, ''),
                ISNULL(Proveedor, ''),
                ISNULL(CuentaProveedor, ''),
                ISNULL(ArchivoNombre, ''),
                ISNULL(ArchivoOrigen, ''),
                ISNULL(TipoArchivo, ''),
                ISNULL(TotalFilasLeidas, 0),
                ISNULL(TotalFilasConCosto, 0),
                ISNULL(TotalFilasConfirmadas, 0),
                ISNULL(TotalActualizadas, 0),
                ISNULL(TotalErrores, 0)
            FROM dbo.IA_Costos_Importacion_CAB
            ORDER BY FechaHora_Alta DESC, ID DESC
            """;

        var items = new List<CostosBatchSummaryDto>();
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                items.Add(new CostosBatchSummaryDto
                {
                    Id = rd.GetInt32(0),
                    CreatedAt = rd.GetDateTime(1),
                    Status = rd.GetString(2),
                    UserName = rd.GetString(3),
                    ProviderName = rd.GetString(4),
                    ProviderAccount = rd.GetString(5),
                    SourceFileName = rd.GetString(6),
                    SourceFilePath = rd.GetString(7),
                    SourceKind = rd.GetString(8),
                    TotalRowsRead = rd.GetInt32(9),
                    TotalRowsWithCost = rd.GetInt32(10),
                    TotalConfirmed = rd.GetInt32(11),
                    TotalUpdated = rd.GetInt32(12),
                    TotalErrors = rd.GetInt32(13)
                });
            }
        }
        catch (SqlException ex) when (IsMissingObject(ex))
        {
            return [];
        }

        return items;
    }

    public async Task<IReadOnlyList<CostosHistoryDto>> GetRecentHistoryAsync(int limit = 100, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT TOP ({Math.Max(1, limit)})
                ISNULL(ImportacionID, 0),
                FechaHora,
                ISNULL(Usuario, ''),
                ISNULL(Proveedor, ''),
                ISNULL(ArchivoOrigen, ''),
                FilaOrigen,
                ISNULL(ArticuloID, ''),
                ISNULL(DescripcionImportada, ''),
                CostoAnterior,
                CostoNuevo,
                ISNULL(MatchTipo, ''),
                ISNULL(MatchScore, 0),
                ISNULL(AlertaDetalle, '')
            FROM dbo.IA_Costos_Actualizacion_Hist
            ORDER BY FechaHora DESC, ID DESC
            """;

        var items = new List<CostosHistoryDto>();
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                items.Add(new CostosHistoryDto
                {
                    ImportBatchId = rd.GetInt32(0) == 0 ? null : rd.GetInt32(0),
                    Timestamp = rd.GetDateTime(1),
                    UserName = rd.GetString(2),
                    ProviderName = rd.GetString(3),
                    SourceFile = rd.GetString(4),
                    RowNumber = rd.GetInt32(5),
                    ArticleId = rd.GetString(6),
                    ImportedDescription = rd.GetString(7),
                    PreviousCost = rd.IsDBNull(8) ? null : rd.GetDecimal(8),
                    NewCost = rd.IsDBNull(9) ? null : rd.GetDecimal(9),
                    MatchType = rd.GetString(10),
                    MatchScore = Convert.ToDouble(rd.GetValue(11), CultureInfo.InvariantCulture),
                    AlertText = rd.GetString(12)
                });
            }
        }
        catch (SqlException ex) when (IsMissingObject(ex))
        {
            return [];
        }

        return items;
    }

    public async Task<CostosImportResultDto> ImportStructuredFileAsync(int profileId, string originalFileName, Stream content, string userName, string? notesOverride = null, int? forcedPriceColIndex = null, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Costos", "ImportStructuredFile", async token =>
        {
            var profile = (await GetProfilesAsync(token)).FirstOrDefault(p => p.Id == profileId)
                ?? throw new InvalidOperationException("Perfil de importación no encontrado.");

            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            if (extension is not ".xlsx" and not ".csv" and not ".txt")
                throw new InvalidOperationException("Por ahora el módulo web soporta Excel, CSV y TXT. PDF/imagen queda para la siguiente etapa.");

            progress?.Report(2);
            Directory.CreateDirectory(_importsPath);
            var storedName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(originalFileName)}";
            var storedPath = Path.Combine(_importsPath, storedName);
            await using (var fs = File.Create(storedPath))
            {
                await content.CopyToAsync(fs, token);
            }

            progress?.Report(8);
            var rows = extension == ".xlsx"
                ? ReadXlsxRows(storedPath, profile.SheetName, forcedPriceColIndex)
                : ReadDelimitedRows(storedPath, forcedPriceColIndex);

            if (rows.Count == 0)
                throw new InvalidOperationException("No se pudieron detectar filas válidas con descripción y costo.");

            progress?.Report(18);
            var batchNotes = !string.IsNullOrWhiteSpace(notesOverride) ? notesOverride : profile.Notes;
            var fileHash = ComputeSha256(storedPath);
            var batch = await CreateBatchAsync(profile, storedPath, originalFileName, extension.TrimStart('.'), rows.Count, userName, fileHash, batchNotes, token);

            progress?.Report(20);
            await InsertBatchRowsAsync(batch.Id, rows, progress, token);
            progress?.Report(96);

            // Deja el lote listo para revisión apenas termina la importación.
            await ProcessMatchingAsync(batch.Id, userName, progress, token);
            progress?.Report(98);

            await _appEvents.LogAuditAsync("Costos", "ImportStructuredFile", "IA_Costos_Importacion_CAB", batch.Id.ToString(CultureInfo.InvariantCulture), "Importación estructurada creada.",
                new { profile.Id, profile.ProviderName, originalFileName, totalRows = rows.Count }, token);

            progress?.Report(100);
            return new CostosImportResultDto
            {
                Batch = batch,
                Rows = rows
            };
        }, "No se pudo crear la corrida de importación.", ct);
    }

    public async Task<CostosBatchDetailDto?> GetBatchDetailAsync(int batchId, CancellationToken ct = default)
    {
        const string sqlCab = """
            SELECT
                c.ID,
                c.FechaHora_Alta,
                ISNULL(c.Estado, ''),
                ISNULL(c.Usuario, ''),
                ISNULL(c.Proveedor, ''),
                ISNULL(c.CuentaProveedor, ''),
                ISNULL(c.ArchivoNombre, ''),
                ISNULL(c.ArchivoOrigen, ''),
                ISNULL(c.TipoArchivo, ''),
                ISNULL(c.TotalFilasLeidas, 0),
                ISNULL(c.TotalFilasConCosto, 0),
                ISNULL(c.TotalFilasConfirmadas, 0),
                ISNULL(c.TotalActualizadas, 0),
                ISNULL(c.TotalErrores, 0),
                c.IdInterODBC
            FROM dbo.IA_Costos_Importacion_CAB c
            WHERE c.ID = @Id
            """;

        const string sqlDet = """
            SELECT
                d.ID,
                d.FilaOrigen,
                ISNULL(d.Estado, ''),
                ISNULL(d.CodigoProveedorLeido, ''),
                ISNULL(d.DescripcionLeida, ''),
                d.PrecioCostoLeido,
                ISNULL(a.CodigoArtProveedor, ''),
                ISNULL(d.IdArticulo, ''),
                ISNULL(d.DescripcionArticulo, ''),
                d.CostoActual,
                d.CostoNuevo,
                ISNULL(d.TipoMatch, ''),
                ISNULL(d.ScoreMatch, 0),
                d.VariacionPct,
                ISNULL(d.AlertaDetalle, ''),
                ISNULL(d.DecisionUsuario, ''),
                ISNULL(d.ResultadoAplicacion, ''),
                ISNULL(d.ErrorAplicacion, '')
            FROM dbo.IA_Costos_Importacion_DET d
            LEFT JOIN dbo.V_MA_ARTICULOS a
                ON a.IDARTICULO = d.IdArticulo
            WHERE d.ID_CAB = @Id
            ORDER BY d.FilaOrigen
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        CostosBatchSummaryDto? batch = null;
        int? profileId = null;

        await using (var cmd = new SqlCommand(sqlCab, cn))
        {
            cmd.Parameters.AddWithValue("@Id", batchId);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                return null;

            batch = new CostosBatchSummaryDto
            {
                Id = rd.GetInt32(0),
                CreatedAt = rd.GetDateTime(1),
                Status = rd.GetString(2),
                UserName = rd.GetString(3),
                ProviderName = rd.GetString(4),
                ProviderAccount = rd.GetString(5),
                SourceFileName = rd.GetString(6),
                SourceFilePath = rd.GetString(7),
                SourceKind = rd.GetString(8),
                TotalRowsRead = rd.GetInt32(9),
                TotalRowsWithCost = rd.GetInt32(10),
                TotalConfirmed = rd.GetInt32(11),
                TotalUpdated = rd.GetInt32(12),
                TotalErrors = rd.GetInt32(13)
            };
            profileId = rd.IsDBNull(14) ? null : rd.GetInt32(14);
        }

        var rows = new List<CostosBatchDetailRowDto>();
        await using (var cmd = new SqlCommand(sqlDet, cn))
        {
            cmd.Parameters.AddWithValue("@Id", batchId);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                rows.Add(new CostosBatchDetailRowDto
                {
                    DetailId = rd.GetInt32(0),
                    RowNumber = rd.GetInt32(1),
                    Status = rd.GetString(2),
                    ProviderCodeRead = rd.GetString(3),
                    DescriptionRead = rd.GetString(4),
                    CostRead = rd.IsDBNull(5) ? null : rd.GetDecimal(5),
                    ArticleProviderCode = rd.GetString(6),
                    ArticleId = rd.GetString(7),
                    ArticleDescription = rd.GetString(8),
                    CurrentCost = rd.IsDBNull(9) ? null : rd.GetDecimal(9),
                    NewCost = rd.IsDBNull(10) ? null : rd.GetDecimal(10),
                    MatchType = rd.GetString(11),
                    MatchScore = Convert.ToDouble(rd.GetValue(12), CultureInfo.InvariantCulture),
                    VariationPct = rd.IsDBNull(13) ? null : Convert.ToDouble(rd.GetValue(13), CultureInfo.InvariantCulture),
                    AlertDetail = rd.GetString(14),
                    Decision = rd.GetString(15),
                    ApplyResult = rd.GetString(16),
                    ApplyError = rd.GetString(17)
                });
            }
        }

        CostosProfileDto? profile = null;
        if (profileId.HasValue)
            profile = (await GetProfilesAsync(ct)).FirstOrDefault(p => p.Id == profileId.Value);

        return new CostosBatchDetailDto
        {
            Batch = batch!,
            Profile = profile,
            Rows = rows
        };
    }

    public async Task<int?> GetLastUsedProfileIdAsync(CancellationToken ct = default)
    {
        try
        {
            const string sql = "SELECT TOP 1 IdInterODBC FROM dbo.IA_Costos_Importacion_CAB WHERE IdInterODBC IS NOT NULL ORDER BY ID DESC";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public async Task ProcessMatchingAsync(int batchId, string userName, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Costos", "ProcessMatching", async token =>
        {
            var detail = await GetBatchDetailAsync(batchId, token)
                ?? throw new InvalidOperationException("Lote no encontrado.");
            if (detail.Profile is null)
                throw new InvalidOperationException("El lote no tiene perfil asociado.");

            progress?.Report(2);
            var articles = await GetMasterArticlesAsync(detail.Profile.ProviderAccount, token);
            progress?.Report(5);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var totalRows = detail.Rows.Count;
            for (var i = 0; i < totalRows; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = detail.Rows[i];
                var candidates = BuildCandidates(row, articles, preferredProviderAccount: detail.Profile.ProviderAccount);
                var best = candidates.FirstOrDefault();
                var alert = BuildVariationAlert(best?.CurrentCost, row.CostRead);

                const string sql = """
                UPDATE dbo.IA_Costos_Importacion_DET
                SET
                    Estado = @Estado,
                    IdArticulo = @IdArticulo,
                    DescripcionArticulo = @DescripcionArticulo,
                    CostoActual = @CostoActual,
                    CostoNuevo = @CostoNuevo,
                    TipoMatch = @TipoMatch,
                    ScoreMatch = @ScoreMatch,
                    CoincidenciaCodigoProveedor = @CoincidenciaCodigoProveedor,
                    ScoreDescripcion = @ScoreDescripcion,
                    AlertaVariacion = @AlertaVariacion,
                    AlertaDetalle = @AlertaDetalle,
                    VariacionPct = @VariacionPct
                WHERE ID_CAB = @BatchId
                  AND FilaOrigen = @FilaOrigen
                """;

                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Estado", best is null ? "SIN_MATCH" : "MATCHEADO");
                cmd.Parameters.AddWithValue("@IdArticulo", (object?)best?.ArticleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DescripcionArticulo", (object?)best?.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CostoActual", (object?)best?.CurrentCost ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CostoNuevo", (object?)row.CostRead ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoMatch", (object?)best?.MatchType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ScoreMatch", best?.Score ?? 0d);
                cmd.Parameters.AddWithValue("@CoincidenciaCodigoProveedor", best?.ProviderCodeHit ?? false);
                cmd.Parameters.AddWithValue("@ScoreDescripcion", best?.DescriptionScore ?? 0d);
                cmd.Parameters.AddWithValue("@AlertaVariacion", !string.IsNullOrWhiteSpace(alert));
                cmd.Parameters.AddWithValue("@AlertaDetalle", (object?)NullIfEmpty(alert) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@VariacionPct", (object?)VariationPct(best?.CurrentCost, row.CostRead) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BatchId", batchId);
                cmd.Parameters.AddWithValue("@FilaOrigen", row.RowNumber);
                await cmd.ExecuteNonQueryAsync(token);

                progress?.Report(5 + (i + 1) * 90 / Math.Max(1, totalRows));
            }

            await using (var cmd = new SqlCommand("""
            UPDATE dbo.IA_Costos_Importacion_CAB
            SET Estado = 'EN_REVISION',
                TotalFilasConfirmadas = (
                    SELECT COUNT(*) FROM dbo.IA_Costos_Importacion_DET
                    WHERE ID_CAB = @BatchId AND Estado = 'CONFIRMADO'
                )
            WHERE ID = @BatchId
            """, cn))
            {
                cmd.Parameters.AddWithValue("@BatchId", batchId);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await _appEvents.LogAuditAsync("Costos", "ProcessMatching", "IA_Costos_Importacion_CAB", batchId.ToString(CultureInfo.InvariantCulture), "Matching procesado para lote.",
                new { batchId, userName }, token);
            progress?.Report(100);
            return true;
        }, "No se pudo procesar el matching del lote.", ct);
    }

    public async Task<IReadOnlyList<CostosMatchCandidateDto>> GetCandidatesAsync(int batchId, int rowNumber, string? searchTerm = null, bool includeFallback = false, CancellationToken ct = default)
    {
        var detail = await GetBatchDetailAsync(batchId, ct)
            ?? throw new InvalidOperationException("Lote no encontrado.");
        if (detail.Profile is null)
            return [];

        var row = detail.Rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is null)
            return [];

        var articles = string.IsNullOrWhiteSpace(searchTerm)
            ? await GetMasterArticlesAsync(detail.Profile.ProviderAccount, ct)
            : await SearchCandidateArticlesAsync(detail.Profile.ProviderAccount, searchTerm, includeFallback, ct);
        return BuildCandidates(row, articles, allowLooseMatches: !string.IsNullOrWhiteSpace(searchTerm), searchTerm, detail.Profile.ProviderAccount);
    }

    public async Task ConfirmRowAsync(int batchId, int rowNumber, string? articleId, string userName, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Costos", "ConfirmRow", async token =>
        {
            var detail = await GetBatchDetailAsync(batchId, token)
                ?? throw new InvalidOperationException("Lote no encontrado.");

            var row = detail.Rows.FirstOrDefault(r => r.RowNumber == rowNumber)
                ?? throw new InvalidOperationException("Fila no encontrada.");

            var chosen = string.IsNullOrWhiteSpace(articleId)
                ? (await GetCandidatesAsync(batchId, rowNumber, null, false, token)).FirstOrDefault()
                : (await GetCandidatesAsync(batchId, rowNumber, null, false, token)).FirstOrDefault(c => string.Equals(c.ArticleId, articleId, StringComparison.OrdinalIgnoreCase));

            if (chosen is null)
                throw new InvalidOperationException("No hay candidato para confirmar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string sql = """
            UPDATE dbo.IA_Costos_Importacion_DET
            SET
                Estado = 'CONFIRMADO',
                DecisionUsuario = 'CONFIRMAR',
                UsuarioRevision = @Usuario,
                FechaHoraRevision = GETDATE(),
                FueSeleccionManual = @Manual,
                IdArticulo = @IdArticulo,
                DescripcionArticulo = @DescripcionArticulo,
                CostoActual = @CostoActual,
                CostoNuevo = @CostoNuevo,
                TipoMatch = @TipoMatch,
                ScoreMatch = @ScoreMatch
            WHERE ID_CAB = @BatchId
              AND FilaOrigen = @FilaOrigen
            """;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Usuario", userName);
                cmd.Parameters.AddWithValue("@Manual", !string.Equals(chosen.MatchType, "provider_code_exact", StringComparison.OrdinalIgnoreCase));
                cmd.Parameters.AddWithValue("@IdArticulo", chosen.ArticleId);
                cmd.Parameters.AddWithValue("@DescripcionArticulo", chosen.Description);
                cmd.Parameters.AddWithValue("@CostoActual", (object?)chosen.CurrentCost ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CostoNuevo", (object?)row.CostRead ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoMatch", chosen.MatchType == "manual" ? "manual" : chosen.MatchType);
                cmd.Parameters.AddWithValue("@ScoreMatch", chosen.Score);
                cmd.Parameters.AddWithValue("@BatchId", batchId);
                cmd.Parameters.AddWithValue("@FilaOrigen", rowNumber);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await UpdateBatchConfirmedCountAsync(batchId, cn, token);
            await _appEvents.LogAuditAsync("Costos", "ConfirmRow", "IA_Costos_Importacion_DET", $"{batchId}:{rowNumber}", "Fila confirmada para aplicación.",
                new { batchId, rowNumber, chosen.ArticleId, userName }, token);
            return true;
        }, "No se pudo confirmar la fila seleccionada.", ct);
    }

    public async Task DiscardRowAsync(int batchId, int rowNumber, string userName, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Costos", "DiscardRow", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string sql = """
            UPDATE dbo.IA_Costos_Importacion_DET
            SET
                Estado = 'DESCARTADO',
                DecisionUsuario = 'DESCARTAR',
                UsuarioRevision = @Usuario,
                FechaHoraRevision = GETDATE()
            WHERE ID_CAB = @BatchId
              AND FilaOrigen = @FilaOrigen
            """;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Usuario", userName);
                cmd.Parameters.AddWithValue("@BatchId", batchId);
                cmd.Parameters.AddWithValue("@FilaOrigen", rowNumber);
                await cmd.ExecuteNonQueryAsync(token);
            }

            await UpdateBatchConfirmedCountAsync(batchId, cn, token);
            await _appEvents.LogAuditAsync("Costos", "DiscardRow", "IA_Costos_Importacion_DET", $"{batchId}:{rowNumber}", "Fila descartada.",
                new { batchId, rowNumber, userName }, token);
            return true;
        }, "No se pudo descartar la fila seleccionada.", ct);
    }

    public async Task<CostosApplyResultDto> ApplyConfirmedRowsAsync(int batchId, string userName, CancellationToken ct = default)
    {
        var detail = await GetBatchDetailAsync(batchId, ct)
            ?? throw new InvalidOperationException("Lote no encontrado.");
        var rowNumbers = detail.Rows
            .Where(r => string.Equals(r.Status, "CONFIRMADO", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.RowNumber)
            .ToArray();

        return await ApplySelectedRowsAsync(batchId, rowNumbers, userName, ct);
    }

    public async Task<CostosApplyResultDto> ApplySelectedRowsAsync(int batchId, IReadOnlyCollection<int> rowNumbers, string userName, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Costos", "ApplyConfirmedRows", async token =>
        {
            var detail = await GetBatchDetailAsync(batchId, token)
                ?? throw new InvalidOperationException("Lote no encontrado.");

            var rowsToApply = detail.Rows
                .Where(r => rowNumbers.Contains(r.RowNumber))
                .Where(r =>
                    string.Equals(r.Status, "CONFIRMADO", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Status, "MATCHEADO", StringComparison.OrdinalIgnoreCase))
                .Where(r => !string.IsNullOrWhiteSpace(r.ArticleId))
                .Where(r => r.CostRead.HasValue)
                .ToList();

            if (rowsToApply.Count == 0)
                throw new InvalidOperationException("No hay filas seleccionadas listas para aplicar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            try
            {
                var updated = 0;
                var same = 0;
                var errors = 0;
                var costosCambiados = new List<string>();

                foreach (var row in rowsToApply)
                {
                    try
                    {
                        if (string.Equals(row.Status, "MATCHEADO", StringComparison.OrdinalIgnoreCase))
                        {
                            await using var markConfirmed = new SqlCommand("""
                                UPDATE dbo.IA_Costos_Importacion_DET
                                SET
                                    Estado = 'CONFIRMADO',
                                    DecisionUsuario = COALESCE(NULLIF(DecisionUsuario, ''), 'CONFIRMAR'),
                                    UsuarioRevision = COALESCE(NULLIF(UsuarioRevision, ''), @Usuario),
                                    FechaHoraRevision = COALESCE(FechaHoraRevision, GETDATE())
                                WHERE ID = @Id
                                """, cn, tx);
                            markConfirmed.Parameters.AddWithValue("@Usuario", userName);
                            markConfirmed.Parameters.AddWithValue("@Id", row.DetailId);
                            await markConfirmed.ExecuteNonQueryAsync(token);
                        }

                        var currentCost = await GetCurrentArticleCostAsync(row.ArticleId, cn, tx, token);
                        if (currentCost.Exists is false)
                        {
                            await MarkDetailAppliedAsync(row.DetailId, userName, "ERROR", "No se encontró el artículo a actualizar.", false, cn, tx, token);
                            errors++;
                            continue;
                        }

                        var newCost = row.CostRead!.Value;
                        if (currentCost.Value == newCost)
                        {
                            await MarkDetailAppliedAsync(row.DetailId, userName, "SIN_CAMBIO", null, true, cn, tx, token);
                            await InsertHistoryRowAsync(detail, row, currentCost.Value, newCost, userName, cn, tx, token);
                            same++;
                            continue;
                        }

                        await using (var cmd = new SqlCommand("""
                        UPDATE dbo.V_MA_ARTICULOS
                        SET COSTO = @Costo,
                            FhUltimoCosto = GETDATE(),
                            Usuario = @Usuario
                        WHERE IDARTICULO = @IdArticulo
                        """, cn, tx))
                    {
                        cmd.Parameters.AddWithValue("@Costo", newCost);
                        cmd.Parameters.AddWithValue("@Usuario", userName);
                        cmd.Parameters.AddWithValue("@IdArticulo", row.ArticleId);
                        var affected = await cmd.ExecuteNonQueryAsync(token);
                        if (affected <= 0)
                            throw new InvalidOperationException("La actualización del costo no afectó registros.");
                    }

                        await MarkDetailAppliedAsync(row.DetailId, userName, "OK", null, true, cn, tx, token);
                        await InsertHistoryRowAsync(detail, row, currentCost.Value, newCost, userName, cn, tx, token);
                        costosCambiados.Add(row.ArticleId);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        await MarkDetailAppliedAsync(row.DetailId, userName, "ERROR", ex.Message, false, cn, tx, token);
                        errors++;
                    }
                }

                var status = errors > 0
                    ? (updated > 0 || same > 0 ? "APLICADA_PARCIAL" : "ERROR")
                    : "APLICADA";

                await using (var cmd = new SqlCommand("""
                UPDATE dbo.IA_Costos_Importacion_CAB
                SET
                    Estado = @Estado,
                    FechaHora_FinProceso = GETDATE(),
                    TotalActualizadas = @TotalActualizadas,
                    TotalAltas = 0,
                    TotalSinCambios = @TotalSinCambios,
                    TotalErrores = @TotalErrores,
                    TotalFilasConfirmadas = (
                        SELECT COUNT(*) FROM dbo.IA_Costos_Importacion_DET
                        WHERE ID_CAB = @BatchId AND Estado IN ('CONFIRMADO', 'APLICADO')
                    )
                WHERE ID = @BatchId
                """, cn, tx))
            {
                cmd.Parameters.AddWithValue("@Estado", status);
                cmd.Parameters.AddWithValue("@TotalActualizadas", updated);
                cmd.Parameters.AddWithValue("@TotalSinCambios", same);
                cmd.Parameters.AddWithValue("@TotalErrores", errors);
                cmd.Parameters.AddWithValue("@BatchId", batchId);
                await cmd.ExecuteNonQueryAsync(token);
                }

                // Fórmulas de producción: los productos que usan estos artículos como insumo se recalculan en la
                // misma transacción (con PreciosArticulosPorBorrador=SI sus precios nuevos van al borrador).
                var cascada = costosCambiados.Count == 0
                    ? new ArticuloCascadaResultado()
                    : await ArticulosService.PropagarCostosAsync(cn, tx, costosCambiados, userName, token);

                await tx.CommitAsync(token);
                await _appEvents.LogAuditAsync("Costos", "ApplyConfirmedRows", "IA_Costos_Importacion_CAB", batchId.ToString(CultureInfo.InvariantCulture), "Aplicación de costos ejecutada.",
                    new { batchId, userName, updated, same, errors, status, productosRecalculados = cascada.Cambios.Count, cascada.AlBorrador }, token);

                return new CostosApplyResultDto
                {
                    BatchId = batchId,
                    Updated = updated,
                    Same = same,
                    Errors = errors,
                    Status = status,
                    ProductosRecalculados = cascada.Cambios.Count,
                    PreciosAlBorrador = cascada.AlBorrador && cascada.Cambios.Count > 0
                };
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }, "No se pudieron aplicar las actualizaciones seleccionadas del lote.", ct);
    }

    public async Task<CostosUndoResultDto> UndoLastApplyAsync(int batchId, string userName, CancellationToken ct = default)
    {
        return await ExecuteLoggedAsync("Costos", "UndoLastApply", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            try
            {
                var rows = new List<(int DetailId, string ArticleId, decimal? PreviousCost)>();
                await using (var cmd = new SqlCommand("""
                    SELECT
                        d.ID,
                        ISNULL(h.ArticuloID, ''),
                        h.CostoAnterior
                    FROM dbo.IA_Costos_Importacion_DET d
                    OUTER APPLY (
                        SELECT TOP (1)
                            hh.ArticuloID,
                            hh.CostoAnterior
                        FROM dbo.IA_Costos_Actualizacion_Hist hh
                        WHERE hh.ImportacionDetID = d.ID
                        ORDER BY hh.ID DESC
                    ) h
                    WHERE d.ID_CAB = @BatchId
                      AND d.Estado = 'APLICADO'
                    """, cn, tx))
                {
                    cmd.Parameters.AddWithValue("@BatchId", batchId);
                    await using var rd = await cmd.ExecuteReaderAsync(token);
                    while (await rd.ReadAsync(token))
                    {
                        rows.Add((
                            rd.GetInt32(0),
                            rd.GetString(1),
                            rd.IsDBNull(2) ? null : rd.GetDecimal(2)));
                    }
                }

                if (rows.Count == 0)
                    throw new InvalidOperationException("No hay filas aplicadas para deshacer en este lote.");

                var reverted = 0;
                var errors = 0;
                var costosRevertidos = new List<string>();
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.ArticleId) || row.PreviousCost is null)
                    {
                        errors++;
                        continue;
                    }

                    try
                    {
                        await using (var cmd = new SqlCommand("""
                            UPDATE dbo.V_MA_ARTICULOS
                            SET COSTO = @CostoAnterior,
                                FhUltimoCosto = GETDATE(),
                                Usuario = @Usuario
                            WHERE IDARTICULO = @IdArticulo
                            """, cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@CostoAnterior", row.PreviousCost.Value);
                            cmd.Parameters.AddWithValue("@Usuario", userName);
                            cmd.Parameters.AddWithValue("@IdArticulo", row.ArticleId);
                            await cmd.ExecuteNonQueryAsync(token);
                        }

                        await using (var cmd = new SqlCommand("""
                            UPDATE dbo.IA_Costos_Importacion_DET
                            SET Estado = 'CONFIRMADO',
                                Aplicado = 0,
                                FechaHoraAplicacion = NULL,
                                UsuarioAplicacion = NULL,
                                ResultadoAplicacion = 'DESHECHO',
                                ErrorAplicacion = NULL
                            WHERE ID = @Id
                            """, cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Id", row.DetailId);
                            await cmd.ExecuteNonQueryAsync(token);
                        }

                        costosRevertidos.Add(row.ArticleId);
                        reverted++;
                    }
                    catch
                    {
                        errors++;
                    }
                }

                // Fórmulas de producción: al volver el costo atrás, los productos que usan esos artículos como
                // insumo también vuelven a recalcularse (si no, quedarían con el costo de la importación deshecha).
                var cascada = costosRevertidos.Count == 0
                    ? new ArticuloCascadaResultado()
                    : await ArticulosService.PropagarCostosAsync(cn, tx, costosRevertidos, userName, token);

                var status = errors > 0 ? "DESHECHA_PARCIAL" : "EN_REVISION";
                await using (var cmd = new SqlCommand("""
                    UPDATE dbo.IA_Costos_Importacion_CAB
                    SET
                        Estado = @Estado,
                        TotalActualizadas = CASE WHEN TotalActualizadas >= @Reverted THEN TotalActualizadas - @Reverted ELSE 0 END,
                        TotalErrores = @Errores
                    WHERE ID = @BatchId
                    """, cn, tx))
                {
                    cmd.Parameters.AddWithValue("@Estado", status);
                    cmd.Parameters.AddWithValue("@Reverted", reverted);
                    cmd.Parameters.AddWithValue("@Errores", errors);
                    cmd.Parameters.AddWithValue("@BatchId", batchId);
                    await cmd.ExecuteNonQueryAsync(token);
                }

                await tx.CommitAsync(token);
                await _appEvents.LogAuditAsync("Costos", "UndoLastApply", "IA_Costos_Importacion_CAB", batchId.ToString(CultureInfo.InvariantCulture), "Se deshicieron actualizaciones del lote.",
                    new { batchId, userName, reverted, errors, productosRecalculados = cascada.Cambios.Count, cascada.AlBorrador }, token);

                return new CostosUndoResultDto
                {
                    BatchId = batchId,
                    Reverted = reverted,
                    Errors = errors,
                    Status = status,
                    ProductosRecalculados = cascada.Cambios.Count,
                    PreciosAlBorrador = cascada.AlBorrador && cascada.Cambios.Count > 0
                };
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }, "No se pudo deshacer la última aplicación del lote.", ct);
    }

    private async Task<CostosBatchSummaryDto> CreateBatchAsync(
        CostosProfileDto profile,
        string storedPath,
        string originalFileName,
        string sourceKind,
        int totalRows,
        string userName,
        string fileHash,
        string? notes,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.IA_Costos_Importacion_CAB
            (
                FechaHora_InicioProceso,
                Estado,
                Usuario,
                IdInterODBC,
                Proveedor,
                CuentaProveedor,
                PoliticaPrecios,
                Lista,
                SoloAlta,
                SoloModificacion,
                ArchivoOrigen,
                ArchivoNombre,
                ArchivoHash,
                TipoArchivo,
                HojaConfigurada,
                RangoDesde,
                RangoHasta,
                NotasConfiguracion,
                TotalFilasLeidas,
                TotalFilasConCosto
            )
            OUTPUT INSERTED.ID, INSERTED.FechaHora_Alta
            VALUES
            (
                GETDATE(),
                'IMPORTADA',
                @Usuario,
                @IdInterODBC,
                @Proveedor,
                @CuentaProveedor,
                @PoliticaPrecios,
                @Lista,
                @SoloAlta,
                @SoloModificacion,
                @ArchivoOrigen,
                @ArchivoNombre,
                @ArchivoHash,
                @TipoArchivo,
                @HojaConfigurada,
                @RangoDesde,
                @RangoHasta,
                @NotasConfiguracion,
                @TotalFilasLeidas,
                @TotalFilasConCosto
            )
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", userName);
        cmd.Parameters.AddWithValue("@IdInterODBC", profile.Id);
        cmd.Parameters.AddWithValue("@Proveedor", profile.ProviderName);
        cmd.Parameters.AddWithValue("@CuentaProveedor", profile.ProviderAccount);
        cmd.Parameters.AddWithValue("@PoliticaPrecios", (object?)NullIfEmpty(profile.PricePolicy) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lista", (object?)NullIfEmpty(profile.ListCode) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SoloAlta", profile.OnlyAdd);
        cmd.Parameters.AddWithValue("@SoloModificacion", profile.OnlyModify);
        cmd.Parameters.AddWithValue("@ArchivoOrigen", storedPath);
        cmd.Parameters.AddWithValue("@ArchivoNombre", originalFileName);
        cmd.Parameters.AddWithValue("@ArchivoHash", fileHash);
        cmd.Parameters.AddWithValue("@TipoArchivo", sourceKind);
        cmd.Parameters.AddWithValue("@HojaConfigurada", (object?)NullIfEmpty(profile.SheetName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RangoDesde", (object?)NullIfEmpty(profile.RangeFrom) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RangoHasta", (object?)NullIfEmpty(profile.RangeTo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NotasConfiguracion", (object?)NullIfEmpty(notes ?? string.Empty) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TotalFilasLeidas", totalRows);
        cmd.Parameters.AddWithValue("@TotalFilasConCosto", totalRows);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        return new CostosBatchSummaryDto
        {
            Id = rd.GetInt32(0),
            CreatedAt = rd.GetDateTime(1),
            Status = "IMPORTADA",
            UserName = userName,
            ProviderName = profile.ProviderName,
            ProviderAccount = profile.ProviderAccount,
            SourceFileName = originalFileName,
            SourceFilePath = storedPath,
            SourceKind = sourceKind,
            TotalRowsRead = totalRows,
            TotalRowsWithCost = totalRows
        };
    }

    private async Task InsertBatchRowsAsync(int batchId, IReadOnlyList<CostosImportedRowDto> rows, IProgress<int>? progress, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.IA_Costos_Importacion_DET
            (
                ID_CAB,
                FilaOrigen,
                CodigoProveedorLeido,
                DescripcionLeida,
                PrecioCostoLeido,
                JsonFilaOriginal
            )
            VALUES
            (
                @ID_CAB,
                @FilaOrigen,
                @CodigoProveedorLeido,
                @DescripcionLeida,
                @PrecioCostoLeido,
                @JsonFilaOriginal
            )
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i];
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@ID_CAB", batchId);
            cmd.Parameters.AddWithValue("@FilaOrigen", row.RowNumber);
            cmd.Parameters.AddWithValue("@CodigoProveedorLeido", (object?)NullIfEmpty(row.ProviderCode) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DescripcionLeida", row.Description);
            cmd.Parameters.AddWithValue("@PrecioCostoLeido", (object?)row.CostPrice ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@JsonFilaOriginal", JsonSerializer.Serialize(row.RawValues));
            await cmd.ExecuteNonQueryAsync(ct);
            // Reporta progreso de 20% a 95% durante la inserción de filas
            progress?.Report(20 + (i + 1) * 75 / rows.Count);
        }
    }

    private static List<CostosImportedRowDto> ReadXlsxRows(string path, string preferredSheet, int? forcedPriceColIndex = null)
    {
        using var workbook = new XLWorkbook(path);
        var sheets = workbook.Worksheets.ToList();
        if (!string.IsNullOrWhiteSpace(preferredSheet))
        {
            sheets = [.. sheets.OrderByDescending(ws => string.Equals(ws.Name.Trim(), preferredSheet.Trim(), StringComparison.OrdinalIgnoreCase))];
        }

        foreach (var sheet in sheets)
        {
            var rows = sheet.RowsUsed()
                .Select(r => r.CellsUsed(XLCellsUsedOptions.AllContents).Select(c => c.GetFormattedString()).ToList())
                .Where(r => r.Count > 0)
                .ToList();
            var parsed = ParseMatrix(rows, sheet.Name, forcedPriceColIndex);
            if (parsed.Count > 0)
                return parsed;
        }

        return [];
    }

    private static List<CostosImportedRowDto> ReadDelimitedRows(string path, int? forcedPriceColIndex = null)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8);
        parser.SetDelimiters([",", ";", "|", "\t"]);
        parser.HasFieldsEnclosedInQuotes = true;

        var rows = new List<List<string>>();
        while (!parser.EndOfData)
        {
            rows.Add([.. (parser.ReadFields() ?? [])]);
        }

        return ParseMatrix(rows, null, forcedPriceColIndex);
    }

    public async Task<CostosFileColumnsDto> DetectFileColumnsAsync(string fileName, Stream content, string preferredSheet = "", CancellationToken ct = default)
    {
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            List<List<string>> matrix;
            if (extension == ".xlsx")
            {
                using var ms = new MemoryStream();
                await content.CopyToAsync(ms, ct);
                ms.Position = 0;
                matrix = ReadXlsxMatrix(ms, preferredSheet);
            }
            else
            {
                matrix = ReadDelimitedMatrix(content);
            }

            var (headerIdx, _, allPriceCols) = DetectHeader(matrix);
            if (headerIdx is null || allPriceCols.Count == 0)
                return new CostosFileColumnsDto { Detected = false };

            var headerRow = matrix[headerIdx.Value];
            var allCols = headerRow
                .Select((name, idx) => new CostosColumnDto { ColIndex = idx, Name = name })
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();

            var sampleRows = matrix
                .Skip(headerIdx.Value + 1)
                .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
                .Take(3)
                .Select(r => (IReadOnlyList<string>)r.AsReadOnly())
                .ToList();

            return new CostosFileColumnsDto
            {
                Detected = true,
                AllColumns = allCols,
                PriceCandidates = allPriceCols,
                DefaultPriceColIndex = allPriceCols[0].ColIndex,
                SampleRows = sampleRows
            };
        }
        catch
        {
            return new CostosFileColumnsDto { Detected = false };
        }
    }

    private static List<List<string>> ReadXlsxMatrix(Stream stream, string preferredSheet)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        using var workbook = new XLWorkbook(ms);
        var sheets = workbook.Worksheets.ToList();
        if (!string.IsNullOrWhiteSpace(preferredSheet))
            sheets = [.. sheets.OrderByDescending(ws => string.Equals(ws.Name.Trim(), preferredSheet.Trim(), StringComparison.OrdinalIgnoreCase))];

        foreach (var sheet in sheets)
        {
            var rows = sheet.RowsUsed()
                .Select(r => r.CellsUsed(XLCellsUsedOptions.AllContents).Select(c => c.GetFormattedString()).ToList())
                .Where(r => r.Count > 0)
                .ToList();
            if (rows.Count > 0)
                return rows;
        }

        return [];
    }

    private static List<List<string>> ReadDelimitedMatrix(Stream stream)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8);
        parser.SetDelimiters([",", ";", "|", "\t"]);
        parser.HasFieldsEnclosedInQuotes = true;

        var rows = new List<List<string>>();
        while (!parser.EndOfData)
            rows.Add([.. (parser.ReadFields() ?? [])]);

        return rows;
    }

    private static List<CostosImportedRowDto> ParseMatrix(List<List<string>> matrix, string? sheetName, int? forcedPriceColIndex = null)
    {
        var (headerIdx, map, _) = DetectHeader(matrix, forcedPriceColIndex);
        if (headerIdx is null || !map.TryGetValue("description", out var descriptionIdx) || !map.TryGetValue("cost_price", out var priceIdx))
            return [];

        int? codeIdx = map.TryGetValue("provider_code", out var detectedCodeIdx) ? detectedCodeIdx : null;
        var items = new List<CostosImportedRowDto>();

        for (var i = headerIdx.Value + 1; i < matrix.Count; i++)
        {
            var row = matrix[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var description = Cell(row, descriptionIdx).Trim();
            var rawPrice = Cell(row, priceIdx).Trim();
            var cost = ParseDecimal(rawPrice);
            if (string.IsNullOrWhiteSpace(description) || cost is null)
                continue;

            var providerCode = codeIdx.HasValue ? Cell(row, codeIdx.Value).Trim() : string.Empty;
            items.Add(new CostosImportedRowDto
            {
                RowNumber = i + 1,
                ProviderCode = providerCode,
                Description = description,
                CostPrice = cost,
                RawPrice = rawPrice,
                SourceSheet = sheetName,
                RawValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider_code"] = providerCode,
                    ["description"] = description,
                    ["cost_price"] = rawPrice
                }
            });
        }

        return items;
    }

    private static (int? HeaderIndex, Dictionary<string, int> Map, IReadOnlyList<CostosColumnDto> AllPriceColumns) DetectHeader(List<List<string>> matrix, int? forcedPriceColIndex = null)
    {
        for (var rowIndex = 0; rowIndex < Math.Min(matrix.Count, 20); rowIndex++)
        {
            var rawRow = matrix[rowIndex];
            var normalized = rawRow.Select(NormalizeHeader).ToList();
            if (normalized.All(string.IsNullOrWhiteSpace))
                continue;

            var map = new Dictionary<string, int>();
            var allPriceCols = new List<CostosColumnDto>();

            for (var colIndex = 0; colIndex < normalized.Count; colIndex++)
            {
                var value = normalized[colIndex];
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!map.ContainsKey("provider_code") && CodeCandidates.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    map["provider_code"] = colIndex;
                if (!map.ContainsKey("description") && DescriptionCandidates.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    map["description"] = colIndex;
                if (PriceCandidates.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    allPriceCols.Add(new CostosColumnDto { ColIndex = colIndex, Name = colIndex < rawRow.Count ? rawRow[colIndex] : value });
            }

            if (!map.ContainsKey("description") || allPriceCols.Count == 0)
                continue;

            // Si el usuario especificó una columna y es válida, usarla; si no, la primera detectada.
            var priceIdx = forcedPriceColIndex.HasValue && allPriceCols.Any(c => c.ColIndex == forcedPriceColIndex.Value)
                ? forcedPriceColIndex.Value
                : allPriceCols[0].ColIndex;
            map["cost_price"] = priceIdx;

            return (rowIndex, map, allPriceCols);
        }

        return (null, [], []);
    }

    private static string NormalizeHeader(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Cell(List<string> row, int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;

    private static decimal? ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value
            .Replace("$", string.Empty)
            .Replace("usd", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("ars", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.GetCultureInfo("es-AR"), out var local))
            return local;
        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
            return invariant;

        cleaned = cleaned.Replace(".", string.Empty).Replace(",", ".");
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var normalized) ? normalized : null;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsMissingObject(SqlException ex) => ex.Number == 208;

    private static void ValidateProfile(CostosProfileDto profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProviderName))
            throw new InvalidOperationException("El nombre del perfil es obligatorio.");
        if (profile.OnlyAdd && profile.OnlyModify)
            throw new InvalidOperationException("No conviene marcar SoloAlta y SoloModificacion al mismo tiempo.");
    }

    private static void AddProfileParameters(SqlCommand cmd, CostosProfileDto profile)
    {
        cmd.Parameters.AddWithValue("@Proveedor", profile.ProviderName.Trim());
        cmd.Parameters.AddWithValue("@CuentaProveedor", profile.ProviderAccount.Trim());
        cmd.Parameters.AddWithValue("@PoliticaPrecios", (object?)NullIfEmpty(profile.PricePolicy) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Hoja", (object?)NullIfEmpty(profile.SheetName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Lista", (object?)NullIfEmpty(profile.ListCode) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RangoDesde", (object?)NullIfEmpty(profile.RangeFrom) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RangoHasta", (object?)NullIfEmpty(profile.RangeTo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CamposClave", (object?)NullIfEmpty(profile.KeyFields) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notas", (object?)NullIfEmpty(profile.Notes) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SoloAlta", profile.OnlyAdd);
        cmd.Parameters.AddWithValue("@SoloModificacion", profile.OnlyModify);
    }

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
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ValidateProviderCodeAsync(string providerCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
            return;

        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.VT_PROVEEDORES
            WHERE LTRIM(RTRIM(CODIGO)) = @Code
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Code", providerCode.Trim());
        var exists = await cmd.ExecuteScalarAsync(ct);
        if (exists is null)
            throw new InvalidOperationException("El código de proveedor no existe. Podés dejarlo vacío o elegir uno válido desde la búsqueda.");
    }

    private async Task<List<MasterArticleMatch>> GetMasterArticlesAsync(string providerAccount, CancellationToken ct)
    {
        const string sql = """
            SELECT
                ISNULL(IDARTICULO, ''),
                ISNULL(IDARTICULO, ''),
                ISNULL(DESCRIPCION, ''),
                COSTO,
                ISNULL(CodigoArtProveedor, ''),
                ISNULL(CUENTAPROVEEDOR, '')
            FROM dbo.V_MA_ARTICULOS
            WHERE (@CuentaProveedor = '' OR ISNULL(CUENTAPROVEEDOR, '') = @CuentaProveedor)
            """;

        var items = new List<MasterArticleMatch>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CuentaProveedor", providerAccount);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new MasterArticleMatch
            {
                ArticleId = rd.GetString(0),
                ArticleCode = rd.GetString(1),
                Description = rd.GetString(2),
                CurrentCost = rd.IsDBNull(3) ? null : rd.GetDecimal(3),
                ProviderCode = rd.GetString(4),
                ProviderAccount = rd.GetString(5)
            });
        }

        return items;
    }

    private async Task<List<MasterArticleMatch>> SearchCandidateArticlesAsync(string providerAccount, string searchTerm, bool includeFallback, CancellationToken ct)
    {
        var sql = """
            SELECT TOP (60)
                ISNULL(IDARTICULO, ''),
                ISNULL(IDARTICULO, ''),
                ISNULL(DESCRIPCION, ''),
                COSTO,
                ISNULL(CodigoArtProveedor, ''),
                ISNULL(CUENTAPROVEEDOR, '')
            FROM dbo.V_MA_ARTICULOS
            WHERE
                (
                    (@CuentaProveedor <> '' AND ISNULL(CUENTAPROVEEDOR, '') = @CuentaProveedor)
                    OR @IncludeFallback = 1
                )
                AND
                (
                    ISNULL(CodigoArtProveedor, '') LIKE '%' + @Term + '%'
                    OR ISNULL(DESCRIPCION, '') LIKE '%' + @Term + '%'
                    OR ISNULL(IDARTICULO, '') LIKE '%' + @Term + '%'
                )
            ORDER BY
                CASE WHEN ISNULL(CUENTAPROVEEDOR, '') = @CuentaProveedor THEN 0 ELSE 1 END,
                CASE WHEN ISNULL(CodigoArtProveedor, '') LIKE @StartsWith THEN 0 ELSE 1 END,
                DESCRIPCION
            """;

        var items = new List<MasterArticleMatch>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CuentaProveedor", providerAccount);
        cmd.Parameters.AddWithValue("@IncludeFallback", includeFallback);
        cmd.Parameters.AddWithValue("@Term", searchTerm.Trim());
        cmd.Parameters.AddWithValue("@StartsWith", $"{searchTerm.Trim()}%");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            items.Add(new MasterArticleMatch
            {
                ArticleId = rd.GetString(0),
                ArticleCode = rd.GetString(1),
                Description = rd.GetString(2),
                CurrentCost = rd.IsDBNull(3) ? null : rd.GetDecimal(3),
                ProviderCode = rd.GetString(4),
                ProviderAccount = rd.GetString(5)
            });
        }

        return items;
    }

    private static IReadOnlyList<CostosMatchCandidateDto> BuildCandidates(CostosBatchDetailRowDto row, IReadOnlyList<MasterArticleMatch> articles, bool allowLooseMatches = false, string? searchTerm = null, string? preferredProviderAccount = null)
    {
        var importedCode = NormalizeCode(row.ProviderCodeRead);
        var importedDesc = NormalizeText(row.DescriptionRead);
        var normalizedSearch = NormalizeText(searchTerm ?? string.Empty);
        var results = new List<CostosMatchCandidateDto>();

        foreach (var article in articles)
        {
            var articleCode = NormalizeCode(article.ProviderCode);
            var descScore = Similarity(importedDesc, NormalizeText(article.Description));
            var variation = VariationPct(article.CurrentCost, row.CostRead);
            var variationPenalty = VariationPenalty(variation);
            var score = 0d;
            var matchType = string.Empty;
            var providerHit = !string.IsNullOrWhiteSpace(importedCode)
                && !string.IsNullOrWhiteSpace(articleCode)
                && importedCode == articleCode;
            var providerScope = string.Equals(article.ProviderAccount, preferredProviderAccount, StringComparison.OrdinalIgnoreCase);

            if (providerHit)
            {
                score = 100;
                matchType = "provider_code_exact";
            }
            else if (descScore >= 55)
            {
                score = (descScore * 0.72) + variationPenalty + (providerScope ? 8 : 0);
                matchType = "description_fuzzy";
            }

            if (providerHit)
            {
                score += Math.Min(12, descScore * 0.12);
            }

            if (!string.IsNullOrWhiteSpace(matchType) && !providerHit)
                score = Math.Max(0, Math.Min(99, score));

            var searchHit = string.IsNullOrWhiteSpace(normalizedSearch)
                || NormalizeText(article.Description).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || NormalizeText(article.ProviderCode).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || NormalizeText(article.ArticleId).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(matchType))
            {
                if (!allowLooseMatches || !searchHit)
                    continue;

                matchType = "manual_search";
                score = Math.Max(0, 35 + (descScore * 0.4) + variationPenalty);
            }

            results.Add(new CostosMatchCandidateDto
            {
                ImportedRowNumber = row.RowNumber,
                ArticleId = article.ArticleId,
                ArticleCode = article.ArticleCode,
                Description = article.Description,
                CurrentCost = article.CurrentCost,
                ProviderCode = article.ProviderCode,
                ProviderAccount = article.ProviderAccount,
                MatchType = matchType,
                Score = Math.Round(score, 2),
                ProviderCodeHit = providerHit,
                DescriptionScore = Math.Round(descScore, 2),
                VariationPct = variation,
                SearchScope = providerScope ? "proveedor" : "plan_b",
                Notes = $"desc={descScore:F2}; var={(variation.HasValue ? variation.Value.ToString("F2", CultureInfo.InvariantCulture) : "n/a")}"
            });
        }

        return results
            .OrderByDescending(r => string.Equals(r.MatchType, "provider_code_exact", StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.SearchScope)
            .ThenByDescending(r => r.Score)
            .Take(25)
            .ToList();
    }

    private static string BuildVariationAlert(decimal? previousCost, decimal? newCost)
    {
        var variation = VariationPct(previousCost, newCost);
        if (variation is null)
            return string.Empty;

        var abs = Math.Abs(variation.Value);
        if (abs >= 40) return $"Variación alta a revisar: {variation.Value:F2}%";
        if (abs >= 20) return $"Variación media: {variation.Value:F2}%";
        return string.Empty;
    }

    private static double? VariationPct(decimal? previousCost, decimal? newCost)
    {
        if (previousCost is null || previousCost == 0 || newCost is null)
            return null;

        return (double)(((newCost.Value - previousCost.Value) / previousCost.Value) * 100);
    }

    private static double VariationPenalty(double? variationPct)
    {
        if (variationPct is null)
            return 0;

        var abs = Math.Abs(variationPct.Value);
        return abs switch
        {
            <= 10 => 16,
            <= 20 => 12,
            <= 40 => 6,
            <= 60 => -10,
            <= 80 => -20,
            _ => -35
        };
    }

    private static string NormalizeCode(string value)
    {
        var raw = (value ?? string.Empty).Trim();
        // Excel numeric cells may come as "1915.00" or "1,915"; normalize to integer string.
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
            && num >= 0 && num == Math.Truncate(num))
        {
            return ((long)num).ToString(CultureInfo.InvariantCulture);
        }
        return string.Concat(raw.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    private static string NormalizeText(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        var chars = normalized
            .Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double Similarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0;

        var aTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToHashSet();
        var bTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToHashSet();
        var inter = aTokens.Intersect(bTokens).Count();
        var union = aTokens.Union(bTokens).Count();
        return union == 0 ? 0 : inter * 100d / union;
    }

    private static async Task UpdateBatchConfirmedCountAsync(int batchId, SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE dbo.IA_Costos_Importacion_CAB
            SET Estado = 'LISTA_PARA_APLICAR',
                TotalFilasConfirmadas = (
                    SELECT COUNT(*) FROM dbo.IA_Costos_Importacion_DET
                    WHERE ID_CAB = @BatchId AND Estado = 'CONFIRMADO'
                )
            WHERE ID = @BatchId
            """, cn);
        cmd.Parameters.AddWithValue("@BatchId", batchId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(bool Exists, decimal? Value)> GetCurrentArticleCostAsync(string articleId, SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) COSTO
            FROM dbo.V_MA_ARTICULOS
            WHERE IDARTICULO = @IdArticulo
            """, cn, tx);
        cmd.Parameters.AddWithValue("@IdArticulo", articleId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return (false, null);

        return (true, rd.IsDBNull(0) ? null : rd.GetDecimal(0));
    }

    private static async Task MarkDetailAppliedAsync(int detailId, string userName, string result, string? errorText, bool applied, SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE dbo.IA_Costos_Importacion_DET
            SET
                Estado = CASE
                    WHEN @Aplicado = 1 THEN 'APLICADO'
                    WHEN @Resultado = 'ERROR' THEN 'ERROR'
                    ELSE Estado
                END,
                Aplicado = @Aplicado,
                FechaHoraAplicacion = GETDATE(),
                UsuarioAplicacion = @Usuario,
                ResultadoAplicacion = @Resultado,
                ErrorAplicacion = @Error
            WHERE ID = @Id
            """, cn, tx);
        cmd.Parameters.AddWithValue("@Aplicado", applied);
        cmd.Parameters.AddWithValue("@Resultado", result);
        cmd.Parameters.AddWithValue("@Usuario", userName);
        cmd.Parameters.AddWithValue("@Error", (object?)NullIfEmpty(errorText ?? string.Empty) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", detailId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertHistoryRowAsync(
        CostosBatchDetailDto detail,
        CostosBatchDetailRowDto row,
        decimal? previousCost,
        decimal? newCost,
        string userName,
        SqlConnection cn,
        SqlTransaction tx,
        CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO dbo.IA_Costos_Actualizacion_Hist
            (
                ImportacionID,
                ImportacionDetID,
                Usuario,
                Proveedor,
                CuentaProveedor,
                ArchivoOrigen,
                FilaOrigen,
                ArticuloID,
                ArticuloCodigo,
                ProveedorCodigo,
                DescripcionImportada,
                DescripcionArticulo,
                CostoAnterior,
                CostoNuevo,
                VariacionPct,
                MatchTipo,
                MatchScore,
                AlertaVariacion,
                AlertaDetalle,
                Observaciones
            )
            VALUES
            (
                @ImportacionID,
                @ImportacionDetID,
                @Usuario,
                @Proveedor,
                @CuentaProveedor,
                @ArchivoOrigen,
                @FilaOrigen,
                @ArticuloID,
                @ArticuloCodigo,
                @ProveedorCodigo,
                @DescripcionImportada,
                @DescripcionArticulo,
                @CostoAnterior,
                @CostoNuevo,
                @VariacionPct,
                @MatchTipo,
                @MatchScore,
                @AlertaVariacion,
                @AlertaDetalle,
                @Observaciones
            )
            """, cn, tx);
        cmd.Parameters.AddWithValue("@ImportacionID", detail.Batch.Id);
        cmd.Parameters.AddWithValue("@ImportacionDetID", row.DetailId);
        cmd.Parameters.AddWithValue("@Usuario", userName);
        cmd.Parameters.AddWithValue("@Proveedor", (object?)NullIfEmpty(detail.Batch.ProviderName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CuentaProveedor", (object?)NullIfEmpty(detail.Batch.ProviderAccount) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArchivoOrigen", detail.Batch.SourceFileName);
        cmd.Parameters.AddWithValue("@FilaOrigen", row.RowNumber);
        cmd.Parameters.AddWithValue("@ArticuloID", row.ArticleId);
        cmd.Parameters.AddWithValue("@ArticuloCodigo", row.ArticleId);
        cmd.Parameters.AddWithValue("@ProveedorCodigo", (object?)NullIfEmpty(row.ProviderCodeRead) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DescripcionImportada", (object?)NullIfEmpty(row.DescriptionRead) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DescripcionArticulo", (object?)NullIfEmpty(row.ArticleDescription) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CostoAnterior", (object?)previousCost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CostoNuevo", (object?)newCost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VariacionPct", (object?)VariationPct(previousCost, newCost) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MatchTipo", (object?)NullIfEmpty(row.MatchType) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MatchScore", row.MatchScore);
        cmd.Parameters.AddWithValue("@AlertaVariacion", !string.IsNullOrWhiteSpace(row.AlertDetail));
        cmd.Parameters.AddWithValue("@AlertaDetalle", (object?)NullIfEmpty(row.AlertDetail) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Observaciones", DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class MasterArticleMatch
    {
        public string ArticleId { get; init; } = string.Empty;
        public string ArticleCode { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal? CurrentCost { get; init; }
        public string ProviderCode { get; init; } = string.Empty;
        public string ProviderAccount { get; init; } = string.Empty;
    }
}

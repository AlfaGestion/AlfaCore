using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class DocumentTemplateService(
    IConfiguration configuration,
    IAppEventService appEvents,
    IAppUserSessionService appUserSession,
    ILogger<DocumentTemplateService> logger) : IDocumentTemplateService
{
    private const string ModuleName = "Documentos";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private string ConnectionString => configuration.GetConnectionString("AlfaGestion")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<IReadOnlyList<DocumentTemplateDto>> GetListAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default)
        => await ExecuteLoggedAsync("GetList", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var items = await cn.QueryAsync<DocumentTemplateDto>(new CommandDefinition("""
                SELECT IdTemplate, UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom,
                       EsSistema, EsPredeterminado, Activo,
                       CAST(CASE WHEN PortadaImagen IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TienePortada,
                       FechaAlta, FechaModificacion, UsuarioModificacion
                FROM dbo.CORE_DocumentTemplate
                WHERE TipoDocumento = @TipoDocumento
                  AND (@UNegocio IS NULL OR UNegocio = @UNegocio OR UNegocio IS NULL)
                ORDER BY CASE WHEN UNegocio = @UNegocio THEN 0 WHEN UNegocio IS NULL THEN 1 ELSE 2 END,
                         EsSistema DESC, Nombre;
                """, new { TipoDocumento = NormalizeTipo(tipoDocumento), UNegocio = NormalizeUNegocio(uNegocio) }, cancellationToken: token));
            return (IReadOnlyList<DocumentTemplateDto>)items.AsList();
        }, "No se pudieron cargar las plantillas de documentos.", ct);

    public async Task<IReadOnlyList<UnidadNegocioOptionDto>> GetUnidadesNegocioAsync(CancellationToken ct = default)
        => await ExecuteLoggedAsync("GetUnidadesNegocio", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var items = await cn.QueryAsync<UnidadNegocioOptionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) AS Codigo, ISNULL(Descripcion, '') AS Descripcion
                FROM dbo.V_TA_UnidadNegocio
                WHERE ISNULL(LTRIM(RTRIM(Codigo)), '') <> ''
                ORDER BY LTRIM(RTRIM(Codigo));
                """, cancellationToken: token));
            return (IReadOnlyList<UnidadNegocioOptionDto>)items.AsList();
        }, "No se pudieron cargar las unidades de negocio.", ct);

    public async Task<bool> CanEditSystemTemplatesAsync(CancellationToken ct = default)
    {
        if (appUserSession.CurrentUser?.SuperAdmin == true)
            return true;

        var user = appUserSession.GetCurrentUserName();
        if (string.IsNullOrWhiteSpace(user))
            return false;

        return await ExecuteLoggedAsync("CanEditSystemTemplate", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            return await cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NULL
                   OR COL_LENGTH(N'dbo.TA_USUARIOS', N'Administrador') IS NULL
                    SELECT CAST(0 AS bit);
                ELSE
                    SELECT CAST(CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.TA_USUARIOS
                        WHERE UPPER(LTRIM(RTRIM(ISNULL(NOMBRE, N'')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                          AND ISNULL(Administrador, 0) = 1
                    ) THEN 1 ELSE 0 END AS bit);
                """, new { Usuario = user.Trim() }, cancellationToken: token));
        }, "No se pudo verificar el permiso para editar plantillas del sistema.", ct);
    }

    public async Task<DocumentTemplateDto?> GetByIdAsync(int idTemplate, CancellationToken ct = default)
        => await ExecuteLoggedAsync("GetById", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            return await cn.QueryFirstOrDefaultAsync<DocumentTemplateDto>(new CommandDefinition("""
                SELECT IdTemplate, UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom,
                       EsSistema, EsPredeterminado, Activo,
                       CAST(CASE WHEN PortadaImagen IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TienePortada,
                       FechaAlta, FechaModificacion, UsuarioModificacion
                FROM dbo.CORE_DocumentTemplate WHERE IdTemplate = @IdTemplate;
                """, new { IdTemplate = idTemplate }, cancellationToken: token));
        }, "No se pudo cargar la plantilla solicitada.", ct);

    public async Task<DocumentTemplateDto> ResolveAsync(string tipoDocumento, string? uNegocio, CancellationToken ct = default)
        => await ExecuteLoggedAsync("Resolve", async token =>
        {
            var tipo = NormalizeTipo(tipoDocumento);
            var unidad = NormalizeUNegocio(uNegocio);
            await using var cn = new SqlConnection(ConnectionString);
            var result = await cn.QueryFirstOrDefaultAsync<DocumentTemplateDto>(new CommandDefinition("""
                SELECT TOP (1) IdTemplate, UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom,
                       EsSistema, EsPredeterminado, Activo,
                       CAST(CASE WHEN PortadaImagen IS NOT NULL THEN 1 ELSE 0 END AS bit) AS TienePortada,
                       FechaAlta, FechaModificacion, UsuarioModificacion
                FROM dbo.CORE_DocumentTemplate
                WHERE TipoDocumento = @TipoDocumento AND Activo = 1
                  AND ((@UNegocio IS NOT NULL AND UNegocio = @UNegocio) OR UNegocio IS NULL)
                ORDER BY CASE WHEN UNegocio = @UNegocio THEN 0 ELSE 1 END,
                         EsPredeterminado DESC, EsSistema DESC, IdTemplate DESC;
                """, new { TipoDocumento = tipo, UNegocio = unidad }, cancellationToken: token));
            if (result is not null)
                return result;

            // El fallback embebido mantiene operativo el beta si todavía no se aplicó el script.
            return new DocumentTemplateDto
            {
                IdTemplate = 0, TipoDocumento = tipo, Nombre = "Cotización estándar (fallback)",
                EsSistema = true, EsPredeterminado = true, Activo = true,
                TemplateJson = JsonSerializer.Serialize(DocumentTemplateDefinition.CrearCotizacionEstandar(), JsonOptions)
            };
        }, "No se pudo resolver la plantilla de documento.", ct);

    public async Task<DocumentTemplateDto> SaveAsync(DocumentTemplateSaveRequest request, CancellationToken ct = default)
        => await ExecuteLoggedAsync("Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var tipo = NormalizeTipo(request.TipoDocumento);
            var unidad = NormalizeUNegocio(request.UNegocio);
            var nombre = Required(request.Nombre, "Nombre", 100);
            ValidateDefinition(request.Definition, tipo, unidad);
            var json = JsonSerializer.Serialize(request.Definition, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            try
            {
                int id;
                DocumentTemplateDto? actual = null;
                if (request.IdTemplate is > 0)
                {
                    actual = await cn.QueryFirstOrDefaultAsync<DocumentTemplateDto>(new CommandDefinition("""
                        SELECT IdTemplate, UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom, EsSistema,
                               EsPredeterminado, Activo, FechaAlta, FechaModificacion, UsuarioModificacion
                        FROM dbo.CORE_DocumentTemplate WITH (UPDLOCK, HOLDLOCK) WHERE IdTemplate = @IdTemplate;
                        """, new { IdTemplate = request.IdTemplate.Value }, tx, cancellationToken: token));
                    if (actual is null)
                        throw new InvalidOperationException("La plantilla ya no existe.");
                    if (actual.EsSistema && !await CanEditSystemTemplatesAsync(token))
                        throw new InvalidOperationException("Solo un administrador puede modificar una plantilla del sistema.");

                    var version = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                        "SELECT ISNULL(MAX(Version), 0) + 1 FROM dbo.CORE_DocumentTemplateVersion WHERE IdTemplate = @IdTemplate;",
                        new { IdTemplate = actual.IdTemplate }, tx, cancellationToken: token));
                    await cn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO dbo.CORE_DocumentTemplateVersion
                            (IdTemplate, Version, TemplateJson, CssCustom, Fecha, Usuario)
                        VALUES (@IdTemplate, @Version, @TemplateJson, @CssCustom, SYSDATETIME(), @Usuario);
                        """, new { actual.IdTemplate, Version = version, actual.TemplateJson, actual.CssCustom, Usuario = NormalizeUser(request.Usuario) }, tx, cancellationToken: token));

                    id = actual.IdTemplate;
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CORE_DocumentTemplate
                        SET UNegocio = @UNegocio, TipoDocumento = @TipoDocumento, Nombre = @Nombre,
                            TemplateJson = @TemplateJson, CssCustom = @CssCustom,
                            EsPredeterminado = @EsPredeterminado, Activo = @Activo,
                            FechaModificacion = SYSDATETIME(), UsuarioModificacion = @Usuario
                        WHERE IdTemplate = @IdTemplate;
                        """, new { IdTemplate = id, UNegocio = unidad, TipoDocumento = tipo, Nombre = nombre,
                            TemplateJson = json, request.CssCustom, request.EsPredeterminado, request.Activo,
                            Usuario = NormalizeUser(request.Usuario) }, tx, cancellationToken: token));
                }
                else
                {
                    id = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                        INSERT INTO dbo.CORE_DocumentTemplate
                            (UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom, EsSistema, EsPredeterminado, Activo, UsuarioModificacion)
                        OUTPUT INSERTED.IdTemplate
                        VALUES (@UNegocio, @TipoDocumento, @Nombre, @TemplateJson, @CssCustom, 0, @EsPredeterminado, @Activo, @Usuario);
                        """, new { UNegocio = unidad, TipoDocumento = tipo, Nombre = nombre, TemplateJson = json,
                            request.CssCustom, request.EsPredeterminado, request.Activo, Usuario = NormalizeUser(request.Usuario) }, tx, cancellationToken: token));
                }

                if (request.EsPredeterminado)
                {
                    await cn.ExecuteAsync(new CommandDefinition("""
                        UPDATE dbo.CORE_DocumentTemplate SET EsPredeterminado = 0
                        WHERE IdTemplate <> @IdTemplate AND TipoDocumento = @TipoDocumento AND Activo = 1
                          AND ((UNegocio = @UNegocio) OR (UNegocio IS NULL AND @UNegocio IS NULL));
                        """, new { IdTemplate = id, TipoDocumento = tipo, UNegocio = unidad }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
                if (actual?.EsSistema == true)
                {
                    await appEvents.LogAuditAsync(ModuleName, "EditSystemTemplate", "CORE_DocumentTemplate", id.ToString(),
                        "Se modificó una plantilla de sistema.", new { TipoDocumento = tipo, UNegocio = unidad, Usuario = NormalizeUser(request.Usuario) }, token);
                }
                logger.LogInformation("Documento: plantilla {IdTemplate} guardada para {TipoDocumento}/{UNegocio}.", id, tipo, unidad ?? "GLOBAL");
                return (await GetByIdAsync(id, token))!;
            }
            catch { await tx.RollbackAsync(token); throw; }
        }, "No se pudo guardar la plantilla de documento.", ct);

    public async Task<DocumentTemplateDto> DuplicateAsync(int idTemplate, string? uNegocio, string? usuario, CancellationToken ct = default)
    {
        var original = await GetByIdAsync(idTemplate, ct) ?? throw new InvalidOperationException("La plantilla indicada no existe.");
        var definition = DeserializeAndValidate(original.TemplateJson);
        return await SaveAsync(new DocumentTemplateSaveRequest
        {
            UNegocio = uNegocio ?? original.UNegocio, TipoDocumento = original.TipoDocumento,
            Nombre = $"Copia de {original.Nombre}", Definition = definition, CssCustom = original.CssCustom,
            EsPredeterminado = false, Activo = true, Usuario = usuario
        }, ct);
    }

    public Task<byte[]?> GetPortadaImageBytesAsync(int idTemplate, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetPortadaImageBytes", async token =>
        {
            if (idTemplate <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand("SELECT PortadaImagen FROM dbo.CORE_DocumentTemplate WHERE IdTemplate = @Id;", cn);
            cmd.Parameters.AddWithValue("@Id", idTemplate);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token) && !await reader.IsDBNullAsync(0, token))
                return reader.GetFieldValue<byte[]>(0);
            return null;
        }, "No se pudo cargar la portada de la plantilla.", ct);

    public Task SavePortadaImageAsync(int idTemplate, byte[] contenido, CancellationToken ct = default)
        => ExecuteLoggedAsync("SavePortadaImage", async token =>
        {
            if (idTemplate <= 0)
                throw new InvalidOperationException("Guardá la plantilla antes de subirle una portada.");
            if (contenido is null || contenido.Length == 0)
                throw new InvalidOperationException("La portada está vacía.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(
                "UPDATE dbo.CORE_DocumentTemplate SET PortadaImagen = @Imagen, FechaModificacion = SYSDATETIME() WHERE IdTemplate = @Id;", cn);
            cmd.Parameters.AddWithValue("@Id", idTemplate);
            cmd.Parameters.AddWithValue("@Imagen", contenido);
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("La plantilla ya no existe.");
            return true;
        }, "No se pudo guardar la portada de la plantilla.", ct);

    public Task DeletePortadaImageAsync(int idTemplate, CancellationToken ct = default)
        => ExecuteLoggedAsync("DeletePortadaImage", async token =>
        {
            if (idTemplate <= 0)
                return true;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(
                "UPDATE dbo.CORE_DocumentTemplate SET PortadaImagen = NULL, FechaModificacion = SYSDATETIME() WHERE IdTemplate = @Id;", cn);
            cmd.Parameters.AddWithValue("@Id", idTemplate);
            await cmd.ExecuteNonQueryAsync(token);
            return true;
        }, "No se pudo quitar la portada de la plantilla.", ct);

    public Task<string> GetGeneralThemeAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetGeneralTheme", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var value = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT TOP (1) VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'DOCUMENTOS_TEMA_GENERAL';",
                cancellationToken: token));
            return DocumentThemePresets.Resolve(value).Key;
        }, "No se pudo cargar el tema general de documentos.", ct);

    public Task SetGeneralThemeAsync(string themeKey, CancellationToken ct = default)
        => ExecuteLoggedAsync("SetGeneralTheme", async token =>
        {
            var key = DocumentThemePresets.Resolve(themeKey).Key;
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var rows = await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'DOCUMENTOS_TEMA_GENERAL';",
                new { Valor = key }, cancellationToken: token));
            if (rows == 0)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
                    VALUES (N'DOCUMENTOS_TEMA_GENERAL', @Valor, N'DOCUMENTOS', GETDATE(), GETDATE());
                    """, new { Valor = key }, cancellationToken: token));
            }
            return true;
        }, "No se pudo guardar el tema general de documentos.", ct);

    public DocumentTemplateDefinition DeserializeAndValidate(string templateJson)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<DocumentTemplateDefinition>(templateJson, JsonOptions)
                ?? throw new InvalidOperationException("El JSON no representa una definición de documento.");
            ValidateDefinition(definition, TiposDocumentoCore.Cotizacion, null);
            return definition;
        }
        catch (JsonException ex) { throw new InvalidOperationException("El JSON de la plantilla no es válido.", ex); }
    }

    private static void ValidateDefinition(DocumentTemplateDefinition definition, string tipoDocumento, string? uNegocio)
    {
        if (definition.SchemaVersion != 1) throw new InvalidOperationException("SchemaVersion no soportado.");
        if (tipoDocumento != TiposDocumentoCore.Cotizacion) throw new InvalidOperationException("Tipo de documento no soportado.");
        if (uNegocio?.Length > 4) throw new InvalidOperationException("UNegocio no puede superar cuatro caracteres.");
        if (definition.Paper.Size is not ("A4" or "A5")) throw new InvalidOperationException("El tamaño de papel debe ser A4 o A5.");
        if (definition.Paper.Orientation is not ("Portrait" or "Landscape")) throw new InvalidOperationException("La orientación debe ser Portrait o Landscape.");
        var margins = new[] { definition.Paper.MarginTopMm, definition.Paper.MarginBottomMm, definition.Paper.MarginLeftMm, definition.Paper.MarginRightMm };
        if (margins.Any(x => x < 0 || x > 50)) throw new InvalidOperationException("Los márgenes deben estar entre 0 y 50 mm.");
        if (definition.Blocks.Count == 0 || definition.Blocks.Any(x => string.IsNullOrWhiteSpace(x.Id) || !TiposBloqueDocumento.Permitidos.Contains(x.Type))) throw new InvalidOperationException("La plantilla contiene bloques inválidos.");
        if (definition.Blocks.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Blocks.Count) throw new InvalidOperationException("Los identificadores de bloque deben ser únicos.");
        var itemBlock = definition.Blocks.FirstOrDefault(x => x.Type.Equals(TiposBloqueDocumento.Items, StringComparison.OrdinalIgnoreCase));
        var allowedColumns = new HashSet<string>(["Codigo", "Descripcion", "Cantidad", "Precio", "Descuento", "Total"], StringComparer.OrdinalIgnoreCase);
        if (itemBlock?.Columns.Any(c => !allowedColumns.Contains(c.Field) || c.WidthPercent < 1 || c.WidthPercent > 100) == true) throw new InvalidOperationException("La plantilla contiene columnas de detalle inválidas.");
        if (itemBlock is not null && itemBlock.Columns.Where(x => x.Visible).Sum(x => x.WidthPercent) > 100) throw new InvalidOperationException("El ancho de las columnas visibles no puede superar 100%.");

        foreach (var block in definition.Blocks)
        {
            if (block.FontSizePt is < 6 or > 24) throw new InvalidOperationException("El tamaño de letra debe estar entre 6 y 24 puntos.");
            if (block.VisibleFields is null) continue;
            var allowedFields = DocumentBlockFields.For(block.Type);
            if (allowedFields.Count == 0) continue;
            var allowedNames = allowedFields.Select(x => x.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (block.VisibleFields.Any(f => !allowedNames.Contains(f))) throw new InvalidOperationException($"El bloque '{block.Type}' contiene campos inválidos.");
        }
    }

    private static string NormalizeTipo(string? value) => string.Equals(value?.Trim(), TiposDocumentoCore.Cotizacion, StringComparison.OrdinalIgnoreCase) ? TiposDocumentoCore.Cotizacion : throw new InvalidOperationException("TipoDocumento inválido.");
    private static string? NormalizeUNegocio(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: <= 4 } normalized ? normalized : throw new InvalidOperationException("UNegocio no puede superar cuatro caracteres.");
    private static string Required(string? value, string name, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength ? value.Trim() : throw new InvalidOperationException($"{name} es obligatorio y no puede superar {maxLength} caracteres.");
    private static string NormalizeUser(string? value) => string.IsNullOrWhiteSpace(value) ? "Sistema" : value.Trim()[..Math.Min(50, value.Trim().Length)];
    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try { return await operation(ct); }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, ct: ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}

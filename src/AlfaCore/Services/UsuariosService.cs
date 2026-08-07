using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class UsuariosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IWebHostEnvironment env,
    UsuariosPasswordCodec passwordCodec,
    IUsuariosValidator validator) : IUsuariosService
{
    private const string ModuleName = "Usuarios";
    private const string SistemaFijo = "CN000PR";
    private const string DefaultCaja = "1";
    private const string DefaultUnidadNegocio = "   1";
    private const string ConfigGroup = "USUARIOS";
    private const string ViewConfigPrefix = "USUVIEW-USUARIOS-";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<UsuarioGridItemDto>> SearchAsync(UsuariosFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Search", async token =>
        {
            filters ??= new UsuariosFilters();
            var pageSize   = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip       = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var activoExpr = hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)";
            var activoFilterSql = !hasActivo && filters.Activo == false
                ? "AND 1 = 0"
                : "AND (@Activo IS NULL OR " + activoExpr + " = @Activo)";
            var orderBySql = hasActivo
                ? $"{activoExpr} DESC, NOMBRE ASC"
                : "NOMBRE ASC";

            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, ''),
                    ISNULL(email_de, ''),
                    ISNULL(EsGrupo, 0),
                    ISNULL(CambiarProximoInicio, 0),
                    {activoExpr},
                    FechaHora_Grabacion,
                    FechaHora_Modificacion
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND (
                        @TextoLike = ''
                        OR ISNULL(NOMBRE, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(email_de, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                      )
                  {activoFilterSql}
                  AND (@EsGrupo IS NULL OR ISNULL(EsGrupo, 0) = @EsGrupo)
                ORDER BY {orderBySql}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND (
                        @TextoLike = ''
                        OR ISNULL(NOMBRE, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(email_de, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                      )
                  {activoFilterSql}
                  AND (@EsGrupo IS NULL OR ISNULL(EsGrupo, 0) = @EsGrupo);
                """;

            var rows = new List<UsuarioGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", SistemaFijo);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Texto));
            cmd.Parameters.AddWithValue("@Activo", filters.Activo.HasValue ? filters.Activo.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@EsGrupo", filters.EsGrupo.HasValue ? filters.EsGrupo.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var nombre = GetString(rd, 0);
                rows.Add(new UsuarioGridItemDto
                {
                    Nombre = nombre,
                    Email = GetString(rd, 1),
                    EsGrupo = GetBool(rd, 2),
                    CambiarProximoInicio = GetBool(rd, 3),
                    Activo = GetBool(rd, 4),
                    FechaHoraGrabacion = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
                    FechaHoraModificacion = rd.IsDBNull(6) ? null : rd.GetDateTime(6),
                    TieneFoto = await PhotoExistsAsync(nombre, token)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd.GetValue(0));

            return new PagedResult<UsuarioGridItemDto>
            {
                Items      = rows,
                Total      = total,
                PageNumber = pageNumber,
                PageSize   = pageSize
            };
        }, "No se pudieron cargar los usuarios.", ct);

    public Task<UsuarioDetailDto?> GetByIdAsync(string nombre, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetById", async token =>
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);

            var sql = $"""
                SELECT
                    ISNULL(NOMBRE, ''),
                    ISNULL(email_de, ''),
                    ISNULL(EsGrupo, 0),
                    ISNULL(CambiarProximoInicio, 0),
                    {(hasActivo ? "ISNULL(Activo, 1)" : "CAST(1 AS bit)")},
                    ISNULL(PASSWORD, ''),
                    FechaHora_Grabacion,
                    FechaHora_Modificacion
                FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(SISTEMA))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(NOMBRE))) = @Nombre
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", SistemaFijo);
            cmd.Parameters.AddWithValue("@Nombre", nombre.Trim().ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);

            if (!await rd.ReadAsync(token))
                return null;

            var decoded = passwordCodec.Decode(GetString(rd, 5));
            var canonicalName = GetString(rd, 0);
            var email = GetString(rd, 1);
            var esGrupo = GetBool(rd, 2);
            var cambiarProximoInicio = GetBool(rd, 3);
            var activo = GetBool(rd, 4);
            var fechaGrabacion = rd.IsDBNull(6) ? (DateTime?)null : rd.GetDateTime(6);
            var fechaModificacion = rd.IsDBNull(7) ? (DateTime?)null : rd.GetDateTime(7);
            await rd.CloseAsync();

            var photoInfo = await TryGetPhotoInfoAsync(canonicalName, token);
            var esTecnico = await IsLinkedTechnicianActiveAsync(cn, canonicalName, token);

            return new UsuarioDetailDto
            {
                Nombre = canonicalName,
                Email = email,
                EsGrupo = esGrupo,
                CambiarProximoInicio = cambiarProximoInicio,
                Activo = activo,
                ContrasenaDecodificada = decoded,
                TieneFoto = photoInfo.Exists,
                FotoCacheToken = photoInfo.CacheToken,
                FechaHoraGrabacion = fechaGrabacion,
                FechaHoraModificacion = fechaModificacion,
                EsTecnico = esTecnico
            };
        }, "No se pudo cargar el usuario seleccionado.", ct);

    public Task<string> SaveAsync(UsuarioSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeRequest(request);
            var validation = await validator.ValidateForSaveAsync(normalized, token);
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos del usuario antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);

            var isNew = string.IsNullOrWhiteSpace(normalized.NombreOriginal);
            var oldName = normalized.NombreOriginal.Trim();
            var newName = normalized.Nombre.Trim();

            await using var tx = await cn.BeginTransactionAsync(token);

            if (isNew)
            {
                var insertSql = hasActivo
                    ? """
                    INSERT INTO dbo.TA_USUARIOS
                    (
                        Nombre,
                        Sistema,
                        Password,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion,
                        CambiarProximoInicio,
                        EsGrupo,
                        IDCAJA,
                        UNEGOCIO,
                        email_de,
                        V_ModificaArtLuegoDeCargado,
                        Activo
                    )
                    VALUES
                    (
                        @Nombre,
                        @Sistema,
                        @Password,
                        GETDATE(),
                        GETDATE(),
                        @CambiarProximoInicio,
                        @EsGrupo,
                        @IdCaja,
                        @UNegocio,
                        @Email,
                        @ModificaArt,
                        1
                    );
                    """
                    : """
                    INSERT INTO dbo.TA_USUARIOS
                    (
                        Nombre,
                        Sistema,
                        Password,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion,
                        CambiarProximoInicio,
                        EsGrupo,
                        IDCAJA,
                        UNEGOCIO,
                        email_de,
                        V_ModificaArtLuegoDeCargado
                    )
                    VALUES
                    (
                        @Nombre,
                        @Sistema,
                        @Password,
                        GETDATE(),
                        GETDATE(),
                        @CambiarProximoInicio,
                        @EsGrupo,
                        @IdCaja,
                        @UNegocio,
                        @Email,
                        @ModificaArt
                    );
                    """;

                await using var insertCmd = new SqlCommand(insertSql, cn, (SqlTransaction)tx);
                FillSaveParameters(insertCmd, normalized, newName);
                await insertCmd.ExecuteNonQueryAsync(token);
            }
            else
            {
                const string updateSql = """
                    UPDATE dbo.TA_USUARIOS
                    SET
                        Nombre = @Nombre,
                        Password = @Password,
                        FechaHora_Modificacion = GETDATE(),
                        CambiarProximoInicio = @CambiarProximoInicio,
                        EsGrupo = @EsGrupo,
                        IDCAJA = @IdCaja,
                        UNEGOCIO = @UNegocio,
                        email_de = @Email,
                        V_ModificaArtLuegoDeCargado = @ModificaArt
                    WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                      AND UPPER(LTRIM(RTRIM(Nombre))) = @NombreOriginal;
                    """;

                await using var updateCmd = new SqlCommand(updateSql, cn, (SqlTransaction)tx);
                FillSaveParameters(updateCmd, normalized, newName);
                updateCmd.Parameters.AddWithValue("@NombreOriginal", oldName.ToUpperInvariant());
                await updateCmd.ExecuteNonQueryAsync(token);
            }

            await SyncTechnicianLinkAsync(cn, (SqlTransaction)tx, oldName, newName, normalized.EsTecnico, token);

            await tx.CommitAsync(token);

            await ApplyPhotoChangesAsync(oldName, newName, normalized, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                isNew ? "Create" : "Update",
                "TA_USUARIOS",
                $"{newName}|{SistemaFijo}",
                isNew ? "Usuario creado." : "Usuario actualizado.",
                new
                {
                    Nombre = newName,
                    normalized.Email,
                    normalized.EsGrupo,
                    normalized.CambiarProximoInicio,
                    Activo = true
                },
                token);

            return newName;
        }, "No se pudo guardar el usuario.", ct);

    public Task DeactivateAsync(string nombre, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Deactivate", async token =>
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new InvalidOperationException("No se recibió el usuario a desactivar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await HasActivoColumnAsync(cn, token))
                throw new InvalidOperationException("La base activa no tiene la columna Activo en TA_USUARIOS, por eso no se puede hacer baja lógica.");

            const string sql = """
                UPDATE dbo.TA_USUARIOS
                SET
                    Activo = 0,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(Sistema))) = @Sistema
                  AND UPPER(LTRIM(RTRIM(Nombre))) = @Nombre;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", SistemaFijo);
            cmd.Parameters.AddWithValue("@Nombre", nombre.Trim().ToUpperInvariant());
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El usuario seleccionado ya no existe en la base activa.");

            await appEvents.LogAuditAsync(
                ModuleName,
                "Deactivate",
                "TA_USUARIOS",
                $"{nombre.Trim()}|{SistemaFijo}",
                "Usuario dado de baja.",
                new { Nombre = nombre.Trim(), Activo = false },
                token);
        }, "No se pudo dar de baja el usuario.", ct);

    public Task<UsuariosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);
            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Clave", configKey.ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return CreateDefaultViewSettings();

            var raw = ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings();

            var parsed = JsonSerializer.Deserialize<UsuariosViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, UsuariosViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var stored = SplitStoredValue(serialized);
            var configKey = BuildViewConfigKey(userName);
            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET
                    VALOR = @Valor,
                    {detailColumn} = @ValorAux,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE,
                        VALOR,
                        {detailColumn},
                        GRUPO,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave,
                        @Valor,
                        @ValorAux,
                        @Grupo,
                        GETDATE(),
                        GETDATE()
                    );
                END;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@ClaveNormalizada", configKey.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Clave", configKey);
            cmd.Parameters.AddWithValue("@Valor", DbNullable(stored.Value));
            cmd.Parameters.AddWithValue("@ValorAux", DbNullable(stored.AuxValue));
            cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
            await cmd.ExecuteNonQueryAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SaveViewSettings",
                "TA_CONFIGURACION",
                configKey,
                "Configuración de vista de usuarios actualizada.",
                new
                {
                    UserName = userName.Trim(),
                    normalized.AgruparPor,
                    Columnas = normalized.Columnas
                },
                token);
        }, "No se pudo guardar la configuración de vista.", ct);

    public Task<UsuarioPhotoServeDto?> GetPhotoForServeAsync(string nombre, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPhotoForServe", async token =>
        {
            var path = await TryResolvePhotoPathAsync(nombre, token);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            return new UsuarioPhotoServeDto
            {
                RutaCompleta = path,
                MimeType = "image/jpeg",
                NombreArchivo = Path.GetFileName(path)
            };
        }, "No se pudo obtener la imagen del usuario.", ct);

    private void FillSaveParameters(SqlCommand cmd, UsuarioSaveRequest request, string newName)
    {
        cmd.Parameters.AddWithValue("@Nombre", newName);
        cmd.Parameters.AddWithValue("@Sistema", SistemaFijo);
        cmd.Parameters.AddWithValue("@Password", DbNullable(request.EsGrupo ? string.Empty : passwordCodec.Encode(request.Contrasena)));
        cmd.Parameters.AddWithValue("@CambiarProximoInicio", request.EsGrupo ? false : request.CambiarProximoInicio);
        cmd.Parameters.AddWithValue("@EsGrupo", request.EsGrupo);
        cmd.Parameters.AddWithValue("@IdCaja", DefaultCaja);
        cmd.Parameters.AddWithValue("@UNegocio", DefaultUnidadNegocio);
        cmd.Parameters.AddWithValue("@Email", DbNullable(request.Email));
        cmd.Parameters.AddWithValue("@ModificaArt", true);
    }

    private async Task ApplyPhotoChangesAsync(string oldName, string newName, UsuarioSaveRequest request, CancellationToken ct)
    {
        var oldPath = await TryResolvePhotoPathAsync(string.IsNullOrWhiteSpace(oldName) ? newName : oldName, ct);
        var newPath = await TryResolvePhotoPathAsync(newName, ct);
        if (string.IsNullOrWhiteSpace(newPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);

        if (request.QuitarFoto)
        {
            if (!string.IsNullOrWhiteSpace(oldPath) && File.Exists(oldPath))
                File.Delete(oldPath);

            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                File.Delete(newPath);

            return;
        }

        if (request.FotoContenido is { Length: > 0 })
        {
            await File.WriteAllBytesAsync(newPath, request.FotoContenido, ct);
            if (!string.IsNullOrWhiteSpace(oldPath) &&
                !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(oldPath) &&
            !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(oldPath))
        {
            File.Copy(oldPath, newPath, true);
            File.Delete(oldPath);
        }
    }

    private static UsuarioSaveRequest NormalizeRequest(UsuarioSaveRequest request)
        => new()
        {
            NombreOriginal = request.NombreOriginal?.Trim() ?? string.Empty,
            Nombre = request.Nombre?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            EsGrupo = request.EsGrupo,
            CambiarProximoInicio = request.CambiarProximoInicio,
            Contrasena = request.Contrasena ?? string.Empty,
            FotoContenido = request.FotoContenido,
            FotoNombreOriginal = request.FotoNombreOriginal ?? string.Empty,
            FotoMimeType = request.FotoMimeType ?? string.Empty,
            QuitarFoto = request.QuitarFoto,
            EsTecnico = request.EsTecnico
        };

    private async Task<bool> PhotoExistsAsync(string nombre, CancellationToken ct)
    {
        var path = await TryResolvePhotoPathAsync(nombre, ct);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private async Task<(bool Exists, string CacheToken)> TryGetPhotoInfoAsync(string nombre, CancellationToken ct)
    {
        var path = await TryResolvePhotoPathAsync(nombre, ct);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (false, string.Empty);

        return (true, File.GetLastWriteTimeUtc(path).Ticks.ToString());
    }

    private async Task<string?> TryResolvePhotoPathAsync(string nombre, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return null;

        var relativeBase = await TryReadRutaImagenesAsync(ct);
        if (string.IsNullOrWhiteSpace(relativeBase))
            return null;

        var cleanedBase = relativeBase.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var basePath = Path.Combine(env.ContentRootPath, cleanedBase);
        return Path.Combine(basePath, "USUARIOS", $"{nombre.Trim()}.jpg");
    }

    private async Task<string> TryReadRutaImagenesAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);

        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'RUTAIMAGENES'
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        return ResolveStoredValue(GetString(rd, 0), GetString(rd, 1));
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static async Task<bool> TechnicianTableExistsAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(1) FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.V_TA_Tecnicos');";
        await using var cmd = tx is null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private async Task<bool> IsLinkedTechnicianActiveAsync(SqlConnection cn, string userName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName) || !await TechnicianTableExistsAsync(cn, null, ct))
            return false;

        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.V_TA_Tecnicos
            WHERE UPPER(LTRIM(RTRIM(UsuarioAsociado))) = @UsuarioAsociado
              AND ISNULL(Baja, 0) = 0;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@UsuarioAsociado", userName.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    /// <summary>
    /// Da de alta/reactiva o da de baja lógica la fila de <c>dbo.V_TA_Tecnicos</c> asociada al
    /// usuario, según el tilde "Es técnico / agente de soporte" del formulario. Nunca borra la
    /// fila — puede tener tickets, mensajes o partes de horas atados.
    /// </summary>
    private static async Task SyncTechnicianLinkAsync(SqlConnection cn, SqlTransaction tx, string previousUserName, string userName, bool esTecnico, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userName) || !await TechnicianTableExistsAsync(cn, tx, ct))
            return;

        var searchName = (string.IsNullOrWhiteSpace(previousUserName) ? userName : previousUserName).Trim();

        string? existingIdTecnico = null;
        var existingBaja = false;

        const string findSql = """
            SELECT TOP (1) IdTecnico, ISNULL(Baja, 0)
            FROM dbo.V_TA_Tecnicos
            WHERE UPPER(LTRIM(RTRIM(UsuarioAsociado))) = @UsuarioAsociado
            ORDER BY ISNULL(Baja, 0) ASC;
            """;

        await using (var findCmd = new SqlCommand(findSql, cn, tx))
        {
            findCmd.Parameters.AddWithValue("@UsuarioAsociado", searchName.ToUpperInvariant());
            await using var rd = await findCmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))
            {
                existingIdTecnico = GetString(rd, 0);
                existingBaja = GetBool(rd, 1);
            }
        }

        if (esTecnico)
        {
            if (existingIdTecnico is not null)
            {
                const string reactivateSql = """
                    UPDATE dbo.V_TA_Tecnicos
                    SET UsuarioAsociado = @UsuarioAsociado, Baja = 0
                    WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnico;
                    """;
                await using var cmd = new SqlCommand(reactivateSql, cn, tx);
                cmd.Parameters.AddWithValue("@UsuarioAsociado", userName);
                cmd.Parameters.AddWithValue("@IdTecnico", existingIdTecnico.ToUpperInvariant());
                await cmd.ExecuteNonQueryAsync(ct);
                return;
            }

            const string nextIdSql = """
                SELECT ISNULL(MAX(TRY_CAST(LTRIM(RTRIM(IdTecnico)) AS int)), 0) + 1
                FROM dbo.V_TA_Tecnicos;
                """;
            string nextId;
            await using (var nextIdCmd = new SqlCommand(nextIdSql, cn, tx))
                nextId = Convert.ToInt32(await nextIdCmd.ExecuteScalarAsync(ct)).ToString();

            const string insertSql = """
                INSERT INTO dbo.V_TA_Tecnicos (IdTecnico, Nombre, Cargo, UsuarioAsociado, Baja)
                VALUES (@IdTecnico, @Nombre, @Cargo, @UsuarioAsociado, 0);
                """;
            await using var insertCmd = new SqlCommand(insertSql, cn, tx);
            insertCmd.Parameters.AddWithValue("@IdTecnico", nextId);
            insertCmd.Parameters.AddWithValue("@Nombre", userName);
            insertCmd.Parameters.AddWithValue("@Cargo", "Agente de soporte");
            insertCmd.Parameters.AddWithValue("@UsuarioAsociado", userName);
            await insertCmd.ExecuteNonQueryAsync(ct);
        }
        else if (existingIdTecnico is not null && !existingBaja)
        {
            const string bajaSql = "UPDATE dbo.V_TA_Tecnicos SET Baja = 1 WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnico;";
            await using var cmd = new SqlCommand(bajaSql, cn, tx);
            cmd.Parameters.AddWithValue("@IdTecnico", existingIdTecnico.ToUpperInvariant());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> HasActivoColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_USUARIOS')
              AND LOWER(name) = N'activo';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length > 150)
            return (string.Empty, normalized);

        return (normalized, string.Empty);
    }

    private static string BuildViewConfigKey(string userName)
    {
        var normalized = userName.Trim().ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static UsuariosViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = UsuariosViewGroupKeys.None,
            Columnas =
            [
                new() { Key = UsuariosViewColumnKeys.Nombre, Label = "Nombre", Visible = true, Order = 0 },
                new() { Key = UsuariosViewColumnKeys.Email, Label = "Email", Visible = true, Order = 1 },
                new() { Key = UsuariosViewColumnKeys.Tipo, Label = "Tipo", Visible = true, Order = 2 },
                new() { Key = UsuariosViewColumnKeys.CambiarClave, Label = "Clave próximo inicio", Visible = true, Order = 3 },
                new() { Key = UsuariosViewColumnKeys.Activo, Label = "Activo", Visible = true, Order = 4 },
                new() { Key = UsuariosViewColumnKeys.Alta, Label = "Alta", Visible = false, Order = 5 },
                new() { Key = UsuariosViewColumnKeys.Modificacion, Label = "Modificación", Visible = true, Order = 6 }
            ]
        };

    private static UsuariosViewSettingsDto NormalizeViewSettings(UsuariosViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new UsuariosViewSettingsDto
        {
            AgruparPor = NormalizeGroupKey(settings.AgruparPor),
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                    {
                        return new UsuarioViewColumnDto
                        {
                            Key = defaultCol.Key,
                            Label = defaultCol.Label,
                            Visible = defaultCol.Visible,
                            Order = defaultCol.Order
                        };
                    }

                    return new UsuarioViewColumnDto
                    {
                        Key = defaultCol.Key,
                        Label = defaultCol.Label,
                        Visible = source.Visible,
                        Order = source.Order
                    };
                })
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select((column, index) =>
                {
                    column.Order = index;
                    return column;
                })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string NormalizeGroupKey(string? groupKey)
        => groupKey?.Trim() switch
        {
            UsuariosViewGroupKeys.Tipo => UsuariosViewGroupKeys.Tipo,
            UsuariosViewGroupKeys.Activo => UsuariosViewGroupKeys.Activo,
            UsuariosViewGroupKeys.CambiarClave => UsuariosViewGroupKeys.CambiarClave,
            _ => UsuariosViewGroupKeys.None
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

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
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
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

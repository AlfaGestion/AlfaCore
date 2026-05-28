using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class TecnicosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    ITecnicosValidator validator) : ITecnicosService
{
    private const string ModuleName       = "Tecnicos";
    private const string ConfigGroup      = "TECNICOS";
    private const string ViewConfigPrefix = "USUVIEW-TECNICOS-";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // ── Búsqueda paginada ────────────────────────────────────────────────────

    public Task<PagedResult<TecnicoGridItemDto>> SearchAsync(TecnicosFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Search", async token =>
        {
            filters ??= new TecnicosFilters();
            var pageSize   = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip       = (pageNumber - 1) * pageSize;

            var activoFilter = filters.Activo switch
            {
                true  => "AND ISNULL(Baja, 0) = 0",
                false => "AND ISNULL(Baja, 0) = 1",
                _     => string.Empty
            };

            var sql = $"""
                SELECT
                    ISNULL(IdTecnico, ''),
                    ISNULL(Nombre, ''),
                    ISNULL(Cargo, ''),
                    ISNULL(Localidad, ''),
                    ISNULL(Telefono, ''),
                    CostoHora,
                    ISNULL(OcultarCliente, 0),
                    ISNULL(UsuarioAsociado, ''),
                    ISNULL(Baja, 0)
                FROM dbo.V_TA_Tecnicos
                WHERE (
                    @TextoLike = ''
                    OR ISNULL(IdTecnico, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Nombre, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(UsuarioAsociado, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Telefono, '') LIKE @TextoLike
                )
                {activoFilter}
                ORDER BY ISNULL(Nombre, '')
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.V_TA_Tecnicos
                WHERE (
                    @TextoLike = ''
                    OR ISNULL(IdTecnico, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Nombre, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(UsuarioAsociado, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                    OR ISNULL(Telefono, '') LIKE @TextoLike
                )
                {activoFilter};
                """;

            var rows = new List<TecnicoGridItemDto>();
            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Texto));
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new TecnicoGridItemDto
                {
                    IdTecnico       = GetString(rd, 0),
                    Nombre          = GetString(rd, 1),
                    Cargo           = GetString(rd, 2),
                    Localidad       = GetString(rd, 3),
                    Telefono        = GetString(rd, 4),
                    CostoHora       = rd.IsDBNull(5) ? null : Convert.ToDecimal(rd.GetValue(5)),
                    OcultarCliente  = GetBool(rd, 6),
                    UsuarioAsociado = GetString(rd, 7),
                    Baja            = GetBool(rd, 8)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd.GetValue(0));

            return new PagedResult<TecnicoGridItemDto>
            {
                Items      = rows,
                Total      = total,
                PageNumber = pageNumber,
                PageSize   = pageSize
            };
        }, "No se pudieron cargar los técnicos.", ct);

    // ── Detalle ──────────────────────────────────────────────────────────────

    public Task<TecnicoDetailDto?> GetByIdAsync(string idTecnico, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetById", async token =>
        {
            if (string.IsNullOrWhiteSpace(idTecnico))
                return null;

            const string sql = """
                SELECT
                    ISNULL(IdTecnico, ''),
                    ISNULL(Nombre, ''),
                    ISNULL(Cargo, ''),
                    ISNULL(Domicilio, ''),
                    ISNULL(Localidad, ''),
                    ISNULL(IdProvincia, ''),
                    ISNULL(Telefono, ''),
                    CostoHora,
                    ISNULL(UsuarioAsociado, ''),
                    ISNULL(OcultarCliente, 0),
                    ISNULL(Baja, 0)
                FROM dbo.V_TA_Tecnicos
                WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnico;
                """;

            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdTecnico", idTecnico.Trim().ToUpperInvariant());
            await using var rd = await cmd.ExecuteReaderAsync(token);

            if (!await rd.ReadAsync(token))
                return null;

            return new TecnicoDetailDto
            {
                IdTecnico       = GetString(rd, 0),
                Nombre          = GetString(rd, 1),
                Cargo           = GetString(rd, 2),
                Domicilio       = GetString(rd, 3),
                Localidad       = GetString(rd, 4),
                IdProvincia     = GetString(rd, 5),
                Telefono        = GetString(rd, 6),
                CostoHora       = rd.IsDBNull(7) ? null : Convert.ToDecimal(rd.GetValue(7)),
                UsuarioAsociado = GetString(rd, 8),
                OcultarCliente  = GetBool(rd, 9),
                Baja            = GetBool(rd, 10)
            };
        }, "No se pudo cargar el técnico seleccionado.", ct);

    // ── Guardar ──────────────────────────────────────────────────────────────

    public Task<string> SaveAsync(TecnicoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeRequest(request);
            var validation = await validator.ValidateForSaveAsync(normalized, token);
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos del técnico antes de guardar.", validation);

            var isNew      = string.IsNullOrWhiteSpace(normalized.IdTecnicoOriginal);
            var idOriginal = normalized.IdTecnicoOriginal;
            var idNuevo    = normalized.IdTecnico;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.V_TA_Tecnicos
                    (
                        IdTecnico, Nombre, Cargo,
                        Domicilio, Localidad, IdProvincia,
                        Telefono, CostoHora,
                        UsuarioAsociado, OcultarCliente, Baja
                    )
                    VALUES
                    (
                        @IdTecnico, @Nombre, @Cargo,
                        @Domicilio, @Localidad, @IdProvincia,
                        @Telefono, @CostoHora,
                        @UsuarioAsociado, @OcultarCliente, 0
                    );
                    """;

                await using var cmd = new SqlCommand(insertSql, cn, (SqlTransaction)tx);
                FillSaveParameters(cmd, normalized);
                await cmd.ExecuteNonQueryAsync(token);
            }
            else
            {
                const string updateSql = """
                    UPDATE dbo.V_TA_Tecnicos
                    SET
                        IdTecnico       = @IdTecnico,
                        Nombre          = @Nombre,
                        Cargo           = @Cargo,
                        Domicilio       = @Domicilio,
                        Localidad       = @Localidad,
                        IdProvincia     = @IdProvincia,
                        Telefono        = @Telefono,
                        CostoHora       = @CostoHora,
                        UsuarioAsociado = @UsuarioAsociado,
                        OcultarCliente  = @OcultarCliente
                    WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnicoOriginal;
                    """;

                await using var cmd = new SqlCommand(updateSql, cn, (SqlTransaction)tx);
                FillSaveParameters(cmd, normalized);
                cmd.Parameters.AddWithValue("@IdTecnicoOriginal", idOriginal.ToUpperInvariant());
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                isNew ? "Create" : "Update",
                "V_TA_Tecnicos",
                idNuevo,
                isNew ? "Técnico creado." : "Técnico actualizado.",
                new
                {
                    IdTecnico = idNuevo,
                    normalized.Nombre,
                    normalized.Cargo,
                    normalized.UsuarioAsociado
                },
                token);

            return idNuevo;
        }, "No se pudo guardar el técnico.", ct);

    // ── Baja lógica ──────────────────────────────────────────────────────────

    public Task DarDeBajaAsync(string idTecnico, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DarDeBaja", async token =>
        {
            if (string.IsNullOrWhiteSpace(idTecnico))
                throw new InvalidOperationException("No se recibió el técnico a dar de baja.");

            const string sql = """
                UPDATE dbo.V_TA_Tecnicos
                SET Baja = 1
                WHERE UPPER(LTRIM(RTRIM(IdTecnico))) = @IdTecnico;
                """;

            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdTecnico", idTecnico.Trim().ToUpperInvariant());
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El técnico seleccionado ya no existe en la base activa.");

            await appEvents.LogAuditAsync(
                ModuleName, "DarDeBaja", "V_TA_Tecnicos", idTecnico.Trim(),
                "Técnico dado de baja.", new { IdTecnico = idTecnico.Trim(), Baja = true }, token);
        }, "No se pudo dar de baja el técnico.", ct);

    // ── Combos de referencia ─────────────────────────────────────────────────

    public Task<IReadOnlyList<EstadoItemDto>> GetEstadosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetEstados", async token =>
        {
            const string sql = """
                SELECT ISNULL(CODIGO, ''), ISNULL(DESCRIPCION, '')
                FROM dbo.TA_ESTADOS
                ORDER BY DESCRIPCION;
                """;

            var items = new List<EstadoItemDto>();
            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd  = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new EstadoItemDto
                {
                    Codigo      = GetString(rd, 0),
                    Descripcion = GetString(rd, 1)
                });
            }

            return (IReadOnlyList<EstadoItemDto>)items;
        }, "No se pudieron cargar las provincias.", ct);

    public Task<IReadOnlyList<string>> GetUsuariosDisponiblesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetUsuariosDisponibles", async token =>
        {
            const string sql = """
                SELECT ISNULL(NOMBRE, '')
                FROM dbo.TA_USUARIOS
                WHERE ISNULL(EsGrupo, 0) = 0
                ORDER BY NOMBRE;
                """;

            var items = new List<string>();
            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd  = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var nombre = GetString(rd, 0);
                if (!string.IsNullOrWhiteSpace(nombre))
                    items.Add(nombre);
            }

            return (IReadOnlyList<string>)items;
        }, "No se pudieron cargar los usuarios del sistema.", ct);

    public Task<string> GetNextIdTecnicoAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetNextId", async token =>
        {
            const string sql = """
                SELECT ISNULL(MAX(TRY_CAST(LTRIM(RTRIM(IdTecnico)) AS int)), 0) + 1
                FROM dbo.V_TA_Tecnicos;
                """;

            await using var cn  = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            var result = await cmd.ExecuteScalarAsync(token);
            return Convert.ToInt32(result).ToString();
        }, "No se pudo obtener el próximo código de técnico.", ct);

    // ── Configuración de vista ────────────────────────────────────────────────

    public Task<TecnicosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey    = BuildViewConfigKey(userName);

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

            var parsed = JsonSerializer.Deserialize<TecnicosViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, TecnicosViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var stored       = SplitStoredValue(serialized);
            var configKey    = BuildViewConfigKey(userName);

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
                    (CLAVE, VALOR, {detailColumn}, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
                    VALUES
                    (@Clave, @Valor, @ValorAux, @Grupo, GETDATE(), GETDATE());
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
                ModuleName, "SaveViewSettings", "TA_CONFIGURACION", configKey,
                "Configuración de vista de técnicos actualizada.",
                new { UserName = userName.Trim(), normalized.AgruparPor }, token);
        }, "No se pudo guardar la configuración de vista.", ct);

    // ── Helpers privados ─────────────────────────────────────────────────────

    private static void FillSaveParameters(SqlCommand cmd, TecnicoSaveRequest r)
    {
        cmd.Parameters.AddWithValue("@IdTecnico",       r.IdTecnico);
        cmd.Parameters.AddWithValue("@Nombre",          r.Nombre);
        cmd.Parameters.AddWithValue("@Cargo",           r.Cargo);
        cmd.Parameters.AddWithValue("@Domicilio",       DbNullable(r.Domicilio));
        cmd.Parameters.AddWithValue("@Localidad",       DbNullable(r.Localidad));
        // IdProvincia es FK a TA_ESTADOS.CODIGO: no se trimea para preservar espacios del código legacy
        cmd.Parameters.AddWithValue("@IdProvincia",     DbNullableExact(r.IdProvincia));
        cmd.Parameters.AddWithValue("@Telefono",        DbNullable(r.Telefono));
        cmd.Parameters.AddWithValue("@CostoHora",       r.CostoHora.HasValue ? r.CostoHora.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@UsuarioAsociado", DbNullable(r.UsuarioAsociado));
        cmd.Parameters.AddWithValue("@OcultarCliente",  r.OcultarCliente);
    }

    private static TecnicoSaveRequest NormalizeRequest(TecnicoSaveRequest r) => new()
    {
        IdTecnicoOriginal = r.IdTecnicoOriginal?.Trim() ?? string.Empty,
        IdTecnico         = r.IdTecnico?.Trim() ?? string.Empty,
        Nombre            = r.Nombre?.Trim() ?? string.Empty,
        Cargo             = r.Cargo?.Trim() ?? string.Empty,
        Domicilio         = r.Domicilio?.Trim() ?? string.Empty,
        Localidad         = r.Localidad?.Trim() ?? string.Empty,
        // IdProvincia no se trimea: su valor proviene de TA_ESTADOS.CODIGO que puede tener
        // espacios de relleno a la izquierda (códigos numéricos alineados a la derecha).
        IdProvincia       = r.IdProvincia ?? string.Empty,
        Telefono          = r.Telefono?.Trim() ?? string.Empty,
        CostoHora         = r.CostoHora,
        UsuarioAsociado   = r.UsuarioAsociado?.Trim() ?? string.Empty,
        OcultarCliente    = r.OcultarCliente
    };

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

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150
            ? (string.Empty, normalized)
            : (normalized, string.Empty);
    }

    private static string BuildViewConfigKey(string userName)
    {
        var normalized = userName.Trim().ToUpperInvariant();
        var hash       = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static TecnicosViewSettingsDto CreateDefaultViewSettings() => new()
    {
        AgruparPor = TecnicosViewGroupKeys.None,
        Columnas =
        [
            new() { Key = TecnicosViewColumnKeys.IdTecnico,       Label = "Código",          Visible = true,  Order = 0 },
            new() { Key = TecnicosViewColumnKeys.Nombre,          Label = "Nombre",           Visible = true,  Order = 1 },
            new() { Key = TecnicosViewColumnKeys.Cargo,           Label = "Cargo",            Visible = true,  Order = 2 },
            new() { Key = TecnicosViewColumnKeys.Localidad,       Label = "Localidad",        Visible = false, Order = 3 },
            new() { Key = TecnicosViewColumnKeys.Telefono,        Label = "Teléfono",         Visible = true,  Order = 4 },
            new() { Key = TecnicosViewColumnKeys.CostoHora,       Label = "Costo/hora",       Visible = true,  Order = 5 },
            new() { Key = TecnicosViewColumnKeys.OcultarCliente,  Label = "Ocultar cliente",  Visible = false, Order = 6 },
            new() { Key = TecnicosViewColumnKeys.UsuarioAsociado, Label = "Usuario sistema",  Visible = true,  Order = 7 },
            new() { Key = TecnicosViewColumnKeys.Activo,          Label = "Activo",           Visible = true,  Order = 8 }
        ]
    };

    private static TecnicosViewSettingsDto NormalizeViewSettings(TecnicosViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new TecnicosViewSettingsDto
        {
            AgruparPor = settings.AgruparPor?.Trim() switch
            {
                TecnicosViewGroupKeys.Cargo          => TecnicosViewGroupKeys.Cargo,
                TecnicosViewGroupKeys.OcultarCliente => TecnicosViewGroupKeys.OcultarCliente,
                TecnicosViewGroupKeys.Activo         => TecnicosViewGroupKeys.Activo,
                _                                    => TecnicosViewGroupKeys.None
            },
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new TecnicoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new TecnicoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
                })
                .OrderBy(c => c.Order)
                .Select((col, idx) => { col.Order = idx; return col; })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

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

    // Versión sin Trim para campos FK cuyo valor debe coincidir exactamente con la PK referenciada.
    private static object DbNullableExact(string? value)
        => value is null || value.Length == 0 ? DBNull.Value : (object)value;

    private async Task<T> ExecuteLoggedAsync<T>(
        string module, string action,
        Func<CancellationToken, Task<T>> operation,
        string userMessage, CancellationToken ct)
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
        string module, string action,
        Func<CancellationToken, Task> operation,
        string userMessage, CancellationToken ct)
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

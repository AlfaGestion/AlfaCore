using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class ContactosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IContactosValidator validator) : IContactosService
{
    private const string ModuleName       = "Contactos";
    private const string ConfigGroup      = "CONTACTOS";
    private const string ViewConfigPrefix = "USUVIEW-CONTACTOS-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<ContactoGridItemDto>> SearchAsync(ContactosFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Search", async token =>
        {
            filters ??= new ContactosFilters();
            var pageSize   = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip       = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var activoExpr = hasActivo ? "ISNULL(c.Activo, 1)" : "CAST(1 AS bit)";
            var activoFilterSql = !hasActivo && filters.Activo == false
                ? "AND 1 = 0"
                : "AND (@Activo IS NULL OR " + activoExpr + " = @Activo)";
            var orderBySql = hasActivo
                ? $"{activoExpr} DESC, ISNULL(c.Nombre_y_Apellido, '') ASC"
                : "ISNULL(c.Nombre_y_Apellido, '') ASC";

            var conversationMatchSql = BuildConversationMatchSql("c");
            var advancedFilterSql = BuildAdvancedFilterSql(conversationMatchSql);
            var ruleFilterSql = BuildRuleFilterSql(filters.Reglas, GetContactosRuleFieldSql);
            var sql = $"""
                SELECT
                    c.id,
                    ISNULL(c.Nombre_y_Apellido, ''),
                    ISNULL(c.Localidad, ''),
                    ISNULL(c.Provincia, ''),
                    ISNULL(e.DESCRIPCION, ''),
                    ISNULL(c.Telefono, ''),
                    ISNULL(c.Celular, ''),
                    ISNULL(c.email, ''),
                    ISNULL(c.Cargo, ''),
                    {activoExpr},
                    conv.IdConversacion
                FROM dbo.MA_CONTACTOS c
                LEFT JOIN dbo.TA_ESTADOS e
                    ON UPPER(LTRIM(RTRIM(e.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(c.Provincia, ''))))
                OUTER APPLY (
                    SELECT TOP (1) cc.IdConversacion
                    FROM dbo.CONV_CONVERSACIONES cc
                    WHERE cc.Canal = N'WHATSAPP'
                      AND (
                            cc.IdContacto = c.id
                            OR {conversationMatchSql}
                          )
                    ORDER BY cc.IdConversacion ASC
                ) conv
                WHERE (
                        @Texto = ''
                        OR ISNULL(c.Nombre_y_Apellido, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.email, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Localidad, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Telefono, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Celular, '') LIKE '%' + @Texto + '%'
                  )
                  {activoFilterSql}
                  {advancedFilterSql}
                  {ruleFilterSql}
                ORDER BY {orderBySql}
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.MA_CONTACTOS c
                WHERE (
                        @Texto = ''
                        OR ISNULL(c.Nombre_y_Apellido, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.email, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Localidad, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Telefono, '') LIKE '%' + @Texto + '%'
                        OR ISNULL(c.Celular, '') LIKE '%' + @Texto + '%'
                  )
                  {advancedFilterSql}
                  {ruleFilterSql}
                  {activoFilterSql};
                """;

            var rows = new List<ContactoGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Texto", filters.Texto?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@Activo", filters.Activo.HasValue ? filters.Activo.Value : DBNull.Value);
            AddAdvancedFilterParameters(cmd, filters);
            AddRuleFilterParameters(cmd, filters.Reglas, GetContactosRuleFieldSql);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new ContactoGridItemDto
                {
                    Id = GetInt(rd, 0),
                    NombreApellido = GetString(rd, 1),
                    Localidad = GetString(rd, 2),
                    ProvinciaCodigo = GetString(rd, 3),
                    ProvinciaDescripcion = GetString(rd, 4),
                    Telefono = GetString(rd, 5),
                    Celular = GetString(rd, 6),
                    Email = GetString(rd, 7),
                    Cargo = GetString(rd, 8),
                    Activo = GetBool(rd, 9),
                    IdConversacionWhatsApp = rd.IsDBNull(10) ? null : rd.GetInt64(10)
                });
            }

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            return new PagedResult<ContactoGridItemDto>
            {
                Items      = rows,
                Total      = total,
                PageNumber = pageNumber,
                PageSize   = pageSize
            };
        }, "No se pudieron cargar los contactos.", ct);

    public Task<ContactoDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var conversationMatchSql = BuildConversationMatchSql("c");
            var sql = $"""
                SELECT
                    c.id,
                    ISNULL(c.Nombre_y_Apellido, ''),
                    ISNULL(c.Domicilio, ''),
                    ISNULL(c.Localidad, ''),
                    ISNULL(c.Provincia, ''),
                    ISNULL(c.C_Postal, ''),
                    ISNULL(c.NroDocumento, ''),
                    ISNULL(c.Telefono, ''),
                    ISNULL(c.Fax, ''),
                    ISNULL(c.Celular, ''),
                    ISNULL(c.email, ''),
                    ISNULL(c.mailPGCB, 0),
                    ISNULL(c.mailOC, 0),
                    ISNULL(c.mailOT, 0),
                    ISNULL(c.WebSite, ''),
                    ISNULL(c.Cargo, ''),
                    ISNULL(CAST(c.Observaciones AS nvarchar(max)), ''),
                    {(hasActivo ? "ISNULL(c.Activo, 1)" : "CAST(1 AS bit)")},
                    conv.IdConversacion
                FROM dbo.MA_CONTACTOS c
                OUTER APPLY (
                    SELECT TOP (1) cc.IdConversacion
                    FROM dbo.CONV_CONVERSACIONES cc
                    WHERE cc.Canal = N'WHATSAPP'
                      AND (
                            cc.IdContacto = c.id
                            OR {conversationMatchSql}
                          )
                    ORDER BY cc.IdConversacion ASC
                ) conv
                WHERE c.id = @Id;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return null;

            return new ContactoDetailDto
            {
                Id = GetInt(rd, 0),
                NombreApellido = GetString(rd, 1),
                Domicilio = GetString(rd, 2),
                Localidad = GetString(rd, 3),
                ProvinciaCodigo = GetString(rd, 4),
                CodigoPostal = GetString(rd, 5),
                NumeroDocumento = GetString(rd, 6),
                Telefono = GetString(rd, 7),
                TelefonoAlternativo = GetString(rd, 8),
                Celular = GetString(rd, 9),
                Email = GetString(rd, 10),
                MailPagosCobranzas = GetBool(rd, 11),
                MailOrdenCompra = GetBool(rd, 12),
                MailOrdenTrabajo = GetBool(rd, 13),
                Website = GetString(rd, 14),
                Cargo = GetString(rd, 15),
                Observaciones = GetString(rd, 16),
                Activo = GetBool(rd, 17),
                IdConversacionWhatsApp = rd.IsDBNull(18) ? null : rd.GetInt64(18)
            };
        }, "No se pudo cargar el contacto seleccionado.", ct);

    public Task<int> SaveAsync(ContactoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var normalized = NormalizeRequest(request);
            var validation = await validator.ValidateForSaveAsync(normalized, token);
            if (!validation.IsValid)
                throw new AppValidationException("Revisá los datos del contacto antes de guardar.", validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var isNew = !normalized.Id.HasValue || normalized.Id.Value <= 0;
            var contactId = normalized.Id ?? 0;

            await using var tx = await cn.BeginTransactionAsync(token);

            if (isNew)
            {
                var sql = hasActivo
                    ? """
                    INSERT INTO dbo.MA_CONTACTOS
                    (
                        Nombre_y_Apellido,
                        Domicilio,
                        Localidad,
                        Provincia,
                        C_Postal,
                        Telefono,
                        Fax,
                        Celular,
                        email,
                        WebSite,
                        Observaciones,
                        Cargo,
                        mailPGCB,
                        mailOT,
                        mailOC,
                        NroDocumento,
                        Activo
                    )
                    VALUES
                    (
                        @NombreApellido,
                        @Domicilio,
                        @Localidad,
                        @Provincia,
                        @CodigoPostal,
                        @Telefono,
                        @TelefonoAlternativo,
                        @Celular,
                        @Email,
                        @Website,
                        @Observaciones,
                        @Cargo,
                        @MailPGCB,
                        @MailOT,
                        @MailOC,
                        @NumeroDocumento,
                        1
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """
                    : """
                    INSERT INTO dbo.MA_CONTACTOS
                    (
                        Nombre_y_Apellido,
                        Domicilio,
                        Localidad,
                        Provincia,
                        C_Postal,
                        Telefono,
                        Fax,
                        Celular,
                        email,
                        WebSite,
                        Observaciones,
                        Cargo,
                        mailPGCB,
                        mailOT,
                        mailOC,
                        NroDocumento
                    )
                    VALUES
                    (
                        @NombreApellido,
                        @Domicilio,
                        @Localidad,
                        @Provincia,
                        @CodigoPostal,
                        @Telefono,
                        @TelefonoAlternativo,
                        @Celular,
                        @Email,
                        @Website,
                        @Observaciones,
                        @Cargo,
                        @MailPGCB,
                        @MailOT,
                        @MailOC,
                        @NumeroDocumento
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                FillSaveParameters(cmd, normalized);
                contactId = Convert.ToInt32(await cmd.ExecuteScalarAsync(token));
            }
            else
            {
                const string sql = """
                    UPDATE dbo.MA_CONTACTOS
                    SET
                        Nombre_y_Apellido = @NombreApellido,
                        Domicilio = @Domicilio,
                        Localidad = @Localidad,
                        Provincia = @Provincia,
                        C_Postal = @CodigoPostal,
                        Telefono = @Telefono,
                        Fax = @TelefonoAlternativo,
                        Celular = @Celular,
                        email = @Email,
                        WebSite = @Website,
                        Observaciones = @Observaciones,
                        Cargo = @Cargo,
                        mailPGCB = @MailPGCB,
                        mailOT = @MailOT,
                        mailOC = @MailOC,
                        NroDocumento = @NumeroDocumento
                    WHERE id = @Id;
                    """;

                await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
                FillSaveParameters(cmd, normalized);
                cmd.Parameters.AddWithValue("@Id", contactId);
                var affected = await cmd.ExecuteNonQueryAsync(token);
                if (affected == 0)
                    throw new InvalidOperationException("El contacto seleccionado ya no existe en la base activa.");
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                isNew ? "Create" : "Update",
                "MA_CONTACTOS",
                contactId.ToString(),
                isNew ? "Contacto creado." : "Contacto actualizado.",
                new
                {
                    Id = contactId,
                    normalized.NombreApellido,
                    normalized.Email,
                    normalized.Localidad,
                    normalized.ProvinciaCodigo
                },
                token);

            return contactId;
        }, "No se pudo guardar el contacto.", ct);

    public Task DeactivateAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Deactivate", async token =>
        {
            if (id <= 0)
                throw new InvalidOperationException("No se recibió el contacto a desactivar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            if (!await HasActivoColumnAsync(cn, token))
                throw new InvalidOperationException("La base activa no tiene la columna Activo en MA_CONTACTOS, por eso no se puede hacer baja lógica.");

            const string sql = """
                UPDATE dbo.MA_CONTACTOS
                SET Activo = 0
                WHERE id = @Id;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", id);
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El contacto seleccionado ya no existe en la base activa.");

            await appEvents.LogAuditAsync(
                ModuleName,
                "Deactivate",
                "MA_CONTACTOS",
                id.ToString(),
                "Contacto dado de baja.",
                new { Id = id, Activo = false },
                token);
        }, "No se pudo dar de baja el contacto.", ct);

    public Task<IReadOnlyList<ProvinciaOptionDto>> GetProvinciasAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetProvincias", async token =>
        {
            const string sql = """
                SELECT
                    ISNULL(CODIGO, ''),
                    ISNULL(DESCRIPCION, '')
                FROM dbo.TA_ESTADOS
                ORDER BY ISNULL(DESCRIPCION, ''), ISNULL(CODIGO, '');
                """;

            var items = new List<ProvinciaOptionDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ProvinciaOptionDto
                {
                    Codigo = GetString(rd, 0),
                    Descripcion = GetString(rd, 1)
                });
            }

            return (IReadOnlyList<ProvinciaOptionDto>)items;
        }, "No se pudieron cargar las provincias.", ct);

    private static ContactoSaveRequest NormalizeRequest(ContactoSaveRequest request)
        => new()
        {
            Id = request.Id,
            NombreApellido = request.NombreApellido?.Trim() ?? string.Empty,
            Domicilio = request.Domicilio?.Trim() ?? string.Empty,
            Localidad = request.Localidad?.Trim() ?? string.Empty,
            ProvinciaCodigo = request.ProvinciaCodigo?.Trim() ?? string.Empty,
            CodigoPostal = request.CodigoPostal?.Trim() ?? string.Empty,
            NumeroDocumento = request.NumeroDocumento?.Trim() ?? string.Empty,
            Telefono = request.Telefono?.Trim() ?? string.Empty,
            TelefonoAlternativo = request.TelefonoAlternativo?.Trim() ?? string.Empty,
            Celular = request.Celular?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            MailPagosCobranzas = request.MailPagosCobranzas,
            MailOrdenCompra = request.MailOrdenCompra,
            MailOrdenTrabajo = request.MailOrdenTrabajo,
            Website = request.Website?.Trim() ?? string.Empty,
            Cargo = request.Cargo?.Trim() ?? string.Empty,
            Observaciones = request.Observaciones?.Trim() ?? string.Empty
        };

    private static void FillSaveParameters(SqlCommand cmd, ContactoSaveRequest request)
    {
        cmd.Parameters.AddWithValue("@NombreApellido", request.NombreApellido);
        cmd.Parameters.AddWithValue("@Domicilio", DbNullable(request.Domicilio));
        cmd.Parameters.AddWithValue("@Localidad", DbNullable(request.Localidad));
        cmd.Parameters.AddWithValue("@Provincia", DbNullable(request.ProvinciaCodigo));
        cmd.Parameters.AddWithValue("@CodigoPostal", DbNullable(request.CodigoPostal));
        cmd.Parameters.AddWithValue("@Telefono", DbNullable(request.Telefono));
        cmd.Parameters.AddWithValue("@TelefonoAlternativo", DbNullable(request.TelefonoAlternativo));
        cmd.Parameters.AddWithValue("@Celular", DbNullable(request.Celular));
        cmd.Parameters.AddWithValue("@Email", DbNullable(request.Email));
        cmd.Parameters.AddWithValue("@Website", DbNullable(request.Website));
        cmd.Parameters.AddWithValue("@Observaciones", DbNullable(request.Observaciones));
        cmd.Parameters.AddWithValue("@Cargo", DbNullable(request.Cargo));
        cmd.Parameters.AddWithValue("@MailPGCB", request.MailPagosCobranzas);
        cmd.Parameters.AddWithValue("@MailOT", request.MailOrdenTrabajo);
        cmd.Parameters.AddWithValue("@MailOC", request.MailOrdenCompra);
        cmd.Parameters.AddWithValue("@NumeroDocumento", DbNullable(request.NumeroDocumento));
    }

    public Task<ContactosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
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

            var parsed = JsonSerializer.Deserialize<ContactosViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, ContactosViewSettingsDto settings, CancellationToken ct = default)
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
                "Configuración de vista de contactos actualizada.",
                new { UserName = userName.Trim(), normalized.AgruparPor, Columnas = normalized.Columnas },
                token);
        }, "No se pudo guardar la configuración de vista.", ct);

    private static ContactosViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = ContactosViewGroupKeys.None,
            Columnas =
            [
                new() { Key = ContactosViewColumnKeys.Nombre,    Label = "Nombre",    Visible = true,  Order = 0 },
                new() { Key = ContactosViewColumnKeys.Localidad, Label = "Localidad", Visible = true,  Order = 1 },
                new() { Key = ContactosViewColumnKeys.Provincia, Label = "Provincia", Visible = true,  Order = 2 },
                new() { Key = ContactosViewColumnKeys.Telefono,  Label = "Teléfono",  Visible = true,  Order = 3 },
                new() { Key = ContactosViewColumnKeys.Celular,   Label = "Celular",   Visible = true,  Order = 4 },
                new() { Key = ContactosViewColumnKeys.Email,     Label = "Email",     Visible = true,  Order = 5 },
                new() { Key = ContactosViewColumnKeys.Cargo,     Label = "Cargo",     Visible = false, Order = 6 }
            ]
        };

    private static ContactosViewSettingsDto NormalizeViewSettings(ContactosViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var normalized = new ContactosViewSettingsDto
        {
            AgruparPor = settings.AgruparPor == ContactosViewGroupKeys.Activo
                ? ContactosViewGroupKeys.Activo
                : ContactosViewGroupKeys.None,
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new ContactoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new ContactoViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
                })
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select((col, idx) => { col.Order = idx; return col; })
                .ToList()
        };

        if (!normalized.Columnas.Any(c => c.Visible))
            normalized.Columnas[0].Visible = true;

        return normalized;
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
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

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static string BuildAdvancedFilterSql(string conversationMatchSql)
        => $"""
              AND (@TieneEmail IS NULL OR CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.email, ''))), '') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END = @TieneEmail)
              AND (@TieneTelefono IS NULL OR CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.Telefono, '') + ISNULL(c.Celular, ''))), '') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END = @TieneTelefono)
              AND (@TieneWhatsApp IS NULL OR CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.CONV_CONVERSACIONES ccFilter
                    WHERE ccFilter.Canal = N'WHATSAPP'
                      AND (ccFilter.IdContacto = c.id OR {conversationMatchSql.Replace("cc.", "ccFilter.")})
                  ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @TieneWhatsApp)
              AND (@ProvinciaCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(c.Provincia, '')))) = @ProvinciaCodigo)
              AND (@Localidad = '' OR ISNULL(c.Localidad, '') LIKE '%' + @Localidad + '%')
              AND (@Cargo = '' OR ISNULL(c.Cargo, '') LIKE '%' + @Cargo + '%')
            """;

    private static void AddAdvancedFilterParameters(SqlCommand cmd, ContactosFilters filters)
    {
        cmd.Parameters.AddWithValue("@TieneEmail", filters.TieneEmail.HasValue ? filters.TieneEmail.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@TieneTelefono", filters.TieneTelefono.HasValue ? filters.TieneTelefono.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@TieneWhatsApp", filters.TieneWhatsApp.HasValue ? filters.TieneWhatsApp.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ProvinciaCodigo", (filters.ProvinciaCodigo ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Localidad", (filters.Localidad ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("@Cargo", (filters.Cargo ?? string.Empty).Trim());
    }

    private static string BuildRuleFilterSql(IEnumerable<SearchRuleDto>? rules, Func<string, string?> fieldResolver)
    {
        var clauses = new List<string>();
        var index = 0;
        foreach (var rule in (rules ?? []).Where(IsValidRule).Take(5))
        {
            var fieldSql = fieldResolver(rule.Campo);
            if (fieldSql is null)
                continue;

            var valueParam = $"@Rule{index}";
            var likeParam = $"@Rule{index}Like";
            clauses.Add(NormalizeRuleOperator(rule.Operador) switch
            {
                "equals" => $"AND {fieldSql} = {valueParam}",
                "starts" => $"AND {fieldSql} LIKE {likeParam}",
                "empty" => $"AND NULLIF(LTRIM(RTRIM({fieldSql})), '') IS NULL",
                "not-empty" => $"AND NULLIF(LTRIM(RTRIM({fieldSql})), '') IS NOT NULL",
                _ => $"AND {fieldSql} LIKE {likeParam}"
            });
            index++;
        }

        return clauses.Count == 0 ? string.Empty : string.Join(Environment.NewLine, clauses);
    }

    private static void AddRuleFilterParameters(SqlCommand cmd, IEnumerable<SearchRuleDto>? rules, Func<string, string?> fieldResolver)
    {
        var index = 0;
        foreach (var rule in (rules ?? []).Where(IsValidRule).Take(5))
        {
            if (fieldResolver(rule.Campo) is null)
                continue;

            var value = (rule.Valor ?? string.Empty).Trim();
            var op = NormalizeRuleOperator(rule.Operador);
            cmd.Parameters.AddWithValue($"@Rule{index}", value);
            cmd.Parameters.AddWithValue($"@Rule{index}Like", op == "starts" ? $"{value}%" : $"%{value}%");
            index++;
        }
    }

    private static string? GetContactosRuleFieldSql(string field)
        => (field ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "nombre" => "ISNULL(c.Nombre_y_Apellido, '')",
            "email" => "ISNULL(c.email, '')",
            "telefono" => "ISNULL(c.Telefono, '')",
            "celular" => "ISNULL(c.Celular, '')",
            "localidad" => "ISNULL(c.Localidad, '')",
            "provincia" => "ISNULL(c.Provincia, '')",
            "cargo" => "ISNULL(c.Cargo, '')",
            _ => null
        };

    private static bool IsValidRule(SearchRuleDto? rule)
        => rule is not null
           && !string.IsNullOrWhiteSpace(rule.Campo)
           && (IsValueOptionalOperator(rule.Operador) || !string.IsNullOrWhiteSpace(rule.Valor));

    private static string NormalizeRuleOperator(string? op)
        => (op ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "equals" => "equals",
            "starts" => "starts",
            "empty" => "empty",
            "not-empty" => "not-empty",
            _ => "contains"
        };

    private static bool IsValueOptionalOperator(string? op)
    {
        var normalized = NormalizeRuleOperator(op);
        return normalized is "empty" or "not-empty";
    }

    private static async Task<bool> HasActivoColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.MA_CONTACTOS')
              AND LOWER(name) = N'activo';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index));

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string BuildConversationMatchSql(string contactAlias)
        => string.Join(
            $"{Environment.NewLine}                            OR ",
            SqlPhoneEquivalentColumnsPredicate("cc.TelefonoWhatsApp", $"{contactAlias}.Telefono"),
            SqlPhoneEquivalentColumnsPredicate("cc.TelefonoWhatsApp", $"{contactAlias}.Celular"),
            SqlPhoneEquivalentColumnsPredicate("cc.TelefonoWhatsApp", $"{contactAlias}.Fax"));

    private static string SqlNormalizePhone(string columnName)
        => $"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL({columnName}, ''), ' ', ''), '-', ''), '+', ''), '(', ''), ')', ''), '.', '')";

    private static string SqlPhoneEquivalentColumnsPredicate(string leftColumnName, string rightColumnName)
    {
        var left = SqlNormalizePhone(leftColumnName);
        var right = SqlNormalizePhone(rightColumnName);
        return $"""
            (
                {left} <> ''
                AND {right} <> ''
                AND (
                    {left} = {right}
                    OR (
                        LEN({left}) >= 10
                        AND LEN({right}) >= 10
                        AND RIGHT({left}, 10) = RIGHT({right}, 10)
                    )
                )
            )
            """;
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

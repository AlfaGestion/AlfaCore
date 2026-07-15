using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, bool> ActiveColumnCache = new(StringComparer.OrdinalIgnoreCase);

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
            var contactOrderSql = BuildContactosOrderBy(
                filters,
                "c",
                "COALESCE(NULLIF(e.DESCRIPCION, ''), ISNULL(c.Provincia, ''))",
                "id");
            var orderBySql = hasActivo
                ? $"{activoExpr} DESC, {contactOrderSql}"
                : contactOrderSql;
            var projectedOrderSql = BuildContactosOrderBy(
                filters,
                "pageContacts",
                "COALESCE(NULLIF(pageContacts.ProvinciaDescripcion, ''), pageContacts.ProvinciaCodigo)",
                "Id");

            var filterConversationMatchSql = BuildConversationMatchSql("c");
            var pageConversationMatchSql = BuildConversationMatchSql("pageContacts");
            var advancedFilterSql = BuildAdvancedFilterSql(filters, filterConversationMatchSql);
            var ruleFilterSql = BuildRuleFilterSql(filters.Reglas, GetContactosRuleFieldSql);
            var sql = $"""
                SELECT
                    pageContacts.Id,
                    pageContacts.NombreApellido,
                    pageContacts.Localidad,
                    pageContacts.ProvinciaCodigo,
                    pageContacts.ProvinciaDescripcion,
                    pageContacts.Telefono,
                    pageContacts.Celular,
                    pageContacts.Email,
                    pageContacts.Cargo,
                    pageContacts.Activo,
                    conv.IdConversacion
                FROM (
                    SELECT
                        c.id AS Id,
                        ISNULL(c.Nombre_y_Apellido, '') AS NombreApellido,
                        ISNULL(c.Localidad, '') AS Localidad,
                        ISNULL(c.Provincia, '') AS ProvinciaCodigo,
                        ISNULL(e.DESCRIPCION, '') AS ProvinciaDescripcion,
                        ISNULL(c.Telefono, '') AS Telefono,
                        ISNULL(c.Celular, '') AS Celular,
                        ISNULL(c.Fax, '') AS Fax,
                        ISNULL(c.email, '') AS Email,
                        ISNULL(c.Cargo, '') AS Cargo,
                        {activoExpr} AS Activo
                    FROM dbo.MA_CONTACTOS c
                    LEFT JOIN dbo.TA_ESTADOS e
                        ON UPPER(LTRIM(RTRIM(e.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(c.Provincia, ''))))
                    WHERE (
                            @TextoLike = ''
                            OR ISNULL(c.Nombre_y_Apellido, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR ISNULL(c.email, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR ISNULL(c.Localidad, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                            OR ISNULL(c.Telefono, '') LIKE @TextoLike
                            OR ISNULL(c.Celular, '') LIKE @TextoLike
                            OR ISNULL(c.Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                      )
                      {activoFilterSql}
                      {advancedFilterSql}
                      {ruleFilterSql}
                    ORDER BY {orderBySql}
                    OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY
                ) pageContacts
                OUTER APPLY (
                    SELECT TOP (1) cc.IdConversacion
                    FROM dbo.CONV_CONVERSACIONES cc
                    WHERE cc.Canal = N'WHATSAPP'
                      AND (
                            cc.IdContacto = pageContacts.Id
                            OR {pageConversationMatchSql}
                          )
                    ORDER BY cc.IdConversacion ASC
                ) conv
                ORDER BY pageContacts.Activo DESC, {projectedOrderSql};

                SELECT COUNT(*)
                FROM dbo.MA_CONTACTOS c
                WHERE (
                        @TextoLike = ''
                        OR ISNULL(c.Nombre_y_Apellido, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.email, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.Localidad, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.Telefono, '') LIKE @TextoLike
                        OR ISNULL(c.Celular, '') LIKE @TextoLike
                        OR ISNULL(c.Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                  )
                  {advancedFilterSql}
                  {ruleFilterSql}
                  {activoFilterSql};
                """;

            var rows = new List<ContactoGridItemDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(filters.Texto));
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

    private static string BuildContactosOrderBy(
        ContactosFilters filters,
        string alias,
        string provinciaExpression,
        string idColumn)
    {
        var projected = string.Equals(idColumn, "Id", StringComparison.Ordinal);
        var expression = (filters.OrdenarPor ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ContactosViewColumnKeys.Localidad => $"ISNULL({alias}.Localidad, '')",
            ContactosViewColumnKeys.Provincia => provinciaExpression,
            ContactosViewColumnKeys.Telefono => $"ISNULL({alias}.Telefono, '')",
            ContactosViewColumnKeys.Celular => $"ISNULL({alias}.Celular, '')",
            ContactosViewColumnKeys.Email => $"ISNULL({alias}.Email, '')",
            ContactosViewColumnKeys.Cargo => $"ISNULL({alias}.Cargo, '')",
            _ => $"ISNULL({alias}.{(projected ? "NombreApellido" : "Nombre_y_Apellido")}, '')"
        };
        var direction = filters.OrdenDescendente ? "DESC" : "ASC";
        return $"CASE WHEN NULLIF(LTRIM(RTRIM({expression})), '') IS NULL THEN 1 ELSE 0 END ASC, {expression} {direction}, {alias}.{idColumn} ASC";
    }

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

            var detail = new ContactoDetailDto
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

            await rd.CloseAsync();
            detail.CuentasVinculadas = (await GetCuentasVinculadasCoreAsync(cn, id, token)).ToList();
            return detail;
        }, "No se pudo cargar el contacto seleccionado.", ct);

    public Task<IReadOnlyList<ContactoCuentaDto>> GetCuentasVinculadasAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCuentasVinculadas", async token =>
        {
            if (id <= 0)
                return [];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await GetCuentasVinculadasCoreAsync(cn, id, token);
        }, "No se pudieron cargar las cuentas vinculadas al contacto.", ct);

    public Task<IReadOnlyList<ContactoMergeCandidateDto>> SearchMergeCandidatesAsync(int contactoPrincipalId, string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchMergeCandidates", async token =>
        {
            if (contactoPrincipalId <= 0)
                return [];

            var normalizedText = SearchTextHelper.Normalize(texto);
            if (normalizedText.Length < 2)
                return [];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var activoExpr = hasActivo ? "ISNULL(c.Activo, 1)" : "CAST(1 AS bit)";
            var sql = $"""
                SELECT TOP (20)
                    c.id,
                    ISNULL(c.Nombre_y_Apellido, ''),
                    ISNULL(c.Cargo, ''),
                    ISNULL(c.email, ''),
                    ISNULL(c.Telefono, ''),
                    ISNULL(c.Celular, ''),
                    ISNULL(c.CuentaRel, ''),
                    {activoExpr}
                FROM dbo.MA_CONTACTOS c
                WHERE c.id <> @ContactoPrincipalId
                  AND (
                        ISNULL(c.Nombre_y_Apellido, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.email, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(c.Telefono, '') LIKE @TextoLike
                        OR ISNULL(c.Celular, '') LIKE @TextoLike
                        OR ISNULL(c.Cargo, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                      )
                ORDER BY {activoExpr} DESC, ISNULL(c.Nombre_y_Apellido, '') ASC, c.id ASC;
                """;

            var rows = new List<ContactoMergeCandidateDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@ContactoPrincipalId", contactoPrincipalId);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(normalizedText));
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                rows.Add(new ContactoMergeCandidateDto
                {
                    Id = GetInt(rd, 0),
                    NombreApellido = GetString(rd, 1),
                    Cargo = GetString(rd, 2),
                    Email = GetString(rd, 3),
                    Telefono = GetString(rd, 4),
                    Celular = GetString(rd, 5),
                    CuentaRel = GetString(rd, 6),
                    Activo = GetBool(rd, 7)
                });
            }

            return (IReadOnlyList<ContactoMergeCandidateDto>)rows;
        }, "No se pudieron buscar contactos para unificar.", ct);

    public Task<ContactoDetailDto?> MergeContactsAsync(ContactoMergeRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MergeContacts", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.ContactoPrincipalId <= 0 || request.ContactoDuplicadoId <= 0)
                throw new InvalidOperationException("Seleccioná el contacto principal y el duplicado para unificar.");
            if (request.ContactoPrincipalId == request.ContactoDuplicadoId)
                throw new InvalidOperationException("No se puede unificar un contacto consigo mismo.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var hasActivo = await HasActivoColumnAsync(cn, token);
            var hasTickets = await TableColumnExistsAsync(cn, "TICK_TICKETS", "IdContacto", token);

            var principal = await GetMergeRowAsync(cn, request.ContactoPrincipalId, token);
            var duplicado = await GetMergeRowAsync(cn, request.ContactoDuplicadoId, token);
            if (principal is null || duplicado is null)
                throw new InvalidOperationException("No se encontraron los contactos seleccionados para unificar.");

            var principalRel = principal.IdContactoRel > 0 ? principal.IdContactoRel : principal.Id;
            var duplicadoRel = duplicado.IdContactoRel > 0 ? duplicado.IdContactoRel : duplicado.Id;
            var merged = MergeRows(principal, duplicado);

            await using var tx = await cn.BeginTransactionAsync(token);
            var sql = hasActivo
                ? """
                UPDATE dbo.MA_CONTACTOS
                SET
                    idContacto = @PrincipalRel,
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
                    NroDocumento = @NumeroDocumento,
                    Activo = 1
                WHERE id = @PrincipalId;

                UPDATE dbo.MA_CONTACTOS
                SET idContacto = @PrincipalRel,
                    Activo = 0
                WHERE id = @DuplicadoId;
                """
                : """
                UPDATE dbo.MA_CONTACTOS
                SET
                    idContacto = @PrincipalRel,
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
                WHERE id = @PrincipalId;

                UPDATE dbo.MA_CONTACTOS
                SET idContacto = @PrincipalRel
                WHERE id = @DuplicadoId;
                """;

            await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
            {
                FillSaveParameters(cmd, merged);
                cmd.Parameters.AddWithValue("@PrincipalId", principal.Id);
                cmd.Parameters.AddWithValue("@DuplicadoId", duplicado.Id);
                cmd.Parameters.AddWithValue("@PrincipalRel", principalRel);
                var affected = await cmd.ExecuteNonQueryAsync(token);
                if (affected == 0)
                    throw new InvalidOperationException("No se pudo actualizar el contacto principal.");
            }

            await ConsolidarRelacionesContactoAsync(cn, (SqlTransaction)tx, principalRel, duplicado.Id, duplicadoRel, duplicado.CuentaRel, token);
            await ReasignarReferenciasContactoAsync(cn, (SqlTransaction)tx, principal.Id, duplicado.Id, duplicadoRel, hasTickets, token);
            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "Merge",
                "MA_CONTACTOS",
                principal.Id.ToString(),
                "Contactos unificados.",
                new
                {
                    ContactoPrincipalId = principal.Id,
                    ContactoDuplicadoId = duplicado.Id,
                    ContactoPrincipalRel = principalRel,
                    ContactoDuplicadoRel = duplicadoRel,
                    hasTickets
                },
                token);

            return await GetByIdAsync(principal.Id, token);
        }, "No se pudieron unificar los contactos.", ct);

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

    private static async Task<ContactoMergeRow?> GetMergeRowAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                id,
                ISNULL(NULLIF(idContacto, 0), id) AS IdContactoRel,
                ISNULL(Nombre_y_Apellido, ''),
                ISNULL(Domicilio, ''),
                ISNULL(Localidad, ''),
                ISNULL(Provincia, ''),
                ISNULL(C_Postal, ''),
                ISNULL(NroDocumento, ''),
                ISNULL(Telefono, ''),
                ISNULL(Fax, ''),
                ISNULL(Celular, ''),
                ISNULL(email, ''),
                ISNULL(mailPGCB, 0),
                ISNULL(mailOC, 0),
                ISNULL(mailOT, 0),
                ISNULL(WebSite, ''),
                ISNULL(Cargo, ''),
                ISNULL(CAST(Observaciones AS nvarchar(max)), ''),
                ISNULL(CuentaRel, '')
            FROM dbo.MA_CONTACTOS
            WHERE id = @Id;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new ContactoMergeRow
        {
            Id = GetInt(rd, 0),
            IdContactoRel = GetInt(rd, 1),
            NombreApellido = GetString(rd, 2),
            Domicilio = GetString(rd, 3),
            Localidad = GetString(rd, 4),
            ProvinciaCodigo = GetString(rd, 5),
            CodigoPostal = GetString(rd, 6),
            NumeroDocumento = GetString(rd, 7),
            Telefono = GetString(rd, 8),
            TelefonoAlternativo = GetString(rd, 9),
            Celular = GetString(rd, 10),
            Email = GetString(rd, 11),
            MailPagosCobranzas = GetBool(rd, 12),
            MailOrdenCompra = GetBool(rd, 13),
            MailOrdenTrabajo = GetBool(rd, 14),
            Website = GetString(rd, 15),
            Cargo = GetString(rd, 16),
            Observaciones = GetString(rd, 17),
            CuentaRel = GetString(rd, 18)
        };
    }

    private static ContactoSaveRequest MergeRows(ContactoMergeRow principal, ContactoMergeRow duplicado)
        => new()
        {
            Id = principal.Id,
            NombreApellido = PreferPrincipal(principal.NombreApellido, duplicado.NombreApellido),
            Domicilio = PreferPrincipal(principal.Domicilio, duplicado.Domicilio),
            Localidad = PreferPrincipal(principal.Localidad, duplicado.Localidad),
            ProvinciaCodigo = PreferPrincipal(principal.ProvinciaCodigo, duplicado.ProvinciaCodigo),
            CodigoPostal = PreferPrincipal(principal.CodigoPostal, duplicado.CodigoPostal),
            NumeroDocumento = PreferPrincipal(principal.NumeroDocumento, duplicado.NumeroDocumento),
            Telefono = PreferPrincipal(principal.Telefono, duplicado.Telefono),
            TelefonoAlternativo = PreferPrincipal(principal.TelefonoAlternativo, duplicado.TelefonoAlternativo),
            Celular = PreferPrincipal(principal.Celular, duplicado.Celular),
            Email = PreferPrincipal(principal.Email, duplicado.Email),
            MailPagosCobranzas = principal.MailPagosCobranzas || duplicado.MailPagosCobranzas,
            MailOrdenCompra = principal.MailOrdenCompra || duplicado.MailOrdenCompra,
            MailOrdenTrabajo = principal.MailOrdenTrabajo || duplicado.MailOrdenTrabajo,
            Website = PreferPrincipal(principal.Website, duplicado.Website),
            Cargo = PreferPrincipal(principal.Cargo, duplicado.Cargo),
            Observaciones = MergeObservaciones(principal.Observaciones, duplicado.Observaciones)
        };

    private static string PreferPrincipal(string principal, string duplicado)
        => string.IsNullOrWhiteSpace(principal) ? (duplicado?.Trim() ?? string.Empty) : principal.Trim();

    private static string MergeObservaciones(string principal, string duplicado)
    {
        var primary = principal?.Trim() ?? string.Empty;
        var duplicate = duplicado?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(primary))
            return duplicate;
        if (string.IsNullOrWhiteSpace(duplicate) || string.Equals(primary, duplicate, StringComparison.OrdinalIgnoreCase))
            return primary;

        return $"{primary}{Environment.NewLine}{Environment.NewLine}--- Dato unificado ---{Environment.NewLine}{duplicate}";
    }

    private static async Task ConsolidarRelacionesContactoAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int principalRel,
        int duplicadoId,
        int duplicadoRel,
        string duplicadoCuentaRel,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.MA_CONTACTOS_CUENTAS (IdContacto, Cuenta)
            SELECT DISTINCT @PrincipalRel, UPPER(LTRIM(RTRIM(rel.Cuenta)))
            FROM dbo.MA_CONTACTOS_CUENTAS rel
            WHERE rel.IdContacto IN (@DuplicadoId, @DuplicadoRel)
              AND rel.IdContacto <> @PrincipalRel
              AND LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))) <> ''
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.MA_CONTACTOS_CUENTAS actual
                    WHERE actual.IdContacto = @PrincipalRel
                      AND UPPER(LTRIM(RTRIM(ISNULL(actual.Cuenta, '')))) = UPPER(LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))))
              );

            INSERT INTO dbo.MA_CONTACTOS_CUENTAS (IdContacto, Cuenta)
            SELECT @PrincipalRel, @DuplicadoCuentaRel
            WHERE @DuplicadoCuentaRel <> ''
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.MA_CONTACTOS_CUENTAS actual
                    WHERE actual.IdContacto = @PrincipalRel
                      AND UPPER(LTRIM(RTRIM(ISNULL(actual.Cuenta, '')))) = @DuplicadoCuentaRel
              );

            DELETE rel
            FROM dbo.MA_CONTACTOS_CUENTAS rel
            WHERE rel.IdContacto IN (@DuplicadoId, @DuplicadoRel)
              AND rel.IdContacto <> @PrincipalRel;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@PrincipalRel", principalRel);
        cmd.Parameters.AddWithValue("@DuplicadoId", duplicadoId);
        cmd.Parameters.AddWithValue("@DuplicadoRel", duplicadoRel);
        cmd.Parameters.AddWithValue("@DuplicadoCuentaRel", (duplicadoCuentaRel ?? string.Empty).Trim().ToUpperInvariant());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReasignarReferenciasContactoAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int principalId,
        int duplicadoId,
        int duplicadoRel,
        bool hasTickets,
        CancellationToken ct)
    {
        var sql = new StringBuilder("""
            UPDATE dbo.CONV_CONVERSACIONES
            SET IdContacto = @PrincipalId
            WHERE IdContacto IN (@DuplicadoId, @DuplicadoRel)
              AND IdContacto <> @PrincipalId;
            """);

        if (hasTickets)
        {
            sql.AppendLine("""

            UPDATE dbo.TICK_TICKETS
            SET IdContacto = @PrincipalId
            WHERE IdContacto IN (@DuplicadoId, @DuplicadoRel)
              AND IdContacto <> @PrincipalId;
            """);
        }

        await using var cmd = new SqlCommand(sql.ToString(), cn, tx);
        cmd.Parameters.AddWithValue("@PrincipalId", principalId);
        cmd.Parameters.AddWithValue("@DuplicadoId", duplicadoId);
        cmd.Parameters.AddWithValue("@DuplicadoRel", duplicadoRel);
        await cmd.ExecuteNonQueryAsync(ct);
    }

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

    private static async Task<IReadOnlyList<ContactoCuentaDto>> GetCuentasVinculadasCoreAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        const string sql = """
            WITH ContactoBase AS
            (
                SELECT TOP (1)
                    id,
                    ISNULL(NULLIF(idContacto, 0), id) AS IdContactoRel,
                    UPPER(LTRIM(RTRIM(ISNULL(CuentaRel, '')))) AS CuentaRel
                FROM dbo.MA_CONTACTOS
                WHERE id = @Id
            ),
            Cuentas AS
            (
                SELECT
                    UPPER(LTRIM(RTRIM(rel.Cuenta))) AS Codigo,
                    CAST(1 AS bit) AS VinculadoPorTabla,
                    CAST(0 AS bit) AS VinculadoPorCuentaRel
                FROM ContactoBase cb
                INNER JOIN dbo.MA_CONTACTOS_CUENTAS rel
                    ON rel.IdContacto = cb.IdContactoRel
                WHERE LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))) <> ''

                UNION ALL

                SELECT
                    cb.CuentaRel AS Codigo,
                    CAST(0 AS bit) AS VinculadoPorTabla,
                    CAST(1 AS bit) AS VinculadoPorCuentaRel
                FROM ContactoBase cb
                WHERE cb.CuentaRel <> ''
            )
            SELECT
                c.Codigo,
                ISNULL(cli.RAZON_SOCIAL, ISNULL(prv.RAZON_SOCIAL, '')) AS RazonSocial,
                CASE
                    WHEN cli.CODIGO IS NOT NULL THEN N'Cliente'
                    WHEN prv.CODIGO IS NOT NULL THEN N'Proveedor'
                    ELSE N'Cuenta'
                END AS Tipo,
                CAST(MAX(CASE WHEN c.VinculadoPorTabla = 1 THEN 1 ELSE 0 END) AS bit) AS VinculadoPorTabla,
                CAST(MAX(CASE WHEN c.VinculadoPorCuentaRel = 1 THEN 1 ELSE 0 END) AS bit) AS VinculadoPorCuentaRel
            FROM Cuentas c
            LEFT JOIN dbo.VT_CLIENTES cli
                ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = c.Codigo
            LEFT JOIN dbo.VT_PROVEEDORES prv
                ON UPPER(LTRIM(RTRIM(prv.CODIGO))) = c.Codigo
            GROUP BY c.Codigo, ISNULL(cli.RAZON_SOCIAL, ISNULL(prv.RAZON_SOCIAL, '')),
                CASE
                    WHEN cli.CODIGO IS NOT NULL THEN N'Cliente'
                    WHEN prv.CODIGO IS NOT NULL THEN N'Proveedor'
                    ELSE N'Cuenta'
                END
            ORDER BY Tipo, RazonSocial, Codigo;
            """;

        var rows = new List<ContactoCuentaDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            rows.Add(new ContactoCuentaDto
            {
                Codigo = GetString(rd, 0),
                RazonSocial = GetString(rd, 1),
                Tipo = GetString(rd, 2),
                VinculadoPorTabla = GetBool(rd, 3),
                VinculadoPorCuentaRel = GetBool(rd, 4)
            });
        }

        return rows;
    }

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

    private static string BuildAdvancedFilterSql(ContactosFilters filters, string conversationMatchSql)
    {
        var whatsappFilterSql = filters.TieneWhatsApp.HasValue
            ? $"""
              AND CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.CONV_CONVERSACIONES ccFilter
                    WHERE ccFilter.Canal = N'WHATSAPP'
                      AND (ccFilter.IdContacto = c.id OR {conversationMatchSql.Replace("cc.", "ccFilter.")})
                  ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @TieneWhatsApp
            """
            : string.Empty;

        return $"""
              AND (@TieneEmail IS NULL OR CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.email, ''))), '') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END = @TieneEmail)
              AND (@TieneCelular IS NULL OR CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.Celular, ''))), '') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END = @TieneCelular)
              {whatsappFilterSql}
              AND (@ProvinciaCodigo = '' OR UPPER(LTRIM(RTRIM(ISNULL(c.Provincia, '')))) = @ProvinciaCodigo)
              AND (@Localidad = '' OR ISNULL(c.Localidad, '') LIKE '%' + @Localidad + '%')
              AND (@Cargo = '' OR ISNULL(c.Cargo, '') LIKE '%' + @Cargo + '%')
            """;
    }

    private static void AddAdvancedFilterParameters(SqlCommand cmd, ContactosFilters filters)
    {
        cmd.Parameters.AddWithValue("@TieneEmail", filters.TieneEmail.HasValue ? filters.TieneEmail.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@TieneCelular", filters.TieneCelular.HasValue ? filters.TieneCelular.Value : DBNull.Value);
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
        var cacheKey = $"{cn.DataSource}|{cn.Database}|dbo.MA_CONTACTOS.Activo";
        if (ActiveColumnCache.TryGetValue(cacheKey, out var cached))
            return cached;

        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.MA_CONTACTOS')
              AND LOWER(name) = N'activo';
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var exists = Convert.ToInt32(result) > 0;
        ActiveColumnCache[cacheKey] = exists;
        return exists;
    }

    private static async Task<bool> TableColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@TableName)
              AND LOWER(name) = LOWER(@ColumnName);
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@TableName", $"dbo.{tableName}");
        cmd.Parameters.AddWithValue("@ColumnName", columnName);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    private sealed class ContactoMergeRow
    {
        public int Id { get; set; }
        public int IdContactoRel { get; set; }
        public string NombreApellido { get; set; } = string.Empty;
        public string Domicilio { get; set; } = string.Empty;
        public string Localidad { get; set; } = string.Empty;
        public string ProvinciaCodigo { get; set; } = string.Empty;
        public string CodigoPostal { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TelefonoAlternativo { get; set; } = string.Empty;
        public string Celular { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool MailPagosCobranzas { get; set; }
        public bool MailOrdenCompra { get; set; }
        public bool MailOrdenTrabajo { get; set; }
        public string Website { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public string CuentaRel { get; set; } = string.Empty;
    }
}

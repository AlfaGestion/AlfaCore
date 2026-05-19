using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class TicketsService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : ITicketsService
{
    private const string ModuleName = "Tickets";
    private const string ConfigGroup = "TICKETS";
    private const string ViewConfigPrefix = "USUVIEW-TICKETS-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<TicketLookupDto> GetLookupsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetLookups", async token =>
        {
            const string sql = """
                SELECT CodigoEstado, Nombre, ISNULL(Color, ''), ISNULL(EsCerrado, 0), ISNULL(Orden, 0)
                FROM dbo.TICK_ESTADOS
                WHERE ISNULL(Activo, 1) = 1
                ORDER BY Orden, Nombre;

                SELECT
                    ISNULL(IdTecnico, ''),
                    ISNULL(Nombre, ''),
                    ISNULL(Cargo, ''),
                    ISNULL(UsuarioAsociado, ''),
                    ISNULL(SistemaAsociado, '')
                FROM dbo.V_TA_Tecnicos
                WHERE ISNULL(Baja, 0) = 0
                ORDER BY Nombre;

                SELECT IdEtiqueta, Nombre, ISNULL(Color, '')
                FROM dbo.TICK_ETIQUETAS
                WHERE ISNULL(Activa, 1) = 1
                ORDER BY Nombre;
                """;

            var result = new TicketLookupDto();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);

            while (await rd.ReadAsync(token))
            {
                result.Estados.Add(new TicketEstadoDto
                {
                    CodigoEstado = GetString(rd, 0),
                    Nombre = GetString(rd, 1),
                    Color = GetString(rd, 2),
                    EsCerrado = GetBool(rd, 3),
                    Orden = GetInt(rd, 4)
                });
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    result.Tecnicos.Add(new ConversacionTecnicoOptionDto
                    {
                        IdTecnico = GetString(rd, 0),
                        Nombre = GetString(rd, 1),
                        Cargo = GetString(rd, 2),
                        UsuarioAsociado = GetString(rd, 3),
                        SistemaAsociado = GetString(rd, 4)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    result.Etiquetas.Add(new TicketEtiquetaDto
                    {
                        IdEtiqueta = GetInt(rd, 0),
                        Nombre = GetString(rd, 1),
                        Color = GetString(rd, 2)
                    });
                }
            }

            return result;
        }, "No se pudieron cargar los datos auxiliares de tickets.", ct);

    public Task<PagedResult<TicketGridItemDto>> SearchAsync(TicketsFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Search", async token =>
        {
            filters ??= new TicketsFilters();
            var pageSize = Math.Clamp(filters.PageSize, 1, 200);
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            var sql = $"""
                {TicketListSelectSql()}
                {TicketWhereSql()}
                ORDER BY t.FechaHoraAlta DESC, t.IdTicket DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.TICK_TICKETS t
                INNER JOIN dbo.TICK_ESTADOS e ON e.CodigoEstado = t.CodigoEstado
                LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = t.ClienteCodigo
                LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = t.IdContacto
                LEFT JOIN dbo.V_TA_Tecnicos tec ON tec.IdTecnico = t.IdTecnico
                {TicketWhereSql()};
                """;

            var rows = new List<TicketGridItemDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            AddFilterParameters(cmd, filters);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);
            await using var rd = await cmd.ExecuteReaderAsync(token);

            while (await rd.ReadAsync(token))
                rows.Add(ReadTicketGridItem(rd));

            var total = 0;
            if (await rd.NextResultAsync(token) && await rd.ReadAsync(token))
                total = GetInt(rd, 0);

            await HydrateEtiquetasAsync(cn, rows, token);

            return new PagedResult<TicketGridItemDto>
            {
                Items = rows,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los tickets.", ct);

    public Task<IReadOnlyList<TicketGridItemDto>> GetKanbanAsync(TicketsFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetKanban", async token =>
        {
            filters ??= new TicketsFilters();
            var limit = Math.Clamp(filters.PageSize <= 0 ? 120 : filters.PageSize, 25, 200);
            var sql = $"""
                {TicketListSelectSql()}
                {TicketWhereSql()}
                ORDER BY e.Orden, t.Prioridad DESC, t.FechaHoraAlta DESC, t.IdTicket DESC
                OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;
                """;

            var rows = new List<TicketGridItemDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            AddFilterParameters(cmd, filters);
            cmd.Parameters.AddWithValue("@Limit", limit);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
                rows.Add(ReadTicketGridItem(rd));

            await HydrateEtiquetasAsync(cn, rows, token);
            return (IReadOnlyList<TicketGridItemDto>)rows;
        }, "No se pudo cargar la vista kanban de tickets.", ct);

    public Task<TicketDetailDto?> GetByIdAsync(long idTicket, CancellationToken ct = default)
        => GetDetailAsync("t.IdTicket = @IdTicket", cmd => cmd.Parameters.AddWithValue("@IdTicket", idTicket), ct);

    public Task<TicketDetailDto?> GetByNumeroAsync(int numero, CancellationToken ct = default)
        => GetDetailAsync("t.Numero = @Numero", cmd => cmd.Parameters.AddWithValue("@Numero", numero), ct);

    public Task<long> CreateAsync(TicketCreateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Create", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var title = NormalizeTitle(request.Titulo);
            var priority = Math.Clamp(request.Prioridad, 0, 3);
            var state = string.IsNullOrWhiteSpace(request.CodigoEstado) ? TicketEstadoKeys.Nuevo : request.CodigoEstado.Trim().ToUpperInvariant();
            var user = NormalizeUser(request.UsuarioAccion);
            var messageIds = request.IdMensajes.Where(x => x > 0).Distinct().ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            if (request.IdConversacion.HasValue)
                await ApplyConversationDefaultsAsync(cn, (SqlTransaction)tx, request, token);

            const string sql = """
                DECLARE @Numero int;

                SELECT @Numero = ISNULL(MAX(Numero), 0) + 1
                FROM dbo.TICK_TICKETS WITH (UPDLOCK, HOLDLOCK);

                INSERT INTO dbo.TICK_TICKETS
                (
                    Numero, Titulo, Descripcion, CodigoEstado, Prioridad, IdTecnico,
                    ClienteCodigo, IdContacto, IdConversacion, UsuarioAlta,
                    FechaHoraAlta, FechaHoraModificacion
                )
                VALUES
                (
                    @Numero, @Titulo, @Descripcion, @CodigoEstado, @Prioridad, @IdTecnico,
                    @ClienteCodigo, @IdContacto, @IdConversacion, @UsuarioAlta,
                    GETDATE(), GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
            cmd.Parameters.AddWithValue("@Titulo", title);
            cmd.Parameters.AddWithValue("@Descripcion", DbNullable(request.Descripcion));
            cmd.Parameters.AddWithValue("@CodigoEstado", state);
            cmd.Parameters.AddWithValue("@Prioridad", priority);
            cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));
            cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(request.ClienteCodigo));
            cmd.Parameters.AddWithValue("@IdContacto", request.IdContacto.HasValue ? request.IdContacto.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion.HasValue ? request.IdConversacion.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@UsuarioAlta", user);
            var idTicket = Convert.ToInt64(await cmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);

            await InsertTicketMessagesAsync(cn, (SqlTransaction)tx, idTicket, messageIds, token);
            await InsertActivityAsync(cn, (SqlTransaction)tx, idTicket, "CREACION", $"Ticket creado: {title}", user, token);

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "Create",
                "TICK_TICKETS",
                idTicket.ToString(CultureInfo.InvariantCulture),
                "Ticket de asistencia creado.",
                new { IdTicket = idTicket, title, request.IdConversacion, Mensajes = messageIds },
                token);

            return idTicket;
        }, "No se pudo crear el ticket.", ct);

    public Task UpdateAsync(TicketUpdateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Update", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdTicket <= 0)
                throw new InvalidOperationException("No se recibió el ticket a actualizar.");

            var title = NormalizeTitle(request.Titulo);
            var priority = Math.Clamp(request.Prioridad, 0, 3);
            var state = string.IsNullOrWhiteSpace(request.CodigoEstado) ? TicketEstadoKeys.Nuevo : request.CodigoEstado.Trim().ToUpperInvariant();
            var user = NormalizeUser(request.UsuarioAccion);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            const string sql = """
                UPDATE dbo.TICK_TICKETS
                SET
                    Titulo = @Titulo,
                    Descripcion = @Descripcion,
                    CodigoEstado = @CodigoEstado,
                    Prioridad = @Prioridad,
                    IdTecnico = @IdTecnico,
                    FechaHoraModificacion = GETDATE(),
                    FechaHoraCierre = CASE WHEN EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = @CodigoEstado AND ISNULL(EsCerrado, 0) = 1) THEN ISNULL(FechaHoraCierre, GETDATE()) ELSE NULL END
                WHERE IdTicket = @IdTicket;
                """;

            await using var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx);
            cmd.Parameters.AddWithValue("@IdTicket", request.IdTicket);
            cmd.Parameters.AddWithValue("@Titulo", title);
            cmd.Parameters.AddWithValue("@Descripcion", DbNullable(request.Descripcion));
            cmd.Parameters.AddWithValue("@CodigoEstado", state);
            cmd.Parameters.AddWithValue("@Prioridad", priority);
            cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El ticket seleccionado ya no existe en la base activa.");

            await InsertActivityAsync(cn, (SqlTransaction)tx, request.IdTicket, "EDICION", "Ticket actualizado.", user, token);
            await tx.CommitAsync(token);
        }, "No se pudo actualizar el ticket.", ct);

    public Task QuickUpdateAsync(TicketQuickUpdateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "QuickUpdate", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdTicket <= 0)
                throw new InvalidOperationException("No se recibió el ticket a actualizar.");

            var updates = new List<string>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            await using var cmd = new SqlCommand { Connection = cn, Transaction = (SqlTransaction)tx };
            cmd.Parameters.AddWithValue("@IdTicket", request.IdTicket);

            if (!string.IsNullOrWhiteSpace(request.CodigoEstado))
            {
                updates.Add("CodigoEstado = @CodigoEstado");
                updates.Add("FechaHoraCierre = CASE WHEN EXISTS (SELECT 1 FROM dbo.TICK_ESTADOS WHERE CodigoEstado = @CodigoEstado AND ISNULL(EsCerrado, 0) = 1) THEN ISNULL(FechaHoraCierre, GETDATE()) ELSE NULL END");
                cmd.Parameters.AddWithValue("@CodigoEstado", request.CodigoEstado.Trim().ToUpperInvariant());
            }

            if (request.IdTecnico is not null)
            {
                updates.Add("IdTecnico = @IdTecnico");
                cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));
            }

            if (request.Prioridad.HasValue)
            {
                updates.Add("Prioridad = @Prioridad");
                cmd.Parameters.AddWithValue("@Prioridad", Math.Clamp(request.Prioridad.Value, 0, 3));
            }

            if (updates.Count == 0)
                return;

            updates.Add("FechaHoraModificacion = GETDATE()");
            cmd.CommandText = $"UPDATE dbo.TICK_TICKETS SET {string.Join(", ", updates)} WHERE IdTicket = @IdTicket;";
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("El ticket seleccionado ya no existe en la base activa.");

            await InsertActivityAsync(cn, (SqlTransaction)tx, request.IdTicket, "CAMBIO", "Ticket actualizado desde acción rápida.", NormalizeUser(request.UsuarioAccion), token);
            await tx.CommitAsync(token);
        }, "No se pudo actualizar el ticket.", ct);

    public Task AddNoteAsync(TicketNotaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AddNote", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdTicket <= 0)
                throw new InvalidOperationException("No se recibió el ticket.");
            if (string.IsNullOrWhiteSpace(request.Texto))
                throw new InvalidOperationException("La nota no puede estar vacía.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            await InsertActivityAsync(cn, (SqlTransaction)tx, request.IdTicket, "NOTA", request.Texto.Trim(), NormalizeUser(request.UsuarioAccion), token);
            await tx.CommitAsync(token);
        }, "No se pudo agregar la nota al ticket.", ct);

    public Task<TicketViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);
            var sql = $"""
                SELECT TOP (1) ISNULL(VALOR, ''), ISNULL({detailColumn}, '')
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

            return NormalizeViewSettings(JsonSerializer.Deserialize<TicketViewSettingsDto>(raw, JsonOptions));
        }, "No se pudo cargar la configuración de vista de tickets.", ct);

    public Task SaveViewSettingsAsync(string userName, TicketViewSettingsDto settings, CancellationToken ct = default)
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
                SET VALOR = @Valor,
                    {detailColumn} = @ValorAux,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE, VALOR, {detailColumn}, GRUPO,
                        FechaHora_Grabacion, FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave, @Valor, @ValorAux, @Grupo,
                        GETDATE(), GETDATE()
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
        }, "No se pudo guardar la configuración de vista de tickets.", ct);

    private Task<TicketDetailDto?> GetDetailAsync(string predicate, Action<SqlCommand> configure, CancellationToken ct)
        => ExecuteLoggedAsync(ModuleName, "GetDetail", async token =>
        {
            var sql = $"""
                {TicketListSelectSql()}
                WHERE {predicate};
                """;

            TicketDetailDto? detail = null;
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using (var cmd = new SqlCommand(sql, cn))
            {
                configure(cmd);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                if (await rd.ReadAsync(token))
                {
                    var grid = ReadTicketGridItem(rd);
                    detail = new TicketDetailDto
                    {
                        IdTicket = grid.IdTicket,
                        Numero = grid.Numero,
                        CodigoVisible = grid.CodigoVisible,
                        Titulo = grid.Titulo,
                        CodigoEstado = grid.CodigoEstado,
                        EstadoNombre = grid.EstadoNombre,
                        EstadoColor = grid.EstadoColor,
                        EstadoOrden = grid.EstadoOrden,
                        Prioridad = grid.Prioridad,
                        IdTecnico = grid.IdTecnico,
                        TecnicoNombre = grid.TecnicoNombre,
                        ClienteCodigo = grid.ClienteCodigo,
                        ClienteNombre = grid.ClienteNombre,
                        IdContacto = grid.IdContacto,
                        ContactoNombre = grid.ContactoNombre,
                        IdConversacion = grid.IdConversacion,
                        MensajesOrigen = grid.MensajesOrigen,
                        FechaHoraAlta = grid.FechaHoraAlta,
                        FechaHoraModificacion = grid.FechaHoraModificacion,
                        FechaHoraCierre = grid.FechaHoraCierre,
                        UsuarioAlta = GetString(rd, 20),
                        Descripcion = GetString(rd, 21)
                    };
                }
            }

            if (detail is null)
                return null;

            await HydrateEtiquetasAsync(cn, new List<TicketGridItemDto> { detail }, token);
            detail.MensajesOrigenDetalle = (await GetSourceMessagesAsync(cn, detail.IdTicket, token)).ToList();
            detail.Actividad = (await GetActivityAsync(cn, detail.IdTicket, token)).ToList();
            return detail;
        }, "No se pudo cargar el ticket seleccionado.", ct);

    private static string TicketListSelectSql()
        => $"""
            SELECT
                t.IdTicket,
                t.Numero,
                RIGHT('0000' + CONVERT(varchar(10), t.Numero), 4),
                ISNULL(t.Titulo, ''),
                ISNULL(t.CodigoEstado, ''),
                ISNULL(e.Nombre, ''),
                ISNULL(e.Color, ''),
                ISNULL(e.Orden, 0),
                ISNULL(t.Prioridad, 0),
                ISNULL(t.IdTecnico, ''),
                ISNULL(tec.Nombre, ''),
                ISNULL(t.ClienteCodigo, ''),
                ISNULL(cli.RAZON_SOCIAL, ''),
                t.IdContacto,
                ISNULL(mc.Nombre_y_Apellido, ''),
                t.IdConversacion,
                ISNULL(msg.CantidadMensajes, 0),
                t.FechaHoraAlta,
                t.FechaHoraModificacion,
                t.FechaHoraCierre,
                ISNULL(t.UsuarioAlta, ''),
                ISNULL(CAST(t.Descripcion AS nvarchar(max)), '')
            {TicketBaseFromSql()}
            """;

    private static string TicketBaseFromSql()
        => """
            FROM dbo.TICK_TICKETS t
            INNER JOIN dbo.TICK_ESTADOS e ON e.CodigoEstado = t.CodigoEstado
            LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = t.ClienteCodigo
            LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = t.IdContacto
            LEFT JOIN dbo.V_TA_Tecnicos tec ON tec.IdTecnico = t.IdTecnico
            OUTER APPLY (
                SELECT COUNT(*) AS CantidadMensajes
                FROM dbo.TICK_TICKET_MENSAJES tm
                WHERE tm.IdTicket = t.IdTicket
            ) msg
            """;

    private static string TicketWhereSql()
        => """
            WHERE
                ISNULL(t.Baja, 0) = 0
                AND (@CodigoEstado IS NULL OR t.CodigoEstado = @CodigoEstado)
                AND (@IdTecnico IS NULL OR t.IdTecnico = @IdTecnico)
                AND (@Prioridad IS NULL OR t.Prioridad = @Prioridad)
                AND (@IncluirCerrados = 1 OR ISNULL(e.EsCerrado, 0) = 0)
                AND (
                    @Texto = ''
                    OR t.Titulo LIKE '%' + @Texto + '%'
                    OR ISNULL(CAST(t.Descripcion AS nvarchar(max)), '') LIKE '%' + @Texto + '%'
                    OR CONVERT(varchar(10), t.Numero) = @Texto
                    OR cli.RAZON_SOCIAL LIKE '%' + @Texto + '%'
                    OR mc.Nombre_y_Apellido LIKE '%' + @Texto + '%'
                    OR tec.Nombre LIKE '%' + @Texto + '%'
                )
            """;

    private static TicketGridItemDto ReadTicketGridItem(SqlDataReader rd)
        => new()
        {
            IdTicket = rd.GetInt64(0),
            Numero = GetInt(rd, 1),
            CodigoVisible = $"#{GetInt(rd, 1).ToString("D2", CultureInfo.InvariantCulture)}",
            Titulo = GetString(rd, 3),
            CodigoEstado = GetString(rd, 4),
            EstadoNombre = GetString(rd, 5),
            EstadoColor = GetString(rd, 6),
            EstadoOrden = GetInt(rd, 7),
            Prioridad = GetInt(rd, 8),
            IdTecnico = GetString(rd, 9),
            TecnicoNombre = GetString(rd, 10),
            ClienteCodigo = GetString(rd, 11),
            ClienteNombre = GetString(rd, 12),
            IdContacto = rd.IsDBNull(13) ? null : rd.GetInt32(13),
            ContactoNombre = GetString(rd, 14),
            IdConversacion = rd.IsDBNull(15) ? null : rd.GetInt64(15),
            MensajesOrigen = GetInt(rd, 16),
            FechaHoraAlta = rd.IsDBNull(17) ? DateTime.MinValue : rd.GetDateTime(17),
            FechaHoraModificacion = rd.IsDBNull(18) ? null : rd.GetDateTime(18),
            FechaHoraCierre = rd.IsDBNull(19) ? null : rd.GetDateTime(19)
        };

    private static void AddFilterParameters(SqlCommand cmd, TicketsFilters filters)
    {
        cmd.Parameters.AddWithValue("@Texto", filters.Texto?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("@CodigoEstado", DbNullable(filters.CodigoEstado));
        cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(filters.IdTecnico));
        cmd.Parameters.AddWithValue("@Prioridad", filters.Prioridad.HasValue ? filters.Prioridad.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@IncluirCerrados", filters.IncluirCerrados);
    }

    private static async Task HydrateEtiquetasAsync(SqlConnection cn, IReadOnlyList<TicketGridItemDto> tickets, CancellationToken ct)
    {
        if (tickets.Count == 0)
            return;

        var ids = tickets.Select(x => x.IdTicket).Distinct().ToList();
        var parameterNames = ids.Select((_, idx) => $"@Id{idx}").ToArray();
        var sql = $"""
            SELECT te.IdTicket, e.IdEtiqueta, e.Nombre, ISNULL(e.Color, '')
            FROM dbo.TICK_TICKET_ETIQUETAS te
            INNER JOIN dbo.TICK_ETIQUETAS e ON e.IdEtiqueta = te.IdEtiqueta
            WHERE te.IdTicket IN ({string.Join(", ", parameterNames)})
            ORDER BY e.Nombre;
            """;

        var byTicket = tickets.ToDictionary(x => x.IdTicket);
        await using var cmd = new SqlCommand(sql, cn);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(parameterNames[i], ids[i]);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var idTicket = rd.GetInt64(0);
            if (!byTicket.TryGetValue(idTicket, out var ticket))
                continue;

            ticket.Etiquetas.Add(new TicketEtiquetaDto
            {
                IdEtiqueta = GetInt(rd, 1),
                Nombre = GetString(rd, 2),
                Color = GetString(rd, 3)
            });
        }
    }

    private static async Task<IReadOnlyList<TicketMensajeOrigenDto>> GetSourceMessagesAsync(SqlConnection cn, long idTicket, CancellationToken ct)
    {
        const string sql = """
            SELECT
                m.IdMensaje,
                m.IdConversacion,
                ISNULL(m.Direction, ''),
                ISNULL(m.MessageType, ''),
                ISNULL(m.Texto, ''),
                m.FechaHora,
                ISNULL(m.UsuarioAutor, ''),
                ISNULL(t.Nombre, '')
            FROM dbo.TICK_TICKET_MENSAJES tm
            INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = tm.IdMensaje
            LEFT JOIN dbo.V_TA_Tecnicos t ON t.IdTecnico = m.IdTecnicoAutor
            WHERE tm.IdTicket = @IdTicket
            ORDER BY tm.Orden, m.FechaHora, m.IdMensaje;

            SELECT
                a.IdAdjunto,
                a.IdMensaje,
                ISNULL(a.TipoArchivo, ''),
                ISNULL(a.NombreArchivo, ''),
                ISNULL(a.MimeType, ''),
                ISNULL(a.TamanoBytes, 0)
            FROM dbo.TICK_TICKET_MENSAJES tm
            INNER JOIN dbo.CONV_ADJUNTOS a ON a.IdMensaje = tm.IdMensaje
            WHERE tm.IdTicket = @IdTicket
            ORDER BY a.IdAdjunto;
            """;

        var messages = new List<TicketMensajeOrigenDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var direction = GetString(rd, 2);
            var techName = GetString(rd, 7);
            var user = GetString(rd, 6);
            messages.Add(new TicketMensajeOrigenDto
            {
                IdMensaje = rd.GetInt64(0),
                IdConversacion = rd.GetInt64(1),
                Direccion = direction,
                TipoMensaje = GetString(rd, 3),
                Texto = GetString(rd, 4),
                FechaHora = rd.IsDBNull(5) ? DateTime.MinValue : rd.GetDateTime(5),
                Autor = direction == "ENTRANTE" ? "Cliente / Contacto" : FirstNonEmpty(techName, user, "Equipo Alfa")
            });
        }

        if (await rd.NextResultAsync(ct))
        {
            var byMessage = messages.ToDictionary(x => x.IdMensaje);
            while (await rd.ReadAsync(ct))
            {
                var idMensaje = rd.GetInt64(1);
                if (!byMessage.TryGetValue(idMensaje, out var message))
                    continue;

                message.Adjuntos.Add(new TicketAdjuntoOrigenDto
                {
                    IdAdjunto = rd.GetInt64(0),
                    IdMensaje = idMensaje,
                    TipoArchivo = GetString(rd, 2),
                    NombreArchivo = GetString(rd, 3),
                    MimeType = GetString(rd, 4),
                    TamanoBytes = rd.IsDBNull(5) ? 0 : Convert.ToInt64(rd.GetValue(5), CultureInfo.InvariantCulture)
                });
            }
        }

        return messages;
    }

    private static async Task<IReadOnlyList<TicketActividadDto>> GetActivityAsync(SqlConnection cn, long idTicket, CancellationToken ct)
    {
        const string sql = """
            SELECT IdActividad, TipoActividad, ISNULL(Descripcion, ''), ISNULL(Usuario, ''), FechaHora
            FROM dbo.TICK_ACTIVIDAD
            WHERE IdTicket = @IdTicket
            ORDER BY FechaHora DESC, IdActividad DESC;
            """;

        var rows = new List<TicketActividadDto>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            rows.Add(new TicketActividadDto
            {
                IdActividad = rd.GetInt64(0),
                TipoActividad = GetString(rd, 1),
                Descripcion = GetString(rd, 2),
                Usuario = GetString(rd, 3),
                FechaHora = rd.IsDBNull(4) ? DateTime.MinValue : rd.GetDateTime(4)
            });
        }

        return rows;
    }

    private static async Task ApplyConversationDefaultsAsync(SqlConnection cn, SqlTransaction tx, TicketCreateRequest request, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(ClienteCodigo, ''),
                IdContacto,
                ISNULL(IdTecnico, '')
            FROM dbo.CONV_CONVERSACIONES
            WHERE IdConversacion = @IdConversacion;
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion!.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return;

        if (string.IsNullOrWhiteSpace(request.ClienteCodigo))
            request.ClienteCodigo = GetString(rd, 0);
        if (!request.IdContacto.HasValue && !rd.IsDBNull(1))
            request.IdContacto = rd.GetInt32(1);
        if (string.IsNullOrWhiteSpace(request.IdTecnico))
            request.IdTecnico = GetString(rd, 2);
    }

    private static async Task InsertTicketMessagesAsync(SqlConnection cn, SqlTransaction tx, long idTicket, IReadOnlyList<long> idMensajes, CancellationToken ct)
    {
        if (idMensajes.Count == 0)
            return;

        const string sql = """
            INSERT INTO dbo.TICK_TICKET_MENSAJES (IdTicket, IdMensaje, Orden)
            VALUES (@IdTicket, @IdMensaje, @Orden);
            """;

        for (var i = 0; i < idMensajes.Count; i++)
        {
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.AddWithValue("@IdTicket", idTicket);
            cmd.Parameters.AddWithValue("@IdMensaje", idMensajes[i]);
            cmd.Parameters.AddWithValue("@Orden", i);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertActivityAsync(SqlConnection cn, SqlTransaction tx, long idTicket, string type, string description, string user, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.TICK_ACTIVIDAD (IdTicket, TipoActividad, Descripcion, Usuario, FechaHora)
            VALUES (@IdTicket, @TipoActividad, @Descripcion, @Usuario, GETDATE());
            """;

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@IdTicket", idTicket);
        cmd.Parameters.AddWithValue("@TipoActividad", type);
        cmd.Parameters.AddWithValue("@Descripcion", description);
        cmd.Parameters.AddWithValue("@Usuario", user);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static TicketViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = TicketViewGroupKeys.Estado,
            Columnas =
            [
                new() { Key = TicketViewColumnKeys.Numero, Label = "Nro.", Visible = true, Order = 0 },
                new() { Key = TicketViewColumnKeys.Titulo, Label = "Ticket", Visible = true, Order = 1 },
                new() { Key = TicketViewColumnKeys.Prioridad, Label = "Prioridad", Visible = true, Order = 2 },
                new() { Key = TicketViewColumnKeys.Estado, Label = "Etapa", Visible = true, Order = 3 },
                new() { Key = TicketViewColumnKeys.Asignado, Label = "Asignado", Visible = true, Order = 4 },
                new() { Key = TicketViewColumnKeys.Cliente, Label = "Cliente", Visible = true, Order = 5 },
                new() { Key = TicketViewColumnKeys.Contacto, Label = "Contacto", Visible = false, Order = 6 },
                new() { Key = TicketViewColumnKeys.Mensajes, Label = "Mensajes", Visible = true, Order = 7 },
                new() { Key = TicketViewColumnKeys.Fecha, Label = "Fecha", Visible = true, Order = 8 }
            ]
        };

    private static TicketViewSettingsDto NormalizeViewSettings(TicketViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        var group = settings.AgruparPor switch
        {
            TicketViewGroupKeys.None => TicketViewGroupKeys.None,
            TicketViewGroupKeys.Prioridad => TicketViewGroupKeys.Prioridad,
            TicketViewGroupKeys.Tecnico => TicketViewGroupKeys.Tecnico,
            _ => TicketViewGroupKeys.Estado
        };

        var normalized = new TicketViewSettingsDto
        {
            AgruparPor = group,
            Columnas = defaults.Columnas
                .Select(defaultCol =>
                {
                    if (!incoming.TryGetValue(defaultCol.Key, out var source))
                        return new TicketViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = defaultCol.Visible, Order = defaultCol.Order };

                    return new TicketViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order };
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

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var column = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static string ResolveStoredValue(string value, string auxValue)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : auxValue.Trim();

    private static (string Value, string AuxValue) SplitStoredValue(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length > 150 ? (string.Empty, normalized) : (normalized, string.Empty);
    }

    private static string NormalizeTitle(string? value)
    {
        var title = value?.Trim() ?? string.Empty;
        if (title.Length == 0)
            throw new InvalidOperationException("El nombre del ticket es obligatorio.");

        return title.Length <= 180 ? title : title[..180];
    }

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static bool TryBuildKnownSqlMessage(SqlException ex, out string message)
    {
        message = string.Empty;
        if (ex.Number != 208)
            return false;

        var rawMessage = ex.Message ?? string.Empty;
        if (!rawMessage.Contains("TICK_", StringComparison.OrdinalIgnoreCase))
            return false;

        message = "El módulo Tickets de asistencia todavía no está inicializado en la base activa. Ejecutá el script de actualización de tickets y recargá la pantalla.";
        return true;
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
        catch (SqlException ex) when (TryBuildKnownSqlMessage(ex, out var knownMessage))
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(knownMessage, incidentId, ex);
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
        await ExecuteLoggedAsync(module, action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
    }
}

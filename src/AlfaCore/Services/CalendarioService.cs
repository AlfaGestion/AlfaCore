using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

public sealed class CalendarioService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IConversacionesService conversacionesService) : ICalendarioService
{
    private const string ModuleName = "Calendario";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuro la cadena de conexion 'ConnectionStrings:AlfaGestion'.");

    public Task<CalendarioMonthDto> GetMonthAsync(CalendarioMonthRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetMonth", async token =>
        {
            request ??= new CalendarioMonthRequest();
            var monthStart = new DateTime(request.Year, request.Month, 1);
            var gridStart = monthStart.AddDays(-(((int)monthStart.DayOfWeek + 6) % 7));
            var gridEnd = gridStart.AddDays(42);

            var result = new CalendarioMonthDto
            {
                MonthStart = monthStart,
                GridStart = gridStart,
                GridEnd = gridEnd
            };

            var sql = """
                SELECT
                    e.IdEvento,
                    e.Titulo,
                    e.Tipo,
                    e.FechaInicio,
                    e.FechaFin,
                    e.TodoElDia,
                    ISNULL(e.IdTecnico, ''),
                    ISNULL(e.TecnicoNombre, ''),
                    ISNULL(e.TelefonoWhatsApp, ''),
                    e.Estado,
                    ISNULL(e.Color, ''),
                    ISNULL(CAST(e.Descripcion AS nvarchar(max)), ''),
                    ISNULL(r.IdRecordatorio, 0),
                    ISNULL(r.Tipo, ''),
                    ISNULL(r.MinutosAntes, 0),
                    r.IdPlantillaWhatsApp,
                    ISNULL(r.NotificarResponsable, 0),
                    r.FechaHoraProgramada,
                    r.FechaHoraEnvio,
                    ISNULL(r.EstadoEnvio, ''),
                    ISNULL(CAST(r.UltimoError AS nvarchar(max)), '')
                FROM dbo.CAL_EVENTOS e
                LEFT JOIN dbo.CAL_RECORDATORIOS r ON r.IdEvento = e.IdEvento AND ISNULL(r.Baja, 0) = 0
                WHERE ISNULL(e.Baja, 0) = 0
                  AND e.FechaInicio < @GridEnd
                  AND e.FechaFin >= @GridStart
                  AND (@Tipo = 'TODOS' OR e.Tipo = @Tipo)
                  AND (@IdTecnico IS NULL OR e.IdTecnico = @IdTecnico)
                  AND (
                        @Search = ''
                        OR e.Titulo LIKE @SearchLike
                        OR e.TecnicoNombre LIKE @SearchLike
                        OR CAST(e.Descripcion AS nvarchar(max)) LIKE @SearchLike
                      )
                ORDER BY e.FechaInicio, e.TodoElDia DESC, e.Titulo;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@GridStart", gridStart);
            cmd.Parameters.AddWithValue("@GridEnd", gridEnd);
            cmd.Parameters.AddWithValue("@Tipo", string.IsNullOrWhiteSpace(request.Tipo) ? CalendarioEventoTipos.Todos : request.Tipo);
            cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));
            cmd.Parameters.AddWithValue("@Search", request.Search?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@SearchLike", $"%{(request.Search ?? string.Empty).Trim()}%");

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
                result.Eventos.Add(ReadEvento(rd));

            result.Tecnicos = (await conversacionesService.GetTechniciansAsync(token)).ToList();
            result.PlantillasWhatsApp = (await conversacionesService.GetTemplatesAsync(new ConversacionPlantillaFilters
            {
                EstadoMeta = "APPROVED",
                IncluirInactivas = false
            }, token)).ToList();

            return result;
        }, "No se pudo cargar el calendario.", ct);

    public Task<CalendarioEventoDto?> GetByIdAsync(long idEvento, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetById", async token =>
        {
            const string sql = """
                SELECT
                    e.IdEvento,
                    e.Titulo,
                    e.Tipo,
                    e.FechaInicio,
                    e.FechaFin,
                    e.TodoElDia,
                    ISNULL(e.IdTecnico, ''),
                    ISNULL(e.TecnicoNombre, ''),
                    ISNULL(e.TelefonoWhatsApp, ''),
                    e.Estado,
                    ISNULL(e.Color, ''),
                    ISNULL(CAST(e.Descripcion AS nvarchar(max)), ''),
                    ISNULL(r.IdRecordatorio, 0),
                    ISNULL(r.Tipo, ''),
                    ISNULL(r.MinutosAntes, 0),
                    r.IdPlantillaWhatsApp,
                    ISNULL(r.NotificarResponsable, 0),
                    r.FechaHoraProgramada,
                    r.FechaHoraEnvio,
                    ISNULL(r.EstadoEnvio, ''),
                    ISNULL(CAST(r.UltimoError AS nvarchar(max)), '')
                FROM dbo.CAL_EVENTOS e
                LEFT JOIN dbo.CAL_RECORDATORIOS r ON r.IdEvento = e.IdEvento AND ISNULL(r.Baja, 0) = 0
                WHERE e.IdEvento = @IdEvento AND ISNULL(e.Baja, 0) = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdEvento", idEvento);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            return await rd.ReadAsync(token) ? ReadEvento(rd) : null;
        }, "No se pudo cargar el evento.", ct);

    public Task<long> SaveAsync(CalendarioEventoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            Validate(request);
            var isNew = request.IdEvento <= 0;
            var color = FirstNonEmpty(request.Color, ResolveColor(request.Tipo));
            var tecnicoNombre = FirstNonEmpty(request.TecnicoNombre, request.IdTecnico);
            var user = NormalizeUser(request.UsuarioAccion);

            const string insertSql = """
                INSERT INTO dbo.CAL_EVENTOS
                (
                    Titulo, Tipo, FechaInicio, FechaFin, TodoElDia, IdTecnico, TecnicoNombre,
                    TelefonoWhatsApp, Estado, Color, Descripcion, UsuarioAlta,
                    FechaHora_Grabacion, FechaHora_Modificacion
                )
                OUTPUT INSERTED.IdEvento
                VALUES
                (
                    @Titulo, @Tipo, @FechaInicio, @FechaFin, @TodoElDia, @IdTecnico, @TecnicoNombre,
                    @TelefonoWhatsApp, @Estado, @Color, @Descripcion, @Usuario,
                    GETDATE(), GETDATE()
                );
                """;

            const string updateSql = """
                UPDATE dbo.CAL_EVENTOS
                   SET Titulo = @Titulo,
                       Tipo = @Tipo,
                       FechaInicio = @FechaInicio,
                       FechaFin = @FechaFin,
                       TodoElDia = @TodoElDia,
                       IdTecnico = @IdTecnico,
                       TecnicoNombre = @TecnicoNombre,
                       TelefonoWhatsApp = @TelefonoWhatsApp,
                       Estado = @Estado,
                       Color = @Color,
                       Descripcion = @Descripcion,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            long idEvento;

            if (isNew)
            {
                await using var cmd = new SqlCommand(insertSql, cn);
                AddSaveParameters(cmd, request, color, tecnicoNombre, user);
                idEvento = Convert.ToInt64(await cmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
            }
            else
            {
                await using var cmd = new SqlCommand(updateSql, cn);
                AddSaveParameters(cmd, request, color, tecnicoNombre, user);
                cmd.Parameters.AddWithValue("@IdEvento", request.IdEvento);
                var affected = await cmd.ExecuteNonQueryAsync(token);
                if (affected == 0)
                    throw new InvalidOperationException("El evento indicado no existe o fue dado de baja.");
                idEvento = request.IdEvento;
            }

            await UpsertReminderAsync(cn, idEvento, request, token);
            await appEvents.LogAuditAsync(
                ModuleName,
                "EventoGuardado",
                "CAL_EVENTOS",
                idEvento.ToString(CultureInfo.InvariantCulture),
                $"Evento de calendario guardado: {request.Titulo}",
                new { request.Tipo, request.FechaInicio, request.FechaFin, request.IdTecnico },
                token);

            return idEvento;
        }, "No se pudo guardar el evento.", ct);

    public Task DeleteAsync(long idEvento, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Delete", async token =>
        {
            const string sql = """
                UPDATE dbo.CAL_EVENTOS
                   SET Baja = 1,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;

                UPDATE dbo.CAL_RECORDATORIOS
                   SET Baja = 1,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdEvento", idEvento);
            await cmd.ExecuteNonQueryAsync(token);
        }, "No se pudo eliminar el evento.", ct);

    public Task<CalendarioRecordatorioSendResult> SendWhatsAppReminderAsync(long idRecordatorio, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SendWhatsAppReminder", async token =>
        {
            var detail = await GetReminderDetailAsync(idRecordatorio, token)
                ?? throw new InvalidOperationException("El recordatorio indicado no existe.");

            if (detail.IdPlantillaWhatsApp is null or <= 0)
                throw new InvalidOperationException("El recordatorio no tiene plantilla de WhatsApp configurada.");
            if (string.IsNullOrWhiteSpace(detail.TelefonoWhatsApp))
                throw new InvalidOperationException("El evento no tiene telefono WhatsApp para enviar el recordatorio.");

            var conv = await conversacionesService.CreateOrGetWhatsAppConversationAsync(new ConversacionCrearWhatsAppRequest
            {
                TelefonoWhatsApp = detail.TelefonoWhatsApp,
                IdTecnico = detail.IdTecnico,
                UsuarioAccion = NormalizeUser(usuarioAccion),
                SistemaAccion = ModuleName
            }, token);

            var message = await conversacionesService.SendTemplateMessageAsync(new ConversacionPlantillaSendRequest
            {
                IdConversacion = conv.IdConversacion,
                IdPlantilla = detail.IdPlantillaWhatsApp.Value,
                IdTecnicoAutor = detail.IdTecnico,
                UsuarioAccion = NormalizeUser(usuarioAccion),
                SistemaAccion = ModuleName,
                ValoresVariables =
                [
                    FirstNonEmpty(detail.TecnicoNombre, detail.Titulo),
                    FormatDate(detail.FechaInicio),
                    FormatDate(detail.FechaFin)
                ]
            }, token);

            await MarkReminderSentAsync(idRecordatorio, message.EstadoEnvio, token);
            return new CalendarioRecordatorioSendResult
            {
                IdRecordatorio = idRecordatorio,
                IdConversacion = conv.IdConversacion,
                IdMensaje = message.IdMensaje,
                EstadoEnvio = message.EstadoEnvio
            };
        }, "No se pudo enviar el recordatorio por WhatsApp.", ct);

    private static CalendarioEventoDto ReadEvento(SqlDataReader rd)
    {
        var item = new CalendarioEventoDto
        {
            IdEvento = GetLong(rd, 0),
            Titulo = GetString(rd, 1),
            Tipo = GetString(rd, 2),
            FechaInicio = GetDateTime(rd, 3),
            FechaFin = GetDateTime(rd, 4),
            TodoElDia = GetBool(rd, 5),
            IdTecnico = GetString(rd, 6),
            TecnicoNombre = GetString(rd, 7),
            TelefonoWhatsApp = GetString(rd, 8),
            Estado = GetString(rd, 9),
            Color = GetString(rd, 10),
            Descripcion = GetString(rd, 11)
        };

        var idRecordatorio = GetLong(rd, 12);
        if (idRecordatorio > 0)
        {
            item.Recordatorio = new CalendarioRecordatorioDto
            {
                IdRecordatorio = idRecordatorio,
                Tipo = GetString(rd, 13),
                MinutosAntes = GetInt(rd, 14),
                IdPlantillaWhatsApp = rd.IsDBNull(15) ? null : Convert.ToInt64(rd.GetValue(15), CultureInfo.InvariantCulture),
                NotificarResponsable = GetBool(rd, 16),
                FechaHoraProgramada = GetNullableDateTime(rd, 17),
                FechaHoraEnvio = GetNullableDateTime(rd, 18),
                EstadoEnvio = GetString(rd, 19),
                UltimoError = GetString(rd, 20)
            };
        }

        return item;
    }

    private async Task UpsertReminderAsync(SqlConnection cn, long idEvento, CalendarioEventoSaveRequest request, CancellationToken ct)
    {
        const string clearSql = """
            UPDATE dbo.CAL_RECORDATORIOS
               SET Baja = 1,
                   FechaHora_Modificacion = GETDATE()
             WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;
            """;

        await using (var clear = new SqlCommand(clearSql, cn))
        {
            clear.Parameters.AddWithValue("@IdEvento", idEvento);
            await clear.ExecuteNonQueryAsync(ct);
        }

        if (!request.CrearRecordatorioWhatsApp)
            return;

        const string insertSql = """
            INSERT INTO dbo.CAL_RECORDATORIOS
            (
                IdEvento, Tipo, MinutosAntes, IdPlantillaWhatsApp, NotificarResponsable,
                FechaHoraProgramada, EstadoEnvio, FechaHora_Grabacion, FechaHora_Modificacion
            )
            VALUES
            (
                @IdEvento, 'WHATSAPP', @MinutosAntes, @IdPlantillaWhatsApp, 1,
                DATEADD(minute, -@MinutosAntes, @FechaInicio), 'PENDIENTE', GETDATE(), GETDATE()
            );
            """;

        await using var cmd = new SqlCommand(insertSql, cn);
        cmd.Parameters.AddWithValue("@IdEvento", idEvento);
        cmd.Parameters.AddWithValue("@MinutosAntes", Math.Max(0, request.MinutosAntesRecordatorio));
        cmd.Parameters.AddWithValue("@IdPlantillaWhatsApp", request.IdPlantillaWhatsApp.HasValue ? request.IdPlantillaWhatsApp.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaInicio", request.FechaInicio);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<ReminderSendDetail?> GetReminderDetailAsync(long idRecordatorio, CancellationToken ct)
    {
        const string sql = """
            SELECT
                r.IdRecordatorio,
                r.IdPlantillaWhatsApp,
                e.IdEvento,
                e.Titulo,
                e.FechaInicio,
                e.FechaFin,
                ISNULL(e.IdTecnico, ''),
                ISNULL(e.TecnicoNombre, ''),
                ISNULL(e.TelefonoWhatsApp, '')
            FROM dbo.CAL_RECORDATORIOS r
            INNER JOIN dbo.CAL_EVENTOS e ON e.IdEvento = r.IdEvento
            WHERE r.IdRecordatorio = @IdRecordatorio
              AND ISNULL(r.Baja, 0) = 0
              AND ISNULL(e.Baja, 0) = 0;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdRecordatorio", idRecordatorio);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new ReminderSendDetail
        {
            IdRecordatorio = GetLong(rd, 0),
            IdPlantillaWhatsApp = rd.IsDBNull(1) ? null : Convert.ToInt64(rd.GetValue(1), CultureInfo.InvariantCulture),
            IdEvento = GetLong(rd, 2),
            Titulo = GetString(rd, 3),
            FechaInicio = GetDateTime(rd, 4),
            FechaFin = GetDateTime(rd, 5),
            IdTecnico = GetString(rd, 6),
            TecnicoNombre = GetString(rd, 7),
            TelefonoWhatsApp = GetString(rd, 8)
        };
    }

    private async Task MarkReminderSentAsync(long idRecordatorio, string estadoEnvio, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CAL_RECORDATORIOS
               SET EstadoEnvio = @EstadoEnvio,
                   FechaHoraEnvio = GETDATE(),
                   UltimoError = NULL,
                   FechaHora_Modificacion = GETDATE()
             WHERE IdRecordatorio = @IdRecordatorio;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdRecordatorio", idRecordatorio);
        cmd.Parameters.AddWithValue("@EstadoEnvio", FirstNonEmpty(estadoEnvio, CalendarioRecordatorioEstados.Enviado));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddSaveParameters(SqlCommand cmd, CalendarioEventoSaveRequest request, string color, string tecnicoNombre, string user)
    {
        cmd.Parameters.AddWithValue("@Titulo", request.Titulo.Trim());
        cmd.Parameters.AddWithValue("@Tipo", request.Tipo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@FechaInicio", request.FechaInicio);
        cmd.Parameters.AddWithValue("@FechaFin", request.FechaFin);
        cmd.Parameters.AddWithValue("@TodoElDia", request.TodoElDia);
        cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));
        cmd.Parameters.AddWithValue("@TecnicoNombre", DbNullable(tecnicoNombre));
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", DbNullable(request.TelefonoWhatsApp));
        cmd.Parameters.AddWithValue("@Estado", request.Estado.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Color", DbNullable(color));
        cmd.Parameters.AddWithValue("@Descripcion", DbNullable(request.Descripcion));
        cmd.Parameters.AddWithValue("@Usuario", user);
    }

    private static void Validate(CalendarioEventoSaveRequest request)
    {
        if (request is null)
            throw new InvalidOperationException("El evento es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Titulo))
            throw new InvalidOperationException("El titulo del evento es obligatorio.");
        if (request.FechaFin < request.FechaInicio)
            throw new InvalidOperationException("La fecha de fin no puede ser anterior al inicio.");
    }

    private static string ResolveColor(string? tipo)
        => (tipo ?? string.Empty).ToUpperInvariant() switch
        {
            CalendarioEventoTipos.Guardia => "#14b8a6",
            CalendarioEventoTipos.Reunion => "#38bdf8",
            CalendarioEventoTipos.Capacitacion => "#f59e0b",
            _ => "#a78bfa"
        };

    private static string FormatDate(DateTime value)
        => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR"));

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static long GetLong(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt64(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static DateTime GetDateTime(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? DateTime.MinValue : Convert.ToDateTime(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static DateTime? GetNullableDateTime(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? null : Convert.ToDateTime(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static bool TryBuildKnownSqlMessage(SqlException ex, out string message)
    {
        message = string.Empty;
        if (ex.Number != 208)
            return false;

        if (!ex.Message.Contains("CAL_", StringComparison.OrdinalIgnoreCase))
            return false;

        message = "El modulo Calendario todavia no esta inicializado en la base activa. Ejecuta el script de actualizacion de calendario y recarga la pantalla.";
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

    private sealed class ReminderSendDetail
    {
        public long IdRecordatorio { get; init; }
        public long? IdPlantillaWhatsApp { get; init; }
        public long IdEvento { get; init; }
        public string Titulo { get; init; } = string.Empty;
        public DateTime FechaInicio { get; init; }
        public DateTime FechaFin { get; init; }
        public string IdTecnico { get; init; } = string.Empty;
        public string TecnicoNombre { get; init; } = string.Empty;
        public string TelefonoWhatsApp { get; init; } = string.Empty;
    }
}

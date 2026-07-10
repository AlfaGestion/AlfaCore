using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Security.Cryptography;

namespace AlfaCore.Services;

public sealed class ReunionesPublicasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IReunionesPublicasService
{
    private const string ModuleName = "CalendarioReuniones";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuro la cadena de conexion 'ConnectionStrings:AlfaGestion'.");

    public Task<ReunionPublicaMesDto> GetPublicMonthAsync(ReunionPublicaMesRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetPublicMonth", async token =>
        {
            request ??= new ReunionPublicaMesRequest();
            var monthStart = new DateTime(request.Year, request.Month, 1);
            var gridStart = monthStart.AddDays(-(((int)monthStart.DayOfWeek + 6) % 7));
            var gridEnd = gridStart.AddDays(42);

            await using var cn = new SqlConnection(ConnectionString);
            var tipo = await GetTipoBySlugAsync(cn, request.Slug, token);
            var result = new ReunionPublicaMesDto
            {
                Tipo = tipo,
                MonthStart = monthStart,
                GridStart = gridStart,
                GridEnd = gridEnd
            };

            if (tipo is null)
                return result;

            var busy = await GetBusyRangesAsync(cn, tipo, gridStart, gridEnd, token);
            for (var day = gridStart.Date; day < gridEnd.Date; day = day.AddDays(1))
            {
                result.Dias.Add(BuildDay(tipo, day, busy));
            }

            return result;
        }, "No se pudo cargar la agenda publica.", ct);

    public Task<ReunionPublicaReservaResult> CreateReservationAsync(ReunionPublicaReservaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("CreateReservation", async token =>
        {
            ValidateReservation(request);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var dbTx = (SqlTransaction)tx;

            var tipo = await GetTipoByIdAsync(cn, dbTx, request.IdTipoReunion, token)
                ?? throw new InvalidOperationException("La capacitacion seleccionada ya no esta disponible.");

            var fechaFin = request.FechaInicio.AddMinutes(tipo.DuracionMinutos);
            if (!IsSlotInsideRules(tipo, request.FechaInicio, fechaFin, DateTime.Now))
                throw new InvalidOperationException("El horario seleccionado ya no esta disponible.");

            var overlaps = await cn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(1)
                FROM dbo.CAL_EVENTOS e
                WHERE ISNULL(e.Baja, 0) = 0
                  AND e.Estado <> N'CANCELADO'
                  AND e.FechaInicio < @FechaFin
                  AND e.FechaFin > @FechaInicio
                  AND (@IdTecnico = N'' OR ISNULL(e.IdTecnico, N'') = @IdTecnico);
                """,
                new { request.FechaInicio, FechaFin = fechaFin, tipo.IdTecnico },
                dbTx);

            if (overlaps > 0)
                throw new InvalidOperationException("Ese horario acaba de ser reservado. Elegi otro horario disponible.");

            var tituloEvento = $"{tipo.Titulo} - {FirstNonEmpty(request.RazonSocial, request.ClienteNombre)}";
            var descripcion = BuildReservationDescription(request);
            var idEvento = await cn.ExecuteScalarAsync<long>(
                """
                INSERT INTO dbo.CAL_EVENTOS
                (
                    Titulo, Tipo, FechaInicio, FechaFin, TodoElDia, IdTecnico, TecnicoNombre,
                    TelefonoWhatsApp, Estado, Color, Descripcion, UsuarioAlta,
                    FechaHora_Grabacion, FechaHora_Modificacion
                )
                OUTPUT INSERTED.IdEvento
                VALUES
                (
                    @Titulo, N'CAPACITACION', @FechaInicio, @FechaFin, 0, @IdTecnico, @TecnicoNombre,
                    @TelefonoWhatsApp, N'CONFIRMADO', @Color, @Descripcion, N'ReunionesPublicas',
                    GETDATE(), GETDATE()
                );
                """,
                new
                {
                    Titulo = tituloEvento,
                    request.FechaInicio,
                    FechaFin = fechaFin,
                    tipo.IdTecnico,
                    tipo.TecnicoNombre,
                    tipo.TelefonoWhatsApp,
                    tipo.Color,
                    Descripcion = descripcion
                },
                dbTx);

            var tokenReserva = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
            var idReserva = await cn.ExecuteScalarAsync<long>(
                """
                INSERT INTO dbo.CAL_RESERVAS_REUNION
                (
                    IdTipoReunion, IdEvento, FechaInicio, FechaFin, ClienteNombre, RazonSocial,
                    Email, Telefono, TipoCapacitacion, Observaciones, Estado, TokenReserva,
                    FechaHora_Grabacion, FechaHora_Modificacion
                )
                OUTPUT INSERTED.IdReserva
                VALUES
                (
                    @IdTipoReunion, @IdEvento, @FechaInicio, @FechaFin, @ClienteNombre, @RazonSocial,
                    @Email, @Telefono, @TipoCapacitacion, @Observaciones, N'CONFIRMADA', @TokenReserva,
                    GETDATE(), GETDATE()
                );
                """,
                new
                {
                    request.IdTipoReunion,
                    IdEvento = idEvento,
                    request.FechaInicio,
                    FechaFin = fechaFin,
                    ClienteNombre = request.ClienteNombre.Trim(),
                    RazonSocial = request.RazonSocial.Trim(),
                    Email = request.Email.Trim(),
                    Telefono = request.Telefono.Trim(),
                    TipoCapacitacion = request.TipoCapacitacion.Trim(),
                    Observaciones = EmptyToNull(request.Observaciones),
                    TokenReserva = tokenReserva
                },
                dbTx);

            await tx.CommitAsync(token);
            await appEvents.LogAuditAsync(ModuleName, "ReservaCreada", "CAL_RESERVAS_REUNION", idReserva.ToString(CultureInfo.InvariantCulture), tituloEvento, new { idEvento, request.Email }, token);

            return new ReunionPublicaReservaResult
            {
                IdReserva = idReserva,
                IdEvento = idEvento,
                FechaInicio = request.FechaInicio,
                FechaFin = fechaFin,
                Titulo = tipo.Titulo,
                TecnicoNombre = tipo.TecnicoNombre,
                Modalidad = tipo.Modalidad
            };
        }, "No se pudo confirmar la reserva.", ct);

    public Task<ReunionPublicaAdminDto> GetAdminAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetAdmin", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var multi = await cn.QueryMultipleAsync(
                """
                SELECT IdTipoReunion, Slug, Titulo, ISNULL(CAST(Descripcion AS nvarchar(max)), '') AS Descripcion,
                       DuracionMinutos, Modalidad, ISNULL(EnlaceVideollamada, '') AS EnlaceVideollamada,
                       ISNULL(IdTecnico, '') AS IdTecnico, ISNULL(TecnicoNombre, '') AS TecnicoNombre,
                       ISNULL(TelefonoWhatsApp, '') AS TelefonoWhatsApp, ISNULL(EmailOperador, '') AS EmailOperador,
                       DiasDisponibles, HoraDesde, HoraHasta, ReservasHastaDias, AnticipacionMinHoras,
                       Activo, ISNULL(Color, '#22d3ee') AS Color
                FROM dbo.CAL_TIPOS_REUNION
                WHERE ISNULL(Baja, 0) = 0
                ORDER BY Activo DESC, Titulo;

                SELECT TOP 80 r.IdReserva, r.IdEvento, t.Titulo, r.FechaInicio, r.FechaFin,
                       r.ClienteNombre, r.RazonSocial, r.Email, r.Telefono, r.TipoCapacitacion,
                       ISNULL(CAST(r.Observaciones AS nvarchar(max)), '') AS Observaciones,
                       r.Estado, ISNULL(t.TecnicoNombre, '') AS TecnicoNombre
                FROM dbo.CAL_RESERVAS_REUNION r
                INNER JOIN dbo.CAL_TIPOS_REUNION t ON t.IdTipoReunion = r.IdTipoReunion
                WHERE ISNULL(r.Baja, 0) = 0
                ORDER BY r.FechaInicio DESC;
                """);

            return new ReunionPublicaAdminDto
            {
                Tipos = (await multi.ReadAsync<ReunionPublicaTipoDto>()).ToList(),
                Reservas = (await multi.ReadAsync<ReunionPublicaReservaAdminDto>()).ToList()
            };
        }, "No se pudo cargar la administracion de reuniones.", ct);

    public Task<long> SaveTipoAsync(ReunionPublicaTipoDto tipo, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveTipo", async token =>
        {
            ValidateTipo(tipo);
            await using var cn = new SqlConnection(ConnectionString);
            var parameters = new
            {
                tipo.IdTipoReunion,
                Slug = Slugify(tipo.Slug),
                Titulo = tipo.Titulo.Trim(),
                Descripcion = EmptyToNull(tipo.Descripcion),
                tipo.DuracionMinutos,
                Modalidad = FirstNonEmpty(tipo.Modalidad, "En linea"),
                EnlaceVideollamada = EmptyToNull(tipo.EnlaceVideollamada),
                IdTecnico = EmptyToNull(tipo.IdTecnico),
                TecnicoNombre = EmptyToNull(tipo.TecnicoNombre),
                TelefonoWhatsApp = EmptyToNull(tipo.TelefonoWhatsApp),
                EmailOperador = EmptyToNull(tipo.EmailOperador),
                DiasDisponibles = NormalizeDays(tipo.DiasDisponibles),
                tipo.HoraDesde,
                tipo.HoraHasta,
                ReservasHastaDias = Math.Clamp(tipo.ReservasHastaDias, 1, 180),
                AnticipacionMinHoras = Math.Clamp(tipo.AnticipacionMinHoras, 0, 720),
                tipo.Activo,
                Color = FirstNonEmpty(tipo.Color, "#22d3ee")
            };

            long id;
            if (tipo.IdTipoReunion > 0)
            {
                await cn.ExecuteAsync(
                    """
                    UPDATE dbo.CAL_TIPOS_REUNION
                       SET Slug = @Slug, Titulo = @Titulo, Descripcion = @Descripcion,
                           DuracionMinutos = @DuracionMinutos, Modalidad = @Modalidad,
                           EnlaceVideollamada = @EnlaceVideollamada, IdTecnico = @IdTecnico,
                           TecnicoNombre = @TecnicoNombre, TelefonoWhatsApp = @TelefonoWhatsApp,
                           EmailOperador = @EmailOperador, DiasDisponibles = @DiasDisponibles,
                           HoraDesde = @HoraDesde, HoraHasta = @HoraHasta,
                           ReservasHastaDias = @ReservasHastaDias, AnticipacionMinHoras = @AnticipacionMinHoras,
                           Activo = @Activo, Color = @Color, FechaHora_Modificacion = GETDATE()
                     WHERE IdTipoReunion = @IdTipoReunion AND ISNULL(Baja, 0) = 0;
                    """,
                    parameters);
                id = tipo.IdTipoReunion;
            }
            else
            {
                id = await cn.ExecuteScalarAsync<long>(
                    """
                    INSERT INTO dbo.CAL_TIPOS_REUNION
                    (
                        Slug, Titulo, Descripcion, DuracionMinutos, Modalidad, EnlaceVideollamada,
                        IdTecnico, TecnicoNombre, TelefonoWhatsApp, EmailOperador, DiasDisponibles,
                        HoraDesde, HoraHasta, ReservasHastaDias, AnticipacionMinHoras, Activo, Color,
                        FechaHora_Grabacion, FechaHora_Modificacion
                    )
                    OUTPUT INSERTED.IdTipoReunion
                    VALUES
                    (
                        @Slug, @Titulo, @Descripcion, @DuracionMinutos, @Modalidad, @EnlaceVideollamada,
                        @IdTecnico, @TecnicoNombre, @TelefonoWhatsApp, @EmailOperador, @DiasDisponibles,
                        @HoraDesde, @HoraHasta, @ReservasHastaDias, @AnticipacionMinHoras, @Activo, @Color,
                        GETDATE(), GETDATE()
                    );
                    """,
                    parameters);
            }

            await appEvents.LogAuditAsync(ModuleName, "TipoGuardado", "CAL_TIPOS_REUNION", id.ToString(CultureInfo.InvariantCulture), tipo.Titulo, null, token);
            return id;
        }, "No se pudo guardar el tipo de reunion.", ct);

    public Task CancelReservationAsync(long idReserva, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("CancelReservation", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(
                """
                DECLARE @IdEvento bigint;
                SELECT @IdEvento = IdEvento
                FROM dbo.CAL_RESERVAS_REUNION
                WHERE IdReserva = @IdReserva AND ISNULL(Baja, 0) = 0;

                UPDATE dbo.CAL_RESERVAS_REUNION
                   SET Estado = N'CANCELADA', Baja = 1, FechaHora_Modificacion = GETDATE()
                 WHERE IdReserva = @IdReserva;

                UPDATE dbo.CAL_EVENTOS
                   SET Estado = N'CANCELADO', Baja = 1, FechaHora_Modificacion = GETDATE()
                 WHERE IdEvento = @IdEvento;
                """,
                new { IdReserva = idReserva });
            await appEvents.LogAuditAsync(ModuleName, "ReservaCancelada", "CAL_RESERVAS_REUNION", idReserva.ToString(CultureInfo.InvariantCulture), "Reserva cancelada", null, token);
        }, "No se pudo cancelar la reserva.", ct);

    private static async Task<ReunionPublicaTipoDto?> GetTipoBySlugAsync(SqlConnection cn, string? slug, CancellationToken ct)
    {
        return await cn.QueryFirstOrDefaultAsync<ReunionPublicaTipoDto>(
            """
            SELECT TOP 1 IdTipoReunion, Slug, Titulo, ISNULL(CAST(Descripcion AS nvarchar(max)), '') AS Descripcion,
                   DuracionMinutos, Modalidad, ISNULL(EnlaceVideollamada, '') AS EnlaceVideollamada,
                   ISNULL(IdTecnico, '') AS IdTecnico, ISNULL(TecnicoNombre, '') AS TecnicoNombre,
                   ISNULL(TelefonoWhatsApp, '') AS TelefonoWhatsApp, ISNULL(EmailOperador, '') AS EmailOperador,
                   DiasDisponibles, HoraDesde, HoraHasta, ReservasHastaDias, AnticipacionMinHoras,
                   Activo, ISNULL(Color, '#22d3ee') AS Color
            FROM dbo.CAL_TIPOS_REUNION
            WHERE Slug = @Slug AND Activo = 1 AND ISNULL(Baja, 0) = 0;
            """,
            new { Slug = Slugify(slug) });
    }

    private static async Task<ReunionPublicaTipoDto?> GetTipoByIdAsync(SqlConnection cn, SqlTransaction tx, long idTipoReunion, CancellationToken ct)
    {
        return await cn.QueryFirstOrDefaultAsync<ReunionPublicaTipoDto>(
            """
            SELECT TOP 1 IdTipoReunion, Slug, Titulo, ISNULL(CAST(Descripcion AS nvarchar(max)), '') AS Descripcion,
                   DuracionMinutos, Modalidad, ISNULL(EnlaceVideollamada, '') AS EnlaceVideollamada,
                   ISNULL(IdTecnico, '') AS IdTecnico, ISNULL(TecnicoNombre, '') AS TecnicoNombre,
                   ISNULL(TelefonoWhatsApp, '') AS TelefonoWhatsApp, ISNULL(EmailOperador, '') AS EmailOperador,
                   DiasDisponibles, HoraDesde, HoraHasta, ReservasHastaDias, AnticipacionMinHoras,
                   Activo, ISNULL(Color, '#22d3ee') AS Color
            FROM dbo.CAL_TIPOS_REUNION
            WHERE IdTipoReunion = @IdTipoReunion AND Activo = 1 AND ISNULL(Baja, 0) = 0;
            """,
            new { IdTipoReunion = idTipoReunion },
            tx);
    }

    private static async Task<List<BusyRange>> GetBusyRangesAsync(SqlConnection cn, ReunionPublicaTipoDto tipo, DateTime start, DateTime end, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<BusyRange>(
            """
            SELECT FechaInicio AS Inicio, FechaFin AS Fin
            FROM dbo.CAL_EVENTOS
            WHERE ISNULL(Baja, 0) = 0
              AND Estado <> N'CANCELADO'
              AND FechaInicio < @End
              AND FechaFin > @Start
              AND (@IdTecnico = N'' OR ISNULL(IdTecnico, N'') = @IdTecnico);
            """,
            new { Start = start, End = end, tipo.IdTecnico });
        return rows.ToList();
    }

    private static ReunionPublicaDiaDto BuildDay(ReunionPublicaTipoDto tipo, DateTime day, IReadOnlyCollection<BusyRange> busy)
    {
        var now = DateTime.Now;
        var result = new ReunionPublicaDiaDto { Fecha = day };
        if (!AllowedDays(tipo).Contains(NormalizeDay(day.DayOfWeek)))
            return result;

        var cursor = day.Date.Add(tipo.HoraDesde);
        var limit = day.Date.Add(tipo.HoraHasta);
        while (cursor.AddMinutes(tipo.DuracionMinutos) <= limit)
        {
            var fin = cursor.AddMinutes(tipo.DuracionMinutos);
            var available = IsSlotInsideRules(tipo, cursor, fin, now)
                            && !busy.Any(x => x.Inicio < fin && x.Fin > cursor);
            result.Horarios.Add(new ReunionPublicaHorarioDto { Inicio = cursor, Fin = fin, Disponible = available });
            cursor = cursor.AddMinutes(tipo.DuracionMinutos);
        }

        result.Disponible = result.Horarios.Any(x => x.Disponible);
        return result;
    }

    private static bool IsSlotInsideRules(ReunionPublicaTipoDto tipo, DateTime inicio, DateTime fin, DateTime now)
    {
        if (!AllowedDays(tipo).Contains(NormalizeDay(inicio.DayOfWeek)))
            return false;
        if (inicio < now.AddHours(Math.Max(0, tipo.AnticipacionMinHoras)))
            return false;
        if (inicio.Date > now.Date.AddDays(Math.Max(1, tipo.ReservasHastaDias)))
            return false;
        if (inicio.TimeOfDay < tipo.HoraDesde || fin.TimeOfDay > tipo.HoraHasta)
            return false;
        return true;
    }

    private static void ValidateReservation(ReunionPublicaReservaRequest request)
    {
        if (request.IdTipoReunion <= 0)
            throw new InvalidOperationException("Selecciona una capacitacion valida.");
        if (string.IsNullOrWhiteSpace(request.ClienteNombre))
            throw new InvalidOperationException("Indica tu nombre para confirmar la reserva.");
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new InvalidOperationException("Indica un email de contacto.");
        if (!request.Email.Contains('@', StringComparison.Ordinal))
            throw new InvalidOperationException("El email no parece valido.");
        if (string.IsNullOrWhiteSpace(request.Telefono))
            throw new InvalidOperationException("Indica un telefono de contacto.");
    }

    private static void ValidateTipo(ReunionPublicaTipoDto tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo.Titulo))
            throw new InvalidOperationException("El titulo de la cita es obligatorio.");
        if (tipo.DuracionMinutos is < 15 or > 480)
            throw new InvalidOperationException("La duracion debe estar entre 15 minutos y 8 horas.");
        if (tipo.HoraHasta <= tipo.HoraDesde)
            throw new InvalidOperationException("La hora hasta debe ser posterior a la hora desde.");
    }

    private static string BuildReservationDescription(ReunionPublicaReservaRequest request)
        => $"""
           Reserva publica de capacitacion
           Cliente: {request.ClienteNombre}
           Razon social: {request.RazonSocial}
           Email: {request.Email}
           Telefono: {request.Telefono}
           Tipo de capacitacion: {request.TipoCapacitacion}

           Observaciones:
           {request.Observaciones}
           """;

    private static IReadOnlySet<int> AllowedDays(ReunionPublicaTipoDto tipo)
        => NormalizeDays(tipo.DiasDisponibles)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(x => x is >= 1 and <= 7)
            .ToHashSet();

    private static int NormalizeDay(DayOfWeek day)
        => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static string NormalizeDays(string? value)
    {
        var days = (value ?? "1,2,3,4,5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(x => x is >= 1 and <= 7)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        return days.Length == 0 ? "1,2,3,4,5" : string.Join(',', days);
    }

    private static string Slugify(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "capacitacion-online" : value.Trim().ToLowerInvariant();
        var chars = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "capacitacion-online" : slug;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
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
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
    {
        await ExecuteLoggedAsync(action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
    }

    private sealed class BusyRange
    {
        public DateTime Inicio { get; init; }
        public DateTime Fin { get; init; }
    }
}

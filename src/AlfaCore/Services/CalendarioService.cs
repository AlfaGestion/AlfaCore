using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

public sealed class CalendarioService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents,
    IConversacionesService conversacionesService,
    IAvisosPushNotifier? avisosPush = null) : ICalendarioService
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

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var columnaIndicador = await TieneColumnaIndicadorAsync(cn, token);

            // Filtro por responsable: un usuario del sistema ("U:nombre") se graba sin IdTecnico.
            var filtroUsuario = CalendarioResponsables.EsUsuario(request.IdTecnico, out var usuarioFiltro);

            var sql = $"""
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
                    ISNULL(CAST(r.UltimoError AS nvarchar(max)), ''),
                    {(columnaIndicador ? "CAST(ISNULL(e.MostrarEnIndicador, 0) AS bit)" : "CAST(0 AS bit)")}
                FROM dbo.CAL_EVENTOS e
                LEFT JOIN dbo.CAL_RECORDATORIOS r ON r.IdEvento = e.IdEvento AND ISNULL(r.Baja, 0) = 0
                WHERE ISNULL(e.Baja, 0) = 0
                  AND e.FechaInicio < @GridEnd
                  AND e.FechaFin >= @GridStart
                  AND (@Tipo = 'TODOS' OR e.Tipo = @Tipo)
                  AND (@IdTecnico IS NULL OR e.IdTecnico = @IdTecnico)
                  AND (@UsuarioResponsable IS NULL
                       OR (ISNULL(LTRIM(RTRIM(e.IdTecnico)), '') = '' AND LTRIM(RTRIM(e.TecnicoNombre)) = @UsuarioResponsable))
                  AND (
                        @Search = ''
                        OR e.Titulo LIKE @SearchLike
                        OR e.TecnicoNombre LIKE @SearchLike
                        OR CAST(e.Descripcion AS nvarchar(max)) LIKE @SearchLike
                      )
                ORDER BY e.FechaInicio, e.TodoElDia DESC, e.Titulo;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@GridStart", gridStart);
            cmd.Parameters.AddWithValue("@GridEnd", gridEnd);
            cmd.Parameters.AddWithValue("@Tipo", string.IsNullOrWhiteSpace(request.Tipo) ? CalendarioEventoTipos.Todos : request.Tipo);
            cmd.Parameters.AddWithValue("@IdTecnico", filtroUsuario ? DBNull.Value : DbNullable(request.IdTecnico));
            cmd.Parameters.AddWithValue("@UsuarioResponsable", filtroUsuario ? usuarioFiltro : DBNull.Value);
            cmd.Parameters.AddWithValue("@Search", request.Search?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("@SearchLike", $"%{(request.Search ?? string.Empty).Trim()}%");

            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                while (await rd.ReadAsync(token))
                    result.Eventos.Add(ReadEvento(rd));
            }

            result.Tecnicos = await GetResponsablesAsync(cn, token);
            result.PlantillasWhatsApp = (await conversacionesService.GetTemplatesAsync(new ConversacionPlantillaFilters
            {
                EstadoMeta = "APPROVED",
                IncluirInactivas = false
            }, token)).ToList();

            return result;
        }, "No se pudo cargar el calendario.", ct);

    /// <summary>
    /// Mismo criterio que la pantalla de Calendario: guardia "activa" es la no cancelada que abarca el
    /// día de hoy; la "siguiente" es la próxima que empieza después de ahora. No pasa por
    /// ExecuteLoggedAsync: la barra la consulta seguido y una base sin Calendario no es un error.
    /// </summary>
    public async Task<CalendarioGuardiaResumenDto?> GetGuardiaResumenAsync(CancellationToken ct = default)
    {
        if (!TenantDataAccessGuard.IsActiveSessionAuthorized(sessionService, appUserSession))
            return null;

        const string sqlBase = """
            IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
                RETURN;

            DECLARE @Hoy date = CAST(GETDATE() AS date);
            DECLARE @IdActual bigint;

            SELECT TOP (1)
                @IdActual = e.IdEvento
            FROM dbo.CAL_EVENTOS e
            WHERE ISNULL(e.Baja, 0) = 0
              AND {FILTRO}
              AND ISNULL(e.Estado, '') <> @Cancelado
              AND CAST(e.FechaInicio AS date) <= @Hoy
              AND CAST(e.FechaFin AS date) >= @Hoy
            ORDER BY e.FechaInicio;

            SELECT
                (SELECT COALESCE(NULLIF(LTRIM(RTRIM(TecnicoNombre)), ''), NULLIF(LTRIM(RTRIM(Titulo)), ''), '')
                 FROM dbo.CAL_EVENTOS WHERE IdEvento = @IdActual) AS ActualResponsable,
                (SELECT FechaFin FROM dbo.CAL_EVENTOS WHERE IdEvento = @IdActual) AS ActualHasta,
                sig.Responsable AS SiguienteResponsable,
                sig.FechaInicio AS SiguienteDesde
            FROM (SELECT 1 AS Uno) x
            OUTER APPLY (
                SELECT TOP (1)
                    COALESCE(NULLIF(LTRIM(RTRIM(e.TecnicoNombre)), ''), NULLIF(LTRIM(RTRIM(e.Titulo)), ''), '') AS Responsable,
                    e.FechaInicio
                FROM dbo.CAL_EVENTOS e
                WHERE ISNULL(e.Baja, 0) = 0
                  AND {FILTRO}
                  AND ISNULL(e.Estado, '') <> @Cancelado
                  AND e.FechaInicio > GETDATE()
                  AND (@IdActual IS NULL OR e.IdEvento <> @IdActual)
                ORDER BY e.FechaInicio
            ) sig;
            """;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var indicador = await LeerIndicadorConfigAsync(cn, ct);
            if (!indicador.Activo)
                return null;

            // Eventos marcados con "Mostrar en la barra superior" (de cualquier tipo) más, si se
            // eligió un tipo, todos los de ese tipo.
            var filtro = await TieneColumnaIndicadorAsync(cn, ct)
                ? "(ISNULL(e.MostrarEnIndicador, 0) = 1 OR e.Tipo = @Tipo)"
                : "e.Tipo = @Tipo";
            await using var cmd = new SqlCommand(sqlBase.Replace("{FILTRO}", filtro, StringComparison.Ordinal), cn);
            cmd.Parameters.AddWithValue("@Tipo", indicador.Tipo);
            cmd.Parameters.AddWithValue("@Cancelado", CalendarioEventoEstados.Cancelado);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                return null;

            return new CalendarioGuardiaResumenDto
            {
                Etiqueta = indicador.Etiqueta,
                ActualResponsable = rd.IsDBNull(0) ? string.Empty : rd.GetString(0),
                ActualHasta = rd.IsDBNull(1) ? null : rd.GetDateTime(1),
                SiguienteResponsable = rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                SiguienteDesde = rd.IsDBNull(3) ? null : rd.GetDateTime(3)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "GetGuardiaResumen", ex,
                "No se pudo consultar la guardia activa.", null, AppEventSeverity.Warning, ct);
            return null;
        }
    }

    public Task<CalendarioEventoDto?> GetByIdAsync(long idEvento, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetById", async token =>
        {
            const string sqlBase = """
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
                    ISNULL(CAST(r.UltimoError AS nvarchar(max)), ''),
                    {INDICADOR}
                FROM dbo.CAL_EVENTOS e
                LEFT JOIN dbo.CAL_RECORDATORIOS r ON r.IdEvento = e.IdEvento AND ISNULL(r.Baja, 0) = 0
                WHERE e.IdEvento = @IdEvento AND ISNULL(e.Baja, 0) = 0;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var sql = sqlBase.Replace("{INDICADOR}", await TieneColumnaIndicadorAsync(cn, token)
                ? "CAST(ISNULL(e.MostrarEnIndicador, 0) AS bit)"
                : "CAST(0 AS bit)", StringComparison.Ordinal);
            CalendarioEventoDto? evento;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@IdEvento", idEvento);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                evento = await rd.ReadAsync(token) ? ReadEvento(rd) : null;
            }

            if (evento is not null && await TieneColumnasSerieAsync(cn, token))
            {
                var serie = await LeerSerieAsync(cn, idEvento, token);
                evento.IdSerie = serie?.IdSerie;
                evento.Repeticion = CalendarioRepeticion.Deserializar(serie?.ReglaJson);
                if (serie?.IdSerie is long idSerie)
                    evento.RotacionTecnicos = await LeerRotacionAsync(cn, idSerie, token);
            }

            return evento;
        }, "No se pudo cargar el evento.", ct);

    public Task<long> SaveAsync(CalendarioEventoSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            Validate(request);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var id = await GuardarAsync(cn, request, token);
            await AplicarMostrarEnIndicadorAsync(cn, id, request, token);
            return id;
        }, "No se pudo guardar el evento.", ct);

    private async Task<long> GuardarAsync(SqlConnection cn, CalendarioEventoSaveRequest request, CancellationToken token)
    {
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

            long idEvento;

            // Eventos repetidos (series). Sin las columnas de serie (base sin la actualización) solo
            // se puede trabajar con eventos sueltos, como antes.
            var regla = request.Repeticion is { SeRepite: true } ? request.Repeticion : null;
            var tieneSeries = await TieneColumnasSerieAsync(cn, token);
            if (regla is not null && !tieneSeries)
                throw new InvalidOperationException("Para repetir eventos falta aplicar la actualización de base de datos del Calendario (Utilidades → Actualizaciones).");

            if (isNew && regla is not null)
                return await CrearSerieAsync(cn, request, regla, null, user, token);

            if (!isNew && tieneSeries)
            {
                var resultadoSerie = await GuardarEventoDeSerieAsync(cn, request, regla, color, tecnicoNombre, user, token);
                if (resultadoSerie is long idSerieGuardado)
                    return idSerieGuardado;
            }

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

            // Evento nuevo agendado para un técnico: push a su usuario (si no es quien lo cargó).
            if (isNew && avisosPush is not null && !string.IsNullOrWhiteSpace(request.IdTecnico))
            {
                var cuando = request.TodoElDia
                    ? request.FechaInicio.ToString("dd/MM", CultureInfo.InvariantCulture)
                    : request.FechaInicio.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
                await avisosPush.NotificarTecnicoAsync(request.IdTecnico, $"Te agendaron: {request.Titulo.Trim()}",
                    $"{cuando} · cargado por {user}", "/calendario", token);
            }

            return idEvento;
    }

    /// <summary>
    /// Casilla "Mostrar en la barra superior": se aplica al evento guardado y, en una serie, a los
    /// mismos eventos que abarca el alcance elegido (una serie nueva: a todos).
    /// </summary>
    private async Task AplicarMostrarEnIndicadorAsync(SqlConnection cn, long idEvento, CalendarioEventoSaveRequest request, CancellationToken ct)
    {
        if (idEvento <= 0 || !await TieneColumnaIndicadorAsync(cn, ct))
            return;

        var serie = await TieneColumnasSerieAsync(cn, ct) ? await LeerSerieAsync(cn, idEvento, ct) : null;
        var alcance = request.IdEvento <= 0 ? CalendarioAlcancesSerie.Todos : NormalizarAlcance(request.Alcance);
        const string sql = """
            UPDATE dbo.CAL_EVENTOS
               SET MostrarEnIndicador = @Mostrar
             WHERE ISNULL(Baja, 0) = 0
               AND (
                    IdEvento = @IdEvento
                    OR (@IdSerie IS NOT NULL AND IdSerie = @IdSerie AND (@Todos = 1 OR FechaInicio >= @Desde))
                   );
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Mostrar", request.MostrarEnIndicador);
        cmd.Parameters.AddWithValue("@IdEvento", idEvento);
        cmd.Parameters.AddWithValue("@IdSerie", serie?.IdSerie is long idSerie && alcance != CalendarioAlcancesSerie.SoloEste ? idSerie : DBNull.Value);
        cmd.Parameters.AddWithValue("@Todos", alcance == CalendarioAlcancesSerie.Todos);
        cmd.Parameters.AddWithValue("@Desde", serie?.FechaInicio ?? request.FechaInicio);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> TieneColumnaIndicadorAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.CAL_EVENTOS', N'MostrarEnIndicador') IS NOT NULL THEN 1 ELSE 0 END;", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>
    /// Responsables que se pueden asignar: los técnicos y, además, los usuarios del sistema que no
    /// están asociados a un técnico (muchas empresas no cargan técnicos).
    /// </summary>
    private async Task<List<ConversacionTecnicoOptionDto>> GetResponsablesAsync(SqlConnection cn, CancellationToken ct)
    {
        var responsables = (await conversacionesService.GetTechniciansAsync(ct)).ToList();
        var asociados = new HashSet<string>(
            responsables.Select(t => (t.UsuarioAsociado ?? string.Empty).Trim()).Where(u => u.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var sistema = (appUserSession.CurrentUser?.SystemCode ?? string.Empty).Trim();
            bool tieneActivo;
            await using (var check = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NULL THEN -1 WHEN COL_LENGTH(N'dbo.TA_USUARIOS', N'Activo') IS NULL THEN 0 ELSE 1 END;", cn))
            {
                var estado = Convert.ToInt32(await check.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
                if (estado < 0)
                    return responsables;
                tieneActivo = estado == 1;
            }

            var sql = $"""
                SELECT DISTINCT LTRIM(RTRIM(NOMBRE))
                FROM dbo.TA_USUARIOS
                WHERE ISNULL(EsGrupo, 0) = 0
                  {(tieneActivo ? "AND ISNULL(Activo, 1) = 1" : string.Empty)}
                  AND LTRIM(RTRIM(ISNULL(NOMBRE, N''))) <> N''
                  AND (@Sistema = N'' OR UPPER(LTRIM(RTRIM(SISTEMA))) = UPPER(@Sistema));
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Sistema", sistema);
            var usuarios = new List<string>();
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
            {
                while (await rd.ReadAsync(ct))
                    usuarios.Add(rd.GetString(0));
            }

            responsables.AddRange(usuarios
                .Where(u => !asociados.Contains(u) && CalendarioResponsables.IdUsuario(u).Length <= 120)
                .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
                .Select(u => new ConversacionTecnicoOptionDto
                {
                    IdTecnico = CalendarioResponsables.IdUsuario(u),
                    Nombre = u,
                    Cargo = "Usuario del sistema",
                    UsuarioAsociado = u
                }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "GetResponsables", ex,
                "No se pudieron leer los usuarios del sistema para el Calendario.", null, AppEventSeverity.Warning, ct);
        }

        return responsables;
    }

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

    public Task DeleteSerieAsync(long idEvento, string alcance, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteSerie", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var serie = await TieneColumnasSerieAsync(cn, token) ? await LeerSerieAsync(cn, idEvento, token) : null;
            if (serie?.IdSerie is not long idSerie || NormalizarAlcance(alcance) == CalendarioAlcancesSerie.SoloEste)
            {
                await DeleteAsync(idEvento, usuarioAccion, token);
                return true;
            }

            const string sql = """
                UPDATE dbo.CAL_EVENTOS
                   SET Baja = 1,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdSerie = @IdSerie
                   AND ISNULL(Baja, 0) = 0
                   AND (@Todos = 1 OR FechaInicio >= @Desde);

                UPDATE r
                   SET Baja = 1,
                       FechaHora_Modificacion = GETDATE()
                  FROM dbo.CAL_RECORDATORIOS r
                 INNER JOIN dbo.CAL_EVENTOS e ON e.IdEvento = r.IdEvento
                 WHERE e.IdSerie = @IdSerie
                   AND ISNULL(e.Baja, 0) = 1
                   AND ISNULL(r.Baja, 0) = 0;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdSerie", idSerie);
            cmd.Parameters.AddWithValue("@Todos", NormalizarAlcance(alcance) == CalendarioAlcancesSerie.Todos);
            cmd.Parameters.AddWithValue("@Desde", serie.FechaInicio);
            var bajas = await cmd.ExecuteNonQueryAsync(token);
            await appEvents.LogAuditAsync(ModuleName, "SerieEliminada", "CAL_EVENTOS",
                idSerie.ToString(CultureInfo.InvariantCulture),
                "Eventos de una serie dados de baja.", new { idEvento, alcance, bajas }, token);
            return true;
        }, "No se pudo eliminar el evento.", ct);

    private const string ClaveIndicadorActivo = "CAL_INDICADOR_ACTIVO";
    private const string ClaveIndicadorTipo = "CAL_INDICADOR_TIPO";
    private const string ClaveIndicadorEtiqueta = "CAL_INDICADOR_ETIQUETA";

    public Task<CalendarioIndicadorConfigDto> GetIndicadorConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetIndicadorConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LeerIndicadorConfigAsync(cn, token);
        }, "No se pudo leer la configuración del indicador.", ct);

    public Task SaveIndicadorConfigAsync(CalendarioIndicadorConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveIndicadorConfig", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);
            var tipo = string.IsNullOrWhiteSpace(config.Tipo) ? CalendarioIndicadorConfigDto.SoloMarcados : config.Tipo.Trim().ToUpperInvariant();
            var etiqueta = string.IsNullOrWhiteSpace(config.Etiqueta) ? EtiquetaPorDefecto(tipo) : config.Etiqueta.Trim();
            if (etiqueta.Length > 40)
                etiqueta = etiqueta[..40];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await GuardarConfigAsync(cn, ClaveIndicadorActivo, config.Activo ? "1" : "0", token);
            await GuardarConfigAsync(cn, ClaveIndicadorTipo, tipo, token);
            await GuardarConfigAsync(cn, ClaveIndicadorEtiqueta, etiqueta, token);
            return true;
        }, "No se pudo guardar la configuración del indicador.", ct);

    /// <summary>
    /// El Calendario sirve para cualquier empresa: sin configurar, el indicador está apagado y muestra
    /// solo los eventos marcados ("A cargo"). Solo las bases que ya tenían guardias cargadas arrancan
    /// con el indicador de guardias activo, como funcionaba antes.
    /// </summary>
    private static async Task<CalendarioIndicadorConfigDto> LeerIndicadorConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            DECLARE @Activo nvarchar(20), @Tipo nvarchar(30), @Etiqueta nvarchar(60), @HayGuardias bit = 0;
            IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
            BEGIN
                SELECT TOP (1) @Activo = LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CAL_INDICADOR_ACTIVO';
                SELECT TOP (1) @Tipo = LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CAL_INDICADOR_TIPO';
                SELECT TOP (1) @Etiqueta = LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CAL_INDICADOR_ETIQUETA';
            END;
            IF OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM dbo.CAL_EVENTOS WHERE Tipo = N'GUARDIA' AND ISNULL(Baja, 0) = 0)
                SET @HayGuardias = 1;
            SELECT @Activo, @Tipo, @Etiqueta, @HayGuardias;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        var activo = rd.IsDBNull(0) ? null : rd.GetString(0);
        var hayGuardias = !rd.IsDBNull(3) && rd.GetBoolean(3);
        var tipoPorDefecto = activo is null && hayGuardias ? CalendarioEventoTipos.Guardia : CalendarioIndicadorConfigDto.SoloMarcados;
        var tipo = rd.IsDBNull(1) || string.IsNullOrWhiteSpace(rd.GetString(1)) ? tipoPorDefecto : rd.GetString(1).ToUpperInvariant();
        var etiqueta = rd.IsDBNull(2) || string.IsNullOrWhiteSpace(rd.GetString(2)) ? EtiquetaPorDefecto(tipo) : rd.GetString(2);
        return new CalendarioIndicadorConfigDto
        {
            Activo = activo is null ? hayGuardias : activo == "1",
            Tipo = tipo,
            Etiqueta = etiqueta
        };
    }

    private static string EtiquetaPorDefecto(string tipo) => tipo switch
    {
        CalendarioEventoTipos.Reunion => "Reunión",
        CalendarioEventoTipos.Capacitacion => "Capacitación",
        CalendarioEventoTipos.Guardia => "Guardia",
        CalendarioEventoTipos.Vacaciones => "Vacaciones",
        CalendarioEventoTipos.Ausencia => "Ausencia",
        _ => "A cargo"
    };

    private static async Task GuardarConfigAsync(SqlConnection cn, string clave, string valor, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR) VALUES (N'CALENDARIO', @Clave, @Valor);
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", clave);
        cmd.Parameters.AddWithValue("@Valor", valor);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed record SerieEvento(long? IdSerie, string? ReglaJson, DateTime FechaInicio, DateTime FechaFin, string IdTecnico, string Color);

    private static async Task<bool> TieneColumnasSerieAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.CAL_EVENTOS', N'IdSerie') IS NOT NULL AND COL_LENGTH(N'dbo.CAL_EVENTOS', N'ReglaRepeticion') IS NOT NULL THEN 1 ELSE 0 END;", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<SerieEvento?> LeerSerieAsync(SqlConnection cn, long idEvento, CancellationToken ct)
    {
        const string sql = """
            SELECT IdSerie, ReglaRepeticion, FechaInicio, FechaFin, ISNULL(IdTecnico, ''), ISNULL(Color, ''), ISNULL(TecnicoNombre, '')
            FROM dbo.CAL_EVENTOS
            WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdEvento", idEvento);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;
        return new SerieEvento(
            rd.IsDBNull(0) ? null : rd.GetInt64(0),
            rd.IsDBNull(1) ? null : rd.GetString(1),
            rd.GetDateTime(2),
            rd.GetDateTime(3),
            IdResponsable(rd.GetString(4), rd.GetString(6)),
            rd.GetString(5).Trim());
    }

    /// <summary>
    /// Rotación de una serie a partir de sus eventos: la secuencia de responsables que se repite
    /// (Juan, Ana, Pedro, Juan, Ana...). Con un solo responsable no hay rotación. Si algún evento se
    /// cambió a mano y la secuencia ya no se repite, se toman las personas en orden de aparición.
    /// </summary>
    private static async Task<List<string>> LeerRotacionAsync(SqlConnection cn, long idSerie, CancellationToken ct)
    {
        const string sql = """
            SELECT ISNULL(IdTecnico, ''), ISNULL(TecnicoNombre, '')
            FROM dbo.CAL_EVENTOS
            WHERE IdSerie = @IdSerie AND ISNULL(Baja, 0) = 0
            ORDER BY FechaInicio;
            """;
        var ids = new List<string>();
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@IdSerie", idSerie);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                ids.Add(IdResponsable(rd.GetString(0), rd.GetString(1)));
        }

        return CalendarioRepeticion.DetectarRotacion(ids);
    }

    private static string NormalizarAlcance(string? alcance) => (alcance ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        CalendarioAlcancesSerie.EsteYSiguientes => CalendarioAlcancesSerie.EsteYSiguientes,
        CalendarioAlcancesSerie.Todos => CalendarioAlcancesSerie.Todos,
        _ => CalendarioAlcancesSerie.SoloEste
    };

    private static CalendarioEventoSaveRequest CopiarRequest(CalendarioEventoSaveRequest r, DateTime inicio, DateTime fin) => new()
    {
        IdEvento = 0,
        Titulo = r.Titulo,
        Tipo = r.Tipo,
        FechaInicio = inicio,
        FechaFin = fin,
        TodoElDia = r.TodoElDia,
        IdTecnico = r.IdTecnico,
        TecnicoNombre = r.TecnicoNombre,
        TelefonoWhatsApp = r.TelefonoWhatsApp,
        Estado = r.Estado,
        Color = r.Color,
        Descripcion = r.Descripcion,
        CrearRecordatorioWhatsApp = r.CrearRecordatorioWhatsApp,
        MinutosAntesRecordatorio = r.MinutosAntesRecordatorio,
        IdPlantillaWhatsApp = r.IdPlantillaWhatsApp,
        UsuarioAccion = r.UsuarioAccion,
        SistemaAccion = r.SistemaAccion,
        RotacionTecnicos = [.. r.RotacionTecnicos],
        MostrarEnIndicador = r.MostrarEnIndicador
    };

    /// <summary>
    /// Graba una serie: un evento real por cada fecha de la regla, todos con el mismo IdSerie (el id
    /// del primero) y la regla en JSON. Con rotación, cada evento le toca al siguiente técnico de la
    /// lista (con su WhatsApp). <paramref name="idPrimeroExistente"/>: un evento ya grabado que pasa a
    /// ser el primero de la serie (evento suelto convertido en repetido).
    /// </summary>
    private async Task<long> CrearSerieAsync(
        SqlConnection cn, CalendarioEventoSaveRequest request, CalendarioRepeticionDto regla, long? idPrimeroExistente, string user, CancellationToken ct)
    {
        var inicios = CalendarioRepeticion.GenerarInicios(request.FechaInicio, regla);
        var duracion = request.FechaFin - request.FechaInicio;
        var reglaJson = CalendarioRepeticion.Serializar(regla);

        var tecnicos = request.RotacionTecnicos.Count > 0
            ? await GetResponsablesAsync(cn, ct)
            : [];
        var rotacion = request.RotacionTecnicos
            .Select(id => tecnicos.FirstOrDefault(t => string.Equals(t.IdTecnico.Trim(), (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        const string insertSql = """
            INSERT INTO dbo.CAL_EVENTOS
            (
                Titulo, Tipo, FechaInicio, FechaFin, TodoElDia, IdTecnico, TecnicoNombre,
                TelefonoWhatsApp, Estado, Color, Descripcion, UsuarioAlta,
                FechaHora_Grabacion, FechaHora_Modificacion, IdSerie, ReglaRepeticion
            )
            OUTPUT INSERTED.IdEvento
            VALUES
            (
                @Titulo, @Tipo, @FechaInicio, @FechaFin, @TodoElDia, @IdTecnico, @TecnicoNombre,
                @TelefonoWhatsApp, @Estado, @Color, @Descripcion, @Usuario,
                GETDATE(), GETDATE(), @IdSerie, @ReglaRepeticion
            );
            """;

        const string primeroSql = """
            UPDATE dbo.CAL_EVENTOS
               SET IdSerie = @IdEvento,
                   ReglaRepeticion = @ReglaRepeticion,
                   IdTecnico = @IdTecnico,
                   TecnicoNombre = @TecnicoNombre,
                   TelefonoWhatsApp = @TelefonoWhatsApp,
                   FechaHora_Modificacion = GETDATE()
             WHERE IdEvento = @IdEvento;
            """;

        long idSerie = 0;
        var porTecnico = new Dictionary<string, (string Nombre, int Cantidad, DateTime Primera)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < inicios.Count; i++)
        {
            var evento = CopiarRequest(request, inicios[i], inicios[i] + duracion);
            if (rotacion.Count > 0)
            {
                var tecnico = rotacion[i % rotacion.Count];
                evento.IdTecnico = tecnico.IdTecnico;
                evento.TecnicoNombre = tecnico.Nombre;
                evento.TelefonoWhatsApp = string.IsNullOrWhiteSpace(tecnico.Telefono) ? null : tecnico.Telefono.Trim();
            }

            var color = FirstNonEmpty(evento.Color, ResolveColor(evento.Tipo));
            var tecnicoNombre = FirstNonEmpty(evento.TecnicoNombre, evento.IdTecnico);
            long id;
            if (i == 0 && idPrimeroExistente is long existente)
            {
                id = existente;
                idSerie = id;
                await using var cmd = new SqlCommand(primeroSql, cn);
                cmd.Parameters.AddWithValue("@IdEvento", id);
                cmd.Parameters.AddWithValue("@ReglaRepeticion", reglaJson);
                var esUsuario = CalendarioResponsables.EsUsuario(evento.IdTecnico, out var usuario);
                cmd.Parameters.AddWithValue("@IdTecnico", esUsuario ? DBNull.Value : DbNullable(evento.IdTecnico));
                cmd.Parameters.AddWithValue("@TecnicoNombre", esUsuario ? usuario : DbNullable(tecnicoNombre));
                cmd.Parameters.AddWithValue("@TelefonoWhatsApp", DbNullable(evento.TelefonoWhatsApp));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await using var cmd = new SqlCommand(insertSql, cn);
                AddSaveParameters(cmd, evento, color, tecnicoNombre, user);
                cmd.Parameters.AddWithValue("@IdSerie", idSerie > 0 ? idSerie : DBNull.Value);
                cmd.Parameters.AddWithValue("@ReglaRepeticion", reglaJson);
                id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
                if (i == 0)
                {
                    idSerie = id;
                    await using var serieCmd = new SqlCommand("UPDATE dbo.CAL_EVENTOS SET IdSerie = @Id WHERE IdEvento = @Id;", cn);
                    serieCmd.Parameters.AddWithValue("@Id", id);
                    await serieCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await UpsertReminderAsync(cn, id, evento, ct);

            if (!string.IsNullOrWhiteSpace(evento.IdTecnico))
            {
                var clave = evento.IdTecnico.Trim();
                porTecnico[clave] = porTecnico.TryGetValue(clave, out var previo)
                    ? (previo.Nombre, previo.Cantidad + 1, previo.Primera)
                    : (tecnicoNombre, 1, evento.FechaInicio);
            }
        }

        await appEvents.LogAuditAsync(ModuleName, "SerieGuardada", "CAL_EVENTOS",
            idSerie.ToString(CultureInfo.InvariantCulture),
            $"Serie de eventos guardada: {request.Titulo} ({inicios.Count} eventos)",
            new { request.Tipo, Regla = CalendarioRepeticion.Describir(request.FechaInicio, regla), Eventos = inicios.Count, Rotacion = rotacion.Count },
            ct);

        // Un solo push por técnico con el resumen (no uno por cada fecha de la serie).
        if (avisosPush is not null)
        {
            foreach (var (idTecnico, datos) in porTecnico)
            {
                var detalle = datos.Cantidad == 1
                    ? $"{datos.Primera:dd/MM} · cargado por {user}"
                    : $"{datos.Cantidad} fechas desde el {datos.Primera:dd/MM} · cargado por {user}";
                await avisosPush.NotificarTecnicoAsync(idTecnico, $"Te agendaron: {request.Titulo.Trim()}", detalle, "/calendario", ct);
            }
        }

        return idSerie;
    }

    /// <summary>
    /// Edición de un evento ya grabado cuando hay columnas de serie. Devuelve null si corresponde la
    /// edición normal de un solo evento (evento suelto sin repetir, o "solo este" sin cambiar la regla).
    /// </summary>
    private async Task<long?> GuardarEventoDeSerieAsync(
        SqlConnection cn, CalendarioEventoSaveRequest request, CalendarioRepeticionDto? regla, string color, string tecnicoNombre, string user, CancellationToken ct)
    {
        var original = await LeerSerieAsync(cn, request.IdEvento, ct)
            ?? throw new InvalidOperationException("El evento indicado no existe o fue dado de baja.");

        // Evento suelto que pasa a repetirse: se actualiza y queda como el primero de la serie.
        if (original.IdSerie is null)
        {
            if (regla is null)
                return null;
            await ActualizarEventoAsync(cn, request, color, tecnicoNombre, user, ct);
            return await CrearSerieAsync(cn, request, regla, request.IdEvento, user, ct);
        }

        var alcance = NormalizarAlcance(request.Alcance);
        if (alcance == CalendarioAlcancesSerie.SoloEste)
            return null;

        var reglaOriginal = CalendarioRepeticion.Deserializar(original.ReglaJson);
        var reglaCambio = !string.Equals(
            regla is null ? string.Empty : CalendarioRepeticion.Serializar(regla),
            reglaOriginal is null ? string.Empty : CalendarioRepeticion.Serializar(reglaOriginal),
            StringComparison.Ordinal);

        var idSerie = original.IdSerie.Value;
        var todos = alcance == CalendarioAlcancesSerie.Todos;
        var desde = original.FechaInicio;
        if (todos)
        {
            await using var primeroCmd = new SqlCommand(
                "SELECT MIN(FechaInicio) FROM dbo.CAL_EVENTOS WHERE IdSerie = @IdSerie AND ISNULL(Baja, 0) = 0;", cn);
            primeroCmd.Parameters.AddWithValue("@IdSerie", idSerie);
            desde = await primeroCmd.ExecuteScalarAsync(ct) is DateTime primero ? primero : original.FechaInicio;
        }

        var corrimientoInicio = request.FechaInicio - original.FechaInicio;
        var corrimientoFin = request.FechaFin - original.FechaFin;

        var rotacionOriginal = await LeerRotacionAsync(cn, idSerie, ct);
        var rotacionCambio = !request.RotacionTecnicos
            .Select(x => (x ?? string.Empty).Trim())
            .SequenceEqual(rotacionOriginal, StringComparer.OrdinalIgnoreCase);

        if (reglaCambio || rotacionCambio)
        {
            // Cambió la regla (o se pidió rotar): se dan de baja los eventos afectados y se vuelve a
            // generar la serie desde este evento (o desde el primero, si es "todos").
            await using (var bajaCmd = new SqlCommand("""
                UPDATE dbo.CAL_EVENTOS SET Baja = 1, FechaHora_Modificacion = GETDATE()
                 WHERE IdSerie = @IdSerie AND ISNULL(Baja, 0) = 0 AND FechaInicio >= @Desde;
                UPDATE r SET Baja = 1, FechaHora_Modificacion = GETDATE()
                  FROM dbo.CAL_RECORDATORIOS r
                 INNER JOIN dbo.CAL_EVENTOS e ON e.IdEvento = r.IdEvento
                 WHERE e.IdSerie = @IdSerie AND ISNULL(e.Baja, 0) = 1 AND ISNULL(r.Baja, 0) = 0;
                """, cn))
            {
                bajaCmd.Parameters.AddWithValue("@IdSerie", idSerie);
                bajaCmd.Parameters.AddWithValue("@Desde", desde);
                await bajaCmd.ExecuteNonQueryAsync(ct);
            }

            var nuevoInicio = desde + corrimientoInicio;
            var nueva = CopiarRequest(request, nuevoInicio, nuevoInicio + (request.FechaFin - request.FechaInicio));
            if (!todos)
                nueva.RotacionTecnicos = CalendarioRepeticion.EmpezarDesde(nueva.RotacionTecnicos, original.IdTecnico);
            if (regla is null)
            {
                await using var cmd = new SqlCommand("""
                    INSERT INTO dbo.CAL_EVENTOS
                    (Titulo, Tipo, FechaInicio, FechaFin, TodoElDia, IdTecnico, TecnicoNombre, TelefonoWhatsApp,
                     Estado, Color, Descripcion, UsuarioAlta, FechaHora_Grabacion, FechaHora_Modificacion)
                    OUTPUT INSERTED.IdEvento
                    VALUES (@Titulo, @Tipo, @FechaInicio, @FechaFin, @TodoElDia, @IdTecnico, @TecnicoNombre, @TelefonoWhatsApp,
                            @Estado, @Color, @Descripcion, @Usuario, GETDATE(), GETDATE());
                    """, cn);
                AddSaveParameters(cmd, nueva, color, tecnicoNombre, user);
                var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
                await UpsertReminderAsync(cn, id, nueva, ct);
                return id;
            }

            return await CrearSerieAsync(cn, nueva, regla, null, user, ct);
        }

        // Misma regla: se aplican los cambios a los eventos afectados. Las fechas se corren lo mismo
        // que se corrió este evento; el técnico solo cambia si se cambió acá (respeta la rotación).
        var cambioTecnico = !string.Equals((request.IdTecnico ?? string.Empty).Trim(), original.IdTecnico, StringComparison.OrdinalIgnoreCase);
        var cambioColor = !string.Equals(color.Trim(), original.Color, StringComparison.OrdinalIgnoreCase);
        const string updateSql = """
            UPDATE dbo.CAL_EVENTOS
               SET Titulo = @Titulo,
                   Tipo = @Tipo,
                   TodoElDia = @TodoElDia,
                   Estado = @Estado,
                   Descripcion = @Descripcion,
                   Color = CASE WHEN @CambioColor = 1 THEN @Color ELSE Color END,
                   IdTecnico = CASE WHEN @CambioTecnico = 1 THEN @IdTecnico ELSE IdTecnico END,
                   TecnicoNombre = CASE WHEN @CambioTecnico = 1 THEN @TecnicoNombre ELSE TecnicoNombre END,
                   TelefonoWhatsApp = CASE WHEN @CambioTecnico = 1 THEN @TelefonoWhatsApp ELSE TelefonoWhatsApp END,
                   FechaInicio = DATEADD(second, @CorrimientoInicio, FechaInicio),
                   FechaFin = DATEADD(second, @CorrimientoFin, FechaFin),
                   FechaHora_Modificacion = GETDATE()
            OUTPUT INSERTED.IdEvento, INSERTED.FechaInicio, INSERTED.FechaFin
             WHERE IdSerie = @IdSerie
               AND ISNULL(Baja, 0) = 0
               AND FechaInicio >= @Desde;
            """;

        var afectados = new List<(long Id, DateTime Inicio, DateTime Fin)>();
        await using (var cmd = new SqlCommand(updateSql, cn))
        {
            AddSaveParameters(cmd, request, color, tecnicoNombre, user);
            cmd.Parameters.AddWithValue("@CambioColor", cambioColor);
            cmd.Parameters.AddWithValue("@CambioTecnico", cambioTecnico);
            cmd.Parameters.AddWithValue("@CorrimientoInicio", (int)Math.Round(corrimientoInicio.TotalSeconds));
            cmd.Parameters.AddWithValue("@CorrimientoFin", (int)Math.Round(corrimientoFin.TotalSeconds));
            cmd.Parameters.AddWithValue("@IdSerie", idSerie);
            cmd.Parameters.AddWithValue("@Desde", desde);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                afectados.Add((rd.GetInt64(0), rd.GetDateTime(1), rd.GetDateTime(2)));
        }

        foreach (var (id, inicio, fin) in afectados)
            await UpsertReminderAsync(cn, id, CopiarRequest(request, inicio, fin), ct);

        await appEvents.LogAuditAsync(ModuleName, "SerieActualizada", "CAL_EVENTOS",
            idSerie.ToString(CultureInfo.InvariantCulture),
            $"Serie de eventos actualizada: {request.Titulo} ({afectados.Count} eventos)",
            new { alcance, afectados = afectados.Count, cambioTecnico }, ct);
        return request.IdEvento;
    }

    private static async Task ActualizarEventoAsync(SqlConnection cn, CalendarioEventoSaveRequest request, string color, string tecnicoNombre, string user, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CAL_EVENTOS
               SET Titulo = @Titulo, Tipo = @Tipo, FechaInicio = @FechaInicio, FechaFin = @FechaFin,
                   TodoElDia = @TodoElDia, IdTecnico = @IdTecnico, TecnicoNombre = @TecnicoNombre,
                   TelefonoWhatsApp = @TelefonoWhatsApp, Estado = @Estado, Color = @Color,
                   Descripcion = @Descripcion, FechaHora_Modificacion = GETDATE()
             WHERE IdEvento = @IdEvento AND ISNULL(Baja, 0) = 0;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        AddSaveParameters(cmd, request, color, tecnicoNombre, user);
        cmd.Parameters.AddWithValue("@IdEvento", request.IdEvento);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            throw new InvalidOperationException("El evento indicado no existe o fue dado de baja.");
    }

    public Task<CalendarioRecordatorioSendResult> SendWhatsAppReminderAsync(long idRecordatorio, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SendWhatsAppReminder", async token =>
        {
            var detail = await GetReminderDetailAsync(idRecordatorio, token)
                ?? throw new InvalidOperationException("El recordatorio indicado no existe.");

            if (detail.IdPlantillaWhatsApp is null or <= 0)
                throw new InvalidOperationException("El recordatorio no tiene plantilla de WhatsApp configurada.");
            if (string.IsNullOrWhiteSpace(detail.TelefonoWhatsApp))
                throw new InvalidOperationException("El evento no tiene teléfono WhatsApp para enviar el recordatorio.");

            var usuarioAutor = ResolveConversationAuthorUser();
            var sistemaAutor = ResolveConversationAuthorSystem(usuarioAutor);

            var conv = await conversacionesService.CreateOrGetWhatsAppConversationAsync(new ConversacionCrearWhatsAppRequest
            {
                TelefonoWhatsApp = detail.TelefonoWhatsApp,
                IdTecnico = detail.IdTecnico,
                UsuarioAccion = usuarioAutor,
                SistemaAccion = sistemaAutor
            }, token);

            var message = await conversacionesService.SendTemplateMessageAsync(new ConversacionPlantillaSendRequest
            {
                IdConversacion = conv.IdConversacion,
                IdPlantilla = detail.IdPlantillaWhatsApp.Value,
                IdTecnicoAutor = detail.IdTecnico,
                UsuarioAccion = usuarioAutor,
                SistemaAccion = sistemaAutor,
                ValoresVariables =
                [
                    FirstNonEmpty(detail.TecnicoNombre, detail.Titulo),
                    FormatDate(detail.FechaInicio),
                    FormatDate(detail.FechaFin)
                ],
                // Vale para cualquier evento (guardia, reunión, capacitación...): las keys guardia.* se
                // muestran en el editor como "Responsable / Inicio / Fin del evento".
                ValoresPorVariable = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [WhatsAppTemplateVariableCatalog.GuardiaTecnico] = FirstNonEmpty(detail.TecnicoNombre, detail.Titulo),
                    [WhatsAppTemplateVariableCatalog.GuardiaInicio] = FormatDate(detail.FechaInicio),
                    [WhatsAppTemplateVariableCatalog.GuardiaFin] = FormatDate(detail.FechaFin)
                }
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

    public Task<int> ProcesarRecordatoriosPendientesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ProcesarRecordatoriosPendientes", async token =>
        {
            await OmitStaleRemindersAsync(token);
            var pendientes = await GetDueReminderIdsAsync(token);
            var enviados = 0;

            foreach (var idRecordatorio in pendientes)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await SendWhatsAppReminderAsync(idRecordatorio, "CalendarioWorker", token);
                    enviados++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await MarkReminderErrorAsync(idRecordatorio, ex.Message, token);
                    await appEvents.LogErrorAsync(ModuleName, "ProcesarRecordatorioPendiente", ex,
                        "No se pudo enviar un recordatorio automático de Calendario.",
                        new { IdRecordatorio = idRecordatorio }, AppEventSeverity.Warning, token);
                }
            }

            return enviados;
        }, "No se pudieron procesar los recordatorios pendientes de Calendario.", ct);

    /// <summary>Responsable sin IdTecnico pero con nombre: es un usuario del sistema ("U:nombre").</summary>
    private static string IdResponsable(string? idTecnico, string? tecnicoNombre)
    {
        var id = (idTecnico ?? string.Empty).Trim();
        var nombre = (tecnicoNombre ?? string.Empty).Trim();
        return id.Length == 0 && nombre.Length > 0 ? CalendarioResponsables.IdUsuario(nombre) : id;
    }

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
            IdTecnico = IdResponsable(GetString(rd, 6), GetString(rd, 7)),
            TecnicoNombre = GetString(rd, 7),
            TelefonoWhatsApp = GetString(rd, 8),
            Estado = GetString(rd, 9),
            Color = GetString(rd, 10),
            Descripcion = GetString(rd, 11),
            MostrarEnIndicador = rd.FieldCount > 21 && GetBool(rd, 21)
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

    private async Task<List<long>> GetDueReminderIdsAsync(CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CAL_RECORDATORIOS', N'U') IS NULL
               OR OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
                RETURN;

            SELECT TOP (25) r.IdRecordatorio
            FROM dbo.CAL_RECORDATORIOS r
            INNER JOIN dbo.CAL_EVENTOS e ON e.IdEvento = r.IdEvento
            WHERE ISNULL(r.Baja, 0) = 0
              AND ISNULL(e.Baja, 0) = 0
              AND ISNULL(e.Estado, N'') <> N'CANCELADO'
              AND r.Tipo = N'WHATSAPP'
              AND r.EstadoEnvio = N'PENDIENTE'
              AND r.FechaHoraProgramada IS NOT NULL
              AND r.FechaHoraProgramada <= GETDATE()
              AND r.FechaHoraProgramada >= DATEADD(day, -1, GETDATE())
              AND e.FechaFin > GETDATE()
            ORDER BY r.FechaHoraProgramada, r.IdRecordatorio;
            """;

        var ids = new List<long>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            ids.Add(GetLong(rd, 0));
        return ids;
    }

    private async Task OmitStaleRemindersAsync(CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CAL_RECORDATORIOS', N'U') IS NULL
               OR OBJECT_ID(N'dbo.CAL_EVENTOS', N'U') IS NULL
                RETURN;

            UPDATE r
               SET EstadoEnvio = N'OMITIDO',
                   UltimoError = N'Recordatorio vencido: no se envió automáticamente porque el evento ya terminó o la fecha programada quedó demasiado atrasada.',
                   FechaHora_Modificacion = GETDATE()
            FROM dbo.CAL_RECORDATORIOS r
            INNER JOIN dbo.CAL_EVENTOS e ON e.IdEvento = r.IdEvento
            WHERE ISNULL(r.Baja, 0) = 0
              AND ISNULL(e.Baja, 0) = 0
              AND r.EstadoEnvio = N'PENDIENTE'
              AND r.FechaHoraProgramada IS NOT NULL
              AND r.FechaHoraProgramada <= GETDATE()
              AND (e.FechaFin <= GETDATE() OR r.FechaHoraProgramada < DATEADD(day, -1, GETDATE()));
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkReminderSentAsync(long idRecordatorio, string estadoEnvio, CancellationToken ct)
    {
        var estadoRecordatorio = NormalizeReminderDeliveryStatus(estadoEnvio);

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
        cmd.Parameters.AddWithValue("@EstadoEnvio", estadoRecordatorio);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkReminderErrorAsync(long idRecordatorio, string error, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CAL_RECORDATORIOS
               SET EstadoEnvio = N'ERROR',
                   UltimoError = @UltimoError,
                   FechaHora_Modificacion = GETDATE()
             WHERE IdRecordatorio = @IdRecordatorio
               AND EstadoEnvio = N'PENDIENTE';
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdRecordatorio", idRecordatorio);
        cmd.Parameters.AddWithValue("@UltimoError", string.IsNullOrWhiteSpace(error) ? "Error de envío automático." : error.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddSaveParameters(SqlCommand cmd, CalendarioEventoSaveRequest request, string color, string tecnicoNombre, string user)
    {
        cmd.Parameters.AddWithValue("@Titulo", request.Titulo.Trim());
        cmd.Parameters.AddWithValue("@Tipo", request.Tipo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@FechaInicio", request.FechaInicio);
        cmd.Parameters.AddWithValue("@FechaFin", request.FechaFin);
        cmd.Parameters.AddWithValue("@TodoElDia", request.TodoElDia);
        var esUsuario = CalendarioResponsables.EsUsuario(request.IdTecnico, out var usuario);
        cmd.Parameters.AddWithValue("@IdTecnico", esUsuario ? DBNull.Value : DbNullable(request.IdTecnico));
        cmd.Parameters.AddWithValue("@TecnicoNombre", esUsuario ? usuario : DbNullable(tecnicoNombre));
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
        if (request.Repeticion is { SeRepite: true }
            && CalendarioRepeticion.FinSinSuperponer(request.FechaInicio, request.FechaFin, request.TodoElDia, request.Repeticion) is not null)
            throw new InvalidOperationException(
                "Cada evento dura más que el tiempo entre una repetición y la siguiente, y se pisarían. Acortá el Fin o elegí repetir menos seguido.");
    }

    private static string ResolveColor(string? tipo)
        => (tipo ?? string.Empty).ToUpperInvariant() switch
        {
            CalendarioEventoTipos.Guardia => "#14b8a6",
            CalendarioEventoTipos.Vacaciones => "#22c55e",
            CalendarioEventoTipos.Ausencia => "#f472b6",
            CalendarioEventoTipos.Feriado => "#f59e0b",
            CalendarioEventoTipos.Reunion => "#38bdf8",
            CalendarioEventoTipos.Capacitacion => "#a78bfa",
            _ => "#a78bfa"
        };

    private static string FormatDate(DateTime value)
        => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR"));

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private string? ResolveConversationAuthorUser()
    {
        var userName = appUserSession.CurrentUser?.UserName;
        return string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
    }

    private string? ResolveConversationAuthorSystem(string? usuarioAutor)
    {
        if (string.IsNullOrWhiteSpace(usuarioAutor))
            return null;

        var systemCode = appUserSession.CurrentUser?.SystemCode;
        return string.IsNullOrWhiteSpace(systemCode) ? null : systemCode.Trim();
    }

    private static string NormalizeReminderDeliveryStatus(string? estadoEnvio)
        => (estadoEnvio ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "ERROR" or "ERROR_ENVIO" or "FAILED" => CalendarioRecordatorioEstados.Error,
            "OMITIDO" => CalendarioRecordatorioEstados.Omitido,
            "PENDIENTE" => CalendarioRecordatorioEstados.Pendiente,
            _ => CalendarioRecordatorioEstados.Enviado
        };

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

        message = "El módulo Calendario todavía no está inicializado en la base activa. Ejecutá el script de actualización de calendario y recargá la pantalla.";
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

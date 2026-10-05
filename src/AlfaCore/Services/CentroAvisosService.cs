using System.Globalization;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ICentroAvisosService
{
    /// <summary>Avisos vigentes del usuario actual en la base activa (vacío si no hay sesión autorizada).</summary>
    Task<TenantScopedResult<IReadOnlyList<AvisoDto>>> GetAsync(CancellationToken ct = default);
    Task MarcarLeidosAsync(IReadOnlyCollection<string> claves, CancellationToken ct = default);
}

/// <summary>
/// Centro de avisos de la campana (2026-10-05). Los avisos se calculan al momento a partir de datos que
/// ya existen —eventos del Calendario del técnico vinculado al usuario, reservas públicas de reuniones
/// y novedades publicadas— y solo se guarda qué marcó leído cada usuario (ALFACORE_AVISOS_LEIDOS).
/// Cada usuario ve únicamente sus propios avisos.
/// </summary>
public sealed class CentroAvisosService(
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    INovedadesService novedadesService,
    IAppEventService appEvents) : ICentroAvisosService
{
    private const string ModuleName = "CentroAvisos";
    internal const int DiasAvisoAlta = 7;
    internal const int MinutosAvisoPorDefecto = 1440;
    internal const int DiasNovedades = 30;

    public Task<TenantScopedResult<IReadOnlyList<AvisoDto>>> GetAsync(CancellationToken ct = default)
        => TenantDataAccessGuard.RunForAuthorizedSessionAsync<IReadOnlyList<AvisoDto>>(
            sessionService,
            appUserSession,
            token => ExecuteLoggedAsync("GetAvisos", () => LoadAsync(token), token),
            ct);

    public async Task MarcarLeidosAsync(IReadOnlyCollection<string> claves, CancellationToken ct = default)
    {
        var usuario = appUserSession.GetCurrentUserName();
        if (string.IsNullOrWhiteSpace(usuario) || claves.Count == 0
            || !TenantDataAccessGuard.IsActiveSessionAuthorized(sessionService, appUserSession))
            return;

        await ExecuteLoggedAsync("MarcarLeidos", async () =>
        {
            var normalizadas = claves
                .Select(c => (c ?? string.Empty).Trim())
                .Where(c => c.Length is > 0 and <= 120)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Las novedades conservan su propio registro de lectura (el mismo que usa el popup).
            foreach (var clave in normalizadas.Where(c => c.StartsWith("nov:", StringComparison.OrdinalIgnoreCase)))
            {
                if (long.TryParse(clave[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idNovedad))
                    await novedadesService.MarkReadAsync(idNovedad, usuario, ct);
            }

            var resto = normalizadas.Where(c => !c.StartsWith("nov:", StringComparison.OrdinalIgnoreCase)).ToList();
            if (resto.Count == 0)
                return true;

            await using var cn = new SqlConnection(sessionService.GetConnectionString());
            await cn.OpenAsync(ct);
            foreach (var clave in resto)
            {
                await using var cmd = new SqlCommand("""
                    IF OBJECT_ID(N'dbo.ALFACORE_AVISOS_LEIDOS', N'U') IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_AVISOS_LEIDOS
                                       WHERE UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario))) AND Clave = @Clave)
                        INSERT INTO dbo.ALFACORE_AVISOS_LEIDOS (Usuario, Clave, FechaHoraLeido) VALUES (@Usuario, @Clave, GETDATE());
                    """, cn);
                cmd.Parameters.AddWithValue("@Usuario", usuario.Trim());
                cmd.Parameters.AddWithValue("@Clave", clave);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            return true;
        }, ct);
    }

    private async Task<IReadOnlyList<AvisoDto>> LoadAsync(CancellationToken ct)
    {
        var usuario = appUserSession.GetCurrentUserName();
        if (string.IsNullOrWhiteSpace(usuario))
            return [];

        var ahora = DateTime.Now;
        await using var cn = new SqlConnection(sessionService.GetConnectionString());
        await cn.OpenAsync(ct);

        var tablas = await GetTablasAsync(cn, ct);
        var tecnicos = await GetTecnicosDelUsuarioAsync(cn, usuario, ct);
        var eventos = tablas.Contains("CAL_EVENTOS")
            ? await GetEventosAsync(cn, tablas, tecnicos, ahora, ct)
            : [];
        var novedades = tablas.Contains("ALFACORE_NOVEDADES") && tablas.Contains("ALFACORE_NOVEDADES_LECTURAS")
            ? await GetNovedadesAsync(cn, usuario, ahora, ct)
            : [];
        var leidos = tablas.Contains("ALFACORE_AVISOS_LEIDOS")
            ? await GetLeidosAsync(cn, usuario, ct)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return ConstruirAvisos(eventos, novedades, tecnicos, usuario, leidos, ahora);
    }

    /// <summary>
    /// Reglas de la campana. Por evento del técnico del usuario:
    /// reserva pública reciente (7 días) → "Nueva reserva"; evento que otra persona le agendó
    /// (7 días) → "Te agendaron"; guardia → aviso el día anterior y el mismo día; reunión,
    /// capacitación u otro → desde la anticipación de su recordatorio (24 h si no tiene) hasta que
    /// termina. Las reservas de tipos sin técnico asignado se avisan a todos.
    /// </summary>
    internal static IReadOnlyList<AvisoDto> ConstruirAvisos(
        IEnumerable<AvisoEventoFuente> eventos,
        IEnumerable<AvisoNovedadFuente> novedades,
        IReadOnlyCollection<string> tecnicosDelUsuario,
        string usuario,
        IReadOnlySet<string> leidos,
        DateTime ahora)
    {
        var avisos = new List<AvisoDto>();
        var hoy = ahora.Date;
        var esMio = new HashSet<string>(tecnicosDelUsuario.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);

        foreach (var e in eventos)
        {
            if (e.FechaFin < ahora)
                continue;

            var idTecnico = e.IdTecnico.Trim();
            var propio = idTecnico.Length > 0 && esMio.Contains(idTecnico);
            var sinTecnico = idTecnico.Length == 0;
            var cuando = DescribirCuando(e, hoy);

            if (e.IdReserva is long idReserva && e.ReservaFechaAlta is DateTime altaReserva
                && altaReserva >= ahora.AddDays(-DiasAvisoAlta) && (propio || sinTecnico))
            {
                var quien = string.IsNullOrWhiteSpace(e.ReservaRazonSocial)
                    ? e.ReservaCliente.Trim()
                    : $"{e.ReservaCliente.Trim()} ({e.ReservaRazonSocial.Trim()})";
                var que = string.IsNullOrWhiteSpace(e.ReservaTipoCapacitacion) ? e.Titulo.Trim() : e.ReservaTipoCapacitacion.Trim();
                avisos.Add(new AvisoDto
                {
                    Clave = $"res:{idReserva}",
                    Tipo = AvisoTipos.Reserva,
                    Titulo = $"Nueva reserva: {que}",
                    Detalle = $"{quien} · {cuando}",
                    FechaHora = altaReserva,
                    Ruta = "/calendario"
                });
            }

            if (!propio)
                continue;

            if (e.IdReserva is null && e.FechaAlta is DateTime alta && alta >= ahora.AddDays(-DiasAvisoAlta)
                && !string.IsNullOrWhiteSpace(e.UsuarioAlta)
                && !string.Equals(e.UsuarioAlta.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add(new AvisoDto
                {
                    Clave = $"cal:{e.IdEvento}:alta",
                    Tipo = AvisoTipos.Asignado,
                    Titulo = $"Te agendaron: {e.Titulo.Trim()}",
                    Detalle = $"{DescribirTipo(e.Tipo)} · {cuando} · por {e.UsuarioAlta.Trim()}",
                    FechaHora = alta,
                    Ruta = "/calendario"
                });
            }

            var tipo = (e.Tipo ?? string.Empty).Trim().ToUpperInvariant();
            if (tipo == CalendarioEventoTipos.Guardia)
            {
                var inicio = e.FechaInicio.Date;
                if (inicio == hoy.AddDays(1))
                {
                    avisos.Add(new AvisoDto
                    {
                        Clave = $"cal:{e.IdEvento}:dia-antes",
                        Tipo = AvisoTipos.Guardia,
                        Titulo = "Mañana tenés guardia",
                        Detalle = $"{e.Titulo.Trim()} · {cuando}",
                        FechaHora = e.FechaInicio,
                        Ruta = "/calendario"
                    });
                }
                else if (inicio == hoy)
                {
                    avisos.Add(new AvisoDto
                    {
                        Clave = $"cal:{e.IdEvento}:mismo-dia",
                        Tipo = AvisoTipos.Guardia,
                        Titulo = "Hoy tenés guardia",
                        Detalle = $"{e.Titulo.Trim()} · {cuando}",
                        FechaHora = e.FechaInicio,
                        Ruta = "/calendario"
                    });
                }
            }
            else if (tipo is CalendarioEventoTipos.Reunion or CalendarioEventoTipos.Capacitacion or CalendarioEventoTipos.Otro)
            {
                var anticipacion = e.MinutosAntes is > 0 ? e.MinutosAntes.Value : MinutosAvisoPorDefecto;
                if (ahora >= e.FechaInicio.AddMinutes(-anticipacion))
                {
                    avisos.Add(new AvisoDto
                    {
                        Clave = $"cal:{e.IdEvento}:aviso",
                        Tipo = tipo == CalendarioEventoTipos.Capacitacion ? AvisoTipos.Capacitacion
                            : tipo == CalendarioEventoTipos.Reunion ? AvisoTipos.Reunion
                            : AvisoTipos.Evento,
                        Titulo = $"{(ahora >= e.FechaInicio ? "En curso" : "Próxima")} {DescribirTipo(tipo).ToLowerInvariant()}: {e.Titulo.Trim()}",
                        Detalle = cuando,
                        FechaHora = e.FechaInicio,
                        Ruta = "/calendario"
                    });
                }
            }
        }

        foreach (var n in novedades)
        {
            var version = string.IsNullOrWhiteSpace(n.Version) ? string.Empty : $" · versión {n.Version.Trim()}";
            avisos.Add(new AvisoDto
            {
                Clave = $"nov:{n.IdNovedad}",
                Tipo = AvisoTipos.Novedad,
                Titulo = n.Titulo.Trim(),
                Detalle = $"Novedad{version}",
                Contenido = FirstNonEmpty(n.Bajada, n.Resumen),
                FechaHora = n.FechaPublicacion
            });
        }

        foreach (var aviso in avisos)
            aviso.Leido = aviso.Tipo != AvisoTipos.Novedad && leidos.Contains(aviso.Clave);

        return avisos
            .GroupBy(a => a.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Leido)
            .ThenByDescending(a => a.FechaHora)
            .ToList();
    }

    internal static string DescribirCuando(AvisoEventoFuente e, DateTime hoy)
    {
        var dia = e.FechaInicio.Date == hoy ? "hoy"
            : e.FechaInicio.Date == hoy.AddDays(1) ? "mañana"
            : e.FechaInicio.ToString("ddd dd/MM", CultureInfo.GetCultureInfo("es-AR"));
        return e.TodoElDia ? dia : $"{dia} {e.FechaInicio:HH:mm}";
    }

    private static string DescribirTipo(string? tipo)
        => (tipo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            CalendarioEventoTipos.Guardia => "Guardia",
            CalendarioEventoTipos.Reunion => "Reunión",
            CalendarioEventoTipos.Capacitacion => "Capacitación",
            CalendarioEventoTipos.Vacaciones => "Vacaciones",
            CalendarioEventoTipos.Ausencia => "Ausencia",
            CalendarioEventoTipos.Feriado => "Feriado",
            _ => "Evento"
        };

    private static async Task<HashSet<string>> GetTablasAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT name FROM sys.tables
            WHERE name IN (N'CAL_EVENTOS', N'CAL_RECORDATORIOS', N'CAL_RESERVAS_REUNION',
                           N'ALFACORE_NOVEDADES', N'ALFACORE_NOVEDADES_LECTURAS', N'ALFACORE_AVISOS_LEIDOS');
            """;
        var tablas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            tablas.Add(rd.GetString(0));
        return tablas;
    }

    private static async Task<List<string>> GetTecnicosDelUsuarioAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.V_TA_Tecnicos') IS NOT NULL
                SELECT LTRIM(RTRIM(ISNULL(IdTecnico, '')))
                FROM dbo.V_TA_Tecnicos
                WHERE ISNULL(Baja, 0) = 0
                  AND UPPER(LTRIM(RTRIM(ISNULL(UsuarioAsociado, '')))) = UPPER(LTRIM(RTRIM(@Usuario)));
            """;
        var tecnicos = new List<string>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var id = rd.IsDBNull(0) ? string.Empty : rd.GetString(0);
            if (id.Length > 0)
                tecnicos.Add(id);
        }
        return tecnicos;
    }

    private static async Task<List<AvisoEventoFuente>> GetEventosAsync(
        SqlConnection cn, IReadOnlySet<string> tablas, IReadOnlyList<string> tecnicos, DateTime ahora, CancellationToken ct)
    {
        var conRecordatorios = tablas.Contains("CAL_RECORDATORIOS");
        var conReservas = tablas.Contains("CAL_RESERVAS_REUNION");
        if (tecnicos.Count == 0 && !conReservas)
            return [];

        var tecnicoParams = tecnicos.Select((_, i) => $"@T{i}").ToList();
        var filtroTecnico = tecnicoParams.Count == 0
            ? "1 = 0"
            : $"LTRIM(RTRIM(ISNULL(e.IdTecnico, ''))) IN ({string.Join(", ", tecnicoParams)})";
        var filtroReservaSinTecnico = conReservas
            ? " OR (res.IdReserva IS NOT NULL AND LTRIM(RTRIM(ISNULL(e.IdTecnico, ''))) = '')"
            : string.Empty;

        var sql = $"""
            SELECT TOP (200)
                e.IdEvento, ISNULL(e.Titulo, ''), ISNULL(e.Tipo, ''), e.FechaInicio, e.FechaFin, ISNULL(e.TodoElDia, 1),
                LTRIM(RTRIM(ISNULL(e.IdTecnico, ''))), LTRIM(RTRIM(ISNULL(e.UsuarioAlta, ''))), e.FechaHora_Grabacion,
                {(conRecordatorios ? "rec.MinutosAntes" : "CAST(NULL AS int)")},
                {(conReservas ? "res.IdReserva, ISNULL(res.ClienteNombre, ''), ISNULL(res.RazonSocial, ''), ISNULL(res.TipoCapacitacion, ''), res.FechaHora_Grabacion" : "CAST(NULL AS bigint), N'', N'', N'', CAST(NULL AS datetime)")}
            FROM dbo.CAL_EVENTOS e
            {(conRecordatorios ? """
                OUTER APPLY (SELECT MIN(r.MinutosAntes) AS MinutosAntes
                             FROM dbo.CAL_RECORDATORIOS r
                             WHERE r.IdEvento = e.IdEvento AND ISNULL(r.Baja, 0) = 0) rec
                """ : string.Empty)}
            {(conReservas ? """
                OUTER APPLY (SELECT TOP (1) x.IdReserva, x.ClienteNombre, x.RazonSocial, x.TipoCapacitacion, x.FechaHora_Grabacion
                             FROM dbo.CAL_RESERVAS_REUNION x
                             WHERE x.IdEvento = e.IdEvento AND ISNULL(x.Baja, 0) = 0 AND x.Estado = N'CONFIRMADA'
                             ORDER BY x.IdReserva DESC) res
                """ : string.Empty)}
            WHERE ISNULL(e.Baja, 0) = 0
              AND ISNULL(e.Estado, N'') <> N'CANCELADO'
              AND e.FechaFin >= @Ahora
              AND e.FechaInicio <= @Hasta
              AND ({filtroTecnico}{filtroReservaSinTecnico})
            ORDER BY e.FechaInicio;
            """;

        var eventos = new List<AvisoEventoFuente>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        cmd.Parameters.AddWithValue("@Hasta", ahora.AddDays(90));
        for (var i = 0; i < tecnicos.Count; i++)
            cmd.Parameters.AddWithValue(tecnicoParams[i], tecnicos[i]);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            eventos.Add(new AvisoEventoFuente
            {
                IdEvento = rd.GetInt64(0),
                Titulo = rd.GetString(1),
                Tipo = rd.GetString(2),
                FechaInicio = rd.GetDateTime(3),
                FechaFin = rd.GetDateTime(4),
                TodoElDia = rd.GetBoolean(5),
                IdTecnico = rd.GetString(6),
                UsuarioAlta = rd.GetString(7),
                FechaAlta = rd.IsDBNull(8) ? null : rd.GetDateTime(8),
                MinutosAntes = rd.IsDBNull(9) ? null : rd.GetInt32(9),
                IdReserva = rd.IsDBNull(10) ? null : rd.GetInt64(10),
                ReservaCliente = rd.GetString(11),
                ReservaRazonSocial = rd.GetString(12),
                ReservaTipoCapacitacion = rd.GetString(13),
                ReservaFechaAlta = rd.IsDBNull(14) ? null : rd.GetDateTime(14)
            });
        }

        return eventos;
    }

    private static async Task<List<AvisoNovedadFuente>> GetNovedadesAsync(SqlConnection cn, string usuario, DateTime ahora, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (20)
                n.IdNovedad, ISNULL(n.Titulo, ''), ISNULL(n.Bajada, ''), ISNULL(CAST(n.Resumen AS nvarchar(max)), ''),
                ISNULL(n.Version, ''), COALESCE(n.FechaPublicacion, n.FechaProgramada, GETDATE())
            FROM dbo.ALFACORE_NOVEDADES n
            LEFT JOIN dbo.ALFACORE_NOVEDADES_LECTURAS l
                   ON l.IdNovedad = n.IdNovedad
                  AND UPPER(LTRIM(RTRIM(l.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            WHERE n.Estado = N'PUBLICADO'
              AND ISNULL(n.Archivado, 0) = 0
              AND (n.FechaProgramada IS NULL OR n.FechaProgramada <= @Ahora)
              AND (n.VigenteHasta IS NULL OR n.VigenteHasta >= @Ahora)
              AND COALESCE(n.FechaPublicacion, n.FechaProgramada, @Ahora) >= @Desde
              AND l.FechaHoraLeido IS NULL
            ORDER BY COALESCE(n.FechaPublicacion, n.FechaProgramada) DESC;
            """;
        var novedades = new List<AvisoNovedadFuente>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Ahora", ahora);
        cmd.Parameters.AddWithValue("@Desde", ahora.AddDays(-DiasNovedades));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            novedades.Add(new AvisoNovedadFuente
            {
                IdNovedad = rd.GetInt64(0),
                Titulo = rd.GetString(1),
                Bajada = rd.GetString(2),
                Resumen = rd.GetString(3),
                Version = rd.GetString(4),
                FechaPublicacion = rd.GetDateTime(5)
            });
        }
        return novedades;
    }

    private static async Task<HashSet<string>> GetLeidosAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        const string sql = """
            SELECT Clave FROM dbo.ALFACORE_AVISOS_LEIDOS
            WHERE UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
              AND FechaHoraLeido >= DATEADD(DAY, -120, GETDATE());
            """;
        var leidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            leidos.Add(rd.GetString(0));
        return leidos;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, "No se pudieron cargar los avisos.", null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"No se pudieron cargar los avisos. Código: {incidentId}", ex);
        }
    }
}

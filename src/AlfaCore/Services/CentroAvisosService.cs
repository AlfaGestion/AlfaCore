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
/// ya existen —eventos del Calendario del técnico vinculado al usuario, reservas públicas de reuniones,
/// tickets asignados, el tope mensual de créditos de IA y novedades publicadas— y solo se guarda qué marcó leído cada usuario (ALFACORE_AVISOS_LEIDOS).
/// Cada usuario ve únicamente sus propios avisos.
/// </summary>
public sealed class CentroAvisosService(
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    INovedadesService novedadesService,
    IAppEventService appEvents,
    IIaConsumoService? iaConsumo = null) : ICentroAvisosService
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
        var tickets = tablas.Contains("TICK_TICKETS") && tablas.Contains("TICK_ESTADOS") && tecnicos.Count > 0
            ? await GetTicketsAsync(cn, tecnicos, ahora, ct)
            : [];
        var oportunidades = tablas.Contains("CRM_OPORTUNIDADES") && tablas.Contains("CRM_ETAPAS") && tecnicos.Count > 0
            ? await GetOportunidadesAsync(cn, tecnicos, ahora, ct)
            : [];
        var leidos = tablas.Contains("ALFACORE_AVISOS_LEIDOS")
            ? await GetLeidosAsync(cn, usuario, ct)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Nunca lanza y está cacheado: si la central no responde, simplemente no hay aviso de tope.
        var tope = iaConsumo is { Disponible: true } ? await iaConsumo.GetTopeEstadoBaseActivaAsync(ct) : null;

        return ConstruirAvisos(eventos, novedades, tecnicos, usuario, leidos, ahora, tickets, tope, oportunidades);
    }

    /// <summary>
    /// Reglas de la campana. Por evento del técnico del usuario:
    /// reserva pública reciente (7 días) → "Nueva reserva"; evento que otra persona le agendó
    /// (7 días) → "Te agendaron"; guardia → aviso el día anterior y el mismo día; reunión,
    /// capacitación u otro → desde la anticipación de su recordatorio (24 h si no tiene) hasta que
    /// termina. Las reservas de tipos sin técnico asignado se avisan a todos. Tickets abiertos del
    /// técnico con movimiento en los últimos 7 días (salvo los que el usuario se cargó a sí mismo y nadie
    /// tocó); la clave incluye el técnico para que una reasignación vuelva a avisar. Tope de créditos de
    /// IA del mes: un aviso al pasar el porcentaje y otro al alcanzarlo. Oportunidades abiertas del CRM
    /// asignadas al técnico/vendedor del usuario, con la misma regla que los tickets.
    /// </summary>
    internal static IReadOnlyList<AvisoDto> ConstruirAvisos(
        IEnumerable<AvisoEventoFuente> eventos,
        IEnumerable<AvisoNovedadFuente> novedades,
        IReadOnlyCollection<string> tecnicosDelUsuario,
        string usuario,
        IReadOnlySet<string> leidos,
        DateTime ahora,
        IEnumerable<AvisoTicketFuente>? tickets = null,
        IaTopeEstadoDto? topeIa = null,
        IEnumerable<AvisoCrmFuente>? oportunidades = null)
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

        foreach (var t in tickets ?? [])
        {
            var idTecnico = t.IdTecnico.Trim();
            var movimiento = t.FechaModificacion is DateTime mod && mod > t.FechaAlta ? mod : t.FechaAlta;
            if (idTecnico.Length == 0 || !esMio.Contains(idTecnico) || movimiento < ahora.AddDays(-DiasAvisoAlta))
                continue;
            if (t.FechaModificacion is null && string.Equals(t.UsuarioAlta.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var estado = string.IsNullOrWhiteSpace(t.EstadoNombre) ? string.Empty : $" · {t.EstadoNombre.Trim()}";
            avisos.Add(new AvisoDto
            {
                Clave = $"tick:{t.IdTicket}:{idTecnico}",
                Tipo = AvisoTipos.Ticket,
                Titulo = $"Ticket asignado: {t.Titulo.Trim()}",
                Detalle = $"#{t.Numero.ToString(CultureInfo.InvariantCulture)}{estado}",
                FechaHora = movimiento,
                Ruta = $"/tickets?id={t.IdTicket.ToString(CultureInfo.InvariantCulture)}"
            });
        }

        foreach (var o in oportunidades ?? [])
        {
            var idTecnico = o.IdTecnico.Trim();
            var movimiento = o.FechaModificacion is DateTime mod && mod > o.FechaAlta ? mod : o.FechaAlta;
            if (idTecnico.Length == 0 || !esMio.Contains(idTecnico) || movimiento < ahora.AddDays(-DiasAvisoAlta))
                continue;
            if (o.FechaModificacion is null && string.Equals(o.UsuarioAlta.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var cliente = string.IsNullOrWhiteSpace(o.Cliente) ? string.Empty : o.Cliente.Trim();
            var etapa = string.IsNullOrWhiteSpace(o.EtapaNombre) ? string.Empty : o.EtapaNombre.Trim();
            avisos.Add(new AvisoDto
            {
                Clave = $"crm:{o.IdOportunidad}:{idTecnico}",
                Tipo = AvisoTipos.Crm,
                Titulo = $"Oportunidad asignada: {o.Titulo.Trim()}",
                Detalle = string.Join(" · ", new[] { cliente, etapa }.Where(x => x.Length > 0)),
                FechaHora = movimiento,
                Ruta = $"/crm?id={o.IdOportunidad.ToString(CultureInfo.InvariantCulture)}"
            });
        }

        if (topeIa is { TopeCreditos: > 0 } && (topeIa.Alcanzado || topeIa.EnAviso))
        {
            var mes = ahora.ToString("yyyyMM", CultureInfo.InvariantCulture);
            var creditos = topeIa.TopeCreditos.ToString("N0", CultureInfo.GetCultureInfo("es-AR"))
                + (topeIa.TopeCreditos == 1 ? " crédito" : " créditos");
            avisos.Add(new AvisoDto
            {
                Clave = $"ia-tope:{mes}:{(topeIa.Alcanzado ? "tope" : "aviso")}",
                Tipo = AvisoTipos.TopeIa,
                Titulo = topeIa.Alcanzado
                    ? "Se alcanzó el tope de créditos de IA"
                    : $"Usaste el {topeIa.Porcentaje}% del tope de créditos de IA",
                Detalle = topeIa.Alcanzado
                    ? $"Tope de {creditos} · el asistente no responde hasta el mes que viene"
                    : $"Tope mensual de {creditos}",
                FechaHora = ahora.Date,
                Ruta = "/conversaciones/configuracion?seccion=asistente-ia&subseccion=general"
            });
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
                           N'ALFACORE_NOVEDADES', N'ALFACORE_NOVEDADES_LECTURAS', N'ALFACORE_AVISOS_LEIDOS',
                           N'TICK_TICKETS', N'TICK_ESTADOS', N'CRM_OPORTUNIDADES', N'CRM_ETAPAS');
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

    private static async Task<List<AvisoCrmFuente>> GetOportunidadesAsync(
        SqlConnection cn, IReadOnlyList<string> tecnicos, DateTime ahora, CancellationToken ct)
    {
        var tecnicoParams = tecnicos.Select((_, i) => $"@T{i}").ToList();
        var sql = $"""
            SELECT TOP (50)
                o.IdOportunidad, ISNULL(o.Titulo, N''), ISNULL(cli.RAZON_SOCIAL, N''),
                LTRIM(RTRIM(ISNULL(o.IdTecnico, N''))), ISNULL(e.Nombre, N''),
                LTRIM(RTRIM(ISNULL(o.UsuarioAlta, N''))), o.FechaHoraAlta, o.FechaHoraModificacion
            FROM dbo.CRM_OPORTUNIDADES o
            INNER JOIN dbo.CRM_ETAPAS e ON e.IdEtapa = o.IdEtapa
            LEFT JOIN dbo.VT_CLIENTES cli ON LTRIM(RTRIM(cli.Codigo)) = LTRIM(RTRIM(o.ClienteCodigo))
            WHERE ISNULL(o.Baja, 0) = 0
              AND ISNULL(e.EsGanada, 0) = 0
              AND ISNULL(e.EsPerdida, 0) = 0
              AND LTRIM(RTRIM(ISNULL(o.IdTecnico, N''))) IN ({string.Join(", ", tecnicoParams)})
              AND COALESCE(o.FechaHoraModificacion, o.FechaHoraAlta) >= @Desde
            ORDER BY COALESCE(o.FechaHoraModificacion, o.FechaHoraAlta) DESC;
            """;
        var oportunidades = new List<AvisoCrmFuente>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Desde", ahora.AddDays(-DiasAvisoAlta));
        for (var i = 0; i < tecnicos.Count; i++)
            cmd.Parameters.AddWithValue(tecnicoParams[i], tecnicos[i]);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            oportunidades.Add(new AvisoCrmFuente
            {
                IdOportunidad = rd.GetInt64(0),
                Titulo = rd.GetString(1),
                Cliente = rd.GetString(2),
                IdTecnico = rd.GetString(3),
                EtapaNombre = rd.GetString(4),
                UsuarioAlta = rd.GetString(5),
                FechaAlta = rd.GetDateTime(6),
                FechaModificacion = rd.IsDBNull(7) ? null : rd.GetDateTime(7)
            });
        }
        return oportunidades;
    }

    private static async Task<List<AvisoTicketFuente>> GetTicketsAsync(
        SqlConnection cn, IReadOnlyList<string> tecnicos, DateTime ahora, CancellationToken ct)
    {
        var tecnicoParams = tecnicos.Select((_, i) => $"@T{i}").ToList();
        var sql = $"""
            SELECT TOP (50)
                t.IdTicket, t.Numero, ISNULL(t.Titulo, N''), LTRIM(RTRIM(ISNULL(t.IdTecnico, N''))),
                ISNULL(e.Nombre, t.CodigoEstado), LTRIM(RTRIM(ISNULL(t.UsuarioAlta, N''))),
                t.FechaHoraAlta, t.FechaHoraModificacion
            FROM dbo.TICK_TICKETS t
            LEFT JOIN dbo.TICK_ESTADOS e ON e.CodigoEstado = t.CodigoEstado
            WHERE ISNULL(t.Baja, 0) = 0
              AND ISNULL(e.EsCerrado, 0) = 0
              AND LTRIM(RTRIM(ISNULL(t.IdTecnico, N''))) IN ({string.Join(", ", tecnicoParams)})
              AND COALESCE(t.FechaHoraModificacion, t.FechaHoraAlta) >= @Desde
            ORDER BY COALESCE(t.FechaHoraModificacion, t.FechaHoraAlta) DESC;
            """;
        var tickets = new List<AvisoTicketFuente>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Desde", ahora.AddDays(-DiasAvisoAlta));
        for (var i = 0; i < tecnicos.Count; i++)
            cmd.Parameters.AddWithValue(tecnicoParams[i], tecnicos[i]);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            tickets.Add(new AvisoTicketFuente
            {
                IdTicket = rd.GetInt64(0),
                Numero = rd.GetInt32(1),
                Titulo = rd.GetString(2),
                IdTecnico = rd.GetString(3),
                EstadoNombre = rd.GetString(4),
                UsuarioAlta = rd.GetString(5),
                FechaAlta = rd.GetDateTime(6),
                FechaModificacion = rd.IsDBNull(7) ? null : rd.GetDateTime(7)
            });
        }
        return tickets;
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

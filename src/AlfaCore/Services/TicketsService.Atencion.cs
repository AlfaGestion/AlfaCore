using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// "Atender primero": bandejas, plazos y seguimiento de Tickets. Lee de la base lo que hace falta y el cálculo
/// (a quién le toca, plazo, puntaje y motivos) lo hace <see cref="TicketsAtencionCalculo"/>.
/// </summary>
public sealed partial class TicketsService
{
    /// <summary>Tope de tickets que se ordenan en memoria por "Atender primero" o por bandeja.</summary>
    private const int AtencionTope = 2000;

    private static readonly (string Key, string Label, string Icono)[] Bandejas =
    [
        (TicketBandejaKeys.Mios, "Para mí", "bi-person-check"),
        (TicketBandejaKeys.Nosotros, "Esperan respuesta nuestra", "bi-reply"),
        (TicketBandejaKeys.Plazo, "Vencidos y por vencer", "bi-alarm"),
        (TicketBandejaKeys.Prioritarios, "Clientes prioritarios", "bi-star"),
        (TicketBandejaKeys.SinAsignar, "Sin asignar", "bi-person-dash"),
        (TicketBandejaKeys.Seguimientos, "Seguimientos de hoy", "bi-calendar-check"),
        (TicketBandejaKeys.Cliente, "Esperando al cliente", "bi-hourglass-split"),
        (TicketBandejaKeys.Bot, "Creados por el bot", "bi-robot"),
        (TicketBandejaKeys.Abiertos, "Todos los abiertos", "bi-inbox")
    ];

    private static readonly Dictionary<string, Action<TicketsAtencionConfigDto, int>> ClavesPeso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TICKETS-PESO-VENCIDO"] = (c, v) => c.PesoVencido = v,
        ["TICKETS-PESO-POR-VENCER"] = (c, v) => c.PesoPorVencer = v,
        ["TICKETS-PESO-PRIORIDAD"] = (c, v) => c.PesoPrioridad = v,
        ["TICKETS-PESO-CLIENTE-PRIORITARIO"] = (c, v) => c.PesoClientePrioritario = v,
        ["TICKETS-PESO-VARIOS-CASOS"] = (c, v) => c.PesoVariosCasos = v,
        ["TICKETS-PESO-HORA-ESPERA"] = (c, v) => c.PesoHoraEspera = v,
        ["TICKETS-PESO-HORA-ESPERA-TOPE"] = (c, v) => c.PesoHoraEsperaTope = v,
        ["TICKETS-PESO-SEGUIMIENTO-VENCIDO"] = (c, v) => c.PesoSeguimientoVencido = v,
        ["TICKETS-PLAZO-POR-VENCER-PORC"] = (c, v) => c.PorVencerPorcentaje = v,
        ["TICKETS-CLIENTE-VARIOS-CASOS"] = (c, v) => c.VariosCasosDesde = v
    };

    private const string ClaveEstadosEspera = "TICKETS-ESTADOS-ESPERA-CLIENTE";

    public Task<IReadOnlyList<TicketBandejaDto>> GetBandejasAsync(TicketsFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetBandejas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var abiertos = await BuscarParaAtencionAsync(cn, ConBandeja(filters, string.Empty), "t.FechaHoraAlta DESC", token);
            var activos = abiertos.Where(t => !string.IsNullOrEmpty(t.EsperandoA)).ToList();

            return (IReadOnlyList<TicketBandejaDto>)Bandejas
                .Where(b => b.Key != TicketBandejaKeys.Mios || !string.IsNullOrWhiteSpace(filters?.IdTecnicoActual))
                .Select(b =>
                {
                    var enBandeja = activos.Where(t => TicketsAtencionCalculo.EnBandeja(t, b.Key, filters?.IdTecnicoActual)).ToList();
                    return new TicketBandejaDto
                    {
                        Key = b.Key,
                        Label = b.Label,
                        Icono = b.Icono,
                        Cantidad = enBandeja.Count,
                        Alerta = b.Key != TicketBandejaKeys.Cliente
                                 && enBandeja.Any(t => t.EsperandoA == TicketEsperandoA.Nosotros
                                                       && (t.PlazoEstado == TicketPlazoEstados.Vencido || t.SeguimientoVencido))
                    };
                })
                .ToList();
        }, "No se pudieron contar los tickets por bandeja.", ct);

    public Task SetSeguimientoAsync(TicketSeguimientoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SetSeguimiento", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdTicket <= 0)
                throw new InvalidOperationException("No se recibió el ticket.");

            var user = NormalizeUser(request.UsuarioAccion);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            const string sql = """
                UPDATE dbo.TICK_TICKETS
                SET FechaSeguimiento = @Fecha,
                    SeguimientoDesde = CASE WHEN @Fecha IS NULL THEN NULL ELSE GETDATE() END,
                    FechaHoraModificacion = GETDATE()
                WHERE IdTicket = @IdTicket AND ISNULL(Baja, 0) = 0;
                """;
            await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdTicket", request.IdTicket);
                cmd.Parameters.AddWithValue("@Fecha", request.Fecha.HasValue ? request.Fecha.Value : DBNull.Value);
                if (await cmd.ExecuteNonQueryAsync(token) == 0)
                    throw new InvalidOperationException("El ticket seleccionado ya no existe en la base activa.");
            }

            var texto = request.Fecha is DateTime f
                ? $"Seguimiento programado para el {f.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}."
                : "Se quitó el seguimiento programado.";
            await InsertActivityAsync(cn, (SqlTransaction)tx, request.IdTicket, "SEGUIMIENTO", texto, user, token);
            await tx.CommitAsync(token);
        }, "No se pudo programar el seguimiento del ticket.", ct);

    public Task<TicketsAtencionConfigDto> GetAtencionConfigAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetAtencionConfig", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await LeerAtencionConfigAsync(cn, token);
        }, "No se pudo leer la configuración de plazos de tickets.", ct);

    public Task SaveAtencionConfigAsync(TicketsAtencionConfigDto config, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveAtencionConfig", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            foreach (var plazo in config.Plazos.Where(p => p.Prioridad is >= 0 and <= 3))
            {
                const string sqlPlazo = """
                    UPDATE dbo.TICK_PLAZOS
                    SET HorasRespuesta = @Respuesta, HorasResolucion = @Resolucion, Activo = 1, FechaHora_Modificacion = GETDATE()
                    WHERE Prioridad = @Prioridad;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.TICK_PLAZOS (Prioridad, HorasRespuesta, HorasResolucion, FechaHora_Modificacion)
                        VALUES (@Prioridad, @Respuesta, @Resolucion, GETDATE());
                    """;
                await using var cmd = new SqlCommand(sqlPlazo, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@Prioridad", plazo.Prioridad);
                cmd.Parameters.AddWithValue("@Respuesta", Math.Clamp(plazo.HorasRespuesta, 0, 10000));
                cmd.Parameters.AddWithValue("@Resolucion", Math.Clamp(plazo.HorasResolucion, 0, 10000));
                await cmd.ExecuteNonQueryAsync(token);
            }

            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TICKETS-PESO-VENCIDO"] = Entero(config.PesoVencido),
                ["TICKETS-PESO-POR-VENCER"] = Entero(config.PesoPorVencer),
                ["TICKETS-PESO-PRIORIDAD"] = Entero(config.PesoPrioridad),
                ["TICKETS-PESO-CLIENTE-PRIORITARIO"] = Entero(config.PesoClientePrioritario),
                ["TICKETS-PESO-VARIOS-CASOS"] = Entero(config.PesoVariosCasos),
                ["TICKETS-PESO-HORA-ESPERA"] = Entero(config.PesoHoraEspera),
                ["TICKETS-PESO-HORA-ESPERA-TOPE"] = Entero(config.PesoHoraEsperaTope),
                ["TICKETS-PESO-SEGUIMIENTO-VENCIDO"] = Entero(config.PesoSeguimientoVencido),
                ["TICKETS-PLAZO-POR-VENCER-PORC"] = Math.Clamp(config.PorVencerPorcentaje, 1, 100).ToString(CultureInfo.InvariantCulture),
                ["TICKETS-CLIENTE-VARIOS-CASOS"] = Math.Clamp(config.VariosCasosDesde, 2, 100).ToString(CultureInfo.InvariantCulture),
                [ClaveEstadosEspera] = string.Join(",", TicketsAtencionCalculo.EstadosEspera(config))
            };

            await GuardarClavesAsync(cn, (SqlTransaction)tx, valores, token);
            await tx.CommitAsync(token);
        }, "No se pudo guardar la configuración de plazos de tickets.", ct);

    /// <summary>Graba claves del grupo TICKETS en TA_CONFIGURACION (actualiza o inserta; nunca borra).</summary>
    private static async Task GuardarClavesAsync(SqlConnection cn, SqlTransaction tx, IReadOnlyDictionary<string, string> valores, CancellationToken ct)
    {
        foreach (var (clave, valor) in valores)
        {
            const string sqlClave = """
                UPDATE dbo.TA_CONFIGURACION
                SET VALOR = @Valor, GRUPO = @Grupo, FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO, FechaHora_Grabacion, FechaHora_Modificacion)
                    VALUES (@Clave, @Valor, @Grupo, GETDATE(), GETDATE());
                """;
            await using var cmd = new SqlCommand(sqlClave, cn, tx);
            cmd.Parameters.AddWithValue("@ClaveNormalizada", clave.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@Clave", clave);
            cmd.Parameters.AddWithValue("@Valor", valor);
            cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static string Entero(int valor) => Math.Clamp(valor, 0, 10000).ToString(CultureInfo.InvariantCulture);

    private static TicketsFilters ConBandeja(TicketsFilters? filters, string bandeja)
    {
        var f = filters ?? new TicketsFilters();
        return new TicketsFilters
        {
            Texto = f.Texto,
            CodigoEstado = f.CodigoEstado,
            IdTecnico = f.IdTecnico,
            Prioridad = f.Prioridad,
            IncluirCerrados = f.IncluirCerrados,
            TieneAsignado = f.TieneAsignado,
            TieneMensajes = f.TieneMensajes,
            ClienteCodigo = f.ClienteCodigo,
            IdContacto = f.IdContacto,
            IdEtiqueta = f.IdEtiqueta,
            FechaRapida = f.FechaRapida,
            Reglas = f.Reglas,
            OrdenarPor = f.OrdenarPor,
            OrdenDescendente = f.OrdenDescendente,
            PageNumber = f.PageNumber,
            PageSize = f.PageSize,
            Bandeja = bandeja,
            IdTecnicoActual = f.IdTecnicoActual
        };
    }

    /// <summary>
    /// Hasta <see cref="AtencionTope"/> tickets con los filtros de la pantalla, con los datos de atención ya
    /// calculados. Con bandeja, solo abiertos y solo los de esa bandeja.
    /// </summary>
    private async Task<List<TicketGridItemDto>> BuscarParaAtencionAsync(SqlConnection cn, TicketsFilters filters, string orderBy, CancellationToken ct)
    {
        var conBandeja = !string.IsNullOrWhiteSpace(filters.Bandeja);
        var sql = $"""
            {TicketListSelectSql()}
            {TicketWhereSql()}
            {(conBandeja ? "AND ISNULL(e.EsCerrado, 0) = 0" : string.Empty)}
            ORDER BY {orderBy}, t.IdTicket DESC
            OFFSET 0 ROWS FETCH NEXT {AtencionTope} ROWS ONLY;
            """;

        var rows = new List<TicketGridItemDto>();
        await using (var cmd = new SqlCommand(sql, cn))
        {
            AddFilterParameters(cmd, filters);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                rows.Add(ReadTicketGridItem(rd));
        }

        await EnriquecerAtencionAsync(cn, rows, ct);
        return conBandeja
            ? rows.Where(t => !string.IsNullOrEmpty(t.EsperandoA)
                              && TicketsAtencionCalculo.EnBandeja(t, filters.Bandeja, filters.IdTecnicoActual)).ToList()
            : rows;
    }

    /// <summary>Completa los datos de atención (solo tickets abiertos). Si la base todavía no tiene el script, no hace nada.</summary>
    private async Task EnriquecerAtencionAsync(SqlConnection cn, IReadOnlyList<TicketGridItemDto> rows, CancellationToken ct)
    {
        var abiertos = rows.Where(r => r.FechaHoraCierre is null).ToList();
        if (abiertos.Count == 0)
            return;

        var esquema = await LeerEsquemaAtencionAsync(cn, ct);
        if (!esquema.TieneSeguimiento)
            return;

        var config = await LeerAtencionConfigAsync(cn, ct);
        var horario = await LeerHorarioAsync(ct);
        var clasificacionesPrioritarias = await LeerClasificacionesPrioritariasAsync(ct);
        var estadosEspera = TicketsAtencionCalculo.EstadosEspera(config);

        var datos = await LeerDatosTicketsAsync(cn, esquema, abiertos.Select(r => r.IdTicket).ToList(), ct);
        var periodos = esquema.TieneHistorial
            ? await LeerPeriodosEsperaAsync(cn, abiertos.Select(r => r.IdTicket).ToList(), estadosEspera, ct)
            : new Dictionary<long, IReadOnlyList<(DateTime Desde, DateTime? Hasta)>>();
        var clientes = await LeerClientesAsync(cn, abiertos.Select(r => r.ClienteCodigo).ToList(), ct);
        var casosPorCliente = await LeerCasosAbiertosPorClienteAsync(cn, ct);

        foreach (var ticket in abiertos)
        {
            datos.TryGetValue(ticket.IdTicket, out var d);
            var codigo = ticket.ClienteCodigo.Trim().ToUpperInvariant();
            clientes.TryGetValue(codigo, out var cliente);
            casosPorCliente.TryGetValue(codigo, out var casos);

            var periodosTicket = periodos.TryGetValue(ticket.IdTicket, out var p) ? p : [];
            // Tickets en espera desde antes de que existiera el historial: la espera arranca en la última modificación.
            if (estadosEspera.Contains(ticket.CodigoEstado) && !periodosTicket.Any(x => x.Hasta is null))
                periodosTicket = [.. periodosTicket, (ticket.FechaHoraModificacion ?? ticket.FechaHoraAlta, (DateTime?)null)];

            var abono = cliente.Abono ?? string.Empty;
            var prioritario = string.Equals(abono, ConversacionAbonoEstados.Abonado, StringComparison.OrdinalIgnoreCase)
                              || (!string.IsNullOrWhiteSpace(cliente.Clasificacion) && clasificacionesPrioritarias.Contains(cliente.Clasificacion));
            ticket.ClienteClasificacion = cliente.Clasificacion ?? string.Empty;

            TicketsAtencionCalculo.Aplicar(ticket, new TicketAtencionDatos
            {
                CodigoEstado = ticket.CodigoEstado,
                Prioridad = ticket.Prioridad,
                FechaAlta = ticket.FechaHoraAlta,
                TieneConversacion = esquema.TieneConversaciones && ticket.IdConversacion.HasValue,
                UltimoMensajeCliente = d?.UltimoCliente,
                PrimerMensajeClienteSinResponder = d?.PrimerSinResponder,
                UltimaRespuestaNuestra = d?.UltimaNuestra,
                PrimeraRespuestaNuestra = d?.PrimeraNuestra,
                FechaSeguimiento = d?.FechaSeguimiento,
                SeguimientoDesde = d?.SeguimientoDesde,
                PeriodosEspera = periodosTicket,
                ClientePrioritario = prioritario,
                EstadoAbono = string.IsNullOrWhiteSpace(abono) ? ConversacionAbonoEstados.SinAbono : abono,
                CasosAbiertosCliente = codigo.Length == 0 ? 0 : casos
            }, config, horario, esquema.Ahora);
        }
    }

    private sealed record EsquemaAtencion(bool TieneSeguimiento, bool TieneHistorial, bool TieneInformes, bool TieneConversaciones, DateTime Ahora);

    private sealed record DatosTicket(DateTime? FechaSeguimiento, DateTime? SeguimientoDesde, DateTime? UltimoCliente,
        DateTime? UltimaNuestra, DateTime? PrimeraNuestra, DateTime? PrimerSinResponder);

    private static async Task<EsquemaAtencion> LeerEsquemaAtencionAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT
                CASE WHEN COL_LENGTH(N'dbo.TICK_TICKETS', N'FechaSeguimiento') IS NULL OR COL_LENGTH(N'dbo.TICK_TICKETS', N'SeguimientoDesde') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.TICK_ESTADOS_HIST', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.TICK_INFORME_RESPUESTAS', N'U') IS NULL OR OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.CONV_MENSAJES', N'U') IS NULL OR COL_LENGTH(N'dbo.CONV_MENSAJES', N'AutorAutomatico') IS NULL THEN 0 ELSE 1 END,
                GETDATE();
            """;
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        return new EsquemaAtencion(GetInt(rd, 0) == 1, GetInt(rd, 1) == 1, GetInt(rd, 2) == 1, GetInt(rd, 3) == 1, rd.GetDateTime(4));
    }

    private static async Task<TicketsAtencionConfigDto> LeerAtencionConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var config = new TicketsAtencionConfigDto();
        const string sql = """
            IF OBJECT_ID(N'dbo.TICK_PLAZOS', N'U') IS NOT NULL
                SELECT Prioridad, HorasRespuesta, HorasResolucion FROM dbo.TICK_PLAZOS WHERE ISNULL(Activo, 1) = 1 ORDER BY Prioridad;
            ELSE
                SELECT CAST(NULL AS int), CAST(NULL AS int), CAST(NULL AS int) WHERE 1 = 0;

            SELECT UPPER(LTRIM(RTRIM(CLAVE))), LTRIM(RTRIM(ISNULL(VALOR, '')))
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) LIKE 'TICKETS-%';
            """;
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            config.Plazos.Add(new TicketPlazoDto { Prioridad = GetInt(rd, 0), HorasRespuesta = GetInt(rd, 1), HorasResolucion = GetInt(rd, 2) });

        if (await rd.NextResultAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                var clave = GetString(rd, 0);
                var valor = GetString(rd, 1);
                if (string.Equals(clave, ClaveEstadosEspera, StringComparison.OrdinalIgnoreCase))
                {
                    if (valor.Length > 0)
                        config.EstadosEsperaCliente = valor;
                }
                else if (ClavesPeso.TryGetValue(clave, out var asignar) && int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
                {
                    asignar(config, numero);
                }
            }
        }

        if (config.Plazos.Count == 0)
        {
            // Sin tabla todavía: los mismos valores sugeridos del script.
            config.Plazos =
            [
                new() { Prioridad = 0, HorasRespuesta = 16, HorasResolucion = 80 },
                new() { Prioridad = 1, HorasRespuesta = 8, HorasResolucion = 40 },
                new() { Prioridad = 2, HorasRespuesta = 4, HorasResolucion = 24 },
                new() { Prioridad = 3, HorasRespuesta = 2, HorasResolucion = 8 }
            ];
        }
        return config;
    }

    private async Task<HorarioLaboral> LeerHorarioAsync(CancellationToken ct)
    {
        try
        {
            if (conversacionesConfig is not null)
                return HorarioLaboral.Desde(await conversacionesConfig.GetAutomatizacionesConfigAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "HorarioAtencion", ex,
                "No se pudo leer el horario de atención; los plazos de tickets se calculan con lunes a viernes de 9 a 18.",
                null, AppEventSeverity.Warning, ct);
        }
        return HorarioLaboral.Desde(new ConversacionAutomatizacionesConfigDto());
    }

    private async Task<HashSet<string>> LeerClasificacionesPrioritariasAsync(CancellationToken ct)
    {
        try
        {
            if (conversacionesConfig is not null)
            {
                var prio = await conversacionesConfig.GetPrioridadConfigAsync(ct);
                return new[] { prio.Clasifica1, prio.Clasifica2, prio.Clasifica3 }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "ClientesPrioritarios", ex,
                "No se pudieron leer las clasificaciones prioritarias; en Tickets solo cuenta el abono.",
                null, AppEventSeverity.Warning, ct);
        }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<long, DatosTicket>> LeerDatosTicketsAsync(SqlConnection cn, EsquemaAtencion esquema, List<long> ids, CancellationToken ct)
    {
        var humano = ConversacionesService.SalienteHumanoSql("m");
        var conv = esquema.TieneConversaciones;
        var inf = esquema.TieneInformes;
        var hist = esquema.TieneHistorial;
        const string sinFecha = "SELECT CAST(NULL AS datetime) AS F";

        string Union(params string?[] partes)
            => string.Join(" UNION ALL ", new[] { sinFecha }.Concat(partes.Where(x => !string.IsNullOrWhiteSpace(x))!)); // el primero nombra la columna F

        var ultimoCliente = Union(
            conv ? "SELECT MAX(m.FechaHora) AS F FROM dbo.CONV_MENSAJES m WHERE t.IdConversacion IS NOT NULL AND m.IdConversacion = t.IdConversacion AND m.Direction = N'ENTRANTE' AND m.FechaHora > t.FechaHoraAlta" : null,
            inf ? "SELECT MAX(r.FechaHora) FROM dbo.TICK_INFORME_RESPUESTAS r INNER JOIN dbo.TICK_INFORMES i ON i.IdInforme = r.IdInforme WHERE i.IdTicket = t.IdTicket AND r.Tipo = 'CONSULTA'" : null);
        var ultimaNuestra = Union(
            conv ? $"SELECT MAX(m.FechaHora) FROM dbo.CONV_MENSAJES m WHERE t.IdConversacion IS NOT NULL AND m.IdConversacion = t.IdConversacion AND {humano} AND m.FechaHora > t.FechaHoraAlta" : null,
            inf ? "SELECT MAX(i.FechaHoraPublicacion) FROM dbo.TICK_INFORMES i WHERE i.IdTicket = t.IdTicket AND ISNULL(i.Baja, 0) = 0 AND i.Estado = 'PUBLICADO'" : null);
        var primeraNuestra = Union(
            conv ? $"SELECT MIN(m.FechaHora) FROM dbo.CONV_MENSAJES m WHERE t.IdConversacion IS NOT NULL AND m.IdConversacion = t.IdConversacion AND {humano} AND m.FechaHora > t.FechaHoraAlta" : null,
            "SELECT MIN(a.FechaHora) FROM dbo.TICK_ACTIVIDAD a WHERE a.IdTicket = t.IdTicket AND a.TipoActividad IN (N'NOTA', N'CAMBIO', N'EDICION') AND ISNULL(a.Usuario, N'') NOT IN (N'', N'AlfaCore') AND a.FechaHora > t.FechaHoraAlta",
            hist ? "SELECT MIN(h.FechaHora) FROM dbo.TICK_ESTADOS_HIST h WHERE h.IdTicket = t.IdTicket AND h.EstadoAnterior IS NOT NULL AND ISNULL(h.Usuario, N'') NOT IN (N'', N'AlfaCore')" : null,
            inf ? "SELECT MIN(i.FechaHoraPublicacion) FROM dbo.TICK_INFORMES i WHERE i.IdTicket = t.IdTicket AND ISNULL(i.Baja, 0) = 0 AND i.Estado = 'PUBLICADO'" : null);
        var sinResponder = conv
            ? "SELECT MIN(m.FechaHora) AS F FROM dbo.CONV_MENSAJES m WHERE t.IdConversacion IS NOT NULL AND m.IdConversacion = t.IdConversacion AND m.Direction = N'ENTRANTE' AND m.FechaHora > t.FechaHoraAlta AND m.FechaHora > ISNULL(nos.F, t.FechaHoraAlta)"
            : sinFecha;

        var result = new Dictionary<long, DatosTicket>();
        foreach (var lote in ids.Chunk(500))
        {
            var sql = $"""
                SELECT t.IdTicket, t.FechaSeguimiento, t.SeguimientoDesde, cli.F, nos.F, pri.F, sr.F
                FROM dbo.TICK_TICKETS t
                OUTER APPLY (SELECT MAX(x.F) AS F FROM ({ultimoCliente}) x) cli
                OUTER APPLY (SELECT MAX(x.F) AS F FROM ({ultimaNuestra}) x) nos
                OUTER APPLY (SELECT MIN(x.F) AS F FROM ({primeraNuestra}) x) pri
                OUTER APPLY ({sinResponder}) sr
                WHERE t.IdTicket IN ({string.Join(", ", lote.Select(id => id.ToString(CultureInfo.InvariantCulture)))});
                """;
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                result[rd.GetInt64(0)] = new DatosTicket(Fecha(rd, 1), Fecha(rd, 2), Fecha(rd, 3), Fecha(rd, 4), Fecha(rd, 5), Fecha(rd, 6));
            }
        }
        return result;
    }

    private static async Task<Dictionary<long, IReadOnlyList<(DateTime Desde, DateTime? Hasta)>>> LeerPeriodosEsperaAsync(
        SqlConnection cn, List<long> ids, HashSet<string> estadosEspera, CancellationToken ct)
    {
        var result = new Dictionary<long, IReadOnlyList<(DateTime, DateTime?)>>();
        if (estadosEspera.Count == 0)
            return result;

        var cambios = new Dictionary<long, List<(string Estado, DateTime Fecha)>>();
        foreach (var lote in ids.Chunk(500))
        {
            var sql = $"""
                SELECT IdTicket, UPPER(LTRIM(RTRIM(EstadoNuevo))), FechaHora
                FROM dbo.TICK_ESTADOS_HIST
                WHERE IdTicket IN ({string.Join(", ", lote.Select(id => id.ToString(CultureInfo.InvariantCulture)))})
                ORDER BY IdTicket, FechaHora, IdHist;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt64(0);
                if (!cambios.TryGetValue(id, out var lista))
                    cambios[id] = lista = [];
                lista.Add((GetString(rd, 1), rd.GetDateTime(2)));
            }
        }

        foreach (var (id, lista) in cambios)
        {
            var periodos = new List<(DateTime, DateTime?)>();
            DateTime? inicio = null;
            foreach (var (estado, fecha) in lista)
            {
                var esEspera = estadosEspera.Contains(estado);
                if (esEspera && inicio is null)
                    inicio = fecha;
                else if (!esEspera && inicio is DateTime desde)
                {
                    periodos.Add((desde, fecha));
                    inicio = null;
                }
            }
            if (inicio is DateTime abierto)
                periodos.Add((abierto, null));
            result[id] = periodos;
        }
        return result;
    }

    private static async Task<Dictionary<string, (string? Clasificacion, string? Abono)>> LeerClientesAsync(SqlConnection cn, List<string> codigos, CancellationToken ct)
    {
        var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        var distintos = codigos.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        foreach (var lote in distintos.Chunk(500))
        {
            var parametros = lote.Select((_, i) => $"@C{i}").ToList();
            var sql = $"""
                SELECT k.Codigo, MAX(ISNULL(LTRIM(RTRIM(cli.Clasificacion)), N'')), MAX(ab.Estado)
                FROM (SELECT UPPER(LTRIM(RTRIM(CODIGO))) AS Codigo, Clasificacion
                      FROM dbo.VT_CLIENTES
                      WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN ({string.Join(", ", parametros)})) cli
                CROSS APPLY (SELECT cli.Codigo AS Codigo) k
                {ClienteAtencionSql.AbonoApply("k.Codigo", "ab")}
                GROUP BY k.Codigo;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            for (var i = 0; i < lote.Length; i++)
                cmd.Parameters.AddWithValue(parametros[i], lote[i]);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                result[GetString(rd, 0)] = (GetString(rd, 1), rd.IsDBNull(2) ? null : GetString(rd, 2));
        }
        return result;
    }

    private static async Task<Dictionary<string, int>> LeerCasosAbiertosPorClienteAsync(SqlConnection cn, CancellationToken ct)
    {
        var sql = $"""
            SELECT x.Codigo, COUNT(*)
            FROM (
                SELECT UPPER(COALESCE(NULLIF(LTRIM(RTRIM(t.ClienteCodigo)), ''), contactoCuenta.Cuenta)) AS Codigo
                {TicketBaseFromSql(includeMessageCount: false)}
                WHERE ISNULL(t.Baja, 0) = 0 AND ISNULL(e.EsCerrado, 0) = 0
            ) x
            WHERE ISNULL(x.Codigo, '') <> ''
            GROUP BY x.Codigo;
            """;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result[GetString(rd, 0)] = GetInt(rd, 1);
        return result;
    }

    private static DateTime? Fecha(SqlDataReader rd, int index) => rd.IsDBNull(index) ? null : rd.GetDateTime(index);
}

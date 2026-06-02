using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class ConversacionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IHttpClientFactory httpClientFactory,
    IConversacionesConfigService conversacionesConfigService,
    INotificacionesPushService notificacionesPushService,
    IWebHostEnvironment environment) : IConversacionesService
{
    private readonly IAppEventService _appEvents = appEvents;
    private readonly INotificacionesPushService _notificacionesPushService = notificacionesPushService;
    private const string ManualWhatsAppConversationSummary = "Conversaci\u00f3n iniciada manualmente.";
    private const string ManualWhatsAppInitialState = "PENDIENTE";
    private const string InternalEventDirection = "NOTA_INTERNA";
    private const string InternalEventMessageType = "SYSTEM";
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<long, byte> MediaHydrationAttempts = new();
    private static readonly ConcurrentDictionary<long, byte> AttachmentRecoveryFailures = new();
    private static readonly ConcurrentDictionary<string, TypingPresence> TypingPresences = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DebtSourceCandidates =
    [
        "VE_CPTES_SALDOS_VENTAS",
        "VE_CPTES_IMPAGOS",
        "VE_CPTES_SALDOS",
        "VE_CPTES_SALDOS_TODOS_SALDO",
        "VE_CPTES_SALDOS_TODOS"
    ];

    private string UploadsBasePath => Path.Combine(environment.ContentRootPath, "App_Data", "uploads", "conversaciones");
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<bool> HasConversationSchemaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "HasConversationSchema", async token =>
        {
            const string sql = """
                SELECT
                    CASE
                        WHEN OBJECT_ID(N'dbo.CONV_CONVERSACIONES', N'U') IS NOT NULL
                         AND OBJECT_ID(N'dbo.CONV_ESTADOS', N'U') IS NOT NULL
                        THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            var result = await cmd.ExecuteScalarAsync(token);
            return result is not null && Convert.ToBoolean(result);
        }, "No se pudo verificar el esquema de conversaciones.", ct);

    public Task<IReadOnlyList<ConversacionTecnicoOptionDto>> GetTechniciansAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetTechnicians", async token =>
        {
            const string sql = """
                SELECT
                    LTRIM(RTRIM(ISNULL(IdTecnico, ''))),
                    ISNULL(Nombre, ''),
                    ISNULL(Cargo, ''),
                    ISNULL(UsuarioAsociado, ''),
                    ISNULL(SistemaAsociado, '')
                FROM dbo.V_TA_Tecnicos
                WHERE ISNULL(Baja, 0) = 0
                ORDER BY Nombre
                """;

            var items = new List<ConversacionTecnicoOptionDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionTecnicoOptionDto
                {
                    IdTecnico = GetString(rd, 0),
                    Nombre = GetString(rd, 1),
                    Cargo = GetString(rd, 2),
                    UsuarioAsociado = GetString(rd, 3),
                    SistemaAsociado = GetString(rd, 4)
                });
            }

            return (IReadOnlyList<ConversacionTecnicoOptionDto>)items;
        }, "No se pudieron cargar los técnicos.", ct);

    public Task<IReadOnlyList<ConversacionEstadoOptionDto>> GetStatesAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetStates", async token =>
        {
            const string sql = """
                SELECT
                    ISNULL(CodigoEstado, ''),
                    ISNULL(Descripcion, ''),
                    ISNULL(EsCerrado, 0),
                    ISNULL(Orden, 0)
                FROM dbo.CONV_ESTADOS
                WHERE ISNULL(Activo, 1) = 1
                ORDER BY Orden, Descripcion
                """;

            var items = new List<ConversacionEstadoOptionDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionEstadoOptionDto
                {
                    CodigoEstado = GetString(rd, 0),
                    Descripcion = GetString(rd, 1),
                    EsCerrado = GetInt(rd, 2) == 1,
                    Orden = GetInt(rd, 3)
                });
            }

            return (IReadOnlyList<ConversacionEstadoOptionDto>)items;
        }, "No se pudieron cargar los estados de conversación.", ct);

    public Task<ConversacionesEstadisticasDto> GetEstadisticasAsync(ConversacionesEstadisticasFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetEstadisticas", async token =>
        {
            ArgumentNullException.ThrowIfNull(filters);

            var desde = filters.Desde.Date;
            var hastaInclusive = filters.Hasta.Date;
            if (hastaInclusive < desde)
                (desde, hastaInclusive) = (hastaInclusive, desde);

            var hastaExclusive = hastaInclusive.AddDays(1);
            var technicianId = NormalizeTechnicianId(filters.IdTecnico);

            const string sql = """
                DECLARE @Tickets TABLE
                (
                    IdTicket bigint NOT NULL,
                    IdConversacion bigint NULL,
                    FechaHoraAlta datetime NOT NULL,
                    IdTecnico nvarchar(20) NULL
                );

                IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO @Tickets (IdTicket, IdConversacion, FechaHoraAlta, IdTecnico)
                    EXEC sp_executesql
                        N'SELECT IdTicket, IdConversacion, FechaHoraAlta, IdTecnico
                          FROM dbo.TICK_TICKETS
                          WHERE ISNULL(Baja, 0) = 0
                            AND FechaHoraAlta >= @Desde
                            AND FechaHoraAlta < @HastaExclusive',
                        N'@Desde datetime, @HastaExclusive datetime',
                        @Desde = @Desde,
                        @HastaExclusive = @HastaExclusive;
                END;

                WITH BaseRaw AS
                (
                    SELECT
                        c.*,
                        ISNULL(e.Descripcion, c.CodigoEstado) AS EstadoDescripcion,
                        ISNULL(e.EsCerrado, 0) AS EsCerrado,
                        ISNULL(e.Orden, 999) AS EstadoOrden,
                        CASE
                            WHEN c.IdContacto IS NOT NULL THEN N'CONTACTO:' + CONVERT(nvarchar(30), c.IdContacto)
                            WHEN NULLIF(LTRIM(RTRIM(ISNULL(c.TelefonoWhatsApp, N''))), N'') IS NOT NULL THEN N'TEL:' + LTRIM(RTRIM(c.TelefonoWhatsApp))
                            ELSE N'CONV:' + CONVERT(nvarchar(30), c.IdConversacion)
                        END AS DedupeKey
                    FROM dbo.CONV_CONVERSACIONES c
                    LEFT JOIN dbo.CONV_ESTADOS e ON e.CodigoEstado = c.CodigoEstado
                    WHERE c.Canal = N'WHATSAPP'
                      AND (@IdTecnico IS NULL OR c.IdTecnico = @IdTecnico)
                      AND NOT (
                          ISNULL(c.ResumenUltimoMensaje, N'') = @ManualWhatsAppConversationSummary
                          AND NOT EXISTS (
                              SELECT 1
                              FROM dbo.CONV_MENSAJES msg
                              WHERE msg.IdConversacion = c.IdConversacion
                          )
                      )
                ),
                BaseRanked AS
                (
                    SELECT
                        *,
                        ROW_NUMBER() OVER (
                            PARTITION BY DedupeKey
                            ORDER BY ISNULL(FechaHoraUltimoMensaje, FechaHora_Grabacion) DESC, IdConversacion DESC
                        ) AS DedupeRank
                    FROM BaseRaw
                )
                SELECT *
                INTO #BaseConversaciones
                FROM BaseRanked
                WHERE DedupeRank = 1;

                SELECT m.*
                INTO #MensajesRango
                FROM dbo.CONV_MENSAJES m
                INNER JOIN #BaseConversaciones c ON c.IdConversacion = m.IdConversacion
                WHERE m.FechaHora >= @Desde
                  AND m.FechaHora < @HastaExclusive
                  AND (@IdTecnico IS NULL OR c.IdTecnico = @IdTecnico OR m.IdTecnicoAutor = @IdTecnico);

                WITH
                EntrantesRango AS
                (
                    SELECT IdConversacion, MIN(FechaHora) AS PrimerEntrante
                    FROM #MensajesRango
                    WHERE Direction = N'ENTRANTE'
                    GROUP BY IdConversacion
                ),
                PrimerasRespuestas AS
                (
                    SELECT e.IdConversacion, e.PrimerEntrante, MIN(m.FechaHora) AS PrimeraRespuesta
                    FROM EntrantesRango e
                    INNER JOIN dbo.CONV_MENSAJES m
                        ON m.IdConversacion = e.IdConversacion
                       AND m.Direction = N'SALIENTE'
                       AND m.FechaHora >= e.PrimerEntrante
                    GROUP BY e.IdConversacion, e.PrimerEntrante
                ),
                CierresRango AS
                (
                    SELECT c.IdConversacion, c.FechaHoraPrimerMensaje, c.FechaHora_Grabacion, c.FechaHoraCierre
                    FROM #BaseConversaciones c
                    WHERE c.FechaHoraCierre >= @Desde
                      AND c.FechaHoraCierre < @HastaExclusive
                ),
                EventosRango AS
                (
                    SELECT m.*
                    FROM #MensajesRango m
                    WHERE m.Direction = N'NOTA_INTERNA'
                      AND m.MessageType = N'SYSTEM'
                )
                SELECT
                    (SELECT COUNT(1) FROM #BaseConversaciones c),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE c.CodigoEstado = N'ABIERTA'),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE c.CodigoEstado = N'PENDIENTE'),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE c.CodigoEstado = N'EN_GESTION'),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE c.EsCerrado = 1),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE (ISNULL(c.Archivada, 0) = 1 OR c.CodigoEstado = N'ARCHIVADA')),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE NULLIF(LTRIM(RTRIM(ISNULL(c.IdTecnico, N''))), N'') IS NULL),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE NULLIF(LTRIM(RTRIM(ISNULL(c.IdTecnico, N''))), N'') IS NOT NULL),
                    (SELECT COUNT(1) FROM #BaseConversaciones c WHERE c.FechaHora_Grabacion >= @Desde AND c.FechaHora_Grabacion < @HastaExclusive),
                    (SELECT COUNT(DISTINCT IdConversacion) FROM #MensajesRango),
                    (SELECT COUNT(DISTINCT COALESCE(NULLIF(c.ClienteCodigo, N''), NULLIF(c.TelefonoWhatsApp, N''), CONVERT(nvarchar(30), c.IdConversacion)))
                     FROM #BaseConversaciones c
                     WHERE EXISTS (SELECT 1 FROM #MensajesRango m WHERE m.IdConversacion = c.IdConversacion)),
                    (SELECT COUNT(1) FROM EventosRango WHERE Texto LIKE N'%cambió el estado de Cerrada a%' OR Texto LIKE N'%cambio el estado de Cerrada a%'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'ENTRANTE'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'SALIENTE'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'NOTA_INTERNA'),
                    (SELECT COUNT(DISTINCT a.IdMensaje) FROM dbo.CONV_ADJUNTOS a INNER JOIN #MensajesRango m ON m.IdMensaje = a.IdMensaje),
                    (SELECT COUNT(1) FROM PrimerasRespuestas WHERE PrimeraRespuesta IS NOT NULL),
                    (SELECT COUNT(1) FROM EntrantesRango e WHERE NOT EXISTS (SELECT 1 FROM PrimerasRespuestas r WHERE r.IdConversacion = e.IdConversacion AND r.PrimeraRespuesta IS NOT NULL)),
                    (SELECT COUNT(1) FROM CierresRango),
                    (SELECT COUNT(1) FROM @Tickets t LEFT JOIN #BaseConversaciones c ON c.IdConversacion = t.IdConversacion WHERE c.IdConversacion IS NOT NULL AND (@IdTecnico IS NULL OR t.IdTecnico = @IdTecnico OR c.IdTecnico = @IdTecnico)),
                    (SELECT COUNT(1) FROM dbo.CONV_ASIGNACIONES a INNER JOIN #BaseConversaciones c ON c.IdConversacion = a.IdConversacion WHERE a.FechaHora >= @Desde AND a.FechaHora < @HastaExclusive AND (@IdTecnico IS NULL OR a.IdTecnico = @IdTecnico)),
                    (SELECT COUNT(1) FROM EventosRango WHERE Texto LIKE N'%cambió el estado%' OR Texto LIKE N'%cambio el estado%' OR Texto LIKE N'%cerró la conversación%' OR Texto LIKE N'%cerro la conversacion%'),
                    (SELECT AVG(CAST(DATEDIFF(second, PrimerEntrante, PrimeraRespuesta) AS bigint)) FROM PrimerasRespuestas WHERE PrimeraRespuesta IS NOT NULL),
                    (SELECT AVG(CAST(DATEDIFF(second, ISNULL(FechaHoraPrimerMensaje, FechaHora_Grabacion), FechaHoraCierre) AS bigint)) FROM CierresRango WHERE FechaHoraCierre IS NOT NULL);

                SELECT ISNULL(c.CodigoEstado, N''), ISNULL(c.EstadoDescripcion, c.CodigoEstado), COUNT(1), ISNULL(c.EsCerrado, 0)
                FROM #BaseConversaciones c
                GROUP BY ISNULL(c.CodigoEstado, N''), ISNULL(c.EstadoDescripcion, c.CodigoEstado), ISNULL(c.EsCerrado, 0), ISNULL(c.EstadoOrden, 999)
                ORDER BY ISNULL(c.EstadoOrden, 999), ISNULL(c.EstadoDescripcion, c.CodigoEstado);

                SELECT
                    ISNULL(t.IdTecnico, N''),
                    ISNULL(t.Nombre, N'Sin asignar'),
                    ISNULL(asig.ConversacionesAsignadas, 0),
                    ISNULL(msg.MensajesEnviados, 0),
                    ISNULL(cierres.Cierres, 0),
                    ISNULL(tickets.TicketsCreados, 0)
                FROM dbo.V_TA_Tecnicos t
                OUTER APPLY (SELECT COUNT(1) AS ConversacionesAsignadas FROM dbo.CONV_CONVERSACIONES c WHERE c.IdTecnico = t.IdTecnico) asig
                OUTER APPLY (SELECT COUNT(1) AS MensajesEnviados FROM dbo.CONV_MENSAJES m WHERE m.IdTecnicoAutor = t.IdTecnico AND m.Direction = N'SALIENTE' AND m.FechaHora >= @Desde AND m.FechaHora < @HastaExclusive) msg
                OUTER APPLY (SELECT COUNT(1) AS Cierres FROM dbo.CONV_CONVERSACIONES c WHERE c.IdTecnico = t.IdTecnico AND c.FechaHoraCierre >= @Desde AND c.FechaHoraCierre < @HastaExclusive) cierres
                OUTER APPLY (SELECT COUNT(1) AS TicketsCreados FROM @Tickets tk LEFT JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = tk.IdConversacion WHERE tk.IdTecnico = t.IdTecnico OR c.IdTecnico = t.IdTecnico) tickets
                WHERE ISNULL(t.Baja, 0) = 0
                  AND (@IdTecnico IS NULL OR t.IdTecnico = @IdTecnico)
                  AND (ISNULL(asig.ConversacionesAsignadas, 0) > 0 OR ISNULL(msg.MensajesEnviados, 0) > 0 OR ISNULL(cierres.Cierres, 0) > 0 OR ISNULL(tickets.TicketsCreados, 0) > 0)
                ORDER BY ISNULL(msg.MensajesEnviados, 0) DESC, ISNULL(asig.ConversacionesAsignadas, 0) DESC, t.Nombre;

                WITH ActividadDia AS
                (
                    SELECT CAST(m.FechaHora AS date) AS Fecha, COUNT(1) AS Entrantes, 0 AS Salientes, 0 AS Cerradas, 0 AS Tickets
                    FROM dbo.CONV_MENSAJES m
                    INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
                    WHERE m.Direction = N'ENTRANTE' AND m.FechaHora >= @Desde AND m.FechaHora < @HastaExclusive AND (@IdTecnico IS NULL OR c.IdTecnico = @IdTecnico)
                    GROUP BY CAST(m.FechaHora AS date)
                    UNION ALL
                    SELECT CAST(m.FechaHora AS date), 0, COUNT(1), 0, 0
                    FROM dbo.CONV_MENSAJES m
                    WHERE m.Direction = N'SALIENTE' AND m.FechaHora >= @Desde AND m.FechaHora < @HastaExclusive AND (@IdTecnico IS NULL OR m.IdTecnicoAutor = @IdTecnico)
                    GROUP BY CAST(m.FechaHora AS date)
                    UNION ALL
                    SELECT CAST(c.FechaHoraCierre AS date), 0, 0, COUNT(1), 0
                    FROM dbo.CONV_CONVERSACIONES c
                    WHERE c.FechaHoraCierre >= @Desde AND c.FechaHoraCierre < @HastaExclusive AND (@IdTecnico IS NULL OR c.IdTecnico = @IdTecnico)
                    GROUP BY CAST(c.FechaHoraCierre AS date)
                    UNION ALL
                    SELECT CAST(t.FechaHoraAlta AS date), 0, 0, 0, COUNT(1)
                    FROM @Tickets t
                    LEFT JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = t.IdConversacion
                    WHERE @IdTecnico IS NULL OR t.IdTecnico = @IdTecnico OR c.IdTecnico = @IdTecnico
                    GROUP BY CAST(t.FechaHoraAlta AS date)
                )
                SELECT Fecha, SUM(Entrantes), SUM(Salientes), SUM(Cerradas), SUM(Tickets)
                FROM ActividadDia
                GROUP BY Fecha
                ORDER BY Fecha DESC;

                WITH EventosRango AS
                (
                    SELECT m.Texto
                    FROM dbo.CONV_MENSAJES m
                    INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
                    WHERE m.Direction = N'NOTA_INTERNA'
                      AND m.MessageType = N'SYSTEM'
                      AND m.FechaHora >= @Desde
                      AND m.FechaHora < @HastaExclusive
                      AND (@IdTecnico IS NULL OR c.IdTecnico = @IdTecnico OR m.IdTecnicoAutor = @IdTecnico)
                )
                SELECT Tipo, COUNT(1)
                FROM
                (
                    SELECT CASE
                        WHEN Texto LIKE N'%asignó la conversación%' OR Texto LIKE N'%asigno la conversacion%' THEN N'Asignaciones'
                        WHEN Texto LIKE N'%creó un ticket%' OR Texto LIKE N'%creo un ticket%' THEN N'Tickets'
                        WHEN Texto LIKE N'%cerró la conversación%' OR Texto LIKE N'%cerro la conversacion%' THEN N'Cierres'
                        WHEN Texto LIKE N'%cambió el estado%' OR Texto LIKE N'%cambio el estado%' THEN N'Cambios de estado'
                        ELSE N'Otras acciones'
                    END AS Tipo
                    FROM EventosRango
                ) x
                GROUP BY Tipo
                ORDER BY COUNT(1) DESC, Tipo;

                SELECT
                    CASE UPPER(ISNULL(MessageType, N''))
                        WHEN N'TEXT' THEN N'Texto'
                        WHEN N'IMAGE' THEN N'Imagenes'
                        WHEN N'DOCUMENT' THEN N'Documentos'
                        WHEN N'AUDIO' THEN N'Audio'
                        WHEN N'VIDEO' THEN N'Videos'
                        WHEN N'STICKER' THEN N'Stickers'
                        WHEN N'SYSTEM' THEN N'Sistema'
                        ELSE N'Otros'
                    END,
                    COUNT(1)
                FROM #MensajesRango
                GROUP BY CASE UPPER(ISNULL(MessageType, N''))
                        WHEN N'TEXT' THEN N'Texto'
                        WHEN N'IMAGE' THEN N'Imagenes'
                        WHEN N'DOCUMENT' THEN N'Documentos'
                        WHEN N'AUDIO' THEN N'Audio'
                        WHEN N'VIDEO' THEN N'Videos'
                        WHEN N'STICKER' THEN N'Stickers'
                        WHEN N'SYSTEM' THEN N'Sistema'
                        ELSE N'Otros'
                    END
                ORDER BY COUNT(1) DESC;

                WITH ClienteBase AS
                (
                    SELECT
                        c.IdConversacion,
                        NULLIF(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N''))), N'') AS ClienteCodigo,
                        COALESCE(NULLIF(cli.RAZON_SOCIAL, N''), NULLIF(c.NombreVisible, N''), NULLIF(c.TelefonoWhatsApp, N''), N'Sin cliente vinculado') AS ClienteNombre,
                        c.IdContacto,
                        c.FechaHoraCierre
                    FROM #BaseConversaciones c
                    LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = c.ClienteCodigo
                    WHERE EXISTS (SELECT 1 FROM #MensajesRango m WHERE m.IdConversacion = c.IdConversacion)
                ),
                ClienteMensajes AS
                (
                    SELECT
                        cb.ClienteCodigo,
                        cb.ClienteNombre,
                        COUNT(DISTINCT cb.IdConversacion) AS Conversaciones,
                        COUNT(DISTINCT cb.IdContacto) AS Contactos,
                        SUM(CASE WHEN m.Direction = N'ENTRANTE' THEN 1 ELSE 0 END) AS Entrantes,
                        SUM(CASE WHEN m.Direction = N'SALIENTE' THEN 1 ELSE 0 END) AS Salientes,
                        SUM(CASE WHEN m.Direction = N'NOTA_INTERNA' THEN 1 ELSE 0 END) AS Internos,
                        COUNT(1) AS TotalMensajes,
                        MAX(m.FechaHora) AS UltimoMovimiento
                    FROM ClienteBase cb
                    INNER JOIN #MensajesRango m ON m.IdConversacion = cb.IdConversacion
                    GROUP BY cb.ClienteCodigo, cb.ClienteNombre
                ),
                ClienteEntrantes AS
                (
                    SELECT cb.ClienteCodigo, cb.ClienteNombre, m.IdConversacion, MIN(m.FechaHora) AS PrimerEntrante
                    FROM ClienteBase cb
                    INNER JOIN #MensajesRango m ON m.IdConversacion = cb.IdConversacion
                    WHERE m.Direction = N'ENTRANTE'
                    GROUP BY cb.ClienteCodigo, cb.ClienteNombre, m.IdConversacion
                ),
                ClientePrimerasRespuestas AS
                (
                    SELECT ce.ClienteCodigo, ce.ClienteNombre, ce.IdConversacion, ce.PrimerEntrante, MIN(m.FechaHora) AS PrimeraRespuesta
                    FROM ClienteEntrantes ce
                    LEFT JOIN dbo.CONV_MENSAJES m
                        ON m.IdConversacion = ce.IdConversacion
                       AND m.Direction = N'SALIENTE'
                       AND m.FechaHora >= ce.PrimerEntrante
                    GROUP BY ce.ClienteCodigo, ce.ClienteNombre, ce.IdConversacion, ce.PrimerEntrante
                ),
                ClienteTickets AS
                (
                    SELECT cb.ClienteCodigo, cb.ClienteNombre, COUNT(1) AS Tickets
                    FROM @Tickets tk
                    INNER JOIN ClienteBase cb ON cb.IdConversacion = tk.IdConversacion
                    GROUP BY cb.ClienteCodigo, cb.ClienteNombre
                ),
                ClienteEventos AS
                (
                    SELECT
                        cb.ClienteCodigo,
                        cb.ClienteNombre,
                        SUM(CASE WHEN m.Texto LIKE N'%cambiÃ³ el estado de Cerrada a%' OR m.Texto LIKE N'%cambio el estado de Cerrada a%' THEN 1 ELSE 0 END) AS Reabiertas
                    FROM ClienteBase cb
                    INNER JOIN #MensajesRango m ON m.IdConversacion = cb.IdConversacion
                    WHERE m.Direction = N'NOTA_INTERNA'
                      AND m.MessageType = N'SYSTEM'
                    GROUP BY cb.ClienteCodigo, cb.ClienteNombre
                )
                SELECT TOP (10)
                    ISNULL(cm.ClienteCodigo, N''),
                    ISNULL(cm.ClienteNombre, N'Sin cliente vinculado'),
                    cm.Conversaciones,
                    cm.Contactos,
                    cm.Entrantes,
                    cm.Salientes,
                    cm.Internos,
                    cm.TotalMensajes,
                    ISNULL(pr.PendientesRespuesta, 0),
                    ISNULL(cierres.Cerradas, 0),
                    ISNULL(tk.Tickets, 0),
                    ISNULL(ev.Reabiertas, 0),
                    pr.PromedioPrimeraRespuesta,
                    cm.UltimoMovimiento
                FROM ClienteMensajes cm
                OUTER APPLY
                (
                    SELECT
                        COUNT(CASE WHEN cpr.PrimeraRespuesta IS NULL THEN 1 END) AS PendientesRespuesta,
                        AVG(CASE WHEN cpr.PrimeraRespuesta IS NOT NULL THEN CAST(DATEDIFF(second, cpr.PrimerEntrante, cpr.PrimeraRespuesta) AS bigint) END) AS PromedioPrimeraRespuesta
                    FROM ClientePrimerasRespuestas cpr
                    WHERE ISNULL(cpr.ClienteCodigo, N'') = ISNULL(cm.ClienteCodigo, N'')
                      AND cpr.ClienteNombre = cm.ClienteNombre
                ) pr
                OUTER APPLY
                (
                    SELECT COUNT(1) AS Cerradas
                    FROM ClienteBase cb
                    WHERE ISNULL(cb.ClienteCodigo, N'') = ISNULL(cm.ClienteCodigo, N'')
                      AND cb.ClienteNombre = cm.ClienteNombre
                      AND cb.FechaHoraCierre >= @Desde
                      AND cb.FechaHoraCierre < @HastaExclusive
                ) cierres
                LEFT JOIN ClienteTickets tk
                    ON ISNULL(tk.ClienteCodigo, N'') = ISNULL(cm.ClienteCodigo, N'')
                   AND tk.ClienteNombre = cm.ClienteNombre
                LEFT JOIN ClienteEventos ev
                    ON ISNULL(ev.ClienteCodigo, N'') = ISNULL(cm.ClienteCodigo, N'')
                   AND ev.ClienteNombre = cm.ClienteNombre
                ORDER BY
                    ((cm.TotalMensajes * 2) + (cm.Conversaciones * 50) + (cm.Entrantes * 2) + (cm.Contactos * 15) + (ISNULL(tk.Tickets, 0) * 20) + (ISNULL(pr.PendientesRespuesta, 0) * 10) + (ISNULL(ev.Reabiertas, 0) * 10)) DESC,
                    cm.TotalMensajes DESC,
                    cm.Conversaciones DESC,
                    cm.Entrantes DESC,
                    cm.UltimoMovimiento DESC;

                SELECT TOP (8)
                    c.IdConversacion,
                    c.IdContacto,
                    COALESCE(NULLIF(c.NombreVisible, N''), NULLIF(c.TelefonoWhatsApp, N''), N'Sin nombre'),
                    ISNULL(c.TelefonoWhatsApp, N''),
                    ISNULL(t.Nombre, N'Sin asignar'),
                    SUM(CASE WHEN m.Direction = N'ENTRANTE' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN m.Direction = N'SALIENTE' THEN 1 ELSE 0 END),
                    COUNT(1),
                    MAX(m.FechaHora)
                FROM #MensajesRango m
                INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
                LEFT JOIN dbo.V_TA_Tecnicos t ON t.IdTecnico = c.IdTecnico
                GROUP BY c.IdConversacion, c.IdContacto, COALESCE(NULLIF(c.NombreVisible, N''), NULLIF(c.TelefonoWhatsApp, N''), N'Sin nombre'), ISNULL(c.TelefonoWhatsApp, N''), ISNULL(t.Nombre, N'Sin asignar')
                ORDER BY COUNT(1) DESC, MAX(m.FechaHora) DESC;

                SELECT TOP (1000)
                    ISNULL(Texto, N'')
                FROM #MensajesRango
                WHERE Direction = N'ENTRANTE'
                  AND UPPER(ISNULL(MessageType, N'')) = N'TEXT'
                  AND LEN(LTRIM(RTRIM(ISNULL(Texto, N'')))) >= 12
                ORDER BY FechaHora DESC, IdMensaje DESC;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Desde", desde);
            cmd.Parameters.AddWithValue("@HastaExclusive", hastaExclusive);
            cmd.Parameters.AddWithValue("@IdTecnico", string.IsNullOrWhiteSpace(technicianId) ? DBNull.Value : technicianId);
            cmd.Parameters.AddWithValue("@ManualWhatsAppConversationSummary", ManualWhatsAppConversationSummary);

            var stats = new ConversacionesEstadisticasDto { Desde = desde, Hasta = hastaInclusive };

            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (await rd.ReadAsync(token))
            {
                stats.ConversacionesTotales = GetInt(rd, 0);
                stats.ConversacionesAbiertas = GetInt(rd, 1);
                stats.ConversacionesPendientes = GetInt(rd, 2);
                stats.ConversacionesEnGestion = GetInt(rd, 3);
                stats.ConversacionesCerradas = GetInt(rd, 4);
                stats.ConversacionesArchivadas = GetInt(rd, 5);
                stats.ConversacionesSinAsignar = GetInt(rd, 6);
                stats.ConversacionesAsignadas = GetInt(rd, 7);
                stats.ConversacionesNuevas = GetInt(rd, 8);
                stats.ConversacionesActivasEnRango = GetInt(rd, 9);
                stats.ClientesUnicos = GetInt(rd, 10);
                stats.ConversacionesReabiertas = GetInt(rd, 11);
                stats.MensajesEntrantes = GetInt(rd, 12);
                stats.MensajesSalientes = GetInt(rd, 13);
                stats.MensajesInternos = GetInt(rd, 14);
                stats.MensajesConAdjuntos = GetInt(rd, 15);
                stats.ConversacionesConRespuesta = GetInt(rd, 16);
                stats.ConversacionesSinRespuesta = GetInt(rd, 17);
                stats.ConversacionesCerradasEnRango = GetInt(rd, 18);
                stats.TicketsCreados = GetInt(rd, 19);
                stats.Asignaciones = GetInt(rd, 20);
                stats.CambiosEstado = GetInt(rd, 21);
                stats.PromedioPrimeraRespuesta = GetNullableTimeSpan(rd, 22);
                stats.PromedioCierre = GetNullableTimeSpan(rd, 23);
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.PorEstado.Add(new ConversacionesEstadisticaEstadoDto
                    {
                        CodigoEstado = GetString(rd, 0),
                        Descripcion = GetString(rd, 1),
                        Cantidad = GetInt(rd, 2),
                        EsCerrado = GetInt(rd, 3) == 1
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.PorTecnico.Add(new ConversacionesEstadisticaTecnicoDto
                    {
                        IdTecnico = GetString(rd, 0),
                        Nombre = GetString(rd, 1),
                        ConversacionesAsignadas = GetInt(rd, 2),
                        MensajesEnviados = GetInt(rd, 3),
                        Cierres = GetInt(rd, 4),
                        TicketsCreados = GetInt(rd, 5)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.PorDia.Add(new ConversacionesEstadisticaDiaDto
                    {
                        Fecha = rd.GetDateTime(0),
                        Entrantes = GetInt(rd, 1),
                        Salientes = GetInt(rd, 2),
                        Cerradas = GetInt(rd, 3),
                        Tickets = GetInt(rd, 4)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.Actividad.Add(new ConversacionesEstadisticaActividadDto
                    {
                        Tipo = GetString(rd, 0),
                        Cantidad = GetInt(rd, 1)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.PorTipoMensaje.Add(new ConversacionesEstadisticaTipoMensajeDto
                    {
                        Tipo = GetString(rd, 0),
                        Cantidad = GetInt(rd, 1)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.PorCliente.Add(new ConversacionesEstadisticaClienteDto
                    {
                        ClienteCodigo = GetString(rd, 0),
                        ClienteNombre = GetString(rd, 1),
                        Conversaciones = GetInt(rd, 2),
                        Contactos = GetInt(rd, 3),
                        Entrantes = GetInt(rd, 4),
                        Salientes = GetInt(rd, 5),
                        Internos = GetInt(rd, 6),
                        TotalMensajes = GetInt(rd, 7),
                        PendientesRespuesta = GetInt(rd, 8),
                        Cerradas = GetInt(rd, 9),
                        Tickets = GetInt(rd, 10),
                        Reabiertas = GetInt(rd, 11),
                        PromedioPrimeraRespuesta = GetNullableTimeSpan(rd, 12),
                        UltimoMovimiento = rd.GetDateTime(13)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                while (await rd.ReadAsync(token))
                {
                    stats.TopConversaciones.Add(new ConversacionesEstadisticaConversacionDto
                    {
                        IdConversacion = rd.GetInt64(0),
                        IdContacto = rd.IsDBNull(1) ? null : rd.GetInt32(1),
                        Nombre = GetString(rd, 2),
                        Telefono = GetString(rd, 3),
                        Tecnico = GetString(rd, 4),
                        Entrantes = GetInt(rd, 5),
                        Salientes = GetInt(rd, 6),
                        Total = GetInt(rd, 7),
                        UltimoMovimiento = rd.GetDateTime(8)
                    });
                }
            }

            if (await rd.NextResultAsync(token))
            {
                var incomingTexts = new List<string>();
                while (await rd.ReadAsync(token))
                    incomingTexts.Add(GetString(rd, 0));

                stats.TemasFrecuentes = BuildFrequentTerms(incomingTexts);
                stats.FrasesFrecuentes = BuildFrequentPhrases(incomingTexts);
            }

            return stats;
        }, "No se pudieron cargar las estad\u00edsticas de conversaciones.", ct);

    public Task<IReadOnlyList<ConversacionInboxItemDto>> GetInboxAsync(ConversacionesInboxFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetInbox", async token =>
        {
            filters ??= new();
            var items = new List<ConversacionInboxItemDto>();
            var searchPhone = NormalizePhone(filters.Search);
            var searchPhoneTail = GetPhoneComparableTail(searchPhone);
            var desde = filters.Desde?.Date;
            var hastaExclusive = filters.Hasta?.Date.AddDays(1);
            var auditoria = NormalizeAuditFilter(filters.Auditoria);
            var tipoMensaje = NormalizeMessageType(filters.TipoMensaje);
            var clienteCodigo = NormalizeClientCode(filters.ClienteCodigo);
            var usuarioActual = NormalizePinUser(filters.UsuarioActual);
            var sistemaActual = NormalizePinSystem(filters.SistemaActual);
            var sql = $"""
                DECLARE @TicketsFiltro TABLE (IdConversacion bigint NOT NULL PRIMARY KEY);

                IF @Auditoria = N'tickets' AND OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NOT NULL
                BEGIN
                    INSERT INTO @TicketsFiltro (IdConversacion)
                    EXEC sp_executesql
                        N'SELECT DISTINCT IdConversacion
                          FROM dbo.TICK_TICKETS
                          WHERE IdConversacion IS NOT NULL
                            AND ISNULL(Baja, 0) = 0
                            AND (@Desde IS NULL OR FechaHoraAlta >= @Desde)
                            AND (@HastaExclusive IS NULL OR FechaHoraAlta < @HastaExclusive)',
                        N'@Desde datetime, @HastaExclusive datetime',
                        @Desde = @Desde,
                        @HastaExclusive = @HastaExclusive;
                END;

                SELECT
                    c.IdConversacion,
                    ISNULL(c.TelefonoWhatsApp, ''),
                    ISNULL(c.NombreVisible, ''),
                    ISNULL(COALESCE(NULLIF(LTRIM(RTRIM(c.ClienteCodigo)), ''), contactoCuenta.Cuenta), ''),
                    ISNULL(COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial), ''),
                    c.IdContacto,
                    ISNULL(mc.Nombre_y_Apellido, ''),
                    ISNULL(c.CodigoEstado, ''),
                    ISNULL(e.Descripcion, ''),
                    LTRIM(RTRIM(ISNULL(c.IdTecnico, ''))),
                    ISNULL(t.Nombre, ''),
                    ISNULL(c.ResumenUltimoMensaje, ''),
                    ISNULL(ultMsg.FechaHoraVisible, c.FechaHoraUltimoMensaje),
                    ultCliente.FechaHoraUltimoMensajeCliente,
                    ultCliente.IdUltimoMensajeCliente,
                    ISNULL(clienteConteo.CantidadMensajesCliente, 0),
                    ISNULL(c.Archivada, 0),
                    ISNULL(c.Bloqueada, 0),
                    CASE WHEN pin.IdConversacion IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS FijadaPorUsuario,
                    pin.FechaHora_Grabacion AS FechaHoraFijada,
                    ISNULL(noLeidos.CantidadNoLeida, 0) AS MensajesNoLeidosUsuario
                FROM dbo.CONV_CONVERSACIONES c
                INNER JOIN dbo.CONV_ESTADOS e
                    ON e.CodigoEstado = c.CodigoEstado
                OUTER APPLY (
                    SELECT TOP (1)
                        p.IdConversacion,
                        p.FechaHora_Grabacion
                    FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO p
                    WHERE p.IdConversacion = c.IdConversacion
                      AND p.Usuario = @UsuarioActual
                    ORDER BY
                        CASE WHEN p.Sistema = @SistemaActual THEN 0 ELSE 1 END,
                        p.FechaHora_Grabacion DESC
                ) pin
                LEFT JOIN dbo.VT_CLIENTES cli
                    ON cli.CODIGO = c.ClienteCodigo
                LEFT JOIN dbo.MA_CONTACTOS mc
                    ON mc.id = c.IdContacto
                OUTER APPLY (
                    SELECT TOP (1)
                        cuenta.Cuenta,
                        cliCuenta.RAZON_SOCIAL AS RazonSocial
                    FROM (
                        SELECT UPPER(LTRIM(RTRIM(rel.Cuenta))) AS Cuenta, 0 AS Orden
                        FROM dbo.MA_CONTACTOS_CUENTAS rel
                        WHERE c.IdContacto IS NOT NULL
                          AND mc.id IS NOT NULL
                          AND rel.IdContacto = ISNULL(NULLIF(mc.idContacto, 0), mc.id)
                          AND LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))) <> ''
                        UNION ALL
                        SELECT UPPER(LTRIM(RTRIM(mc.CuentaRel))) AS Cuenta, 1 AS Orden
                        WHERE c.IdContacto IS NOT NULL
                          AND mc.id IS NOT NULL
                          AND LTRIM(RTRIM(ISNULL(mc.CuentaRel, ''))) <> ''
                    ) cuenta
                    INNER JOIN dbo.VT_CLIENTES cliCuenta
                        ON UPPER(LTRIM(RTRIM(cliCuenta.CODIGO))) = cuenta.Cuenta
                    ORDER BY cuenta.Orden, cliCuenta.RAZON_SOCIAL
                ) contactoCuenta
                LEFT JOIN dbo.V_TA_Tecnicos t
                    ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
                OUTER APPLY (
                    SELECT TOP (1)
                        m.IdMensaje AS IdUltimoMensajeCliente,
                        {ConversationMessageVisibleDateSql("m")} AS FechaHoraUltimoMensajeCliente
                    FROM dbo.CONV_MENSAJES m
                    WHERE m.IdConversacion = c.IdConversacion
                      AND m.Direction = N'ENTRANTE'
                    ORDER BY {ConversationMessageVisibleDateSql("m")} DESC, m.IdMensaje DESC
                ) ultCliente
                OUTER APPLY (
                    SELECT COUNT(1) AS CantidadMensajesCliente
                    FROM dbo.CONV_MENSAJES m
                    WHERE m.IdConversacion = c.IdConversacion
                      AND m.Direction = N'ENTRANTE'
                ) clienteConteo
                OUTER APPLY (
                    SELECT TOP (1)
                        r.IdUltimoMensajeLeido
                    FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO r
                    WHERE r.IdConversacion = c.IdConversacion
                      AND r.Usuario = @UsuarioActual
                    ORDER BY
                        CASE WHEN r.Sistema = @SistemaActual THEN 0 ELSE 1 END,
                        r.FechaHora_Modificacion DESC
                ) lectura
                OUTER APPLY (
                    SELECT COUNT(1) AS CantidadNoLeida
                    FROM dbo.CONV_MENSAJES m
                    WHERE m.IdConversacion = c.IdConversacion
                      AND m.Direction = N'ENTRANTE'
                      AND m.IdMensaje > ISNULL(lectura.IdUltimoMensajeLeido, 0)
                ) noLeidos
                OUTER APPLY (
                    SELECT TOP (1)
                        msg.IdMensaje,
                        {ConversationMessageVisibleDateSql("msg")} AS FechaHoraVisible
                    FROM dbo.CONV_MENSAJES msg
                    WHERE msg.IdConversacion = c.IdConversacion
                    ORDER BY {ConversationMessageVisibleDateSql("msg")} DESC, msg.IdMensaje DESC
                ) ultMsg
                WHERE
                    (@Canal IS NULL OR c.Canal = @Canal)
                    AND (
                        @CodigoEstado IS NULL
                        OR (@CodigoEstado = @EstadoSinFinalizar AND ISNULL(e.EsCerrado, 0) = 0 AND ISNULL(c.Archivada, 0) = 0)
                        OR (@CodigoEstado <> @EstadoSinFinalizar AND c.CodigoEstado = @CodigoEstado)
                    )
                    AND (
                        @ClienteCodigo IS NULL
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = @ClienteCodigo
                        OR contactoCuenta.Cuenta = @ClienteCodigo
                    )
                    AND (
                        @Search IS NULL
                        OR @ClienteCodigo IS NOT NULL
                        OR c.TelefonoWhatsApp LIKE @Search
                        OR c.NombreVisible COLLATE Latin1_General_CI_AI LIKE @Search
                        OR COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial) COLLATE Latin1_General_CI_AI LIKE @Search
                        OR mc.Nombre_y_Apellido COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(mc.Email, '') COLLATE Latin1_General_CI_AI LIKE @Search
                        OR c.ResumenUltimoMensaje COLLATE Latin1_General_CI_AI LIKE @Search
                        OR EXISTS (
                            SELECT 1
                            FROM dbo.CONV_MENSAJES buscarMsg
                            WHERE buscarMsg.IdConversacion = c.IdConversacion
                              AND buscarMsg.Texto COLLATE Latin1_General_CI_AI LIKE @Search
                              AND (@Desde IS NULL OR buscarMsg.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR buscarMsg.FechaHora < @HastaExclusive)
                        )
                        OR (
                            @SearchPhone IS NOT NULL
                            AND (
                                {SqlPhoneEquivalentPredicate("c.TelefonoWhatsApp", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Telefono", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Celular", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.TelefonoPart", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.CelularPart", "@SearchPhone", "@SearchPhoneTail")}
                            )
                        )
                    )
                    AND (@IdTecnicoActual IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual)
                    AND (
                        @Modo = 'todas'
                        OR (@Modo = 'sin_asignar' AND (c.IdTecnico IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = ''))
                        OR (@Modo = 'asignadas_a_mi' AND LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual)
                        OR (@Modo = 'pendientes' AND ISNULL(e.EsCerrado, 0) = 0)
                        OR (@Modo = 'cerradas' AND ISNULL(e.EsCerrado, 0) = 1)
                    )
                    AND (
                        @Auditoria IS NULL
                        OR (@Auditoria = N'entrantes' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'ENTRANTE'
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'salientes' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'SALIENTE'
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'internos' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'NOTA_INTERNA'
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'adjuntos' AND EXISTS (
                            SELECT 1
                            FROM dbo.CONV_ADJUNTOS a
                            INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = a.IdMensaje
                            WHERE m.IdConversacion = c.IdConversacion
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'activas' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'nuevas' AND c.FechaHora_Grabacion >= @Desde AND c.FechaHora_Grabacion < @HastaExclusive)
                        OR (@Auditoria = N'cerradas_rango' AND c.FechaHoraCierre >= @Desde AND c.FechaHoraCierre < @HastaExclusive)
                        OR (@Auditoria = N'asignaciones' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_ASIGNACIONES a
                            WHERE a.IdConversacion = c.IdConversacion
                              AND (@Desde IS NULL OR a.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR a.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'reabiertas' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'NOTA_INTERNA'
                              AND m.MessageType = N'SYSTEM'
                              AND (m.Texto LIKE N'%cambió el estado de Cerrada a%' OR m.Texto LIKE N'%cambio el estado de Cerrada a%')
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'cambios_estado' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'NOTA_INTERNA'
                              AND m.MessageType = N'SYSTEM'
                              AND (m.Texto LIKE N'%cambió el estado%' OR m.Texto LIKE N'%cambio el estado%' OR m.Texto LIKE N'%cerró la conversación%' OR m.Texto LIKE N'%cerro la conversacion%')
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'tipo_mensaje' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND UPPER(ISNULL(m.MessageType, N'')) = @TipoMensaje
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'tickets' AND EXISTS (
                            SELECT 1 FROM @TicketsFiltro tk WHERE tk.IdConversacion = c.IdConversacion
                        ))
                    )
                    AND NOT (
                        c.Canal = N'WHATSAPP'
                        AND ISNULL(c.ResumenUltimoMensaje, N'') = @ManualWhatsAppConversationSummary
                        AND NOT EXISTS (
                            SELECT 1
                            FROM dbo.CONV_MENSAJES msg
                            WHERE msg.IdConversacion = c.IdConversacion
                        )
                    )
                ORDER BY
                    CASE WHEN pin.IdConversacion IS NULL THEN 0 ELSE 1 END DESC,
                    pin.FechaHora_Grabacion DESC,
                    ISNULL(c.FechaHoraUltimoMensaje, ultMsg.FechaHoraVisible) DESC,
                    c.IdConversacion DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureConversationPinsTableAsync(cn, token);
            await EnsureConversationReadStateTableAsync(cn, token);
            await EnsureConversationReadBaselinesAsync(cn, usuarioActual, sistemaActual, clienteCodigo, token);
            if (string.IsNullOrWhiteSpace(clienteCodigo))
            {
                await LinkUnassociatedWhatsAppConversationsByPhoneAsync(cn, token);
                await ConsolidateExistingDuplicateWhatsAppConversationsAsync(cn, token);
                await ReopenClosedConversationsWithIncomingAfterCloseAsync(cn, null, token);
            }

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Canal", DbNullable(filters.Canal));
            cmd.Parameters.AddWithValue("@CodigoEstado", DbNullable(filters.CodigoEstado));
            cmd.Parameters.AddWithValue("@EstadoSinFinalizar", ConversacionesInboxFilters.EstadoSinFinalizar);
            cmd.Parameters.AddWithValue("@Search", DbNullable(Like(filters.Search)));
            cmd.Parameters.AddWithValue("@SearchPhone", DbNullable(searchPhone));
            cmd.Parameters.AddWithValue("@SearchPhoneTail", DbNullable(searchPhoneTail));
            cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(clienteCodigo));
            cmd.Parameters.AddWithValue("@Modo", NormalizeMode(filters.Modo));
            cmd.Parameters.AddWithValue("@IdTecnicoActual", DbNullable(NormalizeTechnicianId(filters.IdTecnicoActual)));
            cmd.Parameters.AddWithValue("@Desde", desde.HasValue ? desde.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@HastaExclusive", hastaExclusive.HasValue ? hastaExclusive.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Auditoria", DbNullable(auditoria));
            cmd.Parameters.AddWithValue("@TipoMensaje", DbNullable(tipoMensaje));
            cmd.Parameters.AddWithValue("@UsuarioActual", usuarioActual);
            cmd.Parameters.AddWithValue("@SistemaActual", sistemaActual);
            cmd.Parameters.AddWithValue("@ManualWhatsAppConversationSummary", ManualWhatsAppConversationSummary);
            cmd.Parameters.AddWithValue("@Offset", Math.Max(0, filters.Offset));
            cmd.Parameters.AddWithValue("@Limit", Math.Clamp(filters.Limit, 1, 200));

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionInboxItemDto
                {
                    IdConversacion = rd.GetInt64(0),
                    TelefonoWhatsApp = GetString(rd, 1),
                    NombreVisible = GetString(rd, 2),
                    ClienteCodigo = GetString(rd, 3),
                    ClienteNombre = GetString(rd, 4),
                    IdContacto = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                    ContactoNombre = GetString(rd, 6),
                    CodigoEstado = GetString(rd, 7),
                    EstadoDescripcion = GetString(rd, 8),
                    IdTecnico = GetString(rd, 9),
                    TecnicoNombre = GetString(rd, 10),
                    ResumenUltimoMensaje = GetString(rd, 11),
                    FechaHoraUltimoMensaje = rd.IsDBNull(12) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(12)),
                    FechaHoraUltimoMensajeCliente = rd.IsDBNull(13) ? null : NormalizeStoredConversationTime(rd.GetDateTime(13)),
                    IdUltimoMensajeCliente = rd.IsDBNull(14) ? null : rd.GetInt64(14),
                    CantidadMensajesCliente = rd.IsDBNull(15) ? 0 : rd.GetInt32(15),
                    Archivada = !rd.IsDBNull(16) && rd.GetBoolean(16),
                    Bloqueada = !rd.IsDBNull(17) && rd.GetBoolean(17),
                    FijadaPorUsuario = !rd.IsDBNull(18) && rd.GetBoolean(18),
                    FechaHoraFijada = rd.IsDBNull(19) ? null : rd.GetDateTime(19),
                    MensajesNoLeidosUsuario = rd.IsDBNull(20) ? 0 : rd.GetInt32(20)
                });
            }

            foreach (var item in items)
                ApplyWhatsAppWindow(item);

            return DeduplicateInboxItems(items);
        }, "No se pudieron cargar las conversaciones.", ct);

    public Task<IReadOnlyList<ConversacionAuditoriaMensajeDto>> GetAuditMessagesAsync(ConversacionesInboxFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetAuditMessages", async token =>
        {
            filters ??= new();
            var searchPhone = NormalizePhone(filters.Search);
            var searchPhoneTail = GetPhoneComparableTail(searchPhone);
            var desde = filters.Desde?.Date;
            var hastaExclusive = filters.Hasta?.Date.AddDays(1);
            var auditoria = NormalizeAuditFilter(filters.Auditoria);
            var tipoMensaje = NormalizeMessageType(filters.TipoMensaje);
            var clienteCodigo = NormalizeClientCode(filters.ClienteCodigo);

            var sql = $"""
                SELECT TOP (200)
                    m.IdMensaje,
                    m.IdConversacion,
                    COALESCE(NULLIF(c.NombreVisible, N''), NULLIF(mc.Nombre_y_Apellido, N''), NULLIF(cli.RAZON_SOCIAL, N''), NULLIF(c.TelefonoWhatsApp, N''), N'Sin nombre'),
                    ISNULL(c.TelefonoWhatsApp, N''),
                    ISNULL(e.Descripcion, N''),
                    ISNULL(t.Nombre, N''),
                    ISNULL(m.MessageType, N''),
                    ISNULL(m.Direction, N''),
                    ISNULL(m.Texto, N''),
                    {ConversationMessageVisibleDateSql("m")} AS FechaHora,
                    CASE WHEN EXISTS (SELECT 1 FROM dbo.CONV_ADJUNTOS a WHERE a.IdMensaje = m.IdMensaje) THEN 1 ELSE 0 END
                FROM dbo.CONV_MENSAJES m
                INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
                INNER JOIN dbo.CONV_ESTADOS e ON e.CodigoEstado = c.CodigoEstado
                LEFT JOIN dbo.VT_CLIENTES cli ON cli.CODIGO = c.ClienteCodigo
                LEFT JOIN dbo.MA_CONTACTOS mc ON mc.id = c.IdContacto
                LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
                WHERE
                    (@Canal IS NULL OR c.Canal = @Canal)
                    AND (@IdTecnicoActual IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual OR LTRIM(RTRIM(m.IdTecnicoAutor)) = @IdTecnicoActual)
                    AND (@ClienteCodigo IS NULL OR UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = @ClienteCodigo)
                    AND (
                        @CodigoEstado IS NULL
                        OR (@CodigoEstado = @EstadoSinFinalizar AND ISNULL(e.EsCerrado, 0) = 0 AND ISNULL(c.Archivada, 0) = 0)
                        OR (@CodigoEstado <> @EstadoSinFinalizar AND c.CodigoEstado = @CodigoEstado)
                    )
                    AND (
                        @Modo = 'todas'
                        OR (@Modo = 'sin_asignar' AND (c.IdTecnico IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = ''))
                        OR (@Modo = 'asignadas_a_mi' AND LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual)
                        OR (@Modo = 'pendientes' AND ISNULL(e.EsCerrado, 0) = 0)
                        OR (@Modo = 'cerradas' AND ISNULL(e.EsCerrado, 0) = 1)
                    )
                    AND (@Desde IS NULL OR {ConversationMessageVisibleDateSql("m")} >= @Desde)
                    AND (@HastaExclusive IS NULL OR {ConversationMessageVisibleDateSql("m")} < @HastaExclusive)
                    AND (
                        @Search IS NULL
                        OR @ClienteCodigo IS NOT NULL
                        OR m.Texto COLLATE Latin1_General_CI_AI LIKE @Search
                        OR c.TelefonoWhatsApp LIKE @Search
                        OR c.NombreVisible COLLATE Latin1_General_CI_AI LIKE @Search
                        OR cli.RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @Search
                        OR mc.Nombre_y_Apellido COLLATE Latin1_General_CI_AI LIKE @Search
                        OR ISNULL(mc.Email, '') COLLATE Latin1_General_CI_AI LIKE @Search
                        OR (
                            @SearchPhone IS NOT NULL
                            AND (
                                {SqlPhoneEquivalentPredicate("c.TelefonoWhatsApp", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Telefono", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Celular", "@SearchPhone", "@SearchPhoneTail")}
                            )
                        )
                    )
                    AND (
                        @Auditoria IS NULL
                        OR (@Auditoria = N'entrantes' AND m.Direction = N'ENTRANTE')
                        OR (@Auditoria = N'salientes' AND m.Direction = N'SALIENTE')
                        OR (@Auditoria = N'internos' AND m.Direction = N'NOTA_INTERNA')
                        OR (@Auditoria = N'adjuntos' AND EXISTS (SELECT 1 FROM dbo.CONV_ADJUNTOS a WHERE a.IdMensaje = m.IdMensaje))
                        OR (@Auditoria = N'activas')
                        OR (@Auditoria = N'nuevas' AND c.FechaHora_Grabacion >= @Desde AND c.FechaHora_Grabacion < @HastaExclusive)
                        OR (@Auditoria = N'cerradas_rango' AND c.FechaHoraCierre >= @Desde AND c.FechaHoraCierre < @HastaExclusive)
                        OR (@Auditoria = N'asignaciones' AND EXISTS (SELECT 1 FROM dbo.CONV_ASIGNACIONES a WHERE a.IdConversacion = c.IdConversacion AND (@Desde IS NULL OR a.FechaHora >= @Desde) AND (@HastaExclusive IS NULL OR a.FechaHora < @HastaExclusive)))
                        OR (@Auditoria = N'reabiertas' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%cambió el estado de Cerrada a%' OR m.Texto LIKE N'%cambio el estado de Cerrada a%'))
                        OR (@Auditoria = N'cambios_estado' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%cambió el estado%' OR m.Texto LIKE N'%cambio el estado%' OR m.Texto LIKE N'%cerró la conversación%' OR m.Texto LIKE N'%cerro la conversacion%'))
                        OR (@Auditoria = N'tipo_mensaje' AND UPPER(ISNULL(m.MessageType, N'')) = @TipoMensaje)
                        OR (@Auditoria = N'tickets' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%creó un ticket%' OR m.Texto LIKE N'%creo un ticket%'))
                    )
                ORDER BY {ConversationMessageVisibleDateSql("m")} DESC, m.IdMensaje DESC
                """;

            var items = new List<ConversacionAuditoriaMensajeDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Canal", DbNullable(filters.Canal));
            cmd.Parameters.AddWithValue("@CodigoEstado", DbNullable(filters.CodigoEstado));
            cmd.Parameters.AddWithValue("@EstadoSinFinalizar", ConversacionesInboxFilters.EstadoSinFinalizar);
            cmd.Parameters.AddWithValue("@Search", DbNullable(Like(filters.Search)));
            cmd.Parameters.AddWithValue("@SearchPhone", DbNullable(searchPhone));
            cmd.Parameters.AddWithValue("@SearchPhoneTail", DbNullable(searchPhoneTail));
            cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(clienteCodigo));
            cmd.Parameters.AddWithValue("@Modo", NormalizeMode(filters.Modo));
            cmd.Parameters.AddWithValue("@IdTecnicoActual", DbNullable(NormalizeTechnicianId(filters.IdTecnicoActual)));
            cmd.Parameters.AddWithValue("@Desde", desde.HasValue ? desde.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@HastaExclusive", hastaExclusive.HasValue ? hastaExclusive.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Auditoria", DbNullable(auditoria));
            cmd.Parameters.AddWithValue("@TipoMensaje", DbNullable(tipoMensaje));

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionAuditoriaMensajeDto
                {
                    IdMensaje = rd.GetInt64(0),
                    IdConversacion = rd.GetInt64(1),
                    NombreConversacion = GetString(rd, 2),
                    TelefonoWhatsApp = GetString(rd, 3),
                    EstadoDescripcion = GetString(rd, 4),
                    TecnicoNombre = GetString(rd, 5),
                    MessageType = GetString(rd, 6),
                    Direction = GetString(rd, 7),
                    Texto = GetString(rd, 8),
                    FechaHora = rd.IsDBNull(9) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(9)),
                    TieneAdjuntos = GetInt(rd, 10) == 1
                });
            }

            return (IReadOnlyList<ConversacionAuditoriaMensajeDto>)items;
        }, "No se pudieron cargar los mensajes de auditoría.", ct);

    public Task<ConversacionDetalleDto?> GetConversationAsync(long conversationId, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetConversation", async token =>
        {
            var sql = $"""
                SELECT
                    c.IdConversacion,
                    ISNULL(c.Canal, ''),
                    ISNULL(c.TelefonoWhatsApp, ''),
                    ISNULL(c.NombreVisible, ''),
                    ISNULL(COALESCE(NULLIF(LTRIM(RTRIM(c.ClienteCodigo)), ''), contactoCuenta.Cuenta), ''),
                    ISNULL(COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial), ''),
                    c.IdContacto,
                    ISNULL(mc.Nombre_y_Apellido, ''),
                    ISNULL(mc.Telefono, ''),
                    ISNULL(mc.Celular, ''),
                    ISNULL(mc.email, ''),
                    ISNULL(mc.Cargo, ''),
                    ISNULL(c.CodigoEstado, ''),
                    ISNULL(e.Descripcion, ''),
                    LTRIM(RTRIM(ISNULL(c.IdTecnico, ''))),
                    ISNULL(t.Nombre, ''),
                    ISNULL(c.ResumenUltimoMensaje, ''),
                    ISNULL(c.Prioridad, ''),
                    ISNULL(c.Archivada, 0),
                    ISNULL(c.Bloqueada, 0),
                    c.FechaHoraPrimerMensaje,
                    ISNULL(ultMsg.FechaHoraVisible, c.FechaHoraUltimoMensaje),
                    ultCliente.FechaHoraUltimoMensajeCliente,
                    c.FechaHoraCierre
                FROM dbo.CONV_CONVERSACIONES c
                INNER JOIN dbo.CONV_ESTADOS e
                    ON e.CodigoEstado = c.CodigoEstado
                LEFT JOIN dbo.VT_CLIENTES cli
                    ON cli.CODIGO = c.ClienteCodigo
                LEFT JOIN dbo.MA_CONTACTOS mc
                    ON mc.id = c.IdContacto
                OUTER APPLY (
                    SELECT TOP (1)
                        cuenta.Cuenta,
                        cliCuenta.RAZON_SOCIAL AS RazonSocial
                    FROM (
                        SELECT UPPER(LTRIM(RTRIM(rel.Cuenta))) AS Cuenta, 0 AS Orden
                        FROM dbo.MA_CONTACTOS_CUENTAS rel
                        WHERE c.IdContacto IS NOT NULL
                          AND mc.id IS NOT NULL
                          AND rel.IdContacto = ISNULL(NULLIF(mc.idContacto, 0), mc.id)
                          AND LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))) <> ''
                        UNION ALL
                        SELECT UPPER(LTRIM(RTRIM(mc.CuentaRel))) AS Cuenta, 1 AS Orden
                        WHERE c.IdContacto IS NOT NULL
                          AND mc.id IS NOT NULL
                          AND LTRIM(RTRIM(ISNULL(mc.CuentaRel, ''))) <> ''
                    ) cuenta
                    INNER JOIN dbo.VT_CLIENTES cliCuenta
                        ON UPPER(LTRIM(RTRIM(cliCuenta.CODIGO))) = cuenta.Cuenta
                    ORDER BY cuenta.Orden, cliCuenta.RAZON_SOCIAL
                ) contactoCuenta
                LEFT JOIN dbo.V_TA_Tecnicos t
                    ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(c.IdTecnico))
                OUTER APPLY (
                    SELECT TOP (1) {ConversationMessageVisibleDateSql("m")} AS FechaHoraUltimoMensajeCliente
                    FROM dbo.CONV_MENSAJES m
                    WHERE m.IdConversacion = c.IdConversacion
                      AND m.Direction = N'ENTRANTE'
                    ORDER BY {ConversationMessageVisibleDateSql("m")} DESC, m.IdMensaje DESC
                ) ultCliente
                OUTER APPLY (
                    SELECT TOP (1)
                        {ConversationMessageVisibleDateSql("msg")} AS FechaHoraVisible
                    FROM dbo.CONV_MENSAJES msg
                    WHERE msg.IdConversacion = c.IdConversacion
                    ORDER BY {ConversationMessageVisibleDateSql("msg")} DESC, msg.IdMensaje DESC
                ) ultMsg
                WHERE c.IdConversacion = @IdConversacion
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await LinkUnassociatedWhatsAppConversationsByPhoneAsync(cn, token, conversationId);
            await ReopenClosedConversationsWithIncomingAfterCloseAsync(cn, conversationId, token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", conversationId);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            if (!await rd.ReadAsync(token))
                return null;

            var item = new ConversacionDetalleDto
            {
                IdConversacion = rd.GetInt64(0),
                Canal = GetString(rd, 1),
                TelefonoWhatsApp = GetString(rd, 2),
                NombreVisible = GetString(rd, 3),
                ClienteCodigo = GetString(rd, 4),
                ClienteNombre = GetString(rd, 5),
                IdContacto = rd.IsDBNull(6) ? null : rd.GetInt32(6),
                ContactoNombre = GetString(rd, 7),
                ContactoTelefono = GetString(rd, 8),
                ContactoCelular = GetString(rd, 9),
                ContactoEmail = GetString(rd, 10),
                ContactoCargo = GetString(rd, 11),
                CodigoEstado = GetString(rd, 12),
                EstadoDescripcion = GetString(rd, 13),
                IdTecnico = GetString(rd, 14),
                TecnicoNombre = GetString(rd, 15),
                ResumenUltimoMensaje = GetString(rd, 16),
                Prioridad = GetString(rd, 17),
                Archivada = !rd.IsDBNull(18) && rd.GetBoolean(18),
                Bloqueada = !rd.IsDBNull(19) && rd.GetBoolean(19),
                FechaHoraPrimerMensaje = rd.IsDBNull(20) ? null : NormalizeStoredConversationTime(rd.GetDateTime(20)),
                FechaHoraUltimoMensaje = rd.IsDBNull(21) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(21)),
                FechaHoraUltimoMensajeCliente = rd.IsDBNull(22) ? null : NormalizeStoredConversationTime(rd.GetDateTime(22)),
                FechaHoraCierre = rd.IsDBNull(23) ? null : rd.GetDateTime(23)
            };

            ApplyWhatsAppWindow(item);
            return item;
        }, "No se pudo cargar la conversación.", ct);

    public Task<IReadOnlyList<ConversacionMensajeDto>> GetMessagesAsync(long conversationId, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetMessages", async token =>
        {
            var sql = $"""
                SELECT
                    m.IdMensaje,
                    m.IdConversacion,
                    ISNULL(m.TelefonoWhatsApp, ''),
                    ISNULL(m.WhatsAppMessageId, ''),
                    ISNULL(m.WhatsAppReplyToMessageId, ''),
                    ISNULL(m.MessageType, ''),
                    ISNULL(m.Direction, ''),
                    ISNULL(m.EstadoEnvio, ''),
                    ISNULL(m.Texto, ''),
                    {ConversationMessageVisibleDateSql("m")} AS FechaHora,
                    ISNULL(m.UsuarioAutor, ''),
                    ISNULL(m.SistemaAutor, ''),
                    ISNULL(m.IdTecnicoAutor, ''),
                    ISNULL(t.Nombre, ''),
                    ISNULL(m.PayloadJson, ''),
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.CONV_ADJUNTOS a
                        WHERE a.IdMensaje = m.IdMensaje
                    ) THEN 1 ELSE 0 END
                FROM dbo.CONV_MENSAJES m
                LEFT JOIN dbo.V_TA_Tecnicos t
                    ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(m.IdTecnicoAutor))
                WHERE m.IdConversacion = @IdConversacion
                ORDER BY m.IdMensaje ASC
            """;

            var items = new List<ConversacionMensajeDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", conversationId);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var item = new ConversacionMensajeDto
                {
                    IdMensaje = rd.GetInt64(0),
                    IdConversacion = rd.GetInt64(1),
                    TelefonoWhatsApp = GetString(rd, 2),
                    WhatsAppMessageId = GetString(rd, 3),
                    WhatsAppReplyToMessageId = GetString(rd, 4),
                    MessageType = GetString(rd, 5),
                    Direction = GetString(rd, 6),
                    EstadoEnvio = GetString(rd, 7),
                    Texto = GetString(rd, 8),
                    PayloadJson = GetString(rd, 14),
                    FechaHora = rd.IsDBNull(9) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(9)),
                    UsuarioAutor = GetString(rd, 10),
                    SistemaAutor = GetString(rd, 11),
                    IdTecnicoAutor = GetString(rd, 12),
                    TecnicoAutorNombre = GetString(rd, 13),
                    TieneAdjuntos = GetInt(rd, 15) == 1
                };

                items.Add(item);
            }

            return (IReadOnlyList<ConversacionMensajeDto>)items;
        }, "No se pudieron cargar los mensajes.", ct);

    public Task<IReadOnlyList<ConversacionClienteCandidateDto>> SearchClientesParaRelacionarAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync<IReadOnlyList<ConversacionClienteCandidateDto>>("Conversaciones", "SearchClientesParaRelacionar", async token =>
        {
            if (string.IsNullOrWhiteSpace(texto))
                return [];

            var search = SearchTextHelper.Normalize(texto);
            const string sql = """
                SELECT TOP (20)
                    ISNULL(CODIGO, '') AS Codigo,
                    ISNULL(RAZON_SOCIAL, '') AS RazonSocial,
                    ISNULL(LOCALIDAD, '') AS Localidad,
                    ISNULL(PROVINCIA, '') AS Provincia,
                    ISNULL(TELEFONO, '') AS Telefono,
                    ISNULL(MAIL, '') AS Mail
                FROM dbo.VT_CLIENTES
                WHERE ISNULL(CODIGO, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                   OR ISNULL(RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                   OR ISNULL(LOCALIDAD, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                   OR ISNULL(TELEFONO, '') LIKE @TextoLike
                   OR ISNULL(MAIL, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                ORDER BY
                    CASE WHEN ISNULL(RAZON_SOCIAL, '') LIKE @Texto + '%' THEN 0 ELSE 1 END,
                    ISNULL(RAZON_SOCIAL, ''),
                    ISNULL(CODIGO, '');
                """;

            var items = new List<ConversacionClienteCandidateDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Texto", search);
            cmd.Parameters.AddWithValue("@TextoLike", SearchTextHelper.LikeContains(search));
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionClienteCandidateDto
                {
                    Codigo = GetString(rd, 0),
                    RazonSocial = GetString(rd, 1),
                    Localidad = GetString(rd, 2),
                    Provincia = GetString(rd, 3),
                    Telefono = GetString(rd, 4),
                    Mail = GetString(rd, 5)
                });
            }

            return items;
        }, "No se pudieron buscar clientes para relacionar.", ct);

    public async Task RelacionarClienteAsync(ConversacionRelacionarClienteRequest request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "RelacionarCliente", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var clienteCodigo = request.ClienteCodigo?.Trim().ToUpperInvariant() ?? string.Empty;
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("No se recibió la conversación a relacionar.");
            if (string.IsNullOrWhiteSpace(clienteCodigo))
                throw new InvalidOperationException("No se recibió el cliente a relacionar.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);
            var sqlTx = (SqlTransaction)tx;

            const string clienteSql = """
                SELECT TOP (1) ISNULL(CODIGO, ''), ISNULL(RAZON_SOCIAL, '')
                FROM dbo.VT_CLIENTES
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @ClienteCodigo;
                """;
            string clienteNombre;
            await using (var clienteCmd = new SqlCommand(clienteSql, cn, sqlTx))
            {
                clienteCmd.Parameters.AddWithValue("@ClienteCodigo", clienteCodigo);
                await using var clienteRd = await clienteCmd.ExecuteReaderAsync(token);
                if (!await clienteRd.ReadAsync(token))
                    throw new InvalidOperationException("El cliente seleccionado ya no existe en la base activa.");
                clienteNombre = GetString(clienteRd, 1);
            }

            const string conversationSql = """
                SELECT TOP (1) IdContacto
                FROM dbo.CONV_CONVERSACIONES
                WHERE IdConversacion = @IdConversacion;
                """;
            int? idContacto = null;
            await using (var convCmd = new SqlCommand(conversationSql, cn, sqlTx))
            {
                convCmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                var raw = await convCmd.ExecuteScalarAsync(token);
                if (raw is null || raw is DBNull)
                    idContacto = null;
                else
                    idContacto = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }

            const string existsConversationSql = "SELECT COUNT(1) FROM dbo.CONV_CONVERSACIONES WHERE IdConversacion = @IdConversacion;";
            await using (var existsCmd = new SqlCommand(existsConversationSql, cn, sqlTx))
            {
                existsCmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
                if (exists == 0)
                    throw new InvalidOperationException("La conversación seleccionada ya no existe.");
            }

            const string updateConversationSql = """
                UPDATE dbo.CONV_CONVERSACIONES
                SET ClienteCodigo = @ClienteCodigo
                WHERE IdConversacion = @IdConversacion;
                """;
            await using (var updateCmd = new SqlCommand(updateConversationSql, cn, sqlTx))
            {
                updateCmd.Parameters.AddWithValue("@ClienteCodigo", clienteCodigo);
                updateCmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                await updateCmd.ExecuteNonQueryAsync(token);
            }

            if (idContacto.HasValue)
            {
                const string linkSql = """
                    DECLARE @IdContactoRel int;

                    SELECT @IdContactoRel = CONVERT(int, ISNULL(NULLIF(idContacto, 0), id))
                    FROM dbo.MA_CONTACTOS
                    WHERE id = @IdContacto;

                    IF @IdContactoRel IS NOT NULL
                    BEGIN
                        UPDATE dbo.MA_CONTACTOS
                        SET idContacto = @IdContactoRel
                        WHERE id = @IdContacto
                          AND ISNULL(idContacto, 0) = 0;

                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.MA_CONTACTOS_CUENTAS
                            WHERE IdContacto = @IdContactoRel
                              AND UPPER(LTRIM(RTRIM(Cuenta))) = @ClienteCodigo
                        )
                        BEGIN
                            INSERT INTO dbo.MA_CONTACTOS_CUENTAS (IdContacto, Cuenta)
                            VALUES (@IdContactoRel, @ClienteCodigo);
                        END;
                    END;
                    """;
                await using var linkCmd = new SqlCommand(linkSql, cn, sqlTx);
                linkCmd.Parameters.AddWithValue("@IdContacto", idContacto.Value);
                linkCmd.Parameters.AddWithValue("@ClienteCodigo", clienteCodigo);
                await linkCmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "RelacionarCliente",
                "CONV_CONVERSACIONES",
                request.IdConversacion.ToString(CultureInfo.InvariantCulture),
                "Cliente relacionado manualmente desde contexto de conversación.",
                new { request.IdConversacion, ClienteCodigo = clienteCodigo, ClienteNombre = clienteNombre, IdContacto = idContacto },
                token);
            return true;
        }, "No se pudo relacionar el cliente con la conversación.", ct);
    }

    public async Task RenameConversationAsync(ConversacionRenameRequest request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "RenameConversation", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("No se recibiÃ³ la conversaciÃ³n a renombrar.");

            var nombre = request.NombreVisible?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nombre))
                throw new InvalidOperationException("El nombre de la conversaciÃ³n no puede quedar vacÃ­o.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string sql = """
                UPDATE dbo.CONV_CONVERSACIONES
                SET NombreVisible = @NombreVisible,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @IdConversacion;
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
            cmd.Parameters.AddWithValue("@NombreVisible", nombre);
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("La conversaciÃ³n seleccionada ya no existe.");

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "RenameConversation",
                "CONV_CONVERSACIONES",
                request.IdConversacion.ToString(CultureInfo.InvariantCulture),
                "Nombre visible de conversaciÃ³n actualizado.",
                new { request.IdConversacion, NombreVisible = nombre },
                token);

            return true;
        }, "No se pudo actualizar el nombre de la conversaciÃ³n.", ct);
    }

    public Task<IReadOnlyList<ConversacionTypingDto>> GetTypingAsync(long conversationId, string? clienteIdActual = null, CancellationToken ct = default)
    {
        if (conversationId <= 0)
            return Task.FromResult<IReadOnlyList<ConversacionTypingDto>>([]);

        var now = DateTime.UtcNow;
        PurgeExpiredTyping(now);

        var currentClientId = clienteIdActual?.Trim() ?? string.Empty;
        var items = TypingPresences.Values
            .Where(x => x.IdConversacion == conversationId
                        && now - x.LastSeenUtc <= TypingTtl
                        && !string.Equals(x.ClienteId, currentClientId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.NombreTecnico)
            .ThenBy(x => x.Usuario)
            .Select(x => new ConversacionTypingDto
            {
                ClienteId = x.ClienteId,
                IdTecnico = x.IdTecnico,
                NombreTecnico = x.NombreTecnico,
                Usuario = x.Usuario,
                Sistema = x.Sistema,
                FechaHora = x.LastSeenLocal
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ConversacionTypingDto>>(items);
    }

    public Task SetTypingAsync(ConversacionTypingRequest request, CancellationToken ct = default)
    {
        if (request.IdConversacion <= 0)
            return Task.CompletedTask;

        var idTecnico = NormalizeTechnicianId(request.IdTecnico);
        var clienteId = request.ClienteId?.Trim() ?? string.Empty;
        var usuario = request.Usuario?.Trim() ?? string.Empty;
        var sistema = request.Sistema?.Trim() ?? string.Empty;
        var nombreTecnico = FirstNonEmpty(request.NombreTecnico, usuario, idTecnico);
        var actorKey = BuildTypingActorKey(request.IdConversacion, clienteId, usuario, sistema, idTecnico);
        if (string.IsNullOrWhiteSpace(actorKey))
            return Task.CompletedTask;

        if (!request.Escribiendo)
        {
            TypingPresences.TryRemove(actorKey, out _);
            return Task.CompletedTask;
        }

        var nowUtc = DateTime.UtcNow;
        TypingPresences[actorKey] = new TypingPresence(
            ActorKey: actorKey,
            IdConversacion: request.IdConversacion,
            ClienteId: clienteId,
            IdTecnico: idTecnico,
            NombreTecnico: nombreTecnico,
            Usuario: usuario,
            Sistema: sistema,
            LastSeenUtc: nowUtc,
            LastSeenLocal: DateTime.Now);

        PurgeExpiredTyping(nowUtc);
        return Task.CompletedTask;
    }

    public Task<ConversacionMessageResultDto> SendMessageAsync(ConversacionSendMessageRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SendMessage", async token =>
        {
            ValidateOutgoingRequest(request);

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            var isInternal = string.Equals(conversation.Canal, "INTERNO", StringComparison.OrdinalIgnoreCase);
            var now = BusinessNow();

            string initialState;
            ConversacionWhatsAppConfigDto? whatsAppConfig = null;
            if (isInternal)
            {
                initialState = "ENVIADO";
            }
            else
            {
                whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
                var windowActive = await IsWhatsAppWindowActiveAsync(request.IdConversacion, token);
                if (!windowActive)
                    throw new InvalidOperationException("La ventana de WhatsApp está vencida. Para retomar la conversación tenés que enviar una plantilla aprobada.");

                initialState = whatsAppConfig.IsConfiguredForSend ? "PENDIENTE" : "PENDIENTE_CONFIG";
            }

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                ReplyToMessageId = request.WhatsAppReplyToMessageId,
                MessageType = NormalizeMessageType(request.MessageType),
                Direction = "SALIENTE",
                EstadoEnvio = initialState,
                Text = request.Texto.Trim(),
                PayloadJson = string.Empty,
                FechaHora = now,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion,
                IdTecnicoAutor = request.IdTecnicoAutor
            }, token);

            string whatsAppMessageId = string.Empty;
            string finalState = initialState;
            string payload = string.Empty;

            if (!isInternal && whatsAppConfig?.IsConfiguredForSend == true)
            {
                try
                {
                    var sendResult = await SendToWhatsAppAsync(whatsAppConfig, conversation.TelefonoWhatsApp, request.Texto.Trim(), request.WhatsAppReplyToMessageId, token);
                    whatsAppMessageId = sendResult.WhatsAppMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                }
                catch (Exception ex)
                {
                    await UpdateMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

                    throw new InvalidOperationException("No se pudo enviar el mensaje por WhatsApp. Qued\u00f3 marcado con error en la conversaci\u00f3n.", ex);
                }
            }

            await UpdateMessageDeliveryAsync(messageId, finalState, whatsAppMessageId, payload, token);
            await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SendMessage",
                "CONV_MENSAJES",
                messageId.ToString(CultureInfo.InvariantCulture),
                "Mensaje saliente registrado.",
                new { request.IdConversacion, finalState, whatsAppMessageId },
                token);

            return new ConversacionMessageResultDto
            {
                IdMensaje = messageId,
                EstadoEnvio = finalState,
                WhatsAppMessageId = whatsAppMessageId
            };
        }, "No se pudo registrar el mensaje saliente.", ct);

    public Task<ConversacionMessageResultDto> SendReactionAsync(ConversacionReaccionRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SendReaction", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");
            if (request.IdMensaje <= 0)
                throw new InvalidOperationException("El mensaje a reaccionar es obligatorio.");

            var emoji = NormalizeReactionEmoji(request.Emoji);
            if (string.IsNullOrWhiteSpace(emoji))
                throw new InvalidOperationException("Elegí una reacción válida.");

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Las reacciones solo se envían por WhatsApp.");

            var target = await RequireMessageForReactionAsync(request.IdConversacion, request.IdMensaje, token);
            if (string.IsNullOrWhiteSpace(target.WhatsAppMessageId))
                throw new InvalidOperationException("Este mensaje todavía no tiene ID de WhatsApp para reaccionar.");

            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            var now = BusinessNow();
            var text = $"Reacción enviada: {emoji}";

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                ReplyToMessageId = target.WhatsAppMessageId,
                MessageType = "TEXT",
                Direction = "SALIENTE",
                EstadoEnvio = "PENDIENTE",
                Text = text,
                PayloadJson = string.Empty,
                FechaHora = now,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion,
                IdTecnicoAutor = request.IdTecnicoAutor
            }, token);

            var sendResult = await SendReactionToWhatsAppAsync(config, conversation.TelefonoWhatsApp, target.WhatsAppMessageId, emoji, token);
            await UpdateMessageDeliveryAsync(messageId, sendResult.EstadoEnvio, sendResult.WhatsAppMessageId, sendResult.PayloadJson, token);
            await RefreshConversationAsync(request.IdConversacion, now, text, token);

            return new ConversacionMessageResultDto
            {
                IdMensaje = messageId,
                EstadoEnvio = sendResult.EstadoEnvio,
                WhatsAppMessageId = sendResult.WhatsAppMessageId
            };
        }, "No se pudo enviar la reacción por WhatsApp.", ct);

    public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync(ConversacionPlantillaFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetTemplates", async token =>
        {
            filters ??= new();
            const string sql = """
                SELECT
                    IdPlantilla,
                    ISNULL(NombreVisible, ''),
                    ISNULL(NombreMeta, ''),
                    ISNULL(Categoria, ''),
                    ISNULL(Idioma, ''),
                    ISNULL(EncabezadoTexto, ''),
                    ISNULL(CuerpoTexto, ''),
                    ISNULL(PieTexto, ''),
                    ISNULL(EjemplosVariablesJson, ''),
                    ISNULL(EstadoLocal, ''),
                    ISNULL(EstadoMeta, ''),
                    ISNULL(MetaTemplateId, ''),
                    ISNULL(MetaRechazoMotivo, ''),
                    ISNULL(Activa, 1),
                    FechaHora_Grabacion,
                    FechaHora_Modificacion,
                    FechaHoraSincronizacion
                FROM dbo.CONV_PLANTILLAS
                WHERE
                    (@IncluirInactivas = 1 OR ISNULL(Activa, 1) = 1)
                    AND (@EstadoMeta IS NULL OR EstadoMeta = @EstadoMeta)
                    AND (
                        @Search IS NULL
                        OR NombreVisible LIKE @Search
                        OR NombreMeta LIKE @Search
                        OR CuerpoTexto LIKE @Search
                    )
                ORDER BY FechaHora_Grabacion DESC, IdPlantilla DESC
                """;

            var items = new List<ConversacionPlantillaDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IncluirInactivas", filters.IncluirInactivas);
            cmd.Parameters.AddWithValue("@EstadoMeta", DbNullable(filters.EstadoMeta));
            cmd.Parameters.AddWithValue("@Search", DbNullable(Like(filters.Search)));
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
                items.Add(ReadTemplate(rd));

            return (IReadOnlyList<ConversacionPlantillaDto>)items;
        }, "No se pudieron cargar las plantillas de WhatsApp.", ct);

    public Task<ConversacionPlantillaDto?> GetTemplateAsync(long idPlantilla, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetTemplate", async token =>
        {
            const string sql = """
                SELECT
                    IdPlantilla,
                    ISNULL(NombreVisible, ''),
                    ISNULL(NombreMeta, ''),
                    ISNULL(Categoria, ''),
                    ISNULL(Idioma, ''),
                    ISNULL(EncabezadoTexto, ''),
                    ISNULL(CuerpoTexto, ''),
                    ISNULL(PieTexto, ''),
                    ISNULL(EjemplosVariablesJson, ''),
                    ISNULL(EstadoLocal, ''),
                    ISNULL(EstadoMeta, ''),
                    ISNULL(MetaTemplateId, ''),
                    ISNULL(MetaRechazoMotivo, ''),
                    ISNULL(Activa, 1),
                    FechaHora_Grabacion,
                    FechaHora_Modificacion,
                    FechaHoraSincronizacion
                FROM dbo.CONV_PLANTILLAS
                WHERE IdPlantilla = @IdPlantilla
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdPlantilla", idPlantilla);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            return await rd.ReadAsync(token) ? ReadTemplate(rd) : null;
        }, "No se pudo cargar la plantilla de WhatsApp.", ct);

    public Task<long> SaveTemplateDraftAsync(ConversacionPlantillaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SaveTemplateDraft", async token =>
        {
            var normalized = NormalizeTemplateRequest(request);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureTemplateNameIsUniqueAsync(cn, normalized, token);

            if (normalized.IdPlantilla <= 0)
            {
                const string insertSql = """
                    INSERT INTO dbo.CONV_PLANTILLAS
                    (
                        NombreVisible,
                        NombreMeta,
                        Categoria,
                        Idioma,
                        EncabezadoTexto,
                        CuerpoTexto,
                        PieTexto,
                        EjemplosVariablesJson,
                        EstadoLocal,
                        EstadoMeta,
                        Activa,
                        UsuarioAccion,
                        SistemaAccion,
                        FechaHora_Grabacion
                    )
                    VALUES
                    (
                        @NombreVisible,
                        @NombreMeta,
                        @Categoria,
                        @Idioma,
                        @EncabezadoTexto,
                        @CuerpoTexto,
                        @PieTexto,
                        @EjemplosVariablesJson,
                        @EstadoLocal,
                        @EstadoMeta,
                        @Activa,
                        @UsuarioAccion,
                        @SistemaAccion,
                        GETDATE()
                    );

                    SELECT CAST(SCOPE_IDENTITY() AS bigint);
                    """;

                await using var cmd = new SqlCommand(insertSql, cn);
                AddTemplateParameters(cmd, normalized);
                cmd.Parameters.AddWithValue("@EstadoLocal", ConversacionPlantillaEstadosLocales.Borrador);
                cmd.Parameters.AddWithValue("@EstadoMeta", "DRAFT");
                var result = await cmd.ExecuteScalarAsync(token);
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }

            const string updateSql = """
                UPDATE dbo.CONV_PLANTILLAS
                SET
                    NombreVisible = @NombreVisible,
                    NombreMeta = @NombreMeta,
                    Categoria = @Categoria,
                    Idioma = @Idioma,
                    EncabezadoTexto = @EncabezadoTexto,
                    CuerpoTexto = @CuerpoTexto,
                    PieTexto = @PieTexto,
                    EjemplosVariablesJson = @EjemplosVariablesJson,
                    EstadoLocal = CASE WHEN EstadoMeta IN (N'APPROVED', N'PENDING') THEN EstadoLocal ELSE @EstadoLocal END,
                    EstadoMeta = CASE WHEN EstadoMeta IN (N'APPROVED', N'PENDING') THEN EstadoMeta ELSE @EstadoMeta END,
                    Activa = @Activa,
                    UsuarioAccion = @UsuarioAccion,
                    SistemaAccion = @SistemaAccion,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdPlantilla = @IdPlantilla
                """;

            await using var updateCmd = new SqlCommand(updateSql, cn);
            AddTemplateParameters(updateCmd, normalized);
            updateCmd.Parameters.AddWithValue("@EstadoLocal", ConversacionPlantillaEstadosLocales.Borrador);
            updateCmd.Parameters.AddWithValue("@EstadoMeta", "DRAFT");
            var affected = await updateCmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("La plantilla indicada no existe.");

            return normalized.IdPlantilla;
        }, "No se pudo guardar la plantilla de WhatsApp.", ct);

    public Task SubmitTemplateForApprovalAsync(ConversacionPlantillaSubmitRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SubmitTemplateForApproval", async token =>
        {
            if (request.IdPlantilla <= 0)
                throw new InvalidOperationException("La plantilla es obligatoria.");

            var template = await GetTemplateAsync(request.IdPlantilla, token)
                ?? throw new InvalidOperationException("La plantilla indicada no existe.");
            ValidateTemplateCanSubmit(template);

            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            if (string.IsNullOrWhiteSpace(config.BusinessAccountId))
                throw new InvalidOperationException("Falta configurar el WhatsApp Business Account ID.");
            if (string.IsNullOrWhiteSpace(config.AccessToken))
                throw new InvalidOperationException("Falta configurar el token de acceso de WhatsApp.");

            var submitResult = await CreateMetaTemplateAsync(config, template, token);
            await UpdateTemplateMetaStateAsync(
                template.IdPlantilla,
                ConversacionPlantillaEstadosLocales.Enviada,
                submitResult.EstadoMeta,
                submitResult.MetaTemplateId,
                submitResult.PayloadJson,
                string.Empty,
                token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SubmitTemplateForApproval",
                "CONV_PLANTILLAS",
                template.IdPlantilla.ToString(CultureInfo.InvariantCulture),
                "Plantilla enviada a aprobación de Meta.",
                new { template.NombreMeta, submitResult.EstadoMeta },
                token);

            return true;
        }, "No se pudo enviar la plantilla a aprobación de Meta.", ct);

    public Task SyncTemplateStatusAsync(long idPlantilla, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SyncTemplateStatus", async token =>
        {
            var template = await GetTemplateAsync(idPlantilla, token)
                ?? throw new InvalidOperationException("La plantilla indicada no existe.");
            if (string.IsNullOrWhiteSpace(template.NombreMeta))
                throw new InvalidOperationException("La plantilla no tiene nombre Meta para sincronizar.");

            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            if (string.IsNullOrWhiteSpace(config.BusinessAccountId) || string.IsNullOrWhiteSpace(config.AccessToken))
                throw new InvalidOperationException("Falta configurar la cuenta de WhatsApp para sincronizar plantillas.");

            var meta = await GetMetaTemplateStatusAsync(config, template, token);
            await UpdateTemplateMetaStateAsync(
                template.IdPlantilla,
                ConversacionPlantillaEstadosLocales.Sincronizada,
                meta.EstadoMeta,
                string.IsNullOrWhiteSpace(meta.MetaTemplateId) ? template.MetaTemplateId : meta.MetaTemplateId,
                meta.PayloadJson,
                meta.RechazoMotivo,
                token);

            return true;
        }, "No se pudo sincronizar la plantilla con Meta.", ct);

    public Task<ConversacionPlantillaMessageResultDto> SendTemplateMessageAsync(ConversacionPlantillaSendRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SendTemplateMessage", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");
            if (request.IdPlantilla <= 0)
                throw new InvalidOperationException("La plantilla es obligatoria.");

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Las plantillas solo se envían por WhatsApp.");
            if (string.IsNullOrWhiteSpace(conversation.TelefonoWhatsApp))
                throw new InvalidOperationException("La conversación no tiene teléfono WhatsApp.");

            var template = await GetTemplateAsync(request.IdPlantilla, token)
                ?? throw new InvalidOperationException("La plantilla indicada no existe.");

            var values = NormalizeTemplateValues(request.ValoresVariables);
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            if (!string.Equals(template.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                var meta = await GetMetaTemplateStatusAsync(config, template, token);
                await UpdateTemplateMetaStateAsync(
                    template.IdPlantilla,
                    ConversacionPlantillaEstadosLocales.Sincronizada,
                    meta.EstadoMeta,
                    string.IsNullOrWhiteSpace(meta.MetaTemplateId) ? template.MetaTemplateId : meta.MetaTemplateId,
                    meta.PayloadJson,
                    meta.RechazoMotivo,
                    token);

                template.EstadoMeta = meta.EstadoMeta;
                template.MetaTemplateId = string.IsNullOrWhiteSpace(meta.MetaTemplateId) ? template.MetaTemplateId : meta.MetaTemplateId;
            }

            if (!string.Equals(template.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Solo se pueden enviar plantillas aprobadas por Meta.");

            var now = BusinessNow();
            var previewText = RenderTemplatePreview(template.CuerpoTexto, values);

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                MessageType = "TEXT",
                Direction = "SALIENTE",
                EstadoEnvio = "PENDIENTE",
                Text = previewText,
                PayloadJson = string.Empty,
                FechaHora = now,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion,
                IdTecnicoAutor = request.IdTecnicoAutor
            }, token);

            var sendResult = await SendTemplateToWhatsAppAsync(config, conversation.TelefonoWhatsApp, template, values, token);
            await UpdateMessageDeliveryAsync(messageId, sendResult.EstadoEnvio, sendResult.WhatsAppMessageId, sendResult.PayloadJson, token);
            await RefreshConversationAsync(request.IdConversacion, now, previewText, token);

            return new ConversacionPlantillaMessageResultDto
            {
                IdMensaje = messageId,
                EstadoEnvio = sendResult.EstadoEnvio,
                WhatsAppMessageId = sendResult.WhatsAppMessageId
            };
        }, "No se pudo enviar la plantilla por WhatsApp.", ct);

    public Task<ConversacionPlantillaAutoValuesDto> GetTemplateAutoValuesAsync(long idConversacion, int variableCount, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetTemplateAutoValues", async token =>
        {
            if (idConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");

            var conversation = await GetConversationAsync(idConversacion, token)
                ?? throw new InvalidOperationException("La conversación indicada no existe.");

            var displayName = FirstNonEmpty(
                conversation.ClienteNombre,
                conversation.ContactoNombre,
                conversation.NombreVisible,
                conversation.TelefonoWhatsApp);

            var detail = string.Empty;
            var payment = string.Empty;
            var observations = new List<string>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!string.IsNullOrWhiteSpace(conversation.ClienteCodigo))
            {
                var debt = await TryBuildDebtDetailAsync(cn, conversation.ClienteCodigo, token);
                detail = debt.DetailText;
                if (!string.IsNullOrWhiteSpace(debt.Observation))
                    observations.Add(debt.Observation);
            }
            else
            {
                observations.Add("La conversación no tiene cliente vinculado; no se pudo calcular deuda automática.");
            }

            payment = await ReadConversationConfigValueAsync(cn, "CONV_COBRANZA_FORMA_PAGO", token);
            if (string.IsNullOrWhiteSpace(payment))
                observations.Add("Falta configurar CONV_COBRANZA_FORMA_PAGO en TA_CONFIGURACION.");

            var values = new List<string>();
            if (variableCount >= 1)
                values.Add(displayName);
            if (variableCount >= 2)
                values.Add(string.IsNullOrWhiteSpace(detail) ? "Detalle de deuda pendiente de completar." : detail);
            if (variableCount >= 3)
                values.Add(string.IsNullOrWhiteSpace(payment) ? "Datos de transferencia pendientes de configurar." : payment);

            while (values.Count < variableCount)
                values.Add($"Dato {values.Count + 1}");

            return new ConversacionPlantillaAutoValuesDto
            {
                Valores = values,
                ClienteCodigo = conversation.ClienteCodigo,
                ClienteNombre = conversation.ClienteNombre,
                Observaciones = string.Join(" ", observations)
            };
        }, "No se pudieron preparar las variables automáticas.", ct);

    public Task<long> AddInternalNoteAsync(ConversacionNotaInternaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "AddInternalNote", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.Texto))
                throw new InvalidOperationException("La nota interna no puede estar vacía.");

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            var text = request.Texto.Trim();
            var now = BusinessNow();

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                MessageType = "TEXT",
                Direction = "NOTA_INTERNA",
                EstadoEnvio = string.Empty,
                Text = text,
                PayloadJson = string.Empty,
                FechaHora = now,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion,
                IdTecnicoAutor = request.IdTecnicoAutor
            }, token);

            await RefreshConversationAsync(request.IdConversacion, now, $"Nota interna: {TrimForSummary(text)}", token);
            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "AddInternalNote",
                "CONV_MENSAJES",
                messageId.ToString(CultureInfo.InvariantCulture),
                "Nota interna agregada a la conversación.",
                new { request.IdConversacion },
                token);

            return messageId;
        }, "No se pudo agregar la nota interna.", ct);

    public Task<long> AddInternalEventAsync(ConversacionEventoInternoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "AddInternalEvent", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaci\u00f3n es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.Texto))
                throw new InvalidOperationException("El evento interno no puede estar vac\u00edo.");

            return await AddInternalEventCoreAsync(
                request.IdConversacion,
                request.Texto,
                request.IdTecnicoAutor,
                request.NombreTecnicoAccion,
                request.UsuarioAccion,
                request.SistemaAccion,
                token);
        }, "No se pudo registrar el evento interno.", ct);

    public async Task AssignConversationAsync(ConversacionAsignacionRequest request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "AssignConversation", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");

            var technicianId = await ResolveTechnicianIdOrNullAsync(request.IdTecnico, token);
            var previousTechnicianId = await GetConversationTechnicianIdAsync(request.IdConversacion, token);

            const string updateSql = """
                UPDATE dbo.CONV_CONVERSACIONES
                SET
                    IdTecnico = @IdTecnico,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @IdConversacion
                """;

            const string historySql = """
                INSERT INTO dbo.CONV_ASIGNACIONES
                (
                    IdConversacion,
                    FechaHora,
                    IdTecnico,
                    UsuarioAccion,
                    SistemaAccion,
                    Observaciones
                )
                VALUES
                (
                    @IdConversacion,
                    GETDATE(),
                    @IdTecnico,
                    @UsuarioAccion,
                    @SistemaAccion,
                    @Observaciones
                )
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            await using (var cmd = new SqlCommand(updateSql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                cmd.Parameters.AddWithValue("@IdTecnico", DbNullablePreserve(technicianId));
                await cmd.ExecuteNonQueryAsync(token);
            }

            await using (var cmd = new SqlCommand(historySql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                cmd.Parameters.AddWithValue("@IdTecnico", DbNullablePreserve(technicianId));
                cmd.Parameters.AddWithValue("@UsuarioAccion", DbNullable(request.UsuarioAccion));
                cmd.Parameters.AddWithValue("@SistemaAccion", DbNullable(request.SistemaAccion));
                cmd.Parameters.AddWithValue("@Observaciones", DbNullable(request.Observaciones));
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            if (!string.Equals(NormalizeTechnicianId(previousTechnicianId), NormalizeTechnicianId(technicianId), StringComparison.OrdinalIgnoreCase))
            {
                var actorName = await BuildActionActorNameAsync(request.IdTecnicoAutor, request.NombreTecnicoAccion, request.UsuarioAccion, token);
                var targetName = string.IsNullOrWhiteSpace(technicianId)
                    ? string.Empty
                    : FirstNonEmpty(await TryGetTechnicianNameAsync(technicianId, token), technicianId);
                var eventText = string.IsNullOrWhiteSpace(targetName)
                    ? $"{actorName} dej\u00f3 la conversaci\u00f3n sin asignar."
                    : $"{actorName} asign\u00f3 la conversaci\u00f3n a {targetName}.";

                await AddInternalEventCoreAsync(
                    request.IdConversacion,
                    eventText,
                    request.IdTecnicoAutor,
                    request.NombreTecnicoAccion,
                    request.UsuarioAccion,
                    request.SistemaAccion,
                    token);
            }

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "AssignConversation",
                "CONV_CONVERSACIONES",
                request.IdConversacion.ToString(CultureInfo.InvariantCulture),
                "Asignación de conversación actualizada.",
                new { IdTecnico = technicianId },
                token);

            return true;
        }, "No se pudo asignar la conversación.", ct);
    }

    public async Task ChangeStatusAsync(ConversacionEstadoRequest request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "ChangeStatus", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.CodigoEstado))
                throw new InvalidOperationException("El estado es obligatorio.");

            var state = request.CodigoEstado.Trim().ToUpperInvariant();
            var isClosed = await GetStateClosedFlagAsync(state, token);
            var wasClosed = await GetConversationClosedFlagAsync(request.IdConversacion, token);
            var previousState = await GetConversationStateAsync(request.IdConversacion, token);
            var previousTechnicianId = await GetConversationTechnicianIdAsync(request.IdConversacion, token);

            const string sql = """
                UPDATE dbo.CONV_CONVERSACIONES
                SET
                    CodigoEstado = @CodigoEstado,
                    IdTecnico = CASE WHEN @EsCerrado = 1 THEN NULL ELSE IdTecnico END,
                    FechaHoraCierre = CASE WHEN @EsCerrado = 1 THEN ISNULL(FechaHoraCierre, GETDATE()) ELSE NULL END,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @IdConversacion
                """;

            const string unassignHistorySql = """
                INSERT INTO dbo.CONV_ASIGNACIONES
                (
                    IdConversacion,
                    FechaHora,
                    IdTecnico,
                    UsuarioAccion,
                    SistemaAccion,
                    Observaciones
                )
                VALUES
                (
                    @IdConversacion,
                    GETDATE(),
                    NULL,
                    @UsuarioAccion,
                    @SistemaAccion,
                    @Observaciones
                )
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = await cn.BeginTransactionAsync(token);

            await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
            {
                cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                cmd.Parameters.AddWithValue("@CodigoEstado", state);
                cmd.Parameters.AddWithValue("@EsCerrado", isClosed);
                await cmd.ExecuteNonQueryAsync(token);
            }

            if (isClosed && !string.IsNullOrWhiteSpace(previousTechnicianId))
            {
                await using var cmd = new SqlCommand(unassignHistorySql, cn, (SqlTransaction)tx);
                cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
                cmd.Parameters.AddWithValue("@UsuarioAccion", DbNullable(request.UsuarioAccion));
                cmd.Parameters.AddWithValue("@SistemaAccion", DbNullable(request.SistemaAccion));
                cmd.Parameters.AddWithValue("@Observaciones", DbNullable("Asignacion limpiada automaticamente al cerrar la conversacion."));
                await cmd.ExecuteNonQueryAsync(token);
            }

            await tx.CommitAsync(token);

            if (!string.Equals(previousState.CodigoEstado, state, StringComparison.OrdinalIgnoreCase))
            {
                var actorName = await BuildActionActorNameAsync(request.IdTecnicoAutor, request.NombreTecnicoAccion, request.UsuarioAccion, token);
                var nextStateName = FirstNonEmpty(await TryGetStateNameAsync(state, token), state);
                var eventText = isClosed && !wasClosed
                    ? $"{actorName} cerr\u00f3 la conversaci\u00f3n."
                    : $"{actorName} cambi\u00f3 el estado de {FirstNonEmpty(previousState.Descripcion, previousState.CodigoEstado, "Sin estado")} a {nextStateName}.";

                await AddInternalEventCoreAsync(
                    request.IdConversacion,
                    eventText,
                    request.IdTecnicoAutor,
                    request.NombreTecnicoAccion,
                    request.UsuarioAccion,
                    request.SistemaAccion,
                    token);
            }

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "ChangeStatus",
                "CONV_CONVERSACIONES",
                request.IdConversacion.ToString(CultureInfo.InvariantCulture),
                "Estado de conversación actualizado.",
                new { CodigoEstado = state, request.Observaciones, request.UsuarioAccion, request.SistemaAccion },
                token);

            return true;
        }, "No se pudo cambiar el estado de la conversación.", ct);
    }

    public Task SetConversationPinAsync(long idConversacion, string usuario, string? sistema, bool fijada, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SetConversationPin", async token =>
        {
            if (idConversacion <= 0)
                throw new InvalidOperationException("La conversaci\u00f3n es obligatoria.");

            var normalizedUser = NormalizePinUser(usuario);
            if (string.IsNullOrWhiteSpace(normalizedUser))
                throw new InvalidOperationException("No se pudo identificar el usuario actual para fijar la conversaci\u00f3n.");

            var normalizedSystem = NormalizePinSystem(sistema);

            const string upsertSql = """
                DELETE FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO
                WHERE Usuario = @Usuario
                  AND IdConversacion = @IdConversacion;

                INSERT INTO dbo.CONV_CONVERSACIONES_PIN_USUARIO
                (
                    Usuario,
                    Sistema,
                    IdConversacion,
                    FechaHora_Grabacion
                )
                VALUES
                (
                    @Usuario,
                    @Sistema,
                    @IdConversacion,
                    GETDATE()
                );
                """;

            const string deleteSql = """
                DELETE FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO
                WHERE Usuario = @Usuario
                  AND IdConversacion = @IdConversacion;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureConversationPinsTableAsync(cn, token);

            await using var cmd = new SqlCommand(fijada ? upsertSql : deleteSql, cn);
            cmd.Parameters.AddWithValue("@Usuario", normalizedUser);
            cmd.Parameters.AddWithValue("@Sistema", normalizedSystem);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            await cmd.ExecuteNonQueryAsync(token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SetConversationPin",
                "CONV_CONVERSACIONES_PIN_USUARIO",
                idConversacion.ToString(CultureInfo.InvariantCulture),
                fijada ? "Conversaci\u00f3n fijada por usuario." : "Conversaci\u00f3n desfijada por usuario.",
                new { Usuario = normalizedUser, Sistema = normalizedSystem },
                token);

            return true;
        }, "No se pudo actualizar el pin de la conversaci\u00f3n.", ct);

    public Task MarkConversationReadAsync(long idConversacion, string usuario, string? sistema, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "MarkConversationRead", async token =>
        {
            if (idConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");

            var normalizedUser = NormalizePinUser(usuario);
            if (string.IsNullOrWhiteSpace(normalizedUser))
                throw new InvalidOperationException("No se pudo identificar el usuario actual para marcar la conversación como leída.");

            var normalizedSystem = NormalizePinSystem(sistema);

            const string sql = """
                DECLARE @IdUltimoMensajeLeido bigint;

                SELECT @IdUltimoMensajeLeido = MAX(m.IdMensaje)
                FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = @IdConversacion
                  AND m.Direction = N'ENTRANTE';

                IF @IdUltimoMensajeLeido IS NULL
                    SET @IdUltimoMensajeLeido = 0;

                DELETE FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO
                WHERE Usuario = @Usuario
                  AND IdConversacion = @IdConversacion;

                INSERT INTO dbo.CONV_CONVERSACIONES_LECTURA_USUARIO
                (
                    Usuario,
                    Sistema,
                    IdConversacion,
                    IdUltimoMensajeLeido,
                    FechaHora_Grabacion,
                    FechaHora_Modificacion
                )
                VALUES
                (
                    @Usuario,
                    @Sistema,
                    @IdConversacion,
                    @IdUltimoMensajeLeido,
                    GETDATE(),
                    GETDATE()
                );
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureConversationReadStateTableAsync(cn, token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Usuario", normalizedUser);
            cmd.Parameters.AddWithValue("@Sistema", normalizedSystem);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            await cmd.ExecuteNonQueryAsync(token);

            return true;
        }, "No se pudo marcar la conversación como leída.", ct);

    public Task<ConversacionWebhookResultDto> RegisterIncomingWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RegisterIncomingWebhook", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var payloadJson = request.Payload.RootElement.GetRawText();
            var headerJson = JsonSerializer.Serialize(request.Headers);

            var webhookLogId = await InsertWebhookLogAsync(payloadJson, headerJson, token);
            var parsedMessages = ParseIncomingMessages(request.Payload.RootElement);
            var parsedStatuses = ParseIncomingStatuses(request.Payload.RootElement);
            var whatsAppConfig = parsedMessages.Any(x => x.Attachments.Count > 0)
                ? await conversacionesConfigService.GetWhatsAppConfigAsync(token)
                : null;
            var processed = 0;

            foreach (var status in parsedStatuses)
            {
                await UpdateWhatsAppMessageStatusAsync(status, token);
                processed++;
            }

            foreach (var incoming in parsedMessages)
            {
                var conversationId = await EnsureConversationAsync(incoming, token);
                var messageId = await InsertMessageAsync(new PendingMessageInsert
                {
                    ConversationId = conversationId,
                    Phone = incoming.Phone,
                    MessageType = incoming.MessageType,
                    Direction = "ENTRANTE",
                    EstadoEnvio = "RECIBIDO",
                    Text = incoming.Text,
                    PayloadJson = incoming.RawJson,
                    FechaHora = NormalizeIncomingTimestamp(incoming.Timestamp),
                    UsuarioAutor = string.Empty,
                    SistemaAutor = string.Empty,
                    IdTecnicoAutor = string.Empty,
                    WhatsAppMessageId = incoming.WhatsAppMessageId,
                    ReplyToMessageId = incoming.WhatsAppReplyToMessageId
                }, token);

                if (incoming.Attachments.Count > 0 && whatsAppConfig is not null)
                    await StoreIncomingAttachmentsAsync(conversationId, messageId, incoming, whatsAppConfig, token);

                await RefreshConversationAsync(conversationId, NormalizeIncomingTimestamp(incoming.Timestamp), incoming.Text, token, reopenIfClosed: true);
                await NotifyIncomingMessageAsync(conversationId, messageId, token);
                processed++;
            }

            await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "RegisterIncomingWebhook",
                "CONV_WEBHOOK_LOG",
                webhookLogId.ToString(CultureInfo.InvariantCulture),
                "Webhook de WhatsApp procesado.",
                new { Mensajes = parsedMessages.Count, Procesados = processed },
                token);

            return new ConversacionWebhookResultDto
            {
                IdWebhookLog = webhookLogId,
                MensajesDetectados = parsedMessages.Count + parsedStatuses.Count,
                MensajesProcesados = processed
            };
        }, "No se pudo procesar el webhook de WhatsApp.", ct);

    public Task<long> CreateInternalThreadAsync(ConversacionCrearHiloInternoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "CreateInternalThread", async token =>
        {
            if (string.IsNullOrWhiteSpace(request.NombreHilo))
                throw new InvalidOperationException("El nombre del hilo es obligatorio.");

            var technicianId = await ResolveTechnicianIdOrNullAsync(request.IdTecnico, token);

            const string sql = """
                INSERT INTO dbo.CONV_CONVERSACIONES
                (
                    Canal,
                    NombreVisible,
                    CodigoEstado,
                    IdTecnico,
                    FechaHoraUltimoMensaje,
                    FechaHora_Grabacion
                )
                VALUES
                (
                    N'INTERNO',
                    @NombreVisible,
                    N'ABIERTA',
                    @IdTecnico,
                    GETDATE(),
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@NombreVisible", request.NombreHilo.Trim());
            cmd.Parameters.AddWithValue("@IdTecnico", DbNullablePreserve(technicianId));
            var result = await cmd.ExecuteScalarAsync(token);
            var id = Convert.ToInt64(result, CultureInfo.InvariantCulture);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "CreateInternalThread",
                "CONV_CONVERSACIONES",
                id.ToString(CultureInfo.InvariantCulture),
                "Hilo interno creado.",
                new { request.NombreHilo, IdTecnico = technicianId },
                token);

            return id;
        }, "No se pudo crear el hilo interno.", ct);

    public Task<ConversacionCrearWhatsAppResultDto> CreateOrGetWhatsAppConversationAsync(ConversacionCrearWhatsAppRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "CreateOrGetWhatsAppConversation", async token =>
        {
            var phone = NormalizePhone(request.TelefonoWhatsApp);
            if (string.IsNullOrWhiteSpace(phone))
                throw new InvalidOperationException("Ingresá un número de celular para crear la conversación.");
            if (phone.Length < 8)
                throw new InvalidOperationException("El número de celular parece incompleto.");

            var technicianId = await ResolveTechnicianIdOrNullAsync(request.IdTecnico, token);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var contact = await TryFindContactByPhoneAsync(cn, phone, token);
            var existing = await FindWhatsAppConversationByPhoneAsync(cn, phone, contact.IdContact, token);
            if (existing is not null)
            {
                if (contact.IdContact.HasValue)
                    await AssociateContactIfMissingAsync(cn, existing.IdConversacion, contact, token);

                await MergeDuplicateWhatsAppConversationsAsync(cn, existing.IdConversacion, phone, contact.IdContact, token);

                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "CreateOrGetWhatsAppConversation",
                    "CONV_CONVERSACIONES",
                    existing.IdConversacion.ToString(CultureInfo.InvariantCulture),
                    "Conversación de WhatsApp existente abierta desde búsqueda.",
                    new { TelefonoWhatsApp = phone, ContactoAsociado = contact.IdContact.HasValue },
                    token);

                return new ConversacionCrearWhatsAppResultDto
                {
                    IdConversacion = existing.IdConversacion,
                    Creada = false,
                    ContactoAsociado = contact.IdContact.HasValue || existing.IdContacto.HasValue,
                    TelefonoWhatsApp = phone,
                    NombreVisible = FirstNonEmpty(existing.NombreVisible, contact.Name, phone)
                };
            }

            const string insertSql = """
                INSERT INTO dbo.CONV_CONVERSACIONES
                (
                    Canal,
                    TelefonoWhatsApp,
                    NombreVisible,
                    ClienteCodigo,
                    IdContacto,
                    CodigoEstado,
                    IdTecnico,
                    ResumenUltimoMensaje,
                    FechaHoraPrimerMensaje,
                    FechaHoraUltimoMensaje,
                    FechaHora_Grabacion
                )
                VALUES
                (
                    N'WHATSAPP',
                    @TelefonoWhatsApp,
                    @NombreVisible,
                    @ClienteCodigo,
                    @IdContacto,
                    @CodigoEstadoInicial,
                    @IdTecnico,
                    @ResumenInicial,
                    GETDATE(),
                    GETDATE(),
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            var displayName = FirstNonEmpty(contact.Name, phone);
            await using var cmd = new SqlCommand(insertSql, cn);
            cmd.Parameters.AddWithValue("@TelefonoWhatsApp", phone);
            cmd.Parameters.AddWithValue("@NombreVisible", DbNullable(displayName));
            cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(contact.ClientCode));
            cmd.Parameters.AddWithValue("@IdContacto", contact.IdContact.HasValue ? contact.IdContact.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@CodigoEstadoInicial", ManualWhatsAppInitialState);
            cmd.Parameters.AddWithValue("@IdTecnico", DbNullablePreserve(technicianId));
            cmd.Parameters.AddWithValue("@ResumenInicial", ManualWhatsAppConversationSummary);
            var result = await cmd.ExecuteScalarAsync(token);
            var id = Convert.ToInt64(result, CultureInfo.InvariantCulture);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "CreateOrGetWhatsAppConversation",
                "CONV_CONVERSACIONES",
                id.ToString(CultureInfo.InvariantCulture),
                "Conversación de WhatsApp creada manualmente.",
                new { TelefonoWhatsApp = phone, ContactoAsociado = contact.IdContact.HasValue, contact.ClientCode, IdTecnico = technicianId },
                token);

            return new ConversacionCrearWhatsAppResultDto
            {
                IdConversacion = id,
                Creada = true,
                ContactoAsociado = contact.IdContact.HasValue,
                TelefonoWhatsApp = phone,
                NombreVisible = displayName
            };
        }, "No se pudo crear la conversación de WhatsApp.", ct);

    public Task<ConversacionAdjuntoDto> UploadAttachmentAsync(ConversacionUploadAdjuntoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "UploadAttachment", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.NombreArchivo))
                throw new InvalidOperationException("El nombre del archivo es obligatorio.");
            if (request.TamanoBytes <= 0)
                throw new InvalidOperationException("El archivo está vacío.");

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            var isInternal = string.Equals(conversation.Canal, "INTERNO", StringComparison.OrdinalIgnoreCase);
            var messageType = NormalizeMessageType(request.TipoArchivo);
            var mimeType = NormalizeOutgoingMime(request.MimeType, request.NombreArchivo, messageType);
            var nombreArchivo = request.NombreArchivo.Trim();
            var now = BusinessNow();
            string initialState;
            ConversacionWhatsAppConfigDto? whatsAppConfig = null;

            if (isInternal)
            {
                initialState = "ENVIADO";
            }
            else
            {
                whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
                var windowActive = await IsWhatsAppWindowActiveAsync(request.IdConversacion, token);
                if (!windowActive && !request.PermitirEnvioConVentanaVencida)
                    throw new InvalidOperationException("La ventana de WhatsApp está vencida. Para retomar la conversación tenés que enviar una plantilla aprobada.");

                initialState = whatsAppConfig.IsConfiguredForSend ? "PENDIENTE" : "PENDIENTE_CONFIG";
            }

            var folder = Path.Combine(UploadsBasePath, request.IdConversacion.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(nombreArchivo).ToLowerInvariant();
            var safeFileName = $"{Guid.NewGuid():N}{ext}";
            var rutaLocal = Path.Combine(folder, safeFileName);

            await using (var fs = File.Create(rutaLocal))
                await request.Contenido.CopyToAsync(fs, token);

            if (!isInternal && ShouldConvertWebmOpusForWhatsApp(rutaLocal, mimeType, nombreArchivo))
            {
                var oggBytes = ConvertWebmOpusFileToOgg(rutaLocal);
                var oggPath = Path.ChangeExtension(rutaLocal, ".ogg");
                await File.WriteAllBytesAsync(oggPath, oggBytes, token);
                TryDeleteFile(rutaLocal);

                rutaLocal = oggPath;
                nombreArchivo = Path.ChangeExtension(nombreArchivo, ".ogg");
                mimeType = "audio/ogg";
                messageType = "AUDIO";
            }

            var tamanoBytes = new FileInfo(rutaLocal).Length;

            string whatsAppMessageId = string.Empty;
            string finalState = initialState;
            string payload = string.Empty;

            if (!isInternal && whatsAppConfig?.IsConfiguredForSend == true)
            {
                var sendResult = await SendAttachmentToWhatsAppAsync(
                    whatsAppConfig,
                    conversation.TelefonoWhatsApp,
                    rutaLocal,
                    nombreArchivo,
                    mimeType,
                    messageType,
                    token);

                whatsAppMessageId = sendResult.WhatsAppMessageId;
                finalState = sendResult.EstadoEnvio;
                payload = sendResult.PayloadJson;
            }

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                WhatsAppMessageId = whatsAppMessageId,
                MessageType = messageType,
                Direction = "SALIENTE",
                EstadoEnvio = finalState,
                Text = nombreArchivo,
                PayloadJson = payload,
                FechaHora = now,
                IdTecnicoAutor = request.IdTecnicoAutor,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion
            }, token);

            var adjuntoId = await InsertAttachmentRecordAsync(
                messageId,
                messageType,
                nombreArchivo,
                mimeType,
                rutaLocal,
                tamanoBytes,
                payload,
                token);

            await RefreshConversationAsync(request.IdConversacion, now, $"[{messageType}] {nombreArchivo}", token);

            return new ConversacionAdjuntoDto
            {
                IdAdjunto = adjuntoId,
                IdMensaje = messageId,
                TipoArchivo = messageType,
                NombreArchivo = nombreArchivo,
                MimeType = mimeType,
                RutaLocal = rutaLocal,
                TamanoBytes = tamanoBytes
            };
        }, "No se pudo guardar el adjunto.", ct);

    public Task<IReadOnlyList<ConversacionAdjuntoDto>> GetConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetConversationAttachments", async token =>
        {
            const string sql = """
                SELECT
                    a.IdAdjunto,
                    a.IdMensaje,
                    ISNULL(a.TipoArchivo, ''),
                    ISNULL(a.NombreArchivo, ''),
                    ISNULL(a.MimeType, ''),
                    ISNULL(a.UrlArchivo, ''),
                    ISNULL(a.RutaLocal, ''),
                    ISNULL(a.TamanoBytes, 0)
                FROM dbo.CONV_ADJUNTOS a
                INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = a.IdMensaje
                WHERE m.IdConversacion = @IdConversacion
                ORDER BY a.IdAdjunto
                """;

            var items = new List<ConversacionAdjuntoDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionAdjuntoDto
                {
                    IdAdjunto = rd.GetInt64(0),
                    IdMensaje = rd.GetInt64(1),
                    TipoArchivo = GetString(rd, 2),
                    NombreArchivo = GetString(rd, 3),
                    MimeType = GetString(rd, 4),
                    UrlArchivo = GetString(rd, 5),
                    RutaLocal = GetString(rd, 6),
                    TamanoBytes = rd.IsDBNull(7) ? 0 : rd.GetInt64(7)
                });
            }

            return (IReadOnlyList<ConversacionAdjuntoDto>)items;
        }, "No se pudieron cargar los adjuntos.", ct);

    public Task<IReadOnlyList<ConversacionStickerFavoritoDto>> GetFavoriteStickersAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetFavoriteStickers", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureFavoriteStickersTableAsync(cn, token);

            const string sql = """
                SELECT f.IdFavorito, f.IdAdjunto, ISNULL(f.Nombre, ''), ISNULL(a.MimeType, 'image/webp')
                FROM dbo.CONV_STICKERS_FAVORITOS f
                INNER JOIN dbo.CONV_ADJUNTOS a ON a.IdAdjunto = f.IdAdjunto
                ORDER BY f.FechaHora_Grabacion DESC
                """;

            var items = new List<ConversacionStickerFavoritoDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionStickerFavoritoDto
                {
                    IdFavorito = rd.GetInt64(0),
                    IdAdjunto = rd.GetInt64(1),
                    Nombre = GetString(rd, 2),
                    MimeType = GetString(rd, 3)
                });
            }

            return (IReadOnlyList<ConversacionStickerFavoritoDto>)items;
        }, "No se pudieron cargar los stickers favoritos.", ct);

    public async Task SaveFavoriteStickerAsync(long idAdjunto, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "SaveFavoriteSticker", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureFavoriteStickersTableAsync(cn, token);

            const string sql = """
                INSERT INTO dbo.CONV_STICKERS_FAVORITOS (IdAdjunto, Nombre, FechaHora_Grabacion)
                SELECT @IdAdjunto, ISNULL(NULLIF(a.NombreArchivo, ''), N'Sticker'), GETDATE()
                FROM dbo.CONV_ADJUNTOS a
                WHERE a.IdAdjunto = @IdAdjunto
                  AND (UPPER(a.TipoArchivo) = N'STICKER' OR LOWER(ISNULL(a.MimeType, '')) = N'image/webp')
                  AND NOT EXISTS (SELECT 1 FROM dbo.CONV_STICKERS_FAVORITOS f WHERE f.IdAdjunto = a.IdAdjunto)
                """;

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
            var rows = await cmd.ExecuteNonQueryAsync(token);
            if (rows == 0)
                throw new InvalidOperationException("El sticker ya estaba en favoritos o no es un sticker válido.");

            return true;
        }, "No se pudo guardar el sticker favorito.", ct);
    }

    public Task<ConversacionAdjuntoDto> SendFavoriteStickerAsync(long idConversacion, long idFavorito, string? idTecnicoAutor = null, string? usuarioAccion = null, string? sistemaAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SendFavoriteSticker", async token =>
        {
            var sticker = await GetFavoriteStickerFileAsync(idFavorito, token);
            await using var stream = File.OpenRead(sticker.RutaLocal);
            return await UploadAttachmentAsync(new ConversacionUploadAdjuntoRequest
            {
                IdConversacion = idConversacion,
                NombreArchivo = sticker.NombreArchivo,
                MimeType = string.IsNullOrWhiteSpace(sticker.MimeType) ? "image/webp" : sticker.MimeType,
                TipoArchivo = "STICKER",
                Contenido = stream,
                TamanoBytes = new FileInfo(sticker.RutaLocal).Length,
                IdTecnicoAutor = idTecnicoAutor,
                UsuarioAccion = usuarioAccion,
                SistemaAccion = sistemaAccion
            }, token);
        }, "No se pudo enviar el sticker favorito.", ct);

    public Task<ConversacionAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetAttachmentForServe", async token =>
        {
            const string sql = """
                SELECT
                    a.IdAdjunto,
                    a.IdMensaje,
                    m.IdConversacion,
                    ISNULL(a.TipoArchivo, ''),
                    ISNULL(a.NombreArchivo, ''),
                    ISNULL(a.MimeType, ''),
                    ISNULL(a.UrlArchivo, ''),
                    ISNULL(a.RutaLocal, ''),
                    ISNULL(a.PayloadJson, ''),
                    ISNULL(m.PayloadJson, '')
                FROM dbo.CONV_ADJUNTOS a
                INNER JOIN dbo.CONV_MENSAJES m
                    ON m.IdMensaje = a.IdMensaje
                WHERE a.IdAdjunto = @IdAdjunto
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
            AttachmentServeRecord record;
            await using (var rd = await cmd.ExecuteReaderAsync(token))
            {
                if (!await rd.ReadAsync(token))
                    return null;

                record = new AttachmentServeRecord
                {
                    IdAdjunto = rd.GetInt64(0),
                    IdMensaje = rd.GetInt64(1),
                    IdConversacion = rd.GetInt64(2),
                    TipoArchivo = GetString(rd, 3),
                    NombreArchivo = GetString(rd, 4),
                    MimeType = GetString(rd, 5),
                    UrlArchivo = GetString(rd, 6),
                    RutaLocal = GetString(rd, 7),
                    AdjuntoPayloadJson = GetString(rd, 8),
                    MensajePayloadJson = GetString(rd, 9)
                };
            }

            var rutaLocal = ResolveExistingAttachmentPath(record);
            if (!string.IsNullOrWhiteSpace(rutaLocal) && !string.Equals(rutaLocal, record.RutaLocal, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateAttachmentLocalPathAsync(record.IdAdjunto, rutaLocal, token);
                record.RutaLocal = rutaLocal;
            }

            if (string.IsNullOrWhiteSpace(rutaLocal))
                rutaLocal = await TryRecoverAttachmentFileAsync(record, token);

            var nombreDescarga = await BuildAttachmentDownloadNameAsync(cn, record, token);
            return new ConversacionAdjuntoServeDto
            {
                RutaLocal = rutaLocal,
                MimeType = record.MimeType,
                NombreArchivo = record.NombreArchivo,
                NombreDescarga = nombreDescarga
            };
        }, "No se pudo obtener el adjunto.", ct);

    private static async Task<string> BuildAttachmentDownloadNameAsync(SqlConnection cn, AttachmentServeRecord record, CancellationToken ct)
    {
        var kind = NormalizeAttachmentDownloadKind(record.TipoArchivo, record.MimeType);
        if (string.IsNullOrWhiteSpace(kind))
            return SanitizeDownloadFileName(record.NombreArchivo, "Attachment", InferExtension(record.MimeType, record.TipoArchivo));

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.CONV_ADJUNTOS a
            INNER JOIN dbo.CONV_MENSAJES m
                ON m.IdMensaje = a.IdMensaje
            WHERE m.IdConversacion = @IdConversacion
              AND a.IdAdjunto <= @IdAdjunto
              AND (
                    @Kind = N'Image'
                    AND (UPPER(ISNULL(a.TipoArchivo, '')) IN (N'IMAGE', N'STICKER') OR LOWER(ISNULL(a.MimeType, '')) LIKE N'image/%')
                  OR
                    @Kind = N'Audio'
                    AND (UPPER(ISNULL(a.TipoArchivo, '')) = N'AUDIO' OR LOWER(ISNULL(a.MimeType, '')) LIKE N'audio/%')
                  OR
                    @Kind = N'Video'
                    AND (UPPER(ISNULL(a.TipoArchivo, '')) = N'VIDEO' OR LOWER(ISNULL(a.MimeType, '')) LIKE N'video/%')
                  OR
                    @Kind = N'Document'
                    AND NOT (
                        UPPER(ISNULL(a.TipoArchivo, '')) IN (N'IMAGE', N'STICKER', N'AUDIO', N'VIDEO')
                        OR LOWER(ISNULL(a.MimeType, '')) LIKE N'image/%'
                        OR LOWER(ISNULL(a.MimeType, '')) LIKE N'audio/%'
                        OR LOWER(ISNULL(a.MimeType, '')) LIKE N'video/%'
                    )
                  );
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", record.IdConversacion);
        cmd.Parameters.AddWithValue("@IdAdjunto", record.IdAdjunto);
        cmd.Parameters.AddWithValue("@Kind", kind);
        var index = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (index <= 0)
            index = 1;

        var extension = InferExtension(record.MimeType, record.TipoArchivo);
        return SanitizeDownloadFileName($"{kind} ({index}){extension}", kind, extension);
    }

    private static string NormalizeAttachmentDownloadKind(string tipoArchivo, string mimeType)
    {
        if (string.Equals(tipoArchivo, "IMAGE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tipoArchivo, "STICKER", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "Image";
        if (string.Equals(tipoArchivo, "AUDIO", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "Audio";
        if (string.Equals(tipoArchivo, "VIDEO", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return "Video";

        return "Document";
    }

    private static string SanitizeDownloadFileName(string fileName, string fallbackBase, string extension)
    {
        var normalized = string.IsNullOrWhiteSpace(fileName) ? $"{fallbackBase}{extension}" : Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(normalized) ? $"{fallbackBase}{extension}" : normalized;
    }

    private async Task<int> StoreIncomingAttachmentsAsync(
        long conversationId,
        long messageId,
        IncomingWhatsAppMessage incoming,
        ConversacionWhatsAppConfigDto config,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.AccessToken))
            return 0;

        var stored = 0;
        foreach (var attachment in incoming.Attachments)
        {
            try
            {
                var media = await GetWhatsAppMediaAsync(config, attachment.MediaId, ct);
                var bytes = await DownloadWhatsAppMediaAsync(config, media.Url, ct);
                var mimeType = FirstNonEmpty(media.MimeType, attachment.MimeType, InferMimeFromType(attachment.TipoArchivo));
                var fileName = BuildIncomingFileName(attachment, mimeType);
                var rutaLocal = await SaveIncomingAttachmentAsync(conversationId, fileName, bytes, ct);

                await InsertAttachmentRecordAsync(
                    messageId,
                    attachment.TipoArchivo,
                    fileName,
                    mimeType,
                    rutaLocal,
                    bytes.LongLength,
                    JsonSerializer.Serialize(new
                    {
                        attachment.MediaId,
                        media.Sha256,
                        media.FileSize,
                        media.MimeType
                    }),
                    ct);
                stored++;
            }
            catch (Exception ex)
            {
                await _appEvents.LogErrorAsync(
                    "Conversaciones",
                    "DownloadIncomingAttachment",
                    ex,
                    "No se pudo descargar un adjunto entrante de WhatsApp.",
                    new { incoming.WhatsAppMessageId, attachment.MediaId, attachment.TipoArchivo },
                    AppEventSeverity.Warning,
                    ct);
            }
        }

        return stored;
    }

    private async Task HydrateMissingIncomingMediaAsync(List<PendingMediaHydration> pendingItems, CancellationToken ct)
    {
        ConversacionWhatsAppConfigDto? whatsAppConfig = null;

        foreach (var item in pendingItems)
        {
            if (!MediaHydrationAttempts.TryAdd(item.Message.IdMensaje, 0))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(item.PayloadJson);
                var attachments = ExtractIncomingAttachments(doc.RootElement, item.Message.MessageType);
                if (attachments.Count == 0)
                    continue;

                whatsAppConfig ??= await conversacionesConfigService.GetWhatsAppConfigAsync(ct);
                var stored = await StoreIncomingAttachmentsAsync(
                    item.Message.IdConversacion,
                    item.Message.IdMensaje,
                    new IncomingWhatsAppMessage
                    {
                        Phone = item.Message.TelefonoWhatsApp,
                        MessageType = item.Message.MessageType,
                        WhatsAppMessageId = item.Message.WhatsAppMessageId,
                        Timestamp = item.Message.FechaHora,
                        Text = item.Message.Texto,
                        RawJson = item.PayloadJson,
                        Attachments = attachments
                    },
                    whatsAppConfig,
                    ct);

                if (stored > 0)
                    item.Message.TieneAdjuntos = true;
            }
            catch (Exception ex)
            {
                MediaHydrationAttempts.TryRemove(item.Message.IdMensaje, out _);

                await _appEvents.LogErrorAsync(
                    "Conversaciones",
                    "HydrateMissingIncomingMedia",
                    ex,
                    "No se pudo completar un adjunto entrante previamente recibido.",
                    new { item.Message.IdMensaje, item.Message.WhatsAppMessageId, item.Message.MessageType },
                    AppEventSeverity.Warning,
                    ct);
            }
        }
    }

    private string ResolveExistingAttachmentPath(AttachmentServeRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.RutaLocal) && File.Exists(record.RutaLocal))
            return record.RutaLocal;

        foreach (var candidate in BuildAttachmentPathCandidates(record))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }

    private IEnumerable<string> BuildAttachmentPathCandidates(AttachmentServeRecord record)
    {
        var fileName = Path.GetFileName(record.RutaLocal);
        if (!string.IsNullOrWhiteSpace(fileName))
            yield return Path.Combine(UploadsBasePath, record.IdConversacion.ToString(CultureInfo.InvariantCulture), fileName);

        if (!string.IsNullOrWhiteSpace(record.RutaLocal))
        {
            const string marker = "App_Data";
            var index = record.RutaLocal.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var relativeToContent = record.RutaLocal[index..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                yield return Path.Combine(environment.ContentRootPath, relativeToContent);
            }

            if (!Path.IsPathRooted(record.RutaLocal))
                yield return Path.Combine(environment.ContentRootPath, record.RutaLocal);
        }

        if (!string.IsNullOrWhiteSpace(record.UrlArchivo) && !Uri.TryCreate(record.UrlArchivo, UriKind.Absolute, out _))
        {
            var relativeUrl = record.UrlArchivo.TrimStart('/', '\\')
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            yield return Path.Combine(environment.ContentRootPath, relativeUrl);
        }
    }

    private async Task<string> TryRecoverAttachmentFileAsync(AttachmentServeRecord record, CancellationToken ct)
    {
        if (AttachmentRecoveryFailures.ContainsKey(record.IdAdjunto))
            return string.Empty;

        var mediaId = TryExtractMediaId(record.AdjuntoPayloadJson, record.TipoArchivo)
            ?? TryExtractMediaId(record.MensajePayloadJson, record.TipoArchivo);
        if (string.IsNullOrWhiteSpace(mediaId))
            return string.Empty;

        try
        {
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(ct);
            if (string.IsNullOrWhiteSpace(config.AccessToken))
                return string.Empty;

            var media = await GetWhatsAppMediaAsync(config, mediaId, ct);
            var bytes = await DownloadWhatsAppMediaAsync(config, media.Url, ct);
            if (bytes.Length == 0)
                return string.Empty;

            var mimeType = FirstNonEmpty(media.MimeType, record.MimeType, InferMimeFromType(record.TipoArchivo));
            var fileName = string.IsNullOrWhiteSpace(record.NombreArchivo)
                ? BuildIncomingFileName(new IncomingWhatsAppAttachment
                {
                    MediaId = mediaId,
                    TipoArchivo = record.TipoArchivo,
                    MimeType = mimeType
                }, mimeType)
                : record.NombreArchivo;
            var rutaLocal = await SaveIncomingAttachmentAsync(record.IdConversacion, fileName, bytes, ct);

            await UpdateRecoveredAttachmentAsync(record.IdAdjunto, rutaLocal, mimeType, bytes.LongLength, ct);

            record.RutaLocal = rutaLocal;
            record.MimeType = mimeType;
            return rutaLocal;
        }
        catch (Exception ex)
        {
            AttachmentRecoveryFailures.TryAdd(record.IdAdjunto, 0);

            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "RecoverAttachmentFile",
                ex,
                "No se pudo recuperar un archivo adjunto faltante.",
                new { record.IdAdjunto, record.IdMensaje, record.IdConversacion, MediaId = mediaId, record.TipoArchivo },
                AppEventSeverity.Warning,
                ct);
            return string.Empty;
        }
    }

    private async Task UpdateAttachmentLocalPathAsync(long idAdjunto, string rutaLocal, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_ADJUNTOS
            SET RutaLocal = @RutaLocal
            WHERE IdAdjunto = @IdAdjunto
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@RutaLocal", rutaLocal);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateRecoveredAttachmentAsync(long idAdjunto, string rutaLocal, string mimeType, long tamanoBytes, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_ADJUNTOS
            SET
                RutaLocal = @RutaLocal,
                MimeType = CASE WHEN @MimeType IS NULL OR LTRIM(RTRIM(@MimeType)) = '' THEN MimeType ELSE @MimeType END,
                TamanoBytes = @TamanoBytes
            WHERE IdAdjunto = @IdAdjunto
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@RutaLocal", rutaLocal);
        cmd.Parameters.AddWithValue("@MimeType", DbNullable(mimeType));
        cmd.Parameters.AddWithValue("@TamanoBytes", tamanoBytes);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateAttachmentPayloadAsync(long idAdjunto, string payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return;

        const string sql = """
            UPDATE dbo.CONV_ADJUNTOS
            SET PayloadJson = @PayloadJson
            WHERE IdAdjunto = @IdAdjunto
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@PayloadJson", payloadJson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? TryExtractMediaId(string payloadJson, string tipoArchivo)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (TryReadStringProperty(root, "MediaId", out var mediaId) ||
                TryReadStringProperty(root, "mediaId", out mediaId) ||
                TryReadStringProperty(root, "id", out mediaId))
            {
                return mediaId;
            }

            var attachments = ExtractIncomingAttachments(root, tipoArchivo);
            return attachments.Count > 0 ? attachments[0].MediaId : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<DebtTemplateDetail> TryBuildDebtDetailAsync(SqlConnection cn, string clienteCodigo, CancellationToken ct)
    {
        foreach (var source in DebtSourceCandidates)
        {
            var columns = await GetSqlObjectColumnsAsync(cn, source, ct);
            if (columns.Count == 0 ||
                !columns.Contains("CUENTA") ||
                !columns.Contains("SALDO"))
            {
                continue;
            }

            var dateColumn = FirstMatchingColumn(columns, "VENCIMIENTO", "FECHAVTO", "FECHA_VTO", "FECHA");
            var comprobanteDateColumn = FirstMatchingColumn(columns, "FECHA", "FECHACOMPROBANTE", "FECHA_COMPROBANTE", "EMISION");
            var objectName = $"dbo.{QuoteSqlName(source)}";

            var daysExpression = string.IsNullOrWhiteSpace(dateColumn)
                ? "0"
                : $"MAX(CASE WHEN TRY_CONVERT(date, {QuoteSqlName(dateColumn)}) IS NULL THEN 0 ELSE DATEDIFF(day, TRY_CONVERT(date, {QuoteSqlName(dateColumn)}), GETDATE()) END)";
            var oldestExpression = string.IsNullOrWhiteSpace(comprobanteDateColumn)
                ? "NULL"
                : $"MIN(TRY_CONVERT(date, {QuoteSqlName(comprobanteDateColumn)}))";

            var sql = $"""
                SELECT
                    ISNULL(SUM(CONVERT(decimal(18, 2), ISNULL({QuoteSqlName("SALDO")}, 0))), 0) AS SaldoActual,
                    COUNT(1) AS Cantidad,
                    {daysExpression} AS DiasAtraso,
                    {oldestExpression} AS FechaAntigua
                FROM {objectName}
                WHERE LTRIM(RTRIM(CONVERT(nvarchar(50), {QuoteSqlName("CUENTA")}))) = @Cuenta
                  AND ISNULL(CONVERT(decimal(18, 2), {QuoteSqlName("SALDO")}), 0) > 0
                """;

            try
            {
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Cuenta", clienteCodigo.Trim());
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (!await rd.ReadAsync(ct))
                    continue;

                var saldo = rd.IsDBNull(0) ? 0 : Convert.ToDecimal(rd.GetValue(0), CultureInfo.InvariantCulture);
                var cantidad = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture);
                var dias = rd.IsDBNull(2) ? 0 : Math.Max(0, Convert.ToInt32(rd.GetValue(2), CultureInfo.InvariantCulture));
                var fecha = rd.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(rd.GetValue(3), CultureInfo.InvariantCulture);

                if (cantidad <= 0)
                {
                    return new DebtTemplateDetail
                    {
                        DetailText = "No se registran comprobantes pendientes de pago.",
                        Observation = $"Fuente consultada: {source}."
                    };
                }

                var detail = $"""
                    SALDO ACTUAL............................................: {saldo:N2}
                    Cantidad de comprobantes pendientes de pago: {cantidad}
                    Dias de atraso...............................................: {dias}
                    Fecha comprobante mas antiguo....................: {(fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "Sin dato")}
                    """;

                return new DebtTemplateDetail
                {
                    DetailText = detail,
                    Observation = $"Fuente consultada: {source}."
                };
            }
            catch (Exception ex)
            {
                await _appEvents.LogErrorAsync(
                    "Conversaciones",
                    "BuildDebtTemplateDetail",
                    ex,
                    "No se pudo consultar una fuente de deuda para plantillas.",
                    new { Fuente = source, Cliente = clienteCodigo },
                    AppEventSeverity.Warning,
                    ct);
            }
        }

        return new DebtTemplateDetail
        {
            DetailText = string.Empty,
            Observation = "No se encontró una vista de saldos compatible para calcular deuda automática."
        };
    }

    private static async Task<HashSet<string>> GetSqlObjectColumnsAsync(SqlConnection cn, string objectName, CancellationToken ct)
    {
        const string sql = """
            SELECT UPPER(name)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@ObjectName)
            """;

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@ObjectName", $"dbo.{objectName}");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result.Add(GetString(rd, 0));

        return result;
    }

    private async Task<string> ReadConversationConfigValueAsync(SqlConnection cn, string key, CancellationToken ct)
    {
        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({QuoteSqlName(detailColumn)}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Clave", key.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return string.Empty;

        return FirstNonEmpty(GetString(rd, 0), GetString(rd, 1));
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END
            """;

        await using var cmd = new SqlCommand(sql, cn);
        var result = Convert.ToString(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(result) ? "DESCRIPCION" : result;
    }

    private static string FirstMatchingColumn(HashSet<string> columns, params string[] names)
        => names.FirstOrDefault(columns.Contains) ?? string.Empty;

    private static string QuoteSqlName(string value)
        => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private async Task<long> EnsureConversationAsync(IncomingWhatsAppMessage incoming, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var contact = await TryFindContactByPhoneAsync(cn, incoming.Phone, ct);
        var existing = await FindWhatsAppConversationByPhoneAsync(cn, incoming.Phone, contact.IdContact, ct);
        if (existing is not null)
        {
            if (contact.IdContact.HasValue)
                await AssociateContactIfMissingAsync(cn, existing.IdConversacion, contact, ct);

            await MergeDuplicateWhatsAppConversationsAsync(cn, existing.IdConversacion, incoming.Phone, contact.IdContact, ct);

            return existing.IdConversacion;
        }

        var displayName = !string.IsNullOrWhiteSpace(incoming.ContactName)
            ? incoming.ContactName
            : contact.Name;

        const string insertSql = """
            INSERT INTO dbo.CONV_CONVERSACIONES
            (
                Canal,
                TelefonoWhatsApp,
                NombreVisible,
                ClienteCodigo,
                IdContacto,
                CodigoEstado,
                IdTecnico,
                ResumenUltimoMensaje,
                FechaHoraPrimerMensaje,
                FechaHoraUltimoMensaje,
                FechaHora_Grabacion
            )
            VALUES
            (
                N'WHATSAPP',
                @TelefonoWhatsApp,
                @NombreVisible,
                @ClienteCodigo,
                @IdContacto,
                N'ABIERTA',
                NULL,
                @ResumenUltimoMensaje,
                @FechaHora,
                @FechaHora,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var cmd = new SqlCommand(insertSql, cn);
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", incoming.Phone);
        cmd.Parameters.AddWithValue("@NombreVisible", DbNullable(displayName));
        cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(contact.ClientCode));
        cmd.Parameters.AddWithValue("@IdContacto", contact.IdContact.HasValue ? contact.IdContact.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(incoming.Text)));
        cmd.Parameters.AddWithValue("@FechaHora", NormalizeIncomingTimestamp(incoming.Timestamp));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<ConversationIdentity> RequireConversationAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                IdConversacion,
                ISNULL(TelefonoWhatsApp, ''),
                ISNULL(Canal, 'WHATSAPP')
            FROM dbo.CONV_CONVERSACIONES
            WHERE IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        if (!await rd.ReadAsync(ct))
            throw new InvalidOperationException("La conversación indicada no existe.");

        return new ConversationIdentity
        {
            IdConversacion = rd.GetInt64(0),
            TelefonoWhatsApp = GetString(rd, 1),
            Canal = GetString(rd, 2)
        };
    }

    private async Task<MessageReactionTarget> RequireMessageForReactionAsync(long idConversacion, long idMensaje, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                IdMensaje,
                ISNULL(WhatsAppMessageId, ''),
                ISNULL(Direction, '')
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND IdMensaje = @IdMensaje
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        if (!await rd.ReadAsync(ct))
            throw new InvalidOperationException("El mensaje indicado no existe en esta conversación.");

        return new MessageReactionTarget
        {
            IdMensaje = rd.GetInt64(0),
            WhatsAppMessageId = GetString(rd, 1),
            Direction = GetString(rd, 2)
        };
    }

    private async Task<bool> IsWhatsAppWindowActiveAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) FechaHora
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND Direction = N'ENTRANTE'
            ORDER BY FechaHora DESC, IdMensaje DESC
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            return false;

        var lastClientMessage = Convert.ToDateTime(result, CultureInfo.InvariantCulture);
        return BusinessNow() <= NormalizeStoredConversationTime(lastClientMessage).AddHours(24);
    }

    private async Task<long> InsertMessageAsync(PendingMessageInsert message, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.CONV_MENSAJES
            (
                IdConversacion,
                TelefonoWhatsApp,
                WhatsAppMessageId,
                WhatsAppReplyToMessageId,
                MessageType,
                Direction,
                EstadoEnvio,
                Texto,
                PayloadJson,
                FechaHora,
                UsuarioAutor,
                SistemaAutor,
                IdTecnicoAutor,
                FechaHora_Grabacion
            )
            VALUES
            (
                @IdConversacion,
                @TelefonoWhatsApp,
                @WhatsAppMessageId,
                @WhatsAppReplyToMessageId,
                @MessageType,
                @Direction,
                @EstadoEnvio,
                @Texto,
                @PayloadJson,
                @FechaHora,
                @UsuarioAutor,
                @SistemaAutor,
                @IdTecnicoAutor,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        var idTecnicoAutor = await ResolveTechnicianIdOrNullAsync(message.IdTecnicoAutor, ct);
        cmd.Parameters.AddWithValue("@IdConversacion", message.ConversationId);
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", DbNullable(message.Phone));
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", DbNullable(message.WhatsAppMessageId));
        cmd.Parameters.AddWithValue("@WhatsAppReplyToMessageId", DbNullable(message.ReplyToMessageId));
        cmd.Parameters.AddWithValue("@MessageType", NormalizeMessageType(message.MessageType));
        cmd.Parameters.AddWithValue("@Direction", message.Direction);
        cmd.Parameters.AddWithValue("@EstadoEnvio", DbNullable(message.EstadoEnvio));
        cmd.Parameters.AddWithValue("@Texto", DbNullable(message.Text));
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(message.PayloadJson));
        cmd.Parameters.AddWithValue("@FechaHora", message.FechaHora);
        cmd.Parameters.AddWithValue("@UsuarioAutor", DbNullable(message.UsuarioAutor));
        cmd.Parameters.AddWithValue("@SistemaAutor", DbNullable(message.SistemaAutor));
        cmd.Parameters.AddWithValue("@IdTecnicoAutor", DbNullablePreserve(idTecnicoAutor));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task NotifyIncomingMessageAsync(long conversationId, long messageId, CancellationToken ct)
    {
        try
        {
            await _notificacionesPushService.NotifyNewMessageAsync(conversationId, messageId, ct);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "NotifyIncomingMessage",
                ex,
                "No se pudo enviar la notificación push del mensaje entrante.",
                new { IdConversacion = conversationId, IdMensaje = messageId },
                AppEventSeverity.Warning,
                ct);
        }
    }

    private async Task<long> InsertAttachmentRecordAsync(
        long messageId,
        string tipoArchivo,
        string nombreArchivo,
        string mimeType,
        string rutaLocal,
        long tamanoBytes,
        string payloadJson,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.CONV_ADJUNTOS
            (
                IdMensaje,
                TipoArchivo,
                NombreArchivo,
                MimeType,
                RutaLocal,
                TamanoBytes,
                PayloadJson,
                FechaHora_Grabacion
            )
            VALUES
            (
                @IdMensaje,
                @TipoArchivo,
                @NombreArchivo,
                @MimeType,
                @RutaLocal,
                @TamanoBytes,
                @PayloadJson,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdMensaje", messageId);
        cmd.Parameters.AddWithValue("@TipoArchivo", NormalizeMessageType(tipoArchivo));
        cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);
        cmd.Parameters.AddWithValue("@MimeType", DbNullable(mimeType));
        cmd.Parameters.AddWithValue("@RutaLocal", rutaLocal);
        cmd.Parameters.AddWithValue("@TamanoBytes", tamanoBytes);
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(payloadJson));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<long> AddInternalEventCoreAsync(
        long idConversacion,
        string text,
        string? idTecnicoAutor,
        string? nombreTecnicoAccion,
        string? usuarioAccion,
        string? sistemaAccion,
        CancellationToken ct)
    {
        var cleanText = text.Trim();
        var now = BusinessNow();
        var technicianId = await ResolveTechnicianIdOrNullAsync(idTecnicoAutor, ct);
        var conversation = await RequireConversationAsync(idConversacion, ct);

        var messageId = await InsertMessageAsync(new PendingMessageInsert
        {
            ConversationId = idConversacion,
            Phone = conversation.TelefonoWhatsApp,
            MessageType = InternalEventMessageType,
            Direction = InternalEventDirection,
            EstadoEnvio = string.Empty,
            Text = cleanText,
            PayloadJson = string.Empty,
            FechaHora = now,
            UsuarioAutor = usuarioAccion,
            SistemaAutor = sistemaAccion,
            IdTecnicoAutor = technicianId
        }, ct);

        await RefreshConversationAsync(idConversacion, now, cleanText, ct);

        await _appEvents.LogAuditAsync(
            "Conversaciones",
            "AddInternalEvent",
            "CONV_MENSAJES",
            messageId.ToString(CultureInfo.InvariantCulture),
            "Evento interno agregado a la conversaci\u00f3n.",
            new { IdConversacion = idConversacion, Texto = cleanText, IdTecnicoAutor = technicianId, NombreTecnicoAccion = nombreTecnicoAccion, UsuarioAccion = usuarioAccion, SistemaAccion = sistemaAccion },
            ct);

        return messageId;
    }

    private async Task RefreshConversationAsync(long idConversacion, DateTime fechaHora, string? text, CancellationToken ct, bool reopenIfClosed = false)
    {
        const string sql = """
            UPDATE c
            SET
                ResumenUltimoMensaje = CASE
                    WHEN @FechaHora >= ISNULL(FechaHoraUltimoMensaje, CONVERT(datetime, '19000101', 112)) THEN @ResumenUltimoMensaje
                    ELSE ResumenUltimoMensaje
                END,
                FechaHoraPrimerMensaje = ISNULL(FechaHoraPrimerMensaje, @FechaHora),
                FechaHoraUltimoMensaje = CASE
                    WHEN @FechaHora >= ISNULL(FechaHoraUltimoMensaje, CONVERT(datetime, '19000101', 112)) THEN @FechaHora
                    ELSE FechaHoraUltimoMensaje
                END,
                CodigoEstado = CASE
                    WHEN @Reabrir = 1 AND (
                        c.FechaHoraCierre IS NOT NULL
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) IN (N'CERRADA', N'CERRADO')
                        OR EXISTS (
                            SELECT 1
                            FROM dbo.CONV_ESTADOS e
                            WHERE e.CodigoEstado = c.CodigoEstado
                              AND ISNULL(e.EsCerrado, 0) = 1
                        )
                    ) THEN N'ABIERTA'
                    ELSE CodigoEstado
                END,
                FechaHoraCierre = CASE
                    WHEN @Reabrir = 1 AND (
                        c.FechaHoraCierre IS NOT NULL
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) IN (N'CERRADA', N'CERRADO')
                        OR EXISTS (
                            SELECT 1
                            FROM dbo.CONV_ESTADOS e
                            WHERE e.CodigoEstado = c.CodigoEstado
                              AND ISNULL(e.EsCerrado, 0) = 1
                        )
                    ) THEN NULL
                    ELSE FechaHoraCierre
                END,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            WHERE c.IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(text)));
        cmd.Parameters.AddWithValue("@FechaHora", fechaHora);
        cmd.Parameters.AddWithValue("@Reabrir", reopenIfClosed);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReopenClosedConversationsWithIncomingAfterCloseAsync(SqlConnection cn, long? idConversacion, CancellationToken ct)
    {
        const string sql = """
            UPDATE c
               SET CodigoEstado = N'ABIERTA',
                   FechaHoraCierre = NULL,
                   FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            LEFT JOIN dbo.CONV_ESTADOS e
                ON e.CodigoEstado = c.CodigoEstado
            WHERE (
                  ISNULL(e.EsCerrado, 0) = 1
                  OR c.FechaHoraCierre IS NOT NULL
                  OR UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) IN (N'CERRADA', N'CERRADO')
              )
              AND (@IdConversacion IS NULL OR c.IdConversacion = @IdConversacion)
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.CONV_MENSAJES m
                  WHERE m.IdConversacion = c.IdConversacion
                    AND m.Direction = N'ENTRANTE'
                    AND (
                        c.FechaHoraCierre IS NULL
                        OR m.FechaHora > c.FechaHoraCierre
                        OR m.FechaHora_Grabacion > c.FechaHoraCierre
                    )
              );
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion.HasValue ? idConversacion.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateMessageDeliveryAsync(long idMensaje, string estadoEnvio, string whatsAppMessageId, string payloadJson, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_MENSAJES
            SET
                EstadoEnvio = @EstadoEnvio,
                WhatsAppMessageId = CASE WHEN @WhatsAppMessageId IS NULL THEN WhatsAppMessageId ELSE @WhatsAppMessageId END,
                PayloadJson = CASE
                    WHEN @PayloadJson IS NULL OR LTRIM(RTRIM(@PayloadJson)) = '' THEN PayloadJson
                    ELSE @PayloadJson
                END,
                FechaHora_Modificacion = GETDATE()
            WHERE IdMensaje = @IdMensaje
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);
        cmd.Parameters.AddWithValue("@EstadoEnvio", DbNullable(estadoEnvio));
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", DbNullable(whatsAppMessageId));
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(payloadJson));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateWhatsAppMessageStatusAsync(IncomingWhatsAppStatus status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(status.WhatsAppMessageId))
            return;

        const string sql = """
            UPDATE dbo.CONV_MENSAJES
            SET
                EstadoEnvio = CASE
                    WHEN EstadoEnvio = N'LEIDO' THEN EstadoEnvio
                    WHEN EstadoEnvio = N'ENTREGADO' AND @EstadoEnvio IN (N'ENVIADO_META') THEN EstadoEnvio
                    ELSE @EstadoEnvio
                END,
                PayloadJson = CASE
                    WHEN @PayloadJson IS NULL OR LTRIM(RTRIM(@PayloadJson)) = '' THEN PayloadJson
                    ELSE @PayloadJson
                END,
                FechaHora_Modificacion = GETDATE()
            WHERE WhatsAppMessageId = @WhatsAppMessageId
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", status.WhatsAppMessageId);
        cmd.Parameters.AddWithValue("@EstadoEnvio", status.EstadoEnvio);
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(status.RawJson));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateTemplateMetaStateAsync(
        long idPlantilla,
        string estadoLocal,
        string estadoMeta,
        string metaTemplateId,
        string payloadJson,
        string rechazoMotivo,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_PLANTILLAS
            SET
                EstadoLocal = @EstadoLocal,
                EstadoMeta = @EstadoMeta,
                MetaTemplateId = @MetaTemplateId,
                MetaPayloadJson = @MetaPayloadJson,
                MetaRechazoMotivo = @MetaRechazoMotivo,
                FechaHoraSincronizacion = GETDATE(),
                FechaHora_Modificacion = GETDATE()
            WHERE IdPlantilla = @IdPlantilla
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdPlantilla", idPlantilla);
        cmd.Parameters.AddWithValue("@EstadoLocal", estadoLocal);
        cmd.Parameters.AddWithValue("@EstadoMeta", string.IsNullOrWhiteSpace(estadoMeta) ? "PENDING" : estadoMeta.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@MetaTemplateId", DbNullable(metaTemplateId));
        cmd.Parameters.AddWithValue("@MetaPayloadJson", DbNullable(payloadJson));
        cmd.Parameters.AddWithValue("@MetaRechazoMotivo", DbNullable(rechazoMotivo));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<MetaTemplateResult> CreateMetaTemplateAsync(ConversacionWhatsAppConfigDto config, ConversacionPlantillaDto template, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.BusinessAccountId}/message_templates";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        request.Content = new StringContent(JsonSerializer.Serialize(BuildMetaTemplateCreatePayload(template)), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta devolvi\u00f3 {(int)response.StatusCode} al crear la plantilla: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new MetaTemplateResult
        {
            MetaTemplateId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            EstadoMeta = root.TryGetProperty("status", out var status) ? status.GetString() ?? "PENDING" : "PENDING",
            PayloadJson = body
        };
    }

    private async Task<MetaTemplateStatusResult> GetMetaTemplateStatusAsync(ConversacionWhatsAppConfigDto config, ConversacionPlantillaDto template, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.BusinessAccountId}/message_templates?name={Uri.EscapeDataString(template.NombreMeta)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta devolvi\u00f3 {(int)response.StatusCode} al sincronizar la plantilla: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Meta no devolvi\u00f3 informaci\u00f3n de plantillas.");

        foreach (var item in data.EnumerateArray())
        {
            var language = item.TryGetProperty("language", out var languageProp) ? languageProp.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(language, template.Idioma, StringComparison.OrdinalIgnoreCase))
                continue;

            return new MetaTemplateStatusResult
            {
                MetaTemplateId = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                EstadoMeta = item.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                RechazoMotivo = item.TryGetProperty("rejected_reason", out var rejected) ? rejected.GetString() ?? string.Empty : string.Empty,
                PayloadJson = item.GetRawText()
            };
        }

        throw new InvalidOperationException("No se encontro la plantilla en Meta para el idioma configurado.");
    }

    private async Task<WhatsAppSendResult> SendTemplateToWhatsAppAsync(
        ConversacionWhatsAppConfigDto config,
        string phone,
        ConversacionPlantillaDto template,
        IReadOnlyList<string> values,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForSend)
            throw new InvalidOperationException("Falta configurar WhatsApp para enviar mensajes.");

        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var components = new List<Dictionary<string, object?>>();
        if (values.Count > 0)
        {
            components.Add(new Dictionary<string, object?>
            {
                ["type"] = "body",
                ["parameters"] = values.Select(value => new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = value
                }).ToList()
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = phone,
            ["type"] = "template",
            ["template"] = new Dictionary<string, object?>
            {
                ["name"] = template.NombreMeta,
                ["language"] = new Dictionary<string, object?> { ["code"] = template.Idioma },
                ["components"] = components
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta devolvi\u00f3 {(int)response.StatusCode}: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar plantilla");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    private async Task<WhatsAppSendResult> SendToWhatsAppAsync(ConversacionWhatsAppConfigDto config, string phone, string text, string? replyToMessageId, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = phone,
            ["type"] = "text",
            ["text"] = new Dictionary<string, object?> { ["body"] = text }
        };

        if (!string.IsNullOrWhiteSpace(replyToMessageId))
            payload["context"] = new Dictionary<string, object?> { ["message_id"] = replyToMessageId.Trim() };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolvió {(int)response.StatusCode}: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar mensaje");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    private async Task<WhatsAppSendResult> SendReactionToWhatsAppAsync(ConversacionWhatsAppConfigDto config, string phone, string targetMessageId, string emoji, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = phone,
            ["type"] = "reaction",
            ["reaction"] = new Dictionary<string, object?>
            {
                ["message_id"] = targetMessageId.Trim(),
                ["emoji"] = emoji
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolvi\u00f3 {(int)response.StatusCode} al enviar reacci\u00f3n: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar reacci\u00f3n");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    private async Task<WhatsAppSendResult> SendAttachmentToWhatsAppAsync(
        ConversacionWhatsAppConfigDto config,
        string phone,
        string rutaLocal,
        string nombreArchivo,
        string mimeType,
        string tipoArchivo,
        CancellationToken ct)
    {
        if (!File.Exists(rutaLocal))
            throw new InvalidOperationException("No se encontro el archivo local para enviar por WhatsApp.");

        var mediaId = await UploadWhatsAppMediaAsync(config, rutaLocal, nombreArchivo, mimeType, ct);
        var normalizedType = NormalizeOutgoingMediaType(tipoArchivo, mimeType);
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var mediaPayload = new Dictionary<string, object?> { ["id"] = mediaId };
        if (normalizedType == "document" && !string.IsNullOrWhiteSpace(nombreArchivo))
            mediaPayload["filename"] = nombreArchivo.Trim();

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = phone,
            ["type"] = normalizedType,
            [normalizedType] = mediaPayload
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolvi\u00f3 {(int)response.StatusCode} al enviar adjunto: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar adjunto");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                MediaId = mediaId,
                SendResponse = JsonSerializer.Deserialize<JsonElement>(responseBody)
            })
        };
    }

    private async Task<string> UploadWhatsAppMediaAsync(
        ConversacionWhatsAppConfigDto config,
        string rutaLocal,
        string nombreArchivo,
        string mimeType,
        CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/media";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        await using var fs = File.OpenRead(rutaLocal);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whatsapp"), "messaging_product");
        content.Add(new StringContent(FirstNonEmpty(mimeType, "application/octet-stream")), "type");

        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(FirstNonEmpty(mimeType, "application/octet-stream"));
        content.Add(fileContent, "file", string.IsNullOrWhiteSpace(nombreArchivo) ? Path.GetFileName(rutaLocal) : nombreArchivo);
        request.Content = content;

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolvi\u00f3 {(int)response.StatusCode} al subir media: {body}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("id", out var id))
            return id.GetString() ?? string.Empty;

        throw new InvalidOperationException("Meta no devolvi\u00f3 identificador del archivo subido.");
    }

    private async Task<WhatsAppMediaInfo> GetWhatsAppMediaAsync(ConversacionWhatsAppConfigDto config, string mediaId, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{config.ApiVersion}/{mediaId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolvi\u00f3 {(int)response.StatusCode} al obtener media: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new WhatsAppMediaInfo
        {
            Url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty,
            MimeType = root.TryGetProperty("mime_type", out var mimeProp) ? mimeProp.GetString() ?? string.Empty : string.Empty,
            Sha256 = root.TryGetProperty("sha256", out var shaProp) ? shaProp.GetString() ?? string.Empty : string.Empty,
            FileSize = root.TryGetProperty("file_size", out var sizeProp) && sizeProp.TryGetInt64(out var size) ? size : 0
        };
    }

    private async Task<byte[]> DownloadWhatsAppMediaAsync(ConversacionWhatsAppConfigDto config, string mediaUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            throw new InvalidOperationException("Meta no devolvi\u00f3 URL para descargar el adjunto.");

        using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Meta devolvi\u00f3 {(int)response.StatusCode} al descargar media: {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<long> InsertWebhookLogAsync(string payloadJson, string headerJson, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.CONV_WEBHOOK_LOG
            (
                Proveedor,
                Evento,
                PayloadJson,
                HeaderJson,
                ProcesadoOk,
                FechaHoraRecepcion,
                Intentos
            )
            VALUES
            (
                N'META_WHATSAPP',
                N'Webhook',
                @PayloadJson,
                @HeaderJson,
                NULL,
                GETDATE(),
                1
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@PayloadJson", payloadJson);
        cmd.Parameters.AddWithValue("@HeaderJson", DbNullable(headerJson));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task UpdateWebhookLogAsync(long idWebhookLog, bool procesadoOk, string? errorDescripcion, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_WEBHOOK_LOG
            SET
                ProcesadoOk = @ProcesadoOk,
                ErrorDescripcion = @ErrorDescripcion,
                FechaHoraProcesamiento = GETDATE()
            WHERE IdWebhookLog = @IdWebhookLog
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdWebhookLog", idWebhookLog);
        cmd.Parameters.AddWithValue("@ProcesadoOk", procesadoOk);
        cmd.Parameters.AddWithValue("@ErrorDescripcion", DbNullable(errorDescripcion));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<ContactLookupResult> TryFindContactByPhoneAsync(SqlConnection cn, string phone, CancellationToken ct)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return ContactLookupResult.Empty;

        var phoneTail = GetPhoneComparableTail(normalizedPhone);
        var sql = $"""
            SELECT TOP (1)
                c.id,
                ISNULL(COALESCE(contactoCuenta.Cuenta, NULLIF(UPPER(LTRIM(RTRIM(c.CuentaRel))), '')), ''),
                ISNULL(c.Nombre_y_Apellido, '')
            FROM dbo.MA_CONTACTOS c
            OUTER APPLY (
                SELECT TOP (1) UPPER(LTRIM(RTRIM(rel.Cuenta))) AS Cuenta
                FROM dbo.MA_CONTACTOS_CUENTAS rel
                INNER JOIN dbo.VT_CLIENTES cli
                    ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(rel.Cuenta)))
                WHERE rel.IdContacto = ISNULL(NULLIF(c.idContacto, 0), c.id)
                  AND LTRIM(RTRIM(ISNULL(rel.Cuenta, ''))) <> ''
                ORDER BY cli.RAZON_SOCIAL, rel.Cuenta
            ) contactoCuenta
            WHERE
                {SqlPhoneEquivalentPredicate("c.Telefono", "@Telefono", "@TelefonoTail")}
                OR {SqlPhoneEquivalentPredicate("c.Celular", "@Telefono", "@TelefonoTail")}
                OR {SqlPhoneEquivalentPredicate("c.TelefonoPart", "@Telefono", "@TelefonoTail")}
                OR {SqlPhoneEquivalentPredicate("c.CelularPart", "@Telefono", "@TelefonoTail")}
            ORDER BY c.id DESC
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Telefono", normalizedPhone);
        cmd.Parameters.AddWithValue("@TelefonoTail", DbNullable(phoneTail));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return ContactLookupResult.Empty;

        return new ContactLookupResult
        {
            IdContact = rd.GetInt32(0),
            ClientCode = GetString(rd, 1),
            Name = GetString(rd, 2)
        };
    }

    private static async Task<ConversationLookupResult?> FindWhatsAppConversationByPhoneAsync(SqlConnection cn, string phone, int? idContact, CancellationToken ct)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        var phoneTail = GetPhoneComparableTail(normalizedPhone);
        var sql = $"""
            SELECT TOP (1)
                IdConversacion,
                ISNULL(NombreVisible, ''),
                IdContacto
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'WHATSAPP'
              AND (
                    {SqlPhoneEquivalentPredicate("TelefonoWhatsApp", "@TelefonoWhatsApp", "@TelefonoWhatsAppTail")}
                    OR (@IdContacto IS NOT NULL AND IdContacto = @IdContacto)
                  )
            ORDER BY IdConversacion ASC
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", normalizedPhone);
        cmd.Parameters.AddWithValue("@TelefonoWhatsAppTail", DbNullable(phoneTail));
        cmd.Parameters.AddWithValue("@IdContacto", idContact.HasValue ? idContact.Value : DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return null;

        return new ConversationLookupResult
        {
            IdConversacion = rd.GetInt64(0),
            NombreVisible = GetString(rd, 1),
            IdContacto = rd.IsDBNull(2) ? null : rd.GetInt32(2)
        };
    }

    private static async Task AssociateContactIfMissingAsync(SqlConnection cn, long idConversacion, ContactLookupResult contact, CancellationToken ct)
    {
        if (!contact.IdContact.HasValue)
            return;

        const string sql = """
            UPDATE dbo.CONV_CONVERSACIONES
            SET
                IdContacto = CASE WHEN IdContacto IS NULL THEN @IdContacto ELSE IdContacto END,
                ClienteCodigo = CASE
                    WHEN ClienteCodigo IS NULL OR LTRIM(RTRIM(ClienteCodigo)) = '' THEN @ClienteCodigo
                    ELSE ClienteCodigo
                END,
                NombreVisible = CASE
                    WHEN (NombreVisible IS NULL OR LTRIM(RTRIM(NombreVisible)) = '' OR NombreVisible = TelefonoWhatsApp)
                         AND @NombreVisible IS NOT NULL THEN @NombreVisible
                    ELSE NombreVisible
                END
            WHERE IdConversacion = @IdConversacion
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@IdContacto", contact.IdContact.Value);
        cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(contact.ClientCode));
        cmd.Parameters.AddWithValue("@NombreVisible", DbNullable(contact.Name));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task LinkUnassociatedWhatsAppConversationsByPhoneAsync(SqlConnection cn, CancellationToken ct, long? idConversacion = null)
    {
        const string sql = """
            SELECT TOP (200)
                IdConversacion,
                ISNULL(TelefonoWhatsApp, '')
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'WHATSAPP'
              AND IdContacto IS NULL
              AND TelefonoWhatsApp IS NOT NULL
              AND LTRIM(RTRIM(TelefonoWhatsApp)) <> ''
              AND (@IdConversacion IS NULL OR IdConversacion = @IdConversacion)
            ORDER BY FechaHoraUltimoMensaje DESC, IdConversacion DESC
            """;

        var pending = new List<(long IdConversacion, string TelefonoWhatsApp)>();
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion.HasValue ? idConversacion.Value : DBNull.Value);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                pending.Add((rd.GetInt64(0), GetString(rd, 1)));
        }

        var linked = 0;
        foreach (var item in pending)
        {
            var contact = await TryFindContactByPhoneAsync(cn, item.TelefonoWhatsApp, ct);
            if (!contact.IdContact.HasValue)
                continue;

            await AssociateContactIfMissingAsync(cn, item.IdConversacion, contact, ct);
            linked++;
        }

        if (linked > 0)
        {
            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "LinkUnassociatedWhatsAppConversationsByPhone",
                "CONV_CONVERSACIONES",
                idConversacion?.ToString(CultureInfo.InvariantCulture) ?? "AUTO",
                "Conversaciones vinculadas automáticamente a contactos por teléfono equivalente.",
                new { ConversacionesVinculadas = linked, IdConversacion = idConversacion },
                ct);
        }
    }

    private async Task MergeDuplicateWhatsAppConversationsAsync(SqlConnection cn, long canonicalId, string phone, int? idContact, CancellationToken ct)
    {
        var duplicates = await FindDuplicateWhatsAppConversationIdsAsync(cn, canonicalId, phone, idContact, ct);
        foreach (var duplicateId in duplicates)
        {
            await MergeDuplicateWhatsAppConversationAsync(cn, canonicalId, duplicateId, ct);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "MergeDuplicateWhatsAppConversation",
                "CONV_CONVERSACIONES",
                canonicalId.ToString(CultureInfo.InvariantCulture),
                "Conversación duplicada consolidada por teléfono equivalente.",
                new { ConversacionConservada = canonicalId, ConversacionDuplicada = duplicateId, TelefonoWhatsApp = NormalizePhone(phone), IdContacto = idContact },
                ct);
        }
    }

    private async Task ConsolidateExistingDuplicateWhatsAppConversationsAsync(SqlConnection cn, CancellationToken ct)
    {
        var records = await GetWhatsAppConversationMergeCandidatesAsync(cn, ct);
        var merged = new HashSet<long>();

        foreach (var group in BuildDuplicateGroups(records))
        {
            var activeGroup = group.Where(x => !merged.Contains(x.IdConversacion)).OrderBy(x => x.IdConversacion).ToList();
            if (activeGroup.Count < 2)
                continue;

            var canonical = activeGroup[0];
            foreach (var duplicate in activeGroup.Skip(1))
            {
                await MergeDuplicateWhatsAppConversationAsync(cn, canonical.IdConversacion, duplicate.IdConversacion, ct);
                merged.Add(duplicate.IdConversacion);

                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "ConsolidateExistingDuplicateWhatsAppConversations",
                    "CONV_CONVERSACIONES",
                    canonical.IdConversacion.ToString(CultureInfo.InvariantCulture),
                    "Conversación duplicada existente consolidada al cargar inbox.",
                    new { ConversacionConservada = canonical.IdConversacion, ConversacionDuplicada = duplicate.IdConversacion, canonical.TelefonoWhatsApp, canonical.IdContacto },
                    ct);
            }
        }
    }

    private static async Task<List<ConversationMergeCandidate>> GetWhatsAppConversationMergeCandidatesAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT
                IdConversacion,
                ISNULL(TelefonoWhatsApp, ''),
                IdContacto
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'WHATSAPP'
            ORDER BY IdConversacion ASC
            """;

        var result = new List<ConversationMergeCandidate>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            result.Add(new ConversationMergeCandidate
            {
                IdConversacion = rd.GetInt64(0),
                TelefonoWhatsApp = GetString(rd, 1),
                IdContacto = rd.IsDBNull(2) ? null : rd.GetInt32(2)
            });
        }

        return result;
    }

    private static IEnumerable<IReadOnlyList<ConversationMergeCandidate>> BuildDuplicateGroups(IReadOnlyList<ConversationMergeCandidate> records)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in records
                     .Select(x => new { Item = x, Key = GetPhoneComparableTail(x.TelefonoWhatsApp) })
                     .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                     .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.Select(x => x.Item).OrderBy(x => x.IdConversacion).ToList();
            if (items.Count > 1 && emitted.Add($"TEL:{group.Key}"))
                yield return items;
        }

        foreach (var group in records
                     .Where(x => x.IdContacto.HasValue)
                     .GroupBy(x => x.IdContacto!.Value))
        {
            var items = group.OrderBy(x => x.IdConversacion).ToList();
            if (items.Count > 1 && emitted.Add($"CONTACTO:{group.Key.ToString(CultureInfo.InvariantCulture)}"))
                yield return items;
        }
    }

    private static async Task<List<long>> FindDuplicateWhatsAppConversationIdsAsync(SqlConnection cn, long canonicalId, string phone, int? idContact, CancellationToken ct)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone) && !idContact.HasValue)
            return [];

        var phoneTail = GetPhoneComparableTail(normalizedPhone);
        var sql = $"""
            SELECT IdConversacion
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'WHATSAPP'
              AND IdConversacion <> @CanonicalId
              AND (
                    {SqlPhoneEquivalentPredicate("TelefonoWhatsApp", "@TelefonoWhatsApp", "@TelefonoWhatsAppTail")}
                    OR (@IdContacto IS NOT NULL AND IdContacto = @IdContacto)
                  )
            ORDER BY IdConversacion ASC
            """;

        var result = new List<long>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CanonicalId", canonicalId);
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", DbNullable(normalizedPhone));
        cmd.Parameters.AddWithValue("@TelefonoWhatsAppTail", DbNullable(phoneTail));
        cmd.Parameters.AddWithValue("@IdContacto", idContact.HasValue ? idContact.Value : DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result.Add(rd.GetInt64(0));

        return result;
    }

    private static async Task MergeDuplicateWhatsAppConversationAsync(SqlConnection cn, long canonicalId, long duplicateId, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.CONV_CONVERSACION_ETIQUETAS (IdConversacion, IdEtiqueta, FechaHora_Grabacion)
            SELECT @CanonicalId, dup.IdEtiqueta, MIN(dup.FechaHora_Grabacion)
            FROM dbo.CONV_CONVERSACION_ETIQUETAS dup
            WHERE dup.IdConversacion = @DuplicateId
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.CONV_CONVERSACION_ETIQUETAS cur
                  WHERE cur.IdConversacion = @CanonicalId
                    AND cur.IdEtiqueta = dup.IdEtiqueta
              )
            GROUP BY dup.IdEtiqueta;

            DELETE FROM dbo.CONV_CONVERSACION_ETIQUETAS
            WHERE IdConversacion = @DuplicateId;

            UPDATE dbo.CONV_MENSAJES
            SET IdConversacion = @CanonicalId
            WHERE IdConversacion = @DuplicateId;

            UPDATE dbo.CONV_ASIGNACIONES
            SET IdConversacion = @CanonicalId
            WHERE IdConversacion = @DuplicateId;

            UPDATE c
            SET
                TelefonoWhatsApp = COALESCE(NULLIF(LTRIM(RTRIM(c.TelefonoWhatsApp)), ''), d.TelefonoWhatsApp),
                NombreVisible = COALESCE(NULLIF(LTRIM(RTRIM(c.NombreVisible)), ''), d.NombreVisible),
                ClienteCodigo = COALESCE(NULLIF(LTRIM(RTRIM(c.ClienteCodigo)), ''), d.ClienteCodigo),
                ClienteSucursal = COALESCE(c.ClienteSucursal, d.ClienteSucursal),
                IdContacto = COALESCE(c.IdContacto, d.IdContacto),
                CodigoEstado = CASE WHEN d.FechaHoraUltimoMensaje > c.FechaHoraUltimoMensaje THEN d.CodigoEstado ELSE c.CodigoEstado END,
                IdTecnico = COALESCE(NULLIF(LTRIM(RTRIM(c.IdTecnico)), ''), d.IdTecnico),
                ResumenUltimoMensaje = CASE
                    WHEN d.FechaHoraUltimoMensaje > c.FechaHoraUltimoMensaje THEN d.ResumenUltimoMensaje
                    ELSE c.ResumenUltimoMensaje
                END,
                Prioridad = COALESCE(c.Prioridad, d.Prioridad),
                Bloqueada = CASE WHEN c.Bloqueada = 1 OR d.Bloqueada = 1 THEN 1 ELSE 0 END,
                Archivada = CASE WHEN c.Archivada = 1 AND d.Archivada = 1 THEN 1 ELSE 0 END,
                FechaHoraPrimerMensaje = CASE
                    WHEN c.FechaHoraPrimerMensaje IS NULL THEN d.FechaHoraPrimerMensaje
                    WHEN d.FechaHoraPrimerMensaje IS NULL THEN c.FechaHoraPrimerMensaje
                    WHEN d.FechaHoraPrimerMensaje < c.FechaHoraPrimerMensaje THEN d.FechaHoraPrimerMensaje
                    ELSE c.FechaHoraPrimerMensaje
                END,
                FechaHoraUltimoMensaje = CASE
                    WHEN d.FechaHoraUltimoMensaje > c.FechaHoraUltimoMensaje THEN d.FechaHoraUltimoMensaje
                    ELSE c.FechaHoraUltimoMensaje
                END,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            INNER JOIN dbo.CONV_CONVERSACIONES d
                ON d.IdConversacion = @DuplicateId
            WHERE c.IdConversacion = @CanonicalId;

            DELETE FROM dbo.CONV_CONVERSACIONES
            WHERE IdConversacion = @DuplicateId;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CanonicalId", canonicalId);
        cmd.Parameters.AddWithValue("@DuplicateId", duplicateId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<string> ResolveTechnicianIdAsync(string idTecnico, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) IdTecnico
            FROM dbo.V_TA_Tecnicos
            WHERE LTRIM(RTRIM(IdTecnico)) = @IdTecnico
              AND ISNULL(Baja, 0) = 0
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTecnico", idTecnico.Trim());
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            throw new InvalidOperationException("El técnico indicado no existe o está dado de baja.");
        return Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<string> ResolveTechnicianIdOrNullAsync(string? idTecnico, CancellationToken ct)
    {
        var normalized = NormalizeTechnicianId(idTecnico);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : await ResolveTechnicianIdAsync(normalized, ct);
    }

    private static async Task EnsureFavoriteStickersTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_STICKERS_FAVORITOS', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CONV_STICKERS_FAVORITOS
                (
                    IdFavorito bigint IDENTITY(1,1) NOT NULL,
                    IdAdjunto bigint NOT NULL,
                    Nombre nvarchar(120) NULL,
                    FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_STICKERS_FAVORITOS_FhGrab DEFAULT (GETDATE()),
                    CONSTRAINT PK_CONV_STICKERS_FAVORITOS PRIMARY KEY CLUSTERED (IdFavorito),
                    CONSTRAINT UQ_CONV_STICKERS_FAVORITOS_Adjunto UNIQUE (IdAdjunto),
                    CONSTRAINT FK_CONV_STICKERS_FAVORITOS_ADJUNTO FOREIGN KEY (IdAdjunto)
                        REFERENCES dbo.CONV_ADJUNTOS (IdAdjunto)
                );
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureConversationPinsTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES_PIN_USUARIO', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CONV_CONVERSACIONES_PIN_USUARIO
                (
                    Usuario nvarchar(120) NOT NULL,
                    Sistema nvarchar(50) NOT NULL CONSTRAINT DF_CONV_CONV_PIN_USU_Sistema DEFAULT (N''),
                    IdConversacion bigint NOT NULL,
                    FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CONV_PIN_USU_FhGrab DEFAULT (GETDATE()),
                    CONSTRAINT PK_CONV_CONVERSACIONES_PIN_USUARIO PRIMARY KEY CLUSTERED (Usuario, Sistema, IdConversacion),
                    CONSTRAINT FK_CONV_CONV_PIN_USU_CONVERSACION FOREIGN KEY (IdConversacion)
                        REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion)
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_CONV_CONV_PIN_USU_Orden'
                  AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES_PIN_USUARIO')
            )
            BEGIN
                CREATE NONCLUSTERED INDEX IX_CONV_CONV_PIN_USU_Orden
                    ON dbo.CONV_CONVERSACIONES_PIN_USUARIO (Usuario, Sistema, FechaHora_Grabacion DESC)
                    INCLUDE (IdConversacion);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureConversationReadStateTableAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_CONVERSACIONES_LECTURA_USUARIO', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CONV_CONVERSACIONES_LECTURA_USUARIO
                (
                    Usuario nvarchar(120) NOT NULL,
                    Sistema nvarchar(50) NOT NULL CONSTRAINT DF_CONV_CONV_LECT_USU_Sistema DEFAULT (N''),
                    IdConversacion bigint NOT NULL,
                    IdUltimoMensajeLeido bigint NOT NULL CONSTRAINT DF_CONV_CONV_LECT_USU_UltMsg DEFAULT (0),
                    FechaHora_Grabacion datetime NOT NULL CONSTRAINT DF_CONV_CONV_LECT_USU_FhGrab DEFAULT (GETDATE()),
                    FechaHora_Modificacion datetime NOT NULL CONSTRAINT DF_CONV_CONV_LECT_USU_FhMod DEFAULT (GETDATE()),
                    CONSTRAINT PK_CONV_CONVERSACIONES_LECTURA_USUARIO PRIMARY KEY CLUSTERED (Usuario, Sistema, IdConversacion),
                    CONSTRAINT FK_CONV_CONV_LECT_USU_CONVERSACION FOREIGN KEY (IdConversacion)
                        REFERENCES dbo.CONV_CONVERSACIONES (IdConversacion)
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_CONV_CONV_LECT_USU_Conversacion'
                  AND object_id = OBJECT_ID(N'dbo.CONV_CONVERSACIONES_LECTURA_USUARIO')
            )
            BEGIN
                CREATE NONCLUSTERED INDEX IX_CONV_CONV_LECT_USU_Conversacion
                    ON dbo.CONV_CONVERSACIONES_LECTURA_USUARIO (IdConversacion, Usuario)
                    INCLUDE (Sistema, IdUltimoMensajeLeido, FechaHora_Modificacion);
            END;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureConversationReadBaselinesAsync(SqlConnection cn, string usuario, string sistema, string? clienteCodigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return;

        const string sql = """
            INSERT INTO dbo.CONV_CONVERSACIONES_LECTURA_USUARIO
            (
                Usuario,
                Sistema,
                IdConversacion,
                IdUltimoMensajeLeido,
                FechaHora_Grabacion,
                FechaHora_Modificacion
            )
            SELECT
                @Usuario,
                @Sistema,
                c.IdConversacion,
                ISNULL(ult.IdUltimoMensajeCliente, 0),
                GETDATE(),
                GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            OUTER APPLY (
                SELECT MAX(m.IdMensaje) AS IdUltimoMensajeCliente
                FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = c.IdConversacion
                  AND m.Direction = N'ENTRANTE'
            ) ult
            WHERE (@ClienteCodigo IS NULL OR UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = @ClienteCodigo)
              AND NOT EXISTS (
                SELECT 1
                FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO r
                WHERE r.Usuario = @Usuario
                  AND r.IdConversacion = c.IdConversacion
            );
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Sistema", sistema);
        cmd.Parameters.AddWithValue("@ClienteCodigo", DbNullable(NormalizeClientCode(clienteCodigo)));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<ConversacionAdjuntoServeDto> GetFavoriteStickerFileAsync(long idFavorito, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(a.RutaLocal, ''),
                ISNULL(a.MimeType, 'image/webp'),
                ISNULL(a.NombreArchivo, 'sticker.webp')
            FROM dbo.CONV_STICKERS_FAVORITOS f
            INNER JOIN dbo.CONV_ADJUNTOS a ON a.IdAdjunto = f.IdAdjunto
            WHERE f.IdFavorito = @IdFavorito
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await EnsureFavoriteStickersTableAsync(cn, ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdFavorito", idFavorito);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            throw new InvalidOperationException("El sticker favorito indicado no existe.");

        var item = new ConversacionAdjuntoServeDto
        {
            RutaLocal = GetString(rd, 0),
            MimeType = GetString(rd, 1),
            NombreArchivo = GetString(rd, 2)
        };

        if (string.IsNullOrWhiteSpace(item.RutaLocal) || !File.Exists(item.RutaLocal))
            throw new InvalidOperationException("El archivo local del sticker favorito no está disponible.");

        return item;
    }

    private async Task<bool> GetStateClosedFlagAsync(string codigoEstado, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(EsCerrado, 0)
            FROM dbo.CONV_ESTADOS
            WHERE CodigoEstado = @CodigoEstado
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CodigoEstado", codigoEstado);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            throw new InvalidOperationException("El estado indicado no existe.");

        return Convert.ToBoolean(result, CultureInfo.InvariantCulture);
    }

    private async Task<bool> GetConversationClosedFlagAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(e.EsCerrado, 0)
            FROM dbo.CONV_CONVERSACIONES c
            LEFT JOIN dbo.CONV_ESTADOS e
                ON e.CodigoEstado = c.CodigoEstado
            WHERE c.IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result is not DBNull && Convert.ToBoolean(result, CultureInfo.InvariantCulture);
    }

    private async Task<string> GetConversationTechnicianIdAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(IdTecnico, '')
            FROM dbo.CONV_CONVERSACIONES
            WHERE IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull
            ? string.Empty
            : Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<ConversationStateSummary> GetConversationStateAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(c.CodigoEstado, ''),
                ISNULL(e.Descripcion, '')
            FROM dbo.CONV_CONVERSACIONES c
            LEFT JOIN dbo.CONV_ESTADOS e
                ON e.CodigoEstado = c.CodigoEstado
            WHERE c.IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct)
            ? new ConversationStateSummary(GetString(rd, 0), GetString(rd, 1))
            : new ConversationStateSummary(string.Empty, string.Empty);
    }

    private async Task<string> TryGetStateNameAsync(string codigoEstado, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigoEstado))
            return string.Empty;

        const string sql = """
            SELECT TOP (1) ISNULL(Descripcion, '')
            FROM dbo.CONV_ESTADOS
            WHERE CodigoEstado = @CodigoEstado
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CodigoEstado", codigoEstado.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull
            ? string.Empty
            : Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<string> BuildActionActorNameAsync(string? idTecnico, string? nombreTecnico, string? usuario, CancellationToken ct)
    {
        var technicianId = await ResolveTechnicianIdOrNullAsync(idTecnico, ct);
        return FirstNonEmpty(nombreTecnico, await TryGetTechnicianNameAsync(technicianId, ct), usuario, "Un t\u00e9cnico");
    }

    private async Task<string> TryGetTechnicianNameAsync(string? idTecnico, CancellationToken ct)
    {
        var normalized = NormalizeTechnicianId(idTecnico);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        const string sql = """
            SELECT TOP (1) ISNULL(Nombre, '')
            FROM dbo.V_TA_Tecnicos
            WHERE LTRIM(RTRIM(IdTecnico)) = @IdTecnico
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdTecnico", normalized);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull
            ? string.Empty
            : Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static List<IncomingWhatsAppMessage> ParseIncomingMessages(JsonElement root)
    {
        var items = new List<IncomingWhatsAppMessage>();

        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value))
                    continue;

                string contactName = string.Empty;
                if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contact in contacts.EnumerateArray())
                    {
                        if (contact.TryGetProperty("profile", out var profile) &&
                            profile.TryGetProperty("name", out var nameProp))
                        {
                            contactName = nameProp.GetString() ?? string.Empty;
                            break;
                        }
                    }
                }

                if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var message in messages.EnumerateArray())
                {
                    var phone = message.TryGetProperty("from", out var fromProp) ? fromProp.GetString() ?? string.Empty : string.Empty;
                    var type = message.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "unknown" : "unknown";
                    var messageId = message.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    var replyToMessageId = ExtractIncomingReplyToMessageId(message, type);
                    var timestamp = message.TryGetProperty("timestamp", out var tsProp) ? ParseUnixTimestamp(tsProp.GetString()) : BusinessNow();
                    var text = ExtractIncomingText(message, type);
                    var attachments = ExtractIncomingAttachments(message, type);

                    items.Add(new IncomingWhatsAppMessage
                    {
                        Phone = NormalizePhone(phone),
                        ContactName = contactName,
                        MessageType = NormalizeMessageType(type),
                        WhatsAppMessageId = messageId,
                        WhatsAppReplyToMessageId = replyToMessageId,
                        Timestamp = timestamp,
                        Text = text,
                        RawJson = message.GetRawText(),
                        Attachments = attachments
                    });
                }
            }
        }

        return items;
    }

    private static List<IncomingWhatsAppStatus> ParseIncomingStatuses(JsonElement root)
    {
        var items = new List<IncomingWhatsAppStatus>();

        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value))
                    continue;

                if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var status in statuses.EnumerateArray())
                {
                    var messageId = status.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    var rawStatus = status.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(messageId))
                        continue;

                    items.Add(new IncomingWhatsAppStatus
                    {
                        WhatsAppMessageId = messageId,
                        EstadoEnvio = NormalizeWhatsAppDeliveryStatus(rawStatus, status),
                        RawJson = status.GetRawText()
                    });
                }
            }
        }

        return items;
    }

    private static string NormalizeWhatsAppDeliveryStatus(string status, JsonElement payload)
    {
        if (payload.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
            return "ERROR_ENVIO";

        return status.Trim().ToLowerInvariant() switch
        {
            "sent" => "ENVIADO_META",
            "delivered" => "ENTREGADO",
            "read" => "LEIDO",
            "failed" => "ERROR_ENVIO",
            _ => "ENVIADO_META"
        };
    }

    private static List<IncomingWhatsAppAttachment> ExtractIncomingAttachments(JsonElement message, string type)
    {
        var normalizedType = NormalizeMessageType(type);
        if (normalizedType is not ("IMAGE" or "AUDIO" or "STICKER" or "DOCUMENT" or "VIDEO"))
            return [];

        if (!TryGetMediaPayload(message, type, out var media))
            return [];

        var mediaId = media.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(mediaId))
            return [];

        return
        [
            new IncomingWhatsAppAttachment
            {
                MediaId = mediaId,
                TipoArchivo = normalizedType,
                MimeType = media.TryGetProperty("mime_type", out var mimeProp) ? mimeProp.GetString() ?? string.Empty : string.Empty,
                FileName = media.TryGetProperty("filename", out var fileNameProp) ? fileNameProp.GetString() ?? string.Empty : string.Empty
            }
        ];
    }

    private static bool TryGetMediaPayload(JsonElement message, string type, out JsonElement media)
    {
        media = default;
        var propertyName = string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
        return propertyName.Length > 0 && message.TryGetProperty(propertyName, out media) && media.ValueKind == JsonValueKind.Object;
    }

    private static string ExtractIncomingText(JsonElement message, string type)
    {
        if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
            message.TryGetProperty("text", out var text) &&
            text.TryGetProperty("body", out var body))
            return body.GetString() ?? string.Empty;

        if (string.Equals(type, "location", StringComparison.OrdinalIgnoreCase))
            return ExtractLocationText(message);

        if (string.Equals(type, "contacts", StringComparison.OrdinalIgnoreCase))
            return ExtractContactsText(message);

        if (string.Equals(type, "button", StringComparison.OrdinalIgnoreCase))
            return ExtractButtonText(message);

        if (string.Equals(type, "interactive", StringComparison.OrdinalIgnoreCase))
            return ExtractInteractiveText(message);

        if (string.Equals(type, "reaction", StringComparison.OrdinalIgnoreCase))
            return ExtractReactionText(message);

        if (string.Equals(type, "order", StringComparison.OrdinalIgnoreCase))
            return ExtractOrderText(message);

        if (string.Equals(type, "system", StringComparison.OrdinalIgnoreCase))
            return ExtractSystemText(message);

        if (string.Equals(type, "unsupported", StringComparison.OrdinalIgnoreCase))
            return ExtractUnsupportedText(message);

        if (TryGetMediaPayload(message, type, out var media))
        {
            if (media.TryGetProperty("caption", out var caption))
            {
                var captionText = caption.GetString();
                if (!string.IsNullOrWhiteSpace(captionText))
                    return captionText;
            }

            if (media.TryGetProperty("filename", out var fileName))
            {
                var name = fileName.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    return $"[{type}] {name}";
            }
        }

        return $"[{type}]";
    }

    private static string ExtractIncomingReplyToMessageId(JsonElement message, string type)
    {
        if (string.Equals(type, "reaction", StringComparison.OrdinalIgnoreCase) &&
            message.TryGetProperty("reaction", out var reaction) &&
            reaction.TryGetProperty("message_id", out var reactionMessageId))
            return reactionMessageId.GetString() ?? string.Empty;

        if (message.TryGetProperty("context", out var context) &&
            context.TryGetProperty("id", out var contextId))
            return contextId.GetString() ?? string.Empty;

        return string.Empty;
    }

    private static string ExtractLocationText(JsonElement message)
    {
        if (!message.TryGetProperty("location", out var location))
            return "Ubicación compartida.";

        var name = location.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var address = location.TryGetProperty("address", out var addressProp) ? addressProp.GetString() ?? string.Empty : string.Empty;
        var latitude = location.TryGetProperty("latitude", out var latProp) ? latProp.ToString() : string.Empty;
        var longitude = location.TryGetProperty("longitude", out var lonProp) ? lonProp.ToString() : string.Empty;
        var coordinates = string.IsNullOrWhiteSpace(latitude) || string.IsNullOrWhiteSpace(longitude)
            ? string.Empty
            : $"{latitude}, {longitude}";

        return FirstNonEmpty(
            JoinText("Ubicación compartida", name),
            JoinText("Ubicación compartida", address),
            JoinText("Ubicación compartida", coordinates),
            "Ubicación compartida.");
    }

    private static string ExtractContactsText(JsonElement message)
    {
        if (!message.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return "Contacto compartido.";

        var items = new List<string>();
        foreach (var contact in contacts.EnumerateArray())
        {
            var name = ExtractContactName(contact);
            var phone = ExtractContactPhone(contact);
            var label = string.IsNullOrWhiteSpace(phone)
                ? name
                : $"{FirstNonEmpty(name, "Sin nombre")} - {phone}";
            if (!string.IsNullOrWhiteSpace(label))
                items.Add(label);
        }

        return items.Count == 0
            ? "Contacto compartido."
            : $"Contacto compartido: {string.Join(", ", items)}";
    }

    private static string ExtractContactName(JsonElement contact)
    {
        if (!contact.TryGetProperty("name", out var name))
            return string.Empty;

        return FirstNonEmpty(
            TryGetString(name, "formatted_name"),
            TryGetString(name, "first_name"),
            TryGetString(name, "last_name"));
    }

    private static string ExtractContactPhone(JsonElement contact)
    {
        if (!contact.TryGetProperty("phones", out var phones) || phones.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var phone in phones.EnumerateArray())
        {
            var value = FirstNonEmpty(TryGetString(phone, "phone"), TryGetString(phone, "wa_id"));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static string TryGetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string ExtractButtonText(JsonElement message)
    {
        if (!message.TryGetProperty("button", out var button))
            return "Botón seleccionado.";

        var text = button.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
        var payload = button.TryGetProperty("payload", out var payloadProp) ? payloadProp.GetString() ?? string.Empty : string.Empty;
        return JoinText("Botón seleccionado", FirstNonEmpty(text, payload));
    }

    private static string ExtractInteractiveText(JsonElement message)
    {
        if (!message.TryGetProperty("interactive", out var interactive))
            return "Respuesta interactiva recibida.";

        if (interactive.TryGetProperty("button_reply", out var buttonReply))
            return JoinText("Respuesta interactiva", ExtractReplyTitle(buttonReply));

        if (interactive.TryGetProperty("list_reply", out var listReply))
            return JoinText("Respuesta de lista", ExtractReplyTitle(listReply));

        if (interactive.TryGetProperty("nfm_reply", out var flowReply))
            return JoinText("Respuesta de formulario", ExtractReplyTitle(flowReply));

        var type = interactive.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;
        return JoinText("Respuesta interactiva recibida", type);
    }

    private static string ExtractReactionText(JsonElement message)
    {
        if (!message.TryGetProperty("reaction", out var reaction))
            return "Reacción recibida.";

        var emoji = reaction.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? string.Empty : string.Empty;
        var messageId = reaction.TryGetProperty("message_id", out var messageIdProp) ? messageIdProp.GetString() ?? string.Empty : string.Empty;
        var detail = FirstNonEmpty(emoji, messageId);
        return string.IsNullOrWhiteSpace(detail)
            ? "Reacción recibida."
            : $"Reacción recibida: {detail}";
    }

    private static string ExtractOrderText(JsonElement message)
    {
        if (!message.TryGetProperty("order", out var order))
            return "Pedido de catálogo recibido.";

        var catalog = order.TryGetProperty("catalog_id", out var catalogProp) ? catalogProp.GetString() ?? string.Empty : string.Empty;
        var count = 0;
        if (order.TryGetProperty("product_items", out var items) && items.ValueKind == JsonValueKind.Array)
            count = items.GetArrayLength();

        var detail = count > 0 ? $"{count} producto(s)" : catalog;
        return JoinText("Pedido de catálogo recibido", detail);
    }

    private static string ExtractSystemText(JsonElement message)
    {
        if (!message.TryGetProperty("system", out var system))
            return "Evento de sistema de WhatsApp.";

        var body = system.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
        var type = system.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;
        return JoinText("Evento de sistema de WhatsApp", FirstNonEmpty(body, type));
    }

    private static string ExtractUnsupportedText(JsonElement message)
    {
        var unsupportedType = string.Empty;
        if (message.TryGetProperty("unsupported", out var unsupported) &&
            unsupported.TryGetProperty("type", out var unsupportedTypeProp))
        {
            unsupportedType = unsupportedTypeProp.GetString() ?? string.Empty;
        }

        var reason = ExtractFirstErrorDetail(message);
        return JoinText("Mensaje no compatible recibido", FirstNonEmpty(unsupportedType, reason));
    }

    private static string ExtractReplyTitle(JsonElement reply)
    {
        var title = reply.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
        var body = reply.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
        var name = reply.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var id = reply.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        return FirstNonEmpty(title, body, name, id);
    }

    private static string ExtractFirstErrorDetail(JsonElement message)
    {
        if (!message.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var error in errors.EnumerateArray())
        {
            var details = string.Empty;
            if (error.TryGetProperty("error_data", out var errorData) &&
                errorData.TryGetProperty("details", out var detailsProp))
                details = detailsProp.GetString() ?? string.Empty;

            var errorMessage = error.TryGetProperty("message", out var messageProp) ? messageProp.GetString() ?? string.Empty : string.Empty;
            var title = error.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;
            var result = FirstNonEmpty(details, errorMessage, title);
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        return string.Empty;
    }

    private static string JoinText(string label, string detail)
        => string.IsNullOrWhiteSpace(detail) ? $"{label}." : $"{label}: {detail}";

    private async Task<string> SaveIncomingAttachmentAsync(long conversationId, string fileName, byte[] bytes, CancellationToken ct)
    {
        var folder = Path.Combine(UploadsBasePath, conversationId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);

        var rutaLocal = Path.Combine(folder, $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        await File.WriteAllBytesAsync(rutaLocal, bytes, ct);
        return rutaLocal;
    }

    private static string BuildIncomingFileName(IncomingWhatsAppAttachment attachment, string mimeType)
    {
        var baseName = string.IsNullOrWhiteSpace(attachment.FileName)
            ? $"{attachment.TipoArchivo.ToLowerInvariant()}_{DateTime.Now:yyyyMMdd_HHmmss}"
            : Path.GetFileName(attachment.FileName);

        if (Path.HasExtension(baseName))
            return baseName;

        return $"{baseName}{InferExtension(mimeType, attachment.TipoArchivo)}";
    }

    private static string InferMimeFromType(string tipoArchivo)
        => NormalizeMessageType(tipoArchivo) switch
        {
            "IMAGE" => "image/jpeg",
            "STICKER" => "image/webp",
            "AUDIO" => "audio/ogg",
            "VIDEO" => "video/mp4",
            _ => "application/octet-stream"
        };

    private static string InferExtension(string mimeType, string tipoArchivo)
    {
        var normalized = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "audio/ogg" or "audio/ogg; codecs=opus" => ".ogg",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/webm" => ".webm",
            "video/mp4" => ".mp4",
            "application/pdf" => ".pdf",
            _ => NormalizeMessageType(tipoArchivo) == "STICKER" ? ".webp" : ".bin"
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static bool ShouldHydrateIncomingMedia(ConversacionMensajeDto message, string payloadJson)
        => !message.TieneAdjuntos
           && string.Equals(message.Direction, "ENTRANTE", StringComparison.OrdinalIgnoreCase)
           && NormalizeMessageType(message.MessageType) is "IMAGE" or "AUDIO" or "STICKER" or "DOCUMENT" or "VIDEO"
           && !string.IsNullOrWhiteSpace(payloadJson)
           && !MediaHydrationAttempts.ContainsKey(message.IdMensaje);

    private static string ExtractSentMessageId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (message.TryGetProperty("id", out var id))
                        return id.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Leave empty when the response is not parseable.
        }

        return string.Empty;
    }

    private static string RequireSentMessageId(string responseBody, string operation)
    {
        var messageId = ExtractSentMessageId(responseBody);
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId;

        throw new InvalidOperationException($"Meta acept\u00f3 la solicitud de {operation}, pero no devolvi\u00f3 id de mensaje. Respuesta: {responseBody}");
    }

    private static string BuildDeliveryErrorPayload(Exception ex)
        => JsonSerializer.Serialize(new
        {
            Error = ex.GetBaseException().Message,
            Type = ex.GetBaseException().GetType().FullName,
            FechaHora = BusinessNow()
        });

    private static bool ShouldConvertWebmOpusForWhatsApp(string path, string mimeType, string nombreArchivo)
        => LooksLikeWebm(path)
           || mimeType.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
           || Path.GetExtension(nombreArchivo).Equals(".webm", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWebm(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        return fs.Read(header) == 4
               && header[0] == 0x1A
               && header[1] == 0x45
               && header[2] == 0xDF
               && header[3] == 0xA3;
    }

    private static byte[] ConvertWebmOpusFileToOgg(string path)
    {
        var webm = File.ReadAllBytes(path);
        var packets = ExtractWebmOpusPackets(webm);
        if (packets.Count == 0)
            throw new InvalidOperationException("No se pudo convertir el audio WebM a OGG para WhatsApp.");

        var opusHead = ExtractWebmCodecPrivate(webm);
        if (opusHead.Length == 0 || !Encoding.ASCII.GetString(opusHead, 0, Math.Min(opusHead.Length, 8)).Equals("OpusHead", StringComparison.Ordinal))
            opusHead = [.. Encoding.ASCII.GetBytes("OpusHead"), 1, 1, 0, 0, 0x80, 0xbb, 0, 0, 0, 0, 0, 0];

        using var output = new MemoryStream();
        var serial = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
        var sequence = 0;
        WriteOggPacket(output, opusHead, 0, serial, ref sequence, 2);
        WriteOggPacket(output, Encoding.ASCII.GetBytes("OpusTags\0\0\0\0\0\0\0\0"), 0, serial, ref sequence, 0);

        long granule = 0;
        foreach (var packet in packets)
        {
            granule += GetOpusPacketSampleCount(packet);
            WriteOggPacket(output, packet, granule, serial, ref sequence, 0);
        }

        return output.ToArray();
    }

    private static List<byte[]> ExtractWebmOpusPackets(byte[] webm)
    {
        var packets = new List<byte[]>();
        ScanEbmlElements(webm, 0, webm.Length, (id, payloadStart, payloadSize) =>
        {
            if (id is 0xA3 or 0xA1)
            {
                var packet = ExtractWebmBlockPayload(webm.AsSpan(payloadStart, payloadSize).ToArray());
                if (packet.Length > 0)
                    packets.Add(packet);
            }
        });

        return packets;
    }

    private static byte[] ExtractWebmCodecPrivate(byte[] webm)
    {
        byte[] result = [];
        ScanEbmlElements(webm, 0, webm.Length, (id, payloadStart, payloadSize) =>
        {
            if (id == 0x63A2 && result.Length == 0)
                result = webm.AsSpan(payloadStart, payloadSize).ToArray();
        });

        return result;
    }

    private static void ScanEbmlElements(byte[] data, int start, int end, Action<ulong, int, int> visit)
    {
        var offset = start;
        while (offset < end)
        {
            if (!TryReadEbmlId(data, offset, end, out var id, out var idLength))
                break;

            offset += idLength;
            if (!TryReadEbmlSize(data, offset, end, out var size, out var sizeLength))
                break;

            offset += sizeLength;
            if (size < 0 || size > end - offset)
                break;

            var payloadStart = offset;
            var payloadSize = (int)size;
            visit(id, payloadStart, payloadSize);

            if (IsEbmlContainer(id))
                ScanEbmlElements(data, payloadStart, payloadStart + payloadSize, visit);

            offset = payloadStart + payloadSize;
        }
    }

    private static bool TryReadEbmlId(byte[] data, int offset, int end, out ulong value, out int length)
    {
        value = 0;
        length = 0;
        if (offset >= end)
            return false;

        var first = data[offset];
        var mask = 0x80;
        while (length < 4 && (first & mask) == 0)
        {
            mask >>= 1;
            length++;
        }

        length++;
        if (length is < 1 or > 4 || offset + length > end)
            return false;

        for (var i = 0; i < length; i++)
            value = (value << 8) | data[offset + i];

        return true;
    }

    private static bool TryReadEbmlSize(byte[] data, int offset, int end, out long value, out int length)
    {
        value = 0;
        length = 0;
        if (offset >= end)
            return false;

        var first = data[offset];
        var mask = 0x80;
        while (length < 8 && (first & mask) == 0)
        {
            mask >>= 1;
            length++;
        }

        length++;
        if (length is < 1 or > 8 || offset + length > end)
            return false;

        value = first & (mask - 1);
        for (var i = 1; i < length; i++)
            value = (value << 8) | data[offset + i];

        return true;
    }

    private static bool IsEbmlContainer(ulong id)
        => id is 0x1A45DFA3 or 0x18538067 or 0x1549A966 or 0x1654AE6B or 0xAE or 0x1F43B675 or 0xA0;

    private static byte[] ExtractWebmBlockPayload(byte[] block)
    {
        if (block.Length < 4)
            return [];

        if (!TryReadEbmlSize(block, 0, block.Length, out _, out var trackLength))
            return [];

        var payloadStart = trackLength + 3;
        if (payloadStart >= block.Length)
            return [];

        var flags = block[trackLength + 2];
        if ((flags & 0x06) != 0)
            return [];

        return block[payloadStart..];
    }

    private static int GetOpusPacketSampleCount(byte[] packet)
    {
        if (packet.Length == 0)
            return 960;

        var toc = packet[0];
        var config = toc >> 3;
        var code = toc & 0x03;
        var frameCount = code switch
        {
            0 => 1,
            1 or 2 => 2,
            3 when packet.Length > 1 => packet[1] & 0x3F,
            _ => 1
        };

        var samplesPerFrame = config switch
        {
            < 12 => (config % 4) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 },
            < 16 => (config % 2) == 0 ? 480 : 960,
            _ => (config % 4) switch { 0 => 120, 1 => 240, 2 => 480, _ => 960 }
        };

        return Math.Max(120, frameCount * samplesPerFrame);
    }

    private static void WriteOggPacket(Stream output, byte[] packet, long granulePosition, uint serial, ref int sequence, byte headerType)
    {
        var segments = BuildOggSegments(packet.Length);
        using var page = new MemoryStream();
        page.Write(Encoding.ASCII.GetBytes("OggS"));
        page.WriteByte(0);
        page.WriteByte(headerType);
        Span<byte> buffer8 = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer8, granulePosition);
        page.Write(buffer8);
        Span<byte> buffer4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer4, serial);
        page.Write(buffer4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer4, sequence++);
        page.Write(buffer4);
        page.Write(new byte[4]);
        page.WriteByte((byte)segments.Count);
        foreach (var segment in segments)
            page.WriteByte((byte)segment);
        page.Write(packet);

        var pageBytes = page.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(22, 4), ComputeOggCrc(pageBytes));
        output.Write(pageBytes);
    }

    private static List<int> BuildOggSegments(int packetLength)
    {
        var segments = new List<int>();
        var remaining = packetLength;
        while (remaining >= 255)
        {
            segments.Add(255);
            remaining -= 255;
        }

        segments.Add(remaining);
        return segments;
    }

    private static uint ComputeOggCrc(byte[] bytes)
    {
        uint crc = 0;
        foreach (var b in bytes)
        {
            crc ^= (uint)b << 24;
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }

        return crc;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static DateTime ParseUnixTimestamp(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            return NormalizeIncomingTimestamp(TimeZoneInfo.ConvertTimeFromUtc(
                DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime,
                ResolveBusinessTimeZone()));

        return BusinessNow();
    }

    private static DateTime NormalizeIncomingTimestamp(DateTime value)
    {
        var businessValue = ToBusinessTime(value);
        var now = BusinessNow();
        return businessValue > now.AddMinutes(10) ? now : businessValue;
    }

    private static DateTime NormalizeStoredConversationTime(DateTime value)
    {
        if (value == DateTime.MinValue)
            return value;

        var businessValue = ToBusinessTime(value);
        var now = BusinessNow();

        return businessValue > now.AddMinutes(10) ? now : businessValue;
    }

    private static DateTime ToBusinessTime(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return TimeZoneInfo.ConvertTimeFromUtc(value, ResolveBusinessTimeZone());

        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static DateTime BusinessNow()
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveBusinessTimeZone());

    private static string ConversationMessageVisibleDateSql(string alias)
        => $"""
           CASE
               WHEN {alias}.Direction = N'ENTRANTE'
                    AND {alias}.FechaHora > DATEADD(minute, 10, {BusinessNowSql})
                   THEN CASE
                       WHEN {alias}.FechaHora_Grabacion IS NOT NULL
                            AND {alias}.FechaHora_Grabacion <= DATEADD(minute, 10, {BusinessNowSql})
                           THEN {alias}.FechaHora_Grabacion
                       ELSE {BusinessNowSql}
                   END
               WHEN {alias}.Direction = N'ENTRANTE'
                    AND {alias}.FechaHora_Grabacion IS NOT NULL
                    AND {alias}.FechaHora > DATEADD(minute, 10, {alias}.FechaHora_Grabacion)
                   THEN {alias}.FechaHora_Grabacion
               ELSE {alias}.FechaHora
           END
           """;

    private const string BusinessNowSql = "CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS datetime)";

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static void ApplyWhatsAppWindow(ConversacionInboxItemDto item)
    {
        if (item.FechaHoraUltimoMensajeCliente is not DateTime lastClientMessage)
        {
            item.VentanaWhatsAppActiva = false;
            item.FechaHoraVencimientoVentanaWhatsApp = null;
            return;
        }

        item.FechaHoraVencimientoVentanaWhatsApp = lastClientMessage.AddHours(24);
        item.VentanaWhatsAppActiva = BusinessNow() <= item.FechaHoraVencimientoVentanaWhatsApp.Value;
    }

    private static void ApplyWhatsAppWindow(ConversacionDetalleDto item)
    {
        if (!string.Equals(item.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase) ||
            item.FechaHoraUltimoMensajeCliente is not DateTime lastClientMessage)
        {
            item.VentanaWhatsAppActiva = false;
            item.FechaHoraVencimientoVentanaWhatsApp = null;
            return;
        }

        item.FechaHoraVencimientoVentanaWhatsApp = lastClientMessage.AddHours(24);
        item.VentanaWhatsAppActiva = BusinessNow() <= item.FechaHoraVencimientoVentanaWhatsApp.Value;
    }

    private static ConversacionPlantillaDto ReadTemplate(SqlDataReader rd)
        => new()
        {
            IdPlantilla = rd.GetInt64(0),
            NombreVisible = GetString(rd, 1),
            NombreMeta = GetString(rd, 2),
            Categoria = GetString(rd, 3),
            Idioma = GetString(rd, 4),
            EncabezadoTexto = GetString(rd, 5),
            CuerpoTexto = GetString(rd, 6),
            PieTexto = GetString(rd, 7),
            EjemplosVariablesJson = GetString(rd, 8),
            EstadoLocal = GetString(rd, 9),
            EstadoMeta = GetString(rd, 10),
            MetaTemplateId = GetString(rd, 11),
            MetaRechazoMotivo = GetString(rd, 12),
            Activa = !rd.IsDBNull(13) && rd.GetBoolean(13),
            FechaHoraGrabacion = rd.IsDBNull(14) ? DateTime.MinValue : rd.GetDateTime(14),
            FechaHoraModificacion = rd.IsDBNull(15) ? null : rd.GetDateTime(15),
            FechaHoraSincronizacion = rd.IsDBNull(16) ? null : rd.GetDateTime(16)
        };

    private static ConversacionPlantillaSaveRequest NormalizeTemplateRequest(ConversacionPlantillaSaveRequest request)
    {
        if (request is null)
            throw new InvalidOperationException("La plantilla es obligatoria.");

        var body = request.CuerpoTexto?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.NombreVisible))
            throw new InvalidOperationException("El nombre de la plantilla es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.NombreMeta))
            throw new InvalidOperationException("El nombre Meta de la plantilla es obligatorio.");
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("El cuerpo de la plantilla es obligatorio.");

        var metaName = NormalizeMetaTemplateName(request.NombreMeta);
        if (string.IsNullOrWhiteSpace(metaName))
            throw new InvalidOperationException("El nombre Meta solo puede contener letras, numeros y guiones bajos.");

        return new ConversacionPlantillaSaveRequest
        {
            IdPlantilla = request.IdPlantilla,
            NombreVisible = request.NombreVisible.Trim(),
            NombreMeta = metaName,
            Categoria = NormalizeTemplateCategory(request.Categoria),
            Idioma = NormalizeTemplateLanguage(request.Idioma),
            EncabezadoTexto = (request.EncabezadoTexto ?? string.Empty).Trim(),
            CuerpoTexto = body,
            PieTexto = (request.PieTexto ?? string.Empty).Trim(),
            EjemplosVariablesJson = NormalizeTemplateExamples(request.EjemplosVariablesJson, CountTemplateVariables(body)),
            Activa = request.Activa,
            UsuarioAccion = request.UsuarioAccion,
            SistemaAccion = request.SistemaAccion
        };
    }

    private static async Task EnsureTemplateNameIsUniqueAsync(SqlConnection cn, ConversacionPlantillaSaveRequest request, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 IdPlantilla
            FROM dbo.CONV_PLANTILLAS
            WHERE NombreMeta = @NombreMeta
              AND Idioma = @Idioma
              AND IdPlantilla <> @IdPlantilla
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@NombreMeta", request.NombreMeta);
        cmd.Parameters.AddWithValue("@Idioma", request.Idioma);
        cmd.Parameters.AddWithValue("@IdPlantilla", request.IdPlantilla);
        var existing = await cmd.ExecuteScalarAsync(ct);
        if (existing is not null && existing != DBNull.Value)
            throw new InvalidOperationException($"Ya existe una plantilla con nombre Meta '{request.NombreMeta}' e idioma '{request.Idioma}'. Abrila desde la lista o usa otro nombre Meta.");
    }

    private static void AddTemplateParameters(SqlCommand cmd, ConversacionPlantillaSaveRequest request)
    {
        cmd.Parameters.AddWithValue("@IdPlantilla", request.IdPlantilla);
        cmd.Parameters.AddWithValue("@NombreVisible", request.NombreVisible);
        cmd.Parameters.AddWithValue("@NombreMeta", request.NombreMeta);
        cmd.Parameters.AddWithValue("@Categoria", request.Categoria);
        cmd.Parameters.AddWithValue("@Idioma", request.Idioma);
        cmd.Parameters.AddWithValue("@EncabezadoTexto", DbNullable(request.EncabezadoTexto));
        cmd.Parameters.AddWithValue("@CuerpoTexto", request.CuerpoTexto);
        cmd.Parameters.AddWithValue("@PieTexto", DbNullable(request.PieTexto));
        cmd.Parameters.AddWithValue("@EjemplosVariablesJson", DbNullable(request.EjemplosVariablesJson));
        cmd.Parameters.AddWithValue("@Activa", request.Activa);
        cmd.Parameters.AddWithValue("@UsuarioAccion", DbNullable(request.UsuarioAccion));
        cmd.Parameters.AddWithValue("@SistemaAccion", DbNullable(request.SistemaAccion));
    }

    private static void ValidateTemplateCanSubmit(ConversacionPlantillaDto template)
    {
        if (!template.Activa)
            throw new InvalidOperationException("No se puede enviar a aprobación una plantilla inactiva.");
        if (string.Equals(template.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La plantilla ya está aprobada por Meta.");

        var variableCount = CountTemplateVariables(template.CuerpoTexto);
        var examples = ParseTemplateExamples(template.EjemplosVariablesJson);
        if (variableCount > 0 && examples.Count < variableCount)
            throw new InvalidOperationException("Las variables de la plantilla necesitan valores de ejemplo para enviarse a aprobación.");
    }

    private static Dictionary<string, object?> BuildMetaTemplateCreatePayload(ConversacionPlantillaDto template)
    {
        var components = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(template.EncabezadoTexto))
        {
            components.Add(new Dictionary<string, object?>
            {
                ["type"] = "HEADER",
                ["format"] = "TEXT",
                ["text"] = template.EncabezadoTexto
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["type"] = "BODY",
            ["text"] = template.CuerpoTexto
        };

        var examples = ParseTemplateExamples(template.EjemplosVariablesJson);
        if (examples.Count > 0)
            body["example"] = new Dictionary<string, object?> { ["body_text"] = new[] { examples } };
        components.Add(body);

        if (!string.IsNullOrWhiteSpace(template.PieTexto))
        {
            components.Add(new Dictionary<string, object?>
            {
                ["type"] = "FOOTER",
                ["text"] = template.PieTexto
            });
        }

        return new Dictionary<string, object?>
        {
            ["name"] = template.NombreMeta,
            ["language"] = template.Idioma,
            ["category"] = NormalizeTemplateCategory(template.Categoria),
            ["components"] = components
        };
    }

    private static string NormalizeMetaTemplateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^a-z0-9_]+", "_");
        normalized = Regex.Replace(normalized, @"_+", "_").Trim('_');
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }

    private static string NormalizeTemplateCategory(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? ConversacionPlantillaCategorias.Marketing : value.Trim().ToUpperInvariant();
        return normalized == ConversacionPlantillaCategorias.Utility || normalized == ConversacionPlantillaCategorias.Marketing
            ? normalized
            : ConversacionPlantillaCategorias.Marketing;
    }

    private static string NormalizeTemplateLanguage(string? value)
        => string.IsNullOrWhiteSpace(value) ? "es_AR" : value.Trim();

    private static int CountTemplateVariables(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var max = 0;
        foreach (Match match in Regex.Matches(text, @"\{\{\s*(\d+)\s*\}\}"))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                max = Math.Max(max, index);
        }

        return max;
    }

    private static string NormalizeTemplateExamples(string? rawExamples, int variableCount)
    {
        var examples = ParseTemplateExamples(rawExamples);
        if (variableCount == 0)
            return string.Empty;

        while (examples.Count < variableCount)
            examples.Add($"Ejemplo {examples.Count + 1}");

        return JsonSerializer.Serialize(examples.Take(variableCount).ToArray());
    }

    private static List<string> ParseTemplateExamples(string? rawExamples)
    {
        if (string.IsNullOrWhiteSpace(rawExamples))
            return [];

        var trimmed = rawExamples.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(trimmed)?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList() ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        return ParseTemplateTextBlocks(trimmed);
    }

    private static List<string> ParseTemplateTextBlocks(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Split('\n').Any(x => string.Equals(x.Trim(), "---", StringComparison.Ordinal)))
        {
            var result = new List<string>();
            var current = new List<string>();

            foreach (var line in normalized.Split('\n'))
            {
                if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                {
                    AddTemplateTextBlock(result, current);
                    current.Clear();
                    continue;
                }

                current.Add(line);
            }

            AddTemplateTextBlock(result, current);
            return result;
        }

        return normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static void AddTemplateTextBlock(List<string> result, List<string> lines)
    {
        var block = string.Join(Environment.NewLine, lines).Trim();
        if (block.Length > 0)
            result.Add(block);
    }

    private static List<string> NormalizeTemplateValues(IEnumerable<string>? values)
        => values?
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .ToList() ?? [];

    private static string RenderTemplatePreview(string text, IReadOnlyList<string> values)
    {
        var result = text ?? string.Empty;
        for (var i = 0; i < values.Count; i++)
            result = Regex.Replace(result, @"\{\{\s*" + (i + 1).ToString(CultureInfo.InvariantCulture) + @"\s*\}\}", values[i]);

        return result;
    }

    private static void ValidateOutgoingRequest(ConversacionSendMessageRequest request)
    {
        if (request.IdConversacion <= 0)
            throw new InvalidOperationException("La conversación es obligatoria.");
        if (string.IsNullOrWhiteSpace(request.Texto))
            throw new InvalidOperationException("El texto del mensaje es obligatorio.");
    }

    private static string NormalizeReactionEmoji(string? emoji)
    {
        var value = (emoji ?? string.Empty).Trim();
        return value is "👍" or "❤️" or "😂" or "😮" or "😢" or "🙏" ? value : string.Empty;
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "todas" : mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sin_asignar" => normalized,
            "asignadas_a_mi" => normalized,
            "pendientes" => normalized,
            "cerradas" => normalized,
            _ => "todas"
        };
    }

    private static string? NormalizeAuditFilter(string? auditoria)
    {
        if (string.IsNullOrWhiteSpace(auditoria))
            return null;

        var normalized = auditoria.Trim().ToLowerInvariant();
        return normalized switch
        {
            "entrantes" => normalized,
            "salientes" => normalized,
            "internos" => normalized,
            "adjuntos" => normalized,
            "activas" => normalized,
            "nuevas" => normalized,
            "cerradas_rango" => normalized,
            "asignaciones" => normalized,
            "reabiertas" => normalized,
            "cambios_estado" => normalized,
            "tipo_mensaje" => normalized,
            "tickets" => normalized,
            _ => null
        };
    }

    private static string NormalizePinUser(string? usuario)
        => (usuario ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizePinSystem(string? sistema)
        => (sistema ?? string.Empty).Trim().ToUpperInvariant();

    private static IReadOnlyList<ConversacionInboxItemDto> DeduplicateInboxItems(IReadOnlyList<ConversacionInboxItemDto> items)
    {
        var result = new List<ConversacionInboxItemDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var key = BuildConversationDedupeKey(item);
            if (!string.IsNullOrWhiteSpace(key) && !seen.Add(key))
                continue;

            result.Add(item);
        }

        return result;
    }

    private static string BuildConversationDedupeKey(ConversacionInboxItemDto item)
    {
        if (item.IdContacto.HasValue)
            return $"CONTACTO:{item.IdContacto.Value.ToString(CultureInfo.InvariantCulture)}";

        var phoneTail = GetPhoneComparableTail(item.TelefonoWhatsApp);
        return string.IsNullOrWhiteSpace(phoneTail) ? string.Empty : $"TEL:{phoneTail}";
    }

    private static List<ConversacionesEstadisticaTemaDto> BuildFrequentTerms(IEnumerable<string> texts)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "para", "como", "pero", "porque", "cuando", "donde", "hola", "buen", "buenas",
            "buenos", "dias", "gracias", "este", "esta", "esto", "tengo", "tiene", "tienen",
            "puede", "puedo", "quiero", "necesito", "favor", "consulta", "consultar", "cliente",
            "mensaje", "whatsapp", "sistema", "plataforma", "nombre", "razon", "social"
        };

        return texts
            .SelectMany(text => Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+"))
            .Select(NormalizeFrequentTerm)
            .Where(x => x.Length >= 4 && !stopWords.Contains(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ConversacionesEstadisticaTemaDto { Texto = g.Key, Cantidad = g.Count() })
            .OrderByDescending(x => x.Cantidad)
            .ThenBy(x => x.Texto, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string NormalizeFrequentTerm(string? value)
    {
        var normalized = RemoveDiacritics(value).Trim().ToLowerInvariant();
        if (normalized.Length <= 4)
            return normalized;

        if (normalized.EndsWith("ciones", StringComparison.Ordinal) && normalized.Length > 8)
            return normalized[..^2];

        if (normalized.EndsWith("ces", StringComparison.Ordinal) && normalized.Length > 5)
            return normalized[..^3] + "z";

        if (normalized.EndsWith("es", StringComparison.Ordinal) && normalized.Length > 5)
        {
            var singular = normalized[..^2];
            if (singular.EndsWith("r", StringComparison.Ordinal)
                || singular.EndsWith("l", StringComparison.Ordinal)
                || singular.EndsWith("n", StringComparison.Ordinal)
                || singular.EndsWith("d", StringComparison.Ordinal)
                || singular.EndsWith("z", StringComparison.Ordinal))
            {
                return singular;
            }
        }

        if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 4)
            return normalized[..^1];

        return normalized;
    }

    private static string RemoveDiacritics(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static List<ConversacionesEstadisticaTemaDto> BuildFrequentPhrases(IEnumerable<string> texts)
        => texts
            .Select(NormalizeFrequentPhrase)
            .Where(x => x.Length >= 12)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ConversacionesEstadisticaTemaDto { Texto = g.Key, Cantidad = g.Count() })
            .Where(x => x.Cantidad > 1)
            .OrderByDescending(x => x.Cantidad)
            .ThenBy(x => x.Texto, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static string NormalizeFrequentPhrase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }

    private static string NormalizeMessageType(string? messageType)
    {
        var normalized = string.IsNullOrWhiteSpace(messageType) ? "TEXT" : messageType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "TEXT" => "TEXT",
            "IMAGE" => "IMAGE",
            "DOCUMENT" => "DOCUMENT",
            "AUDIO" => "AUDIO",
            "VIDEO" => "VIDEO",
            "STICKER" => "STICKER",
            "LOCATION" => "LOCATION",
            "CONTACT" or "CONTACTS" => "CONTACT",
            "BUTTON" or "INTERACTIVE" or "REACTION" => "TEXT",
            "SYSTEM" => "SYSTEM",
            _ => "UNKNOWN"
        };
    }

    private static string NormalizeOutgoingMediaType(string? tipoArchivo, string? mimeType)
    {
        var normalized = NormalizeMessageType(tipoArchivo);
        if (normalized == "IMAGE" && string.Equals(mimeType, "image/webp", StringComparison.OrdinalIgnoreCase))
            return "sticker";

        if (normalized == "AUDIO" && string.Equals(mimeType, "audio/webm", StringComparison.OrdinalIgnoreCase))
            return "document";

        return normalized switch
        {
            "IMAGE" => "image",
            "AUDIO" => "audio",
            "VIDEO" => "video",
            "STICKER" => "sticker",
            _ => "document"
        };
    }

    private static string NormalizeOutgoingMime(string? mimeType, string? fileName, string tipoArchivo)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
            return NormalizeMimeForHttp(mimeType);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => NormalizeMessageType(tipoArchivo) == "STICKER" ? "image/webp" : "image/webp",
            ".ogg" or ".oga" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => NormalizeMessageType(tipoArchivo) == "VIDEO" ? "video/mp4" : "audio/mp4",
            ".webm" => NormalizeMessageType(tipoArchivo) == "VIDEO" ? "video/webm" : "audio/webm",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => InferMimeFromType(tipoArchivo)
        };
    }

    private static string NormalizeMimeForHttp(string mimeType)
    {
        var normalized = (mimeType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "application/octet-stream";

        var semicolonIndex = normalized.IndexOf(';', StringComparison.Ordinal);
        if (semicolonIndex >= 0)
            normalized = normalized[..semicolonIndex].Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? "application/octet-stream"
            : normalized;
    }

    private static string NormalizePhone(string? phone)
        => SearchTextHelper.DigitsOnly(phone);

    private static string SqlNormalizePhone(string columnName)
        => SearchTextHelper.SqlNormalizePhone(columnName);

    private static string SqlPhoneEquivalentPredicate(string columnName, string phoneParameterName, string tailParameterName)
    {
        var normalizedColumn = SqlNormalizePhone(columnName);
        return $"""
            (
                {normalizedColumn} = {phoneParameterName}
                OR (
                    {tailParameterName} IS NOT NULL
                    AND LEN({normalizedColumn}) >= 10
                    AND RIGHT({normalizedColumn}, 10) = {tailParameterName}
                )
            )
            """;
    }

    private static string? GetPhoneComparableTail(string? phone)
        => SearchTextHelper.PhoneTail(phone);

    private static string? Like(string? value)
        => SearchTextHelper.LikeContainsOrNull(value);

    private static object DbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object DbNullablePreserve(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string NormalizeTechnicianId(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeClientCode(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string BuildTypingActorKey(long idConversacion, string? clienteId, string? usuario, string? sistema, string? idTecnico)
    {
        var client = clienteId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(client))
            return $"{idConversacion}:client:{client}".ToUpperInvariant();

        var user = usuario?.Trim() ?? string.Empty;
        var system = sistema?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(user))
            return $"{idConversacion}:user:{system}:{user}".ToUpperInvariant();

        var technician = NormalizeTechnicianId(idTecnico);
        return string.IsNullOrWhiteSpace(technician)
            ? string.Empty
            : $"{idConversacion}:tech:{technician}".ToUpperInvariant();
    }

    private static void PurgeExpiredTyping(DateTime nowUtc)
    {
        foreach (var item in TypingPresences)
        {
            if (nowUtc - item.Value.LastSeenUtc > TypingTtl)
                TypingPresences.TryRemove(item.Key, out _);
        }
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;

    private static int GetInt(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? 0 : Convert.ToInt32(rd.GetValue(index), CultureInfo.InvariantCulture);

    private static TimeSpan? GetNullableTimeSpan(SqlDataReader rd, int index)
    {
        if (rd.IsDBNull(index))
            return null;

        var seconds = Convert.ToDouble(rd.GetValue(index), CultureInfo.InvariantCulture);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string TrimForSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var clean = value.Trim();
        return clean.Length <= 500 ? clean : clean[..500];
    }

    private static bool TryBuildKnownSqlMessage(SqlException ex, out string message)
    {
        message = string.Empty;
        if (ex.Number != 208)
            return false;

        var rawMessage = ex.Message ?? string.Empty;
        if (!rawMessage.Contains("CONV_", StringComparison.OrdinalIgnoreCase))
            return false;

        var objectName = ExtractMissingObjectName(rawMessage);
        var objectLabel = string.IsNullOrWhiteSpace(objectName) ? "CONV_*" : objectName;
        message = $"El módulo Conversaciones todavía no está inicializado en la base activa. Falta crear el objeto {objectLabel}. Ejecutá el script docs/conversaciones_modelo_inicial.sql y recargá el módulo.";
        return true;
    }

    private static string ExtractMissingObjectName(string rawMessage)
    {
        var marker = "'";
        var firstQuote = rawMessage.IndexOf(marker, StringComparison.Ordinal);
        if (firstQuote < 0)
            return string.Empty;

        var secondQuote = rawMessage.IndexOf(marker, firstQuote + 1, StringComparison.Ordinal);
        if (secondQuote <= firstQuote)
            return string.Empty;

        var value = rawMessage[(firstQuote + 1)..secondQuote].Trim();
        return value.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase)
            ? value[4..]
            : value;
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
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(knownMessage, incidentId, ex);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await _appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private sealed class PendingMessageInsert
    {
        public long ConversationId { get; init; }
        public string Phone { get; init; } = string.Empty;
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string? ReplyToMessageId { get; init; }
        public string MessageType { get; init; } = "TEXT";
        public string Direction { get; init; } = string.Empty;
        public string EstadoEnvio { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
        public DateTime FechaHora { get; init; }
        public string? UsuarioAutor { get; init; }
        public string? SistemaAutor { get; init; }
        public string? IdTecnicoAutor { get; init; }
    }

    private sealed class ConversationIdentity
    {
        public long IdConversacion { get; init; }
        public string TelefonoWhatsApp { get; init; } = string.Empty;
        public string Canal { get; init; } = string.Empty;
    }

    private sealed class MessageReactionTarget
    {
        public long IdMensaje { get; init; }
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
    }

    private sealed class ContactLookupResult
    {
        public static ContactLookupResult Empty { get; } = new();

        public int? IdContact { get; init; }
        public string ClientCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ConversationLookupResult
    {
        public long IdConversacion { get; init; }
        public string NombreVisible { get; init; } = string.Empty;
        public int? IdContacto { get; init; }
    }

    private sealed class ConversationMergeCandidate
    {
        public long IdConversacion { get; init; }
        public string TelefonoWhatsApp { get; init; } = string.Empty;
        public int? IdContacto { get; init; }
    }

    private sealed class IncomingWhatsAppMessage
    {
        public string Phone { get; init; } = string.Empty;
        public string ContactName { get; init; } = string.Empty;
        public string MessageType { get; init; } = "TEXT";
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string WhatsAppReplyToMessageId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
        public List<IncomingWhatsAppAttachment> Attachments { get; init; } = [];
    }

    private sealed class IncomingWhatsAppStatus
    {
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string EstadoEnvio { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
    }

    private sealed class PendingMediaHydration
    {
        public ConversacionMensajeDto Message { get; init; } = new();
        public string PayloadJson { get; init; } = string.Empty;
    }

    private sealed class DebtTemplateDetail
    {
        public string DetailText { get; init; } = string.Empty;
        public string Observation { get; init; } = string.Empty;
    }

    private sealed class AttachmentServeRecord
    {
        public long IdAdjunto { get; init; }
        public long IdMensaje { get; init; }
        public long IdConversacion { get; init; }
        public string TipoArchivo { get; init; } = string.Empty;
        public string NombreArchivo { get; init; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string UrlArchivo { get; init; } = string.Empty;
        public string RutaLocal { get; set; } = string.Empty;
        public string AdjuntoPayloadJson { get; init; } = string.Empty;
        public string MensajePayloadJson { get; init; } = string.Empty;
    }

    private sealed class IncomingWhatsAppAttachment
    {
        public string MediaId { get; init; } = string.Empty;
        public string TipoArchivo { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
    }

    private sealed class WhatsAppMediaInfo
    {
        public string Url { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
        public long FileSize { get; init; }
    }

    private sealed class WhatsAppSendResult
    {
        public string EstadoEnvio { get; init; } = string.Empty;
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
    }

    private sealed class MetaTemplateResult
    {
        public string MetaTemplateId { get; init; } = string.Empty;
        public string EstadoMeta { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
    }

    private sealed class MetaTemplateStatusResult
    {
        public string MetaTemplateId { get; init; } = string.Empty;
        public string EstadoMeta { get; init; } = string.Empty;
        public string RechazoMotivo { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
    }

    private sealed record ConversationStateSummary(string CodigoEstado, string Descripcion);

    private sealed record TypingPresence(
        string ActorKey,
        long IdConversacion,
        string ClienteId,
        string IdTecnico,
        string NombreTecnico,
        string Usuario,
        string Sistema,
        DateTime LastSeenUtc,
        DateTime LastSeenLocal);
}

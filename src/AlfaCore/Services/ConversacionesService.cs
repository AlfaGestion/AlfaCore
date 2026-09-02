using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class ConversacionesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IHttpClientFactory httpClientFactory,
    ICentralBasesService centralBasesService,
    IConversacionesConfigService conversacionesConfigService,
    IWhatsAppWebSessionService whatsAppWebSessionService,
    INotificacionesPushService notificacionesPushService,
    ICentralAdminService centralAdminService,
    IConversacionesAuthorizationService conversacionesAuthorizationService,
    IConversacionAsistenteService asistenteService,
    IConversacionAsistenteHerramientasService asistenteHerramientasService,
    IAlfaKnowledgeSuggestionService alfaKnowledgeService,
    IWhatsAppWebhookTenantGuard whatsAppWebhookTenantGuard,
    IWhatsAppRuntimeCredentialResolver whatsAppRuntimeCredentialResolver,
    IMetaWhatsAppManagementClient metaWhatsAppManagementClient,
    IWebHostEnvironment environment) : IConversacionesService
{
    private readonly IAppEventService _appEvents = appEvents;
    private readonly INotificacionesPushService _notificacionesPushService = notificacionesPushService;
    private const string ManualWhatsAppConversationSummary = "Conversaci\u00f3n iniciada manualmente.";

    /// <summary>
    /// El worker de WhatsApp Web (Node) escribe los archivos de inbox en camelCase (phone,
    /// contactName, text...). System.Text.Json es case-sensitive por defecto, as\u00ed que sin esta
    /// opci\u00f3n cada propiedad del DTO quedaba en su valor default (vac\u00edo) al deserializar.
    /// </summary>
    private static readonly JsonSerializerOptions WhatsAppWebInboxJsonOptions = new(JsonSerializerDefaults.Web);
    private const string ManualWhatsAppInitialState = "PENDIENTE";
    private const string InternalEventDirection = "NOTA_INTERNA";
    private const string InternalEventMessageType = "SYSTEM";
    private const string MercadoLibreAnsweredExistsSql = """
        EXISTS (
            SELECT 1
            FROM dbo.CONV_MENSAJES ans
            WHERE ans.IdConversacion = c.IdConversacion
              AND ans.Direction = N'SALIENTE'
        )
        """;
    private const int DefaultAttachmentRecoveryMaxAttempts = 1;
    private const int MaxAutomaticMediaHydrationsPerConversation = 3;
    private const int MaxAutomaticAttachmentRecoveriesPerConversation = 3;
    private const int MaxAutomaticMediaRecoveryRequestsPerWindow = 20;
    private static readonly TimeSpan DefaultAutomaticMediaRecoveryMaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan AutomaticMediaRecoveryRateWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(8);
    private static readonly object AutomaticMediaRecoveryRateLock = new();
    private static readonly Queue<DateTime> AutomaticMediaRecoveryRequestTimesUtc = new();
    private static readonly ConcurrentDictionary<long, byte> MediaHydrationAttempts = new();
    private static readonly ConcurrentDictionary<long, int> AttachmentRecoveryAttempts = new();
    private static readonly ConcurrentDictionary<string, TypingPresence> TypingPresences = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> MaintenanceLastRunUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> DurableAttachmentColumnsEnsured = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DurableAttachmentColumnsLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan InboxMaintenanceInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InboxDuplicateMaintenanceInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ReadBaselineMaintenanceInterval = TimeSpan.FromMinutes(15);
    private static readonly string[] DebtSourceCandidates =
    [
        "VE_CPTES_SALDOS_VENTAS",
        "VE_CPTES_IMPAGOS",
        "VE_CPTES_SALDOS",
        "VE_CPTES_SALDOS_TODOS_SALDO",
        "VE_CPTES_SALDOS_TODOS"
    ];

    private string LegacyUploadsBasePath => Path.Combine(environment.ContentRootPath, "App_Data", "uploads", "conversaciones");
    private string UploadsBasePath => Path.Combine(LegacyUploadsBasePath, GetAttachmentStorageScope());
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configurÃ³ la cadena de conexiÃ³n 'ConnectionStrings:AlfaGestion'.");

    private async Task<string> ResolveConnectionStringAsync(int? idBase, CancellationToken ct)
    {
        var resolvedIdBase = idBase.GetValueOrDefault();
        if (resolvedIdBase > 0)
        {
            var baseInfo = await centralBasesService.GetByIdAsync(resolvedIdBase, ct);
            if (baseInfo is null)
                throw new InvalidOperationException("La base indicada para el adjunto no existe.");

            return new SqlConnectionStringBuilder
            {
                DataSource = baseInfo.DbServer,
                InitialCatalog = baseInfo.DbName,
                UserID = baseInfo.DbUser,
                Password = baseInfo.DbPassword,
                TrustServerCertificate = true
            }.ConnectionString;
        }

        return ConnectionString;
    }

    public string GetAttachmentScopeKey()
        => GetAttachmentScope().ScopeKey;

    private string GetAttachmentStorageScope()
    {
        var scope = GetAttachmentScope();
        var label = SanitizePathSegment(FirstNonEmpty(scope.Database, "base"));
        return $"{label}-{scope.ScopeKey}";
    }

    private (string Database, string ScopeKey) GetAttachmentScope()
    {
        var activeSession = sessionService.GetActiveSession();
        var server = activeSession?.Servidor?.Trim() ?? string.Empty;
        var database = activeSession?.BaseDatos?.Trim() ?? string.Empty;
        var baseId = activeSession?.BaseId ?? 0;

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(ConnectionString);
                server = FirstNonEmpty(server, builder.DataSource);
                database = FirstNonEmpty(database, builder.InitialCatalog);
            }
            catch
            {
                database = FirstNonEmpty(database, "default");
            }
        }

        var source = $"{baseId}|{server}|{database}".Trim('|');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())))[..16].ToLowerInvariant();
        return (database, hash);
    }

    public async Task<bool> HasConversationSchemaAsync(CancellationToken ct = default)
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

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn)
            {
                CommandTimeout = 3
            };

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is not null && Convert.ToBoolean(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (SqlException ex)
        {
            await TryLogConversationSchemaCheckFailureAsync(ex, ct);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            await TryLogConversationSchemaCheckFailureAsync(ex, ct);
            return false;
        }
        catch (Exception ex)
        {
            await TryLogConversationSchemaCheckFailureAsync(ex, ct);
            return false;
        }
    }

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
        }, "No se pudieron cargar los tÃ©cnicos.", ct);

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
        }, "No se pudieron cargar los estados de conversaciÃ³n.", ct);

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
                    (SELECT COUNT(1) FROM EventosRango WHERE Texto LIKE N'%cambiÃ³ el estado de Cerrada a%' OR Texto LIKE N'%cambio el estado de Cerrada a%'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'ENTRANTE'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'SALIENTE'),
                    (SELECT COUNT(1) FROM #MensajesRango WHERE Direction = N'NOTA_INTERNA'),
                    (SELECT COUNT(DISTINCT a.IdMensaje) FROM dbo.CONV_ADJUNTOS a INNER JOIN #MensajesRango m ON m.IdMensaje = a.IdMensaje),
                    (SELECT COUNT(1) FROM PrimerasRespuestas WHERE PrimeraRespuesta IS NOT NULL),
                    (SELECT COUNT(1) FROM EntrantesRango e WHERE NOT EXISTS (SELECT 1 FROM PrimerasRespuestas r WHERE r.IdConversacion = e.IdConversacion AND r.PrimeraRespuesta IS NOT NULL)),
                    (SELECT COUNT(1) FROM CierresRango),
                    (SELECT COUNT(1) FROM @Tickets t LEFT JOIN #BaseConversaciones c ON c.IdConversacion = t.IdConversacion WHERE c.IdConversacion IS NOT NULL AND (@IdTecnico IS NULL OR t.IdTecnico = @IdTecnico OR c.IdTecnico = @IdTecnico)),
                    (SELECT COUNT(1) FROM dbo.CONV_ASIGNACIONES a INNER JOIN #BaseConversaciones c ON c.IdConversacion = a.IdConversacion WHERE a.FechaHora >= @Desde AND a.FechaHora < @HastaExclusive AND (@IdTecnico IS NULL OR a.IdTecnico = @IdTecnico)),
                    (SELECT COUNT(1) FROM EventosRango WHERE Texto LIKE N'%cambiÃ³ el estado%' OR Texto LIKE N'%cambio el estado%' OR Texto LIKE N'%cerrÃ³ la conversaciÃ³n%' OR Texto LIKE N'%cerro la conversacion%'),
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
                        WHEN Texto LIKE N'%asignÃ³ la conversaciÃ³n%' OR Texto LIKE N'%asigno la conversacion%' THEN N'Asignaciones'
                        WHEN Texto LIKE N'%creÃ³ un ticket%' OR Texto LIKE N'%creo un ticket%' THEN N'Tickets'
                        WHEN Texto LIKE N'%cerrÃ³ la conversaciÃ³n%' OR Texto LIKE N'%cerro la conversacion%' THEN N'Cierres'
                        WHEN Texto LIKE N'%cambiÃ³ el estado%' OR Texto LIKE N'%cambio el estado%' THEN N'Cambios de estado'
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
            if (ShouldRunMaintenance("AutoCloseExpiredOpenWhatsAppConversations", InboxMaintenanceInterval))
                await AutoCloseExpiredOpenWhatsAppConversationsAsync(null, token);

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
                    ISNULL(noLeidos.CantidadNoLeida, 0) AS MensajesNoLeidosUsuario,
                    ISNULL(c.Canal, N''),
                    ISNULL(c.UsuarioExterno, N''),
                    ISNULL(c.FotoPerfilUrl, N''),
                    ISNULL(c.PerfilExternoVerificado, 0),
                    ISNULL(ultMsg.Direction, N''),
                    ISNULL(ultMsg.EstadoEnvio, N''),
                    ISNULL(ultMsg.MessageType, N''),
                    ISNULL(mlMeta.QuestionStatus, N''),
                    ISNULL(mlMeta.ItemStatus, N''),
                    ISNULL(clasificacionCliente.Descripcion, N''),
                    ISNULL(LTRIM(RTRIM(clienteClasificacion.Codigo)), N''),
                    ISNULL(clasificacionCliente.Color, 0),
                    CASE
                        WHEN c.Canal = N'WHATSAPP'
                         AND EXISTS (
                            SELECT 1
                            FROM dbo.CONV_WHATSAPP_NUMEROS n
                            WHERE n.IdNumero = c.IdNumeroWhatsApp
                              AND LTRIM(RTRIM(ISNULL(n.WebInstanceName, N''))) <> N''
                         )
                        THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS EsWhatsAppQr
                FROM dbo.CONV_CONVERSACIONES c
                INNER JOIN dbo.CONV_ESTADOS e
                    ON e.CodigoEstado = c.CodigoEstado
                OUTER APPLY (
                    SELECT TOP (1)
                        p.IdConversacion,
                        p.FechaHora_Grabacion
                    FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO p
                    WHERE p.IdConversacion = c.IdConversacion
                      AND p.Usuario = @UsuarioActual COLLATE Latin1_General_CI_AI
                    ORDER BY
                        CASE WHEN p.Sistema = @SistemaActual COLLATE Latin1_General_CI_AI THEN 0 ELSE 1 END,
                        p.FechaHora_Grabacion DESC
                ) pin
                LEFT JOIN dbo.VT_CLIENTES cli
                    ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, ''))))
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
                OUTER APPLY (
                    -- Codigo de clasificacion del cliente resuelto (directo o via contacto->cuenta),
                    -- solo para la cola de espera por prioridad (ver Orden = 'cola_espera').
                    SELECT TOP (1) ISNULL(cliCls.Clasificacion, '') AS Codigo
                    FROM dbo.VT_CLIENTES cliCls
                    WHERE LTRIM(RTRIM(COALESCE(NULLIF(c.ClienteCodigo, ''), contactoCuenta.Cuenta, ''))) <> ''
                      AND UPPER(LTRIM(RTRIM(cliCls.CODIGO))) = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(c.ClienteCodigo, ''), contactoCuenta.Cuenta, ''))))
                ) clienteClasificacion
                OUTER APPLY (
                    SELECT TOP (1) ISNULL(cls.Descripcion, '') AS Descripcion, ISNULL(cls.Color, 0) AS Color
                    FROM dbo.TA_CLASIFICACIONES cls
                    WHERE UPPER(LTRIM(RTRIM(cls.Codigo))) = UPPER(LTRIM(RTRIM(clienteClasificacion.Codigo)))
                ) clasificacionCliente
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
                      AND r.Usuario = @UsuarioActual COLLATE Latin1_General_CI_AI
                    ORDER BY
                        CASE WHEN r.Sistema = @SistemaActual COLLATE Latin1_General_CI_AI THEN 0 ELSE 1 END,
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
                        {ConversationMessageVisibleDateSql("msg")} AS FechaHoraVisible,
                        msg.Direction,
                        msg.EstadoEnvio,
                        msg.MessageType,
                        ISNULL(msg.Texto, N'') AS Texto
                    FROM dbo.CONV_MENSAJES msg
                    WHERE msg.IdConversacion = c.IdConversacion
                      AND NOT (
                          UPPER(LTRIM(RTRIM(ISNULL(msg.MessageType, N'')))) = N'REACTION'
                          OR ISNULL(msg.PayloadJson, N'') LIKE N'%"type":"reaction"%'
                          OR (
                              LTRIM(RTRIM(ISNULL(msg.WhatsAppReplyToMessageId, N''))) <> N''
                              AND (
                                  ISNULL(msg.Texto, N'') LIKE N'Reacci%n recibida:%'
                                  OR ISNULL(msg.Texto, N'') LIKE N'Reacci%n enviada:%'
                              )
                          )
                      )
                    ORDER BY {ConversationMessageVisibleDateSql("msg")} DESC, msg.IdMensaje DESC
                ) ultMsg
                OUTER APPLY (
                    SELECT TOP (1)
                        CASE
                            WHEN {MercadoLibreAnsweredExistsSql} THEN N'ANSWERED'
                            WHEN ISJSON(m.PayloadJson) = 1 THEN ISNULL(JSON_VALUE(m.PayloadJson, '$.question_status'), N'')
                            ELSE N''
                        END AS QuestionStatus,
                        CASE WHEN ISJSON(m.PayloadJson) = 1 THEN ISNULL(JSON_VALUE(m.PayloadJson, '$.item_status'), N'') ELSE N'' END AS ItemStatus
                    FROM dbo.CONV_MENSAJES m
                    WHERE c.Canal = N'MERCADOLIBRE'
                      AND m.IdConversacion = c.IdConversacion
                      AND m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                    ORDER BY m.IdMensaje DESC
                ) mlMeta
                CROSS APPLY (
                    SELECT
                        CASE
                            WHEN @Search IS NULL OR @ClienteCodigo IS NOT NULL THEN 0
                            WHEN LTRIM(RTRIM(ISNULL(c.NombreVisible, N''))) COLLATE Latin1_General_CI_AI = @SearchTerm THEN 0
                            WHEN LTRIM(RTRIM(ISNULL(mc.Nombre_y_Apellido, N''))) COLLATE Latin1_General_CI_AI = @SearchTerm THEN 0
                            WHEN LTRIM(RTRIM(ISNULL(COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial), N''))) COLLATE Latin1_General_CI_AI = @SearchTerm THEN 0
                            WHEN c.NombreVisible COLLATE Latin1_General_CI_AI LIKE @SearchPrefix THEN 1
                            WHEN mc.Nombre_y_Apellido COLLATE Latin1_General_CI_AI LIKE @SearchPrefix THEN 1
                            WHEN COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial) COLLATE Latin1_General_CI_AI LIKE @SearchPrefix THEN 1
                            WHEN c.NombreVisible COLLATE Latin1_General_CI_AI LIKE @Search THEN 2
                            WHEN mc.Nombre_y_Apellido COLLATE Latin1_General_CI_AI LIKE @Search THEN 2
                            WHEN COALESCE(NULLIF(cli.RAZON_SOCIAL, ''), contactoCuenta.RazonSocial) COLLATE Latin1_General_CI_AI LIKE @Search THEN 2
                            WHEN @SearchPhone IS NOT NULL AND (
                                {SqlPhoneEquivalentPredicate("c.TelefonoWhatsApp", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Telefono", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.Celular", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.TelefonoPart", "@SearchPhone", "@SearchPhoneTail")}
                                OR {SqlPhoneEquivalentPredicate("mc.CelularPart", "@SearchPhone", "@SearchPhoneTail")}
                            ) THEN 3
                            WHEN c.ResumenUltimoMensaje COLLATE Latin1_General_CI_AI LIKE @Search THEN 4
                            WHEN EXISTS (
                                SELECT 1
                                FROM dbo.CONV_MENSAJES rankMsg
                                WHERE rankMsg.IdConversacion = c.IdConversacion
                                  AND rankMsg.Texto COLLATE Latin1_General_CI_AI LIKE @Search
                                  AND (@Desde IS NULL OR rankMsg.FechaHora >= @Desde)
                                  AND (@HastaExclusive IS NULL OR rankMsg.FechaHora < @HastaExclusive)
                            ) THEN 5
                            ELSE 9
                        END AS SearchRank
                ) searchRank
                WHERE
                    (
                        @Canal IS NULL
                        OR (@Canal = N'SOCIAL' AND c.Canal IN (N'WHATSAPP', N'INSTAGRAM', N'FACEBOOK'))
                        OR (@Canal <> N'SOCIAL' AND c.Canal = @Canal)
                    )
                    AND (
                        c.IdNumeroWhatsApp IS NULL
                        OR EXISTS (
                            SELECT 1 FROM dbo.CONV_ADMINISTRADORES admConv
                            WHERE admConv.Usuario = @UsuarioActual COLLATE Latin1_General_CI_AI
                              AND admConv.Sistema = @SistemaActual COLLATE Latin1_General_CI_AI
                        )
                        OR NOT EXISTS (
                            SELECT 1 FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numUsuAny
                            WHERE numUsuAny.IdNumero = c.IdNumeroWhatsApp
                        )
                        OR EXISTS (
                            SELECT 1 FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numUsu
                            WHERE numUsu.IdNumero = c.IdNumeroWhatsApp
                              AND numUsu.Usuario = @UsuarioActual COLLATE Latin1_General_CI_AI
                              AND numUsu.Sistema = @SistemaActual COLLATE Latin1_General_CI_AI
                        )
                    )
                    AND (
                        @IdNumeroWhatsApp IS NULL
                        OR (
                            c.Canal = N'WHATSAPP'
                            AND c.IdNumeroWhatsApp = @IdNumeroWhatsApp
                        )
                    )
                    AND (
                        @CodigoEstado IS NULL
                        OR (@CodigoEstado = @EstadoSinFinalizar AND ISNULL(e.EsCerrado, 0) = 0 AND ISNULL(c.Archivada, 0) = 0)
                        OR (@CodigoEstado <> @EstadoSinFinalizar AND c.CodigoEstado = @CodigoEstado)
                    )
                    AND (
                        c.Canal <> N'MERCADOLIBRE'
                        OR EXISTS (
                            SELECT 1
                            FROM dbo.CONV_MENSAJES mlVisible
                            WHERE mlVisible.IdConversacion = c.IdConversacion
                        )
                    )
                    AND (
                        @ClienteCodigo IS NULL
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = @ClienteCodigo
                        OR contactoCuenta.Cuenta = @ClienteCodigo COLLATE Latin1_General_CI_AI
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
                    AND (@IdTecnicoActual IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual COLLATE Latin1_General_CI_AI)
                    AND (
                        @Modo = 'todas'
                        OR (@Modo = 'sin_asignar' AND (c.IdTecnico IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = ''))
                        OR (@Modo = 'asignadas_a_mi' AND LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual COLLATE Latin1_General_CI_AI)
                        OR (@Modo = 'pendientes' AND ISNULL(e.EsCerrado, 0) = 0)
                        OR (@Modo = 'cerradas' AND ISNULL(e.EsCerrado, 0) = 1)
                    )
                    AND NOT (
                        c.Canal = N'MERCADOLIBRE'
                        AND UPPER(ISNULL(mlMeta.QuestionStatus, N'')) = N'ANSWERED'
                        AND ISNULL(e.EsCerrado, 0) = 0
                        AND (
                            @CodigoEstado = @EstadoSinFinalizar
                            OR @Modo = 'pendientes'
                            OR UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) = N'ABIERTA'
                        )
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
                              AND (m.Texto LIKE N'%cambiÃ³ el estado de Cerrada a%' OR m.Texto LIKE N'%cambio el estado de Cerrada a%')
                              AND (@Desde IS NULL OR m.FechaHora >= @Desde)
                              AND (@HastaExclusive IS NULL OR m.FechaHora < @HastaExclusive)
                        ))
                        OR (@Auditoria = N'cambios_estado' AND EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES m
                            WHERE m.IdConversacion = c.IdConversacion
                              AND m.Direction = N'NOTA_INTERNA'
                              AND m.MessageType = N'SYSTEM'
                              AND (m.Texto LIKE N'%cambiÃ³ el estado%' OR m.Texto LIKE N'%cambio el estado%' OR m.Texto LIKE N'%cerrÃ³ la conversaciÃ³n%' OR m.Texto LIKE N'%cerro la conversacion%')
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
                {BuildInboxOrderByClause(filters.Orden)}
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureConversationPinsTableAsync(cn, token);
            await EnsureConversationReadStateTableAsync(cn, token);
            if (ShouldRunMaintenance($"ReadBaselines:{usuarioActual}:{sistemaActual}:{clienteCodigo}", ReadBaselineMaintenanceInterval))
                await EnsureConversationReadBaselinesAsync(cn, usuarioActual, sistemaActual, clienteCodigo, token);

            if (string.IsNullOrWhiteSpace(clienteCodigo))
            {
                if (ShouldRunMaintenance("LinkUnassociatedWhatsAppConversationsByPhone", InboxMaintenanceInterval))
                    await LinkUnassociatedWhatsAppConversationsByPhoneAsync(cn, token);

                if (ShouldRunMaintenance("ConsolidateExistingDuplicateWhatsAppConversations", InboxDuplicateMaintenanceInterval))
                    await ConsolidateExistingDuplicateWhatsAppConversationsAsync(cn, token);

                if (ShouldRunMaintenance("ReopenClosedConversationsWithIncomingAfterClose", InboxMaintenanceInterval))
                    await ReopenClosedConversationsWithIncomingAfterCloseAsync(cn, null, token);
            }

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Canal", DbNullable(filters.Canal));
            cmd.Parameters.AddWithValue("@CodigoEstado", DbNullable(filters.CodigoEstado));
            cmd.Parameters.AddWithValue("@EstadoSinFinalizar", ConversacionesInboxFilters.EstadoSinFinalizar);
            cmd.Parameters.AddWithValue("@Search", DbNullable(Like(filters.Search)));
            cmd.Parameters.AddWithValue("@SearchTerm", DbNullable(SearchTextHelper.Normalize(filters.Search)));
            cmd.Parameters.AddWithValue("@SearchPrefix", DbNullable(LikePrefix(filters.Search)));
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
            cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", filters.IdNumeroWhatsApp.HasValue ? filters.IdNumeroWhatsApp.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ManualWhatsAppConversationSummary", ManualWhatsAppConversationSummary);
            cmd.Parameters.AddWithValue("@Offset", Math.Max(0, filters.Offset));
            cmd.Parameters.AddWithValue("@Limit", Math.Clamp(filters.Limit, 1, 200));
            cmd.Parameters.AddWithValue("@Clasifica1", DbNullable(filters.Clasifica1));
            cmd.Parameters.AddWithValue("@Clasifica2", DbNullable(filters.Clasifica2));
            cmd.Parameters.AddWithValue("@Clasifica3", DbNullable(filters.Clasifica3));

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
                    MensajesNoLeidosUsuario = rd.IsDBNull(20) ? 0 : rd.GetInt32(20),
                    Canal = GetString(rd, 21),
                    UsuarioExterno = GetString(rd, 22),
                    FotoPerfilUrl = GetString(rd, 23),
                    PerfilExternoVerificado = !rd.IsDBNull(24) && rd.GetBoolean(24),
                    DireccionUltimoMensaje = GetString(rd, 25),
                    EstadoUltimoMensaje = GetString(rd, 26),
                    TipoUltimoMensaje = GetString(rd, 27),
                    MercadoLibreQuestionStatus = GetString(rd, 28),
                    MercadoLibreItemStatus = GetString(rd, 29),
                    ClasificacionDescripcion = GetString(rd, 30),
                    ClasificacionCodigo = GetString(rd, 31),
                    ClasificacionColorHex = rd.IsDBNull(32) ? string.Empty : VbColorIntToHex(Convert.ToInt32(rd.GetValue(32))),
                    EsWhatsAppQr = !rd.IsDBNull(33) && rd.GetBoolean(33)
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
                        OR (@Auditoria = N'reabiertas' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%cambiÃ³ el estado de Cerrada a%' OR m.Texto LIKE N'%cambio el estado de Cerrada a%'))
                        OR (@Auditoria = N'cambios_estado' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%cambiÃ³ el estado%' OR m.Texto LIKE N'%cambio el estado%' OR m.Texto LIKE N'%cerrÃ³ la conversaciÃ³n%' OR m.Texto LIKE N'%cerro la conversacion%'))
                        OR (@Auditoria = N'tipo_mensaje' AND UPPER(ISNULL(m.MessageType, N'')) = @TipoMensaje)
                        OR (@Auditoria = N'tickets' AND m.Direction = N'NOTA_INTERNA' AND m.MessageType = N'SYSTEM' AND (m.Texto LIKE N'%creÃ³ un ticket%' OR m.Texto LIKE N'%creo un ticket%'))
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
        }, "No se pudieron cargar los mensajes de auditorÃ­a.", ct);

    public Task<ConversacionDetalleDto?> GetConversationAsync(long conversationId, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetConversation", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(conversationId, token);
            await ReassignStaleWhatsAppConversationsToDefaultWebAsync(conversationId, token);

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
                    c.FechaHoraCierre,
                    ISNULL(CAST(mc.Observaciones AS nvarchar(max)), ''),
                    ISNULL(contactoNotas.Notas, ''),
                    COALESCE(NULLIF(clienteNotas.Observaciones, ''), NULLIF(clienteAdicNotas.Observaciones, ''), ''),
                    ISNULL(cuentaObs.Nota, ''),
                    ISNULL(c.IdentificadorExternoContacto, N''),
                    ISNULL(c.UsuarioExterno, N''),
                    ISNULL(c.FotoPerfilUrl, N''),
                    c.SeguidoresExterno,
                    ISNULL(c.PerfilExternoVerificado, 0),
                    c.UsuarioSigueCuenta,
                    c.CuentaSigueUsuario,
                    c.FechaHoraPerfilExterno,
                    ISNULL(mlMeta.QuestionStatus, N''),
                    ISNULL(mlMeta.ItemStatus, N''),
                    ISNULL(mlMeta.ItemPermalink, N''),
                    ISNULL(clienteClasificacion.Descripcion, N''),
                    clienteAbono.Estado,
                    c.IdNumeroWhatsApp,
                    CASE
                        WHEN c.Canal = N'WHATSAPP'
                         AND EXISTS (
                            SELECT 1
                            FROM dbo.CONV_WHATSAPP_NUMEROS n
                            WHERE n.IdNumero = c.IdNumeroWhatsApp
                              AND LTRIM(RTRIM(ISNULL(n.WebInstanceName, N''))) <> N''
                         )
                        THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS EsWhatsAppQr
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
                OUTER APPLY (
                    SELECT COALESCE(NULLIF(LTRIM(RTRIM(c.ClienteCodigo)), ''), contactoCuenta.Cuenta, '') AS Codigo
                ) clienteContexto
                OUTER APPLY (
                    SELECT
                        STUFF((
                            SELECT CHAR(10) + LTRIM(RTRIM(CAST(adic.DescrAdic AS nvarchar(max))))
                            FROM dbo.MA_CONTACTOS_ADIC adic
                            WHERE c.IdContacto IS NOT NULL
                              AND mc.id IS NOT NULL
                              AND (
                                    adic.id = mc.id
                                 OR adic.IdContacto = mc.id
                                 OR adic.IdContacto = ISNULL(NULLIF(mc.idContacto, 0), mc.id)
                              )
                              AND LTRIM(RTRIM(ISNULL(CAST(adic.DescrAdic AS nvarchar(max)), ''))) <> ''
                            ORDER BY adic.id
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'), 1, 1, '') AS Notas
                ) contactoNotas
                OUTER APPLY (
                    SELECT TOP (1)
                        ISNULL(CAST(cliNotas.OBSERVACIONES AS nvarchar(max)), '') AS Observaciones
                    FROM dbo.VT_CLIENTES cliNotas
                    WHERE UPPER(LTRIM(RTRIM(cliNotas.CODIGO))) = UPPER(LTRIM(RTRIM(clienteContexto.Codigo)))
                ) clienteNotas
                OUTER APPLY (
                    SELECT TOP (1)
                        ISNULL(CAST(adic.OBSERVACIONES AS nvarchar(max)), '') AS Observaciones
                    FROM dbo.MA_CUENTASADIC adic
                    WHERE UPPER(LTRIM(RTRIM(adic.CODIGO))) = UPPER(LTRIM(RTRIM(clienteContexto.Codigo)))
                ) clienteAdicNotas
                OUTER APPLY (
                    SELECT TOP (1)
                        ISNULL(CAST(obs.Nota AS nvarchar(max)), '') AS Nota
                    FROM dbo.MA_CUENTASOBS obs
                    WHERE UPPER(LTRIM(RTRIM(obs.Codigo))) = UPPER(LTRIM(RTRIM(clienteContexto.Codigo)))
                      AND ISNULL(obs.Sucursal, 0) = 0
                ) cuentaObs
                OUTER APPLY (
                    SELECT TOP (1) ISNULL(cls.Descripcion, '') AS Descripcion
                    FROM dbo.VT_CLIENTES cliCls
                    LEFT JOIN dbo.TA_CLASIFICACIONES cls ON cls.Codigo = cliCls.Clasificacion
                    WHERE LTRIM(RTRIM(clienteContexto.Codigo)) <> ''
                      AND UPPER(LTRIM(RTRIM(cliCls.CODIGO))) = UPPER(LTRIM(RTRIM(clienteContexto.Codigo)))
                ) clienteClasificacion
                OUTER APPLY (
                    -- Abonado al mantenimiento: cualquier fila de la cuenta en MA_CUENTAS_AUTOCPTES (sin
                    -- filtrar por Concepto), la mÃ¡s reciente por FechaUltMov si hay varias (distintas
                    -- sucursales/conceptos). Sin fila = NULL = "sin abono" (se resuelve en C#).
                    SELECT TOP (1)
                        CASE
                            WHEN ISNULL(auto.Activo, 0) = 0 THEN 'Suspendido'
                            WHEN auto.FechaUltMov IS NULL OR auto.FechaUltMov < DATEADD(MONTH, -3, GETDATE()) THEN 'Suspendido'
                            ELSE 'Abonado'
                        END AS Estado
                    FROM dbo.MA_CUENTAS_AUTOCPTES auto
                    WHERE LTRIM(RTRIM(clienteContexto.Codigo)) <> ''
                      AND UPPER(LTRIM(RTRIM(auto.Cuenta))) = UPPER(LTRIM(RTRIM(clienteContexto.Codigo)))
                    ORDER BY auto.FechaUltMov DESC
                ) clienteAbono
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
                OUTER APPLY (
                    SELECT TOP (1)
                        CASE
                            WHEN {MercadoLibreAnsweredExistsSql} THEN N'ANSWERED'
                            WHEN ISJSON(m.PayloadJson) = 1 THEN ISNULL(JSON_VALUE(m.PayloadJson, '$.question_status'), N'')
                            ELSE N''
                        END AS QuestionStatus,
                        CASE WHEN ISJSON(m.PayloadJson) = 1 THEN ISNULL(JSON_VALUE(m.PayloadJson, '$.item_status'), N'') ELSE N'' END AS ItemStatus,
                        CASE WHEN ISJSON(m.PayloadJson) = 1 THEN ISNULL(JSON_VALUE(m.PayloadJson, '$.item_permalink'), N'') ELSE N'' END AS ItemPermalink
                    FROM dbo.CONV_MENSAJES m
                    WHERE c.Canal = N'MERCADOLIBRE'
                      AND m.IdConversacion = c.IdConversacion
                      AND m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                    ORDER BY m.IdMensaje DESC
                ) mlMeta
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
                FechaHoraCierre = rd.IsDBNull(23) ? null : rd.GetDateTime(23),
                ContactoObservaciones = GetString(rd, 24),
                ContactoNotas = GetString(rd, 25),
                ClienteObservaciones = GetString(rd, 26),
                ClienteNotaCuenta = GetString(rd, 27),
                IdentificadorExternoContacto = GetString(rd, 28),
                UsuarioExterno = GetString(rd, 29),
                FotoPerfilUrl = GetString(rd, 30),
                SeguidoresExterno = rd.IsDBNull(31) ? null : rd.GetInt32(31),
                PerfilExternoVerificado = !rd.IsDBNull(32) && rd.GetBoolean(32),
                UsuarioSigueCuenta = rd.IsDBNull(33) ? null : rd.GetBoolean(33),
                CuentaSigueUsuario = rd.IsDBNull(34) ? null : rd.GetBoolean(34),
                FechaHoraPerfilExterno = rd.IsDBNull(35) ? null : rd.GetDateTime(35),
                MercadoLibreQuestionStatus = GetString(rd, 36),
                MercadoLibreItemStatus = GetString(rd, 37),
                MercadoLibreItemPermalink = GetString(rd, 38),
                ClasificacionDescripcion = GetString(rd, 39),
                EstadoAbono = rd.IsDBNull(40) ? ConversacionAbonoEstados.SinAbono : GetString(rd, 40),
                IdNumeroWhatsApp = rd.IsDBNull(41) ? null : rd.GetInt32(41),
                EsWhatsAppQr = !rd.IsDBNull(42) && rd.GetBoolean(42)
            };

            await TryRefreshFacebookConversationProfileAsync(item, token);
            ApplyWhatsAppWindow(item);
            return item;
        }, "No se pudo cargar la conversaciÃ³n.", ct);

    public Task<IReadOnlyList<ConversacionMensajeDto>> GetMessagesAsync(long conversationId, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetMessages", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(conversationId, token);
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
                    ) THEN 1 ELSE 0 END,
                    ISNULL(m.MarcaInterna, '')
                FROM dbo.CONV_MENSAJES m
                INNER JOIN dbo.CONV_CONVERSACIONES c
                    ON c.IdConversacion = @IdConversacion
                LEFT JOIN dbo.V_TA_Tecnicos t
                    ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(m.IdTecnicoAutor))
                WHERE m.IdConversacion = @IdConversacion
                  AND (
                        c.Canal <> N'MERCADOLIBRE'
                        OR NULLIF(c.IdentificadorExternoConversacion, N'') IS NULL
                        OR m.Direction = N'NOTA_INTERNA'
                        OR m.Direction = N'SALIENTE'
                        OR m.MessageType = N'SYSTEM'
                        OR NULLIF(m.MessageIdExterno, N'') IS NULL
                        OR m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                        OR m.MessageIdExterno LIKE CONCAT(N'meli-answer-', c.IdentificadorExternoConversacion, N'-%')
                      )
                ORDER BY {ConversationMessageVisibleDateSql("m")} ASC, m.IdMensaje ASC
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
                    TieneAdjuntos = GetInt(rd, 15) == 1,
                    MarcaInterna = GetString(rd, 16)
                };

                items.Add(item);
            }

            return (IReadOnlyList<ConversacionMensajeDto>)items;
        }, "No se pudieron cargar los mensajes.", ct);

    public Task<ConversacionMensajesPaginaDto> GetMessagesPageAsync(
        long conversationId,
        int take,
        DateTime? beforeDate = null,
        long? beforeMessageId = null,
        CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetMessagesPage", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(conversationId, token);
            await AutoCloseExpiredOpenWhatsAppConversationsAsync(conversationId, token);

            var pageSize = Math.Max(1, Math.Min(200, take));
            var hasCursor = beforeDate.HasValue && beforeMessageId.HasValue;
            var pageSql = $"""
                ;WITH FilteredMessages AS (
                    SELECT
                        m.IdMensaje,
                        m.IdConversacion,
                        ISNULL(m.TelefonoWhatsApp, '') AS TelefonoWhatsApp,
                        ISNULL(m.WhatsAppMessageId, '') AS WhatsAppMessageId,
                        ISNULL(m.WhatsAppReplyToMessageId, '') AS WhatsAppReplyToMessageId,
                        ISNULL(m.MessageType, '') AS MessageType,
                        ISNULL(m.Direction, '') AS Direction,
                        ISNULL(m.EstadoEnvio, '') AS EstadoEnvio,
                        ISNULL(m.Texto, '') AS Texto,
                        {ConversationMessageVisibleDateSql("m")} AS FechaHora,
                        ISNULL(m.UsuarioAutor, '') AS UsuarioAutor,
                        ISNULL(m.SistemaAutor, '') AS SistemaAutor,
                        ISNULL(m.IdTecnicoAutor, '') AS IdTecnicoAutor,
                        ISNULL(t.Nombre, '') AS TecnicoAutorNombre,
                        ISNULL(m.PayloadJson, '') AS PayloadJson,
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM dbo.CONV_ADJUNTOS a
                            WHERE a.IdMensaje = m.IdMensaje
                        ) THEN 1 ELSE 0 END AS TieneAdjuntos,
                        ISNULL(m.MarcaInterna, '') AS MarcaInterna
                    FROM dbo.CONV_MENSAJES m
                    INNER JOIN dbo.CONV_CONVERSACIONES c
                        ON c.IdConversacion = @IdConversacion
                    LEFT JOIN dbo.V_TA_Tecnicos t
                        ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(m.IdTecnicoAutor))
                    WHERE m.IdConversacion = @IdConversacion
                      AND (
                            c.Canal <> N'MERCADOLIBRE'
                            OR NULLIF(c.IdentificadorExternoConversacion, N'') IS NULL
                            OR m.Direction = N'NOTA_INTERNA'
                            OR m.Direction = N'SALIENTE'
                            OR m.MessageType = N'SYSTEM'
                            OR NULLIF(m.MessageIdExterno, N'') IS NULL
                            OR m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                            OR m.MessageIdExterno LIKE CONCAT(N'meli-answer-', c.IdentificadorExternoConversacion, N'-%')
                          )
                ),
                PagedMessages AS (
                    SELECT TOP (@Take)
                        *
                    FROM FilteredMessages
                    WHERE @HasCursor = 0
                       OR FechaHora < @BeforeDate
                       OR (FechaHora = @BeforeDate AND IdMensaje < @BeforeMessageId)
                    ORDER BY FechaHora DESC, IdMensaje DESC
                )
                SELECT
                    page.IdMensaje,
                    page.IdConversacion,
                    page.TelefonoWhatsApp,
                    page.WhatsAppMessageId,
                    page.WhatsAppReplyToMessageId,
                    page.MessageType,
                    page.Direction,
                    page.EstadoEnvio,
                    page.Texto,
                    page.FechaHora,
                    page.UsuarioAutor,
                    page.SistemaAutor,
                    page.IdTecnicoAutor,
                    page.TecnicoAutorNombre,
                    page.PayloadJson,
                    page.TieneAdjuntos,
                    page.MarcaInterna
                FROM PagedMessages page
                ORDER BY page.FechaHora ASC, page.IdMensaje ASC;
                """;
            var totalSql = $"""
                SELECT COUNT(1)
                FROM dbo.CONV_MENSAJES m
                INNER JOIN dbo.CONV_CONVERSACIONES c
                    ON c.IdConversacion = @IdConversacion
                WHERE m.IdConversacion = @IdConversacion
                  AND (
                        c.Canal <> N'MERCADOLIBRE'
                        OR NULLIF(c.IdentificadorExternoConversacion, N'') IS NULL
                        OR m.Direction = N'NOTA_INTERNA'
                        OR m.Direction = N'SALIENTE'
                        OR m.MessageType = N'SYSTEM'
                        OR NULLIF(m.MessageIdExterno, N'') IS NULL
                        OR m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                        OR m.MessageIdExterno LIKE CONCAT(N'meli-answer-', c.IdentificadorExternoConversacion, N'-%')
                      );
                """;
            var hasMoreSql = $"""
                SELECT TOP (1) 1
                FROM dbo.CONV_MENSAJES m
                INNER JOIN dbo.CONV_CONVERSACIONES c
                    ON c.IdConversacion = @IdConversacion
                WHERE m.IdConversacion = @IdConversacion
                  AND (
                        c.Canal <> N'MERCADOLIBRE'
                        OR NULLIF(c.IdentificadorExternoConversacion, N'') IS NULL
                        OR m.Direction = N'NOTA_INTERNA'
                        OR m.Direction = N'SALIENTE'
                        OR m.MessageType = N'SYSTEM'
                        OR NULLIF(m.MessageIdExterno, N'') IS NULL
                        OR m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
                        OR m.MessageIdExterno LIKE CONCAT(N'meli-answer-', c.IdentificadorExternoConversacion, N'-%')
                      )
                  AND (
                        {ConversationMessageVisibleDateSql("m")} < @OldestDate
                        OR ({ConversationMessageVisibleDateSql("m")} = @OldestDate AND m.IdMensaje < @OldestMessageId)
                      );
                """;

            var items = new List<ConversacionMensajeDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(pageSql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", conversationId);
            cmd.Parameters.AddWithValue("@Take", pageSize);
            cmd.Parameters.AddWithValue("@HasCursor", hasCursor ? 1 : 0);
            cmd.Parameters.AddWithValue("@BeforeDate", hasCursor ? beforeDate!.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@BeforeMessageId", hasCursor ? beforeMessageId!.Value : DBNull.Value);

            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionMensajeDto
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
                    FechaHora = rd.IsDBNull(9) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(9)),
                    UsuarioAutor = GetString(rd, 10),
                    SistemaAutor = GetString(rd, 11),
                    IdTecnicoAutor = GetString(rd, 12),
                    TecnicoAutorNombre = GetString(rd, 13),
                    PayloadJson = GetString(rd, 14),
                    TieneAdjuntos = GetInt(rd, 15) == 1,
                    MarcaInterna = GetString(rd, 16)
                });
            }

            await rd.DisposeAsync();

            await using var totalCmd = new SqlCommand(totalSql, cn);
            totalCmd.Parameters.AddWithValue("@IdConversacion", conversationId);
            var totalMessages = Convert.ToInt32(await totalCmd.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture);

            var hasMoreBefore = false;
            if (items.Count > 0)
            {
                var oldestMessage = items[0];
                await using var hasMoreCmd = new SqlCommand(hasMoreSql, cn);
                hasMoreCmd.Parameters.AddWithValue("@IdConversacion", conversationId);
                hasMoreCmd.Parameters.AddWithValue("@OldestDate", oldestMessage.FechaHora);
                hasMoreCmd.Parameters.AddWithValue("@OldestMessageId", oldestMessage.IdMensaje);
                hasMoreBefore = await hasMoreCmd.ExecuteScalarAsync(token) is not null;
            }

            return new ConversacionMensajesPaginaDto
            {
                Messages = items,
                TotalMessages = totalMessages,
                HasMoreBefore = hasMoreBefore
            };
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
                throw new InvalidOperationException("No se recibiÃ³ la conversaciÃ³n a relacionar.");
            if (string.IsNullOrWhiteSpace(clienteCodigo))
                throw new InvalidOperationException("No se recibiÃ³ el cliente a relacionar.");

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
                    throw new InvalidOperationException("La conversaciÃ³n seleccionada ya no existe.");
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
                "Cliente relacionado manualmente desde contexto de conversaciÃ³n.",
                new { request.IdConversacion, ClienteCodigo = clienteCodigo, ClienteNombre = clienteNombre, IdContacto = idContacto },
                token);
            return true;
        }, "No se pudo relacionar el cliente con la conversaciÃ³n.", ct);
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
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            if (request.IdNumeroWhatsApp is > 0)
                await conversacionesAuthorizationService.EnsureCanUseWhatsAppNumeroAsync(request.IdNumeroWhatsApp.Value, token);

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            var isInternal = string.Equals(conversation.Canal, "INTERNO", StringComparison.OrdinalIgnoreCase);
            var isWhatsApp = string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase);
            var isInstagram = string.Equals(conversation.Canal, "INSTAGRAM", StringComparison.OrdinalIgnoreCase);
            var isFacebook = string.Equals(conversation.Canal, "FACEBOOK", StringComparison.OrdinalIgnoreCase);
            var isMercadoLibre = string.Equals(conversation.Canal, "MERCADOLIBRE", StringComparison.OrdinalIgnoreCase);
            if (!isInternal && !isWhatsApp && !isInstagram && !isFacebook && !isMercadoLibre)
                throw new InvalidOperationException($"El canal {conversation.Canal} todavÃ­a no tiene envÃ­o habilitado.");
            var now = BusinessNow();

            string initialState;
            ConversacionWhatsAppConfigDto? whatsAppConfig = null;
            ConversacionInstagramConfigDto? instagramConfig = null;
            ConversacionFacebookConfigDto? facebookConfig = null;
            ConversacionMercadoLibreConfigDto? mercadoLibreConfig = null;
            ConversacionWhatsAppNumeroDto? numeroWeb = null;
            string whatsAppDeliveryProvider = ConversacionWhatsAppProviders.MetaCloud;
            if (isInternal)
            {
                initialState = "ENVIADO";
            }
            else if (isWhatsApp)
            {
                var usarApiMetaPredeterminada = request.UsarApiMetaPredeterminada;
                var idNumeroWhatsAppParaEnvio = usarApiMetaPredeterminada
                    ? null
                    : request.IdNumeroWhatsApp is > 0
                        ? request.IdNumeroWhatsApp
                        : conversation.IdNumeroWhatsApp;

                whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(token);

                // El proveedor global (Meta vs Web) es sólo un default de base: la conversación ya
                // sabe de qué número entró (IdNumeroWhatsApp). Si ese número específico tiene una
                // instancia de WhatsApp Web propia (WebInstanceName, seteado una sola vez al conectar
                // por QR/código), la respuesta SIEMPRE sale por esa sesión -- sin importar que el
                // "Proveedor predeterminado" global de la base todavía diga Meta Cloud. Evita que una
                // conversación real de WhatsApp Business quede en "Sin configuración" solo porque
                // nadie cambió el select global (gap ya documentado en C2.3).
                numeroWeb = idNumeroWhatsAppParaEnvio.HasValue
                    ? await conversacionesConfigService.GetWhatsAppNumeroAsync(idNumeroWhatsAppParaEnvio.Value, token)
                    : null;
                numeroWeb = await ResolveWhatsAppWebNumeroForSendAsync(conversation.IdConversacion, numeroWeb, token);

                // PhoneNumberId a usar para el envío por API: preferí el del número elegido en
                // "Enviar desde" (numeroWeb, la selección explícita del técnico); si ese número no
                // tiene uno propio cargado, caé al de la conversación (con qué PhoneNumberId entró
                // originalmente); si tampoco hay, quedate con el default global de whatsAppConfig.
                // Antes solo miraba conversation.PhoneNumberId, ignorando la selección manual del
                // combo para números API -- si ese campo de la conversación estaba vacío (ej. una
                // conversación sin ese dato cargado), el mensaje se mandaba silenciosamente con lo
                // que hubiera en la config global, sin respetar el número elegido.
                var phoneNumberIdParaEnvio = usarApiMetaPredeterminada
                    ? string.Empty
                    : FirstValidMetaPhoneNumberId(numeroWeb?.PhoneNumberId, conversation.PhoneNumberId);
                if (!string.IsNullOrWhiteSpace(phoneNumberIdParaEnvio))
                    whatsAppConfig.PhoneNumberId = phoneNumberIdParaEnvio;

                var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
                var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync(
                    activeBaseId, numeroWeb?.IdNumero ?? conversation.IdNumeroWhatsApp,
                    whatsAppConfig.PhoneNumberId, whatsAppConfig, token);
                whatsAppConfig.PhoneNumberId = runtimeCredential.PhoneNumberId;
                whatsAppConfig.BusinessAccountId = runtimeCredential.WabaId;
                whatsAppConfig.ApiVersion = runtimeCredential.GraphVersion;
                whatsAppConfig.AccessToken = runtimeCredential.AccessToken;

                whatsAppDeliveryProvider = ResolveWhatsAppDeliveryProviderForNumero(whatsAppConfig, numeroWeb);

                var isWhatsAppWebDelivery = string.Equals(whatsAppDeliveryProvider, ConversacionWhatsAppProviders.WhatsAppWeb, StringComparison.OrdinalIgnoreCase);
                var windowActive = isWhatsAppWebDelivery || await IsWhatsAppWindowActiveAsync(request.IdConversacion, token);
                if (!windowActive)
                    throw new InvalidOperationException("La ventana de WhatsApp estÃ¡ vencida. Para retomar la conversaciÃ³n tenÃ©s que enviar una plantilla aprobada.");

                // Si no hay sesión Web activa NI configuración de API válida para el número resuelto,
                // antes esto quedaba en silencio: el mensaje se guardaba como "PENDIENTE_CONFIG" y el
                // método devolvía éxito igual (el técnico veía "Mensaje enviado correctamente" aunque
                // WhatsApp nunca lo recibiera). Ahora se corta acá con un error visible.
                if (!isWhatsAppWebDelivery && whatsAppConfig?.IsConfiguredForSend != true)
                {
                    throw new InvalidOperationException(
                        "No se pudo enviar: el número de WhatsApp elegido no tiene una sesión Web conectada ni un Phone Number ID/Access Token de API configurados.");
                }

                // Llegado acá, isWhatsAppWebDelivery es true o whatsAppConfig.IsConfiguredForSend es
                // true (si no, ya se tiró la excepción de arriba) -- siempre hay un camino de envío.
                initialState = "PENDIENTE";
            }
            else if (isInstagram)
            {
                if (string.IsNullOrWhiteSpace(conversation.IdentificadorExternoContacto))
                    throw new InvalidOperationException("La conversaciÃ³n de Instagram no tiene identificado al destinatario.");

                instagramConfig = await conversacionesConfigService.GetInstagramConfigAsync(token);
                if (!instagramConfig.IsConfiguredForSend)
                    throw new InvalidOperationException("Falta configurar el token o el ID de cuenta de Instagram.");
                if (!await IsInstagramWindowActiveAsync(request.IdConversacion, token))
                    throw new InvalidOperationException("La ventana estÃ¡ndar de 24 horas de Instagram estÃ¡ vencida.");

                initialState = "PENDIENTE";
            }
            else if (isFacebook)
            {
                if (string.IsNullOrWhiteSpace(conversation.IdentificadorExternoContacto))
                    throw new InvalidOperationException("La conversaciÃ³n de Facebook no tiene identificado al destinatario.");

                facebookConfig = await conversacionesConfigService.GetFacebookConfigAsync(token);
                if (!facebookConfig.IsConfiguredForSend)
                    throw new InvalidOperationException("Falta configurar el token o el Page ID de Facebook Messenger.");
                if (!await IsInstagramWindowActiveAsync(request.IdConversacion, token))
                    throw new InvalidOperationException("La ventana estÃ¡ndar de 24 horas de Messenger estÃ¡ vencida.");

                initialState = "PENDIENTE";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(conversation.IdentificadorExternoConversacion))
                    throw new InvalidOperationException("La conversaciÃ³n de Mercado Libre no tiene identificada la pregunta a responder.");

                mercadoLibreConfig = await conversacionesConfigService.GetMercadoLibreConfigAsync(token);
                if (!mercadoLibreConfig.IsConfiguredForApi)
                    throw new InvalidOperationException("Falta configurar Client ID, Client Secret y Access Token de Mercado Libre.");

                initialState = "PENDIENTE";
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

            // Si nadie tenía la conversación, el primer técnico que responde queda asignado.
            if (!isInternal && !string.IsNullOrWhiteSpace(request.IdTecnicoAutor))
                await AutoAssignIfUnassignedAsync(request.IdConversacion, request.IdTecnicoAutor.Trim(), request.UsuarioAccion, request.SistemaAccion, token);

            string whatsAppMessageId = string.Empty;
            string externalMessageId = string.Empty;
            string finalState = initialState;
            string payload = string.Empty;

            if (isWhatsApp && whatsAppConfig is not null && string.Equals(whatsAppDeliveryProvider, ConversacionWhatsAppProviders.WhatsAppWeb, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var sendResult = await whatsAppWebSessionService.SendTextAsync(numeroWeb?.IdNumero ?? conversation.IdNumeroWhatsApp, conversation.TelefonoWhatsApp, request.Texto.Trim(), request.WhatsAppReplyToMessageId, token);
                    whatsAppMessageId = sendResult.ExternalMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                }
                catch (Exception ex)
                {
                    await UpdateMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);
                    throw new InvalidOperationException("No se pudo enviar el mensaje por WhatsApp Web. Quedó marcado con error en la conversación.", ex);
                }
            }
            else if (!isInternal && whatsAppConfig?.IsConfiguredForSend == true)
            {
                try
                {
                    var sendResult = await SendToWhatsAppAsync(whatsAppConfig, conversation.TelefonoWhatsApp, request.Texto.Trim(), request.WhatsAppReplyToMessageId, token);
                    whatsAppMessageId = sendResult.WhatsAppMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                    await BindConversationToMetaNumberAsync(request.IdConversacion, whatsAppConfig.PhoneNumberId, token);
                }
                catch (Exception ex)
                {
                    await UpdateMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

                    throw new InvalidOperationException("No se pudo enviar el mensaje por WhatsApp. Qued\u00f3 marcado con error en la conversaci\u00f3n.", ex);
                }
            }
            else if (isInstagram && instagramConfig is not null)
            {
                try
                {
                    var sendResult = await SendToInstagramAsync(
                        instagramConfig,
                        conversation.IdentificadorExternoContacto,
                        request.Texto.Trim(),
                        token);
                    externalMessageId = sendResult.WhatsAppMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                }
                catch (Exception ex)
                {
                    await UpdateInstagramMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

                    throw new InvalidOperationException("No se pudo enviar el mensaje por Instagram. QuedÃ³ marcado con error en la conversaciÃ³n.", ex);
                }
            }

            else if (isFacebook && facebookConfig is not null)
            {
                try
                {
                    var sendResult = await SendToFacebookMessengerAsync(
                        facebookConfig,
                        conversation.IdentificadorExternoContacto,
                        request.Texto.Trim(),
                        token);
                    externalMessageId = sendResult.WhatsAppMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                }
                catch (Exception ex)
                {
                    await UpdateInstagramMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

                    throw new InvalidOperationException("No se pudo enviar el mensaje por Facebook Messenger. QuedÃ³ marcado con error en la conversaciÃ³n.", ex);
                }
            }


            else if (isMercadoLibre && mercadoLibreConfig is not null)
            {
                try
                {
                    var sendResult = await SendToMercadoLibreQuestionAsync(
                        mercadoLibreConfig,
                        conversation.IdentificadorExternoConversacion,
                        request.Texto.Trim(),
                        token);
                    externalMessageId = sendResult.WhatsAppMessageId;
                    finalState = sendResult.EstadoEnvio;
                    payload = sendResult.PayloadJson;
                }
                catch (Exception ex)
                {
                    await UpdateInstagramMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                    await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);

                    throw new InvalidOperationException("No se pudo responder la pregunta en Mercado Libre. QuedÃ³ marcada con error en la conversaciÃ³n.", ex);
                }
            }
            if (isInstagram || isFacebook || isMercadoLibre)
                await UpdateInstagramMessageDeliveryAsync(messageId, finalState, externalMessageId, payload, token);
            else
                await UpdateMessageDeliveryAsync(messageId, finalState, whatsAppMessageId, payload, token);
            await RefreshConversationAsync(request.IdConversacion, now, request.Texto.Trim(), token);
            if (isMercadoLibre && string.Equals(finalState, "ENVIADO_MELI", StringComparison.OrdinalIgnoreCase))
                await MarkMercadoLibreQuestionAnsweredAsync(request.IdConversacion, conversation.IdentificadorExternoConversacion, token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SendMessage",
                "CONV_MENSAJES",
                messageId.ToString(CultureInfo.InvariantCulture),
                "Mensaje saliente registrado.",
                new { request.IdConversacion, Canal = conversation.Canal, finalState, whatsAppMessageId, externalMessageId },
                token);

            return new ConversacionMessageResultDto
            {
                IdMensaje = messageId,
                EstadoEnvio = finalState,
                WhatsAppMessageId = whatsAppMessageId,
                MessageIdExterno = externalMessageId
            };
        }, "No se pudo registrar el mensaje saliente.", ct);

    private static readonly HashSet<string> MarcasInternasValidas = new(StringComparer.OrdinalIgnoreCase) { "PENDIENTE", "COMPLETADA" };

    public Task SetMensajeMarcaInternaAsync(long idConversacion, long idMensaje, string? marca, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SetMensajeMarcaInterna", async token =>
        {
            if (idConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
            if (idMensaje <= 0)
                throw new InvalidOperationException("El mensaje es obligatorio.");

            var normalizada = (marca ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizada.Length > 0 && !MarcasInternasValidas.Contains(normalizada))
                throw new InvalidOperationException("Marca interna invÃ¡lida.");

            const string sql = """
                UPDATE dbo.CONV_MENSAJES
                SET MarcaInterna = @Marca
                WHERE IdMensaje = @IdMensaje AND IdConversacion = @IdConversacion;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            cmd.Parameters.AddWithValue("@Marca", normalizada.Length == 0 ? DBNull.Value : normalizada);
            await cmd.ExecuteNonQueryAsync(token);

            return true;
        }, "No se pudo actualizar la marca interna del mensaje.", ct);

    public Task<ConversacionMessageResultDto> SendReactionAsync(ConversacionReaccionRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SendReaction", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
            if (request.IdMensaje <= 0)
                throw new InvalidOperationException("El mensaje a reaccionar es obligatorio.");

            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);

            var emoji = NormalizeReactionEmoji(request.Emoji);
            if (string.IsNullOrWhiteSpace(emoji))
                throw new InvalidOperationException("ElegÃ­ una reacciÃ³n vÃ¡lida.");

            var conversationTask = RequireConversationAsync(request.IdConversacion, token);
            var targetTask = RequireMessageForReactionAsync(request.IdConversacion, request.IdMensaje, token);
            var configTask = conversacionesConfigService.GetWhatsAppConfigAsync(token);
            await Task.WhenAll(conversationTask, targetTask, configTask);

            var conversation = await conversationTask;
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Las reacciones solo se envÃ­an por WhatsApp.");

            var target = await targetTask;
            if (string.IsNullOrWhiteSpace(target.WhatsAppMessageId))
                throw new InvalidOperationException("Este mensaje todavÃ­a no tiene ID de WhatsApp para reaccionar.");

            var config = await configTask;
            if (!string.IsNullOrWhiteSpace(conversation.PhoneNumberId))
                config.PhoneNumberId = conversation.PhoneNumberId;
            EnsureWhatsAppMetaProvider(config, "enviar reacciones");
            var now = BusinessNow();
            var text = emoji;

            var messageId = await InsertMessageAsync(new PendingMessageInsert
            {
                ConversationId = request.IdConversacion,
                Phone = conversation.TelefonoWhatsApp,
                ReplyToMessageId = target.WhatsAppMessageId,
                MessageType = "REACTION",
                Direction = "SALIENTE",
                EstadoEnvio = "PENDIENTE",
                Text = text,
                PayloadJson = string.Empty,
                FechaHora = now,
                UsuarioAutor = request.UsuarioAccion,
                SistemaAutor = request.SistemaAccion,
                IdTecnicoAutor = request.IdTecnicoAutor
            }, token);

            WhatsAppSendResult sendResult;
            try
            {
                sendResult = await SendReactionToWhatsAppAsync(config, conversation.TelefonoWhatsApp, target.WhatsAppMessageId, emoji, token);
            }
            catch (Exception ex)
            {
                await UpdateMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                throw new InvalidOperationException("No se pudo enviar la reacciÃ³n por WhatsApp.", ex);
            }

            await UpdateMessageDeliveryAsync(messageId, sendResult.EstadoEnvio, sendResult.WhatsAppMessageId, sendResult.PayloadJson, token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SendReaction",
                "CONV_MENSAJES",
                messageId.ToString(CultureInfo.InvariantCulture),
                "ReacciÃ³n de WhatsApp enviada.",
                new { request.IdConversacion, request.IdMensaje, emoji, sendResult.EstadoEnvio },
                token);

            return new ConversacionMessageResultDto
            {
                IdMensaje = messageId,
                EstadoEnvio = sendResult.EstadoEnvio,
                WhatsAppMessageId = sendResult.WhatsAppMessageId
            };
        }, "No se pudo enviar la reacciÃ³n por WhatsApp.", ct);

    public Task SetConversationWhatsAppNumeroAsync(ConversacionWhatsAppNumeroRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SetConversationWhatsAppNumero", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new ArgumentException("La conversaci\u00f3n indicada no es v\u00e1lida.", nameof(request));
            if (request.IdNumeroWhatsApp <= 0)
                throw new ArgumentException("La cuenta de WhatsApp indicada no es v\u00e1lida.", nameof(request));

            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            await conversacionesAuthorizationService.EnsureCanUseWhatsAppNumeroAsync(request.IdNumeroWhatsApp, token);
            await SetConversationWhatsAppNumeroCoreAsync(
                request.IdConversacion,
                request.IdNumeroWhatsApp,
                request.UsuarioAccion,
                request.SistemaAccion,
                token);
        }, "No se pudo cambiar la cuenta de WhatsApp de la conversaci\u00f3n.", ct);

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

    public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesForConversationAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetTemplatesForConversation", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(idConversacion, token);
            var conversation = await RequireConversationAsync(idConversacion, token);
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            var runtime = await whatsAppRuntimeCredentialResolver.ResolveAsync(sessionService.GetActiveSession()?.BaseId ?? 0,
                conversation.IdNumeroWhatsApp, conversation.PhoneNumberId, config, token);
            if (runtime.Origin == WhatsAppRuntimeCredentialOrigin.Legacy)
                return await GetTemplatesAsync(new ConversacionPlantillaFilters { EstadoMeta = "APPROVED" }, token);
            var reference = runtime.CredentialReference
                ?? throw new InvalidOperationException("La referencia segura de Meta no está disponible.");
            var templates = await metaWhatsAppManagementClient.DiscoverTemplatesAsync(runtime.WabaId, reference, token);
            return templates.Where(static x => string.Equals(x.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                .Select(MapRemoteTemplate).OrderBy(static x => x.NombreVisible, StringComparer.OrdinalIgnoreCase).ToArray();
        }, "No se pudieron cargar las plantillas aprobadas de este WhatsApp.", ct);

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

    public Task ArchiveTemplateAsync(long idPlantilla, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "ArchiveTemplate", async token =>
        {
            if (idPlantilla <= 0)
                throw new InvalidOperationException("La plantilla es obligatoria.");

            const string sql = """
                UPDATE dbo.CONV_PLANTILLAS
                SET Activa = 0,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdPlantilla = @IdPlantilla;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdPlantilla", idPlantilla);
            var affected = await cmd.ExecuteNonQueryAsync(token);
            if (affected == 0)
                throw new InvalidOperationException("La plantilla indicada no existe.");

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "ArchiveTemplate",
                "CONV_PLANTILLAS",
                idPlantilla.ToString(CultureInfo.InvariantCulture),
                "Plantilla de WhatsApp archivada.",
                new { IdPlantilla = idPlantilla },
                token);

            return true;
        }, "No se pudo archivar la plantilla de WhatsApp.", ct);

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
                "Plantilla enviada a aprobaciÃ³n de Meta.",
                new { template.NombreMeta, submitResult.EstadoMeta },
                token);

            return true;
        }, "No se pudo enviar la plantilla a aprobaciÃ³n de Meta.", ct);

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
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
            if (request.IdPlantilla <= 0)
                throw new InvalidOperationException("La plantilla es obligatoria.");

            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Las plantillas solo se envÃ­an por WhatsApp.");
            if (string.IsNullOrWhiteSpace(conversation.TelefonoWhatsApp))
                throw new InvalidOperationException("La conversaciÃ³n no tiene telÃ©fono WhatsApp.");

            ConversacionPlantillaDto template;
            if (request.EsMetaRemota)
            {
                template = (await GetTemplatesForConversationAsync(request.IdConversacion, token)).SingleOrDefault(x => x.EsMetaRemota
                    && string.Equals(x.NombreMeta, request.NombreMeta.Trim(), StringComparison.Ordinal)
                    && string.Equals(x.Idioma, request.Idioma.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("La plantilla ya no está aprobada para la WABA de este número.");
            }
            else
            {
                template = await GetTemplateAsync(request.IdPlantilla, token)
                    ?? throw new InvalidOperationException("La plantilla indicada no existe.");
            }

            var values = NormalizeTemplateValues(request.ValoresVariables);
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(token);
            if (!string.IsNullOrWhiteSpace(conversation.PhoneNumberId))
                config.PhoneNumberId = conversation.PhoneNumberId;
            var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync(
                sessionService.GetActiveSession()?.BaseId ?? 0,
                conversation.IdNumeroWhatsApp,
                config.PhoneNumberId,
                config,
                token);
            config.PhoneNumberId = runtimeCredential.PhoneNumberId;
            config.BusinessAccountId = runtimeCredential.WabaId;
            config.ApiVersion = runtimeCredential.GraphVersion;
            config.AccessToken = runtimeCredential.AccessToken;
            EnsureWhatsAppMetaProvider(config, "enviar plantillas");
            if (!template.EsMetaRemota && !string.Equals(template.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase))
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

            WhatsAppSendResult sendResult;
            try
            {
                sendResult = await SendTemplateToWhatsAppAsync(config, conversation.TelefonoWhatsApp, template, values, token);
            }
            catch (Exception ex)
            {
                await UpdateMessageDeliveryAsync(messageId, "ERROR_ENVIO", string.Empty, BuildDeliveryErrorPayload(ex), token);
                await RefreshConversationAsync(request.IdConversacion, now, previewText, token);

                throw;
            }

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
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");

            var conversation = await GetConversationAsync(idConversacion, token)
                ?? throw new InvalidOperationException("La conversaciÃ³n indicada no existe.");

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
                observations.Add("La conversaciÃ³n no tiene cliente vinculado; no se pudo calcular deuda automÃ¡tica.");
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
        }, "No se pudieron preparar las variables automÃ¡ticas.", ct);

    public Task<long> AddInternalNoteAsync(ConversacionNotaInternaRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "AddInternalNote", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.Texto))
                throw new InvalidOperationException("La nota interna no puede estar vacÃ­a.");

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
                "Nota interna agregada a la conversaciÃ³n.",
                new { request.IdConversacion },
                token);

            return messageId;
        }, "No se pudo agregar la nota interna.", ct);

    public Task<long> AddInternalEventAsync(ConversacionEventoInternoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "AddInternalEvent", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
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
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");

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
                "AsignaciÃ³n de conversaciÃ³n actualizada.",
                new { IdTecnico = technicianId },
                token);

            return true;
        }, "No se pudo asignar la conversaciÃ³n.", ct);
    }

    public async Task ChangeStatusAsync(ConversacionEstadoRequest request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "ChangeStatus", async token =>
        {
            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
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
                var eventText = !string.IsNullOrWhiteSpace(request.Observaciones)
                    ? request.Observaciones.Trim()
                    : isClosed && !wasClosed
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
                "Estado de conversaciÃ³n actualizado.",
                new { CodigoEstado = state, request.Observaciones, request.UsuarioAccion, request.SistemaAccion },
                token);

            return true;
        }, "No se pudo cambiar el estado de la conversaciÃ³n.", ct);
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
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");

            var normalizedUser = NormalizePinUser(usuario);
            if (string.IsNullOrWhiteSpace(normalizedUser))
                throw new InvalidOperationException("No se pudo identificar el usuario actual para marcar la conversaciÃ³n como leÃ­da.");

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
        }, "No se pudo marcar la conversaciÃ³n como leÃ­da.", ct);

    public Task<ConversacionWebhookResultDto> RegisterIncomingWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RegisterIncomingWebhook", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var payloadJson = string.IsNullOrWhiteSpace(request.RawPayload)
                ? request.Payload.RootElement.GetRawText()
                : request.RawPayload;
            var headerJson = JsonSerializer.Serialize(request.Headers);

            var parsedMessages = ParseIncomingMessages(request.Payload.RootElement);
            var parsedStatuses = ParseIncomingStatuses(request.Payload.RootElement);
            var currentBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
            await whatsAppWebhookTenantGuard.ValidateAsync(currentBaseId, ExtractWhatsAppPhoneNumberIds(request.Payload.RootElement), token);
            var webhookLogId = await InsertWebhookLogAsync("META_WHATSAPP", "Webhook", payloadJson, headerJson, token);
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
                var messageId = await GetExistingMessageIdByWhatsAppIdAsync(incoming.WhatsAppMessageId, token);
                var isNewMessage = messageId <= 0;
                if (isNewMessage)
                {
                    messageId = await InsertMessageAsync(new PendingMessageInsert
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
                }

                if (incoming.Attachments.Count > 0 && whatsAppConfig is not null)
                {
                    var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync(
                        currentBaseId, null, incoming.PhoneNumberId, whatsAppConfig, token);
                    whatsAppConfig.PhoneNumberId = runtimeCredential.PhoneNumberId;
                    whatsAppConfig.BusinessAccountId = runtimeCredential.WabaId;
                    whatsAppConfig.ApiVersion = runtimeCredential.GraphVersion;
                    whatsAppConfig.AccessToken = runtimeCredential.AccessToken;
                    await StoreIncomingAttachmentsAsync(conversationId, messageId, incoming, whatsAppConfig, token);
                }

                if (string.Equals(incoming.MessageType, "REACTION", StringComparison.OrdinalIgnoreCase))
                {
                    await RefreshConversationAsync(
                        conversationId,
                        NormalizeIncomingTimestamp(incoming.Timestamp),
                        BuildReactionSummary(incoming.Text, incoming: true),
                        token,
                        reopenIfClosed: false);
                }
                else
                {
                    await RefreshConversationAsync(conversationId, NormalizeIncomingTimestamp(incoming.Timestamp), incoming.Text, token, reopenIfClosed: true);
                    if (isNewMessage)
                    {
                        await NotifyIncomingMessageAsync(conversationId, messageId, token);
                        // Las respuestas automáticas (reglas/bienvenida/fuera de horario/bot) se corren
                        // con un token propio, no el de la request del webhook: si quien llama (Meta u
                        // otro proveedor) corta la conexión antes de que el bot termine -- AlfaKnowledge +
                        // OpenAI + el envío real pueden superar fácil los ~20s que tolera el webhook --,
                        // "token" se cancela y aborta todo en silencio a mitad de camino: el mensaje queda
                        // insertado en PENDIENTE, sin error, sin traza, sin envío. Visto en producción.
                        if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
                        {
                            await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
                        }
                    }
                }
                processed++;
            }

            await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "RegisterIncomingWebhook",
                "CONV_WEBHOOK_LOG",
                webhookLogId.ToString(CultureInfo.InvariantCulture),
                "Webhook de WhatsApp procesado.",
                new
                {
                    Mensajes = parsedMessages.Count,
                    Procesados = processed,
                    Entrantes = parsedMessages
                        .Select(x => new
                        {
                            x.Phone,
                            x.PhoneNumberId,
                            x.DisplayPhoneNumber,
                            x.WhatsAppMessageId,
                            x.MessageType
                        })
                        .ToList()
                },
                token);

            return new ConversacionWebhookResultDto
            {
                IdWebhookLog = webhookLogId,
                MensajesDetectados = parsedMessages.Count + parsedStatuses.Count,
                MensajesProcesados = processed
            };
        }, "No se pudo procesar el webhook de WhatsApp.", ct);

    /// <summary>
    /// Baileys entrega Status de WhatsApp (remoteJid "status@broadcast"), canales/newsletters y
    /// mensajes de protocolo (revoke, cambios de ephemeral, edits) por el mismo evento
    /// messages.upsert que los mensajes reales. Se identifican por el remoteJid/estructura del
    /// mensaje original de Baileys (rawJson), no por el teléfono/nombre que el worker ya normalizó
    /// -- para un Status, ese teléfono es el de quien lo publicó, no un remitente real de chat.
    /// </summary>
    private static bool IsWhatsAppWebNonConversationalEvent(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var remoteJid = doc.RootElement.TryGetProperty("key", out var keyElement)
                && keyElement.TryGetProperty("remoteJid", out var remoteJidElement)
                    ? remoteJidElement.GetString() ?? string.Empty
                    : string.Empty;

            if (remoteJid.EndsWith("@broadcast", StringComparison.OrdinalIgnoreCase)
                || remoteJid.EndsWith("@newsletter", StringComparison.OrdinalIgnoreCase))
                return true;

            // Sin objeto "message" no hay contenido real que mostrar -- caso confirmado:
            // messageStubType 2 ("Message absent from node", el cifrado nunca llegó). Un worker
            // viejo (sin el filtro de worker.mjs) puede seguir escribiendo estos al inbox hasta
            // que la sesión se reconecte; esta es la red de seguridad para esos casos.
            if (!doc.RootElement.TryGetProperty("message", out var messageElement))
                return true;

            return messageElement.TryGetProperty("protocolMessage", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task RegisterIncomingWhatsAppWebMessageAsync(ConversacionWhatsAppWebIncomingMessageDto request, CancellationToken ct = default)
    {
        await ExecuteLoggedAsync("Conversaciones", "RegisterIncomingWhatsAppWebMessage", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            // Red de seguridad además del filtro en worker.mjs: una sesión que ya estaba corriendo
            // cuando se desplegó ese fix sigue viva con el worker.mjs viejo hasta que se desvincule y
            // reconecte, así que puede seguir escribiendo archivos de inbox de Status/broadcast hasta
            // ese momento. Se corta acá antes de tocar contacto/conversación/mensaje/contadores.
            if (IsWhatsAppWebNonConversationalEvent(request.RawJson))
            {
                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "WhatsAppWebEventIgnored",
                    "CONV_WHATSAPP_NUMEROS",
                    request.InstanceName ?? string.Empty,
                    "Evento de WhatsApp Web ignorado: no es un mensaje de conversación (status/broadcast/newsletter/protocolo).",
                    new { request.InstanceName },
                    token);
                return;
            }

            var payloadJson = string.IsNullOrWhiteSpace(request.RawJson)
                ? JsonSerializer.Serialize(request)
                : request.RawJson;

            var webhookLogId = await InsertWebhookLogAsync("WHATSAPP_WEB", "InboxFile", payloadJson, string.Empty, token);
            try
            {
                var incoming = new IncomingWhatsAppMessage
                {
                    Phone = request.Phone?.Trim() ?? string.Empty,
                    PhoneNumberId = string.IsNullOrWhiteSpace(request.PhoneNumberId)
                        ? request.InstanceName?.Trim() ?? string.Empty
                        : request.PhoneNumberId.Trim(),
                    DisplayPhoneNumber = string.Empty,
                    ContactName = request.ContactName?.Trim() ?? string.Empty,
                    MessageType = string.IsNullOrWhiteSpace(request.MessageType) ? "TEXT" : request.MessageType.Trim().ToUpperInvariant(),
                    WhatsAppMessageId = request.MessageId?.Trim() ?? string.Empty,
                    WhatsAppReplyToMessageId = request.ReplyToMessageId?.Trim() ?? string.Empty,
                    Timestamp = request.TimestampUtc == default ? BusinessNow() : request.TimestampUtc,
                    Text = request.Text?.Trim() ?? string.Empty,
                    RawJson = payloadJson
                };

                var conversationId = await EnsureConversationAsync(incoming, token);
                var messageId = await GetExistingMessageIdByWhatsAppIdAsync(incoming.WhatsAppMessageId, token);
                var isNewMessage = messageId <= 0;
                if (isNewMessage)
                {
                    messageId = await InsertMessageAsync(new PendingMessageInsert
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
                        SistemaAutor = "WHATSAPP_WEB",
                        IdTecnicoAutor = string.Empty,
                        WhatsAppMessageId = incoming.WhatsAppMessageId,
                        ReplyToMessageId = incoming.WhatsAppReplyToMessageId
                    }, token);
                }

                if (string.Equals(incoming.MessageType, "REACTION", StringComparison.OrdinalIgnoreCase))
                {
                    await RefreshConversationAsync(
                        conversationId,
                        NormalizeIncomingTimestamp(incoming.Timestamp),
                        BuildReactionSummary(incoming.Text, incoming: true),
                        token,
                        reopenIfClosed: false);
                }
                else
                {
                    await RefreshConversationAsync(conversationId, NormalizeIncomingTimestamp(incoming.Timestamp), incoming.Text, token, reopenIfClosed: true);
                    if (isNewMessage)
                    {
                        await NotifyIncomingMessageAsync(conversationId, messageId, token);
                        // Las respuestas automáticas (reglas/bienvenida/fuera de horario/bot) se corren
                        // con un token propio, no el de la request del webhook: si quien llama (Meta u
                        // otro proveedor) corta la conexión antes de que el bot termine -- AlfaKnowledge +
                        // OpenAI + el envío real pueden superar fácil los ~20s que tolera el webhook --,
                        // "token" se cancela y aborta todo en silencio a mitad de camino: el mensaje queda
                        // insertado en PENDIENTE, sin error, sin traza, sin envío. Visto en producción.
                        if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
                        {
                            await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
                        }
                    }
                }

                await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);
            }
            catch (Exception ex)
            {
                await UpdateWebhookLogAsync(webhookLogId, false, ex.Message, token);
                throw;
            }
        }, "No se pudo registrar el mensaje entrante de WhatsApp Web.", ct);
    }

    public Task<int> ProcessWhatsAppWebInboxAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "ProcessWhatsAppWebInbox", async token =>
        {
            var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
            if (activeBaseId <= 0)
                return 0;

            // AppContext.BaseDirectory, no ContentRootPath: mismo motivo que
            // WhatsAppWebSessionService.EnsureSessionDirectory()/GetWorkerDirectory() -- el worker
            // real escribe su carpeta de sesión (y su inbox) ahí, invariante al cwd con el que se
            // lanzó el proceso. Si este servicio usara un ContentRootPath distinto al que tenía el
            // proceso que arrancó el worker, dejaría de encontrar mensajes entrantes reales sin
            // ningún error visible -- simplemente Directory.Exists(sessionsRoot) da false más abajo.
            var sessionsRoot = Path.Combine(
                AppContext.BaseDirectory,
                "App_Data",
                "whatsapp-web",
                activeBaseId.ToString(CultureInfo.InvariantCulture));

            if (!Directory.Exists(sessionsRoot))
                return 0;

            var inboxFiles = Directory
                .EnumerateFiles(sessionsRoot, "*.json", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "inbox", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var processed = 0;
            foreach (var inboxFile in inboxFiles)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var json = await File.ReadAllTextAsync(inboxFile, token);
                    var incoming = JsonSerializer.Deserialize<ConversacionWhatsAppWebIncomingMessageDto>(json, WhatsAppWebInboxJsonOptions);
                    if (incoming is null)
                    {
                        throw new InvalidOperationException("El archivo de inbox no contiene un mensaje válido.");
                    }

                    var inboxDir = Path.GetDirectoryName(inboxFile);
                    var instanceDir = string.IsNullOrWhiteSpace(inboxDir)
                        ? null
                        : Directory.GetParent(inboxDir);
                    var instanceName = instanceDir?.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(instanceName))
                    {
                        incoming.InstanceName = instanceName;
                        var numero = await conversacionesConfigService.GetWhatsAppNumeroByInstanceNameAsync(instanceName, token);
                        if (numero is null)
                        {
                            throw new InvalidOperationException($"No existe un número de WhatsApp configurado para la instancia '{instanceName}'.");
                        }

                        incoming.PhoneNumberId = numero.PhoneNumberId;
                    }

                    incoming.RawJson = string.IsNullOrWhiteSpace(incoming.RawJson) ? json : incoming.RawJson;
                    await RegisterIncomingWhatsAppWebMessageAsync(incoming, token);
                    File.Delete(inboxFile);
                    processed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _appEvents.LogErrorAsync(
                        "Conversaciones",
                        "ProcessWhatsAppWebInbox",
                        ex,
                        "No se pudo procesar un archivo del inbox de WhatsApp Web.",
                        new { InboxFile = inboxFile },
                        AppEventSeverity.Warning,
                        token);
                }
            }

            return processed;
        }, "No se pudo procesar la bandeja de entrada de WhatsApp Web.", ct);

    private static readonly Dictionary<string, int> WhatsAppWebDeliveryRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PENDIENTE"] = 0,
        ["ENVIADO"] = 1,
        ["ENTREGADO"] = 2,
        ["LEIDO"] = 3
    };

    private static string MapWhatsAppWebAckStatus(string status) => status.Trim().ToUpperInvariant() switch
    {
        "DELIVERED" => "ENTREGADO",
        "READ" => "LEIDO",
        "ERROR" => "ERROR_ENVIO",
        _ => string.Empty
    };

    private async Task<(long IdMensaje, string EstadoEnvio)> GetOutgoingWhatsAppWebMessageAsync(string whatsAppMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(whatsAppMessageId))
            return (0, string.Empty);

        const string sql = """
            SELECT TOP (1) IdMensaje, ISNULL(EstadoEnvio, '')
            FROM dbo.CONV_MENSAJES
            WHERE WhatsAppMessageId = @WhatsAppMessageId
              AND Direction = 'SALIENTE';
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", whatsAppMessageId.Trim());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
            return (0, string.Empty);

        return (rd.GetInt64(0), rd.GetString(1));
    }

    /// <summary>
    /// Acks/receipts de mensajes SALIENTES (messages.update de Baileys, ver worker.mjs). A
    /// diferencia de ProcessWhatsAppWebInboxAsync, esto nunca crea contacto/conversación/mensaje ni
    /// toca contadores/automatizaciones -- solo actualiza el EstadoEnvio de un mensaje que AlfaCore
    /// ya envió, identificado por WhatsAppMessageId. Guard de orden simple (WhatsAppWebDeliveryRank)
    /// para no regresar Leído a Entregado si un ack tardío llega desordenado; un ERROR post-send
    /// siempre se aplica.
    /// </summary>
    public Task<int> ProcessWhatsAppWebAcksAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "ProcessWhatsAppWebAcks", async token =>
        {
            var activeBaseId = sessionService.GetActiveSession()?.BaseId ?? 0;
            if (activeBaseId <= 0)
                return 0;

            var sessionsRoot = Path.Combine(
                AppContext.BaseDirectory,
                "App_Data",
                "whatsapp-web",
                activeBaseId.ToString(CultureInfo.InvariantCulture));

            if (!Directory.Exists(sessionsRoot))
                return 0;

            var ackFiles = Directory
                .EnumerateFiles(sessionsRoot, "*.json", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "acks", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var processed = 0;
            foreach (var ackFile in ackFiles)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var json = await File.ReadAllTextAsync(ackFile, token);
                    var ack = JsonSerializer.Deserialize<ConversacionWhatsAppWebAckDto>(json, WhatsAppWebInboxJsonOptions);
                    var mappedState = MapWhatsAppWebAckStatus(ack?.Status ?? string.Empty);

                    if (ack is not null && !string.IsNullOrWhiteSpace(ack.ExternalMessageId) && !string.IsNullOrEmpty(mappedState))
                    {
                        var (idMensaje, currentState) = await GetOutgoingWhatsAppWebMessageAsync(ack.ExternalMessageId, token);
                        var currentRank = WhatsAppWebDeliveryRank.GetValueOrDefault(currentState.Trim().ToUpperInvariant(), -1);
                        var newRank = WhatsAppWebDeliveryRank.GetValueOrDefault(mappedState, int.MaxValue);

                        if (idMensaje > 0 && newRank >= currentRank)
                        {
                            await UpdateMessageDeliveryAsync(idMensaje, mappedState, string.Empty, string.Empty, token);
                            processed++;
                        }
                    }

                    File.Delete(ackFile);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _appEvents.LogErrorAsync(
                        "Conversaciones",
                        "ProcessWhatsAppWebAcks",
                        ex,
                        "No se pudo procesar un ack de WhatsApp Web.",
                        new { AckFile = ackFile },
                        AppEventSeverity.Warning,
                        token);
                }
            }

            return processed;
        }, "No se pudo procesar los acks de WhatsApp Web.", ct);

    public Task<ConversacionWebhookResultDto> RegisterIncomingInstagramWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RegisterIncomingInstagramWebhook", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var payloadJson = string.IsNullOrWhiteSpace(request.RawPayload)
                ? request.Payload.RootElement.GetRawText()
                : request.RawPayload;
            var headerJson = JsonSerializer.Serialize(request.Headers);
            var webhookLogId = await InsertWebhookLogAsync("META_INSTAGRAM", "messages", payloadJson, headerJson, token);

            try
            {
                var messages = ParseIncomingInstagramMessages(request.Payload.RootElement);
                var config = messages.Count > 0
                    ? await conversacionesConfigService.GetInstagramConfigAsync(token)
                    : null;
                var processed = 0;

                foreach (var incoming in messages)
                {
                    if (string.IsNullOrWhiteSpace(incoming.SenderId)
                        || string.IsNullOrWhiteSpace(incoming.MessageId)
                        || incoming.IsEcho)
                        continue;

                    var profile = config is null
                        ? InstagramProfile.Empty
                        : await TryGetInstagramProfileAsync(config, incoming.SenderId, token);
                    var conversationId = await EnsureInstagramConversationAsync(incoming, profile, token);
                    var storedMessage = await InsertInstagramMessageIfMissingAsync(conversationId, incoming, token);

                    await RefreshConversationAsync(conversationId, incoming.Timestamp, incoming.Text, token, reopenIfClosed: true);
                    if (storedMessage.Created)
                    {
                        await NotifyIncomingMessageAsync(conversationId, storedMessage.MessageId, token);
                        // Las respuestas automáticas (reglas/bienvenida/fuera de horario/bot) se corren
                        // con un token propio, no el de la request del webhook: si quien llama (Meta u
                        // otro proveedor) corta la conexión antes de que el bot termine -- AlfaKnowledge +
                        // OpenAI + el envío real pueden superar fácil los ~20s que tolera el webhook --,
                        // "token" se cancela y aborta todo en silencio a mitad de camino: el mensaje queda
                        // insertado en PENDIENTE, sin error, sin traza, sin envío. Visto en producción.
                        if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
                        {
                            await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
                        }
                    }
                    processed++;
                }

                await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);
                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "RegisterIncomingInstagramWebhook",
                    "CONV_WEBHOOK_LOG",
                    webhookLogId.ToString(CultureInfo.InvariantCulture),
                    "Webhook de Instagram procesado.",
                    new { Mensajes = messages.Count, Procesados = processed },
                    token);

                return new ConversacionWebhookResultDto
                {
                    IdWebhookLog = webhookLogId,
                    MensajesDetectados = messages.Count,
                    MensajesProcesados = processed
                };
            }
            catch (Exception ex)
            {
                await UpdateWebhookLogAsync(webhookLogId, false, ex.Message, token);
                throw;
            }
        }, "No se pudo procesar el webhook de Instagram.", ct);

    public Task<ConversacionWebhookResultDto> RegisterIncomingFacebookWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RegisterIncomingFacebookWebhook", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var payloadJson = string.IsNullOrWhiteSpace(request.RawPayload)
                ? request.Payload.RootElement.GetRawText()
                : request.RawPayload;
            var headerJson = JsonSerializer.Serialize(request.Headers);
            var webhookLogId = await InsertWebhookLogAsync("META_FACEBOOK", "messages", payloadJson, headerJson, token);

            try
            {
                var messages = ParseIncomingFacebookMessages(request.Payload.RootElement);
                var config = messages.Count > 0
                    ? await conversacionesConfigService.GetFacebookConfigAsync(token)
                    : null;
                var processed = 0;

                foreach (var incoming in messages)
                {
                    if (string.IsNullOrWhiteSpace(incoming.SenderId)
                        || string.IsNullOrWhiteSpace(incoming.MessageId)
                        || incoming.IsEcho)
                        continue;

                    var profile = config is null
                        ? FacebookProfile.Empty
                        : await TryGetFacebookProfileAsync(config, incoming, token);
                    var conversationId = await EnsureFacebookConversationAsync(incoming, profile, token);
                    var storedMessage = await InsertInstagramMessageIfMissingAsync(conversationId, incoming, token);

                    await RefreshConversationAsync(conversationId, incoming.Timestamp, incoming.Text, token, reopenIfClosed: true);
                    if (storedMessage.Created)
                    {
                        await NotifyIncomingMessageAsync(conversationId, storedMessage.MessageId, token);
                        // Las respuestas automáticas (reglas/bienvenida/fuera de horario/bot) se corren
                        // con un token propio, no el de la request del webhook: si quien llama (Meta u
                        // otro proveedor) corta la conexión antes de que el bot termine -- AlfaKnowledge +
                        // OpenAI + el envío real pueden superar fácil los ~20s que tolera el webhook --,
                        // "token" se cancela y aborta todo en silencio a mitad de camino: el mensaje queda
                        // insertado en PENDIENTE, sin error, sin traza, sin envío. Visto en producción.
                        if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
                        {
                            await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
                        }
                    }
                    processed++;
                }

                await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);
                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "RegisterIncomingFacebookWebhook",
                    "CONV_WEBHOOK_LOG",
                    webhookLogId.ToString(CultureInfo.InvariantCulture),
                    "Webhook de Facebook Messenger procesado.",
                    new { Mensajes = messages.Count, Procesados = processed },
                    token);

                return new ConversacionWebhookResultDto
                {
                    IdWebhookLog = webhookLogId,
                    MensajesDetectados = messages.Count,
                    MensajesProcesados = processed
                };
            }
            catch (Exception ex)
            {
                await UpdateWebhookLogAsync(webhookLogId, false, ex.Message, token);
                throw;
            }
        }, "No se pudo procesar el webhook de Facebook.", ct);

    public Task<ConversacionWebhookResultDto> RegisterIncomingMercadoLibreWebhookAsync(ConversacionWebhookRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RegisterIncomingMercadoLibreWebhook", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var payloadJson = string.IsNullOrWhiteSpace(request.RawPayload)
                ? request.Payload.RootElement.GetRawText()
                : request.RawPayload;
            var headerJson = JsonSerializer.Serialize(request.Headers);
            var webhookLogId = await InsertWebhookLogAsync("MERCADOLIBRE", "questions", payloadJson, headerJson, token);

            try
            {
                var notifications = ParseMercadoLibreNotifications(request.Payload.RootElement);
                var config = notifications.Count > 0
                    ? await conversacionesConfigService.GetMercadoLibreConfigAsync(token)
                    : null;
                var processed = 0;

                foreach (var notification in notifications)
                {
                    if (!string.Equals(notification.Topic, "questions", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var question = config is null
                        ? MercadoLibreQuestion.FromNotification(notification)
                        : await TryGetMercadoLibreQuestionAsync(config, notification, token);

                    if (string.IsNullOrWhiteSpace(question.QuestionId))
                        continue;

                    var conversationId = await EnsureMercadoLibreConversationAsync(question, token);
                    var incoming = question.ToIncomingMessage();
                    var storedMessage = await InsertInstagramMessageIfMissingAsync(conversationId, incoming, token);

                    await RefreshConversationAsync(conversationId, incoming.Timestamp, incoming.Text, token, reopenIfClosed: true);
                    if (storedMessage.Created)
                    {
                        await NotifyIncomingMessageAsync(conversationId, storedMessage.MessageId, token);
                        // Las respuestas automáticas (reglas/bienvenida/fuera de horario/bot) se corren
                        // con un token propio, no el de la request del webhook: si quien llama (Meta u
                        // otro proveedor) corta la conexión antes de que el bot termine -- AlfaKnowledge +
                        // OpenAI + el envío real pueden superar fácil los ~20s que tolera el webhook --,
                        // "token" se cancela y aborta todo en silencio a mitad de camino: el mensaje queda
                        // insertado en PENDIENTE, sin error, sin traza, sin envío. Visto en producción.
                        if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))
                        {
                            await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);
                            await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);
                        }
                    }
                    processed++;
                }

                await UpdateWebhookLogAsync(webhookLogId, true, string.Empty, token);
                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "RegisterIncomingMercadoLibreWebhook",
                    "CONV_WEBHOOK_LOG",
                    webhookLogId.ToString(CultureInfo.InvariantCulture),
                    "Webhook de Mercado Libre procesado.",
                    new { Notificaciones = notifications.Count, Procesadas = processed },
                    token);

                return new ConversacionWebhookResultDto
                {
                    IdWebhookLog = webhookLogId,
                    MensajesDetectados = notifications.Count,
                    MensajesProcesados = processed
                };
            }
            catch (Exception ex)
            {
                await UpdateWebhookLogAsync(webhookLogId, false, ex.Message, token);
                throw;
            }
        }, "No se pudo procesar el webhook de Mercado Libre.", ct);

    public Task<ConversacionWebhookResultDto> SyncMercadoLibreQuestionsAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "SyncMercadoLibreQuestions", async token =>
        {
            var config = await conversacionesConfigService.GetMercadoLibreConfigAsync(token);
            var diagnostics = new List<string>();
            config = await RefreshMercadoLibreAccessTokenAsync(config, diagnostics, token);
            if (!config.IsConfiguredForApi || string.IsNullOrWhiteSpace(config.SellerId))
                throw new InvalidOperationException("Falta configurar Mercado Libre con Access Token y Seller/User ID.");

            await MarkLocalMercadoLibreAnsweredQuestionsAsync(token);
            var questions = await GetMercadoLibreUnansweredQuestionsAsync(config, diagnostics, token);
            var processed = 0;

            foreach (var question in questions)
            {
                if (string.IsNullOrWhiteSpace(question.QuestionId))
                    continue;

                var conversationId = await EnsureMercadoLibreConversationAsync(question, token);
                var incoming = question.ToIncomingMessage();
                var storedMessage = await InsertInstagramMessageIfMissingAsync(conversationId, incoming, token);

                await RefreshConversationAsync(conversationId, incoming.Timestamp, incoming.Text, token, reopenIfClosed: !question.IsAnswered);
                if (storedMessage.Created && !question.IsAnswered)
                    await NotifyIncomingMessageAsync(conversationId, storedMessage.MessageId, token);
                processed++;
            }

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "SyncMercadoLibreQuestions",
                "CONV_CONVERSACIONES",
                config.SellerId,
                "Preguntas de Mercado Libre sincronizadas.",
                new { Detectadas = questions.Count, Procesadas = processed, Detalle = diagnostics },
                token);

            return new ConversacionWebhookResultDto
            {
                IdWebhookLog = 0,
                MensajesDetectados = questions.Count,
                MensajesProcesados = processed,
                Detalle = string.Join(" | ", diagnostics)
            };
        }, "No se pudieron sincronizar las preguntas de Mercado Libre.", ct);

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

    private async Task<ConversacionMercadoLibreConfigDto> RefreshMercadoLibreAccessTokenAsync(
        ConversacionMercadoLibreConfigDto config,
        List<string> diagnostics,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForAuth || string.IsNullOrWhiteSpace(config.RefreshToken))
            return config;

        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/oauth/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = config.ClientId.Trim(),
            ["client_secret"] = config.ClientSecret.Trim(),
            ["refresh_token"] = config.RefreshToken.Trim()
        });

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mercado Libre devolviÃ³ {(int)response.StatusCode} al renovar el token: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("access_token", out var accessToken))
            config.AccessToken = accessToken.GetString() ?? string.Empty;
        if (root.TryGetProperty("refresh_token", out var refreshToken))
            config.RefreshToken = refreshToken.GetString() ?? config.RefreshToken;
        if (root.TryGetProperty("user_id", out var userId))
            config.SellerId = ConvertJsonElementToString(userId);

        await conversacionesConfigService.SaveMercadoLibreTokensAsync(config, ct);
        diagnostics.Add("Token ML renovado");
        return config;
    }

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
            var defaultWebNumero = await ResolveDefaultWhatsAppWebNumeroForManualConversationAsync(token);
            var idNumeroWhatsApp = defaultWebNumero?.IdNumero;
            if (idNumeroWhatsApp.HasValue)
                await conversacionesAuthorizationService.EnsureCanUseWhatsAppNumeroAsync(idNumeroWhatsApp.Value, token);
            var existing = await FindWhatsAppConversationByPhoneAsync(cn, phone, contact.IdContact, idNumeroWhatsApp, token);
            if (existing is null && idNumeroWhatsApp.HasValue)
            {
                existing = await FindWhatsAppConversationByPhoneAsync(cn, phone, contact.IdContact, idNumeroWhatsApp: null, token);
                if (existing is not null)
                    await UpdateConversationWhatsAppNumeroAsync(existing.IdConversacion, idNumeroWhatsApp.Value, token);
            }
            if (existing is not null)
            {
                if (contact.IdContact.HasValue)
                    await AssociateContactIfMissingAsync(cn, existing.IdConversacion, contact, token);

                await MergeDuplicateWhatsAppConversationsAsync(cn, existing.IdConversacion, phone, contact.IdContact, idNumeroWhatsApp, token);

                await _appEvents.LogAuditAsync(
                    "Conversaciones",
                    "CreateOrGetWhatsAppConversation",
                    "CONV_CONVERSACIONES",
                    existing.IdConversacion.ToString(CultureInfo.InvariantCulture),
                    "ConversaciÃ³n de WhatsApp existente abierta desde bÃºsqueda.",
                    new { TelefonoWhatsApp = phone, ContactoAsociado = contact.IdContact.HasValue, IdNumeroWhatsApp = idNumeroWhatsApp },
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
                    IdNumeroWhatsApp,
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
                    @IdNumeroWhatsApp,
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
            cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", idNumeroWhatsApp.HasValue ? idNumeroWhatsApp.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ResumenInicial", ManualWhatsAppConversationSummary);
            var result = await cmd.ExecuteScalarAsync(token);
            var id = Convert.ToInt64(result, CultureInfo.InvariantCulture);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "CreateOrGetWhatsAppConversation",
                "CONV_CONVERSACIONES",
                id.ToString(CultureInfo.InvariantCulture),
                "ConversaciÃ³n de WhatsApp creada manualmente.",
                new { TelefonoWhatsApp = phone, ContactoAsociado = contact.IdContact.HasValue, contact.ClientCode, IdTecnico = technicianId, IdNumeroWhatsApp = idNumeroWhatsApp },
                token);

            return new ConversacionCrearWhatsAppResultDto
            {
                IdConversacion = id,
                Creada = true,
                ContactoAsociado = contact.IdContact.HasValue,
                TelefonoWhatsApp = phone,
                NombreVisible = displayName
            };
        }, "No se pudo crear la conversaciÃ³n de WhatsApp.", ct);

    public Task<ConversacionAdjuntoDto> UploadAttachmentAsync(ConversacionUploadAdjuntoRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "UploadAttachment", async token =>
        {
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
            if (string.IsNullOrWhiteSpace(request.NombreArchivo))
                throw new InvalidOperationException("El nombre del archivo es obligatorio.");
            if (request.TamanoBytes <= 0)
                throw new InvalidOperationException("El archivo estÃ¡ vacÃ­o.");

            await conversacionesAuthorizationService.EnsureCanAttendConversationAsync(request.IdConversacion, token);
            if (request.IdNumeroWhatsApp is > 0)
                await conversacionesAuthorizationService.EnsureCanUseWhatsAppNumeroAsync(request.IdNumeroWhatsApp.Value, token);

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            var isInternal = string.Equals(conversation.Canal, "INTERNO", StringComparison.OrdinalIgnoreCase);
            var isWhatsApp = string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase);
            if (!isInternal && !isWhatsApp)
                throw new InvalidOperationException($"El canal {conversation.Canal} todavÃ­a no tiene envÃ­o de adjuntos habilitado.");
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
                var idNumeroWhatsAppParaEnvio = request.UsarApiMetaPredeterminada
                    ? null
                    : request.IdNumeroWhatsApp is > 0
                        ? request.IdNumeroWhatsApp
                        : conversation.IdNumeroWhatsApp;
                var numero = idNumeroWhatsAppParaEnvio.HasValue
                    ? await conversacionesConfigService.GetWhatsAppNumeroAsync(idNumeroWhatsAppParaEnvio.Value, token)
                    : null;
                numero = await ResolveWhatsAppWebNumeroForSendAsync(conversation.IdConversacion, numero, token);

                var phoneNumberIdParaEnvio = request.UsarApiMetaPredeterminada
                    ? string.Empty
                    : FirstValidMetaPhoneNumberId(numero?.PhoneNumberId, conversation.PhoneNumberId);
                if (!string.IsNullOrWhiteSpace(phoneNumberIdParaEnvio))
                    whatsAppConfig.PhoneNumberId = phoneNumberIdParaEnvio;

                var deliveryProvider = ResolveWhatsAppDeliveryProviderForNumero(whatsAppConfig, numero);
                EnsureWhatsAppProviderImplemented(deliveryProvider, "enviar adjuntos");
                var windowActive = await IsWhatsAppWindowActiveAsync(request.IdConversacion, token);
                if (!windowActive && !request.PermitirEnvioConVentanaVencida)
                    throw new InvalidOperationException("La ventana de WhatsApp estÃ¡ vencida. Para retomar la conversaciÃ³n tenÃ©s que enviar una plantilla aprobada.");

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
            var archivoContenido = await File.ReadAllBytesAsync(rutaLocal, token);

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
                await BindConversationToMetaNumberAsync(request.IdConversacion, whatsAppConfig.PhoneNumberId, token);
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
                archivoContenido,
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
                TamanoBytes = tamanoBytes,
                ArchivoDisponible = true
            };
        }, "No se pudo guardar el adjunto.", ct);

    public Task<IReadOnlyList<ConversacionAdjuntoDto>> GetConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default)
        => GetConversationAttachmentsInternalAsync(idConversacion, messageIds: null, ct);

    public Task<IReadOnlyList<ConversacionAdjuntoDto>> GetMessageAttachmentsAsync(long idConversacion, IReadOnlyCollection<long> messageIds, CancellationToken ct = default)
        => GetConversationAttachmentsInternalAsync(idConversacion, messageIds, ct);

    private Task<IReadOnlyList<ConversacionAdjuntoDto>> GetConversationAttachmentsInternalAsync(
        long idConversacion,
        IReadOnlyCollection<long>? messageIds,
        CancellationToken ct)
        => ExecuteLoggedAsync("Conversaciones", "GetConversationAttachments", async token =>
        {
            var filteredMessageIds = messageIds?
                .Where(x => x > 0)
                .Distinct()
                .ToArray();
            if (filteredMessageIds is { Length: 0 })
                return (IReadOnlyList<ConversacionAdjuntoDto>)[];

            var items = new List<ConversacionAdjuntoDto>();
            var pathUpdates = new List<(long IdAdjunto, string RutaLocal)>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, token);
            var supportsDurableContent = await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "ArchivoContenido", token);
            var messageFilterSql = filteredMessageIds is { Length: > 0 }
                ? $" AND a.IdMensaje IN ({string.Join(", ", filteredMessageIds.Select((_, index) => $"@MessageId{index}"))})"
                : string.Empty;
            var sql = supportsDurableContent
                ? $$"""
                    SELECT
                        a.IdAdjunto,
                        a.IdMensaje,
                        m.IdConversacion,
                        ISNULL(a.TipoArchivo, ''),
                        ISNULL(a.NombreArchivo, ''),
                        ISNULL(a.MimeType, ''),
                        ISNULL(a.UrlArchivo, ''),
                        ISNULL(a.RutaLocal, ''),
                        ISNULL(a.TamanoBytes, 0),
                        ISNULL(a.PayloadJson, ''),
                        ISNULL(m.PayloadJson, ''),
                        m.FechaHora,
                        CASE WHEN a.ArchivoContenido IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
                        ISNULL(a.AlmacenamientoEstado, '')
                    FROM dbo.CONV_ADJUNTOS a
                    INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = a.IdMensaje
                    WHERE m.IdConversacion = @IdConversacion
                    {{messageFilterSql}}
                    ORDER BY a.IdAdjunto
                    """
                : $$"""
                    SELECT
                        a.IdAdjunto,
                        a.IdMensaje,
                        m.IdConversacion,
                        ISNULL(a.TipoArchivo, ''),
                        ISNULL(a.NombreArchivo, ''),
                        ISNULL(a.MimeType, ''),
                        ISNULL(a.UrlArchivo, ''),
                        ISNULL(a.RutaLocal, ''),
                        ISNULL(a.TamanoBytes, 0),
                        ISNULL(a.PayloadJson, ''),
                        ISNULL(m.PayloadJson, ''),
                        m.FechaHora,
                        CAST(0 AS bit),
                        CAST('' AS nvarchar(20))
                    FROM dbo.CONV_ADJUNTOS a
                    INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = a.IdMensaje
                    WHERE m.IdConversacion = @IdConversacion
                    {{messageFilterSql}}
                    ORDER BY a.IdAdjunto
                    """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            if (filteredMessageIds is { Length: > 0 })
            {
                for (var index = 0; index < filteredMessageIds.Length; index++)
                    cmd.Parameters.AddWithValue($"@MessageId{index}", filteredMessageIds[index]);
            }
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                var record = new AttachmentServeRecord
                {
                    IdAdjunto = rd.GetInt64(0),
                    IdMensaje = rd.GetInt64(1),
                    IdConversacion = rd.GetInt64(2),
                    TipoArchivo = GetString(rd, 3),
                    NombreArchivo = GetString(rd, 4),
                    MimeType = GetString(rd, 5),
                    UrlArchivo = GetString(rd, 6),
                    RutaLocal = GetString(rd, 7),
                    AdjuntoPayloadJson = GetString(rd, 9),
                    MensajePayloadJson = GetString(rd, 10),
                    FechaHora = rd.IsDBNull(11) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(11))
                };

                var rutaLocal = ResolveExistingAttachmentPath(record);
                var hasSqlContent = !rd.IsDBNull(12) && rd.GetBoolean(12);
                var estadoAlmacenamiento = GetString(rd, 13);
                if (!string.IsNullOrWhiteSpace(rutaLocal) && !string.Equals(rutaLocal, record.RutaLocal, StringComparison.OrdinalIgnoreCase))
                    pathUpdates.Add((record.IdAdjunto, rutaLocal));

                items.Add(new ConversacionAdjuntoDto
                {
                    IdAdjunto = record.IdAdjunto,
                    IdMensaje = record.IdMensaje,
                    TipoArchivo = record.TipoArchivo,
                    NombreArchivo = record.NombreArchivo,
                    MimeType = record.MimeType,
                    UrlArchivo = record.UrlArchivo,
                    RutaLocal = rutaLocal,
                    TamanoBytes = rd.IsDBNull(8) ? 0 : rd.GetInt64(8),
                    ArchivoDisponible = (!string.IsNullOrWhiteSpace(rutaLocal) && File.Exists(rutaLocal)) || hasSqlContent,
                    PuedeRecuperarse = IsAttachmentRecoveryCandidate(record, hasSqlContent, rutaLocal, estadoAlmacenamiento),
                    EstadoAlmacenamiento = estadoAlmacenamiento
                });
            }

            await rd.DisposeAsync();

            foreach (var update in pathUpdates)
                await UpdateAttachmentLocalPathAsync(update.IdAdjunto, update.RutaLocal, ConnectionString, token);

            return (IReadOnlyList<ConversacionAdjuntoDto>)items;
        }, "No se pudieron cargar los adjuntos.", ct);

    public Task<ConversacionAdjuntosRecoveryResultDto> RecoverConversationAttachmentsAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "RecoverConversationAttachments", async token =>
        {
            if (idConversacion <= 0)
                return new ConversacionAdjuntosRecoveryResultDto();

            var result = new ConversacionAdjuntosRecoveryResultDto();
            var pendingMedia = await GetPendingMediaHydrationAsync(idConversacion, token);
            if (pendingMedia.Count > 0)
            {
                await HydrateMissingIncomingMediaAsync(pendingMedia, token);
                result.MensajesHidratados = pendingMedia.Count(x => x.Message.TieneAdjuntos);
            }

            var attachmentIds = await GetRecoverableAttachmentIdsAsync(idConversacion, token);
            result.AdjuntosRevisados = attachmentIds.Count;

            foreach (var idAdjunto in attachmentIds)
            {
                var file = await GetAttachmentForServeInternalAsync(
                    idAdjunto,
                    idBase: null,
                    includeDownloadName: false,
                    allowRemoteRecovery: true,
                    ct: token);
                if (file is null)
                    continue;

                var hasLocalFile = !string.IsNullOrWhiteSpace(file.RutaLocal) && File.Exists(file.RutaLocal);
                if (hasLocalFile || file.Contenido.Length > 0)
                    result.AdjuntosDisponibles++;
            }

            result.AdjuntosRecuperados = Math.Max(0, result.AdjuntosDisponibles - (attachmentIds.Count - pendingMedia.Count));
            return result;
        }, "No se pudieron recuperar los adjuntos de la conversaciÃ³n.", ct);

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
                throw new InvalidOperationException("El sticker ya estaba en favoritos o no es un sticker vÃ¡lido.");

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

    public Task<ConversacionAdjuntoServeDto?> GetAttachmentForServeAsync(long idAdjunto, int? idBase = null, bool includeDownloadName = true, CancellationToken ct = default)
        => GetAttachmentForServeInternalAsync(idAdjunto, idBase, includeDownloadName, allowRemoteRecovery: true, ct: ct);

    private Task<ConversacionAdjuntoServeDto?> GetAttachmentForServeInternalAsync(
        long idAdjunto,
        int? idBase,
        bool includeDownloadName,
        bool allowRemoteRecovery,
        CancellationToken ct)
        => ExecuteLoggedAsync("Conversaciones", "GetAttachmentForServe", async token =>
        {
            var connectionString = await ResolveConnectionStringAsync(idBase, token);
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(token);
            await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, token);
            var supportsDurableContent = await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "ArchivoContenido", token);
            var sql = supportsDurableContent
                ? """
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
                        ISNULL(m.PayloadJson, ''),
                        m.FechaHora,
                        a.ArchivoContenido,
                        a.FechaHora_Archivo
                    FROM dbo.CONV_ADJUNTOS a
                    INNER JOIN dbo.CONV_MENSAJES m
                        ON m.IdMensaje = a.IdMensaje
                    WHERE a.IdAdjunto = @IdAdjunto
                    """
                : """
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
                        ISNULL(m.PayloadJson, ''),
                        m.FechaHora,
                        CAST(NULL AS varbinary(max)),
                        CAST(NULL AS datetime)
                    FROM dbo.CONV_ADJUNTOS a
                    INNER JOIN dbo.CONV_MENSAJES m
                        ON m.IdMensaje = a.IdMensaje
                    WHERE a.IdAdjunto = @IdAdjunto
                    """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
            AttachmentServeRecord record;
            byte[] contenido = [];
            DateTime? fechaHoraArchivo = null;
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
                    MensajePayloadJson = GetString(rd, 9),
                    FechaHora = rd.IsDBNull(10) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(10))
                };

                if (!rd.IsDBNull(11))
                    contenido = (byte[])rd.GetValue(11);
                if (!rd.IsDBNull(12))
                    fechaHoraArchivo = rd.GetDateTime(12);
            }

            var rutaLocal = ResolveExistingAttachmentPath(record);
            if (!string.IsNullOrWhiteSpace(rutaLocal) && !string.Equals(rutaLocal, record.RutaLocal, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateAttachmentLocalPathAsync(record.IdAdjunto, rutaLocal, connectionString, token);
                record.RutaLocal = rutaLocal;
            }

            if (allowRemoteRecovery && string.IsNullOrWhiteSpace(rutaLocal))
                rutaLocal = await TryRecoverAttachmentFileAsync(record, connectionString, token);

            if (supportsDurableContent
                && contenido.Length == 0
                && !string.IsNullOrWhiteSpace(rutaLocal)
                && File.Exists(rutaLocal))
            {
                contenido = await File.ReadAllBytesAsync(rutaLocal, token);
                var fileInfo = new FileInfo(rutaLocal);
                await UpdateRecoveredAttachmentAsync(record.IdAdjunto, rutaLocal, record.MimeType, fileInfo.Length, contenido, connectionString, token);
                fechaHoraArchivo = fileInfo.LastWriteTimeUtc;
            }

            var nombreDescarga = includeDownloadName
                ? await BuildAttachmentDownloadNameAsync(cn, record, token)
                : record.NombreArchivo;
            return new ConversacionAdjuntoServeDto
            {
                RutaLocal = rutaLocal,
                MimeType = record.MimeType,
                NombreArchivo = record.NombreArchivo,
                NombreDescarga = nombreDescarga,
                Contenido = contenido,
                FechaHoraModificacion = fechaHoraArchivo
            };
        }, "No se pudo obtener el adjunto.", ct);

    private static async Task<string> BuildAttachmentDownloadNameAsync(SqlConnection cn, AttachmentServeRecord record, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(record.NombreArchivo))
            return SanitizeDownloadFileName(record.NombreArchivo, "Attachment", InferExtension(record.MimeType, record.TipoArchivo));

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

    private static string SanitizePathSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "base" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            normalized = normalized.Replace(invalid, '_');

        normalized = Regex.Replace(normalized, @"\s+", "_");
        normalized = Regex.Replace(normalized, @"[^a-zA-Z0-9._-]", "_");
        normalized = normalized.Trim('.', '_', '-');
        return string.IsNullOrWhiteSpace(normalized) ? "base" : normalized;
    }

    private async Task<int> StoreIncomingAttachmentsAsync(
        long conversationId,
        long messageId,
        IncomingWhatsAppMessage incoming,
        ConversacionWhatsAppConfigDto config,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.AccessToken))
            throw new InvalidOperationException("No hay token de WhatsApp configurado para descargar el adjunto entrante.");

        var stored = 0;
        foreach (var attachment in incoming.Attachments)
        {
            try
            {
                if (await MessageAttachmentExistsAsync(messageId, attachment.MediaId, ct))
                {
                    stored++;
                    continue;
                }

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
                    bytes,
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
                throw;
            }
        }

        return stored;
    }

    private async Task<bool> MessageAttachmentExistsAsync(long messageId, string mediaId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.CONV_ADJUNTOS
            WHERE IdMensaje = @IdMensaje
              AND (
                    @MediaId = N''
                    OR ISNULL(PayloadJson, '') LIKE @MediaIdLike
                  );
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdMensaje", messageId);
        cmd.Parameters.AddWithValue("@MediaId", mediaId ?? string.Empty);
        cmd.Parameters.AddWithValue("@MediaIdLike", $"%{(mediaId ?? string.Empty).Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
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
                if (!TryAcquireAutomaticMediaRecoveryPermit())
                    continue;

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

    private async Task<List<PendingMediaHydration>> GetPendingMediaHydrationAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT
                m.IdMensaje,
                m.IdConversacion,
                ISNULL(m.TelefonoWhatsApp, ''),
                ISNULL(m.WhatsAppMessageId, ''),
                ISNULL(m.MessageType, ''),
                ISNULL(m.Direction, ''),
                ISNULL(m.Texto, ''),
                ISNULL(m.PayloadJson, ''),
                m.FechaHora
            FROM dbo.CONV_MENSAJES m
            WHERE m.IdConversacion = @IdConversacion
              AND m.FechaHora >= @RecoveryCutoff
              AND UPPER(ISNULL(m.Direction, '')) = N'ENTRANTE'
              AND UPPER(ISNULL(m.MessageType, '')) IN (N'IMAGE', N'AUDIO', N'STICKER', N'DOCUMENT', N'VIDEO')
              AND ISNULL(m.PayloadJson, '') <> ''
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_ADJUNTOS a
                    WHERE a.IdMensaje = m.IdMensaje
              )
            ORDER BY m.IdMensaje;
            """;

        var items = new List<PendingMediaHydration>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@RecoveryCutoff", BusinessNow().Subtract(GetAttachmentRecoveryMaxAge()));
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var message = new ConversacionMensajeDto
            {
                IdMensaje = rd.GetInt64(0),
                IdConversacion = rd.GetInt64(1),
                TelefonoWhatsApp = GetString(rd, 2),
                WhatsAppMessageId = GetString(rd, 3),
                MessageType = GetString(rd, 4),
                Direction = GetString(rd, 5),
                Texto = GetString(rd, 6),
                PayloadJson = GetString(rd, 7),
                FechaHora = rd.IsDBNull(8) ? DateTime.MinValue : NormalizeStoredConversationTime(rd.GetDateTime(8))
            };

            if (ShouldHydrateIncomingMedia(message, message.PayloadJson))
            {
                items.Add(new PendingMediaHydration
                {
                    Message = message,
                    PayloadJson = message.PayloadJson
                });

                if (items.Count >= MaxAutomaticMediaHydrationsPerConversation)
                    break;
            }
        }

        return items;
    }

    private async Task<List<long>> GetConversationAttachmentIdsAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT a.IdAdjunto
            FROM dbo.CONV_ADJUNTOS a
            INNER JOIN dbo.CONV_MENSAJES m
                ON m.IdMensaje = a.IdMensaje
            WHERE m.IdConversacion = @IdConversacion
            ORDER BY a.IdAdjunto;
            """;

        var ids = new List<long>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            ids.Add(rd.GetInt64(0));

        return ids;
    }

    private async Task<List<long>> GetRecoverableAttachmentIdsAsync(long idConversacion, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, ct);

        const string sql = """
            SELECT TOP (@MaxItems)
                a.IdAdjunto
            FROM dbo.CONV_ADJUNTOS a
            INNER JOIN dbo.CONV_MENSAJES m
                ON m.IdMensaje = a.IdMensaje
            WHERE m.IdConversacion = @IdConversacion
              AND m.FechaHora >= @RecoveryCutoff
              AND UPPER(ISNULL(m.Direction, '')) = N'ENTRANTE'
              AND UPPER(ISNULL(a.TipoArchivo, '')) IN (N'IMAGE', N'AUDIO', N'STICKER', N'DOCUMENT', N'VIDEO')
              AND a.ArchivoContenido IS NULL
              AND UPPER(ISNULL(a.AlmacenamientoEstado, N'')) NOT IN (N'SQL_Y_RUTA', N'SQL', N'NO_DISPONIBLE_META', N'RECUPERACION_FALLIDA')
              AND (
                    ISNULL(a.PayloadJson, N'') LIKE N'%MediaId%'
                 OR ISNULL(a.PayloadJson, N'') LIKE N'%media_id%'
                 OR ISNULL(m.PayloadJson, N'') LIKE N'%"id"%'
              )
            ORDER BY m.FechaHora DESC, a.IdAdjunto DESC;
            """;

        var ids = new List<long>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@RecoveryCutoff", BusinessNow().Subtract(GetAttachmentRecoveryMaxAge()));
        cmd.Parameters.AddWithValue("@MaxItems", MaxAutomaticAttachmentRecoveriesPerConversation);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            ids.Add(rd.GetInt64(0));

        return ids;
    }

    private string ResolveExistingAttachmentPath(AttachmentServeRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.RutaLocal))
        {
            var directPath = ToAbsoluteAttachmentPath(record.RutaLocal);
            if (File.Exists(directPath))
                return directPath;
        }

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
        {
            yield return Path.Combine(UploadsBasePath, record.IdConversacion.ToString(CultureInfo.InvariantCulture), fileName);
            yield return Path.Combine(LegacyUploadsBasePath, record.IdConversacion.ToString(CultureInfo.InvariantCulture), fileName);
        }

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

    private string ToAbsoluteAttachmentPath(string rutaLocal)
        => string.IsNullOrWhiteSpace(rutaLocal)
            ? string.Empty
            : Path.IsPathRooted(rutaLocal)
                ? rutaLocal
                : Path.Combine(environment.ContentRootPath, rutaLocal);

    private string ToStoredAttachmentPath(string rutaLocal)
    {
        if (string.IsNullOrWhiteSpace(rutaLocal))
            return string.Empty;

        if (!Path.IsPathRooted(rutaLocal))
            return rutaLocal;

        var fullPath = Path.GetFullPath(rutaLocal);
        var contentRoot = Path.GetFullPath(environment.ContentRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(contentRoot, fullPath)
            : rutaLocal;
    }

    private bool IsAttachmentRecoveryCandidate(
        AttachmentServeRecord record,
        bool hasSqlContent,
        string resolvedLocalPath,
        string storageState)
    {
        if (hasSqlContent)
            return false;
        if (!string.IsNullOrWhiteSpace(resolvedLocalPath) && File.Exists(resolvedLocalPath))
            return false;
        if (!CanRecoverMediaByAge(record.FechaHora))
            return false;

        var normalizedState = (storageState ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedState is "NO_DISPONIBLE_META" or "RECUPERACION_FALLIDA")
            return false;

        var mediaId = TryExtractMediaId(record.AdjuntoPayloadJson, record.TipoArchivo)
                      ?? TryExtractMediaId(record.MensajePayloadJson, record.TipoArchivo);
        return !string.IsNullOrWhiteSpace(mediaId);
    }

    private async Task<string> TryRecoverAttachmentFileAsync(AttachmentServeRecord record, string connectionString, CancellationToken ct)
    {
        if (!CanRecoverMediaByAge(record.FechaHora))
        {
            await MarkAttachmentStorageStateAsync(record.IdAdjunto, "NO_DISPONIBLE_META", connectionString, ct);
            return string.Empty;
        }

        var mediaId = TryExtractMediaId(record.AdjuntoPayloadJson, record.TipoArchivo)
            ?? TryExtractMediaId(record.MensajePayloadJson, record.TipoArchivo);
        if (string.IsNullOrWhiteSpace(mediaId))
        {
            await MarkAttachmentStorageStateAsync(record.IdAdjunto, "NO_DISPONIBLE_META", connectionString, ct);
            return string.Empty;
        }

        if (!CanAttemptAttachmentRecovery(record.IdAdjunto, mediaId))
            return string.Empty;

        if (!TryAcquireAutomaticMediaRecoveryPermit())
            return string.Empty;

        try
        {
            var config = await conversacionesConfigService.GetWhatsAppConfigAsync(connectionString, ct);
            if (string.IsNullOrWhiteSpace(config.AccessToken))
                return string.Empty;

            var media = await GetWhatsAppMediaAsync(config, mediaId, ct);
            var bytes = await DownloadWhatsAppMediaAsync(config, media.Url, ct);
            if (bytes.Length == 0)
            {
                await MarkAttachmentStorageStateAsync(record.IdAdjunto, "RECUPERACION_FALLIDA", connectionString, ct);
                return string.Empty;
            }

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

            await UpdateRecoveredAttachmentAsync(record.IdAdjunto, rutaLocal, mimeType, bytes.LongLength, bytes, connectionString, ct);

            record.RutaLocal = rutaLocal;
            record.MimeType = mimeType;
            return rutaLocal;
        }
        catch (Exception ex)
        {
            await MarkAttachmentStorageStateAsync(record.IdAdjunto, "RECUPERACION_FALLIDA", connectionString, ct);
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

    private bool CanAttemptAttachmentRecovery(long idAdjunto, string mediaId)
    {
        var maxAttempts = GetAttachmentRecoveryMaxAttempts();
        if (maxAttempts <= 0)
            return false;

        var key = GetAttachmentRecoveryAttemptKey(idAdjunto, mediaId);
        var attempts = AttachmentRecoveryAttempts.AddOrUpdate(key, 1, (_, current) => current + 1);
        return attempts <= maxAttempts;
    }

    private bool CanRecoverMediaByAge(DateTime messageDate)
    {
        if (messageDate == DateTime.MinValue)
            return false;

        var age = BusinessNow() - NormalizeStoredConversationTime(messageDate);
        return age >= TimeSpan.Zero && age <= GetAttachmentRecoveryMaxAge();
    }

    private static bool TryAcquireAutomaticMediaRecoveryPermit()
    {
        var nowUtc = DateTime.UtcNow;
        var cutoffUtc = nowUtc - AutomaticMediaRecoveryRateWindow;

        lock (AutomaticMediaRecoveryRateLock)
        {
            while (AutomaticMediaRecoveryRequestTimesUtc.Count > 0 &&
                   AutomaticMediaRecoveryRequestTimesUtc.Peek() <= cutoffUtc)
            {
                AutomaticMediaRecoveryRequestTimesUtc.Dequeue();
            }

            if (AutomaticMediaRecoveryRequestTimesUtc.Count >= MaxAutomaticMediaRecoveryRequestsPerWindow)
                return false;

            AutomaticMediaRecoveryRequestTimesUtc.Enqueue(nowUtc);
            return true;
        }
    }

    private int GetAttachmentRecoveryMaxAttempts()
    {
        var configured = configuration.GetValue<int?>("WhatsApp:AttachmentRecoveryMaxAttempts");
        if (configured.HasValue)
            return Math.Clamp(configured.Value, 0, DefaultAttachmentRecoveryMaxAttempts);

        var rawEnv = Environment.GetEnvironmentVariable("ALFACORE_WHATSAPP_ATTACHMENT_RECOVERY_MAX_ATTEMPTS");
        return int.TryParse(rawEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var envValue)
            ? Math.Clamp(envValue, 0, DefaultAttachmentRecoveryMaxAttempts)
            : DefaultAttachmentRecoveryMaxAttempts;
    }

    private TimeSpan GetAttachmentRecoveryMaxAge()
    {
        var configured = configuration.GetValue<double?>("WhatsApp:AttachmentRecoveryMaxAgeHours");
        if (configured.HasValue)
            return TimeSpan.FromHours(Math.Clamp(configured.Value, 0, DefaultAutomaticMediaRecoveryMaxAge.TotalHours));

        var rawEnv = Environment.GetEnvironmentVariable("ALFACORE_WHATSAPP_ATTACHMENT_RECOVERY_MAX_AGE_HOURS");
        return double.TryParse(rawEnv, NumberStyles.Float, CultureInfo.InvariantCulture, out var envValue)
            ? TimeSpan.FromHours(Math.Clamp(envValue, 0, DefaultAutomaticMediaRecoveryMaxAge.TotalHours))
            : DefaultAutomaticMediaRecoveryMaxAge;
    }

    private static long GetAttachmentRecoveryAttemptKey(long idAdjunto, string mediaId)
        => idAdjunto > 0
            ? idAdjunto
            : mediaId.GetHashCode(StringComparison.OrdinalIgnoreCase);

    private async Task UpdateAttachmentLocalPathAsync(long idAdjunto, string rutaLocal, string? connectionString, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_ADJUNTOS
            SET RutaLocal = @RutaLocal
            WHERE IdAdjunto = @IdAdjunto
            """;

        await using var cn = new SqlConnection(string.IsNullOrWhiteSpace(connectionString) ? ConnectionString : connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@RutaLocal", ToStoredAttachmentPath(rutaLocal));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateRecoveredAttachmentAsync(long idAdjunto, string rutaLocal, string mimeType, long tamanoBytes, byte[]? archivoContenido, string? connectionString, CancellationToken ct)
    {
        await using var cn = new SqlConnection(string.IsNullOrWhiteSpace(connectionString) ? ConnectionString : connectionString);
        await cn.OpenAsync(ct);
        await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, ct);
        var supportsDurableContent = await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "ArchivoContenido", ct);
        var sql = supportsDurableContent
            ? """
                UPDATE dbo.CONV_ADJUNTOS
                SET
                    RutaLocal = @RutaLocal,
                    MimeType = CASE WHEN @MimeType IS NULL OR LTRIM(RTRIM(@MimeType)) = '' THEN MimeType ELSE @MimeType END,
                    TamanoBytes = @TamanoBytes,
                    ArchivoContenido = COALESCE(@ArchivoContenido, ArchivoContenido),
                    ArchivoHashSha256 = COALESCE(@ArchivoHashSha256, ArchivoHashSha256),
                    AlmacenamientoEstado = CASE WHEN @ArchivoContenido IS NULL THEN ISNULL(AlmacenamientoEstado, N'RUTA_LOCAL') ELSE N'SQL_Y_RUTA' END,
                    FechaHora_Archivo = CASE WHEN @ArchivoContenido IS NULL THEN FechaHora_Archivo ELSE GETDATE() END
                WHERE IdAdjunto = @IdAdjunto
                """
            : """
                UPDATE dbo.CONV_ADJUNTOS
                SET
                    RutaLocal = @RutaLocal,
                    MimeType = CASE WHEN @MimeType IS NULL OR LTRIM(RTRIM(@MimeType)) = '' THEN MimeType ELSE @MimeType END,
                    TamanoBytes = @TamanoBytes
                WHERE IdAdjunto = @IdAdjunto
                """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@RutaLocal", ToStoredAttachmentPath(rutaLocal));
        cmd.Parameters.AddWithValue("@MimeType", DbNullable(mimeType));
        cmd.Parameters.AddWithValue("@TamanoBytes", tamanoBytes);
        if (supportsDurableContent)
        {
            var content = archivoContenido is { Length: > 0 } ? archivoContenido : null;
            cmd.Parameters.AddWithValue("@ArchivoContenido", content is null ? DBNull.Value : content);
            cmd.Parameters.AddWithValue("@ArchivoHashSha256", content is null ? DBNull.Value : Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        }
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkAttachmentStorageStateAsync(long idAdjunto, string storageState, string? connectionString, CancellationToken ct)
    {
        if (idAdjunto <= 0 || string.IsNullOrWhiteSpace(storageState))
            return;

        await using var cn = new SqlConnection(string.IsNullOrWhiteSpace(connectionString) ? ConnectionString : connectionString);
        await cn.OpenAsync(ct);
        await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, ct);

        const string sql = """
            UPDATE dbo.CONV_ADJUNTOS
            SET
                AlmacenamientoEstado = @AlmacenamientoEstado,
                FechaHora_Archivo = COALESCE(FechaHora_Archivo, GETDATE())
            WHERE IdAdjunto = @IdAdjunto
              AND ArchivoContenido IS NULL
              AND UPPER(ISNULL(AlmacenamientoEstado, N'')) NOT IN (N'SQL_Y_RUTA', N'SQL')
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdAdjunto", idAdjunto);
        cmd.Parameters.AddWithValue("@AlmacenamientoEstado", storageState.Trim().ToUpperInvariant());
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

    private static async Task EnsureConversationAttachmentsDurableColumnsAsync(SqlConnection cn, CancellationToken ct)
    {
        string[] commands =
        [
            """
            IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'ArchivoContenido') IS NULL
                ALTER TABLE dbo.CONV_ADJUNTOS ADD ArchivoContenido varbinary(max) NULL;
            """,
            """
            IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'ArchivoHashSha256') IS NULL
                ALTER TABLE dbo.CONV_ADJUNTOS ADD ArchivoHashSha256 nvarchar(64) NULL;
            """,
            """
            IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'AlmacenamientoEstado') IS NULL
                ALTER TABLE dbo.CONV_ADJUNTOS ADD AlmacenamientoEstado nvarchar(20) NULL;
            """,
            """
            IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'FechaHora_Archivo') IS NULL
                ALTER TABLE dbo.CONV_ADJUNTOS ADD FechaHora_Archivo datetime NULL;
            """,
            """
            IF OBJECT_ID(N'dbo.CONV_ADJUNTOS', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CONV_ADJUNTOS', N'AlmacenamientoEstado') IS NOT NULL
            BEGIN
                UPDATE dbo.CONV_ADJUNTOS
                SET AlmacenamientoEstado = N'RUTA_LOCAL'
                WHERE AlmacenamientoEstado IS NULL
                  AND NULLIF(LTRIM(RTRIM(ISNULL(RutaLocal, N''))), N'') IS NOT NULL;
            END
            """
        ];

        foreach (var sql in commands)
        {
            await using var cmd = new SqlCommand(sql, cn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EnsureConversationAttachmentsDurableColumnsOnceAsync(SqlConnection cn, CancellationToken ct)
    {
        var key = $"{cn.DataSource}|{cn.Database}".Trim().ToUpperInvariant();
        if (DurableAttachmentColumnsEnsured.ContainsKey(key))
            return;

        var gate = DurableAttachmentColumnsLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (DurableAttachmentColumnsEnsured.ContainsKey(key))
                return;

            await EnsureConversationAttachmentsDurableColumnsAsync(cn, ct);
            DurableAttachmentColumnsEnsured[key] = 1;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection cn, string tableName, string columnName, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.' + @TableName)
                      AND name = @ColumnName
                ) THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@TableName", tableName);
        cmd.Parameters.AddWithValue("@ColumnName", columnName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool value && value;
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
            Observation = "No se encontrÃ³ una vista de saldos compatible para calcular deuda automÃ¡tica."
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
        var idNumeroWhatsApp = await ResolveNumeroWhatsAppIdAsync(cn, incoming.PhoneNumberId, ct);
        var existing = await FindWhatsAppConversationByPhoneAsync(cn, incoming.Phone, contact.IdContact, idNumeroWhatsApp, ct);
        if (existing is null && idNumeroWhatsApp.HasValue)
        {
            existing = await FindProvisionalWhatsAppConversationByPhoneAsync(cn, incoming.Phone, contact.IdContact, ct);
            if (existing is not null)
                await UpdateConversationWhatsAppNumberAsync(cn, existing.IdConversacion, idNumeroWhatsApp.Value, ct);
        }

        if (existing is not null)
        {
            if (contact.IdContact.HasValue)
                await AssociateContactIfMissingAsync(cn, existing.IdConversacion, contact, ct);

            await MergeDuplicateWhatsAppConversationsAsync(cn, existing.IdConversacion, incoming.Phone, contact.IdContact, idNumeroWhatsApp, ct);

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
                IdNumeroWhatsApp,
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
                @IdNumeroWhatsApp,
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
        cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", idNumeroWhatsApp.HasValue ? idNumeroWhatsApp.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(incoming.Text)));
        cmd.Parameters.AddWithValue("@FechaHora", NormalizeIncomingTimestamp(incoming.Timestamp));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resuelve a que fila de CONV_WHATSAPP_NUMEROS pertenece un phone_number_id entrante de Meta.
    /// Si Meta envio un numero valido pero todavia no esta cargado, lo registra para no mezclar
    /// conversaciones de dos numeros receptores distintos bajo el mismo cliente.
    /// </summary>
    private static async Task<int?> ResolveNumeroWhatsAppIdAsync(SqlConnection cn, string phoneNumberId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId))
            return null;

        const string sql = """
            DECLARE @IdNumero int;

            SELECT @IdNumero = IdNumero
            FROM dbo.CONV_WHATSAPP_NUMEROS
            WHERE PhoneNumberId = @PhoneNumberId;

            IF @IdNumero IS NULL
            BEGIN
                INSERT INTO dbo.CONV_WHATSAPP_NUMEROS (PhoneNumberId, Nombre, Activo)
                VALUES (@PhoneNumberId, @NombreDetectado, 1);

                SET @IdNumero = CAST(SCOPE_IDENTITY() AS int);
            END;

            SELECT @IdNumero;
            """;

        var normalizedPhoneNumberId = phoneNumberId.Trim();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@PhoneNumberId", normalizedPhoneNumberId);
        cmd.Parameters.AddWithValue("@NombreDetectado", BuildDetectedWhatsAppNumberName(normalizedPhoneNumberId));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string BuildDetectedWhatsAppNumberName(string phoneNumberId)
    {
        var normalized = phoneNumberId.Trim();
        var suffix = normalized.Length <= 6 ? normalized : normalized[^6..];
        return string.IsNullOrWhiteSpace(suffix)
            ? "Numero WhatsApp detectado"
            : $"Numero WhatsApp ...{suffix}";
    }

    private async Task<long> EnsureInstagramConversationAsync(
        IncomingInstagramMessage incoming,
        InstagramProfile profile,
        CancellationToken ct)
    {
        const string selectSql = """
            SELECT TOP (1) IdConversacion
            FROM dbo.CONV_CONVERSACIONES WITH (UPDLOCK, HOLDLOCK)
            WHERE Canal = N'INSTAGRAM'
              AND IdentificadorExternoContacto = @SenderId
            ORDER BY FechaHoraUltimoMensaje DESC, IdConversacion DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await using (var selectCmd = new SqlCommand(selectSql, cn, tx))
        {
            selectCmd.Parameters.AddWithValue("@SenderId", incoming.SenderId);
            var existing = await selectCmd.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
            {
                var existingId = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                const string updateProfileSql = """
                    UPDATE dbo.CONV_CONVERSACIONES
                    SET NombreVisible = CASE
                            WHEN @NombreVisible IS NOT NULL
                             AND (NULLIF(LTRIM(RTRIM(ISNULL(NombreVisible, N''))), N'') IS NULL
                                  OR NombreVisible LIKE N'Instagram %')
                                THEN @NombreVisible
                            ELSE NombreVisible
                        END,
                        UsuarioExterno = COALESCE(@UsuarioExterno, UsuarioExterno),
                        FotoPerfilUrl = COALESCE(@FotoPerfilUrl, FotoPerfilUrl),
                        SeguidoresExterno = COALESCE(@SeguidoresExterno, SeguidoresExterno),
                        PerfilExternoVerificado = COALESCE(@PerfilExternoVerificado, PerfilExternoVerificado),
                        UsuarioSigueCuenta = COALESCE(@UsuarioSigueCuenta, UsuarioSigueCuenta),
                        CuentaSigueUsuario = COALESCE(@CuentaSigueUsuario, CuentaSigueUsuario),
                        FechaHoraPerfilExterno = CASE WHEN @TienePerfil = 1 THEN GETDATE() ELSE FechaHoraPerfilExterno END,
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdConversacion = @IdConversacion;
                    """;
                await using var updateCmd = new SqlCommand(updateProfileSql, cn, tx);
                AddInstagramProfileParameters(updateCmd, profile);
                updateCmd.Parameters.AddWithValue("@IdConversacion", existingId);
                await updateCmd.ExecuteNonQueryAsync(ct);

                await tx.CommitAsync(ct);
                return existingId;
            }
        }

        var displayName = FirstNonEmpty(profile.DisplayName, BuildInstagramFallbackName(incoming.SenderId));
        const string insertSql = """
            INSERT INTO dbo.CONV_CONVERSACIONES
            (
                Canal,
                NombreVisible,
                IdentificadorExternoContacto,
                IdentificadorExternoConversacion,
                UsuarioExterno,
                FotoPerfilUrl,
                SeguidoresExterno,
                PerfilExternoVerificado,
                UsuarioSigueCuenta,
                CuentaSigueUsuario,
                FechaHoraPerfilExterno,
                CodigoEstado,
                ResumenUltimoMensaje,
                FechaHoraPrimerMensaje,
                FechaHoraUltimoMensaje,
                FechaHora_Grabacion
            )
            VALUES
            (
                N'INSTAGRAM',
                @NombreVisible,
                @SenderId,
                @SenderId,
                @UsuarioExterno,
                @FotoPerfilUrl,
                @SeguidoresExterno,
                @PerfilExternoVerificado,
                @UsuarioSigueCuenta,
                @CuentaSigueUsuario,
                CASE WHEN @TienePerfil = 1 THEN GETDATE() ELSE NULL END,
                N'ABIERTA',
                @ResumenUltimoMensaje,
                @FechaHora,
                @FechaHora,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var insertCmd = new SqlCommand(insertSql, cn, tx);
        AddInstagramProfileParameters(insertCmd, profile);
        insertCmd.Parameters["@NombreVisible"].Value = displayName;
        insertCmd.Parameters.AddWithValue("@SenderId", incoming.SenderId);
        insertCmd.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(incoming.Text)));
        insertCmd.Parameters.AddWithValue("@FechaHora", incoming.Timestamp);
        var result = await insertCmd.ExecuteScalarAsync(ct);
        var conversationId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return conversationId;
    }

    private async Task<InstagramProfile> TryGetInstagramProfileAsync(
        ConversacionInstagramConfigDto config,
        string senderId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.AccessToken) || string.IsNullOrWhiteSpace(senderId))
            return InstagramProfile.Empty;

        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v25.0" : config.ApiVersion.Trim();
        var fields = "id,name,username,profile_pic,follower_count,is_verified_user,is_user_follow_business,is_business_follow_user";
        var url = $"https://graph.instagram.com/{version}/{Uri.EscapeDataString(senderId)}?fields={fields}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            var client = httpClientFactory.CreateClient();
            using var profileCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            profileCts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await client.SendAsync(request, profileCts.Token);
            if (!response.IsSuccessStatusCode)
                return InstagramProfile.Empty;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new InstagramProfile
            {
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Username = root.TryGetProperty("username", out var username) ? username.GetString() ?? string.Empty : string.Empty,
                ProfilePictureUrl = root.TryGetProperty("profile_pic", out var profilePicture) ? profilePicture.GetString() ?? string.Empty : string.Empty,
                FollowerCount = root.TryGetProperty("follower_count", out var followerCount) && followerCount.TryGetInt32(out var followers) ? followers : null,
                IsVerified = ReadOptionalJsonBoolean(root, "is_verified_user"),
                IsUserFollowingBusiness = ReadOptionalJsonBoolean(root, "is_user_follow_business"),
                IsBusinessFollowingUser = ReadOptionalJsonBoolean(root, "is_business_follow_user")
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return InstagramProfile.Empty;
        }
    }

    private async Task<long> EnsureFacebookConversationAsync(
        IncomingInstagramMessage incoming,
        FacebookProfile profile,
        CancellationToken ct)
    {
        const string selectSql = """
            SELECT TOP (1) IdConversacion
            FROM dbo.CONV_CONVERSACIONES WITH (UPDLOCK, HOLDLOCK)
            WHERE Canal = N'FACEBOOK'
              AND IdentificadorExternoContacto = @SenderId
            ORDER BY FechaHoraUltimoMensaje DESC, IdConversacion DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await using (var selectCmd = new SqlCommand(selectSql, cn, tx))
        {
            selectCmd.Parameters.AddWithValue("@SenderId", incoming.SenderId);
            var existing = await selectCmd.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
            {
                var existingId = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                const string updateProfileSql = """
                    UPDATE dbo.CONV_CONVERSACIONES
                    SET NombreVisible = CASE
                            WHEN @NombreVisible IS NOT NULL
                             AND (NULLIF(LTRIM(RTRIM(ISNULL(NombreVisible, N''))), N'') IS NULL
                                  OR NombreVisible LIKE N'Facebook %')
                                THEN @NombreVisible
                            ELSE NombreVisible
                        END,
                        UsuarioExterno = COALESCE(@UsuarioExterno, UsuarioExterno),
                        FotoPerfilUrl = COALESCE(@FotoPerfilUrl, FotoPerfilUrl),
                        FechaHoraPerfilExterno = CASE WHEN @TienePerfil = 1 THEN GETDATE() ELSE FechaHoraPerfilExterno END,
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdConversacion = @IdConversacion;
                    """;
                await using var updateCmd = new SqlCommand(updateProfileSql, cn, tx);
                AddFacebookProfileParameters(updateCmd, profile);
                updateCmd.Parameters.AddWithValue("@IdConversacion", existingId);
                await updateCmd.ExecuteNonQueryAsync(ct);

                await tx.CommitAsync(ct);
                return existingId;
            }
        }

        var displayName = FirstNonEmpty(profile.DisplayName, BuildFacebookFallbackName(incoming.SenderId));
        const string insertSql = """
            INSERT INTO dbo.CONV_CONVERSACIONES
            (
                Canal,
                NombreVisible,
                IdentificadorExternoContacto,
                IdentificadorExternoConversacion,
                UsuarioExterno,
                FotoPerfilUrl,
                FechaHoraPerfilExterno,
                CodigoEstado,
                ResumenUltimoMensaje,
                FechaHoraPrimerMensaje,
                FechaHoraUltimoMensaje,
                FechaHora_Grabacion
            )
            VALUES
            (
                N'FACEBOOK',
                @NombreVisible,
                @SenderId,
                @SenderId,
                @UsuarioExterno,
                @FotoPerfilUrl,
                CASE WHEN @TienePerfil = 1 THEN GETDATE() ELSE NULL END,
                N'ABIERTA',
                @ResumenUltimoMensaje,
                @FechaHora,
                @FechaHora,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var insertCmd = new SqlCommand(insertSql, cn, tx);
        AddFacebookProfileParameters(insertCmd, profile);
        insertCmd.Parameters["@NombreVisible"].Value = displayName;
        insertCmd.Parameters.AddWithValue("@SenderId", incoming.SenderId);
        insertCmd.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(incoming.Text)));
        insertCmd.Parameters.AddWithValue("@FechaHora", incoming.Timestamp);
        var result = await insertCmd.ExecuteScalarAsync(ct);
        var conversationId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return conversationId;
    }

    private async Task<bool> TryRefreshFacebookConversationProfileAsync(ConversacionDetalleDto item, CancellationToken ct)
    {
        if (!NeedsFacebookProfileRefresh(item))
            return false;

        try
        {
            var config = await conversacionesConfigService.GetFacebookConfigAsync(ct);
            var pageContext = await TryGetFacebookConversationPageContextAsync(item.IdConversacion, ct);
            var profile = await TryGetFacebookProfileAsync(
                config,
                item.IdentificadorExternoContacto,
                pageContext.RecipientId,
                pageContext.AccountId,
                ct);
            if (!profile.HasData)
                return false;

            const string sql = """
                UPDATE dbo.CONV_CONVERSACIONES
                SET NombreVisible = CASE
                        WHEN @NombreVisible IS NOT NULL
                         AND (NULLIF(LTRIM(RTRIM(ISNULL(NombreVisible, N''))), N'') IS NULL
                              OR NombreVisible LIKE N'Facebook %')
                            THEN @NombreVisible
                        ELSE NombreVisible
                    END,
                    FotoPerfilUrl = COALESCE(@FotoPerfilUrl, FotoPerfilUrl),
                    FechaHoraPerfilExterno = GETDATE(),
                    FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @IdConversacion
                  AND Canal = N'FACEBOOK';
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            AddFacebookProfileParameters(cmd, profile);
            cmd.Parameters.AddWithValue("@IdConversacion", item.IdConversacion);
            await cmd.ExecuteNonQueryAsync(ct);

            if (!string.IsNullOrWhiteSpace(profile.DisplayName) && IsFacebookFallbackName(item.NombreVisible))
                item.NombreVisible = profile.DisplayName;

            if (string.IsNullOrWhiteSpace(item.FotoPerfilUrl) && !string.IsNullOrWhiteSpace(profile.ProfilePictureUrl))
                item.FotoPerfilUrl = profile.ProfilePictureUrl;

            item.FechaHoraPerfilExterno = BusinessNow();
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "RefreshFacebookConversationProfile",
                ex,
                "No se pudo actualizar el perfil de Facebook Messenger.",
                new { item.IdConversacion, item.IdentificadorExternoContacto },
                AppEventSeverity.Warning,
                ct);
            return false;
        }
    }

    private static bool NeedsFacebookProfileRefresh(ConversacionDetalleDto item)
        => string.Equals(item.Canal, "FACEBOOK", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(item.IdentificadorExternoContacto)
           && (string.IsNullOrWhiteSpace(item.FotoPerfilUrl) || IsFacebookFallbackName(item.NombreVisible));

    private static bool IsFacebookFallbackName(string? value)
        => string.IsNullOrWhiteSpace(value)
           || value.Trim().StartsWith("Facebook ", StringComparison.OrdinalIgnoreCase);

    private async Task<FacebookConversationPageContext> TryGetFacebookConversationPageContextAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(PayloadJson, N'')
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND LTRIM(RTRIM(ISNULL(PayloadJson, N''))) <> N''
            ORDER BY IdMensaje DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var payload = Convert.ToString(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
            return FacebookConversationPageContext.Empty;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.Object)
            {
                return new FacebookConversationPageContext(
                    FirstNonEmpty(ReadNestedString(raw, "recipient", "id"), ReadNestedString(root, "recipient", "id")),
                    FirstNonEmpty(ReadJsonString(root, "entry_page_id"), ReadJsonString(root, "entry_id")));
            }

            return new FacebookConversationPageContext(
                ReadNestedString(root, "recipient", "id"),
                FirstNonEmpty(ReadJsonString(root, "entry_page_id"), ReadJsonString(root, "entry_id")));
        }
        catch
        {
            return FacebookConversationPageContext.Empty;
        }
    }

    private Task<FacebookProfile> TryGetFacebookProfileAsync(
        ConversacionFacebookConfigDto config,
        IncomingInstagramMessage incoming,
        CancellationToken ct)
        => TryGetFacebookProfileAsync(config, incoming.SenderId, incoming.RecipientId, incoming.AccountId, ct);

    private async Task<FacebookProfile> TryGetFacebookProfileAsync(
        ConversacionFacebookConfigDto config,
        string senderId,
        string recipientId,
        string accountId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.AccessToken) || string.IsNullOrWhiteSpace(senderId))
            return FacebookProfile.Empty;

        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v25.0" : config.ApiVersion.Trim();
        string[] fieldSets =
        [
            "first_name,last_name,profile_pic",
            "first_name,last_name,name,profile_pic",
            "name,picture",
            "id,first_name,last_name,profile_pic",
            "id,name,picture"
        ];

        try
        {
            var client = httpClientFactory.CreateClient();
            var failures = new List<string>();

            foreach (var fields in fieldSets)
            {
                var profile = await TryGetFacebookProfileAsync(client, version, config.AccessToken, senderId, fields, failures, ct);
                if (profile.HasData)
                    return profile;
            }

            await LogFacebookProfileLookupFailureAsync(config, senderId, recipientId, accountId, failures, ct);

            return FacebookProfile.Empty;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await LogFacebookProfileLookupFailureAsync(config, senderId, recipientId, accountId, [$"exception={ex.GetType().Name}; message={TrimForLog(ex.Message)}"], ct);
            return FacebookProfile.Empty;
        }
    }

    private static async Task<FacebookProfile> TryGetFacebookProfileAsync(
        HttpClient client,
        string version,
        string accessToken,
        string senderId,
        string fields,
        List<string> failures,
        CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(senderId)}?fields={Uri.EscapeDataString(fields)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

        using var profileCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        profileCts.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await client.SendAsync(request, profileCts.Token);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            failures.Add($"fields={fields}; http={(int)response.StatusCode}; body={TrimForLog(body)}");
            return FacebookProfile.Empty;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var profile = new FacebookProfile
        {
            Name = ReadJsonString(root, "name"),
            FirstName = ReadJsonString(root, "first_name"),
            LastName = ReadJsonString(root, "last_name"),
            ProfilePictureUrl = FirstNonEmpty(ReadJsonString(root, "profile_pic"), ReadFacebookPictureUrl(root))
        };

        if (!profile.HasData)
            failures.Add($"fields={fields}; http={(int)response.StatusCode}; body={TrimForLog(body)}");

        return profile;
    }

    private async Task LogFacebookProfileLookupFailureAsync(
        ConversacionFacebookConfigDto config,
        string senderId,
        string recipientId,
        string accountId,
        List<string> failures,
        CancellationToken ct)
    {
        try
        {
            var configuredPageId = config.PageId?.Trim() ?? string.Empty;
            var webhookPageId = FirstNonEmpty(recipientId, accountId);
            var pageMismatch = !string.IsNullOrWhiteSpace(configuredPageId)
                               && !string.IsNullOrWhiteSpace(webhookPageId)
                               && !string.Equals(configuredPageId, webhookPageId, StringComparison.Ordinal);

            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "GetFacebookProfile",
                new InvalidOperationException("Meta no devolvio el perfil de Facebook Messenger."),
                "No se pudo obtener el nombre y foto de un contacto de Facebook Messenger.",
                new
                {
                    SenderId = senderId,
                    RecipientId = recipientId,
                    EntryPageId = accountId,
                    ConfiguredPageId = configuredPageId,
                    PageMismatch = pageMismatch,
                    Diagnostico = pageMismatch
                        ? "El mensaje entro por una Page distinta a la configurada. Usar el Page Access Token y PageId de RecipientId/EntryPageId."
                        : "Meta rechazo el PSID para el token configurado. Revisar que CONV_FACEBOOK_ACCESS_TOKEN sea Page Access Token de esa misma pagina y tenga permisos de Messenger.",
                    Intentos = failures.Count == 0 ? ["Meta devolvio respuesta exitosa sin datos de perfil."] : failures
                },
                AppEventSeverity.Warning,
                ct);
        }
        catch
        {
        }
    }

    private static string ReadFacebookPictureUrl(JsonElement root)
    {
        if (!root.TryGetProperty("picture", out var picture) || picture.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!picture.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return string.Empty;

        return ReadJsonString(data, "url");
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private async Task<long> EnsureMercadoLibreConversationAsync(MercadoLibreQuestion question, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question.QuestionId))
            throw new InvalidOperationException("Mercado Libre no informÃ³ el ID de la pregunta.");

        var targetState = question.IsAnswered
            ? await GetFirstClosedConversationStateAsync(ct)
            : "ABIERTA";

        const string selectSql = """
            SELECT TOP (1) IdConversacion
            FROM dbo.CONV_CONVERSACIONES WITH (UPDLOCK, HOLDLOCK)
            WHERE Canal = N'MERCADOLIBRE'
              AND IdentificadorExternoConversacion = @QuestionId
            ORDER BY FechaHoraUltimoMensaje DESC, IdConversacion DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await using (var selectCmd = new SqlCommand(selectSql, cn, tx))
        {
            selectCmd.Parameters.AddWithValue("@QuestionId", question.QuestionId);
            var existing = await selectCmd.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
            {
                var existingId = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                const string updateSql = """
                    UPDATE dbo.CONV_CONVERSACIONES
                    SET NombreVisible = COALESCE(NULLIF(@NombreVisible, N''), NombreVisible),
                        TelefonoWhatsApp = COALESCE(NULLIF(@BuyerDisplayName, N''), TelefonoWhatsApp),
                        IdentificadorExternoContacto = COALESCE(NULLIF(@BuyerId, N''), IdentificadorExternoContacto),
                        IdentificadorExternoConversacion = COALESCE(NULLIF(@QuestionId, N''), IdentificadorExternoConversacion),
                        UsuarioExterno = COALESCE(NULLIF(@ItemId, N''), UsuarioExterno),
                        FotoPerfilUrl = COALESCE(NULLIF(@FotoPerfilUrl, N''), FotoPerfilUrl),
                        FechaHoraPerfilExterno = CASE WHEN @TieneItem = 1 THEN GETDATE() ELSE FechaHoraPerfilExterno END,
                        CodigoEstado = @CodigoEstado,
                        IdTecnico = CASE WHEN @PreguntaRespondida = 1 THEN NULL ELSE IdTecnico END,
                        FechaHoraCierre = CASE WHEN @PreguntaRespondida = 1 THEN ISNULL(FechaHoraCierre, GETDATE()) ELSE NULL END,
                        Archivada = 0,
                        ResumenUltimoMensaje = COALESCE(NULLIF(@ResumenUltimoMensaje, N''), ResumenUltimoMensaje),
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdConversacion = @IdConversacion;
                    """;

                await using var updateCmd = new SqlCommand(updateSql, cn, tx);
                updateCmd.Parameters.AddWithValue("@IdConversacion", existingId);
                AddMercadoLibreConversationParameters(updateCmd, question);
                updateCmd.Parameters.AddWithValue("@QuestionId", question.QuestionId);
                updateCmd.Parameters.AddWithValue("@CodigoEstado", targetState);
                updateCmd.Parameters.AddWithValue("@PreguntaRespondida", question.IsAnswered);
                await updateCmd.ExecuteNonQueryAsync(ct);
                await MoveMercadoLibreQuestionMessagesAsync(cn, tx, existingId, question.QuestionId, ct);

                await tx.CommitAsync(ct);
                return existingId;
            }
        }

        const string insertSql = """
            INSERT INTO dbo.CONV_CONVERSACIONES
            (
                Canal,
                NombreVisible,
                TelefonoWhatsApp,
                IdentificadorExternoContacto,
                IdentificadorExternoConversacion,
                UsuarioExterno,
                FotoPerfilUrl,
                FechaHoraPerfilExterno,
                CodigoEstado,
                FechaHoraCierre,
                ResumenUltimoMensaje,
                FechaHoraPrimerMensaje,
                FechaHoraUltimoMensaje,
                FechaHora_Grabacion
            )
            VALUES
            (
                N'MERCADOLIBRE',
                @NombreVisible,
                @BuyerDisplayName,
                @BuyerId,
                @QuestionId,
                @ItemId,
                @FotoPerfilUrl,
                CASE WHEN @TieneItem = 1 THEN GETDATE() ELSE NULL END,
                @CodigoEstado,
                CASE WHEN @PreguntaRespondida = 1 THEN GETDATE() ELSE NULL END,
                @ResumenUltimoMensaje,
                @FechaHora,
                @FechaHora,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var insertCmd = new SqlCommand(insertSql, cn, tx);
        AddMercadoLibreConversationParameters(insertCmd, question);
        insertCmd.Parameters.AddWithValue("@QuestionId", question.QuestionId);
        insertCmd.Parameters.AddWithValue("@CodigoEstado", targetState);
        insertCmd.Parameters.AddWithValue("@PreguntaRespondida", question.IsAnswered);
        insertCmd.Parameters.AddWithValue("@FechaHora", question.Timestamp);
        var result = await insertCmd.ExecuteScalarAsync(ct);
        var conversationId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        await MoveMercadoLibreQuestionMessagesAsync(cn, tx, conversationId, question.QuestionId, ct);
        await tx.CommitAsync(ct);
        return conversationId;
    }

    private static void AddMercadoLibreConversationParameters(SqlCommand command, MercadoLibreQuestion question)
    {
        command.Parameters.AddWithValue("@NombreVisible", DbNullable(question.DisplayName));
        command.Parameters.AddWithValue("@BuyerDisplayName", DbNullable(question.BuyerDisplayName));
        command.Parameters.AddWithValue("@BuyerId", DbNullable(question.BuyerId));
        command.Parameters.AddWithValue("@ItemId", DbNullable(question.ItemId));
        command.Parameters.AddWithValue("@FotoPerfilUrl", DbNullable(question.ItemPictureUrl));
        command.Parameters.AddWithValue("@TieneItem", question.HasItemMetadata);
        command.Parameters.AddWithValue("@ResumenUltimoMensaje", DbNullable(TrimForSummary(question.Text)));
    }

    private static async Task MoveMercadoLibreQuestionMessagesAsync(
        SqlConnection cn,
        SqlTransaction tx,
        long targetConversationId,
        string questionId,
        CancellationToken ct)
    {
        if (targetConversationId <= 0 || string.IsNullOrWhiteSpace(questionId))
            return;

        const string moveIncomingSql = """
            UPDATE m
            SET IdConversacion = @TargetConversationId
            FROM dbo.CONV_MENSAJES m
            WHERE m.MessageIdExterno = @QuestionMessageId
              AND m.IdConversacion <> @TargetConversationId
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.CONV_MENSAJES x
                  WHERE x.IdConversacion = @TargetConversationId
                    AND x.MessageIdExterno = m.MessageIdExterno
              );
            """;

        const string deleteDuplicatedIncomingSql = """
            DELETE m
            FROM dbo.CONV_MENSAJES m
            WHERE m.MessageIdExterno = @QuestionMessageId
              AND m.IdConversacion <> @TargetConversationId
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.CONV_MENSAJES x
                  WHERE x.IdConversacion = @TargetConversationId
                    AND x.MessageIdExterno = m.MessageIdExterno
              );
            """;

        const string moveAnswersSql = """
            UPDATE m
            SET IdConversacion = @TargetConversationId
            FROM dbo.CONV_MENSAJES m
            WHERE m.MessageIdExterno LIKE @AnswerMessagePrefix
              AND m.IdConversacion <> @TargetConversationId
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.CONV_MENSAJES x
                  WHERE x.IdConversacion = @TargetConversationId
                    AND x.MessageIdExterno = m.MessageIdExterno
              );
            """;

        var questionMessageId = $"meli-question-{questionId.Trim()}";
        var answerMessagePrefix = $"meli-answer-{questionId.Trim()}-%";

        await using (var moveIncomingCmd = new SqlCommand(moveIncomingSql, cn, tx))
        {
            moveIncomingCmd.Parameters.AddWithValue("@TargetConversationId", targetConversationId);
            moveIncomingCmd.Parameters.AddWithValue("@QuestionMessageId", questionMessageId);
            await moveIncomingCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteIncomingCmd = new SqlCommand(deleteDuplicatedIncomingSql, cn, tx))
        {
            deleteIncomingCmd.Parameters.AddWithValue("@TargetConversationId", targetConversationId);
            deleteIncomingCmd.Parameters.AddWithValue("@QuestionMessageId", questionMessageId);
            await deleteIncomingCmd.ExecuteNonQueryAsync(ct);
        }

        await using var moveAnswersCmd = new SqlCommand(moveAnswersSql, cn, tx);
        moveAnswersCmd.Parameters.AddWithValue("@TargetConversationId", targetConversationId);
        moveAnswersCmd.Parameters.AddWithValue("@AnswerMessagePrefix", answerMessagePrefix);
        await moveAnswersCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkMercadoLibreQuestionAnsweredAsync(long conversationId, string questionId, CancellationToken ct)
    {
        if (conversationId <= 0 || string.IsNullOrWhiteSpace(questionId))
            return;

        var closedState = await GetFirstClosedConversationStateAsync(ct);
        const string sql = """
            UPDATE dbo.CONV_CONVERSACIONES
            SET
                CodigoEstado = @CodigoEstado,
                IdTecnico = NULL,
                FechaHoraCierre = ISNULL(FechaHoraCierre, GETDATE()),
                FechaHora_Modificacion = GETDATE()
            WHERE IdConversacion = @IdConversacion
              AND Canal = N'MERCADOLIBRE';

            UPDATE dbo.CONV_MENSAJES
            SET
                PayloadJson = CASE
                    WHEN ISJSON(PayloadJson) = 1
                        THEN JSON_MODIFY(PayloadJson, '$.question_status', 'ANSWERED')
                    ELSE PayloadJson
                END,
                FechaHora_Modificacion = GETDATE()
            WHERE IdConversacion = @IdConversacion
              AND MessageIdExterno = @QuestionMessageId;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", conversationId);
        cmd.Parameters.AddWithValue("@CodigoEstado", closedState);
        cmd.Parameters.AddWithValue("@QuestionMessageId", $"meli-question-{questionId.Trim()}");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkLocalMercadoLibreAnsweredQuestionsAsync(CancellationToken ct)
    {
        var closedState = await GetFirstClosedConversationStateAsync(ct);
        var sql = $"""
            UPDATE c
            SET
                CodigoEstado = @CodigoEstado,
                IdTecnico = NULL,
                FechaHoraCierre = ISNULL(c.FechaHoraCierre, GETDATE()),
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            WHERE c.Canal = N'MERCADOLIBRE'
              AND {MercadoLibreAnsweredExistsSql};

            UPDATE m
            SET
                PayloadJson = CASE
                    WHEN ISJSON(m.PayloadJson) = 1
                        THEN JSON_MODIFY(m.PayloadJson, '$.question_status', 'ANSWERED')
                    ELSE m.PayloadJson
                END,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_MENSAJES m
            INNER JOIN dbo.CONV_CONVERSACIONES c
                ON c.IdConversacion = m.IdConversacion
            WHERE c.Canal = N'MERCADOLIBRE'
              AND m.MessageIdExterno = CONCAT(N'meli-question-', c.IdentificadorExternoConversacion)
              AND {MercadoLibreAnsweredExistsSql};
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CodigoEstado", closedState);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddFacebookProfileParameters(SqlCommand command, FacebookProfile profile)
    {
        command.Parameters.AddWithValue("@NombreVisible", DbNullable(profile.DisplayName));
        command.Parameters.AddWithValue("@UsuarioExterno", DBNull.Value);
        command.Parameters.AddWithValue("@FotoPerfilUrl", DbNullable(profile.ProfilePictureUrl));
        command.Parameters.AddWithValue("@TienePerfil", profile.HasData);
    }

    private static void AddInstagramProfileParameters(SqlCommand command, InstagramProfile profile)
    {
        command.Parameters.AddWithValue("@NombreVisible", DbNullable(profile.DisplayName));
        command.Parameters.AddWithValue("@UsuarioExterno", DbNullable(profile.Username));
        command.Parameters.AddWithValue("@FotoPerfilUrl", DbNullable(profile.ProfilePictureUrl));
        command.Parameters.AddWithValue("@SeguidoresExterno", profile.FollowerCount.HasValue ? profile.FollowerCount.Value : DBNull.Value);
        command.Parameters.AddWithValue("@PerfilExternoVerificado", profile.IsVerified.HasValue ? profile.IsVerified.Value : DBNull.Value);
        command.Parameters.AddWithValue("@UsuarioSigueCuenta", profile.IsUserFollowingBusiness.HasValue ? profile.IsUserFollowingBusiness.Value : DBNull.Value);
        command.Parameters.AddWithValue("@CuentaSigueUsuario", profile.IsBusinessFollowingUser.HasValue ? profile.IsBusinessFollowingUser.Value : DBNull.Value);
        command.Parameters.AddWithValue("@TienePerfil", profile.HasData);
    }

    private static bool? ReadOptionalJsonBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string BuildInstagramFallbackName(string senderId)
    {
        var normalized = senderId.Trim();
        var suffix = normalized.Length <= 6 ? normalized : normalized[^6..];
        return string.IsNullOrWhiteSpace(suffix) ? "Contacto de Instagram" : $"Instagram â€¦{suffix}";
    }

    private static string BuildFacebookFallbackName(string senderId)
    {
        var normalized = senderId.Trim();
        var suffix = normalized.Length <= 6 ? normalized : normalized[^6..];
        return string.IsNullOrWhiteSpace(suffix) ? "Contacto de Facebook" : $"Facebook ...{suffix}";
    }

    // Asigna la conversación al técnico SOLO si todavía no tiene ninguno (update condicional
    // atómico). Registra el movimiento en CONV_ASIGNACIONES. No pisa una asignación existente.
    private async Task AutoAssignIfUnassignedAsync(long idConversacion, string idTecnico, string? usuarioAccion, string? sistemaAccion, CancellationToken ct)
    {
        var technicianId = await ResolveTechnicianIdOrNullAsync(idTecnico, ct);
        if (string.IsNullOrWhiteSpace(technicianId))
            return;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            int affected;
            await using (var cmd = new SqlCommand("""
                UPDATE dbo.CONV_CONVERSACIONES
                SET IdTecnico = @IdTecnico, FechaHora_Modificacion = GETDATE()
                WHERE IdConversacion = @IdConversacion
                  AND NULLIF(LTRIM(RTRIM(ISNULL(IdTecnico, N''))), N'') IS NULL;
                """, cn, tx))
            {
                cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
                cmd.Parameters.AddWithValue("@IdTecnico", technicianId);
                affected = await cmd.ExecuteNonQueryAsync(ct);
            }

            if (affected > 0)
            {
                await using var histCmd = new SqlCommand("""
                    INSERT INTO dbo.CONV_ASIGNACIONES (IdConversacion, FechaHora, IdTecnico, UsuarioAccion, SistemaAccion, Observaciones)
                    VALUES (@IdConversacion, GETDATE(), @IdTecnico, @UsuarioAccion, @SistemaAccion, N'Auto-asignada al responder');
                    """, cn, tx);
                histCmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
                histCmd.Parameters.AddWithValue("@IdTecnico", technicianId);
                histCmd.Parameters.AddWithValue("@UsuarioAccion", DbNullable(usuarioAccion));
                histCmd.Parameters.AddWithValue("@SistemaAccion", DbNullable(sistemaAccion));
                await histCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<ConversationIdentity> RequireConversationAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                c.IdConversacion,
                ISNULL(c.TelefonoWhatsApp, ''),
                ISNULL(c.Canal, 'WHATSAPP'),
                ISNULL(c.IdentificadorExternoContacto, ''),
                ISNULL(c.IdentificadorExternoConversacion, ''),
                ISNULL(n.PhoneNumberId, ''),
                c.IdNumeroWhatsApp
            FROM dbo.CONV_CONVERSACIONES c
            LEFT JOIN dbo.CONV_WHATSAPP_NUMEROS n ON n.IdNumero = c.IdNumeroWhatsApp
            WHERE c.IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        if (!await rd.ReadAsync(ct))
            throw new InvalidOperationException("La conversaciÃ³n indicada no existe.");

        return new ConversationIdentity
        {
            IdConversacion = rd.GetInt64(0),
            TelefonoWhatsApp = GetString(rd, 1),
            Canal = GetString(rd, 2),
            IdentificadorExternoContacto = GetString(rd, 3),
            IdentificadorExternoConversacion = GetString(rd, 4),
            PhoneNumberId = GetString(rd, 5),
            IdNumeroWhatsApp = rd.IsDBNull(6) ? null : rd.GetInt32(6)
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
            throw new InvalidOperationException("El mensaje indicado no existe en esta conversaciÃ³n.");

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

    private async Task<bool> IsWhatsAppQrConversationAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.CONV_CONVERSACIONES c
            INNER JOIN dbo.CONV_WHATSAPP_NUMEROS n
                ON n.IdNumero = c.IdNumeroWhatsApp
            WHERE c.IdConversacion = @IdConversacion
              AND c.Canal = N'WHATSAPP'
              AND LTRIM(RTRIM(ISNULL(n.WebInstanceName, N''))) <> N''
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result is not DBNull;
    }

    private async Task<bool> IsInstagramWindowActiveAsync(long idConversacion, CancellationToken ct)
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

    private async Task AutoCloseExpiredOpenWhatsAppConversationsAsync(long? idConversacion, CancellationToken ct)
    {
        await ReassignStaleWhatsAppConversationsToDefaultWebAsync(idConversacion, ct);

        var closedState = await GetFirstClosedConversationStateAsync(ct);
        if (string.IsNullOrWhiteSpace(closedState))
            return;

        var sql = $"""
            SELECT TOP (50) c.IdConversacion
            FROM dbo.CONV_CONVERSACIONES c
            OUTER APPLY (
                SELECT TOP (1) {ConversationMessageVisibleDateSql("m")} AS FechaHoraUltimoMensajeCliente
                FROM dbo.CONV_MENSAJES m
                WHERE m.IdConversacion = c.IdConversacion
                  AND m.Direction = N'ENTRANTE'
                ORDER BY {ConversationMessageVisibleDateSql("m")} DESC, m.IdMensaje DESC
            ) ultCliente
            WHERE c.Canal = N'WHATSAPP'
              AND UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) = N'ABIERTA'
              AND (@IdConversacion IS NULL OR c.IdConversacion = @IdConversacion)
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_WHATSAPP_NUMEROS n
                    WHERE n.IdNumero = c.IdNumeroWhatsApp
                      AND LTRIM(RTRIM(ISNULL(n.WebInstanceName, N''))) <> N''
                      AND UPPER(LTRIM(RTRIM(ISNULL(n.WebSessionStatus, N'')))) = N'CONNECTED'
              )
              AND ultCliente.FechaHoraUltimoMensajeCliente IS NOT NULL
              AND DATEADD(hour, 24, ultCliente.FechaHoraUltimoMensajeCliente) < {BusinessNowSql}
            ORDER BY ultCliente.FechaHoraUltimoMensajeCliente, c.IdConversacion;
            """;

        var ids = new List<long>();
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion.HasValue ? idConversacion.Value : DBNull.Value);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                ids.Add(rd.GetInt64(0));
        }

        if (ids.Count == 0)
            return;

        var now = BusinessNow();
        var eventText = $"AlfaCore ha cerrado la conversaci\u00f3n el {now:dd/MM/yyyy HH:mm} porque super\u00f3 la ventana de 24 horas de WhatsApp.";

        foreach (var id in ids)
        {
            await ChangeStatusAsync(new ConversacionEstadoRequest
            {
                IdConversacion = id,
                CodigoEstado = closedState,
                NombreTecnicoAccion = "AlfaCore",
                UsuarioAccion = null,
                SistemaAccion = null,
                Observaciones = eventText
            }, ct);
        }
    }

    /// <summary>
    /// Automatizaciones "Nivel 0": si el mÃ³dulo estÃ¡ contratado, estÃ¡ activo en la configuraciÃ³n
    /// y el mensaje entrante llegÃ³ fuera del horario configurado, manda una respuesta fija â€”
    /// sin IA, sin aprobaciÃ³n de operador. No repite el mensaje mientras la Ãºltima salida de la
    /// conversaciÃ³n ya haya sido esta misma respuesta automÃ¡tica (evita spamear si el cliente
    /// sigue escribiendo fuera de horario). Nunca bloquea el procesamiento del webhook si falla.
    /// </summary>
    private async Task TryAutoReplyOutOfHoursAsync(long idConversacion, CancellationToken ct)
    {
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                return;

            await RunWithConversationAutomationLockAsync(
                idConversacion,
                "fuera-horario",
                async token =>
                {
                    var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(token).ConfigureAwait(false);
                    if (!config.IsConfigured)
                        return;

                    var businessNow = BusinessNow();
                    if (!IsOutsideBusinessHours(config, businessNow))
                        return;

                    // Si el asistente IA está configurado para atender fuera de horario, no mandamos el mensaje
                    // fijo: lo maneja el bot (que se presenta como IA, intenta ayudar y marca urgencias).
                    if (config.BotActivo && config.AsistenteFueraHorario && asistenteService.IsConfigured)
                        return;

                    if (await HasSentOutOfHoursNoticeAsync(idConversacion, businessNow, token).ConfigureAwait(false))
                        return;

                    await SendMessageAsync(new ConversacionSendMessageRequest
                    {
                        IdConversacion = idConversacion,
                        Texto = config.MensajeFueraHorario,
                        MessageType = "TEXT",
                        UsuarioAccion = "AlfaCore",
                        SistemaAccion = "AUTOMATIZACION"
                    }, token).ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "AutoReplyOutOfHours",
                ex,
                "No se pudo enviar la respuesta automÃ¡tica fuera de horario.",
                new { idConversacion },
                AppEventSeverity.Warning,
                ct).ConfigureAwait(false);
        }
    }

    // Entrada del bot autónomo desde el webhook: si hay que esperar antes de contestar (para darle
    // margen a un agente humano), no responde ahora — encola la conversación y la retoma el job de
    // espera. Si no, corre los guardarraíles y responde ya (ver EjecutarRespuestaBotAsync).
    private async Task TryAutoReplyBotAsync(long idConversacion, string? incomingText, CancellationToken ct)
    {
        await TraceDiagAsync("BotStart", idConversacion, ct).ConfigureAwait(false);
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
            {
                await TraceDiagAsync("BotStop:ModuloInactivo", idConversacion, ct).ConfigureAwait(false);
                return;
            }

            var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
            if (!config.BotActivo || !asistenteService.IsConfigured)
            {
                await TraceDiagAsync($"BotStop:BotActivo={config.BotActivo},OpenAiConfigured={asistenteService.IsConfigured}", idConversacion, ct).ConfigureAwait(false);
                return;
            }

            var texto = (incomingText ?? string.Empty).Trim();
            if (texto.Length == 0)
            {
                await TraceDiagAsync("BotStop:TextoVacio", idConversacion, ct).ConfigureAwait(false);
                return;
            }

            if (config.BotEsperaMinutos > 0)
            {
                await TraceDiagAsync("BotStop:Encolado", idConversacion, ct).ConfigureAwait(false);
                await EncolarRespuestaBotAsync(idConversacion, config.BotEsperaMinutos, ct).ConfigureAwait(false);
                return;
            }

            await TraceDiagAsync("BotSigueAEjecutar", idConversacion, ct).ConfigureAwait(false);

            await EjecutarRespuestaBotAsync(idConversacion, texto, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones", "BotAutoReply", ex,
                "No se pudo procesar la respuesta automática del bot.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
    }

    // Cola de espera (dbo.CONV_BOT_PENDIENTES): a lo sumo una fila PENDIENTE por conversación. Si el
    // cliente escribe de nuevo mientras se espera, no reprograma el reloj (para que no charlar
    // indefinidamente evite la respuesta del bot); el job de espera usa el mensaje más reciente al
    // momento de responder.
    private async Task EncolarRespuestaBotAsync(long idConversacion, int minutos, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            IF NOT EXISTS (
                SELECT 1 FROM dbo.CONV_BOT_PENDIENTES
                WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE')
            INSERT INTO dbo.CONV_BOT_PENDIENTES (IdConversacion, FechaProgramada)
            VALUES (@IdConversacion, @FechaProgramada);
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@FechaProgramada", BusinessNow().AddMinutes(minutos));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // Job en segundo plano (ver ConversacionesBotEsperaHostedService): retoma las conversaciones cuya
    // espera configurada ya se cumplió. Vuelve a correr todos los guardarraíles del bot con el mensaje
    // entrante más reciente — por si el cliente escribió de nuevo, o si un agente humano ya la tomó
    // mientras tanto (en cuyo caso el bot calla).
    public async Task<int> ProcesarRespuestasBotPendientesAsync(CancellationToken ct = default)
    {
        var pendientes = new List<(long IdPendiente, long IdConversacion)>();
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct).ConfigureAwait(false);
            const string sql = """
                IF OBJECT_ID(N'dbo.CONV_BOT_PENDIENTES', N'U') IS NOT NULL
                    SELECT TOP (200) IdPendiente, IdConversacion
                    FROM dbo.CONV_BOT_PENDIENTES
                    WHERE Estado = N'PENDIENTE' AND FechaProgramada <= @Ahora
                    ORDER BY FechaProgramada;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Ahora", BusinessNow());
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                pendientes.Add((rd.GetInt64(0), rd.GetInt64(1)));
        }

        if (pendientes.Count == 0)
            return 0;

        var procesados = 0;
        foreach (var (idPendiente, idConversacion) in pendientes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                {
                    var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
                    if (config.BotActivo && asistenteService.IsConfigured)
                    {
                        var texto = await GetLastIncomingTextAsync(idConversacion, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(texto))
                            await EjecutarRespuestaBotAsync(idConversacion, texto, config, ct).ConfigureAwait(false);
                    }
                }

                await MarcarBotPendienteProcesadoAsync(idPendiente, ct).ConfigureAwait(false);
                procesados++;
            }
            catch (Exception ex)
            {
                await _appEvents.LogErrorAsync(
                    "Conversaciones", "BotAutoReplyPendiente", ex,
                    "No se pudo procesar la respuesta en espera del bot.",
                    new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            }
        }

        return procesados;
    }

    private async Task<string> GetLastIncomingTextAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(CAST(Texto AS nvarchar(max)), '')
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @Id AND Direction = N'ENTRANTE'
            ORDER BY FechaHora DESC, IdMensaje DESC;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string ?? string.Empty;
    }

    private async Task MarcarBotPendienteProcesadoAsync(long idPendiente, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "UPDATE dbo.CONV_BOT_PENDIENTES SET Estado = N'PROCESADO', FechaProcesado = GETDATE() WHERE IdPendiente = @Id;", cn);
        cmd.Parameters.AddWithValue("@Id", idPendiente);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // Bot autónomo con guardarraíles. Responde con el Asistente IA (comportamiento + información
    // del negocio configurados, vía OpenAI) si: no hay palabras de escalado, la conversación está sin
    // asignar (si corresponde), estamos en el horario permitido (si "solo fuera de horario" está
    // activo), la ventana del canal está activa, no se superó el tope de respuestas y no hay otra
    // automática encima. La política del asistente decide qué pasa cuando la info no alcanza; si no
    // puede resolver, escala a humano. Se llama tanto al llegar el mensaje (sin espera configurada)
    // como desde el job de espera, ya con todos los chequeos de activación resueltos.
    private async Task EjecutarRespuestaBotAsync(long idConversacion, string texto, ConversacionAutomatizacionesConfigDto config, CancellationToken ct)
    {
        await RunWithConversationAutomationLockAsync(
            idConversacion,
            "bot-auto-reply",
            async token =>
            {
                await TraceDiagAsync("LockOk:EjecutarStart", idConversacion, ct).ConfigureAwait(false);

                if (ContienePalabraEscalado(texto, config.BotPalabrasEscalado))
                {
                    await TraceDiagAsync("EjecutarStop:PalabraEscalado", idConversacion, ct).ConfigureAwait(false);
                    return;
                }

                if (config.BotSoloSinAsignar)
                {
                    var tecnico = await GetConversationTechnicianIdAsync(idConversacion, token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tecnico))
                    {
                        await TraceDiagAsync($"EjecutarStop:TecnicoAsignado={tecnico}", idConversacion, ct).ConfigureAwait(false);
                        return;
                    }
                }

                var canalBot = await GetConversationChannelAsync(idConversacion, token).ConfigureAwait(false);
                if (!await IsSendWindowActiveAsync(idConversacion, canalBot, token).ConfigureAwait(false))
                {
                    await TraceDiagAsync($"EjecutarStop:VentanaInactiva:{canalBot}", idConversacion, ct).ConfigureAwait(false);
                    return;
                }

                var (botCount, yaRespondioEsteMensaje) = await GetBotReplyStatsAsync(idConversacion, token).ConfigureAwait(false);
                if (botCount >= Math.Max(1, config.BotMaxRespuestas))
                {
                    await TraceDiagAsync($"EjecutarStop:MaxRespuestas={botCount}", idConversacion, ct).ConfigureAwait(false);
                    return;
                }
                if (yaRespondioEsteMensaje)
                {
                    await TraceDiagAsync("EjecutarStop:YaRespondioEsteMensaje", idConversacion, ct).ConfigureAwait(false);
                    return;
                }

                var fueraDeHorario = config.IsConfigured && IsOutsideBusinessHours(config, BusinessNow());
                if (config.BotSoloFueraHorario && !fueraDeHorario)
                {
                    await TraceDiagAsync("EjecutarStop:SoloFueraHorario", idConversacion, ct).ConfigureAwait(false);
                    return;
                }

                await TraceDiagAsync("EjecutarSigueALlamarOpenAi", idConversacion, ct).ConfigureAwait(false);

                var esUrgente = ContienePalabraEscalado(texto, config.AsistenteUrgenciaPalabras);
                var (rubro, esPrioritario) = await ObtenerContextoClienteAsync(idConversacion, token).ConfigureAwait(false);

                if (esUrgente)
                    await SubirPrioridadAsync(idConversacion, "URGENTE", token).ConfigureAwait(false);
                else if (esPrioritario)
                    await SubirPrioridadAsync(idConversacion, "ALTA", token).ConfigureAwait(false);

                if (fueraDeHorario && (esUrgente || esPrioritario))
                {
                    var motivo = esUrgente ? "posible URGENCIA" : "cliente prioritario";
                    var conRubro = string.IsNullOrWhiteSpace(rubro) ? string.Empty : $" (rubro: {rubro})";
                    await AddInternalEventCoreAsync(idConversacion,
                        $"⏰🚨 Fuera de horario, {motivo}{conRubro}. El cliente escribió: \"{Truncar(texto, 200)}\". Revisar para atención inmediata.",
                        null, null, "AlfaCore", "URGENCIA", token).ConfigureAwait(false);
                }

                var mensajes = await GetRecentMessagesForBotAsync(idConversacion, token).ConfigureAwait(false);
                await TraceDiagAsync("Paso:MensajesOk", idConversacion, ct).ConfigureAwait(false);
                var contextoCliente = ConstruirContextoCliente(rubro, esPrioritario);
                var cuentaVinculada = await ResolverCuentaVinculadaAsync(idConversacion, token).ConfigureAwait(false);
                await TraceDiagAsync("Paso:CuentaVinculadaOk", idConversacion, ct).ConfigureAwait(false);
                var herramientas = asistenteHerramientasService.ObtenerHerramientasDisponibles(config, cuentaVinculada, texto);
                Func<string, string, CancellationToken, Task<string>>? ejecutarHerramientaAsync = herramientas.Count > 0
                    ? (nombreHerramienta, argumentosJson, ctHerramienta) =>
                        asistenteHerramientasService.EjecutarAsync(nombreHerramienta, argumentosJson, cuentaVinculada, ctHerramienta)
                    : null;

                // Un saludo o un cierre/agradecimiento suelto no tiene nada que buscar -- consultarlo
                // igual en AlfaKnowledge puede traer una cita débil (ej. la página de presentación del
                // bot) que después se le adjunta como link al cliente, aunque no preguntó nada. Se deja
                // resolver por OpenAI directo, que ya sabe responder sin inventar un link.
                var usaKnowledge = !EsMensajeSocial(texto) && config.AsistenteUsaKnowledge && alfaKnowledgeService.IsConfigured;
                await TraceDiagAsync($"Paso:AntesKnowledge:usaKnowledge={usaKnowledge}", idConversacion, ct).ConfigureAwait(false);
                var knowledgeContext = usaKnowledge
                    ? await ObtenerConocimientoBaseAsync(texto, mensajes, idConversacion, token).ConfigureAwait(false)
                    : new AsistenteKnowledgeContext();
                await TraceDiagAsync("Paso:DespuesKnowledgeOk", idConversacion, ct).ConfigureAwait(false);

                // Modo exclusivo automático: si AlfaKnowledge ya tiene contexto suficiente y el mensaje
                // no necesita herramientas (saldo/precio/pedido -- eso son datos reales de AlfaCore que
                // AlfaKnowledge no tiene), se responde directo con lo que trajo AlfaKnowledge sin llamar
                // a OpenAI. Es la fuente de verdad para consultas de conocimiento, y evita pagar tokens
                // de un modelo cuya respuesta se iba a descartar igual (además de que puede "alucinar"
                // contenido plausible: ver caso real donde inventó un video de YouTube en vez de
                // linkear el manual real que AlfaKnowledge sí tenía).
                var resueltoSoloPorKnowledge = herramientas.Count == 0
                    && knowledgeContext.HasSufficientContext
                    && !string.IsNullOrWhiteSpace(knowledgeContext.SuggestedReply);
                await TraceDiagAsync($"Paso:Rama:resueltoSoloPorKnowledge={resueltoSoloPorKnowledge}", idConversacion, ct).ConfigureAwait(false);

                ConversacionAsistenteRespuesta? result;
                if (resueltoSoloPorKnowledge)
                {
                    var respuestaConLink = knowledgeContext.SuggestedReply;
                    if (!string.IsNullOrWhiteSpace(knowledgeContext.LinkPublico)
                        && !respuestaConLink.Contains(knowledgeContext.LinkPublico, StringComparison.OrdinalIgnoreCase))
                    {
                        respuestaConLink = $"{respuestaConLink}\n\n{knowledgeContext.LinkPublico}";
                    }

                    result = new ConversacionAsistenteRespuesta
                    {
                        Tipo = "RESUELVE",
                        PuedeResponder = true,
                        Respuesta = respuestaConLink
                    };
                }
                else
                {
                    await TraceDiagAsync($"Paso:AntesResponderAsync:herramientas={herramientas.Count}", idConversacion, ct).ConfigureAwait(false);
                    result = await asistenteService.ResponderAsync(
                        config.AsistenteComportamiento, config.AsistenteInformacion, config.AsistentePolitica,
                        texto, mensajes, fueraDeHorario, esUrgente, knowledgeContext.ConocimientoBase, knowledgeContext.SuggestedReply, contextoCliente,
                        herramientas, ejecutarHerramientaAsync, token).ConfigureAwait(false);
                    await TraceDiagAsync($"Paso:DespuesResponderAsync:resultNull={result is null}", idConversacion, ct).ConfigureAwait(false);

                    if ((result is null || string.IsNullOrWhiteSpace(result.Respuesta))
                        && knowledgeContext.NeedsClarification
                        && !string.IsNullOrWhiteSpace(knowledgeContext.ClarificationQuestion))
                    {
                        result = new ConversacionAsistenteRespuesta
                        {
                            Tipo = "ACLARA",
                            PuedeResponder = false,
                            Respuesta = knowledgeContext.ClarificationQuestion
                        };
                    }
                }

                var tipo = (result?.Tipo ?? "DERIVA").ToUpperInvariant();
                var usoFallbackBot = result is null || string.IsNullOrWhiteSpace(result.Respuesta);
                var respuesta = !usoFallbackBot
                    ? result!.Respuesta.Trim()
                    : "Gracias por tu mensaje. Lo estoy viendo con un compañero y te respondemos en un ratito 🙂";
                if (usoFallbackBot)
                    tipo = "DERIVA";

                // El bot escala en silencio (nunca manda EscalationHint al cliente), pero deja la
                // pista en el timeline interno para que el humano que retome sepa que hay
                // documentación marcada "uso interno" en AlfaKnowledge relacionada con esta consulta.
                if (usoFallbackBot && !string.IsNullOrWhiteSpace(knowledgeContext.EscalationHint))
                {
                    await AddInternalEventCoreAsync(idConversacion,
                        $"📚 {knowledgeContext.EscalationHint}",
                        null, null, "AlfaCore", "ALFAKNOWLEDGE", token).ConfigureAwait(false);
                }

                var businessNow = BusinessNow();
                if (usoFallbackBot
                    && await HasSentAutomaticMessageTextAsync(idConversacion, "BOT", respuesta, businessNow, token).ConfigureAwait(false))
                {
                    await _appEvents.LogAuditAsync(
                        "Conversaciones", "BotFallbackDuplicadoOmitido", "CONV_CONVERSACIONES",
                        idConversacion.ToString(CultureInfo.InvariantCulture),
                        "Se omitió una contención repetida del bot porque ya se había enviado hoy en esta conversación.",
                        new { idConversacion, fueraDeHorario, respuesta }, token).ConfigureAwait(false);
                    return;
                }

                await TraceDiagAsync($"Paso:AntesSendMessage:tipo={tipo}", idConversacion, ct).ConfigureAwait(false);
                await SendMessageAsync(new ConversacionSendMessageRequest
                {
                    IdConversacion = idConversacion,
                    Texto = respuesta,
                    MessageType = "TEXT",
                    UsuarioAccion = "AlfaCore",
                    SistemaAccion = "BOT"
                }, token).ConfigureAwait(false);
                await TraceDiagAsync("Paso:DespuesSendMessageOk", idConversacion, ct).ConfigureAwait(false);

                if (tipo == "ACLARA")
                {
                    await _appEvents.LogAuditAsync(
                        "Conversaciones", "BotAclaracion", "CONV_CONVERSACIONES",
                        idConversacion.ToString(CultureInfo.InvariantCulture),
                        "El asistente le pidió una aclaración al cliente para poder resolver.",
                        new { idConversacion, config.AsistentePolitica }, token).ConfigureAwait(false);
                }
                else if (tipo == "DERIVA")
                {
                    if (!fueraDeHorario)
                    {
                        await AddInternalEventCoreAsync(idConversacion,
                            "🤖➡️👤 El asistente no pudo resolver y le mandó una contención al cliente. Requiere que un operador lo tome.",
                            null, null, "AlfaCore", "BOT", token).ConfigureAwait(false);
                        if (!esUrgente && !esPrioritario)
                            await SubirPrioridadAsync(idConversacion, "MEDIA", token).ConfigureAwait(false);
                    }
                    await _appEvents.LogAuditAsync(
                        "Conversaciones", "BotHandoff", "CONV_CONVERSACIONES",
                        idConversacion.ToString(CultureInfo.InvariantCulture),
                        "El asistente mandó una contención y derivó a un humano (no pudo resolver).",
                        new { idConversacion, config.AsistentePolitica, fueraDeHorario }, token).ConfigureAwait(false);
                }
                else
                {
                    await _appEvents.LogAuditAsync(
                        "Conversaciones", "BotAutoReply", "CONV_CONVERSACIONES",
                        idConversacion.ToString(CultureInfo.InvariantCulture),
                        "El asistente respondió automáticamente.",
                        new { idConversacion, config.AsistentePolitica, fueraDeHorario, esUrgente }, token).ConfigureAwait(false);
                }
            },
            ct).ConfigureAwait(false);
    }

    // ===================== Mensajes programados (envío diferido) =====================

    public Task<long> ProgramarMensajeAsync(ConversacionProgramarRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "ProgramarMensaje", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.IdConversacion <= 0)
                throw new InvalidOperationException("La conversación es obligatoria.");

            var tipo = string.Equals(request.TipoEnvio, "PLANTILLA", StringComparison.OrdinalIgnoreCase) ? "PLANTILLA" : "TEXTO";
            if (tipo == "TEXTO" && string.IsNullOrWhiteSpace(request.Texto))
                throw new InvalidOperationException("Escribí el mensaje a programar.");
            if (tipo == "PLANTILLA" && (request.IdPlantilla is null || request.IdPlantilla <= 0))
                throw new InvalidOperationException("Elegí una plantilla para programar fuera de la ventana de 24 h.");
            if (request.FechaProgramada <= BusinessNow())
                throw new InvalidOperationException("La fecha y hora deben ser futuras.");

            var conversation = await RequireConversationAsync(request.IdConversacion, token);
            if (!string.Equals(conversation.Canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Por ahora solo se pueden programar mensajes de WhatsApp.");

            var valoresJson = JsonSerializer.Serialize(request.ValoresVariables ?? []);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            const string sql = """
                INSERT INTO dbo.CONV_MENSAJES_PROGRAMADOS
                    (IdConversacion, FechaProgramada, TipoEnvio, Texto, IdPlantilla, ValoresVariables,
                     Estado, IdTecnicoAutor, UsuarioAutor, FechaCreacion)
                VALUES
                    (@IdConversacion, @FechaProgramada, @TipoEnvio, @Texto, @IdPlantilla, @ValoresVariables,
                     N'PENDIENTE', @IdTecnicoAutor, @UsuarioAutor, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", request.IdConversacion);
            cmd.Parameters.AddWithValue("@FechaProgramada", request.FechaProgramada);
            cmd.Parameters.AddWithValue("@TipoEnvio", tipo);
            cmd.Parameters.AddWithValue("@Texto", (request.Texto ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@IdPlantilla", tipo == "PLANTILLA" ? request.IdPlantilla!.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ValoresVariables", valoresJson);
            cmd.Parameters.AddWithValue("@IdTecnicoAutor", DbNullable(request.IdTecnicoAutor));
            cmd.Parameters.AddWithValue("@UsuarioAutor", DbNullable(request.UsuarioAccion));
            var result = await cmd.ExecuteScalarAsync(token);
            var id = result is long l ? l : Convert.ToInt64(result, CultureInfo.InvariantCulture);

            await _appEvents.LogAuditAsync(
                "Conversaciones", "ProgramarMensaje", "CONV_MENSAJES_PROGRAMADOS",
                id.ToString(CultureInfo.InvariantCulture),
                "Se programó un mensaje para envío diferido.",
                new { request.IdConversacion, request.FechaProgramada, tipo }, token);

            return id;
        }, "No se pudo programar el mensaje.", ct);

    public Task<IReadOnlyList<ConversacionMensajeProgramadoDto>> GetMensajesProgramadosAsync(long idConversacion, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "GetMensajesProgramados", async token =>
        {
            var items = new List<ConversacionMensajeProgramadoDto>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                IF OBJECT_ID(N'dbo.CONV_MENSAJES_PROGRAMADOS', N'U') IS NULL
                    SELECT TOP (0) CAST(0 AS bigint) AS IdProgramado, CAST(0 AS bigint) AS IdConversacion,
                        CAST(GETDATE() AS datetime) AS FechaProgramada, N'' AS TipoEnvio, N'' AS Texto,
                        CAST(NULL AS bigint) AS IdPlantilla, N'' AS PlantillaNombre, N'' AS Estado,
                        N'' AS MotivoError, N'' AS UsuarioAutor, CAST(GETDATE() AS datetime) AS FechaCreacion
                ELSE
                    SELECT p.IdProgramado, p.IdConversacion, p.FechaProgramada, p.TipoEnvio, ISNULL(p.Texto, N''),
                        p.IdPlantilla, ISNULL(t.NombreVisible, N''), p.Estado, ISNULL(p.MotivoError, N''),
                        ISNULL(p.UsuarioAutor, N''), p.FechaCreacion
                    FROM dbo.CONV_MENSAJES_PROGRAMADOS p
                    LEFT JOIN dbo.CONV_PLANTILLAS t ON t.IdPlantilla = p.IdPlantilla
                    WHERE p.IdConversacion = @IdConversacion
                      AND (p.Estado = N'PENDIENTE' OR p.FechaCreacion >= DATEADD(day, -7, GETDATE()))
                    ORDER BY CASE WHEN p.Estado = N'PENDIENTE' THEN 0 ELSE 1 END, p.FechaProgramada;
                """;
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            await using var rd = await cmd.ExecuteReaderAsync(token);
            while (await rd.ReadAsync(token))
            {
                items.Add(new ConversacionMensajeProgramadoDto
                {
                    IdProgramado = rd.GetInt64(0),
                    IdConversacion = rd.GetInt64(1),
                    FechaProgramada = rd.GetDateTime(2),
                    TipoEnvio = GetString(rd, 3),
                    Texto = GetString(rd, 4),
                    IdPlantilla = rd.IsDBNull(5) ? null : rd.GetInt64(5),
                    PlantillaNombre = GetString(rd, 6),
                    Estado = GetString(rd, 7),
                    MotivoError = GetString(rd, 8),
                    UsuarioAutor = GetString(rd, 9),
                    FechaCreacion = rd.GetDateTime(10)
                });
            }

            return (IReadOnlyList<ConversacionMensajeProgramadoDto>)items;
        }, "No se pudieron cargar los mensajes programados.", ct);

    public Task CancelarMensajeProgramadoAsync(long idProgramado, CancellationToken ct = default)
        => ExecuteLoggedAsync("Conversaciones", "CancelarMensajeProgramado", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var cmd = new SqlCommand(
                "UPDATE dbo.CONV_MENSAJES_PROGRAMADOS SET Estado = N'CANCELADO' WHERE IdProgramado = @Id AND Estado = N'PENDIENTE';", cn);
            cmd.Parameters.AddWithValue("@Id", idProgramado);
            await cmd.ExecuteNonQueryAsync(token);
            return true;
        }, "No se pudo cancelar el mensaje programado.", ct);

    public async Task<DateTime?> GetVentanaCierreWhatsAppAsync(long idConversacion, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) FechaHora FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @Id AND Direction = N'ENTRANTE'
            ORDER BY FechaHora DESC, IdMensaje DESC;
            """, cn);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            return null;
        return NormalizeStoredConversationTime(Convert.ToDateTime(result, CultureInfo.InvariantCulture)).AddHours(24);
    }

    // Job en segundo plano: envía los mensajes programados que llegaron a su hora. TEXTO solo si la
    // ventana de WhatsApp sigue abierta; si está cerrada, marca error (debería haberse programado como
    // PLANTILLA). PLANTILLA se envía siempre (es lo válido fuera de la ventana).
    public async Task<int> ProcesarMensajesProgramadosAsync(CancellationToken ct = default)
    {
        int enviados = 0;
        List<(long Id, long IdConv, string Tipo, string Texto, long? IdPlantilla, string Valores, string IdTecnico, string Usuario)> pendientes = new();
        try
        {
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                const string sql = """
                    IF OBJECT_ID(N'dbo.CONV_MENSAJES_PROGRAMADOS', N'U') IS NULL RETURN;
                    SELECT TOP (50) IdProgramado, IdConversacion, TipoEnvio, ISNULL(Texto, N''), IdPlantilla,
                        ISNULL(ValoresVariables, N'[]'), ISNULL(LTRIM(RTRIM(IdTecnicoAutor)), N''), ISNULL(UsuarioAutor, N'')
                    FROM dbo.CONV_MENSAJES_PROGRAMADOS
                    WHERE Estado = N'PENDIENTE' AND FechaProgramada <= GETDATE()
                    ORDER BY FechaProgramada;
                    """;
                await using var cmd = new SqlCommand(sql, cn);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                    pendientes.Add((rd.GetInt64(0), rd.GetInt64(1), GetString(rd, 2), GetString(rd, 3),
                        rd.IsDBNull(4) ? null : rd.GetInt64(4), GetString(rd, 5), GetString(rd, 6), GetString(rd, 7)));
            }

            foreach (var p in pendientes)
            {
                try
                {
                    if (string.Equals(p.Tipo, "PLANTILLA", StringComparison.OrdinalIgnoreCase))
                    {
                        var valores = JsonSerializer.Deserialize<List<string>>(p.Valores) ?? [];
                        await SendTemplateMessageAsync(new ConversacionPlantillaSendRequest
                        {
                            IdConversacion = p.IdConv,
                            IdPlantilla = p.IdPlantilla ?? 0,
                            ValoresVariables = valores,
                            IdTecnicoAutor = string.IsNullOrWhiteSpace(p.IdTecnico) ? null : p.IdTecnico,
                            UsuarioAccion = string.IsNullOrWhiteSpace(p.Usuario) ? "AlfaCore" : p.Usuario,
                            SistemaAccion = "PROGRAMADO"
                        }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var windowActive = await IsWhatsAppQrConversationAsync(p.IdConv, ct).ConfigureAwait(false)
                            || await IsWhatsAppWindowActiveAsync(p.IdConv, ct).ConfigureAwait(false);
                        if (!windowActive)
                        {
                            await MarcarProgramadoAsync(p.Id, "ERROR",
                                "La ventana de 24 h de WhatsApp está vencida; no se pudo enviar el mensaje programado (no se eligió plantilla).", ct).ConfigureAwait(false);
                            await AddInternalEventCoreAsync(p.IdConv,
                                "⏰ No se pudo enviar un mensaje programado: la ventana de WhatsApp estaba vencida y no se eligió una plantilla.",
                                null, null, "AlfaCore", "PROGRAMADO", ct).ConfigureAwait(false);
                            continue;
                        }

                        await SendMessageAsync(new ConversacionSendMessageRequest
                        {
                            IdConversacion = p.IdConv,
                            Texto = p.Texto,
                            MessageType = "TEXT",
                            UsuarioAccion = string.IsNullOrWhiteSpace(p.Usuario) ? "AlfaCore" : p.Usuario,
                            SistemaAccion = "PROGRAMADO",
                            IdTecnicoAutor = string.IsNullOrWhiteSpace(p.IdTecnico) ? null : p.IdTecnico
                        }, ct).ConfigureAwait(false);
                    }

                    await MarcarProgramadoAsync(p.Id, "ENVIADO", null, ct).ConfigureAwait(false);
                    enviados++;
                }
                catch (Exception ex)
                {
                    await MarcarProgramadoAsync(p.Id, "ERROR", Truncar(ex.Message, 480), ct).ConfigureAwait(false);
                    try
                    {
                        await AddInternalEventCoreAsync(p.IdConv,
                            $"⏰ No se pudo enviar un mensaje programado: {Truncar(ex.Message, 200)}",
                            null, null, "AlfaCore", "PROGRAMADO", ct).ConfigureAwait(false);
                    }
                    catch { /* la nota es best-effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "ProcesarProgramados", ex,
                "No se pudieron procesar los mensajes programados.", null, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
        return enviados;
    }

    private async Task MarcarProgramadoAsync(long idProgramado, string estado, string? motivoError, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            UPDATE dbo.CONV_MENSAJES_PROGRAMADOS
            SET Estado = @Estado, MotivoError = @Motivo,
                FechaEnvio = CASE WHEN @Estado = N'ENVIADO' THEN GETDATE() ELSE FechaEnvio END
            WHERE IdProgramado = @Id;
            """, cn);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Motivo", DbNullable(motivoError));
        cmd.Parameters.AddWithValue("@Id", idProgramado);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Auto-cierre por inactividad. Para conversaciones donde estamos esperando al cliente (último
    // mensaje nuestro) y no responde: a las N horas manda un aviso previo; si sigue sin responder,
    // a las M horas del aviso cierra. Aplica a todos los canales; el aviso/cierre solo se envían si
    // el canal/ventana lo permiten. Corre desde un job en segundo plano, una vez por base.
    public async Task<int> ProcesarAutoCierreAsync(CancellationToken ct = default)
    {
        int acciones = 0;
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                return 0;

            var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
            if (!config.AutoCierreActivo)
                return 0;

            var horasAviso = Math.Max(1, config.AutoCierreHorasAviso);
            var horasCierre = Math.Max(1, config.AutoCierreHorasCierre);

            var candidatos = new List<(long Id, string Canal, string SistemaAutor)>();
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                // Si hay un ticket abierto vinculado, un humano ya se hizo cargo de forma asíncrona
                // (ej. "lo vamos a analizar y te avisamos") -- el cliente no está siendo ignorado, así
                // que el aviso de "¿seguís ahí?" y el cierre automático no aplican mientras ese ticket
                // siga abierto.
                const string sql = """
                    DECLARE @ConTicketAbierto TABLE (IdConversacion bigint NOT NULL PRIMARY KEY);
                    IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NOT NULL
                    BEGIN
                        INSERT INTO @ConTicketAbierto (IdConversacion)
                        SELECT DISTINCT tk.IdConversacion
                        FROM dbo.TICK_TICKETS tk
                        INNER JOIN dbo.TICK_ESTADOS te ON te.CodigoEstado = tk.CodigoEstado
                        WHERE tk.IdConversacion IS NOT NULL
                          AND ISNULL(te.EsCerrado, 0) = 0;
                    END

                    SELECT TOP (200) c.IdConversacion, ISNULL(c.Canal, '') AS Canal, m.SistemaAutor
                    FROM dbo.CONV_CONVERSACIONES c
                    INNER JOIN dbo.CONV_ESTADOS e ON e.CodigoEstado = c.CodigoEstado
                    OUTER APPLY (
                        SELECT TOP (1) ISNULL(mm.Direction, '') AS Direction, ISNULL(mm.SistemaAutor, '') AS SistemaAutor, mm.FechaHora
                        FROM dbo.CONV_MENSAJES mm
                        WHERE mm.IdConversacion = c.IdConversacion AND mm.Direction IN (N'ENTRANTE', N'SALIENTE')
                        ORDER BY mm.FechaHora DESC, mm.IdMensaje DESC
                    ) m
                    WHERE ISNULL(c.Archivada, 0) = 0
                      AND ISNULL(e.EsCerrado, 0) = 0
                      AND m.Direction = N'SALIENTE'
                      AND NOT EXISTS (SELECT 1 FROM @ConTicketAbierto tk WHERE tk.IdConversacion = c.IdConversacion)
                      AND (
                            (m.SistemaAutor <> N'AUTOCIERRE_AVISO' AND m.FechaHora <= DATEADD(hour, -@HorasAviso, GETDATE()))
                         OR (m.SistemaAutor =  N'AUTOCIERRE_AVISO' AND m.FechaHora <= DATEADD(hour, -@HorasCierre, GETDATE()))
                      );
                    """;
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@HorasAviso", horasAviso);
                cmd.Parameters.AddWithValue("@HorasCierre", horasCierre);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                    candidatos.Add((rd.GetInt64(0), GetString(rd, 1), GetString(rd, 2)));
            }

            if (candidatos.Count == 0)
                return 0;

            var estadoCerrado = await GetClosedStateCodeAsync(ct).ConfigureAwait(false);

            foreach (var cand in candidatos)
            {
                try
                {
                    var yaAvisado = string.Equals(cand.SistemaAutor, "AUTOCIERRE_AVISO", StringComparison.OrdinalIgnoreCase);
                    if (yaAvisado)
                    {
                        await CerrarPorInactividadAsync(cand.Id, estadoCerrado, config.AutoCierreMensajeCierre, ct).ConfigureAwait(false);
                        acciones++;
                    }
                    else if (!string.IsNullOrWhiteSpace(config.AutoCierreMensajeAviso))
                    {
                        try
                        {
                            await SendMessageAsync(new ConversacionSendMessageRequest
                            {
                                IdConversacion = cand.Id,
                                Texto = config.AutoCierreMensajeAviso.Trim(),
                                MessageType = "TEXT",
                                UsuarioAccion = "AlfaCore",
                                SistemaAccion = "AUTOCIERRE_AVISO"
                            }, ct).ConfigureAwait(false);
                            acciones++;
                        }
                        catch
                        {
                            // No se pudo avisar (ventana vencida / canal sin envío): se cierra directo.
                            await CerrarPorInactividadAsync(cand.Id, estadoCerrado, string.Empty, ct).ConfigureAwait(false);
                            acciones++;
                        }
                    }
                    else
                    {
                        // Sin mensaje de aviso configurado: cierra directo.
                        await CerrarPorInactividadAsync(cand.Id, estadoCerrado, config.AutoCierreMensajeCierre, ct).ConfigureAwait(false);
                        acciones++;
                    }
                }
                catch (Exception ex)
                {
                    await _appEvents.LogErrorAsync("Conversaciones", "AutoCierreItem", ex,
                        "No se pudo procesar el auto-cierre de una conversación.",
                        new { cand.Id }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "AutoCierre", ex,
                "No se pudo procesar el auto-cierre por inactividad.", null, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
        return acciones;
    }

    private async Task<string> GetClosedStateCodeAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT TOP (1) LTRIM(RTRIM(CodigoEstado)) FROM dbo.CONV_ESTADOS WHERE ISNULL(EsCerrado, 0) = 1 ORDER BY ISNULL(Orden, 999), CodigoEstado;", cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s && !string.IsNullOrWhiteSpace(s) ? s : "CERRADA";
    }

    private async Task CerrarPorInactividadAsync(long idConversacion, string estadoCerrado, string? mensajeCierre, CancellationToken ct)
    {
        // Intentar mandar el mensaje de cierre (si el canal/ventana lo permiten). No frena el cierre.
        if (!string.IsNullOrWhiteSpace(mensajeCierre))
        {
            try
            {
                await SendMessageAsync(new ConversacionSendMessageRequest
                {
                    IdConversacion = idConversacion,
                    Texto = mensajeCierre.Trim(),
                    MessageType = "TEXT",
                    UsuarioAccion = "AlfaCore",
                    SistemaAccion = "AUTOCIERRE"
                }, ct).ConfigureAwait(false);
            }
            catch
            {
                // Ventana vencida o canal sin envío: se cierra igual, sin mensaje.
            }
        }

        await ChangeStatusAsync(new ConversacionEstadoRequest
        {
            IdConversacion = idConversacion,
            CodigoEstado = estadoCerrado,
            UsuarioAccion = "AlfaCore",
            SistemaAccion = "AUTOCIERRE"
        }, ct).ConfigureAwait(false);

        await _appEvents.LogAuditAsync("Conversaciones", "AutoCierre", "CONV_CONVERSACIONES",
            idConversacion.ToString(CultureInfo.InvariantCulture),
            "Conversación cerrada automáticamente por inactividad.",
            new { idConversacion }, ct).ConfigureAwait(false);
    }

    // Seguimientos / SLA. Para conversaciones donde el cliente espera respuesta (último mensaje
    // entrante): a las N horas deja una nota interna de recordatorio; a las M horas, si estaba
    // asignada, la desasigna para que otro la tome. No manda nada al cliente (aplica a todo canal).
    public async Task<int> ProcesarSeguimientosSlaAsync(CancellationToken ct = default)
    {
        int acciones = 0;
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                return 0;

            var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
            if (!config.SlaActivo)
                return 0;

            var horasRec = Math.Max(1, config.SlaHorasRecordatorio);
            var horasReasig = Math.Max(horasRec, config.SlaHorasReasignar);

            var candidatos = new List<(long Id, string IdTecnico, DateTime UltInbound, bool YaRecordado)>();
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                const string sql = """
                    SELECT TOP (200)
                        c.IdConversacion,
                        LTRIM(RTRIM(ISNULL(c.IdTecnico, ''))) AS IdTecnico,
                        m.FechaHora,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.CONV_MENSAJES n
                            WHERE n.IdConversacion = c.IdConversacion
                              AND n.Direction = N'NOTA_INTERNA' AND ISNULL(n.SistemaAutor, '') = N'SLA'
                              AND n.FechaHora > m.FechaHora
                        ) THEN 1 ELSE 0 END AS YaRecordado
                    FROM dbo.CONV_CONVERSACIONES c
                    INNER JOIN dbo.CONV_ESTADOS e ON e.CodigoEstado = c.CodigoEstado
                    OUTER APPLY (
                        SELECT TOP (1) ISNULL(mm.Direction, '') AS Direction, mm.FechaHora
                        FROM dbo.CONV_MENSAJES mm
                        WHERE mm.IdConversacion = c.IdConversacion AND mm.Direction IN (N'ENTRANTE', N'SALIENTE')
                        ORDER BY mm.FechaHora DESC, mm.IdMensaje DESC
                    ) m
                    WHERE ISNULL(c.Archivada, 0) = 0
                      AND ISNULL(e.EsCerrado, 0) = 0
                      AND UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) NOT IN (N'PENDIENTE', N'EN_GESTION')
                      AND m.Direction = N'ENTRANTE'
                      AND m.FechaHora <= DATEADD(hour, -@HorasRecordatorio, GETDATE());
                    """;
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@HorasRecordatorio", horasRec);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                    candidatos.Add((rd.GetInt64(0), GetString(rd, 1),
                        rd.IsDBNull(2) ? DateTime.Now : rd.GetDateTime(2),
                        !rd.IsDBNull(3) && rd.GetInt32(3) == 1));
            }

            foreach (var cand in candidatos)
            {
                try
                {
                    var horasEspera = (int)Math.Floor((DateTime.Now - cand.UltInbound).TotalHours);
                    if (!string.IsNullOrWhiteSpace(cand.IdTecnico) && horasEspera >= horasReasig)
                    {
                        await AssignConversationAsync(new ConversacionAsignacionRequest
                        {
                            IdConversacion = cand.Id,
                            IdTecnico = null,
                            UsuarioAccion = "AlfaCore",
                            SistemaAccion = "SLA",
                            Observaciones = "Reasignada por falta de respuesta (SLA)."
                        }, ct).ConfigureAwait(false);
                        await AddInternalEventCoreAsync(cand.Id,
                            $"🔁 Reasignada por SLA: el cliente espera respuesta hace ~{horasEspera} h. Vuelve a la cola.",
                            null, null, "AlfaCore", "SLA", ct).ConfigureAwait(false);
                        acciones++;
                    }
                    else if (!cand.YaRecordado)
                    {
                        await AddInternalEventCoreAsync(cand.Id,
                            $"⏰ SLA: el cliente espera respuesta hace ~{horasEspera} h.",
                            null, null, "AlfaCore", "SLA", ct).ConfigureAwait(false);
                        acciones++;
                    }
                }
                catch (Exception ex)
                {
                    await _appEvents.LogErrorAsync("Conversaciones", "SlaItem", ex,
                        "No se pudo procesar el seguimiento SLA de una conversación.",
                        new { cand.Id }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "Sla", ex,
                "No se pudo procesar los seguimientos SLA.", null, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
        return acciones;
    }

    private static bool ContienePalabraEscalado(string texto, string? palabras)
    {
        if (string.IsNullOrWhiteSpace(palabras))
            return false;
        var lower = texto.ToLowerInvariant();
        return palabras
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => lower.Contains(p.ToLowerInvariant()));
    }

    private static string Truncar(string? texto, int max)
    {
        var t = (texto ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    // Saludos de apertura Y cierres/agradecimientos: ninguno de los dos tiene nada que buscar en la
    // base de conocimiento -- un "muchas gracias" al final de una consulta ya resuelta es tan vacío
    // de contenido como un "hola" al principio, y le adjuntaba igual el link de la mejor cita.
    // Comparar contra frases exactas era demasiado frágil (una variante como "hola denuevo" no
    // matcheaba nada); en cambio, se clasifica por PALABRA: si TODAS las palabras del mensaje son de
    // relleno social (sin acentos, listadas abajo), no hay contenido real que buscar. Cualquier
    // palabra fuera de esta lista ("factura", "stock", etc.) ya lo saca de esta clasificación.
    private static readonly HashSet<string> PalabrasSocialesRelleno = new(StringComparer.OrdinalIgnoreCase)
    {
        "hola", "holis", "buenas", "buenos", "dias", "dia", "tardes", "noches",
        "tal", "hey", "hi", "que", "de", "nuevo", "denuevo", "otra", "vez",
        "gracias", "mil", "muchas", "muchisimas", "totales", "genial", "perfecto",
        "buenisimo", "excelente", "dale", "ok", "okay", "listo", "joya", "barbaro",
        "nada", "bien", "todo", "vos", "y", "che", "buen"
    };

    private static bool EsMensajeSocial(string texto)
    {
        var normalizado = RemoveDiacritics(texto).ToLowerInvariant();
        var palabras = Regex.Matches(normalizado, "[a-z]+").Select(m => m.Value).ToArray();
        return palabras.Length > 0 && palabras.All(PalabrasSocialesRelleno.Contains);
    }

    private static string ConstruirContextoCliente(string rubro, bool esPrioritario)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(rubro))
            partes.Add($"Rubro del negocio del cliente: {rubro.Trim()}.");
        if (esPrioritario)
            partes.Add("Es un cliente prioritario: tratalo con atención preferencial.");
        return string.Join(" ", partes);
    }

    // Contexto del cliente para el copiloto: N° de cliente, razón social, rubro del negocio y
    // prioridad P1-P4 según su clasificación (misma prioridad que el badge del inbox: Clasifica1/2/3
    // -> P1/P2/P3, el resto -> P4). Texto listo para anteponer a la sugerencia. Vacío si no hay cliente.
    public async Task<string> GetContextoClienteAsistenteAsync(long idConversacion, CancellationToken ct = default)
    {
        try
        {
            string codigo = string.Empty, razon = string.Empty, rubro = string.Empty, clasif = string.Empty;
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                const string sql = """
                    SELECT TOP (1)
                        ISNULL(LTRIM(RTRIM(cli.CODIGO)), N''),
                        ISNULL(LTRIM(RTRIM(cli.RAZON_SOCIAL)), N''),
                        ISNULL(cat.Descripcion, N''),
                        ISNULL(LTRIM(RTRIM(cli.Clasificacion)), N'')
                    FROM dbo.CONV_CONVERSACIONES c
                    LEFT JOIN dbo.VT_CLIENTES cli
                        ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N''))))
                    LEFT JOIN dbo.v_ta_categoria cat ON cat.IdCategoria = cli.IDCategoria
                    WHERE c.IdConversacion = @Id;
                    """;
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Id", idConversacion);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    codigo = GetString(rd, 0);
                    razon = GetString(rd, 1);
                    rubro = GetString(rd, 2);
                    clasif = GetString(rd, 3);
                }
            }

            if (string.IsNullOrWhiteSpace(codigo))
                return string.Empty;

            var prioridad = "P4";
            if (!string.IsNullOrWhiteSpace(clasif))
            {
                var prio = await conversacionesConfigService.GetPrioridadConfigAsync(ct).ConfigureAwait(false);
                if (Coincide(prio.Clasifica1, clasif)) prioridad = "P1";
                else if (Coincide(prio.Clasifica2, clasif)) prioridad = "P2";
                else if (Coincide(prio.Clasifica3, clasif)) prioridad = "P3";
            }

            var quien = string.IsNullOrWhiteSpace(razon) ? $"N° {codigo}" : $"N° {codigo} — {razon}";
            var partes = new List<string> { $"Datos del cliente (para adaptar la respuesta): {quien}." };
            if (!string.IsNullOrWhiteSpace(rubro))
                partes.Add($"Rubro del negocio: {rubro.Trim()}. Adaptá ejemplos y pasos a ese rubro.");
            var prioTexto = prioridad switch
            {
                "P1" => "P1 (máxima prioridad, atención preferencial)",
                "P2" => "P2 (prioridad alta)",
                "P3" => "P3 (prioridad media)",
                _ => "P4 (prioridad normal)"
            };
            partes.Add($"Prioridad de atención: {prioTexto}.");
            return string.Join(" ", partes);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones", "ContextoClienteAsistente", ex,
                "No se pudo leer el contexto del cliente para el copiloto.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            return string.Empty;
        }

        static bool Coincide(string? config, string clasif)
            => !string.IsNullOrWhiteSpace(config) && string.Equals(config.Trim(), clasif, StringComparison.OrdinalIgnoreCase);
    }

    // Lee el rubro del cliente (VT_CLIENTES.IdCategoria -> v_ta_categoria.Descripcion) y determina si
    // es un cliente prioritario (su clasificación está entre las configuradas como prioridad de atención).
    private async Task<(string Rubro, bool EsPrioritario)> ObtenerContextoClienteAsync(long idConversacion, CancellationToken ct)
    {
        try
        {
            string rubro = string.Empty, clasif = string.Empty;
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                const string sql = """
                    SELECT TOP (1)
                        ISNULL(cat.Descripcion, N''),
                        ISNULL(LTRIM(RTRIM(cli.Clasificacion)), N'')
                    FROM dbo.CONV_CONVERSACIONES c
                    LEFT JOIN dbo.VT_CLIENTES cli
                        ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N''))))
                    LEFT JOIN dbo.v_ta_categoria cat ON cat.IdCategoria = cli.IdCategoria
                    WHERE c.IdConversacion = @Id;
                    """;
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Id", idConversacion);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    rubro = GetString(rd, 0);
                    clasif = GetString(rd, 1);
                }
            }

            var esPrioritario = false;
            if (!string.IsNullOrWhiteSpace(clasif))
            {
                var prio = await conversacionesConfigService.GetPrioridadConfigAsync(ct).ConfigureAwait(false);
                esPrioritario = new[] { prio.Clasifica1, prio.Clasifica2, prio.Clasifica3 }
                    .Any(p => !string.IsNullOrWhiteSpace(p) && string.Equals(p.Trim(), clasif, StringComparison.OrdinalIgnoreCase));
            }

            return (rubro, esPrioritario);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones", "ContextoCliente", ex,
                "No se pudo leer el rubro/clasificación del cliente para el asistente.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            return (string.Empty, false);
        }
    }

    // Sube la prioridad de la conversación solo si la nueva es mayor que la actual (nunca la baja).
    private async Task SubirPrioridadAsync(long idConversacion, string prioridad, CancellationToken ct)
    {
        var p = (prioridad ?? string.Empty).Trim().ToUpperInvariant();
        var rank = p switch { "URGENTE" => 4, "ALTA" => 3, "MEDIA" => 2, "BAJA" => 1, _ => 0 };
        if (rank == 0)
            return;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        const string sql = """
            UPDATE dbo.CONV_CONVERSACIONES SET Prioridad = @P
            WHERE IdConversacion = @Id
              AND @Rank > CASE UPPER(ISNULL(Prioridad, ''))
                    WHEN 'URGENTE' THEN 4 WHEN 'ALTA' THEN 3 WHEN 'MEDIA' THEN 2 WHEN 'BAJA' THEN 1 ELSE 0 END;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@P", p);
        cmd.Parameters.AddWithValue("@Rank", rank);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class AsistenteKnowledgeContext
    {
        public string ConocimientoBase { get; init; } = string.Empty;
        public string SuggestedReply { get; init; } = string.Empty;
        public bool HasSufficientContext { get; init; }
        public bool NeedsClarification { get; init; }
        public string ClarificationQuestion { get; init; } = string.Empty;

        /// <summary>
        /// Nota SOLO para uso interno (nunca se envía al cliente): AlfaKnowledge la llena cuando la
        /// mejor fuente para esta consulta estaba marcada "uso interno únicamente" y por eso no se
        /// pudo usar en la respuesta. Se deja registrada como nota interna de la conversación para
        /// que un humano la revise en AlfaKnowledge con su propio usuario.
        /// </summary>
        public string EscalationHint { get; init; } = string.Empty;

        /// <summary>
        /// URL pública real (no un link interno de AlfaKnowledge) de la mejor cita, si hay una. Se
        /// adjunta al enviarle SuggestedReply directo al cliente -- ver ObtenerConocimientoBaseAsync.
        /// </summary>
        public string LinkPublico { get; init; } = string.Empty;
    }

    // Recupera de AlfaKnowledge tanto los fragmentos relevantes como la sugerencia directa. Así el
    // bot puede usar la respuesta sugerida como base principal cuando AlfaKnowledge sí encontró
    // material suficiente, sin perder las citas que sirven de respaldo adicional para OpenAI.
    private async Task<AsistenteKnowledgeContext> ObtenerConocimientoBaseAsync(
        string texto, IReadOnlyList<ConversacionMensajeDto> mensajes, long idConversacion, CancellationToken ct)
    {
        try
        {
            var ak = await alfaKnowledgeService.SuggestReplyAsync(
                texto, mensajes, idConversacion, AlfaKnowledgeSuggestionModes.ReplySuggestion,
                null, null, null, null, ct).ConfigureAwait(false);

            if (ak is null)
                return new AsistenteKnowledgeContext();

            var sb = new StringBuilder();
            foreach (var cita in ak.Citations
                         .Where(c => !string.IsNullOrWhiteSpace(c.PlainText))
                         .OrderByDescending(c => c.Score)
                         .Take(5))
            {
                var titulo = string.IsNullOrWhiteSpace(cita.Title) ? cita.SourceLabel : cita.Title;
                if (!string.IsNullOrWhiteSpace(titulo))
                    sb.Append("• [").Append(titulo.Trim()).AppendLine("]");
                sb.AppendLine(Truncar(cita.PlainText, 800));
                sb.AppendLine();
            }

            // AlfaKnowledge redacta suggestedReply pensando en un técnico que la revisa y, si hace
            // falta, agrega el link de la cita a mano desde su propio chat (ahí las citas se muestran
            // como chips aparte). Acá no hay técnico en el medio -- si no se adjunta el link ahora, se
            // pierde: el cliente se queda con una respuesta genérica sin la fuente real.
            var linkPublico = ak.Citations
                .OrderByDescending(c => c.Score)
                .Select(c => alfaKnowledgeService.TryGetPublicCitationUrl(c))
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? string.Empty;

            return new AsistenteKnowledgeContext
            {
                ConocimientoBase = sb.ToString().Trim(),
                SuggestedReply = (ak.SuggestedReply ?? string.Empty).Trim(),
                HasSufficientContext = ak.HasSufficientContext,
                NeedsClarification = ak.NeedsClarification,
                ClarificationQuestion = (ak.ClarificationQuestion ?? string.Empty).Trim(),
                EscalationHint = (ak.EscalationHint ?? string.Empty).Trim(),
                LinkPublico = linkPublico
            };
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones", "AsistenteKnowledge", ex,
                "No se pudo recuperar conocimiento de AlfaKnowledge para el asistente.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
            return new AsistenteKnowledgeContext();
        }
    }

    /// <summary>
    /// Cuenta de respuestas del bot (para el tope <c>BotMaxRespuestas</c>) y si el último mensaje
    /// entrante YA tiene una respuesta automática posterior (bienvenida/fuera de horario/auto-cierre/
    /// bot) -- comparado por <c>IdMensaje</c>, no por "el último saliente alguna vez fue automático":
    /// eso evita que, tras la primera respuesta automática de toda la conversación, quede bloqueado
    /// para siempre (ver comentario en el llamador).
    /// </summary>
    private async Task<(int Count, bool YaRespondioUltimoEntrante)> GetBotReplyStatsAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(1) FROM dbo.CONV_MENSAJES WHERE IdConversacion = @Id AND Direction = N'SALIENTE' AND ISNULL(SistemaAutor, '') = N'BOT'),
                ISNULL((SELECT MAX(IdMensaje) FROM dbo.CONV_MENSAJES WHERE IdConversacion = @Id AND Direction = N'ENTRANTE'), 0),
                ISNULL((SELECT MAX(IdMensaje) FROM dbo.CONV_MENSAJES WHERE IdConversacion = @Id AND Direction = N'SALIENTE' AND ISNULL(SistemaAutor, '') IN (N'AUTOMATIZACION', N'BIENVENIDA', N'BOT', N'AUTOCIERRE_AVISO', N'AUTOCIERRE', N'REGLA')), 0);
            """;
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (await rd.ReadAsync(ct))
        {
            var count = rd.IsDBNull(0) ? 0 : rd.GetInt32(0);
            var ultimoEntranteId = rd.IsDBNull(1) ? 0L : rd.GetInt64(1);
            var ultimoAutomaticoId = rd.IsDBNull(2) ? 0L : rd.GetInt64(2);
            return (count, ultimoAutomaticoId > ultimoEntranteId);
        }
        return (0, false);
    }

    private async Task<IReadOnlyList<ConversacionMensajeDto>> GetRecentMessagesForBotAsync(long idConversacion, CancellationToken ct)
    {
        // Si la conversación se cerró y después se reabrió (un cliente nuevo escribe tiempo después),
        // FechaHoraCierre ya quedó en NULL para ese momento (se limpia al reabrir -- ver
        // RefreshConversationAsync/ReopenClosedConversationsWithIncomingAfterCloseAsync), así que no
        // sirve para acotar el historial. En cambio, el cierre siempre deja un evento de sistema
        // ("... cerró la conversación.") en CONV_MENSAJES -- se usa ESE como corte: todo lo anterior al
        // último cierre es una consulta ya resuelta y no debe mezclarse con la conversación nueva (el
        // bot llegó a responder una pregunta vieja de antes del cierre en vez del saludo actual).
        const string sql = """
            DECLARE @UltimoCierre datetime = (
                SELECT MAX(FechaHora)
                FROM dbo.CONV_MENSAJES
                WHERE IdConversacion = @Id
                  AND Direction = N'NOTA_INTERNA'
                  AND MessageType = N'SYSTEM'
                  AND (Texto LIKE N'%cerrÃ³ la conversaciÃ³n%' OR Texto LIKE N'%cerro la conversacion%')
            );

            SELECT TOP (40)
                ISNULL(Direction, '') AS Direction,
                ISNULL(CAST(Texto AS nvarchar(max)), '') AS Texto,
                FechaHora
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @Id
              AND Direction IN (N'ENTRANTE', N'SALIENTE')
              AND ISNULL(CAST(Texto AS nvarchar(max)), '') <> ''
              AND (@UltimoCierre IS NULL OR FechaHora > @UltimoCierre)
            ORDER BY FechaHora DESC, IdMensaje DESC;
            """;
        var result = new List<ConversacionMensajeDto>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            result.Add(new ConversacionMensajeDto
            {
                Direction = GetString(rd, 0),
                Texto = GetString(rd, 1),
                FechaHora = rd.IsDBNull(2) ? DateTime.MinValue : rd.GetDateTime(2)
            });
        }
        return result;
    }

    private static bool IsOutsideBusinessHours(ConversacionAutomatizacionesConfigDto config, DateTime now)
    {
        var atiendeHoy = now.DayOfWeek switch
        {
            DayOfWeek.Monday => config.Lunes,
            DayOfWeek.Tuesday => config.Martes,
            DayOfWeek.Wednesday => config.Miercoles,
            DayOfWeek.Thursday => config.Jueves,
            DayOfWeek.Friday => config.Viernes,
            DayOfWeek.Saturday => config.Sabado,
            DayOfWeek.Sunday => config.Domingo,
            _ => false
        };

        if (!atiendeHoy)
            return true;

        if (!TimeSpan.TryParse(config.HoraDesde, out var desde) || !TimeSpan.TryParse(config.HoraHasta, out var hasta))
            return false;

        var horaActual = now.TimeOfDay;
        return desde <= hasta
            ? horaActual < desde || horaActual > hasta
            : horaActual < desde && horaActual > hasta;
    }

    /// <summary>
    /// Si ya se mandó el aviso fijo de "fuera de horario" (SistemaAutor='AUTOMATIZACION') en la
    /// fecha local actual de la conversación, no se repite. Esto evita que varios mensajes seguidos
    /// del cliente disparen la misma respuesta automática en cadena, pero permite volver a avisar al
    /// día siguiente si la conversación sigue llegando fuera de horario.
    /// </summary>
    private async Task<bool> HasSentOutOfHoursNoticeAsync(long idConversacion, DateTime fechaLocalActual, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND Direction = N'SALIENTE'
              AND SistemaAutor = N'AUTOMATIZACION'
              AND CAST(FechaHora AS date) = @FechaLocal;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@FechaLocal", fechaLocalActual.Date);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    // DIAGNÓSTICO TEMPORAL (retirar una vez encontrada la causa de las respuestas silenciosas del
    // bot): escribe directo a AUX_ERR por SQL, sin pasar por AppEventService/el archivo .jsonl -- ese
    // archivo pierde escrituras por una condición de carrera cuando varios procesos escriben a la vez
    // (confirmado en producción, "the file is being used by another process"), lo que hacía
    // indistinguible "no se ejecutó este paso" de "se ejecutó pero el log se perdió". Esto no debería
    // fallar nunca (columna Error es NOT NULL en AUX_ERR, se manda -1 como valor sin significado); si
    // aun así falla, se traga el error para no romper el flujo real por un problema de diagnóstico.
    private async Task TraceDiagAsync(string paso, long idConversacion, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(
                "INSERT INTO dbo.AUX_ERR (Proceso, Fecha, Error, Descripcion, Usuario) VALUES (@Proceso, GETDATE(), -1, @Descripcion, N'DIAG');",
                cn);
            cmd.Parameters.AddWithValue("@Proceso", "Conversaciones.TraceDiag");
            cmd.Parameters.AddWithValue("@Descripcion", $"{paso} | idConversacion={idConversacion}");
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: un fallo acá nunca debe interrumpir el flujo real.
        }
    }

    private async Task RunWithConversationAutomationLockAsync(
        long idConversacion,
        string motivo,
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        var resource = $"CONV_AUTO:{idConversacion}:{motivo}";
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var lockResult = await AcquireApplicationLockAsync(cn, resource, ct).ConfigureAwait(false);
        if (lockResult < 0)
        {
            await TraceDiagAsync($"LockSkip:{motivo}:lockResult={lockResult}", idConversacion, ct).ConfigureAwait(false);
            await _appEvents.LogAuditAsync(
                "Conversaciones", "AutomationLockSkipped", "CONV_CONVERSACIONES",
                idConversacion.ToString(CultureInfo.InvariantCulture),
                "Se omitió una automatización porque otro nodo ya la estaba procesando para esta conversación.",
                new { idConversacion, motivo, lockResult }, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await action(ct).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseApplicationLockAsync(cn, resource, ct).ConfigureAwait(false);
        }
    }

    private static async Task<int> AcquireApplicationLockAsync(SqlConnection cn, string resource, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @Resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @result;
            """,
            cn);
        cmd.Parameters.AddWithValue("@Resource", resource);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? -999 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ReleaseApplicationLockAsync(SqlConnection cn, string resource, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            """
            EXEC sp_releaseapplock
                @Resource = @Resource,
                @LockOwner = 'Session';
            """,
            cn);
        cmd.Parameters.AddWithValue("@Resource", resource);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> HasSentAutomaticMessageTextAsync(
        long idConversacion,
        string sistemaAutor,
        string texto,
        DateTime fechaLocalActual,
        CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion
              AND Direction = N'SALIENTE'
              AND UPPER(LTRIM(RTRIM(ISNULL(SistemaAutor, N'')))) = UPPER(LTRIM(RTRIM(@SistemaAutor)))
              AND CAST(FechaHora AS date) = @FechaLocal
              AND LTRIM(RTRIM(ISNULL(CAST(Texto AS nvarchar(max)), N''))) = LTRIM(RTRIM(@Texto));
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@SistemaAutor", sistemaAutor);
        cmd.Parameters.AddWithValue("@FechaLocal", fechaLocalActual.Date);
        cmd.Parameters.AddWithValue("@Texto", texto.Trim());
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    // Motor de reglas / palabras clave. Corre en cada mensaje entrante (todos los canales), antes de
    // bienvenida/bot. Evalúa las reglas activas por Orden; la primera que coincide aplica sus acciones
    // (responder, derivar a un técnico, fijar prioridad) y, si tiene Detener=1, corta el resto — incluidas
    // las auto-respuestas de bienvenida/bot. Devuelve true si alguna regla pidió detener.
    private async Task<bool> TryAutoReplyReglasAsync(long idConversacion, string? incomingText, CancellationToken ct)
    {
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                return false;

            var texto = (incomingText ?? string.Empty).Trim();
            if (texto.Length == 0)
                return false;

            var reglas = await conversacionesConfigService.GetReglasAsync(ct).ConfigureAwait(false);
            if (reglas.Count == 0)
                return false;

            var conversation = await RequireConversationAsync(idConversacion, ct).ConfigureAwait(false);
            var canal = (conversation.Canal ?? string.Empty).Trim();
            var textoLower = texto.ToLowerInvariant();
            string? tecnicoActual = null;
            ConversacionAutomatizacionesConfigDto? autoConfig = null;
            int? entrantes = null;

            foreach (var regla in reglas.Where(r => r.Activa).OrderBy(r => r.Orden).ThenBy(r => r.IdRegla))
            {
                if (!string.IsNullOrWhiteSpace(regla.Canal)
                    && !string.Equals(regla.Canal, canal, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!CoincideRegla(textoLower, regla))
                    continue;

                // Solo en el primer contacto: exactamente un mensaje entrante en la conversación.
                if (regla.SoloPrimerContacto)
                {
                    entrantes ??= await GetIncomingCountAsync(idConversacion, ct).ConfigureAwait(false);
                    if (entrantes != 1)
                        continue;
                }

                // Condición de horario (dentro/fuera del configurado en Automatizaciones). Si el horario
                // no está configurado, no se puede evaluar: la condición se ignora.
                if (!string.Equals(regla.Horario, "SIEMPRE", StringComparison.OrdinalIgnoreCase))
                {
                    autoConfig ??= await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
                    if (autoConfig.IsConfigured)
                    {
                        var fueraDeHorario = IsOutsideBusinessHours(autoConfig, BusinessNow());
                        var quiereFuera = string.Equals(regla.Horario, "FUERA", StringComparison.OrdinalIgnoreCase);
                        if (fueraDeHorario != quiereFuera)
                            continue;
                    }
                }

                if (regla.SoloSinAsignar)
                {
                    tecnicoActual ??= await GetConversationTechnicianIdAsync(idConversacion, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tecnicoActual))
                        continue;
                }

                // Etiquetar: fijar prioridad de la conversación.
                if (!string.IsNullOrWhiteSpace(regla.Prioridad))
                    await SetConversationPrioridadAsync(idConversacion, regla.Prioridad, ct).ConfigureAwait(false);

                // Derivar: asignar a un técnico.
                if (!string.IsNullOrWhiteSpace(regla.AsignarTecnico))
                {
                    await AssignConversationAsync(new ConversacionAsignacionRequest
                    {
                        IdConversacion = idConversacion,
                        IdTecnico = regla.AsignarTecnico.Trim(),
                        UsuarioAccion = "AlfaCore",
                        SistemaAccion = "REGLA",
                        Observaciones = $"Asignada por la regla '{regla.Nombre}'."
                    }, ct).ConfigureAwait(false);
                    tecnicoActual = regla.AsignarTecnico.Trim();
                }

                // Responder: auto-respuesta de texto (si el canal/ventana lo permiten).
                if (!string.IsNullOrWhiteSpace(regla.RespuestaTexto))
                {
                    try
                    {
                        await SendMessageAsync(new ConversacionSendMessageRequest
                        {
                            IdConversacion = idConversacion,
                            Texto = regla.RespuestaTexto.Trim(),
                            MessageType = "TEXT",
                            UsuarioAccion = "AlfaCore",
                            SistemaAccion = "REGLA"
                        }, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Ventana vencida / canal sin envío: la derivación y la prioridad ya se aplicaron.
                        await _appEvents.LogErrorAsync(
                            "Conversaciones", "ReglaRespuesta", ex,
                            "No se pudo enviar la respuesta automática de una regla.",
                            new { idConversacion, regla.Nombre }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
                    }
                }

                await _appEvents.LogAuditAsync(
                    "Conversaciones", "ReglaAplicada", "CONV_CONVERSACIONES",
                    idConversacion.ToString(CultureInfo.InvariantCulture),
                    $"Se aplicó la regla '{regla.Nombre}'.",
                    new { idConversacion, regla.IdRegla, regla.Nombre, regla.Detener }, ct).ConfigureAwait(false);

                if (regla.Detener)
                    return true;
            }
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones", "ReglasMotor", ex,
                "No se pudieron procesar las reglas de palabras clave.",
                new { idConversacion }, AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static bool CoincideRegla(string textoLower, ConversacionReglaDto regla)
    {
        var palabras = (regla.Palabras ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (palabras.Length == 0)
            return false;

        var tipo = (regla.TipoCoincidencia ?? string.Empty).Trim().ToUpperInvariant();
        foreach (var palabra in palabras)
        {
            var kw = palabra.Trim().ToLowerInvariant();
            if (kw.Length == 0)
                continue;

            var coincide = tipo switch
            {
                "IGUAL" => string.Equals(textoLower, kw, StringComparison.Ordinal),
                "EMPIEZA" => textoLower.StartsWith(kw, StringComparison.Ordinal),
                _ => textoLower.Contains(kw, StringComparison.Ordinal)
            };

            if (coincide)
                return true;
        }

        return false;
    }

    private async Task SetConversationPrioridadAsync(long idConversacion, string prioridad, CancellationToken ct)
    {
        var p = (prioridad ?? string.Empty).Trim().ToUpperInvariant();
        if (p is not ("BAJA" or "MEDIA" or "ALTA" or "URGENTE"))
            return;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "UPDATE dbo.CONV_CONVERSACIONES SET Prioridad = @Prioridad WHERE IdConversacion = @Id;", cn);
        cmd.Parameters.AddWithValue("@Prioridad", p);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Ventana de envío según el canal: WhatsApp e Instagram/Facebook tienen ventana de 24 h; Mercado
    // Libre e INTERNO no. Se usa para no gastar una llamada de IA del bot si igual no se podría enviar.
    private async Task<bool> IsSendWindowActiveAsync(long idConversacion, string canal, CancellationToken ct)
    {
        if (string.Equals(canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
            return await IsWhatsAppQrConversationAsync(idConversacion, ct).ConfigureAwait(false)
                || await IsWhatsAppWindowActiveAsync(idConversacion, ct).ConfigureAwait(false);
        if (string.Equals(canal, "INSTAGRAM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(canal, "FACEBOOK", StringComparison.OrdinalIgnoreCase))
            return await IsInstagramWindowActiveAsync(idConversacion, ct).ConfigureAwait(false);
        return true;
    }

    // Bienvenida al primer contacto: cuando llega el primer mensaje de una conversación de WhatsApp
    // y todavía no le respondimos nada, manda una sola vez el mensaje de bienvenida.
    private async Task TryAutoReplyWelcomeAsync(long idConversacion, CancellationToken ct)
    {
        try
        {
            if (!await centralAdminService.IsModuloActivoParaClienteActualAsync("AUTOMATIZACIONES", ct).ConfigureAwait(false))
                return;

            var config = await conversacionesConfigService.GetAutomatizacionesConfigAsync(ct).ConfigureAwait(false);
            if (!config.BienvenidaActivo || string.IsNullOrWhiteSpace(config.BienvenidaMensaje))
                return;

            int entrantes, salientes;
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                await using var cmd = new SqlCommand("""
                    SELECT
                        SUM(CASE WHEN Direction = N'ENTRANTE' THEN 1 ELSE 0 END),
                        SUM(CASE WHEN Direction = N'SALIENTE' THEN 1 ELSE 0 END)
                    FROM dbo.CONV_MENSAJES
                    WHERE IdConversacion = @Id AND Direction IN (N'ENTRANTE', N'SALIENTE');
                    """, cn);
                cmd.Parameters.AddWithValue("@Id", idConversacion);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (!await rd.ReadAsync(ct))
                    return;
                entrantes = rd.IsDBNull(0) ? 0 : rd.GetInt32(0);
                salientes = rd.IsDBNull(1) ? 0 : rd.GetInt32(1);
            }

            // Primer contacto real: exactamente un mensaje del cliente y ninguno nuestro todavía.
            if (entrantes != 1 || salientes != 0)
                return;

            await SendMessageAsync(new ConversacionSendMessageRequest
            {
                IdConversacion = idConversacion,
                Texto = config.BienvenidaMensaje.Trim(),
                MessageType = "TEXT",
                UsuarioAccion = "AlfaCore",
                SistemaAccion = "BIENVENIDA"
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _appEvents.LogErrorAsync("Conversaciones", "AutoReplyWelcome", ex,
                "No se pudo enviar el mensaje de bienvenida.", new { idConversacion },
                AppEventSeverity.Warning, ct).ConfigureAwait(false);
        }
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
                MessageIdExterno,
                ReplyToMessageIdExterno,
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
                @MessageIdExterno,
                @ReplyToMessageIdExterno,
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
        var author = await ResolveLegacyMessageAuthorAsync(cn, message.UsuarioAutor, message.SistemaAutor, ct);
        cmd.Parameters.AddWithValue("@IdConversacion", message.ConversationId);
        cmd.Parameters.AddWithValue("@TelefonoWhatsApp", DbNullable(message.Phone));
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", DbNullable(message.WhatsAppMessageId));
        cmd.Parameters.AddWithValue("@WhatsAppReplyToMessageId", DbNullable(message.ReplyToMessageId));
        cmd.Parameters.AddWithValue("@MessageIdExterno", DbNullable(message.MessageIdExterno));
        cmd.Parameters.AddWithValue("@ReplyToMessageIdExterno", DbNullable(message.ReplyToMessageIdExterno));
        cmd.Parameters.AddWithValue("@MessageType", NormalizeMessageType(message.MessageType));
        cmd.Parameters.AddWithValue("@Direction", message.Direction);
        cmd.Parameters.AddWithValue("@EstadoEnvio", DbNullable(message.EstadoEnvio));
        cmd.Parameters.AddWithValue("@Texto", DbNullable(message.Text));
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(message.PayloadJson));
        cmd.Parameters.AddWithValue("@FechaHora", message.FechaHora);
        cmd.Parameters.AddWithValue("@UsuarioAutor", DbNullable(author.Usuario));
        cmd.Parameters.AddWithValue("@SistemaAutor", DbNullable(author.Sistema));
        cmd.Parameters.AddWithValue("@IdTecnicoAutor", DbNullablePreserve(idTecnicoAutor));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<(string Usuario, string Sistema)> ResolveLegacyMessageAuthorAsync(
        SqlConnection cn,
        string? usuario,
        string? sistema,
        CancellationToken ct)
    {
        var normalizedUser = (usuario ?? string.Empty).Trim();
        var normalizedSystem = (sistema ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser) || string.IsNullOrWhiteSpace(normalizedSystem))
            return (string.Empty, string.Empty);

        const string sql = """
            SELECT TOP (1)
                ISNULL(NOMBRE, N''),
                ISNULL(SISTEMA, N'')
            FROM dbo.TA_USUARIOS
            WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = UPPER(LTRIM(RTRIM(@Usuario)))
              AND UPPER(LTRIM(RTRIM(SISTEMA))) = UPPER(LTRIM(RTRIM(@Sistema)));
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", normalizedUser);
        cmd.Parameters.AddWithValue("@Sistema", normalizedSystem);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
        {
            // dbo.CONV_MENSAJES tiene un CHECK (CK_CONV_MENSAJES_UsuarioAutor) que exige que
            // UsuarioAutor y SistemaAutor sean los DOS NULL o los DOS con valor -- no se puede
            // vaciar uno solo. Sumado a que UsuarioAutor tiene una FOREIGN KEY contra TA_USUARIOS
            // (no puede llevar un valor inventado como "AlfaCore" que no existe ahí), la única
            // combinación segura cuando el par no matchea un usuario legacy real es vaciar los DOS.
            // (Se intentó preservar solo Sistema para destrabar el tope de respuestas/guardarraíl
            // anti-repetición -- rompió el INSERT en producción por el CHECK. Revertido: para que
            // SistemaAutor identifique mensajes automáticos hace falta una columna nueva, dedicada,
            // no reusar este par legacy.)
            return (string.Empty, string.Empty);
        }

        return (GetString(rd, 0), GetString(rd, 1));
    }

    private async Task<long> GetExistingMessageIdByWhatsAppIdAsync(string whatsAppMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(whatsAppMessageId))
            return 0;

        const string sql = """
            SELECT TOP (1) IdMensaje
            FROM dbo.CONV_MENSAJES
            WHERE WhatsAppMessageId = @WhatsAppMessageId;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@WhatsAppMessageId", whatsAppMessageId.Trim());
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<(long MessageId, bool Created)> InsertInstagramMessageIfMissingAsync(
        long conversationId,
        IncomingInstagramMessage incoming,
        CancellationToken ct)
    {
        const string selectSql = """
            SELECT TOP (1) m.IdMensaje
            FROM dbo.CONV_MENSAJES m WITH (UPDLOCK, HOLDLOCK)
            WHERE m.IdConversacion = @IdConversacion
              AND m.MessageIdExterno = @MessageIdExterno
            ORDER BY m.IdMensaje DESC;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await using (var selectCmd = new SqlCommand(selectSql, cn, tx))
        {
            selectCmd.Parameters.AddWithValue("@IdConversacion", conversationId);
            selectCmd.Parameters.AddWithValue("@MessageIdExterno", incoming.MessageId.Trim());
            var existing = await selectCmd.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
            {
                var existingId = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                const string updateExistingSql = """
                    UPDATE dbo.CONV_MENSAJES
                    SET
                        Texto = COALESCE(NULLIF(@Texto, N''), Texto),
                        PayloadJson = COALESCE(NULLIF(@PayloadJson, N''), PayloadJson),
                        FechaHora_Modificacion = GETDATE()
                    WHERE IdMensaje = @IdMensaje;
                    """;

                await using var updateCmd = new SqlCommand(updateExistingSql, cn, tx);
                updateCmd.Parameters.AddWithValue("@IdMensaje", existingId);
                updateCmd.Parameters.AddWithValue("@Texto", DbNullable(incoming.Text));
                updateCmd.Parameters.AddWithValue("@PayloadJson", DbNullable(incoming.RawJson));
                await updateCmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                return (existingId, false);
            }
        }

        const string insertSql = """
            INSERT INTO dbo.CONV_MENSAJES
            (
                IdConversacion,
                MessageIdExterno,
                ReplyToMessageIdExterno,
                MessageType,
                Direction,
                EstadoEnvio,
                Texto,
                PayloadJson,
                FechaHora,
                FechaHora_Grabacion
            )
            VALUES
            (
                @IdConversacion,
                @MessageIdExterno,
                @ReplyToMessageIdExterno,
                @MessageType,
                N'ENTRANTE',
                N'RECIBIDO',
                @Texto,
                @PayloadJson,
                @FechaHora,
                GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        await using var insertCmd = new SqlCommand(insertSql, cn, tx);
        insertCmd.Parameters.AddWithValue("@IdConversacion", conversationId);
        insertCmd.Parameters.AddWithValue("@MessageIdExterno", incoming.MessageId.Trim());
        insertCmd.Parameters.AddWithValue("@ReplyToMessageIdExterno", DbNullable(incoming.ReplyToMessageId));
        insertCmd.Parameters.AddWithValue("@MessageType", NormalizeMessageType(incoming.MessageType));
        insertCmd.Parameters.AddWithValue("@Texto", DbNullable(incoming.Text));
        insertCmd.Parameters.AddWithValue("@PayloadJson", DbNullable(incoming.RawJson));
        insertCmd.Parameters.AddWithValue("@FechaHora", incoming.Timestamp);
        var result = await insertCmd.ExecuteScalarAsync(ct);
        var messageId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return (messageId, true);
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
                "No se pudo enviar la notificaciÃ³n push del mensaje entrante.",
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
        byte[]? archivoContenido,
        CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await EnsureConversationAttachmentsDurableColumnsOnceAsync(cn, ct);
        var supportsDurableContent = await ColumnExistsAsync(cn, "CONV_ADJUNTOS", "ArchivoContenido", ct);
        var sql = supportsDurableContent
            ? """
                INSERT INTO dbo.CONV_ADJUNTOS
                (
                    IdMensaje,
                    TipoArchivo,
                    NombreArchivo,
                    MimeType,
                    RutaLocal,
                    TamanoBytes,
                    PayloadJson,
                    ArchivoContenido,
                    ArchivoHashSha256,
                    AlmacenamientoEstado,
                    FechaHora_Archivo,
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
                    @ArchivoContenido,
                    @ArchivoHashSha256,
                    @AlmacenamientoEstado,
                    GETDATE(),
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """
            : """
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

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdMensaje", messageId);
        cmd.Parameters.AddWithValue("@TipoArchivo", NormalizeMessageType(tipoArchivo));
        cmd.Parameters.AddWithValue("@NombreArchivo", DbNullable(nombreArchivo));
        cmd.Parameters.AddWithValue("@MimeType", DbNullable(mimeType));
        cmd.Parameters.AddWithValue("@RutaLocal", DbNullable(ToStoredAttachmentPath(rutaLocal)));
        cmd.Parameters.AddWithValue("@TamanoBytes", tamanoBytes);
        cmd.Parameters.AddWithValue("@PayloadJson", DbNullable(payloadJson));
        if (supportsDurableContent)
        {
            var content = archivoContenido is { Length: > 0 } ? archivoContenido : null;
            cmd.Parameters.AddWithValue("@ArchivoContenido", content is null ? DBNull.Value : content);
            cmd.Parameters.AddWithValue("@ArchivoHashSha256", content is null ? DBNull.Value : Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
            cmd.Parameters.AddWithValue("@AlmacenamientoEstado", content is null ? "RUTA_LOCAL" : "SQL_Y_RUTA");
        }
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
                        c.FechaHoraCierre IS NULL
                        OR @FechaHora > c.FechaHoraCierre
                    ) AND (
                        UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) IN (N'CERRADA', N'CERRADO')
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
                        c.FechaHoraCierre IS NULL
                        OR @FechaHora > c.FechaHoraCierre
                    ) AND (
                        UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) IN (N'CERRADA', N'CERRADO')
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

    private async Task UpdateInstagramMessageDeliveryAsync(
        long idMensaje,
        string estadoEnvio,
        string messageIdExterno,
        string payloadJson,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_MENSAJES
            SET
                EstadoEnvio = @EstadoEnvio,
                MessageIdExterno = CASE WHEN @MessageIdExterno IS NULL THEN MessageIdExterno ELSE @MessageIdExterno END,
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
        cmd.Parameters.AddWithValue("@MessageIdExterno", DbNullable(messageIdExterno));
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
            ["recipient_type"] = "individual",
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
        client.Timeout = MetaSendTimeout;
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta devolviÃ³ {(int)response.StatusCode}: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar mensaje");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    // Sin esto, HttpClient usa su default de 100s -- si la API de Meta/Mercado Libre responde lento,
    // el request del webhook (y el bot que espera adentro) queda bloqueado ese tiempo entero sin dar
    // ninguna señal de error. Visto en producción: un mensaje del bot que quedó en "Pendiente" sin
    // confirmar éxito ni error.
    private static readonly TimeSpan MetaSendTimeout = TimeSpan.FromSeconds(30);

    private async Task<WhatsAppSendResult> SendToInstagramAsync(
        ConversacionInstagramConfigDto config,
        string recipientId,
        string text,
        CancellationToken ct)
    {
        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v25.0" : config.ApiVersion.Trim();
        var accountId = config.InstagramAccountId.Trim();
        var url = $"https://graph.instagram.com/{version}/{Uri.EscapeDataString(accountId)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var payload = new
        {
            recipient = new { id = recipientId.Trim() },
            message = new { text }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        client.Timeout = MetaSendTimeout;
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta Instagram devolviÃ³ {(int)response.StatusCode}: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar mensaje de Instagram");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    private async Task<WhatsAppSendResult> SendToFacebookMessengerAsync(
        ConversacionFacebookConfigDto config,
        string recipientId,
        string text,
        CancellationToken ct)
    {
        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v25.0" : config.ApiVersion.Trim();
        var pageId = config.PageId.Trim();
        var url = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(pageId)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        var payload = new
        {
            recipient = new { id = recipientId.Trim() },
            message = new { text }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        client.Timeout = MetaSendTimeout;
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Meta Messenger devolviÃ³ {(int)response.StatusCode}: {responseBody}");

        var messageId = RequireSentMessageId(responseBody, "enviar mensaje de Messenger");
        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_META",
            WhatsAppMessageId = messageId,
            PayloadJson = responseBody
        };
    }

    private async Task<WhatsAppSendResult> SendToMercadoLibreQuestionAsync(
        ConversacionMercadoLibreConfigDto config,
        string questionId,
        string text,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForApi)
            throw new InvalidOperationException("Falta configurar Mercado Libre para responder preguntas.");

        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/answers";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        object questionIdValue = long.TryParse(questionId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : questionId.Trim();
        var payload = new
        {
            question_id = questionIdValue,
            text
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = httpClientFactory.CreateClient();
        client.Timeout = MetaSendTimeout;
        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mercado Libre devolviÃ³ {(int)response.StatusCode}: {responseBody}");

        var messageId = $"meli-answer-{questionId.Trim()}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";

        return new WhatsAppSendResult
        {
            EstadoEnvio = "ENVIADO_MELI",
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

    private async Task<long> InsertWebhookLogAsync(
        string provider,
        string eventName,
        string payloadJson,
        string headerJson,
        CancellationToken ct)
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
                @Proveedor,
                @Evento,
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
        cmd.Parameters.AddWithValue("@Proveedor", provider.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Evento", eventName.Trim());
        cmd.Parameters.AddWithValue("@PayloadJson", payloadJson);
        cmd.Parameters.AddWithValue("@HeaderJson", DbNullable(headerJson));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static string ResolveWhatsAppDeliveryProvider(ConversacionWhatsAppConfigDto config)
    {
        var providerMode = (config.ProviderMode ?? string.Empty).Trim().ToUpperInvariant();
        var defaultProvider = (config.DefaultProvider ?? string.Empty).Trim().ToUpperInvariant();

        if (providerMode == ConversacionWhatsAppProviderModes.WebOnly)
            return ConversacionWhatsAppProviders.WhatsAppWeb;

        if (providerMode == ConversacionWhatsAppProviderModes.MetaAndWeb)
            return defaultProvider == ConversacionWhatsAppProviders.WhatsAppWeb
                ? ConversacionWhatsAppProviders.WhatsAppWeb
                : ConversacionWhatsAppProviders.MetaCloud;

        return ConversacionWhatsAppProviders.MetaCloud;
    }

    /// <summary>
    /// ResolveWhatsAppDeliveryProvider es un default GLOBAL de base (Modo del canal/Proveedor
    /// predeterminado en Configuración) -- no sabe nada de la conversación puntual. Una conversación
    /// ya sabe de qué número entró (IdNumeroWhatsApp); si ESE número tiene su propia instancia de
    /// WhatsApp Web (WebInstanceName, seteado una única vez al conectar por QR/código en
    /// WhatsAppWebSessionService.StartSessionAsync), el routing de esa conversación puntual es Web
    /// pase lo que pase con el select global -- nunca cae a Meta Cloud ni a "otra sesión conectada"
    /// solo porque el default de la base todavía diga Meta. Números agregados solo para Meta Cloud
    /// (sin pasar nunca por el flujo de conexión Business) no tienen WebInstanceName, así que siguen
    /// el default global exactamente como antes.
    /// </summary>
    private static string ResolveWhatsAppDeliveryProviderForNumero(ConversacionWhatsAppConfigDto config, ConversacionWhatsAppNumeroDto? numero)
        => !string.IsNullOrWhiteSpace(numero?.WebInstanceName)
            ? ConversacionWhatsAppProviders.WhatsAppWeb
            : ResolveWhatsAppDeliveryProvider(config);

    private async Task SetConversationWhatsAppNumeroCoreAsync(
        long idConversacion,
        int idNumero,
        string? usuario,
        string? sistema,
        CancellationToken ct)
    {
        const string validateSql = """
            SELECT TOP (1) c.Canal
            FROM dbo.CONV_CONVERSACIONES c
            WHERE c.IdConversacion = @IdConversacion;

            SELECT TOP (1) n.IdNumero
            FROM dbo.CONV_WHATSAPP_NUMEROS n
            WHERE n.IdNumero = @IdNumero
              AND ISNULL(n.Activo, 1) = 1;

            SELECT TOP (1) 1
            WHERE @Usuario IS NULL
               OR EXISTS (
                    SELECT 1
                    FROM dbo.CONV_ADMINISTRADORES adm
                    WHERE adm.Usuario = @Usuario
                      AND (@Sistema IS NULL OR adm.Sistema = @Sistema)
               )
               OR NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numUsu
                    WHERE numUsu.IdNumero = @IdNumero
               )
               OR EXISTS (
                    SELECT 1
                    FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numUsu
                    WHERE numUsu.IdNumero = @IdNumero
                      AND numUsu.Usuario = @Usuario
                      AND (@Sistema IS NULL OR numUsu.Sistema = @Sistema)
               );
            """;

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(validateSql, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            cmd.Parameters.AddWithValue("@IdNumero", idNumero);
            cmd.Parameters.AddWithValue("@Usuario", DbNullable(usuario));
            cmd.Parameters.AddWithValue("@Sistema", DbNullable(sistema));

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                throw new InvalidOperationException("La conversaci\u00f3n indicada no existe.");

            var canal = GetString(rd, 0);
            if (!string.Equals(canal, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Solo se puede cambiar la cuenta de envío en conversaciones de WhatsApp.");

            if (!await rd.NextResultAsync(ct) || !await rd.ReadAsync(ct))
                throw new InvalidOperationException("La cuenta de WhatsApp indicada no existe o no est\u00e1 activa.");

            if (!await rd.NextResultAsync(ct) || !await rd.ReadAsync(ct))
                throw new InvalidOperationException("No ten\u00e9s permiso para enviar por esa cuenta de WhatsApp.");
        }

        await UpdateConversationWhatsAppNumeroAsync(idConversacion, idNumero, ct);

        await _appEvents.LogAuditAsync(
            "Conversaciones",
            "SetConversationWhatsAppNumero",
            "CONV_CONVERSACIONES",
            idConversacion.ToString(CultureInfo.InvariantCulture),
            "Cuenta de WhatsApp de la conversaci\u00f3n actualizada.",
            new { IdNumeroWhatsApp = idNumero, Usuario = usuario, Sistema = sistema },
            ct);
    }

    private async Task<ConversacionWhatsAppNumeroDto?> ResolveWhatsAppWebNumeroForSendAsync(
        long idConversacion,
        ConversacionWhatsAppNumeroDto? assignedNumero,
        CancellationToken ct)
    {
        if (IsUsableWhatsAppWebNumero(assignedNumero))
            return assignedNumero;

        var fallback = (await conversacionesConfigService.GetWhatsAppNumerosAsync(ct))
            .Where(IsUsableWhatsAppWebNumero)
            .OrderByDescending(x => x.Activo)
            .ThenByDescending(x => x.WebRuntimeUpdatedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.IdNumero)
            .FirstOrDefault();

        if (fallback is null)
            return assignedNumero;

        if (assignedNumero?.IdNumero != fallback.IdNumero)
            await UpdateConversationWhatsAppNumeroAsync(idConversacion, fallback.IdNumero, ct);

        return fallback;
    }

    private async Task<ConversacionWhatsAppNumeroDto?> ResolveDefaultWhatsAppWebNumeroForManualConversationAsync(CancellationToken ct)
        => (await conversacionesConfigService.GetWhatsAppNumerosAsync(ct))
            .Where(IsUsableWhatsAppWebNumero)
            .OrderByDescending(x => x.Activo)
            .ThenByDescending(x => x.WebRuntimeUpdatedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.IdNumero)
            .FirstOrDefault();

    private static bool IsUsableWhatsAppWebNumero(ConversacionWhatsAppNumeroDto? numero)
        => numero is not null
           && numero.IsWebSessionReady
           && !string.IsNullOrWhiteSpace(numero.WebInstanceName);

    private async Task ReassignStaleWhatsAppConversationsToDefaultWebAsync(long? idConversacion, CancellationToken ct)
    {
        var defaultWebNumero = await ResolveDefaultWhatsAppWebNumeroForManualConversationAsync(ct);
        if (defaultWebNumero is null)
            return;

        const string sql = """
            UPDATE c
            SET IdNumeroWhatsApp = @IdNumero,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            WHERE c.Canal = N'WHATSAPP'
              AND (UPPER(LTRIM(RTRIM(ISNULL(c.CodigoEstado, N'')))) = N'ABIERTA' OR @IdConversacion IS NOT NULL)
              AND (@IdConversacion IS NULL OR c.IdConversacion = @IdConversacion)
              AND (
                    c.IdNumeroWhatsApp IS NULL
                    OR NOT EXISTS (
                        SELECT 1
                        FROM dbo.CONV_WHATSAPP_NUMEROS n
                        WHERE n.IdNumero = c.IdNumeroWhatsApp
                          AND LTRIM(RTRIM(ISNULL(n.WebInstanceName, N''))) <> N''
                          AND UPPER(LTRIM(RTRIM(ISNULL(n.WebSessionStatus, N'')))) = N'CONNECTED'
                    )
              );
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdNumero", defaultWebNumero.IdNumero);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion.HasValue ? idConversacion.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateConversationWhatsAppNumeroAsync(long idConversacion, int idNumero, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_CONVERSACIONES
            SET IdNumeroWhatsApp = @IdNumero,
                FechaHora_Modificacion = GETDATE()
            WHERE IdConversacion = @IdConversacion
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@IdNumero", idNumero);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void EnsureWhatsAppProviderImplemented(string provider, string actionLabel)
    {
        if (string.Equals(provider, ConversacionWhatsAppProviders.WhatsAppWeb, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La base está configurada para usar WhatsApp Web al {actionLabel}, pero ese conector todavía no está implementado en AlfaCore. " +
                "Por ahora dejá el proveedor en Meta Cloud API o en convivencia con Meta como predeterminado.");
        }
    }

    private static void EnsureWhatsAppMetaProvider(ConversacionWhatsAppConfigDto config, string actionLabel)
    {
        var provider = ResolveWhatsAppDeliveryProvider(config);
        if (!string.Equals(provider, ConversacionWhatsAppProviders.MetaCloud, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La acción de {actionLabel} hoy solo está disponible con Meta Cloud API. " +
                "Si querés usarla, dejá Meta como proveedor predeterminado para WhatsApp.");
        }
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

    private static async Task<ConversationLookupResult?> FindWhatsAppConversationByPhoneAsync(SqlConnection cn, string phone, int? idContact, int? idNumeroWhatsApp, CancellationToken ct)
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
                    (@IdNumeroWhatsApp IS NULL AND IdNumeroWhatsApp IS NULL)
                    OR IdNumeroWhatsApp = @IdNumeroWhatsApp
                  )
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
        cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", idNumeroWhatsApp.HasValue ? idNumeroWhatsApp.Value : DBNull.Value);
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

    private static async Task<ConversationLookupResult?> FindProvisionalWhatsAppConversationByPhoneAsync(
        SqlConnection cn,
        string phone,
        int? idContact,
        CancellationToken ct)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        var phoneTail = GetPhoneComparableTail(normalizedPhone);
        var sql = $"""
            SELECT TOP (1)
                c.IdConversacion,
                ISNULL(c.NombreVisible, ''),
                c.IdContacto
            FROM dbo.CONV_CONVERSACIONES c
            INNER JOIN dbo.CONV_WHATSAPP_NUMEROS n
                ON n.IdNumero = c.IdNumeroWhatsApp
            WHERE c.Canal = N'WHATSAPP'
              AND LTRIM(RTRIM(n.PhoneNumberId)) LIKE N'WEBPENDING-%'
              AND (
                    {SqlPhoneEquivalentPredicate("c.TelefonoWhatsApp", "@TelefonoWhatsApp", "@TelefonoWhatsAppTail")}
                    OR (@IdContacto IS NOT NULL AND c.IdContacto = @IdContacto)
                  )
            ORDER BY c.FechaHoraUltimoMensaje DESC, c.IdConversacion DESC;
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

    private static async Task UpdateConversationWhatsAppNumberAsync(SqlConnection cn, long idConversacion, int idNumeroWhatsApp, CancellationToken ct)
    {
        const string sql = """
            UPDATE dbo.CONV_CONVERSACIONES
            SET
                IdNumeroWhatsApp = @IdNumeroWhatsApp,
                FechaHora_Modificacion = GETDATE()
            WHERE IdConversacion = @IdConversacion;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", idNumeroWhatsApp);
        await cmd.ExecuteNonQueryAsync(ct);
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
                "Conversaciones vinculadas automÃ¡ticamente a contactos por telÃ©fono equivalente.",
                new { ConversacionesVinculadas = linked, IdConversacion = idConversacion },
                ct);
        }
    }

    private async Task MergeDuplicateWhatsAppConversationsAsync(SqlConnection cn, long canonicalId, string phone, int? idContact, int? idNumeroWhatsApp, CancellationToken ct)
    {
        var duplicates = await FindDuplicateWhatsAppConversationIdsAsync(cn, canonicalId, phone, idContact, idNumeroWhatsApp, ct);
        foreach (var duplicateId in duplicates)
        {
            await MergeDuplicateWhatsAppConversationAsync(cn, canonicalId, duplicateId, ct);

            await _appEvents.LogAuditAsync(
                "Conversaciones",
                "MergeDuplicateWhatsAppConversation",
                "CONV_CONVERSACIONES",
                canonicalId.ToString(CultureInfo.InvariantCulture),
                "ConversaciÃ³n duplicada consolidada por telÃ©fono equivalente.",
                new { ConversacionConservada = canonicalId, ConversacionDuplicada = duplicateId, TelefonoWhatsApp = NormalizePhone(phone), IdContacto = idContact, IdNumeroWhatsApp = idNumeroWhatsApp },
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
                    "ConversaciÃ³n duplicada existente consolidada al cargar inbox.",
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
                IdContacto,
                IdNumeroWhatsApp
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'WHATSAPP'
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_MENSAJES msg
                    WHERE msg.IdConversacion = CONV_CONVERSACIONES.IdConversacion
                  )
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
                IdContacto = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                IdNumeroWhatsApp = rd.IsDBNull(3) ? null : rd.GetInt32(3)
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
                     .GroupBy(x => $"{BuildNumeroWhatsAppMergeKey(x.Item.IdNumeroWhatsApp)}:TEL:{x.Key}", StringComparer.OrdinalIgnoreCase))
        {
            var items = group.Select(x => x.Item).OrderBy(x => x.IdConversacion).ToList();
            if (items.Count > 1 && emitted.Add(group.Key))
                yield return items;
        }

        foreach (var group in records
                     .Where(x => x.IdContacto.HasValue)
                     .GroupBy(x => $"{BuildNumeroWhatsAppMergeKey(x.IdNumeroWhatsApp)}:CONTACTO:{x.IdContacto!.Value.ToString(CultureInfo.InvariantCulture)}", StringComparer.OrdinalIgnoreCase))
        {
            var items = group.OrderBy(x => x.IdConversacion).ToList();
            if (items.Count > 1 && emitted.Add(group.Key))
                yield return items;
        }
    }

    private static string BuildNumeroWhatsAppMergeKey(int? idNumeroWhatsApp)
        => idNumeroWhatsApp.HasValue
            ? $"NUM:{idNumeroWhatsApp.Value.ToString(CultureInfo.InvariantCulture)}"
            : "NUM:NULL";

    private static async Task<List<long>> FindDuplicateWhatsAppConversationIdsAsync(SqlConnection cn, long canonicalId, string phone, int? idContact, int? idNumeroWhatsApp, CancellationToken ct)
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
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_MENSAJES msg
                    WHERE msg.IdConversacion = CONV_CONVERSACIONES.IdConversacion
                  )
              AND (
                    (@IdNumeroWhatsApp IS NULL AND IdNumeroWhatsApp IS NULL)
                    OR IdNumeroWhatsApp = @IdNumeroWhatsApp
                  )
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
        cmd.Parameters.AddWithValue("@IdNumeroWhatsApp", idNumeroWhatsApp.HasValue ? idNumeroWhatsApp.Value : DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result.Add(rd.GetInt64(0));

        return result;
    }

    private static async Task MergeDuplicateWhatsAppConversationAsync(SqlConnection cn, long canonicalId, long duplicateId, CancellationToken ct)
    {
        const string sql = """
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_WARNINGS ON;
            SET ANSI_PADDING ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET ARITHABORT ON;
            SET NUMERIC_ROUNDABORT OFF;

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

            UPDATE cur
            SET
                IdUltimoMensajeLeido = CASE
                    WHEN dup.IdUltimoMensajeLeido > cur.IdUltimoMensajeLeido THEN dup.IdUltimoMensajeLeido
                    ELSE cur.IdUltimoMensajeLeido
                END,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO cur
            INNER JOIN dbo.CONV_CONVERSACIONES_LECTURA_USUARIO dup
                ON dup.Usuario = cur.Usuario
               AND dup.Sistema = cur.Sistema
               AND dup.IdConversacion = @DuplicateId
            WHERE cur.IdConversacion = @CanonicalId;

            UPDATE dup
            SET IdConversacion = @CanonicalId,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO dup
            WHERE dup.IdConversacion = @DuplicateId
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO cur
                    WHERE cur.IdConversacion = @CanonicalId
                      AND cur.Usuario = dup.Usuario
                      AND cur.Sistema = dup.Sistema
              );

            DELETE FROM dbo.CONV_CONVERSACIONES_LECTURA_USUARIO
            WHERE IdConversacion = @DuplicateId;

            UPDATE dup
            SET IdConversacion = @CanonicalId
            FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO dup
            WHERE dup.IdConversacion = @DuplicateId
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO cur
                    WHERE cur.IdConversacion = @CanonicalId
                      AND cur.Usuario = dup.Usuario
                      AND cur.Sistema = dup.Sistema
              );

            DELETE FROM dbo.CONV_CONVERSACIONES_PIN_USUARIO
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
            throw new InvalidOperationException("El tÃ©cnico indicado no existe o estÃ¡ dado de baja.");
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
            SELECT TOP (500)
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
            )
            ORDER BY c.FechaHoraUltimoMensaje DESC, c.IdConversacion DESC;
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

        item.RutaLocal = ToAbsoluteAttachmentPath(item.RutaLocal);
        if (string.IsNullOrWhiteSpace(item.RutaLocal) || !File.Exists(item.RutaLocal))
            throw new InvalidOperationException("El archivo local del sticker favorito no estÃ¡ disponible.");

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

    private async Task<string> GetFirstClosedConversationStateAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(CodigoEstado, '')
            FROM dbo.CONV_ESTADOS
            WHERE ISNULL(EsCerrado, 0) = 1
              AND ISNULL(Activo, 1) = 1
            ORDER BY ISNULL(Orden, 999), CodigoEstado;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var state = result is null || result is DBNull
            ? string.Empty
            : Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;

        return string.IsNullOrWhiteSpace(state) ? "CERRADA" : state.Trim().ToUpperInvariant();
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

    private async Task<int> GetIncomingCountAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.CONV_MENSAJES
            WHERE IdConversacion = @IdConversacion AND Direction = N'ENTRANTE'
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<string> GetConversationChannelAsync(long idConversacion, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) ISNULL(Canal, '')
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

    /// <summary>
    /// Resuelve la cuenta comercial (Cliente o Proveedor) vinculada a una conversación, para el
    /// asistente de IA -- ver conversaciones_agente_ia_plan.md. Primero el camino rápido ya existente
    /// (CONV_CONVERSACIONES.ClienteCodigo, específico de Cliente); si no hay, generaliza el vínculo
    /// Contacto -> Cuenta comercial (dbo.MA_CONTACTOS_CUENTAS) probando VT_CLIENTES y, si no matchea,
    /// VT_PROVEEDORES. Sin match en ningún lado, devuelve null -- el asistente no debe ofrecer
    /// herramientas sensibles (saldo/pedidos) sin una cuenta identificada.
    /// </summary>
    private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync(long idConversacion, CancellationToken ct)
    {
        string clienteCodigo;
        int? idContacto;

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            const string sqlConversacion = """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(ClienteCodigo)), N''), IdContacto
                FROM dbo.CONV_CONVERSACIONES
                WHERE IdConversacion = @IdConversacion;
                """;
            await using var cmd = new SqlCommand(sqlConversacion, cn);
            cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                return null;

            clienteCodigo = GetString(rd, 0);
            idContacto = rd.IsDBNull(1) ? null : rd.GetInt32(1);
        }

        if (!string.IsNullOrWhiteSpace(clienteCodigo))
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            const string sqlCliente = """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), N'')
                FROM dbo.VT_CLIENTES
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                """;
            await using var cmd = new SqlCommand(sqlCliente, cn);
            cmd.Parameters.AddWithValue("@Codigo", clienteCodigo);
            var razonSocial = await cmd.ExecuteScalarAsync(ct) as string ?? string.Empty;
            return new ConversacionCuentaVinculadaDto(clienteCodigo, CuentaComercialTipo.Cliente, razonSocial);
        }

        if (idContacto is null)
            return null;

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            const string sqlContacto = """
                SELECT TOP (1)
                    LTRIM(RTRIM(mcc.Cuenta)),
                    CASE WHEN cli.CODIGO IS NOT NULL THEN 1 ELSE 2 END AS TipoOrdinal,
                    ISNULL(LTRIM(RTRIM(ISNULL(cli.RAZON_SOCIAL, prv.RAZON_SOCIAL))), N'')
                FROM dbo.MA_CONTACTOS_CUENTAS mcc
                LEFT JOIN dbo.VT_CLIENTES cli ON UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(mcc.Cuenta)))
                LEFT JOIN dbo.VT_PROVEEDORES prv ON UPPER(LTRIM(RTRIM(prv.CODIGO))) = UPPER(LTRIM(RTRIM(mcc.Cuenta))) AND cli.CODIGO IS NULL
                WHERE mcc.IdContacto = @IdContacto
                  AND (cli.CODIGO IS NOT NULL OR prv.CODIGO IS NOT NULL)
                ORDER BY TipoOrdinal;
                """;
            await using var cmd = new SqlCommand(sqlContacto, cn);
            cmd.Parameters.AddWithValue("@IdContacto", idContacto.Value);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                return null;

            var codigo = rd.GetString(0);
            var tipo = rd.GetInt32(1) == 1 ? CuentaComercialTipo.Cliente : CuentaComercialTipo.Proveedor;
            var razonSocial = GetString(rd, 2);
            return new ConversacionCuentaVinculadaDto(codigo, tipo, razonSocial);
        }
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

    private static List<IncomingInstagramMessage> ParseIncomingInstagramMessages(JsonElement root)
    {
        var items = new List<IncomingInstagramMessage>();
        if (!root.TryGetProperty("object", out var objectProperty)
            || !string.Equals(objectProperty.GetString(), "instagram", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in entries.EnumerateArray())
        {
            var accountId = entry.TryGetProperty("id", out var accountIdProperty)
                ? accountIdProperty.GetString() ?? string.Empty
                : string.Empty;
            if (!entry.TryGetProperty("messaging", out var messaging) || messaging.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in messaging.EnumerateArray())
            {
                var senderId = ReadNestedString(item, "sender", "id");
                var recipientId = ReadNestedString(item, "recipient", "id");
                var timestamp = ParseInstagramTimestamp(item);

                if (item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    var messageId = message.TryGetProperty("mid", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(messageId))
                        continue;

                    var text = message.TryGetProperty("text", out var textProperty)
                        ? textProperty.GetString() ?? string.Empty
                        : string.Empty;
                    var messageType = ResolveInstagramMessageType(message);
                    if (string.IsNullOrWhiteSpace(text))
                        text = BuildInstagramAttachmentSummary(messageType);

                    items.Add(new IncomingInstagramMessage
                    {
                        AccountId = accountId,
                        SenderId = senderId,
                        RecipientId = recipientId,
                        MessageId = messageId,
                        ReplyToMessageId = ReadNestedString(message, "reply_to", "mid"),
                        MessageType = messageType,
                        Timestamp = timestamp,
                        Text = text,
                        IsEcho = message.TryGetProperty("is_echo", out var echo) && echo.ValueKind == JsonValueKind.True,
                        RawJson = item.GetRawText()
                    });
                    continue;
                }

                if (item.TryGetProperty("postback", out var postback) && postback.ValueKind == JsonValueKind.Object)
                {
                    var messageId = postback.TryGetProperty("mid", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(messageId))
                        continue;

                    var title = postback.TryGetProperty("title", out var titleProperty)
                        ? titleProperty.GetString() ?? string.Empty
                        : string.Empty;
                    var payload = postback.TryGetProperty("payload", out var payloadProperty)
                        ? payloadProperty.GetString() ?? string.Empty
                        : string.Empty;

                    items.Add(new IncomingInstagramMessage
                    {
                        AccountId = accountId,
                        SenderId = senderId,
                        RecipientId = recipientId,
                        MessageId = messageId,
                        MessageType = "TEXT",
                        Timestamp = timestamp,
                        Text = FirstNonEmpty(title, payload, "InteracciÃ³n recibida desde Instagram."),
                        RawJson = item.GetRawText()
                    });
                }
            }
        }

        return items;
    }

    private static List<IncomingInstagramMessage> ParseIncomingFacebookMessages(JsonElement root)
    {
        var items = new List<IncomingInstagramMessage>();
        if (!root.TryGetProperty("object", out var objectProperty)
            || !string.Equals(objectProperty.GetString(), "page", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = entry.TryGetProperty("id", out var pageIdProperty)
                ? pageIdProperty.GetString() ?? string.Empty
                : string.Empty;
            if (!entry.TryGetProperty("messaging", out var messaging) || messaging.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in messaging.EnumerateArray())
            {
                var senderId = ReadNestedString(item, "sender", "id");
                var recipientId = ReadNestedString(item, "recipient", "id");
                var timestamp = ParseInstagramTimestamp(item);

                if (item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    var messageId = message.TryGetProperty("mid", out var mid) ? mid.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(messageId))
                        continue;

                    var text = message.TryGetProperty("text", out var textProperty)
                        ? textProperty.GetString() ?? string.Empty
                        : string.Empty;
                    var messageType = ResolveInstagramMessageType(message);
                    if (string.IsNullOrWhiteSpace(text))
                        text = BuildFacebookAttachmentSummary(messageType);

                    items.Add(new IncomingInstagramMessage
                    {
                        AccountId = pageId,
                        SenderId = senderId,
                        RecipientId = recipientId,
                        MessageId = messageId,
                        ReplyToMessageId = ReadNestedString(message, "reply_to", "mid"),
                        MessageType = messageType,
                        Timestamp = timestamp,
                        Text = text,
                        IsEcho = message.TryGetProperty("is_echo", out var echo) && echo.ValueKind == JsonValueKind.True,
                        RawJson = BuildFacebookIncomingPayloadJson(item, pageId)
                    });
                    continue;
                }

                if (item.TryGetProperty("postback", out var postback) && postback.ValueKind == JsonValueKind.Object)
                {
                    var title = postback.TryGetProperty("title", out var titleProperty)
                        ? titleProperty.GetString() ?? string.Empty
                        : string.Empty;
                    var payload = postback.TryGetProperty("payload", out var payloadProperty)
                        ? payloadProperty.GetString() ?? string.Empty
                        : string.Empty;
                    var messageId = FirstNonEmpty(
                        postback.TryGetProperty("mid", out var mid) ? mid.GetString() ?? string.Empty : string.Empty,
                        $"postback:{senderId}:{timestamp:yyyyMMddHHmmssfff}");

                    items.Add(new IncomingInstagramMessage
                    {
                        AccountId = pageId,
                        SenderId = senderId,
                        RecipientId = recipientId,
                        MessageId = messageId,
                        MessageType = "TEXT",
                        Timestamp = timestamp,
                        Text = FirstNonEmpty(title, payload, "InteracciÃ³n recibida desde Facebook Messenger."),
                        RawJson = BuildFacebookIncomingPayloadJson(item, pageId)
                    });
                }
            }
        }

        return items;
    }

    private static string ReadNestedString(JsonElement parent, string objectName, string propertyName)
    {
        if (!parent.TryGetProperty(objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !nested.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.GetString() ?? string.Empty;
    }

    private static string BuildFacebookIncomingPayloadJson(JsonElement item, string pageId)
        => JsonSerializer.Serialize(new
        {
            entry_page_id = pageId,
            raw = JsonSerializer.Deserialize<JsonElement>(item.GetRawText())
        });

    private static List<MercadoLibreNotification> ParseMercadoLibreNotifications(JsonElement root)
    {
        var items = new List<MercadoLibreNotification>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                AddMercadoLibreNotification(items, item);
            return items;
        }

        AddMercadoLibreNotification(items, root);
        return items;
    }

    private static void AddMercadoLibreNotification(List<MercadoLibreNotification> items, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;

        var topic = ReadJsonString(root, "topic");
        var resource = ReadJsonString(root, "resource");
        var userId = FirstNonEmpty(ReadJsonString(root, "user_id"), ReadJsonString(root, "userId"), ReadJsonString(root, "seller_id"));
        var notificationId = FirstNonEmpty(ReadJsonString(root, "_id"), ReadJsonString(root, "id"), resource);
        var questionId = ExtractMercadoLibreQuestionId(resource);
        var sent = ParseMercadoLibreDate(FirstNonEmpty(ReadJsonString(root, "sent"), ReadJsonString(root, "received")));

        if (string.IsNullOrWhiteSpace(topic) && resource.Contains("question", StringComparison.OrdinalIgnoreCase))
            topic = "questions";

        items.Add(new MercadoLibreNotification
        {
            Id = notificationId,
            Topic = topic,
            Resource = resource,
            UserId = userId,
            QuestionId = questionId,
            Timestamp = sent,
            RawJson = root.GetRawText()
        });
    }

    private async Task<MercadoLibreQuestion> TryGetMercadoLibreQuestionAsync(
        ConversacionMercadoLibreConfigDto config,
        MercadoLibreNotification notification,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForApi || string.IsNullOrWhiteSpace(notification.QuestionId))
            return MercadoLibreQuestion.FromNotification(notification);

        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/questions/{Uri.EscapeDataString(notification.QuestionId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return MercadoLibreQuestion.FromNotification(notification);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var buyerId = string.Empty;
            if (root.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
                buyerId = ReadJsonString(from, "id");

            var question = new MercadoLibreQuestion
            {
                QuestionId = FirstNonEmpty(ReadJsonString(root, "id"), notification.QuestionId),
                BuyerId = FirstNonEmpty(buyerId, notification.UserId),
                SellerId = FirstNonEmpty(ReadJsonString(root, "seller_id"), config.SellerId),
                ItemId = ReadJsonString(root, "item_id"),
                Status = ReadJsonString(root, "status"),
                Text = FirstNonEmpty(ReadJsonString(root, "text"), "Pregunta recibida en Mercado Libre."),
                Timestamp = ParseMercadoLibreDate(FirstNonEmpty(ReadJsonString(root, "date_created"), ReadJsonString(root, "last_updated"), notification.Timestamp.ToString("O", CultureInfo.InvariantCulture))),
                RawJson = body
            };

            await EnrichMercadoLibreQuestionAsync(config, question, ct);
            return question;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return MercadoLibreQuestion.FromNotification(notification);
        }
    }

    private async Task<List<MercadoLibreQuestion>> GetMercadoLibreUnansweredQuestionsAsync(
        ConversacionMercadoLibreConfigDto config,
        List<string> diagnostics,
        CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        var client = httpClientFactory.CreateClient();
        var result = new List<MercadoLibreQuestion>();
        await AddMercadoLibreReceivedQuestionsByStatusAsync(client, baseUrl, config, "UNANSWERED", result, diagnostics, ct);
        await AddMercadoLibreReceivedQuestionsByStatusAsync(client, baseUrl, config, "ANSWERED", result, diagnostics, ct);
        await AddMercadoLibreQuestionsByStatusAsync(client, baseUrl, config, "UNANSWERED", result, diagnostics, ct);
        await AddMercadoLibreQuestionsByStatusAsync(client, baseUrl, config, "ANSWERED", result, diagnostics, ct);

        var itemIds = (await GetKnownMercadoLibreItemIdsAsync(ct))
            .Concat(await TryGetMercadoLibreSellerItemIdsAsync(client, baseUrl, config, diagnostics, ct))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        diagnostics.Add($"Publicaciones consultadas: {itemIds.Count.ToString(CultureInfo.InvariantCulture)}");

        foreach (var itemId in itemIds)
        {
            await TryAddMercadoLibreQuestionsByItemAsync(client, baseUrl, config, itemId, string.Empty, result, diagnostics, ct);
            await TryAddMercadoLibreQuestionsByItemAsync(client, baseUrl, config, itemId, "UNANSWERED", result, diagnostics, ct);
            await TryAddMercadoLibreQuestionsByItemAsync(client, baseUrl, config, itemId, "ANSWERED", result, diagnostics, ct);
        }

        await EnrichMercadoLibreQuestionsAsync(config, result, ct);
        return result
            .GroupBy(x => x.QuestionId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(q => q.Timestamp).First())
            .OrderByDescending(x => x.Timestamp)
            .ToList();
    }

    private static async Task AddMercadoLibreReceivedQuestionsByStatusAsync(
        HttpClient client,
        string baseUrl,
        ConversacionMercadoLibreConfigDto config,
        string status,
        List<MercadoLibreQuestion> result,
        List<string> diagnostics,
        CancellationToken ct)
    {
        var receivedStatus = status.ToLowerInvariant();
        var url = $"{baseUrl}/my/received_questions/search?status={Uri.EscapeDataString(receivedStatus)}&sort_fields=date_created&sort_types=DESC&limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add($"Recibidas {status}: HTTP {(int)response.StatusCode}");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Recibidas {status}: 0");
                return;
            }

            var found = 0;
            foreach (var item in questions.EnumerateArray())
            {
                result.Add(ParseMercadoLibreQuestion(item, body, config.SellerId));
                found++;
            }

            diagnostics.Add($"Recibidas {status}: {found.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            diagnostics.Add($"Recibidas {status}: {ex.GetType().Name}");
        }
    }

    private static async Task AddMercadoLibreQuestionsByStatusAsync(
        HttpClient client,
        string baseUrl,
        ConversacionMercadoLibreConfigDto config,
        string status,
        List<MercadoLibreQuestion> result,
        List<string> diagnostics,
        CancellationToken ct)
    {
        var url = $"{baseUrl}/questions/search?seller_id={Uri.EscapeDataString(config.SellerId.Trim())}&status={Uri.EscapeDataString(status)}&sort_fields=date_created&sort_types=DESC&limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mercado Libre devolviÃ³ {(int)response.StatusCode} al sincronizar preguntas {status}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            return;

        var found = 0;
        foreach (var item in questions.EnumerateArray())
        {
            result.Add(ParseMercadoLibreQuestion(item, body, config.SellerId));
            found++;
        }

        diagnostics.Add($"{status}: {found.ToString(CultureInfo.InvariantCulture)}");
    }

    private async Task<IReadOnlyList<string>> GetKnownMercadoLibreItemIdsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT TOP (50) LTRIM(RTRIM(ISNULL(UsuarioExterno, N'')))
            FROM dbo.CONV_CONVERSACIONES
            WHERE Canal = N'MERCADOLIBRE'
              AND LTRIM(RTRIM(ISNULL(UsuarioExterno, N''))) <> N''
            ORDER BY LTRIM(RTRIM(ISNULL(UsuarioExterno, N'')));
            """;

        var result = new List<string>();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            result.Add(GetString(rd, 0));

        return result;
    }

    private static async Task<IReadOnlyList<string>> TryGetMercadoLibreSellerItemIdsAsync(
        HttpClient client,
        string baseUrl,
        ConversacionMercadoLibreConfigDto config,
        List<string> diagnostics,
        CancellationToken ct)
    {
        var result = new List<string>();
        var url = $"{baseUrl}/users/{Uri.EscapeDataString(config.SellerId.Trim())}/items/search?status=active,paused&limit=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add($"Publicaciones ML: HTTP {(int)response.StatusCode}");
                return result;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    var itemId = ConvertJsonElementToString(item);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        result.Add(itemId);
                }
            }

            diagnostics.Add($"Publicaciones ML: {result.Count.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            diagnostics.Add($"Publicaciones ML: {ex.GetType().Name}");
        }

        return result;
    }

    private static async Task TryAddMercadoLibreQuestionsByItemAsync(
        HttpClient client,
        string baseUrl,
        ConversacionMercadoLibreConfigDto config,
        string itemId,
        string status,
        List<MercadoLibreQuestion> result,
        List<string> diagnostics,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        if (await TryAddMercadoLibreQuestionsByItemParameterAsync(client, baseUrl, config, "item", itemId, status, result, ct))
            return;

        await TryAddMercadoLibreQuestionsByItemParameterAsync(client, baseUrl, config, "item_id", itemId, status, result, ct);
    }

    private static async Task<bool> TryAddMercadoLibreQuestionsByItemParameterAsync(
        HttpClient client,
        string baseUrl,
        ConversacionMercadoLibreConfigDto config,
        string itemParameterName,
        string itemId,
        string status,
        List<MercadoLibreQuestion> result,
        CancellationToken ct)
    {
        var statusFilter = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : $"&status={Uri.EscapeDataString(status)}";
        var url = $"{baseUrl}/questions/search?{itemParameterName}={Uri.EscapeDataString(itemId.Trim())}{statusFilter}&sort_fields=date_created&sort_types=DESC&limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return false;

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            return false;

        var found = false;
        foreach (var item in questions.EnumerateArray())
        {
            result.Add(ParseMercadoLibreQuestion(item, body, config.SellerId));
            found = true;
        }

        return found;
    }

    private async Task EnrichMercadoLibreQuestionsAsync(
        ConversacionMercadoLibreConfigDto config,
        List<MercadoLibreQuestion> questions,
        CancellationToken ct)
    {
        var itemIds = questions
            .Select(x => x.ItemId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var itemId in itemIds)
        {
            var item = await TryGetMercadoLibreItemAsync(config, itemId, ct);
            if (item is null)
                continue;

            foreach (var question in questions.Where(x => string.Equals(x.ItemId, itemId, StringComparison.OrdinalIgnoreCase)))
                ApplyMercadoLibreItem(question, item);
        }

        var buyerIds = questions
            .Select(x => x.BuyerId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var buyerId in buyerIds)
        {
            var buyer = await TryGetMercadoLibreBuyerAsync(config, buyerId, ct);
            if (buyer is null)
                continue;

            foreach (var question in questions.Where(x => string.Equals(x.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase)))
                ApplyMercadoLibreBuyer(question, buyer);
        }
    }

    private async Task EnrichMercadoLibreQuestionAsync(
        ConversacionMercadoLibreConfigDto config,
        MercadoLibreQuestion question,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(question.ItemId))
        {
            var item = await TryGetMercadoLibreItemAsync(config, question.ItemId, ct);
            if (item is not null)
                ApplyMercadoLibreItem(question, item);
        }

        if (!string.IsNullOrWhiteSpace(question.BuyerId))
        {
            var buyer = await TryGetMercadoLibreBuyerAsync(config, question.BuyerId, ct);
            if (buyer is not null)
                ApplyMercadoLibreBuyer(question, buyer);
        }
    }

    private static void ApplyMercadoLibreItem(MercadoLibreQuestion question, MercadoLibreItem item)
    {
        question.ItemTitle = item.Title;
        question.ItemPermalink = item.Permalink;
        question.ItemPictureUrl = item.PictureUrl;
        question.ItemStatus = item.Status;
    }

    private static void ApplyMercadoLibreBuyer(MercadoLibreQuestion question, MercadoLibreBuyer buyer)
    {
        question.BuyerNickname = buyer.Nickname;
        question.BuyerFirstName = buyer.FirstName;
        question.BuyerLastName = buyer.LastName;
    }

    private async Task<MercadoLibreItem?> TryGetMercadoLibreItemAsync(
        ConversacionMercadoLibreConfigDto config,
        string itemId,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForApi || string.IsNullOrWhiteSpace(itemId))
            return null;

        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/items/{Uri.EscapeDataString(itemId.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var pictureUrl = FirstNonEmpty(
                ReadJsonString(root, "secure_thumbnail"),
                ReadJsonString(root, "thumbnail"));

            if (string.IsNullOrWhiteSpace(pictureUrl)
                && root.TryGetProperty("pictures", out var pictures)
                && pictures.ValueKind == JsonValueKind.Array)
            {
                foreach (var picture in pictures.EnumerateArray())
                {
                    pictureUrl = FirstNonEmpty(
                        ReadJsonString(picture, "secure_url"),
                        ReadJsonString(picture, "url"));
                    if (!string.IsNullOrWhiteSpace(pictureUrl))
                        break;
                }
            }

            return new MercadoLibreItem
            {
                Id = FirstNonEmpty(ReadJsonString(root, "id"), itemId),
                Title = ReadJsonString(root, "title"),
                Status = ReadJsonString(root, "status"),
                Permalink = ReadJsonString(root, "permalink"),
                PictureUrl = pictureUrl
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<MercadoLibreBuyer?> TryGetMercadoLibreBuyerAsync(
        ConversacionMercadoLibreConfigDto config,
        string buyerId,
        CancellationToken ct)
    {
        if (!config.IsConfiguredForApi || string.IsNullOrWhiteSpace(buyerId))
            return null;

        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.mercadolibre.com"
            : config.ApiBaseUrl.Trim().TrimEnd('/');
        var url = $"{baseUrl}/users/{Uri.EscapeDataString(buyerId.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());

        try
        {
            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new MercadoLibreBuyer
            {
                Id = FirstNonEmpty(ReadJsonString(root, "id"), buyerId),
                Nickname = ReadJsonString(root, "nickname"),
                FirstName = ReadJsonString(root, "first_name"),
                LastName = ReadJsonString(root, "last_name")
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static MercadoLibreQuestion ParseMercadoLibreQuestion(JsonElement root, string rawJson, string fallbackSellerId)
    {
        var buyerId = string.Empty;
        if (root.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
            buyerId = ReadJsonString(from, "id");

        return new MercadoLibreQuestion
        {
            QuestionId = ReadJsonString(root, "id"),
            BuyerId = buyerId,
            SellerId = FirstNonEmpty(ReadJsonString(root, "seller_id"), fallbackSellerId),
            ItemId = ReadJsonString(root, "item_id"),
            Status = ReadJsonString(root, "status"),
            Text = FirstNonEmpty(ReadJsonString(root, "text"), "Pregunta recibida en Mercado Libre."),
            Timestamp = ParseMercadoLibreDate(FirstNonEmpty(ReadJsonString(root, "date_created"), ReadJsonString(root, "last_updated"))),
            RawJson = root.GetRawText()
        };
    }

    private static string ExtractMercadoLibreQuestionId(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return string.Empty;

        var match = Regex.Match(resource, @"questions/([^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static DateTime ParseMercadoLibreDate(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.LocalDateTime;

        return BusinessNow();
    }

    private static string ReadJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return string.Empty;

        return ConvertJsonElementToString(value);
    }

    private static string ConvertJsonElementToString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };

    private static DateTime ParseInstagramTimestamp(JsonElement item)
    {
        if (!item.TryGetProperty("timestamp", out var timestamp))
            return BusinessNow();

        long unixValue;
        if (timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out var number))
            unixValue = number;
        else if (!long.TryParse(timestamp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out unixValue))
            return BusinessNow();

        try
        {
            var utc = unixValue > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixValue)
                : DateTimeOffset.FromUnixTimeSeconds(unixValue);
            return TimeZoneInfo.ConvertTime(utc, ResolveBusinessTimeZone()).DateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return BusinessNow();
        }
    }

    private static string ResolveInstagramMessageType(JsonElement message)
    {
        if (!message.TryGetProperty("attachments", out var attachments)
            || attachments.ValueKind != JsonValueKind.Array
            || attachments.GetArrayLength() == 0)
            return "TEXT";

        var first = attachments[0];
        var type = first.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString() ?? string.Empty
            : string.Empty;
        return type.Trim().ToLowerInvariant() switch
        {
            "image" or "share" or "story_mention" => "IMAGE",
            "video" or "reel" => "VIDEO",
            "audio" => "AUDIO",
            "file" => "DOCUMENT",
            _ => "UNKNOWN"
        };
    }

    private static string BuildInstagramAttachmentSummary(string messageType)
        => NormalizeMessageType(messageType) switch
        {
            "IMAGE" => "Imagen recibida en Instagram.",
            "VIDEO" => "Video recibido en Instagram.",
            "AUDIO" => "Audio recibido en Instagram.",
            "DOCUMENT" => "Archivo recibido en Instagram.",
            _ => "Mensaje recibido en Instagram."
        };

    private static string BuildFacebookAttachmentSummary(string messageType)
        => NormalizeMessageType(messageType) switch
        {
            "IMAGE" => "Imagen recibida en Facebook Messenger.",
            "VIDEO" => "Video recibido en Facebook Messenger.",
            "AUDIO" => "Audio recibido en Facebook Messenger.",
            "DOCUMENT" => "Archivo recibido en Facebook Messenger.",
            _ => "Mensaje recibido en Facebook Messenger."
        };

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

                var phoneNumberId = value.TryGetProperty("metadata", out var metadata)
                    && metadata.TryGetProperty("phone_number_id", out var phoneNumberIdProp)
                        ? phoneNumberIdProp.GetString() ?? string.Empty
                        : string.Empty;
                var displayPhoneNumber = value.TryGetProperty("metadata", out metadata)
                    && metadata.TryGetProperty("display_phone_number", out var displayPhoneNumberProp)
                        ? displayPhoneNumberProp.GetString() ?? string.Empty
                        : string.Empty;

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
                        PhoneNumberId = phoneNumberId,
                        DisplayPhoneNumber = displayPhoneNumber,
                        ContactName = contactName,
                        MessageType = NormalizeMessageType(type),
                        WhatsAppMessageId = messageId,
                        WhatsAppReplyToMessageId = replyToMessageId,
                        Timestamp = timestamp,
                        Text = text,
                        RawJson = BuildIncomingWhatsAppMessagePayloadJson(message, phoneNumberId, displayPhoneNumber),
                        Attachments = attachments
                    });
                }
            }
        }

        return items;
    }

    private static IReadOnlyList<string> ExtractWhatsAppPhoneNumberIds(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return [];
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)
                    || !value.TryGetProperty("metadata", out var metadata)
                    || !metadata.TryGetProperty("phone_number_id", out var phone)
                    || phone.ValueKind != JsonValueKind.String) continue;
                var normalized = (phone.GetString() ?? string.Empty).Trim();
                if (normalized.Length > 0) result.Add(normalized);
            }
        }
        return result.ToArray();
    }

    private static string BuildIncomingWhatsAppMessagePayloadJson(JsonElement message, string phoneNumberId, string displayPhoneNumber)
    {
        return JsonSerializer.Serialize(new
        {
            metadata = new
            {
                phone_number_id = phoneNumberId,
                display_phone_number = displayPhoneNumber
            },
            message
        });
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
            return "UbicaciÃ³n compartida.";

        var name = location.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var address = location.TryGetProperty("address", out var addressProp) ? addressProp.GetString() ?? string.Empty : string.Empty;
        var latitude = location.TryGetProperty("latitude", out var latProp) ? latProp.ToString() : string.Empty;
        var longitude = location.TryGetProperty("longitude", out var lonProp) ? lonProp.ToString() : string.Empty;
        var coordinates = string.IsNullOrWhiteSpace(latitude) || string.IsNullOrWhiteSpace(longitude)
            ? string.Empty
            : $"{latitude}, {longitude}";

        return FirstNonEmpty(
            JoinText("UbicaciÃ³n compartida", name),
            JoinText("UbicaciÃ³n compartida", address),
            JoinText("UbicaciÃ³n compartida", coordinates),
            "UbicaciÃ³n compartida.");
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
            return "BotÃ³n seleccionado.";

        var text = button.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
        var payload = button.TryGetProperty("payload", out var payloadProp) ? payloadProp.GetString() ?? string.Empty : string.Empty;
        return JoinText("BotÃ³n seleccionado", FirstNonEmpty(text, payload));
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
            return string.Empty;

        var emoji = reaction.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? string.Empty : string.Empty;
        return emoji.Trim();
    }

    private static string ExtractOrderText(JsonElement message)
    {
        if (!message.TryGetProperty("order", out var order))
            return "Pedido de catÃ¡logo recibido.";

        var catalog = order.TryGetProperty("catalog_id", out var catalogProp) ? catalogProp.GetString() ?? string.Empty : string.Empty;
        var count = 0;
        if (order.TryGetProperty("product_items", out var items) && items.ValueKind == JsonValueKind.Array)
            count = items.GetArrayLength();

        var detail = count > 0 ? $"{count} producto(s)" : catalog;
        return JoinText("Pedido de catÃ¡logo recibido", detail);
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
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            "application/zip" => ".zip",
            "application/x-rar-compressed" => ".rar",
            _ => NormalizeMessageType(tipoArchivo) == "STICKER" ? ".webp" : ".bin"
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string FirstValidMetaPhoneNumberId(params string?[] values)
        => values.FirstOrDefault(IsValidMetaPhoneNumberId)?.Trim() ?? string.Empty;

    private static bool IsValidMetaPhoneNumberId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Trim().StartsWith("WEBPENDING-", StringComparison.OrdinalIgnoreCase);

    private async Task BindConversationToMetaNumberAsync(long idConversacion, string? phoneNumberId, CancellationToken ct)
    {
        if (idConversacion <= 0 || !IsValidMetaPhoneNumberId(phoneNumberId))
            return;

        const string sql = """
            UPDATE c
            SET
                IdNumeroWhatsApp = n.IdNumero,
                FechaHora_Modificacion = GETDATE()
            FROM dbo.CONV_CONVERSACIONES c
            INNER JOIN dbo.CONV_WHATSAPP_NUMEROS n
                ON LTRIM(RTRIM(n.PhoneNumberId)) = @PhoneNumberId
            WHERE c.IdConversacion = @IdConversacion
              AND (c.IdNumeroWhatsApp IS NULL OR c.IdNumeroWhatsApp <> n.IdNumero);
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@PhoneNumberId", phoneNumberId!.Trim());
        await cmd.ExecuteNonQueryAsync(ct);
    }

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
            if (doc.RootElement.TryGetProperty("message_id", out var messageId))
                return messageId.GetString() ?? string.Empty;

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
        if (item.EsWhatsAppQr)
        {
            item.VentanaWhatsAppActiva = true;
            item.FechaHoraVencimientoVentanaWhatsApp = null;
            return;
        }

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
        if (item.EsWhatsAppQr)
        {
            item.VentanaWhatsAppActiva = true;
            item.FechaHoraVencimientoVentanaWhatsApp = null;
            return;
        }

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
            throw new InvalidOperationException("No se puede enviar a aprobaciÃ³n una plantilla inactiva.");
        if (string.Equals(template.EstadoMeta, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La plantilla ya estÃ¡ aprobada por Meta.");

        var variableCount = CountTemplateVariables(template.CuerpoTexto);
        var examples = ParseTemplateExamples(template.EjemplosVariablesJson);
        if (variableCount > 0 && examples.Count < variableCount)
            throw new InvalidOperationException("Las variables de la plantilla necesitan valores de ejemplo para enviarse a aprobaciÃ³n.");
        if (StartsOrEndsWithTemplateVariable(template.CuerpoTexto))
            throw new InvalidOperationException("Meta no permite variables al principio ni al final del cuerpo. AgregÃ¡ texto fijo antes y despuÃ©s de la variable.");
    }

    private static bool StartsOrEndsWithTemplateVariable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return Regex.IsMatch(trimmed, @"^\{\{\s*\d+\s*\}\}", RegexOptions.CultureInvariant)
               || Regex.IsMatch(trimmed, @"\{\{\s*\d+\s*\}\}$", RegexOptions.CultureInvariant);
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

    private static ConversacionPlantillaDto MapRemoteTemplate(MetaMessageTemplate template)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{template.Id}|{template.Name}|{template.Language}"));
        var id = (long)(BitConverter.ToUInt64(hash, 0) & 0x7FFFFFFFFFFFFFFF);
        if (id == 0) id = 1;
        return new ConversacionPlantillaDto
        {
            IdPlantilla = id, NombreVisible = template.Name, NombreMeta = template.Name,
            Categoria = template.Category, Idioma = template.Language, EncabezadoTexto = template.HeaderText,
            CuerpoTexto = template.BodyText, PieTexto = template.FooterText, EstadoLocal = ConversacionPlantillaEstadosLocales.Sincronizada,
            EstadoMeta = template.Status, MetaTemplateId = template.Id, Activa = true, EsMetaRemota = true
        };
    }

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
            throw new InvalidOperationException("La conversaciÃ³n es obligatoria.");
        if (string.IsNullOrWhiteSpace(request.Texto))
            throw new InvalidOperationException("El texto del mensaje es obligatorio.");
    }

    private static string NormalizeReactionEmoji(string? emoji)
    {
        var value = (emoji ?? string.Empty).Trim();
        if (value.Length is 0 or > 32)
            return string.Empty;

        var elements = StringInfo.GetTextElementEnumerator(value);
        return elements.MoveNext() && !elements.MoveNext()
            ? value
            : string.Empty;
    }

    private static string BuildReactionSummary(string? emoji, bool incoming)
    {
        var direction = incoming ? "recibida" : "enviada";
        var value = emoji?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return "ReacciÃ³n eliminada.";

        return $"ReacciÃ³n {direction}: {value}";
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

    /// <summary>
    /// "recientes" (de siempre): fijadas primero, despuÃ©s la actividad mÃ¡s nueva. "cola_espera":
    /// las conversaciones donde el Ãºltimo mensaje es del cliente (esperando respuesta) van primero,
    /// agrupadas por prioridad (Clasifica1/2/3 configurados, el resto al final) y dentro de cada
    /// grupo por cuÃ¡nto tiempo llevan esperando (mÃ¡s viejas primero); lo que ya se respondiÃ³ queda
    /// despuÃ©s, ordenado como siempre.
    /// </summary>
    private static string BuildInboxOrderByClause(string? orden)
    {
        if (!string.Equals(orden, ConversacionesInboxOrden.ColaEspera, StringComparison.OrdinalIgnoreCase))
        {
            return """
                ORDER BY
                    CASE WHEN @Search IS NULL OR @ClienteCodigo IS NOT NULL THEN 0 ELSE searchRank.SearchRank END ASC,
                    CASE WHEN pin.IdConversacion IS NULL THEN 0 ELSE 1 END DESC,
                    pin.FechaHora_Grabacion DESC,
                    ISNULL(c.FechaHoraUltimoMensaje, ultMsg.FechaHoraVisible) DESC,
                    c.IdConversacion DESC
                """;
        }

        return """
            ORDER BY
                CASE WHEN pin.IdConversacion IS NULL THEN 0 ELSE 1 END DESC,
                pin.FechaHora_Grabacion DESC,
                CASE WHEN ISNULL(ultMsg.Direction, N'') = N'ENTRANTE' THEN 0 ELSE 1 END ASC,
                CASE
                    WHEN ISNULL(ultMsg.Direction, N'') <> N'ENTRANTE' THEN 99
                    WHEN @Clasifica1 IS NOT NULL AND UPPER(LTRIM(RTRIM(clienteClasificacion.Codigo))) = UPPER(LTRIM(RTRIM(@Clasifica1))) THEN 1
                    WHEN @Clasifica2 IS NOT NULL AND UPPER(LTRIM(RTRIM(clienteClasificacion.Codigo))) = UPPER(LTRIM(RTRIM(@Clasifica2))) THEN 2
                    WHEN @Clasifica3 IS NOT NULL AND UPPER(LTRIM(RTRIM(clienteClasificacion.Codigo))) = UPPER(LTRIM(RTRIM(@Clasifica3))) THEN 3
                    ELSE 4
                END ASC,
                ISNULL(c.FechaHoraUltimoMensaje, ultMsg.FechaHoraVisible) ASC,
                c.IdConversacion ASC
            """;
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

    private bool ShouldRunMaintenance(string operation, TimeSpan interval)
    {
        var key = $"{ConnectionString.GetHashCode(StringComparison.Ordinal)}:{operation}";
        var now = DateTime.UtcNow;

        if (MaintenanceLastRunUtc.TryGetValue(key, out var lastRun) && now - lastRun < interval)
            return false;

        MaintenanceLastRunUtc[key] = now;
        return true;
    }

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
            "BUTTON" or "INTERACTIVE" => "TEXT",
            "REACTION" => "REACTION",
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

    private static string? LikePrefix(string? value)
    {
        var normalized = SearchTextHelper.Normalize(value);
        return normalized.Length == 0 ? null : $"{normalized}%";
    }

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

    // TA_CLASIFICACIONES.Color guarda un long de VB6 (&H00BBGGRR, canales invertidos respecto a web).
    private static string VbColorIntToHex(int value)
    {
        if (value == 0)
            return string.Empty;
        var r = value & 0xFF;
        var g = (value >> 8) & 0xFF;
        var b = (value >> 16) & 0xFF;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

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
        message = $"El mÃ³dulo Conversaciones todavÃ­a no estÃ¡ inicializado en la base activa. Falta crear el objeto {objectLabel}. EjecutÃ¡ el script docs/conversaciones_modelo_inicial.sql y recargÃ¡ el mÃ³dulo.";
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    private async Task ExecuteLoggedAsync(
        string module,
        string action,
        Func<CancellationToken, Task> operation,
        string userMessage,
        CancellationToken ct)
    {
        await ExecuteLoggedAsync(
            module,
            action,
            async token =>
            {
                await operation(token);
                return true;
            },
            userMessage,
            ct);
    }

    private async Task TryLogConversationSchemaCheckFailureAsync(Exception exception, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        try
        {
            await _appEvents.LogErrorAsync(
                "Conversaciones",
                "HasConversationSchema",
                exception,
                "No se pudo verificar el esquema de conversaciones.",
                null,
                AppEventSeverity.Error,
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private sealed class PendingMessageInsert
    {
        public long ConversationId { get; init; }
        public string Phone { get; init; } = string.Empty;
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string? ReplyToMessageId { get; init; }
        public string MessageIdExterno { get; init; } = string.Empty;
        public string ReplyToMessageIdExterno { get; init; } = string.Empty;
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
        public int? IdNumeroWhatsApp { get; init; }
        public string TelefonoWhatsApp { get; init; } = string.Empty;
        public string Canal { get; init; } = string.Empty;
        public string IdentificadorExternoContacto { get; init; } = string.Empty;
        public string IdentificadorExternoConversacion { get; init; } = string.Empty;

        /// <summary>
        /// Phone Number ID de dbo.CONV_WHATSAPP_NUMEROS fijado para esta conversaciÃ³n (vacÃ­o si la
        /// conversaciÃ³n no tiene un nÃºmero propio asignado -- cliente sin migrar a multi-nÃºmero, o
        /// canal que no es WhatsApp). Cuando estÃ¡ vacÃ­o, el envÃ­o usa el Ãºnico nÃºmero configurado
        /// en TA_CONFIGURACION, igual que antes de esta funcionalidad.
        /// </summary>
        public string PhoneNumberId { get; init; } = string.Empty;
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
        public int? IdNumeroWhatsApp { get; init; }
    }

    private sealed class IncomingWhatsAppMessage
    {
        public string Phone { get; init; } = string.Empty;
        public string PhoneNumberId { get; init; } = string.Empty;
        public string DisplayPhoneNumber { get; init; } = string.Empty;
        public string ContactName { get; init; } = string.Empty;
        public string MessageType { get; init; } = "TEXT";
        public string WhatsAppMessageId { get; init; } = string.Empty;
        public string WhatsAppReplyToMessageId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
        public List<IncomingWhatsAppAttachment> Attachments { get; init; } = [];
    }

    private sealed class IncomingInstagramMessage
    {
        public string AccountId { get; init; } = string.Empty;
        public string SenderId { get; init; } = string.Empty;
        public string RecipientId { get; init; } = string.Empty;
        public string MessageId { get; init; } = string.Empty;
        public string ReplyToMessageId { get; init; } = string.Empty;
        public string MessageType { get; init; } = "TEXT";
        public DateTime Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
        public bool IsEcho { get; init; }
        public string RawJson { get; init; } = string.Empty;
    }

    private sealed class MercadoLibreNotification
    {
        public string Id { get; init; } = string.Empty;
        public string Topic { get; init; } = string.Empty;
        public string Resource { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public string QuestionId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string RawJson { get; init; } = string.Empty;
    }

    private sealed class MercadoLibreQuestion
    {
        public string QuestionId { get; set; } = string.Empty;
        public string BuyerId { get; set; } = string.Empty;
        public string SellerId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemTitle { get; set; } = string.Empty;
        public string ItemPermalink { get; set; } = string.Empty;
        public string ItemPictureUrl { get; set; } = string.Empty;
        public string ItemStatus { get; set; } = string.Empty;
        public string BuyerNickname { get; set; } = string.Empty;
        public string BuyerFirstName { get; set; } = string.Empty;
        public string BuyerLastName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string RawJson { get; set; } = string.Empty;
        public bool HasItemMetadata => !string.IsNullOrWhiteSpace(ItemTitle) || !string.IsNullOrWhiteSpace(ItemPictureUrl);
        public bool IsAnswered => string.Equals(Status, "ANSWERED", StringComparison.OrdinalIgnoreCase);
        public string DisplayName => FirstNonEmpty(ItemTitle, string.IsNullOrWhiteSpace(BuyerId) ? string.Empty : $"Mercado Libre {BuyerId}", "Mercado Libre");
        public string BuyerDisplayName
        {
            get
            {
                var fullName = $"{BuyerFirstName} {BuyerLastName}".Trim();
                return FirstNonEmpty(BuyerNickname, fullName, string.IsNullOrWhiteSpace(BuyerId) ? string.Empty : $"Comprador {BuyerId}");
            }
        }

        public static MercadoLibreQuestion FromNotification(MercadoLibreNotification notification)
            => new()
            {
                QuestionId = notification.QuestionId,
                BuyerId = notification.UserId,
                BuyerNickname = string.IsNullOrWhiteSpace(notification.UserId) ? string.Empty : $"Comprador {notification.UserId}",
                Text = "Nueva pregunta recibida en Mercado Libre.",
                Timestamp = notification.Timestamp == default ? BusinessNow() : notification.Timestamp,
                RawJson = notification.RawJson
            };

        public IncomingInstagramMessage ToIncomingMessage()
            => new()
            {
                AccountId = SellerId,
                SenderId = BuyerId,
                RecipientId = SellerId,
                MessageId = $"meli-question-{QuestionId}",
                MessageType = "TEXT",
                Timestamp = Timestamp == default ? BusinessNow() : Timestamp,
                Text = Text,
                RawJson = BuildPayloadJson()
            };

        private string BuildPayloadJson()
            => JsonSerializer.Serialize(new
            {
                provider = "mercadolibre",
                question_id = QuestionId,
                question_status = Status,
                seller_id = SellerId,
                buyer_id = BuyerId,
                buyer_nickname = BuyerNickname,
                item_id = ItemId,
                item_title = ItemTitle,
                item_status = ItemStatus,
                item_permalink = ItemPermalink,
                item_picture_url = ItemPictureUrl,
                text = Text,
                date_created = Timestamp == default ? string.Empty : Timestamp.ToString("O", CultureInfo.InvariantCulture),
                raw = RawJson
            });
    }

    private sealed class MercadoLibreItem
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Permalink { get; init; } = string.Empty;
        public string PictureUrl { get; init; } = string.Empty;
    }

    private sealed class MercadoLibreBuyer
    {
        public string Id { get; init; } = string.Empty;
        public string Nickname { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
    }

    private sealed class InstagramProfile
    {
        public static InstagramProfile Empty { get; } = new();

        public string Name { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string ProfilePictureUrl { get; init; } = string.Empty;
        public int? FollowerCount { get; init; }
        public bool? IsVerified { get; init; }
        public bool? IsUserFollowingBusiness { get; init; }
        public bool? IsBusinessFollowingUser { get; init; }
        public bool HasData => !string.IsNullOrWhiteSpace(Name)
                               || !string.IsNullOrWhiteSpace(Username)
                               || !string.IsNullOrWhiteSpace(ProfilePictureUrl)
                               || FollowerCount.HasValue
                               || IsVerified.HasValue
                               || IsUserFollowingBusiness.HasValue
                               || IsBusinessFollowingUser.HasValue;
        public string DisplayName => FirstNonEmpty(Name, string.IsNullOrWhiteSpace(Username) ? string.Empty : $"@{Username}");
    }

    private readonly record struct FacebookConversationPageContext(string RecipientId, string AccountId)
    {
        public static FacebookConversationPageContext Empty { get; } = new(string.Empty, string.Empty);
    }

    private sealed class FacebookProfile
    {
        public static FacebookProfile Empty { get; } = new();

        public string Name { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string ProfilePictureUrl { get; init; } = string.Empty;
        public bool HasData => !string.IsNullOrWhiteSpace(Name)
                               || !string.IsNullOrWhiteSpace(FirstName)
                               || !string.IsNullOrWhiteSpace(LastName)
                               || !string.IsNullOrWhiteSpace(ProfilePictureUrl);
        public string DisplayName => FirstNonEmpty(Name, $"{FirstName} {LastName}".Trim(), FirstName, LastName);
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
        public DateTime FechaHora { get; init; }
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


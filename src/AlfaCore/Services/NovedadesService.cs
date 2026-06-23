using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class NovedadesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IWebHostEnvironment environment) : INovedadesService
{
    private const string ModuleName = "Novedades";
    private static readonly HashSet<string> AllowedCoverContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuro la cadena de conexion 'ConnectionStrings:AlfaGestion'.");

    public Task<NovedadesPageDto> GetPageAsync(NovedadesFilterDto filter, long? idNovedad, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPage", async token =>
        {
            filter ??= new NovedadesFilterDto();
            var user = NormalizeUser(usuario);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);

            var items = (await cn.QueryAsync<NovedadResumenDto>(new CommandDefinition(
                BuildListSql(filter),
                new
                {
                    Estado = NormalizeNullable(filter.Estado),
                    Tipo = NormalizeNullable(filter.Tipo),
                    Busqueda = NormalizeNullable(filter.Busqueda)
                },
                cancellationToken: token))).ToList();

            var selectedId = idNovedad.GetValueOrDefault();
            if (selectedId <= 0)
                selectedId = items.FirstOrDefault()?.IdNovedad ?? 0;

            var selected = selectedId > 0 ? await GetCoreAsync(cn, selectedId, user, token) : null;
            var metrics = await GetMetricsAsync(cn, user, token);

            return new NovedadesPageDto
            {
                Novedades = items,
                Seleccionada = selected,
                Metricas = metrics
            };
        }, "No se pudo cargar el modulo de novedades.", ct);

    public Task<NovedadDetalleDto?> GetAsync(long idNovedad, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Get", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            return await GetCoreAsync(cn, idNovedad, NormalizeUser(usuario), token);
        }, "No se pudo cargar la novedad.", ct);

    public Task<long> CreateDraftAsync(string tipo, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateDraft", async token =>
        {
            var normalizedType = NormalizeTipo(tipo);
            var user = NormalizeUser(usuarioAccion);
            var template = BuildTemplate(normalizedType);

            const string sql = """
                INSERT INTO dbo.ALFACORE_NOVEDADES
                (
                    Tipo, Estado, Prioridad, Version, Titulo, Bajada, Resumen, Contenido,
                    Icono, Acento, PortadaTipo, PortadaValor, Footer, FechaProgramada,
                    VigenteHasta, MostrarPopup, LecturaObligatoria, RepetirHastaLeer,
                    Segmento, UsuarioAlta, FechaHora_Alta, UsuarioModificacion,
                    FechaHora_Modificacion, Archivado
                )
                VALUES
                (
                    @Tipo, N'BORRADOR', @Prioridad, @Version, @Titulo, @Bajada, @Resumen, @Contenido,
                    @Icono, @Acento, @PortadaTipo, @PortadaValor, @Footer, NULL,
                    NULL, 1, @LecturaObligatoria, 1,
                    N'TODOS', @Usuario, GETDATE(), @Usuario, GETDATE(), 0
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
            {
                Tipo = normalizedType,
                template.Prioridad,
                template.Version,
                template.Titulo,
                template.Bajada,
                Resumen = BuildSummary(template.Contenido),
                template.Contenido,
                template.Icono,
                template.Acento,
                template.PortadaTipo,
                template.PortadaValor,
                template.Footer,
                template.LecturaObligatoria,
                Usuario = user
            }, cancellationToken: token));
        }, "No se pudo crear el borrador de novedad.", ct);

    public Task<long> SaveAsync(NovedadSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Save", async token =>
        {
            request ??= new NovedadSaveRequest();
            if (request.IdNovedad <= 0)
                return await CreateDraftAsync(request.Tipo, request.UsuarioAccion, token);

            var user = NormalizeUser(request.UsuarioAccion);
            var estado = NormalizeEstado(request.Estado);
            var fechaPublicacionSql = estado == NovedadEstados.Publicado
                ? "FechaPublicacion = COALESCE(FechaPublicacion, GETDATE()),"
                : "FechaPublicacion = CASE WHEN @Estado IN (N'BORRADOR', N'PROGRAMADO') THEN NULL ELSE FechaPublicacion END,";

            var sql = $"""
                UPDATE dbo.ALFACORE_NOVEDADES
                   SET Tipo = @Tipo,
                       Estado = @Estado,
                       Prioridad = @Prioridad,
                       Version = @Version,
                       Titulo = @Titulo,
                       Bajada = @Bajada,
                       Resumen = @Resumen,
                       Contenido = @Contenido,
                       Icono = @Icono,
                       Acento = @Acento,
                       PortadaTipo = @PortadaTipo,
                       PortadaValor = @PortadaValor,
                       Footer = @Footer,
                       FechaProgramada = @FechaProgramada,
                       {fechaPublicacionSql}
                       VigenteHasta = @VigenteHasta,
                       MostrarPopup = @MostrarPopup,
                       LecturaObligatoria = @LecturaObligatoria,
                       RepetirHastaLeer = @RepetirHastaLeer,
                       Segmento = @Segmento,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE(),
                       Archivado = CASE WHEN @Estado = N'ARCHIVADO' THEN 1 ELSE 0 END
                 WHERE IdNovedad = @IdNovedad;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                request.IdNovedad,
                Tipo = NormalizeTipo(request.Tipo),
                Estado = estado,
                Prioridad = NormalizePrioridad(request.Prioridad),
                Version = Truncate(request.Version, 80),
                Titulo = FirstNonEmpty(Truncate(request.Titulo, 180), "Sin titulo"),
                Bajada = Truncate(request.Bajada, 300),
                Resumen = BuildSummary(request.Contenido),
                Contenido = request.Contenido?.Trim() ?? string.Empty,
                Icono = FirstNonEmpty(Truncate(request.Icono, 60), "bi-megaphone"),
                Acento = NormalizeAcento(request.Acento),
                PortadaTipo = NormalizePortadaTipo(request.PortadaTipo),
                PortadaValor = Truncate(request.PortadaValor, 500),
                Footer = Truncate(request.Footer, 300),
                request.FechaProgramada,
                request.VigenteHasta,
                request.MostrarPopup,
                request.LecturaObligatoria,
                request.RepetirHastaLeer,
                Segmento = NovedadSegmentos.Todos,
                Usuario = user
            }, cancellationToken: token));

            await appEvents.LogAuditAsync(ModuleName, "Save", "ALFACORE_NOVEDADES", request.IdNovedad.ToString(), "Novedad guardada.", new { Estado = estado }, token);
            return request.IdNovedad;
        }, "No se pudo guardar la novedad.", ct);

    public Task ArchiveAsync(long idNovedad, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Archive", async token =>
        {
            const string sql = """
                UPDATE dbo.ALFACORE_NOVEDADES
                   SET Estado = N'ARCHIVADO',
                       Archivado = 1,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdNovedad = @IdNovedad;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdNovedad = idNovedad, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo archivar la novedad.", ct);

    public Task DuplicateAsync(long idNovedad, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Duplicate", async token =>
        {
            const string sql = """
                INSERT INTO dbo.ALFACORE_NOVEDADES
                (
                    Tipo, Estado, Prioridad, Version, Titulo, Bajada, Resumen, Contenido,
                    Icono, Acento, PortadaTipo, PortadaValor, Footer, FechaProgramada,
                    VigenteHasta, MostrarPopup, LecturaObligatoria, RepetirHastaLeer,
                    Segmento, UsuarioAlta, FechaHora_Alta, UsuarioModificacion,
                    FechaHora_Modificacion, Archivado
                )
                SELECT
                    Tipo, N'BORRADOR', Prioridad, Version, Titulo + N' (copia)', Bajada, Resumen, Contenido,
                    Icono, Acento, PortadaTipo, PortadaValor, Footer, NULL,
                    NULL, MostrarPopup, LecturaObligatoria, RepetirHastaLeer,
                    Segmento, @Usuario, GETDATE(), @Usuario, GETDATE(), 0
                FROM dbo.ALFACORE_NOVEDADES
                WHERE IdNovedad = @IdNovedad;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdNovedad = idNovedad, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo duplicar la novedad.", ct);

    public Task<NovedadDetalleDto?> GetPendingAnnouncementAsync(string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPendingAnnouncement", async token =>
        {
            var user = NormalizeUser(usuario);

            const string sql = """
                SELECT TOP (1)
                    n.IdNovedad
                FROM dbo.ALFACORE_NOVEDADES n
                LEFT JOIN dbo.ALFACORE_NOVEDADES_LECTURAS l
                       ON l.IdNovedad = n.IdNovedad
                      AND UPPER(LTRIM(RTRIM(l.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                WHERE n.Estado = N'PUBLICADO'
                  AND ISNULL(n.Archivado, 0) = 0
                  AND ISNULL(n.MostrarPopup, 0) = 1
                  AND (n.FechaProgramada IS NULL OR n.FechaProgramada <= GETDATE())
                  AND (n.VigenteHasta IS NULL OR n.VigenteHasta >= GETDATE())
                  AND (
                        l.IdNovedad IS NULL
                        OR l.FechaHoraLeido IS NULL AND ISNULL(n.RepetirHastaLeer, 1) = 1
                      )
                ORDER BY
                    CASE n.Prioridad WHEN N'CRITICO' THEN 0 WHEN N'IMPORTANTE' THEN 1 ELSE 2 END,
                    ISNULL(n.FechaPublicacion, n.FechaProgramada) DESC,
                    n.IdNovedad DESC;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            var id = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { Usuario = user }, cancellationToken: token));
            return id.HasValue ? await GetCoreAsync(cn, id.Value, user, token) : null;
        }, "No se pudo consultar novedades pendientes.", ct);

    public Task RegisterViewAsync(long idNovedad, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RegisterView", async token =>
        {
            const string sql = """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.ALFACORE_NOVEDADES_LECTURAS
                    WHERE IdNovedad = @IdNovedad
                      AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                )
                BEGIN
                    UPDATE dbo.ALFACORE_NOVEDADES_LECTURAS
                       SET FechaHoraUltimaVista = GETDATE(),
                           VecesVisto = ISNULL(VecesVisto, 0) + 1
                     WHERE IdNovedad = @IdNovedad
                       AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)));
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.ALFACORE_NOVEDADES_LECTURAS
                        (IdNovedad, Usuario, FechaHoraPrimeraVista, FechaHoraUltimaVista, VecesVisto)
                    VALUES
                        (@IdNovedad, @Usuario, GETDATE(), GETDATE(), 1);
                END;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdNovedad = idNovedad, Usuario = NormalizeUser(usuario) }, cancellationToken: token));
        }, "No se pudo registrar la vista de la novedad.", ct);

    public Task MarkReadAsync(long idNovedad, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "MarkRead", async token =>
        {
            const string sql = """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.ALFACORE_NOVEDADES_LECTURAS
                    WHERE IdNovedad = @IdNovedad
                      AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
                )
                BEGIN
                    UPDATE dbo.ALFACORE_NOVEDADES_LECTURAS
                       SET FechaHoraUltimaVista = GETDATE(),
                           FechaHoraLeido = COALESCE(FechaHoraLeido, GETDATE()),
                           VecesVisto = CASE WHEN ISNULL(VecesVisto, 0) = 0 THEN 1 ELSE VecesVisto END
                     WHERE IdNovedad = @IdNovedad
                       AND UPPER(LTRIM(RTRIM(Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)));
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.ALFACORE_NOVEDADES_LECTURAS
                        (IdNovedad, Usuario, FechaHoraPrimeraVista, FechaHoraUltimaVista, FechaHoraLeido, VecesVisto)
                    VALUES
                        (@IdNovedad, @Usuario, GETDATE(), GETDATE(), GETDATE(), 1);
                END;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await EnsureSchemaAsync(cn, token);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdNovedad = idNovedad, Usuario = NormalizeUser(usuario) }, cancellationToken: token));
        }, "No se pudo marcar la novedad como leida.", ct);

    public Task<string> SaveCoverImageAsync(Stream content, string fileName, string contentType, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveCoverImage", async token =>
        {
            if (content is null || !content.CanRead)
                throw new InvalidOperationException("No se recibio una imagen valida.");

            if (!AllowedCoverContentTypes.Contains(contentType))
                throw new InvalidOperationException("La imagen debe ser JPG, PNG, WebP o GIF.");

            var extension = ResolveCoverExtension(contentType, fileName);
            var safeUser = Regex.Replace(NormalizeUser(usuarioAccion), @"[^a-zA-Z0-9_-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(safeUser))
                safeUser = "usuario";

            var fileBase = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeUser}-{Guid.NewGuid():N}";
            if (fileBase.Length > 90)
                fileBase = fileBase[..90];

            var relativePath = $"/uploads/novedades/{fileBase}{extension}";
            var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;
            var directory = Path.Combine(webRoot, "uploads", "novedades");
            Directory.CreateDirectory(directory);

            await using var output = File.Create(Path.Combine(directory, $"{fileBase}{extension}"));
            await content.CopyToAsync(output, token);
            return relativePath;
        }, "No se pudo guardar la imagen de novedades.", ct);

    private static string BuildListSql(NovedadesFilterDto filter)
    {
        var filters = new List<string> { "ISNULL(n.Archivado, 0) = 0" };

        if (!string.IsNullOrWhiteSpace(filter.Estado))
            filters.Add("n.Estado = @Estado");
        if (!string.IsNullOrWhiteSpace(filter.Tipo))
            filters.Add("n.Tipo = @Tipo");
        if (!string.IsNullOrWhiteSpace(filter.Busqueda))
            filters.Add("(n.Titulo LIKE '%' + @Busqueda + '%' OR n.Version LIKE '%' + @Busqueda + '%' OR n.Resumen LIKE '%' + @Busqueda + '%')");

        return $"""
            SELECT
                n.IdNovedad,
                n.Tipo,
                n.Estado,
                n.Prioridad,
                ISNULL(n.Version, '') AS Version,
                n.Titulo,
                ISNULL(n.Bajada, '') AS Bajada,
                ISNULL(n.Resumen, '') AS Resumen,
                ISNULL(n.Icono, 'bi-megaphone') AS Icono,
                ISNULL(n.Acento, 'azul') AS Acento,
                n.FechaProgramada,
                n.FechaPublicacion,
                n.VigenteHasta,
                ISNULL(n.MostrarPopup, 0) AS MostrarPopup,
                ISNULL(n.LecturaObligatoria, 0) AS LecturaObligatoria,
                ISNULL(n.RepetirHastaLeer, 1) AS RepetirHastaLeer,
                COUNT(l.Usuario) AS Vistas,
                SUM(CASE WHEN l.FechaHoraLeido IS NULL THEN 0 ELSE 1 END) AS Lecturas,
                ISNULL(n.FechaHora_Modificacion, n.FechaHora_Alta) AS FechaHoraModificacion
            FROM dbo.ALFACORE_NOVEDADES n
            LEFT JOIN dbo.ALFACORE_NOVEDADES_LECTURAS l ON l.IdNovedad = n.IdNovedad
            WHERE {string.Join(" AND ", filters)}
            GROUP BY
                n.IdNovedad, n.Tipo, n.Estado, n.Prioridad, n.Version, n.Titulo, n.Bajada,
                n.Resumen, n.Icono, n.Acento, n.FechaProgramada, n.FechaPublicacion,
                n.VigenteHasta, n.MostrarPopup, n.LecturaObligatoria, n.RepetirHastaLeer,
                n.FechaHora_Modificacion, n.FechaHora_Alta
            ORDER BY
                CASE n.Estado WHEN N'PROGRAMADO' THEN 0 WHEN N'BORRADOR' THEN 1 WHEN N'PUBLICADO' THEN 2 ELSE 3 END,
                COALESCE(n.FechaProgramada, n.FechaPublicacion, n.FechaHora_Modificacion, n.FechaHora_Alta) DESC,
                n.IdNovedad DESC;
            """;
    }

    private static async Task EnsureSchemaAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_NOVEDADES
                (
                    IdNovedad bigint IDENTITY(1,1) NOT NULL,
                    Tipo nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Tipo DEFAULT (N'MANUAL'),
                    Estado nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Estado DEFAULT (N'BORRADOR'),
                    Prioridad nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Prioridad DEFAULT (N'INFO'),
                    Version nvarchar(80) NULL,
                    Titulo nvarchar(180) NOT NULL,
                    Bajada nvarchar(300) NULL,
                    Resumen nvarchar(500) NULL,
                    Contenido nvarchar(max) NULL,
                    Icono nvarchar(60) NULL,
                    Acento nvarchar(20) NULL,
                    PortadaTipo nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_PortadaTipo DEFAULT (N'NONE'),
                    PortadaValor nvarchar(500) NULL,
                    Footer nvarchar(300) NULL,
                    FechaProgramada datetime NULL,
                    FechaPublicacion datetime NULL,
                    VigenteHasta datetime NULL,
                    MostrarPopup bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_MostrarPopup DEFAULT (1),
                    LecturaObligatoria bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_LecturaObligatoria DEFAULT (0),
                    RepetirHastaLeer bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Repetir DEFAULT (1),
                    Segmento nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Segmento DEFAULT (N'TODOS'),
                    UsuarioAlta nvarchar(50) NULL,
                    FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_Alta DEFAULT (GETDATE()),
                    UsuarioModificacion nvarchar(50) NULL,
                    FechaHora_Modificacion datetime NULL,
                    Archivado bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Archivado DEFAULT (0),
                    CONSTRAINT PK_ALFACORE_NOVEDADES PRIMARY KEY CLUSTERED (IdNovedad),
                    CONSTRAINT CK_ALFACORE_NOV_Tipo CHECK (Tipo IN (N'MANUAL', N'BOLETIN_PATCH')),
                    CONSTRAINT CK_ALFACORE_NOV_Estado CHECK (Estado IN (N'BORRADOR', N'PROGRAMADO', N'PUBLICADO', N'ARCHIVADO')),
                    CONSTRAINT CK_ALFACORE_NOV_Prioridad CHECK (Prioridad IN (N'INFO', N'IMPORTANTE', N'CRITICO')),
                    CONSTRAINT CK_ALFACORE_NOV_PortadaTipo CHECK (PortadaTipo IN (N'NONE', N'GRADIENT', N'URL'))
                );
            END;

            IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ALFACORE_NOVEDADES_LECTURAS
                (
                    IdNovedad bigint NOT NULL,
                    Usuario nvarchar(50) NOT NULL,
                    FechaHoraPrimeraVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Primera DEFAULT (GETDATE()),
                    FechaHoraUltimaVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Ultima DEFAULT (GETDATE()),
                    FechaHoraLeido datetime NULL,
                    VecesVisto int NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Veces DEFAULT (0),
                    CONSTRAINT PK_ALFACORE_NOVEDADES_LECTURAS PRIMARY KEY CLUSTERED (IdNovedad, Usuario),
                    CONSTRAINT FK_ALFACORE_NOV_LEC_NOVEDAD FOREIGN KEY (IdNovedad)
                        REFERENCES dbo.ALFACORE_NOVEDADES (IdNovedad)
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_NOV_ESTADO_FECHA' AND object_id = OBJECT_ID(N'dbo.ALFACORE_NOVEDADES'))
                CREATE INDEX IX_ALFACORE_NOV_ESTADO_FECHA ON dbo.ALFACORE_NOVEDADES (Estado, Archivado, MostrarPopup, FechaProgramada, VigenteHasta) INCLUDE (Prioridad, Titulo, Version);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_NOV_LECT_USUARIO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS'))
                CREATE INDEX IX_ALFACORE_NOV_LECT_USUARIO ON dbo.ALFACORE_NOVEDADES_LECTURAS (Usuario, FechaHoraLeido) INCLUDE (IdNovedad, VecesVisto);
            """;

        await cn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private static async Task<NovedadDetalleDto?> GetCoreAsync(SqlConnection cn, long idNovedad, string usuario, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                n.IdNovedad,
                n.Tipo,
                n.Estado,
                n.Prioridad,
                ISNULL(n.Version, '') AS Version,
                n.Titulo,
                ISNULL(n.Bajada, '') AS Bajada,
                ISNULL(n.Resumen, '') AS Resumen,
                ISNULL(n.Contenido, '') AS Contenido,
                ISNULL(n.Icono, 'bi-megaphone') AS Icono,
                ISNULL(n.Acento, 'azul') AS Acento,
                ISNULL(n.PortadaTipo, 'NONE') AS PortadaTipo,
                ISNULL(n.PortadaValor, '') AS PortadaValor,
                ISNULL(n.Footer, '') AS Footer,
                n.FechaProgramada,
                n.FechaPublicacion,
                n.VigenteHasta,
                ISNULL(n.MostrarPopup, 0) AS MostrarPopup,
                ISNULL(n.LecturaObligatoria, 0) AS LecturaObligatoria,
                ISNULL(n.RepetirHastaLeer, 1) AS RepetirHastaLeer,
                ISNULL(n.Segmento, 'TODOS') AS Segmento,
                ISNULL(n.UsuarioAlta, '') AS UsuarioAlta,
                ISNULL(n.UsuarioModificacion, '') AS UsuarioModificacion,
                ISNULL(n.FechaHora_Modificacion, n.FechaHora_Alta) AS FechaHoraModificacion,
                CONVERT(bit, CASE WHEN l.FechaHoraLeido IS NULL THEN 0 ELSE 1 END) AS LeidaPorUsuario,
                l.FechaHoraLeido
            FROM dbo.ALFACORE_NOVEDADES n
            LEFT JOIN dbo.ALFACORE_NOVEDADES_LECTURAS l
                   ON l.IdNovedad = n.IdNovedad
                  AND UPPER(LTRIM(RTRIM(l.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            WHERE n.IdNovedad = @IdNovedad;
            """;

        return await cn.QueryFirstOrDefaultAsync<NovedadDetalleDto>(new CommandDefinition(sql, new { IdNovedad = idNovedad, Usuario = usuario }, cancellationToken: ct));
    }

    private static async Task<NovedadesMetricasDto> GetMetricsAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        const string sql = """
            SELECT
                SUM(CASE WHEN Estado = N'BORRADOR' THEN 1 ELSE 0 END) AS Borradores,
                SUM(CASE WHEN Estado = N'PROGRAMADO' THEN 1 ELSE 0 END) AS Programadas,
                SUM(CASE WHEN Estado = N'PUBLICADO' THEN 1 ELSE 0 END) AS Publicadas
            FROM dbo.ALFACORE_NOVEDADES
            WHERE ISNULL(Archivado, 0) = 0;

            SELECT COUNT(1)
            FROM dbo.ALFACORE_NOVEDADES n
            LEFT JOIN dbo.ALFACORE_NOVEDADES_LECTURAS l
                   ON l.IdNovedad = n.IdNovedad
                  AND UPPER(LTRIM(RTRIM(l.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            WHERE n.Estado = N'PUBLICADO'
              AND ISNULL(n.Archivado, 0) = 0
              AND ISNULL(n.MostrarPopup, 0) = 1
              AND (n.FechaProgramada IS NULL OR n.FechaProgramada <= GETDATE())
              AND (n.VigenteHasta IS NULL OR n.VigenteHasta >= GETDATE())
              AND l.FechaHoraLeido IS NULL;
            """;

        using var grid = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { Usuario = usuario }, cancellationToken: ct));
        var metrics = await grid.ReadFirstOrDefaultAsync<NovedadesMetricasDto>() ?? new NovedadesMetricasDto();
        metrics.LecturasPendientes = await grid.ReadFirstOrDefaultAsync<int>();
        return metrics;
    }

    private static NovedadSaveRequest BuildTemplate(string tipo)
    {
        if (tipo == NovedadTipos.BoletinPatch)
        {
            return new NovedadSaveRequest
            {
                Tipo = NovedadTipos.BoletinPatch,
                Prioridad = NovedadPrioridades.Importante,
                Version = "AlfaCore Patch 0.1",
                Titulo = "Actualizacion AlfaCore Patch 0.1",
                Bajada = "Resumen interno de cambios, nuevos modulos y mejoras listas para comunicar al equipo.",
                Icono = "bi-stars",
                Acento = NovedadAcentos.Cian,
                PortadaTipo = NovedadPortadas.Gradiente,
                PortadaValor = "cian",
                LecturaObligatoria = true,
                Footer = "Equipo AlfaCore",
                Contenido = """
                    <h1>Actualizacion AlfaCore Patch 0.1</h1>
                    <p>Este boletin queda como borrador base para completar con las novedades del periodo.</p>
                    <div class="ticket-editor-banner ticket-editor-banner--info"><i class="bi bi-info-circle-fill ticket-editor-banner__icon" contenteditable="false"></i><div class="ticket-editor-banner__body"><p>Completar alcance, fechas, modulos impactados y capturas antes de programar la publicacion.</p></div></div>
                    <h2>Resumen ejecutivo</h2>
                    <p>Describir en pocas lineas que cambio y por que es importante para el equipo.</p>
                    <h2>Nuevos modulos</h2>
                    <ul><li>Modulo - objetivo - donde encontrarlo.</li></ul>
                    <h2>Funcionalidades nuevas</h2>
                    <ul><li>Funcionalidad - beneficio - usuarios impactados.</li></ul>
                    <h2>Mejoras operativas</h2>
                    <ul><li>Mejora - antes - ahora.</li></ul>
                    <h2>Imagenes y capturas sugeridas</h2>
                    <div class="ticket-editor-file ticket-editor-file--media" contenteditable="false" tabindex="0" data-ticket-media-block="true"><button type="button" class="ticket-editor-file__button" data-ticket-file-picker="true" data-ticket-upload-kind="media"><i class="bi bi-file-earmark-image ticket-editor-file__icon"></i><span>Elegir medio</span></button><div class="ticket-editor-file__content"><strong>Medio</strong><span class="ticket-editor-file__status">Imagen del modulo o pantalla principal</span><div class="ticket-editor-file__files"></div><div class="ticket-editor-file__preview"></div><small class="ticket-editor-file__replace-hint">Doble click para reemplazar</small></div></div>
                    <h2>Acciones para el equipo</h2>
                    <ul class="ticket-editor-todo"><li><input type="checkbox"> Revisar el borrador</li><li><input type="checkbox"> Agregar capturas finales</li><li><input type="checkbox"> Programar envio</li></ul>
                    """
            };
        }

        return new NovedadSaveRequest
        {
            Tipo = NovedadTipos.Manual,
            Prioridad = NovedadPrioridades.Info,
            Version = string.Empty,
            Titulo = "Nueva novedad interna",
            Bajada = "Comunicado interno para el equipo.",
            Icono = "bi-megaphone",
            Acento = NovedadAcentos.Azul,
            PortadaTipo = NovedadPortadas.Ninguna,
            Footer = "AlfaCore",
            Contenido = "<h1>Nueva novedad interna</h1><p>Escribi el comunicado para el equipo.</p><h2>Detalle</h2><p><br></p>"
        };
    }

    private static string BuildSummary(string? content)
    {
        var text = Regex.Replace(content ?? string.Empty, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text ?? string.Empty, @"[#*_`\[\]\|\-]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 500 ? text : text[..500];
    }

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeTipo(string? value)
        => string.Equals(value?.Trim(), NovedadTipos.BoletinPatch, StringComparison.OrdinalIgnoreCase)
            ? NovedadTipos.BoletinPatch
            : NovedadTipos.Manual;

    private static string NormalizeEstado(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            NovedadEstados.Programado => NovedadEstados.Programado,
            NovedadEstados.Publicado => NovedadEstados.Publicado,
            NovedadEstados.Archivado => NovedadEstados.Archivado,
            _ => NovedadEstados.Borrador
        };

    private static string NormalizePrioridad(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            NovedadPrioridades.Importante => NovedadPrioridades.Importante,
            NovedadPrioridades.Critico => NovedadPrioridades.Critico,
            _ => NovedadPrioridades.Info
        };

    private static string NormalizeAcento(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            NovedadAcentos.Verde => NovedadAcentos.Verde,
            NovedadAcentos.Cian => NovedadAcentos.Cian,
            NovedadAcentos.Ambar => NovedadAcentos.Ambar,
            NovedadAcentos.Rojo => NovedadAcentos.Rojo,
            NovedadAcentos.Grafito => NovedadAcentos.Grafito,
            _ => NovedadAcentos.Azul
        };

    private static string NormalizePortadaTipo(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            NovedadPortadas.Gradiente => NovedadPortadas.Gradiente,
            NovedadPortadas.Url => NovedadPortadas.Url,
            _ => NovedadPortadas.Ninguna
        };

    private static string ResolveCoverExtension(string contentType, string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
            return extension == ".jpeg" ? ".jpg" : extension;

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string Truncate(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private async Task<T> ExecuteLoggedAsync<T>(string module, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
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
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Codigo: {incidentId}", ex);
        }
    }

    private async Task ExecuteLoggedAsync(string module, string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
        => await ExecuteLoggedAsync(module, action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
}

using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class InformesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IWebHostEnvironment environment) : IInformesService
{
    private const string ModuleName = "Informes";
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

    public Task<InformesPageDto> GetPageAsync(string usuario, long? idArticulo = null, string? busqueda = null, bool incluirPapelera = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPage", async token =>
        {
            var user = NormalizeUser(usuario);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var articles = (await cn.QueryAsync<InformesArticuloResumenDto>(new CommandDefinition(
                ArticleListSql(busqueda),
                new
                {
                    Usuario = user,
                    Busqueda = NormalizeSearch(busqueda),
                    IncluirPapelera = incluirPapelera
                },
                cancellationToken: token))).ToList();

            var selectedId = idArticulo.GetValueOrDefault();
            if (selectedId <= 0)
                selectedId = articles.FirstOrDefault(x => !x.Archivado)?.IdArticulo ?? articles.FirstOrDefault()?.IdArticulo ?? 0;

            var selected = selectedId > 0 ? await GetArticleCoreAsync(cn, selectedId, user, token) : null;

            return new InformesPageDto
            {
                Articulos = articles,
                Plantillas = BuildTemplates(),
                Seleccionado = selected,
                Comentarios = selected is null ? [] : await GetCommentsCoreAsync(cn, selected.IdArticulo, token),
                Versiones = selected is null ? [] : await GetVersionsCoreAsync(cn, selected.IdArticulo, token)
            };
        }, "No se pudo cargar el modulo de informes.", ct);

    public Task<InformesArticuloDetalleDto?> GetArticleAsync(long idArticulo, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetArticle", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await GetArticleCoreAsync(cn, idArticulo, NormalizeUser(usuario), token);
        }, "No se pudo cargar el articulo.", ct);

    public Task<long> CreateArticleAsync(InformesArticuloCreateRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CreateArticle", async token =>
        {
            request ??= new InformesArticuloCreateRequest();
            var user = NormalizeUser(request.UsuarioAccion);
            var template = BuildTemplates().FirstOrDefault(x => string.Equals(x.Key, request.PlantillaKey, StringComparison.OrdinalIgnoreCase));
            var title = template?.Titulo ?? "Sin titulo";
            var content = template?.Contenido ?? string.Empty;
            var emoji = template?.Emoji ?? "bi-file-earmark-text";
            var category = NormalizeCategory(request.Categoria);

            const string sql = """
                INSERT INTO dbo.ALFACORE_INFORMES_ARTICULOS
                (
                    IdPadre, Categoria, Titulo, Emoji, Contenido, Resumen,
                    PortadaTipo, PortadaValor, UsuarioPropietario, UsuarioAlta,
                    FechaHora_Alta, FechaHora_Modificacion, Archivado, Orden
                )
                VALUES
                (
                    @IdPadre, @Categoria, @Titulo, @Emoji, @Contenido, @Resumen,
                    @PortadaTipo, @PortadaValor, @UsuarioPropietario, @UsuarioAlta,
                    GETDATE(), GETDATE(), 0,
                    ISNULL((SELECT MAX(Orden) + 10 FROM dbo.ALFACORE_INFORMES_ARTICULOS WHERE ISNULL(IdPadre, 0) = ISNULL(@IdPadre, 0)), 10)
                );

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
            {
                request.IdPadre,
                Categoria = category,
                Titulo = title,
                Emoji = emoji,
                Contenido = content,
                Resumen = BuildSummary(content),
                PortadaTipo = template is null ? InformesPortadas.Ninguna : InformesPortadas.Gradiente,
                PortadaValor = template is null ? string.Empty : "alfa",
                UsuarioPropietario = category == InformesCategorias.Privado ? user : null,
                UsuarioAlta = user
            }, cancellationToken: token));
        }, "No se pudo crear el articulo.", ct);

    public Task<long> SaveArticleAsync(InformesArticuloSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveArticle", async token =>
        {
            request ??= new InformesArticuloSaveRequest();
            if (request.IdArticulo <= 0)
            {
                return await CreateArticleAsync(new InformesArticuloCreateRequest
                {
                    IdPadre = request.IdPadre,
                    Categoria = request.Categoria,
                    UsuarioAccion = request.UsuarioAccion
                }, token);
            }

            var user = NormalizeUser(request.UsuarioAccion);
            var title = FirstNonEmpty(request.Titulo, "Sin titulo");
            var content = request.Contenido?.Trim() ?? string.Empty;
            var category = NormalizeCategory(request.Categoria);
            var coverType = NormalizeCoverType(request.PortadaTipo);

            const string sql = """
                INSERT INTO dbo.ALFACORE_INFORMES_VERSIONES
                (
                    IdArticulo, Titulo, Emoji, Contenido, PortadaTipo, PortadaValor,
                    Usuario, FechaHora
                )
                SELECT
                    IdArticulo, Titulo, Emoji, Contenido, PortadaTipo, PortadaValor,
                    @Usuario, GETDATE()
                FROM dbo.ALFACORE_INFORMES_ARTICULOS
                WHERE IdArticulo = @IdArticulo;

                UPDATE dbo.ALFACORE_INFORMES_ARTICULOS
                   SET IdPadre = @IdPadre,
                       Categoria = @Categoria,
                       Titulo = @Titulo,
                       Emoji = @Emoji,
                       Contenido = @Contenido,
                       Resumen = @Resumen,
                       PortadaTipo = @PortadaTipo,
                       PortadaValor = @PortadaValor,
                       Bloqueado = @Bloqueado,
                       UsuarioPropietario = CASE WHEN @Categoria = N'PRIVATE' THEN COALESCE(NULLIF(UsuarioPropietario, N''), @Usuario) ELSE NULL END,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdArticulo = @IdArticulo;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                request.IdArticulo,
                request.IdPadre,
                Categoria = category,
                Titulo = title,
                Emoji = FirstNonEmpty(request.Emoji, "bi-file-earmark-text"),
                Contenido = content,
                Resumen = BuildSummary(content),
                PortadaTipo = coverType,
                PortadaValor = request.PortadaValor?.Trim() ?? string.Empty,
                Bloqueado = request.Bloqueado,
                Usuario = user
            }, cancellationToken: token));

            return request.IdArticulo;
        }, "No se pudo guardar el articulo.", ct);

    public Task DeleteArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteArticle", async token =>
        {
            const string sql = """
                UPDATE dbo.ALFACORE_INFORMES_ARTICULOS
                   SET Archivado = 1,
                       FechaHora_Archivado = GETDATE(),
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdArticulo = @IdArticulo;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdArticulo = idArticulo, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo enviar el articulo a la papelera.", ct);

    public Task RestoreArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RestoreArticle", async token =>
        {
            const string sql = """
                UPDATE dbo.ALFACORE_INFORMES_ARTICULOS
                   SET Archivado = 0,
                       FechaHora_Archivado = NULL,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdArticulo = @IdArticulo;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdArticulo = idArticulo, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo restaurar el articulo.", ct);

    public Task<long> DuplicateArticleAsync(long idArticulo, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DuplicateArticle", async token =>
        {
            const string sql = """
                INSERT INTO dbo.ALFACORE_INFORMES_ARTICULOS
                (
                    IdPadre, Categoria, Titulo, Emoji, Contenido, Resumen,
                    PortadaTipo, PortadaValor, EsCompartido, Bloqueado,
                    UsuarioPropietario, UsuarioAlta, FechaHora_Alta,
                    FechaHora_Modificacion, Archivado, Orden
                )
                SELECT
                    IdPadre, Categoria, Titulo + N' (copia)', Emoji, Contenido, Resumen,
                    PortadaTipo, PortadaValor, 0, 0,
                    CASE WHEN Categoria = N'PRIVATE' THEN @Usuario ELSE NULL END,
                    @Usuario, GETDATE(), GETDATE(), 0,
                    ISNULL((SELECT MAX(Orden) + 10 FROM dbo.ALFACORE_INFORMES_ARTICULOS WHERE ISNULL(IdPadre, 0) = ISNULL(a.IdPadre, 0)), 10)
                FROM dbo.ALFACORE_INFORMES_ARTICULOS a
                WHERE a.IdArticulo = @IdArticulo;

                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            return await cn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { IdArticulo = idArticulo, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo duplicar el articulo.", ct);

    public Task ToggleFavoriteAsync(long idArticulo, string usuario, bool favorito, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleFavorite", async token =>
        {
            var user = NormalizeUser(usuario);
            var sql = favorito
                ? """
                  IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_INFORMES_FAVORITOS WHERE IdArticulo = @IdArticulo AND Usuario = @Usuario)
                  BEGIN
                      INSERT INTO dbo.ALFACORE_INFORMES_FAVORITOS (IdArticulo, Usuario, FechaHora_Alta)
                      VALUES (@IdArticulo, @Usuario, GETDATE());
                  END;
                  """
                : """
                  DELETE FROM dbo.ALFACORE_INFORMES_FAVORITOS
                  WHERE IdArticulo = @IdArticulo AND Usuario = @Usuario;
                  """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdArticulo = idArticulo, Usuario = user }, cancellationToken: token));
        }, "No se pudo actualizar el favorito.", ct);

    public Task ToggleShareAsync(long idArticulo, string usuarioAccion, bool compartido, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleShare", async token =>
        {
            const string sql = """
                UPDATE dbo.ALFACORE_INFORMES_ARTICULOS
                   SET EsCompartido = @Compartido,
                       TokenPublico = CASE
                           WHEN @Compartido = 1 THEN COALESCE(TokenPublico, NEWID())
                           ELSE TokenPublico
                       END,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdArticulo = @IdArticulo;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdArticulo = idArticulo, Compartido = compartido, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo actualizar el estado de compartir.", ct);

    public Task AddCommentAsync(InformesComentarioRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AddComment", async token =>
        {
            if (request is null || request.IdArticulo <= 0 || string.IsNullOrWhiteSpace(request.Texto))
                return;

            const string sql = """
                INSERT INTO dbo.ALFACORE_INFORMES_COMENTARIOS
                    (IdArticulo, Texto, UsuarioAlta, FechaHora_Alta, Resuelto)
                VALUES
                    (@IdArticulo, @Texto, @Usuario, GETDATE(), 0);
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new
            {
                request.IdArticulo,
                Texto = request.Texto.Trim(),
                Usuario = NormalizeUser(request.UsuarioAccion)
            }, cancellationToken: token));
        }, "No se pudo agregar el comentario.", ct);

    public Task ToggleCommentResolvedAsync(long idComentario, string usuarioAccion, bool resuelto, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleCommentResolved", async token =>
        {
            const string sql = """
                UPDATE dbo.ALFACORE_INFORMES_COMENTARIOS
                   SET Resuelto = @Resuelto,
                       UsuarioModificacion = @Usuario,
                       FechaHora_Modificacion = GETDATE()
                 WHERE IdComentario = @IdComentario;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdComentario = idComentario, Resuelto = resuelto, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo actualizar el comentario.", ct);

    public Task RestoreVersionAsync(long idVersion, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "RestoreVersion", async token =>
        {
            const string sql = """
                INSERT INTO dbo.ALFACORE_INFORMES_VERSIONES
                (
                    IdArticulo, Titulo, Emoji, Contenido, PortadaTipo, PortadaValor,
                    Usuario, FechaHora
                )
                SELECT
                    a.IdArticulo, a.Titulo, a.Emoji, a.Contenido, a.PortadaTipo, a.PortadaValor,
                    @Usuario, GETDATE()
                FROM dbo.ALFACORE_INFORMES_ARTICULOS a
                INNER JOIN dbo.ALFACORE_INFORMES_VERSIONES v ON v.IdArticulo = a.IdArticulo
                WHERE v.IdVersion = @IdVersion;

                UPDATE a
                   SET a.Titulo = v.Titulo,
                       a.Emoji = v.Emoji,
                       a.Contenido = v.Contenido,
                       a.Resumen = LEFT(REPLACE(REPLACE(v.Contenido, CHAR(13), N' '), CHAR(10), N' '), 240),
                       a.PortadaTipo = v.PortadaTipo,
                       a.PortadaValor = v.PortadaValor,
                       a.UsuarioModificacion = @Usuario,
                       a.FechaHora_Modificacion = GETDATE()
                FROM dbo.ALFACORE_INFORMES_ARTICULOS a
                INNER JOIN dbo.ALFACORE_INFORMES_VERSIONES v ON v.IdArticulo = a.IdArticulo
                WHERE v.IdVersion = @IdVersion;
                """;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdVersion = idVersion, Usuario = NormalizeUser(usuarioAccion) }, cancellationToken: token));
        }, "No se pudo restaurar la version.", ct);

    public Task<string> SaveCoverImageAsync(Stream content, string fileName, string contentType, string usuarioAccion, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveCoverImage", async token =>
        {
            if (content is null || !content.CanRead)
                throw new InvalidOperationException("No se recibio una imagen valida para la portada.");

            if (!AllowedCoverContentTypes.Contains(contentType))
                throw new InvalidOperationException("La portada debe ser una imagen JPG, PNG, WebP o GIF.");

            var extension = ResolveCoverExtension(contentType, fileName);
            var safeUser = Regex.Replace(NormalizeUser(usuarioAccion), @"[^a-zA-Z0-9_-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(safeUser))
                safeUser = "usuario";

            var rawFileBase = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeUser}-{Guid.NewGuid():N}";
            var fileBase = rawFileBase.Length <= 80 ? rawFileBase : rawFileBase[..80];
            var relativePath = $"/uploads/informes-covers/{fileBase}{extension}";
            var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;
            var directory = Path.Combine(webRoot, "uploads", "informes-covers");
            Directory.CreateDirectory(directory);

            var physicalPath = Path.Combine(directory, $"{fileBase}{extension}");
            await using var output = File.Create(physicalPath);
            await content.CopyToAsync(output, token);
            return relativePath;
        }, "No se pudo guardar la imagen de portada.", ct);

    private static string ArticleListSql(string? busqueda)
    {
        var searchFilter = string.IsNullOrWhiteSpace(busqueda)
            ? string.Empty
            : "AND (a.Titulo LIKE '%' + @Busqueda + '%' OR a.Contenido LIKE '%' + @Busqueda + '%' OR a.Resumen LIKE '%' + @Busqueda + '%')";

        return $"""
            SELECT
                a.IdArticulo,
                a.IdPadre,
                a.Categoria,
                a.Titulo,
                a.Emoji,
                ISNULL(a.Resumen, '') AS Resumen,
                CONVERT(bit, CASE WHEN f.IdArticulo IS NULL THEN 0 ELSE 1 END) AS EsFavorito,
                ISNULL(a.EsCompartido, 0) AS EsCompartido,
                ISNULL(a.Bloqueado, 0) AS Bloqueado,
                ISNULL(a.Archivado, 0) AS Archivado,
                ISNULL(a.Orden, 0) AS Orden,
                ISNULL(a.FechaHora_Modificacion, a.FechaHora_Alta) AS FechaHoraModificacion
            FROM dbo.ALFACORE_INFORMES_ARTICULOS a
            LEFT JOIN dbo.ALFACORE_INFORMES_FAVORITOS f
                   ON f.IdArticulo = a.IdArticulo
                  AND UPPER(LTRIM(RTRIM(f.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            WHERE (@IncluirPapelera = 1 OR ISNULL(a.Archivado, 0) = 0)
              AND (
                    a.Categoria = N'WORKSPACE'
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.UsuarioPropietario, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                  )
              {searchFilter}
            ORDER BY a.Categoria, ISNULL(a.IdPadre, 0), ISNULL(a.Orden, 0), a.Titulo;
            """;
    }

    private static async Task<InformesArticuloDetalleDto?> GetArticleCoreAsync(SqlConnection cn, long idArticulo, string usuario, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                a.IdArticulo,
                a.IdPadre,
                a.Categoria,
                a.Titulo,
                a.Emoji,
                ISNULL(a.Resumen, '') AS Resumen,
                CONVERT(bit, CASE WHEN f.IdArticulo IS NULL THEN 0 ELSE 1 END) AS EsFavorito,
                ISNULL(a.EsCompartido, 0) AS EsCompartido,
                ISNULL(a.Bloqueado, 0) AS Bloqueado,
                ISNULL(a.Archivado, 0) AS Archivado,
                ISNULL(a.Orden, 0) AS Orden,
                ISNULL(a.FechaHora_Modificacion, a.FechaHora_Alta) AS FechaHoraModificacion,
                ISNULL(a.Contenido, '') AS Contenido,
                ISNULL(a.PortadaTipo, 'NONE') AS PortadaTipo,
                ISNULL(a.PortadaValor, '') AS PortadaValor,
                ISNULL(a.UsuarioPropietario, '') AS UsuarioPropietario,
                ISNULL(a.UsuarioAlta, '') AS UsuarioAlta,
                ISNULL(a.UsuarioModificacion, '') AS UsuarioModificacion,
                a.TokenPublico
            FROM dbo.ALFACORE_INFORMES_ARTICULOS a
            LEFT JOIN dbo.ALFACORE_INFORMES_FAVORITOS f
                   ON f.IdArticulo = a.IdArticulo
                  AND UPPER(LTRIM(RTRIM(f.Usuario))) = UPPER(LTRIM(RTRIM(@Usuario)))
            WHERE a.IdArticulo = @IdArticulo
              AND (
                    a.Categoria = N'WORKSPACE'
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.UsuarioPropietario, '')))) = UPPER(LTRIM(RTRIM(@Usuario)))
                  );
            """;

        return await cn.QueryFirstOrDefaultAsync<InformesArticuloDetalleDto>(new CommandDefinition(sql, new { IdArticulo = idArticulo, Usuario = usuario }, cancellationToken: ct));
    }

    private static async Task<List<InformesComentarioDto>> GetCommentsCoreAsync(SqlConnection cn, long idArticulo, CancellationToken ct)
    {
        const string sql = """
            SELECT
                IdComentario,
                IdArticulo,
                ISNULL(Texto, '') AS Texto,
                ISNULL(UsuarioAlta, '') AS UsuarioAlta,
                FechaHora_Alta AS FechaHoraAlta,
                ISNULL(Resuelto, 0) AS Resuelto
            FROM dbo.ALFACORE_INFORMES_COMENTARIOS
            WHERE IdArticulo = @IdArticulo
            ORDER BY ISNULL(Resuelto, 0), FechaHora_Alta DESC;
            """;

        return (await cn.QueryAsync<InformesComentarioDto>(new CommandDefinition(sql, new { IdArticulo = idArticulo }, cancellationToken: ct))).ToList();
    }

    private static async Task<List<InformesVersionDto>> GetVersionsCoreAsync(SqlConnection cn, long idArticulo, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (15)
                IdVersion,
                IdArticulo,
                ISNULL(Titulo, '') AS Titulo,
                ISNULL(Usuario, '') AS Usuario,
                FechaHora
            FROM dbo.ALFACORE_INFORMES_VERSIONES
            WHERE IdArticulo = @IdArticulo
            ORDER BY FechaHora DESC, IdVersion DESC;
            """;

        return (await cn.QueryAsync<InformesVersionDto>(new CommandDefinition(sql, new { IdArticulo = idArticulo }, cancellationToken: ct))).ToList();
    }

    private static List<InformesPlantillaDto> BuildTemplates()
        =>
        [
            new()
            {
                Key = "minuta",
                Grupo = "Productividad",
                Nombre = "Minuta de reunion",
                Icono = "bi-journal-check",
                Descripcion = "Acuerdos, responsables y proximos pasos.",
                Titulo = "Minuta de reunion",
                Emoji = "bi-journal-check",
                Contenido = "<h2>Objetivo</h2><p><br></p><h2>Participantes</h2><ul><li><br></li></ul><h2>Acuerdos</h2><ul class=\"ticket-editor-todo\"><li><input type=\"checkbox\"> Pendiente</li></ul><h2>Proximos pasos</h2><p>Responsable - Tarea - Fecha</p><p><br></p>"
            },
            new()
            {
                Key = "agenda",
                Grupo = "Productividad",
                Nombre = "Agenda personal",
                Icono = "bi-calendar3",
                Descripcion = "Plan semanal con prioridades y recordatorios.",
                Titulo = "Agenda personal",
                Emoji = "bi-calendar3",
                Contenido = "<h2>Prioridades de la semana</h2><ul class=\"ticket-editor-todo\"><li><input type=\"checkbox\"> Prioridad A</li><li><input type=\"checkbox\"> Prioridad B</li></ul><h2>Recordatorios</h2><ul><li>Recordatorio</li></ul><h2>Agenda</h2><p>Lunes - Bloque - Tema</p><p>Martes - Bloque - Tema</p>"
            },
            new()
            {
                Key = "implementacion",
                Grupo = "Operacion",
                Nombre = "Guia de implementacion",
                Icono = "bi-rocket-takeoff",
                Descripcion = "Checklist de alcance, tareas y validaciones.",
                Titulo = "Guia de implementacion",
                Emoji = "bi-rocket-takeoff",
                Contenido = "<h2>Alcance</h2><p><br></p><h2>Requisitos</h2><ul class=\"ticket-editor-todo\"><li><input type=\"checkbox\"> Requisito pendiente</li></ul><h2>Plan</h2><p>Relevamiento - Responsable - Pendiente</p><p>Configuracion - Responsable - Pendiente</p><p>Puesta en marcha - Responsable - Pendiente</p><h2>Riesgos</h2><p><br></p>"
            },
            new()
            {
                Key = "procedimiento",
                Grupo = "Soporte",
                Nombre = "Procedimiento interno",
                Icono = "bi-tools",
                Descripcion = "Pasos claros para soporte y mesa de ayuda.",
                Titulo = "Procedimiento interno",
                Emoji = "bi-tools",
                Contenido = "<h2>Cuando usarlo</h2><p><br></p><h2>Pasos</h2><ol><li>Primer paso</li><li>Segundo paso</li><li>Tercer paso</li></ol><h2>Validaciones</h2><ul class=\"ticket-editor-todo\"><li><input type=\"checkbox\"> Validacion pendiente</li></ul><h2>Escalamiento</h2><p><br></p>"
            },
            new()
            {
                Key = "analisis",
                Grupo = "Gestion",
                Nombre = "Analisis de resultados",
                Icono = "bi-graph-up-arrow",
                Descripcion = "Resumen ejecutivo con hallazgos y decisiones.",
                Titulo = "Analisis de resultados",
                Emoji = "bi-graph-up-arrow",
                Contenido = "<h2>Resumen ejecutivo</h2><p><br></p><h2>Indicadores</h2><p>Indicador - Valor - Observacion</p><h2>Hallazgos</h2><ul><li>Hallazgo</li></ul><h2>Decisiones</h2><ul class=\"ticket-editor-todo\"><li><input type=\"checkbox\"> Decision pendiente</li></ul>"
            }
        ];

    private static string BuildSummary(string? content)
    {
        var text = Regex.Replace(content ?? string.Empty, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text ?? string.Empty, @"[#*_`\[\]\|\-]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 240 ? text : text[..240];
    }

    private static string NormalizeSearch(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? Environment.UserName : value.Trim();

    private static string NormalizeCategory(string? value)
        => string.Equals(value?.Trim(), InformesCategorias.Privado, StringComparison.OrdinalIgnoreCase)
            ? InformesCategorias.Privado
            : InformesCategorias.EspacioTrabajo;

    private static string NormalizeCoverType(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            InformesPortadas.Gradiente => InformesPortadas.Gradiente,
            InformesPortadas.Url => InformesPortadas.Url,
            _ => InformesPortadas.Ninguna
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

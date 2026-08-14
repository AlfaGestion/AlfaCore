using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class InterfacesCatalogosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IInterfacesCatalogosService
{
    private const string ModuleName = "Interfaces";
    private const string ConfigGroup = "CATALOGOS";
    private const string MenuEnabledConfigKey = "CATALOGOS-MENU-HABILITADO";
    private const string ViewConfigPrefix = "USUVIEW-CATALOGOS-";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<IReadOnlyList<CatalogosModalidadOptionDto>> GetModalidadesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CatalogosModalidadOptionDto>>(
            [
                new() { Clave = "permanente", Nombre = "Catálogo permanente", Descripcion = "Catálogo sin vencimiento, preparado para publicar más adelante." },
                new() { Clave = "vigencia", Nombre = "Catálogo con vigencia", Descripcion = "Catálogo publicado sobre V_MV_INSERT con control de fechas y estado." }
            ]);

    public Task<IReadOnlyList<CatalogosListaPrecioDto>> GetListasPrecioAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetListasPrecio", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_PreciosCab", token))
                return Array.Empty<CatalogosListaPrecioDto>();

            const string sql = """
                SELECT
                    ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                    ISNULL(LTRIM(RTRIM(Nombre)), '') AS Nombre,
                    ISNULL(LTRIM(RTRIM(Grupo)), '') AS Grupo,
                    ISNULL(LTRIM(RTRIM(TipoLista)), '') AS TipoLista,
                    CASE
                        WHEN (VigenciaDesde IS NULL OR VigenciaDesde <= GETDATE())
                         AND (VigenciaHasta IS NULL OR VigenciaHasta >= GETDATE()) THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS Vigente
                FROM dbo.V_MA_PreciosCab
                WHERE UPPER(LTRIM(RTRIM(ISNULL(TipoLista, 'V')))) = 'V'
                ORDER BY
                    CASE
                        WHEN (VigenciaDesde IS NULL OR VigenciaDesde <= GETDATE())
                         AND (VigenciaHasta IS NULL OR VigenciaHasta >= GETDATE()) THEN 0
                        ELSE 1
                    END,
                    Nombre,
                    IdLista;
                """;

            var rows = await cn.QueryAsync<CatalogosListaPrecioDto>(new CommandDefinition(sql, cancellationToken: token));
            return (IReadOnlyList<CatalogosListaPrecioDto>)rows.ToList();
        }, "No se pudieron cargar las listas de precios.", ct);

    public Task<PagedResult<CatalogosArticuloBusquedaDto>> SearchArticulosAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchArticulos", async token =>
        {
            filters ??= new CatalogosArticuloBusquedaFiltersDto();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 100));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;
            var textoLike = LikeContains(filters.Texto);
            var idLista = (filters.IdLista ?? string.Empty).Trim();
            var origen = (filters.Origen ?? string.Empty).Trim();
            var usarLista = string.Equals(origen, CatalogosArticuloOrigenKeys.ListaPrecio, StringComparison.OrdinalIgnoreCase);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
                return EmptyArticuloPage(pageNumber, pageSize);

            if (usarLista && string.IsNullOrWhiteSpace(idLista))
                return EmptyArticuloPage(pageNumber, pageSize);

            var joinPrecio = string.IsNullOrWhiteSpace(idLista)
                ? """
                  LEFT JOIN dbo.V_MA_Precios p
                      ON 1 = 0
                  LEFT JOIN dbo.V_MA_PreciosCab pc
                      ON 1 = 0
                  """
                : """
                  LEFT JOIN dbo.V_MA_Precios p
                      ON p.IdArticulo = a.IDARTICULO
                     AND p.IdLista = @IdLista
                     AND p.TipoLista = 'V'
                  LEFT JOIN dbo.V_MA_PreciosCab pc
                      ON pc.IdLista = p.IdLista
                  """;

            var whereLista = usarLista ? "AND p.IdLista IS NOT NULL" : string.Empty;

            var selectSql = $"""
                SELECT
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    ISNULL(LTRIM(RTRIM(p.IdLista)), '') AS ListaPrecio,
                    ISNULL(LTRIM(RTRIM(pc.Nombre)), '') AS NombreListaPrecio,
                    ISNULL(p.Precio1, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND p.FhOfertaHasta IS NOT NULL
                         AND GETDATE() BETWEEN p.FhOfertaDesde AND p.FhOfertaHasta THEN p.Precio0
                        ELSE NULL
                    END AS PrecioOferta
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {joinPrecio}
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  {whereLista}
                  AND (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                      )
                ORDER BY a.DESCRIPCION, a.IDARTICULO
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            var countSql = $"""
                SELECT COUNT(1)
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {joinPrecio}
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  {whereLista}
                  AND (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                      );
                """;

            var items = (await cn.QueryAsync<CatalogosArticuloBusquedaDto>(new CommandDefinition(
                selectSql,
                new { IdLista = idLista, TextoLike = textoLike, Skip = skip, PageSize = pageSize },
                cancellationToken: token))).ToList();

            var count = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                countSql,
                new { IdLista = idLista, TextoLike = textoLike },
                cancellationToken: token));

            return new PagedResult<CatalogosArticuloBusquedaDto>
            {
                Items = items,
                Total = count,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron buscar los artículos.", ct);

    public Task<int> CountArticulosDesdeListaAsync(string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CountArticulosDesdeLista", async token =>
        {
            var lista = (idLista ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(lista))
                return 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return 0;

            const string sql = """
                SELECT COUNT(1)
                FROM dbo.V_MA_Precios p
                WHERE UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, '')))) = UPPER(LTRIM(RTRIM(@IdLista)))
                  AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, 'V')))) = 'V';
                """;

            return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { IdLista = lista }, cancellationToken: token));
        }, "No se pudo contar la lista de precios.", ct);

    public Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetArticulosDesdeLista", async token =>
        {
            var lista = (idLista ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(lista))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token) || !await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            const string sql = """
                SELECT
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    ISNULL(LTRIM(RTRIM(p.IdLista)), '') AS ListaPrecio,
                    ISNULL(LTRIM(RTRIM(pc.Nombre)), '') AS NombreListaPrecio,
                    ISNULL(p.Precio1, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND p.FhOfertaHasta IS NOT NULL
                         AND GETDATE() BETWEEN p.FhOfertaDesde AND p.FhOfertaHasta THEN p.Precio0
                        ELSE NULL
                    END AS PrecioOferta
                FROM dbo.V_MA_Precios p
                INNER JOIN dbo.V_MA_ARTICULOS a
                    ON a.IDARTICULO = p.IdArticulo
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                LEFT JOIN dbo.V_MA_PreciosCab pc
                    ON pc.IdLista = p.IdLista
                WHERE UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, '')))) = UPPER(LTRIM(RTRIM(@IdLista)))
                  AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, 'V')))) = 'V'
                  AND ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                ORDER BY a.DESCRIPCION, a.IDARTICULO;
                """;

            var items = await cn.QueryAsync<CatalogosArticuloBusquedaDto>(new CommandDefinition(sql, new { IdLista = lista }, cancellationToken: token));
            return (IReadOnlyList<CatalogosArticuloBusquedaDto>)items.ToList();
        }, "No se pudieron importar los artículos de la lista.", ct);

    public Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchCatalogos", async token =>
        {
            var normalizedText = LikeContains(texto);
            pageSize = Math.Max(1, Math.Min(pageSize, 100));
            pageNumber = Math.Max(1, pageNumber);
            var skip = (pageNumber - 1) * pageSize;
            var fecha = (fechaFiltro ?? DateTime.Today).Date;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MV_INSERT", token))
            {
                return new PagedResult<CatalogosCatalogoResumenDto>
                {
                    Items = [],
                    Total = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }

            var sql = """
                SELECT
                    c.IdInsert,
                    CASE
                        WHEN MIN(c.VigenciaDesde) IS NULL
                         AND MAX(c.VigenciaHasta) IS NULL THEN N'Permanente'
                        ELSE N'Con vigencia'
                    END AS Tipo,
                    MAX(ISNULL(NULLIF(LTRIM(RTRIM(c.GRUPO)), ''), CONCAT(N'Catálogo ', CONVERT(nvarchar(20), c.IDINSERT)))) AS Nombre,
                    CASE
                        WHEN MIN(c.VigenciaDesde) IS NULL AND MAX(c.VigenciaHasta) IS NULL THEN N'Sin vigencia'
                        ELSE CONCAT(
                            ISNULL(CONVERT(nvarchar(10), MIN(c.VigenciaDesde), 103), N''),
                            CASE WHEN MIN(c.VigenciaDesde) IS NOT NULL OR MAX(c.VigenciaHasta) IS NOT NULL THEN N' - ' ELSE N'' END,
                            ISNULL(CONVERT(nvarchar(10), MAX(c.VigenciaHasta), 103), N'')
                        )
                    END AS Vigencia,
                    CASE WHEN MAX(CASE WHEN ISNULL(c.FINALIZADO, 0) = 0 THEN 1 ELSE 0 END) = 1 THEN N'Publicado' ELSE N'Finalizado' END AS Estado,
                    MIN(c.VigenciaDesde) AS VigenciaDesde,
                    MAX(c.VigenciaHasta) AS VigenciaHasta,
                    MAX(ISNULL(LTRIM(RTRIM(c.IDLISTA)), '')) AS IdLista,
                    MAX(ISNULL(LTRIM(RTRIM(c.GRUPO)), '')) AS Grupo,
                    MAX(ISNULL(LTRIM(RTRIM(c.Observaciones)), '')) AS Observaciones,
                    MAX(c.FECHACARGA) AS FechaCarga,
                    COUNT(1) AS CantidadArticulos,
                    CASE WHEN MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Finalizado
                FROM dbo.V_MV_INSERT c
                WHERE (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.GRUPO, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.Observaciones, '')))) LIKE @TextoLike
                        OR CONVERT(nvarchar(20), c.IDINSERT) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(c.IDLISTA, '')))) LIKE @TextoLike
                      )
                  AND ISNULL(c.FINALIZADO, 0) = 0
                  AND (c.VigenciaDesde IS NULL OR c.VigenciaDesde <= @FechaFiltro)
                  AND (c.VigenciaHasta IS NULL OR c.VigenciaHasta >= @FechaFiltro)
                GROUP BY c.IDINSERT
                ORDER BY MAX(c.FECHACARGA) DESC, c.IDINSERT DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            var items = (await cn.QueryAsync<CatalogosCatalogoResumenDto>(new CommandDefinition(
                sql,
                new { TextoLike = normalizedText, FechaFiltro = fecha, Skip = skip, PageSize = pageSize },
                cancellationToken: token))).ToList();

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM (
                    SELECT c.IDINSERT
                    FROM dbo.V_MV_INSERT c
                    WHERE (
                            @TextoLike = ''
                            OR UPPER(LTRIM(RTRIM(ISNULL(c.GRUPO, '')))) LIKE @TextoLike
                            OR UPPER(LTRIM(RTRIM(ISNULL(c.Observaciones, '')))) LIKE @TextoLike
                            OR CONVERT(nvarchar(20), c.IDINSERT) LIKE REPLACE(@TextoLike, N'%', N'')
                            OR UPPER(LTRIM(RTRIM(ISNULL(c.IDLISTA, '')))) LIKE @TextoLike
                          )
                      AND ISNULL(c.FINALIZADO, 0) = 0
                      AND (c.VigenciaDesde IS NULL OR c.VigenciaDesde <= @FechaFiltro)
                      AND (c.VigenciaHasta IS NULL OR c.VigenciaHasta >= @FechaFiltro)
                    GROUP BY c.IDINSERT
                ) x;
                """,
                new { TextoLike = normalizedText, FechaFiltro = fecha },
                cancellationToken: token));

            return new PagedResult<CatalogosCatalogoResumenDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los catálogos.", ct);

    public Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default)
        => GetCatalogoInternalAsync(idInsert, soloPublico: false, ct);

    public Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, CancellationToken ct = default)
        => GetCatalogoInternalAsync(idInsert, soloPublico: true, ct);

    public Task<CatalogosCatalogoSaveResultDto> SaveCatalogoVigenciaAsync(CatalogosCatalogoSaveRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveCatalogoVigencia", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var articulos = request.Articulos
                .Where(x => !string.IsNullOrWhiteSpace(x.IdArticulo))
                .GroupBy(x => x.IdArticulo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (articulos.Count == 0)
                throw new InvalidOperationException("Seleccioná al menos un artículo antes de publicar el catálogo.");

            if (string.IsNullOrWhiteSpace(request.Nombre))
                throw new InvalidOperationException("Ingresá el nombre del catálogo.");

            if (string.IsNullOrWhiteSpace(request.IdLista))
                throw new InvalidOperationException("Seleccioná una lista de precios.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MV_INSERT", token))
                throw new InvalidOperationException("La base activa no tiene V_MV_INSERT. No se puede publicar el catálogo con vigencia.");

            await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            try
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    EXEC sp_getapplock
                        @Resource = @Resource,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 10000;
                    """,
                    new { Resource = "ALFACORE-V_MV_INSERT-IDINSERT" },
                    transaction: (SqlTransaction)tx,
                    cancellationToken: token));

                var idInsert = request.IdInsert ?? await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT ISNULL(MAX(IDINSERT), 0) + 1
                    FROM dbo.V_MV_INSERT WITH (UPDLOCK, HOLDLOCK);
                    """,
                    transaction: (SqlTransaction)tx,
                    cancellationToken: token));

                if (request.IdInsert.HasValue)
                {
                    await cn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM dbo.V_MV_INSERT WHERE IDINSERT = @IdInsert;",
                        new { IdInsert = request.IdInsert.Value },
                        transaction: (SqlTransaction)tx,
                        cancellationToken: token));
                }

                var items = articulos.Select(item => new
                {
                    IdInsert = idInsert,
                    FechaCarga = DateTime.Now,
                    VigenciaDesde = request.VigenciaDesde,
                    VigenciaHasta = request.VigenciaHasta,
                    IdLista = Truncate(request.IdLista, 4),
                    Usuario = Truncate(request.Usuario, 100),
                    Grupo = Truncate(request.Nombre, 50),
                    Finalizado = false,
                    Observaciones = Truncate(request.Observaciones, 250),
                    IdArticulo = Truncate(item.IdArticulo, 25),
                    DescripcionArticulo = Truncate(item.DescripcionArticulo, 100),
                    Presentacion = Truncate(item.Presentacion, 50),
                    Marca = Truncate(item.Marca, 50),
                    Costo = (decimal?)null,
                    Precio = item.Precio,
                    MUPanterior = (double?)null,
                    PrecioOferta = item.PrecioOferta,
                    MUPactual = (double?)null,
                    Cantidad1 = 1d,
                    Cantidad2 = 1d,
                    Cantidad3 = 1d,
                    Cantidad4 = 1d,
                    Cantidad5 = 1d,
                    Cantidad6 = 1d,
                    Cantidad7 = 1d,
                    Cantidad8 = 1d,
                    Cantidad9 = 1d,
                    Cantidad10 = 1d,
                    Rubro = Truncate(item.Rubro, 50)
                });

                const string insertSql = """
                    INSERT INTO dbo.V_MV_INSERT
                    (
                        IDINSERT,
                        FECHACARGA,
                        VigenciaDesde,
                        VigenciaHasta,
                        IDLISTA,
                        USUARIO,
                        GRUPO,
                        FINALIZADO,
                        Observaciones,
                        IDARTICULO,
                        DescripcionArticulo,
                        Presentacion,
                        Marca,
                        Costo,
                        Precio,
                        MUPanterior,
                        PrecioOferta,
                        MUPactual,
                        Cantidad1,
                        Cantidad2,
                        Cantidad3,
                        Cantidad4,
                        Cantidad5,
                        Cantidad6,
                        Cantidad7,
                        Cantidad8,
                        Cantidad9,
                        Cantidad10,
                        RUBRO
                    )
                    VALUES
                    (
                        @IdInsert,
                        @FechaCarga,
                        @VigenciaDesde,
                        @VigenciaHasta,
                        @IdLista,
                        @Usuario,
                        @Grupo,
                        @Finalizado,
                        @Observaciones,
                        @IdArticulo,
                        @DescripcionArticulo,
                        @Presentacion,
                        @Marca,
                        @Costo,
                        @Precio,
                        @MUPanterior,
                        @PrecioOferta,
                        @MUPactual,
                        @Cantidad1,
                        @Cantidad2,
                        @Cantidad3,
                        @Cantidad4,
                        @Cantidad5,
                        @Cantidad6,
                        @Cantidad7,
                        @Cantidad8,
                        @Cantidad9,
                        @Cantidad10,
                        @Rubro
                    );
                    """;

                foreach (var row in items)
                    await cn.ExecuteAsync(new CommandDefinition(insertSql, row, transaction: (SqlTransaction)tx, cancellationToken: token));

                await tx.CommitAsync(token);

                var url = BuildPublicUrl(idInsert);
                await appEvents.LogAuditAsync(
                    ModuleName,
                    "SaveCatalogoVigencia",
                    "V_MV_INSERT",
                    idInsert.ToString(),
                    "Catálogo con vigencia publicado.",
                    new { request.Nombre, request.IdLista, request.VigenciaDesde, request.VigenciaHasta, Articulos = articulos.Count, Url = url },
                    token);

                return new CatalogosCatalogoSaveResultDto
                {
                    Persistido = true,
                    Simulado = false,
                    IdInsert = idInsert,
                    UrlPublica = url,
                    Mensaje = "Catálogo publicado correctamente."
                };
            }
            catch
            {
                try { await tx.RollbackAsync(token); } catch { }
                throw;
            }
        }, "No se pudo publicar el catálogo con vigencia.", ct);

    public Task FinalizarCatalogoAsync(int idInsert, string usuario, string pc, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "FinalizarCatalogo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.V_MV_INSERT
                SET FINALIZADO = 1
                WHERE IDINSERT = @IdInsert;
                """,
                new { IdInsert = idInsert },
                cancellationToken: token));

            await appEvents.LogAuditAsync(
                ModuleName,
                "FinalizarCatalogo",
                "V_MV_INSERT",
                idInsert.ToString(),
                "Catálogo finalizado.",
                new { Usuario = usuario, Pc = pc },
                token);

            return true;
        }, "No se pudo finalizar el catálogo.", ct);

    public async Task<bool> GetMenuHabilitadoAsync(CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct))
                return false;

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(sql, new { Clave = MenuEnabledConfigKey }, cancellationToken: ct));
            var raw = ResolveStoredValue(row.Valor, row.ValorAux);
            return string.Equals(raw.Trim(), "SI", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(
                ModuleName,
                "GetCatalogosMenuEnabled",
                ex,
                "No se pudo leer la configuración de visibilidad del menú de catálogos.",
                new { Usuario = appUserSession.GetCurrentUserName(Environment.UserName) },
                AppEventSeverity.Warning,
                ct);

            return false;
        }
    }

    public Task SaveMenuHabilitadoAsync(string userName, bool habilitado, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveCatalogosMenuEnabled", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la configuración del menú.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var value = habilitado ? "SI" : "NO";

            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET
                    VALOR = @Valor,
                    {detailColumn} = NULL,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE,
                        VALOR,
                        {detailColumn},
                        GRUPO,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave,
                        @Valor,
                        NULL,
                        @Grupo,
                        GETDATE(),
                        GETDATE()
                    );
                END;
                """;

            await cn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    ClaveNormalizada = MenuEnabledConfigKey,
                    Clave = MenuEnabledConfigKey,
                    Valor = value,
                    Grupo = ConfigGroup
                },
                cancellationToken: token));

            await appEvents.LogAuditAsync(
                ModuleName,
                "SaveCatalogosMenuEnabled",
                "TA_CONFIGURACION",
                MenuEnabledConfigKey,
                "Configuración de visibilidad del menú de catálogos actualizada.",
                new { UserName = userName.Trim(), Habilitado = habilitado },
                token);

            return true;
        }, "No se pudo guardar la configuración del menú de catálogos.", ct);

    public Task<CatalogosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);

            var sql = $"""
                SELECT TOP (1)
                    ISNULL(VALOR, ''),
                    ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;

            var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(sql, new { Clave = configKey.ToUpperInvariant() }, cancellationToken: token));
            var raw = ResolveStoredValue(row.Valor, row.ValorAux);
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings();

            var parsed = JsonSerializer.Deserialize<CatalogosViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista de catálogos.", ct);

    public Task SaveViewSettingsAsync(string userName, CatalogosViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var stored = SplitStoredValue(serialized);
            var configKey = BuildViewConfigKey(userName);

            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET
                    VALOR = @Valor,
                    {detailColumn} = @ValorAux,
                    GRUPO = @Grupo,
                    FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @ClaveNormalizada;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION
                    (
                        CLAVE,
                        VALOR,
                        {detailColumn},
                        GRUPO,
                        FechaHora_Grabacion,
                        FechaHora_Modificacion
                    )
                    VALUES
                    (
                        @Clave,
                        @Valor,
                        @ValorAux,
                        @Grupo,
                        GETDATE(),
                        GETDATE()
                    );
                END;
                """;

            await cn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    ClaveNormalizada = configKey.ToUpperInvariant(),
                    Clave = configKey,
                    Valor = DbNullable(stored.Value, 150),
                    ValorAux = DbNullable(stored.AuxValue, 4000),
                    Grupo = ConfigGroup
                },
                cancellationToken: token));

            await appEvents.LogAuditAsync(
                ModuleName,
                "SaveViewSettings",
                "TA_CONFIGURACION",
                configKey,
                "Configuración de vista de catálogos actualizada.",
                new { UserName = userName.Trim(), normalized.AgruparPor, Columnas = normalized.Columnas },
                token);

            return true;
        }, "No se pudo guardar la configuración de vista.", ct);

    private async Task<CatalogosCatalogoDetalleDto?> GetCatalogoInternalAsync(int idInsert, bool soloPublico, CancellationToken ct)
        => await ExecuteLoggedAsync(ModuleName, soloPublico ? "GetCatalogoPublico" : "GetCatalogo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var where = soloPublico
                ? """
                  WHERE c.IDINSERT = @IdInsert
                    AND ISNULL(c.FINALIZADO, 0) = 0
                    AND (c.VigenciaDesde IS NULL OR c.VigenciaDesde <= GETDATE())
                    AND (c.VigenciaHasta IS NULL OR c.VigenciaHasta >= GETDATE())
                  """
                : "WHERE c.IDINSERT = @IdInsert";

            var sql = $"""
                SELECT
                    c.IDINSERT AS IdInsert,
                    CASE
                        WHEN MIN(c.VigenciaDesde) IS NULL
                         AND MAX(c.VigenciaHasta) IS NULL THEN N'Permanente'
                        ELSE N'Con vigencia'
                    END AS Tipo,
                    MAX(ISNULL(NULLIF(LTRIM(RTRIM(c.GRUPO)), ''), CONCAT(N'Catálogo ', CONVERT(nvarchar(20), c.IDINSERT)))) AS Nombre,
                    MAX(ISNULL(LTRIM(RTRIM(c.IDLISTA)), '')) AS IdLista,
                    MAX(ISNULL(LTRIM(RTRIM(c.GRUPO)), '')) AS Grupo,
                    MAX(ISNULL(LTRIM(RTRIM(c.Observaciones)), '')) AS Observaciones,
                    MIN(c.VigenciaDesde) AS VigenciaDesde,
                    MAX(c.VigenciaHasta) AS VigenciaHasta,
                    CASE WHEN MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Finalizado
                FROM dbo.V_MV_INSERT c
                {where}
                GROUP BY c.IDINSERT;

                SELECT
                    c.IDINSERT AS IdInsert,
                    c.FECHACARGA AS FechaCarga,
                    c.VigenciaDesde,
                    c.VigenciaHasta,
                    ISNULL(LTRIM(RTRIM(c.IDLISTA)), '') AS IdLista,
                    ISNULL(LTRIM(RTRIM(c.USUARIO)), '') AS Usuario,
                    ISNULL(LTRIM(RTRIM(c.GRUPO)), '') AS Grupo,
                    ISNULL(c.FINALIZADO, 0) AS Finalizado,
                    ISNULL(LTRIM(RTRIM(c.Observaciones)), '') AS Observaciones,
                    ISNULL(LTRIM(RTRIM(c.IDARTICULO)), '') AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(c.DescripcionArticulo)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(c.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(c.Marca)), '') AS Marca,
                    c.Precio,
                    c.PrecioOferta,
                    ISNULL(LTRIM(RTRIM(c.RUBRO)), '') AS Rubro
                FROM dbo.V_MV_INSERT c
                {where}
                ORDER BY c.IDARTICULO;
                """;

            using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { IdInsert = idInsert }, cancellationToken: token));
            var header = await multi.ReadSingleOrDefaultAsync<CatalogosCatalogoDetalleDto>();
            if (header is null)
                return null;

            var items = (await multi.ReadAsync<CatalogosCatalogoItemDto>()).ToList();
            header.Articulos = items;
            return header;
        }, soloPublico ? "No se pudo cargar el catálogo público." : "No se pudo cargar el catálogo.", ct);

    private static CatalogosViewSettingsDto CreateDefaultViewSettings()
        => new()
        {
            AgruparPor = CatalogosViewGroupKeys.None,
            Columnas =
            [
                new() { Key = CatalogosViewColumnKeys.Id, Label = "ID", Visible = true, Order = 0 },
                new() { Key = CatalogosViewColumnKeys.Tipo, Label = "Tipo", Visible = true, Order = 1 },
                new() { Key = CatalogosViewColumnKeys.Nombre, Label = "Nombre", Visible = true, Order = 2 },
                new() { Key = CatalogosViewColumnKeys.Vigencia, Label = "Vigencia", Visible = true, Order = 3 },
                new() { Key = CatalogosViewColumnKeys.Estado, Label = "Estado", Visible = true, Order = 4 }
            ]
        };

    private static CatalogosViewSettingsDto NormalizeViewSettings(CatalogosViewSettingsDto? settings)
    {
        var result = settings ?? CreateDefaultViewSettings();
        result.AgruparPor = string.IsNullOrWhiteSpace(result.AgruparPor) ? CatalogosViewGroupKeys.None : result.AgruparPor;
        result.Columnas = (result.Columnas ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new CatalogosViewColumnDto { Key = c.Key.Trim(), Label = string.IsNullOrWhiteSpace(c.Label) ? c.Key.Trim() : c.Label.Trim(), Visible = c.Visible, Order = c.Order })
            .OrderBy(c => c.Order)
            .ToList();

        if (result.Columnas.Count == 0)
            result = CreateDefaultViewSettings();
        else if (!result.Columnas.Any(c => c.Visible))
            result.Columnas[0].Visible = true;

        return result;
    }

    private static string BuildViewConfigKey(string userName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));
        return $"{ViewConfigPrefix}{hash[..24]}";
    }

    private static string BuildPublicUrl(int idInsert)
        => $"/catalogo/{idInsert}";

    private static string ResolveStoredValue(string valor, string valorAux)
        => !string.IsNullOrWhiteSpace(valor)
            ? valor
            : valorAux;

    private static (string Value, string AuxValue) SplitStoredValue(string value)
        => value.Length <= 150
            ? (value, string.Empty)
            : (string.Empty, value);

    private static object DbNullable(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : Truncate(value.Trim(), maxLength);

    private static string Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Length <= maxLength ? value.Trim() : value.Trim()[..maxLength];

    private static string LikeContains(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return string.Empty;

        return $"%{normalized.ToUpperInvariant()}%";
    }

    private static PagedResult<CatalogosArticuloBusquedaDto> EmptyArticuloPage(int pageNumber, int pageSize)
        => new()
        {
            Items = [],
            Total = 0,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

    private async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN OBJECT_ID(@ObjectName, 'U') IS NOT NULL THEN 1
                WHEN OBJECT_ID(@ObjectName, 'V') IS NOT NULL THEN 1
                ELSE 0
            END;
            """;

        var exists = await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { ObjectName = $"dbo.{objectName}" }, cancellationToken: ct));
        return exists == 1;
    }

    private async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string auxColumnSql = """
            SELECT CASE
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALOR_AUX') IS NOT NULL THEN N'VALOR_AUX'
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALORAUX') IS NOT NULL THEN N'VALORAUX'
                ELSE N'VALOR_AUX'
            END;
            """;

        var column = await cn.ExecuteScalarAsync<string>(new CommandDefinition(auxColumnSql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(column) ? "VALOR_AUX" : column;
    }

    private async Task<T> ExecuteLoggedAsync<T>(
        string module,
        string action,
        Func<CancellationToken, Task<T>> operation,
        string friendlyMessage,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                module,
                action,
                ex,
                friendlyMessage,
                new
                {
                    Usuario = appUserSession.GetCurrentUserName(Environment.UserName),
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException($"{friendlyMessage} Código: {incidentId}", incidentId, ex);
        }
    }
}

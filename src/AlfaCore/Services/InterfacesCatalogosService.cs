using AlfaCore.Models;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore.Services;

public sealed class InterfacesCatalogosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IWebHostEnvironment environment,
    IAppEventService appEvents,
    IArticuloImagenFtpService articuloImagenFtpService,
    IPuntoVentaService puntoVentaService,
    CatalogoPedidoProcessingGuard pedidoProcessingGuard) : IInterfacesCatalogosService
{
    private const string ModuleName = "Interfaces";
    private const string ConfigGroup = "CATALOGOS";
    private const string MenuEnabledConfigKey = "CATALOGOS-MENU-HABILITADO";
    private const string PublicNameConfigKey = "CATALOGOS-NOMBRE-PUBLICO";
    private const string PublicLogoFormatConfigKey = "CATALOGOS-LOGO-FORMATO";
    private const string PublicClasePrecioConfigKey = "CATALOGOS-CLASE-PRECIO";
    private const string DefaultClasePrecio = "1";
    private const string OfertaClasePrecioConfigKey = "CLASEPRECIOOFERTA";
    private const string CarritoHabilitadoConfigKeyPrefix = "CATALOGOS-CARRITO";
    private const string PredeterminadoConfigKey = "CATALOGOS-PREDETERMINADO";
    private const string TcPedidoWeb = "NP";
    private const string SucursalPedidoWeb = "9999";
    private const string LetraPedidoWebDefault = "X";
    private const string DefaultPublicLogoUrl = "/logos/Logo.png";
    private const string LogoFtpArticuloKey = "Logo";
    private const string ViewConfigPrefix = "USUVIEW-CATALOGOS-";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

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
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 50));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;
            var textoLike = LikeContains(filters.Texto);
            var idLista = (filters.IdLista ?? string.Empty).Trim();
            var idRubro = (filters.IdRubro ?? string.Empty).Trim();
            var idFamilia = (filters.IdFamilia ?? string.Empty).Trim();
            var idTipo = (filters.IdTipo ?? string.Empty).Trim();
            var idProveedor = (filters.IdProveedor ?? string.Empty).Trim();
            var origen = (filters.Origen ?? string.Empty).Trim();
            var usarLista = string.Equals(origen, CatalogosArticuloOrigenKeys.ListaPrecio, StringComparison.OrdinalIgnoreCase);
            var clasePrecio = ParseClasePrecio(await GetPublicClasePrecioAsync(filters.IdWeb, token));
            // IDARTICULO en V_MA_ARTICULOS viene con padding (algunos códigos numéricos quedan
            // con espacios a la izquierda) — hay que normalizar en mayúsculas + trim para que la
            // exclusión matchee igual que el resto de la consulta (LTRIM/RTRIM en SELECT/ORDER BY).
            var excludedIds = (filters.ExcludedIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
                return EmptyArticuloPage(pageNumber, pageSize);

            if (usarLista && string.IsNullOrWhiteSpace(idLista))
                return EmptyArticuloPage(pageNumber, pageSize);

            if (usarLista && !await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return EmptyArticuloPage(pageNumber, pageSize);

            var ofertaClase = usarLista ? await GetOfertaClasePrecioAsync(cn, token) : 0;
            var tieneFamilias = await SqlObjectExistsAsync(cn, "V_TA_FAMILIAS", token);
            var familiaJoin = tieneFamilias
                ? "LEFT JOIN dbo.V_TA_FAMILIAS f ON LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))) = LTRIM(RTRIM(f.IdFamilia))"
                : string.Empty;
            var familiaSelect = tieneFamilias ? "ISNULL(LTRIM(RTRIM(f.Descripcion)), '')" : "''";
            // Solo se agrega el fragmento si realmente hay IDs a excluir: "NOT IN ()" es inválido en
            // SQL Server, y no tiene sentido pagar el costo del filtro cuando la lista viene vacía
            // (catálogo/carrito recién creado, sin artículos todavía).
            var exclusionFilter = excludedIds.Count > 0
                ? "AND UPPER(LTRIM(RTRIM(a.IDARTICULO))) NOT IN @ExcludedIds"
                : string.Empty;

            var sql = usarLista
                ? $"""
                SELECT
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, ''))), ''),
                        N''
                    ) AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), '') AS RutaImagen,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    {familiaSelect} AS Familia,
                    ISNULL(LTRIM(RTRIM(p.IdLista)), '') AS ListaPrecio,
                    N'' AS NombreListaPrecio,
                    ISNULL(p.Precio{clasePrecio}, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND GETDATE() >= p.FhOfertaDesde
                         AND (p.FhOfertaHasta IS NULL OR GETDATE() <= p.FhOfertaHasta) THEN p.Precio{ofertaClase}
                        ELSE NULL
                    END AS PrecioOferta,
                    COUNT(1) OVER() AS TotalRows
                FROM dbo.V_MA_Precios p
                INNER JOIN dbo.V_MA_ARTICULOS a
                    ON a.IDARTICULO = p.IdArticulo
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {familiaJoin}
                WHERE UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, '')))) = UPPER(LTRIM(RTRIM(@IdLista)))
                  AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, 'V')))) = 'V'
                  AND ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND (@IdRubro = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, '')))) = UPPER(@IdRubro))
                  AND (@IdFamilia = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IdFamilia, '')))) = UPPER(@IdFamilia))
                  AND (@IdTipo = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, '')))) = UPPER(@IdTipo))
                  AND (@IdProveedor = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, '')))) = UPPER(@IdProveedor))
                  AND (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                      )
                  {exclusionFilter}
                ORDER BY a.DESCRIPCION, a.IDARTICULO
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """
                : $"""
                SELECT
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, ''))), ''),
                        N''
                    ) AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), '') AS RutaImagen,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    {familiaSelect} AS Familia,
                    N'' AS ListaPrecio,
                    N'' AS NombreListaPrecio,
                    ISNULL(a.Precio{clasePrecio}, 0) AS Precio,
                    CAST(NULL AS decimal(18, 4)) AS PrecioOferta,
                    COUNT(1) OVER() AS TotalRows
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {familiaJoin}
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND (@IdRubro = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, '')))) = UPPER(@IdRubro))
                  AND (@IdFamilia = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IdFamilia, '')))) = UPPER(@IdFamilia))
                  AND (@IdTipo = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, '')))) = UPPER(@IdTipo))
                  AND (@IdProveedor = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, '')))) = UPPER(@IdProveedor))
                  AND (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                      )
                  {exclusionFilter}
                ORDER BY a.DESCRIPCION, a.IDARTICULO
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            var rows = (await cn.QueryAsync<CatalogosArticuloBusquedaPageRowDto>(new CommandDefinition(
                sql,
                new { IdLista = idLista, IdRubro = idRubro, IdFamilia = idFamilia, IdTipo = idTipo, IdProveedor = idProveedor, TextoLike = textoLike, ExcludedIds = excludedIds, Skip = skip, PageSize = pageSize },
                cancellationToken: token))).ToList();

            var total = rows.FirstOrDefault()?.TotalRows ?? 0;
            var items = rows.Select(row => new CatalogosArticuloBusquedaDto
            {
                IdArticulo = row.IdArticulo,
                DescripcionArticulo = row.DescripcionArticulo,
                CodigoBarra = row.CodigoBarra,
                RutaImagen = row.RutaImagen,
                Presentacion = row.Presentacion,
                Marca = row.Marca,
                Rubro = row.Rubro,
                Familia = row.Familia,
                ListaPrecio = row.ListaPrecio,
                NombreListaPrecio = row.NombreListaPrecio,
                Precio = row.Precio,
                PrecioOferta = row.PrecioOferta
            }).ToList();

            return new PagedResult<CatalogosArticuloBusquedaDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron buscar los artículos.", ct);

    public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetRubrosArticuloAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetRubrosArticulo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_TA_Rubros", token))
                return (IReadOnlyList<CatalogosClasificacionOpcionDto>)Array.Empty<CatalogosClasificacionOpcionDto>();

            var rows = await cn.QueryAsync<CatalogosClasificacionOpcionDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(IdRubro)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_Rubros
                ORDER BY Descripcion, IdRubro;
                """,
                cancellationToken: token));

            return (IReadOnlyList<CatalogosClasificacionOpcionDto>)rows.ToList();
        }, "No se pudieron cargar los rubros.", ct);

    public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetFamiliasArticuloAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetFamiliasArticulo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_TA_FAMILIAS", token))
                return (IReadOnlyList<CatalogosClasificacionOpcionDto>)Array.Empty<CatalogosClasificacionOpcionDto>();

            var rows = await cn.QueryAsync<CatalogosClasificacionOpcionDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(IdFamilia)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_FAMILIAS
                ORDER BY Descripcion, IdFamilia;
                """,
                cancellationToken: token));

            return (IReadOnlyList<CatalogosClasificacionOpcionDto>)rows.ToList();
        }, "No se pudieron cargar las familias.", ct);

    public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetMarcasArticuloAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetMarcasArticulo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_TA_TipoArticulo", token))
                return (IReadOnlyList<CatalogosClasificacionOpcionDto>)Array.Empty<CatalogosClasificacionOpcionDto>();

            var rows = await cn.QueryAsync<CatalogosClasificacionOpcionDto>(new CommandDefinition(
                """
                SELECT
                    LTRIM(RTRIM(IdTipo)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(Descripcion)), '') AS Descripcion
                FROM dbo.V_TA_TipoArticulo
                ORDER BY Descripcion, IdTipo;
                """,
                cancellationToken: token));

            return (IReadOnlyList<CatalogosClasificacionOpcionDto>)rows.ToList();
        }, "No se pudieron cargar las marcas.", ct);

    // Fuente oficial de proveedores confirmada en docs/DATABASE_TABLES_SUMMARY.md: Vt_Proveedores
    // (no consultar el plan de cuentas base). El combo se limita a los proveedores que efectivamente
    // tienen artículos vía V_MA_ARTICULOS.CUENTAPROVEEDOR, igual que Rubro/Familia/Marca.
    public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetProveedoresArticuloAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetProveedoresArticulo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token) || !await SqlObjectExistsAsync(cn, "Vt_Proveedores", token))
                return (IReadOnlyList<CatalogosClasificacionOpcionDto>)Array.Empty<CatalogosClasificacionOpcionDto>();

            var rows = await cn.QueryAsync<CatalogosClasificacionOpcionDto>(new CommandDefinition(
                """
                SELECT DISTINCT
                    LTRIM(RTRIM(v.CODIGO)) AS Codigo,
                    ISNULL(NULLIF(LTRIM(RTRIM(v.RAZON_SOCIAL)), ''), LTRIM(RTRIM(v.CODIGO))) AS Descripcion
                FROM dbo.V_MA_ARTICULOS a
                INNER JOIN dbo.Vt_Proveedores v
                    ON LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, ''))) = LTRIM(RTRIM(v.CODIGO))
                WHERE ISNULL(a.Suspendido, 0) <> 1
                  AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, ''))) <> ''
                ORDER BY Descripcion, Codigo;
                """,
                cancellationToken: token));

            return (IReadOnlyList<CatalogosClasificacionOpcionDto>)rows.ToList();
        }, "No se pudieron cargar los proveedores.", ct);

    // Variante sin paginar de SearchArticulosAsync: mismo WHERE/joins, reutilizada tanto por
    // "Importar todo" (Catálogo, sin filtros de clasificación) como por "Seleccionar todos los
    // resultados" (ArticuloPickerDialog, con los filtros activos del modal). Devuelve como máximo
    // MaxArticulosBatch filas para no cargar un resultado descontrolado en memoria; CountArticulosAllAsync
    // permite avisar antes si el conjunto excede ese límite.
    private const int MaxArticulosBatch = 20000;

    public Task<int> CountArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "CountArticulosAll", async token =>
        {
            filters ??= new CatalogosArticuloBusquedaFiltersDto();
            var (whereSql, usarLista, parameters) = BuildArticulosWhere(filters);
            if (whereSql is null)
                return 0;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
                return 0;

            if (usarLista && !await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return 0;

            const string rubroTipoJoins = """
                LEFT JOIN dbo.V_TA_Rubros r ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                """;
            var from = usarLista
                ? $"FROM dbo.V_MA_Precios p INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = p.IdArticulo {rubroTipoJoins}"
                : $"FROM dbo.V_MA_ARTICULOS a {rubroTipoJoins}";

            var sql = $"SELECT COUNT(1) {from} WHERE {whereSql}";
            return await cn.ExecuteScalarAsync<int>(new CommandDefinition(sql, ToDynamicParameters(parameters), cancellationToken: token));
        }, "No se pudo contar los artículos.", ct);

    public Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> SearchArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchArticulosAll", async token =>
        {
            filters ??= new CatalogosArticuloBusquedaFiltersDto();
            var (whereSql, usarLista, parameters) = BuildArticulosWhere(filters);
            if (whereSql is null)
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            if (usarLista && !await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            var clasePrecio = ParseClasePrecio(await GetPublicClasePrecioAsync(filters.IdWeb, token));
            var ofertaClase = usarLista ? await GetOfertaClasePrecioAsync(cn, token) : 0;
            var tieneFamilias = await SqlObjectExistsAsync(cn, "V_TA_FAMILIAS", token);
            var familiaJoin = tieneFamilias
                ? "LEFT JOIN dbo.V_TA_FAMILIAS f ON LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))) = LTRIM(RTRIM(f.IdFamilia))"
                : string.Empty;
            var familiaSelect = tieneFamilias ? "ISNULL(LTRIM(RTRIM(f.Descripcion)), '')" : "''";

            var sql = usarLista
                ? $"""
                SELECT TOP (@MaxRows)
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    {familiaSelect} AS Familia,
                    ISNULL(LTRIM(RTRIM(p.IdLista)), '') AS ListaPrecio,
                    N'' AS NombreListaPrecio,
                    ISNULL(p.Precio{clasePrecio}, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND GETDATE() >= p.FhOfertaDesde
                         AND (p.FhOfertaHasta IS NULL OR GETDATE() <= p.FhOfertaHasta) THEN p.Precio{ofertaClase}
                        ELSE NULL
                    END AS PrecioOferta
                FROM dbo.V_MA_Precios p
                INNER JOIN dbo.V_MA_ARTICULOS a
                    ON a.IDARTICULO = p.IdArticulo
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {familiaJoin}
                WHERE {whereSql}
                ORDER BY a.DESCRIPCION, a.IDARTICULO;
                """
                : $"""
                SELECT TOP (@MaxRows)
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    {familiaSelect} AS Familia,
                    N'' AS ListaPrecio,
                    N'' AS NombreListaPrecio,
                    ISNULL(a.Precio{clasePrecio}, 0) AS Precio,
                    CAST(NULL AS decimal(18, 4)) AS PrecioOferta
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.V_TA_Rubros r
                    ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                {familiaJoin}
                WHERE {whereSql}
                ORDER BY a.DESCRIPCION, a.IDARTICULO;
                """;

            var dapperParams = ToDynamicParameters(parameters);
            dapperParams.Add("MaxRows", MaxArticulosBatch);

            var items = await cn.QueryAsync<CatalogosArticuloBusquedaDto>(new CommandDefinition(sql, dapperParams, cancellationToken: token));
            return (IReadOnlyList<CatalogosArticuloBusquedaDto>)items.ToList();
        }, "No se pudieron obtener los artículos.", ct);

    // Arma el WHERE compartido por CountArticulosAllAsync/SearchArticulosAllAsync (mismas reglas que
    // SearchArticulosAsync: suspendido, rubro/familia/marca/proveedor, texto y exclusión de ya agregados).
    // Devuelve whereSql = null cuando el origen es lista y no hay IdLista (no hay nada para traer).
    private static (string? WhereSql, bool UsarLista, Dictionary<string, object> Parameters) BuildArticulosWhere(CatalogosArticuloBusquedaFiltersDto filters)
    {
        var idLista = (filters.IdLista ?? string.Empty).Trim();
        var idRubro = (filters.IdRubro ?? string.Empty).Trim();
        var idFamilia = (filters.IdFamilia ?? string.Empty).Trim();
        var idTipo = (filters.IdTipo ?? string.Empty).Trim();
        var idProveedor = (filters.IdProveedor ?? string.Empty).Trim();
        var textoLike = LikeContains(filters.Texto);
        var origen = (filters.Origen ?? string.Empty).Trim();
        var usarLista = string.Equals(origen, CatalogosArticuloOrigenKeys.ListaPrecio, StringComparison.OrdinalIgnoreCase);

        if (usarLista && string.IsNullOrWhiteSpace(idLista))
            return (null, usarLista, []);

        var excludedIds = (filters.ExcludedIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var exclusionFilter = excludedIds.Count > 0
            ? "AND UPPER(LTRIM(RTRIM(a.IDARTICULO))) NOT IN @ExcludedIds"
            : string.Empty;

        var parameters = new Dictionary<string, object>
        {
            ["IdRubro"] = idRubro,
            ["IdFamilia"] = idFamilia,
            ["IdTipo"] = idTipo,
            ["IdProveedor"] = idProveedor,
            ["TextoLike"] = textoLike,
            ["ExcludedIds"] = excludedIds
        };

        var whereSql = usarLista
            ? $"""
              UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, '')))) = UPPER(LTRIM(RTRIM(@IdLista)))
                AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, 'V')))) = 'V'
                AND ISNULL(a.Suspendido, 0) <> 1
                AND ISNULL(a.SuspendidoV, 0) <> 1
                AND (@IdRubro = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, '')))) = UPPER(@IdRubro))
                AND (@IdFamilia = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IdFamilia, '')))) = UPPER(@IdFamilia))
                AND (@IdTipo = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, '')))) = UPPER(@IdTipo))
                AND (@IdProveedor = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, '')))) = UPPER(@IdProveedor))
                AND (
                      @TextoLike = ''
                      OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                    )
                {exclusionFilter}
              """
            : $"""
              ISNULL(a.Suspendido, 0) <> 1
                AND ISNULL(a.SuspendidoV, 0) <> 1
                AND (@IdRubro = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, '')))) = UPPER(@IdRubro))
                AND (@IdFamilia = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IdFamilia, '')))) = UPPER(@IdFamilia))
                AND (@IdTipo = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, '')))) = UPPER(@IdTipo))
                AND (@IdProveedor = '' OR UPPER(LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, '')))) = UPPER(@IdProveedor))
                AND (
                      @TextoLike = ''
                      OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                      OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                    )
                {exclusionFilter}
              """;

        if (usarLista)
            parameters["IdLista"] = idLista;

        return (whereSql, usarLista, parameters);
    }

    // DynamicParameters no tiene un constructor propio a partir de un Dictionary<string, object>
    // que garantice el mismo comportamiento de expansión de listas ("NOT IN @ExcludedIds") que ya
    // usan los métodos con objetos anónimos: se arma explícitamente con Add() por entrada.
    private static DynamicParameters ToDynamicParameters(Dictionary<string, object> source)
    {
        var parameters = new DynamicParameters();
        foreach (var (key, value) in source)
            parameters.Add(key, value);

        return parameters;
    }

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

    public Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, string? idWeb = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetArticulosDesdeLista", async token =>
        {
            var lista = (idLista ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(lista))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token) || !await SqlObjectExistsAsync(cn, "V_MA_Precios", token))
                return Array.Empty<CatalogosArticuloBusquedaDto>();

            var clasePrecio = ParseClasePrecio(await GetPublicClasePrecioAsync(idWeb, token));
            var ofertaClase = await GetOfertaClasePrecioAsync(cn, token);

            var sql = $"""
                SELECT
                    a.IDARTICULO AS IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS DescripcionArticulo,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                    ISNULL(LTRIM(RTRIM(p.IdLista)), '') AS ListaPrecio,
                    ISNULL(LTRIM(RTRIM(pc.Nombre)), '') AS NombreListaPrecio,
                    ISNULL(p.Precio{clasePrecio}, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND GETDATE() >= p.FhOfertaDesde
                         AND (p.FhOfertaHasta IS NULL OR GETDATE() <= p.FhOfertaHasta) THEN p.Precio{ofertaClase}
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

    public Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, string? tipoFiltro = null, string? estadoFiltro = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SearchCatalogos", async token =>
        {
            var normalizedText = LikeContains(texto);
            var normalizedTipo = NormalizeCatalogFilter(tipoFiltro);
            var normalizedEstado = NormalizeCatalogFilter(estadoFiltro);
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
                GROUP BY c.IDINSERT
                HAVING (
                        @TextoLike = ''
                        OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.GRUPO, ''))))) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.Observaciones, ''))))) LIKE @TextoLike
                        OR CONVERT(nvarchar(20), c.IDINSERT) LIKE @TextoLike
                        OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.IDLISTA, ''))))) LIKE @TextoLike
                      )
                  AND (
                        @TipoFiltro = N'todos'
                        OR (@TipoFiltro = N'predeterminado' AND MIN(c.VigenciaDesde) IS NULL AND MAX(c.VigenciaHasta) IS NULL)
                        OR (@TipoFiltro = N'vigencia' AND (MIN(c.VigenciaDesde) IS NOT NULL OR MAX(c.VigenciaHasta) IS NOT NULL))
                      )
                  AND (
                        @EstadoFiltro = N'todos'
                        OR (@EstadoFiltro = N'publicado' AND MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 0)
                        OR (@EstadoFiltro = N'finalizado' AND MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 1)
                      )
                  AND (
                        @EstadoFiltro = N'finalizado'
                        OR (
                            MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 0
                            AND (MIN(c.VigenciaDesde) IS NULL OR CONVERT(date, MIN(c.VigenciaDesde)) <= @FechaFiltro)
                            AND (MAX(c.VigenciaHasta) IS NULL OR CONVERT(date, MAX(c.VigenciaHasta)) >= @FechaFiltro)
                        )
                      )
                ORDER BY MAX(c.FECHACARGA) DESC, c.IDINSERT DESC
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            var items = (await cn.QueryAsync<CatalogosCatalogoResumenDto>(new CommandDefinition(
                sql,
                new { TextoLike = normalizedText, TipoFiltro = normalizedTipo, EstadoFiltro = normalizedEstado, FechaFiltro = fecha, Skip = skip, PageSize = pageSize },
                cancellationToken: token))).ToList();

            var predeterminadoId = await GetCatalogoPredeterminadoIdInternalAsync(cn, token);
            if (predeterminadoId > 0)
            {
                foreach (var item in items)
                    item.Predeterminado = item.IdInsert == predeterminadoId;
            }

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM (
                    SELECT c.IDINSERT
                    FROM dbo.V_MV_INSERT c
                    GROUP BY c.IDINSERT
                    HAVING (
                            @TextoLike = ''
                            OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.GRUPO, ''))))) LIKE @TextoLike
                            OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.Observaciones, ''))))) LIKE @TextoLike
                            OR CONVERT(nvarchar(20), c.IDINSERT) LIKE @TextoLike
                            OR UPPER(LTRIM(RTRIM(MAX(ISNULL(c.IDLISTA, ''))))) LIKE @TextoLike
                          )
                      AND (
                            @TipoFiltro = N'todos'
                            OR (@TipoFiltro = N'predeterminado' AND MIN(c.VigenciaDesde) IS NULL AND MAX(c.VigenciaHasta) IS NULL)
                            OR (@TipoFiltro = N'vigencia' AND (MIN(c.VigenciaDesde) IS NOT NULL OR MAX(c.VigenciaHasta) IS NOT NULL))
                          )
                      AND (
                            @EstadoFiltro = N'todos'
                            OR (@EstadoFiltro = N'publicado' AND MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 0)
                            OR (@EstadoFiltro = N'finalizado' AND MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 1)
                          )
                      AND (
                            @EstadoFiltro = N'finalizado'
                            OR (
                                MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 0
                                AND (MIN(c.VigenciaDesde) IS NULL OR CONVERT(date, MIN(c.VigenciaDesde)) <= @FechaFiltro)
                                AND (MAX(c.VigenciaHasta) IS NULL OR CONVERT(date, MAX(c.VigenciaHasta)) >= @FechaFiltro)
                            )
                          )
                ) x;
                """,
                new { TextoLike = normalizedText, TipoFiltro = normalizedTipo, EstadoFiltro = normalizedEstado, FechaFiltro = fecha },
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

            // IdLista es solo metadata descriptiva en V_MV_INSERT (nvarchar(4), sin FK): un catálogo
            // con origen Maestro de artículos la deja vacía a propósito (así es como GetCatalogoAsync
            // ya infiere el origen al reabrir para editar). No exigirla acá, o "Maestro" nunca podría
            // publicarse.
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

                await SetCarritoHabilitadoAsync(cn, idInsert, request.HabilitarCarrito, token);

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

    public Task<CatalogosCatalogoAccessUrlsDto> GetCatalogoAccessUrlsAsync(int idInsert, string? idWeb = null, int? idBase = null, CancellationToken ct = default)
        => Task.FromResult(new CatalogosCatalogoAccessUrlsDto
        {
            UrlPublica = BuildPublicUrl(idInsert, idWeb, idBase)
        });

    public async Task<int> GetCatalogoPredeterminadoIdAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return await GetCatalogoPredeterminadoIdInternalAsync(cn, ct);
    }

    public Task SetCatalogoPredeterminadoAsync(string userName, int idInsert, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SetCatalogoPredeterminado", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para marcar el catálogo predeterminado.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (idInsert > 0)
            {
                var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.V_MV_INSERT WHERE IDINSERT = @IdInsert;",
                    new { IdInsert = idInsert },
                    cancellationToken: token));

                if (existe == 0)
                    throw new InvalidOperationException("El catálogo indicado no existe.");
            }

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var valor = idInsert > 0 ? idInsert.ToString() : string.Empty;
            await UpsertConfigValueAsync(cn, detailColumn, PredeterminadoConfigKey, valor, ConfigGroup, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SetCatalogoPredeterminado",
                "TA_CONFIGURACION",
                PredeterminadoConfigKey,
                idInsert > 0 ? $"Catálogo #{idInsert} marcado como predeterminado (accesible vía /catalogo/0)." : "Se quitó el catálogo predeterminado.",
                new { UserName = userName.Trim(), IdInsert = idInsert },
                token);

            return true;
        }, "No se pudo actualizar el catálogo predeterminado.", ct);

    private async Task<int> GetCatalogoPredeterminadoIdInternalAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct))
            return 0;

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(
            sql,
            new { Clave = PredeterminadoConfigKey },
            cancellationToken: ct));

        var raw = ResolveStoredValue(row.Valor ?? string.Empty, row.ValorAux ?? string.Empty);
        return int.TryParse(raw.Trim(), out var idInsert) && idInsert > 0 ? idInsert : 0;
    }

    private async Task<int> GetCatalogoGeneralFallbackIdInternalAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                c.IDINSERT
            FROM dbo.V_MV_INSERT c
            GROUP BY c.IDINSERT
            HAVING
                MAX(CAST(ISNULL(c.FINALIZADO, 0) AS int)) = 0
                AND (MIN(c.VigenciaDesde) IS NULL OR CONVERT(date, MIN(c.VigenciaDesde)) <= CONVERT(date, GETDATE()))
                AND (MAX(c.VigenciaHasta) IS NULL OR CONVERT(date, MAX(c.VigenciaHasta)) >= CONVERT(date, GETDATE()))
            ORDER BY MAX(c.FECHACARGA) DESC, c.IDINSERT DESC;
            """;

        var idInsert = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, cancellationToken: ct));
        return idInsert.GetValueOrDefault();
    }

    public Task<CatalogosClienteSessionInfo> LoginClienteAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default)
        => ExecuteCatalogoClienteLoginAsync(request, ct);

    private sealed record CabeceraPedidoWeb(int IdComprobante, string IdComprobanteTexto, string Numero, DateTime Fecha);

    public async Task<CatalogoPedidoResultDto> ConfirmarPedidoCarritoAsync(CatalogoPedidoConfirmarRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codigoClienteSesion = (request.CodigoCliente ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigoClienteSesion))
            throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

        var lineasSolicitadas = (request.Lineas ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.IdArticulo) && l.Cantidad > 0)
            .GroupBy(l => l.IdArticulo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogoPedidoLineaSolicitudDto { IdArticulo = g.Key, Cantidad = g.Sum(x => x.Cantidad) })
            .ToList();

        if (lineasSolicitadas.Count == 0)
            throw new InvalidOperationException("El carrito está vacío.");

        if (!pedidoProcessingGuard.TryStart(request.IdInsert, codigoClienteSesion))
            throw new InvalidOperationException("Ya hay un pedido en proceso para este carrito. Esperá un momento y verificá antes de reintentar.");

        try
        {
            // 1) Catálogo: existe, publicado/vigente y con carrito habilitado. Nunca confío en
            //    lo que venga del navegador para esto, siempre releo el catálogo real.
            var catalogo = await GetCatalogoPublicoAsync(request.IdInsert, ct);
            if (catalogo is null)
                throw new InvalidOperationException("El catálogo no está disponible, no está publicado o venció su vigencia.");

            if (!catalogo.HabilitarCarrito)
                throw new InvalidOperationException("Este catálogo no tiene habilitada la toma de pedidos.");

            // 2) Cada línea debe corresponder a un artículo real de ESTE catálogo, con el precio
            //    exactamente como está publicado ahí (nunca se vuelve a calcular ni se reemplaza
            //    por el precio vigente del maestro).
            var articulosPorCodigo = catalogo.Articulos.ToDictionary(a => a.IdArticulo.Trim(), a => a, StringComparer.OrdinalIgnoreCase);
            var lineasResueltas = new List<(CatalogosCatalogoItemDto Articulo, decimal Cantidad)>();
            foreach (var linea in lineasSolicitadas)
            {
                if (!articulosPorCodigo.TryGetValue(linea.IdArticulo, out var articulo))
                    throw new InvalidOperationException($"El artículo {linea.IdArticulo} no pertenece a este catálogo.");

                if (CatalogosPriceDisplayHelper.GetPrecioAplicado(articulo) <= 0m)
                    throw new InvalidOperationException($"El artículo {linea.IdArticulo} no tiene un precio válido en este catálogo.");

                lineasResueltas.Add((articulo, linea.Cantidad));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            // 3) Revalido al cliente contra la fuente oficial: no confío únicamente en la sesión.
            var cliente = await cn.QuerySingleOrDefaultAsync<(string? Codigo, string RazonSocial, string Email)>(new CommandDefinition(
                """
                SELECT TOP (1)
                    LTRIM(RTRIM(CODIGO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), '') AS RazonSocial,
                    ISNULL(LTRIM(RTRIM(MAIL)), '') AS Email
                FROM dbo.VT_CLIENTES
                WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)))
                  AND ISNULL(Dada_De_Baja, 0) = 0;
                """,
                new { Codigo = codigoClienteSesion },
                cancellationToken: ct));

            if (cliente.Codigo is null)
                throw new InvalidOperationException("No pudimos validar tu cuenta de cliente. Volvé a iniciar sesión e intentá nuevamente.");

            var letra = await ResolveLetraPedidoWebAsync(cn, ct);
            var observaciones = $"Pedido web - Catálogo #{request.IdInsert}";
            if (observaciones.Length > 250)
                observaciones = observaciones[..250];

            // 4) Transacción con numeración protegida. La numeración legacy (MAX(NUMERO)+1 dentro
            //    de sp_web_Alta_Comprobante) no tiene ningún candado propio, y confirmamos con datos
            //    reales que SUCURSAL=9999/NP/X ya la está escribiendo otro proceso en este momento.
            //    sp_getapplock protege la concurrencia dentro de AlfaCore; si igual choca con ese
            //    otro proceso (violación de clave), reintentamos con una transacción nueva.
            CabeceraPedidoWeb? cabecera = null;
            const int maxIntentos = 5;

            for (var intento = 1; intento <= maxIntentos; intento++)
            {
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
                try
                {
                    await using (var lockCmd = new SqlCommand(
                        "EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;",
                        cn, tx))
                    {
                        lockCmd.Parameters.AddWithValue("@Resource", $"ALFACORE-V_MV_CPTE-{TcPedidoWeb}-{SucursalPedidoWeb}-{letra}");
                        await lockCmd.ExecuteNonQueryAsync(ct);
                    }

                    cabecera = await CrearCabeceraPedidoWebAsync(cn, tx, cliente.Codigo, letra, observaciones, ct);

                    foreach (var (articulo, cantidad) in lineasResueltas)
                        await AgregarLineaPedidoWebAsync(cn, tx, cabecera.IdComprobante, articulo, cantidad, ct);

                    await tx.CommitAsync(ct);
                    break;
                }
                catch (Exception ex) when (intento < maxIntentos && EsPosibleColisionDeNumeracion(ex))
                {
                    try { await tx.RollbackAsync(ct); } catch { }
                    cabecera = null;
                }
                catch
                {
                    try { await tx.RollbackAsync(ct); } catch { }
                    throw;
                }
            }

            if (cabecera is null)
                throw new InvalidOperationException("No se pudo registrar el pedido por demasiados intentos simultáneos. Esperá unos segundos y volvé a intentar.");

            var lineasResultado = lineasResueltas
                .Select(l => new CatalogoPedidoLineaResultDto
                {
                    IdArticulo = l.Articulo.IdArticulo,
                    Descripcion = l.Articulo.DescripcionArticulo,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = CatalogosPriceDisplayHelper.GetPrecioAplicado(l.Articulo),
                    Subtotal = l.Cantidad * CatalogosPriceDisplayHelper.GetPrecioAplicado(l.Articulo)
                })
                .ToList();

            var resultado = new CatalogoPedidoResultDto
            {
                Tc = TcPedidoWeb,
                Sucursal = SucursalPedidoWeb,
                Numero = cabecera.Numero,
                Letra = letra,
                IdComprobanteTexto = cabecera.IdComprobanteTexto,
                IdComprobante = cabecera.IdComprobante,
                Fecha = cabecera.Fecha,
                CodigoCliente = cliente.Codigo,
                RazonSocial = cliente.RazonSocial,
                Email = cliente.Email,
                Total = lineasResultado.Sum(l => l.Subtotal),
                Lineas = lineasResultado
            };

            await appEvents.LogAuditAsync(
                ModuleName,
                "ConfirmarPedidoCarrito",
                "V_MV_Cpte",
                resultado.IdComprobanteTexto,
                "Pedido NP generado desde el carrito de catálogos.",
                new
                {
                    request.IdInsert,
                    request.IdWeb,
                    request.IdBase,
                    CodigoCliente = resultado.CodigoCliente,
                    resultado.IdComprobanteTexto,
                    Articulos = resultado.Lineas.Count,
                    resultado.Total
                },
                ct);

            return resultado;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            const string mensaje = "No se pudo registrar el pedido. El carrito se conserva para que puedas reintentar.";
            var incidentId = await appEvents.LogErrorAsync(
                ModuleName,
                "ConfirmarPedidoCarrito",
                ex,
                mensaje,
                new { request.IdInsert, request.IdWeb, request.IdBase, CodigoCliente = codigoClienteSesion },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException(mensaje, incidentId, ex);
        }
        finally
        {
            pedidoProcessingGuard.Finish(request.IdInsert, codigoClienteSesion);
        }
    }

    private async Task<string> ResolveLetraPedidoWebAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "V_TA_Cpte", ct))
            return LetraPedidoWebDefault;

        var letras = await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT TOP (1) ISNULL(LTRIM(RTRIM(LETRAS)), '')
            FROM dbo.V_TA_Cpte
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo
              AND ISNULL(X_SUC_DEFAULT, 0) = @Sucursal;
            """,
            new { Codigo = TcPedidoWeb, Sucursal = int.Parse(SucursalPedidoWeb) },
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(letras))
            return LetraPedidoWebDefault;

        return letras.Contains('X') ? "X" : letras.Trim()[..1];
    }

    private async Task<CabeceraPedidoWeb> CrearCabeceraPedidoWebAsync(SqlConnection cn, SqlTransaction tx, string cliente, string letra, string observaciones, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_Alta_Comprobante", cn, tx)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pCliente", cliente);
        cmd.Parameters.AddWithValue("@pVendedor", string.Empty);
        cmd.Parameters.AddWithValue("@pFecha", DateTime.Today);
        cmd.Parameters.AddWithValue("@pObservaciones", string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
        cmd.Parameters.AddWithValue("@pLat", DBNull.Value);
        cmd.Parameters.AddWithValue("@pLng", DBNull.Value);
        cmd.Parameters.AddWithValue("@pTC", TcPedidoWeb);
        cmd.Parameters.AddWithValue("@pSucursal", SucursalPedidoWeb);
        cmd.Parameters.AddWithValue("@pNumero", DBNull.Value);
        cmd.Parameters.AddWithValue("@pLetra", letra);

        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdComprobanteRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        var resultado = resultadoParam.Value is null or DBNull ? (int?)null : Convert.ToInt32(resultadoParam.Value);
        var mensaje = mensajeParam.Value is null or DBNull ? string.Empty : Convert.ToString(mensajeParam.Value) ?? string.Empty;

        if (resultado != 11 || idParam.Value is null or DBNull)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(mensaje) ? "No se pudo generar el comprobante del pedido." : mensaje);

        var idComprobante = Convert.ToInt32(idParam.Value);

        var row = await cn.QuerySingleOrDefaultAsync<(string? IdComprobanteTexto, string Numero, DateTime Fecha)>(new CommandDefinition(
            """
            SELECT TOP (1) LTRIM(RTRIM(IDCOMPROBANTE)) AS IdComprobanteTexto, LTRIM(RTRIM(NUMERO)) AS Numero, FECHA AS Fecha
            FROM dbo.V_MV_Cpte
            WHERE ID = @Id;
            """,
            new { Id = idComprobante },
            transaction: tx,
            cancellationToken: ct));

        if (row.IdComprobanteTexto is null)
            throw new InvalidOperationException("El pedido se generó pero no se pudo releer el comprobante.");

        return new CabeceraPedidoWeb(idComprobante, row.IdComprobanteTexto, row.Numero, row.Fecha);
    }

    private async Task AgregarLineaPedidoWebAsync(SqlConnection cn, SqlTransaction tx, int idComprobante, CatalogosCatalogoItemDto articulo, decimal cantidad, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.sp_web_CpteInsumos", cn, tx)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@pIdCpte", idComprobante);
        cmd.Parameters.AddWithValue("@pIdArticulo", articulo.IdArticulo.Trim());
        cmd.Parameters.AddWithValue("@pCantidad", (double)cantidad);
        cmd.Parameters.AddWithValue("@pImporteUnitario", CatalogosPriceDisplayHelper.GetPrecioAplicado(articulo));
        cmd.Parameters.AddWithValue("@pPorcDescuento", "0");

        var resultadoParam = new SqlParameter("@pResultado", System.Data.SqlDbType.SmallInt) { Direction = System.Data.ParameterDirection.Output };
        var mensajeParam = new SqlParameter("@pMensaje", System.Data.SqlDbType.VarChar, 255) { Direction = System.Data.ParameterDirection.Output };
        var idParam = new SqlParameter("@pIdVMVCpteInsumosRES", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        cmd.Parameters.Add(resultadoParam);
        cmd.Parameters.Add(mensajeParam);
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync(ct);

        var resultado = resultadoParam.Value is null or DBNull ? (int?)null : Convert.ToInt32(resultadoParam.Value);
        if (resultado != 11)
        {
            var mensaje = mensajeParam.Value is null or DBNull ? string.Empty : Convert.ToString(mensajeParam.Value) ?? string.Empty;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(mensaje)
                ? $"No se pudo agregar el artículo {articulo.IdArticulo} al pedido."
                : $"Artículo {articulo.IdArticulo}: {mensaje}");
        }
    }

    private static bool EsPosibleColisionDeNumeracion(Exception ex)
    {
        if (ex is SqlException sqlEx && sqlEx.Number is 2627 or 2601)
            return true;

        var mensaje = ex.Message ?? string.Empty;
        return mensaje.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || mensaje.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
            || mensaje.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || mensaje.Contains("duplicad", StringComparison.OrdinalIgnoreCase);
    }

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

    public Task<CatalogosPublicIdentityDto> GetPublicIdentityAsync(string? idWeb, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPublicIdentity", async token =>
        {
            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var scopedLogoFormatKey = BuildScopedConfigKey(PublicLogoFormatConfigKey, effectiveIdWeb);
            var sql = $"""
                SELECT
                    UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                    ISNULL(VALOR, '') AS Valor,
                    ISNULL({detailColumn}, '') AS ValorAux
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (@NombreKey, @LogoFormatKey, @LegacyLogoFormatKey);
                """;

            var rows = await cn.QueryAsync<(string Clave, string Valor, string ValorAux)>(new CommandDefinition(
                sql,
                new
                {
                    NombreKey = PublicNameConfigKey,
                    LogoFormatKey = scopedLogoFormatKey,
                    LegacyLogoFormatKey = PublicLogoFormatConfigKey
                },
                cancellationToken: token));

            var values = rows.ToDictionary(x => x.Clave, x => ResolveStoredValue(x.Valor, x.ValorAux), StringComparer.OrdinalIgnoreCase);
            var sessionFallback = sessionService.GetActiveSession()?.Nombre?.Trim();
            var activeBaseId = sessionService.GetActiveSession()?.BaseId;
            var logoFormat = NormalizePublicLogoFormat(ReadFirstConfigValue(values, scopedLogoFormatKey, PublicLogoFormatConfigKey));
            var logoPersonalizadoExiste = await ResolveLogoPersonalizadoExisteAsync(activeBaseId, token);

            return new CatalogosPublicIdentityDto
            {
                NombreVisible = ResolvePublicName(values.TryGetValue(PublicNameConfigKey, out var rawName) ? rawName : string.Empty, sessionFallback),
                NombreFallback = string.IsNullOrWhiteSpace(sessionFallback) ? "Catálogos" : sessionFallback,
                LogoUrl = logoPersonalizadoExiste ? BuildPublicLogoUrl(effectiveIdWeb, activeBaseId) : DefaultPublicLogoUrl,
                LogoFormato = logoFormat,
                TieneLogoPersonalizado = logoPersonalizadoExiste
            };
        }, "No se pudo cargar la identidad pública de catálogos.", ct);

    public Task SavePublicIdentityNameAsync(string userName, string? nombreVisible, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePublicIdentityName", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la identidad pública.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            await UpsertConfigValueAsync(cn, detailColumn, PublicNameConfigKey, nombreVisible?.Trim() ?? string.Empty, ConfigGroup, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SavePublicIdentityName",
                "TA_CONFIGURACION",
                PublicNameConfigKey,
                "Nombre visible del catálogo público actualizado.",
                new { UserName = userName.Trim(), NombreVisible = nombreVisible?.Trim() ?? string.Empty },
                token);

            return true;
        }, "No se pudo guardar el nombre visible del catálogo.", ct);

    public Task SavePublicLogoFormatAsync(string userName, string? idWeb, string logoFormat, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePublicLogoFormat", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar el formato del logo.");

            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            var normalizedFormat = NormalizePublicLogoFormat(logoFormat);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildScopedConfigKey(PublicLogoFormatConfigKey, effectiveIdWeb);
            await UpsertConfigValueAsync(cn, detailColumn, configKey, normalizedFormat, ConfigGroup, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SavePublicLogoFormat",
                "TA_CONFIGURACION",
                configKey,
                "Formato del logo público del catálogo actualizado.",
                new { UserName = userName.Trim(), LogoFormato = normalizedFormat, IdWeb = effectiveIdWeb },
                token);

            return true;
        }, "No se pudo guardar el formato del logo del catálogo.", ct);

    public Task<string> GetPublicClasePrecioAsync(string? idWeb, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPublicClasePrecio", async token =>
        {
            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", token))
                return DefaultClasePrecio;

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var scopedKey = BuildScopedConfigKey(PublicClasePrecioConfigKey, effectiveIdWeb);

            var sql = $"""
                SELECT
                    UPPER(LTRIM(RTRIM(CLAVE))) AS Clave,
                    ISNULL(VALOR, '') AS Valor,
                    ISNULL({detailColumn}, '') AS ValorAux
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (@ScopedKey, @LegacyKey);
                """;

            var rows = await cn.QueryAsync<(string Clave, string Valor, string ValorAux)>(new CommandDefinition(
                sql,
                new { ScopedKey = scopedKey.ToUpperInvariant(), LegacyKey = PublicClasePrecioConfigKey.ToUpperInvariant() },
                cancellationToken: token));

            var values = rows.ToDictionary(x => x.Clave, x => ResolveStoredValue(x.Valor, x.ValorAux), StringComparer.OrdinalIgnoreCase);
            return NormalizeClasePrecio(ReadFirstConfigValue(values, scopedKey, PublicClasePrecioConfigKey));
        }, "No se pudo cargar la clase de precio del catálogo.", ct);

    private async Task<int> GetOfertaClasePrecioAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct))
            return 0;

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(
            sql,
            new { Clave = OfertaClasePrecioConfigKey },
            cancellationToken: ct));
        var raw = ResolveStoredValue(row.Valor ?? string.Empty, row.ValorAux ?? string.Empty);
        return int.TryParse(raw.Trim(), out var parsed) && parsed is >= 0 and <= 8 ? parsed : 0;
    }

    public Task SavePublicClasePrecioAsync(string userName, string? idWeb, string clasePrecio, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePublicClasePrecio", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la clase de precio.");

            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            var normalizedClase = NormalizeClasePrecio(clasePrecio);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildScopedConfigKey(PublicClasePrecioConfigKey, effectiveIdWeb);
            await UpsertConfigValueAsync(cn, detailColumn, configKey, normalizedClase, ConfigGroup, token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "SavePublicClasePrecio",
                "TA_CONFIGURACION",
                configKey,
                "Clase de precio del catálogo público actualizada.",
                new { UserName = userName.Trim(), ClasePrecio = normalizedClase, IdWeb = effectiveIdWeb },
                token);

            return true;
        }, "No se pudo guardar la clase de precio del catálogo.", ct);

    public Task<CatalogosPublicIdentityDto> SavePublicIdentityLogoAsync(string userName, string? idWeb, Stream content, string fileName, string contentType, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePublicIdentityLogo", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar el logo público.");

            if (content is null || !content.CanRead)
                throw new InvalidOperationException("No se recibió una imagen válida.");

            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            var (normalizedContentType, extension) = ResolveLogoType(contentType, fileName);
            if (!AllowedLogoContentTypes.Contains(normalizedContentType))
                throw new InvalidOperationException("El logo debe ser JPG, PNG, WebP o GIF.");

            var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(token);
            if (string.IsNullOrWhiteSpace(ftpCodigoCta))
                throw new InvalidOperationException("Falta configurar el código de cuenta FTP (FTP_CODIGOCTA) para poder guardar el logo.");

            var activeBaseId = sessionService.GetActiveSession()?.BaseId;
            var subido = await articuloImagenFtpService.SubirImagenAsync(ftpCodigoCta, activeBaseId, LogoFtpArticuloKey, extension, content, thumbnail: false, ct: token);
            if (!subido)
                throw new InvalidOperationException("No se pudo subir el logo al servidor de imágenes. Probá de nuevo en unos minutos.");

            await appEvents.LogAuditAsync(
                ModuleName,
                "SavePublicIdentityLogo",
                "TA_CONFIGURACION",
                ftpCodigoCta,
                "Logo público del catálogo actualizado.",
                new { UserName = userName.Trim(), FileName = fileName, IdWeb = effectiveIdWeb, IdBase = activeBaseId },
                token);

            return await GetPublicIdentityAsync(effectiveIdWeb, token);
        }, "No se pudo guardar el logo del catálogo.", ct);

    public Task ResetPublicIdentityLogoAsync(string userName, string? idWeb, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ResetPublicIdentityLogo", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para restaurar el logo público.");

            var effectiveIdWeb = GetEffectiveIdWeb(idWeb);
            var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(token);
            var activeBaseId = sessionService.GetActiveSession()?.BaseId;
            if (!string.IsNullOrWhiteSpace(ftpCodigoCta))
                await articuloImagenFtpService.EliminarImagenAsync(ftpCodigoCta, activeBaseId, LogoFtpArticuloKey, thumbnail: false, ct: token);

            await appEvents.LogAuditAsync(
                ModuleName,
                "ResetPublicIdentityLogo",
                "TA_CONFIGURACION",
                ftpCodigoCta,
                "Logo público del catálogo restaurado al valor predeterminado.",
                new { UserName = userName.Trim(), IdWeb = effectiveIdWeb, IdBase = activeBaseId },
                token);

            return true;
        }, "No se pudo restaurar el logo del catálogo.", ct);

    public Task<CatalogosPublicLogoServeDto?> GetPublicLogoForServeAsync(string? idWeb, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPublicLogoForServe", async token =>
        {
            var activeBaseId = sessionService.GetActiveSession()?.BaseId;
            var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(token);
            var imagen = string.IsNullOrWhiteSpace(ftpCodigoCta)
                ? null
                : await articuloImagenFtpService.ObtenerImagenAsync(ftpCodigoCta, activeBaseId, LogoFtpArticuloKey, thumbnail: false, ct: token);

            if (imagen is not null)
            {
                return new CatalogosPublicLogoServeDto
                {
                    RutaCompleta = imagen.RutaCompleta,
                    NombreArchivo = Path.GetFileName(imagen.RutaCompleta),
                    MimeType = imagen.MimeType
                };
            }

            var fallbackPath = GetDefaultPublicLogoPhysicalPath();
            if (!File.Exists(fallbackPath))
                return null;

            return new CatalogosPublicLogoServeDto
            {
                RutaCompleta = fallbackPath,
                NombreArchivo = Path.GetFileName(fallbackPath),
                MimeType = InferImageMimeType(fallbackPath)
            };
        }, "No se pudo resolver el logo público del catálogo.", ct);

    private async Task<bool> ResolveLogoPersonalizadoExisteAsync(int? idBase, CancellationToken ct)
    {
        var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(ct);
        if (string.IsNullOrWhiteSpace(ftpCodigoCta))
            return false;

        var imagen = await articuloImagenFtpService.ObtenerImagenAsync(ftpCodigoCta, idBase, LogoFtpArticuloKey, thumbnail: false, ct: ct);
        return imagen is not null;
    }

    private async Task<string> ResolveFtpCodigoCtaAsync(CancellationToken ct)
    {
        var settings = await puntoVentaService.GetSettingsAsync(ct);
        return (settings.FtpCodigoCta ?? string.Empty).Trim();
    }

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

            const string where = "WHERE c.IDINSERT = @IdInsert";
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
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, ''))), ''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, ''))), ''),
                        N''
                    ) AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), '') AS RutaImagen,
                    ISNULL(LTRIM(RTRIM(c.Presentacion)), '') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(c.Marca)), '') AS Marca,
                    c.Precio,
                    c.PrecioOferta,
                    ISNULL(LTRIM(RTRIM(c.RUBRO)), '') AS Rubro,
                    -- Marcada por BaseMaestraImagenService (o por afuera de AlfaCore, ej. '1'/'P')
                    -- cuando la imagen del artículo cambió; el catálogo la usa para forzar una
                    -- redescarga salteando el caché local (ver ArticuloImagenFtpService.ObtenerImagenAsync),
                    -- por si thumbs4 todavía no se regeneró con la imagen nueva.
                    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(a.ModificoImagen, '')))) IN ('1', 'P') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS ImagenModificada
                FROM dbo.V_MV_INSERT c
                LEFT JOIN dbo.V_MA_ARTICULOS a
                    ON UPPER(LTRIM(RTRIM(a.IDARTICULO))) = UPPER(LTRIM(RTRIM(c.IDARTICULO)))
                {where}
                ORDER BY c.IDARTICULO;
                """;

            var predeterminadoId = await GetCatalogoPredeterminadoIdInternalAsync(cn, token);
            var candidateIds = new List<int>();
            if (idInsert > 0)
            {
                candidateIds.Add(idInsert);
            }
            else
            {
                if (predeterminadoId > 0)
                    candidateIds.Add(predeterminadoId);

                var fallbackId = await GetCatalogoGeneralFallbackIdInternalAsync(cn, token);
                if (fallbackId > 0 && !candidateIds.Contains(fallbackId))
                    candidateIds.Add(fallbackId);
            }

            foreach (var candidateId in candidateIds)
            {
                using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { IdInsert = candidateId }, cancellationToken: token));
                var header = await multi.ReadSingleOrDefaultAsync<CatalogosCatalogoDetalleDto>();
                if (header is null)
                    continue;

                if (soloPublico && !IsCatalogoPublicoVigente(header))
                    continue;

                var items = (await multi.ReadAsync<CatalogosCatalogoItemDto>()).ToList();
                header.Articulos = items;

                await EnrichOfertaHastaAsync(items, header.IdLista, token);
                header.HabilitarCarrito = await GetCarritoHabilitadoAsync(candidateId, token);
                header.Predeterminado = candidateId == predeterminadoId;

                return header;
            }

            return null;
        }, soloPublico ? "No se pudo cargar el catálogo público." : "No se pudo cargar el catálogo.", ct);

    private async Task<bool> GetCarritoHabilitadoAsync(int idInsert, CancellationToken ct)
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

        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(
            sql,
            new { Clave = BuildCarritoConfigKey(idInsert) },
            cancellationToken: ct));
        var raw = ResolveStoredValue(row.Valor ?? string.Empty, row.ValorAux ?? string.Empty);
        return string.Equals(raw.Trim(), "SI", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SetCarritoHabilitadoAsync(SqlConnection cn, int idInsert, bool habilitado, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct))
            return;

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        await UpsertConfigValueAsync(cn, detailColumn, BuildCarritoConfigKey(idInsert), habilitado ? "SI" : "NO", ConfigGroup, ct);
    }

    private static string BuildCarritoConfigKey(int idInsert)
        => $"{CarritoHabilitadoConfigKeyPrefix}-{idInsert}".ToUpperInvariant();

    private async Task EnrichOfertaHastaAsync(List<CatalogosCatalogoItemDto> items, string idLista, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idLista) || !items.Any(i => i.PrecioOferta is > 0))
            return;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (!await SqlObjectExistsAsync(cn, "V_MA_Precios", ct))
            return;

        const string sql = """
            SELECT
                LTRIM(RTRIM(p.IdArticulo)) AS IdArticulo,
                p.FhOfertaHasta AS OfertaHasta
            FROM dbo.V_MA_Precios p
            WHERE UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, '')))) = UPPER(LTRIM(RTRIM(@IdLista)))
              AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, 'V')))) = 'V';
            """;

        var rows = await cn.QueryAsync<(string IdArticulo, DateTime? OfertaHasta)>(new CommandDefinition(
            sql,
            new { IdLista = idLista },
            cancellationToken: ct));

        var hastaPorArticulo = rows.ToDictionary(r => r.IdArticulo, r => r.OfertaHasta, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.PrecioOferta is > 0 && hastaPorArticulo.TryGetValue(item.IdArticulo.Trim(), out var hasta))
                item.OfertaHasta = hasta;
        }
    }

    private static bool IsCatalogoPublicoVigente(CatalogosCatalogoDetalleDto catalogo)
    {
        if (catalogo.Finalizado)
            return false;

        var hoy = DateTime.Today;
        if (catalogo.VigenciaDesde.HasValue && catalogo.VigenciaDesde.Value.Date > hoy)
            return false;

        if (catalogo.VigenciaHasta.HasValue && catalogo.VigenciaHasta.Value.Date < hoy)
            return false;

        return true;
    }

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

    private static string BuildPublicUrl(int idInsert, string? idWeb = null, int? idBase = null)
    {
        var route = $"/catalogo/{idInsert}";
        if (!string.IsNullOrWhiteSpace(idWeb) && idBase.HasValue)
            return $"/{NormalizeRoutePart(idWeb)}/{idBase.Value}{route}";

        return route;
    }

    private static string NormalizeRoutePart(string value)
        => Uri.EscapeDataString((value ?? string.Empty).Trim());

    private static string ResolveStoredValue(string valor, string valorAux)
        => !string.IsNullOrWhiteSpace(valor)
            ? valor
            : valorAux;

    private static string ResolvePublicName(string storedName, string? sessionFallback)
    {
        var normalized = (storedName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        if (!string.IsNullOrWhiteSpace(sessionFallback))
            return sessionFallback.Trim();

        return "Catálogos";
    }

    private static string ReadFirstConfigValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string NormalizePublicLogoFormat(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            CatalogosPublicLogoFormatKeys.Square => CatalogosPublicLogoFormatKeys.Square,
            CatalogosPublicLogoFormatKeys.Horizontal => CatalogosPublicLogoFormatKeys.Horizontal,
            _ => CatalogosPublicLogoFormatKeys.Auto
        };

    private static int ParseClasePrecio(string? value)
        => int.TryParse((value ?? string.Empty).Trim(), out var parsed) && parsed is >= 1 and <= 8 ? parsed : 1;

    private static string NormalizeClasePrecio(string? value)
        => ParseClasePrecio(value).ToString();

    private static string BuildScopedConfigKey(string baseKey, string? idWeb)
    {
        var normalizedIdWeb = NormalizeIdWebSegment(idWeb);
        return string.Equals(normalizedIdWeb, "default", StringComparison.OrdinalIgnoreCase)
            ? baseKey
            : $"{baseKey}-{normalizedIdWeb}";
    }

    private string BuildPublicLogoUrl(string? idWeb, int? idBase)
    {
        var routeIdWeb = GetEffectiveIdWeb(idWeb);
        if (idBase is > 0)
            return $"/api/catalogos/logo-publico/{Uri.EscapeDataString(routeIdWeb)}?idbase={idBase.Value}";

        return $"/api/catalogos/logo-publico/{Uri.EscapeDataString(routeIdWeb)}";
    }

    private string GetDefaultPublicLogoPhysicalPath()
    {
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;

        return Path.Combine(webRoot, "logos", "Logo.png");
    }

    private static string NormalizeIdWebSegment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return "default";

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(ch);
        }

        return builder.Length == 0 ? "default" : builder.ToString();
    }

    private string GetEffectiveIdWeb(string? idWeb)
        => NormalizeIdWebSegment(!string.IsNullOrWhiteSpace(idWeb) ? idWeb : appUserSession.CurrentUser?.IdWeb);

    private static (string ContentType, string Extension) ResolveLogoType(string contentType, string fileName)
    {
        var normalizedContentType = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var mappedExtension = extension switch
        {
            ".jpeg" => ".jpg",
            ".jpg" => ".jpg",
            ".png" => ".png",
            ".webp" => ".webp",
            ".gif" => ".gif",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(normalizedContentType))
        {
            normalizedContentType = mappedExtension switch
            {
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => string.Empty
            };
        }

        if (!AllowedLogoContentTypes.Contains(normalizedContentType))
        {
            normalizedContentType = mappedExtension switch
            {
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => string.Empty
            };
        }

        if (!AllowedLogoContentTypes.Contains(normalizedContentType))
            throw new InvalidOperationException("El logo debe ser JPG, PNG, WebP o GIF.");

        if (string.IsNullOrWhiteSpace(mappedExtension))
        {
            mappedExtension = normalizedContentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".png"
            };
        }

        return (normalizedContentType, mappedExtension);
    }

    private static string InferImageMimeType(string path)
    {
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    private async Task<string> ReadConfigValueAsync(SqlConnection cn, string detailColumn, string key, string fallbackKey, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, ''),
                ISNULL({detailColumn}, '')
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (@Clave, @FallbackClave)
            ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(CLAVE))) = @Clave THEN 0 ELSE 1 END;
            """;

        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(sql, new { Clave = key.ToUpperInvariant(), FallbackClave = fallbackKey.ToUpperInvariant() }, cancellationToken: ct));
        return ResolveStoredValue(row.Valor, row.ValorAux);
    }

    private async Task UpsertConfigValueAsync(SqlConnection cn, string detailColumn, string key, string value, string group, CancellationToken ct)
    {
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
                ClaveNormalizada = key.ToUpperInvariant(),
                Clave = key,
                Valor = DbNullable(value, 150),
                Grupo = group
            },
            cancellationToken: ct));
    }

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

    private static string NormalizeCatalogFilter(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "todos" : normalized;
    }

    private static PagedResult<CatalogosArticuloBusquedaDto> EmptyArticuloPage(int pageNumber, int pageSize)
        => new()
        {
            Items = [],
            Total = 0,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

    private sealed class CatalogosArticuloBusquedaPageRowDto
    {
        public string IdArticulo { get; set; } = string.Empty;
        public string DescripcionArticulo { get; set; } = string.Empty;
        public string CodigoBarra { get; set; } = string.Empty;
        public string RutaImagen { get; set; } = string.Empty;
        public string Presentacion { get; set; } = string.Empty;
        public string Marca { get; set; } = string.Empty;
        public string Rubro { get; set; } = string.Empty;
        public string Familia { get; set; } = string.Empty;
        public string ListaPrecio { get; set; } = string.Empty;
        public string NombreListaPrecio { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public decimal? PrecioOferta { get; set; }
        public int TotalRows { get; set; }
    }

    private sealed class CatalogosClienteLoginRowDto
    {
        public string CodigoCliente { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public string NumeroDocumento { get; set; } = string.Empty;
    }

    private const string CredencialesInvalidasMensaje = "Código/email o contraseña incorrectos.";
    private const string EmailAmbiguoMensaje = "El email está asociado a más de una cuenta. Ingresá con tu código de cliente.";

    private async Task<CatalogosClienteSessionInfo> ExecuteCatalogoClienteLoginAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var identificador = (request.CodigoCliente ?? string.Empty).Trim();
            var password = request.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identificador) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(CredencialesInvalidasMensaje);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await SqlObjectExistsAsync(cn, "VT_CLIENTES", ct) || !await SqlObjectExistsAsync(cn, "MA_CUENTASADIC", ct))
                throw new InvalidOperationException(CredencialesInvalidasMensaje);

            var esEmail = identificador.Contains('@');
            var codigoCliente = identificador;

            if (esEmail)
            {
                const string sqlEmail = """
                    SELECT DISTINCT LTRIM(RTRIM(cli.CODIGO)) AS Codigo
                    FROM dbo.VT_CLIENTES cli
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(cli.MAIL, '')))) = UPPER(LTRIM(RTRIM(@Email)));
                    """;

                var codigos = (await cn.QueryAsync<string>(new CommandDefinition(
                    sqlEmail,
                    new { Email = identificador },
                    cancellationToken: ct))).ToList();

                if (codigos.Count == 0)
                    throw new InvalidOperationException(CredencialesInvalidasMensaje);

                if (codigos.Count > 1)
                    throw new InvalidOperationException(EmailAmbiguoMensaje);

                codigoCliente = codigos[0];
            }

            const string sql = """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(cli.CODIGO)), '') AS CodigoCliente,
                    ISNULL(LTRIM(RTRIM(cli.RAZON_SOCIAL)), '') AS RazonSocial,
                    ISNULL(LTRIM(RTRIM(cli.MAIL)), '') AS Email,
                    ISNULL(LTRIM(RTRIM(adic.CLAVE)), '') AS Clave,
                    ISNULL(LTRIM(RTRIM(adic.NUMERO_DOCUMENTO)), '') AS NumeroDocumento
                FROM dbo.VT_CLIENTES cli
                LEFT JOIN dbo.MA_CUENTASADIC adic
                    ON UPPER(LTRIM(RTRIM(ISNULL(adic.CODIGO, '')))) = UPPER(LTRIM(RTRIM(cli.CODIGO)))
                WHERE UPPER(LTRIM(RTRIM(ISNULL(cli.CODIGO, '')))) = UPPER(LTRIM(RTRIM(@CodigoCliente)));
                """;

            var row = await cn.QuerySingleOrDefaultAsync<CatalogosClienteLoginRowDto>(new CommandDefinition(
                sql,
                new { CodigoCliente = codigoCliente },
                cancellationToken: ct));

            if (row is null)
                throw new InvalidOperationException(CredencialesInvalidasMensaje);

            var storedPassword = !string.IsNullOrWhiteSpace(row.Clave)
                ? row.Clave
                : row.NumeroDocumento;

            if (string.IsNullOrWhiteSpace(storedPassword) || !string.Equals(storedPassword, password, StringComparison.Ordinal))
                throw new InvalidOperationException(CredencialesInvalidasMensaje);

            return new CatalogosClienteSessionInfo
            {
                CodigoCliente = row.CodigoCliente,
                RazonSocial = row.RazonSocial,
                Email = row.Email,
                IdWeb = request.IdWeb?.Trim() ?? string.Empty,
                IdBase = request.IdBase ?? 0,
                LoginAt = DateTime.Now
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(
                ModuleName,
                "LoginCliente",
                ex,
                "No se pudo validar el acceso del cliente.",
                new
                {
                    CodigoCliente = (request?.CodigoCliente ?? string.Empty).Trim(),
                    IdWeb = request?.IdWeb,
                    IdBase = request?.IdBase,
                    IdInsert = request?.IdInsert
                },
                AppEventSeverity.Warning,
                ct);

            throw new AppUserFacingException("No pudimos validar el acceso en este momento. Intentá nuevamente.", incidentId, ex);
        }
    }

    public async Task ClearImagenModificadaAsync(string idArticulo, CancellationToken ct = default)
    {
        var articuloId = (idArticulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(articuloId))
            return;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", ct))
                return;

            const string sql = """
                UPDATE dbo.V_MA_ARTICULOS
                SET ModificoImagen = ''
                WHERE LTRIM(RTRIM(IDARTICULO)) = @IdArticulo
                  AND UPPER(LTRIM(RTRIM(ISNULL(ModificoImagen, '')))) IN ('1', 'P');
                """;

            await cn.ExecuteAsync(new CommandDefinition(sql, new { IdArticulo = articuloId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // No debe romper la respuesta de la imagen: en el peor caso, el próximo pedido va a
            // seguir forzando la redescarga (más lento para ese artículo, pero no incorrecto).
            await appEvents.LogErrorAsync(ModuleName, "ClearImagenModificada", ex, "No se pudo apagar ModificoImagen.", new { IdArticulo = articuloId }, AppEventSeverity.Warning, ct);
        }
    }

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
        catch (InvalidOperationException validationEx)
        {
            // Validación funcional esperable (ej. "Ingresá el nombre del catálogo"): se muestra
            // tal cual al usuario en vez de reemplazarla por el mensaje genérico de friendlyMessage.
            // Igual queda registrada para trazabilidad.
            await appEvents.LogErrorAsync(
                module,
                action,
                validationEx,
                validationEx.Message,
                new
                {
                    Usuario = appUserSession.GetCurrentUserName(Environment.UserName),
                    SesionSql = sessionService.GetActiveSession()?.Nombre
                },
                AppEventSeverity.Warning,
                ct);

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

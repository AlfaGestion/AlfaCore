using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text;

namespace AlfaCore.Services;

public sealed class CarritoComprasService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IInterfacesCatalogosService catalogosService,
    CatalogoPedidoProcessingGuard pedidoProcessingGuard,
    IAppEventService appEvents) : ICarritoComprasService
{
    private const string ModuleName = "CarritoCompras";
    private const string ConfigGroup = "CATALOGOS";
    private const string CarritoHabilitadoConfigKeyPrefix = "CATALOGOS-CARRITO";
    private const string TcPedidoWeb = "NP";
    private const string SucursalPedidoWeb = "9999";
    private const string LetraPedidoWebDefault = "X";
    private const string OfertaClasePrecioConfigKey = "CLASEPRECIOOFERTA";
    private const string DefaultClasePrecio = "1";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<CarritoComprasResumenDto>> SearchAsync(CarritoComprasFiltroDto filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Search", async token =>
        {
            filtro ??= new CarritoComprasFiltroDto();
            var pageNumber = filtro.PageNumber <= 0 ? 1 : filtro.PageNumber;
            var pageSize = filtro.PageSize <= 0 ? 50 : Math.Min(filtro.PageSize, 100);
            var offset = (pageNumber - 1) * pageSize;
            var textoLike = LikeContains(filtro.Texto);
            var tipoFiltro = NormalizeFilter(filtro.Tipo, CarritoComprasTipoKeys.Todos);
            var estadoFiltro = NormalizeFilter(filtro.Estado, CarritoComprasEstadoKeys.Todos);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var hasCatalogosNuevo = await TableExistsAsync(cn, "CATALOGOS", token) && await TableExistsAsync(cn, "CATALOGOS_DETALLE", token);
            var hasCatalogosLegacy = await SqlObjectExistsAsync(cn, "V_MV_INSERT", token);
            var hasGenerales = await TableExistsAsync(cn, "ALFACORE_CARRITOS_WEB", token);
            if (!hasCatalogosNuevo && !hasCatalogosLegacy && !hasGenerales)
            {
                return new PagedResult<CarritoComprasResumenDto>
                {
                    Items = [],
                    Total = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var sourceSql = BuildSourceSql(hasCatalogosNuevo, hasCatalogosLegacy, hasGenerales, detailColumn);
            var whereSql = """
                (@TextoLike = N''
                 OR UPPER(LTRIM(RTRIM(ISNULL(Nombre, N'')))) LIKE @TextoLike
                 OR CONVERT(nvarchar(20), IdReferencia) LIKE @TextoLike)
                AND (@TipoFiltro = N'todos' OR TipoClave = @TipoFiltro)
                AND (
                    @EstadoFiltro = N'todos'
                    OR (@EstadoFiltro = N'activo' AND Activo = 1)
                    OR (@EstadoFiltro = N'inactivo' AND Activo = 0)
                )
                """;

            var parametros = new
            {
                TextoLike = textoLike,
                TipoFiltro = tipoFiltro,
                EstadoFiltro = estadoFiltro,
                Offset = offset,
                PageSize = pageSize
            };

            var countSql = $"""
                WITH Datos AS (
                {sourceSql}
                )
                SELECT COUNT(1)
                FROM Datos
                WHERE {whereSql};
                """;

            var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parametros, cancellationToken: token));

            var rowsSql = $"""
                WITH Datos AS (
                {sourceSql}
                )
                SELECT
                    IdReferencia,
                    IdCatalogo,
                    IdCarrito,
                    TipoClave,
                    Nombre,
                    Tipo,
                    Vigencia,
                    Estado,
                    CantidadArticulos,
                    Precios,
                    Activo,
                    OrdenTipo
                FROM Datos
                WHERE {whereSql}
                ORDER BY OrdenTipo, Nombre, IdReferencia
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            var rows = await cn.QueryAsync<CarritoResumenRow>(new CommandDefinition(rowsSql, parametros, cancellationToken: token));
            var items = rows.Select(row => new CarritoComprasResumenDto
            {
                Id = row.IdReferencia.ToString(),
                IdCatalogo = row.IdCatalogo,
                IdCarrito = row.IdCarrito,
                TipoClave = row.TipoClave,
                Nombre = row.Nombre,
                Tipo = row.Tipo,
                Vigencia = row.Vigencia,
                Estado = row.Estado,
                CantidadArticulos = row.CantidadArticulos,
                Precios = row.Precios,
                Activo = row.Activo,
                OrdenTipo = row.OrdenTipo
            }).ToList();

            return new PagedResult<CarritoComprasResumenDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los carritos de compra.", ct);

    public Task<CarritoComprasGeneralDetalleDto?> GetGeneralAsync(int idCarrito, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetGeneral", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_CARRITOS_WEB", token))
                return null;

            var effectiveId = idCarrito > 0
                ? idCarrito
                : await ResolveGeneralIdAsync(cn, token);

            if (effectiveId <= 0)
                return null;

            var sql = $"""
                SELECT TOP (1)
                    c.IdCarrito,
                    ISNULL(LTRIM(RTRIM(c.Nombre)), N'') AS Nombre,
                    ISNULL(LTRIM(RTRIM(c.Descripcion)), N'') AS Descripcion,
                    CAST(ISNULL(c.Activo, 0) AS bit) AS Activo,
                    ISNULL(LTRIM(RTRIM(c.OrigenArticulos)), N'maestro') AS OrigenArticulos,
                    ISNULL(LTRIM(RTRIM(c.IdListaArticulos)), N'') AS IdListaArticulos,
                    ISNULL(LTRIM(RTRIM(c.OrigenPrecios)), N'segun-cliente') AS OrigenPrecios,
                    ISNULL(LTRIM(RTRIM(c.IdListaPrecios)), N'') AS IdListaPrecios,
                    c.ClasePrecio
                FROM dbo.ALFACORE_CARRITOS_WEB c
                WHERE c.IdCarrito = @IdCarrito;

                SELECT
                    d.IdArticulo,
                    ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), N'') AS DescripcionArticulo,
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, N''))), N''),
                        N''
                    ) AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), N'') AS RutaImagen,
                    ISNULL(LTRIM(RTRIM(a.Presentacion)), N'') AS Presentacion,
                    ISNULL(LTRIM(RTRIM(t.Descripcion)), N'') AS Marca,
                    ISNULL(LTRIM(RTRIM(r.Descripcion)), N'') AS Rubro
                FROM dbo.ALFACORE_CARRITOS_WEB_DET d
                LEFT JOIN dbo.V_MA_ARTICULOS a
                    ON UPPER(LTRIM(RTRIM(a.IDARTICULO))) = UPPER(LTRIM(RTRIM(d.IdArticulo)))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(t.IdTipo, N''))))
                LEFT JOIN dbo.V_TA_Rubros r
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(r.IdRubro, N''))))
                WHERE d.IdCarrito = @IdCarrito
                ORDER BY d.Orden, d.IdArticulo;
                """;

            using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { IdCarrito = effectiveId }, cancellationToken: token));
            var header = await multi.ReadSingleOrDefaultAsync<CarritoComprasGeneralDetalleDto>();
            if (header is null)
                return null;

            header.Articulos = (await multi.ReadAsync<CarritoComprasGeneralArticuloDto>()).ToList();
            return header;
        }, "No se pudo cargar el carrito general.", ct);

    public Task<CarritoComprasResumenDto?> ResolvePortalCarritoAsync(string? idWeb, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ResolvePortalCarrito", async token =>
        {
            var catalogos = await SearchAsync(new CarritoComprasFiltroDto
            {
                Tipo = CarritoComprasTipoKeys.Catalogo,
                Estado = CarritoComprasEstadoKeys.Activo,
                PageNumber = 1,
                PageSize = 200
            }, token);

            foreach (var item in catalogos.Items
                .OrderBy(x => x.OrdenTipo)
                .ThenBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase))
            {
                if (item.IdCatalogo is not { } idCatalogo || idCatalogo <= 0)
                    continue;

                if (await CatalogoPublicoDisponibleAsync(idCatalogo, idWeb, token))
                    return item;
            }

            var general = await GetGeneralAsync(0, token);
            if (general is null || !general.Activo)
                return null;

            return new CarritoComprasResumenDto
            {
                Id = "0",
                IdCarrito = general.IdCarrito,
                TipoClave = CarritoComprasTipoKeys.General,
                Nombre = general.Nombre,
                Tipo = "General",
                Vigencia = "Sin vigencia",
                Estado = general.Activo ? "Activo" : "Inactivo",
                CantidadArticulos = general.Articulos.Count,
                Precios = DescribeGeneralPriceOrigin(general),
                Activo = general.Activo,
                UrlCarrito = string.Empty,
                UrlEdicion = string.Empty,
                OrdenTipo = 1
            };
        }, "No se pudo resolver el carrito disponible del portal.", ct);

    public Task<CatalogosCatalogoDetalleDto?> GetPublicCartAsync(int idInsert, string? idWeb = null, string? codigoCliente = null, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPublicCart", async token =>
        {
            if (idInsert > 0)
                return await catalogosService.GetCatalogoPublicoAsync(idInsert, token);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var general = await GetGeneralAsync(0, token);
            if (general is null || !general.Activo)
                return null;

            var items = await LoadGeneralItemsAsync(cn, general, idWeb, codigoCliente, token);
            return new CatalogosCatalogoDetalleDto
            {
                IdInsert = 0,
                Tipo = "General",
                Nombre = general.Nombre,
                IdLista = general.IdListaPrecios ?? string.Empty,
                Grupo = general.Nombre,
                Observaciones = general.Descripcion,
                Finalizado = false,
                HabilitarCarrito = general.Activo,
                Articulos = items
            };
        }, "No se pudo cargar el carrito público.", ct);

    public Task<CatalogoPedidoResultDto> ConfirmarPedidoPublicoAsync(CatalogoPedidoConfirmarRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ConfirmarPedidoPublico", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.IdInsert > 0)
                return await catalogosService.ConfirmarPedidoCarritoAsync(request, token);

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

            if (!pedidoProcessingGuard.TryStart(0, codigoClienteSesion))
                throw new InvalidOperationException("Ya hay un pedido en proceso para este carrito. Esperá un momento y verificá antes de reintentar.");

            try
            {
                var carrito = await GetPublicCartAsync(0, request.IdWeb, codigoClienteSesion, token);
                if (carrito is null)
                    throw new InvalidOperationException("El carrito general no está disponible.");

                if (!carrito.HabilitarCarrito)
                    throw new InvalidOperationException("Este carrito no tiene habilitada la toma de pedidos.");

                var articulosPorCodigo = carrito.Articulos.ToDictionary(a => a.IdArticulo.Trim(), a => a, StringComparer.OrdinalIgnoreCase);
                var lineasResueltas = new List<(CatalogosCatalogoItemDto Articulo, decimal Cantidad)>();

                foreach (var linea in lineasSolicitadas)
                {
                    if (!articulosPorCodigo.TryGetValue(linea.IdArticulo, out var articulo))
                        throw new InvalidOperationException($"El artículo {linea.IdArticulo} no pertenece a este carrito.");

                    if (CatalogosPriceDisplayHelper.GetPrecioAplicado(articulo) <= 0m)
                        throw new InvalidOperationException($"El artículo {linea.IdArticulo} no tiene un precio válido en este carrito.");

                    lineasResueltas.Add((articulo, linea.Cantidad));
                }

                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync(token);

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
                    cancellationToken: token));

                if (cliente.Codigo is null)
                    throw new InvalidOperationException("No pudimos validar tu cuenta de cliente. Volvé a iniciar sesión e intentá nuevamente.");

                var letra = await ResolveLetraPedidoWebAsync(cn, token);
                var observaciones = "Pedido web - Carrito general";
                if (observaciones.Length > 250)
                    observaciones = observaciones[..250];

                CabeceraPedidoWeb? cabecera = null;
                const int maxIntentos = 5;

                for (var intento = 1; intento <= maxIntentos; intento++)
                {
                    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
                    try
                    {
                        await using (var lockCmd = new SqlCommand(
                            "EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;",
                            cn, tx))
                        {
                            lockCmd.Parameters.AddWithValue("@Resource", $"ALFACORE-V_MV_CPTE-{TcPedidoWeb}-{SucursalPedidoWeb}-{letra}");
                            await lockCmd.ExecuteNonQueryAsync(token);
                        }

                        cabecera = await CrearCabeceraPedidoWebAsync(cn, tx, cliente.Codigo, letra, observaciones, token);

                        foreach (var (articulo, cantidad) in lineasResueltas)
                            await AgregarLineaPedidoWebAsync(cn, tx, cabecera.IdComprobante, articulo, cantidad, token);

                        await tx.CommitAsync(token);
                        break;
                    }
                    catch (Exception ex) when (intento < maxIntentos && EsPosibleColisionDeNumeracion(ex))
                    {
                        try { await tx.RollbackAsync(token); } catch { }
                        cabecera = null;
                    }
                    catch
                    {
                        try { await tx.RollbackAsync(token); } catch { }
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
                    "ConfirmarPedidoPublico",
                    "V_MV_Cpte",
                    resultado.IdComprobanteTexto,
                    "Pedido NP generado desde el carrito general.",
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
                    token);

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
                    "ConfirmarPedidoPublico",
                    ex,
                    mensaje,
                    new { request.IdInsert, request.IdWeb, request.IdBase, CodigoCliente = codigoClienteSesion },
                    AppEventSeverity.Warning,
                    token);

                throw new AppUserFacingException(mensaje, incidentId, ex);
            }
            finally
            {
                pedidoProcessingGuard.Finish(0, codigoClienteSesion);
            }
        }, "No se pudo registrar el pedido.", ct);

    private sealed record CabeceraPedidoWeb(int IdComprobante, string IdComprobanteTexto, string Numero, DateTime Fecha);

    public Task<int> SaveGeneralAsync(CarritoComprasGeneralSaveRequestDto request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveGeneral", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var nombre = Truncate(request.Nombre, 150);
            if (string.IsNullOrWhiteSpace(nombre))
                throw new InvalidOperationException("El nombre del carrito es obligatorio.");

            var origenArticulos = NormalizeArticuloOrigin(request.OrigenArticulos);
            var origenPrecios = NormalizePrecioOrigin(request.OrigenPrecios);
            var listaArticulos = Truncate(request.IdListaArticulos, 4);
            var listaPrecios = Truncate(request.IdListaPrecios, 4);
            var clasePrecio = request.ClasePrecio is > 0 and <= 8 ? request.ClasePrecio : null;

            var articulos = (request.Articulos ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.IdArticulo))
                .GroupBy(a => a.IdArticulo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select((group, index) =>
                {
                    var first = group.First();
                    return new CarritoComprasGeneralArticuloDto
                    {
                        IdArticulo = group.Key,
                        DescripcionArticulo = Truncate(first.DescripcionArticulo, 100),
                        CodigoBarra = Truncate(first.CodigoBarra, 100),
                        RutaImagen = Truncate(first.RutaImagen, 100),
                        Presentacion = Truncate(first.Presentacion, 100),
                        Marca = Truncate(first.Marca, 100),
                        Rubro = Truncate(first.Rubro, 100)
                    };
                })
                .ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_CARRITOS_WEB", token))
                throw new InvalidOperationException("La tabla ALFACORE_CARRITOS_WEB no existe en la base activa.");

            if (!await TableExistsAsync(cn, "ALFACORE_CARRITOS_WEB_DET", token))
                throw new InvalidOperationException("La tabla ALFACORE_CARRITOS_WEB_DET no existe en la base activa.");

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                var idCarrito = request.IdCarrito.GetValueOrDefault();
                if (idCarrito <= 0)
                    idCarrito = await ResolveGeneralIdAsync(cn, token, tx) ?? 0;

                if (idCarrito <= 0)
                {
                    idCarrito = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                        """
                        INSERT INTO dbo.ALFACORE_CARRITOS_WEB
                        (
                            Nombre,
                            Descripcion,
                            Activo,
                            OrigenArticulos,
                            IdListaArticulos,
                            OrigenPrecios,
                            IdListaPrecios,
                            ClasePrecio,
                            FechaHora_Grabacion,
                            FechaHora_Modificacion
                        )
                        VALUES
                        (
                            @Nombre,
                            @Descripcion,
                            @Activo,
                            @OrigenArticulos,
                            NULLIF(@IdListaArticulos, N''),
                            @OrigenPrecios,
                            NULLIF(@IdListaPrecios, N''),
                            @ClasePrecio,
                            GETDATE(),
                            GETDATE()
                        );

                        SELECT CAST(SCOPE_IDENTITY() AS int);
                        """,
                        new
                        {
                            Nombre = nombre,
                            Descripcion = Truncate(request.Descripcion, 500),
                            Activo = request.Activo,
                            OrigenArticulos = origenArticulos,
                            IdListaArticulos = listaArticulos,
                            OrigenPrecios = origenPrecios,
                            IdListaPrecios = listaPrecios,
                            ClasePrecio = clasePrecio
                        },
                        transaction: tx,
                        cancellationToken: token));
                }
                else
                {
                    var affected = await cn.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE dbo.ALFACORE_CARRITOS_WEB
                        SET
                            Nombre = @Nombre,
                            Descripcion = @Descripcion,
                            Activo = @Activo,
                            OrigenArticulos = @OrigenArticulos,
                            IdListaArticulos = NULLIF(@IdListaArticulos, N''),
                            OrigenPrecios = @OrigenPrecios,
                            IdListaPrecios = NULLIF(@IdListaPrecios, N''),
                            ClasePrecio = @ClasePrecio,
                            FechaHora_Modificacion = GETDATE()
                        WHERE IdCarrito = @IdCarrito;
                        """,
                        new
                        {
                            IdCarrito = idCarrito,
                            Nombre = nombre,
                            Descripcion = Truncate(request.Descripcion, 500),
                            Activo = request.Activo,
                            OrigenArticulos = origenArticulos,
                            IdListaArticulos = listaArticulos,
                            OrigenPrecios = origenPrecios,
                            IdListaPrecios = listaPrecios,
                            ClasePrecio = clasePrecio
                        },
                        transaction: tx,
                        cancellationToken: token));

                    if (affected == 0)
                        throw new InvalidOperationException("No se encontró el carrito general indicado.");
                }

                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.ALFACORE_CARRITOS_WEB_DET WHERE IdCarrito = @IdCarrito;",
                    new { IdCarrito = idCarrito },
                    transaction: tx,
                    cancellationToken: token));

                if (articulos.Count > 0)
                {
                    var insertSql = """
                        INSERT INTO dbo.ALFACORE_CARRITOS_WEB_DET
                        (
                            IdCarrito,
                            Orden,
                            IdArticulo,
                            DescripcionArticulo,
                            Presentacion,
                            Marca,
                            Rubro
                        )
                        VALUES
                        (
                            @IdCarrito,
                            @Orden,
                            @IdArticulo,
                            @DescripcionArticulo,
                            @Presentacion,
                            @Marca,
                            @Rubro
                        );
                        """;

                    var orden = 1;
                    foreach (var articulo in articulos)
                    {
                        await cn.ExecuteAsync(new CommandDefinition(
                            insertSql,
                            new
                            {
                                IdCarrito = idCarrito,
                                Orden = orden++,
                                articulo.IdArticulo,
                                articulo.DescripcionArticulo,
                                articulo.Presentacion,
                                articulo.Marca,
                                articulo.Rubro
                            },
                            transaction: tx,
                            cancellationToken: token));
                    }
                }

                await tx.CommitAsync(token);
                return idCarrito;
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }
        }, "No se pudo guardar el carrito general.", ct);

    public Task<bool> ToggleCatalogoAsync(int idCatalogo, bool activo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleCatalogo", async token =>
        {
            if (idCatalogo <= 0)
                return false;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", token))
                return false;

            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            await UpsertConfigValueAsync(cn, detailColumn, BuildCarritoConfigKey(idCatalogo), activo ? "SI" : "NO", ConfigGroup, token);
            return true;
        }, "No se pudo actualizar el estado del carrito de catálogo.", ct);

    public Task<bool> ToggleGeneralAsync(int idCarrito, bool activo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ToggleGeneral", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "ALFACORE_CARRITOS_WEB", token))
                return false;

            var effectiveId = idCarrito > 0
                ? idCarrito
                : await ResolveGeneralIdAsync(cn, token);

            if (effectiveId <= 0)
                return false;

            var rows = await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ALFACORE_CARRITOS_WEB
                SET Activo = @Activo,
                    FechaHora_Modificacion = GETDATE()
                WHERE IdCarrito = @IdCarrito;
                """,
                new { IdCarrito = effectiveId, Activo = activo },
                cancellationToken: token));

            return rows > 0;
        }, "No se pudo actualizar el carrito general.", ct);

    public Task<CarritoComprasPrecioClienteDto> ResolvePrecioClienteAsync(string? codigoCliente, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "ResolvePrecioCliente", async token =>
        {
            var codigo = (codigoCliente ?? string.Empty).Trim();
            var result = new CarritoComprasPrecioClienteDto
            {
                CodigoCliente = codigo,
                NombreCliente = string.Empty,
                OrigenLista = CarritoComprasPrecioOrigenKeys.Maestro,
                OrigenClase = "ClaseMaestro",
                IdLista = string.Empty,
                ClasePrecio = 1,
                Origen = "MaestroPrecios"
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            if (!await TableExistsAsync(cn, "MA_CUENTASADIC", token))
                return result;

            var defaultClase = await ReadDefaultClasePrecioValueAsync(cn, token);
            var clientRow = await cn.QuerySingleOrDefaultAsync<(string Nombre, string IdLista, string Clase)>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(cli.RAZON_SOCIAL)), N'') AS Nombre,
                    ISNULL(LTRIM(RTRIM(adic.IdLista)), N'') AS IdLista,
                    ISNULL(LTRIM(RTRIM(adic.Clase)), N'') AS Clase
                FROM dbo.VT_CLIENTES cli
                LEFT JOIN dbo.MA_CUENTASADIC adic
                    ON UPPER(LTRIM(RTRIM(adic.CODIGO))) = UPPER(LTRIM(RTRIM(cli.CODIGO)))
                WHERE UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)));
                """,
                new { Codigo = codigo },
                cancellationToken: token));

            result.NombreCliente = clientRow.Nombre;
            var claseCliente = NormalizeClasePrecioValue(clientRow.Clase, defaultClase);

            if (!string.IsNullOrWhiteSpace(clientRow.IdLista))
            {
                result.IdLista = clientRow.IdLista.Trim();
                result.ClasePrecio = claseCliente;
                result.OrigenLista = "ListaCliente";
                result.OrigenClase = "ClaseCliente";
                result.Origen = "ListaCliente";
                return result;
            }

            var listaVentas = await ReadFirstSalesListAsync(cn, token);
            if (!string.IsNullOrWhiteSpace(listaVentas.IdLista))
            {
                result.IdLista = listaVentas.IdLista;
                result.ClasePrecio = claseCliente;
                result.OrigenLista = "ListaVentas";
                result.OrigenClase = "ClaseVenta";
                result.Origen = "ListaVentas";
                return result;
            }

            result.ClasePrecio = claseCliente;
            result.OrigenLista = "MaestroPrecios";
            result.OrigenClase = "ClaseVenta";
            result.Origen = "MaestroPrecios";
            return result;
        }, "No se pudo resolver el precio del cliente.", ct);

    private async Task<bool> CatalogoPublicoDisponibleAsync(int idCatalogo, string? idWeb, CancellationToken ct)
    {
        try
        {
            var publicCatalog = await catalogosService.GetCatalogoPublicoAsync(idCatalogo, ct);
            return publicCatalog is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int?> ResolveGeneralIdAsync(SqlConnection cn, CancellationToken ct, SqlTransaction? tx = null)
    {
        const string sql = """
            SELECT TOP (1) IdCarrito
            FROM dbo.ALFACORE_CARRITOS_WEB
            ORDER BY
                CASE WHEN ISNULL(Activo, 0) = 1 THEN 0 ELSE 1 END,
                IdCarrito;
            """;

        var id = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
        return id;
    }

    private async Task<IReadOnlyList<CatalogosCatalogoItemDto>> LoadGeneralItemsAsync(
        SqlConnection cn,
        CarritoComprasGeneralDetalleDto general,
        string? idWeb,
        string? codigoCliente,
        CancellationToken ct)
    {
        if (general.IdCarrito <= 0)
            return [];

        if (general.Articulos.Count == 0)
            return [];

        var publicClase = NormalizeClasePrecioValue(await catalogosService.GetPublicClasePrecioAsync(idWeb, ct), int.Parse(DefaultClasePrecio));
        var ofertaClase = await GetOfertaClasePrecioAsync(cn, ct);
        var pricing = await ResolvePrecioClienteAsync(codigoCliente, ct);
        var itemIds = general.Articulos.Select(a => a.IdArticulo.Trim()).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (itemIds.Length == 0)
            return [];

        var idLista = string.Empty;
        var clasePrecio = general.ClasePrecio ?? publicClase;
        var usarLista = false;

        if (string.Equals(general.OrigenPrecios, CarritoComprasPrecioOrigenKeys.ListaFija, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(general.IdListaPrecios))
        {
            usarLista = true;
            idLista = general.IdListaPrecios.Trim();
            clasePrecio = general.ClasePrecio ?? pricing.ClasePrecio;
        }
        else if (string.Equals(general.OrigenPrecios, CarritoComprasPrecioOrigenKeys.SegunCliente, StringComparison.OrdinalIgnoreCase))
        {
            idLista = pricing.IdLista;
            if (!string.IsNullOrWhiteSpace(idLista))
            {
                usarLista = true;
                clasePrecio = pricing.ClasePrecio;
            }
            else
            {
                clasePrecio = pricing.ClasePrecio;
            }
        }

        clasePrecio = Math.Max(1, Math.Min(clasePrecio, 8));

        var sql = usarLista
            ? $"""
                SELECT
                    d.Orden,
                    d.IdArticulo AS IdArticulo,
                    COALESCE(NULLIF(LTRIM(RTRIM(a.DESCRIPCION)), N''), NULLIF(LTRIM(RTRIM(d.DescripcionArticulo)), N''), N'') AS DescripcionArticulo,
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA1, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA2, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA3, N''))), N''),
                        NULLIF(LTRIM(RTRIM(ISNULL(a.CODIGOBARRA4, N''))), N''),
                        N''
                    ) AS CodigoBarra,
                    ISNULL(LTRIM(RTRIM(a.RutaImagen)), N'') AS RutaImagen,
                    COALESCE(NULLIF(LTRIM(RTRIM(a.Presentacion)), N''), NULLIF(LTRIM(RTRIM(d.Presentacion)), N''), N'') AS Presentacion,
                    COALESCE(NULLIF(LTRIM(RTRIM(t.Descripcion)), N''), NULLIF(LTRIM(RTRIM(d.Marca)), N''), N'') AS Marca,
                    COALESCE(NULLIF(LTRIM(RTRIM(r.Descripcion)), N''), NULLIF(LTRIM(RTRIM(d.Rubro)), N''), N'') AS Rubro,
                    ISNULL(p.Precio{clasePrecio}, 0) AS Precio,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND GETDATE() >= p.FhOfertaDesde
                         AND (p.FhOfertaHasta IS NULL OR GETDATE() <= p.FhOfertaHasta) THEN p.Precio{ofertaClase}
                        ELSE NULL
                    END AS PrecioOferta,
                    CASE
                        WHEN p.FhOfertaDesde IS NOT NULL
                         AND GETDATE() >= p.FhOfertaDesde
                         AND (p.FhOfertaHasta IS NULL OR GETDATE() <= p.FhOfertaHasta) THEN p.FhOfertaHasta
                        ELSE NULL
                    END AS OfertaHasta
                FROM dbo.ALFACORE_CARRITOS_WEB_DET d
                LEFT JOIN dbo.V_MA_ARTICULOS a
                    ON UPPER(LTRIM(RTRIM(a.IDARTICULO))) = UPPER(LTRIM(RTRIM(d.IdArticulo)))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(t.IdTipo, N''))))
                LEFT JOIN dbo.V_TA_Rubros r
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(r.IdRubro, N''))))
                LEFT JOIN dbo.V_MA_Precios p
                    ON UPPER(LTRIM(RTRIM(p.IdArticulo))) = UPPER(LTRIM(RTRIM(d.IdArticulo)))
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.IdLista, N'')))) = UPPER(LTRIM(RTRIM(@IdLista)))
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.TipoLista, N'V')))) = N'V'
                WHERE d.IdCarrito = @IdCarrito
                  AND UPPER(LTRIM(RTRIM(d.IdArticulo))) IN @Ids
                ORDER BY d.Orden, d.IdArticulo;
                """
            : $"""
                SELECT
                    d.Orden,
                    d.IdArticulo AS IdArticulo,
                    COALESCE(NULLIF(LTRIM(RTRIM(a.DESCRIPCION)), N''), NULLIF(LTRIM(RTRIM(d.DescripcionArticulo)), N''), N'') AS DescripcionArticulo,
                    COALESCE(NULLIF(LTRIM(RTRIM(a.Presentacion)), N''), NULLIF(LTRIM(RTRIM(d.Presentacion)), N''), N'') AS Presentacion,
                    COALESCE(NULLIF(LTRIM(RTRIM(t.Descripcion)), N''), NULLIF(LTRIM(RTRIM(d.Marca)), N''), N'') AS Marca,
                    COALESCE(NULLIF(LTRIM(RTRIM(r.Descripcion)), N''), NULLIF(LTRIM(RTRIM(d.Rubro)), N''), N'') AS Rubro,
                    ISNULL(a.Precio{clasePrecio}, 0) AS Precio,
                    CAST(NULL AS decimal(18,4)) AS PrecioOferta,
                    CAST(NULL AS datetime) AS OfertaHasta
                FROM dbo.ALFACORE_CARRITOS_WEB_DET d
                LEFT JOIN dbo.V_MA_ARTICULOS a
                    ON UPPER(LTRIM(RTRIM(a.IDARTICULO))) = UPPER(LTRIM(RTRIM(d.IdArticulo)))
                LEFT JOIN dbo.V_TA_TipoArticulo t
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDTIPO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(t.IdTipo, N''))))
                LEFT JOIN dbo.V_TA_Rubros r
                    ON UPPER(LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'')))) = UPPER(LTRIM(RTRIM(ISNULL(r.IdRubro, N''))))
                WHERE d.IdCarrito = @IdCarrito
                  AND UPPER(LTRIM(RTRIM(d.IdArticulo))) IN @Ids
                ORDER BY d.Orden, d.IdArticulo;
                """;

        var rows = await cn.QueryAsync<GeneralPublicCartRow>(new CommandDefinition(
            sql,
            new { IdCarrito = general.IdCarrito, IdLista = idLista, Ids = itemIds },
            cancellationToken: ct));

            return rows.Select(row => new CatalogosCatalogoItemDto
            {
                IdInsert = 0,
                IdArticulo = row.IdArticulo,
                DescripcionArticulo = row.DescripcionArticulo,
                CodigoBarra = row.CodigoBarra,
                RutaImagen = row.RutaImagen,
                Presentacion = row.Presentacion,
                Marca = row.Marca,
                Rubro = row.Rubro,
                Precio = row.Precio,
            PrecioOferta = row.PrecioOferta,
            OfertaHasta = row.OfertaHasta
        }).ToList();
    }

    private static string DescribeGeneralPriceOrigin(CarritoComprasGeneralDetalleDto general)
        => string.Equals(general.OrigenPrecios, CarritoComprasPrecioOrigenKeys.ListaFija, StringComparison.OrdinalIgnoreCase)
            ? $"Lista fija{(string.IsNullOrWhiteSpace(general.IdListaPrecios) ? string.Empty : $" {general.IdListaPrecios}")}"
            : string.Equals(general.OrigenPrecios, CarritoComprasPrecioOrigenKeys.Maestro, StringComparison.OrdinalIgnoreCase)
                ? "Maestro"
                : "Según cliente";

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

    private static string BuildSourceSql(bool hasCatalogosNuevo, bool hasCatalogosLegacy, bool hasGenerales, string detailColumn)
    {
        var parts = new List<string>();

        if (hasCatalogosNuevo)
        {
            parts.Add($"""
                SELECT
                    c.IdCatalogo AS IdReferencia,
                    c.IdCatalogo AS IdCatalogo,
                    CAST(NULL AS int) AS IdCarrito,
                    N'catalogo' AS TipoClave,
                    N'Catálogo' AS Tipo,
                    MAX(ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)), N''), CONCAT(N'Catálogo ', CONVERT(nvarchar(20), c.IdCatalogo)))) AS Nombre,
                    CASE
                        WHEN MIN(c.FechaDesde) IS NULL AND MAX(c.FechaHasta) IS NULL THEN N'Sin vigencia'
                        ELSE CONCAT(
                            ISNULL(CONVERT(nvarchar(10), MIN(c.FechaDesde), 103), N''),
                            CASE WHEN MIN(c.FechaDesde) IS NOT NULL OR MAX(c.FechaHasta) IS NOT NULL THEN N' - ' ELSE N'' END,
                            ISNULL(CONVERT(nvarchar(10), MAX(c.FechaHasta), 103), N'')
                        )
                    END AS Vigencia,
                    CASE
                        WHEN MAX(CASE WHEN ISNULL(c.Anulado, 0) = 0
                                       AND (c.FechaDesde IS NULL OR CONVERT(date, c.FechaDesde) <= CONVERT(date, GETDATE()))
                                       AND (c.FechaHasta IS NULL OR CONVERT(date, c.FechaHasta) >= CONVERT(date, GETDATE()))
                                      THEN 1 ELSE 0 END) = 1 THEN N'Activo'
                        ELSE N'Inactivo'
                    END AS Estado,
                    COUNT(d.IdArticulo) AS CantidadArticulos,
                    N'Catálogo' AS Precios,
                    CAST(CASE WHEN MAX(CASE WHEN ISNULL(c.Anulado, 0) = 0
                                              AND (c.FechaDesde IS NULL OR CONVERT(date, c.FechaDesde) <= CONVERT(date, GETDATE()))
                                              AND (c.FechaHasta IS NULL OR CONVERT(date, c.FechaHasta) >= CONVERT(date, GETDATE()))
                                             THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END AS bit) AS Activo,
                    0 AS OrdenTipo
                FROM dbo.CATALOGOS c
                LEFT JOIN dbo.CATALOGOS_DETALLE d
                    ON d.IdCatalogo = c.IdCatalogo
                GROUP BY c.IdCatalogo, c.Nombre, c.FechaDesde, c.FechaHasta, c.Anulado
                """);
        }

        if (hasCatalogosLegacy)
        {
            parts.Add($"""
                SELECT
                    c.IDINSERT AS IdReferencia,
                    c.IDINSERT AS IdCatalogo,
                    CAST(NULL AS int) AS IdCarrito,
                    N'catalogo' AS TipoClave,
                    N'Catálogo' AS Tipo,
                    MAX(ISNULL(NULLIF(LTRIM(RTRIM(c.GRUPO)), N''), CONCAT(N'Catálogo ', CONVERT(nvarchar(20), c.IDINSERT)))) AS Nombre,
                    CASE
                        WHEN MIN(c.VigenciaDesde) IS NULL AND MAX(c.VigenciaHasta) IS NULL THEN N'Sin vigencia'
                        ELSE CONCAT(
                            ISNULL(CONVERT(nvarchar(10), MIN(c.VigenciaDesde), 103), N''),
                            CASE WHEN MIN(c.VigenciaDesde) IS NOT NULL OR MAX(c.VigenciaHasta) IS NOT NULL THEN N' - ' ELSE N'' END,
                            ISNULL(CONVERT(nvarchar(10), MAX(c.VigenciaHasta), 103), N'')
                        )
                    END AS Vigencia,
                    CASE
                        WHEN MAX(CASE WHEN ISNULL(c.FINALIZADO, 0) = 0 THEN 1 ELSE 0 END) = 1 THEN N'Activo'
                        ELSE N'Inactivo'
                    END AS Estado,
                    COUNT(1) AS CantidadArticulos,
                    N'Catálogo' AS Precios,
                    CAST(CASE WHEN MAX(CASE WHEN ISNULL(c.FINALIZADO, 0) = 0 THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END AS bit) AS Activo,
                    0 AS OrdenTipo
                FROM dbo.V_MV_INSERT c
                GROUP BY c.IDINSERT
                """);
        }

        if (hasGenerales)
        {
            parts.Add("""
                SELECT
                    g.IdCarrito AS IdReferencia,
                    CAST(NULL AS int) AS IdCatalogo,
                    g.IdCarrito AS IdCarrito,
                    N'general' AS TipoClave,
                    N'General' AS Tipo,
                    ISNULL(LTRIM(RTRIM(g.Nombre)), N'') AS Nombre,
                    N'Sin vigencia' AS Vigencia,
                    CASE WHEN ISNULL(g.Activo, 0) = 1 THEN N'Activo' ELSE N'Inactivo' END AS Estado,
                    COUNT(d.IdArticulo) AS CantidadArticulos,
                    CASE
                        WHEN g.OrigenPrecios = N'segun-cliente' THEN N'Según cliente'
                        WHEN g.OrigenPrecios = N'lista-fija' THEN CONCAT(N'Lista fija', CASE WHEN ISNULL(g.IdListaPrecios, N'') <> N'' THEN CONCAT(N' ', g.IdListaPrecios) ELSE N'' END, CASE WHEN g.ClasePrecio IS NOT NULL THEN CONCAT(N' / Clase ', CONVERT(nvarchar(10), g.ClasePrecio)) ELSE N'' END)
                        WHEN g.OrigenPrecios = N'maestro' THEN CONCAT(N'Maestro', CASE WHEN g.ClasePrecio IS NOT NULL THEN CONCAT(N' / Clase ', CONVERT(nvarchar(10), g.ClasePrecio)) ELSE N'' END)
                        ELSE N'Según cliente'
                    END AS Precios,
                    CAST(ISNULL(g.Activo, 0) AS bit) AS Activo,
                    1 AS OrdenTipo
                FROM dbo.ALFACORE_CARRITOS_WEB g
                LEFT JOIN dbo.ALFACORE_CARRITOS_WEB_DET d
                    ON d.IdCarrito = g.IdCarrito
                GROUP BY g.IdCarrito, g.Nombre, g.Activo, g.OrigenPrecios, g.IdListaPrecios, g.ClasePrecio
                """);
        }

        return string.Join("\nUNION ALL\n", parts);
    }

    private async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALOR_AUX') IS NOT NULL THEN N'VALOR_AUX'
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'VALORAUX') IS NOT NULL THEN N'VALORAUX'
                WHEN COL_LENGTH(N'dbo.TA_CONFIGURACION', N'DESCRIPCION') IS NOT NULL THEN N'DESCRIPCION'
                ELSE N'VALOR_AUX'
            END;
            """;

        var column = await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(column) ? "VALOR_AUX" : column;
    }

    private async Task<int> ReadDefaultClasePrecioValueAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct))
            return int.Parse(DefaultClasePrecio);

        var detailColumn = await ResolveConfigDetailColumnAsync(cn, ct);
        var sql = $"""
            SELECT TOP (1)
                ISNULL(VALOR, N'') AS Valor,
                ISNULL({detailColumn}, N'') AS ValorAux
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CLASEDEPRECIODEFAULT';
            """;

        var row = await cn.QuerySingleOrDefaultAsync<(string Valor, string ValorAux)>(new CommandDefinition(sql, cancellationToken: ct));
        var raw = ResolveStoredValue(row.Valor, row.ValorAux);
        return NormalizeClasePrecioValue(raw, int.Parse(DefaultClasePrecio));
    }

    private async Task<(string IdLista, string Nombre)> ReadFirstSalesListAsync(SqlConnection cn, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "V_MA_PreciosCab", ct))
            return (string.Empty, string.Empty);

        var row = await cn.QuerySingleOrDefaultAsync<(string IdLista, string Nombre)>(new CommandDefinition(
            """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(IdLista)), N'') AS IdLista,
                ISNULL(LTRIM(RTRIM(Nombre)), N'') AS Nombre
            FROM dbo.V_MA_PreciosCab
            WHERE UPPER(LTRIM(RTRIM(ISNULL(TipoLista, N'V')))) = N'V'
            ORDER BY
                CASE
                    WHEN (VigenciaDesde IS NULL OR VigenciaDesde <= GETDATE())
                     AND (VigenciaHasta IS NULL OR VigenciaHasta >= GETDATE()) THEN 0
                    ELSE 1
                END,
                Nombre,
                IdLista;
            """,
            cancellationToken: ct));

        return (row.IdLista.Trim(), row.Nombre.Trim());
    }

    private static int NormalizeClasePrecioValue(string? value, int fallback)
        => int.TryParse((value ?? string.Empty).Trim(), out var parsed) && parsed is >= 1 and <= 8
            ? parsed
            : fallback;

    private static string ResolveStoredValue(string valor, string valorAux)
        => !string.IsNullOrWhiteSpace(valor) ? valor : valorAux;

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

    private async Task<bool> TableExistsAsync(SqlConnection cn, string tableName, CancellationToken ct)
        => await SqlObjectExistsAsync(cn, tableName, ct);

    private static string BuildCarritoConfigKey(int idCatalogo)
        => $"{CarritoHabilitadoConfigKeyPrefix}-{idCatalogo}".ToUpperInvariant();

    private static string NormalizeFilter(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeArticuloOrigin(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            CarritoComprasArticuloOrigenKeys.Maestro => CarritoComprasArticuloOrigenKeys.Maestro,
            CarritoComprasArticuloOrigenKeys.Lista => CarritoComprasArticuloOrigenKeys.Lista,
            _ => CarritoComprasArticuloOrigenKeys.Maestro
        };
    }

    private static string NormalizePrecioOrigin(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            CarritoComprasPrecioOrigenKeys.SegunCliente => CarritoComprasPrecioOrigenKeys.SegunCliente,
            CarritoComprasPrecioOrigenKeys.ListaFija => CarritoComprasPrecioOrigenKeys.ListaFija,
            CarritoComprasPrecioOrigenKeys.Maestro => CarritoComprasPrecioOrigenKeys.Maestro,
            _ => CarritoComprasPrecioOrigenKeys.SegunCliente
        };
    }

    private static string Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : (value.Trim().Length <= maxLength ? value.Trim() : value.Trim()[..maxLength]);

    private static object DbNullable(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : Truncate(value, maxLength);

    private static string LikeContains(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : $"%{normalized.ToUpperInvariant()}%";
    }

    private sealed class CarritoResumenRow
    {
        public int IdReferencia { get; set; }
        public int? IdCatalogo { get; set; }
        public int? IdCarrito { get; set; }
        public string TipoClave { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Vigencia { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public int CantidadArticulos { get; set; }
        public string Precios { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public int OrdenTipo { get; set; }
    }

    private sealed class GeneralPublicCartRow
    {
        public int Orden { get; set; }
        public string IdArticulo { get; set; } = string.Empty;
        public string DescripcionArticulo { get; set; } = string.Empty;
        public string CodigoBarra { get; set; } = string.Empty;
        public string RutaImagen { get; set; } = string.Empty;
        public string Presentacion { get; set; } = string.Empty;
        public string Marca { get; set; } = string.Empty;
        public string Rubro { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public decimal? PrecioOferta { get; set; }
        public DateTime? OfertaHasta { get; set; }
    }
}

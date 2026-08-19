using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Lista de precios del Portal Cliente. Centraliza la resolución de lista + clase de precio del
// cliente autenticado (sin repetirla en el Razor) reutilizando exactamente los mismos objetos y el
// mismo criterio que ya usa Alfa Gestión:
//   - Lista/clase propias del cliente: dbo.VT_CLIENTES.IdLista / .Clase (mismas columnas que ya usa
//     CuentasComercialesService para la ficha del cliente).
//   - Clase general de ventas: TA_CONFIGURACION.CLASEDEPRECIODEFAULT (mismo criterio que
//     PuntoVentaService.ResolvePricingContextAsync).
//   - Lista general de ventas: no existe una clave fija en TA_CONFIGURACION para esto (se verificó
//     por SQL); Alfa Gestión la resuelve dinámicamente como la primera lista vigente
//     dbo.V_MA_PreciosCab con TipoLista='V' — mismo algoritmo que ya usa
//     PuntoVentaService.ResolvePricingContextAsync para el punto de venta.
//   - Sin lista en absoluto: precio maestro dbo.V_MA_ARTICULOS.Precio{clase} (mismo patrón "Origen
//     maestro" que ya usa InterfacesCatalogosService.SearchArticulosAsync).
// No se crea ninguna tabla ni vista nueva.
public sealed class ListaPreciosClienteService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IAppEventService appEvents) : IListaPreciosClienteService
{
    private const string ModuleName = "PortalClienteListaPrecios";
    private const string ClaseDefaultConfigKey = "CLASEDEPRECIODEFAULT";
    private const string ClaseDefaultFallback = "1";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // Tope de seguridad para exportaciones (Excel/PDF no paginan, pero no deben poder pedir todo el
    // maestro de artículos sin límite).
    private const int ExportMaxRows = 5000;

    public Task<ListaPreciosResultadoDto> BuscarAsync(ListaPreciosBusquedaFiltroDto filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Buscar", async token =>
        {
            ArgumentNullException.ThrowIfNull(filtro);

            var codigoCliente = (filtro.CodigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoCliente))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            var pageNumber = filtro.PageNumber <= 0 ? 1 : filtro.PageNumber;
            var pageSize = filtro.PageSize <= 0 ? 50 : Math.Min(filtro.PageSize, 50);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var resolucion = await ResolverListaYClaseAsync(cn, codigoCliente, token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
            {
                return new ListaPreciosResultadoDto
                {
                    Resolucion = resolucion,
                    Pagina = new PagedResult<ListaPreciosArticuloDto> { PageNumber = pageNumber, PageSize = pageSize }
                };
            }

            var (rows, _) = await BuscarArticulosAsync(cn, resolucion, filtro.Texto, filtro.AgruparPor, skip, pageSize, token);

            var total = rows.FirstOrDefault()?.TotalRows ?? 0;
            var items = rows.Select(MapArticulo).ToList();

            return new ListaPreciosResultadoDto
            {
                Resolucion = resolucion,
                Pagina = new PagedResult<ListaPreciosArticuloDto>
                {
                    Items = items,
                    Total = total,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                }
            };
        }, "No pudimos cargar la lista de precios en este momento.", ct);

    public Task<ListaPreciosExportDto> ObtenerParaExportarAsync(ListaPreciosBusquedaFiltroDto filtro, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "Exportar", async token =>
        {
            ArgumentNullException.ThrowIfNull(filtro);

            var codigoCliente = (filtro.CodigoCliente ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoCliente))
                throw new InvalidOperationException("No se pudo identificar al cliente. Volvé a iniciar sesión.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var resolucion = await ResolverListaYClaseAsync(cn, codigoCliente, token);

            if (!await SqlObjectExistsAsync(cn, "V_MA_ARTICULOS", token))
                return new ListaPreciosExportDto { Resolucion = resolucion };

            var (rows, _) = await BuscarArticulosAsync(cn, resolucion, filtro.Texto, filtro.AgruparPor, skip: 0, pageSize: ExportMaxRows, token);

            var total = rows.FirstOrDefault()?.TotalRows ?? 0;
            return new ListaPreciosExportDto
            {
                Resolucion = resolucion,
                Articulos = rows.Select(MapArticulo).ToList(),
                Truncado = total > ExportMaxRows
            };
        }, "No pudimos exportar la lista de precios en este momento.", ct);

    private static ListaPreciosArticuloDto MapArticulo(ArticuloRow r) => new()
    {
        IdArticulo = r.IdArticulo,
        Descripcion = r.Descripcion,
        Presentacion = r.Presentacion,
        Marca = r.Marca,
        Familia = r.Familia,
        Rubro = r.Rubro,
        Precio = r.Precio
    };

    private async Task<(List<ArticuloRow> Rows, bool UsarLista)> BuscarArticulosAsync(
        SqlConnection cn,
        ListaPreciosResolucionDto resolucion,
        string? texto,
        string? agruparPor,
        int skip,
        int pageSize,
        CancellationToken ct)
    {
        var clase = resolucion.Clase;
        var textoLike = LikeContains(texto);
        var orderBy = BuildOrderBy(agruparPor);

        var usarLista = !resolucion.UsaMaestro && !string.IsNullOrWhiteSpace(resolucion.IdLista)
            && await SqlObjectExistsAsync(cn, "V_MA_Precios", ct);
        var tieneFamilias = await SqlObjectExistsAsync(cn, "V_TA_FAMILIAS", ct);

        var familiaJoin = tieneFamilias
            ? "LEFT JOIN dbo.V_TA_FAMILIAS f ON LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))) = LTRIM(RTRIM(f.IdFamilia))"
            : string.Empty;
        var familiaSelect = tieneFamilias ? "ISNULL(LTRIM(RTRIM(f.Descripcion)), '')" : "''";

        var sql = usarLista
            ? $"""
            SELECT
                a.IDARTICULO AS IdArticulo,
                ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS Descripcion,
                ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                {familiaSelect} AS Familia,
                ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                ISNULL(p.Precio{clase}, 0) AS Precio,
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
              AND (
                    @TextoLike = ''
                    OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                  )
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
            """
            : $"""
            SELECT
                a.IDARTICULO AS IdArticulo,
                ISNULL(LTRIM(RTRIM(a.DESCRIPCION)), '') AS Descripcion,
                ISNULL(LTRIM(RTRIM(a.Presentacion)), '') AS Presentacion,
                ISNULL(LTRIM(RTRIM(t.Descripcion)), '') AS Marca,
                {familiaSelect} AS Familia,
                ISNULL(LTRIM(RTRIM(r.Descripcion)), '') AS Rubro,
                ISNULL(a.Precio{clase}, 0) AS Precio,
                COUNT(1) OVER() AS TotalRows
            FROM dbo.V_MA_ARTICULOS a
            LEFT JOIN dbo.V_TA_Rubros r
                ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
            LEFT JOIN dbo.V_TA_TipoArticulo t
                ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
            {familiaJoin}
            WHERE ISNULL(a.Suspendido, 0) <> 1
              AND ISNULL(a.SuspendidoV, 0) <> 1
              AND (
                    @TextoLike = ''
                    OR UPPER(LTRIM(RTRIM(a.IDARTICULO))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.DESCRIPCION, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(a.Presentacion, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(t.Descripcion, '')))) LIKE @TextoLike
                    OR UPPER(LTRIM(RTRIM(ISNULL(r.Descripcion, '')))) LIKE @TextoLike
                  )
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var rows = (await cn.QueryAsync<ArticuloRow>(new CommandDefinition(
            sql,
            new { IdLista = resolucion.IdLista ?? string.Empty, TextoLike = textoLike, Skip = skip, PageSize = pageSize },
            cancellationToken: ct))).ToList();

        return (rows, usarLista);
    }

    private static string BuildOrderBy(string? agruparPor)
        => agruparPor switch
        {
            ListaPreciosAgruparPorKeys.Familia => "CASE WHEN ISNULL(LTRIM(RTRIM(f.Descripcion)), '') = '' THEN 1 ELSE 0 END, f.Descripcion, a.DESCRIPCION, a.IDARTICULO",
            ListaPreciosAgruparPorKeys.Marca => "CASE WHEN ISNULL(LTRIM(RTRIM(t.Descripcion)), '') = '' THEN 1 ELSE 0 END, t.Descripcion, a.DESCRIPCION, a.IDARTICULO",
            _ => "a.DESCRIPCION, a.IDARTICULO"
        };

    private async Task<ListaPreciosResolucionDto> ResolverListaYClaseAsync(SqlConnection cn, string codigoCliente, CancellationToken ct)
    {
        var cliente = await cn.QuerySingleOrDefaultAsync<ClienteListaClaseRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                ISNULL(Clase, 0) AS Clase
            FROM dbo.VT_CLIENTES
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(LTRIM(RTRIM(@Codigo)))
              AND ISNULL(Dada_De_Baja, 0) = 0;
            """,
            new { Codigo = codigoCliente },
            cancellationToken: ct));

        var resolucion = new ListaPreciosResolucionDto();

        // Clase: la del cliente si tiene una configurada (>0); si no, la clase general de ventas.
        if (cliente is { Clase: > 0 })
        {
            resolucion.Clase = ParseClasePrecio(cliente.Clase.ToString());
            resolucion.ClaseDesdeCliente = true;
        }
        else
        {
            var claseSistema = await SqlObjectExistsAsync(cn, "TA_CONFIGURACION", ct)
                ? await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                    """
                    SELECT TOP (1)
                        CASE
                            WHEN ISNULL(LTRIM(RTRIM(VALOR)), '') <> '' THEN LTRIM(RTRIM(VALOR))
                            WHEN ISNULL(CAST(ValorAux AS nvarchar(150)), '') <> '' THEN LTRIM(RTRIM(CAST(ValorAux AS nvarchar(150))))
                            ELSE ''
                        END
                    FROM dbo.TA_CONFIGURACION
                    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                    """,
                    new { Clave = ClaseDefaultConfigKey },
                    cancellationToken: ct))
                : null;

            resolucion.Clase = ParseClasePrecio(string.IsNullOrWhiteSpace(claseSistema) ? ClaseDefaultFallback : claseSistema);
            resolucion.ClaseDesdeCliente = false;
        }

        // Lista: la del cliente si tiene una asignada; si no, la primera lista de ventas vigente
        // (mismo criterio que PuntoVentaService.ResolvePricingContextAsync); si no hay ninguna, maestro.
        if (cliente is not null && !string.IsNullOrWhiteSpace(cliente.IdLista))
        {
            resolucion.IdLista = cliente.IdLista.Trim();
            resolucion.ListaDesdeCliente = true;
            resolucion.NombreLista = await ObtenerNombreListaAsync(cn, resolucion.IdLista, ct);
        }
        else if (await SqlObjectExistsAsync(cn, "V_MA_PreciosCab", ct))
        {
            var listaSistema = await cn.QuerySingleOrDefaultAsync<ListaRow>(new CommandDefinition(
                """
                SELECT TOP (1)
                    ISNULL(LTRIM(RTRIM(IdLista)), '') AS IdLista,
                    ISNULL(LTRIM(RTRIM(Nombre)), '') AS Nombre
                FROM dbo.V_MA_PreciosCab
                WHERE TipoLista = 'V'
                ORDER BY
                    CASE
                        WHEN (VigenciaDesde IS NULL OR VigenciaDesde <= GETDATE())
                         AND (VigenciaHasta IS NULL OR VigenciaHasta >= GETDATE()) THEN 0
                        ELSE 1
                    END,
                    IdLista;
                """,
                cancellationToken: ct));

            if (listaSistema is not null && !string.IsNullOrWhiteSpace(listaSistema.IdLista))
            {
                resolucion.IdLista = listaSistema.IdLista;
                resolucion.NombreLista = listaSistema.Nombre;
                resolucion.ListaDesdeCliente = false;
            }
            else
            {
                resolucion.UsaMaestro = true;
            }
        }
        else
        {
            resolucion.UsaMaestro = true;
        }

        return resolucion;
    }

    private static async Task<string?> ObtenerNombreListaAsync(SqlConnection cn, string idLista, CancellationToken ct)
    {
        if (!await SqlObjectExistsAsync(cn, "V_MA_PreciosCab", ct))
            return null;

        return await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT TOP (1) LTRIM(RTRIM(Nombre))
            FROM dbo.V_MA_PreciosCab
            WHERE UPPER(LTRIM(RTRIM(IdLista))) = UPPER(LTRIM(RTRIM(@IdLista)));
            """,
            new { IdLista = idLista },
            cancellationToken: ct));
    }

    private static int ParseClasePrecio(string? value)
        => int.TryParse((value ?? string.Empty).Trim(), out var parsed) && parsed is >= 1 and <= 8 ? parsed : 1;

    private static string LikeContains(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : $"%{normalized.ToUpperInvariant()}%";
    }

    private static async Task<bool> SqlObjectExistsAsync(SqlConnection cn, string objectName, CancellationToken ct)
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

    private sealed class ClienteListaClaseRow
    {
        public string IdLista { get; set; } = string.Empty;
        public int Clase { get; set; }
    }

    private sealed class ListaRow
    {
        public string IdLista { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    private sealed class ArticuloRow
    {
        public string IdArticulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Presentacion { get; set; } = string.Empty;
        public string Marca { get; set; } = string.Empty;
        public string Familia { get; set; } = string.Empty;
        public string Rubro { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int TotalRows { get; set; }
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
                module, action, validationEx, validationEx.Message,
                new { Usuario = appUserSession.GetCurrentUserName(Environment.UserName), SesionSql = sessionService.GetActiveSession()?.Nombre },
                AppEventSeverity.Warning, ct);
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

            throw new AppUserFacingException(friendlyMessage, incidentId, ex);
        }
    }
}

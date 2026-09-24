using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace AlfaCore.Services;

// MVP del maestro de Artículos -- ver plan en
// C:\Users\albert\.claude\plans\fluttering-drifting-moonbeam.md ("Módulo Archivos > Maestros >
// Artículos"). Columnas de V_MA_ARTICULOS/v_ta_rubros/v_ta_tipoArticulo/v_ta_unidad verificadas contra
// una base real (ALFANET2007) antes de escribir este servicio -- no se asumió ninguna estructura.
// Alcance: datos básicos + un único precio de venta (la clase que indique Cfg("ClasePrecioVenta")),
// calcado del formulario legacy FrmArtAlta.frm pero sin la sección "Precios de referencia" (web
// service externo) ni la replicación multi-empresa (ActualizaGEmpresas), que el usuario confirmó que
// ya no se usa.
public sealed class ArticulosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IArticuloImagenFtpService imagenFtpService,
    ICentralBasesService centralBasesService) : IArticulosService
{
    private const string ModuleName = "Articulos";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PagedResult<ArticuloGridItemDto>> SearchAsync(ArticuloFilters filters, CancellationToken ct = default)
        => ExecuteLoggedAsync("Search", async token =>
        {
            filters ??= new ArticuloFilters();
            var pageSize = Math.Max(1, Math.Min(filters.PageSize, 200));
            var pageNumber = Math.Max(1, filters.PageNumber);
            var skip = (pageNumber - 1) * pageSize;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var precioColumn = await ResolvePrecioVentaColumnAsync(cn, token);

            var sql = $"""
                SELECT
                    LTRIM(RTRIM(a.IDARTICULO)) AS Codigo,
                    ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)), '') AS CodigoBarra,
                    ISNULL(a.DESCRIPCION, '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(a.IDRUBRO)), '') AS RubroCodigo,
                    ISNULL(r.Descripcion, '') AS RubroDescripcion,
                    ISNULL(LTRIM(RTRIM(a.IDTIPO)), '') AS MarcaCodigo,
                    ISNULL(t.Descripcion, '') AS MarcaDescripcion,
                    ISNULL(LTRIM(RTRIM(a.IDUNIDAD)), '') AS UnidadCodigo,
                    ISNULL(u.Descripcion, '') AS UnidadDescripcion,
                    ISNULL(a.COSTO, 0) AS Costo,
                    ISNULL(a.{precioColumn}, 0) AS Precio,
                    ISNULL(a.TasaIVA, 0) AS TasaIva,
                    ISNULL(a.EXENTO, 0) AS Exento,
                    ISNULL(a.Pesable, 0) AS Pesable,
                    CASE WHEN ISNULL(a.SUSPENDIDO, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Activo
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.v_ta_rubros r ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.v_ta_tipoArticulo t ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                LEFT JOIN dbo.v_ta_unidad u ON LTRIM(RTRIM(ISNULL(a.IDUNIDAD, ''))) = LTRIM(RTRIM(u.IdUnidad))
                WHERE (
                        @TextoLike = ''
                        OR ISNULL(a.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(a.IDARTICULO, '') LIKE @TextoLike
                        OR ISNULL(a.CODIGOBARRA, '') LIKE @TextoLike
                      )
                  AND (@RubroCodigo = '' OR LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = @RubroCodigo)
                  AND (@MarcaCodigo = '' OR LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = @MarcaCodigo)
                  AND (@Activo IS NULL OR CASE WHEN ISNULL(a.SUSPENDIDO, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @Activo)
                ORDER BY
                    CASE WHEN ISNULL(a.SUSPENDIDO, 0) = 0 THEN 0 ELSE 1 END,
                    ISNULL(a.DESCRIPCION, ''),
                    LTRIM(RTRIM(a.IDARTICULO))
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT COUNT(*)
                FROM dbo.V_MA_ARTICULOS a
                WHERE (
                        @TextoLike = ''
                        OR ISNULL(a.DESCRIPCION, '') COLLATE Latin1_General_CI_AI LIKE @TextoLike
                        OR ISNULL(a.IDARTICULO, '') LIKE @TextoLike
                        OR ISNULL(a.CODIGOBARRA, '') LIKE @TextoLike
                      )
                  AND (@RubroCodigo = '' OR LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = @RubroCodigo)
                  AND (@MarcaCodigo = '' OR LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = @MarcaCodigo)
                  AND (@Activo IS NULL OR CASE WHEN ISNULL(a.SUSPENDIDO, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = @Activo);
                """;

            var parameters = new
            {
                TextoLike = SearchTextHelper.LikeContains(filters.Texto),
                RubroCodigo = (filters.RubroCodigo ?? string.Empty).Trim().ToUpperInvariant(),
                MarcaCodigo = (filters.MarcaCodigo ?? string.Empty).Trim().ToUpperInvariant(),
                filters.Activo,
                Skip = skip,
                PageSize = pageSize
            };

            await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: token));
            var items = (await multi.ReadAsync<ArticuloGridItemDto>()).ToList();
            var total = await multi.ReadSingleAsync<int>();

            return new PagedResult<ArticuloGridItemDto>
            {
                Items = items,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }, "No se pudieron cargar los artículos.", ct);

    public Task<ArticuloDetailDto?> GetByIdAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetById", async token =>
        {
            var normalizado = (codigo ?? string.Empty).Trim();
            if (normalizado.Length == 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var precioColumn = await ResolvePrecioVentaColumnAsync(cn, token);

            var sql = $"""
                SELECT TOP (1)
                    LTRIM(RTRIM(a.IDARTICULO)) AS Codigo,
                    LTRIM(RTRIM(a.IDARTICULO)) AS CodigoOriginal,
                    ISNULL(LTRIM(RTRIM(a.CODIGOBARRA)), '') AS CodigoBarra,
                    ISNULL(a.DESCRIPCION, '') AS Descripcion,
                    ISNULL(LTRIM(RTRIM(a.IDRUBRO)), '') AS RubroCodigo,
                    ISNULL(r.Descripcion, '') AS RubroDescripcion,
                    ISNULL(LTRIM(RTRIM(a.IDTIPO)), '') AS MarcaCodigo,
                    ISNULL(t.Descripcion, '') AS MarcaDescripcion,
                    ISNULL(LTRIM(RTRIM(a.IDUNIDAD)), '') AS UnidadCodigo,
                    ISNULL(u.Descripcion, '') AS UnidadDescripcion,
                    ISNULL(LTRIM(RTRIM(a.CUENTAPROVEEDOR)), '') AS CuentaProveedor,
                    ISNULL(LTRIM(RTRIM(p.RAZON_SOCIAL)), '') AS CuentaProveedorNombre,
                    ISNULL(LTRIM(RTRIM(a.CodigoArtProveedor)), '') AS CodigoArtProveedor,
                    ISNULL(a.Pesable, 0) AS Pesable,
                    ISNULL(a.COSTO, 0) AS Costo,
                    ISNULL(a.{precioColumn}, 0) AS Precio,
                    ISNULL(a.UTILIDAD, 0) AS Utilidad,
                    ISNULL(a.TasaIVA, 0) AS TasaIva,
                    ISNULL(a.EXENTO, 0) AS Exento,
                    CASE WHEN ISNULL(a.SUSPENDIDO, 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Activo,
                    a.FHALTA AS FechaAlta
                FROM dbo.V_MA_ARTICULOS a
                LEFT JOIN dbo.v_ta_rubros r ON LTRIM(RTRIM(ISNULL(a.IDRUBRO, ''))) = LTRIM(RTRIM(r.IdRubro))
                LEFT JOIN dbo.v_ta_tipoArticulo t ON LTRIM(RTRIM(ISNULL(a.IDTIPO, ''))) = LTRIM(RTRIM(t.IdTipo))
                LEFT JOIN dbo.v_ta_unidad u ON LTRIM(RTRIM(ISNULL(a.IDUNIDAD, ''))) = LTRIM(RTRIM(u.IdUnidad))
                LEFT JOIN dbo.vt_proveedores p ON LTRIM(RTRIM(ISNULL(a.CUENTAPROVEEDOR, ''))) = LTRIM(RTRIM(p.CODIGO))
                WHERE LTRIM(RTRIM(a.IDARTICULO)) = @Codigo;
                """;

            var rows = await cn.QueryAsync<ArticuloDetailDto>(new CommandDefinition(sql, new { Codigo = normalizado }, cancellationToken: token));
            return rows.FirstOrDefault();
        }, "No se pudo cargar el artículo.", ct);

    public Task<ArticuloLookupDataDto> GetLookupDataAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetLookupData", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var rubros = await cn.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdRubro)) AS Codigo, ISNULL(Descripcion, '') AS Descripcion FROM dbo.v_ta_rubros ORDER BY Descripcion;",
                cancellationToken: token));
            var marcas = await cn.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdTipo)) AS Codigo, ISNULL(Descripcion, '') AS Descripcion FROM dbo.v_ta_tipoArticulo ORDER BY Descripcion;",
                cancellationToken: token));
            var unidades = await cn.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdUnidad)) AS Codigo, ISNULL(Descripcion, '') AS Descripcion FROM dbo.v_ta_unidad ORDER BY Descripcion;",
                cancellationToken: token));

            var cfg = await ReadConfigMapAsync(cn, ["PIVA", "MAESTROARTICULOCONIVA", "RETAIL"], token);

            return new ArticuloLookupDataDto
            {
                Rubros = rubros.ToList(),
                Marcas = marcas.ToList(),
                Unidades = unidades.ToList(),
                TasaIvaDefault = ParseDecimal(Get(cfg, "PIVA")),
                PrecioIncluyeIva = ParseBool(Get(cfg, "MAESTROARTICULOCONIVA")),
                ModoRetail = ParseBool(Get(cfg, "RETAIL"))
            };
        }, "No se pudieron cargar las referencias de artículos.", ct);

    public Task<IReadOnlyList<ArticuloLookupOptionDto>> SearchProveedoresAsync(string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync("SearchProveedores", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var like = SearchTextHelper.LikeContains(texto);
            var rows = await cn.QueryAsync<ArticuloLookupOptionDto>(new CommandDefinition("""
                SELECT TOP (20) LTRIM(RTRIM(CODIGO)) AS Codigo, ISNULL(RAZON_SOCIAL, '') AS Descripcion
                FROM dbo.vt_proveedores
                WHERE ISNULL(Dada_De_Baja, 0) = 0
                  AND (
                        @Like = ''
                        OR ISNULL(RAZON_SOCIAL, '') COLLATE Latin1_General_CI_AI LIKE @Like
                        OR ISNULL(CODIGO, '') LIKE @Like
                      )
                ORDER BY RAZON_SOCIAL;
                """, new { Like = like }, cancellationToken: token));
            return (IReadOnlyList<ArticuloLookupOptionDto>)rows.ToList();
        }, "No se pudieron buscar proveedores.", ct);

    public Task<string> GetImagenUrlAsync(string codigo, bool forzarRecarga = false, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetImagenUrl", async token =>
        {
            var normalizado = (codigo ?? string.Empty).Trim();
            if (normalizado.Length == 0)
                return string.Empty;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(cn, token);
            if (string.IsNullOrWhiteSpace(ftpCodigoCta))
                return string.Empty;

            var idBase = sessionService.GetActiveSession()?.BaseId;
            return ArticuloImagenUrlHelper.BuildPublicImageUrl(ftpCodigoCta, normalizado, idBase, thumbnail: false, forzarRecarga: forzarRecarga);
        }, "No se pudo resolver la imagen del artículo.", ct);

    public Task<string> GetFtpCodigoCtaAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("GetFtpCodigoCta", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            return await ResolveFtpCodigoCtaAsync(cn, token);
        }, "No se pudo resolver el código de cliente FTP.", ct);

    public Task UploadImagenAsync(string codigo, byte[] contenido, string extension, CancellationToken ct = default)
        => ExecuteLoggedAsync("UploadImagen", async token =>
        {
            var normalizado = (codigo ?? string.Empty).Trim();
            if (normalizado.Length == 0)
                throw new InvalidOperationException("Falta el código del artículo.");
            if (contenido is null || contenido.Length == 0)
                throw new InvalidOperationException("La imagen está vacía.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var ftpCodigoCta = await ResolveFtpCodigoCtaAsync(cn, token);
            if (string.IsNullOrWhiteSpace(ftpCodigoCta))
                throw new InvalidOperationException("No se pudo determinar el código de cliente para subir la imagen (clave FTP_CODIGOCTA vacía y no se pudo resolver desde la base central).");

            var idBase = sessionService.GetActiveSession()?.BaseId;
            var ext = (extension ?? string.Empty).TrimStart('.').Trim().ToLowerInvariant();
            if (ext.Length == 0)
                ext = "jpg";

            // Mismos bytes para tamaño completo y thumbnail -- sin resize server-side, igual que hace
            // BaseMaestraImagenService.AsignarImagenesAsync para una asignación manual.
            await using (var full = new MemoryStream(contenido, writable: false))
                await imagenFtpService.SubirImagenAsync(ftpCodigoCta, idBase, normalizado, ext, full, thumbnail: false, token);
            await using (var thumb = new MemoryStream(contenido, writable: false))
                await imagenFtpService.SubirImagenAsync(ftpCodigoCta, idBase, normalizado, ext, thumb, thumbnail: true, token);

            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.V_MA_ARTICULOS SET ModificoImagen = 'S' WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;",
                new { Codigo = normalizado }, cancellationToken: token));
        }, "No se pudo subir la imagen del artículo.", ct);

    public Task<string> SaveAsync(ArticuloSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync("Save", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var descripcion = (request.Descripcion ?? string.Empty).Trim();
            var codigoCrudo = (request.Codigo ?? string.Empty).Trim();
            if (codigoCrudo.Length == 0)
                throw new InvalidOperationException("Ingresá el código de artículo.");
            if (descripcion.Length == 0)
                throw new InvalidOperationException("Ingresá la descripción del artículo.");

            // Código-PK de texto (IDARTICULO nvarchar(25)): numérico a la derecha, alfanumérico a la
            // izquierda -- misma regla que el resto de los maestros del sistema (ver AGENTS.md).
            var codigo = CodigoPk.Format(codigoCrudo, 25);
            var codigoBarra = CodigoPk.Format(request.CodigoBarra, 25);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var precioColumn = await ResolvePrecioVentaColumnAsync(cn, token);

            var esAlta = string.IsNullOrWhiteSpace(request.CodigoOriginal);
            if (esAlta)
            {
                var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.V_MA_ARTICULOS WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;",
                    new { Codigo = codigoCrudo }, cancellationToken: token));
                if (existe > 0)
                    throw new InvalidOperationException($"Ya existe un artículo con el código {codigoCrudo}.");
            }

            var rubroCodigo = CodigoPk.Format(
                await ResolveOrCreateLookupCodeAsync(cn, "v_ta_rubros", "IdRubro", request.RubroCodigo, request.RubroDescripcion, token), 4);
            var marcaCodigo = CodigoPk.Format(
                await ResolveOrCreateLookupCodeAsync(cn, "v_ta_tipoArticulo", "IdTipo", request.MarcaCodigo, request.MarcaDescripcion, token), 4);
            var unidadCodigo = await ResolveOrCreateLookupCodeAsync(cn, "v_ta_unidad", "IdUnidad", request.UnidadCodigo, request.UnidadDescripcion, token);
            if (unidadCodigo.Length == 0)
                unidadCodigo = await ResolveOrCreateLookupCodeAsync(cn, "v_ta_unidad", "IdUnidad", "UD", "UNIDAD", token);
            unidadCodigo = CodigoPk.Format(unidadCodigo, 4);

            // Proveedor: puede quedar vacío, pero si tiene valor debe existir en vt_proveedores (es el
            // proveedor del artículo). "Artículo proveedor" en cambio es el código que ESE proveedor
            // usa para el producto -- texto libre, no se valida contra ninguna tabla.
            var cuentaProveedorCruda = (request.CuentaProveedor ?? string.Empty).Trim();
            var cuentaProveedor = string.Empty;
            if (cuentaProveedorCruda.Length > 0)
            {
                cuentaProveedor = CodigoPk.Format(cuentaProveedorCruda, 15);
                var existeProveedor = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM dbo.vt_proveedores WHERE LTRIM(RTRIM(CODIGO)) = @Codigo;",
                    new { Codigo = cuentaProveedorCruda }, cancellationToken: token));
                if (existeProveedor == 0)
                    throw new InvalidOperationException($"El proveedor '{cuentaProveedorCruda}' no existe.");
            }

            // Si no es exento y no tiene alícuota propia, toma la alícuota general (Cfg PIVA) -- misma
            // regla que CalcularUtlPrecio en FrmArtAlta.frm.
            var tasaIva = request.TasaIva;
            if (tasaIva <= 0 && !request.Exento)
                tasaIva = ParseDecimal(await ReadSingleConfigValueAsync(cn, "PIVA", token));

            var parameters = new
            {
                Codigo = codigo,
                CodigoWhere = codigoCrudo,
                CodigoBarra = codigoBarra,
                Descripcion = descripcion,
                RubroCodigo = rubroCodigo,
                MarcaCodigo = marcaCodigo,
                UnidadCodigo = unidadCodigo,
                CuentaProveedor = cuentaProveedor,
                CodigoArtProveedor = (request.CodigoArtProveedor ?? string.Empty).Trim(),
                request.Pesable,
                request.Costo,
                Precio = request.Precio,
                request.Utilidad,
                TasaIva = tasaIva,
                request.Exento
            };

            if (esAlta)
            {
                var insertSql = $"""
                    INSERT INTO dbo.V_MA_ARTICULOS
                        (IDARTICULO, CODIGOBARRA, DESCRIPCION, IDRUBRO, IDTIPO, IDUNIDAD, CUENTAPROVEEDOR,
                         CodigoArtProveedor, Pesable, COSTO, {precioColumn}, UTILIDAD, TasaIVA, EXENTO, SUSPENDIDO, FHALTA)
                    VALUES
                        (@Codigo, @CodigoBarra, @Descripcion, @RubroCodigo, @MarcaCodigo, @UnidadCodigo, @CuentaProveedor,
                         @CodigoArtProveedor, @Pesable, @Costo, @Precio, @Utilidad, @TasaIva, @Exento, 0, GETDATE());
                    """;
                await cn.ExecuteAsync(new CommandDefinition(insertSql, parameters, cancellationToken: token));
            }
            else
            {
                var original = request.CodigoOriginal!.Trim();
                if (!string.Equals(original, codigoCrudo, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("El código de un artículo existente no se puede modificar.");

                var updateSql = $"""
                    UPDATE dbo.V_MA_ARTICULOS
                    SET
                        CODIGOBARRA = @CodigoBarra,
                        DESCRIPCION = @Descripcion,
                        IDRUBRO = @RubroCodigo,
                        IDTIPO = @MarcaCodigo,
                        IDUNIDAD = @UnidadCodigo,
                        CUENTAPROVEEDOR = @CuentaProveedor,
                        CodigoArtProveedor = @CodigoArtProveedor,
                        Pesable = @Pesable,
                        COSTO = @Costo,
                        {precioColumn} = @Precio,
                        UTILIDAD = @Utilidad,
                        TasaIVA = @TasaIva,
                        EXENTO = @Exento
                    WHERE LTRIM(RTRIM(IDARTICULO)) = @CodigoWhere;
                    """;
                var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, parameters, cancellationToken: token));
                if (affected == 0)
                    throw new InvalidOperationException("El artículo ya no existe en la base activa.");
            }

            return codigoCrudo;
        }, "No se pudo guardar el artículo.", ct);

    public Task DeactivateAsync(string codigo, CancellationToken ct = default)
        => ExecuteLoggedAsync("Deactivate", async token =>
        {
            var normalizado = (codigo ?? string.Empty).Trim();
            if (normalizado.Length == 0)
                throw new InvalidOperationException("Falta el código del artículo a dar de baja.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.V_MA_ARTICULOS SET SUSPENDIDO = 1 WHERE LTRIM(RTRIM(IDARTICULO)) = @Codigo;",
                new { Codigo = normalizado }, cancellationToken: token));
        }, "No se pudo dar de baja el artículo.", ct);

    public Task<ArticuloViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                return CreateDefaultViewSettings();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);

            var sql = $"""
                SELECT TOP (1) ISNULL(VALOR, ''), ISNULL({detailColumn}, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """;
            var row = await cn.QueryFirstOrDefaultAsync<(string Valor, string Aux)>(new CommandDefinition(
                sql, new { Clave = configKey }, cancellationToken: token));

            var raw = !string.IsNullOrWhiteSpace(row.Valor) ? row.Valor.Trim() : (row.Aux ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return CreateDefaultViewSettings();

            var parsed = JsonSerializer.Deserialize<ArticuloViewSettingsDto>(raw, JsonOptions);
            return NormalizeViewSettings(parsed);
        }, "No se pudo cargar la configuración de vista.", ct);

    public Task SaveViewSettingsAsync(string userName, ArticuloViewSettingsDto settings, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveViewSettings", async token =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("No hay un usuario logueado para guardar la vista.");

            var normalized = NormalizeViewSettings(settings);
            var serialized = JsonSerializer.Serialize(normalized, JsonOptions);
            var (valor, valorAux) = serialized.Length > 150 ? (string.Empty, serialized) : (serialized, string.Empty);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var detailColumn = await ResolveConfigDetailColumnAsync(cn, token);
            var configKey = BuildViewConfigKey(userName);

            var sql = $"""
                UPDATE dbo.TA_CONFIGURACION
                SET VALOR = @Valor, {detailColumn} = @ValorAux, GRUPO = N'ARTICULOS', FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;

                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, {detailColumn}, GRUPO, FechaHora_Grabacion)
                    VALUES (@Clave, @Valor, @ValorAux, N'ARTICULOS', GETDATE());
                """;
            await cn.ExecuteAsync(new CommandDefinition(sql, new { Clave = configKey, Valor = valor, ValorAux = valorAux }, cancellationToken: token));
        }, "No se pudo guardar la configuración de vista.", ct);

    // ---- Helpers privados ----

    private static async Task<string> ResolvePrecioVentaColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        var raw = await ReadSingleConfigValueAsync(cn, "ClasePrecioVenta", ct);
        var n = int.TryParse(raw, out var parsed) && parsed is >= 1 and <= 8 ? parsed : 1;
        return $"PRECIO{n}";
    }

    private static async Task<string> ReadSingleConfigValueAsync(SqlConnection cn, string clave, CancellationToken ct)
        => await cn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(LTRIM(RTRIM(VALOR)), '') FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
            new { Clave = clave.ToUpperInvariant() }, cancellationToken: ct)) ?? string.Empty;

    /// <summary>El "código de cuenta FTP" (clave TA_CONFIGURACION FTP_CODIGOCTA) es, en la práctica,
    /// el mismo `idcliente` de la base en la tabla central `bases` (confirmado contra una base real:
    /// FTP_CODIGOCTA='112010001' == bases.idcliente para esa misma base). Si la base activa nunca
    /// cargó esa clave a mano, la resolvemos desde ahí en vez de fallar -- así una base nueva no se
    /// queda sin imágenes solo por no tener ese dato duplicado en TA_CONFIGURACION.</summary>
    private async Task<string> ResolveFtpCodigoCtaAsync(SqlConnection cn, CancellationToken ct)
    {
        var configurado = await ReadSingleConfigValueAsync(cn, "FTP_CODIGOCTA", ct);
        if (!string.IsNullOrWhiteSpace(configurado))
            return configurado;

        var idBase = sessionService.GetActiveSession()?.BaseId;
        if (idBase is not > 0)
            return string.Empty;

        var baseCentral = await centralBasesService.GetByIdAsync(idBase.Value, ct);
        return baseCentral?.IdCliente ?? string.Empty;
    }

    private static async Task<Dictionary<string, string>> ReadConfigMapAsync(SqlConnection cn, IReadOnlyList<string> claves, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<(string Clave, string Valor)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) AS Clave, ISNULL(LTRIM(RTRIM(VALOR)), '') AS Valor
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;
            """, new { Claves = claves }, cancellationToken: ct));
        return rows.ToDictionary(r => r.Clave, r => r.Valor, StringComparer.OrdinalIgnoreCase);
    }

    private static string Get(IReadOnlyDictionary<string, string> valores, string clave)
        => valores.TryGetValue(clave, out var v) ? v : string.Empty;

    private static bool ParseBool(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return v is "1" or "S" or "SI" or "SÍ" or "TRUE" or "T" or "Y";
    }

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse((value ?? string.Empty).Trim().Replace(",", "."),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;

    /// <summary>Resuelve el código de Rubro/Marca/Unidad -- si viene un código que ya existe, se usa
    /// tal cual; si viene un código nuevo (tipeado a mano) se crea con ese código y la descripción
    /// dada; si no viene código, busca por descripción y si tampoco existe crea una fila nueva con el
    /// próximo código numérico (mismo criterio que ElCodigo() en FrmArtAlta.frm).</summary>
    private static async Task<string> ResolveOrCreateLookupCodeAsync(
        SqlConnection cn, string tableName, string idColumn, string? codigo, string? descripcion, CancellationToken ct)
    {
        var codigoLimpio = (codigo ?? string.Empty).Trim();
        var descripcionLimpia = (descripcion ?? string.Empty).Trim();

        if (codigoLimpio.Length > 0)
        {
            var existeCodigo = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM dbo.{tableName} WHERE LTRIM(RTRIM({idColumn})) = @Codigo;",
                new { Codigo = codigoLimpio }, cancellationToken: ct));
            if (existeCodigo > 0)
                return codigoLimpio;

            await cn.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO dbo.{tableName} ({idColumn}, Descripcion) VALUES (@Codigo, @Descripcion);",
                new { Codigo = codigoLimpio, Descripcion = descripcionLimpia.Length > 0 ? descripcionLimpia : codigoLimpio }, cancellationToken: ct));
            return codigoLimpio;
        }

        if (descripcionLimpia.Length == 0)
            return string.Empty;

        var existente = await cn.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            $"SELECT TOP (1) LTRIM(RTRIM({idColumn})) FROM dbo.{tableName} WHERE UPPER(LTRIM(RTRIM(Descripcion))) = @Descripcion;",
            new { Descripcion = descripcionLimpia.ToUpperInvariant() }, cancellationToken: ct));
        if (!string.IsNullOrWhiteSpace(existente))
            return existente;

        var maximo = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT ISNULL(MAX(TRY_CAST(LTRIM(RTRIM({idColumn})) AS INT)), 0) FROM dbo.{tableName};",
            cancellationToken: ct));
        var nuevoCodigo = (maximo + 1).ToString().PadLeft(4, '0');

        await cn.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO dbo.{tableName} ({idColumn}, Descripcion) VALUES (@Codigo, @Descripcion);",
            new { Codigo = nuevoCodigo, Descripcion = descripcionLimpia }, cancellationToken: ct));
        return nuevoCodigo;
    }

    private static string BuildViewConfigKey(string userName)
    {
        var normalized = userName.Trim().ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        return $"USUVIEW-ARTICULOS-{hash[..24]}";
    }

    private static async Task<string> ResolveConfigDetailColumnAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.TA_CONFIGURACION')
              AND LOWER(name) IN (N'valoraux', N'valor_aux', N'descripcion')
            ORDER BY CASE WHEN LOWER(name) IN (N'valoraux', N'valor_aux') THEN 0 ELSE 1 END, name;
            """;
        var column = await cn.ExecuteScalarAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(column) ? "DESCRIPCION" : column;
    }

    private static ArticuloViewSettingsDto CreateDefaultViewSettings() => new()
    {
        AgruparPor = ArticuloViewGroupKeys.None,
        Vista = ArticuloViewModeKeys.Listado,
        Columnas =
        [
            new() { Key = ArticuloViewColumnKeys.Codigo, Label = "Código", Visible = true, Order = 0 },
            new() { Key = ArticuloViewColumnKeys.CodigoBarra, Label = "Cód. barras", Visible = false, Order = 1 },
            new() { Key = ArticuloViewColumnKeys.Descripcion, Label = "Descripción", Visible = true, Order = 2 },
            new() { Key = ArticuloViewColumnKeys.Rubro, Label = "Rubro", Visible = true, Order = 3 },
            new() { Key = ArticuloViewColumnKeys.Marca, Label = "Marca", Visible = false, Order = 4 },
            new() { Key = ArticuloViewColumnKeys.Unidad, Label = "Unidad", Visible = false, Order = 5 },
            new() { Key = ArticuloViewColumnKeys.Costo, Label = "Costo", Visible = true, Order = 6 },
            new() { Key = ArticuloViewColumnKeys.Precio, Label = "Precio", Visible = true, Order = 7 },
            new() { Key = ArticuloViewColumnKeys.TasaIva, Label = "IVA %", Visible = false, Order = 8 }
        ]
    };

    private static ArticuloViewSettingsDto NormalizeViewSettings(ArticuloViewSettingsDto? settings)
    {
        var defaults = CreateDefaultViewSettings();
        if (settings is null)
            return defaults;

        var incoming = settings.Columnas
            .Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .ToDictionary(c => c.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        return new ArticuloViewSettingsDto
        {
            AgruparPor = settings.AgruparPor == ArticuloViewGroupKeys.Rubro ? ArticuloViewGroupKeys.Rubro : ArticuloViewGroupKeys.None,
            Vista = settings.Vista == ArticuloViewModeKeys.Kanban ? ArticuloViewModeKeys.Kanban : ArticuloViewModeKeys.Listado,
            Columnas = defaults.Columnas
                .Select(defaultCol => incoming.TryGetValue(defaultCol.Key, out var source)
                    ? new ArticuloViewColumnDto { Key = defaultCol.Key, Label = defaultCol.Label, Visible = source.Visible, Order = source.Order }
                    : defaultCol)
                .OrderBy(c => c.Order)
                .ToList()
        };
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
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
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }

    private async Task ExecuteLoggedAsync(string action, Func<CancellationToken, Task> operation, string userMessage, CancellationToken ct)
        => await ExecuteLoggedAsync(action, async token =>
        {
            await operation(token);
            return true;
        }, userMessage, ct);
}

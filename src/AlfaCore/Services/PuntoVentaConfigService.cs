using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaConfigService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaConfigValidator validator,
    ILogger<PuntoVentaConfigService> logger) : IPuntoVentaConfigService
{
    private const string ModuleName = "PuntoVentaConfig";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public Task<PuntoVentaCatalogosDto> GetCatalogosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetCatalogos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var unidades = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(Codigo)) AS Valor, LTRIM(RTRIM(Descripcion)) AS Descripcion
                FROM dbo.V_TA_UnidadNegocio
                WHERE LTRIM(RTRIM(ISNULL(Codigo, ''))) <> ''
                ORDER BY Codigo;
                """, cancellationToken: token))).ToList();

            var cajas = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdCajas)) AS Valor,
                       LTRIM(RTRIM(IdCajas)) + CASE WHEN LTRIM(RTRIM(ISNULL(Descripcion, ''))) = '' THEN '' ELSE ' · ' + LTRIM(RTRIM(Descripcion)) END AS Descripcion
                FROM dbo.V_Ta_Cajas
                WHERE LTRIM(RTRIM(ISNULL(IdCajas, ''))) <> ''
                ORDER BY IdCajas;
                """, cancellationToken: token))).ToList();

            var sucursales = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                SELECT DISTINCT RIGHT('0000' + CONVERT(varchar(20), Sucursal), 4) AS Valor,
                       RIGHT('0000' + CONVERT(varchar(20), Sucursal), 4) AS Descripcion
                FROM (
                    SELECT A_SUC_DEFAULT AS Sucursal FROM dbo.V_TA_CPTE
                    UNION ALL SELECT B_SUC_DEFAULT FROM dbo.V_TA_CPTE
                    UNION ALL SELECT C_SUC_DEFAULT FROM dbo.V_TA_CPTE
                    UNION ALL SELECT X_SUC_DEFAULT FROM dbo.V_TA_CPTE
                ) AS sucursales
                WHERE ISNULL(Sucursal, 0) > 0
                ORDER BY Valor;
                """, cancellationToken: token))).ToList();

            var sucursalConfigurada = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(VALOR, '')))
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'TPV_SUCURSAL';
                """, cancellationToken: token));
            AgregarOpcion(sucursales, sucursalConfigurada, NormalizarCodigo(sucursalConfigurada));

            var sucursalesGuardadas = await cn.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT LTRIM(RTRIM(ISNULL(SUCURSAL, '')))
                FROM dbo.POS_PUNTOVENTA
                WHERE LTRIM(RTRIM(ISNULL(SUCURSAL, ''))) <> '';
                """, cancellationToken: token));
            foreach (var sucursal in sucursalesGuardadas)
                AgregarOpcion(sucursales, sucursal, NormalizarCodigo(sucursal));

            var listas = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.V_MA_PreciosCab') IS NOT NULL
                    SELECT LTRIM(RTRIM(IdLista)) AS Valor,
                           LTRIM(RTRIM(IdLista)) + CASE WHEN LTRIM(RTRIM(ISNULL(Nombre, ''))) = '' THEN '' ELSE ' · ' + LTRIM(RTRIM(Nombre)) END AS Descripcion
                    FROM dbo.V_MA_PreciosCab
                    WHERE TipoLista = 'V' AND LTRIM(RTRIM(ISNULL(IdLista, ''))) <> ''
                    ORDER BY IdLista;
                """, cancellationToken: token))).ToList();

            var rubros = (await cn.QueryAsync<PuntoVentaCatalogoOpcionDto>(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.V_TA_Rubros') IS NOT NULL
                    SELECT LTRIM(RTRIM(IdRubro)) AS Valor,
                           LTRIM(RTRIM(ISNULL(Descripcion, IdRubro))) AS Descripcion
                    FROM dbo.V_TA_Rubros
                    WHERE LTRIM(RTRIM(ISNULL(IdRubro, ''))) <> ''
                    ORDER BY Descripcion, IdRubro;
                """, cancellationToken: token))).ToList();

            return new PuntoVentaCatalogosDto
            {
                Sucursales = sucursales.OrderBy(x => x.Valor).ToList(),
                Cajas = cajas,
                UnidadesNegocio = unidades,
                ListasPrecios = listas,
                Rubros = rubros
            };
        }, "No se pudieron cargar los catálogos de puntos de venta.", ct);

    private static void AgregarOpcion(List<PuntoVentaCatalogoOpcionDto> opciones, string? valor, string? descripcion)
    {
        var normalizado = NormalizarCodigo(valor);
        if (normalizado.Length == 0 || opciones.Any(x => string.Equals(x.Valor, normalizado, StringComparison.OrdinalIgnoreCase)))
            return;

        opciones.Add(new PuntoVentaCatalogoOpcionDto { Valor = normalizado, Descripcion = descripcion?.Trim() is { Length: > 0 } texto ? texto : normalizado });
    }

    private static string NormalizarCodigo(string? valor)
    {
        var texto = (valor ?? string.Empty).Trim();
        return int.TryParse(texto, out var numero) && numero >= 0 && numero <= 9999
            ? numero.ToString("0000")
            : texto;
    }

    public Task<string> GetComprobanteHabitualAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetComprobanteHabitual", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var clave = BuildComprobanteHabitualKey();
            var valor = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(VALOR)), '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
                """, new { Clave = clave }, cancellationToken: token));

            return NormalizeComprobanteHabitual(valor);
        }, "No se pudo leer el comprobante habitual del punto de venta.", ct);

    public Task SaveComprobanteHabitualAsync(string comprobante, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveComprobanteHabitual", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var clave = BuildComprobanteHabitualKey();
            var valor = NormalizeComprobanteHabitual(comprobante) switch
            {
                "FP" => "FP",
                "NCFP" => "NCFP",
                "NC" => "NC",
                _ => "eFC"
            };
            var affected = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION
                SET VALOR = @Valor, FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion)
                    VALUES (N'DATOS', @Clave, @Valor, GETDATE());
                END;
                """, new { Clave = clave, Valor = valor }, cancellationToken: token));

            return affected;
        }, "No se pudo guardar el comprobante habitual del punto de venta.", ct);

    public Task<IReadOnlyList<PuntoVentaTopArticuloDto>> GetTopArticulosAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTopArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var configuracion = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) ISNULL(VALOR, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'GOUR_TOPVTAS';
                """, cancellationToken: token));
            var ids = ParseTopArticuloIds(configuracion);
            if (ids.Count == 0)
                return (IReadOnlyList<PuntoVentaTopArticuloDto>)[];

            var rows = (await cn.QueryAsync<PuntoVentaTopArticuloDto>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IDARTICULO)) AS IdArticulo,
                       LTRIM(RTRIM(ISNULL(DESCRIPCION, ''))) AS Descripcion
                FROM dbo.V_MA_ARTICULOS
                WHERE LTRIM(RTRIM(IDARTICULO)) IN @Ids;
                """, new { Ids = ids }, cancellationToken: token))).ToDictionary(x => x.IdArticulo, StringComparer.OrdinalIgnoreCase);

            return (IReadOnlyList<PuntoVentaTopArticuloDto>)ids
                .Where(rows.ContainsKey)
                .Select((id, index) => new PuntoVentaTopArticuloDto
                {
                    Posicion = index + 1,
                    IdArticulo = rows[id].IdArticulo,
                    Descripcion = rows[id].Descripcion
                })
                .ToList();
        }, "No se pudo leer la lista Top de artículos.", ct);

    public Task<IReadOnlyList<PuntoVentaTopArticuloDto>> DetectarTopArticulosAsync(int limite = 100, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DetectarTopArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var cantidad = Math.Clamp(limite, 1, 1000);
            var vistaVentasDisponible = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT CASE WHEN OBJECT_ID(N'dbo.VT_DETALLEIVAPROFORMA', N'V') IS NULL THEN 0 ELSE 1 END;
                """, cancellationToken: token));
            var sql = vistaVentasDisponible == 1 ? """
                SELECT TOP (@Limite)
                       CONVERT(varchar(50), d.Articulo) AS IdArticulo,
                       LTRIM(RTRIM(ISNULL(d.DESCRIPCION, ''))) AS Descripcion,
                       CONVERT(decimal(18, 4), SUM(ISNULL(d.Consumo, 0))) AS CantidadVendida,
                       CONVERT(decimal(18, 2), SUM(ISNULL(d.ValorVenta, 0))) AS TotalVendido
                FROM dbo.VT_DETALLEIVAPROFORMA d
                WHERE d.FECHA >= DATEADD(month, -12, CONVERT(date, GETDATE()))
                  AND d.FECHA < DATEADD(day, 1, CONVERT(date, GETDATE()))
                GROUP BY d.Articulo, d.DESCRIPCION
                ORDER BY SUM(ISNULL(d.Consumo, 0)) DESC,
                         SUM(ISNULL(d.ValorVenta, 0)) DESC,
                         d.DESCRIPCION;
                """ : """
                SELECT TOP (@Limite)
                       LTRIM(RTRIM(i.IDARTICULO)) AS IdArticulo,
                       LTRIM(RTRIM(ISNULL(i.DESCRIPCION, a.DESCRIPCION))) AS Descripcion,
                       CONVERT(decimal(18, 4), SUM(ISNULL(i.CANTIDAD, 0))) AS CantidadVendida,
                       CONVERT(decimal(18, 2), SUM(ISNULL(i.TOTAL, 0))) AS TotalVendido
                FROM dbo.V_MV_CPTEINSUMOS i
                INNER JOIN dbo.V_MV_CPTE c
                    ON c.TC = i.TC AND c.IDCOMPROBANTE = i.IDCOMPROBANTE
                INNER JOIN dbo.V_MA_ARTICULOS a
                    ON LTRIM(RTRIM(a.IDARTICULO)) = LTRIM(RTRIM(i.IDARTICULO))
                WHERE ISNULL(c.ANULADA, 0) = 0
                  AND i.TC IN (N'TK', N'TKFC', N'FC', N'ND', N'NC', N'FP', N'NCFP')
                  AND c.FECHA >= DATEADD(month, -12, CONVERT(date, GETDATE()))
                  AND c.FECHA < DATEADD(day, 1, CONVERT(date, GETDATE()))
                GROUP BY i.IDARTICULO, i.DESCRIPCION, a.DESCRIPCION
                ORDER BY SUM(ISNULL(i.CANTIDAD, 0)) DESC,
                         SUM(ISNULL(i.TOTAL, 0)) DESC,
                         i.DESCRIPCION;
                """;
            var rows = (await cn.QueryAsync<PuntoVentaTopArticuloDto>(new CommandDefinition(
                sql, new { Limite = cantidad }, cancellationToken: token))).ToList();

            return (IReadOnlyList<PuntoVentaTopArticuloDto>)rows
                .Select((row, index) => new PuntoVentaTopArticuloDto
                {
                    Posicion = index + 1,
                    IdArticulo = row.IdArticulo,
                    Descripcion = row.Descripcion,
                    CantidadVendida = row.CantidadVendida,
                    TotalVendido = row.TotalVendido
                })
                .ToList();
        }, "No se pudo detectar los artículos más vendidos.", ct);

    public Task<int> GetTopArticulosConfiguradosCountAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetTopArticulosConfiguradosCount", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var configuracion = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT TOP (1) ISNULL(VALOR, '')
                FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'GOUR_TOPVTAS';
                """, cancellationToken: token));
            return ParseTopArticuloIds(configuracion).Count;
        }, "No se pudo contar la lista Top de artículos.", ct);

    public Task SaveTopArticulosAsync(IReadOnlyList<string> idsArticulo, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveTopArticulos", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var ids = idsArticulo
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var valor = ids.Count == 0 ? string.Empty : $"|{string.Join('|', ids)}|";
            var limiteValor = await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
                SELECT CHARACTER_MAXIMUM_LENGTH
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo'
                  AND TABLE_NAME = 'TA_CONFIGURACION'
                  AND COLUMN_NAME = 'VALOR';
                """, cancellationToken: token)) ?? 150;
            if (limiteValor > 0 && valor.Length > limiteValor)
            {
                throw new InvalidOperationException(
                    $"La configuración legacy GOUR_TOPVTAS admite hasta {limiteValor} caracteres y la lista actual ocupa {valor.Length}. Reducí la cantidad de artículos antes de guardar; ampliar esa columna requeriría una modificación de esquema no autorizada.");
            }
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TA_CONFIGURACION
                SET VALOR = @Valor, FechaHora_Modificacion = GETDATE()
                WHERE UPPER(LTRIM(RTRIM(CLAVE))) = 'GOUR_TOPVTAS';

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion)
                    VALUES (N'DATOS', N'GOUR_TOPVTAS', @Valor, GETDATE());
                END;
                """, new { Valor = valor }, cancellationToken: token));
            return 0;
        }, "No se pudo guardar la lista Top de artículos.", ct);

    private static List<string> ParseTopArticuloIds(string? value)
        => (value ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildComprobanteHabitualKey()
        => $"{Environment.MachineName.Trim()}_GOUR_CPTE_PC".ToUpperInvariant();

    private static string NormalizeComprobanteHabitual(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "FP" => "FP",
            "NCFP" => "NCFP",
            "NC" => "NC",
            _ => "FC"
        };

    public Task<IReadOnlyList<PuntoVentaEntidadDto>> GetPuntosVentaAsync(bool soloActivos = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPuntosVenta", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, NOMBRE, MODO,
                    ISNULL(UNEGOCIO, '') AS Unegocio,
                    ISNULL(IDCAJA, '') AS IdCaja,
                    ISNULL(SUCURSAL, '') AS Sucursal,
                    LTRIM(RTRIM(ISNULL(IDLISTA, ''))) AS IdLista,
                    CLASEPRECIO AS ClasePrecio,
                    ISNULL(CARGARAPIDAGRAFICA, 0) AS CargaRapidaGrafica,
                    ISNULL(MODO_INGRESO_RAPIDO, CASE WHEN ISNULL(CARGARAPIDAGRAFICA, 0) = 1 THEN 'COMPLETO' ELSE 'DESHABILITADO' END) AS ModoIngresoRapido,
                    ISNULL(GOUR_IMPORTE1, 5) AS ImporteRapido1,
                    ISNULL(GOUR_IMPORTE2, 10) AS ImporteRapido2,
                    ISNULL(GOUR_IMPORTE3, 25) AS ImporteRapido3,
                    ISNULL(GOUR_IMPORTE4, 50) AS ImporteRapido4,
                    ISNULL(GOUR_IMPORTE5, 100) AS ImporteRapido5,
                    ISNULL(GOUR_IMPORTE6, 200) AS ImporteRapido6,
                    ISNULL(GOUR_CANTIDAD1, 0.1) AS CantidadRapida1,
                    ISNULL(GOUR_CANTIDAD2, 0.25) AS CantidadRapida2,
                    ISNULL(GOUR_CANTIDAD3, 0.5) AS CantidadRapida3,
                    ISNULL(GOUR_CANTIDAD4, 1) AS CantidadRapida4,
                    ISNULL(GOUR_CANTIDAD5, 6) AS CantidadRapida5,
                    ISNULL(GOUR_CANTIDAD6, 12) AS CantidadRapida6,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                {(soloActivos ? "WHERE ACTIVO = 1" : string.Empty)}
                ORDER BY NOMBRE, CODIGO;
                """;

            var rows = (await cn.QueryAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, cancellationToken: token))).ToList();
            var rubros = await GetRubrosPorPuntoVentaAsync(cn, null, token);
            foreach (var row in rows)
                row.Rubros = rubros.TryGetValue(row.Id, out var lista) ? lista : [];
            return (IReadOnlyList<PuntoVentaEntidadDto>)rows;
        }, "No se pudieron cargar los puntos de venta.", ct);

    public Task<PuntoVentaEntidadDto?> GetPuntoVentaByIdAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetPuntoVentaById", async token =>
        {
            if (id <= 0)
                return null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            const string sql = """
                SELECT
                    ID, CODIGO, NOMBRE, MODO,
                    ISNULL(UNEGOCIO, '') AS Unegocio,
                    ISNULL(IDCAJA, '') AS IdCaja,
                    ISNULL(SUCURSAL, '') AS Sucursal,
                    LTRIM(RTRIM(ISNULL(IDLISTA, ''))) AS IdLista,
                    CLASEPRECIO AS ClasePrecio,
                    ISNULL(CARGARAPIDAGRAFICA, 0) AS CargaRapidaGrafica,
                    ISNULL(MODO_INGRESO_RAPIDO, CASE WHEN ISNULL(CARGARAPIDAGRAFICA, 0) = 1 THEN 'COMPLETO' ELSE 'DESHABILITADO' END) AS ModoIngresoRapido,
                    ISNULL(GOUR_IMPORTE1, 5) AS ImporteRapido1,
                    ISNULL(GOUR_IMPORTE2, 10) AS ImporteRapido2,
                    ISNULL(GOUR_IMPORTE3, 25) AS ImporteRapido3,
                    ISNULL(GOUR_IMPORTE4, 50) AS ImporteRapido4,
                    ISNULL(GOUR_IMPORTE5, 100) AS ImporteRapido5,
                    ISNULL(GOUR_IMPORTE6, 200) AS ImporteRapido6,
                    ISNULL(GOUR_CANTIDAD1, 0.1) AS CantidadRapida1,
                    ISNULL(GOUR_CANTIDAD2, 0.25) AS CantidadRapida2,
                    ISNULL(GOUR_CANTIDAD3, 0.5) AS CantidadRapida3,
                    ISNULL(GOUR_CANTIDAD4, 1) AS CantidadRapida4,
                    ISNULL(GOUR_CANTIDAD5, 6) AS CantidadRapida5,
                    ISNULL(GOUR_CANTIDAD6, 12) AS CantidadRapida6,
                    ACTIVO,
                    ISNULL(USUARIO, '') AS Usuario,
                    FECHAHORA_ALTA, FECHAHORA_MODIFICACION
                FROM dbo.POS_PUNTOVENTA
                WHERE ID = @Id;
                """;

            var pv = await cn.QuerySingleOrDefaultAsync<PuntoVentaEntidadDto>(new CommandDefinition(sql, new { Id = id }, cancellationToken: token));
            if (pv is not null)
                pv.Rubros = (await GetRubrosPorPuntoVentaAsync(cn, id, token)).TryGetValue(id, out var lista) ? lista : [];
            return pv;
        }, "No se pudo cargar el punto de venta seleccionado.", ct);

    private static async Task<Dictionary<int, List<string>>> GetRubrosPorPuntoVentaAsync(SqlConnection cn, int? id, CancellationToken ct)
    {
        var rows = await cn.QueryAsync<(int IdPuntoVenta, string IdRubro)>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.POS_PUNTOVENTA_RUBRO', N'U') IS NOT NULL
                SELECT IDPUNTOVENTA, LTRIM(RTRIM(IDRUBRO))
                FROM dbo.POS_PUNTOVENTA_RUBRO
                WHERE @Id IS NULL OR IDPUNTOVENTA = @Id
                ORDER BY IDPUNTOVENTA, IDRUBRO;
            """, new { Id = id }, cancellationToken: ct));
        return rows.GroupBy(r => r.IdPuntoVenta).ToDictionary(g => g.Key, g => g.Select(r => r.IdRubro).ToList());
    }

    private static async Task GuardarRubrosAsync(SqlConnection cn, SqlTransaction tx, int id, IEnumerable<string> rubros, CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.POS_PUNTOVENTA_RUBRO WHERE IDPUNTOVENTA = @Id;", new { Id = id }, tx, cancellationToken: ct));
        foreach (var rubro in rubros.Select(r => (r ?? string.Empty).Trim()).Where(r => r.Length is > 0 and <= 10).Distinct(StringComparer.OrdinalIgnoreCase))
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.POS_PUNTOVENTA_RUBRO (IDPUNTOVENTA, IDRUBRO) VALUES (@Id, @Rubro);",
                new { Id = id, Rubro = rubro }, tx, cancellationToken: ct));
    }

    public Task<int> SavePuntoVentaAsync(PuntoVentaEntidadSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SavePuntoVenta", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Modo = (request.Modo ?? string.Empty).Trim().ToUpperInvariant();
            request.Unegocio = (request.Unegocio ?? string.Empty).Trim();
            request.IdCaja = (request.IdCaja ?? string.Empty).Trim();
            request.Sucursal = (request.Sucursal ?? string.Empty).Trim();
            request.IdLista = (request.IdLista ?? string.Empty).Trim();
            if (request.ClasePrecio is < 1 or > 8)
                request.ClasePrecio = null;
            request.Rubros ??= [];
            request.ModoIngresoRapido = (request.ModoIngresoRapido ?? string.Empty).Trim().ToUpperInvariant();
            if (request.ModoIngresoRapido is not (PuntoVentaIngresoRapidoModoKeys.Deshabilitado or PuntoVentaIngresoRapidoModoKeys.Compacto or PuntoVentaIngresoRapidoModoKeys.Completo))
                request.ModoIngresoRapido = request.CargaRapidaGrafica ? PuntoVentaIngresoRapidoModoKeys.Completo : PuntoVentaIngresoRapidoModoKeys.Deshabilitado;
            request.CargaRapidaGrafica = request.ModoIngresoRapido != PuntoVentaIngresoRapidoModoKeys.Deshabilitado;
            request.ImporteRapido1 = NormalizarImporteRapido(request.ImporteRapido1, 5m);
            request.ImporteRapido2 = NormalizarImporteRapido(request.ImporteRapido2, 10m);
            request.ImporteRapido3 = NormalizarImporteRapido(request.ImporteRapido3, 25m);
            request.ImporteRapido4 = NormalizarImporteRapido(request.ImporteRapido4, 50m);
            request.ImporteRapido5 = NormalizarImporteRapido(request.ImporteRapido5, 100m);
            request.ImporteRapido6 = NormalizarImporteRapido(request.ImporteRapido6, 200m);
            request.CantidadRapida1 = NormalizarImporteRapido(request.CantidadRapida1, 0.1m);
            request.CantidadRapida2 = NormalizarImporteRapido(request.CantidadRapida2, 0.25m);
            request.CantidadRapida3 = NormalizarImporteRapido(request.CantidadRapida3, 0.5m);
            request.CantidadRapida4 = NormalizarImporteRapido(request.CantidadRapida4, 1m);
            request.CantidadRapida5 = NormalizarImporteRapido(request.CantidadRapida5, 6m);
            request.CantidadRapida6 = NormalizarImporteRapido(request.CantidadRapida6, 12m);

            var validation = await validator.ValidatePuntoVentaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;
            var usuario = (request.UsuarioAccion ?? string.Empty).Trim();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_PUNTOVENTA
                        (CODIGO, NOMBRE, MODO, UNEGOCIO, IDCAJA, SUCURSAL, IDLISTA, CLASEPRECIO, CARGARAPIDAGRAFICA, MODO_INGRESO_RAPIDO, GOUR_IMPORTE1, GOUR_IMPORTE2, GOUR_IMPORTE3, GOUR_IMPORTE4, GOUR_IMPORTE5, GOUR_IMPORTE6, GOUR_CANTIDAD1, GOUR_CANTIDAD2, GOUR_CANTIDAD3, GOUR_CANTIDAD4, GOUR_CANTIDAD5, GOUR_CANTIDAD6, ACTIVO, USUARIO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @Nombre, @Modo, @Unegocio, @IdCaja, @Sucursal, @IdLista, @ClasePrecio, @CargaRapidaGrafica, @ModoIngresoRapido, @ImporteRapido1, @ImporteRapido2, @ImporteRapido3, @ImporteRapido4, @ImporteRapido5, @ImporteRapido6, @CantidadRapida1, @CantidadRapida2, @CantidadRapida3, @CantidadRapida4, @CantidadRapida5, @CantidadRapida6, @Activo, @Usuario, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                var nuevoId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.Nombre,
                    request.Modo,
                    request.Unegocio,
                    request.IdCaja,
                    request.Sucursal,
                    request.IdLista,
                    request.ClasePrecio,
                    request.CargaRapidaGrafica,
                    request.ModoIngresoRapido,
                    request.ImporteRapido1,
                    request.ImporteRapido2,
                    request.ImporteRapido3,
                    request.ImporteRapido4,
                    request.ImporteRapido5,
                    request.ImporteRapido6,
                    request.CantidadRapida1,
                    request.CantidadRapida2,
                    request.CantidadRapida3,
                    request.CantidadRapida4,
                    request.CantidadRapida5,
                    request.CantidadRapida6,
                    request.Activo,
                    Usuario = usuario
                }, tx, cancellationToken: token));
                await GuardarRubrosAsync(cn, tx, nuevoId, request.Rubros, token);
                if (request.Modo == PuntoVentaModoKeys.Salon)
                    await CrearSalonPredeterminadoAsync(cn, tx, nuevoId, token);
                await tx.CommitAsync(token);
                return nuevoId;
            }

            const string updateSql = """
                UPDATE dbo.POS_PUNTOVENTA
                SET CODIGO = @Codigo,
                    NOMBRE = @Nombre,
                    MODO = @Modo,
                    UNEGOCIO = @Unegocio,
                    IDCAJA = @IdCaja,
                    SUCURSAL = @Sucursal,
                    IDLISTA = @IdLista,
                    CLASEPRECIO = @ClasePrecio,
                    CARGARAPIDAGRAFICA = @CargaRapidaGrafica,
                    MODO_INGRESO_RAPIDO = @ModoIngresoRapido,
                    GOUR_IMPORTE1 = @ImporteRapido1,
                    GOUR_IMPORTE2 = @ImporteRapido2,
                    GOUR_IMPORTE3 = @ImporteRapido3,
                    GOUR_IMPORTE4 = @ImporteRapido4,
                    GOUR_IMPORTE5 = @ImporteRapido5,
                    GOUR_IMPORTE6 = @ImporteRapido6,
                    GOUR_CANTIDAD1 = @CantidadRapida1,
                    GOUR_CANTIDAD2 = @CantidadRapida2,
                    GOUR_CANTIDAD3 = @CantidadRapida3,
                    GOUR_CANTIDAD4 = @CantidadRapida4,
                    GOUR_CANTIDAD5 = @CantidadRapida5,
                    GOUR_CANTIDAD6 = @CantidadRapida6,
                    ACTIVO = @Activo,
                    USUARIO = @Usuario,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.Nombre,
                request.Modo,
                request.Unegocio,
                request.IdCaja,
                request.Sucursal,
                request.IdLista,
                request.ClasePrecio,
                request.CargaRapidaGrafica,
                request.ModoIngresoRapido,
                request.ImporteRapido1,
                request.ImporteRapido2,
                request.ImporteRapido3,
                request.ImporteRapido4,
                request.ImporteRapido5,
                request.ImporteRapido6,
                request.CantidadRapida1,
                request.CantidadRapida2,
                request.CantidadRapida3,
                request.CantidadRapida4,
                request.CantidadRapida5,
                request.CantidadRapida6,
                request.Activo,
                Usuario = usuario
            }, tx, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el punto de venta para actualizar.");

            await GuardarRubrosAsync(cn, tx, request.Id!.Value, request.Rubros, token);
            // Un punto de venta que pasa a Salón y no tiene sectores también recibe el salón inicial.
            if (request.Modo == PuntoVentaModoKeys.Salon)
                await CrearSalonPredeterminadoAsync(cn, tx, request.Id!.Value, token);
            await tx.CommitAsync(token);
            return request.Id!.Value;
        }, "No se pudo guardar el punto de venta.", ct);

    /// <summary>Movimientos que impiden borrar un punto de venta (tabla, filtro por @Id, nombre para el mensaje).</summary>
    internal static readonly (string Tabla, string Filtro, string Nombre)[] MovimientosPuntoVenta =
    [
        ("POS_PEDIDO", "IDPUNTOVENTA = @Id", "pedido(s)"),
        ("POS_COMANDA", "IDPUNTOVENTA = @Id", "comanda(s)"),
        ("POS_TURNO", "IDPUNTOVENTA = @Id", "turno(s) de caja"),
        ("POS_MESA_LLAMADO", "IDPUNTOVENTA = @Id", "llamado(s) de mesa"),
        ("COMANDA_RESERVAS", "IdMesa IN (SELECT m.ID FROM dbo.POS_MESA m JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR WHERE s.IDPUNTOVENTA = @Id)", "reserva(s) de mesa")
    ];

    /// <summary>Configuración propia del punto de venta que se borra con él, en orden (las mesas antes que sus sectores).</summary>
    internal static readonly (string Tabla, string Filtro)[] ConfiguracionPuntoVenta =
    [
        ("POS_ARTICULO_AGOTADO", "IDPUNTOVENTA = @Id"),
        ("POS_CARTA_FAMILIA", "IDPUNTOVENTA = @Id"),
        ("POS_PUNTOVENTA_RUBRO", "IDPUNTOVENTA = @Id"),
        ("POS_MESA", "IDSECTOR IN (SELECT ID FROM dbo.POS_SECTOR WHERE IDPUNTOVENTA = @Id)"),
        ("POS_SECTOR", "IDPUNTOVENTA = @Id")
    ];

    internal static string? MotivoNoBorrar(IEnumerable<(string Nombre, int Cantidad)> movimientos)
    {
        var con = movimientos.Where(m => m.Cantidad > 0).Select(m => $"{m.Cantidad} {m.Nombre}").ToList();
        return con.Count == 0 ? null
            : $"El punto de venta tiene movimientos ({string.Join(", ", con)}): no se puede borrar. Si no se usa más, desmarcá «Activo».";
    }

    public Task DeletePuntoVentaAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeletePuntoVenta", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);

            var pv = await cn.QuerySingleOrDefaultAsync<(string Codigo, string Nombre)?>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(CODIGO)), LTRIM(RTRIM(ISNULL(NOMBRE,''))) FROM dbo.POS_PUNTOVENTA WITH (UPDLOCK, HOLDLOCK) WHERE ID = @Id;",
                new { Id = id }, tx, cancellationToken: token))
                ?? throw new InvalidOperationException("El punto de venta ya no existe.");

            // Las tablas pueden no existir en bases que todavía no corrieron todos los scripts del POS.
            static async Task<bool> ExisteAsync(SqlConnection cn, SqlTransaction tx, string tabla, CancellationToken token)
                => await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(@Tabla, N'U') IS NULL THEN 0 ELSE 1 END;",
                    new { Tabla = "dbo." + tabla }, tx, cancellationToken: token)) == 1;

            var movimientos = new List<(string Nombre, int Cantidad)>();
            foreach (var (tabla, filtro, nombre) in MovimientosPuntoVenta)
            {
                if (!await ExisteAsync(cn, tx, tabla, token) || (tabla == "COMANDA_RESERVAS" && !await ExisteAsync(cn, tx, "POS_MESA", token)))
                    continue;
                movimientos.Add((nombre, await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    $"SELECT COUNT(*) FROM dbo.{tabla} WHERE {filtro};", new { Id = id }, tx, commandTimeout: 120, cancellationToken: token))));
            }
            if (MotivoNoBorrar(movimientos) is { } motivo)
                throw new InvalidOperationException(motivo);

            foreach (var (tabla, filtro) in ConfiguracionPuntoVenta)
                if (await ExisteAsync(cn, tx, tabla, token))
                    await cn.ExecuteAsync(new CommandDefinition($"DELETE FROM dbo.{tabla} WHERE {filtro};", new { Id = id }, tx, cancellationToken: token));
            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.POS_PUNTOVENTA WHERE ID = @Id;", new { Id = id }, tx, cancellationToken: token));
            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(ModuleName, "DeletePuntoVenta", "POS_PUNTOVENTA", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Borró el punto de venta {pv.Codigo} {pv.Nombre} (sin movimientos).", new { id, pv.Codigo, pv.Nombre }, CancellationToken.None);
            return true;
        }, "No se pudo borrar el punto de venta.", ct);

    // ------------------------------------------------------------ salón: armado automático y cantidad de mesas

    /// <summary>Código de la mesa que representa la barra del salón (no cuenta como mesa en «Cantidad de mesas»).</summary>
    internal const string CodigoBarra = "BAR";
    internal const string CodigoSectorSalon = "SAL";
    private const double PlanoAncho = 1000, PlanoAlto = 600, PlanoMargen = 25, PlanoInicioMesasConBarra = 130;

    internal readonly record struct PosicionPlano(double X, double Y, double W, double H);

    /// <summary>La barra va arriba, centrada, a lo ancho del plano virtual de 1000 x 600.</summary>
    internal static PosicionPlano PosicionBarra() => new(200, PlanoMargen, 600, 70);

    /// <summary>
    /// Reparte <paramref name="cantidad"/> mesas en grilla pareja sobre el plano (debajo de la barra si la hay):
    /// filas y columnas según la proporción del área libre, la última fila centrada.
    /// </summary>
    internal static IReadOnlyList<PosicionPlano> DistribuirMesas(int cantidad, bool conBarra)
    {
        if (cantidad <= 0)
            return [];
        var top = conBarra ? PlanoInicioMesasConBarra : PlanoMargen;
        var areaW = PlanoAncho - 2 * PlanoMargen;
        var areaH = PlanoAlto - PlanoMargen - top;
        var filas = Math.Clamp((int)Math.Round(Math.Sqrt(cantidad * areaH / areaW)), 1, cantidad);
        var columnas = (int)Math.Ceiling(cantidad / (double)filas);
        filas = (int)Math.Ceiling(cantidad / (double)columnas);
        var celdaW = areaW / columnas;
        var celdaH = areaH / filas;
        var lado = Math.Floor(Math.Min(110, Math.Min(celdaW, celdaH) * 0.75));

        var posiciones = new List<PosicionPlano>(cantidad);
        for (var i = 0; i < cantidad; i++)
        {
            var fila = i / columnas;
            var columna = i % columnas;
            var enLaFila = fila == filas - 1 ? cantidad - fila * columnas : columnas;
            var corrimiento = (columnas - enLaFila) * celdaW / 2;
            posiciones.Add(new PosicionPlano(
                Math.Round(PlanoMargen + corrimiento + columna * celdaW + (celdaW - lado) / 2),
                Math.Round(top + fila * celdaH + (celdaH - lado) / 2),
                lado, lado));
        }
        return posiciones;
    }

    /// <summary>Número de la mesa a partir del código (M12 → 12); las que no tienen número van al final.</summary>
    internal static int NumeroMesa(string? codigo)
        => int.TryParse(new string((codigo ?? string.Empty).Where(char.IsDigit).ToArray()), out var n) ? n : int.MaxValue;

    private sealed class MesaEstado
    {
        public int Id { get; init; }
        public string Codigo { get; init; } = string.Empty;
        public bool Activo { get; init; }
        public bool Abierta { get; init; }
        public bool TieneMovimientos { get; init; }
        public bool EsBarra => string.Equals(Codigo, CodigoBarra, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mesas con su estado: si tienen la mesa abierta ahora (pedido ABIERTA/CUENTA) y si tienen historia
    /// (pedidos, llamados o reservas). Las tablas o columnas que la base todavía no tiene no cuentan.
    /// </summary>
    private static async Task<List<MesaEstado>> LeerMesasEstadoAsync(SqlConnection cn, SqlTransaction tx, string filtro, object parametros, CancellationToken token)
    {
        var existe = await cn.QuerySingleAsync<(int Pedido, int Llamado, int Reserva)>(new CommandDefinition("""
            SELECT CASE WHEN COL_LENGTH(N'dbo.POS_PEDIDO', N'IDMESA') IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN OBJECT_ID(N'dbo.POS_MESA_LLAMADO', N'U') IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN COL_LENGTH(N'dbo.COMANDA_RESERVAS', N'IdMesa') IS NULL THEN 0 ELSE 1 END;
            """, transaction: tx, cancellationToken: token));
        var abierta = existe.Pedido == 1
            ? "EXISTS (SELECT 1 FROM dbo.POS_PEDIDO p WHERE p.IDMESA = m.ID AND p.ESTADO IN ('ABIERTA', 'CUENTA'))" : "1 = 0";
        var movimientos = new List<string> { "1 = 0" };
        if (existe.Pedido == 1) movimientos.Add("EXISTS (SELECT 1 FROM dbo.POS_PEDIDO p WHERE p.IDMESA = m.ID)");
        if (existe.Llamado == 1) movimientos.Add("EXISTS (SELECT 1 FROM dbo.POS_MESA_LLAMADO l WHERE l.IDMESA = m.ID)");
        if (existe.Reserva == 1) movimientos.Add("EXISTS (SELECT 1 FROM dbo.COMANDA_RESERVAS r WHERE r.IdMesa = m.ID)");

        var sql = $"""
            SELECT m.ID AS Id, LTRIM(RTRIM(m.CODIGO)) AS Codigo, m.ACTIVO AS Activo,
                   CAST(CASE WHEN {abierta} THEN 1 ELSE 0 END AS bit) AS Abierta,
                   CAST(CASE WHEN {string.Join(" OR ", movimientos)} THEN 1 ELSE 0 END AS bit) AS TieneMovimientos
            FROM dbo.POS_MESA m WITH (UPDLOCK, HOLDLOCK)
            WHERE {filtro};
            """;
        return (await cn.QueryAsync<MesaEstado>(new CommandDefinition(sql, parametros, tx, commandTimeout: 120, cancellationToken: token))).ToList();
    }

    private static async Task<int> InsertarMesaAsync(SqlConnection cn, SqlTransaction tx, int idSector, string codigo, string nombre, int capacidad, PosicionPlano pos, CancellationToken token)
        => await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.POS_MESA (CODIGO, IDSECTOR, NOMBRE, CAPACIDAD, POSX, POSY, WIDTH, HEIGHT, FORMATO, ACTIVO, FECHAHORA_ALTA)
            VALUES (@Codigo, @IdSector, @Nombre, @Capacidad, @X, @Y, @W, @H, @Formato, 1, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Codigo = codigo, IdSector = idSector, Nombre = nombre, Capacidad = capacidad, pos.X, pos.Y, pos.W, pos.H, Formato = PuntoVentaMesaFormatoKeys.Cuadrada },
            tx, cancellationToken: token));

    private static Task MoverMesaAsync(SqlConnection cn, SqlTransaction tx, int id, PosicionPlano pos, CancellationToken token)
        => cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.POS_MESA SET POSX = @X, POSY = @Y, WIDTH = @W, HEIGHT = @H, FECHAHORA_MODIFICACION = GETDATE() WHERE ID = @Id;",
            new { Id = id, pos.X, pos.Y, pos.W, pos.H }, tx, cancellationToken: token));

    private static async Task<string> LeerConfigAsync(SqlConnection cn, SqlTransaction tx, string clave, string porDefecto, CancellationToken token)
    {
        var valor = await cn.QueryFirstOrDefaultAsync<string>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
                SELECT TOP (1) LTRIM(RTRIM(ISNULL(VALOR, ''))) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, new { Clave = clave }, tx, cancellationToken: token));
        return string.IsNullOrWhiteSpace(valor) ? porDefecto : valor.Trim();
    }

    private static async Task<int> LeerConfigEnteroAsync(SqlConnection cn, SqlTransaction tx, string clave, int porDefecto, int min, int max, CancellationToken token)
        => int.TryParse(await LeerConfigAsync(cn, tx, clave, string.Empty, token), out var n) && n >= min && n <= max ? n : porDefecto;

    /// <summary>
    /// Si el punto de venta de salón todavía no tiene sectores, arma el salón inicial: sector «Salón», barra y
    /// mesas repartidas en el plano (cantidades y capacidades en TA_CONFIGURACION, grupo POS).
    /// </summary>
    private static async Task<int> CrearSalonPredeterminadoAsync(SqlConnection cn, SqlTransaction tx, int idPuntoVenta, CancellationToken token)
    {
        var tieneSectores = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.POS_SECTOR WITH (UPDLOCK, HOLDLOCK) WHERE IDPUNTOVENTA = @Id;", new { Id = idPuntoVenta }, tx, cancellationToken: token));
        if (tieneSectores > 0)
            return 0;

        var nombreSector = await LeerConfigAsync(cn, tx, "POS-SALON-SECTOR-NOMBRE", "Salón", token);
        var mesas = await LeerConfigEnteroAsync(cn, tx, "POS-SALON-MESAS-INICIALES", 5, 0, 200, token);
        var capacidad = await LeerConfigEnteroAsync(cn, tx, "POS-SALON-CAPACIDAD-MESA", 4, 1, 99, token);
        var conBarra = !string.Equals(await LeerConfigAsync(cn, tx, "POS-SALON-CREAR-BARRA", "S", token), "N", StringComparison.OrdinalIgnoreCase);
        var capacidadBarra = await LeerConfigEnteroAsync(cn, tx, "POS-SALON-CAPACIDAD-BARRA", 6, 1, 99, token);

        var idSector = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.POS_SECTOR (CODIGO, IDPUNTOVENTA, NOMBRE, ORDEN, ACTIVO, FECHAHORA_ALTA)
            VALUES (@Codigo, @IdPuntoVenta, @Nombre, 0, 1, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Codigo = CodigoSectorSalon, IdPuntoVenta = idPuntoVenta, Nombre = nombreSector.Length > 50 ? nombreSector[..50] : nombreSector }, tx, cancellationToken: token));

        if (conBarra)
            await InsertarMesaAsync(cn, tx, idSector, CodigoBarra, "Barra", capacidadBarra, PosicionBarra(), token);
        var posiciones = DistribuirMesas(mesas, conBarra);
        for (var i = 0; i < posiciones.Count; i++)
            await InsertarMesaAsync(cn, tx, idSector, $"M{i + 1}", $"Mesa {i + 1}", capacidad, posiciones[i], token);
        return mesas;
    }

    public Task<PuntoVentaAjusteMesasResultado> AjustarCantidadMesasAsync(int idSector, int cantidad, int? capacidad, bool redistribuir, string usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "AjustarCantidadMesas", async token =>
        {
            if (cantidad is < 0 or > 200)
                throw new InvalidOperationException("La cantidad de mesas tiene que estar entre 0 y 200.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);

            var sector = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(NOMBRE)) FROM dbo.POS_SECTOR WITH (UPDLOCK, HOLDLOCK) WHERE ID = @Id;", new { Id = idSector }, tx, cancellationToken: token))
                ?? throw new InvalidOperationException("El sector ya no existe.");
            capacidad = capacidad is >= 1 and <= 99 ? capacidad : await LeerConfigEnteroAsync(cn, tx, "POS-SALON-CAPACIDAD-MESA", 4, 1, 99, token);

            var todas = await LeerMesasEstadoAsync(cn, tx, "m.IDSECTOR = @IdSector", new { IdSector = idSector }, token);
            var activas = todas.Where(m => m.Activo && !m.EsBarra).OrderBy(m => NumeroMesa(m.Codigo)).ThenBy(m => m.Id).ToList();
            var resultado = new PuntoVentaAjusteMesasResultado();
            var idsActivas = activas.Select(m => m.Id).ToList();

            if (cantidad > activas.Count)
            {
                // Primero se reactivan las mesas dadas de baja (conservan su historia); después se crean nuevas.
                foreach (var mesa in todas.Where(m => !m.Activo && !m.EsBarra).OrderBy(m => NumeroMesa(m.Codigo)).ThenBy(m => m.Id).Take(cantidad - activas.Count))
                {
                    await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.POS_MESA SET ACTIVO = 1, FECHAHORA_MODIFICACION = GETDATE() WHERE ID = @Id;",
                        new { mesa.Id }, tx, cancellationToken: token));
                    idsActivas.Add(mesa.Id);
                    resultado.Reactivadas++;
                }

                var codigos = todas.Select(m => m.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var numero = 1;
                var provisoria = new PosicionPlano(PlanoMargen, PlanoMargen, 110, 110);
                while (idsActivas.Count < cantidad)
                {
                    while (codigos.Contains($"M{numero}")) numero++;
                    codigos.Add($"M{numero}");
                    idsActivas.Add(await InsertarMesaAsync(cn, tx, idSector, $"M{numero}", $"Mesa {numero}", capacidad.Value, provisoria, token));
                    resultado.Agregadas++;
                }
                // Si no se redistribuye, las nuevas quedan sin posición y el plano las acomoda en grilla.
                if (!redistribuir && resultado.Agregadas > 0)
                    await cn.ExecuteAsync(new CommandDefinition(
                        "UPDATE dbo.POS_MESA SET POSX = NULL, POSY = NULL, WIDTH = NULL, HEIGHT = NULL WHERE ID IN @Ids;",
                        new { Ids = idsActivas.Skip(idsActivas.Count - resultado.Agregadas).ToList() }, tx, cancellationToken: token));
            }
            else if (cantidad < activas.Count)
            {
                // Se sacan las de número más alto. Una mesa ocupada no se toca; con historia se da de baja; sin historia se borra.
                var sobran = activas.Count - cantidad;
                var aQuitar = activas.AsEnumerable().Reverse().Where(m => !m.Abierta).Take(sobran).ToList();
                if (aQuitar.Count < sobran)
                    throw new InvalidOperationException(
                        $"No se pueden quitar {sobran} mesa(s) de {sector}: hay mesas ocupadas. Cerralas o liberalas y volvé a intentar.");
                foreach (var mesa in aQuitar)
                {
                    var sql = mesa.TieneMovimientos
                        ? "UPDATE dbo.POS_MESA SET ACTIVO = 0, FECHAHORA_MODIFICACION = GETDATE() WHERE ID = @Id;"
                        : "DELETE FROM dbo.POS_MESA WHERE ID = @Id;";
                    await cn.ExecuteAsync(new CommandDefinition(sql, new { mesa.Id }, tx, cancellationToken: token));
                    if (mesa.TieneMovimientos) resultado.Desactivadas++; else resultado.Borradas++;
                    idsActivas.Remove(mesa.Id);
                }
            }

            if (redistribuir)
            {
                var barra = todas.FirstOrDefault(m => m.EsBarra && m.Activo);
                if (barra is not null)
                    await MoverMesaAsync(cn, tx, barra.Id, PosicionBarra(), token);
                var posiciones = DistribuirMesas(idsActivas.Count, barra is not null);
                for (var i = 0; i < idsActivas.Count; i++)
                    await MoverMesaAsync(cn, tx, idsActivas[i], posiciones[i], token);
            }

            await tx.CommitAsync(token);
            await appEvents.LogAuditAsync(ModuleName, "AjustarCantidadMesas", "POS_SECTOR", idSector.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Cantidad de mesas de {sector}: {cantidad} ({resultado.Agregadas} nuevas, {resultado.Reactivadas} reactivadas, {resultado.Borradas} borradas, {resultado.Desactivadas} dadas de baja).",
                new { idSector, cantidad, capacidad, redistribuir, usuario, resultado }, CancellationToken.None);
            return resultado;
        }, "No se pudo cambiar la cantidad de mesas.", ct);

    public Task DeleteMesaAsync(int id, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "DeleteMesa", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);

            var mesa = (await LeerMesasEstadoAsync(cn, tx, "m.ID = @Id", new { Id = id }, token)).FirstOrDefault()
                ?? throw new InvalidOperationException("La mesa ya no existe.");
            if (mesa.TieneMovimientos)
                throw new InvalidOperationException(mesa.Abierta
                    ? $"La mesa {mesa.Codigo} está ocupada: no se puede borrar."
                    : $"La mesa {mesa.Codigo} tiene pedidos, llamados o reservas: no se puede borrar. Si no se usa más, desmarcá «Activa».");

            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.POS_MESA WHERE ID = @Id;", new { Id = id }, tx, cancellationToken: token));
            await tx.CommitAsync(token);
            await appEvents.LogAuditAsync(ModuleName, "DeleteMesa", "POS_MESA", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Borró la mesa {mesa.Codigo} (sin movimientos).", new { id, mesa.Codigo }, CancellationToken.None);
            return true;
        }, "No se pudo borrar la mesa.", ct);

    private static decimal NormalizarImporteRapido(decimal value, decimal defaultValue)
        => value > 0m && value <= 999999999999m ? decimal.Round(value, 4, MidpointRounding.AwayFromZero) : defaultValue;

    public Task<IReadOnlyList<PuntoVentaSectorDto>> GetSectoresAsync(int idPuntoVenta, bool soloActivos = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSectores", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, IDPUNTOVENTA, NOMBRE,
                    ISNULL(ICONO, '') AS Icono,
                    ISNULL(IMAGEN, '') AS Imagen,
                    ORDEN, ACTIVO
                FROM dbo.POS_SECTOR
                WHERE IDPUNTOVENTA = @IdPuntoVenta
                {(soloActivos ? "AND ACTIVO = 1" : string.Empty)}
                ORDER BY ORDEN, NOMBRE;
                """;

            var rows = await cn.QueryAsync<PuntoVentaSectorDto>(new CommandDefinition(sql, new { idPuntoVenta }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaSectorDto>)rows.ToList();
        }, "No se pudieron cargar los sectores del punto de venta.", ct);

    public Task<int> SaveSectorAsync(PuntoVentaSectorSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveSector", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Icono = (request.Icono ?? string.Empty).Trim();
            request.Imagen = (request.Imagen ?? string.Empty).Trim();

            var validation = await validator.ValidateSectorForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_SECTOR
                        (CODIGO, IDPUNTOVENTA, NOMBRE, ICONO, IMAGEN, ORDEN, ACTIVO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @IdPuntoVenta, @Nombre, @Icono, @Imagen, @Orden, @Activo, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.IdPuntoVenta,
                    request.Nombre,
                    request.Icono,
                    request.Imagen,
                    request.Orden,
                    request.Activo
                }, cancellationToken: token));
            }

            const string updateSql = """
                UPDATE dbo.POS_SECTOR
                SET CODIGO = @Codigo,
                    IDPUNTOVENTA = @IdPuntoVenta,
                    NOMBRE = @Nombre,
                    ICONO = @Icono,
                    IMAGEN = @Imagen,
                    ORDEN = @Orden,
                    ACTIVO = @Activo,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.IdPuntoVenta,
                request.Nombre,
                request.Icono,
                request.Imagen,
                request.Orden,
                request.Activo
            }, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró el sector para actualizar.");

            return request.Id!.Value;
        }, "No se pudo guardar el sector.", ct);

    public Task<IReadOnlyList<PuntoVentaMesaDto>> GetMesasAsync(int idSector, bool soloActivas = false, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetMesas", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var sql = $"""
                SELECT
                    ID, CODIGO, IDSECTOR, NOMBRE, CAPACIDAD,
                    POSX AS PosX, POSY AS PosY, WIDTH, HEIGHT,
                    ISNULL(FORMATO, 'CUADRADA') AS Formato,
                    ISNULL(ICONO, '') AS Icono,
                    ISNULL(IMAGEN, '') AS Imagen,
                    ACTIVO
                FROM dbo.POS_MESA
                WHERE IDSECTOR = @IdSector
                {(soloActivas ? "AND ACTIVO = 1" : string.Empty)}
                ORDER BY NOMBRE, CODIGO;
                """;

            var rows = await cn.QueryAsync<PuntoVentaMesaDto>(new CommandDefinition(sql, new { idSector }, cancellationToken: token));
            return (IReadOnlyList<PuntoVentaMesaDto>)rows.ToList();
        }, "No se pudieron cargar las mesas del sector.", ct);

    public Task<int> SaveMesaAsync(PuntoVentaMesaSaveRequest request, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "SaveMesa", async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            request.Nombre = (request.Nombre ?? string.Empty).Trim();
            request.Icono = (request.Icono ?? string.Empty).Trim();
            request.Imagen = (request.Imagen ?? string.Empty).Trim();
            request.Formato = PuntoVentaMesaFormatoKeys.Normalizar(request.Formato);

            var validation = await validator.ValidateMesaForSaveAsync(request, token);
            if (!validation.IsValid)
                throw new AppValidationException(BuildValidationMessage(validation), validation);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var isNew = !request.Id.HasValue || request.Id.Value <= 0;

            if (isNew)
            {
                const string insertSql = """
                    INSERT INTO dbo.POS_MESA
                        (CODIGO, IDSECTOR, NOMBRE, CAPACIDAD, POSX, POSY, WIDTH, HEIGHT, FORMATO, ICONO, IMAGEN, ACTIVO, FECHAHORA_ALTA)
                    VALUES
                        (@Codigo, @IdSector, @Nombre, @Capacidad, @PosX, @PosY, @Width, @Height, @Formato, @Icono, @Imagen, @Activo, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                return await cn.ExecuteScalarAsync<int>(new CommandDefinition(insertSql, new
                {
                    request.Codigo,
                    request.IdSector,
                    request.Nombre,
                    request.Capacidad,
                    request.PosX,
                    request.PosY,
                    request.Width,
                    request.Height,
                    request.Formato,
                    request.Icono,
                    request.Imagen,
                    request.Activo
                }, cancellationToken: token));
            }

            const string updateSql = """
                UPDATE dbo.POS_MESA
                SET CODIGO = @Codigo,
                    IDSECTOR = @IdSector,
                    NOMBRE = @Nombre,
                    CAPACIDAD = @Capacidad,
                    POSX = @PosX,
                    POSY = @PosY,
                    WIDTH = @Width,
                    HEIGHT = @Height,
                    FORMATO = @Formato,
                    ICONO = @Icono,
                    IMAGEN = @Imagen,
                    ACTIVO = @Activo,
                    FECHAHORA_MODIFICACION = GETDATE()
                WHERE ID = @Id;
                """;
            var affected = await cn.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.Id,
                request.Codigo,
                request.IdSector,
                request.Nombre,
                request.Capacidad,
                request.PosX,
                request.PosY,
                request.Width,
                request.Height,
                request.Formato,
                request.Icono,
                request.Imagen,
                request.Activo
            }, cancellationToken: token));

            if (affected <= 0)
                throw new InvalidOperationException("No se encontró la mesa para actualizar.");

            return request.Id!.Value;
        }, "No se pudo guardar la mesa.", ct);

    private static string BuildValidationMessage(ValidationResult validation)
    {
        var issues = validation.Issues
            .Select(issue => (issue.Message ?? string.Empty).Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return issues.Count == 0
            ? "Revisá los campos marcados antes de guardar."
            : string.Join(" ", issues);
    }

    private async Task<T> ExecuteLoggedAsync<T>(string module, string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Operacion cancelada {Module}.{Action}", module, action);
            throw;
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (AppUserFacingException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(module, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException(userMessage, incidentId, ex);
        }
    }
}

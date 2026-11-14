using System.Collections.Concurrent;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaCartaService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IPuntoVentaService puntoVentaService,
    IPuntoVentaSalonService salonService,
    ILogger<PuntoVentaCartaService> logger) : IPuntoVentaCartaService
{
    private const string ModuleName = "PuntoVentaCarta";
    private const string UsuarioQr = "QR";
    private const int MaxItemsPorPedido = 30;
    private const int MaxCantidadPorItem = 20;
    private const int MaxLineasQrPorMinuto = 60;
    private const int MaxIntentosPin = 8;
    private static readonly TimeSpan VentanaIntentosPin = TimeSpan.FromMinutes(5);

    // Intentos de PIN por base+mesa (anti fuerza bruta de los 4 digitos). En memoria del servidor.
    private static readonly ConcurrentDictionary<string, List<DateTime>> IntentosPin = new();

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // ------------------------------------------------------------------ configuracion

    public Task<PuntoVentaCartaConfigDto> GetConfigAsync(int idPuntoVenta, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetConfig", async token =>
        {
            await using var cn = await OpenAsync(token);
            var row = await cn.QuerySingleOrDefaultAsync<(int Id, string QrModo, bool AutoAcepta, string Mensaje)>(new CommandDefinition("""
                SELECT ID, ISNULL(QR_MODO, ''), ISNULL(QR_AUTOACEPTA, 0), ISNULL(QR_MENSAJE, '')
                FROM dbo.POS_PUNTOVENTA WHERE ID = @Id;
                """, new { Id = idPuntoVenta }, cancellationToken: token));
            if (row.Id <= 0)
                throw new InvalidOperationException("No se encontró el punto de venta indicado.");

            var familias = (await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT IDFAMILIA FROM dbo.POS_CARTA_FAMILIA WHERE IDPUNTOVENTA = @Id ORDER BY ORDEN, IDFAMILIA;",
                new { Id = idPuntoVenta }, cancellationToken: token))).Select(f => f.Trim()).ToList();

            return new PuntoVentaCartaConfigDto
            {
                IdPuntoVenta = idPuntoVenta,
                QrModo = NormalizeQrModo(row.QrModo, PuntoVentaQrModoKeys.No),
                AutoAcepta = row.AutoAcepta,
                Mensaje = row.Mensaje.Trim(),
                Familias = familias
            };
        }, "No se pudo cargar la configuración de la carta.", ct);

    public Task SaveConfigAsync(PuntoVentaCartaConfigDto config, string? usuarioAccion = null, CancellationToken ct = default)
        => ExecuteLoggedAsync("SaveConfig", async token =>
        {
            ArgumentNullException.ThrowIfNull(config);
            await using var cn = await OpenAsync(token);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);
            try
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.POS_PUNTOVENTA
                    SET QR_MODO = @QrModo, QR_AUTOACEPTA = @AutoAcepta, QR_MENSAJE = @Mensaje, FECHAHORA_MODIFICACION = GETDATE()
                    WHERE ID = @Id;
                    DELETE FROM dbo.POS_CARTA_FAMILIA WHERE IDPUNTOVENTA = @Id;
                    """, new
                {
                    Id = config.IdPuntoVenta,
                    QrModo = NormalizeQrModo(config.QrModo, PuntoVentaQrModoKeys.No),
                    config.AutoAcepta,
                    Mensaje = Truncate((config.Mensaje ?? string.Empty).Trim(), 200)
                }, tx, cancellationToken: token));

                var orden = 0;
                foreach (var familia in config.Familias.Select(f => (f ?? string.Empty).Trim()).Where(f => f.Length > 0).Distinct())
                {
                    await cn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO dbo.POS_CARTA_FAMILIA (IDPUNTOVENTA, IDFAMILIA, ORDEN) VALUES (@Id, @Familia, @Orden);",
                        new { Id = config.IdPuntoVenta, Familia = Truncate(familia, 30), Orden = orden++ }, tx, cancellationToken: token));
                }

                await tx.CommitAsync(token);
            }
            catch
            {
                await tx.RollbackAsync(token);
                throw;
            }

            CacheCarta.TryRemove(CacheKey(config.IdPuntoVenta), out _);
            await AuditAsync("SaveConfig", config.IdPuntoVenta.ToString(),"Configuración de la carta QR actualizada.", new { config.QrModo, config.AutoAcepta, config.Familias });
            return true;
        }, "No se pudo guardar la configuración de la carta.", ct);

    public Task<PuntoVentaCartaDto> GetCartaAsync(int idPuntoVenta, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetCarta", async token =>
        {
            // La carta armada (con precios) se cachea un rato por base + punto de venta: la abren
            // muchos celulares a la vez. Los agotados NO se cachean: se filtran siempre al momento.
            var claveCache = CacheKey(idPuntoVenta);
            if (!CacheCarta.TryGetValue(claveCache, out var cacheada) || DateTime.UtcNow - cacheada.Fecha > DuracionCacheCarta)
            {
                cacheada = (DateTime.UtcNow, await ArmarCartaAsync(idPuntoVenta, token));
                CacheCarta[claveCache] = cacheada;
            }

            HashSet<string> agotados;
            await using (var cn = await OpenAsync(token))
            {
                agotados = (await cn.QueryAsync<string>(new CommandDefinition(
                    "SELECT IDARTICULO FROM dbo.POS_ARTICULO_AGOTADO WHERE IDPUNTOVENTA = @Id;", new { Id = idPuntoVenta }, cancellationToken: token)))
                    .Select(a => a.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var carta = cacheada.Carta;
            return new PuntoVentaCartaDto
            {
                NombrePuntoVenta = carta.NombrePuntoVenta,
                QrModo = carta.QrModo,
                Mensaje = carta.Mensaje,
                Familias = carta.Familias
                    .Select(f => new PuntoVentaCartaFamiliaDto
                    {
                        IdFamilia = f.IdFamilia,
                        Descripcion = f.Descripcion,
                        Articulos = f.Articulos.Where(a => !agotados.Contains(a.IdArticulo)).ToList()
                    })
                    .Where(f => f.Articulos.Count > 0)
                    .ToList()
            };
        }, "No se pudo cargar la carta.", ct);

    private static readonly ConcurrentDictionary<string, (DateTime Fecha, PuntoVentaCartaDto Carta)> CacheCarta = new();
    private static readonly TimeSpan DuracionCacheCarta = TimeSpan.FromSeconds(60);
    private const int MaxArticulosCarta = 300;

    private string CacheKey(int idPuntoVenta)
        => $"{sessionService.GetActiveSession()?.BaseId}:{ConnectionString.GetHashCode()}:{idPuntoVenta}";

    /// <summary>
    /// Arma la carta: articulos cuya familia empieza con alguna de las familias configuradas (incluye
    /// subfamilias: "07" trae 0701, 0702...). Sin familias configuradas, toda familia con articulos.
    /// El precio sale de PuntoVentaService (misma lista/clase/oferta que el Mostrador).
    /// </summary>
    private async Task<PuntoVentaCartaDto> ArmarCartaAsync(int idPuntoVenta, CancellationToken ct)
    {
        var config = await GetConfigAsync(idPuntoVenta, ct);
        string nombrePv;
        List<(string IdArticulo, string IdFamilia)> candidatos;
        Dictionary<string, string> nombresFamilia;
        await using (var cn = await OpenAsync(ct))
        {
            nombrePv = (await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT NOMBRE FROM dbo.POS_PUNTOVENTA WHERE ID = @Id;", new { Id = idPuntoVenta }, cancellationToken: ct)))?.Trim() ?? string.Empty;

            candidatos = (await cn.QueryAsync<(string IdArticulo, string IdFamilia)>(new CommandDefinition($"""
                SELECT TOP ({MaxArticulosCarta}) LTRIM(RTRIM(a.IdArticulo)), LTRIM(RTRIM(ISNULL(a.IdFamilia, '')))
                FROM dbo.V_MA_ARTICULOS a
                WHERE ISNULL(a.Suspendido, 0) <> 1 AND ISNULL(a.SuspendidoV, 0) <> 1
                  AND LTRIM(RTRIM(ISNULL(a.IdFamilia, ''))) <> ''
                  AND (NOT EXISTS (SELECT 1 FROM dbo.POS_CARTA_FAMILIA cf WHERE cf.IDPUNTOVENTA = @Id)
                       OR EXISTS (SELECT 1 FROM dbo.POS_CARTA_FAMILIA cf
                                  WHERE cf.IDPUNTOVENTA = @Id AND LTRIM(RTRIM(a.IdFamilia)) LIKE LTRIM(RTRIM(cf.IDFAMILIA)) + '%'))
                ORDER BY a.IdFamilia, a.DESCRIPCION;
                """, new { Id = idPuntoVenta }, cancellationToken: ct))).ToList();

            nombresFamilia = (await cn.QueryAsync<(string Id, string Descripcion)>(new CommandDefinition(
                "SELECT LTRIM(RTRIM(IdFamilia)), LTRIM(RTRIM(ISNULL(Descripcion, ''))) FROM dbo.V_TA_FAMILIAS;", cancellationToken: ct)))
                .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Descripcion, StringComparer.OrdinalIgnoreCase);
        }

        var settings = await puntoVentaService.GetSettingsAsync(ct);
        var idBase = sessionService.GetActiveSession()?.BaseId;

        // Cada articulo va a la familia configurada mas especifica que lo contiene (o a la suya).
        var familiasConfig = config.Familias.OrderByDescending(f => f.Length).ToList();
        string GrupoDe(string idFamilia) => familiasConfig.FirstOrDefault(f => idFamilia.StartsWith(f, StringComparison.OrdinalIgnoreCase)) ?? idFamilia;
        var orden = config.Familias.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i, StringComparer.OrdinalIgnoreCase);

        var grupos = new Dictionary<string, List<PuntoVentaCartaArticuloDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (idArticulo, idFamilia) in candidatos)
        {
            var articulo = await puntoVentaService.GetArticuloPorCodigoAsync(idArticulo, ct);
            if (articulo is null || articulo.PrecioUnitario <= 0)
                continue;

            var grupo = GrupoDe(idFamilia);
            if (!grupos.TryGetValue(grupo, out var lista))
                grupos[grupo] = lista = [];
            lista.Add(new PuntoVentaCartaArticuloDto
            {
                IdArticulo = articulo.IdArticulo.Trim(),
                Descripcion = ToTitulo(articulo.Descripcion),
                Presentacion = articulo.Presentacion,
                Precio = articulo.PrecioUnitario,
                ImagenUrl = ArticuloImagenUrlHelper.BuildPublicImageUrl(settings.FtpCodigoCta, articulo.IdArticulo, idBase, thumbnail: true)
            });
        }

        return new PuntoVentaCartaDto
        {
            NombrePuntoVenta = nombrePv,
            QrModo = config.QrModo,
            Mensaje = config.Mensaje,
            Familias = grupos
                .OrderBy(g => orden.TryGetValue(g.Key, out var i) ? i : int.MaxValue)
                .ThenBy(g => g.Key)
                .Select(g => new PuntoVentaCartaFamiliaDto
                {
                    IdFamilia = g.Key,
                    Descripcion = ToTitulo(nombresFamilia.TryGetValue(g.Key, out var nombre) && nombre.Length > 0 ? nombre : g.Key),
                    Articulos = g.Value.OrderBy(a => a.Descripcion).ToList()
                })
                .ToList()
        };
    }

    /// <summary>"TAPAS Y PICADAS" -> "Tapas y picadas" (los maestros suelen estar en mayusculas).</summary>
    internal static string ToTitulo(string texto)
    {
        var t = (texto ?? string.Empty).Trim();
        if (t.Length == 0 || t.Any(char.IsLower))
            return t;
        var minus = t.ToLower(System.Globalization.CultureInfo.GetCultureInfo("es-AR"));
        return char.ToUpper(minus[0], System.Globalization.CultureInfo.GetCultureInfo("es-AR")) + minus[1..];
    }

    // ------------------------------------------------------------------ carta publica

    public Task<PuntoVentaMesaPublicaDto?> GetMesaPublicaAsync(int idMesa, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetMesaPublica", async token =>
        {
            await using var cn = await OpenAsync(token);
            return await LoadMesaPublicaAsync(cn, idMesa, token);
        }, "No se pudo cargar la mesa.", ct);

    public Task<PuntoVentaMesaSesionDto?> AbrirMesaAsync(int idMesa, CancellationToken ct = default)
        => ExecuteLoggedAsync("AbrirMesaQr", async token =>
        {
            var mesa = await RequireMesaQrAsync(idMesa, requierePedido: true, token);
            if (mesa.IdPedidoAbierto is > 0)
                return null;

            var result = await salonService.AbrirMesaAsync(new PuntoVentaSalonAbrirMesaRequest
            {
                IdMesa = idMesa,
                Observaciones = "Abierta desde la carta QR",
                UsuarioAccion = UsuarioQr
            }, token);
            if (result.YaEstabaAbierta)
                return null;

            await using var cn = await OpenAsync(token);
            var pin = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT PIN FROM dbo.POS_PEDIDO WHERE ID = @Id;", new { Id = result.IdPedido }, cancellationToken: token));
            return new PuntoVentaMesaSesionDto { IdPedido = result.IdPedido, Pin = pin?.Trim() ?? string.Empty };
        }, "No se pudo abrir la mesa.", ct);

    public Task<PuntoVentaMesaSesionDto?> UnirseAsync(int idMesa, string pin, CancellationToken ct = default)
        => ExecuteLoggedAsync("UnirseMesa", async token =>
        {
            var clave = $"{sessionService.GetActiveSession()?.BaseId}:{idMesa}";
            if (!RegistrarIntentoPin(clave))
                throw new InvalidOperationException("Demasiados intentos. Esperá unos minutos o pedile el código al mozo.");

            await using var cn = await OpenAsync(token);
            var idPedido = await cn.ExecuteScalarAsync<int?>(new CommandDefinition("""
                SELECT TOP (1) ID FROM dbo.POS_PEDIDO
                WHERE IDMESA = @IdMesa AND MODALIDAD = 'SALON' AND ESTADO IN ('ABIERTA', 'CUENTA') AND PIN = @Pin
                ORDER BY ID;
                """, new { IdMesa = idMesa, Pin = (pin ?? string.Empty).Trim() }, cancellationToken: token));

            if (idPedido is not > 0)
                return null;

            IntentosPin.TryRemove(clave, out _);
            return new PuntoVentaMesaSesionDto { IdPedido = idPedido.Value, Pin = (pin ?? string.Empty).Trim() };
        }, "No se pudo validar el código de la mesa.", ct);

    public Task<PuntoVentaMesaEstadoPublicoDto?> GetEstadoAsync(int idMesa, PuntoVentaMesaSesionDto sesion, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetEstadoPublico", async token =>
        {
            await using var cn = await OpenAsync(token);
            var estado = await ValidarSesionAsync(cn, idMesa, sesion, token);
            if (estado is null)
                return null;

            var detalle = await salonService.GetMesaDetalleAsync(sesion.IdPedido, token);
            var llamados = (await cn.QueryAsync<(string Tipo, bool Atendido)>(new CommandDefinition("""
                SELECT TIPO, ATENDIDO FROM dbo.POS_MESA_LLAMADO
                WHERE IDPEDIDO = @Id AND FECHAHORA >= DATEADD(MINUTE, -30, GETDATE())
                ORDER BY ID DESC;
                """, new { Id = sesion.IdPedido }, cancellationToken: token))).ToList();
            var ultimoMozo = llamados.FirstOrDefault(l => l.Tipo == PuntoVentaLlamadoTipoKeys.Mozo);

            return new PuntoVentaMesaEstadoPublicoDto
            {
                IdPedido = sesion.IdPedido,
                Estado = estado,
                Lineas = detalle?.Lineas.Where(l => l.Estado != PuntoVentaSalonLineaEstadoKeys.Anulado).ToList() ?? [],
                LlamadoMozoPendiente = ultimoMozo.Tipo is not null && !ultimoMozo.Atendido,
                LlamadoMozoAtendido = ultimoMozo.Tipo is not null && ultimoMozo.Atendido,
                CuentaPedida = estado == PuntoVentaSalonMesaEstadoKeys.Cuenta
                               || llamados.Any(l => l.Tipo == PuntoVentaLlamadoTipoKeys.Cuenta && !l.Atendido)
            };
        }, "No se pudo cargar el estado de la mesa.", ct);

    public Task PedirAsync(int idMesa, PuntoVentaMesaSesionDto sesion, IReadOnlyList<PuntoVentaCartaItemRequest> items, CancellationToken ct = default)
        => ExecuteLoggedAsync("PedirQr", async token =>
        {
            ArgumentNullException.ThrowIfNull(items);
            var pedidos = items
                .Where(i => !string.IsNullOrWhiteSpace(i.IdArticulo) && i.Cantidad > 0)
                .ToList();
            if (pedidos.Count == 0)
                throw new InvalidOperationException("Elegí al menos un producto.");
            if (pedidos.Count > MaxItemsPorPedido)
                throw new InvalidOperationException($"Podés enviar hasta {MaxItemsPorPedido} productos por vez.");

            var mesa = await RequireMesaQrAsync(idMesa, requierePedido: true, token);

            await using (var cn = await OpenAsync(token))
            {
                if (await ValidarSesionAsync(cn, idMesa, sesion, token) is null)
                    throw new InvalidOperationException("La mesa ya se cerró o el código no es válido. Volvé a escanear el QR.");

                var recientes = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.POS_PEDIDO_DET
                    WHERE IDPEDIDO = @Id AND ORIGEN = 'QR' AND FECHAHORA_ALTA >= DATEADD(MINUTE, -1, GETDATE());
                    """, new { Id = sesion.IdPedido }, cancellationToken: token));
                if (recientes + pedidos.Count > MaxLineasQrPorMinuto)
                    throw new InvalidOperationException("Recibimos muchos pedidos seguidos. Esperá un minuto o llamá al mozo.");
            }

            // Solo se acepta lo que esta en la carta y con el precio del servidor.
            var carta = await GetCartaAsync(mesa.IdPuntoVenta, token);
            var enCarta = carta.Familias.SelectMany(f => f.Articulos).Select(a => a.IdArticulo).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var lineas = new List<PuntoVentaPedidoItemDto>();
            foreach (var pedido in pedidos)
            {
                var id = pedido.IdArticulo.Trim();
                if (!enCarta.Contains(id))
                    throw new InvalidOperationException("Uno de los productos ya no está disponible. Actualizá la carta.");

                var articulo = await puntoVentaService.GetArticuloPorCodigoAsync(id, token)
                    ?? throw new InvalidOperationException("Uno de los productos ya no está disponible. Actualizá la carta.");

                lineas.Add(new PuntoVentaPedidoItemDto
                {
                    IdArticulo = articulo.IdArticulo.Trim(),
                    Descripcion = articulo.Descripcion,
                    IdUnidad = articulo.IdUnidad,
                    Presentacion = articulo.Presentacion,
                    Cantidad = Math.Clamp(Math.Round(pedido.Cantidad, 0), 1, MaxCantidadPorItem),
                    Precio = articulo.PrecioUnitario,
                    TasaIva = articulo.TasaIva,
                    Exento = articulo.Exento,
                    Familia = articulo.Familia,
                    Observaciones = Truncate((pedido.Observaciones ?? string.Empty).Trim(), 120)
                });
            }

            await salonService.AgregarItemsAsync(sesion.IdPedido, lineas, enviar: mesa.AutoAcepta, PuntoVentaSalonOrigenKeys.Qr, UsuarioQr, token);
            return true;
        }, "No se pudo enviar el pedido.", ct);

    public Task LlamarAsync(int idMesa, PuntoVentaMesaSesionDto sesion, string tipo, CancellationToken ct = default)
        => ExecuteLoggedAsync("LlamarMozo", async token =>
        {
            var tipoNormalizado = string.Equals(tipo, PuntoVentaLlamadoTipoKeys.Cuenta, StringComparison.OrdinalIgnoreCase)
                ? PuntoVentaLlamadoTipoKeys.Cuenta
                : PuntoVentaLlamadoTipoKeys.Mozo;

            var mesa = await RequireMesaQrAsync(idMesa, requierePedido: false, token);
            await using var cn = await OpenAsync(token);
            if (await ValidarSesionAsync(cn, idMesa, sesion, token) is null)
                throw new InvalidOperationException("La mesa ya se cerró o el código no es válido. Volvé a escanear el QR.");

            await cn.ExecuteAsync(new CommandDefinition("""
                IF NOT EXISTS (SELECT 1 FROM dbo.POS_MESA_LLAMADO WHERE IDPEDIDO = @IdPedido AND TIPO = @Tipo AND ATENDIDO = 0)
                    INSERT INTO dbo.POS_MESA_LLAMADO (IDPUNTOVENTA, IDMESA, IDPEDIDO, TIPO, FECHAHORA)
                    VALUES (@IdPuntoVenta, @IdMesa, @IdPedido, @Tipo, GETDATE());
                """, new { mesa.IdPuntoVenta, IdMesa = idMesa, sesion.IdPedido, Tipo = tipoNormalizado }, cancellationToken: token));

            if (tipoNormalizado == PuntoVentaLlamadoTipoKeys.Cuenta)
                await salonService.PedirCuentaAsync(sesion.IdPedido, UsuarioQr, token);
            return true;
        }, "No se pudo avisar al mozo.", ct);

    // ------------------------------------------------------------------ helpers

    private static async Task<PuntoVentaMesaPublicaDto?> LoadMesaPublicaAsync(SqlConnection cn, int idMesa, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<PuntoVentaMesaPublicaDto>(new CommandDefinition("""
            SELECT m.ID AS IdMesa, m.NOMBRE AS NombreMesa, s.NOMBRE AS NombreSector,
                pv.ID AS IdPuntoVenta, pv.NOMBRE AS NombrePuntoVenta,
                ISNULL(pv.QR_MODO, 'NO') AS QrModo, ISNULL(pv.QR_AUTOACEPTA, 0) AS AutoAcepta,
                ISNULL(pv.QR_MENSAJE, '') AS Mensaje,
                (SELECT TOP (1) p.ID FROM dbo.POS_PEDIDO p
                 WHERE p.IDMESA = m.ID AND p.MODALIDAD = 'SALON' AND p.ESTADO IN ('ABIERTA', 'CUENTA') ORDER BY p.ID) AS IdPedidoAbierto
            FROM dbo.POS_MESA m
            INNER JOIN dbo.POS_SECTOR s ON s.ID = m.IDSECTOR
            INNER JOIN dbo.POS_PUNTOVENTA pv ON pv.ID = s.IDPUNTOVENTA
            WHERE m.ID = @IdMesa AND m.ACTIVO = 1 AND s.ACTIVO = 1 AND pv.ACTIVO = 1;
            """, new { IdMesa = idMesa }, cancellationToken: ct));

    private async Task<PuntoVentaMesaPublicaDto> RequireMesaQrAsync(int idMesa, bool requierePedido, CancellationToken ct)
    {
        await using var cn = await OpenAsync(ct);
        var mesa = await LoadMesaPublicaAsync(cn, idMesa, ct)
            ?? throw new InvalidOperationException("Esta mesa no está disponible.");
        var modo = NormalizeQrModo(mesa.QrModo, PuntoVentaQrModoKeys.No);
        if (modo == PuntoVentaQrModoKeys.No || (requierePedido && modo != PuntoVentaQrModoKeys.Pedido))
            throw new InvalidOperationException("Este local no toma pedidos desde la carta. Llamá al mozo.");
        return mesa;
    }

    /// <summary>Devuelve el estado de la mesa si el PIN corresponde al pedido abierto de esa mesa; null si no.</summary>
    private static async Task<string?> ValidarSesionAsync(SqlConnection cn, int idMesa, PuntoVentaMesaSesionDto sesion, CancellationToken ct)
    {
        if (sesion is null || sesion.IdPedido <= 0 || string.IsNullOrWhiteSpace(sesion.Pin))
            return null;

        var estado = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT ESTADO FROM dbo.POS_PEDIDO
            WHERE ID = @Id AND IDMESA = @IdMesa AND MODALIDAD = 'SALON' AND PIN = @Pin AND ESTADO IN ('ABIERTA', 'CUENTA');
            """, new { Id = sesion.IdPedido, IdMesa = idMesa, Pin = sesion.Pin.Trim() }, cancellationToken: ct));
        return estado?.Trim();
    }

    private static bool RegistrarIntentoPin(string clave)
    {
        var ahora = DateTime.UtcNow;
        var intentos = IntentosPin.GetOrAdd(clave, _ => []);
        lock (intentos)
        {
            intentos.RemoveAll(t => ahora - t > VentanaIntentosPin);
            if (intentos.Count >= MaxIntentosPin)
                return false;
            intentos.Add(ahora);
            return true;
        }
    }

    internal static string NormalizeQrModo(string? valor, string porDefecto)
    {
        var modo = (valor ?? string.Empty).Trim().ToUpperInvariant();
        return PuntoVentaQrModoKeys.All.Contains(modo) ? modo : porDefecto;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private async Task AuditAsync(string action, string entityId, string message, object? data)
    {
        try
        {
            await appEvents.LogAuditAsync(ModuleName, action, "POS_PUNTOVENTA", entityId, message, data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo auditar {Action}.", action);
        }
    }

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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
}

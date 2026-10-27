using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Pedidos desde el chat (F05, 2026-10-27). Reglas:
// - Sólo Cliente identificado (la cuenta la resuelve ConversacionesService, nunca el modelo).
// - Artículos y precios del catálogo web por defecto, igual que el carrito: lo que se le muestra al
//   cliente es lo que se graba (ConfirmarPedidoCarritoAsync vuelve a validar todo).
// - El modelo sólo puede PREPARAR. Confirmar/cancelar lo decide ConversacionesService con la respuesta
//   literal del cliente ("sí"/"no") contra el borrador guardado: la IA no puede registrar un pedido sola.
public sealed class ConversacionBotPedidosService(
    IConfiguration configuration,
    ISessionService sessionService,
    IInterfacesCatalogosService catalogosService,
    IAppEventService appEvents) : IConversacionBotPedidosService
{
    private const string ModuleName = "Conversaciones";
    internal const int MaxOpciones = 5;
    internal static readonly TimeSpan VigenciaBorrador = TimeSpan.FromHours(12);

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    internal sealed record LineaBorrador(string IdArticulo, string Descripcion, decimal Cantidad, decimal Precio);

    public async Task<string> PrepararAsync(long idConversacion, ConversacionCuentaVinculadaDto cuenta, string argumentosJson, CancellationToken ct = default)
    {
        try
        {
            return await PrepararCoreAsync(idConversacion, cuenta, argumentosJson, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin esto el despachador de tools se tragaba el motivo (catálogo, tabla sin crear...).
            await appEvents.LogErrorAsync(ModuleName, "BotPedidoPreparar", ex,
                "No se pudo armar el pedido pedido por chat.",
                new { idConversacion, cliente = cuenta.Codigo }, AppEventSeverity.Warning, ct);
            return "No se pudo armar el pedido por un error del sistema. Respondé tipo DERIVA: avisale que un asesor le toma el pedido; también podés ofrecerle el link del portal.";
        }
    }

    private async Task<string> PrepararCoreAsync(long idConversacion, ConversacionCuentaVinculadaDto cuenta, string argumentosJson, CancellationToken ct)
    {
        if (idConversacion <= 0 || cuenta.Tipo != CuentaComercialTipo.Cliente || cuenta.EsAmbigua)
            return "No se pudo identificar al cliente para tomar el pedido. Respondé tipo DERIVA.";

        var pedidos = LeerItems(argumentosJson);
        if (pedidos.Count == 0)
            return "No se indicaron artículos. Respondé tipo ACLARA preguntando qué artículos y cuántos quiere.";

        var idBase = sessionService.GetActiveSession()?.BaseId ?? 0;
        var catalogo = idBase > 0 ? await catalogosService.GetCatalogoPublicoAsync(0, expectedBaseId: idBase, ct) : null;
        if (catalogo is null || !catalogo.HabilitarCarrito)
            return "Por chat no se pueden tomar pedidos porque el catálogo web no tiene habilitada la toma de pedidos. Ofrecele hacerlo desde el link del portal (generar_link_portal) o que un asesor lo toma.";

        // Precio del cliente (el que informa consultar_precio y el que graba ConfirmarPedidoCarritoAsync).
        var articulosCliente = await catalogosService.AplicarPrecioClienteAsync(catalogo.Articulos, cuenta.Codigo, idBase, ct);

        var lineas = new List<LineaBorrador>();
        var problemas = new StringBuilder();
        foreach (var (articulo, cantidad) in pedidos)
        {
            if (cantidad <= 0)
            {
                problemas.AppendLine($"- \"{articulo}\": falta la cantidad.");
                continue;
            }

            var candidatos = BuscarEnCatalogo(articulosCliente, articulo);
            if (candidatos.Count == 0)
                problemas.AppendLine($"- \"{articulo}\": no está en el catálogo web.");
            else if (candidatos.Count > 1)
                problemas.AppendLine($"- \"{articulo}\": hay varias opciones: "
                    + string.Join("; ", candidatos.Take(MaxOpciones).Select(a => $"{a.DescripcionArticulo.Trim()} (código {a.IdArticulo.Trim()})")) + ".");
            else
                lineas.Add(new LineaBorrador(candidatos[0].IdArticulo.Trim(), candidatos[0].DescripcionArticulo.Trim(), cantidad, CatalogosPriceDisplayHelper.GetPrecioAplicado(candidatos[0])));
        }

        if (problemas.Length > 0)
            return "No se armó el pedido todavía:\n" + problemas.ToString().Trim()
                + "\nRespondé tipo ACLARA con UNA pregunta para resolverlo (qué opción, qué cantidad, o si quiere otro artículo). No digas que el pedido está hecho.";

        var total = lineas.Sum(l => l.Cantidad * l.Precio);
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.CONV_BOT_PEDIDOS SET Estado = N'REEMPLAZADO', FechaEstado = GETDATE()
                WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE';
                INSERT INTO dbo.CONV_BOT_PEDIDOS (IdConversacion, ClienteCodigo, IdInsertCatalogo, LineasJson, Total)
                VALUES (@IdConversacion, @Cliente, @IdInsert, @Lineas, @Total);
                """,
                new { IdConversacion = idConversacion, Cliente = cuenta.Codigo.Trim(), IdInsert = catalogo.IdInsert, Lineas = JsonSerializer.Serialize(lineas), Total = total },
                cancellationToken: ct));
        }

        return FormatearResumen(lineas, total)
            + "\nRespondé tipo ACLARA mostrándole este resumen y preguntándole si lo confirma: que responda SÍ para registrarlo o NO para cancelarlo. "
            + "NO digas que el pedido está registrado: se graba recién cuando el cliente confirma.";
    }

    public async Task<bool> HayPendienteAsync(long idConversacion, string codigoCliente, CancellationToken ct = default)
        => await LeerPendienteAsync(idConversacion, codigoCliente, ct) is not null;

    public async Task<string> ConfirmarAsync(long idConversacion, ConversacionCuentaVinculadaDto cuenta, CancellationToken ct = default)
    {
        var pendiente = await LeerPendienteAsync(idConversacion, cuenta.Codigo, ct);
        if (pendiente is null)
            return "No tengo un pedido pendiente para confirmar. Decime qué artículos y cuántos querés y te lo armo.";

        var (idBorrador, idInsert, lineas) = pendiente.Value;
        try
        {
            var resultado = await catalogosService.ConfirmarPedidoCarritoAsync(new CatalogoPedidoConfirmarRequestDto
            {
                IdInsert = idInsert,
                CodigoCliente = cuenta.Codigo.Trim(),
                IdBase = sessionService.GetActiveSession()?.BaseId,
                Lineas = lineas.Select(l => new CatalogoPedidoLineaSolicitudDto { IdArticulo = l.IdArticulo, Cantidad = l.Cantidad }).ToList(),
                Observaciones = $"Pedido por chat (asistente) - Conversación #{idConversacion}"
            }, ct);

            await MarcarAsync(idBorrador, "CONFIRMADO", resultado.IdComprobanteTexto, null, ct);
            var cultura = CultureInfo.GetCultureInfo("es-AR");
            return $"¡Listo! Registré tu pedido {resultado.IdComprobanteTexto} por $ {resultado.Total.ToString("N2", cultura)}. Te avisamos cuando esté preparado.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarcarAsync(idBorrador, "ERROR", null, ex.Message, ct);
            await appEvents.LogErrorAsync(ModuleName, "BotPedidoConfirmar", ex,
                "No se pudo registrar el pedido armado por el asistente.",
                new { idConversacion, idBorrador }, AppEventSeverity.Warning, ct);
            return "No pude registrar el pedido en este momento. Un asesor lo revisa y te confirma a la brevedad.";
        }
    }

    public async Task<string> CancelarAsync(long idConversacion, string codigoCliente, CancellationToken ct = default)
    {
        var pendiente = await LeerPendienteAsync(idConversacion, codigoCliente, ct);
        if (pendiente is null)
            return "No hay ningún pedido pendiente. ¿Te ayudo con algo más?";

        await MarcarAsync(pendiente.Value.IdBorrador, "CANCELADO", null, null, ct);
        return "Listo, cancelé el pedido; no se registró nada. Si querés armar otro, decime qué artículos y cuántos.";
    }

    /// <summary>"Sí", "dale", "confirmo"... como respuesta corta, sin negación.</summary>
    internal static bool EsConfirmacion(string? texto)
    {
        var t = Normalizar(texto);
        return t.Length > 0 && t.Split(' ').Length <= 8
            && !Regex.IsMatch(t, @"\b(no|cancel\w*|anul\w*)\b")
            && Regex.IsMatch(t, @"^(si|dale|ok|okey|okay|listo|perfecto|correcto|confirmo|confirmado|confirmar|confirmalo|de acuerdo|va|hacelo|mandalo|cargalo|registralo|adelante)\b");
    }

    /// <summary>"No", "cancelalo", "mejor no"... como respuesta corta.</summary>
    internal static bool EsCancelacion(string? texto)
    {
        var t = Normalizar(texto);
        return t.Length > 0 && t.Split(' ').Length <= 8
            && Regex.IsMatch(t, @"^(no|mejor no|cancela\w*|anula\w*|dejalo|olvidalo|no gracias)\b");
    }

    internal static IReadOnlyList<CatalogosCatalogoItemDto> BuscarEnCatalogo(IReadOnlyList<CatalogosCatalogoItemDto> articulos, string pedido)
    {
        var exacto = articulos.Where(a => string.Equals(a.IdArticulo.Trim(), pedido.Trim(), StringComparison.OrdinalIgnoreCase)
                                          || (!string.IsNullOrWhiteSpace(a.CodigoBarra) && string.Equals(a.CodigoBarra.Trim(), pedido.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (exacto.Count > 0)
            return exacto;

        var terminos = ConversacionAsistenteHerramientasService.NormalizarBusquedaArticulo(pedido)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terminos.Length == 0)
            return [];

        var coinciden = articulos
            .Where(a =>
            {
                var texto = Normalizar($"{a.IdArticulo} {a.DescripcionArticulo} {a.Marca} {a.Presentacion}");
                return terminos.All(t => texto.Contains(t, StringComparison.Ordinal));
            })
            .ToList();

        // "pila AA" también trae "AAA" por substring: si algunas tienen todos los términos como palabra
        // completa, se usan sólo esas (mismo criterio que consultar_precio).
        var exactas = coinciden.Where(a =>
        {
            var palabras = Regex.Split(Normalizar($"{a.IdArticulo} {a.DescripcionArticulo} {a.Marca} {a.Presentacion}"), "[^a-z0-9]+").ToHashSet(StringComparer.Ordinal);
            return terminos.All(palabras.Contains);
        }).ToList();
        return exactas.Count > 0 && exactas.Count < coinciden.Count ? exactas : coinciden;
    }

    internal static string FormatearResumen(IReadOnlyList<LineaBorrador> lineas, decimal total)
    {
        var cultura = CultureInfo.GetCultureInfo("es-AR");
        var sb = new StringBuilder("Pedido armado (todavía SIN registrar):\n");
        foreach (var l in lineas)
            sb.AppendLine($"- {l.Cantidad.ToString("0.##", cultura)} x {l.Descripcion} (código {l.IdArticulo}) a $ {l.Precio.ToString("N2", cultura)} = $ {(l.Cantidad * l.Precio).ToString("N2", cultura)}");
        sb.Append($"Total: $ {total.ToString("N2", cultura)} (precios de su cuenta).");
        return sb.ToString();
    }

    internal static List<(string Articulo, decimal Cantidad)> LeerItems(string? argumentosJson)
    {
        var items = new List<(string, decimal)>();
        if (string.IsNullOrWhiteSpace(argumentosJson))
            return items;
        try
        {
            using var doc = JsonDocument.Parse(argumentosJson);
            if (!doc.RootElement.TryGetProperty("items", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return items;
            foreach (var item in lista.EnumerateArray())
            {
                var articulo = item.TryGetProperty("articulo", out var a) && a.ValueKind == JsonValueKind.String ? (a.GetString() ?? string.Empty).Trim() : string.Empty;
                var cantidad = item.TryGetProperty("cantidad", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var n) ? n : 0m;
                if (articulo.Length > 0)
                    items.Add((articulo, cantidad));
            }
        }
        catch (JsonException)
        {
        }
        return items;
    }

    private async Task<(int IdBorrador, int IdInsert, List<LineaBorrador> Lineas)?> LeerPendienteAsync(long idConversacion, string codigoCliente, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_BOT_PEDIDOS', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0)
            return null;

        var fila = await cn.QueryFirstOrDefaultAsync<BorradorRow>(new CommandDefinition(
            """
            SELECT TOP (1) IdBorrador, IdInsertCatalogo, LineasJson
            FROM dbo.CONV_BOT_PEDIDOS
            WHERE IdConversacion = @IdConversacion
              AND Estado = N'PENDIENTE'
              AND UPPER(LTRIM(RTRIM(ClienteCodigo))) = UPPER(LTRIM(RTRIM(@Cliente)))
              AND FechaAlta >= @Desde
            ORDER BY IdBorrador DESC;
            """,
            new { IdConversacion = idConversacion, Cliente = codigoCliente ?? string.Empty, Desde = DateTime.Now - VigenciaBorrador },
            cancellationToken: ct));
        if (fila is null)
            return null;

        var lineas = JsonSerializer.Deserialize<List<LineaBorrador>>(fila.LineasJson) ?? [];
        return lineas.Count == 0 ? null : (fila.IdBorrador, fila.IdInsertCatalogo, lineas);
    }

    private sealed class BorradorRow
    {
        public int IdBorrador { get; set; }
        public int IdInsertCatalogo { get; set; }
        public string LineasJson { get; set; } = "[]";
    }

    private async Task MarcarAsync(int idBorrador, string estado, string? idComprobante, string? error, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.CONV_BOT_PEDIDOS
            SET Estado = @Estado, FechaEstado = GETDATE(), IdComprobanteTexto = @IdComprobante, Error = LEFT(@Error, 500)
            WHERE IdBorrador = @IdBorrador;
            """,
            new { IdBorrador = idBorrador, Estado = estado, IdComprobante = idComprobante, Error = error },
            cancellationToken: ct));
    }

    private static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;
        var normalized = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return Regex.Replace(Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"[¿?¡!.,;:]", " "), @"\s+", " ").Trim();
    }
}

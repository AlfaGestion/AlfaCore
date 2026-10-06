using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Ejecuta las herramientas del asistente de Conversaciones. Reutiliza servicios ya existentes
// (ICrmCotizacionService para precios, IPortalClienteService para saldo de Cliente,
// IProveedorSaldoService para saldo de Proveedor) y agrega la consulta de Notas de Pedido (NP)
// directo contra dbo.V_MV_Cpte, que no tenía un método reusable con el estado que necesita el
// asistente (ver conversaciones_agente_ia_plan.md).
//
// GUARDRAIL DE SEGURIDAD: ninguna herramienta sensible recibe la cuenta desde argumentosJson (lo
// que manda el modelo) -- siempre se usa el parámetro `cuenta`, resuelto server-side por
// ConversacionesService antes de ofrecer las herramientas. Aunque el cliente escriba "dame el saldo
// de la cuenta 000123" en el chat, no hay forma de que ese texto llegue a determinar qué cuenta se
// consulta.
public sealed class ConversacionAsistenteHerramientasService(
    IConfiguration configuration,
    ISessionService sessionService,
    ICrmCotizacionService crmCotizacionService,
    IInterfacesCatalogosService catalogosService,
    ICentralPublicLinkService publicLinkService,
    ICentralBasesService centralBasesService,
    ICentralClientesService centralClientesService,
    IPortalClienteService portalClienteService,
    IProveedorSaldoService proveedorSaldoService,
    IConversacionesConfigService conversacionesConfigService,
    ICatalogoClienteLinkService catalogoClienteLinkService) : IConversacionAsistenteHerramientasService
{
    private const string ToolConsultarPrecio = "consultar_precio";
    private const string ToolConsultarSaldoTotal = "consultar_saldo_total";
    private const string ToolConsultarSaldoDetalle = "consultar_saldo_detalle";
    private const string ToolConsultarPedidos = "consultar_pedidos";
    private const string ToolGenerarLinkPortal = "generar_link_portal";
    private const string ToolGenerarLinkCatalogoPublico = "generar_link_catalogo_publico";

    // Defensa en profundidad para identidad ambigua: aunque ObtenerHerramientasDisponibles ya no
    // ofrece estas tools cuando EsAmbigua, este set corta también acá -- por si el modelo alucina un
    // nombre de tool no ofrecido, no depende únicamente del filtro de la lista.
    private static readonly HashSet<string> ToolsPrivadasDeCuenta = new(StringComparer.Ordinal)
    {
        ToolConsultarPrecio, ToolConsultarSaldoTotal, ToolConsultarSaldoDetalle, ToolConsultarPedidos, ToolGenerarLinkPortal
    };

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // Palabras que indican que el mensaje puede necesitar alguna herramienta. Filtro deliberadamente
    // amplio (mejor un falso positivo que uno negativo) -- el objetivo NO es adivinar la intención con
    // precisión, es evitar mandarle la lista de 5 herramientas a OpenAI en TODOS los mensajes. Con
    // gpt-4o-mini se comprobó en producción que ofrecer tool-calling en cada respuesta lo vuelve
    // sobre-cauteloso incluso para un simple "hola" (deriva a un humano en vez de saludar), aunque el
    // prompt le aclare que las herramientas son opcionales -- es una limitación real del modelo
    // combinando function-calling con el JSON forzado, no un tema de instrucciones. Reducir cuándo se
    // ofrecen las herramientas restaura el comportamiento normal para la charla común.
    private static readonly string[] PalabrasClaveHerramientas =
    [
        "precio", "precios", "cuesta", "cuesto", "cuánto sale", "cuanto sale", "vale", "valen", "cotiz",
        "saldo", "deuda", "debo", "cuenta corriente", "cta cte", "cta.cte", "cuánto debo", "cuanto debo",
        "pendiente de pago", "factura pendiente", "cobranza", "cobranzas",
        "pedido", "pedidos", "nota de pedido", "np-",
        "portal", "autogestión", "autogestion", "acceso online", "ver mi cuenta", "cuenta online",
        "catálogo", "catalogo", "producto", "productos", "comprar", "compra"
    ];

    private static bool MensajeNecesitaHerramientas(string mensajeCliente)
    {
        var texto = (mensajeCliente ?? string.Empty).ToLowerInvariant();
        return texto.Length > 0 && PalabrasClaveHerramientas.Any(texto.Contains);
    }

    /// <summary>
    /// Intención explícita de precio ("precio", "cuánto sale/cuesta/vale", "a cuánto", "valor",
    /// "cotización"). Se usa para decidir la respuesta a un contacto sin identificar cuando la empresa
    /// no informa precios a leads -- no para ofrecer herramientas (eso sigue siendo
    /// MensajeNecesitaHerramientas).
    /// </summary>
    internal static bool MensajePidePrecio(string? mensajeCliente)
    {
        var texto = NormalizarTexto(mensajeCliente);
        if (texto.Length == 0)
            return false;

        return Regex.IsMatch(texto,
            @"\b(precio|precios|valor|valores|cotizacion|cotizar|cotizame)\b"
            + @"|\bcuanto\s+(sale|salen|cuesta|cuestan|vale|valen|esta|estan)\b"
            + @"|\ba\s+cuanto\b");
    }

    /// <summary>
    /// Pide el precio de un artículo puntual. Excluye preguntas generales sobre precios ("¿los precios
    /// incluyen IVA?", "lista de precios", "¿aumentaron los precios?") que no se responden buscando un
    /// artículo. Es la que decide forzar consultar_precio.
    /// </summary>
    internal static bool MensajePidePrecioArticulo(string? mensajeCliente)
    {
        if (!MensajePidePrecio(mensajeCliente))
            return false;

        var texto = NormalizarTexto(mensajeCliente);
        return !Regex.IsMatch(texto,
            @"\b(incluye|incluyen|con|sin|mas)\s+iva\b"
            + @"|\blista\s+de\s+precios?\b"
            + @"|\b(aumentaron|subieron|bajaron|cambiaron|actualizaron)\b"
            + @"|\bprecios?\s+(actualizados?|vigentes?|nuevos?)\b");
    }

    private static readonly string[] InicioPreguntaNueva =
    [
        "que ", "como ", "cual ", "cuales ", "donde ", "cuando ", "quien ", "por que ", "porque ",
        "tienen ", "tenes ", "hacen ", "haces ", "venden ", "aceptan ", "puedo ", "se puede "
    ];

    /// <summary>
    /// Si el mensaje puede ser la respuesta a una aclaración de precio ("AA", "Duracell", "la de 9 V").
    /// Una pregunta nueva ("¿Qué es el código RECICLA10?") no completa la consulta anterior y no debe
    /// forzar consultar_precio con el artículo del mensaje previo (2026-10-06).
    /// </summary>
    internal static bool PuedeCompletarAclaracion(string? mensajeCliente)
    {
        var texto = Regex.Replace(NormalizarTexto(mensajeCliente), @"[¿?¡!.,;:]", " ").Trim();
        if (texto.Length == 0)
            return false;

        var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var conEspacio = string.Join(' ', palabras) + " ";
        return palabras.Length <= 6 && !InicioPreguntaNueva.Any(conEspacio.StartsWith);
    }

    private static bool MensajePideCatalogo(string mensajeCliente)
    {
        var texto = NormalizarTexto(mensajeCliente);
        if (texto.Length == 0)
            return false;

        return texto.Contains("catalogo", StringComparison.Ordinal)
            || texto.Contains("ver productos", StringComparison.Ordinal)
            || texto.Contains("ver los productos", StringComparison.Ordinal)
            || texto.Contains("mostrar productos", StringComparison.Ordinal)
            || texto.Contains("pasame productos", StringComparison.Ordinal)
            || texto.Contains("pasar productos", StringComparison.Ordinal)
            || texto.Contains("lista de productos", StringComparison.Ordinal);
    }

    // Palabras que el cliente/modelo suele incluir pero nunca forman parte de la descripción de un
    // artículo. Van sin tildes (se comparan contra el texto ya normalizado).
    private static readonly HashSet<string> PalabrasVaciasBusqueda = new(StringComparer.Ordinal)
    {
        "de", "del", "la", "las", "el", "los", "lo", "un", "una", "unos", "unas", "al", "en", "y", "o",
        "para", "con", "por", "que", "cual", "cuales", "es", "son", "me", "mi", "su", "sus",
        "precio", "precios", "valor", "cuanto", "cuantos", "cuanta", "cuantas", "sale", "salen",
        "cuesta", "cuestan", "vale", "valen", "tiene", "tienen", "tenes", "hay",
        "quiero", "necesito", "busco", "pasame"
    };

    /// <summary>
    /// Versión tolerante de lo que pidió el cliente, para reintentar la búsqueda de artículos:
    /// sin tildes ni signos, "doble A"/"triple A" → AA/AAA, sin palabras vacías ni de una letra, y
    /// en singular aproximado (la búsqueda es por substring: "pila" encuentra "PILA", "cabl" encuentra
    /// "CABLE"). Devuelve "" si no queda ningún término útil.
    /// </summary>
    internal static string NormalizarBusquedaArticulo(string? texto)
    {
        var normalizado = Regex.Replace(NormalizarTexto(texto), "[^a-z0-9]+", " ");
        normalizado = Regex.Replace(normalizado, @"\b(doble|dos)\s+a\b", "aa");
        normalizado = Regex.Replace(normalizado, @"\b(triple|tres)\s+a\b", "aaa");

        var terminos = normalizado
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !PalabrasVaciasBusqueda.Contains(t))
            .Select(Singularizar)
            .Distinct(StringComparer.Ordinal);
        return string.Join(' ', terminos);
    }

    private static string Singularizar(string termino)
    {
        // "adaptadores" → "adaptador", "pilas" → "pila", "cables" → "cabl". Sólo plurales regulares;
        // ante la duda queda más corto, que en una búsqueda por substring sigue encontrando "CABLE".
        if (termino.Length >= 5 && termino.EndsWith("es", StringComparison.Ordinal) && "rlndj".Contains(termino[^3]))
            return termino[..^2];
        if (termino.Length >= 4 && termino.EndsWith('s') && !termino.EndsWith("ss", StringComparison.Ordinal))
            return termino[..^1];
        return termino;
    }

    private static string NormalizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var normalized = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public IReadOnlyList<ConversacionAsistenteHerramientaDefinicionDto> ObtenerHerramientasDisponibles(
        ConversacionAutomatizacionesConfigDto config,
        ConversacionCuentaVinculadaDto? cuenta,
        string mensajeCliente,
        string? mensajePrevioCliente = null)
    {
        // El seguimiento ("Pila duracell AA" después de "Que precio tienen las pilas?") no trae
        // palabra clave propia: sin el mensaje previo quedaba sin herramientas y el bot derivaba. El
        // previo sólo habilita la lista; catálogo forzado/ofrecido sigue dependiendo del mensaje actual.
        if (!MensajeNecesitaHerramientas(mensajeCliente) && !MensajeNecesitaHerramientas(mensajePrevioCliente ?? string.Empty))
            return [];

        var herramientas = new List<ConversacionAsistenteHerramientaDefinicionDto>();
        const string sinParametros = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";

        // Identidad ambigua (contacto vinculado a más de un Cliente, ver ResolverCuentaVinculadaAsync):
        // ninguna tool privada de cuenta, y tampoco el fallback de precio consumidor -- ese fallback es
        // solo para leads sin ninguna cuenta vinculada, no para "no sé cuál de estas cuentas es".
        var esAmbigua = cuenta?.EsAmbigua == true;
        var esCliente = !esAmbigua && cuenta?.Tipo == CuentaComercialTipo.Cliente;
        var esProveedor = !esAmbigua && cuenta?.Tipo == CuentaComercialTipo.Proveedor;
        var puedeInformarPrecio = !esAmbigua && (esCliente || config.CatalogoMuestraPrecioConsumidor);
        var pideCatalogo = MensajePideCatalogo(mensajeCliente);

        if (config.AsistenteHerramientaPrecios && puedeInformarPrecio)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolConsultarPrecio,
                Descripcion = esCliente
                    ? "Busca el precio real de un artículo para el Cliente identificado. La cuenta ya está resuelta por el servidor; no pedir ni aceptar código de cliente."
                    : "Busca el precio real de consumidor final usando la fuente/lista real configurada en AlfaCore. No inventar precios.",
                ParametrosJsonSchema = """
                    {"type":"object","properties":{"articulo":{"type":"string","description":"Código o términos clave cortos del artículo (tipo, marca, medida), en singular. Ej.: 'pila AA', 'pila duracell AA'. No incluir palabras como precio o cuánto."}},"required":["articulo"]}
                    """
            });
        }

        var ofreceSaldo = (esCliente && config.AsistenteHerramientaSaldoCliente)
                           || (esProveedor && config.AsistenteHerramientaSaldoProveedor);
        if (ofreceSaldo)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolConsultarSaldoTotal,
                Descripcion = "Devuelve el saldo total de cuenta corriente de quien está escribiendo (la cuenta ya está identificada, no hace falta pedirla).",
                ParametrosJsonSchema = sinParametros
            });
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolConsultarSaldoDetalle,
                Descripcion = "Devuelve el detalle (lista de comprobantes) de la deuda pendiente de quien está escribiendo, como texto.",
                ParametrosJsonSchema = sinParametros
            });
        }

        if (esCliente && config.AsistenteHerramientaPedidos)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolConsultarPedidos,
                Descripcion = "Consulta el estado de Notas de Pedido (pendiente/aprobado/finalizado/anulado). Sin número trae los últimos pedidos; con número busca uno puntual.",
                ParametrosJsonSchema = """
                    {"type":"object","properties":{"numero_pedido":{"type":"string","description":"Número de comprobante puntual a buscar (opcional)"}},"required":[]}
                    """
            });
        }

        if (config.AsistenteHerramientaPortalLink && esCliente)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolGenerarLinkPortal,
                Descripcion = "Genera el link de acceso al Portal Cliente para el Cliente identificado. No usar para proveedores ni leads.",
                ParametrosJsonSchema = sinParametros
            });
        }

        if (!esCliente || pideCatalogo)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolGenerarLinkCatalogoPublico,
                Descripcion = puedeInformarPrecio
                    ? "Genera el link al catálogo público/consumidor final de la empresa actual. Puede acompañarse con precios solo si se consulta la tool de precios."
                    : "Genera el link al catálogo público de la empresa actual. No informa precios: si el comprador quiere avanzar, debe identificarse o registrarse por el flujo existente.",
                ParametrosJsonSchema = sinParametros
            });
        }

        return herramientas;
    }

    public async Task<string> EjecutarAsync(
        string nombre,
        string argumentosJson,
        ConversacionCuentaVinculadaDto? cuenta,
        CancellationToken ct = default)
    {
        if (cuenta?.EsAmbigua == true && ToolsPrivadasDeCuenta.Contains(nombre))
            return "Este contacto tiene más de una cuenta vinculada; no puedo mostrar datos privados de cuenta hasta confirmar cuál corresponde. Puedo derivarlo con un asesor para identificarlo.";

        try
        {
            return nombre switch
            {
                ToolConsultarPrecio => await EjecutarConsultarPrecioAsync(argumentosJson, cuenta, ct),
                ToolConsultarSaldoTotal => await EjecutarConsultarSaldoTotalAsync(cuenta, ct),
                ToolConsultarSaldoDetalle => await EjecutarConsultarSaldoDetalleAsync(cuenta, ct),
                ToolConsultarPedidos => await EjecutarConsultarPedidosAsync(argumentosJson, cuenta, ct),
                ToolGenerarLinkPortal => await EjecutarGenerarLinkPortalAsync(cuenta, ct),
                ToolGenerarLinkCatalogoPublico => await EjecutarGenerarLinkCatalogoPublicoAsync(cuenta, ct),
                _ => "Herramienta desconocida."
            };
        }
        catch (Exception)
        {
            // Defensa en profundidad: cualquier falla de una tool no debe tirar abajo la respuesta
            // del bot -- el asistente recibe este texto y decide cómo seguir (normalmente DERIVA).
            return "No se pudo obtener el dato en este momento (error de consulta). Avisale que un asesor lo va a confirmar.";
        }
    }

    private async Task<string> EjecutarConsultarPrecioAsync(string argumentosJson, ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        var articulo = LeerArgumentoString(argumentosJson, "articulo");
        if (string.IsNullOrWhiteSpace(articulo))
            return "No se especificó qué artículo buscar.";

        var codigoCliente = cuenta?.Tipo == CuentaComercialTipo.Cliente ? cuenta.Codigo : null;
        // Se piden MaxOpcionesPrecio + 1 para saber si hay "muchas" coincidencias sin traer todo.
        var busquedaUsada = articulo.Trim();
        var resultados = await crmCotizacionService.SearchArticulosAsync(codigoCliente, busquedaUsada, take: MaxOpcionesPrecio + 1, ct: ct);

        // La búsqueda compartida (POS/Cotizaciones) exige que CADA palabra aparezca en el artículo:
        // "pilas doble A" no encuentra "PILA DURACELL - AA" (Base4264, 2026-09-30). No se toca ese
        // motor; acá, sólo si no hubo resultados, se reintenta una vez con los términos normalizados.
        if (resultados.Count == 0)
        {
            var normalizada = NormalizarBusquedaArticulo(articulo);
            if (normalizada.Length > 0 && !string.Equals(normalizada, busquedaUsada, StringComparison.OrdinalIgnoreCase))
            {
                busquedaUsada = normalizada;
                resultados = await crmCotizacionService.SearchArticulosAsync(codigoCliente, busquedaUsada, take: MaxOpcionesPrecio + 1, ct: ct);
            }
        }

        return FormatearResultadoPrecio(articulo, busquedaUsada, resultados, esConsumidorFinal: codigoCliente is null);
    }

    internal const int MaxOpcionesPrecio = 5;

    /// <summary>
    /// Arma el resultado de consultar_precio con una instrucción explícita para el modelo según
    /// cuántas coincidencias hubo (1 → RESUELVE; 2..5 → ACLARA mostrando opciones con precio; más de
    /// 5 → ACLARA pidiendo marca/medida; 0 → ACLARA pidiendo marca/medida). La búsqueda es por
    /// substring, así que "AA" también trae "AAA": si algunas coincidencias contienen TODOS los
    /// términos como palabra completa, se usan sólo esas.
    /// </summary>
    internal static string FormatearResultadoPrecio(
        string articuloPedido,
        string busquedaUsada,
        IReadOnlyList<CrmCotizacionArticuloDto> resultados,
        bool esConsumidorFinal)
    {
        if (resultados.Count == 0)
            // "¿Cuánto sale el envío?" o "¿qué es el código X?" no son artículos: si la información del
            // negocio (texto, bloques o archivos) lo responde, se usa eso en vez de repreguntar (2026-10-06).
            return $"Sin coincidencias para \"{articuloPedido}\" entre los artículos. Si la INFORMACIÓN DEL NEGOCIO responde la consulta (por ejemplo envíos, servicios, promociones, códigos o condiciones), respondé con esa información tipo RESUELVE. Si no, respondé tipo ACLARA con UNA sola pregunta pidiendo la marca o la medida del artículo (no derives y no digas que no tenés el precio).";

        var hayMas = resultados.Count > MaxOpcionesPrecio;
        var candidatos = resultados.Take(MaxOpcionesPrecio).ToList();
        if (!hayMas)
        {
            var terminos = PalabrasCompletas(busquedaUsada);
            var exactos = candidatos.Where(a => terminos.IsSubsetOf(PalabrasCompletas($"{a.IdArticulo} {a.Codigo} {a.Descripcion}"))).ToList();
            if (exactos.Count > 0 && exactos.Count < candidatos.Count)
                candidatos = exactos;
        }

        var lineas = new StringBuilder();
        foreach (var art in candidatos)
        {
            var codigo = string.IsNullOrWhiteSpace(art.Codigo) ? art.IdArticulo : art.Codigo;
            // Consumidor final/lead (sin cuenta Cliente): si la base no tiene bien configurado el precio
            // de consumidor final, el resolver general puede devolver 0 -- no rediseñamos ese motor (lo
            // usan POS/Cotizaciones/Crm), pero acá no debe salir como si fuera un precio real.
            if (esConsumidorFinal && art.PrecioUnitarioConIva <= 0)
            {
                lineas.AppendLine($"- {art.Descripcion} (código {codigo}): precio no disponible, hay que consultarlo.");
                continue;
            }

            lineas.AppendLine(
                $"- {art.Descripcion} (código {codigo}): $ {art.PrecioUnitarioConIva.ToString("N2", CultureInfo.GetCultureInfo("es-AR"))} (IVA incluido)");
        }

        var detalle = lineas.ToString().Trim();
        if (hayMas)
            return $"Más de {MaxOpcionesPrecio} coincidencias para \"{articuloPedido}\" (se muestran {MaxOpcionesPrecio}). Respondé tipo ACLARA sin listar todos los precios: preguntale qué marca o medida busca, mencionando como ejemplo algunas de estas opciones.\n{detalle}";
        if (candidatos.Count == 1)
            return $"1 coincidencia. Respondé tipo RESUELVE informando este precio:\n{detalle}";
        return $"{candidatos.Count} coincidencias. Si el cliente no especificó cuál de estas quiere, respondé tipo ACLARA mostrando estas opciones con su precio y preguntando cuál necesita (una sola pregunta):\n{detalle}";
    }

    private static HashSet<string> PalabrasCompletas(string? texto)
        => Regex.Split(NormalizarTexto(texto), "[^a-z0-9]+")
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private async Task<string> EjecutarConsultarSaldoTotalAsync(ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        if (cuenta is null)
            return "No se pudo identificar la cuenta para consultar el saldo.";

        if (cuenta.Tipo == CuentaComercialTipo.Cliente)
        {
            var resumen = await portalClienteService.GetResumenCuentaCorrienteAsync(cuenta.Codigo, ct);
            return FormatearResumen(resumen.SaldoTotal, resumen.Vencido, resumen.AVencer, resumen.CantidadPendientes);
        }

        var resumenProv = await proveedorSaldoService.GetResumenSaldoAsync(cuenta.Codigo, ct);
        return FormatearResumen(resumenProv.SaldoTotal, resumenProv.Vencido, resumenProv.AVencer, resumenProv.CantidadPendientes);
    }

    private static string FormatearResumen(decimal total, decimal vencido, decimal aVencer, int cantidad)
    {
        var cultura = CultureInfo.GetCultureInfo("es-AR");
        if (cantidad == 0)
            return "No tiene saldo pendiente registrado.";
        return $"Saldo total: $ {total.ToString("N2", cultura)} " +
               $"(vencido: $ {vencido.ToString("N2", cultura)}, a vencer: $ {aVencer.ToString("N2", cultura)}) " +
               $"en {cantidad} comprobante(s) pendiente(s).";
    }

    private async Task<string> EjecutarConsultarSaldoDetalleAsync(ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        if (cuenta is null)
            return "No se pudo identificar la cuenta para consultar el detalle.";

        var cultura = CultureInfo.GetCultureInfo("es-AR");
        var sb = new StringBuilder();

        if (cuenta.Tipo == CuentaComercialTipo.Cliente)
        {
            var detalle = await portalClienteService.GetCuentaCorrienteAsync(
                new PortalClienteCuentaCorrienteFiltroDto { CodigoCliente = cuenta.Codigo }, ct);
            if (detalle.Pendientes.Count == 0)
                return "No tiene comprobantes pendientes de pago.";

            foreach (var p in detalle.Pendientes.Take(10))
            {
                var vencido = p.EstaVencido ? " VENCIDO" : "";
                sb.AppendLine($"- {p.Tc} {p.Numero}-{p.Letra} del {p.Fecha:dd/MM/yyyy}, vence {p.Vencimiento:dd/MM/yyyy}: $ {p.Saldo.ToString("N2", cultura)}{vencido}");
            }
            return sb.ToString().Trim();
        }

        var pendientesProv = await proveedorSaldoService.GetComprobantesPendientesAsync(cuenta.Codigo, ct);
        if (pendientesProv.Count == 0)
            return "No hay comprobantes pendientes de pago a ese proveedor.";

        foreach (var p in pendientesProv.Take(10))
        {
            var vencido = p.EstaVencido ? " VENCIDO" : "";
            sb.AppendLine($"- {p.Tc} {p.Numero}-{p.Letra} del {p.Fecha:dd/MM/yyyy}, vence {p.Vencimiento:dd/MM/yyyy}: $ {p.Saldo.ToString("N2", cultura)}{vencido}");
        }
        return sb.ToString().Trim();
    }

    private async Task<string> EjecutarConsultarPedidosAsync(string argumentosJson, ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        if (cuenta is null || cuenta.Tipo != CuentaComercialTipo.Cliente)
            return "No se pudo identificar al cliente para consultar sus pedidos.";

        var numeroPedido = LeerArgumentoString(argumentosJson, "numero_pedido");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var filas = await cn.QueryAsync<PedidoRow>(new CommandDefinition(
            """
            SELECT TOP (5)
                ISNULL(LTRIM(RTRIM(IDCOMPROBANTE)), '') AS IdComprobanteTexto,
                FECHA AS Fecha,
                ISNULL(CONVERT(decimal(15,2), IMPORTE), 0) AS Total,
                CAST(ISNULL(ANULADA, 0) AS bit) AS Anulada,
                CAST(ISNULL(APROBADO, 0) AS bit) AS Aprobado,
                CAST(ISNULL(FINALIZADA, 0) AS bit) AS Finalizada
            FROM dbo.V_MV_Cpte
            WHERE UPPER(LTRIM(RTRIM(TC))) = 'NP'
              AND UPPER(LTRIM(RTRIM(CUENTA))) = UPPER(LTRIM(RTRIM(@CodigoCliente)))
              AND (@NumeroPedido IS NULL OR UPPER(LTRIM(RTRIM(IDCOMPROBANTE))) = UPPER(LTRIM(RTRIM(@NumeroPedido))))
            ORDER BY FECHA DESC;
            """,
            new { CodigoCliente = cuenta.Codigo, NumeroPedido = string.IsNullOrWhiteSpace(numeroPedido) ? null : numeroPedido },
            cancellationToken: ct));

        var lista = filas.ToList();
        if (lista.Count == 0)
        {
            // Nunca se distingue "no existe" de "es de otro cliente" -- mismo criterio que el resto
            // de las consultas del Portal Cliente.
            return string.IsNullOrWhiteSpace(numeroPedido)
                ? "No tiene pedidos registrados."
                : "No encontré ningún pedido con ese número asociado a su cuenta.";
        }

        var cultura = CultureInfo.GetCultureInfo("es-AR");
        var sb = new StringBuilder();
        foreach (var p in lista)
        {
            var estado = p.Anulada ? "Anulado" : p.Finalizada ? "Finalizado" : p.Aprobado ? "Aprobado (en preparación)" : "Pendiente";
            sb.AppendLine($"- Pedido {p.IdComprobanteTexto} del {p.Fecha:dd/MM/yyyy}: {estado} — $ {p.Total.ToString("N2", cultura)}");
        }
        return sb.ToString().Trim();
    }

    private async Task<string> EjecutarGenerarLinkPortalAsync(ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        if (cuenta is null)
            return "No se pudo identificar la cuenta para generar el link.";

        if (cuenta.Tipo == CuentaComercialTipo.Proveedor)
            return "El portal de autogestión para proveedores todavía no está disponible; ofrecele que un asesor lo contacta con la información que necesite.";

        var whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(ct);
        var baseUrl = (whatsAppConfig.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "El portal está disponible pero todavía no se configuró la URL pública del sistema; avisale que un asesor le manda el link.";

        return $"{baseUrl}/portal-cliente";
    }

    private async Task<string> EjecutarGenerarLinkCatalogoPublicoAsync(ConversacionCuentaVinculadaDto? cuenta, CancellationToken ct)
    {
        // El catálogo público no expone datos privados de cuenta: puede compartirse con Cliente,
        // Lead, Proveedor o identidad ambigua. La empresa/base se resuelve server-side.
        var contexto = await ResolverContextoCatalogoPublicoAsync(ct);
        if (contexto is null)
            return "El catálogo público está disponible, pero no se pudo resolver la empresa/base actual para generar el link. Avisale que un asesor lo comparte.";

        var (idWeb, idBase) = contexto.Value;
        var whatsAppConfig = await conversacionesConfigService.GetWhatsAppConfigAsync(ct);
        var baseUrl = (whatsAppConfig.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "El catálogo público está disponible, pero todavía no se configuró la URL pública del sistema; avisale que un asesor le manda el link.";

        var catalogo = await catalogosService.GetCatalogoPublicoAsync(0, expectedBaseId: idBase, ct);
        if (catalogo is null)
            return "No hay un catálogo público predeterminado disponible para compartir en este momento.";

        var link = await publicLinkService.TryGetExistingAsync(idWeb, idBase, PublicLinkTipos.Catalogo, catalogo.IdInsert, ct)
            ?? await publicLinkService.GetOrCreateAsync(idWeb, idBase, PublicLinkTipos.Catalogo, catalogo.IdInsert, catalogo.Nombre, ct);

        var url = $"{baseUrl}/{Uri.EscapeDataString(idWeb)}/catalogo/{link.RouteSegment}";

        // Cliente identificado (nunca ambiguo) → link PERSONAL: el mismo catálogo + una credencial
        // firmada server-side con su identidad, para que vea los precios sin volver a loguearse.
        // Lead/Proveedor/ambigua → link público (sin precios si la base no los muestra a leads).
        // La cuenta viene resuelta por el servidor, nunca de los argumentos del modelo.
        if (cuenta is { EsAmbigua: false, Tipo: CuentaComercialTipo.Cliente } && !string.IsNullOrWhiteSpace(cuenta.Codigo))
        {
            var credencial = await catalogoClienteLinkService.CrearCredencialAsync(idWeb, idBase, link.IdReferencia, cuenta.Codigo, ct);
            if (!string.IsNullOrWhiteSpace(credencial))
                url += $"?{CatalogoClienteLinkService.QueryParameter}={Uri.EscapeDataString(credencial)}";
        }

        return url;
    }

    private async Task<(string IdWeb, int IdBase)?> ResolverContextoCatalogoPublicoAsync(CancellationToken ct)
    {
        var idBase = sessionService.GetActiveSession()?.BaseId ?? 0;
        if (idBase <= 0)
            return null;

        var baseCentral = await centralBasesService.GetByIdAsync(idBase, ct);
        if (baseCentral is null || baseCentral.IdBase != idBase || string.IsNullOrWhiteSpace(baseCentral.IdCliente))
            return null;

        var clienteCentral = await centralClientesService.GetByIdClienteAsync(baseCentral.IdCliente, ct);
        var idWeb = clienteCentral?.IdWeb?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(idWeb))
            return null;

        return (idWeb, idBase);
    }

    private static string? LeerArgumentoString(string argumentosJson, string nombre)
    {
        if (string.IsNullOrWhiteSpace(argumentosJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(argumentosJson);
            return doc.RootElement.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class PedidoRow
    {
        public string IdComprobanteTexto { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public bool Anulada { get; set; }
        public bool Aprobado { get; set; }
        public bool Finalizada { get; set; }
    }
}

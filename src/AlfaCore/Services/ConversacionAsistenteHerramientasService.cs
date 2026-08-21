using System.Globalization;
using System.Text;
using System.Text.Json;
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
    IPortalClienteService portalClienteService,
    IProveedorSaldoService proveedorSaldoService,
    IConversacionesConfigService conversacionesConfigService) : IConversacionAsistenteHerramientasService
{
    private const string ToolConsultarPrecio = "consultar_precio";
    private const string ToolConsultarSaldoTotal = "consultar_saldo_total";
    private const string ToolConsultarSaldoDetalle = "consultar_saldo_detalle";
    private const string ToolConsultarPedidos = "consultar_pedidos";
    private const string ToolGenerarLinkPortal = "generar_link_portal";

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
        "portal", "autogestión", "autogestion", "acceso online", "ver mi cuenta", "cuenta online"
    ];

    private static bool MensajeNecesitaHerramientas(string mensajeCliente)
    {
        var texto = (mensajeCliente ?? string.Empty).ToLowerInvariant();
        return texto.Length > 0 && PalabrasClaveHerramientas.Any(texto.Contains);
    }

    public IReadOnlyList<ConversacionAsistenteHerramientaDefinicionDto> ObtenerHerramientasDisponibles(
        ConversacionAutomatizacionesConfigDto config,
        ConversacionCuentaVinculadaDto? cuenta,
        string mensajeCliente)
    {
        if (!MensajeNecesitaHerramientas(mensajeCliente))
            return [];

        var herramientas = new List<ConversacionAsistenteHerramientaDefinicionDto>();
        const string sinParametros = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";

        if (config.AsistenteHerramientaPrecios)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolConsultarPrecio,
                Descripcion = "Busca el precio real de un artículo por nombre o código. Usar cuando preguntan cuánto sale un producto.",
                ParametrosJsonSchema = """
                    {"type":"object","properties":{"articulo":{"type":"string","description":"Nombre o código del artículo a buscar"}},"required":["articulo"]}
                    """
            });
        }

        var esCliente = cuenta?.Tipo == CuentaComercialTipo.Cliente;
        var esProveedor = cuenta?.Tipo == CuentaComercialTipo.Proveedor;

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

        if (config.AsistenteHerramientaPortalLink && cuenta is not null)
        {
            herramientas.Add(new ConversacionAsistenteHerramientaDefinicionDto
            {
                Nombre = ToolGenerarLinkPortal,
                Descripcion = "Genera el link de acceso al portal de autogestión (cuenta corriente, pedidos) para que ingrese con su usuario y clave.",
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
        try
        {
            return nombre switch
            {
                ToolConsultarPrecio => await EjecutarConsultarPrecioAsync(argumentosJson, cuenta, ct),
                ToolConsultarSaldoTotal => await EjecutarConsultarSaldoTotalAsync(cuenta, ct),
                ToolConsultarSaldoDetalle => await EjecutarConsultarSaldoDetalleAsync(cuenta, ct),
                ToolConsultarPedidos => await EjecutarConsultarPedidosAsync(argumentosJson, cuenta, ct),
                ToolGenerarLinkPortal => await EjecutarGenerarLinkPortalAsync(cuenta, ct),
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
        var resultados = await crmCotizacionService.SearchArticulosAsync(codigoCliente, articulo, take: 5, ct: ct);
        if (resultados.Count == 0)
            return $"No se encontró ningún artículo que coincida con \"{articulo}\".";

        var sb = new StringBuilder();
        foreach (var art in resultados)
        {
            sb.AppendLine(
                $"- {art.Descripcion} (código {art.Codigo}): $ {art.PrecioUnitarioConIva.ToString("N2", CultureInfo.GetCultureInfo("es-AR"))} (IVA incluido)");
        }
        return sb.ToString().Trim();
    }

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

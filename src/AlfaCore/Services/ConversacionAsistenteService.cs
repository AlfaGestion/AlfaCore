using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class ConversacionAsistenteService(IHttpClientFactory httpClientFactory, IAppEventService appEvents, IIaUsoRecorder? iaUso = null) : IConversacionAsistenteService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    // 3: buscar, probar otro nombre común si no aparece (lapicera/birome) y responder (2026-10-29).
    private const int MaxRondasHerramientas = 3;

    public async Task<ConversacionAsistenteRespuesta?> ResponderAsync(
        string comportamiento,
        string informacion,
        string politica,
        string mensajeCliente,
        IReadOnlyList<ConversacionMensajeDto> historial,
        bool fueraDeHorario = false,
        bool esUrgente = false,
        string? conocimientoBase = null,
        string? sugerenciaKnowledge = null,
        string? contextoCliente = null,
        IReadOnlyList<ConversacionAsistenteHerramientaDefinicionDto>? herramientas = null,
        Func<string, string, CancellationToken, Task<string>>? ejecutarHerramientaAsync = null,
        Func<string, CancellationToken, Task>? traceDiagAsync = null,
        bool precioRequiereIdentificacion = false,
        string? linkCatalogoParaLead = null,
        IReadOnlyList<string>? imagenesDataUri = null,
        string? criterioTicket = null,
        bool casoYaDerivado = false,
        string? temaPendiente = null,
        bool ultimoFueAclaracion = false,
        string? modelo = null,
        CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(mensajeCliente))
            return null;

        // El de la configuración del asistente de la base (CONV_ASISTENTE_MODELO); si no, el del servidor.
        var model = string.IsNullOrWhiteSpace(modelo) ? Environment.GetEnvironmentVariable("OPENAI_MODEL") : modelo.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var haySaldoEntreHerramientas = herramientas?.Any(h => h.Nombre.StartsWith("consultar_saldo", StringComparison.Ordinal)) ?? false;
        var hayCatalogoEntreHerramientas = herramientas?.Any(h => string.Equals(h.Nombre, "generar_link_catalogo_publico", StringComparison.Ordinal)) ?? false;
        var hayPrecioEntreHerramientas = herramientas?.Any(h => string.Equals(h.Nombre, "consultar_precio", StringComparison.Ordinal)) ?? false;
        var hayPortalEntreHerramientas = herramientas?.Any(h => string.Equals(h.Nombre, "generar_link_portal", StringComparison.Ordinal)) ?? false;
        var hayPrepararPedido = herramientas?.Any(h => string.Equals(h.Nombre, ConversacionAsistenteHerramientasService.ToolPrepararPedido, StringComparison.Ordinal)) ?? false;
        var hayRegistrarCliente = herramientas?.Any(h => string.Equals(h.Nombre, ConversacionAsistenteHerramientasService.ToolRegistrarCliente, StringComparison.Ordinal)) ?? false;
        var hayGuardarContactoWeb = herramientas?.Any(h => string.Equals(h.Nombre, ConversacionAsistenteHerramientasService.ToolGuardarContactoWeb, StringComparison.Ordinal)) ?? false;
        // La IA decide qué herramienta usar (2026-10-28). La única que se fuerza es consultar_precio,
        // y sólo como red de seguridad: si la respuesta trae un monto que no salió de ninguna herramienta,
        // ni de la información del negocio, ni de la charla.
        var textoConRespaldo = new StringBuilder();
        var reintentoPorPrecioSinRespaldo = false;
        var reintentoPorDerivarSinBuscar = false;
        var seUsoAlgunaHerramienta = false;
        string? herramientaForzadaSiguienteRonda = null;
        var systemPrompt = BuildSystemPrompt(comportamiento, informacion, politica, fueraDeHorario, esUrgente, conocimientoBase, sugerenciaKnowledge, contextoCliente, haySaldoEntreHerramientas, hayCatalogoEntreHerramientas, precioRequiereIdentificacion, hayPrecioEntreHerramientas, linkCatalogoParaLead, hayPortalEntreHerramientas, hayPrepararPedido, hayRegistrarCliente, hayGuardarContactoWeb, criterioTicket, casoYaDerivado, temaPendiente, ultimoFueAclaracion);

        textoConRespaldo.AppendLine(systemPrompt);
        foreach (var m in historial)
            textoConRespaldo.AppendLine(m.Texto);
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        string? ultimaRespuestaAutomatica = null;
        foreach (var m in historial
                     .Where(x => (x.Direction == "ENTRANTE" || x.Direction == "SALIENTE") && !string.IsNullOrWhiteSpace(x.Texto))
                     .OrderBy(x => x.FechaHora)
                     .TakeLast(30))
        {
            var texto = m.Texto.Trim();
            if (m.Direction == "SALIENTE")
            {
                // Colapsa una misma respuesta automática repetida (ej. varias contenciones DERIVA
                // seguidas) a una sola aparición en el contexto: si el modelo ve su propia respuesta
                // repetida varias veces, tiende a "seguir el patrón" en vez de razonar de nuevo cada
                // mensaje nuevo del cliente. Se compara contra la última SALIENTE distinta agregada,
                // no contra el mensaje anterior a secas (puede haber ENTRANTE del cliente en el medio).
                if (string.Equals(texto, ultimaRespuestaAutomatica, StringComparison.Ordinal))
                    continue;
                ultimaRespuestaAutomatica = texto;
            }

            messages.Add(new
            {
                role = m.Direction == "ENTRANTE" ? "user" : "assistant",
                content = texto
            });
        }
        // Imágenes del cliente (F07): van en el mismo mensaje, como partes image_url (gpt-4o-mini ve imágenes).
        var conImagenes = imagenesDataUri is { Count: > 0 };
        if (conImagenes)
        {
            var partes = new List<object> { new { type = "text", text = mensajeCliente.Trim() } };
            partes.AddRange(imagenesDataUri!.Select(uri => (object)new { type = "image_url", image_url = new { url = uri, detail = "high" } }));
            messages.Add(new { role = "user", content = partes });
            messages[0] = new { role = "system", content = systemPrompt + "\n\n" + InstruccionesImagen };
        }
        else
        {
            messages.Add(new { role = "user", content = mensajeCliente.Trim() });
        }

        object? tools = herramientas is { Count: > 0 }
            ? herramientas.Select(h => new
            {
                type = "function",
                function = new
                {
                    name = h.Nombre,
                    description = h.Descripcion,
                    parameters = JsonSerializer.Deserialize<JsonElement>(h.ParametrosJsonSchema)
                }
            }).ToArray()
            : null;

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(40);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            for (var ronda = 0; ronda <= MaxRondasHerramientas; ronda++)
            {
                // En la última ronda tiene que responder: si pedía otra herramienta quedaba sin texto y salía
                // la contención genérica ("quiero 10 pilas AA, ¿cuánto me sale?", Base4264 2026-10-28).
                object? toolChoice = tools is null ? null
                    : ronda == MaxRondasHerramientas ? "none"
                    : herramientaForzadaSiguienteRonda is null ? null
                    : new { type = "function", function = new { name = herramientaForzadaSiguienteRonda } };
                herramientaForzadaSiguienteRonda = null;
                var payload = BuildChatPayload(model, messages, 0.3, responseFormat: new { type = "json_object" }, tools, toolChoice);

                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    // Diagnóstico temporal (ver conversaciones_agente_ia_plan.md): el asistente venía
                    // derivando siempre, sin poder confirmar si el modelo respondía mal o si la llamada
                    // a OpenAI directamente fallaba (ej. nombre de modelo inválido). Antes esto se
                    // perdía en silencio (return null). Se loguea acá, no se elimina hasta confirmar
                    // la causa real con datos.
                    await appEvents.LogErrorAsync(
                        "Conversaciones", "AsistenteOpenAiError",
                        new InvalidOperationException($"OpenAI respondió {(int)response.StatusCode}"),
                        "El asistente de Conversaciones no pudo obtener respuesta de OpenAI.",
                        new { model, ronda, herramientas = tools is null ? 0 : herramientas!.Count, statusCode = (int)response.StatusCode, body = Truncar(body, 1500) },
                        AppEventSeverity.Warning, ct);
                    return null;
                }

                using var document = JsonDocument.Parse(body);
                iaUso?.Registrar(IaUsoFunciones.Bot, document.RootElement, model);
                var choice = document.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

                var toolCalls = message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array
                    ? tc
                    : (JsonElement?)null;
                var tieneToolCalls = toolCalls is not null && toolCalls.Value.GetArrayLength() > 0;

                if (!tieneToolCalls || ejecutarHerramientaAsync is null || ronda == MaxRondasHerramientas)
                {
                    var texto = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    var respuestaParseada = ParseRespuesta(texto);

                    // Sólo si no buscó nada: con herramientas usadas, un total calculado (10 x $ 2.500) no es inventado.
                    if (hayPrecioEntreHerramientas && !reintentoPorPrecioSinRespaldo && !seUsoAlgunaHerramienta && ejecutarHerramientaAsync is not null
                        && ronda + 2 <= MaxRondasHerramientas
                        && MontosSinRespaldo(respuestaParseada?.Respuesta, textoConRespaldo.ToString()).Count > 0)
                    {
                        reintentoPorPrecioSinRespaldo = true;
                        herramientaForzadaSiguienteRonda = ConversacionAsistenteHerramientasService.ToolConsultarPrecio;
                        messages.Add(new { role = "system", content = "Tu respuesta tenía un precio que no salió de ninguna herramienta. Buscalo con consultar_precio antes de responder; nunca escribas un precio de memoria." });
                        await TraceDiagAsync(traceDiagAsync, "BotPrecioSinRespaldo|reintento=consultar_precio", ct);
                        continue;
                    }

                    // "Tengo una papelera... necesitaría lapiceras, ¿ustedes venden?" (audio, Base4271 2026-10-29):
                    // derivó sin buscar teniendo consultar_precio. No se decide por palabras: se le pide a la IA que
                    // lo revise una vez; ella interpreta si es un artículo (y lo busca) o si derivar estaba bien.
                    if (hayPrecioEntreHerramientas && !reintentoPorDerivarSinBuscar && !seUsoAlgunaHerramienta && ejecutarHerramientaAsync is not null
                        && ronda + 2 <= MaxRondasHerramientas
                        && string.Equals(respuestaParseada?.Tipo, "DERIVA", StringComparison.OrdinalIgnoreCase))
                    {
                        reintentoPorDerivarSinBuscar = true;
                        messages.Add(new { role = "system", content = "Antes de derivar, revisá: si el cliente pregunta por algún artículo o producto (aunque lo diga con otras palabras), buscalo primero con consultar_precio. Si no es sobre artículos, devolvé la misma respuesta." });
                        await TraceDiagAsync(traceDiagAsync, "BotDerivaSinBuscar|revisa", ct);
                        continue;
                    }

                    // Mismo diagnóstico temporal: acá vemos qué modelo respondió realmente (confirma si
                    // OPENAI_MODEL se está tomando), si había herramientas ofrecidas, y el JSON crudo que
                    // mandó el modelo -- sin esto solo veíamos el resultado final ya parseado.
                    await appEvents.LogAuditAsync(
                        "Conversaciones", "AsistenteOpenAiRespuesta", "CONV_ASISTENTE", model,
                        "Diagnóstico de respuesta del asistente.",
                        new
                        {
                            model,
                            ronda,
                            herramientasOfrecidas = tools is null ? 0 : herramientas!.Count,
                            finishReason,
                            tipoParseado = respuestaParseada?.Tipo,
                            textoCrudo = Truncar(texto, 800)
                        },
                        ct);
                    await TraceDiagAsync(traceDiagAsync, $"BotOpenAiResponse|finishReason={SanitizeDiag(finishReason)}|tipo={SanitizeDiag(respuestaParseada?.Tipo)}", ct);

                    // Mail inventado ("soporte@alfagestion.com" con la plantilla sin completar, 2026-10-28): no
                    // se manda un dato de contacto que no figura en ningún lado; se pasa al equipo.
                    if (respuestaParseada is not null && MailsSinRespaldo(respuestaParseada.Respuesta, textoConRespaldo.ToString()).Count > 0)
                    {
                        await TraceDiagAsync(traceDiagAsync, "BotMailSinRespaldo|deriva", ct);
                        return new ConversacionAsistenteRespuesta { Tipo = "DERIVA", PuedeResponder = false, Respuesta = string.Empty };
                    }

                    return respuestaParseada;
                }

                // El modelo pidió usar una o más herramientas: se ejecutan en C# (nunca del lado del
                // modelo) y se le devuelve el resultado para que redacte la respuesta final.
                messages.Add(message.Clone());
                var toolCallsValue = toolCalls.GetValueOrDefault();
                foreach (var call in toolCallsValue.EnumerateArray())
                {
                    var callId = call.GetProperty("id").GetString() ?? string.Empty;
                    var function = call.GetProperty("function");
                    var nombreHerramienta = function.GetProperty("name").GetString() ?? string.Empty;
                    var argumentos = function.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? "{}" : "{}";
                    await TraceDiagAsync(traceDiagAsync, $"BotToolCall|tool={SanitizeDiag(nombreHerramienta)}{DescribirArgumentoDiag(nombreHerramienta, argumentos)}", ct);

                    string resultado;
                    try
                    {
                        resultado = await ejecutarHerramientaAsync(nombreHerramienta, argumentos, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        resultado = "No se pudo obtener el dato en este momento.";
                    }
                    await TraceDiagAsync(traceDiagAsync, $"BotToolResult|tool={SanitizeDiag(nombreHerramienta)}|status={ClasificarResultadoTool(resultado)}", ct);

                    messages.Add(new { role = "tool", tool_call_id = callId, content = resultado });
                    textoConRespaldo.AppendLine(resultado);
                    seUsoAlgunaHerramienta = true;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    // OpenAI admite hasta 25 MB; una nota de voz de WhatsApp de varios minutos pesa bastante menos.
    internal const int TamanoMaximoAudioBytes = 20 * 1024 * 1024;

    public async Task<string?> TranscribirAudioAsync(byte[] audio, string nombreArchivo, string mimeType, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || audio.Length == 0 || audio.Length > TamanoMaximoAudioBytes)
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_TRANSCRIBE_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini-transcribe";

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var form = new MultipartFormDataContent();
            var archivo = new ByteArrayContent(audio);
            archivo.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType) ? "audio/ogg" : mimeType.Split(';')[0].Trim());
            form.Add(archivo, "file", string.IsNullOrWhiteSpace(nombreArchivo) ? "audio.ogg" : nombreArchivo);
            form.Add(new StringContent(model), "model");
            form.Add(new StringContent("es"), "language");
            form.Add(new StringContent("json"), "response_format");

            using var response = await client.PostAsync("https://api.openai.com/v1/audio/transcriptions", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                await appEvents.LogErrorAsync(
                    "Conversaciones", "AsistenteTranscripcionError",
                    new InvalidOperationException($"OpenAI respondió {(int)response.StatusCode}"),
                    "No se pudo transcribir un audio entrante.",
                    new { model, bytes = audio.Length, statusCode = (int)response.StatusCode, body = Truncar(body, 800) },
                    AppEventSeverity.Warning, ct);
                return null;
            }

            using var document = JsonDocument.Parse(body);
            iaUso?.Registrar(IaUsoFunciones.Transcripcion, document.RootElement, model);
            return document.RootElement.TryGetProperty("text", out var texto) ? texto.GetString()?.Trim() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> ResumirAsync(string instrucciones, string contenido, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(contenido))
            return null;

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var messages = new object[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(instrucciones) ? "Resumí el siguiente contenido de forma clara y breve." : instrucciones },
            new { role = "user", content = contenido }
        };
        var payload = BuildChatPayload(model, messages, 0.4);

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            iaUso?.Registrar(IaUsoFunciones.Resumen, document.RootElement, model);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> BuildChatPayload(
        string model,
        IReadOnlyList<object> messages,
        double? temperature = null,
        object? responseFormat = null,
        object? tools = null,
        object? toolChoice = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (responseFormat is not null)
            payload["response_format"] = responseFormat;

        if (tools is not null)
            payload["tools"] = tools;
        if (toolChoice is not null)
            payload["tool_choice"] = toolChoice;

        if (temperature.HasValue && SupportsCustomTemperature(model))
            payload["temperature"] = temperature.Value;

        if (EsfuerzoRazonamiento(model) is { } esfuerzo)
            payload["reasoning_effort"] = esfuerzo;

        return payload;
    }

    // gpt-5, gpt-5-mini y gpt-5-nano (con o sin fecha): sólo aceptan la temperatura por defecto.
    private static readonly Regex ModeloGpt5Original = new(@"^gpt-5(-mini|-nano)?(-\d{4}-\d{2}-\d{2})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool SupportsCustomTemperature(string? model)
        => string.IsNullOrWhiteSpace(model)
           || !(model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase) || ModeloGpt5Original.IsMatch(model.Trim()));

    /// <summary>
    /// Modelos que "piensan" antes de contestar (2026-11-26, prueba directa contra OpenAI): sin bajarles el
    /// razonamiento tardaban 10-13 s y gastaban hasta 1.800 tokens de más por respuesta, y gpt-5.6 / gpt-6
    /// rechazan las herramientas en chat/completions. Para un chat alcanza con la respuesta directa.
    /// </summary>
    internal static string? EsfuerzoRazonamiento(string? model)
    {
        var m = (model ?? string.Empty).Trim();
        if (m.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase) || m.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase))
            return "none";
        return ModeloGpt5Original.IsMatch(m) ? "minimal" : null;
    }

    internal const string DatoSinCargar = "(sin dato cargado)";

    /// <summary>
    /// La plantilla de información trae textos guía entre corchetes ("Soporte: [días y horario, ej: lunes
    /// a viernes de 9 a 18 hs]"). Si la empresa no los completó, el modelo los tomaba como dato (ALFA NET,
    /// 2026-10-28). Se reemplazan antes de mandarlos; los links Markdown [texto](url) quedan igual.
    /// </summary>
    internal static string QuitarTextosSinCompletar(string? informacion)
        => Regex.Replace(informacion ?? string.Empty, @"\[(?=[^\]\r\n]*\p{L})[^\]\r\n]{3,300}\](?!\()", DatoSinCargar);

    internal static string LineaFechaActual(DateTime ahora)
        => $"FECHA Y HORA ACTUAL: {ahora.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.GetCultureInfo("es-AR"))} h "
           // A las 17:38 gpt-4o-mini contestó "ya pasaron las 18 h" (batería 4264, 2026-10-28).
           + $"(ya pasaron las {ahora.Hour:00}:00 y todavía no son las {(ahora.Hour + 1) % 24:00}:00; para saber si el negocio está abierto, comparala con su horario). "
           + "Usala para fechas y plazos: no ofrezcas una promoción que ya venció o que todavía no empezó, y calculá horarios de corte y entregas desde ahora.";

    /// <summary>
    /// Montos ("$ 350", "$1.234,50") de la respuesta que no aparecen en ningún texto de respaldo
    /// (información del negocio, charla, resultados de herramientas). Se compara la parte entera.
    /// </summary>
    internal static IReadOnlyList<string> MailsSinRespaldo(string? respuesta, string respaldo)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return [];
        return Regex.Matches(respuesta, @"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")
            .Select(m => m.Value.TrimEnd('.'))
            .Where(m => (respaldo ?? string.Empty).IndexOf(m, StringComparison.OrdinalIgnoreCase) < 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> MontosSinRespaldo(string? respuesta, string respaldo)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return [];

        static string Entero(string monto) => new(monto.Split(',')[0].Where(char.IsDigit).ToArray());

        var conRespaldo = Regex.Matches(respaldo ?? string.Empty, @"\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:,\d{1,2})?")
            .Select(m => Entero(m.Value))
            .ToHashSet(StringComparer.Ordinal);
        return Regex.Matches(respuesta, @"\$\s*(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:,\d{1,2})?)")
            .Select(m => Entero(m.Groups[1].Value))
            .Where(m => m.Length > 0 && !conRespaldo.Contains(m))
            .Distinct()
            .ToList();
    }

    /// <summary>Cuándo el bot abre ticket si la empresa no cargó su propio criterio (CONV_BOT_TICKET_CRITERIO).</summary>
    public const string CriterioTicketPorDefecto =
        "sólo cuando hace falta un desarrollo en profundidad: programar algo nuevo o cambiar cómo funciona el sistema (una función, un informe, un formato, una adaptación a su negocio). Un problema puntual (algo que no anda, no imprime, no conecta, no deja entrar o muestra un cartel de error) lo atiende una persona del equipo por el chat o con acceso remoto, sin ticket. Tampoco llevan ticket las dudas de uso, las consultas ni los pedidos de precio, de contacto o de un trámite.";

    internal static string BuildSystemPrompt(string comportamiento, string informacion, string politica,
        bool fueraDeHorario, bool esUrgente, string? conocimientoBase, string? sugerenciaKnowledge,
        string? contextoCliente, bool haySaldoEntreHerramientas = false, bool hayCatalogoEntreHerramientas = false,
        bool precioRequiereIdentificacion = false, bool hayPrecioEntreHerramientas = false, string? linkCatalogoParaLead = null,
        bool hayPortalEntreHerramientas = false, bool hayPrepararPedido = false, bool hayRegistrarCliente = false,
        bool hayGuardarContactoWeb = false, string? criterioTicket = null, bool casoYaDerivado = false,
        string? temaPendiente = null, bool ultimoFueAclaracion = false)
    {
        var sb = new StringBuilder();
        var comp = (comportamiento ?? string.Empty).Trim();
        sb.AppendLine(comp.Length > 0
            ? comp
            : "Sos un asistente de atención al cliente. Respondés de forma cordial, breve y clara, en español rioplatense.");

        // Sin la fecha el modelo no puede saber si una promoción "hasta el domingo 2/11" sigue vigente
        // ni si un pedido llega a salir hoy (corte de las 14 h).
        sb.AppendLine();
        sb.AppendLine(LineaFechaActual(ConversacionesService.BusinessNow()));

        var cliente = (contextoCliente ?? string.Empty).Trim();
        if (cliente.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DATOS DEL CLIENTE (usalos para ajustar y razonar la respuesta según su rubro y prioridad):");
            sb.AppendLine(cliente);
        }

        var info = QuitarTextosSinCompletar(informacion).Trim();
        if (info.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("INFORMACIÓN DEL NEGOCIO (tu fuente de verdad; priorizala sobre cualquier suposición):");
            sb.AppendLine(info);
            if (info.Contains(DatoSinCargar, StringComparison.Ordinal))
                sb.AppendLine($"Donde dice {DatoSinCargar} la empresa todavía no cargó ese dato: no lo sabés. No lo inventes ni des un ejemplo; si la consulta depende de eso, tipo DERIVA.");
        }

        var conocimiento = (conocimientoBase ?? string.Empty).Trim();
        if (conocimiento.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BASE DE CONOCIMIENTO (fragmentos de documentos/instructivos recuperados para esta consulta; usalos como fuente de verdad y no los contradigas):");
            sb.AppendLine(conocimiento);
        }

        var sugerencia = (sugerenciaKnowledge ?? string.Empty).Trim();
        if (sugerencia.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SUGERENCIA PRELIMINAR DE ALFAKNOWLEDGE (si es consistente con la información y las herramientas, tomala como base principal para responder al cliente):");
            sb.AppendLine(sugerencia);
        }

        sb.AppendLine();
        sb.AppendLine("REGLAS:");
        // Lo que antes decidían listas de palabras (saludos, "sí" sueltos, mensajes de prueba o sin contenido)
        // lo decide la IA leyendo la conversación, con criterio y sin frases de ejemplo (2026-11-25).
        sb.AppendLine("- Leé el último mensaje junto con toda la conversación y respondé a lo que la persona quiere decir, no a palabras sueltas. Si escribió varios mensajes seguidos, te llegan uno por renglón: es una sola intervención y la contestás en una sola respuesta. Un dato suelto (números, un nombre, un sí o un no) contesta lo que se venía hablando.");
        sb.AppendLine("- Un saludo, un agradecimiento, una despedida o un mensaje de prueba lo contestás vos, breve y natural (tipo RESUELVE): no se deriva ni abre ticket. Si ya saludaste en esta charla, no vuelvas a saludar.");
        sb.AppendLine("- Si no se entiende qué quiere, preguntale qué necesita (tipo ACLARA) en vez de suponer o derivar.");
        // Prueba con IA real (4264, 2026-11-25): derivaba "no me actualizan los precios en las cajas" sin preguntar
        // cuáles, y después no relacionaba "2,3y4" con las cajas ni contestaba un dato agregado al caso.
        sb.AppendLine("- Antes de pasar un problema al equipo, si falta un dato que seguro van a necesitar para empezar a verlo (dónde pasa, en qué equipo o puesto, desde cuándo), preguntalo primero (tipo ACLARA), de a una cosa y sin repreguntar lo que ya dijo.");
        sb.AppendLine("- Si el tema ya se le pasó al equipo y el cliente agrega un dato o un detalle, es parte de ese mismo caso: confirmale breve que se lo sumás al equipo (tipo RESUELVE, abrir_ticket false), sin pedirle que lo explique de nuevo ni abrir otro caso.");
        sb.AppendLine("- Respondé en español rioplatense, breve y directo, sin inventar datos del negocio (precios, stock, plazos) que no estén en la información.");
        sb.AppendLine("- No prometas nada que no puedas sostener con la información dada.");
        sb.AppendLine("- Si tenés herramientas disponibles para consultar datos reales (precio, saldo, pedidos), usalas en vez de inventar o suponer un valor. Nunca redactes un monto, precio o estado sin haber llamado a la herramienta correspondiente primero.");
        sb.AppendLine("- Las herramientas son opcionales y puntuales: úsalas SOLO cuando la consulta pida específicamente ese dato (precio de un artículo, saldo, estado de un pedido). Para un saludo, charla general o cualquier consulta que no pida ese dato puntual, respondé directo sin llamar a ninguna herramienta -- eso no te impide resolver (tipo RESUELVE).");
        sb.AppendLine("- Nunca ofrezcas ni propongas conectarte por AnyDesk (ni ningún otro acceso remoto): eso solo lo puede hacer un humano. Si la situación lo amerita, derivá a un operador en vez de ofrecerlo vos.");
        // Prueba con gpt-4.1 (2026-11-26): "cuando tengas los datos lo vemos juntos" con una prueba que sólo hace una persona.
        sb.AppendLine("- Vos sólo podés escribir por este chat con lo que tenés en la información y en las herramientas. No te ofrezcas a revisar, probar, verificar, configurar o ver algo junto con el cliente, ni a volver a escribirle más tarde: eso lo hace una persona del equipo, y si hace falta, derivá.");
        sb.AppendLine("- Nunca digas que la empresa no vende, no tiene o no trabaja un producto, ni ofrezcas otra marca en su lugar: eso solo se sabe consultando los artículos con la herramienta de precios. Si no podés consultarlo, preguntá qué necesita o derivá.");
        sb.AppendLine("- Lo mismo con los servicios (reparaciones, instalaciones, etc.): si no figuran en la información, no digas que no se ofrecen. Decile que lo consultás con el equipo (tipo DERIVA) sin afirmar nada.");
        // Batería "empresa de servicios" (ALFA NET, 2026-10-28): inventó un mail de soporte, que el sistema
        // "funciona en Mac" y pasos de menú ("buscá 'Actualizaciones'") que no estaban en ningún lado.
        sb.AppendLine("- Nunca inventes datos de contacto (mails, teléfonos, direcciones, CBU, horarios), pasos o menús de un sistema, compatibilidades ni funciones: sólo los que figuran tal cual en la información o en los resultados de las herramientas. Si te piden algo de eso y no está, decile que se lo pasás a alguien del equipo (tipo DERIVA).");
        sb.AppendLine("- Escribí texto plano, sin ningún formato Markdown: nada de **negritas**, # títulos ni [texto](link). Los links van como la URL completa: en WhatsApp ese formato no se ve.");
        if (hayPrepararPedido)
            sb.AppendLine("- PEDIDOS POR CHAT: si el cliente quiere comprar artículos, armá el pedido con preparar_pedido (artículos y cantidades) y mostrale el resumen que devuelve. Si ya dijo qué artículo y cuántos (\"quiero 10 pilas AA\", \"mandame 5 AAA y 3 AA\"), llamá preparar_pedido directamente: no le preguntes antes \"¿querés que arme el pedido?\", el resumen ya le pide la confirmación. El pedido se registra SOLO cuando el cliente responde que confirma: nunca digas que está registrado, ni lo des por hecho. Si quiere hacer un pedido sin decir qué, preguntale qué artículos y cuántos. Si pide cambiar algo, volvé a llamar preparar_pedido con el pedido completo. Un pedido ya registrado (confirmado) no se modifica por chat: si después quiere agregar o sumar artículos, armale un pedido NUEVO con preparar_pedido sólo con lo que agrega y aclaráselo; no lo derives por esto.");
        else if (hayRegistrarCliente)
            sb.AppendLine("- CLIENTE NUEVO O NO IDENTIFICADO: quien escribe todavía no está identificado como cliente. Si quiere hacer un pedido o que lo registren, pedile en un solo mensaje nombre y apellido, DNI, calle y número, localidad y alguna referencia para encontrar la casa. Apenas te dé algún dato, llamá registrar_cliente con lo que tengas (sin inventar nada): la herramienta busca si ya es cliente y te dice qué falta o qué responder. Si ya dijo qué quiere pedir, anotalo y retomalo cuando quede registrado. Nunca digas que quedó registrado: se registra recién cuando responde que los datos están bien.");
        else
            sb.AppendLine("- No ofrezcas armar ni tomar el pedido por este chat: si quiere comprar, indicale cómo hacerlo según la información del negocio (local, catálogo o portal).");
        if (hayGuardarContactoWeb)
            sb.AppendLine("- VISITANTE DEL CHAT WEB: quien escribe llega desde la página web y todavía no sabemos cómo contactarlo. Además de ayudarlo con lo que necesita, en un momento natural de la charla (sin frenar lo que está pidiendo) pedile un número de WhatsApp para contactarlo y cómo se llama (o el nombre de su empresa, si escribe por una). Cuando te los dé, guardalos con guardar_contacto_web. Si ya se los pediste y no los dio, no insistas.");
        if (hayPortalEntreHerramientas)
            sb.AppendLine(hayPrepararPedido
                ? "- PORTAL CLIENTE: si el cliente pide su lista de precios, usá generar_link_portal y pasale el link (ahí ve los precios de su cuenta). También podés ofrecerlo como alternativa para cargar pedidos. No lo derives a un asesor por esto."
                : "- PORTAL CLIENTE: si el cliente pide su lista de precios o quiere hacer un pedido, usá generar_link_portal y pasale el link: ahí ve los precios de su cuenta y carga el pedido. Si quiere comprar artículos puntuales, informale el precio y ofrecele ese link para cargar el pedido (por chat todavía no se toman pedidos). No lo derives a un asesor por esto.");
        if (hayCatalogoEntreHerramientas)
            sb.AppendLine("- Si el cliente pide explícitamente catálogo o ver productos y está disponible la herramienta generar_link_catalogo_publico, tenés que usar esa herramienta antes de derivar. No reemplaces catálogo por Portal Cliente: son funciones distintas.");
        if (hayPrecioEntreHerramientas)
        {
            sb.AppendLine("- PRODUCTOS Y PRECIOS: ante cualquier consulta por un producto (si lo tienen, qué opciones hay o cuánto sale) buscalo primero con consultar_precio, aunque la consulta sea general (\"¿tenés biromes?\" → \"birome\"). Si encuentra varias opciones, mostrale algunas con su precio y preguntale cuál quiere; no le preguntes \"¿qué tipo buscás?\" sin haber buscado. Usá términos cortos del artículo (si el mensaje actual completa una aclaración anterior, combiná ambos: \"pila\" + \"AA\" → \"pila AA\"). El resultado de la herramienta trae la instrucción de qué tipo responder (RESUELVE o ACLARA): seguila. No derives una consulta de precio porque falte un dato que el cliente puede darte: preguntá.");
            // "Tengo una distribuidora de papelería... busco biromes, ¿trabajan algo?" (Base4271, 2026-10-29):
            // derivaba sin buscar, como si un comercio que revende necesitara sí o sí un asesor.
            sb.AppendLine("- Vale igual si quien escribe es un comercio, empresa o distribuidor que quiere revender: primero buscá lo que pide y contale qué tienen y el precio que da la herramienta. Si pide condiciones o precios mayoristas, además ofrecele que alguien del equipo lo contacte para eso; no lo derives sin haber buscado.");
            sb.AppendLine("- CANTIDADES: los precios de las herramientas son por unidad. Nunca supongas que un artículo viene por paquete, caja o docena ni cuántas unidades trae: si el cliente pide \"un paquete\" o \"una caja\", decile el precio unitario y preguntale cuántas unidades quiere. Si dice cuántas unidades quiere, decile también el total (precio unitario por cantidad).");
            sb.AppendLine("- Nunca repitas una pregunta de aclaración que ya le hiciste en esta conversación (mirá tus mensajes anteriores).");
        }
        if (precioRequiereIdentificacion)
        {
            // Política de la empresa, no un disparador: la IA lee la conversación y decide si es una
            // consulta de precio (lo informa en "pide_precio"). Antes una lista de palabras ("precio",
            // "cuánto sale") forzaba un texto fijo que no dejaba registrarse a un cliente nuevo
            // (Las Vertientes, 2026-11-17).
            var conLink = !string.IsNullOrWhiteSpace(linkCatalogoParaLead);
            sb.AppendLine("- PRECIO SIN CLIENTE IDENTIFICADO: la empresa informa precios solo a clientes identificados y quien escribe todavía no lo está. Si en la conversación te pide precios, no los des ni los inventes y no lo derives por eso (tipo RESUELVE): explicale que para pasarle el precio correcto primero tenés que identificarlo, porque puede variar según su cuenta, y que si ya es cliente te diga su nombre o razón social."
                + (hayRegistrarCliente
                    ? " Si por lo que dice es su primera compra (o no lo sabés), ofrecele también registrarlo como cliente siguiendo el punto CLIENTE NUEVO: una vez registrado le podés pasar los precios y tomarle el pedido."
                    : " Vos no podés registrar clientes nuevos: no le ofrezcas registrarlo. Si por lo que dice no es cliente, eso lo resuelve alguien del equipo (tipo DERIVA).")
                + (conLink
                    ? $" Incluí, tal cual, este link real del catálogo para que vea los productos disponibles: {linkCatalogoParaLead!.Trim()} (no lo cambies ni inventes otros links)."
                    : " No menciones el catálogo (no hay un link disponible para compartir)."));
            sb.AppendLine("  No digas que no tenés el precio, no digas que un asesor le va a responder y no digas que el catálogo muestra precios.");
            sb.AppendLine("- En \"pide_precio\" indicá true si, leyendo el mensaje junto con la conversación, quien escribe está pidiendo precios (ahora o retomando lo que preguntó antes); si no, false.");
        }

        if (haySaldoEntreHerramientas)
        {
            sb.AppendLine();
            sb.AppendLine("SALDO / CUENTA CORRIENTE: si detectás una consulta de saldo o deuda y todavía no dijo qué necesita, preguntá en una sola línea si quiere el total, el detalle de los comprobantes, o el link para verlo en el portal online — no asumas cuál quiere. Si ya lo especificó (o lo pide después de que preguntaste), resolvé directo con la herramienta correspondiente sin volver a preguntar.");
        }

        switch (NormalizePolitica(politica))
        {
            case "SOLO_INFO":
                sb.AppendLine("- Solo resolvés con la información del negocio/base. Si la consulta NO se puede responder con eso, NO respondas con conocimiento general: pasá a la regla de \"cuando no podés resolver\".");
                break;
            case "GENERAL":
                sb.AppendLine("- Si la información del negocio no alcanza, resolvé igual con tu conocimiento general razonable (tipo RESUELVE).");
                break;
            default: // GENERAL_AVISA
                sb.AppendLine("- Si la información del negocio no alcanza, resolvé con tu conocimiento general PERO aclarando en la respuesta que un asesor lo va a confirmar (tipo RESUELVE).");
                break;
        }

        // Nunca dejar al cliente sin respuesta: si no se puede resolver, o se repregunta (ACLARA) o
        // se manda una contención y se deriva a un humano (DERIVA). Aplica a todas las políticas.
        sb.AppendLine();
        sb.AppendLine("CUANDO NO PODÉS RESOLVER (nunca dejes al cliente sin respuesta):");
        sb.AppendLine("- Si resolviste la consulta: tipo=\"RESUELVE\", \"puede_responder\": true, y la respuesta.");
        sb.AppendLine("- Si NO podés resolver pero UNA pregunta concreta te permitiría hacerlo, y NO se la hiciste ya antes en esta conversación: tipo=\"ACLARA\", \"puede_responder\": false, y en \"respuesta\" esa única pregunta (breve y puntual).");
        sb.AppendLine("- Si NO podés resolver y no hay una pregunta útil, o ya pediste una aclaración antes en esta conversación: tipo=\"DERIVA\", \"puede_responder\": false, y en \"respuesta\" una contención breve que deje claro que le va a responder una persona del equipo (un asesor o un compañero), no vos: nada de \"voy a verificar\" ni \"te respondo en un momento\", porque vos no vas a volver a escribir con la respuesta. Nombrá en pocas palabras qué es lo que le pasás al equipo, con lo que contó, para que vea que lo entendiste; redactala con tus palabras, no con una frase hecha. No inventes la solución. Nunca le pidas que contacte o consulte a un asesor: vos ya sos la empresa y la consulta le llega al equipo por este mismo chat.");
        sb.AppendLine("- Si el cliente no plantea nada nuevo y sólo quiere saber cómo va algo que ya consultó antes: tipo=\"SEGUIMIENTO\", \"puede_responder\": false, y en \"respuesta\", con tus palabras y sobre lo que quiere saber, que le avisás al equipo que escribió por eso (sin prometer plazos ni nombrar a nadie). Si además plantea un problema o una pregunta nueva, no es SEGUIMIENTO: tratalo como una consulta normal. Si lo que quiere saber es cómo viene un pedido o una entrega y tenés la herramienta de pedidos, usala: tampoco es SEGUIMIENTO.");
        sb.AppendLine("- Si el cliente avisa que el problema que había consultado ya se resolvió: no es SEGUIMIENTO ni DERIVA. tipo=\"RESUELVE\", \"puede_responder\": true, \"tema_resuelto\": true, y en \"respuesta\" un agradecimiento breve que cierre la charla, sin hacer preguntas. En cualquier otro caso \"tema_resuelto\": false.");
        sb.AppendLine("- DERIVA es sólo cuando hace falta que una persona del equipo tome el tema. Si vos le hacés una pregunta para poder ayudarlo, eso es ACLARA.");
        // Charlas reales repetidas (2026-11-26): con "si dudás, true" casi todo lo derivado abría ticket. El
        // ticket es para trabajo a fondo; lo demás lo contesta una persona por el chat. El criterio es de cada empresa.
        var criterio = string.IsNullOrWhiteSpace(criterioTicket) ? CriterioTicketPorDefecto : criterioTicket.Trim();
        sb.AppendLine($"- Cuando derivás, decidí en \"abrir_ticket\" si hace falta registrar un ticket, leyendo el mensaje junto con toda la conversación. Cuándo abrir ticket, según la empresa: {criterio} Si no se cumple, alcanza con que una persona del equipo lo lea y le conteste por acá: \"abrir_ticket\": false. Si dudás, false.");
        sb.AppendLine("- En \"resumen_caso\" poné en una línea corta de qué se trata lo que plantea el cliente, juntando lo que dijo en toda la charla (qué le pasa, dónde, con qué), escrito para que alguien del equipo lo entienda sin leer el chat. Si sólo saluda o agradece, dejalo vacío.");
        sb.AppendLine("- Nunca dejes \"respuesta\" vacía.");

        // Lo que el sistema sabe del caso, para que la IA conteste con eso en vez de que el código pise su
        // respuesta con textos fijos ("¿Me contás a qué consulta te referís?", "Gracias por tu mensaje…"), 2026-11-26.
        if (temaPendiente is not null || ultimoFueAclaracion)
        {
            sb.AppendLine();
            sb.AppendLine("ESTADO DEL CASO:");
            if (temaPendiente is { Length: 0 })
                sb.AppendLine("- No tiene ningún tema anterior pendiente con el equipo: no es SEGUIMIENTO. Si pregunta por algo anterior, preguntale de qué se trata.");
            else if (!string.IsNullOrWhiteSpace(temaPendiente))
                sb.AppendLine($"- Tema anterior pendiente con el equipo: {temaPendiente.Trim()}.");
            if (ultimoFueAclaracion)
                sb.AppendLine("- Tu último mensaje ya fue una pregunta y el cliente contestó: no repitas esa pregunta. Volvé a preguntar sólo si sin otro dato el equipo no puede ni empezar a verlo; si no, con lo que hay resolvé o pasáselo al equipo (DERIVA) diciendo de qué se trata.");
        }

        // Antes, ya derivado y sin respuesta del equipo, el bot callaba todo lo nuevo (2026-11-26, pedido de
        // ALFA NET: que analice la situación y responda en consecuencia).
        if (casoYaDerivado)
        {
            sb.AppendLine();
            sb.AppendLine("CASO YA PASADO AL EQUIPO: en esta charla ya le dijiste al cliente que su consulta la ve una persona del equipo, y todavía nadie del equipo le respondió.");
            sb.AppendLine("- Leé lo nuevo que escribe en ese contexto y contestá lo que corresponda a eso: puede sumar datos al mismo caso, preguntar cómo va, agradecer o decir que espera, plantear algo que podés resolver vos o traer un tema distinto.");
            sb.AppendLine("- Si suma datos o pedidos al mismo caso: tipo=\"RESUELVE\", \"abrir_ticket\": false, y en \"respuesta\" confirmale en una frase que lo agregás a lo que ya tiene el equipo. No es DERIVA: el caso ya está derivado.");
            sb.AppendLine("- Si agradece o dice que espera, sin preguntar nada: tipo=\"RESUELVE\" y una respuesta breve y cordial. No es SEGUIMIENTO.");
            sb.AppendLine("- Usá DERIVA sólo si trae un tema distinto que también necesita al equipo. Nunca le repitas que lo pasás al equipo con la misma idea que ya le dijiste.");
        }

        if (fueraDeHorario)
        {
            sb.AppendLine();
            sb.AppendLine("CONTEXTO HORARIO: estás atendiendo FUERA del horario de atención.");
            sb.AppendLine("- Presentate con naturalidad como asistente virtual y avisá que el equipo humano está fuera de horario.");
            sb.AppendLine("- Intentá resolver con la información. Si no podés, decile que dejás su mensaje registrado y que un operador lo revisa apenas sea posible (tipo DERIVA).");
            if (esUrgente)
                sb.AppendLine("- El cliente reporta un problema URGENTE (no puede facturar, o el sistema no anda/ no arranca). Avisale que lo marcaste como URGENTE para que puedan atenderlo a la brevedad, aunque sea fuera de horario.");
        }

        sb.AppendLine();
        sb.AppendLine(precioRequiereIdentificacion
            ? "Devolvé SOLO un JSON válido con esta forma exacta: {\"tipo\": \"RESUELVE|ACLARA|DERIVA|SEGUIMIENTO\", \"puede_responder\": true/false, \"abrir_ticket\": true/false, \"tema_resuelto\": true/false, \"pide_precio\": true/false, \"resumen_caso\": \"...\", \"respuesta\": \"...\"}."
            : "Devolvé SOLO un JSON válido con esta forma exacta: {\"tipo\": \"RESUELVE|ACLARA|DERIVA|SEGUIMIENTO\", \"puede_responder\": true/false, \"abrir_ticket\": true/false, \"tema_resuelto\": true/false, \"resumen_caso\": \"...\", \"respuesta\": \"...\"}.");
        return sb.ToString().Trim();
    }

    private static string NormalizePolitica(string? politica)
    {
        var p = (politica ?? string.Empty).Trim().ToUpperInvariant();
        return p is "SOLO_INFO" or "GENERAL" ? p : "GENERAL_AVISA";
    }

    private static string NormalizeTipo(string? tipo, bool puedeResponder)
    {
        var t = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (t is "RESUELVE" or "ACLARA" or "DERIVA" or "SEGUIMIENTO")
            return t;
        return puedeResponder ? "RESUELVE" : "DERIVA";
    }

    private static string Truncar(string? texto, int maxLength)
    {
        var t = texto ?? string.Empty;
        return t.Length <= maxLength ? t : t[..maxLength] + "…";
    }

    private static Task TraceDiagAsync(Func<string, CancellationToken, Task>? traceDiagAsync, string paso, CancellationToken ct)
        => traceDiagAsync is null ? Task.CompletedTask : traceDiagAsync(paso, ct);

    private static string ClasificarResultadoTool(string? resultado)
    {
        if (string.IsNullOrWhiteSpace(resultado))
            return "EMPTY";

        var texto = resultado.Trim();
        // "No se encontró ningún artículo..." antes salía como OK y ocultaba que la búsqueda no
        // había traído nada (caso pilas, Base4264).
        if (texto.StartsWith("No se encontró", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("No encontré", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("Sin coincidencias", StringComparison.OrdinalIgnoreCase))
            return "NOT_FOUND";
        // consultar_precio: "Más de N coincidencias..." o "N coincidencias. Si el cliente no especificó..."
        if (texto.StartsWith("Más de ", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(texto, @"^[2-9]\d* coincidencias\."))
            return "AMBIGUOUS";

        return texto.Contains("No se pudo", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("no se pudo", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("no hay", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("todavía no se configur", StringComparison.OrdinalIgnoreCase)
            ? "ERROR"
            : "OK";
    }

    /// <summary>Sólo para consultar_precio: el término que buscó el modelo, sanitizado y truncado
    /// (letras, dígitos, espacios, '-', '.'). Nunca el JSON completo ni argumentos de otras tools.</summary>
    private static string DescribirArgumentoDiag(string nombreHerramienta, string argumentosJson)
    {
        if (!string.Equals(nombreHerramienta, "consultar_precio", StringComparison.Ordinal))
            return string.Empty;

        string? articulo = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentosJson) ? "{}" : argumentosJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("articulo", out var valor)
                && valor.ValueKind == JsonValueKind.String)
                articulo = valor.GetString();
        }
        catch (JsonException)
        {
            return "|articulo=(invalido)";
        }

        var sb = new StringBuilder();
        foreach (var ch in (articulo ?? string.Empty).Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '.')
                sb.Append(ch);
        }

        var limpio = sb.ToString();
        return $"|articulo={(limpio.Length == 0 ? "(vacio)" : limpio[..Math.Min(limpio.Length, 60)])}";
    }

    private static string SanitizeDiag(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
                sb.Append(ch);
        }

        return sb.Length == 0 ? string.Empty : sb.ToString()[..Math.Min(sb.Length, 80)];
    }

    internal const string InstruccionesImagen =
        "IMAGEN: el cliente mandó una imagen (foto, captura de pantalla, comprobante). Miralo y usalo para responder: " +
        "si es un error en pantalla, leé el mensaje y resolvé con la información del negocio o derivá; si es un producto, " +
        "consultá el artículo con la herramienta de precios si está disponible. No inventes lo que no se lee bien: preguntá. " +
        "Nunca ofrezcas conectarte por AnyDesk aunque veas un ID. Agregá al JSON el campo \"imagen\" con una descripción " +
        "breve para el operador, incluyendo textualmente los datos útiles que se lean (códigos de error, IDs de AnyDesk, " +
        "números de comprobante, montos).";

    internal static ConversacionAsistenteRespuesta? ParseRespuesta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json.Substring(start, end - start + 1));
            var root = doc.RootElement;
            var puede = root.TryGetProperty("puede_responder", out var p)
                && (p.ValueKind == JsonValueKind.True
                    || (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b) && b));
            var respuesta = (root.TryGetProperty("respuesta", out var r) ? r.GetString() ?? string.Empty : string.Empty).Trim();
            var tipo = NormalizeTipo(root.TryGetProperty("tipo", out var t) ? t.GetString() : null, puede);

            if (puede && respuesta.Length == 0)
                return null;

            var imagen = root.TryGetProperty("imagen", out var img) && img.ValueKind == JsonValueKind.String
                ? (img.GetString() ?? string.Empty).Trim()
                : string.Empty;
            // Sin el campo no se abre ticket: el ticket es para trabajo a fondo y lo tiene que decidir la IA (2026-11-26).
            var abrirTicket = root.TryGetProperty("abrir_ticket", out var at)
                && (at.ValueKind == JsonValueKind.True
                    || (at.ValueKind == JsonValueKind.String && bool.TryParse(at.GetString(), out var bt) && bt));
            var pidePrecio = root.TryGetProperty("pide_precio", out var pp)
                && (pp.ValueKind == JsonValueKind.True
                    || (pp.ValueKind == JsonValueKind.String && bool.TryParse(pp.GetString(), out var bp) && bp));
            var temaResuelto = root.TryGetProperty("tema_resuelto", out var tr)
                && (tr.ValueKind == JsonValueKind.True
                    || (tr.ValueKind == JsonValueKind.String && bool.TryParse(tr.GetString(), out var btr) && btr));
            var resumenCaso = root.TryGetProperty("resumen_caso", out var rc) && rc.ValueKind == JsonValueKind.String
                ? (rc.GetString() ?? string.Empty).Trim()
                : string.Empty;
            return new ConversacionAsistenteRespuesta { PuedeResponder = puede, Respuesta = respuesta, Tipo = tipo, ImagenDescripcion = imagen, AbrirTicket = abrirTicket, PidePrecio = pidePrecio, TemaResuelto = temaResuelto, ResumenCaso = resumenCaso };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

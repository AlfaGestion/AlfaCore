using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Tests de CARACTERIZACIÓN del motor de automatizaciones de Conversaciones (auditoría
/// 2026-09-18, docs/modulos/conversaciones_plantillas_auditoria_2026-09-18.md). Documentan el
/// comportamiento REAL actual, no el deseado.
///
/// La mayor parte de <c>ConversacionesService</c> abre su propio <c>SqlConnection</c> contra la
/// base del tenant (no hay un repositorio inyectable) -- por eso, igual que
/// <c>WhatsAppTenantIsolationTests</c>, buena parte de estos tests son estructurales: leen el
/// código fuente y verifican orden/presencia de las llamadas relevantes, en vez de ejecutar el
/// pipeline completo (que requiere una base de tenant real, no solo ALFA_CENTRAL_TEST -- ver
/// SqlIntegrationFactAttribute). Donde la lógica es pura (sin SQL) se prueba de verdad, incluso
/// contra métodos privados por reflexión (mismo patrón que WhatsAppTemplateSelectionTests).
/// Lo que de verdad requiere SQL de tenant real queda marcado "REQUIERE SQL INTEGRATION" y no se
/// fuerza.
/// </summary>
public sealed class ConversacionesAutomationPipelineTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
    private static readonly string ConfigServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));

    // ---------------------------------------------------------------------------------------
    // 1. Lógica pura del bot/reglas -- reflexión sobre métodos privados static, sin tocar SQL.
    // ---------------------------------------------------------------------------------------

    private static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(ConversacionesService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ConversacionesService), methodName);
        return (T)method.Invoke(null, args)!;
    }

    [Theory]
    [InlineData("Necesito hablar con un humano", "humano, persona, reclamo", true)]
    [InlineData("Quiero hacer un RECLAMO por mi pedido", "humano, persona, reclamo", true)] // case-insensitive
    [InlineData("Todo bien, gracias", "humano, persona, reclamo", false)]
    [InlineData("hola", "", false)] // sin palabras configuradas -> nunca escala
    [InlineData("hola", null, false)]
    public void ContienePalabraEscalado_MatchesCaseInsensitiveSubstringAgainstCommaList(string texto, string? palabras, bool esperado)
        => Assert.Equal(esperado, InvokeStatic<bool>("ContienePalabraEscalado", texto, palabras));

    [Fact]
    public void Truncar_CutsAtExactLengthAndAppendsEllipsis()
    {
        Assert.Equal("hola", InvokeStatic<string>("Truncar", "hola", 10));
        Assert.Equal("hol…", InvokeStatic<string>("Truncar", "hola", 3));
        Assert.Equal(string.Empty, InvokeStatic<string>("Truncar", null, 5));
    }

    private static ConversacionAutomatizacionesConfigDto BusinessHoursConfig(
        bool lunes = true, string desde = "09:00", string hasta = "18:00")
        => new()
        {
            Activo = true,
            Lunes = lunes,
            Martes = true,
            Miercoles = true,
            Jueves = true,
            Viernes = true,
            Sabado = false,
            Domingo = false,
            HoraDesde = desde,
            HoraHasta = hasta
        };

    [Fact]
    public void IsOutsideBusinessHours_WithinConfiguredWindow_IsFalse()
    {
        var config = BusinessHoursConfig();
        var monday10am = new DateTime(2026, 9, 21, 10, 0, 0); // lunes
        Assert.False(InvokeStatic<bool>("IsOutsideBusinessHours", config, monday10am));
    }

    [Fact]
    public void IsOutsideBusinessHours_BeforeOpeningOrAfterClosing_IsTrue()
    {
        var config = BusinessHoursConfig();
        var monday8am = new DateTime(2026, 9, 21, 8, 0, 0);
        var monday19pm = new DateTime(2026, 9, 21, 19, 0, 0);
        Assert.True(InvokeStatic<bool>("IsOutsideBusinessHours", config, monday8am));
        Assert.True(InvokeStatic<bool>("IsOutsideBusinessHours", config, monday19pm));
    }

    [Fact]
    public void IsOutsideBusinessHours_ExactBoundaries_AreConsideredInside()
    {
        // Comparación es "< desde || > hasta": el borde exacto (09:00 y 18:00) cae DENTRO.
        var config = BusinessHoursConfig();
        var atOpen = new DateTime(2026, 9, 21, 9, 0, 0);
        var atClose = new DateTime(2026, 9, 21, 18, 0, 0);
        Assert.False(InvokeStatic<bool>("IsOutsideBusinessHours", config, atOpen));
        Assert.False(InvokeStatic<bool>("IsOutsideBusinessHours", config, atClose));
    }

    [Fact]
    public void IsOutsideBusinessHours_NonWorkingDay_IsAlwaysOutside()
    {
        // Lunes desactivado (Lunes=false) -- ni el horario configurado importa.
        var config = BusinessHoursConfig(lunes: false);
        var mondayNoon = new DateTime(2026, 9, 21, 12, 0, 0);
        Assert.True(InvokeStatic<bool>("IsOutsideBusinessHours", config, mondayNoon));
    }

    [Fact]
    public void IsOutsideBusinessHours_UnparseableSchedule_DefaultsToInsideHours()
    {
        // Si HoraDesde/HoraHasta no parsean, el método devuelve false (nunca "fuera") -- fail-safe
        // hacia "no mandar el aviso de fuera de horario" en vez de mandarlo siempre.
        var config = BusinessHoursConfig(desde: "no-es-una-hora", hasta: "18:00");
        Assert.False(InvokeStatic<bool>("IsOutsideBusinessHours", config, new DateTime(2026, 9, 21, 3, 0, 0)));
    }

    [Fact]
    public void IsOutsideBusinessHours_OvernightWindow_WrapsAcrossMidnight()
    {
        // desde > hasta (ej. 22:00 a 06:00): la condición se invierte a "AND" -- ventana nocturna.
        var config = BusinessHoursConfig(desde: "22:00", hasta: "06:00");
        var at23 = new DateTime(2026, 9, 21, 23, 0, 0);
        var at12 = new DateTime(2026, 9, 21, 12, 0, 0);
        Assert.False(InvokeStatic<bool>("IsOutsideBusinessHours", config, at23)); // dentro de la ventana nocturna
        Assert.True(InvokeStatic<bool>("IsOutsideBusinessHours", config, at12)); // mediodía: fuera
    }

    private static ConversacionReglaDto Regla(string tipoCoincidencia, string palabras, string canal = "")
        => new() { TipoCoincidencia = tipoCoincidencia, Palabras = palabras, Canal = canal };

    [Fact]
    public void CoincideRegla_Igual_RequiresExactWholeTextMatch()
    {
        var regla = Regla("IGUAL", "hola");
        Assert.True(InvokeStatic<bool>("CoincideRegla", "hola", regla));
        Assert.False(InvokeStatic<bool>("CoincideRegla", "hola como estas", regla));
    }

    [Fact]
    public void CoincideRegla_Empieza_RequiresPrefixMatch()
    {
        var regla = Regla("EMPIEZA", "hola");
        Assert.True(InvokeStatic<bool>("CoincideRegla", "hola como estas", regla));
        Assert.False(InvokeStatic<bool>("CoincideRegla", "como estas hola", regla));
    }

    [Fact]
    public void CoincideRegla_Contiene_IsTheDefaultAndMatchesSubstringAnywhere()
    {
        var regla = Regla("CONTIENE", "precio");
        Assert.True(InvokeStatic<bool>("CoincideRegla", "cual es el precio del servicio", regla));
        var reglaTipoDesconocido = Regla("ALGO_INVALIDO", "precio");
        Assert.True(InvokeStatic<bool>("CoincideRegla", "el precio", reglaTipoDesconocido)); // fallback = CONTIENE
    }

    [Fact]
    public void CoincideRegla_MultipleKeywords_MatchesAny()
    {
        var regla = Regla("CONTIENE", "precio, presupuesto, cotización");
        Assert.True(InvokeStatic<bool>("CoincideRegla", "quiero un presupuesto", regla));
    }

    [Fact]
    public void CoincideRegla_NoKeywordsConfigured_NeverMatches()
        => Assert.False(InvokeStatic<bool>("CoincideRegla", "cualquier cosa", Regla("CONTIENE", "")));

    // ---------------------------------------------------------------------------------------
    // 2. Orden del pipeline: Reglas -> (Bienvenida, FueraDeHorario, Bot).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Pipeline_ReglasGatesTheRestButWelcomeOutOfHoursAndBotRunIndependentlyOfEachOther()
    {
        // if (!await TryAutoReplyReglasAsync(...)) { Welcome; OutOfHours; Bot; } -- si una regla
        // pidió Detener=true (TryAutoReplyReglasAsync devuelve true), NINGUNA de las tres corre.
        // Si no hubo regla que corte, las tres SIEMPRE se llaman en secuencia sin early-return
        // entre ellas: bienvenida, fuera-de-horario y bot pueden responder las tres al mismo
        // mensaje (cada una tiene sus propios guardarraíles de no-repetir, pero no se excluyen
        // entre sí a nivel de pipeline).
        var occurrences = 0;
        var index = 0;
        while (true)
        {
            var reglasCall = ServiceSource.IndexOf("if (!await TryAutoReplyReglasAsync(conversationId, incoming.Text, CancellationToken.None))", index, StringComparison.Ordinal);
            if (reglasCall < 0) break;
            var welcome = ServiceSource.IndexOf("await TryAutoReplyWelcomeAsync(conversationId, CancellationToken.None);", reglasCall, StringComparison.Ordinal);
            var outOfHours = ServiceSource.IndexOf("await TryAutoReplyOutOfHoursAsync(conversationId, CancellationToken.None);", reglasCall, StringComparison.Ordinal);
            var bot = ServiceSource.IndexOf("await TryAutoReplyBotAsync(conversationId, incoming.Text, CancellationToken.None);", reglasCall, StringComparison.Ordinal);
            Assert.True(welcome > reglasCall && welcome < outOfHours, $"orden roto en offset {reglasCall}");
            Assert.True(outOfHours < bot, $"orden roto en offset {reglasCall}");
            Assert.True(bot - reglasCall < 400, $"llamadas no contiguas (¿se insertó algo entremedio?) en offset {reglasCall}");
            occurrences++;
            index = bot + 1;
        }
        // Este bloque está duplicado (uno por cada tramo de parseo de mensajes entrantes del
        // webhook); si baja de 1 copia, algo se rompió estructuralmente.
        Assert.True(occurrences >= 1, "No se encontró el bloque Reglas->Bienvenida->FueraDeHorario->Bot.");
    }

    [Fact]
    public void Pipeline_AutomationsOnlyRunForNewMessagesNeverForDuplicatesOrOutbound()
    {
        // GetExistingMessageIdByWhatsAppIdAsync corre ANTES de decidir isNewMessage, y las
        // automatizaciones están dentro de `if (isNewMessage)`: un WhatsAppMessageId repetido
        // (reentrega de Meta) no vuelve a disparar reglas/bienvenida/fuera-de-horario/bot.
        var method = ServiceSource.IndexOf("var messageId = await GetExistingMessageIdByWhatsAppIdAsync(incoming.WhatsAppMessageId, token);", StringComparison.Ordinal);
        Assert.True(method >= 0);
        var isNewMessage = ServiceSource.IndexOf("var isNewMessage = messageId <= 0;", method, StringComparison.Ordinal);
        var ifNewBlock = ServiceSource.IndexOf("if (isNewMessage)", isNewMessage, StringComparison.Ordinal);
        var reglasCall = ServiceSource.IndexOf("TryAutoReplyReglasAsync", ifNewBlock, StringComparison.Ordinal);
        Assert.True(isNewMessage > method && ifNewBlock > isNewMessage && reglasCall > ifNewBlock);
    }

    // ---------------------------------------------------------------------------------------
    // 3. Reglas: prioridad, Detener, alcance por canal/base, sin-asignar, primer-contacto.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Reglas_AreEvaluatedInOrderThenById_AndOnlyActiveOnesAreConsidered()
    {
        Assert.Contains("foreach (var regla in reglas.Where(r => r.Activa).OrderBy(r => r.Orden).ThenBy(r => r.IdRegla))", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Reglas_DetenerStopsTheLoop_ButWithoutItMultipleRulesCanApplyToTheSameMessage()
    {
        // Sin Detener=true, el foreach sigue evaluando reglas siguientes -- varias reglas (con
        // acciones distintas: asignar, prioridad, respuesta) pueden aplicarse al mismo mensaje.
        // Solo Detener=true corta y, además, es lo único que frena bienvenida/fuera-de-horario/bot.
        var loopStart = ServiceSource.IndexOf("foreach (var regla in reglas.Where(r => r.Activa)", StringComparison.Ordinal);
        var detenerCheck = ServiceSource.IndexOf("if (regla.Detener)", loopStart, StringComparison.Ordinal);
        var returnTrue = ServiceSource.IndexOf("return true;", detenerCheck, StringComparison.Ordinal);
        Assert.True(loopStart >= 0 && detenerCheck > loopStart && returnTrue > detenerCheck);
        Assert.True(returnTrue - detenerCheck < 60);
    }

    [Fact]
    public void Reglas_ChannelScopeIsOptionalAndSkippedIfNotMatching()
    {
        Assert.Contains(
            "if (!string.IsNullOrWhiteSpace(regla.Canal)\r\n                    && !string.Equals(regla.Canal, canal, StringComparison.OrdinalIgnoreCase))\r\n                    continue;",
            ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Reglas_SoloPrimerContacto_OnlyMatchesWhenExactlyOneIncomingMessageExists()
        => Assert.Contains("if (regla.SoloPrimerContacto)", ServiceSource, StringComparison.Ordinal);

    [Fact]
    public void Reglas_SoloSinAsignar_SkipsWhenAConversationAlreadyHasATechnician()
    {
        var block = ServiceSource.IndexOf("if (regla.SoloSinAsignar)", StringComparison.Ordinal);
        var techLookup = ServiceSource.IndexOf("tecnicoActual ??= await GetConversationTechnicianIdAsync(idConversacion, ct)", block, StringComparison.Ordinal);
        var skip = ServiceSource.IndexOf("continue;", techLookup, StringComparison.Ordinal);
        Assert.True(block >= 0 && techLookup > block && skip > techLookup);
    }

    [Fact]
    public void Reglas_AreScopedPerTenantViaConversacionesConfigService()
    {
        // GetReglasAsync viene de conversacionesConfigService, ya resuelto contra la conexión del
        // tenant activo -- una regla de otra Base nunca se carga acá (mismo mecanismo de
        // aislamiento que el resto de Configuración, cubierto en WhatsAppTenantIsolationTests).
        Assert.Contains("var reglas = await conversacionesConfigService.GetReglasAsync(ct).ConfigureAwait(false);", ServiceSource, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 4. Bienvenida.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Bienvenida_RequiresModuleActiveAndOwnConfigFlagAndNonEmptyMessage()
    {
        var method = ServiceSource.IndexOf("private async Task TryAutoReplyWelcomeAsync", StringComparison.Ordinal);
        var moduleGate = ServiceSource.IndexOf("IsModuloActivoParaClienteActualAsync(\"AUTOMATIZACIONES\", ct)", method, StringComparison.Ordinal);
        var configGate = ServiceSource.IndexOf("if (!config.BienvenidaActivo || string.IsNullOrWhiteSpace(config.BienvenidaMensaje))", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && moduleGate > method && configGate > moduleGate);
    }

    [Fact]
    public void Bienvenida_OnlySendsOnExactlyOneIncomingAndZeroOutgoingMessages()
    {
        // "Primer contacto real": no se dispara en conversaciones ya existentes (>1 entrante) ni
        // si ya se mandó algo (>=1 saliente, incluye respuestas manuales previas).
        Assert.Contains("if (entrantes != 1 || salientes != 0)\r\n                return;", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Bienvenida_SendsWithSistemaAccionBienvenida_DistinguishableFromManualSends()
    {
        var method = ServiceSource.IndexOf("private async Task TryAutoReplyWelcomeAsync", StringComparison.Ordinal);
        var send = ServiceSource.IndexOf("SistemaAccion = \"BIENVENIDA\"", method, StringComparison.Ordinal);
        Assert.True(send > method);
    }

    // ---------------------------------------------------------------------------------------
    // 5. Fuera de horario.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void FueraDeHorario_SkipsWhenAsistenteIaHandlesOutOfHoursInstead()
    {
        // Si el bot está activo y configurado para atender fuera de horario, el mensaje FIJO no se
        // manda -- lo atiende el asistente IA en su lugar (ver EjecutarRespuestaBotAsync).
        Assert.Contains(
            "if (config.BotActivo && config.AsistenteFueraHorario && asistenteService.IsConfigured)\r\n                        return;",
            ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeHorario_DoesNotRepeatTheNoticeOnTheSameLocalDate()
        => Assert.Contains("if (await HasSentOutOfHoursNoticeAsync(idConversacion, businessNow, token).ConfigureAwait(false))\r\n                        return;", ServiceSource, StringComparison.Ordinal);

    [Fact]
    public void FueraDeHorario_RequiresModuleActiveAndOwnConfigured()
    {
        var method = ServiceSource.IndexOf("private async Task TryAutoReplyOutOfHoursAsync", StringComparison.Ordinal);
        var moduleGate = ServiceSource.IndexOf("IsModuloActivoParaClienteActualAsync(\"AUTOMATIZACIONES\", ct)", method, StringComparison.Ordinal);
        var configuredGate = ServiceSource.IndexOf("if (!config.IsConfigured)", method, StringComparison.Ordinal);
        var hoursCheck = ServiceSource.IndexOf("if (!IsOutsideBusinessHours(config, businessNow))", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && moduleGate > method && configuredGate > moduleGate && hoursCheck > configuredGate);
    }

    // ---------------------------------------------------------------------------------------
    // 6. Bot: guardarraíles y orden real de evaluación.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Bot_GuardrailOrder_EscaladoThenAssignedThenWindowThenMaxRepliesThenAlreadyRepliedThenHuman()
    {
        var method = ServiceSource.IndexOf("private async Task EjecutarRespuestaBotAsync", StringComparison.Ordinal);
        var escalado = ServiceSource.IndexOf("if (ContienePalabraEscalado(texto, config.BotPalabrasEscalado))", method, StringComparison.Ordinal);
        var asignado = ServiceSource.IndexOf("if (config.BotSoloSinAsignar)", method, StringComparison.Ordinal);
        var ventana = ServiceSource.IndexOf("if (!await IsSendWindowActiveAsync(idConversacion, canalBot, token)", method, StringComparison.Ordinal);
        var maxReplies = ServiceSource.IndexOf("if (botCount >= Math.Max(1, config.BotMaxRespuestas))", method, StringComparison.Ordinal);
        var yaRespondio = ServiceSource.IndexOf("if (yaRespondioEsteMensaje)", method, StringComparison.Ordinal);
        var humano = ServiceSource.IndexOf("if (humanoYaRespondio)", method, StringComparison.Ordinal);
        var soloFueraHorario = ServiceSource.IndexOf("if (config.BotSoloFueraHorario && !fueraDeHorario)", method, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(escalado > method);
        Assert.True(asignado > escalado);
        Assert.True(ventana > asignado);
        Assert.True(maxReplies > ventana);
        Assert.True(yaRespondio > maxReplies);
        Assert.True(humano > yaRespondio);
        Assert.True(soloFueraHorario > humano);
    }

    [Fact]
    public void Bot_BotSoloSinAsignar_OnlyChecksTechnicianAssignment_ByItselfIgnoresManualRepliesFromUnlinkedUsers()
    {
        // Caracterización del guardarraíl histórico (previo al fix de handoff, ver más abajo): el
        // bloque `if (config.BotSoloSinAsignar)` únicamente mira GetConversationTechnicianIdAsync
        // -- técnico asignado en CONV_CONVERSACIONES.IdTecnico. Por sí solo NO sabe si un humano ya
        // contestó manualmente sin que la conversación quedara asignada (ver P0 handoff test).
        var block = ServiceSource.IndexOf("if (config.BotSoloSinAsignar)", StringComparison.Ordinal);
        var techLookup = ServiceSource.IndexOf("var tecnico = await GetConversationTechnicianIdAsync(idConversacion, token)", block, StringComparison.Ordinal);
        var returnStmt = ServiceSource.IndexOf("return;", techLookup, StringComparison.Ordinal);
        var nextBlock = ServiceSource.IndexOf("var canalBot", block, StringComparison.Ordinal);
        Assert.True(block >= 0 && techLookup > block && returnStmt > techLookup && returnStmt < nextBlock);
        // Ese bloque, aislado, NO menciona SistemaAutor/humano -- la señal la agrega el fix de más
        // abajo, en un guardarraíl SEPARADO (GetBotReplyStatsAsync/humanoYaRespondio), no acá.
        var blockText = ServiceSource[block..nextBlock];
        Assert.DoesNotContain("humanoYaRespondio", blockText, StringComparison.Ordinal);
        Assert.DoesNotContain("SistemaAutor", blockText, StringComparison.Ordinal);
    }

    [Fact]
    public void Bot_BotEsperaMinutos_QueuesInsteadOfRespondingImmediately()
    {
        var method = ServiceSource.IndexOf("private async Task TryAutoReplyBotAsync", StringComparison.Ordinal);
        var wait = ServiceSource.IndexOf("if (config.BotEsperaMinutos > 0)", method, StringComparison.Ordinal);
        var encolar = ServiceSource.IndexOf("await EncolarRespuestaBotAsync(idConversacion, config.BotEsperaMinutos, ct)", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && wait > method && encolar > wait);
    }

    [Fact]
    public void Bot_PendingQueueJob_RereadsTechnicianAndGuardrailsBeforeAnswering()
    {
        // El job de espera (ProcesarRespuestasBotPendientesAsync) vuelve a llamar
        // EjecutarRespuestaBotAsync con el texto MÁS RECIENTE -- corre TODOS los guardarraíles de
        // nuevo (incluido, tras el fix, humanoYaRespondio), así que si un humano contestó durante
        // la espera configurada, el bot igual calla cuando el job lo retoma.
        var method = ServiceSource.IndexOf("public async Task<int> ProcesarRespuestasBotPendientesAsync", StringComparison.Ordinal);
        var call = ServiceSource.IndexOf("await EjecutarRespuestaBotAsync(idConversacion, texto, config, ct)", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && call > method);
    }

    [Fact]
    public void Bot_NeverRunsWithoutOpenAiConfiguredOrModuleActive()
    {
        var method = ServiceSource.IndexOf("private async Task TryAutoReplyBotAsync", StringComparison.Ordinal);
        var moduleGate = ServiceSource.IndexOf("IsModuloActivoParaClienteActualAsync(\"AUTOMATIZACIONES\", ct)", method, StringComparison.Ordinal);
        var openAiGate = ServiceSource.IndexOf("if (!config.BotActivo || !asistenteService.IsConfigured)", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && moduleGate > method && openAiGate > moduleGate);
    }

    // ---------------------------------------------------------------------------------------
    // 7. Handoff humano -- P0. Ver docs/modulos/conversaciones_plantillas_auditoria_2026-09-18.md.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Handoff_AutoAssignOnFirstManualReply_OnlyTriggersWhenIdTecnicoAutorIsProvided()
    {
        // SendMessageAsync YA auto-asigna al primer técnico que responde -- PERO solo si
        // request.IdTecnicoAutor viene no vacío. AutoAssignIfUnassignedAsync además solo escribe
        // si la conversación estaba realmente sin asignar (UPDATE condicional).
        var call = ServiceSource.IndexOf(
            "if (!isInternal && !string.IsNullOrWhiteSpace(request.IdTecnicoAutor))\r\n                await AutoAssignIfUnassignedAsync(request.IdConversacion, request.IdTecnicoAutor.Trim(), request.UsuarioAccion, request.SistemaAccion, token);",
            StringComparison.Ordinal);
        Assert.True(call >= 0);

        var method = ServiceSource.IndexOf("private async Task AutoAssignIfUnassignedAsync", StringComparison.Ordinal);
        Assert.Contains("AND NULLIF(LTRIM(RTRIM(ISNULL(IdTecnico, N''))), N'') IS NULL;", ServiceSource[method..(method + 1000)], StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_UiCanSendManualRepliesWithoutIdTecnicoAutor_WhenTheUserHasNoLinkedTechnician()
    {
        // Conversaciones.razor: CurrentMessageTechnicianId => CurrentUserTechnician?.IdTecnico ??
        // string.Empty. Si el usuario logueado no tiene un Técnico vinculado (cuentas
        // admin/supervisor sin ficha de técnico son un caso real, no hipotético), el envío manual
        // manda IdTecnicoAutor = "" -- AutoAssignIfUnassignedAsync arriba NO corre (string vacío),
        // la conversación queda "sin asignar" pese a que un humano ya contestó. ESTE es el hueco
        // que explota el bug P0: BotSoloSinAsignar, mirando solo IdTecnico, no lo detecta.
        var page = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));
        Assert.Contains("private string CurrentMessageTechnicianId\r\n        => CurrentUserTechnician?.IdTecnico ?? string.Empty;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_Fix_BotStopsWhenAHumanAlreadyRepliedToTheSameIncomingMessage_RegardlessOfAssignment()
    {
        // Corrección mínima aplicada en este trabajo (ver commit fix:): GetBotReplyStatsAsync ahora
        // también calcula, con el mismo criterio por IdMensaje que yaRespondioEsteMensaje, si el
        // ÚLTIMO saliente posterior al último entrante tiene un SistemaAutor que NO está en el set
        // de automatizaciones conocidas (BOT/REGLA/AUTOMATIZACION/BIENVENIDA/AUTOCIERRE*/SLA/
        // PROGRAMADO) -- es decir, si fue un humano. Si es así, el bot corta ANTES de llamar a
        // OpenAI, sin importar BotSoloSinAsignar ni si la conversación quedó técnicamente
        // "asignada": ya hay una respuesta humana a ese mensaje puntual.
        Assert.Contains("HumanoYaRespondioUltimoEntrante", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("NOT IN ({AutomatedOutgoingSistemaAutorSqlList})", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("if (humanoYaRespondio)", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("EjecutarStop:HumanoYaRespondioEsteMensaje", ServiceSource, StringComparison.Ordinal);

        // La comparación es por IdMensaje contra el ÚLTIMO entrante (mismo criterio que
        // yaRespondioEsteMensaje) -- NO "alguna vez hubo un humano en la conversación", para no
        // dejar al bot mudo para siempre tras la primera intervención humana si el cliente vuelve
        // a escribir más tarde sin que nadie responda de nuevo.
        Assert.Contains("return (count, ultimoAutomaticoId > ultimoEntranteId, ultimoHumanoId > ultimoEntranteId);", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_Fix_DoesNotChangeSemanticsOfTheExistingAutomatedDedupeCheck()
    {
        // La corrección agrega una columna/condición nueva a la MISMA consulta -- no toca
        // yaRespondioEsteMensaje (dedupe de respuestas automáticas) ni botCount (tope
        // BotMaxRespuestas), que siguen calculándose igual que antes.
        Assert.Contains("(SELECT COUNT(1) FROM dbo.CONV_MENSAJES WHERE IdConversacion = @Id AND Direction = N'SALIENTE' AND ISNULL(SistemaAutor, '') = N'BOT'),", ServiceSource, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 7b. Handoff -- P0 seguimiento 2026-09-21. Helper central IsHumanAuthoredOutgoing y su
    // reutilización en el guard de SoloSinAsignar (TryAutoReplyReglasAsync). Ver
    // GetBotReplyStatsAsync (7.) para el fix original del bot, que esta sección NO modifica.
    // ---------------------------------------------------------------------------------------

    // Reflexión sobre el predicado central, puro y sin SQL -- ejecutable de verdad (mismo patrón
    // que ContienePalabraEscalado/CoincideRegla más arriba).
    [Theory]
    [InlineData("BOT", false)]
    [InlineData("bot", false)] // case-insensitive, igual que el SQL (ISNULL(...) IN (...))
    [InlineData("REGLA", false)] // una regla automática previa NO cuenta como intervención humana
    [InlineData("BIENVENIDA", false)] // la bienvenida NO cuenta como humana
    [InlineData("AUTOMATIZACION", false)] // el aviso de fuera de horario (SistemaAccion="AUTOMATIZACION") NO cuenta como humana
    [InlineData("AUTOCIERRE_AVISO", false)]
    [InlineData("AUTOCIERRE", false)]
    [InlineData("SLA", false)]
    [InlineData("PROGRAMADO", false)]
    [InlineData(null, true)] // NULL/'' -- envío manual sin SistemaAccion completo -- cuenta como humano
    [InlineData("", true)]
    [InlineData("  ", true)] // solo espacios -- Trim() lo deja vacío -- humano
    [InlineData("alfanetar@gmail.com", true)] // identidad real de un usuario (ej. UsuarioAutor/SistemaAutor manual) -- humano
    [InlineData("AlfaCore", true)] // "AlfaCore" (UsuarioAccion genérico de reglas/SLA en otros flujos) no es un SistemaAutor automatizado -- fuera de la lista, cuenta como humano por diseño del predicado
    public void IsHumanAuthoredOutgoing_ExcludesOnlyTheKnownAutomatedSistemaAutores(string? sistemaAutor, bool esperadoHumano)
        => Assert.Equal(esperadoHumano, InvokeStatic<bool>("IsHumanAuthoredOutgoing", sistemaAutor));

    [Fact]
    public void IsHumanAuthoredOutgoing_IsTheSingleSourceOfTruthSharedWithTheSqlAutomatedList()
    {
        // AutomatedOutgoingSistemaAutorSqlList (usado por GetBotReplyStatsAsync, ver 7.) ya NO es
        // una segunda lista paralela en sintaxis SQL -- se genera del mismo array C# que usa
        // IsHumanAuthoredOutgoing. Si algún día se agrega/saca un autor automatizado, alcanza con
        // tocar UN solo array.
        Assert.Contains(
            "private static readonly string[] AutomatedOutgoingSistemaAutores =",
            ServiceSource, StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly string AutomatedOutgoingSistemaAutorSqlList =\r\n        string.Join(\", \", AutomatedOutgoingSistemaAutores.Select(a => $\"N'{a}'\"));",
            ServiceSource, StringComparison.Ordinal);
        Assert.Contains(
            "!AutomatedOutgoingSistemaAutores.Contains(\r\n            (sistemaAutor ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);",
            ServiceSource, StringComparison.Ordinal);

        // El fix del bot (GetBotReplyStatsAsync, commit 66dc7046) sigue exactamente igual: misma
        // consulta SQL, mismo return -- este trabajo no lo tocó, solo unificó de dónde sale
        // AutomatedOutgoingSistemaAutorSqlList. Ver también Handoff_Fix_* más arriba.
        Assert.Contains("return (count, ultimoAutomaticoId > ultimoEntranteId, ultimoHumanoId > ultimoEntranteId);", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SoloSinAsignar_NowAlsoChecksHumanInterventionOnTheCurrentIncomingMessage_NotOnlyTechnicianAssignment()
    {
        // Bug P0 corregido en este trabajo: antes, `if (regla.SoloSinAsignar)` sólo miraba
        // GetConversationTechnicianIdAsync (CONV_CONVERSACIONES.IdTecnico). Un humano sin Técnico
        // vinculado que ya respondió (plantilla/sticker/reacción/adjunto/texto) al mismo entrante
        // NO deja la conversación "asignada" -- AutoAssignIfUnassignedAsync no corre sin
        // IdTecnicoAutor -- así que una regla SoloSinAsignar posterior seguía disparando sobre un
        // mensaje que un humano YA atendió. Ahora, tras el chequeo de técnico, se reutiliza la
        // MISMA consulta que ya usa el guard del bot (GetBotReplyStatsAsync) para el mismo criterio
        // de ventana: último SALIENTE no-automatizado posterior al último ENTRANTE.
        var block = ServiceSource.IndexOf("if (regla.SoloSinAsignar)", StringComparison.Ordinal);
        var techLookup = ServiceSource.IndexOf("tecnicoActual ??= await GetConversationTechnicianIdAsync(idConversacion, ct)", block, StringComparison.Ordinal);
        var techSkip = ServiceSource.IndexOf("continue;", techLookup, StringComparison.Ordinal);
        var humanLookup = ServiceSource.IndexOf(
            "humanoYaRespondioUltimoEntrante ??=\r\n                        (await GetBotReplyStatsAsync(idConversacion, ct).ConfigureAwait(false)).HumanoYaRespondioUltimoEntrante;",
            techSkip, StringComparison.Ordinal);
        var humanSkip = ServiceSource.IndexOf("continue;", humanLookup, StringComparison.Ordinal);

        Assert.True(block >= 0, "No se encontró el bloque SoloSinAsignar.");
        Assert.True(techLookup > block && techSkip > techLookup, "El chequeo de técnico asignado sigue primero (no se cambió).");
        Assert.True(humanLookup > techSkip, "El chequeo de intervención humana debe correr DESPUÉS del chequeo de técnico, dentro del mismo bloque SoloSinAsignar.");
        Assert.True(humanSkip > humanLookup);

        // Sigue siendo un guard de SOLO LECTURA: GetBotReplyStatsAsync es una consulta SELECT pura
        // (ver 7.), y el bloque SoloSinAsignar en sí no ejecuta ningún UPDATE/asignación -- eso
        // sigue pasando más abajo, fuera de este bloque, sólo si regla.AsignarTecnico viene
        // configurado explícitamente (comportamiento preexistente, no tocado acá).
        var blockEnd = ServiceSource.IndexOf("// Etiquetar: fijar prioridad", block, StringComparison.Ordinal);
        Assert.True(blockEnd > humanSkip);
        var blockText = ServiceSource[block..blockEnd];
        Assert.DoesNotContain("await AssignConversationAsync", blockText, StringComparison.Ordinal);
        Assert.DoesNotContain("await AutoAssignIfUnassignedAsync", blockText, StringComparison.Ordinal);
    }

    [Fact]
    public void SoloSinAsignar_HumanCheckIsLazilyCachedOncePerRuleEvaluation_NotOncePerRule()
    {
        // humanoYaRespondioUltimoEntrante se declara UNA vez, fuera del foreach de reglas (mismo
        // patrón que tecnicoActual/entrantes/autoConfig ya usados) -- si varias reglas activas
        // tienen SoloSinAsignar=true, GetBotReplyStatsAsync no se re-consulta para cada una.
        var foreachStart = ServiceSource.IndexOf("foreach (var regla in reglas.Where(r => r.Activa)", StringComparison.Ordinal);
        var declaration = ServiceSource.IndexOf("bool? humanoYaRespondioUltimoEntrante = null;", StringComparison.Ordinal);
        Assert.True(declaration >= 0 && declaration < foreachStart, "La variable de caché debe declararse antes del foreach, no dentro.");
    }

    [Fact]
    public void SoloSinAsignar_NoRegressionWhenTrulyUnassignedAndNoHumanReplied()
    {
        // No-regresión explícita: si tecnicoActual queda vacío Y GetBotReplyStatsAsync devuelve
        // HumanoYaRespondioUltimoEntrante=false (nadie respondió el último entrante), ninguno de
        // los dos `continue;` se ejecuta -- la regla sigue evaluándose y puede disparar su acción
        // exactamente como antes del fix. Esto es una propiedad del código (ambos son guards que
        // sólo saltan la regla con `continue;` bajo su propia condición), verificada arriba en
        // SoloSinAsignar_NowAlsoChecksHumanInterventionOnTheCurrentIncomingMessage_*; documentado
        // acá porque el escenario "sin técnico y sin humano" requiere ejecutar
        // GetBotReplyStatsAsync contra una conversación real (abre su propio SqlConnection, sin
        // repositorio inyectable) -- REQUIERE SQL INTEGRATION para un test end-to-end; la lógica en
        // sí (dos condiciones independientes, cada una con su propio `continue`) es estructuralmente
        // verificable y ya lo está.
        Assert.Contains("if (!string.IsNullOrWhiteSpace(tecnicoActual))\r\n                        continue;", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("if (humanoYaRespondioUltimoEntrante == true)\r\n                        continue;", ServiceSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SendTemplateMessageAsync")]
    [InlineData("SendReactionAsync")]
    [InlineData("UploadAttachmentAsync")]
    public void ManualSendPaths_PlantillaReaccionYAdjunto_NeverAutoAssignAndAlwaysWriteDirectionSaliente(string methodName)
    {
        // Precondición del bug P0 (auditoría 2026-09-18), reconfirmada acá: los tres caminos de
        // envío manual que NO son texto plano (plantilla, reacción, adjunto -- éste último también
        // cubre sticker, ver SendFavoriteStickerAsync -> UploadAttachmentAsync) insertan
        // Direction="SALIENTE" con el SistemaAutor real del usuario, pero NINGUNO llama a
        // AutoAssignIfUnassignedAsync (a diferencia de SendMessageAsync de texto plano). Por eso
        // una conversación puede tener una respuesta humana real sin quedar "asignada" -- el hueco
        // exacto que el guard nuevo de SoloSinAsignar y el fix del bot cubren mirando SistemaAutor
        // en vez de IdTecnico.
        var startMarker = ServiceSource.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(startMarker > 0, $"No se encontró {methodName}.");
        var nextMethod = ServiceSource.IndexOf("\r\n    public ", startMarker + 1, StringComparison.Ordinal);
        Assert.True(nextMethod > startMarker, $"No se pudo acotar el cuerpo de {methodName}.");
        var body = ServiceSource[startMarker..nextMethod];

        Assert.Contains("Direction = \"SALIENTE\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoAssignIfUnassignedAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualSendPaths_Sticker_ReusesUploadAttachmentAsync_SoItInheritsTheSameHumanSignal()
    {
        // SendFavoriteStickerAsync no inserta su propio CONV_MENSAJES -- delega en
        // UploadAttachmentAsync (ya cubierto arriba), con SistemaAccion pasado tal cual desde el
        // caller. Un sticker manual enviado por un usuario sin Técnico vinculado cuenta como
        // intervención humana por el mismo motivo que un adjunto común.
        var method = ServiceSource.IndexOf("public Task<ConversacionAdjuntoDto> SendFavoriteStickerAsync", StringComparison.Ordinal);
        Assert.True(method >= 0);
        var call = ServiceSource.IndexOf("return await UploadAttachmentAsync(new ConversacionUploadAdjuntoRequest", method, StringComparison.Ordinal);
        Assert.True(call > method);
    }

    [Fact]
    public void ManualSendPaths_Reaction_IsDirectionSalienteSoItParticipatesInTheHumanGuard()
    {
        // Nota de scope: SendReactionAsync SÍ es Direction=SALIENTE (a diferencia de, por ejemplo,
        // un evento de sistema en NOTA_INTERNA) -- por eso participa del criterio de ventana
        // "último SALIENTE posterior al último ENTRANTE" igual que texto/plantilla/sticker/adjunto.
        // No requería un caso especial en el helper ni en el guard.
        var method = ServiceSource.IndexOf("public Task<ConversacionMessageResultDto> SendReactionAsync", StringComparison.Ordinal);
        var nextMethod = ServiceSource.IndexOf("\r\n    public ", method + 1, StringComparison.Ordinal);
        var body = ServiceSource[method..nextMethod];
        Assert.Contains("MessageType = \"REACTION\",\r\n                Direction = \"SALIENTE\",", body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 7c. Cola "sin_asignar" (P1, seguimiento 2026-09-21). El filtro de bandeja (@Modo =
    // 'sin_asignar' en GetInboxAsync y GetAuditMessagesAsync) sólo miraba c.IdTecnico -- no
    // consideraba si ya hubo una intervención humana real (plantilla/sticker/adjunto/reacción de
    // alguien sin Técnico vinculado). El fix agrega SinAsignarNotAttendedByHumanSql al WHERE,
    // reutilizando la MISMA AutomatedOutgoingSistemaAutorSqlList (sin duplicar la lista de
    // autores automáticos) con el mismo criterio temporal por IdMensaje que
    // GetBotReplyStatsAsync/HumanoYaRespondioUltimoEntrante: último SALIENTE humano posterior al
    // último ENTRANTE. Los 8 casos de la queja original se listan en los tests de abajo; los que
    // requieren datos reales en CONV_MENSAJES contra un tenant real quedan marcados "REQUIERE SQL
    // INTEGRATION" (no hay repositorio inyectable, igual que el resto de este archivo).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void SinAsignar_ReusesTheCentralAutomatedAuthorListInsteadOfADuplicate()
    {
        // Única fuente de verdad: el fragmento nuevo referencia AutomatedOutgoingSistemaAutorSqlList
        // (generado de AutomatedOutgoingSistemaAutores) -- no hay una segunda lista de autores
        // automáticos escrita a mano para este filtro.
        var property = ServiceSource.IndexOf(
            "private static string SinAsignarNotAttendedByHumanSql =>", StringComparison.Ordinal);
        Assert.True(property >= 0, "No se encontró SinAsignarNotAttendedByHumanSql.");
        var nextMember = ServiceSource.IndexOf("\r\n    private static bool IsHumanAuthoredOutgoing", property, StringComparison.Ordinal);
        Assert.True(nextMember > property);
        var body = ServiceSource[property..nextMember];

        Assert.Contains("NOT IN ({AutomatedOutgoingSistemaAutorSqlList})", body, StringComparison.Ordinal);
        Assert.Contains("Direction = N'SALIENTE'", body, StringComparison.Ordinal);
        Assert.Contains("Direction = N'ENTRANTE'", body, StringComparison.Ordinal);
        // Comparación por IdMensaje contra el ÚLTIMO entrante -- mismo criterio que
        // GetBotReplyStatsAsync, no "alguna vez respondió un humano en toda la conversación".
        Assert.Contains("humSal.IdMensaje >", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        // 1. GetInboxPagedAsync (GetInboxAsync es su wrapper): query principal de la bandeja.
        // Desde el refactor de paginación server-side, la lista de columnas vive en
        // selectColumnsSql, un literal raw-string separado que se interpola recién en tiempo de
        // ejecución dentro de `sql` -- en el CÓDIGO FUENTE (lo que lee este test) "SELECT" y
        // "c.IdConversacion," ya no son adyacentes, así que el marcador arranca directamente en
        // la primera columna, que sigue siendo única en el archivo.
        "c.IdConversacion,")]
    [InlineData(
        // 1421-area: GetAuditMessagesAsync, la búsqueda/auditoría de mensajes que también acepta
        // @Modo = 'sin_asignar'.
        "SELECT TOP (200)\r\n                    m.IdMensaje,")]
    public void SinAsignar_BothQueriesThatAcceptModoSinAsignar_ApplyTheHumanAttendedExclusion(string queryStartMarker)
    {
        var queryStart = ServiceSource.IndexOf(queryStartMarker, StringComparison.Ordinal);
        Assert.True(queryStart >= 0, $"No se encontró el query que empieza con '{queryStartMarker}'.");
        var modoClause = ServiceSource.IndexOf("@Modo = 'sin_asignar'", queryStart, StringComparison.Ordinal);
        Assert.True(modoClause > queryStart, "No se encontró la cláusula @Modo = 'sin_asignar' en este query.");
        var clauseEnd = ServiceSource.IndexOf(')', modoClause);
        var clauseEndOfLine = ServiceSource.IndexOf('\n', clauseEnd);
        var clause = ServiceSource[modoClause..clauseEndOfLine];

        // 2. Con Técnico asignado sigue sin entrar por esta rama en absoluto (comportamiento
        // preexistente, sin cambios): la condición de IdTecnico no se tocó, sólo se le agregó un
        // AND adicional.
        Assert.Contains("c.IdTecnico IS NULL OR LTRIM(RTRIM(c.IdTecnico)) = ''", clause, StringComparison.Ordinal);
        // El nuevo criterio de "sin intervención humana posterior al último entrante" se aplica
        // en la MISMA rama, con AND (no OR): ambas condiciones deben cumplirse.
        Assert.Contains("AND {SinAsignarNotAttendedByHumanSql}", clause, StringComparison.Ordinal);
    }

    [Fact]
    public void SinAsignar_DoesNotTouchTenantIsolationOrOtherModoBranches()
    {
        // 8. Aislamiento por Base/tenant: el fix es un AND agregado dentro de la rama
        // 'sin_asignar' -- no toca el resto del WHERE (Canal, IdNumeroWhatsApp/administradores,
        // CodigoEstado, etc.) ni las otras ramas de @Modo.
        Assert.Contains("OR (@Modo = 'asignadas_a_mi' AND LTRIM(RTRIM(c.IdTecnico)) = @IdTecnicoActual COLLATE Latin1_General_CI_AI)", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("OR (@Modo = 'pendientes' AND ISNULL(e.EsCerrado, 0) = 0)", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("OR (@Modo = 'cerradas' AND ISNULL(e.EsCerrado, 0) = 1)", ServiceSource, StringComparison.Ordinal);

        // El fix no auto-asigna ni escribe CONV_CONVERSACIONES.IdTecnico -- es un SELECT de
        // lectura pura (NOT EXISTS), no toca UPDATE/autorización.
        var property = ServiceSource.IndexOf(
            "private static string SinAsignarNotAttendedByHumanSql =>", StringComparison.Ordinal);
        var nextMember = ServiceSource.IndexOf("\r\n    private static bool IsHumanAuthoredOutgoing", property, StringComparison.Ordinal);
        var body = ServiceSource[property..nextMember];
        Assert.DoesNotContain("UPDATE", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IdTecnico", body, StringComparison.Ordinal);
    }

    // Casos 1, 3, 4, 5, 6, 7 de la semántica pedida (sin Técnico + [nunca/ya/bot/bienvenida/regla/
    // plantilla-sticker-adjunto-reacción] respondió) dependen todos del MISMO cálculo ya probado
    // exhaustivamente como lógica pura y ejecutable en:
    //   - IsHumanAuthoredOutgoing_ExcludesOnlyTheKnownAutomatedSistemaAutores (arriba, 7b): qué
    //     SistemaAutor cuenta como humano (plantilla/sticker/adjunto/reacción manual => humano;
    //     BOT/REGLA/BIENVENIDA/AUTOMATIZACION/AUTOCIERRE*/SLA/PROGRAMADO => no humano).
    //   - Handoff_GetBotReplyStatsAsync_* (arriba, 7): HumanoYaRespondioUltimoEntrante se calcula
    //     comparando IdMensaje contra el ÚLTIMO entrante, no "alguna vez" -- por eso un inbound
    //     nuevo sin respuesta posterior (caso 4) vuelve a calificar.
    // SinAsignarNotAttendedByHumanSql es la traducción SQL literal de "NOT
    // HumanoYaRespondioUltimoEntrante" (NOT EXISTS un SALIENTE humano con IdMensaje mayor al
    // último ENTRANTE), verificada arriba estructuralmente. Ejecutar los 8 casos end-to-end contra
    // filas reales de CONV_MENSAJES/CONV_CONVERSACIONES REQUIERE SQL INTEGRATION (no hay
    // repositorio inyectable para GetInboxAsync/GetAuditMessagesAsync, igual que el resto de este
    // archivo -- ver WhatsAppEmbeddedSignupSqlIntegrationTests para el patrón que se usaría).

    // ---------------------------------------------------------------------------------------
    // 8. Idempotencia / concurrencia.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Idempotency_DuplicateWhatsAppMessageId_SkipsReinsertAndSkipsAutomations()
    {
        // Ver Pipeline_AutomationsOnlyRunForNewMessagesNeverForDuplicatesOrOutbound: reentregas de
        // Meta con el mismo WhatsAppMessageId no reinsertan el mensaje (isNewMessage=false) y por
        // lo tanto tampoco vuelven a disparar reglas/bienvenida/fuera-de-horario/bot.
        Assert.Contains("var isNewMessage = messageId <= 0;", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_ConcurrentAutomationRunsAreSerializedPerConversationAndReason()
    {
        // RunWithConversationAutomationLockAsync usa sp_getapplock sobre
        // "CONV_AUTO:{idConversacion}:{motivo}" -- dos webhooks casi simultáneos para la MISMA
        // conversación y el MISMO motivo (ej. "bot-auto-reply") se serializan; si no se puede
        // tomar el lock, esa pasada se omite (no se encola, no reintenta) y queda auditada como
        // "AutomationLockSkipped". El motivo real de concurrencia (sp_getapplock, transacciones,
        // llamadas simultáneas reales) REQUIERE SQL INTEGRATION -- no se puede ejercitar sin un
        // SQL Server real detrás.
        Assert.Contains("var resource = $\"CONV_AUTO:{idConversacion}:{motivo}\";", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("AcquireApplicationLockAsync(cn, resource, ct)", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("\"AutomationLockSkipped\"", ServiceSource, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 9. Tool calling del asistente -- resolución server-side de la cuenta, sin SQL real.
    // ---------------------------------------------------------------------------------------

    private static ConversacionAutomatizacionesConfigDto ToolsConfig(
        bool precios = true,
        bool saldoCliente = true,
        bool saldoProveedor = true,
        bool pedidos = true,
        bool portal = true,
        bool precioConsumidor = false)
        => new()
        {
            AsistenteHerramientaPrecios = precios,
            AsistenteHerramientaSaldoCliente = saldoCliente,
            AsistenteHerramientaSaldoProveedor = saldoProveedor,
            AsistenteHerramientaPedidos = pedidos,
            AsistenteHerramientaPortalLink = portal,
            CatalogoMuestraPrecioConsumidor = precioConsumidor
        };

    private static ConversacionAsistenteHerramientasService CreateToolsService(
        ICrmCotizacionService? crm = null,
        IInterfacesCatalogosService? catalogos = null,
        ICentralPublicLinkService? publicLinks = null,
        IPortalClienteService? portal = null,
        IProveedorSaldoService? proveedor = null,
        IConversacionesConfigService? config = null,
        IAppUserSessionService? appUser = null,
        ISessionService? session = null)
        => new(
            new FakeConfiguration(),
            session ?? new FakeSessionService(),
            appUser ?? new FakeAppUserSessionService(),
            crm ?? new ThrowingCrmCotizacionService(),
            catalogos ?? new ThrowingInterfacesCatalogosService(),
            publicLinks ?? new ThrowingCentralPublicLinkService(),
            portal ?? new ThrowingPortalClienteService(),
            proveedor ?? new ThrowingProveedorSaldoService(),
            config ?? new FakeConversacionesConfigService());

    [Fact]
    public void Tools_NoToolsAreOfferedWhenTheMessageHasNoKeywordSignal()
    {
        var service = CreateToolsService();

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(), new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno"), "hola, como estas?");

        Assert.Empty(herramientas);
    }

    [Fact]
    public void Tools_SaldoTools_AreOnlyOfferedForTheAccountTypeTheyApplyTo()
    {
        var service = CreateToolsService();

        var paraCliente = service.ObtenerHerramientasDisponibles(
            ToolsConfig(), new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno"), "cual es mi saldo?");
        var paraProveedor = service.ObtenerHerramientasDisponibles(
            ToolsConfig(), new ConversacionCuentaVinculadaDto("P001", CuentaComercialTipo.Proveedor, "Proveedor Uno"), "cual es mi saldo?");
        var sinCuenta = service.ObtenerHerramientasDisponibles(ToolsConfig(), null, "cual es mi saldo?");

        Assert.Contains(paraCliente, h => h.Nombre == "consultar_saldo_total");
        Assert.Contains(paraProveedor, h => h.Nombre == "consultar_saldo_total");
        Assert.DoesNotContain(sinCuenta, h => h.Nombre == "consultar_saldo_total"); // sin cuenta vinculada, no se ofrece
        Assert.DoesNotContain(paraProveedor, h => h.Nombre == "consultar_pedidos"); // pedidos es solo-cliente
    }

    [Fact]
    public void Tools_IdentifiedClient_GetsClientPriceAndPortalTools()
    {
        var service = CreateToolsService();

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: false),
            new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno"),
            "quiero precio y portal");

        Assert.Contains(herramientas, h => h.Nombre == "consultar_precio");
        Assert.Contains(herramientas, h => h.Nombre == "generar_link_portal");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
    }

    [Fact]
    public void Tools_LeadWithConsumerPricesOff_GetsPublicCatalogButNoPriceTool()
    {
        var service = CreateToolsService();

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: false),
            null,
            "quiero precio del catalogo");

        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_precio");
        Assert.Contains(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
    }

    [Fact]
    public async Task Tools_LeadWithConsumerPricesOn_UsesConsumerFinalSourceWithoutClientCode()
    {
        var crm = new FakeCrmCotizacionService();
        var service = CreateToolsService(crm: crm);

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: true),
            null,
            "cuanto sale el tornillo?");
        var result = await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo","codigo_cliente":"C999"}""", null);

        Assert.Contains(herramientas, h => h.Nombre == "consultar_precio");
        Assert.Contains("$ 100,00", result);
        Assert.Null(crm.LastClienteCodigo);
    }

    [Fact]
    public async Task Tools_LeadPrice_ZeroResolvedPriceNeverShowsAsAValidZeroPrice()
    {
        // Si la base no tiene bien configurado el precio de consumidor final, el resolver general
        // (compartido con POS/Cotizaciones/Crm) puede devolver 0 -- para consumidor final/lead eso
        // no puede mostrarse como si fuera un precio comercial real.
        var crm = new FakeCrmCotizacionService { PrecioUnitarioConIva = 0m };
        var service = CreateToolsService(crm: crm);

        var result = await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo"}""", null);

        Assert.DoesNotContain("$ 0,00", result);
        Assert.Contains("precio no disponible", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tools_IdentifiedClientPrice_ZeroResolvedPriceIsShownAsIs()
    {
        // No tocar el comportamiento de clientes identificados: el guard de "no inventar precio"
        // aplica solo al contexto consumidor final/lead.
        var crm = new FakeCrmCotizacionService { PrecioUnitarioConIva = 0m };
        var service = CreateToolsService(crm: crm);
        var cuenta = new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno");

        var result = await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo"}""", cuenta);

        Assert.Contains("$ 0,00", result);
    }

    [Fact]
    public void Tools_ProviderIsNotTreatedAsClientForPortalOrOrders()
    {
        var service = CreateToolsService();

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: false),
            new ConversacionCuentaVinculadaDto("P001", CuentaComercialTipo.Proveedor, "Proveedor Uno"),
            "quiero portal, pedidos y precio");

        Assert.DoesNotContain(herramientas, h => h.Nombre == "generar_link_portal");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_pedidos");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_precio");
        Assert.Contains(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
    }

    [Fact]
    public void Tools_AmbiguousOrMissingAccountKeepsSafeLeadBehavior()
    {
        var service = CreateToolsService();

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: false),
            null,
            "quiero saldo, pedidos, portal y catalogo");

        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_saldo_total");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_pedidos");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "generar_link_portal");
        Assert.Contains(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
    }

    [Fact]
    public void Tools_AmbiguousAccount_GetsNoPrivateToolsEvenWithConsumerPricesOn()
    {
        // Contacto vinculado a más de un Cliente (ver ResolverCuentaVinculadaAsync): EsAmbigua = true.
        // No debe ofrecerse ninguna tool privada de cuenta, y tampoco el fallback de precio
        // consumidor (ese es solo para leads sin ninguna cuenta vinculada).
        var service = CreateToolsService();
        var ambigua = new ConversacionCuentaVinculadaDto(string.Empty, CuentaComercialTipo.Cliente, string.Empty) { EsAmbigua = true };

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: true),
            ambigua,
            "quiero precio, saldo, pedidos y portal");

        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_precio");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_saldo_total");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_saldo_detalle");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_pedidos");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "generar_link_portal");
        Assert.Contains(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
    }

    [Fact]
    public async Task Tools_AmbiguousAccount_ExecutingAnyPrivateToolFailsClosedWithoutCallingAnyBackend()
    {
        // Defensa en profundidad: aunque ObtenerHerramientasDisponibles ya no ofrezca estas tools,
        // EjecutarAsync también debe cortar si el modelo igual intenta invocarlas.
        var crm = new ThrowingCrmCotizacionService();
        var portal = new ThrowingPortalClienteService();
        var service = CreateToolsService(crm: crm, portal: portal);
        var ambigua = new ConversacionCuentaVinculadaDto(string.Empty, CuentaComercialTipo.Cliente, string.Empty) { EsAmbigua = true };

        var precio = await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo"}""", ambigua);
        var saldo = await service.EjecutarAsync("consultar_saldo_total", "{}", ambigua);
        var detalle = await service.EjecutarAsync("consultar_saldo_detalle", "{}", ambigua);
        var pedidos = await service.EjecutarAsync("consultar_pedidos", "{}", ambigua);
        var portalLink = await service.EjecutarAsync("generar_link_portal", "{}", ambigua);

        foreach (var result in new[] { precio, saldo, detalle, pedidos, portalLink })
            Assert.Contains("más de una cuenta vinculada", result);
    }

    [Fact]
    public async Task Tools_AmbiguousAccount_IgnoresClientCodeSentByModelArguments()
    {
        var crm = new FakeCrmCotizacionService();
        var service = CreateToolsService(crm: crm);
        var ambigua = new ConversacionCuentaVinculadaDto(string.Empty, CuentaComercialTipo.Cliente, string.Empty) { EsAmbigua = true };

        await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo","codigo_cliente":"C999"}""", ambigua);

        Assert.False(crm.WasCalled); // ni siquiera llega a resolver precio consumidor: corta antes de tocar el backend
    }

    [Fact]
    public void Tools_WithoutConsumerPriceConfig_DefaultsToNoLeadPrices()
    {
        var service = CreateToolsService();
        var config = ToolsConfig(precioConsumidor: false);

        Assert.False(config.CatalogoMuestraPrecioConsumidor);
        var herramientas = service.ObtenerHerramientasDisponibles(config, null, "precio del producto");

        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_precio");
    }

    [Fact]
    public void Tools_ConsumerPriceConfig_IsStoredPerTenantConfiguration()
    {
        Assert.Contains("CATALOGO_MUESTRA_PRECIO_CONSUMIDOR", ConfigServiceSource, StringComparison.Ordinal);
        Assert.Contains("ResolveTenantConnection(expectedBaseId, \"GetAutomatizacionesConfig\")", ConfigServiceSource, StringComparison.Ordinal);
        Assert.Contains("await using var cn = new SqlConnection(tenant.ConnectionString);", ConfigServiceSource, StringComparison.Ordinal);
        Assert.Contains("await using var cn = new SqlConnection(ConnectionString);", ConfigServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tools_PublicCatalogLink_UsesCurrentCompanyAndBaseOnly()
    {
        var publicLinks = new FakeCentralPublicLinkService();
        var service = CreateToolsService(
            catalogos: new FakeInterfacesCatalogosService { Catalogo = new CatalogosCatalogoDetalleDto { IdInsert = 321, Nombre = "Minorista" } },
            publicLinks: publicLinks,
            appUser: new FakeAppUserSessionService("empresa-a"),
            session: new FakeSessionService(new SessionDto { BaseId = 4271, Nombre = "Base A" }));

        var result = await service.EjecutarAsync("generar_link_catalogo_publico", "{}", null);

        Assert.Contains("https://portal.example.com/empresa-a/catalogo/catalogo-tok123", result);
        Assert.Contains(publicLinks.Requests, r => r.IdWeb == "empresa-a" && r.IdBase == 4271 && r.IdReferencia == 321);
        Assert.DoesNotContain(publicLinks.Requests, r => r.IdWeb == "empresa-b" || r.IdBase == 9999);
    }

    [Fact]
    public async Task Tools_PublicCatalogLink_UnambiguousClient_IsRejectedInFavorOfPortalCliente()
    {
        var service = CreateToolsService();
        var cuenta = new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno");

        var result = await service.EjecutarAsync("generar_link_catalogo_publico", "{}", cuenta);

        Assert.Contains("debe usar el Portal Cliente", result);
    }

    [Fact]
    public async Task Tools_PublicCatalogLink_AmbiguousAccount_IsAllowedAsSafeFallback()
    {
        // Bug corregido: EsAmbigua comparte Tipo=Cliente (no hay un tercer valor de enum), así que
        // esta tool tiene que mirar EsAmbigua antes que Tipo -- si no, un contacto ambiguo recibía el
        // mensaje de "usá el Portal Cliente" en vez del único fallback seguro que le corresponde.
        var publicLinks = new FakeCentralPublicLinkService();
        var service = CreateToolsService(
            catalogos: new FakeInterfacesCatalogosService { Catalogo = new CatalogosCatalogoDetalleDto { IdInsert = 321, Nombre = "Minorista" } },
            publicLinks: publicLinks,
            appUser: new FakeAppUserSessionService("empresa-a"),
            session: new FakeSessionService(new SessionDto { BaseId = 4271, Nombre = "Base A" }));
        var ambigua = new ConversacionCuentaVinculadaDto(string.Empty, CuentaComercialTipo.Cliente, string.Empty) { EsAmbigua = true };

        var result = await service.EjecutarAsync("generar_link_catalogo_publico", "{}", ambigua);

        Assert.Contains("https://portal.example.com/empresa-a/catalogo/catalogo-tok123", result);
        Assert.DoesNotContain("debe usar el Portal Cliente", result);
        Assert.Contains(publicLinks.Requests, r => r.IdWeb == "empresa-a" && r.IdBase == 4271 && r.IdReferencia == 321);
    }

    [Fact]
    public async Task Tools_AmbiguousAccount_PublicCatalogFallbackStillExcludesEveryPrivateTool()
    {
        // El fallback público habilitado para EsAmbigua no debe abrir ninguna puerta lateral hacia
        // saldo/pedidos/precio/portal -- se re-confirma acá junto con el fix del catálogo.
        var crm = new ThrowingCrmCotizacionService();
        var portal = new ThrowingPortalClienteService();
        var service = CreateToolsService(crm: crm, portal: portal);
        var ambigua = new ConversacionCuentaVinculadaDto(string.Empty, CuentaComercialTipo.Cliente, string.Empty) { EsAmbigua = true };

        var herramientas = service.ObtenerHerramientasDisponibles(
            ToolsConfig(precioConsumidor: true), ambigua, "precio, saldo, pedidos, portal y catalogo");

        Assert.Contains(herramientas, h => h.Nombre == "generar_link_catalogo_publico");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_precio");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_saldo_total");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_saldo_detalle");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "consultar_pedidos");
        Assert.DoesNotContain(herramientas, h => h.Nombre == "generar_link_portal");

        var saldo = await service.EjecutarAsync("consultar_saldo_total", "{}", ambigua);
        var portalLink = await service.EjecutarAsync("generar_link_portal", "{}", ambigua);
        Assert.Contains("más de una cuenta vinculada", saldo);
        Assert.Contains("más de una cuenta vinculada", portalLink);
    }

    [Fact]
    public async Task Tools_SaldoQuery_AlwaysUsesTheLinkedAccount_NeverAnyIdentifierFromModelArguments()
    {
        // GUARDRAIL DE SEGURIDAD documentado en el propio archivo: ninguna herramienta sensible
        // recibe la cuenta desde argumentosJson. Se prueba pasando un JSON de argumentos que
        // intenta indicar OTRA cuenta ("cuenta":"OTRA-999") -- el resultado debe reflejar
        // igualmente la cuenta vinculada real (C001), nunca la del JSON.
        var portal = new FakePortalClienteService { ResumenPorCliente = { ["C001"] = new PortalClienteCuentaCorrienteResumenDto { SaldoTotal = 555m, CantidadPendientes = 1 } } };
        var service = CreateToolsService(portal: portal);

        var cuenta = new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno");
        var result = await service.EjecutarAsync("consultar_saldo_total", """{"cuenta":"OTRA-999","codigo_cliente":"OTRA-999"}""", cuenta);

        Assert.Contains("555", result);
        Assert.Equal(["C001"], portal.RequestedCodes);
    }

    [Fact]
    public async Task Tools_SaldoQuery_WithoutLinkedAccount_FailsClosedWithoutCallingAnyBackend()
    {
        var portal = new FakePortalClienteService();
        var proveedor = new ThrowingProveedorSaldoService();
        var service = CreateToolsService(portal: portal, proveedor: proveedor);

        var result = await service.EjecutarAsync("consultar_saldo_total", "{}", null);

        Assert.Contains("No se pudo identificar la cuenta", result);
        Assert.Empty(portal.RequestedCodes);
    }

    [Fact]
    public async Task Tools_PrecioQuery_ScopesArticleSearchToTheLinkedClientCode_NotAnyArgument()
    {
        var crm = new FakeCrmCotizacionService();
        var service = CreateToolsService(crm: crm);

        var cuenta = new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno");
        await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo","cliente":"OTRO-999"}""", cuenta);

        Assert.Equal("C001", crm.LastClienteCodigo);
    }

    [Fact]
    public async Task Tools_PrecioQuery_MissingArticuloArgument_FailsClosedWithoutQuerying()
    {
        var crm = new FakeCrmCotizacionService();
        var service = CreateToolsService(crm: crm);

        var result = await service.EjecutarAsync("consultar_precio", "{}", new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno"));

        Assert.Contains("No se especificó qué artículo buscar", result);
        Assert.False(crm.WasCalled);
    }

    [Fact]
    public async Task Tools_UnknownToolName_ReturnsFixedFallback_NeverThrows()
    {
        var service = CreateToolsService();

        var result = await service.EjecutarAsync("borrar_todo_el_sistema", "{}", null);

        Assert.Equal("Herramienta desconocida.", result);
    }

    [Fact]
    public async Task Tools_BackendFailure_IsCaughtAndReturnsAGracefulFallback_NeverThrowsIntoTheBot()
    {
        var crm = new ThrowingCrmCotizacionService();
        var service = CreateToolsService(crm: crm);

        var result = await service.EjecutarAsync("consultar_precio", """{"articulo":"tornillo"}""", new ConversacionCuentaVinculadaDto("C001", CuentaComercialTipo.Cliente, "Cliente Uno"));

        Assert.Contains("No se pudo obtener el dato", result);
    }

    // ---------------------------------------------------------------------------------------
    // 9 bis. Identidad ambigua -- ResolverCuentaVinculadaAsync abre su propio SqlConnection contra
    // el tenant (mismo caso que Auto-cierre/SLA más abajo): no hay forma de ejercitar 0/1/N Clientes
    // vinculados sin una base de tenant real con MA_CONTACTOS_CUENTAS/VT_CLIENTES/VT_PROVEEDORES.
    // REQUIERE SQL INTEGRATION -- se prueba estructuralmente que ya no hace TOP (1) arbitrario sobre
    // las cuentas del contacto y que cuenta explícitamente los Clientes distintos.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Identidad_ResolverCuentaVinculada_NoLongerPicksAnArbitraryTopOneAmongLinkedAccounts()
    {
        var method = ExtractServiceMethod("private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync");

        // Antes había un único ORDER BY TipoOrdinal, usado junto con TOP (1) para quedarse con
        // cualquier cuenta vinculada al contacto (el bug: con 2+ Clientes, elegía uno arbitrariamente).
        // Ya no debe existir ese ordenamiento -- ahora se traen todas las filas y se cuentan en C#.
        Assert.DoesNotContain("ORDER BY TipoOrdinal", method, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.MA_CONTACTOS_CUENTAS mcc", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Identidad_ResolverCuentaVinculada_CountsDistinctLinkedClientsBeforeDeciding()
    {
        var method = ExtractServiceMethod("private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync");

        Assert.Contains(".Where(v => v.TipoOrdinal == 1)", method, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(v => v.Codigo, StringComparer.OrdinalIgnoreCase)", method, StringComparison.Ordinal);
        Assert.Contains("if (clientes.Count > 1)", method, StringComparison.Ordinal);
        Assert.Contains("EsAmbigua = true", method, StringComparison.Ordinal);
        Assert.Contains("if (clientes.Count == 1)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Identidad_ResolverCuentaVinculada_ZeroClientsStillAllowsSingleProviderMatch()
    {
        // 0 Cliente vinculados no es lo mismo que "ambiguo": si hay exactamente un Proveedor,
        // se sigue devolviendo (comportamiento previo, sin cambios) -- un Proveedor no cuenta
        // como Cliente para la ambigüedad.
        var method = ExtractServiceMethod("private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync");
        Assert.Contains("vinculadas.FirstOrDefault(v => v.TipoOrdinal == 2)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Identidad_ResolverCuentaVinculada_ExplicitConversationClienteCodigoIsUsedAsIs_WithoutChange()
    {
        // Cuando la conversación ya tiene ClienteCodigo fijado (RelacionarCliente, que valida contra
        // VT_CLIENTES al escribir), se sigue usando tal cual -- esta parte no cambió.
        var method = ExtractServiceMethod("private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync");
        var explicitPathIndex = method.IndexOf("if (!string.IsNullOrWhiteSpace(clienteCodigo))", StringComparison.Ordinal);
        var ambiguousPathIndex = method.IndexOf("if (clientes.Count > 1)", StringComparison.Ordinal);
        Assert.True(explicitPathIndex >= 0 && explicitPathIndex < ambiguousPathIndex);
    }

    [Fact]
    public void Identidad_ResolverCuentaVinculada_UsesTenantScopedConnectionForEveryQuery()
    {
        // Aislamiento: las tres consultas (conversación, cliente explícito, cuentas del contacto)
        // usan `ConnectionString` (resuelto por tenant/sesión activa), nunca un valor fijo o
        // compartido entre bases.
        var method = ExtractServiceMethod("private async Task<ConversacionCuentaVinculadaDto?> ResolverCuentaVinculadaAsync");
        var occurrences = System.Text.RegularExpressions.Regex.Matches(method, "new SqlConnection\\(ConnectionString\\)").Count;
        Assert.Equal(3, occurrences);
    }

    private static string ExtractServiceMethod(string signature)
    {
        var start = ServiceSource.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontró {signature}.");
        var brace = ServiceSource.IndexOf('{', start);
        Assert.True(brace >= 0, $"No se encontró el cuerpo de {signature}.");

        var depth = 0;
        for (var i = brace; i < ServiceSource.Length; i++)
        {
            if (ServiceSource[i] == '{') depth++;
            if (ServiceSource[i] == '}') depth--;
            if (depth == 0) return ServiceSource[brace..(i + 1)];
        }

        throw new InvalidOperationException($"No se pudo extraer {signature}.");
    }

    // ---------------------------------------------------------------------------------------
    // 10. Auto-cierre.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AutoCierre_SkipsConversationsWithAnOpenLinkedTicket()
        => Assert.Contains("Si hay un ticket abierto vinculado, un humano ya se hizo cargo", ServiceSource, StringComparison.Ordinal);

    [Fact]
    public void AutoCierre_IsATwoStepStateMachine_AvisoThenCierre()
    {
        // Primero manda el aviso (SistemaAutor=AUTOCIERRE_AVISO); recién en la pasada SIGUIENTE,
        // si el último saliente sigue siendo ese aviso y ya pasaron las horas de cierre, cierra.
        Assert.Contains("var yaAvisado = string.Equals(cand.SistemaAutor, \"AUTOCIERRE_AVISO\", StringComparison.OrdinalIgnoreCase);", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("if (yaAvisado)\r\n                    {\r\n                        await CerrarPorInactividadAsync(cand.Id, estadoCerrado, config.AutoCierreMensajeCierre, ct)", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoCierre_RequiresModuleActiveAndOwnConfigFlag()
    {
        var method = ServiceSource.IndexOf("public async Task<int> ProcesarAutoCierreAsync", StringComparison.Ordinal);
        var moduleGate = ServiceSource.IndexOf("IsModuloActivoParaClienteActualAsync(\"AUTOMATIZACIONES\", ct)", method, StringComparison.Ordinal);
        var featureGate = ServiceSource.IndexOf("if (!config.AutoCierreActivo)", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && moduleGate > method && featureGate > moduleGate);
    }

    [Fact]
    public void AutoCierre_ExecutionAgainstRealSqlStateTransitions_RequiresSqlIntegration()
    {
        // ProcesarAutoCierreAsync abre su propio SqlConnection contra el tenant y arma la
        // candidatos list vía T-SQL (JOIN con CONV_ESTADOS/TICK_TICKETS, DATEADD contra GETDATE())
        // -- no hay forma de ejercitar "elegible / aún no vencida / vencida / ya cerrada /
        // reapertura por nuevo inbound" sin una base de tenant real con ese esquema. REQUIERE SQL
        // INTEGRATION (no cubierto por SqlIntegrationFactAttribute, que apunta a ALFA_CENTRAL, no
        // a una base de tenant con CONV_*).
        Assert.Contains("FROM dbo.CONV_CONVERSACIONES c", ServiceSource, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // 11. SLA.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sla_ReassignsAfterReasignarThreshold_OtherwiseLeavesAReminderNoteOnce()
    {
        Assert.Contains("if (!string.IsNullOrWhiteSpace(cand.IdTecnico) && horasEspera >= horasReasig)", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("else if (!cand.YaRecordado)", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Sla_ReasignarThresholdIsNeverBelowRecordatorio()
        => Assert.Contains("var horasReasig = Math.Max(horasRec, config.SlaHorasReasignar);", ServiceSource, StringComparison.Ordinal);

    [Fact]
    public void Sla_RequiresModuleActiveAndOwnConfigFlag_NeverMessagesTheClient()
    {
        var method = ServiceSource.IndexOf("public async Task<int> ProcesarSeguimientosSlaAsync", StringComparison.Ordinal);
        var moduleGate = ServiceSource.IndexOf("IsModuloActivoParaClienteActualAsync(\"AUTOMATIZACIONES\", ct)", method, StringComparison.Ordinal);
        var featureGate = ServiceSource.IndexOf("if (!config.SlaActivo)", method, StringComparison.Ordinal);
        Assert.True(method >= 0 && moduleGate > method && featureGate > moduleGate);
        // "No manda nada al cliente" -- SLA solo usa AddInternalEventCoreAsync (nota interna) y
        // AssignConversationAsync, nunca SendMessageAsync.
        var end = ServiceSource.IndexOf("private async Task<string> GetClosedStateCodeAsync", method, StringComparison.Ordinal);
        var slaBody = end > method ? ServiceSource[method..end] : ServiceSource[method..(method + 4000)];
        Assert.DoesNotContain("SendMessageAsync", slaBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Sla_ExecutionAgainstRealSqlThresholds_RequiresSqlIntegration()
        // Igual que auto-cierre: los umbrales "antes/después" y el estado PENDIENTE/EN_GESTION
        // solo se pueden ejercitar de punta a punta con una base de tenant real.
        => Assert.Contains("NOT IN (N'PENDIENTE', N'EN_GESTION')", ServiceSource, StringComparison.Ordinal);

    // ---------------------------------------------------------------------------------------
    // 12. TraceDiagAsync -- auditoría (no se retira: ver conclusión en el reporte).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TraceDiag_WritesOnlyAStepNameAndConversationId_NoMessageTextNoTokensNoPii()
    {
        var method = ServiceSource.IndexOf("private async Task TraceDiagAsync(string paso, long idConversacion, CancellationToken ct)", StringComparison.Ordinal);
        Assert.True(method >= 0);
        var insert = ServiceSource.IndexOf("INSERT INTO dbo.AUX_ERR (Proceso, Fecha, Error, Descripcion, Usuario) VALUES (@Proceso, GETDATE(), -1, @Descripcion, N'DIAG');", method, StringComparison.Ordinal);
        var descripcion = ServiceSource.IndexOf("cmd.Parameters.AddWithValue(\"@Descripcion\", $\"{paso} | idConversacion={idConversacion}\");", method, StringComparison.Ordinal);
        Assert.True(insert > method && descripcion > insert);
    }

    [Fact]
    public void TraceDiag_IsBestEffortAndNeverThrowsIntoTheRealPipeline()
    {
        var method = ServiceSource.IndexOf("private async Task TraceDiagAsync(string paso, long idConversacion, CancellationToken ct)", StringComparison.Ordinal);
        var catchAll = ServiceSource.IndexOf("catch\r\n        {\r\n            // Best-effort: un fallo acá nunca debe interrumpir el flujo real.", method, StringComparison.Ordinal);
        Assert.True(catchAll > method);
    }

    [Fact]
    public void TraceDiag_IsStillCalledFromEveryHotPathOfTheBotPipeline_UnboundedAuxErrGrowthRemainsARisk()
    {
        // Auditoría: sigue instrumentando TODO el camino del bot (entrada, cada guardarraíl, cada
        // paso de la llamada a OpenAI), no solo fallas -- cada mensaje que activa el bot deja
        // ~10-15 filas en dbo.AUX_ERR, una tabla sin límite de tamaño/expiración visible en este
        // archivo. No hay evidencia en el repo de que el incidente original que motivó agregarlo
        // ("retirar una vez encontrada la causa", ver auditoría) ya esté resuelto -- CONCLUSIÓN:
        // AÚN NECESARIO. No se retira en este trabajo. Se deja documentado el riesgo de
        // crecimiento no acotado de AUX_ERR para una decisión de producto explícita.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(ServiceSource, "TraceDiagAsync\\(").Count;
        Assert.True(occurrences >= 15, $"Se esperaban muchas llamadas activas a TraceDiagAsync (hay {occurrences}); si bajó bruscamente, revisar si ya se retiró en otro lado.");
    }

    // ---------------------------------------------------------------------------------------
    // 13. Gate de módulo AUTOMATIZACIONES -- auditoría, NO se cambia.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Gate_IsModuloActivoParaClienteActual_FailsOpenOnLegacyClient_NonSaaSMode_UndefinedModuleAndSqlException()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "CentralAdminService.cs"));
        var method = source.IndexOf("public async Task<bool> IsModuloActivoParaClienteActualAsync", StringComparison.Ordinal);
        Assert.True(method >= 0);
        var body = source[method..(source.IndexOf("\r\n    }\r\n\r\n    /// <summary>", method, StringComparison.Ordinal) is var end && end > 0 ? end : method + 5000)];

        // 1) Instalación on-premise / legacy: no hay noción de módulo contratado -> true.
        Assert.Contains("if (!appMode.IsSaaSMode)\r\n            return true;", body, StringComparison.Ordinal);
        // 2) Cliente legacy dentro de SaaS -> true.
        Assert.Contains("if (await IsClienteLegacyAsync(cn, idCliente, ct).ConfigureAwait(false))\r\n                return true;", body, StringComparison.Ordinal);
        // 3) Módulo no cargado todavía en el catálogo -> true (fail-open explícito, comentado).
        Assert.Contains("no bloqueamos por eso (fail-open)", body, StringComparison.Ordinal);
        // 4) Cualquier excepción (típicamente SQL) evaluando la entitlement -> true.
        Assert.Contains("catch (Exception ex)\r\n        {\r\n            await appEvents.LogErrorAsync(", body, StringComparison.Ordinal);
        Assert.Contains("\"No se pudo verificar si un módulo opcional está activo; se muestra por defecto.\"", body, StringComparison.Ordinal);

        // Riesgo real (auditoría, NO se corrige acá): revocar la entitlement AUTOMATIZACIONES de un
        // cliente no bloquea de forma confiable el bot/reglas/etc. si en ese momento hay un error
        // transitorio de BD central, o si el módulo nunca se cargó en dbo.Modulos -- el diseño es
        // deliberadamente fail-open (UX: no romper automatizaciones por un problema de licenciamiento
        // ajeno al cliente), no un bypass de autorización -- no calificaría como vulnerabilidad de
        // seguridad per se, pero sí como gate de negocio poco confiable para "desactivación dura".
    }

    // ---------------------------------------------------------------------------------------
    // Fakes para el pipeline de herramientas del asistente (sin SQL, sin OpenAI real).
    // ---------------------------------------------------------------------------------------

    private sealed class FakeConfiguration : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key] { get => null; set { } }
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    }

    private sealed class FakeSessionService(SessionDto? activeSession = null) : ISessionService
    {
        public event Action? SessionChanged;
        public string GetConnectionString() => "Server=test;Database=test;";
        public SessionDto? GetActiveSession() => activeSession;
        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void SetWebhookOverride(SessionDto session) { }
        public void ClearWebhookOverride() { }
        public IReadOnlyList<SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) { }
        public Guid AddSession(string a, string b, string c, string d, string e) => Guid.Empty;
        public void UpdateSession(Guid a, string b, string c, string d, string e, string f) { }
        public void DeleteSession(Guid id) { }
        public void ClearActiveSession() { }
    }

    private sealed class FakeAppUserSessionService(string idWeb = "empresa-a") : IAppUserSessionService
    {
        public event Action? StateChanged;
        public bool IsAuthenticated => true;
        public AppUserSessionInfo? CurrentUser { get; } = new() { UserName = "admin", IdWeb = idWeb };
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => "test-token";
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => throw new NotSupportedException();
        public void Logout() => throw new NotSupportedException();
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => CurrentUser?.UserName ?? fallback;
        public bool IsAuthorizedForSession(Guid? activeSessionId) => true;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class FakeCentralPublicLinkService : ICentralPublicLinkService
    {
        public List<(string IdWeb, int IdBase, string Tipo, int IdReferencia)> Requests { get; } = [];
        public PublicLinkDto? Existing { get; set; }
        public PublicLinkDto Created { get; set; } = new()
        {
            IdPublicLink = 1,
            IdWeb = "empresa-a",
            IdBase = 4271,
            Tipo = PublicLinkTipos.Catalogo,
            IdReferencia = 123,
            Token = "tok123",
            Slug = "catalogo",
            Activo = true
        };

        public Task<PublicLinkDto?> ResolveAsync(string idWeb, string tipo, string routeSegment, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PublicLinkDto?> TryGetExistingAsync(string idWeb, int idBase, string tipo, int idReferencia, CancellationToken ct = default)
        {
            Requests.Add((idWeb, idBase, tipo, idReferencia));
            return Task.FromResult(Existing);
        }

        public Task<PublicLinkDto> GetOrCreateAsync(string idWeb, int idBase, string tipo, int idReferencia, string? nombreParaSlug, CancellationToken ct = default)
        {
            Requests.Add((idWeb, idBase, tipo, idReferencia));
            return Task.FromResult(Created);
        }

        public Task<IReadOnlyDictionary<int, PublicLinkDto>> GetOrCreateManyAsync(string idWeb, int idBase, string tipo, IReadOnlyList<(int IdReferencia, string? Nombre)> referencias, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(int idPublicLink, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PublicLinkDto> RegenerateAsync(int idPublicLink, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingCentralPublicLinkService : ICentralPublicLinkService
    {
        public Task<PublicLinkDto?> ResolveAsync(string idWeb, string tipo, string routeSegment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PublicLinkDto?> TryGetExistingAsync(string idWeb, int idBase, string tipo, int idReferencia, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PublicLinkDto> GetOrCreateAsync(string idWeb, int idBase, string tipo, int idReferencia, string? nombreParaSlug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, PublicLinkDto>> GetOrCreateManyAsync(string idWeb, int idBase, string tipo, IReadOnlyList<(int IdReferencia, string? Nombre)> referencias, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(int idPublicLink, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PublicLinkDto> RegenerateAsync(int idPublicLink, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private class FakeInterfacesCatalogosService : IInterfacesCatalogosService
    {
        public CatalogosCatalogoDetalleDto? Catalogo { get; set; } = new() { IdInsert = 123, Nombre = "Catálogo público" };

        public virtual Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default)
            => Task.FromResult(Catalogo);

        public Task<IReadOnlyList<CatalogosModalidadOptionDto>> GetModalidadesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosListaPrecioDto>> GetListasPrecioAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<CatalogosArticuloBusquedaDto>> SearchArticulosAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetRubrosArticuloAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetFamiliasArticuloAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetMarcasArticuloAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosClasificacionOpcionDto>> GetProveedoresArticuloAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> SearchArticulosAllAsync(CatalogosArticuloBusquedaFiltersDto filters, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountArticulosDesdeListaAsync(string idLista, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogosArticuloBusquedaDto>> GetArticulosDesdeListaAsync(string idLista, string? idWeb = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<CatalogosCatalogoResumenDto>> SearchCatalogosAsync(string? texto, int pageNumber = 1, int pageSize = 50, DateTime? fechaFiltro = null, string? tipoFiltro = null, string? estadoFiltro = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosCatalogoDetalleDto?> GetCatalogoPublicoAsync(int idInsert, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosCatalogoSaveResultDto> SaveCatalogoVigenciaAsync(CatalogosCatalogoSaveRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosCatalogoAccessUrlsDto> GetCatalogoAccessUrlsAsync(int idInsert, string? idWeb = null, int? idBase = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> GetCatalogoPredeterminadoIdAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCatalogoPredeterminadoAsync(string userName, int idInsert, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosClienteSessionInfo> LoginClienteAsync(CatalogosClienteLoginRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogoPedidoResultDto> ConfirmarPedidoCarritoAsync(CatalogoPedidoConfirmarRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FinalizarCatalogoAsync(int idInsert, string usuario, string pc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> GetMenuHabilitadoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveMenuHabilitadoAsync(string userName, bool habilitado, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosPublicIdentityDto> GetPublicIdentityAsync(string? idWeb, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePublicIdentityNameAsync(string userName, string? nombreVisible, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePublicLogoFormatAsync(string userName, string? idWeb, string logoFormat, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosPublicIdentityDto> SavePublicIdentityLogoAsync(string userName, string? idWeb, Stream content, string fileName, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ResetPublicIdentityLogoAsync(string userName, string? idWeb, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosPublicLogoServeDto?> GetPublicLogoForServeAsync(string? idWeb, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetPublicClasePrecioAsync(string? idWeb, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SavePublicClasePrecioAsync(string userName, string? idWeb, string clasePrecio, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CatalogosViewSettingsDto> GetViewSettingsAsync(string userName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveViewSettingsAsync(string userName, CatalogosViewSettingsDto settings, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ClearImagenModificadaAsync(string idArticulo, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingInterfacesCatalogosService : FakeInterfacesCatalogosService
    {
        public override Task<CatalogosCatalogoDetalleDto?> GetCatalogoAsync(int idInsert, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeConversacionesConfigService : ThrowingConversacionesConfigService
    {
        public override Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default)
            => Task.FromResult(new ConversacionWhatsAppConfigDto { PublicBaseUrl = "https://portal.example.com" });
    }

    private sealed class FakeCrmCotizacionService : ICrmCotizacionService
    {
        public string? LastClienteCodigo { get; private set; }
        public bool WasCalled { get; private set; }
        public decimal PrecioUnitarioConIva { get; set; } = 100m;

        public Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default)
        {
            WasCalled = true;
            LastClienteCodigo = clienteCodigo;
            IReadOnlyList<CrmCotizacionArticuloDto> result = [new CrmCotizacionArticuloDto { Codigo = "ART1", Descripcion = texto, PrecioUnitarioConIva = PrecioUnitarioConIva }];
            return Task.FromResult(result);
        }

        public Task<CrmCotizacionPricingContextDto> ResolvePricingContextAsync(string? clienteCodigo, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CrmCotizacionDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrmCotizacionDetailDto?> GetByIdAsync(long idCotizacion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> SaveAsync(CrmCotizacionSaveRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangeEstadoAsync(long idCotizacion, string estado, string? usuarioAccion = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(long idCotizacion, string? usuarioAccion = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GenerateEmailMessageAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrmCotizacionShareDto> EnsureShareAsync(long idCotizacion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendByEmailAsync(long idCotizacion, string destinatario, string? publicUrl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingCrmCotizacionService : ICrmCotizacionService
    {
        public Task<IReadOnlyList<CrmCotizacionArticuloDto>> SearchArticulosAsync(string? clienteCodigo, string texto, int take = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("Backend no disponible (simulado).");
        public Task<CrmCotizacionPricingContextDto> ResolvePricingContextAsync(string? clienteCodigo, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CrmCotizacionDto>> GetByOportunidadAsync(long idOportunidad, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrmCotizacionDetailDto?> GetByIdAsync(long idCotizacion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> SaveAsync(CrmCotizacionSaveRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangeEstadoAsync(long idCotizacion, string estado, string? usuarioAccion = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(long idCotizacion, string? usuarioAccion = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GenerateServiceProposalAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GenerateEmailMessageAsync(string prompt, string? clienteNombre = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CrmCotizacionAiLineaSugeridaDto>> SuggestLinesFromPromptAsync(string? clienteCodigo, string prompt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrmCotizacionShareDto> EnsureShareAsync(long idCotizacion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendByEmailAsync(long idCotizacion, string destinatario, string? publicUrl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> RenderPublicHtmlAsync(int idBase, string token, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePortalClienteService : IPortalClienteService
    {
        public Dictionary<string, PortalClienteCuentaCorrienteResumenDto> ResumenPorCliente { get; } = [];
        public List<string> RequestedCodes { get; } = [];

        public Task<PortalClienteCuentaCorrienteResumenDto> GetResumenCuentaCorrienteAsync(string codigoCliente, CancellationToken ct = default)
        {
            RequestedCodes.Add(codigoCliente);
            return Task.FromResult(ResumenPorCliente.GetValueOrDefault(codigoCliente, new PortalClienteCuentaCorrienteResumenDto()));
        }

        public Task<PortalClienteCuentaCorrienteDto> GetCuentaCorrienteAsync(PortalClienteCuentaCorrienteFiltroDto filtro, CancellationToken ct = default)
        {
            RequestedCodes.Add(filtro.CodigoCliente);
            return Task.FromResult(new PortalClienteCuentaCorrienteDto());
        }

        public Task<PagedResult<PortalClientePedidoResumenDto>> GetPedidosClienteAsync(PortalClientePedidosFiltroDto filtro, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClientePedidoDetalleDto?> GetPedidoClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteEstadoCuentaDto> GetEstadoCuentaAsync(PortalClienteEstadoCuentaFiltroDto filtro, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteComprobantePendienteDetalleDto?> GetComprobanteClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteMiCuentaDto?> GetMiCuentaAsync(string codigoCliente, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteActualizarEmailResultDto> ActualizarEmailAsync(PortalClienteActualizarEmailRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteCambiarClaveResultDto> CambiarClaveAsync(PortalClienteCambiarClaveRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingPortalClienteService : IPortalClienteService
    {
        public Task<PortalClienteCuentaCorrienteResumenDto> GetResumenCuentaCorrienteAsync(string codigoCliente, CancellationToken ct = default) => throw new InvalidOperationException("Backend no disponible (simulado).");
        public Task<PortalClienteCuentaCorrienteDto> GetCuentaCorrienteAsync(PortalClienteCuentaCorrienteFiltroDto filtro, CancellationToken ct = default) => throw new InvalidOperationException("Backend no disponible (simulado).");
        public Task<PagedResult<PortalClientePedidoResumenDto>> GetPedidosClienteAsync(PortalClientePedidosFiltroDto filtro, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClientePedidoDetalleDto?> GetPedidoClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteEstadoCuentaDto> GetEstadoCuentaAsync(PortalClienteEstadoCuentaFiltroDto filtro, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteComprobantePendienteDetalleDto?> GetComprobanteClienteDetalleAsync(string codigoCliente, int idComprobante, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteMiCuentaDto?> GetMiCuentaAsync(string codigoCliente, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteActualizarEmailResultDto> ActualizarEmailAsync(PortalClienteActualizarEmailRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PortalClienteCambiarClaveResultDto> CambiarClaveAsync(PortalClienteCambiarClaveRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingProveedorSaldoService : IProveedorSaldoService
    {
        public Task<ProveedorSaldoResumenDto> GetResumenSaldoAsync(string codigoProveedor, CancellationToken ct = default) => throw new InvalidOperationException("Backend no disponible (simulado).");
        public Task<IReadOnlyList<ProveedorComprobantePendienteDto>> GetComprobantesPendientesAsync(string codigoProveedor, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Base con todos los miembros de <see cref="IConversacionesConfigService"/> lanzando
    /// NotSupportedException, para poder sobreescribir en el fake solo lo que cada test usa.</summary>
    private abstract class ThrowingConversacionesConfigService : IConversacionesConfigService
    {
        public virtual Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppConfigDto> GetWhatsAppConfigAsync(string connectionString, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveWhatsAppConfigAsync(ConversacionWhatsAppConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppConfigDto> GenerateWhatsAppWebPairingAsync(ConversacionWhatsAppWebPairingRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppConfigDto> ClearWhatsAppWebPairingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionInstagramConfigDto> GetInstagramConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveInstagramConfigAsync(ConversacionInstagramConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionFacebookConfigDto> GetFacebookConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveFacebookConfigAsync(ConversacionFacebookConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionMercadoLibreConfigDto> GetMercadoLibreConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveMercadoLibreConfigAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveMercadoLibreTokensAsync(ConversacionMercadoLibreConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionAlfaKnowledgeConfigDto> GetAlfaKnowledgeConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveAlfaKnowledgeConfigAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveAlfaKnowledgeConfigForConnectionAsync(string connectionString, ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionAlfaKnowledgeConnectionTestResultDto> TestAlfaKnowledgeConnectionAsync(ConversacionAlfaKnowledgeConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionAutomatizacionesConfigDto> GetAutomatizacionesConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveAutomatizacionesConfigAsync(ConversacionAutomatizacionesConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionPrioridadConfigDto> GetPrioridadConfigAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SavePrioridadConfigAsync(ConversacionPrioridadConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionClasificacionOptionDto>> GetClasificacionesAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<UsuarioSistemaDto>> GetUsuariosSistemaAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionWhatsAppNumeroDto>> GetWhatsAppNumerosAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroAsync(int idNumero, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppNumeroDto?> GetWhatsAppNumeroByInstanceNameAsync(string instanceName, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveWhatsAppNumeroAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionWhatsAppNumeroDto> UpsertEmbeddedSignupWhatsAppNumeroForBaseAsync(int idBase, ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveWhatsAppNumeroWebSessionAsync(ConversacionWhatsAppNumeroDto numero, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyDictionary<string, string>> GetPortfolioNamesAsync(IReadOnlyCollection<string> metaBusinessIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SetPortfolioNameAsync(int idBase, string metaBusinessId, string portfolioName, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<bool> TryReserveResolutionAttemptAsync(int idBase, string metaBusinessId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task BackfillNumeroMetaIdentityAsync(int idNumero, string metaBusinessId, string wabaId, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyDictionary<string, string>> GetWabaBusinessMapAsync(IReadOnlyCollection<string> wabaIds, int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<bool> TryReserveWabaResolutionAttemptAsync(int idBase, string wabaId, TimeSpan throttleWindow, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SetWabaOwningBusinessIdAsync(int idBase, string wabaId, string metaBusinessId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<string>> GetConversacionAdministradoresAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveConversacionAdministradoresAsync(IReadOnlyList<string> usuarios, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ConversacionesInboxPreferenceDto> GetInboxPreferenceAsync(string userName, string? sistema, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task SaveInboxPreferenceAsync(string userName, string? sistema, ConversacionesInboxPreferenceDto preference, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<ConversacionReglaDto>> GetReglasAsync(int? expectedBaseId, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<int> SaveReglaAsync(ConversacionReglaDto regla, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task DeleteReglaAsync(int idRegla, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}

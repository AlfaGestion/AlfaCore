using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bot (2026-11-25): lo que quiere decir el cliente lo interpreta la IA leyendo la conversación. Antes listas
/// de palabras pisaban su respuesta: "2,3y4" (cajas 2, 3 y 4) quedaba reducido a la "y" y se contestaba
/// "¡Hola! ¿En qué te puedo ayudar?" (Supermercado Manantial, ALFANET). El código sólo respeta lo que decide
/// la IA y conserva los controles de seguridad (borrador antes de grabar, identidad antes de datos privados).
/// </summary>
public sealed class ConversacionesBotIaDecideTests
{
    private static string Prompt()
        => ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null);

    private static string Fuente()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    [Fact]
    public void ElPrompt_PideInterpretarConLaConversacion_SinFrasesDeEjemplo()
    {
        var prompt = Prompt();
        Assert.Contains("Leé el último mensaje junto con toda la conversación", prompt, StringComparison.Ordinal);
        Assert.Contains("uno por renglón", prompt, StringComparison.Ordinal);
        Assert.Contains("Un dato suelto (números, un nombre, un sí o un no) contesta lo que se venía hablando", prompt, StringComparison.Ordinal);
        Assert.Contains("\"tema_resuelto\": true/false", prompt, StringComparison.Ordinal);
        Assert.Contains("Antes de pasar un problema al equipo, si falta un dato", prompt, StringComparison.Ordinal);
        Assert.Contains("es parte de ese mismo caso", prompt, StringComparison.Ordinal);
        // Las frases de ejemplo terminaban funcionando como disparadores.
        Assert.DoesNotContain("ya anda", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("¿pudieron ver algo?", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"tema_resuelto":true,"respuesta":"¡Qué bueno!"}""", true)]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"tema_resuelto":"true","respuesta":"Listo"}""", true)]
    [InlineData("""{"tipo":"RESUELVE","puede_responder":true,"respuesta":"Hola"}""", false)]
    public void TemaResuelto_LoDecideLaIa(string json, bool esperado)
        => Assert.Equal(esperado, ConversacionAsistenteService.ParseRespuesta(json)!.TemaResuelto);

    [Theory]
    [InlineData("""{"confirma":true}""", true, true)]
    [InlineData("""{"confirma":false}""", true, false)]
    [InlineData("""{"confirma":"true"}""", true, true)]
    [InlineData("""{}""", false, false)]
    [InlineData("no es json", false, false)]
    public void Confirmaciones_LeenLoQueDecidioLaIa(string argumentos, bool valido, bool confirma)
    {
        Assert.Equal(valido, ConversacionesService.TryLeerConfirma(argumentos, out var leido));
        Assert.Equal(confirma, leido);
    }

    [Fact]
    public void NumeroDePedido_LoPasaLaIaALaHerramienta()
    {
        Assert.True(ConversacionesService.TryLeerTexto("""{"numero_pedido":"0003X"}""", "numero_pedido", out var numero));
        Assert.Equal("0003X", numero);
        Assert.False(ConversacionesService.TryLeerTexto("""{}""", "numero_pedido", out _));
        Assert.False(ConversacionesService.TryLeerTexto("""{"numero_pedido":"  "}""", "numero_pedido", out _));
    }

    [Fact]
    public void HerramientasDeConfirmacion_SoloConAlgoPendiente()
    {
        var source = Fuente();
        Assert.Contains("if (identidadPendiente && identidadYaPreguntada)", source, StringComparison.Ordinal);
        Assert.Contains("if (pedidoPendiente)", source, StringComparison.Ordinal);
        Assert.Contains("if (altaPendiente)", source, StringComparison.Ordinal);
        // El servidor graba sólo si hay borrador: la IA decide, el control de seguridad queda.
        Assert.Contains("nombreHerramienta == ToolConfirmarPedido && pedidoPendiente && botPedidosService is not null", source, StringComparison.Ordinal);
        Assert.Contains("nombreHerramienta == ToolConfirmarAltaCliente && altaPendiente && botClientesService is not null", source, StringComparison.Ordinal);
        Assert.Contains("if (identidadNegadaAhora && EsHerramientaConDatosPrivados(nombreHerramienta))", source, StringComparison.Ordinal);
        Assert.Equal("confirma", System.Text.Json.JsonDocument.Parse(ConversacionesService.HerramientaConfirmarPedido.ParametrosJsonSchema)
            .RootElement.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void NingunaReglaDePalabrasPisaLaRespuestaDeLaIa()
    {
        var source = Fuente();
        Assert.DoesNotContain("RespuestaSaludo(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsMensajeDePrueba(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsRespuestaSuelta(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AvisaTemaResuelto(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PideNovedades(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EsRespuestaIdentidad", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eligioPedido", source, StringComparison.Ordinal);
        // Ticket: lo decide abrir_ticket de la IA.
        Assert.Contains("!omitirTicket && !correccionSinTicket && abrirTicketSegunIa)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PedidoOTicket_SoloLosPedidosEnCursoCompitenConElTicket_YNoSeRepreguntan()
    {
        const string terminados = "- Pedido 0001X del 01/10/2026: Finalizado — $ 1.000,00\n- Pedido 0002X del 02/10/2026: Anulado — $ 500,00";
        const string enCurso = "- Pedido 0001X del 01/10/2026: Finalizado — $ 1.000,00\n- Pedido 0003X del 27/10/2026: Aprobado (en preparación) — $ 7.000,00";

        Assert.Null(ConversacionAsistenteHerramientasService.PrimerPedidoEnCurso(terminados));
        Assert.Equal("0003X", ConversacionAsistenteHerramientasService.PrimerPedidoEnCurso(enCurso));
        Assert.Equal("¿Me preguntás por tu pedido 0003X o por tu consulta en curso (ticket #1)?",
            ConversacionesService.PreguntaPedidoOTicket("0003X", 1));
        Assert.Contains("if (!yaPreguntoPedidoOTicket && !pidioPedidoPuntual)", Fuente(), StringComparison.Ordinal);
    }

    [Fact]
    public void Seguimiento_RespondeSinNombrarANadie_YTemaResueltoVaAntes()
    {
        var source = Fuente();
        Assert.DoesNotContain("{", ConversacionesService.RespuestaSeguimiento, StringComparison.Ordinal);
        Assert.Matches(@"await ResponderSeguimientoAsync\(idConversacion, pendiente, token, respuestaSeguimientoIa\)\.ConfigureAwait\(false\);\s*return;", source);
        var resuelto = source.IndexOf("if (result?.TemaResuelto == true)", StringComparison.Ordinal);
        var novedades = source.IndexOf("var omitirTicket = false;", StringComparison.Ordinal);
        Assert.True(resuelto > 0 && resuelto < novedades);
        Assert.DoesNotContain("?", ConversacionesService.RespuestaTemaResuelto, StringComparison.Ordinal);
    }

    // Charlas reales repetidas (2026-11-25): después de derivar, el bot callaba a propósito y cada mensaje
    // nuevo se juntaba con la consulta vieja; la IA volvía a derivar y ya no contestaba ni un "gracias".
    [Fact]
    public void LoPendiente_ArrancaDespuesDeLoQueElBotYaAtendio_AunqueNoHayaContestado()
    {
        var source = Fuente();
        Assert.Contains("IF @Atendido > @Desde SET @Desde = @Atendido;", source, StringComparison.Ordinal);
        Assert.Matches(@"await ResponderConReintentoAsync\(texto, null\)\.ConfigureAwait\(false\);\s*MarcarEntranteAtendidoBot\(idConversacion, ultimoAtendido\);", source);
        Assert.Matches(@"await ResponderConReintentoAsync\(textoNuevo, automaticoConocido\)\.ConfigureAwait\(false\);\s*MarcarEntranteAtendidoBot\(idConversacion, ultimoAtendido\);", source);
        // Fotos y audios de la tanda salen del mismo corte que el texto.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"\{SqlDesdeSinResponder\}").Count);
    }

    // Tickets "4", "Ok", "En bariloche" (charlas reales repetidas, 2026-11-26): el título lo arma la IA con la charla.
    [Fact]
    public void TicketDelBot_TituloConElResumenDeLaIa()
    {
        Assert.Contains("\"resumen_caso\": \"...\"", Prompt(), StringComparison.Ordinal);
        Assert.Equal("Cajas 2, 3 y 4 no actualizan precios",
            ConversacionAsistenteService.ParseRespuesta("""{"tipo":"DERIVA","puede_responder":false,"resumen_caso":" Cajas 2, 3 y 4 no actualizan precios ","respuesta":"Le paso"}""")!.ResumenCaso);
        Assert.Equal("Consulta sin resolver: Cajas 2, 3 y 4 no actualizan precios",
            ConversacionesService.TituloTicketBot("2,3y4", "Cajas 2, 3 y 4 no actualizan precios"));
        Assert.Equal("Consulta sin resolver: no anda / la caja", ConversacionesService.TituloTicketBot("no anda\nla caja", ""));
        Assert.Contains("resumenCasoIa = result?.ResumenCaso ?? string.Empty;", Fuente(), StringComparison.Ordinal);
    }

    // Pedido de ALFA NET (2026-11-26): ticket sólo cuando hace falta trabajar el problema a fondo.
    [Fact]
    public void Ticket_SoloSiLaIaLoPide_ConElCriterioDeLaEmpresa()
    {
        Assert.Contains(ConversacionAsistenteService.CriterioTicketPorDefecto, Prompt(), StringComparison.Ordinal);
        Assert.Contains("Si dudás, false.", Prompt(), StringComparison.Ordinal);
        var propio = ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null, criterioTicket: "Sólo fallas de facturación.");
        Assert.Contains("Sólo fallas de facturación.", propio, StringComparison.Ordinal);
        Assert.DoesNotContain(ConversacionAsistenteService.CriterioTicketPorDefecto, propio, StringComparison.Ordinal);
        Assert.False(ConversacionAsistenteService.ParseRespuesta("""{"tipo":"DERIVA","puede_responder":false,"respuesta":"Le paso"}""")!.AbrirTicket);
        Assert.True(ConversacionAsistenteService.ParseRespuesta("""{"tipo":"DERIVA","puede_responder":false,"abrir_ticket":true,"respuesta":"Le paso"}""")!.AbrirTicket);
        Assert.Contains("var abrirTicketSegunIa = result?.AbrirTicket == true;", Fuente(), StringComparison.Ordinal);
    }

    // Ya derivado y sin respuesta del equipo: la IA sabe que el caso está con el equipo y contesta lo nuevo.
    // Si un técnico ya contestó, el bot no se mete (2026-11-26).
    [Fact]
    public void CasoDerivado_LaIaContestaLoNuevo_YConTecnicoElBotCalla()
    {
        Assert.DoesNotContain("CASO YA PASADO AL EQUIPO", Prompt(), StringComparison.Ordinal);
        Assert.Contains("CASO YA PASADO AL EQUIPO",
            ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null, casoYaDerivado: true), StringComparison.Ordinal);
        var source = Fuente();
        Assert.Contains("casoYaDerivado: casoYaDerivado,", source, StringComparison.Ordinal);
        Assert.Contains("(usoFallbackBot || await HasSentAutomaticMessageTextAsync(idConversacion, \"BOT\", respuesta", source, StringComparison.Ordinal);
        var tecnico = source.IndexOf("await GetEstadoAsistenteCoreAsync(idConversacion, config.BotPausaTrasTecnicoHoras, token)", StringComparison.Ordinal);
        var ia = source.IndexOf("result = await asistenteService.ResponderAsync(", StringComparison.Ordinal);
        Assert.True(tecnico > 0 && tecnico < ia);
    }

    // "Las respuestas son muy por default" (ALFA NET, 2026-11-26): la contención de ejemplo salía copiada tal
    // cual y textos fijos pisaban a la IA. Ahora la IA sabe el estado del caso y redacta ella.
    [Fact]
    public void Respuestas_LasRedactaLaIaConElEstadoDelCaso()
    {
        var prompt = Prompt();
        Assert.DoesNotContain("Le paso tu consulta a un asesor y te responde a la brevedad", prompt, StringComparison.Ordinal);
        Assert.Contains("Nombrá en pocas palabras qué es lo que le pasás al equipo", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ESTADO DEL CASO", prompt, StringComparison.Ordinal);

        var sinTema = ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null, temaPendiente: "", ultimoFueAclaracion: true);
        Assert.Contains("No tiene ningún tema anterior pendiente", sinTema, StringComparison.Ordinal);
        Assert.Contains("no repitas esa pregunta", sinTema, StringComparison.Ordinal);
        var conTicket = ConversacionAsistenteService.BuildSystemPrompt("", "", "", false, false, null, null, null,
            temaPendiente: ConversacionesService.DescribirTemaPendiente(57, false));
        Assert.Contains("Tema anterior pendiente con el equipo: tiene abierto el ticket #57.", conTicket, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ConversacionesService.DescribirTemaPendiente(null, false));

        var source = Fuente();
        Assert.Contains("ultimoFueAclaracion: ultimaRespuestaFueAclaracion,", source, StringComparison.Ordinal);
        Assert.Contains("respuesta = respuestaIa.Trim();", source, StringComparison.Ordinal);
    }

    // "te hago una consulta más" → "¿cuál?" contaba como aclaración y la siguiente pregunta del problema
    // salía como contención fija (charlas reales de ALFANET, 2026-11-26): ahora corta en la tercera.
    [Fact]
    public void Aclaraciones_SeCuentanSeguidas_YCortaEnLaTercera()
    {
        var ahora = new DateTime(2026, 11, 26, 10, 0, 0);
        var ventana = TimeSpan.FromHours(2);
        (bool, DateTime) nota(int min) => (true, ahora.AddMinutes(-min));
        (bool, DateTime) resp(int min) => (false, ahora.AddMinutes(-min));

        Assert.Equal(0, ConversacionesService.ContarAclaracionesSeguidas([], ahora, ventana));
        Assert.Equal(0, ConversacionesService.ContarAclaracionesSeguidas([resp(1), nota(5), resp(5)], ahora, ventana));
        Assert.Equal(1, ConversacionesService.ContarAclaracionesSeguidas([nota(1), resp(1), resp(9)], ahora, ventana));
        Assert.Equal(2, ConversacionesService.ContarAclaracionesSeguidas([nota(1), resp(1), nota(5), resp(5), resp(9)], ahora, ventana));
        Assert.Equal(1, ConversacionesService.ContarAclaracionesSeguidas([nota(1), resp(1), nota(300), resp(300)], ahora, ventana));
        Assert.Equal(2, ConversacionesService.MaxAclaracionesSeguidas);
    }

    // Plan acordado con ALFA NET (2026-11-26): cuándo interviene el asistente, qué lee y con qué modelo.
    [Fact]
    public void Asistente_CuandoInterviene_QueLee_YConQueModelo()
    {
        var source = Fuente();
        // En horario primero el equipo; fuera de horario enseguida.
        Assert.Contains("if (config.BotEsperaMinutos > 0 && !(config.IsConfigured && IsOutsideBusinessHours(config, BusinessNow())))", source, StringComparison.Ordinal);
        // Pausa a mano, respuesta o asignación de una persona (las automáticas se graban "AlfaCore").
        Assert.Contains("if (estadoAsistente is ConversacionAsistenteEstado.PausadoAMano or ConversacionAsistenteEstado.AtiendeElEquipo)", source, StringComparison.Ordinal);
        Assert.Contains("NOT IN (N'', N'AlfaCore')", source, StringComparison.Ordinal);
        Assert.Equal("Eve pausó el asistente en esta conversación.", ConversacionesService.NotaAsistentePausado("Eve"));
        Assert.Equal("Eve reactivó el asistente en esta conversación.", ConversacionesService.NotaAsistenteReactivado("Eve"));
        // Sólo la charla actual.
        Assert.Contains("GetRecentMessagesForBotAsync(idConversacion, config.BotContextoHoras, token)", source, StringComparison.Ordinal);
        Assert.Contains("AND (@Horas <= 0 OR FechaHora >= DATEADD(hour, -@Horas, GETDATE()))", source, StringComparison.Ordinal);
        // Modelo de la base, sólo de la lista (cada uno con precio en la central).
        Assert.Contains("modelo: config.AsistenteModeloEfectivo,", source, StringComparison.Ordinal);
        Assert.Equal("gpt-6-luna", ConversacionesConfigService.NormalizarModeloAsistente(" GPT-6-LUNA "));
        Assert.Equal(string.Empty, ConversacionesConfigService.NormalizarModeloAsistente("gpt-5-turbo-inventado"));
        foreach (var modelo in AlfaCore.Models.ConversacionAutomatizacionesConfigDto.AsistenteModelosDisponibles)
        {
            Assert.Contains($"WHERE Modelo = N'{modelo.Codigo}')", CentralIaSchema.Script, StringComparison.Ordinal);
            // El precio que muestra la pantalla es el mismo que cobra la central.
            Assert.Contains($"VALUES (N'{modelo.Codigo}', ", CentralIaSchema.Script, StringComparison.Ordinal);
            Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{modelo.UsdEntrada:0.00}, "), CentralIaSchema.Script, StringComparison.Ordinal);
        }
        var recomendado = AlfaCore.Models.ConversacionAutomatizacionesConfigDto.AsistenteModelosDisponibles[0];
        Assert.Equal("gpt-6-luna", recomendado.Codigo);
        Assert.Equal("Estándar", recomendado.Nombre);
        Assert.Equal("Aprox. 0,4 créditos por respuesta", recomendado.ConsumoTexto);
        // Sin modelo elegido, el asistente usa el Estándar.
        Assert.Equal("gpt-6-luna", new AlfaCore.Models.ConversacionAutomatizacionesConfigDto().AsistenteModeloEfectivo);
        Assert.Equal("gpt-6.1-terra", new AlfaCore.Models.ConversacionAutomatizacionesConfigDto { AsistenteModelo = "gpt-6.1-terra" }.AsistenteModeloEfectivo);
        Assert.Contains("N'gpt-6.1-terra')", CentralIaSchema.VerificacionSql, StringComparison.Ordinal);
        Assert.Contains("N'gpt-4.1-mini')", CentralIaSchema.VerificacionSql, StringComparison.Ordinal);
        // No se ofrece a hacer lo que sólo hace una persona.
        Assert.Contains("No te ofrezcas a revisar, probar, verificar, configurar o ver algo junto con el cliente", Prompt(), StringComparison.Ordinal);
    }

    // Prueba directa contra OpenAI (2026-11-26): los modelos que "piensan" tardaban 10-13 s y gastaban de más,
    // y gpt-5.6 / gpt-6 rechazan las herramientas si no se les baja el razonamiento.
    [Theory]
    [InlineData("gpt-4o-mini", true, null)]
    [InlineData("gpt-4.1", true, null)]
    [InlineData("gpt-5.4-mini", true, null)]
    [InlineData("gpt-5.4-nano", true, null)]
    [InlineData("gpt-5", false, "minimal")]
    [InlineData("gpt-5-mini", false, "minimal")]
    [InlineData("gpt-5-nano-2025-08-07", false, "minimal")]
    [InlineData("gpt-5.6-luna", false, "none")]
    [InlineData("gpt-6-luna", true, "none")]
    [InlineData("gpt-6.1-terra", true, "none")]
    public void ModelosNuevos_TemperaturaYRazonamiento(string modelo, bool temperatura, string? esfuerzo)
    {
        Assert.Equal(temperatura, ConversacionAsistenteService.SupportsCustomTemperature(modelo));
        Assert.Equal(esfuerzo, ConversacionAsistenteService.EsfuerzoRazonamiento(modelo));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return current!.FullName;
    }
}

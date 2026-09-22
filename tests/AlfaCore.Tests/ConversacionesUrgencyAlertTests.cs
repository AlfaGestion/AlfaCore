using Xunit;

namespace AlfaCore.Tests;

public sealed class ConversacionesUrgencyAlertTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServiceSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
    private static readonly string ConfigSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));
    private static readonly string ConfigModelSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "ConversacionesConfiguracionModels.cs"));
    private static readonly string ConfigPageSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
    private static readonly string SqlSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "App_Data", "updates", "2026-09-22-003__conversaciones_alertas_urgencia.sql"));

    [Fact]
    public void UrgencyHook_RunsOnlyAfterExistingUrgencyDecision()
    {
        var method = ExtractMethodBody(ServiceSource, "private async Task EjecutarRespuestaBotAsync");
        var urgency = method.IndexOf("var esUrgente = ContienePalabraEscalado(texto, config.AsistenteUrgenciaPalabras);", StringComparison.Ordinal);
        var priority = method.IndexOf("await SubirPrioridadAsync(idConversacion, \"URGENTE\", token)", urgency, StringComparison.Ordinal);
        var notify = method.IndexOf("await TryNotifyUrgencyTechniciansAsync(idConversacion, texto, config, token)", priority, StringComparison.Ordinal);

        Assert.True(urgency >= 0);
        Assert.True(priority > urgency);
        Assert.True(notify > priority);
    }

    [Fact]
    public void UrgencyNotification_HappensBeforeTheEscaladoEarlyReturn_SoOverlappingWordsStillAlert()
    {
        // Bug corregido: antes "esUrgente" se calculaba DESPUÉS del return de BotPalabrasEscalado, así
        // que si la misma palabra estaba en ambas listas (BotPalabrasEscalado y
        // AsistenteUrgenciaPalabras), el handoff cortaba la función antes de avisar a los técnicos.
        // Ahora la detección + alerta de urgencia se evalúa primero, y el escalado sigue cortando
        // exactamente igual que antes (no se tocó su semántica).
        var method = ExtractMethodBody(ServiceSource, "private async Task EjecutarRespuestaBotAsync");
        var urgentBlock = method.IndexOf("var esUrgente = ContienePalabraEscalado(texto, config.AsistenteUrgenciaPalabras);", StringComparison.Ordinal);
        var escaladoReturn = method.IndexOf("if (ContienePalabraEscalado(texto, config.BotPalabrasEscalado))", StringComparison.Ordinal);

        Assert.True(urgentBlock >= 0);
        Assert.True(escaladoReturn >= 0);
        Assert.True(urgentBlock < escaladoReturn, "La detección+alerta de urgencia debe evaluarse antes del corte de escalado.");
        Assert.Contains("TraceDiagAsync(\"EjecutarStop:PalabraEscalado\"", method, StringComparison.Ordinal); // el handoff sigue existiendo tal cual
    }

    [Fact]
    public void UrgencyNotification_HappensBeforeEveryOtherEarlyBotCut()
    {
        // La alerta de urgencia es responsabilidad distinta de si el Bot responde/hace handoff --
        // debe evaluarse antes de CUALQUIER corte de negocio, no solo el de escalado.
        var method = ExtractMethodBody(ServiceSource, "private async Task EjecutarRespuestaBotAsync");
        var urgentBlock = method.IndexOf("var esUrgente = ContienePalabraEscalado(texto, config.AsistenteUrgenciaPalabras);", StringComparison.Ordinal);
        var sinAsignarCut = method.IndexOf("if (config.BotSoloSinAsignar)", StringComparison.Ordinal);
        var maxRespuestasCut = method.IndexOf("if (botCount >= Math.Max(1, config.BotMaxRespuestas))", StringComparison.Ordinal);
        var humanoYaRespondioCut = method.IndexOf("if (humanoYaRespondio)", StringComparison.Ordinal);
        var soloFueraHorarioCut = method.IndexOf("if (config.BotSoloFueraHorario && !fueraDeHorario)", StringComparison.Ordinal);

        Assert.True(urgentBlock >= 0 && sinAsignarCut > urgentBlock && maxRespuestasCut > urgentBlock
            && humanoYaRespondioCut > urgentBlock && soloFueraHorarioCut > urgentBlock);
    }

    [Fact]
    public void TemplateSend_UsesTheSameExplicitMetaTimeoutAsOtherGraphSends()
    {
        // SendTemplateToWhatsAppAsync es el camino que usan tanto el envío manual/programado de
        // plantillas como SendSystemTemplateToPhoneAsync (alertas de urgencia) -- antes era el único
        // sender de Graph sin client.Timeout explícito, quedando en el default de HttpClient (100s)
        // por cada técnico, bloqueando la respuesta al cliente si Meta se cuelga.
        var method = ExtractMethodBody(ServiceSource, "private async Task<WhatsAppSendResult> SendTemplateToWhatsAppAsync");
        Assert.Contains("client.Timeout = MetaSendTimeout;", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Alerts_AreSentWithLowerMetaLayerWithoutCreatingTechnicianConversation()
    {
        var method = ExtractMethodBody(ServiceSource, "private async Task<WhatsAppSendResult> SendSystemTemplateToPhoneAsync");

        Assert.Contains("SendTemplateToWhatsAppAsync(config, destinationPhone, template, values, ct)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateOrGetWhatsAppConversationAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertMessageAsync", method, StringComparison.Ordinal);
        Assert.Contains("EnsureWhatsAppMetaProvider(config, \"enviar alertas de urgencia\")", method, StringComparison.Ordinal);
        Assert.Contains("EnsureTemplateMatchesRuntime(template, runtimeCredential)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Recipients_AreResolvedServerSideFromConfiguredTechnicianIds()
    {
        var method = ExtractMethodBody(ServiceSource, "private async Task<IReadOnlyList<UrgencyAlertRecipient>> GetUrgencyAlertRecipientsAsync");

        Assert.Contains("FROM dbo.V_TA_Tecnicos", method, StringComparison.Ordinal);
        Assert.Contains("WHERE ISNULL(Baja, 0) = 0", method, StringComparison.Ordinal);
        Assert.Contains("ISNULL(Telefono, N'') AS Telefono", method, StringComparison.Ordinal);
        Assert.Contains("NormalizePhone(GetString(rd, 2))", method, StringComparison.Ordinal);
        Assert.DoesNotContain("LLM", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idempotency_UsesDurableUniqueReservationAndRecordsFailures()
    {
        Assert.Contains("CONV_ALERTAS_URGENCIA_ENVIADAS", SqlSource, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX UX_CONV_ALERTAS_URGENCIA_MENSAJE_TECNICO", SqlSource, StringComparison.Ordinal);
        Assert.Contains("(IdMensajeOrigen, IdTecnico)", SqlSource, StringComparison.Ordinal);

        var reserve = ExtractMethodBody(ServiceSource, "private async Task<bool> TryReserveUrgencyAlertAsync");
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", reserve, StringComparison.Ordinal);
        Assert.Contains("catch (SqlException ex) when (ex.Number is 2601 or 2627)", reserve, StringComparison.Ordinal);

        var notify = ExtractMethodBody(ServiceSource, "private async Task TryNotifyUrgencyTechniciansAsync");
        Assert.Contains("catch (Exception ex)", notify, StringComparison.Ordinal);
        Assert.Contains("await MarkUrgencyAlertErrorAsync", notify, StringComparison.Ordinal);
        Assert.DoesNotContain("throw;", notify, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_IsNotImplemented_AndTheGapIsDocumentedRatherThanClaimedFalsely()
    {
        // La review confirmó que hoy una fila en ERROR (o PENDIENTE huérfana) queda reservada para
        // siempre y nunca se reintenta -- TryReserveUrgencyAlertAsync es un insert-si-no-existe que
        // no filtra por Estado. Alcance de este fix: documentarlo correctamente, no implementar
        // retry. Si en el futuro se agrega un reintento real, este test debe actualizarse.
        Assert.Contains("Retry: TODO -- no implementado.", SqlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("puede reintentarse actualizando la misma fila", SqlSource, StringComparison.Ordinal);

        var reserve = ExtractMethodBody(ServiceSource, "private async Task<bool> TryReserveUrgencyAlertAsync");
        Assert.Contains("WHERE NOT EXISTS", reserve, StringComparison.Ordinal);
        Assert.DoesNotContain("Estado = N'ERROR'", reserve, StringComparison.Ordinal); // no filtra por Estado al reservar
    }

    [Fact]
    public void Configuration_StoresTemplateAndTechniciansInTaConfiguracionAndUi()
    {
        Assert.Contains("public string AsistenteUrgenciaTemplate", ConfigModelSource, StringComparison.Ordinal);
        Assert.Contains("public List<string> AsistenteUrgenciaTecnicos", ConfigModelSource, StringComparison.Ordinal);
        Assert.Contains("CONV_ASISTENTE_URGENCIA_TEMPLATE", ConfigSource, StringComparison.Ordinal);
        Assert.Contains("CONV_ASISTENTE_URGENCIA_TECNICOS", ConfigSource, StringComparison.Ordinal);
        Assert.Contains("Técnicos que reciben alertas de urgencia", ConfigPageSource, StringComparison.Ordinal);
        Assert.Contains("ToggleUrgencyTechnician", ConfigPageSource, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontró {signature}.");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"No se encontró el cuerpo de {signature}.");

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}') depth--;
            if (depth == 0) return source[brace..(i + 1)];
        }

        throw new InvalidOperationException($"No se pudo extraer {signature}.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AlfaCore")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}

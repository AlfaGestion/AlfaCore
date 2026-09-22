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
        Assert.Contains("Retry: un registro en ERROR puede reintentarse actualizando la misma fila", SqlSource, StringComparison.Ordinal);

        var reserve = ExtractMethodBody(ServiceSource, "private async Task<bool> TryReserveUrgencyAlertAsync");
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", reserve, StringComparison.Ordinal);
        Assert.Contains("catch (SqlException ex) when (ex.Number is 2601 or 2627)", reserve, StringComparison.Ordinal);

        var notify = ExtractMethodBody(ServiceSource, "private async Task TryNotifyUrgencyTechniciansAsync");
        Assert.Contains("catch (Exception ex)", notify, StringComparison.Ordinal);
        Assert.Contains("await MarkUrgencyAlertErrorAsync", notify, StringComparison.Ordinal);
        Assert.DoesNotContain("throw;", notify, StringComparison.Ordinal);
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

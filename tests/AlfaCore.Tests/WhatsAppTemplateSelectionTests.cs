using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppTemplateSelectionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ConversationTemplatesPreferLocalWabaTemplatesBeforeRemoteDiscovery()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var method = service.IndexOf("GetTemplatesForConversationAsync(long idConversacion, int? expectedBaseId", StringComparison.Ordinal);
        var local = service.IndexOf("var localTemplates = await GetTemplatesAsync(new ConversacionPlantillaFilters", method, StringComparison.Ordinal);
        var numberFilter = service.IndexOf("IdNumeroWhatsApp = conversation.IdNumeroWhatsApp", local, StringComparison.Ordinal);
        var localReturn = service.IndexOf("return localTemplates;", numberFilter, StringComparison.Ordinal);
        var remoteDiscovery = service.IndexOf("DiscoverTemplatesAsync(runtime.WabaId", localReturn, StringComparison.Ordinal);
        var runtime = service.IndexOf("var runtime = await whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(local > method);
        Assert.True(numberFilter > local);
        Assert.True(localReturn > numberFilter);
        Assert.True(runtime > localReturn);
        Assert.True(remoteDiscovery > localReturn);
    }

    [Fact]
    public void ConversationTemplatesDoNotRequireRuntimeCredentialsWhenLocalApprovedTemplateExists()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var method = service.IndexOf("GetTemplatesForConversationAsync(long idConversacion, int? expectedBaseId", StringComparison.Ordinal);
        var local = service.IndexOf("var localTemplates = await GetTemplatesAsync(new ConversacionPlantillaFilters", method, StringComparison.Ordinal);
        var approved = service.IndexOf("EstadoMeta = \"APPROVED\"", local, StringComparison.Ordinal);
        var localReturnCondition = service.IndexOf("if (localTemplates.Count > 0)", approved, StringComparison.Ordinal);
        var localReturn = service.IndexOf("return localTemplates;", localReturnCondition, StringComparison.Ordinal);
        var config = service.IndexOf("var config = await conversacionesConfigService.GetWhatsAppConfigAsync", localReturn, StringComparison.Ordinal);
        var runtime = service.IndexOf("var runtime = await whatsAppRuntimeCredentialResolver.ResolveAsync", config, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(local > method);
        Assert.True(approved > local);
        Assert.True(localReturnCondition > approved);
        Assert.True(localReturn > localReturnCondition);
        Assert.True(config > localReturn);
        Assert.True(runtime > config);
    }

    [Fact]
    public void ConversationTemplatesUseWabaScopeForSameAndDifferentNumberCases()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var getTemplates = service.IndexOf("public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync", StringComparison.Ordinal);
        var listContext = service.IndexOf("ResolveTemplateListContextAsync(filters.IdNumeroWhatsApp", getTemplates, StringComparison.Ordinal);
        var storedWaba = service.IndexOf("var wabaId = (numero.WabaId ?? string.Empty).Trim();", StringComparison.Ordinal);
        var where = service.IndexOf("AND ((@WabaId IS NULL AND WabaId IS NULL) OR WabaId = @WabaId)", getTemplates, StringComparison.Ordinal);
        var noTemplateNumberColumn = service.IndexOf("IdNumeroWhatsApp = @IdNumeroWhatsApp", getTemplates, where - getTemplates, StringComparison.Ordinal);

        Assert.True(getTemplates >= 0);
        Assert.True(listContext > getTemplates);
        Assert.True(storedWaba > listContext);
        Assert.True(where > listContext);
        Assert.Equal(-1, noTemplateNumberColumn);
    }

    [Fact]
    public void TemplatesRouteListsByStoredNumberWabaWithoutResolvingRuntimeCredentials()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var getTemplates = service.IndexOf("public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesAsync", StringComparison.Ordinal);
        var getTemplatesForConversation = service.IndexOf("public Task<IReadOnlyList<ConversacionPlantillaDto>> GetTemplatesForConversationAsync", getTemplates, StringComparison.Ordinal);
        var listContext = service.IndexOf("ResolveTemplateListContextAsync(filters.IdNumeroWhatsApp", getTemplates, StringComparison.Ordinal);
        var commandParameter = service.IndexOf("cmd.Parameters.AddWithValue(\"@WabaId\", DbNullable(templateContext.WabaId));", listContext, StringComparison.Ordinal);
        var runtime = service.IndexOf("whatsAppRuntimeCredentialResolver.ResolveAsync", getTemplates, StringComparison.Ordinal);
        var listMethod = service.IndexOf("private async Task<TemplateListContext> ResolveTemplateListContextAsync", StringComparison.Ordinal);
        var storedWaba = service.IndexOf("var wabaId = (numero.WabaId ?? string.Empty).Trim();", listMethod, StringComparison.Ordinal);

        Assert.True(getTemplates >= 0);
        Assert.True(getTemplatesForConversation > getTemplates);
        Assert.True(listContext > getTemplates);
        Assert.True(commandParameter > listContext);
        Assert.True(runtime < 0 || runtime > getTemplatesForConversation);
        Assert.True(listMethod > getTemplates);
        Assert.True(storedWaba > listMethod);
    }

    [Fact]
    public void RemoteTemplateIdentityIncludesWabaAndMetaTemplateId()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");

        Assert.Contains("MapRemoteTemplate(runtime.WabaId, template)", service, StringComparison.Ordinal);
        Assert.Contains("HashData(Encoding.UTF8.GetBytes($\"{wabaId}|{template.Id}|{template.Name}|{template.Language}\"))", service, StringComparison.Ordinal);
        Assert.Contains("MetaTemplateId = template.Id, ComponentesMetaJson = template.ComponentsJson", service, StringComparison.Ordinal);
        Assert.Contains("WabaId = wabaId, Activa = true, EsMetaRemota = true", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateCatalogSyncIsTenantAndWabaScoped()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var sync = service.IndexOf("SyncTemplatesCatalogFromMetaAsync(int? idNumeroWhatsApp", StringComparison.Ordinal);
        var tenant = service.IndexOf("ResolveTenantConnection(expectedBaseId, \"SyncTemplatesCatalogFromMeta\")", sync, StringComparison.Ordinal);
        var listContext = service.IndexOf("ResolveTemplateListContextAsync(idNumeroWhatsApp, expectedBaseId, tenant.ConnectionString", tenant, StringComparison.Ordinal);
        var runtime = service.IndexOf("whatsAppRuntimeCredentialResolver.ResolveAsync", listContext, StringComparison.Ordinal);
        var resolvedWaba = service.IndexOf("var resolvedWabaId = (runtime.WabaId ?? string.Empty).Trim();", runtime, StringComparison.Ordinal);
        var wabaGuard = service.IndexOf("!string.IsNullOrWhiteSpace(listContext.WabaId) && !string.Equals(resolvedWabaId, listContext.WabaId", resolvedWaba, StringComparison.Ordinal);
        var referenceDiscovery = service.IndexOf("DiscoverTemplatesAsync(resolvedWabaId, reference", wabaGuard, StringComparison.Ordinal);
        var runtimeDiscovery = service.IndexOf("DiscoverTemplatesAsync(resolvedWabaId, runtime.AccessToken, runtime.GraphVersion", referenceDiscovery, StringComparison.Ordinal);
        var connection = service.IndexOf("new SqlConnection(tenant.ConnectionString)", runtimeDiscovery, StringComparison.Ordinal);

        Assert.True(sync >= 0);
        Assert.True(tenant > sync);
        Assert.True(listContext > tenant);
        Assert.True(runtime > listContext);
        Assert.True(resolvedWaba > runtime);
        Assert.True(wabaGuard > resolvedWaba);
        Assert.True(referenceDiscovery > wabaGuard);
        Assert.True(runtimeDiscovery > referenceDiscovery);
        Assert.True(connection > runtimeDiscovery);
        Assert.DoesNotContain("La sincronizaci\u00f3n de cat\u00e1logo requiere un WhatsApp conectado por Embedded Signup.", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateCatalogSyncMatchesByMetaIdThenNameLanguageAndPreservesLocalFlags()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var upsert = service.IndexOf("UpsertRemoteTemplateAsync(SqlConnection cn, string wabaId, MetaMessageTemplate template", StringComparison.Ordinal);
        var select = service.IndexOf("SELECT TOP 1", upsert, StringComparison.Ordinal);
        var metaIdMatch = service.IndexOf("MetaTemplateId = @MetaTemplateId", select, StringComparison.Ordinal);
        var nameLanguageMatch = service.IndexOf("NombreMeta = @NombreMeta AND Idioma = @Idioma", metaIdMatch, StringComparison.Ordinal);
        var update = service.IndexOf("UPDATE dbo.CONV_PLANTILLAS", nameLanguageMatch, StringComparison.Ordinal);
        var insert = service.IndexOf("INSERT INTO dbo.CONV_PLANTILLAS", update, StringComparison.Ordinal);
        var updateBlock = service.Substring(update, insert - update);

        Assert.True(upsert >= 0);
        Assert.True(metaIdMatch > select);
        Assert.True(nameLanguageMatch > metaIdMatch);
        Assert.DoesNotContain("Activa =", updateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("NombreVisible =", updateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("EjemplosVariablesJson", updateBlock, StringComparison.Ordinal);
        Assert.Contains("MetaPayloadJson = @MetaPayloadJson", updateBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateCatalogSyncDoesNotDeleteOrMarkMissingRemoteTemplates()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var sync = service.IndexOf("SyncTemplatesCatalogFromMetaAsync(int? idNumeroWhatsApp", StringComparison.Ordinal);
        var saveDraft = service.IndexOf("SaveTemplateDraftAsync(ConversacionPlantillaSaveRequest request", sync, StringComparison.Ordinal);
        var syncBlock = service.Substring(sync, saveDraft - sync);

        Assert.DoesNotContain("DELETE FROM dbo.CONV_PLANTILLAS", syncBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Activa = 0", syncBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("result.ConError++", syncBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRemoteTemplateNamesRemainDistinctPerWabaAndPreviewUsesSelectedTemplate()
    {
        var templateA = MapRemoteTemplate(
            "waba-a",
            new MetaMessageTemplate("meta-a", "contacto", "es_AR", "APPROVED", "UTILITY", string.Empty, "Hola!", string.Empty));
        var templateB = MapRemoteTemplate(
            "waba-b",
            new MetaMessageTemplate("meta-b", "contacto", "es_AR", "APPROVED", "UTILITY", string.Empty, "Hola, este es un mensaje de prueba.", string.Empty));

        Assert.NotEqual(templateA.IdPlantilla, templateB.IdPlantilla);
        Assert.Equal("waba-b", templateB.WabaId);
        Assert.Equal("meta-b", templateB.MetaTemplateId);

        var selected = new[] { templateA, templateB }.Single(x => x.IdPlantilla == templateB.IdPlantilla);

        Assert.Equal("contacto", selected.NombreMeta);
        Assert.Equal("es_AR", selected.Idioma);
        Assert.Equal("Hola, este es un mensaje de prueba.", selected.CuerpoTexto);
    }

    [Fact]
    public void SendTemplateUsesSelectedIdentityAndValidatesWabaBeforePost()
    {
        var service = Read("src", "AlfaCore", "Services", "ConversacionesService.cs");
        var send = service.IndexOf("SendTemplateMessageAsync(ConversacionPlantillaSendRequest request", StringComparison.Ordinal);
        var remoteLookup = service.IndexOf("x.IdPlantilla == request.IdPlantilla", send, StringComparison.Ordinal);
        var runtime = service.IndexOf("var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync", remoteLookup, StringComparison.Ordinal);
        var guard = service.IndexOf("EnsureTemplateMatchesRuntime(template, runtimeCredential);", runtime, StringComparison.Ordinal);
        var post = service.IndexOf("SendTemplateToWhatsAppAsync(config, conversation.TelefonoWhatsApp, template, values, token)", guard, StringComparison.Ordinal);

        Assert.True(send >= 0);
        Assert.True(remoteLookup > send);
        Assert.True(runtime > remoteLookup);
        Assert.True(guard > runtime);
        Assert.True(post > guard);

        var ambiguousNameLookup = service.IndexOf("string.Equals(x.NombreMeta, request.NombreMeta.Trim()", send, StringComparison.Ordinal);
        Assert.True(ambiguousNameLookup < 0 || ambiguousNameLookup > post);
    }

    [Fact]
    public void TemplatePanelClearsStaleStateAndDiscardsLateLoads()
    {
        var page = Read("src", "AlfaCore", "Components", "Pages", "Conversaciones.razor");

        Assert.Contains("private int _templateLoadGeneration;", page, StringComparison.Ordinal);
        Assert.Contains("var generation = ++_templateLoadGeneration;", page, StringComparison.Ordinal);
        Assert.Contains("var conversationId = _selectedConversation.IdConversacion;", page, StringComparison.Ordinal);
        Assert.Contains("IsTemplateLoadCurrent(generation, conversationId, lease)", page, StringComparison.Ordinal);
        Assert.Contains("_selectedConversation?.IdConversacion == conversationId", page, StringComparison.Ordinal);
        var openPanel = page.IndexOf("private async Task OpenTemplatePanel()", StringComparison.Ordinal);
        var clearSelection = page.IndexOf("ClearTemplateSelection();", openPanel, StringComparison.Ordinal);
        var loadTemplates = page.IndexOf("await LoadTemplatesForConversationAsync();", clearSelection, StringComparison.Ordinal);
        Assert.True(openPanel >= 0);
        Assert.True(clearSelection > openPanel);
        Assert.True(loadTemplates > clearSelection);
        Assert.Contains("private void CloseTemplatePanel()", page, StringComparison.Ordinal);
        Assert.Contains("_templateLoadGeneration++;", page, StringComparison.Ordinal);
    }

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static ConversacionPlantillaDto MapRemoteTemplate(string wabaId, MetaMessageTemplate template)
    {
        var method = typeof(ConversacionesService).GetMethod("MapRemoteTemplate", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("No se encontró MapRemoteTemplate.");
        return (ConversacionPlantillaDto)method.Invoke(null, [wabaId, template])!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raiz del repositorio.");
    }
}

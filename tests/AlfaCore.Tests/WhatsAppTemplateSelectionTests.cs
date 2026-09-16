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

        Assert.True(method >= 0);
        Assert.True(local > method);
        Assert.True(numberFilter > local);
        Assert.True(localReturn > numberFilter);
        Assert.True(remoteDiscovery > localReturn);
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
        Assert.Contains("MetaTemplateId = template.Id, WabaId = wabaId, Activa = true, EsMetaRemota = true", service, StringComparison.Ordinal);
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

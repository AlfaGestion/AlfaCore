using AlfaCore.Services;
using System.Reflection;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppMediaRuntimeCredentialTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    [Fact]
    public void UploadAttachment_ResolvesRuntimeCredentialBeforeSendConfigurationAndGraph()
    {
        var method = MethodOffset("UploadAttachmentAsync");
        var resolver = ServiceSource.IndexOf("var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);
        var configured = ServiceSource.IndexOf("initialState = whatsAppConfig.IsConfiguredForSend", method, StringComparison.Ordinal);
        var graphSend = ServiceSource.IndexOf("SendAttachmentToWhatsAppAsync(", configured, StringComparison.Ordinal);

        Assert.True(resolver > method);
        Assert.True(configured > resolver);
        Assert.True(graphSend > configured);
    }

    [Fact]
    public void UploadAttachment_AppliesRuntimeCredentialToTheMediaConfig()
    {
        var method = MethodOffset("UploadAttachmentAsync");
        var resolver = ServiceSource.IndexOf("var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);
        var configured = ServiceSource.IndexOf("initialState = whatsAppConfig.IsConfiguredForSend", method, StringComparison.Ordinal);
        var block = ServiceSource[resolver..configured];

        Assert.Contains("whatsAppConfig.PhoneNumberId = runtimeCredential.PhoneNumberId;", block, StringComparison.Ordinal);
        Assert.Contains("whatsAppConfig.BusinessAccountId = runtimeCredential.WabaId;", block, StringComparison.Ordinal);
        Assert.Contains("whatsAppConfig.ApiVersion = runtimeCredential.GraphVersion;", block, StringComparison.Ordinal);
        Assert.Contains("whatsAppConfig.AccessToken = runtimeCredential.AccessToken;", block, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadAttachment_UsesSelectedPhoneAndLegacyConfigAsResolverInput()
    {
        var method = MethodOffset("UploadAttachmentAsync");
        var resolver = ServiceSource.IndexOf("var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);
        var configured = ServiceSource.IndexOf("initialState = whatsAppConfig.IsConfiguredForSend", method, StringComparison.Ordinal);
        var block = ServiceSource[resolver..configured];

        Assert.Contains("numero?.IdNumero ?? conversation.IdNumeroWhatsApp", block, StringComparison.Ordinal);
        Assert.Contains("whatsAppConfig.PhoneNumberId, whatsAppConfig, token", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IMAGE", "image/jpeg", "image")]
    [InlineData("DOCUMENT", "application/pdf", "document")]
    public void ImageAndPdfMediaTypesUseTheSameRuntimeMediaPath(string tipoArchivo, string mimeType, string expectedGraphType)
        => Assert.Equal(expectedGraphType, InvokeNormalizeOutgoingMediaType(tipoArchivo, mimeType));

    [Fact]
    public void MediaUploadAndFinalMessageUseTheSameConfiguredPhoneNumberId()
    {
        var upload = MethodOffset("UploadWhatsAppMediaAsync");
        var uploadUrl = ServiceSource.IndexOf("var url = $\"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/media\";", upload, StringComparison.Ordinal);

        var send = MethodOffset("SendAttachmentToWhatsAppAsync");
        var mediaId = ServiceSource.IndexOf("var mediaId = await UploadWhatsAppMediaAsync(config", send, StringComparison.Ordinal);
        var messageUrl = ServiceSource.IndexOf("var url = $\"https://graph.facebook.com/{config.ApiVersion}/{config.PhoneNumberId}/messages\";", mediaId, StringComparison.Ordinal);

        Assert.True(uploadUrl > upload);
        Assert.True(mediaId > send);
        Assert.True(messageUrl > mediaId);
    }

    [Fact]
    public void ResolverFailureHappensBeforeAnyMediaGraphCall()
    {
        var method = MethodOffset("UploadAttachmentAsync");
        var resolver = ServiceSource.IndexOf("var runtimeCredential = await whatsAppRuntimeCredentialResolver.ResolveAsync", method, StringComparison.Ordinal);
        var graphSend = ServiceSource.IndexOf("SendAttachmentToWhatsAppAsync(", resolver, StringComparison.Ordinal);

        Assert.True(resolver > method);
        Assert.True(graphSend > resolver);
    }

    [Fact]
    public void ManualStickerSendReusesUploadAttachmentSoItInheritsRuntimeCredentials()
    {
        var method = MethodOffset("SendFavoriteStickerAsync");
        var upload = ServiceSource.IndexOf("return await UploadAttachmentAsync(new ConversacionUploadAdjuntoRequest", method, StringComparison.Ordinal);

        Assert.True(upload > method);
    }

    [Fact]
    public void ThereIsOnlyOneOutgoingWhatsAppMediaUploadPath()
    {
        var callCount = CountOccurrences("UploadWhatsAppMediaAsync(");

        // Una definicion y una llamada desde SendAttachmentToWhatsAppAsync.
        Assert.Equal(2, callCount);
    }

    private static string InvokeNormalizeOutgoingMediaType(string tipoArchivo, string mimeType)
    {
        var method = typeof(ConversacionesService).GetMethod("NormalizeOutgoingMediaType", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ConversacionesService), "NormalizeOutgoingMediaType");
        return (string)method.Invoke(null, [tipoArchivo, mimeType])!;
    }

    private static int MethodOffset(string methodName)
    {
        var offset = ServiceSource.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"No se encontro {methodName}.");
        return offset;
    }

    private static int CountOccurrences(string value)
    {
        var count = 0;
        var index = 0;
        while ((index = ServiceSource.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

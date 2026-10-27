using AlfaCore;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppAttachmentHardeningTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("image/jpeg", "foto.jpg", "image/jpeg", null)]
    [InlineData("image/png", "foto.png", "image/png", null)]
    [InlineData("application/pdf", "factura.pdf", "application/pdf", null)]
    [InlineData("text/html", "x.html", "application/octet-stream", "x.html")]
    [InlineData("image/svg+xml", "vector.svg", "application/octet-stream", "vector.svg")]
    [InlineData("application/x-msdownload", "setup.exe", "application/octet-stream", "setup.exe")]
    public void AttachmentServePolicy_AllowsOnlySafeInlineTypes(
        string mime,
        string fileName,
        string expectedContentType,
        string? expectedDownloadName)
    {
        var policy = Program.BuildAttachmentServePolicy(mime, fileName, downloadName: null, forceDownload: false);

        Assert.Equal(expectedContentType, policy.ContentType);
        Assert.Equal(expectedDownloadName, policy.FileDownloadName);
    }

    [Fact]
    public void AttachmentServePolicy_ForcedDownload_SanitizesHostileFilename()
    {
        var policy = Program.BuildAttachmentServePolicy(
            "image/jpeg",
            "..\\..\\cliente\r\nContent-Type text/html.jpg",
            downloadName: null,
            forceDownload: true);

        Assert.Equal("image/jpeg", policy.ContentType);
        Assert.DoesNotContain("..", policy.FileDownloadName);
        Assert.DoesNotContain("\\", policy.FileDownloadName);
        Assert.DoesNotContain("\r", policy.FileDownloadName);
        Assert.DoesNotContain("\n", policy.FileDownloadName);
    }

    [Fact]
    public void AttachmentPathConfinement_AcceptsOnlyTenantRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "alfacore-tests", "uploads", "conversaciones", "AW_84-scope");
        var inside = Path.Combine(root, "123", "file.jpg");
        var traversal = Path.Combine(root, "123", "..", "..", "outside.jpg");
        var otherTenant = Path.Combine(Path.GetTempPath(), "alfacore-tests", "uploads", "conversaciones", "AW_106-scope", "123", "file.jpg");

        Assert.True(ConversacionesService.IsAttachmentPathAllowedForRoot(inside, root));
        Assert.False(ConversacionesService.IsAttachmentPathAllowedForRoot(traversal, root));
        Assert.False(ConversacionesService.IsAttachmentPathAllowedForRoot(otherTenant, root));
        Assert.False(ConversacionesService.IsAttachmentPathAllowedForRoot(@"\\server\share\file.jpg", root));
    }

    [Fact]
    public void Endpoint_Source_FailsClosedBeforeLoadingAttachment()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var endpoint = source.IndexOf("app.MapGet(\"/api/conversaciones/adjuntos/{idAdjunto:long}\"", StringComparison.Ordinal);
        Assert.True(endpoint >= 0);

        var activation = source.IndexOf("TryActivateAuthorizedAttachmentBase(idBase, sessionService, appUserSession)", endpoint, StringComparison.Ordinal);
        var lookup = source.IndexOf("svc.GetAttachmentForServeAsync(idAdjunto, idBase", endpoint, StringComparison.Ordinal);
        var nosniff = source.IndexOf("\"X-Content-Type-Options\"] = \"nosniff\"", endpoint, StringComparison.Ordinal);
        var sqlBlob = source.IndexOf("if (hasSqlContent)", endpoint, StringComparison.Ordinal);
        var localFileInfo = source.IndexOf("var fileInfo = new FileInfo(adjunto.RutaLocal)", endpoint, StringComparison.Ordinal);

        Assert.True(nosniff > endpoint && nosniff < activation);
        Assert.True(activation > endpoint);
        Assert.True(lookup > activation);
        Assert.True(sqlBlob > lookup);
        Assert.True(localFileInfo > sqlBlob);
    }

    [Fact]
    public void Service_Source_DoesNotOpenRequestedBaseDirectlyForAttachmentServe()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var serve = source.IndexOf("GetAttachmentForServeInternalAsync", StringComparison.Ordinal);
        Assert.True(serve >= 0);

        Assert.DoesNotContain("ResolveConnectionStringAsync", source);
        Assert.Contains("ResolveActiveTenantContext(idBase, \"GetAttachmentForServe\")", source);
        Assert.Contains("EnsureCanAttendConversationAsync(record.IdConversacion, connectionString", source);
        Assert.Contains("RutaLocalConfiable = !string.IsNullOrWhiteSpace(rutaLocal)", source);
        Assert.Contains("=> GetAttachmentForServeInternalAsync(idAdjunto, idBase, includeDownloadName, allowRemoteRecovery: true", source);
        Assert.Contains("SaveIncomingAttachmentAsync(long conversationId", source);
    }

    [Fact]
    public void ConversationsPage_AttachmentUrls_RepaintWhenUserTokenChanges()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

        Assert.Contains("AppUserSession.StateChanged += OnAppUserSessionChanged;", source);
        Assert.Contains("AppUserSession.StateChanged -= OnAppUserSessionChanged;", source);
        Assert.Contains("private string _attachmentUrlUserToken = string.Empty;", source);
        Assert.Contains("private string _attachmentUrlAuthSignature = string.Empty;", source);
        Assert.Contains("private int _attachmentUrlVersion;", source);
        Assert.Contains("private void OnAppUserSessionChanged()", source);
        Assert.Contains("NormalizeAttachmentUserToken(AppUserSession.CurrentToken)", source);
        Assert.Contains("BuildAttachmentAuthSignature(currentToken)", source);
        Assert.Contains("_attachmentUrlVersion++;", source);
        Assert.Contains("StateHasChanged();", source);
        Assert.Contains("Uri.EscapeDataString(_attachmentUrlUserToken)", source);
        Assert.Contains("return $\"{query}&av={_attachmentUrlVersion.ToString(CultureInfo.InvariantCulture)}\";", source);
        Assert.Contains("localStorage.getItem\", \"alfacore_user_token\"", source);
    }

    [Fact]
    public void WebhookStorageScope_Audit_TokenizedRouteSetsOverride_LegacyRouteDoesNotRewriteStorage()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        var tokenRoute = program.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        var tokenResolve = program.IndexOf("TryResolveWebhookTenantAsync(token, basesService, sessionService, ct)", tokenRoute, StringComparison.Ordinal);
        var tokenHandle = program.IndexOf("HandleWhatsAppMessageAsync(", tokenResolve, StringComparison.Ordinal);
        var resolver = program.IndexOf("internal static async Task<int?> TryResolveWebhookTenantAsync", StringComparison.Ordinal);
        var setOverride = program.IndexOf("sessionService.SetWebhookOverride(new SessionDto", resolver, StringComparison.Ordinal);

        Assert.True(tokenRoute >= 0);
        Assert.True(tokenResolve > tokenRoute);
        Assert.True(tokenHandle > tokenResolve);
        Assert.True(setOverride > resolver);

        var legacyRoute = program.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook\", (", StringComparison.Ordinal);
        var legacyHandler = program.IndexOf("HandleWhatsAppMessageAsync(request, configService, svc", legacyRoute, StringComparison.Ordinal);
        Assert.True(legacyRoute >= 0);
        Assert.True(legacyHandler > legacyRoute);

        Assert.Contains("private async Task<string> SaveIncomingAttachmentAsync(long conversationId", service);
        Assert.DoesNotContain("SaveIncomingAttachmentAsync(int tenantBaseId", service);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "AlfaCore.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }
}

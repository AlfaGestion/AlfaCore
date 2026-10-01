using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Regresión Base4264 (2026-10-01): las imágenes de Conversaciones dejaron de verse en hosts SaaS
/// (AlfaWeb y localhost con ModoSaaS=true) después del hardening de adjuntos (00b30fb1). El
/// endpoint GET /api/conversaciones/adjuntos/{id} es un request HTTP sin circuito Blazor: su guard
/// (Program.TryActivateAuthorizedAttachmentBase) llama GetActiveSession/GetAllSessions, que en SaaS
/// intentaban resolver la base desde la ruta leyendo NavigationManager.Uri -- sin inicializar en
/// ese request → InvalidOperationException → 500 → imagen/thumbnail/visor rotos. Mismo patrón que
/// el incidente del webhook (WhatsAppWebhookSessionResolutionTests). Se usa la clase real
/// ConexionClienteService, no un fake de ISessionService.
/// </summary>
public sealed class WhatsAppAttachmentSaaSSessionTests
{
    private const int BaseCliente = 4264;
    private const int BaseAjena = 4271;

    [Fact]
    public void AttachmentGuard_SaaSHttpRequestWithoutCircuit_AuthorizesUsersBaseWithoutThrowing()
    {
        var (session, appUser) = CreateSaaSSession(new FakeAppUserSession(authenticated: true, authorizedBaseIds: [BaseCliente]));

        var authorized = Program.TryActivateAuthorizedAttachmentBase(BaseCliente, session, appUser);

        Assert.True(authorized);
        Assert.Equal(BaseCliente, session.GetActiveSession()?.BaseId);
    }

    [Fact]
    public void AttachmentGuard_SaaS_CrossTenantBase_FailsClosed()
    {
        var (session, appUser) = CreateSaaSSession(new FakeAppUserSession(authenticated: true, authorizedBaseIds: [BaseCliente]));

        Assert.False(Program.TryActivateAuthorizedAttachmentBase(BaseAjena, session, appUser));
    }

    [Fact]
    public void AttachmentGuard_SaaS_BaseNotAuthorizedForUser_FailsClosed()
    {
        var (session, appUser) = CreateSaaSSession(new FakeAppUserSession(authenticated: true, authorizedBaseIds: []));

        Assert.False(Program.TryActivateAuthorizedAttachmentBase(BaseCliente, session, appUser));
    }

    [Fact]
    public void AttachmentGuard_SaaS_Unauthenticated_FailsClosed()
    {
        var (session, appUser) = CreateSaaSSession(new FakeAppUserSession(authenticated: false, authorizedBaseIds: [BaseCliente]));

        Assert.False(Program.TryActivateAuthorizedAttachmentBase(BaseCliente, session, appUser));
    }

    [Fact]
    public void GetActiveSession_SaaSWithoutCircuitAndWithoutUser_ReturnsNullInsteadOfThrowing()
    {
        var (session, _) = CreateSaaSSession(new FakeAppUserSession(authenticated: false, authorizedBaseIds: []));

        Assert.Null(session.GetActiveSession());
        Assert.Empty(session.GetAllSessions());
    }

    [Fact]
    public void ServePolicy_AdjuntoReal4264_JpegSeSirveInlineComoImagen()
    {
        var policy = Program.BuildAttachmentServePolicy("image/jpeg", "image_20261001_121357.jpg", downloadName: null, forceDownload: false);

        Assert.Equal("image/jpeg", policy.ContentType);
        Assert.Null(policy.FileDownloadName);
    }

    /// <summary>Imagen, thumbnail, visor y descarga: toda URL de /api/conversaciones/adjuntos/ que
    /// arma la UI lleva idBase y el token del usuario (el endpoint exige sesión real).</summary>
    [Fact]
    public void Source_UiAttachmentUrls_AlwaysCarryIdBaseAndUserToken()
    {
        var root = FindRepositoryRoot();
        foreach (var page in new[] { "Conversaciones.razor", "Tickets.razor" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "AlfaCore", "Components", "Pages", page));
            var builders = System.Text.RegularExpressions.Regex.Matches(source, @"\$""/api/conversaciones/adjuntos/[^""]*""");

            Assert.NotEmpty(builders);
            Assert.All(builders, m =>
            {
                Assert.Contains("idBase={GetAttachmentBaseId()}", m.Value);
                Assert.Contains("{GetAttachmentUserTokenQuery()}", m.Value);
            });
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    private static (ISessionService Session, IAppUserSessionService AppUser) CreateSaaSSession(FakeAppUserSession appUser)
    {
        var conexion = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true),
            appUser,
            new FakeCentralBases(),
            new NullCentralClientes(),
            new FakeHostEnvironment(),
            new UninitializedNavigationManager());
        return (new SessionService(conexion), appUser);
    }

    private static BaseCentralDto Base(int idBase, string idCliente) => new()
    {
        IdBase = idBase,
        IdCliente = idCliente,
        Nombre = $"Base {idBase}",
        DbServer = "server",
        DbName = $"AW_{idBase}",
        DbUser = "user",
        DbPassword = "pwd"
    };

    private sealed class FakeCentralBases : ICentralBasesService
    {
        // El usuario de prueba es del cliente 112010001, dueño sólo de la base 4264.
        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>(idCliente == "112010001" ? [Base(BaseCliente, "112010001")] : []);
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(idBase switch
            {
                BaseCliente => Base(BaseCliente, "112010001"),
                BaseAjena => Base(BaseAjena, "999"),
                _ => null
            });
        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>([Base(BaseCliente, "112010001"), Base(BaseAjena, "999")]);
        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default) => Task.FromResult<BaseCentralDto?>(null);
        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullCentralClientes : ICentralClientesService
    {
        public Task<ClienteCentralDto?> GetByIdClienteAsync(string idCliente, CancellationToken ct = default) => Task.FromResult<ClienteCentralDto?>(null);
        public Task<ClienteCentralDto?> GetByIdWebAsync(string idWeb, CancellationToken ct = default) => Task.FromResult<ClienteCentralDto?>(null);
        public Task<ClienteCentralDto?> GetByLicenciaPrincipalAsync(string licenciaPrincipal, CancellationToken ct = default) => Task.FromResult<ClienteCentralDto?>(null);
        public Task<IReadOnlyList<ClienteCentralDto>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ClienteCentralDto>>([]);
        public Task<string> GenerateAndSaveIdWebAsync(string idCliente, string razonSocial, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAppMode(bool isSaaSMode) : IAppModeService
    {
        public bool IsSaaSMode { get; } = isSaaSMode;
    }

    private sealed class FakeAppUserSession(bool authenticated, IReadOnlyCollection<int> authorizedBaseIds) : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => authenticated;
        public AppUserSessionInfo? CurrentUser => authenticated
            ? new AppUserSessionInfo { UserName = "evelybn", IdCliente = "112010001", IdWeb = "ALFANET" }
            : null;
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => authenticated ? "token" : null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => authenticated;
        public void Logout() { }
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => authenticated ? "evelybn" : fallback;
        public bool IsAuthorizedForSession(Guid? activeSessionId)
            => authenticated && activeSessionId is Guid id && authorizedBaseIds.Any(b => SessionDto.BuildGuidFromBaseId(b) == id);
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "AlfaCore.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }

    /// <summary>Como en un request HTTP sin circuito: cualquier acceso a Uri tira
    /// InvalidOperationException ("... has not been initialized.").</summary>
    private sealed class UninitializedNavigationManager : NavigationManager
    {
    }
}

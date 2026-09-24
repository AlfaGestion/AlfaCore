using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Regresión del incidente P0 2026-09-22: el webhook tokenizado de WhatsApp (con resolvedBaseId
/// ya resuelto server-side desde el token) devolvía 500 porque
/// <see cref="ConexionClienteService.GetActiveSession"/> intentaba resolver primero un override de
/// ruta (<c>ResolveRouteSessionOverride</c>), que toca <see cref="NavigationManager.Uri"/> --
/// inexistente en un POST HTTP sin circuito Blazor -- antes de siquiera mirar el webhook override
/// que <c>TryResolveWebhookTenantAsync</c> ya había seteado. Estos tests instancian la clase real
/// (no un fake de <see cref="ISessionService"/>) con un <see cref="NavigationManager"/> nunca
/// inicializado, exactamente como ocurre en un request de servidor a servidor de Meta.
/// </summary>
public sealed class WhatsAppWebhookSessionResolutionTests
{
    [Fact]
    public void WebhookOverride_ResolvesWithoutTouchingUninitializedNavigationManager()
    {
        var basesSpy = new SpyCentralBasesService();
        var session = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true),
            new FakeAppUserSession(),
            basesSpy,
            new FakeHostEnvironment(),
            new UninitializedNavigationManager());

        session.SetWebhookOverride(WebhookSession(baseId: 106, nombre: "ALFANET"));

        var active = session.GetActiveSession();

        Assert.NotNull(active);
        Assert.Equal(106, active!.BaseId);
        Assert.Equal("ALFANET", active.Nombre);
        // Si GetActiveSession hubiera intentado resolver el override de ruta primero, esta llamada
        // habría tocado NavigationManager.Uri (uninitialized => throw) antes de llegar acá.
        Assert.Equal(0, basesSpy.GetByIdCalls);
    }

    [Fact]
    public void WebhookOverride_TakesPriorityEvenWhenNavigationManagerIsInitializedWithADifferentRoute()
    {
        var navigationManager = new UninitializedNavigationManager();
        navigationManager.InitializeForTest("https://alfacentral.ddns.net/", "https://alfacentral.ddns.net/ALFANET/999/conversaciones");
        var basesSpy = new SpyCentralBasesService();
        var session = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true),
            new FakeAppUserSession(),
            basesSpy,
            new FakeHostEnvironment(),
            navigationManager);

        session.SetWebhookOverride(WebhookSession(baseId: 106, nombre: "ALFANET"));

        var active = session.GetActiveSession();

        // El override del webhook gana aunque el NavigationManager esté inicializado y "apunte" a
        // otra base (999): el token del webhook es la única autoridad para este request.
        Assert.Equal(106, active!.BaseId);
        Assert.Equal(0, basesSpy.GetByIdCalls);
    }

    [Fact]
    public void DifferentWebhookOverrides_NeverLeakBetweenBases()
    {
        var sessionForBaseA = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true), new FakeAppUserSession(),
            new SpyCentralBasesService(), new FakeHostEnvironment(), new UninitializedNavigationManager());
        sessionForBaseA.SetWebhookOverride(WebhookSession(baseId: 84, nombre: "BASE_A", dbName: "AW_84"));

        var sessionForBaseB = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true), new FakeAppUserSession(),
            new SpyCentralBasesService(), new FakeHostEnvironment(), new UninitializedNavigationManager());
        sessionForBaseB.SetWebhookOverride(WebhookSession(baseId: 106, nombre: "BASE_B", dbName: "AW_106"));

        var activeA = sessionForBaseA.GetActiveSession();
        var activeB = sessionForBaseB.GetActiveSession();

        Assert.Equal(84, activeA!.BaseId);
        Assert.Equal("AW_84", activeA.BaseDatos);
        Assert.Equal(106, activeB!.BaseId);
        Assert.Equal("AW_106", activeB.BaseDatos);
    }

    // Prueba de punta a punta con las clases REALES de producción (ConexionClienteService +
    // ConversacionesConfigService, no los fakes de ISessionService que usa
    // WhatsAppTenantIsolationTests para aislar el resto del pipeline del webhook): confirma que un
    // webhook tokenizado para la Base 106 no puede leer la configuración de WhatsApp de otra base
    // (4264), y que el rechazo ocurre antes de intentar abrir conexión SQL alguna.
    [Fact]
    public async Task RealSessionChain_WebhookOverrideForBaseA_CannotResolveConfigOfBaseB()
    {
        var session = new ConexionClienteService(
            new FakeAppMode(isSaaSMode: true),
            new FakeAppUserSession(),
            new SpyCentralBasesService(),
            new FakeHostEnvironment(),
            new UninitializedNavigationManager());
        session.SetWebhookOverride(WebhookSession(baseId: 106, nombre: "BASE_A"));

        var configService = new ConversacionesConfigService(
            new ConfigurationBuilder().Build(),
            new SessionService(session),
            ThrowingProxy<IAppEventService>(),
            Options.Create(new AlfaCore.Configuration.WhatsAppOptions()),
            new NullHttpClientFactory(),
            ThrowingProxy<IAppUserSessionService>(),
            ThrowingProxy<IConversacionesAuthorizationService>(),
            ThrowingProxy<ICentralBasesService>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => configService.GetWhatsAppConfigAsync(expectedBaseId: 4264));

        Assert.Contains("no coincide", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Regresión estructural del stack exacto reportado en producción (2026-09-22, ~10:15):
    // ResolveRouteSessionOverride -> GetActiveSession -> ResolveTenantConnection ->
    // GetWhatsAppConfigAsync -> HandleWhatsAppMessageAsync. Introducido por el commit ff676aaa4
    // (2026-09-21, "Corrección de bug de conexión ... al recargar la web"), que insertó la
    // resolución de ruta ANTES del check de _webhookOverride en GetActiveSession. Este test fija en
    // el texto fuente que (a) GetActiveSession mira el webhook override antes que cualquier cosa que
    // toque NavigationManager, y (b) ResolveTenantConnection resuelve por GetWebhookOverride(base
    // esperada) sin pasar por GetActiveSession cuando el caller ya trae un BaseId autoritativo.
    [Fact]
    public void GetActiveSession_ChecksWebhookOverrideBeforeAnyNavigationManagerAccess()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Services", "ConexionClienteService.cs"));
        var method = source.IndexOf("public SessionDto? GetActiveSession()", StringComparison.Ordinal);
        var webhookCheck = source.IndexOf("if (_webhookOverride is not null)", method, StringComparison.Ordinal);
        var routeResolution = source.IndexOf("ResolveRouteSessionOverride()", method, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(webhookCheck > method && webhookCheck < routeResolution,
            "El webhook override debe resolverse ANTES de tocar NavigationManager vía ResolveRouteSessionOverride.");
    }

    [Fact]
    public void ResolveTenantConnection_UsesWebhookOverrideDirectly_BeforeCallingGetActiveSession()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Services", "ConversacionesConfigService.cs"));
        var method = source.IndexOf("private TenantConnectionContext ResolveTenantConnection(int? expectedBaseId, string operation)", StringComparison.Ordinal);
        var webhookBypass = source.IndexOf("sessionService.GetWebhookOverride(expectedBaseId.Value)", method, StringComparison.Ordinal);
        var activeSessionCall = source.IndexOf("sessionService.GetActiveSession()", method, StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.True(webhookBypass > method && webhookBypass < activeSessionCall,
            "GetWebhookOverride(expectedBaseId) debe intentarse antes que GetActiveSession().");
    }

    [Fact]
    public void HandleWhatsAppMessageAsync_PassesResolvedBaseIdIntoGetWhatsAppConfigAsync()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Program.cs"));
        var method = source.IndexOf("internal static async Task<IResult> HandleWhatsAppMessageAsync", StringComparison.Ordinal);

        Assert.True(method >= 0);
        Assert.Contains("configService.GetWhatsAppConfigAsync(resolvedBaseId, ct)", source[method..], StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }

    private static SessionDto WebhookSession(int baseId, string nombre, string dbName = "AW_TEST") => new()
    {
        Id = SessionDto.BuildGuidFromBaseId(baseId),
        BaseId = baseId,
        Nombre = nombre,
        Servidor = "invalid-server",
        BaseDatos = dbName,
        Usuario = "sa",
        Password = "pwd",
        TrustServerCertificate = true,
        Activa = true
    };

    private static TService ThrowingProxy<TService>() where TService : class
        => System.Reflection.DispatchProxy.Create<TService, WhatsAppTenantIsolationTests.ThrowingProxy>();

    private sealed class FakeAppMode(bool isSaaSMode) : IAppModeService
    {
        public bool IsSaaSMode { get; } = isSaaSMode;
    }

    private sealed class FakeAppUserSession : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => false;
        public AppUserSessionInfo? CurrentUser => null;
        public bool RequiresInternalLogin => false;
        public string? CurrentToken => null;
        public Task<AppUserSessionInfo> LoginAsync(string userName, string password, CancellationToken ct = default)
            => throw new NotSupportedException();
        public void AdoptInternalUser(AppUserSessionInfo internalUser) => throw new NotSupportedException();
        public bool TryRestoreFromToken(string token) => false;
        public void Logout() { }
        public void HandleSqlSessionChanged() { }
        public string GetCurrentUserName(string fallback = "") => fallback;
        public bool IsAuthorizedForSession(Guid? activeSessionId) => false;
        public void EnsureAuthorizedForSession(Guid? activeSessionId) { }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "AlfaCore.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }

    /// <summary>
    /// Espía de <see cref="ICentralBasesService"/>: si <see cref="GetByIdCalls"/> queda en 0 después
    /// de <c>GetActiveSession()</c>, es prueba directa de que nunca se llegó a
    /// <c>ResolveRouteSessionOverride</c> (que es el único llamador de <c>GetByIdAsync</c> en
    /// ConexionClienteService) -- y por lo tanto tampoco se tocó NavigationManager.Uri.
    /// </summary>
    private sealed class SpyCentralBasesService : ICentralBasesService
    {
        public int GetByIdCalls { get; private set; }

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>([]);

        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        {
            GetByIdCalls++;
            return Task.FromResult<BaseCentralDto?>(null);
        }

        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>([]);

        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(null);

        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>
    /// Subclase mínima de <see cref="NavigationManager"/> que reproduce el comportamiento real de
    /// <c>RemoteNavigationManager</c> en un request sin circuito: si nadie llama a
    /// <see cref="InitializeForTest"/>, cualquier acceso a <see cref="NavigationManager.Uri"/> tira
    /// <see cref="InvalidOperationException"/> ("... has not been initialized."), igual que en
    /// producción.
    /// </summary>
    private sealed class UninitializedNavigationManager : NavigationManager
    {
        public void InitializeForTest(string baseUri, string uri) => Initialize(baseUri, uri);
    }
}

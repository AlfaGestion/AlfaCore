using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Aislamiento multitenant de la resolución de sesión por URL (auditoría 2026-09-24):
/// - una ruta root de la app (/consultas/12, /catalogo/5) NO es /{idweb}/{idbase};
/// - /{idweb}/{idbase} con idweb que no es el dueño de la base falla cerrado;
/// - en SaaS sin base activa, los servicios tenant-sensitive no abren conexión contra
///   ConnectionStrings:AlfaGestion (base global);
/// - el webhook override sigue teniendo prioridad y no toca NavigationManager.
/// Se usan las clases reales (ConexionClienteService + SessionService + servicios de dominio);
/// sólo ALFA_CENTRAL (bases/clientes) está simulada.
/// </summary>
public sealed class TenantRouteSessionIsolationTests
{
    private const string UnreachableAlfaGestion = "Server=tcp:alfagestion-global.invalid,1433;Database=ALFANET2007;User Id=x;Password=y;Connect Timeout=1";

    [Theory]
    [InlineData("consultas/12")]
    [InlineData("/consultas/12/editar")]
    [InlineData("catalogo/5")]
    [InlineData("carrito/7")]
    [InlineData("carrito-general/3")]
    [InlineData("interfaces/99")]
    [InlineData("auditoria/error/15")]
    [InlineData("auditoria/errores")]
    [InlineData("ALFANET/0/auditoria")]
    [InlineData("ALFANET")]
    [InlineData("")]
    public void TenantRouteParser_RejectsAppRootRoutes(string path)
    {
        Assert.False(TenantRouteParser.TryParse(path, out _, out _));
    }

    [Theory]
    [InlineData("ALFANET/4264", "ALFANET", 4264)]
    [InlineData("/ALFANET/4264/auditoria/errores?x=1", "ALFANET", 4264)]
    [InlineData("alfanet/4264/auditoria/error/15#top", "alfanet", 4264)]
    public void TenantRouteParser_AcceptsRealTenantRoutes(string path, string expectedIdWeb, int expectedBaseId)
    {
        Assert.True(TenantRouteParser.TryParse(path, out var idWeb, out var baseId));
        Assert.Equal(expectedIdWeb, idWeb);
        Assert.Equal(expectedBaseId, baseId);
    }

    [Theory]
    [InlineData("https://alfanetweb.ddns.net/consultas/12", 12)]
    [InlineData("https://alfanetweb.ddns.net/catalogo/5", 5)]
    public void AppRootRouteWithNumber_DoesNotActivateThatBase(string uri, int wouldBeBaseId)
    {
        var central = new FakeCentral().WithBase(wouldBeBaseId, idCliente: "C_OTRO", idWeb: "consultas");
        var session = CreateSession(uri, central);

        Assert.Null(session.GetActiveSession());
        // Ni siquiera se consulta ALFA_CENTRAL por esa "base": la forma de la ruta ya no es tenant.
        Assert.Equal(0, central.GetBaseByIdCalls);
    }

    [Fact]
    public void ValidTenantRoute_ActivatesThatBase()
    {
        var central = new FakeCentral().WithBase(4264, idCliente: "C_ALFANET", idWeb: "ALFANET");
        var session = CreateSession("https://alfanetweb.ddns.net/ALFANET/4264/auditoria/errores", central);

        var active = session.GetActiveSession();

        Assert.NotNull(active);
        Assert.Equal(4264, active!.BaseId);
    }

    [Fact]
    public void IdWebIdBaseMismatch_FailsClosed_EvenWithACachedUserSession()
    {
        var central = new FakeCentral().WithBase(4264, idCliente: "C_ALFANET", idWeb: "ALFANET");
        // Usuario central autenticado de ALFANET con la base 4264 en su lista: sin la URL, la sesión
        // normal resolvería 4264. Con una URL tenant de OTRA empresa no debe caer a esa sesión.
        var user = new AppUserSessionInfo { UserName = "EVE", IdCliente = "C_ALFANET", IdWeb = "ALFANET" };
        var session = CreateSession("https://alfanetweb.ddns.net/OTRAEMPRESA/4264/auditoria/errores", central, user);

        Assert.Null(session.GetActiveSession());
        Assert.Equal(string.Empty, session.GetConnectionString());
    }

    [Fact]
    public void UnknownBaseInTenantRoute_FailsClosed()
    {
        var central = new FakeCentral();
        var user = new AppUserSessionInfo { UserName = "EVE", IdCliente = "C_ALFANET", IdWeb = "ALFANET" };
        central.WithBase(4264, idCliente: "C_ALFANET", idWeb: "ALFANET");
        var session = CreateSession("https://alfanetweb.ddns.net/ALFANET/9999/auditoria", central, user);

        Assert.Null(session.GetActiveSession());
    }

    [Fact]
    public void CentralUnavailable_FailsClosed()
    {
        var central = new FakeCentral { ThrowOnLookup = true };
        var session = CreateSession("https://alfanetweb.ddns.net/ALFANET/4264/auditoria", central);

        Assert.Null(session.GetActiveSession());
    }

    [Fact]
    public void RootRouteWithoutTenant_StillUsesNormalUserSession()
    {
        // Prioridad WebhookOverride > RouteSessionOverride > sesión normal: una ruta root (sin forma
        // tenant) no se rechaza, sigue resolviendo la sesión normal del usuario central.
        var central = new FakeCentral().WithBase(4264, idCliente: "C_ALFANET", idWeb: "ALFANET");
        var user = new AppUserSessionInfo { UserName = "EVE", IdCliente = "C_ALFANET", IdWeb = "ALFANET" };
        var session = CreateSession("https://alfanetweb.ddns.net/auditoria/errores", central, user);

        Assert.Equal(4264, session.GetActiveSession()?.BaseId);
    }

    [Fact]
    public void WebhookOverride_KeepsPriorityOverAnyRoute_WithoutCentralLookups()
    {
        var central = new FakeCentral().WithBase(4264, idCliente: "C_ALFANET", idWeb: "ALFANET");
        var session = CreateSession("https://alfanetweb.ddns.net/OTRAEMPRESA/4264/auditoria", central);
        session.SetWebhookOverride(new SessionDto
        {
            Id = SessionDto.BuildGuidFromBaseId(106),
            BaseId = 106,
            Nombre = "WEBHOOK",
            Servidor = "invalid-server",
            BaseDatos = "AW_106",
            Usuario = "sa",
            Password = "pwd",
            Activa = true
        });

        Assert.Equal(106, session.GetActiveSession()?.BaseId);
        Assert.Equal(0, central.GetBaseByIdCalls);
        Assert.Equal(0, central.GetClienteCalls);
    }

    [Fact]
    public void WebhookOverride_NeverTouchesUninitializedNavigationManager()
    {
        var central = new FakeCentral();
        var session = new ConexionClienteService(
            new FakeAppMode(true), new FakeAppUserSession(null), central, central,
            new FakeHostEnvironment(), new TestNavigationManager());
        session.SetWebhookOverride(new SessionDto { Id = SessionDto.BuildGuidFromBaseId(84), BaseId = 84, Servidor = "x", BaseDatos = "AW_84", Activa = true });

        Assert.Equal(84, session.GetActiveSession()?.BaseId);
    }

    [Fact]
    public async Task RootAuditoriaWithoutSession_DoesNotQueryGlobalAlfaGestion()
    {
        // /auditoria/errores abierto directo: sin sesión tenant. Antes AuditoriaService caía a
        // ConnectionStrings:AlfaGestion y leía AUX_ERR de esa base global. Ahora falla cerrado
        // ANTES de abrir conexión (si intentara conectar, el error sería SqlException, no este).
        var central = new FakeCentral();
        var session = CreateSession("https://alfanetweb.ddns.net/auditoria/errores", central);
        var auditoria = new AuditoriaService(
            GlobalAlfaGestionConfiguration(),
            new SessionService(session),
            ThrowingProxy<IAppEventService>(),
            new FakeAppMode(true));

        await Assert.ThrowsAsync<TenantSessionRequiredException>(() => auditoria.GetResumenAsync());
    }

    [Fact]
    public async Task RootMenuWithoutSession_ReturnsEmptyWithoutQueryOrPermissionLookups()
    {
        var central = new FakeCentral();
        var session = CreateSession("https://alfanetweb.ddns.net/auditoria", central);
        var menu = new MenuService(
            GlobalAlfaGestionConfiguration(),
            new SessionService(session),
            ThrowingProxy<IPermissionService>(),
            ThrowingProxy<IAppEventService>(),
            ThrowingProxy<IActualizacionesService>(),
            new FakeAppUserSession(null),
            ThrowingProxy<ICentralAdminService>(),
            new FakeAppMode(true));

        var modules = await menu.GetModulesAsync();

        Assert.Empty(modules);
    }

    [Fact]
    public void LegacyInstallation_KeepsAlfaGestionFallback()
    {
        var ok = TenantConnectionGuard.TryResolve(
            System.Reflection.DispatchProxy.Create<ISessionService, NoActiveSessionProxy>(),
            GlobalAlfaGestionConfiguration(),
            new FakeAppMode(false),
            out var connectionString);

        Assert.True(ok);
        Assert.Equal(UnreachableAlfaGestion, connectionString);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]  // SaaS, sin usuario: no se monta la página
    [InlineData(true, true, false, false, false)]   // SaaS, login interno pendiente para la base
    [InlineData(true, true, true, false, true)]     // SaaS, autorizado
    [InlineData(true, false, false, true, true)]    // SaaS, raíz "/" (Launcher redirige a /login)
    [InlineData(false, false, false, false, true)]  // instalación clásica: sin cambios
    public void TenantBodyGate_OnlyMountsPageWhenAuthorized(bool saas, bool auth, bool authorized, bool root, bool expected)
    {
        Assert.Equal(expected, TenantBodyGate.ShouldRenderBody(saas, auth, authorized, root));
    }

    [Theory]
    [InlineData("/consultas/12", false, "")]
    [InlineData("/catalogo/5", false, "")]
    [InlineData("/ALFANET/4264/auditoria", true, "/ALFANET/4264")]
    public void RouteContext_SaaSPrefixUsesTenantRouteRule(string route, bool expected, string expectedPrefix)
    {
        Assert.Equal(expected, RouteContextService.TryGetSaaSPrefix(route, out var prefix));
        Assert.Equal(expectedPrefix, prefix);
    }

    [Fact]
    public void MainLayout_MountsBodyOnlyThroughTenantGate()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AlfaCore", "Components", "Layout", "MainLayout.razor"));
        var bodyCount = CountOccurrences(source, "@Body");
        var gatedBodyCount = CountOccurrences(source, "@if (ShouldRenderTenantBody)\n        {\n            @Body")
            + CountOccurrences(source, "@if (ShouldRenderTenantBody)\r\n        {\r\n            @Body")
            + CountOccurrences(source, "@if (ShouldRenderTenantBody)\n            {\n                @Body")
            + CountOccurrences(source, "@if (ShouldRenderTenantBody)\r\n            {\r\n                @Body");

        Assert.True(bodyCount > 0);
        Assert.Equal(bodyCount, gatedBodyCount);
    }

    private static ConexionClienteService CreateSession(string uri, FakeCentral central, AppUserSessionInfo? user = null)
    {
        var navigation = new TestNavigationManager();
        navigation.InitializeForTest("https://alfanetweb.ddns.net/", uri);
        return new ConexionClienteService(
            new FakeAppMode(true),
            new FakeAppUserSession(user),
            central,
            central,
            new FakeHostEnvironment(),
            navigation);
    }

    private static IConfiguration GlobalAlfaGestionConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AlfaGestion"] = UnreachableAlfaGestion })
            .Build();

    private static TService ThrowingProxy<TService>() where TService : class
        => System.Reflection.DispatchProxy.Create<TService, WhatsAppTenantIsolationTests.ThrowingProxy>();

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }

    private sealed class FakeCentral : ICentralBasesService, ICentralClientesService
    {
        private readonly Dictionary<int, BaseCentralDto> _bases = [];
        private readonly Dictionary<string, ClienteCentralDto> _clientes = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOnLookup { get; init; }
        public int GetBaseByIdCalls { get; private set; }
        public int GetClienteCalls { get; private set; }

        public FakeCentral WithBase(int idBase, string idCliente, string idWeb)
        {
            _bases[idBase] = new BaseCentralDto
            {
                IdBase = idBase,
                IdCliente = idCliente,
                Nombre = $"BASE_{idBase}",
                DbServer = "invalid-server",
                DbName = $"AW_{idBase}",
                DbUser = "sa",
                DbPassword = "pwd"
            };
            _clientes[idCliente] = new ClienteCentralDto { IdCliente = idCliente, IdWeb = idWeb };
            return this;
        }

        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
        {
            GetBaseByIdCalls++;
            if (ThrowOnLookup)
                throw new InvalidOperationException("ALFA_CENTRAL no disponible.");
            return Task.FromResult(_bases.TryGetValue(idBase, out var b) ? b : null);
        }

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>(_bases.Values.Where(b => string.Equals(b.IdCliente, idCliente, StringComparison.OrdinalIgnoreCase)).ToArray());

        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>(_bases.Values.ToArray());

        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(null);

        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<ClienteCentralDto?> GetByIdClienteAsync(string idCliente, CancellationToken ct = default)
        {
            GetClienteCalls++;
            return Task.FromResult(_clientes.TryGetValue(idCliente, out var c) ? c : null);
        }

        public Task<ClienteCentralDto?> GetByIdWebAsync(string idWeb, CancellationToken ct = default)
            => Task.FromResult(_clientes.Values.FirstOrDefault(c => string.Equals(c.IdWeb, idWeb, StringComparison.OrdinalIgnoreCase)));

        public Task<ClienteCentralDto?> GetByLicenciaPrincipalAsync(string licenciaPrincipal, CancellationToken ct = default)
            => Task.FromResult<ClienteCentralDto?>(null);

        Task<IReadOnlyList<ClienteCentralDto>> ICentralClientesService.GetAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ClienteCentralDto>>(_clientes.Values.ToArray());

        public Task<string> GenerateAndSaveIdWebAsync(string idCliente, string razonSocial, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    public class NoActiveSessionProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                nameof(ISessionService.GetConnectionString) => string.Empty,
                nameof(ISessionService.GetActiveSession) => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
    }

    private sealed class FakeAppMode(bool isSaaSMode) : IAppModeService
    {
        public bool IsSaaSMode { get; } = isSaaSMode;
    }

    private sealed class FakeAppUserSession(AppUserSessionInfo? user) : IAppUserSessionService
    {
        public event Action? StateChanged { add { } remove { } }
        public bool IsAuthenticated => user is not null;
        public AppUserSessionInfo? CurrentUser => user;
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

    private sealed class TestNavigationManager : NavigationManager
    {
        public void InitializeForTest(string baseUri, string uri) => Initialize(baseUri, uri);
    }
}

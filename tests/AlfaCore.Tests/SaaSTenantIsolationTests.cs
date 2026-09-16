using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class SaaSTenantIsolationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task RouteBaseActivatesExpectedTenantBeforeReturningSnapshot()
    {
        var session = new FakeSessionService(Session(106));
        var guard = new SaaSTenantRouteGuard(new FakeAppMode(true), session, new FakeCentralBasesService());

        var context = await guard.ResolveAsync(4264, "ALFANET");

        Assert.True(context.IsValid);
        Assert.Equal(4264, context.IdBase);
        Assert.Equal(4264, session.GetActiveSession()?.BaseId);
        Assert.Contains("AW_4264", context.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServiceGuardRejectsActiveSessionMismatchBeforeSql()
    {
        var session = new FakeSessionService(Session(106));
        var guard = new SaaSTenantRouteGuard(new FakeAppMode(true), session, new FakeCentralBasesService());

        var error = Assert.Throws<InvalidOperationException>(() => guard.GetRequiredConnection(4264, "test"));

        Assert.Contains("4264", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LateLeaseIsNotCurrentAfterRouteChanges()
    {
        var session = new FakeSessionService(Session(106));
        var guard = new SaaSTenantRouteGuard(new FakeAppMode(true), session, new FakeCentralBasesService());

        var oldLease = (await guard.ResolveAsync(106, "BASEB")).Lease;
        var current = await guard.ResolveAsync(4264, "BASEA");

        Assert.False(guard.IsCurrent(oldLease, 4264, session.GetActiveSession()?.BaseId));
        Assert.True(guard.IsCurrent(current.Lease, 4264, session.GetActiveSession()?.BaseId));
    }

    [Fact]
    public void CatalogoCarrito_ResolvesTenantBeforePublicCartAndStorageIsTenantScoped()
    {
        var source = Read("src", "AlfaCore", "Components", "Pages", "CatalogoCarrito.razor");
        var legacy = source.IndexOf("RedirectLegacyPublicRouteAsync", StringComparison.Ordinal);
        var resolve = source.IndexOf("TenantRouteGuard.ResolveAsync(idbase, idweb)", legacy, StringComparison.Ordinal);
        var read = source.IndexOf("CarritoComprasSvc.GetPublicCartAsync(IdInsert, idweb, null, routeContext.IdBase)", legacy, StringComparison.Ordinal);

        Assert.True(resolve > legacy && read > resolve);
        Assert.Contains("alfacore_catalogo_carrito_{NormalizeRouteSegment(idweb)}_{(_effectiveIdBase ?? 0)", source, StringComparison.Ordinal);
        Assert.Contains("GetPublicCartAsync(IdInsert, idweb, CatalogoClienteSession.CurrentClient?.CodigoCliente, _effectiveIdBase)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InterfacesPages_PassExpectedBaseIdToCriticalReads()
    {
        var list = Read("src", "AlfaCore", "Components", "Pages", "Interfaces.razor");
        Assert.Contains("TenantRouteGuard.ResolveAsync(idbase, idweb)", list, StringComparison.Ordinal);
        Assert.Contains("InterfacesSvc.SearchAsync(_filters, expectedBaseId)", list, StringComparison.Ordinal);
        Assert.Contains("InterfacesSvc.GetByIdAsync(idComprobanteRecibido, expectedBaseId)", list, StringComparison.Ordinal);

        var editor = Read("src", "AlfaCore", "Components", "Pages", "InterfacesEditor.razor");
        Assert.Contains("InterfacesSvc.GetByIdAsync(IdComprobanteRecibido.Value, expectedBaseId)", editor, StringComparison.Ordinal);
        Assert.Contains("InterfacesSvc.FindFilesByHashAsync(hashes, CurrentExpectedBaseId())", editor, StringComparison.Ordinal);
        Assert.Contains("InterfacesSvc.CreateAsync(new InterfacesCrearComprobanteRequest", editor, StringComparison.Ordinal);
        Assert.Contains("}, CurrentExpectedBaseId())", editor, StringComparison.Ordinal);

        var config = Read("src", "AlfaCore", "Components", "Pages", "InterfacesConfiguracion.razor");
        Assert.Contains("ConfigSvc.GetUploadSettingsAsync(expectedBaseId)", config, StringComparison.Ordinal);
        Assert.Contains("ConfigSvc.GetCompraIaSettingsAsync(expectedBaseId)", config, StringComparison.Ordinal);
    }

    [Fact]
    public void InterfacesCatalogos_DiscardLateSearchAndUsesRouteBaseForPublicLinks()
    {
        var source = Read("src", "AlfaCore", "Components", "Pages", "InterfacesCatalogos.razor");

        Assert.Contains("var routeKey = BuildRouteKey();", source, StringComparison.Ordinal);
        Assert.Contains("traceId != _catalogosSearchSequence", source, StringComparison.Ordinal);
        Assert.Contains("idbase ?? SessionSvc.GetActiveSession()?.BaseId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceAndPortalCaches_AreTenantGuarded()
    {
        var launcher = Read("src", "AlfaCore", "Components", "Pages", "Launcher.razor");
        Assert.Contains("CanUseActiveSessionForRoute() && WorkspaceSvc.TryGetCachedHome", launcher, StringComparison.Ordinal);

        var workspace = Read("src", "AlfaCore", "Components", "Pages", "ShellWorkspacePage.razor");
        Assert.Contains("CanUseActiveSessionForRoute() && WorkspaceSvc.TryGetCachedModuleWorkspace", workspace, StringComparison.Ordinal);

        var portalBase = Read("src", "AlfaCore", "Components", "Pages", "PortalClientePageBase.cs");
        Assert.Contains("SeccionesCache.Get(idweb, _effectiveIdBase)", portalBase, StringComparison.Ordinal);
        Assert.Contains("SeccionesCache.Set(idweb, _effectiveIdBase, PortalSecciones)", portalBase, StringComparison.Ordinal);

        var portalCache = Read("src", "AlfaCore", "Services", "PortalClienteSeccionesCache.cs");
        Assert.Contains("ConfiguracionVentasPortalClienteDto? Get(string? idWeb, int? idBase)", portalCache, StringComparison.Ordinal);
    }

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AlfaCore.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No se pudo ubicar la raíz del repositorio.");
    }

    private static SessionDto Session(int baseId)
        => new()
        {
            Id = SessionDto.BuildGuidFromBaseId(baseId),
            BaseId = baseId,
            Nombre = $"Base {baseId}",
            Servidor = ".",
            BaseDatos = $"AW_{baseId}",
            Usuario = "sa",
            Password = "pwd",
            Activa = true
        };

    private sealed class FakeAppMode(bool isSaaSMode) : IAppModeService
    {
        public bool IsSaaSMode { get; } = isSaaSMode;
    }

    private sealed class FakeCentralBasesService : ICentralBasesService
    {
        public Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(new()
            {
                IdBase = idBase,
                IdCliente = "C",
                Nombre = $"Base {idBase}",
                DbServer = ".",
                DbName = $"AW_{idBase}",
                DbUser = "sa",
                DbPassword = "pwd"
            });

        public Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>([]);

        public Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BaseCentralDto>>([]);

        public Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default)
            => Task.FromResult<BaseCentralDto?>(null);

        public Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default)
            => Task.FromResult("token");
    }

    private sealed class FakeSessionService(SessionDto? active) : ISessionService
    {
        private SessionDto? _active = active;
        public event Action? SessionChanged;
        public string GetConnectionString() => _active is null ? string.Empty : $"Server={_active.Servidor};Database={_active.BaseDatos};User Id={_active.Usuario};Password={_active.Password};TrustServerCertificate=True";
        public SessionDto? GetActiveSession() => _active;
        public void SetWebhookOverride(SessionDto session) { _active = session; SessionChanged?.Invoke(); }
        public void ClearWebhookOverride() => _active = null;
        public IReadOnlyList<SessionDto> GetAllSessions() => _active is null ? [] : [_active];
        public void SwitchSession(Guid id) { }
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => Guid.NewGuid();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) { }
        public void DeleteSession(Guid id) { }
        public void ClearActiveSession() => _active = null;
    }
}

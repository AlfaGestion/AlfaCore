using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AlfaCore.Components.Pages;

// Logica compartida por todas las paginas del Portal Cliente (Home, Pedidos, Detalle, etc.):
// resolucion de sesion SaaS, restauracion/login/logout de cliente e identidad publica (logo/nombre).
// Cada pagina define su propio markup; esta clase solo evita duplicar el mismo codigo 6 veces.
public abstract class PortalClientePageBase : SaaSRoutePageBase, IAsyncDisposable
{
    private const string ClientTokenStorageKey = "alfacore_catalogo_cliente_token";

    [Inject] protected IInterfacesCatalogosService CatalogosSvc { get; set; } = null!;
    [Inject] protected ICatalogosClienteSessionService CatalogoClienteSession { get; set; } = null!;
    [Inject] protected ICentralBasesService CentralBasesSvc { get; set; } = null!;
    [Inject] protected IAppUiOperationService UiOps { get; set; } = null!;
    [Inject] protected IAppModeService AppMode { get; set; } = null!;
    [Inject] protected ISessionService SessionSvc { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    [Inject] protected IJSRuntime Js { get; set; } = null!;

    protected bool NotFound { get; private set; }
    protected bool SessionBusy { get; private set; }
    protected string ClienteCodigo { get; set; } = string.Empty;
    protected string ClientePassword { get; set; } = string.Empty;
    protected bool ShowPassword { get; set; }
    protected AppUiMessage? LoginFeedback { get; private set; }
    protected CatalogosPublicIdentityDto Branding { get; private set; } = new();

    private bool _restoreAttempted;

    protected bool IsAuthenticated
        => CatalogoClienteSession.CurrentClient is { } client
           && string.Equals(client.IdWeb, NormalizeRouteSegment(idweb), StringComparison.OrdinalIgnoreCase)
           && client.IdBase == (idbase ?? 0);

    protected override async Task OnInitializedAsync()
    {
        CatalogoClienteSession.StateChanged += OnClientSessionStateChanged;
        await EnsureRouteSessionAsync();
        await LoadPublicIdentityAsync();
        await OnPortalInitializedAsync();
    }

    protected override async Task OnParametersSetAsync()
        => await EnsureRouteSessionAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_restoreAttempted)
        {
            _restoreAttempted = true;
            await RestoreClientSessionAsync();
            await OnAfterClientSessionRestoredAsync();
        }
    }

    // Hook para que cada pagina cargue sus propios datos (ej. pedidos) despues de inicializar.
    protected virtual Task OnPortalInitializedAsync() => Task.CompletedTask;

    // Hook para que cada pagina reaccione una vez restaurada (o no) la sesion desde localStorage.
    protected virtual Task OnAfterClientSessionRestoredAsync() => Task.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        CatalogoClienteSession.StateChanged -= OnClientSessionStateChanged;
        await Task.CompletedTask;
    }

    private void OnClientSessionStateChanged()
        => _ = InvokeAsync(StateHasChanged);

    protected int? GetEffectiveIdBase() => _effectiveIdBase;

    private int? _effectiveIdBase;

    private async Task EnsureRouteSessionAsync()
    {
        if (idbase is > 0)
        {
            _effectiveIdBase = idbase;
        }
        else
        {
            var sessionBaseId = SessionSvc.GetActiveSession()?.BaseId ?? 0;
            _effectiveIdBase = sessionBaseId > 0 ? sessionBaseId : null;
        }

        if (!AppMode.IsSaaSMode || !idbase.HasValue)
            return;

        var routeBaseId = idbase.Value;
        var routeBase = await CentralBasesSvc.GetByIdAsync(routeBaseId);
        if (routeBase is null)
        {
            NotFound = true;
            return;
        }

        NotFound = false;

        SessionSvc.SetWebhookOverride(new SessionDto
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{routeBaseId:000000000000}"),
            BaseId = routeBase.IdBase,
            Nombre = routeBase.Nombre,
            Servidor = routeBase.DbServer,
            BaseDatos = routeBase.DbName,
            Usuario = routeBase.DbUser,
            Password = routeBase.DbPassword,
            TrustServerCertificate = true,
            Activa = true
        });
    }

    private async Task LoadPublicIdentityAsync()
    {
        try
        {
            Branding = await CatalogosSvc.GetPublicIdentityAsync(idweb);
        }
        catch
        {
            Branding = new CatalogosPublicIdentityDto();
        }
    }

    private async Task RestoreClientSessionAsync()
    {
        try
        {
            var token = await Js.InvokeAsync<string?>("localStorage.getItem", ClientTokenStorageKey);
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (!CatalogoClienteSession.TryRestoreFromToken(token))
                await Js.InvokeVoidAsync("localStorage.removeItem", ClientTokenStorageKey);
        }
        catch
        {
        }
    }

    protected async Task LoginClienteAsync()
    {
        LoginFeedback = null;
        SessionBusy = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await UiOps.RunAsync(() => CatalogoClienteSession.LoginAsync(new CatalogosClienteLoginRequestDto
            {
                CodigoCliente = ClienteCodigo,
                Password = ClientePassword,
                IdWeb = idweb,
                IdBase = idbase,
                IdInsert = 0
            }), "No pudimos validar el acceso en este momento. Intentá nuevamente.");

            if (!result.Success || result.Value is null)
            {
                LoginFeedback = result.Feedback;
                ClientePassword = string.Empty;
                return;
            }

            await Js.InvokeVoidAsync("localStorage.setItem", ClientTokenStorageKey, CatalogoClienteSession.CurrentToken ?? string.Empty);
            ClientePassword = string.Empty;
            await OnAfterLoginAsync();
        }
        finally
        {
            SessionBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected virtual Task OnAfterLoginAsync() => Task.CompletedTask;

    protected async Task LogoutClienteAsync()
    {
        SessionBusy = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            CatalogoClienteSession.Logout();
            await Js.InvokeVoidAsync("localStorage.removeItem", ClientTokenStorageKey);
        }
        finally
        {
            SessionBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected string GetCurrentClientLabel()
        => CatalogoClienteSession.CurrentClient?.RazonSocial?.Trim()
           ?? CatalogoClienteSession.CurrentClient?.CodigoCliente?.Trim()
           ?? string.Empty;

    protected string GetCurrentCodigoCliente()
        => CatalogoClienteSession.CurrentClient?.CodigoCliente?.Trim() ?? string.Empty;

    protected string GetBrandingName()
        => string.IsNullOrWhiteSpace(Branding.NombreVisible)
            ? "Catálogos"
            : Branding.NombreVisible.Trim();

    protected string GetBrandingLogoUrl()
        => string.IsNullOrWhiteSpace(Branding.LogoUrl)
            ? "/logos/Logo.png"
            : Branding.LogoUrl.Trim();

    protected string GetBrandingLogoFrameClass()
        => Branding.LogoFormato?.Trim().ToLowerInvariant() switch
        {
            CatalogosPublicLogoFormatKeys.Square => "portal-cliente__logo--square",
            CatalogosPublicLogoFormatKeys.Horizontal => "portal-cliente__logo--horizontal",
            _ => "portal-cliente__logo--auto"
        };

    protected string BuildPortalRoute(string suffix)
    {
        var route = string.IsNullOrWhiteSpace(suffix) ? "/portal-cliente" : $"/portal-cliente/{suffix.Trim('/')}";
        if (idweb is null || idbase is null)
            return route;

        return $"/{Uri.EscapeDataString(idweb)}/{idbase.Value}{route}";
    }

    private static string NormalizeRouteSegment(string? value)
        => (value ?? string.Empty).Trim().Trim('/');
}

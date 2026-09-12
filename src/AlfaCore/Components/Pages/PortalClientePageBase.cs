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
    [Inject] protected IConfiguracionGeneralService ConfigGeneralSvc { get; set; } = null!;
    [Inject] protected ICatalogosClienteSessionService CatalogoClienteSession { get; set; } = null!;
    [Inject] protected ICentralBasesService CentralBasesSvc { get; set; } = null!;
    [Inject] protected IAppUiOperationService UiOps { get; set; } = null!;
    [Inject] protected IAppModeService AppMode { get; set; } = null!;
    [Inject] protected ISessionService SessionSvc { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    [Inject] protected IJSRuntime Js { get; set; } = null!;
    [Inject] protected IPortalClienteSeccionesCache SeccionesCache { get; set; } = null!;

    [Parameter, SupplyParameterFromQuery(Name = "email")]
    public string? EmailQuery { get; set; }

    protected bool NotFound { get; private set; }
    protected bool SessionBusy { get; private set; }
    protected string ClienteCodigo { get; set; } = string.Empty;
    protected string ClientePassword { get; set; } = string.Empty;
    protected bool ShowPassword { get; set; }
    protected AppUiMessage? LoginFeedback { get; private set; }
    protected CatalogosPublicIdentityDto Branding { get; private set; } = new();

    // Placeholder deliberadamente restrictivo (todo en false) hasta que LoadPortalSeccionesAsync
    // resuelva la config real: Blazor Server renderiza una vez con el estado de campos que haya en
    // el momento en que OnInitializedAsync pega su primer await real, antes de que la lectura a
    // TA_CONFIGURACION vuelva. Si el default fuera "todo true" (como el de
    // ConfiguracionVentasPortalClienteDto, pensado para "sin configurar = mostrar todo" en el
    // service), cada navegación a una página nueva del Portal mostraría un parpadeo de TODAS las
    // pestañas — incluidas las que el admin deshabilitó — antes de ocultarse solas.
    protected ConfiguracionVentasPortalClienteDto PortalSecciones { get; private set; } = new()
    {
        CuentaCorriente = false,
        ListaPrecios = false,
        Catalogos = false,
        Carrito = false,
        Pedidos = false
    };

    /// <summary>Clave de la sección que exige esta página puntual ("cuenta-corriente",
    /// "lista-precios", "catalogos", "carrito", "pedidos") -- null si es una sección fija (Inicio,
    /// Mi cuenta) que no se puede deshabilitar. Si la sección está deshabilitada por configuración,
    /// la página redirige a Inicio en vez de mostrarse (evita que alguien la abra por URL directa
    /// aunque el tab esté oculto).</summary>
    protected virtual string? RequiredSectionKey => null;

    private bool _restoreAttempted;

    protected bool IsAuthenticated
        => CatalogoClienteSession.CurrentClient is { } client
           && string.Equals(client.IdWeb, NormalizeRouteSegment(idweb), StringComparison.OrdinalIgnoreCase)
           && client.IdBase == (idbase ?? 0);

    protected override async Task OnInitializedAsync()
    {
        // Sin esto, CADA navegación entre páginas del Portal (son componentes distintos: Blazor
        // crea una instancia nueva por cada una) repetía la consulta a TA_CONFIGURACION y el tab
        // bar parpadeaba -- se pintaba con el placeholder (todo oculto) apenas arrancaba el método,
        // y recién mostraba las pestañas reales cuando la consulta volvía. SeccionesCache vive en un
        // servicio Scoped (uno por circuito, sobrevive entre navegaciones), así que si otra página
        // ya cargó la config en esta misma sesión del navegador, se usa ese valor ANTES de cualquier
        // await real: el primer render ya sale correcto, sin esperar una vuelta a la base.
        if (SeccionesCache.Cached is { } seccionesCacheadas)
            PortalSecciones = seccionesCacheadas;

        if (!string.IsNullOrWhiteSpace(EmailQuery))
            ClienteCodigo = EmailQuery.Trim();

        CatalogoClienteSession.StateChanged += OnClientSessionStateChanged;
        await EnsureRouteSessionAsync();
        await LoadPublicIdentityAsync();
        await LoadPortalSeccionesAsync();
        EnforceSectionEnabled();
        await OnPortalInitializedAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(EmailQuery))
            ClienteCodigo = EmailQuery.Trim();

        await EnsureRouteSessionAsync();
    }

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

    private async Task LoadPortalSeccionesAsync()
    {
        try
        {
            // EnsureRouteSessionAsync ya dejó activa (vía SetWebhookOverride) la base de este
            // idweb/idbase, así que ConfigGeneralSvc resuelve la instalación correcta aunque nadie
            // haya iniciado sesión de AlfaCore acá (el Portal Cliente es público/anónimo). Siempre
            // se revalida contra la base (no se confía solo en el cache) por si el admin cambió la
            // config mientras el cliente navegaba; SeccionesCache solo evita el parpadeo del primer
            // render, no reemplaza esta lectura.
            PortalSecciones = await ConfigGeneralSvc.GetVentasPortalClienteAsync();
            SeccionesCache.Set(PortalSecciones);
        }
        catch
        {
            // Si falla la lectura, se sigue mostrando todo (comportamiento de siempre) en vez de
            // esconder secciones por un problema de configuración ajeno al cliente -- salvo que ya
            // hubiera algo cacheado de una página anterior en este circuito, en cuyo caso se
            // mantiene ese valor conocido en vez de pisarlo con el default "todo visible".
            if (SeccionesCache.Cached is null)
                PortalSecciones = new ConfiguracionVentasPortalClienteDto();
        }
    }

    /// <summary>Si esta página requiere una sección deshabilitada por configuración, redirige a
    /// Inicio -- evita que se pueda entrar por URL directa aunque el tab esté oculto.</summary>
    private void EnforceSectionEnabled()
    {
        var habilitada = RequiredSectionKey switch
        {
            "cuenta-corriente" => PortalSecciones.CuentaCorriente,
            "lista-precios" => PortalSecciones.ListaPrecios,
            "catalogos" => PortalSecciones.Catalogos,
            "carrito" => PortalSecciones.Carrito,
            "pedidos" => PortalSecciones.Pedidos,
            _ => true
        };
        if (!habilitada)
            Nav.NavigateTo(BuildPortalRoute(""), replace: true);
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

    // Sin logo propio configurado, LogoUrl NO viene vacío: el servicio ya devuelve
    // DefaultPublicLogoUrl (/logos/Logo.png) como fallback — la señal real de "no hay logo
    // propio" es TieneLogoPersonalizado. Chequear IsNullOrWhiteSpace(LogoUrl) acá siempre daba
    // false una vez que Branding terminaba de cargar, así que el marco volvía a "auto" (pensado
    // para logos horizontales) apenas se resolvía la identidad pública — el isotipo por defecto
    // es cuadrado, por eso se veía bien un instante y "se rompía" después.
    protected string GetBrandingLogoFrameClass()
    {
        if (!Branding.TieneLogoPersonalizado)
            return "portal-cliente__logo--square";

        return Branding.LogoFormato?.Trim().ToLowerInvariant() switch
        {
            CatalogosPublicLogoFormatKeys.Square => "portal-cliente__logo--square",
            CatalogosPublicLogoFormatKeys.Horizontal => "portal-cliente__logo--horizontal",
            _ => "portal-cliente__logo--auto"
        };
    }

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

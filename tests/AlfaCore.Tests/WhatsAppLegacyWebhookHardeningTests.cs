using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Rutas legacy sin token: GET (verificación de Meta) y POST (mensajes) en
/// /api/conversaciones/whatsapp/webhook (sin "/{token}"). Auditoría confirmó que NO están huérfanas --
/// Program.cs las documenta explícitamente ("se conserva para no romper lo que ya está configurado en
/// Meta/MercadoLibre para el cliente actual") y docs/modulos/integraciones/whatsapp_cloud_api.md
/// confirma que "en instalaciones monobase o legacy puede usarse la ruta sin token". Decommission
/// quedó descartado: hay un consumidor real (instalación monobase/legacy con Meta ya apuntando a esa
/// URL).
///
/// El riesgo real no era "BaseId=0 y falla aguas abajo" -- es que
/// ConexionClienteService.GetActiveSession(), en modo SaaS, intenta resolver
/// ResolveRouteSessionOverride() -> NavigationManager.Uri antes de caer a cualquier fallback. Un
/// request server-to-server sin circuito Blazor no tiene NavigationManager inicializado -- eso ya tumbó
/// este mismo webhook con 500 en producción (ver el comentario de GetActiveSession). Y en legacy/
/// monobase con más de una base local configurada, "la que esté marcada Activa en sessions.json en ese
/// instante" es exactamente la sesión de UI accidental que no debe decidir un tenant server-to-server.
///
/// IsLegacyWhatsAppWebhookTenantAuthoritative(appMode, sessionService) corta ANTES de que el request
/// llegue a tocar esa ruta de código: sólo autoriza cuando NO es SaaS (nunca toca GetActiveSession/
/// GetAllSessions en modo SaaS) y hay EXACTAMENTE una base local configurada -- ni cero (no depende de
/// que BaseId=0 falle más abajo, corta acá mismo) ni más de una (ambigüedad real).
/// </summary>
public sealed class WhatsAppLegacyWebhookHardeningTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ProgramSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));

    private sealed class FakeAppModeService(bool isSaaSMode) : IAppModeService
    {
        public bool IsSaaSMode { get; } = isSaaSMode;
    }

    private sealed class FakeSessionService(IReadOnlyList<SessionDto> sessions) : ISessionService
    {
        public bool GetAllSessionsCalled { get; private set; }
        public bool GetActiveSessionCalled { get; private set; }

        public event Action? SessionChanged { add { } remove { } }
        public string GetConnectionString() => string.Empty;

        public SessionDto? GetActiveSession()
        {
            GetActiveSessionCalled = true;
            return sessions.Count > 0 ? sessions[0] : null;
        }

        public SessionDto? GetWebhookOverride(int expectedBaseId) => null;
        public void SetWebhookOverride(SessionDto session) { }
        public void ClearWebhookOverride() { }

        public IReadOnlyList<SessionDto> GetAllSessions()
        {
            GetAllSessionsCalled = true;
            return sessions;
        }

        public void SwitchSession(Guid id) => throw new NotSupportedException();
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) => throw new NotSupportedException();
        public void DeleteSession(Guid id) => throw new NotSupportedException();
        public void ClearActiveSession() { }
    }

    private static SessionDto BuildSession(int baseId) => new() { BaseId = baseId, Nombre = $"Base{baseId}" };

    // --- Simulaciones pedidas en la auditoría ---------------------------------------------------

    [Fact]
    public void SaaS_PostAnonimoSinSesion_FallaCerrado_SinTocarGetActiveSession()
    {
        // "Sesión UI accidental" en un host SaaS: incluso si por alguna razón GetAllSessions()
        // devolviera algo, el modo SaaS solo debe fallar cerrado -- y sin siquiera intentar leer
        // sesión (evita el crash de NavigationManager visto en producción).
        var sessionService = new FakeSessionService([]);

        var authorized = Program.IsLegacyWhatsAppWebhookTenantAuthoritative(new FakeAppModeService(isSaaSMode: true), sessionService);

        Assert.False(authorized);
        Assert.False(sessionService.GetAllSessionsCalled, "En modo SaaS no debe ni consultar GetAllSessions -- corta antes.");
    }

    [Fact]
    public void SaaS_ConUnaSolaBaseLocalIgualFallaCerrado_NuncaUsaSesionComoTenantEnSaaS()
    {
        // Aunque hubiera exactamente una sesión "disponible", en SaaS jamás se debe usar sesión de
        // UI para decidir el tenant de un webhook server-to-server -- eso es exclusivo del token.
        var sessionService = new FakeSessionService([BuildSession(4271)]);

        var authorized = Program.IsLegacyWhatsAppWebhookTenantAuthoritative(new FakeAppModeService(isSaaSMode: true), sessionService);

        Assert.False(authorized);
    }

    [Fact]
    public void Legacy_ConCeroBasesConfiguradas_FallaCerrado_NoDependeDeQueBaseId0FalleAguasAbajo()
    {
        // Sin ninguna base local configurada no hay tenant autoritativo al que atribuirle el
        // request -- ya no se deja que BaseId=0 llegue a fallar más abajo por falta de secreto, se
        // corta explícitamente acá.
        var sessionService = new FakeSessionService([]);

        var authorized = Program.IsLegacyWhatsAppWebhookTenantAuthoritative(new FakeAppModeService(isSaaSMode: false), sessionService);

        Assert.False(authorized);
    }

    [Fact]
    public void Legacy_ConUnaSolaBaseConfigurada_AutorizaSinAmbiguedad_ElConsumidorRealSigueFuncionando()
    {
        // El caso real confirmado por Program.cs/documentación: instalación monobase con Meta ya
        // apuntando a esta URL. Una sola base configurada -> sin ambigüedad -> sigue funcionando
        // exactamente igual que hoy.
        var sessionService = new FakeSessionService([BuildSession(4264)]);

        var authorized = Program.IsLegacyWhatsAppWebhookTenantAuthoritative(new FakeAppModeService(isSaaSMode: false), sessionService);

        Assert.True(authorized);
    }

    [Fact]
    public void Legacy_ConVariasBasesLocales_FallaCerrado_NoAdivinaCualEsLaActiva()
    {
        // Un mismo AlfaCore legacy atendiendo varias empresas: "la que esté Activa en sessions.json
        // en ese instante" es justo la sesión de UI accidental que no debe decidir un webhook
        // server-to-server -- sin token no hay forma autoritativa de saber a cuál pertenece.
        var sessionService = new FakeSessionService([BuildSession(100), BuildSession(200)]);

        var authorized = Program.IsLegacyWhatsAppWebhookTenantAuthoritative(new FakeAppModeService(isSaaSMode: false), sessionService);

        Assert.False(authorized);
    }

    [Fact]
    public void PostConcurrente_MismaEvaluacionSinEstadoCompartido_EsPuraFuncionDeSusArgumentos()
    {
        // IsLegacyWhatsAppWebhookTenantAuthoritative no guarda ningún estado propio -- dos llamadas
        // concurrentes con distintos snapshots de sesiones nunca se pisan entre sí (cada una evalúa
        // sólo lo que su propio ISessionService le devuelve en ese momento).
        var sessionServiceA = new FakeSessionService([BuildSession(1)]);
        var sessionServiceB = new FakeSessionService([BuildSession(1), BuildSession(2)]);
        var appMode = new FakeAppModeService(isSaaSMode: false);

        Assert.True(Program.IsLegacyWhatsAppWebhookTenantAuthoritative(appMode, sessionServiceA));
        Assert.False(Program.IsLegacyWhatsAppWebhookTenantAuthoritative(appMode, sessionServiceB));
    }

    // --- No debe romper nada tokenizado / firma / demás rutas ----------------------------------

    [Fact]
    public void RutaLegacyPost_UsaElGuardAntesDeHandleWhatsAppMessageAsync()
    {
        var legacyRoute = ProgramSource.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook\", (", StringComparison.Ordinal);
        Assert.True(legacyRoute >= 0, "No se encontró la ruta legacy.");

        var guardCheck = ProgramSource.IndexOf("IsLegacyWhatsAppWebhookTenantAuthoritative(appMode, sessionService)", legacyRoute, StringComparison.Ordinal);
        var handlerCall = ProgramSource.IndexOf("HandleWhatsAppMessageAsync(request, configService, svc", legacyRoute, StringComparison.Ordinal);
        var notFoundFallback = ProgramSource.IndexOf("Task.FromResult<IResult>(Results.NotFound())", legacyRoute, StringComparison.Ordinal);

        Assert.True(guardCheck > legacyRoute, "El guard debe evaluarse dentro del registro de la ruta legacy.");
        Assert.True(handlerCall > guardCheck, "HandleWhatsAppMessageAsync debe quedar condicionado al guard, no incondicional.");
        Assert.True(notFoundFallback > guardCheck, "Debe existir un fallback explícito a NotFound cuando el guard rechaza.");
    }

    [Fact]
    public void RutaLegacyGet_UsaElMismoGuardAntesDeHandleWhatsAppVerifyAsync()
    {
        // GET legacy: "app.MapGet(\"/api/conversaciones/whatsapp/webhook\", (" -- ya no es una
        // referencia directa a HandleWhatsAppVerifyAsync como método de grupo, está envuelta con el
        // mismo guard que el POST legacy.
        var legacyGetRoute = ProgramSource.IndexOf("app.MapGet(\"/api/conversaciones/whatsapp/webhook\", (", StringComparison.Ordinal);
        Assert.True(legacyGetRoute >= 0, "No se encontró la ruta GET legacy envuelta en una lambda.");

        var guardCheck = ProgramSource.IndexOf("IsLegacyWhatsAppWebhookTenantAuthoritative(appMode, sessionService)", legacyGetRoute, StringComparison.Ordinal);
        var handlerCall = ProgramSource.IndexOf("HandleWhatsAppVerifyAsync(request, configService, configuration, whatsAppOptions, sessionService, ct)", legacyGetRoute, StringComparison.Ordinal);
        var notFoundFallback = ProgramSource.IndexOf("Task.FromResult<IResult>(Results.NotFound())", legacyGetRoute, StringComparison.Ordinal);

        Assert.True(guardCheck > legacyGetRoute, "El guard debe evaluarse dentro del registro del GET legacy.");
        Assert.True(handlerCall > guardCheck, "HandleWhatsAppVerifyAsync debe quedar condicionado al guard, no incondicional.");
        Assert.True(notFoundFallback > guardCheck, "Debe existir un fallback explícito a NotFound cuando el guard rechaza.");

        // No debe haber quedado ninguna referencia directa de método de grupo (la forma vieja, sin
        // guard) al handler para esta ruta.
        Assert.DoesNotContain(
            "app.MapGet(\"/api/conversaciones/whatsapp/webhook\", HandleWhatsAppVerifyAsync);",
            ProgramSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RutaTokenizadaGet_SigueResolviendoTenantYSetWebhookOverride_SinTocarElGuardLegacy()
    {
        var tokenGetRoute = ProgramSource.IndexOf("app.MapGet(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        Assert.True(tokenGetRoute >= 0);

        var resolveCall = ProgramSource.IndexOf("TryResolveWebhookTenantAsync(token, basesService, sessionService, ct)", tokenGetRoute, StringComparison.Ordinal);
        var handlerCall = ProgramSource.IndexOf("HandleWhatsAppVerifyAsync(request, configService, configuration, whatsAppOptions, sessionService, ct, resolvedBaseId)", tokenGetRoute, StringComparison.Ordinal);

        Assert.True(resolveCall > tokenGetRoute);
        Assert.True(handlerCall > resolveCall);

        // El guard legacy nunca debe aparecer en la ruta GET tokenizada -- su tenant siempre viene
        // del token, nunca del guard de sesión-local.
        var tokenGetRouteBlock = ProgramSource[tokenGetRoute..(tokenGetRoute + 900)];
        Assert.DoesNotContain("IsLegacyWhatsAppWebhookTenantAuthoritative", tokenGetRouteBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void RutaTokenizada_SigueResolviendoTenantYSetWebhookOverride_SinTocarElGuardLegacy()
    {
        var tokenRoute = ProgramSource.IndexOf("app.MapPost(\"/api/conversaciones/whatsapp/webhook/{token}\"", StringComparison.Ordinal);
        Assert.True(tokenRoute >= 0);

        var resolveCall = ProgramSource.IndexOf("TryResolveWebhookTenantAsync(token, basesService, sessionService, ct)", tokenRoute, StringComparison.Ordinal);
        var handlerCall = ProgramSource.IndexOf("HandleWhatsAppMessageAsync(", resolveCall, StringComparison.Ordinal);
        var resolverDef = ProgramSource.IndexOf("internal static async Task<int?> TryResolveWebhookTenantAsync", StringComparison.Ordinal);
        var setOverride = ProgramSource.IndexOf("sessionService.SetWebhookOverride(new SessionDto", resolverDef, StringComparison.Ordinal);

        Assert.True(resolveCall > tokenRoute);
        Assert.True(handlerCall > resolveCall);
        Assert.True(setOverride > resolverDef);

        // El guard legacy nunca debe aparecer en la ruta tokenizada -- su tenant siempre viene del
        // token, nunca del guard de sesión-local.
        var tokenRouteBlock = ProgramSource[tokenRoute..(tokenRoute + 1200)];
        Assert.DoesNotContain("IsLegacyWhatsAppWebhookTenantAuthoritative", tokenRouteBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void NingunCallbackGeneradoApuntaALaRutaLegacy()
    {
        // Los generadores de URL de callback (Embedded Signup / inspección de suscripción) deben
        // seguir armando siempre la ruta tokenizada -- nunca deben "caer" a la legacy por error.
        Assert.DoesNotContain(
            "\"/api/conversaciones/whatsapp/webhook\" +",
            ProgramSource,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

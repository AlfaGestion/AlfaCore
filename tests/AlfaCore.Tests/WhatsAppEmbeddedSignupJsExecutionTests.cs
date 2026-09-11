using Jint;
using Jint.Native;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Ejecuta de verdad (motor JS puro-C#, sin Node/npm) el whatsappEmbeddedSignup.js real que se
/// sirve en producción -- no un texto reimplementado en C#. Nace de la causa raíz confirmada del
/// incidente Base4264/Alfanet Papelera: en modo BUSINESS_APP_COEXISTENCE, Meta mandó eventName=
/// "FINISH" (no "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING"), nuestra lógica lo rechazaba
/// (eventAccepted=false), session nunca se construía y CompleteEmbeddedSignupAuthorization nunca
/// se invocaba pese a tener auth code válido. Los tests de solo-texto-fuente (WhatsAppEmbeddedSignupJavaScriptTests)
/// no podían haber atrapado esta regresión: el string "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING"
/// seguía presente en el archivo, el bug estaba en la fórmula booleana, no en el texto.
/// </summary>
public sealed class WhatsAppEmbeddedSignupJsExecutionTests
{
    private const string StandardMode = "standard";
    private const string CoexistenceMode = "businessAppCoexistence";

    [Fact]
    public void Standard_Finish_IsAccepted()
    {
        var harness = Harness.Launch(StandardMode);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("FINISH", "waba-1", "phone-1"));

        Assert.Contains(harness.Calls, c => c.Method == "CompleteEmbeddedSignupAuthorization");
    }

    [Fact]
    public void Coexistence_Finish_IsAccepted()
    {
        // La regresión exacta del incidente: antes del fix, esto quedaba silenciosamente
        // descartado (eventAccepted=false) y el onboarding se estancaba en STARTED hasta
        // expirar por el watchdog de 90s.
        var harness = Harness.Launch(CoexistenceMode);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("FINISH", "waba-2", "phone-2"));

        Assert.Contains(harness.Calls, c => c.Method == "CompleteEmbeddedSignupAuthorization");
    }

    [Fact]
    public void Coexistence_FinishWhatsAppBusinessAppOnboarding_IsAccepted()
    {
        // Compatibilidad hacia atrás: el evento específico de coexistence sigue aceptado por si
        // Meta lo manda en otros casos/versiones.
        var harness = Harness.Launch(CoexistenceMode);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING", "waba-3", "phone-3"));

        Assert.Contains(harness.Calls, c => c.Method == "CompleteEmbeddedSignupAuthorization");
    }

    [Fact]
    public void UnknownEvent_IsRejected()
    {
        var harness = Harness.Launch(CoexistenceMode);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("SOME_OTHER_EVENT", "waba-4", "phone-4"));

        Assert.DoesNotContain(harness.Calls, c => c.Method is "CompleteEmbeddedSignupAuthorization" or "EmbeddedSignupCancelled" or "EmbeddedSignupFailed");
    }

    [Fact]
    public void WrongPayloadType_IsRejected()
    {
        var harness = Harness.Launch(CoexistenceMode);
        var payload = "{\"type\":\"SOME_OTHER_TYPE\",\"event\":\"FINISH\",\"data\":{\"waba_id\":\"waba-5\",\"phone_number_id\":\"phone-5\"}}";
        harness.DispatchMessage("https://www.facebook.com", payload);

        Assert.DoesNotContain(harness.Calls, c => c.Method is "CompleteEmbeddedSignupAuthorization" or "EmbeddedSignupCancelled" or "EmbeddedSignupFailed");
    }

    [Fact]
    public void FinishWithCodeAndSession_InvokesDotNetCallbackWithExactArguments()
    {
        var harness = Harness.Launch(StandardMode, facebookLoginProvidesCode: true);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("FINISH", "waba-exact", "phone-exact"));

        var call = Assert.Single(harness.Calls, c => c.Method == "CompleteEmbeddedSignupAuthorization");
        Assert.Equal("auth-code-123", call.Args[0].AsString());
        Assert.Equal("state-abc", call.Args[1].AsString());
        Assert.Equal("waba-exact", call.Args[2].AsString());
        Assert.Equal("phone-exact", call.Args[3].AsString());
    }

    [Fact]
    public void FinishArrivesBeforeFacebookLoginResolves_SessionSetButCodePending_DoesNotInvokeDotNetCallback()
    {
        // El postMessage de sesión y el callback de FB.login son dos canales independientes que
        // pueden llegar en cualquier orden (justo lo auditado en el incidente). Acá el popup de
        // Meta todavía no resolvió (FB.login nunca calback-eó) cuando llega el FINISH: session se
        // construye, pero completeIfReady sigue exigiendo code -- confirma que el fix de aceptación
        // de eventos no relajó la exigencia de code+session juntos.
        var harness = Harness.Launch(CoexistenceMode, facebookLoginProvidesCode: false);
        harness.DispatchMessage("https://www.facebook.com", BuildFinishPayload("FINISH", "waba-6", "phone-6"));

        Assert.DoesNotContain(harness.Calls, c => c.Method == "CompleteEmbeddedSignupAuthorization");
    }

    private static string BuildFinishPayload(string eventName, string wabaId, string phoneNumberId)
        => "{\"type\":\"WA_EMBEDDED_SIGNUP\",\"event\":\"" + eventName + "\",\"data\":{\"waba_id\":\"" + wabaId + "\",\"phone_number_id\":\"" + phoneNumberId + "\"}}";

    private sealed record DotNetCall(string Method, JsValue[] Args);

    private sealed class Harness
    {
        private readonly Engine _engine;
        public List<DotNetCall> Calls { get; } = [];

        private Harness(Engine engine) => _engine = engine;

        public static Harness Launch(string onboardingMode, bool facebookLoginProvidesCode = true)
        {
            var engine = new Engine();
            var harness = new Harness(engine);

            engine.Execute("""
                var window = {};
                window.addEventListener = function (type, handler) { if (type === 'message') window.__messageHandler = handler; };
                window.removeEventListener = function (type, handler) { if (type === 'message') window.__messageHandler = null; };
                var document = {
                    getElementById: function (id) { return null; },
                    createElement: function (tag) { return {}; },
                    head: { appendChild: function (el) { window.fbAsyncInit(); } }
                };
                var console = { debug: function () {} };
                """);

            engine.Execute(ReadModuleSourceAsClassicScript());

            engine.SetValue("__record", new Action<string, JsValue[]>((method, args) => harness.Calls.Add(new DotNetCall(method, args))));

            // Si facebookLoginProvidesCode es false, el callback deliberadamente NUNCA se invoca --
            // simula el popup de Meta todavía sin resolver (no un authResponse vacío, que dispararía
            // el propio cleanup+EmbeddedSignupCancelled del módulo y desconectaría el listener).
            var loginCallbackBody = facebookLoginProvidesCode
                ? "callback({ authResponse: { code: 'auth-code-123' } });"
                : "";
            engine.Execute($$"""
                window.FB = {
                    init: function () {},
                    login: function (callback, options) { window.__lastLoginOptions = options; {{loginCallbackBody}} }
                };
                var dotnet = {
                    invokeMethodAsync: function () {
                        var args = Array.prototype.slice.call(arguments);
                        var method = args.shift();
                        __record(method, args);
                        return Promise.resolve();
                    }
                };
                """);

            engine.Execute($$"""
                var launchOptions = {
                    appId: 'test-app-id', graphApiVersion: 'v26.0', config_id: 'test-config-id',
                    onboardingMode: '{{onboardingMode}}', state: 'state-abc',
                    developmentDiagnostics: false
                };
                launch(launchOptions, dotnet);
                """);

            return harness;
        }

        public void DispatchMessage(string origin, string data)
        {
            var handler = _engine.GetValue("window").Get("__messageHandler");
            Assert.False(handler.IsUndefined());
            var eventObj = JsValue.FromObject(_engine, new Dictionary<string, object> { ["origin"] = origin, ["data"] = data });
            _engine.Invoke(handler, eventObj);
        }

        private static string ReadModuleSourceAsClassicScript()
        {
            var source = File.ReadAllText(FindRepoFile("src", "AlfaCore", "wwwroot", "js", "whatsappEmbeddedSignup.js"));
            // Jint aquí corre el módulo como script clásico (sin loader de módulos configurado);
            // sólo se quitan las palabras clave `export` -- el resto del archivo queda intacto.
            return source
                .Replace("export async function launch", "async function launch")
                .Replace("export const MODULE_VERSION", "const MODULE_VERSION");
        }

        private static string FindRepoFile(params string[] segments)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine([directory.FullName, .. segments]);
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }

            throw new FileNotFoundException("No se encontró whatsappEmbeddedSignup.js desde el directorio de pruebas.");
        }
    }
}

using System.Text.RegularExpressions;
using Xunit;

namespace AlfaCore.Tests;

public sealed class WhatsAppEmbeddedSignupJavaScriptTests
{
    [Fact]
    public void FacebookLoginReceivesAConventionalSynchronousCallback()
    {
        var source = File.ReadAllText(FindModulePath());

        Assert.Contains("function facebookLoginCallback(loginResponse)", source, StringComparison.Ordinal);
        Assert.Contains("window.FB.login(facebookLoginCallback, loginOptions)", source, StringComparison.Ordinal);
        Assert.Contains("void handleFacebookLoginResponse(loginResponse)", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bFB\.login\s*\(\s*async\b", RegexOptions.CultureInvariant), source);
        Assert.DoesNotMatch(new Regex(@"function\s+facebookLoginCallback\s*\([^)]*\)\s*\{[^}]*\breturn\b", RegexOptions.CultureInvariant | RegexOptions.Singleline), source);
        Assert.Contains("facebookLoginCallback.constructor === Function", source, StringComparison.Ordinal);
        Assert.Contains("const functionPaths = findFunctionPaths(loginOptions)", source, StringComparison.Ordinal);
        Assert.Contains("const configId = typeof options.config_id === \"string\" ? options.config_id.trim() : \"\"", source, StringComparison.Ordinal);
        Assert.Contains("config_id: configId", source, StringComparison.Ordinal);
        Assert.Contains("configIdPresent: typeof loginOptions.config_id === \"string\" && loginOptions.config_id.length > 0", source, StringComparison.Ordinal);
        Assert.Contains("response_type: \"code\"", source, StringComparison.Ordinal);
        Assert.Contains("override_default_response_type: true", source, StringComparison.Ordinal);
        Assert.Contains("export const MODULE_VERSION = \"es2-coexistence-contract-1\"", source, StringComparison.Ordinal);

        var pageSource = File.ReadAllText(FindRepoFile("src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        Assert.Contains("whatsappEmbeddedSignup.js?v={moduleVersion}", pageSource, StringComparison.Ordinal);
        Assert.Contains("[\"config_id\"] = options.EmbeddedSignupConfigId", pageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("configId = options.EmbeddedSignupConfigId", pageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingModesBuildTheExpectedMetaContract()
    {
        var source = File.ReadAllText(FindModulePath());

        Assert.Contains("options.onboardingMode === \"businessAppCoexistence\"", source, StringComparison.Ordinal);
        Assert.Contains("? { setup: {}, featureType: \"whatsapp_business_app_onboarding\", sessionInfoVersion: \"3\" }", source, StringComparison.Ordinal);
        Assert.Contains(": { sessionInfoVersion: \"3\" }", source, StringComparison.Ordinal);
        Assert.DoesNotContain(": { setup: {}, sessionInfoVersion: \"3\" }", source, StringComparison.Ordinal);
        Assert.Contains("setupIsObject", source, StringComparison.Ordinal);
        Assert.Contains("loginContract.featureType === \"whatsapp_business_app_onboarding\"", source, StringComparison.Ordinal);
        Assert.Contains("!setupPresent && !featureTypePresent", source, StringComparison.Ordinal);
        Assert.Contains("captureLoginContract(loginContract, options.developmentDiagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("onboardingMode: coexistence ? \"businessAppCoexistence\" : \"standard\"", source, StringComparison.Ordinal);
        Assert.Contains("FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING", source, StringComparison.Ordinal);
        Assert.Contains("const expectedEvent = coexistence ? \"FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING\" : \"FINISH\"", source, StringComparison.Ordinal);
        Assert.Contains("const isExpectedFinish = eventName === expectedEvent", source, StringComparison.Ordinal);

        var pageSource = File.ReadAllText(FindRepoFile("src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        Assert.Contains("const string moduleVersion = \"es2-coexistence-contract-1\"", pageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationOffersBothFriendlyPathsAndKeepsManualAdvancedFlow()
    {
        var pageSource = File.ReadAllText(FindRepoFile("src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));

        Assert.Contains("Conectar WhatsApp", pageSource, StringComparison.Ordinal);
        Assert.Contains("Ya uso WhatsApp Business", pageSource, StringComparison.Ordinal);
        Assert.Contains("Quiero empezar con un WhatsApp nuevo", pageSource, StringComparison.Ordinal);
        Assert.Contains("role=\"radiogroup\"", pageSource, StringComparison.Ordinal);
        Assert.Contains("role=\"radio\"", pageSource, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(_whatsAppConnectionChoiceSelection.Selected is null)\"", pageSource, StringComparison.Ordinal);
        Assert.Contains("ContinueWhatsAppOnboardingAsync", pageSource, StringComparison.Ordinal);
        Assert.Contains("WhatsAppConnectionChoice.ExistingWhatsAppBusiness))", pageSource, StringComparison.Ordinal);
        Assert.Contains("WhatsAppConnectionChoice.NewWhatsApp))", pageSource, StringComparison.Ordinal);
        Assert.Contains("WhatsAppConnectionChoiceMapper.ToOnboardingMode(choice)", pageSource, StringComparison.Ordinal);
        Assert.Contains("[\"onboardingMode\"] = jsInteropMode", pageSource, StringComparison.Ordinal);
        Assert.Contains("if (start.OnboardingMode != expectedMode)", pageSource, StringComparison.Ordinal);
        Assert.Contains("Vas a configurar un WhatsApp nuevo.", pageSource, StringComparison.Ordinal);
        Assert.Contains("WhatsApp conectados", pageSource, StringComparison.Ordinal);
        Assert.Contains("Agregar por Phone Number ID", pageSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Conectar con Meta<", pageSource, StringComparison.Ordinal);
    }

    // Diagnóstico temporal [WAES-DIAG] del incidente Base4264 (ver auditoría onMessage/FB.login):
    // sin runtime JS en este repo, se verifica el CONTRATO por texto fuente, siguiendo el mismo
    // patrón que los tests de arriba. Cubre exactamente lo pedido: string JSON válido -> log
    // sanitizado, evento inesperado -> eventAccepted=false, callback FB sin code -> hasCode=false.
    [Fact]
    public void OnMessageInstrumentation_LogsSanitizedFieldsAndNeverSecrets()
    {
        var source = File.ReadAllText(FindModulePath());

        // string JSON válido -> log sanitizado con exactamente los campos pedidos.
        Assert.Contains("function diagLog(stage, details)", source, StringComparison.Ordinal);
        Assert.Contains("console.debug(\"[WAES-DIAG]\", stage, details)", source, StringComparison.Ordinal);
        Assert.Contains("try { payload = JSON.parse(event.data); jsonParseOk = true; } catch { jsonParseOk = false; }", source, StringComparison.Ordinal);
        Assert.Contains("diagLog(\"onMessage\", {", source, StringComparison.Ordinal);
        foreach (var field in new[] { "postMessageReceived: true", "origin: event.origin", "dataType,", "jsonParseOk,", "payloadType,", "payloadEvent,", "expectedEvent,", "eventAccepted" })
            Assert.Contains(field, source, StringComparison.Ordinal);

        // evento inesperado -> eventAccepted=false (isKnownPayload exige type+parse+origin+dataType
        // correctos, y sólo FINISH/FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING/CANCEL/ERROR aceptan).
        Assert.Contains("const eventAccepted = isKnownPayload && (isExpectedFinish || isCancel || isError)", source, StringComparison.Ordinal);
        Assert.Contains("const isKnownPayload = originAllowed && dataType === \"string\" && jsonParseOk && payloadType === \"WA_EMBEDDED_SIGNUP\"", source, StringComparison.Ordinal);

        // callback FB sin code -> hasCode=false, logueado ANTES del branch que corta por !receivedCode
        // (así el log siempre refleja hasCode, incluso cuando falta).
        Assert.Contains("const hasCode = !!receivedCode;", source, StringComparison.Ordinal);
        var facebookCallbackIndex = source.IndexOf("async function handleFacebookLoginResponse", StringComparison.Ordinal);
        var facebookDiagIndex = source.IndexOf("diagLog(\"facebookLoginCallback\", { facebookLoginCallbackInvoked: true, hasAuthResponse, hasCode });", StringComparison.Ordinal);
        var facebookGuardIndex = source.IndexOf("if (!receivedCode) {", StringComparison.Ordinal);
        Assert.True(facebookCallbackIndex >= 0 && facebookDiagIndex > facebookCallbackIndex && facebookGuardIndex > facebookDiagIndex,
            "El log de hasCode debe ocurrir ANTES del guard que corta por !receivedCode, para que siempre quede registrado.");

        // completeIfReady: booleanos únicamente, y el error del interop se loguea sanitizado.
        Assert.Contains("diagLog(\"completeIfReady\", { completeIfReadyCalled: true, hasCode, hasSession });", source, StringComparison.Ordinal);
        Assert.Contains("diagLog(\"completeIfReady\", { dotnetCallbackInvoked: true });", source, StringComparison.Ordinal);
        Assert.Contains("dotnetCallbackError: true,", source, StringComparison.Ordinal);
        Assert.Contains("errorMessage: sanitizeStack(String(error?.message || error || \"\"))", source, StringComparison.Ordinal);

        // Nunca se loguean secretos: ni el propio code/state, ni payload.data, ni waba_id/phone_number_id
        // dentro de ninguna llamada a diagLog.
        foreach (Match call in Regex.Matches(source, @"diagLog\([^;]*?\)\s*;", RegexOptions.Singleline))
        {
            var call1 = call.Value;
            Assert.DoesNotContain("payload.data", call1, StringComparison.Ordinal);
            Assert.DoesNotContain("waba_id", call1, StringComparison.Ordinal);
            Assert.DoesNotContain("phone_number_id", call1, StringComparison.Ordinal);
            Assert.DoesNotContain("receivedCode", call1, StringComparison.Ordinal);
            Assert.DoesNotContain("options.state", call1, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"(?<!has)\bcode\b(?!Present)"), call1);
        }
    }

    private static string FindModulePath()
    {
        return FindRepoFile("src", "AlfaCore", "wwwroot", "js", "whatsappEmbeddedSignup.js");
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

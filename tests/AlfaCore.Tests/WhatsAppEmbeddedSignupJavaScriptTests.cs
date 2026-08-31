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
        Assert.Contains("eventName === \"FINISH\"", source, StringComparison.Ordinal);

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

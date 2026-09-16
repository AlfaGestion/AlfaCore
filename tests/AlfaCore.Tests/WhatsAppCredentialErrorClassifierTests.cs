using System.Security.Cryptography;
using AlfaCore.Components.Pages;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Caso real reportado durante validación manual: corriendo AlfaCore local contra Base4264, el Vault
/// no puede abrirse porque el SecureVault de esa base está protegido con Data Protection/certificado
/// que sólo existe en SERVER-ALFACENT (esperado -- no es un bug de Meta). Pero en algunos caminos la UI
/// terminaba mostrando "WhatsApp requiere reconexión: La credencial de esta conexión de WhatsApp ya no
/// es válida." -- semánticamente incorrecto: implica que el TOKEN DE META está mal, cuando en realidad
/// es este PROCESO el que no puede leer la credencial (que sigue siendo válida).
///
/// WhatsAppCredentialErrorClassifier separa por TIPO de excepción (nunca por texto, para no depender de
/// coincidencias) dos causas que antes terminaban mezcladas:
/// A) Vault/DataProtection/proceso local no puede abrir la credencial (WhatsAppEmbeddedVaultUnavailableException
///    o una CryptographicException cruda que escapa sin envolver).
/// B) Meta realmente rechazó/venció el token (código Graph 190, vía WhatsAppOutboundErrorClassifier) --
///    ahí sí corresponde "WhatsApp requiere reconexión".
/// </summary>
public sealed class WhatsAppCredentialErrorClassifierTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void VaultUnavailableException_NeverClassifiesAsReconnectionRequired()
    {
        var ex = new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.");

        var message = WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: false);

        Assert.NotNull(message);
        Assert.DoesNotContain("reconexión", message!.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reconexión", message.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ya no es válida", message.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultUnavailableException_NonProduction_ShowsEnvironmentOrientedMessage()
    {
        var ex = new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.");

        var message = WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: false)!;

        Assert.Equal("No se pudo acceder a la credencial segura de WhatsApp en este entorno", message.Title);
        Assert.Equal(AppUiFeedbackSeverity.Error, message.Severity);
    }

    [Fact]
    public void VaultUnavailableException_Production_ShowsAdminOrientedMessage()
    {
        var ex = new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.");

        var message = WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: true)!;

        Assert.Equal("No se pudo acceder a la credencial segura de WhatsApp", message.Title);
        Assert.Equal("Contactá al administrador.", message.Message);
    }

    [Fact]
    public void RawCryptographicException_IsAlsoClassifiedAsCredentialUnavailable_EvenUnwrapped()
    {
        // WhatsAppSecureVault.StoreAsync/Protect (usado, entre otros, por el intercambio OAuth de
        // Embedded Signup) puede dejar escapar una CryptographicException cruda sin pasar por
        // WhatsAppRuntimeCredentialResolver -- también debe reconocerse, no sólo el tipo envuelto.
        var ex = new CryptographicException("The parameter is incorrect.");

        var message = WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: false);

        Assert.NotNull(message);
        Assert.DoesNotContain("reconexión", message!.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CryptographicException_DeepInsideTheExceptionChain_IsStillFound()
    {
        var inner = new CryptographicException("Key not valid for use in specified state.");
        var wrapped = new InvalidOperationException("Algo salió mal al resolver la credencial.", inner);

        var message = WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(wrapped, isProduction: false);

        Assert.NotNull(message);
    }

    [Fact]
    public void RealMetaTokenError_IsNeverMisclassifiedAsVaultUnavailable()
    {
        // Caso B: un rechazo real de Meta (HttpRequestException, no criptográfico) nunca debe pasar por
        // este clasificador -- sigue correspondiendo "WhatsApp requiere reconexión" vía
        // WhatsAppOutboundErrorClassifier (código Graph 190), no el mensaje de entorno/Vault.
        var ex = new HttpRequestException("Meta devolvió 401: {\"error\":{\"code\":190,\"message\":\"Error validating access token\"}}");

        Assert.Null(WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: false));

        var graphClassified = WhatsAppOutboundErrorClassifier.ClassifyDeliverySendException(ex);
        Assert.Equal("WhatsApp requiere reconexión", graphClassified!.Title);
    }

    [Fact]
    public void OrdinaryBusinessRuleException_IsNeverMisclassifiedAsVaultUnavailable()
    {
        var ex = new InvalidOperationException("Solo se pueden enviar plantillas aprobadas por Meta.");

        Assert.Null(WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction: false));
    }

    [Fact]
    public void ClassifyDeliverySendException_ChecksCredentialUnavailability_BeforeGraphCodeParsing()
    {
        // Integración del punto de entrada real usado por templates/reacciones: aunque el mensaje de la
        // excepción no tenga JSON embebido de Graph (y por lo tanto ExtractGraphError no encontraría
        // ningún código), el chequeo de credencial debe intervenir ANTES de caer al fallback genérico
        // "No se pudo enviar" / "Meta no pudo entregar este mensaje.".
        var ex = new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.");

        var message = WhatsAppOutboundErrorClassifier.ClassifyDeliverySendException(ex, isProduction: false);

        Assert.NotNull(message);
        Assert.Equal("No se pudo acceder a la credencial segura de WhatsApp en este entorno", message!.Title);
    }

    [Fact]
    public void EmbeddedSignupClassifier_AlsoDistinguishesVaultUnavailable_FromMetaRejection()
    {
        // Auditoría: WhatsAppEmbeddedVaultUnavailableException ES-A InvalidOperationException, así que
        // sin un chequeo explícito por tipo antes de la rama genérica de ClassifyEmbeddedSignupAuthorizationError,
        // caía en "Meta rechazó el intercambio con AlfaCore" -- tan incorrecto como "requiere reconexión":
        // ni Meta rechazó nada, ni el token está mal.
        var vaultEx = new WhatsAppEmbeddedVaultUnavailableException("La credencial segura de WhatsApp Embedded Signup no puede abrirse en este proceso.");

        var message = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(vaultEx, "INC-1", isProduction: false);

        Assert.DoesNotContain("Meta rechazó", message.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("No se pudo acceder a la credencial segura de WhatsApp en este entorno", message.Title);

        var productionMessage = ConversacionesConfiguracion.ClassifyEmbeddedSignupAuthorizationError(vaultEx, "INC-2", isProduction: true);
        Assert.Equal("Contactá al administrador.", productionMessage.Message);
    }

    [Fact]
    public void CredentialUnavailable_IsCheckedBeforeAllOtherEmbeddedSignupBranches()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "ConversacionesConfiguracion.razor"));
        var methodStart = source.IndexOf("internal static AppUiMessage ClassifyEmbeddedSignupAuthorizationError(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractMethodBody(source, source.IndexOf('{', methodStart));

        var credentialCheckIndex = methodBody.IndexOf("WhatsAppCredentialErrorClassifier.ClassifyCredentialUnavailable(ex, isProduction)", StringComparison.Ordinal);
        var firstOtherBranchIndex = methodBody.IndexOf("if (ex is UnauthorizedAccessException", StringComparison.Ordinal);
        Assert.True(credentialCheckIndex >= 0, "No se encontró el chequeo de credencial no disponible.");
        Assert.True(credentialCheckIndex < firstOtherBranchIndex, "El chequeo de credencial debe ir antes que cualquier otra rama.");
    }

    [Fact]
    public void NoLegacyFallbackOrVaultOrDataProtectionOrTokenChangesWereIntroduced()
    {
        // Restricción explícita del pedido: esta auditoría es puramente de clasificación/UI. No debe
        // haber tocado Vault, Data Protection ni el manejo del token en sí.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "WhatsAppCredentialErrorClassifier.cs"));
        Assert.DoesNotContain("Protect(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Unprotect(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("legacyConfig", source, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, int openBrace)
    {
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[openBrace..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo del método.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se pudo ubicar la raíz del repositorio (AlfaCore.sln) desde el directorio de pruebas.");
    }
}

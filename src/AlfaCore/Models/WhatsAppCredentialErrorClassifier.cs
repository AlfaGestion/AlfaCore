using System.Security.Cryptography;
using AlfaCore.Services;

namespace AlfaCore.Models;

/// <summary>
/// Distingue dos causas de falla que hoy pueden terminar mostrando el mismo mensaje engañoso
/// ("WhatsApp requiere reconexión") aunque signifiquen cosas completamente distintas:
///
/// A) Vault/DataProtection/proceso local no puede abrir la credencial segura -- p. ej. corriendo
///    AlfaCore local contra una base cuyo SecureVault está protegido con un certificado/Data
///    Protection que sólo existe en SERVER-ALFACENT. Esto es un problema de ESTE PROCESS/ENTORNO,
///    no de la conexión de WhatsApp: la credencial en sí sigue siendo válida, sólo que este proceso
///    no puede descifrarla. Nunca implica que Meta rechazó nada.
/// B) Meta realmente rechazó/venció/revocó el token (p. ej. error Graph 190). Acá sí corresponde
///    "WhatsApp requiere reconexión".
///
/// Se reconoce por TIPO de excepción (WhatsAppEmbeddedVaultUnavailableException -- ver
/// WhatsAppRuntimeCredentialResolver.cs -- o CryptographicException cruda, que puede escapar sin
/// envolver desde WhatsAppSecureVault.StoreAsync/Protect en el camino de Embedded Signup), nunca por
/// coincidencia de texto -- así no hay forma de que un mensaje de Meta que mencione "credencial" o
/// "token" se confunda con esto, y viceversa.
/// </summary>
public static class WhatsAppCredentialErrorClassifier
{
    /// <summary>
    /// Devuelve null si la excepción (o alguna en su cadena de InnerException) no es un caso de
    /// credencial/Vault inaccesible en este proceso -- en ese caso, quien llama debe seguir con su
    /// clasificación normal (p. ej. WhatsAppOutboundErrorClassifier para errores reales de Graph).
    /// </summary>
    public static AppUiMessage? ClassifyCredentialUnavailable(Exception? ex, bool isProduction)
    {
        var found = FindInChain(ex);
        if (found is null)
            return null;

        // Nunca "reconexión", nunca insinúa que la conexión de WhatsApp está rota -- eso es
        // exactamente la confusión que se reportó. El número sigue conectado; sólo este proceso no
        // puede leer su credencial ahora mismo.
        return isProduction
            ? AppUiMessage.Error(
                "No se pudo acceder a la credencial segura de WhatsApp",
                "Contactá al administrador.")
            : AppUiMessage.Error(
                "No se pudo acceder a la credencial segura de WhatsApp en este entorno",
                found.Message,
                "Esto es esperado si este proceso no tiene acceso al Data Protection/certificado del servidor donde vive el SecureVault. No significa que la conexión de WhatsApp esté rota ni que el token de Meta haya vencido.");
    }

    private static Exception? FindInChain(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is WhatsAppEmbeddedVaultUnavailableException or CryptographicException)
                return current;
        }

        return null;
    }
}

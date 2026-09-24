namespace AlfaCore.Services;

/// <summary>
/// Se lanza cuando, en modo SaaS, un servicio tenant-sensitive necesita conexión pero no hay una
/// base activa resuelta (ruta root sin sesión, URL tenant rechazada, login pendiente). Deriva de
/// <see cref="InvalidOperationException"/> para que los ExecuteLoggedAsync existentes la relancen
/// tal cual en vez de envolverla.
/// </summary>
public sealed class TenantSessionRequiredException(string consumer)
    : InvalidOperationException($"No hay una base activa para {consumer}. Ingresá desde la dirección de tu empresa (/{{idweb}}/{{idbase}}) o elegí una base.");

/// <summary>
/// Resolución de la cadena de conexión tenant para servicios de dominio.
/// Históricamente cada servicio hacía "sesión activa ?? ConnectionStrings:AlfaGestion". En modo
/// SaaS ese fallback es una base GLOBAL: una ruta root sin sesión (ej. /auditoria/errores abierta
/// directo) terminaba leyendo AUX_ERR de esa base, incluso sin usuario autenticado. Acá, en SaaS,
/// sin sesión activa no hay conexión: se falla cerrado ANTES de abrir cualquier SqlConnection.
/// En instalaciones clásicas (no SaaS) se mantiene el fallback, que ahí sí es la base del cliente.
/// </summary>
public static class TenantConnectionGuard
{
    public static bool TryResolve(
        ISessionService sessionService,
        IConfiguration configuration,
        IAppModeService appMode,
        out string connectionString)
    {
        var active = sessionService.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(active))
        {
            connectionString = active;
            return true;
        }

        if (appMode.IsSaaSMode)
        {
            connectionString = string.Empty;
            return false;
        }

        connectionString = configuration.GetConnectionString("AlfaGestion") ?? string.Empty;
        return connectionString.Length > 0;
    }

    public static string Resolve(
        ISessionService sessionService,
        IConfiguration configuration,
        IAppModeService appMode,
        string consumer)
    {
        if (TryResolve(sessionService, configuration, appMode, out var connectionString))
            return connectionString;

        if (appMode.IsSaaSMode)
            throw new TenantSessionRequiredException(consumer);

        throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");
    }
}

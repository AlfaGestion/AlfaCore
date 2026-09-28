namespace AlfaCore.Services;

/// <summary>
/// Regla de MainLayout para montar (o no) el <c>@Body</c> de la página. Antes la página se
/// renderizaba DEBAJO del overlay de login: su OnInitializedAsync corría igual y consultaba datos
/// (ej. /auditoria/errores leía AUX_ERR) sin usuario autorizado para la base activa. En SaaS la
/// página sólo se monta cuando hay usuario autenticado y autorizado para la base activa; mientras
/// tanto sólo se ve el overlay (que ya tapaba la página, así que visualmente no cambia nada).
/// Las páginas públicas y de selección de base usan PublicLayout y no pasan por acá.
/// En instalaciones clásicas (no SaaS) se mantiene el comportamiento histórico.
/// Excepción: la raíz exacta "/" (Launcher). Launcher no carga datos sin sesión autorizada y es
/// quien redirige a /login en ese caso; si no se montara, la redirección no ocurriría.
/// </summary>
public static class TenantBodyGate
{
    public static bool ShouldRenderBody(bool isSaaSMode, bool isAuthenticated, bool isActiveSessionAuthorized, bool isAppRootPath)
        => !isSaaSMode || isAppRootPath || (isAuthenticated && isActiveSessionAuthorized);
}

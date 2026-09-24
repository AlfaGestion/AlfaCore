using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConexionClienteService
{
    event Action? SessionChanged;

    string GetConnectionString();
    SessionDto? GetActiveSession();
    void SetWebhookOverride(SessionDto session);

    /// <summary>
    /// Devuelve el override fijado por <see cref="SetWebhookOverride"/> únicamente si su
    /// <c>BaseId</c> coincide con <paramref name="expectedBaseId"/> -- sin evaluar override de
    /// ruta, sin tocar <c>NavigationManager</c> y sin caer a la sesión SaaS. Pensado para que un
    /// caller que ya conoce autoritativamente el tenant (p. ej. el BaseId resuelto server-side por
    /// el token de un webhook) pueda resolver la conexión sin pasar por
    /// <see cref="GetActiveSession"/> en absoluto. Devuelve <c>null</c> si no hay override o si
    /// pertenece a otra base -- nunca expone la conexión de una base distinta a la esperada.
    /// </summary>
    SessionDto? GetWebhookOverride(int expectedBaseId);

    /// <summary>
    /// Da de baja el override fijado por <see cref="SetWebhookOverride"/>. Necesario para el login
    /// directo por ruta (/{idweb}/{idbase} sin sesión central previa): si la URL pasa a pedir OTRA
    /// base, la conexión activada para la anterior no debe seguir sirviendo como autorizada.
    /// </summary>
    void ClearWebhookOverride();

    IReadOnlyList<SessionDto> GetAllSessions();
    void SwitchSession(Guid id);
    Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password);
    void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password);
    void DeleteSession(Guid id);
    void ClearActiveSession();
}

using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConexionClienteService
{
    event Action? SessionChanged;

    string GetConnectionString();
    SessionDto? GetActiveSession();
    void SetWebhookOverride(SessionDto session);

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

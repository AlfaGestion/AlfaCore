using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConexionClienteService
{
    event Action? SessionChanged;

    string GetConnectionString();
    SessionDto? GetActiveSession();
    void SetWebhookOverride(SessionDto session);
    IReadOnlyList<SessionDto> GetAllSessions();
    void SwitchSession(Guid id);
    Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password);
    void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password);
    void DeleteSession(Guid id);
    void ClearActiveSession();
}

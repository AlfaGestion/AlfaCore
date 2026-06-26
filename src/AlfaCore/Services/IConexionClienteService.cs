using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConexionClienteService
{
    event Action? SessionChanged;

    string GetConnectionString();
    SessionDto? GetActiveSession();
    IReadOnlyList<SessionDto> GetAllSessions();
    void SwitchSession(Guid id);
    void AddSession(string nombre, string servidor, string baseDatos, string usuario, string password);
    void DeleteSession(Guid id);
    void ClearActiveSession();
}

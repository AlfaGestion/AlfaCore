using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class SessionService(IConexionClienteService conexionClienteService) : ISessionService
{
    public event Action? SessionChanged
    {
        add => conexionClienteService.SessionChanged += value;
        remove => conexionClienteService.SessionChanged -= value;
    }

    public string GetConnectionString() => conexionClienteService.GetConnectionString();
    public SessionDto? GetActiveSession() => conexionClienteService.GetActiveSession();
    public IReadOnlyList<SessionDto> GetAllSessions() => conexionClienteService.GetAllSessions();
    public void SwitchSession(Guid id) => conexionClienteService.SwitchSession(id);
    public void AddSession(string nombre, string servidor, string baseDatos, string usuario, string password)
        => conexionClienteService.AddSession(nombre, servidor, baseDatos, usuario, password);
    public void DeleteSession(Guid id) => conexionClienteService.DeleteSession(id);
    public void ClearActiveSession() => conexionClienteService.ClearActiveSession();
}

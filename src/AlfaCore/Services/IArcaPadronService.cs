using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IArcaPadronService
{
    Task<ArcaPadronPersonaDto> ConsultarPersonaAsync(string cuit, CancellationToken ct = default);
}

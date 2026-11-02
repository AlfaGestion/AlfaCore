using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IArcaPadronService
{
    Task<ArcaPadronPersonaDto> ConsultarPersonaAsync(string cuit, CancellationToken ct = default);

    /// <summary>Busca los CUIT de un DNI en ARCA (consulta central) y devuelve los datos de cada uno.</summary>
    Task<IReadOnlyList<ArcaPadronPersonaDto>> ConsultarDniAsync(string dni, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ArcaPadronPersonaDto>>([]);
}

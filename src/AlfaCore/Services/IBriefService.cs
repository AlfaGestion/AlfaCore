using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Resumen diario de solo lectura para un asistente externo. Usa su propia cadena de conexión
/// (usuario SQL de solo lectura), nunca la de la sesión.
/// </summary>
public interface IBriefService
{
    /// <summary>True si hay token y cadena de conexión configurados; si no, los endpoints no existen.</summary>
    bool EstaHabilitado { get; }

    /// <summary>Compara el token recibido con el configurado en tiempo constante.</summary>
    bool TokenValido(string? presentado);

    Task<BriefSoportePendientesResponse> GetSoportePendientesAsync(CancellationToken ct = default);

    Task<BriefActividadResponse> GetActividadAsync(DateTime desde, CancellationToken ct = default);

    Task<BriefOportunidadesResponse> GetOportunidadesAsync(bool soloMias, CancellationToken ct = default);
}

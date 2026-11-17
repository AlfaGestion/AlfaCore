using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Respuestas rápidas de Conversaciones: textos guardados por base que se insertan en el chat
/// escribiendo "::" y el atajo. Se administran en Configuración > Respuestas rápidas.
/// </summary>
public interface IConversacionesRespuestasRapidasService
{
    /// <summary>Las activas, ordenadas por atajo.</summary>
    Task<IReadOnlyList<ConversacionRespuestaRapidaDto>> GetAsync(CancellationToken ct = default);

    /// <summary>Alta (IdRespuesta = 0) o modificación. El atajo no puede repetirse entre las activas.</summary>
    Task<ConversacionRespuestaRapidaDto> GuardarAsync(ConversacionRespuestaRapidaDto respuesta, string? usuarioAccion, CancellationToken ct = default);

    /// <summary>Baja lógica (Activa = 0).</summary>
    Task EliminarAsync(int idRespuesta, string? usuarioAccion, CancellationToken ct = default);
}

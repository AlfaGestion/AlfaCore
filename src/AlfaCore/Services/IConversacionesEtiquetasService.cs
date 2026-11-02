using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Etiquetas de conversaciones (CONV_ETIQUETAS / CONV_CONVERSACION_ETIQUETAS): sirven para clasificar y filtrar la bandeja.</summary>
public interface IConversacionesEtiquetasService
{
    Task<IReadOnlyList<ConversacionEtiquetaDto>> GetEtiquetasAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<long, IReadOnlyList<ConversacionEtiquetaDto>>> GetEtiquetasPorConversacionAsync(IReadOnlyCollection<long> idsConversacion, CancellationToken ct = default);
    Task SetEtiquetasConversacionAsync(long idConversacion, IReadOnlyCollection<int> idsEtiqueta, string? usuarioAccion, CancellationToken ct = default);
    /// <summary>Crea la etiqueta (o reactiva la que ya existe con ese nombre) y la devuelve.</summary>
    Task<ConversacionEtiquetaDto> CrearEtiquetaAsync(string nombre, string? color, string? usuarioAccion, CancellationToken ct = default);
    Task DesactivarEtiquetaAsync(int idEtiqueta, string? usuarioAccion, CancellationToken ct = default);
}

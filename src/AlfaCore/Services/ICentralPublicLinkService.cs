using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralPublicLinkService
{
    /// <summary>
    /// Resuelve un link público a partir de IdWeb + Tipo + el segmento de ruta recibido
    /// ("slug-token" o "token" solo). Sólo mira el token (los últimos caracteres del segmento) —
    /// el slug nunca se usa para autorizar. Devuelve null si el token no existe, no coincide con
    /// ese IdWeb/Tipo, está inactivo o venció — siempre la misma respuesta "no encontrado", sin
    /// distinguir el motivo, para no revelar si la base existe.
    /// </summary>
    Task<PublicLinkDto?> ResolveAsync(string idWeb, string tipo, string routeSegment, CancellationToken ct = default);

    /// <summary>
    /// Busca un link activo existente para (IdWeb, IdBase, Tipo, IdReferencia) sin crearlo.
    /// Devuelve null si no existe un link activo válido.
    /// </summary>
    Task<PublicLinkDto?> TryGetExistingAsync(string idWeb, int idBase, string tipo, int idReferencia, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el link activo existente para (IdWeb, IdBase, Tipo, IdReferencia) o crea uno nuevo
    /// con un token aleatorio si no hay ninguno. Nunca genera un token distinto en cada llamada.
    /// </summary>
    Task<PublicLinkDto> GetOrCreateAsync(string idWeb, int idBase, string tipo, int idReferencia, string? nombreParaSlug, CancellationToken ct = default);

    /// <summary>
    /// Versión en lote de <see cref="GetOrCreateAsync"/> para pantallas que listan muchas filas
    /// (ej. la grilla de carritos de catálogo) — resuelve/crea todos los links en una sola
    /// consulta + un solo insert, en vez de un round-trip por fila.
    /// </summary>
    Task<IReadOnlyDictionary<int, PublicLinkDto>> GetOrCreateManyAsync(
        string idWeb,
        int idBase,
        string tipo,
        IReadOnlyList<(int IdReferencia, string? Nombre)> referencias,
        CancellationToken ct = default);

    /// <summary>Da de baja el link (Activo = 0). El recurso deja de ser accesible por esa URL.</summary>
    Task RevokeAsync(int idPublicLink, CancellationToken ct = default);

    /// <summary>Revoca el link actual y crea uno nuevo con token distinto para el mismo recurso.</summary>
    Task<PublicLinkDto> RegenerateAsync(int idPublicLink, CancellationToken ct = default);
}

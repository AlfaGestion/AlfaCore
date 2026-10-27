namespace AlfaCore.Services;

public interface IWebChatSiteKeyService
{
    /// <summary>Clave pública del chat web de la base activa, o null si todavía no se generó.</summary>
    Task<string?> GetSiteKeyAsync(CancellationToken ct = default);

    /// <summary>Devuelve la clave pública del chat web de la base activa, generándola la primera vez.</summary>
    Task<string> EnsureSiteKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// Resuelve a qué base pertenece una clave de sitio (request anónimo del widget) y, en SaaS,
    /// fija esa base como activa para el resto del request. Devuelve el IdBase (0 en instalaciones
    /// de una sola base) o null si la clave no corresponde a ninguna base: el caller responde 404
    /// sin revelar el motivo.
    /// </summary>
    Task<int?> TryResolveTenantAsync(string siteKey, CancellationToken ct = default);
}

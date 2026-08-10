using AlfaCore.Models;

namespace AlfaCore.Services;

public interface ICentralBasesService
{
    Task<IReadOnlyList<BaseCentralDto>> GetByClienteAsync(string idCliente, bool includeAllForSuperAdmin = false, CancellationToken ct = default);
    Task<BaseCentralDto?> GetByIdAsync(int idBase, CancellationToken ct = default);
    Task<IReadOnlyList<BaseCentralDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Resuelve a qué base pertenece un token de webhook. Es el único mecanismo con el que un
    /// request anónimo (sin sesión de usuario, ej. un webhook de Meta/MercadoLibre) puede saber
    /// a qué cliente pertenece — no depende de <c>ISessionService</c>.
    /// </summary>
    Task<BaseCentralDto?> GetByWebhookTokenAsync(string webhookToken, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el <c>WebhookToken</c> de la base indicada, generando y persistiendo uno nuevo
    /// la primera vez que se pide (las bases creadas antes de este mecanismo no tienen uno).
    /// </summary>
    Task<string> EnsureWebhookTokenAsync(int idBase, CancellationToken ct = default);
}

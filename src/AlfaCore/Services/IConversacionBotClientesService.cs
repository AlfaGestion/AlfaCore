using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionBotClientesService
{
    /// <summary>
    /// Tool registrar_cliente: con los datos que juntó el asistente busca si quien escribe ya es Cliente
    /// (DNI, teléfono, o nombre y dirección). Si es uno solo lo vincula a la conversación; si hay dudas no
    /// crea nada y lo deja para una persona; si no existe deja un borrador PENDIENTE para que confirme.
    /// Nunca graba el Cliente: eso lo hace <see cref="ConfirmarAsync"/> con el "sí" literal del contacto.
    /// </summary>
    Task<ConversacionBotAltaPreparada> PrepararAsync(long idConversacion, string argumentosJson, CancellationToken ct = default);

    Task<bool> HayPendienteAsync(long idConversacion, CancellationToken ct = default);

    /// <summary>Da de alta el Cliente del borrador pendiente y lo vincula a la conversación.</summary>
    Task<ConversacionBotAltaConfirmada> ConfirmarAsync(long idConversacion, CancellationToken ct = default);

    /// <summary>Descarta el borrador pendiente. Devuelve el texto para el contacto.</summary>
    Task<string> CancelarAsync(long idConversacion, CancellationToken ct = default);

    /// <summary>El único Cliente activo cuyo teléfono cargado coincide con el del chat; null si ninguno o varios.</summary>
    Task<ConversacionCuentaVinculadaDto?> BuscarPorTelefonoAsync(long idConversacion, CancellationToken ct = default);
}

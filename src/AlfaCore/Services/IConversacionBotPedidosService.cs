using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IConversacionBotPedidosService
{
    /// <summary>
    /// Tool preparar_pedido: resuelve los artículos contra el catálogo web y deja un borrador PENDIENTE
    /// (reemplaza al anterior). Devuelve la instrucción para el modelo; nunca graba la Nota de Pedido.
    /// </summary>
    Task<string> PrepararAsync(long idConversacion, ConversacionCuentaVinculadaDto cuenta, string argumentosJson, CancellationToken ct = default);

    Task<bool> HayPendienteAsync(long idConversacion, string codigoCliente, CancellationToken ct = default);

    /// <summary>Graba la NP del borrador pendiente con el circuito del carrito web. Devuelve el texto para el cliente.</summary>
    Task<string> ConfirmarAsync(long idConversacion, ConversacionCuentaVinculadaDto cuenta, CancellationToken ct = default);

    /// <summary>Descarta el borrador pendiente. Devuelve el texto para el cliente.</summary>
    Task<string> CancelarAsync(long idConversacion, string codigoCliente, CancellationToken ct = default);
}

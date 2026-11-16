using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Cocina / barra del modo Salon: pantalla de comandas (KDS), avance de cada plato
/// (enviado -> en marcha -> listo), platos agotados y cola de impresion para comanderas.
/// Una comanda (POS_COMANDA) nace en cada envio a preparar, una por destino.
/// </summary>
public interface IPuntoVentaCocinaService
{
    /// <summary>Comandas de hoy con algo pendiente de servir. <paramref name="destino"/> vacio = todos.</summary>
    Task<IReadOnlyList<PuntoVentaCocinaComandaDto>> GetComandasAsync(int idPuntoVenta, string? destino, CancellationToken ct = default);
    Task<PuntoVentaCocinaComandaDto?> GetComandaAsync(int idComanda, CancellationToken ct = default);

    /// <summary>Enviado -> En marcha -> Listo; tocar un plato listo lo vuelve a En marcha.</summary>
    Task AvanzarLineaAsync(int idLinea, string? usuarioAccion = null, CancellationToken ct = default);

    /// <summary>Pasa todos los platos de la comanda a <paramref name="estado"/> (EN_PREPARACION, LISTO o SERVIDO).</summary>
    Task MarcarComandaAsync(int idComanda, string estado, string? usuarioAccion = null, CancellationToken ct = default);

    /// <summary>
    /// Reserva (de forma atomica) las comandas sin imprimir de la ultima hora para esta estacion y las
    /// devuelve. Si hay dos comanderas abiertas para el mismo destino, cada comanda sale una sola vez.
    /// </summary>
    Task<IReadOnlyList<PuntoVentaCocinaComandaDto>> ReclamarParaImprimirAsync(int idPuntoVenta, string? destino, string estacion, CancellationToken ct = default);

    Task<IReadOnlyList<PuntoVentaAgotadoDto>> GetAgotadosAsync(int idPuntoVenta, CancellationToken ct = default);
    Task SetAgotadoAsync(int idPuntoVenta, string idArticulo, bool agotado, string? usuarioAccion = null, CancellationToken ct = default);
}

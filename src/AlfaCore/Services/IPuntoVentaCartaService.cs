using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Carta QR del modo Salon. Lo "publico" (sin login) entra por /{idweb}/mesa/{token}: el token
/// (ALFA_PUBLIC_LINK tipo MESA) resuelve la base y la mesa; para pedir o llamar al mozo el
/// celular tiene que estar unido a la mesa con su PIN (lo ve quien la abre y el mozo).
/// Todo lo que llega del celular se revalida aca: PIN, mesa abierta, articulo en la carta,
/// no agotado, precio del servidor (nunca el del navegador) y limite de pedidos por minuto.
/// </summary>
public interface IPuntoVentaCartaService
{
    // ----- configuracion (usuario logueado)
    Task<PuntoVentaCartaConfigDto> GetConfigAsync(int idPuntoVenta, CancellationToken ct = default);
    Task SaveConfigAsync(PuntoVentaCartaConfigDto config, string? usuarioAccion = null, CancellationToken ct = default);

    /// <summary>Carta armada: familias de la carta con sus articulos con precio, sin agotados.</summary>
    Task<PuntoVentaCartaDto> GetCartaAsync(int idPuntoVenta, CancellationToken ct = default);

    // ----- carta publica
    Task<PuntoVentaMesaPublicaDto?> GetMesaPublicaAsync(int idMesa, CancellationToken ct = default);

    /// <summary>Abre la mesa desde el celular. Devuelve null si ya estaba abierta (hay que pedir el PIN).</summary>
    Task<PuntoVentaMesaSesionDto?> AbrirMesaAsync(int idMesa, CancellationToken ct = default);

    /// <summary>Une el celular a la mesa abierta con su PIN. Null = PIN incorrecto o mesa cerrada.</summary>
    Task<PuntoVentaMesaSesionDto?> UnirseAsync(int idMesa, string pin, CancellationToken ct = default);

    Task<PuntoVentaMesaEstadoPublicoDto?> GetEstadoAsync(int idMesa, PuntoVentaMesaSesionDto sesion, CancellationToken ct = default);
    Task PedirAsync(int idMesa, PuntoVentaMesaSesionDto sesion, IReadOnlyList<PuntoVentaCartaItemRequest> items, CancellationToken ct = default);
    Task LlamarAsync(int idMesa, PuntoVentaMesaSesionDto sesion, string tipo, CancellationToken ct = default);
}

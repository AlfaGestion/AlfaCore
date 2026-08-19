using System.Text.RegularExpressions;

namespace AlfaCore.Services;

// El pedido web escribe este texto en COMENTARIOS (InterfacesCatalogosService.ConfirmarPedidoCarritoAsync).
// No hay otra columna que marque el origen; se parsea este formato controlado (lo genera el propio
// AlfaCore, no es texto libre de un tercero) en vez de inventar un flag nuevo en la base. Compartido
// por PortalClienteService (cliente) y CarritoComprasService (admin) para no duplicar la lógica.
public static class PedidoWebOrigenHelper
{
    private static readonly Regex PedidoWebRegex = new(
        @"^Pedido web - Catálogo #(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (bool EsWeb, int? IdCatalogo) Parse(string? comentarios)
    {
        var match = PedidoWebRegex.Match((comentarios ?? string.Empty).Trim());
        if (!match.Success)
            return (false, null);

        return int.TryParse(match.Groups[1].Value, out var idCatalogo)
            ? (true, idCatalogo)
            : (true, null);
    }
}

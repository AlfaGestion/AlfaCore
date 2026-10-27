using System.Text.Json;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Visibilidad de precios de un catálogo servido por URL pública/anónima. Con
/// CATALOGO_MUESTRA_PRECIO_CONSUMIDOR=0 ("Mostrar precios a consumidores finales / leads"
/// desactivado) el visitante anónimo no recibe precios: se quitan del DTO del lado servidor (no sólo
/// se ocultan en HTML). El carrito y la confirmación de pedido NO usan esto: trabajan con el
/// catálogo completo y un cliente autenticado.
/// </summary>
public static class CatalogosPublicPriceVisibility
{
    /// <summary>
    /// Copia del catálogo sin ningún dato de precio (Precio, PrecioOferta, PrecioOfertaAnterior,
    /// PrecioOfertaNuevo en null) y <see cref="CatalogosCatalogoDetalleDto.PreciosVisibles"/> en
    /// false. Nunca modifica <paramref name="catalogo"/> (puede estar cacheado y compartirse).
    /// </summary>
    public static CatalogosCatalogoDetalleDto SinPrecios(CatalogosCatalogoDetalleDto catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo);

        var copia = JsonSerializer.Deserialize<CatalogosCatalogoDetalleDto>(JsonSerializer.Serialize(catalogo))
            ?? throw new InvalidOperationException("No se pudo copiar el catálogo.");
        copia.PreciosVisibles = false;
        foreach (var articulo in copia.Articulos)
        {
            articulo.Precio = null;
            articulo.PrecioOferta = null;
            articulo.PrecioOfertaAnterior = null;
            articulo.PrecioOfertaNuevo = null;
        }

        return copia;
    }

    /// <summary>Catálogo a mostrar a quien lo pide: completo si los precios se pueden mostrar o si el
    /// visitante está autenticado como cliente; si no, sin precios.</summary>
    public static CatalogosCatalogoDetalleDto ParaVisitante(CatalogosCatalogoDetalleDto catalogo, bool muestraPreciosConsumidorFinal, bool visitanteAutenticado)
        => muestraPreciosConsumidorFinal || visitanteAutenticado ? catalogo : SinPrecios(catalogo);
}

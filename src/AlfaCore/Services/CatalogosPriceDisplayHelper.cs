using AlfaCore.Models;

namespace AlfaCore.Services;

public static class CatalogosPriceDisplayHelper
{
    public static bool HasValidOffer(CatalogosCatalogoItemDto item)
    {
        var oferta = ResolveOfferPrice(item);
        var precio = item.Precio.GetValueOrDefault();
        return oferta.HasValue && oferta.Value > 0m && precio > 0m && oferta.Value < precio;
    }

    public static int GetOfertaPercent(CatalogosCatalogoItemDto item)
    {
        var oferta = ResolveOfferPrice(item);
        var precio = item.Precio.GetValueOrDefault();
        if (!oferta.HasValue || oferta.Value <= 0m || precio <= 0m)
            return 0;

        var descuento = 1m - (oferta.Value / precio);
        return (int)Math.Round((double)(descuento * 100m), 0, MidpointRounding.AwayFromZero);
    }

    public static string GetOfertaHastaTexto(CatalogosCatalogoItemDto item)
        => item.OfertaHasta.HasValue
            ? $"Válido hasta {item.OfertaHasta.Value:dd/MM/yyyy}"
            : string.Empty;

    public static string Money(decimal? value)
        => value.HasValue ? value.Value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) : "—";

    public static decimal GetPrecioAplicado(CatalogosCatalogoItemDto item)
    {
        var oferta = ResolveOfferPrice(item);
        return oferta.HasValue && oferta.Value > 0m
            ? oferta.Value
            : item.Precio.GetValueOrDefault();
    }

    private static decimal? ResolveOfferPrice(CatalogosCatalogoItemDto item)
        => item.PrecioOfertaNuevo is > 0m
            ? item.PrecioOfertaNuevo
            : item.PrecioOferta is > 0m
                ? item.PrecioOferta
                : null;
}

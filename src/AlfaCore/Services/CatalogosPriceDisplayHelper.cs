using AlfaCore.Models;

namespace AlfaCore.Services;

public static class CatalogosPriceDisplayHelper
{
    public static bool HasValidOffer(CatalogosCatalogoItemDto item)
        => item.PrecioOferta.HasValue
           && item.PrecioOferta.Value > 0m
           && item.PrecioOferta.Value < item.Precio;

    public static int GetOfertaPercent(CatalogosCatalogoItemDto item)
    {
        if (!HasValidOffer(item) || item.Precio is not > 0m)
            return 0;

        var descuento = 1m - (item.PrecioOferta!.Value / item.Precio!.Value);
        return (int)Math.Round(descuento * 100m, MidpointRounding.AwayFromZero);
    }

    public static string GetOfertaHastaTexto(CatalogosCatalogoItemDto item)
        => item.OfertaHasta.HasValue
            ? $"Válido hasta {item.OfertaHasta.Value:dd/MM/yyyy}"
            : string.Empty;

    public static string Money(decimal? value)
        => value.HasValue ? value.Value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) : "—";

    public static decimal GetPrecioAplicado(CatalogosCatalogoItemDto item)
        => HasValidOffer(item) ? item.PrecioOferta!.Value : (item.Precio ?? 0m);
}

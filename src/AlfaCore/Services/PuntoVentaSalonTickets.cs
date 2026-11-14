using System.Globalization;
using System.Net;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Tickets de 80 mm del salon: precuenta de la mesa y comanda de cocina/barra.</summary>
public static class PuntoVentaSalonTickets
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    public static string FmtMoney(decimal value) => value.ToString("C2", Ar);

    public static string FmtQty(decimal value)
        => value.ToString(value == Math.Truncate(value) ? "0" : "0.###", Ar);

    public static string DestinoTexto(string destino) => destino switch
    {
        PuntoVentaSalonDestinoKeys.Cocina => "COCINA",
        PuntoVentaSalonDestinoKeys.Barra => "BARRA",
        PuntoVentaSalonDestinoKeys.Barra2 => "BARRA 2",
        _ => destino
    };

    /// <summary>
    /// Comanda para la comandera: letra grande, sin precios. Varias comandas se imprimen en un solo
    /// trabajo con corte de pagina entre cada una (la termica corta en cada page-break).
    /// </summary>
    public static string BuildComandasHtml(IEnumerable<PuntoVentaCocinaComandaDto> comandas, bool reimpresion = false)
    {
        ArgumentNullException.ThrowIfNull(comandas);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        var cuerpo = string.Join("<div class=\"corte\"></div>", comandas.Select(c =>
        {
            var lineas = string.Join(string.Empty, c.Lineas.Select(l =>
                $"<div class=\"item\"><b>{FmtQty(l.Cantidad)}</b> {E(l.Descripcion)}"
                + (string.IsNullOrWhiteSpace(l.Observaciones) ? string.Empty : $"<div class=\"obs\">&gt; {E(l.Observaciones)}</div>")
                + "</div>"));
            var mozo = string.IsNullOrWhiteSpace(c.Mozo) ? string.Empty : $" · Mozo: {E(c.Mozo)}";
            var cubiertos = c.Cubiertos is > 0 ? $" · {c.Cubiertos} pers." : string.Empty;
            return $"""
                <div class="cab">{E(DestinoTexto(c.Destino))} · #{c.IdComanda}{(reimpresion ? " (REIMPRESION)" : string.Empty)}</div>
                <div class="mesa">{E(c.NombreMesa)}</div>
                <div class="datos">{E(c.NombreSector)} · {c.FechaHora.ToString("HH:mm", CultureInfo.InvariantCulture)}{mozo}{cubiertos}</div>
                {lineas}
                """;
        }));

        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Comanda</title>
            <style>
              @page { margin: 3mm; }
              body { font-family: Arial, sans-serif; width: 72mm; margin: 0; color: #000; }
              .cab { font-size: 13px; font-weight: bold; border-bottom: 2px solid #000; padding-bottom: 2px; }
              .mesa { font-size: 26px; font-weight: bold; margin: 4px 0 0; }
              .datos { font-size: 12px; margin-bottom: 6px; border-bottom: 1px dashed #000; padding-bottom: 4px; }
              .item { font-size: 17px; margin: 4px 0; }
              .obs { font-size: 14px; font-style: italic; margin-left: 14px; }
              .corte { page-break-after: always; break-after: page; height: 1px; }
            </style></head><body>
            {{cuerpo}}
            </body></html>
            """;
    }

    public static string BuildHtml(string nombrePuntoVenta, PuntoVentaSalonMesaDetalleDto detalle)
    {
        ArgumentNullException.ThrowIfNull(detalle);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        var filas = detalle.Lineas
            .Where(l => l.Estado != PuntoVentaSalonLineaEstadoKeys.Anulado)
            .GroupBy(l => new { IdArticulo = l.IdArticulo.Trim(), l.Precio })
            .Select(g => $"<tr><td>{FmtQty(g.Sum(x => x.Cantidad))}</td><td>{E(g.First().Descripcion)}</td><td class=\"r\">{FmtMoney(g.Sum(x => x.Subtotal))}</td></tr>");

        var mozo = string.IsNullOrWhiteSpace(detalle.MozoNombre) ? detalle.Mozo : detalle.MozoNombre;
        var datos = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            + (string.IsNullOrWhiteSpace(mozo) ? string.Empty : " · Mozo: " + E(mozo))
            + (detalle.Cubiertos is > 0 ? $" · {detalle.Cubiertos} cubiertos" : string.Empty);

        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Precuenta</title>
            <style>
              @page { margin: 4mm; }
              body { font-family: monospace; font-size: 12px; width: 72mm; margin: 0; color: #000; }
              h1 { font-size: 15px; text-align: center; margin: 0 0 4px; }
              p { margin: 2px 0; }
              table { width: 100%; border-collapse: collapse; margin: 6px 0; }
              td { vertical-align: top; padding: 1px 2px 1px 0; }
              td.r { text-align: right; white-space: nowrap; }
              .tot { border-top: 1px dashed #000; font-size: 15px; font-weight: bold; display: flex; justify-content: space-between; padding-top: 4px; }
              .pie { text-align: center; margin-top: 8px; }
            </style></head><body>
            <h1>{{E(nombrePuntoVenta)}}</h1>
            <p>{{E(detalle.NombreMesa)}} - {{E(detalle.NombreSector)}}</p>
            <p>{{datos}}</p>
            <table>{{string.Join(string.Empty, filas)}}</table>
            <div class="tot"><span>TOTAL</span><span>{{FmtMoney(detalle.Total)}}</span></div>
            <p class="pie">PRECUENTA - NO VALIDO COMO FACTURA</p>
            </body></html>
            """;
    }
}

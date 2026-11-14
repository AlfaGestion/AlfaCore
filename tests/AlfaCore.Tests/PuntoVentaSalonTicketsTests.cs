using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class PuntoVentaSalonTicketsTests
{
    private static PuntoVentaSalonMesaDetalleDto Detalle() => new()
    {
        IdPedido = 1,
        NombreMesa = "Mesa 5",
        NombreSector = "Terraza",
        MozoNombre = "Juan",
        Cubiertos = 2,
        Lineas =
        [
            new() { IdArticulo = "101", Descripcion = "Cerveza", Cantidad = 1, Precio = 1000m, Estado = PuntoVentaSalonLineaEstadoKeys.Servido },
            new() { IdArticulo = "101 ", Descripcion = "Cerveza", Cantidad = 2, Precio = 1000m, Estado = PuntoVentaSalonLineaEstadoKeys.Enviado },
            new() { IdArticulo = "200", Descripcion = "Papas <bravas>", Cantidad = 1, Precio = 500m, Estado = PuntoVentaSalonLineaEstadoKeys.Pendiente },
            new() { IdArticulo = "300", Descripcion = "Pulpo", Cantidad = 1, Precio = 9000m, Estado = PuntoVentaSalonLineaEstadoKeys.Anulado }
        ]
    };

    [Fact]
    public void Total_excluye_lineas_anuladas()
        => Assert.Equal(3500m, Detalle().Total);

    [Fact]
    public void Precuenta_agrupa_rondas_y_omite_anulados()
    {
        var html = PuntoVentaSalonTickets.BuildHtml("Bar", Detalle());

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Cerveza"));
        Assert.Contains("<td>3</td><td>Cerveza</td>", html);
        Assert.DoesNotContain("Pulpo", html);
        Assert.Contains("NO VALIDO COMO FACTURA", html);
    }

    [Fact]
    public void Comandas_salen_sin_precios_con_aclaracion_y_corte_entre_tickets()
    {
        var comandas = new[]
        {
            new PuntoVentaCocinaComandaDto
            {
                IdComanda = 7, Destino = PuntoVentaSalonDestinoKeys.Cocina, NombreMesa = "Mesa 2", NombreSector = "Terraza",
                FechaHora = new DateTime(2026, 11, 14, 21, 5, 0), Mozo = "Lucia",
                Lineas = [new() { Descripcion = "Rabas <xl>", Cantidad = 2, Observaciones = "sin limon" }]
            },
            new PuntoVentaCocinaComandaDto
            {
                IdComanda = 8, Destino = PuntoVentaSalonDestinoKeys.Barra, NombreMesa = "Mesa 2", FechaHora = DateTime.Today,
                Lineas = [new() { Descripcion = "Pinta", Cantidad = 1 }]
            }
        };

        var html = PuntoVentaSalonTickets.BuildComandasHtml(comandas);

        Assert.Contains("COCINA · #7", html);
        Assert.Contains("BARRA · #8", html);
        Assert.Contains("Rabas &lt;xl&gt;", html);
        Assert.Contains("&gt; sin limon", html);
        Assert.Contains("21:05", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"corte\""));
        Assert.DoesNotContain("$", html);
    }

    [Fact]
    public void Reimpresion_queda_marcada()
        => Assert.Contains("(REIMPRESION)", PuntoVentaSalonTickets.BuildComandasHtml(
            [new PuntoVentaCocinaComandaDto { IdComanda = 1, Destino = PuntoVentaSalonDestinoKeys.Barra2 }], reimpresion: true));

    [Theory]
    [InlineData("TAPAS Y PICADAS", "Tapas y picadas")]
    [InlineData("Café con leche", "Café con leche")]
    [InlineData("  ", "")]
    public void Titulo_de_la_carta_pasa_mayusculas_a_oracion(string entrada, string esperado)
        => Assert.Equal(esperado, PuntoVentaCartaService.ToTitulo(entrada));

    [Theory]
    [InlineData("pedido", "PEDIDO")]
    [InlineData("CARTA", "CARTA")]
    [InlineData("cualquiera", "NO")]
    [InlineData(null, "NO")]
    public void Modo_qr_desconocido_queda_desactivado(string? entrada, string esperado)
        => Assert.Equal(esperado, PuntoVentaCartaService.NormalizeQrModo(entrada, PuntoVentaQrModoKeys.No));

    [Fact]
    public void Precuenta_escapa_html_de_las_descripciones()
    {
        var html = PuntoVentaSalonTickets.BuildHtml("Bar & Co", Detalle());

        Assert.Contains("Papas &lt;bravas&gt;", html);
        Assert.Contains("Bar &amp; Co", html);
    }
}

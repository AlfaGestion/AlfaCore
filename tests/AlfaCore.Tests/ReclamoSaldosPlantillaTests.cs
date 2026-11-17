using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ReclamoSaldosPlantillaTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(15, 1)]
    [InlineData(16, 2)]
    [InlineData(45, 2)]
    [InlineData(46, 3)]
    public void ElegirTramo_SegunDiasDeAtraso(int dias, int esperado)
        => Assert.Equal(esperado, ReclamoSaldosPlantilla.ElegirTramo(dias, 15, 45));

    [Fact]
    public void ReemplazarHtml_EscapaElTextoDelUsuarioYInsertaElDetalleComoHtml()
    {
        var valores = new Dictionary<string, string> { ["<NOMBRE>"] = "Pérez &amp; Cía", ["<DETALLE>"] = "<table></table>" };
        var html = ReclamoSaldosPlantilla.ReemplazarHtml("Hola <NOMBRE>\n<script>x</script>\n<detalle>", valores);
        Assert.Contains("Pérez &amp; Cía", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<table></table>", html);
        Assert.Contains("<br>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void DetalleUnaLinea_NoTieneSaltosDeLineaYResumeLosRestantes()
    {
        var cli = new ReclamoCliente { Saldo = 1500m, SumaComprobantes = 1500m, Vencido = 1000m };
        var cptes = Enumerable.Range(1, 6).Select(i => new ReclamoComprobante { Tc = "FP", Numero = $"0001-{i:00000000} X", Saldo = 250m, Vencimiento = new DateTime(2026, 10, i) }).ToList();
        var s = ReclamoSaldosPlantilla.DetalleUnaLinea(cptes, cli, 3);
        Assert.DoesNotContain('\n', s);
        Assert.Contains("y 3 más", s);
        Assert.Contains("vencido", s);
    }

    [Theory]
    [InlineData("0000-0000", "")]
    [InlineData("", "")]
    [InlineData("+54 11 1111-1111", "+541111111111")]
    [InlineData("011 4444 5555", "01144445555")]
    public void NormalizarTelefono_DescartaPlaceholders(string entrada, string esperado)
        => Assert.Equal(esperado, ReclamoSaldosPlantilla.NormalizarTelefono(entrada));

    [Fact]
    public void Plantilla_CuentaYRellenaVariables()
    {
        const string cuerpo = "Hola {{1}}, tu deuda: {{2}}. Pagá por {{3}}.";
        Assert.Equal(3, ReclamoSaldosPlantilla.ContarVariablesPlantilla(cuerpo));
        Assert.Equal("Hola Ana, tu deuda: $ 5. Pagá por CBU.", ReclamoSaldosPlantilla.RellenarPlantilla(cuerpo, ["Ana", "$ 5", "CBU"]));
    }

    [Fact]
    public void TarjetasHtml_MuestraLosCuatroDatosConColoresPropios()
    {
        var html = ReclamoSaldosPlantilla.TarjetasHtml(new ReclamoCliente { Saldo = 1500m, Vencido = 1000m, CantidadComprobantes = 3, DiasAtraso = 20 });
        Assert.Contains("Saldo total", html);
        Assert.Contains("Vencido", html);
        Assert.Contains("Comprobantes", html);
        Assert.Contains("atraso", html); // "Días" sale como entidad HTML
        Assert.Contains("#b42318", html); // vencido en rojo, independiente del tema de la app
        Assert.Contains("Sin atraso", ReclamoSaldosPlantilla.TarjetasHtml(new ReclamoCliente { Saldo = 5m }));
    }

    [Fact]
    public void PortalHtml_EscapaLaUrlYAvisaElVencimiento()
    {
        var html = ReclamoSaldosPlantilla.PortalHtml("https://x.test/portal-cliente/acceso?token=a&email=b", new DateTime(2026, 12, 1));
        Assert.Contains("token=a&amp;email=b", html);
        Assert.Contains("Vence el 01/12/2026", html);
        Assert.DoesNotContain("<script", ReclamoSaldosPlantilla.DocumentoVistaPrevia(html));
    }

    [Fact]
    public void DetalleHtml_AvisaCuandoElSaldoDifiereDeLaSuma()
    {
        var cli = new ReclamoCliente { Saldo = 100m, SumaComprobantes = 150m };
        var html = ReclamoSaldosPlantilla.DetalleHtml([new ReclamoComprobante { Tc = "FP", Numero = "0001-1", Importe = 150m, Saldo = 150m, Fecha = DateTime.Today }], cli);
        Assert.Contains("notas de crédito", html);
    }
}

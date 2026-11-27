using Xunit;
using AlfaCore.Models;
using AlfaCore.Services;

namespace AlfaCore.Tests;

public class ComprobantesTablaServiceTests
{
    private static ComprobanteTalonario Talonario(string codigo = "FC", int talonario = 1, int pv = 1007, string letras = "AB")
    {
        var t = new ComprobanteTalonario
        {
            Codigo = codigo, Talonario = talonario, Descripcion = "FACTURA", Sistema = "Ventas", PuntoVenta = pv, Letras = letras,
            Numeracion = ComprobantesTablaService.LetrasOrden.Select(l => new ComprobanteLetra { Letra = l }).ToList()
        };
        foreach (var l in letras) { var n = t.Letra(l.ToString()); n.Desde = 1; n.Hasta = 99_999_999; n.Proximo = 1; }
        return t;
    }

    [Fact]
    public void Factura_fuerza_salida_debe_ventas_y_signo()
    {
        var t = Talonario();
        t.Sistema = "Compras"; t.EntradaSalida = "E"; t.DebeHaber = "H";
        ComprobantesTablaService.Validar(t, 2);
        Assert.Equal(("Ventas", "S", "D", "1"), (t.Sistema, t.EntradaSalida, t.DebeHaber, t.Signo));
    }

    [Fact]
    public void Contable_no_lleva_stock_ni_cuenta_ni_deposito()
    {
        var t = Talonario("ZZ");
        t.Sistema = "contable"; t.EntradaSalida = "E"; t.DebeHaber = "D"; t.Deposito = "1";
        ComprobantesTablaService.Validar(t, 2);
        Assert.Equal(("Contable", "", "", ""), (t.Sistema, t.EntradaSalida, t.DebeHaber, t.Deposito));
    }

    [Fact]
    public void Stock_admite_entrada_salida_pero_no_cuenta()
    {
        var t = Talonario("ZZ");
        t.Sistema = "STOCK"; t.EntradaSalida = "S"; t.DebeHaber = "H";
        ComprobantesTablaService.Validar(t, 2);
        Assert.Equal(("Stock", "S", ""), (t.Sistema, t.EntradaSalida, t.DebeHaber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Punto_de_venta_fuera_de_rango(int pv)
        => Assert.ThrowsAny<InvalidOperationException>(() => ComprobantesTablaService.Validar(Talonario(pv: pv), 2));

    [Fact]
    public void Proximo_fuera_del_rango_se_rechaza()
    {
        var t = Talonario();
        t.Letra("A").Desde = 10; t.Letra("A").Hasta = 20; t.Letra("A").Proximo = 21;
        var ex = Assert.ThrowsAny<InvalidOperationException>(() => ComprobantesTablaService.Validar(t, 2));
        Assert.Contains("Letra A", ex.Message);
    }

    [Fact]
    public void Desde_mayor_que_hasta_se_rechaza()
    {
        var t = Talonario();
        t.Letra("B").Desde = 30; t.Letra("B").Hasta = 20; t.Letra("B").Proximo = 25;
        Assert.ThrowsAny<InvalidOperationException>(() => ComprobantesTablaService.Validar(t, 2));
    }

    [Fact]
    public void Sin_rango_no_controla_el_proximo_y_letras_no_habilitadas_tampoco()
    {
        var t = Talonario(letras: "X");
        t.Letra("X").Desde = 0; t.Letra("X").Hasta = 0; t.Letra("X").Proximo = 3869;
        t.Letra("A").Desde = 50; t.Letra("A").Hasta = 10; // A no está habilitada
        ComprobantesTablaService.Validar(t, 2);
    }

    [Fact]
    public void Externo_no_valida_numeracion_y_habilita_todas_las_letras()
    {
        var t = Talonario("FCC", 0, pv: 0, letras: "A");
        t.Sistema = "Compras"; t.Externo = true;
        t.Letra("A").Desde = 0; t.Letra("A").Hasta = 0; t.Letra("A").Proximo = 20200507;
        ComprobantesTablaService.Validar(t, 2);
        Assert.Equal((1, "ABCXREM"), (t.PuntoVenta, t.Letras));
    }

    [Fact]
    public void Cpte_afip_respeta_el_ancho_de_la_columna()
    {
        var t = Talonario();
        t.Letra("A").CpteAfip = "201";
        Assert.ThrowsAny<InvalidOperationException>(() => ComprobantesTablaService.Validar(t, 2));
        ComprobantesTablaService.Validar(t, 4);
    }

    [Fact]
    public void Rango_superpuesto_solo_con_mismo_codigo_y_punto_de_venta()
    {
        var nuevo = Talonario(talonario: 4, pv: 1007);
        var mismoPv = Talonario(talonario: 3, pv: 1007, letras: "B");
        var otroPv = Talonario(talonario: 2, pv: 6);
        Assert.Null(ComprobantesTablaService.RangoSuperpuesto(nuevo, [otroPv]));
        var sup = ComprobantesTablaService.RangoSuperpuesto(nuevo, [otroPv, mismoPv]);
        Assert.Equal((3, "B"), (sup!.Value.Otro.Talonario, sup.Value.Letra));

        mismoPv.Letra("B").Desde = 1; mismoPv.Letra("B").Hasta = 1000;
        nuevo.Letra("B").Desde = 1001; nuevo.Letra("B").Proximo = 1001;
        Assert.Null(ComprobantesTablaService.RangoSuperpuesto(nuevo, [mismoPv]));
    }

    [Theory]
    [InlineData(new[] { "A", "B", "C", "X", "R", "E", "M" }, "A", "ABCXREM", "ABCXREM")]
    [InlineData(new[] { "A", "B" }, "B", "AB", "BA")]
    [InlineData(new[] { "X", "A" }, null, null, "AX")]
    [InlineData(new string[0], null, "AB", "")]
    public void Ordenar_letras(string[] habilitadas, string? porDefecto, string? original, string esperado)
        => Assert.Equal(esperado, ComprobantesTablaService.OrdenarLetras(habilitadas, porDefecto, original));
}

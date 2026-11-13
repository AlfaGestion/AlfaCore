using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class LibroIvaArcaValidatorTests
{
    private static LibroIvaResultado Libro(bool compras = false) => new()
    {
        CuitEmisor = "30709390356",
        Filtro = new() { Compras = compras, Desde = new(2026, 10, 1), Hasta = new(2026, 10, 31) },
        Filas = [new() { Fecha = new(2026, 10, 2), Tc = compras ? "FCC" : "FC", Letra = "A", CodigoArca = 1,
            PuntoVenta = 1, Numero = 1, RazonSocial = "Prueba", Cuit = "30709390356", NetoGravado = 1000,
            Total = 1210, Alicuotas = [new(21, 1000, 210)] }]
    };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExportacionValidaCuadraCabecerasYAlicuotas(bool compras)
    {
        var r = Libro(compras);
        Assert.Empty(LibroIvaArcaValidator.Validar(r));
        var (c, a) = compras ? LibroIvaService.ArmarTxtCompras(r.Filas) : LibroIvaService.ArmarTxt(r.Filas);
        LibroIvaArcaValidator.ValidarFormato(r, c, a);
        Assert.Throws<InvalidOperationException>(() => LibroIvaArcaValidator.ValidarFormato(r, c, ""));
        Assert.Throws<InvalidOperationException>(() => LibroIvaArcaValidator.ValidarFormato(r, c, a.Replace("000000000100000", "000000000200000")));
    }

    [Fact]
    public void DetectaCuitTotalesDuplicadosYCodigosSinOmitirFilas()
    {
        var r = Libro(); var f = r.Filas[0];
        f.Cuit = "30709390357"; f.Total = 1211; f.CodigoArca = 0;
        r.Filas = [f, f];
        var e = LibroIvaArcaValidator.Validar(r);
        Assert.Contains(e, x => x.Contains("Documento/CUIT inválido"));
        Assert.Contains(e, x => x.Contains("no coincide con netos"));
        Assert.Contains(e, x => x.Contains("sin equivalencia"));
        Assert.Contains(e, x => x.Contains("duplicado"));
    }

    [Fact]
    public void RechazaTasaDesconocidaDesbordeYSignosMezclados()
    {
        var r = Libro(); var f = r.Filas[0];
        f.NoGravado = -1; f.Total = 10000000000000m; f.Alicuotas = [new(22, 1000, 210)];
        var e = LibroIvaArcaValidator.Validar(r);
        Assert.Contains(e, x => x.Contains("Alícuota no admitida"));
        Assert.Contains(e, x => x.Contains("13 enteros"));
        Assert.Contains(e, x => x.Contains("signo opuesto"));
    }

    [Theory]
    [InlineData("30-70939035-6", true)] [InlineData("30709390357", false)]
    [InlineData("99999999995", false)] [InlineData("00000000000", false)]
    [InlineData("x30709390356", false)]
    public void VerificaDigitoDelCuit(string cuit, bool valido) => Assert.Equal(valido, LibroIvaArcaValidator.CuitValido(cuit));

    [Fact]
    public void ConsumidorGenericoSoloSeAdmiteEnVentasBC()
    {
        var r = Libro(); var f = r.Filas[0]; f.Letra = "B"; f.CodigoArca = 6; f.Cuit = "99999999995";
        Assert.Empty(LibroIvaArcaValidator.Validar(r));
        r.Filtro.Compras = true;
        Assert.Contains(LibroIvaArcaValidator.Validar(r), x => x.Contains("Documento/CUIT inválido"));
    }

    [Theory]
    [InlineData("111111111", true)] [InlineData("304656383", true)] [InlineData("111111111", false)] [InlineData("11111111111", false)]
    public void ConsumidorFinalNoSeObservaPorElNumeroDeDocumento(string documento, bool consumidorFinal)
    {
        var r = Libro(); var f = r.Filas[0]; f.Letra = "B"; f.CodigoArca = 6; f.Cuit = documento; f.ConsumidorFinal = consumidorFinal;
        var e = LibroIvaArcaValidator.Validar(r);
        if (consumidorFinal) Assert.DoesNotContain(e, x => x.Contains("Documento/CUIT inválido"));
        // Como el legacy (UT_SIAP_CITI): en ventas B un documento que no es CUIT (DNI, 999999999) no bloquea; solo un CUIT de 11 dígitos inválido.
        else if (documento.Length == 11) Assert.Contains(e, x => x.Contains("Documento/CUIT inválido"));
        else Assert.DoesNotContain(e, x => x.Contains("Documento/CUIT inválido"));
        if (!consumidorFinal) return;
        // En el TXT va como sin identificar (99 / 0), no con el número cargado.
        var (cab, _) = LibroIvaService.ArmarTxt(r.Filas);
        Assert.Contains("99" + new string('0', 20), cab);
        Assert.DoesNotContain(documento, cab);
    }

    [Fact]
    public void SumaCentavosPorComprobanteAntesDeTotalizar()
    {
        Assert.Equal(2.02m, new[] { 1.005m, 1.005m }.Sum(LibroIvaService.MonedaLibro));
        Assert.Equal(-1.01m, LibroIvaService.MonedaLibro(-1.005m));
        Assert.Equal(0, LibroIvaService.ExentoCompras(2204.92m, 2204.92m, 111160.208m, 113365.128m));
    }
}

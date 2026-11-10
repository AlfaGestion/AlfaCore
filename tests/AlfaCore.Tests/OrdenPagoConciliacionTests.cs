using System.Text.Json;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

// Pago a proveedores desde un egreso del extracto: reglas puras (sugerencia de comprobantes, contrato de la solicitud y de la clasificación).
public sealed class OrdenPagoConciliacionTests
{
    private static OrdenPagoDocumento Doc(int dia, string numero, decimal saldo, string motivo = "") => new()
    {
        Tc = "FCC", Sucursal = "0001", Numero = numero.PadLeft(8, '0'), Letra = "A", Fecha = new DateTime(2026, 10, dia), Saldo = saldo, Motivo = motivo
    };

    [Fact]
    public void SugiereLaFacturaQueCoincideExactoAunqueNoSeaLaMasAntigua()
    {
        var docs = new[] { Doc(1, "1", 500), Doc(2, "2", 1200), Doc(3, "3", 700) };
        var s = OrdenPagoService.SugerirAplicacion(docs, 700);
        var unica = Assert.Single(s);
        Assert.Equal("00000003", unica.Clave.Numero);
        Assert.Equal(700, unica.Importe);
    }

    [Fact]
    public void SugiereLaCombinacionExactaConMenosComprobantes()
    {
        var docs = new[] { Doc(1, "1", 300), Doc(2, "2", 400), Doc(3, "3", 700), Doc(4, "4", 1000) };
        var s = OrdenPagoService.SugerirAplicacion(docs, 1000);
        Assert.Equal("00000004", Assert.Single(s).Clave.Numero);
        var dos = OrdenPagoService.SugerirAplicacion(docs, 1100);
        Assert.Equal(["00000002", "00000003"], dos.Select(x => x.Clave.Numero).ToArray());
        Assert.Equal(1100, dos.Sum(x => x.Importe));
    }

    [Fact]
    public void SinCombinacionExactaCancelaLasMasAntiguasYDejaParcialLaUltima()
    {
        var docs = new[] { Doc(1, "1", 500), Doc(2, "2", 500), Doc(3, "3", 500) };
        var s = OrdenPagoService.SugerirAplicacion(docs, 750.50m);
        Assert.Equal(2, s.Count);
        Assert.Equal(500, s[0].Importe);
        Assert.Equal(250.50m, s[1].Importe);
    }

    [Fact]
    public void SiNoAlcanzanDevuelveTodosYIgnoraLosBloqueados()
    {
        var docs = new[] { Doc(1, "1", 100), Doc(2, "2", 100, "Comprobante bloqueado para su pago."), Doc(3, "3", 100) };
        var s = OrdenPagoService.SugerirAplicacion(docs, 1000);
        Assert.Equal(["00000001", "00000003"], s.Select(x => x.Clave.Numero).ToArray());
        Assert.Empty(OrdenPagoService.SugerirAplicacion(docs, 0));
        Assert.Empty(OrdenPagoService.SugerirAplicacion([], 100));
    }

    [Fact]
    public void LaSolicitudSinMovimientoConservaSuHashYConMovimientoLoIncluye()
    {
        var s = new OrdenPagoSolicitud { Operacion = Guid.Empty, Proveedor = "1" };
        Assert.DoesNotContain("MovimientoId", JsonSerializer.Serialize(s));
        s.MovimientoId = 77;
        Assert.Contains("\"MovimientoId\":77", JsonSerializer.Serialize(s));
    }

    [Fact]
    public void ElPagoAProveedorEsUnaClasificacionDelExtractoYSeFiltraPorSuNombre()
    {
        Assert.True(Enum.IsDefined(MovementCategory.PagoProveedor));
        Assert.Contains("PagoProveedor", Enum.GetNames<MovementCategory>());
        // Los valores existentes no cambian: están guardados como texto en BK_MovimientoClasificado, pero el número también es contrato.
        Assert.Equal(9, (int)MovementCategory.IngresoFinanciero);
        Assert.Equal(10, (int)MovementCategory.PagoProveedor);
    }
}

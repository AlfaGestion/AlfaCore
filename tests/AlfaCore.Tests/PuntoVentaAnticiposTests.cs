using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class PuntoVentaAnticiposTests
{
    [Fact]
    public void RegistroDeAnticipoPermaneceDeshabilitadoHastaValidacionLegacy()
    {
        var servicio = new PuntoVentaAnticipoRegistroService();

        Assert.False(servicio.RegistroHabilitado);
    }

    [Fact]
    public async Task RegistroDeshabilitadoNoEscribeAunqueLaSolicitudSeaValida()
    {
        var servicio = new PuntoVentaAnticipoRegistroService();
        var solicitud = new PuntoVentaAnticipoRegistroSolicitud(
            Guid.NewGuid(), "000127331022X", "112010001", 15000m,
            [new CobranzaPago("111010001", 15000m)]);

        var error = await Assert.ThrowsAsync<AppValidationException>(() => servicio.RegistrarAsync(solicitud));

        Assert.Contains(error.Validation.Issues, x => x.FieldKey == "POS_ANTICIPO_REGISTRO_NO_HABILITADO");
    }

    [Fact]
    public void EstadoEntregaDistinguePendienteParcialYCompleta()
    {
        static PuntoVentaNpItemDto Linea(decimal cantidad, decimal entregada) => new()
        {
            Cantidad = cantidad,
            CantidadEntregada = entregada
        };

        Assert.Equal("PENDIENTE", new PuntoVentaNpAnticiposDto { Items = [Linea(2m, 0m)] }.EstadoEntrega);
        Assert.Equal("PARCIAL", new PuntoVentaNpAnticiposDto { Items = [Linea(2m, 1m)] }.EstadoEntrega);
        Assert.Equal("ENTREGADO", new PuntoVentaNpAnticiposDto { Items = [Linea(2m, 2m)] }.EstadoEntrega);
    }

    [Fact]
    public void EntregaAnuladaNoSePresentaComoPendiente()
    {
        var np = new PuntoVentaNpAnticiposDto
        {
            EstadoComprobante = "ANULADA",
            Items = [new PuntoVentaNpItemDto { Cantidad = 1m, CantidadEntregada = 1m }]
        };

        Assert.Equal("ANULADA", np.EstadoEntrega);
    }

    [Fact]
    public void NotaSinAnticiposTieneTotalCeroVerificado()
    {
        var total = PuntoVentaAnticiposCalculador.Totalizar([]);

        Assert.True(total.Verificado);
        Assert.Equal(0m, total.Total);
    }

    [Fact]
    public void UnaCbctSinAplicacionesConservaTodoElDisponible()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, false, []);

        Assert.True(calculo.Verificado);
        Assert.Equal(0m, calculo.Aplicado);
        Assert.Equal(20000m, calculo.Disponible);
    }

    [Fact]
    public void VariasCobranzasSeAcumulanSinUsarObservaciones()
    {
        var cobranzas = new[]
        {
            Cobranza(20000m, 0m),
            Cobranza(10000m, 0m)
        };

        var total = PuntoVentaAnticiposCalculador.Totalizar(cobranzas);

        Assert.True(total.Verificado);
        Assert.Equal(30000m, total.Total);
    }

    [Fact]
    public void CobranzaAnuladaNoAportaAnticipo()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, true, []);

        Assert.True(calculo.Verificado);
        Assert.Equal(0m, calculo.Disponible);
        Assert.Equal(PuntoVentaAnticiposEstados.Anulado, calculo.Estado);
    }

    [Fact]
    public void AplicacionParcialDejaDisponibleLaDiferencia()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, false, [Aplicacion(5000m)]);

        Assert.True(calculo.Verificado);
        Assert.Equal(5000m, calculo.Aplicado);
        Assert.Equal(15000m, calculo.Disponible);
    }

    [Fact]
    public void AplicacionCompletaDejaDisponibleCero()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, false, [Aplicacion(20000m)]);

        Assert.True(calculo.Verificado);
        Assert.Equal(0m, calculo.Disponible);
    }

    [Fact]
    public void ImporteAplicadoMayorAlOriginalEsNoVerificable()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, false, [Aplicacion(20000m), Aplicacion(1m)]);

        Assert.False(calculo.Verificado);
        Assert.Null(calculo.Disponible);
        Assert.Equal(PuntoVentaAnticiposEstados.NoVerificado, calculo.Estado);
    }

    [Fact]
    public void AplicacionSinIdentidadCompletaEsNoVerificable()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(20000m, false,
            [new PuntoVentaNpAplicacionDto { TcCobranza = "CBCT", IdCobranza = "", TcDestino = "FC", IdDestino = "000100000001A", Importe = 1m }]);

        Assert.False(calculo.Verificado);
        Assert.Null(calculo.Aplicado);
    }

    [Fact]
    public void ElTotalNoVerificadoNoSeConvierteEnSaldoCobrable()
    {
        var total = PuntoVentaAnticiposCalculador.Totalizar([
            Cobranza(20000m, 0m),
            new PuntoVentaNpCobranzaDto { ImporteDisponibleVerificado = false, ImporteDisponible = null }
        ]);

        Assert.False(total.Verificado);
        Assert.Null(total.Total);
        Assert.Equal(PuntoVentaAnticiposEstados.NoVerificado, total.Estado);
    }

    [Fact]
    public void CasoDeModificacionDeNpConservaElAcumuladoDeCobranzas()
    {
        var cobranzas = new[] { Cobranza(20000m, 0m), Cobranza(10000m, 0m) };

        var total = PuntoVentaAnticiposCalculador.Totalizar(cobranzas);

        Assert.True(total.Verificado);
        Assert.Equal(30000m, total.Total);
        Assert.Equal(35000m, 65000m - total.Total!.Value);
    }

    [Fact]
    public void RelacionPorPrVentaEsVerificada()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion("000127331022X", "", "000127331022X");

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.PrVenta, relacion.Tipo);
        Assert.True(relacion.Verificada);
    }

    [Fact]
    public void LeeResumenLegacyDeLaNotaDePedido()
    {
        var ok = PuntoVentaAnticiposCalculador.TryLeerResumenLegacy(
            "SENA $2000.00 // SALDO $3000.00", out var sena, out var saldo);

        Assert.True(ok);
        Assert.Equal(2000m, sena);
        Assert.Equal(3000m, saldo);
    }

    [Fact]
    public void NoTomaTextoLibreComoResumenLegacy()
    {
        Assert.False(PuntoVentaAnticiposCalculador.TryLeerResumenLegacy(
            "Cob.NP 000600000005X", out _, out _));
    }

    [Fact]
    public void RelacionPorObservacionesLegacyEsVerificada()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion("", "Cob.NP 000127331022X", "000127331022X");

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.ObservacionesLegacy, relacion.Tipo);
        Assert.True(relacion.Verificada);
    }

    [Fact]
    public void RelacionPorAmbosMecanismosNoDuplicaElAnticipo()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion(
            "000127331022X", "Cob.NP 000127331022X", "000127331022X");
        var total = PuntoVentaAnticiposCalculador.Totalizar([Cobranza(15000m, 0m, relacion.Tipo, relacion.Verificada)]);

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.PrVentaYObservaciones, relacion.Tipo);
        Assert.Equal(15000m, total.Total);
    }

    [Fact]
    public void RelacionLegacyYPrVentaDeDosCobranzasAcumulaUnaVezCadaUna()
    {
        var cobranzas = new[]
        {
            Cobranza(15000m, 0m, PuntoVentaAnticiposTiposRelacion.ObservacionesLegacy),
            Cobranza(10000m, 0m, PuntoVentaAnticiposTiposRelacion.PrVenta)
        };

        var total = PuntoVentaAnticiposCalculador.Totalizar(cobranzas);

        Assert.Equal(25000m, total.Total);
    }

    [Fact]
    public void ObservacionDeOtraNpNoSeConsideraRelacionada()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion("", "Cob.NP 000127331021X", "000127331022X");

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.CandidataNoVerificada, relacion.Tipo);
        Assert.False(relacion.Verificada);
    }

    [Fact]
    public void NpConNumeracionSimilarNoCoincideParcialmente()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion("", "Cob.NP 000127331022X9", "000127331022X");

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.CandidataNoVerificada, relacion.Tipo);
    }

    [Fact]
    public void RelacionLegacyAnuladaNoEsAnticipoVigente()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(15000m, true, []);
        var total = PuntoVentaAnticiposCalculador.Totalizar([new PuntoVentaNpCobranzaDto
        {
            ImporteOriginal = 15000m,
            Anulada = true,
            ImporteDisponible = 0m,
            ImporteDisponibleVerificado = true,
            RelacionVerificada = true,
            TipoRelacion = PuntoVentaAnticiposTiposRelacion.ObservacionesLegacy
        }]);

        Assert.Equal(PuntoVentaAnticiposEstados.Anulado, calculo.Estado);
        Assert.Equal(0m, total.Total);
    }

    [Fact]
    public void RelacionContradictoriaNoSeSuma()
    {
        var relacion = PuntoVentaAnticiposCalculador.ClasificarRelacion(
            "000127331022X", "Cob.NP 000127331021X", "000127331022X");
        var total = PuntoVentaAnticiposCalculador.Totalizar([Cobranza(15000m, 0m, relacion.Tipo, relacion.Verificada)]);

        Assert.Equal(PuntoVentaAnticiposTiposRelacion.Conflicto, relacion.Tipo);
        Assert.Null(total.Total);
        Assert.False(total.Verificado);
    }

    [Fact]
    public void AplicacionParcialLegacyMantieneDisponibleVerificado()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(15000m, false, [Aplicacion(5000m)]);

        Assert.Equal(10000m, calculo.Disponible);
        Assert.True(calculo.Verificado);
    }

    [Fact]
    public void AplicacionCompletaLegacyDejaDisponibleCero()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(15000m, false, [Aplicacion(15000m)]);

        Assert.Equal(0m, calculo.Disponible);
    }

    [Fact]
    public void AplicacionInconsistenteNoPermiteSaldoDisponible()
    {
        var calculo = PuntoVentaAnticiposCalculador.Calcular(15000m, false, [Aplicacion(15001m)]);

        Assert.False(calculo.Verificado);
        Assert.Null(calculo.Disponible);
    }

    private static PuntoVentaNpCobranzaDto Cobranza(
        decimal original,
        decimal aplicado,
        string tipoRelacion = PuntoVentaAnticiposTiposRelacion.PrVenta,
        bool relacionVerificada = true)
        => new()
        {
            ImporteOriginal = original,
            ImporteAplicado = aplicado,
            ImporteDisponible = original - aplicado,
            ImporteDisponibleVerificado = true,
            RelacionVerificada = relacionVerificada,
            TipoRelacion = tipoRelacion,
            EstadoDisponible = PuntoVentaAnticiposEstados.Verificado
        };

    private static PuntoVentaNpAplicacionDto Aplicacion(decimal importe)
        => new()
        {
            TcCobranza = "CBCT",
            IdCobranza = "000100000001X",
            TcDestino = "FC",
            IdDestino = "000100000002A",
            Importe = importe
        };
}

using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Contador de mensajes frente a los cobros de Meta (2026-10-14): desde el 1/10/2026 Meta cobra las
/// respuestas pasadas las 1.000 por mes y por número, y sin método de pago deja de entregarlas.
/// </summary>
public sealed class WhatsAppUsoMetaTests
{
    [Theory]
    [InlineData(0, 0, false, false, 1000)]
    [InlineData(799, 80, false, false, 201)]
    [InlineData(800, 80, true, false, 200)]
    [InlineData(1000, 100, true, false, 0)]
    [InlineData(1500, 100, true, true, 0)]
    public void Contador_CalculaUsoYLimite(int servicio, int porcentaje, bool cerca, bool supero, int restantes)
    {
        var uso = new WhatsAppUsoMetaDto { MensajesServicio = servicio };

        Assert.Equal(porcentaje, uso.PorcentajeServicioUsado);
        Assert.Equal(cerca, uso.CercaDelLimite);
        Assert.Equal(supero, uso.SuperoGratis);
        Assert.Equal(restantes, uso.ServicioGratisRestantes);
    }

    [Fact]
    public void LinkDePago_VaALaCuentaDeWhatsAppDelCliente()
    {
        Assert.Equal("https://business.facebook.com/wa/manage/home/?waba_id=888902717349521&business_id=123",
            WhatsAppUsoMetaService.BuildUrlCargarPago("888902717349521", "123"));
        Assert.Equal("https://business.facebook.com/wa/manage/home/?waba_id=888902717349521",
            WhatsAppUsoMetaService.BuildUrlCargarPago(" 888902717349521 ", ""));
    }

    [Fact]
    public void LinkDePago_SinCuentaConocida_VaAlCentroDePagosDeMeta()
        => Assert.Equal("https://business.facebook.com/billing_hub/payment_settings/",
            WhatsAppUsoMetaService.BuildUrlCargarPago(null, null));
}

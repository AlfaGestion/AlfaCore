using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Variables "clásicas" de plantillas (fecha/hora, contacto, cliente) que se resuelven solo con la
/// conversación y la fecha del negocio, y las de contexto de Cierre de caja.
/// </summary>
public sealed class WhatsAppTemplateVariablesBasicasTests
{
    private static readonly DateTime Ahora = new(2026, 10, 8, 14, 5, 0);

    private static ConversacionDetalleDto Conversacion() => new()
    {
        TelefonoWhatsApp = "5491153859509",
        ContactoEmail = "evelyn@ejemplo.com",
        ClienteNombre = "Alfa Net SRL",
        ClienteCodigo = "112010001"
    };

    [Theory]
    [InlineData(WhatsAppTemplateVariableCatalog.FechaHoy, "08/10/2026")]
    [InlineData(WhatsAppTemplateVariableCatalog.FechaAyer, "07/10/2026")]
    [InlineData(WhatsAppTemplateVariableCatalog.HoraActual, "14:05")]
    [InlineData(WhatsAppTemplateVariableCatalog.MesActual, "octubre de 2026")]
    [InlineData(WhatsAppTemplateVariableCatalog.AnioActual, "2026")]
    [InlineData(WhatsAppTemplateVariableCatalog.ContactPhone, "5491153859509")]
    [InlineData(WhatsAppTemplateVariableCatalog.ContactEmail, "evelyn@ejemplo.com")]
    [InlineData(WhatsAppTemplateVariableCatalog.ClienteNombre, "Alfa Net SRL")]
    [InlineData(WhatsAppTemplateVariableCatalog.ClienteCodigo, "112010001")]
    public void VariablesBasicas_SeResuelvenDesdeConversacionYFecha(string key, string esperado)
    {
        var (resuelta, valor, _) = ConversacionesService.ResolveSimpleTemplateAutoValue(key, Conversacion(), Ahora);
        Assert.True(resuelta);
        Assert.Equal(esperado, valor);
    }

    [Theory]
    [InlineData(WhatsAppTemplateVariableCatalog.ClienteNombre)]
    [InlineData(WhatsAppTemplateVariableCatalog.ClienteCodigo)]
    [InlineData(WhatsAppTemplateVariableCatalog.ContactEmail)]
    public void SinDatoEnLaConversacion_NoSeInventaUnValor(string key)
    {
        var (resuelta, valor, observacion) = ConversacionesService.ResolveSimpleTemplateAutoValue(key, new ConversacionDetalleDto(), Ahora);
        Assert.False(resuelta);
        Assert.Equal(string.Empty, valor);
        Assert.False(string.IsNullOrWhiteSpace(observacion));
    }

    [Fact]
    public void VariablesNuevas_EstanEnElCatalogoConSuDisponibilidad()
    {
        foreach (var key in new[]
                 {
                     WhatsAppTemplateVariableCatalog.FechaHoy, WhatsAppTemplateVariableCatalog.FechaAyer,
                     WhatsAppTemplateVariableCatalog.HoraActual, WhatsAppTemplateVariableCatalog.MesActual,
                     WhatsAppTemplateVariableCatalog.AnioActual, WhatsAppTemplateVariableCatalog.ContactPhone,
                     WhatsAppTemplateVariableCatalog.ContactEmail, WhatsAppTemplateVariableCatalog.ClienteNombre,
                     WhatsAppTemplateVariableCatalog.ClienteCodigo
                 })
            Assert.True(WhatsAppTemplateVariableCatalog.Find(key)?.CanResolveAutomaticallyInManualSend);

        // Las de Cierre de caja se pueden insertar, pero fuera de esa pantalla piden el valor a mano.
        Assert.False(string.IsNullOrWhiteSpace(WhatsAppTemplateVariableCatalog.Find(WhatsAppTemplateVariableCatalog.CierreFecha)?.RequiredContext));
        var (resuelta, _, observacion) = ConversacionesService.ResolveSimpleTemplateAutoValue(WhatsAppTemplateVariableCatalog.CierreFecha, Conversacion(), Ahora);
        Assert.False(resuelta);
        Assert.False(string.IsNullOrWhiteSpace(observacion));
    }
}

using AlfaCore.Components.Layout;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Burbuja de mensajes de la barra superior (2026-10-05): vista previa estilo Odoo con las
/// conversaciones sin leer del usuario y las que esperan respuesta del equipo.
/// </summary>
public sealed class TopbarMensajesMenuTests
{
    private static ConversacionInboxItemDto Item(long id, int noLeidos, string direccion, int minutosAtras, bool archivada = false)
        => new()
        {
            IdConversacion = id,
            MensajesNoLeidosUsuario = noLeidos,
            DireccionUltimoMensaje = direccion,
            FechaHoraUltimoMensaje = DateTime.Now.AddMinutes(-minutosAtras),
            FechaHoraUltimoMensajeCliente = DateTime.Now.AddMinutes(-minutosAtras),
            Archivada = archivada
        };

    [Fact]
    public void Clasificar_SeparaSinLeerYSinResponder()
    {
        var items = new[]
        {
            Item(1, 2, "ENTRANTE", 5),
            Item(2, 0, "ENTRANTE", 60),
            Item(3, 1, "SALIENTE", 1),
            Item(4, 0, "SALIENTE", 2),
            Item(5, 3, "ENTRANTE", 1, archivada: true)
        };

        var (sinLeer, sinResponder) = GlobalConversationNotificationsSource.Clasificar(items);

        // Sin leer: más recientes primero; las archivadas no se muestran.
        Assert.Equal([3L, 1L], sinLeer.Select(x => x.IdConversacion));
        // Sin responder: el que más espera primero.
        Assert.Equal([2L, 1L], sinResponder.Select(x => x.IdConversacion));
    }

    [Theory]
    [InlineData("Evelyn Pérez", "EP")]
    [InlineData("evelyn", "E")]
    [InlineData("+54 9 11 5385", "")]
    [InlineData("   ", "")]
    public void Iniciales_UsaLasDosPrimerasPalabrasConLetra(string nombre, string esperado)
        => Assert.Equal(esperado, TopbarMensajesMenu.Iniciales(nombre));

    [Theory]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    public void BadgeText_LimitaANueveMas(int cantidad, string esperado)
        => Assert.Equal(esperado, TopbarMensajesMenu.BadgeText(cantidad));

    [Theory]
    [InlineData("WHATSAPP", "bi-whatsapp")]
    [InlineData("instagram", "bi-instagram")]
    [InlineData("FACEBOOK", "bi-messenger")]
    [InlineData("MERCADOLIBRE", "bi-bag")]
    [InlineData(null, "bi-whatsapp")]
    public void CanalIcono_PorCanal(string? canal, string esperado)
        => Assert.Equal(esperado, TopbarMensajesMenu.CanalIcono(canal));

    [Fact]
    public void Nombre_PrefiereContactoLuegoNombreVisible()
    {
        Assert.Equal("Ana", TopbarMensajesMenu.Nombre(new ConversacionInboxItemDto { ContactoNombre = "Ana", NombreVisible = "Ani" }));
        Assert.Equal("Ani", TopbarMensajesMenu.Nombre(new ConversacionInboxItemDto { NombreVisible = "Ani", TelefonoWhatsApp = "549" }));
        Assert.Equal("549", TopbarMensajesMenu.Nombre(new ConversacionInboxItemDto { TelefonoWhatsApp = "549" }));
        Assert.Equal("Sin nombre", TopbarMensajesMenu.Nombre(new ConversacionInboxItemDto()));
    }
}

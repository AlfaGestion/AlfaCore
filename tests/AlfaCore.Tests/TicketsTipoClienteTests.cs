using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Tipo de cliente en Tickets: niveles por clasificación (TA_CLASIFICACIONES) y abono activo como mínimo.</summary>
public sealed class TicketsTipoClienteTests
{
    private static readonly HorarioLaboral Horario = HorarioLaboral.Desde(new ConversacionAutomatizacionesConfigDto());
    private static readonly DateTime Lunes9 = new(2026, 11, 23, 9, 0, 0);

    private static TicketsAtencionConfigDto Config(int abono = 1) => new()
    {
        Plazos =
        [
            new() { Prioridad = 0, HorasRespuesta = 16, HorasResolucion = 80 },
            new() { Prioridad = 1, HorasRespuesta = 8, HorasResolucion = 40 },
            new() { Prioridad = 2, HorasRespuesta = 4, HorasResolucion = 24 },
            new() { Prioridad = 3, HorasRespuesta = 2, HorasResolucion = 8 }
        ],
        AbonoActivoNiveles = abono,
        TiposCliente =
        [
            new() { Codigo = "1", Descripcion = "A-ABONO FULL", ColorHex = "#4A99ED", Niveles = 2 },
            new() { Codigo = "3", Descripcion = "C-ABONADO BASICO", Niveles = 1 },
            new() { Codigo = "4", Descripcion = "D-NO ABONADO", Niveles = 0 }
        ]
    };

    [Fact]
    public void Clasificacion_DaSusNivelesNombreYColor()
    {
        var (niveles, nombre, color) = TicketsAtencionCalculo.TipoCliente(Config(), " 1 ", abonoActivo: false);
        Assert.Equal(2, niveles);
        Assert.Equal("A-ABONO FULL", nombre);
        Assert.Equal("#4A99ED", color);
    }

    [Fact]
    public void AbonoActivo_EsElMinimo_AunqueLaClasificacionDigaOtraCosa()
    {
        var (niveles, nombre, _) = TicketsAtencionCalculo.TipoCliente(Config(), "4", abonoActivo: true);
        Assert.Equal(1, niveles);
        Assert.Equal("Con abono", nombre);

        // Con más niveles que el abono, manda la clasificación.
        Assert.Equal(2, TicketsAtencionCalculo.TipoCliente(Config(), "1", abonoActivo: true).Niveles);
    }

    [Fact]
    public void SinClasificacion_ConYSinAbono()
    {
        Assert.Equal((1, "Con abono", ""), TicketsAtencionCalculo.TipoCliente(Config(), "", abonoActivo: true));
        Assert.Equal((0, "", ""), TicketsAtencionCalculo.TipoCliente(Config(), null, abonoActivo: false));
        Assert.Equal(0, TicketsAtencionCalculo.TipoCliente(Config(abono: 0), "", abonoActivo: true).Niveles);
    }

    [Fact]
    public void ClasificacionNormal_MuestraSuNombreSinSubir()
    {
        var (niveles, nombre, _) = TicketsAtencionCalculo.TipoCliente(Config(), "4", abonoActivo: false);
        Assert.Equal(0, niveles);
        Assert.Equal("D-NO ABONADO", nombre);
    }

    [Fact]
    public void Niveles_SubenPrioridadPlazoYPuntaje()
    {
        var config = Config();
        TicketGridItemDto Calcular(int niveles, string nombre)
        {
            var t = new TicketGridItemDto { IdTicket = 1, FechaHoraAlta = Lunes9, Prioridad = 1 };
            TicketsAtencionCalculo.Aplicar(t, new TicketAtencionDatos
            {
                FechaAlta = Lunes9, Prioridad = 1, NivelesCliente = niveles, TipoClienteNombre = nombre
            }, config, Horario, Lunes9.AddHours(1.6));
            return t;
        }

        var normal = Calcular(0, "D-NO ABONADO");
        var basico = Calcular(1, "C-ABONADO BASICO");
        var full = Calcular(2, "A-ABONO FULL");

        Assert.False(normal.ClientePrioritario);
        Assert.Equal(1, normal.PrioridadEfectiva);
        Assert.Equal(2, basico.PrioridadEfectiva);
        Assert.Equal(3, full.PrioridadEfectiva);
        Assert.True(full.ClientePrioritario);
        Assert.Equal(2, full.NivelesCliente);
        Assert.Equal("A-ABONO FULL", full.TipoClienteNombre);
        Assert.Contains("Cliente A-ABONO FULL", full.Motivos);
        Assert.True(full.Puntaje > basico.Puntaje && basico.Puntaje > normal.Puntaje);
        // Prioridad 3 = plazo de respuesta de 2 h: a la hora y 36 min (80 %) ya está por vencer.
        Assert.Equal(TicketPlazoEstados.PorVencer, full.PlazoEstado);
        Assert.Equal(TicketPlazoEstados.EnTermino, normal.PlazoEstado);
    }

    [Fact]
    public void SinNiveles_UsaElPrioritarioDeAntes()
    {
        var t = new TicketGridItemDto { IdTicket = 1, FechaHoraAlta = Lunes9, Prioridad = 1 };
        TicketsAtencionCalculo.Aplicar(t, new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 1, ClientePrioritario = true },
            Config(), Horario, Lunes9.AddHours(1));
        Assert.Equal(2, t.PrioridadEfectiva);
        Assert.Equal(1, t.NivelesCliente);
    }
}

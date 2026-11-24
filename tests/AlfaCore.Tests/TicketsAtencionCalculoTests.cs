using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TicketsAtencionCalculoTests
{
    // Lunes a viernes de 9 a 18 (valores por defecto del horario de Conversaciones).
    private static readonly HorarioLaboral Horario = HorarioLaboral.Desde(new ConversacionAutomatizacionesConfigDto());
    private static readonly DateTime Lunes9 = new(2026, 11, 23, 9, 0, 0);

    private static TicketsAtencionConfigDto Config() => new()
    {
        Plazos =
        [
            new() { Prioridad = 0, HorasRespuesta = 16, HorasResolucion = 80 },
            new() { Prioridad = 1, HorasRespuesta = 8, HorasResolucion = 40 },
            new() { Prioridad = 2, HorasRespuesta = 4, HorasResolucion = 24 },
            new() { Prioridad = 3, HorasRespuesta = 2, HorasResolucion = 8 }
        ]
    };

    private static TicketGridItemDto Calcular(TicketAtencionDatos datos, DateTime ahora, TicketsAtencionConfigDto? config = null)
    {
        var ticket = new TicketGridItemDto { IdTicket = 1, CodigoEstado = datos.CodigoEstado, FechaHoraAlta = datos.FechaAlta, Prioridad = datos.Prioridad };
        TicketsAtencionCalculo.Aplicar(ticket, datos, config ?? Config(), Horario, ahora);
        return ticket;
    }

    [Fact]
    public void HorasHabiles_SaltanFinDeSemanaYFueraDeHorario()
    {
        var viernes17 = new DateTime(2026, 11, 20, 17, 0, 0);
        var lunes10 = new DateTime(2026, 11, 23, 10, 0, 0);
        Assert.Equal(2, Horario.HorasHabilesEntre(viernes17, lunes10), 3);
        Assert.Equal(0, Horario.HorasHabilesEntre(new DateTime(2026, 11, 21, 10, 0, 0), new DateTime(2026, 11, 22, 18, 0, 0)), 3);
        Assert.Equal(9, Horario.HorasHabilesEntre(Lunes9, Lunes9.AddDays(1)), 3);
    }

    [Fact]
    public void SumarHorasHabiles_PasaAlDiaHabilSiguiente()
    {
        var viernes17 = new DateTime(2026, 11, 20, 17, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 23, 10, 0, 0), Horario.SumarHorasHabiles(viernes17, 2));
    }

    [Fact]
    public void FranjaNocturna_CuentaNocheYMadrugada()
    {
        var noche = new HorarioLaboral([DayOfWeek.Monday, DayOfWeek.Tuesday], TimeSpan.FromHours(20), TimeSpan.FromHours(6));
        Assert.True(noche.EsHabil(new DateTime(2026, 11, 23, 22, 0, 0)));
        Assert.True(noche.EsHabil(new DateTime(2026, 11, 24, 5, 0, 0)));
        Assert.False(noche.EsHabil(new DateTime(2026, 11, 23, 12, 0, 0)));
    }

    [Fact]
    public void SinRespuestaNuestra_EsperaANosotrosDesdeElAlta()
    {
        var t = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 1, TieneConversacion = true }, Lunes9.AddHours(3));
        Assert.Equal(TicketEsperandoA.Nosotros, t.EsperandoA);
        Assert.Equal(3, t.HorasHabilesEsperando, 3);
        Assert.Equal(TicketPlazoTipos.Respuesta, t.PlazoTipo);
        Assert.Equal(TicketPlazoEstados.EnTermino, t.PlazoEstado);
    }

    [Fact]
    public void RespondimosYElClienteNoEscribio_EsperaAlCliente()
    {
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            TieneConversacion = true,
            UltimaRespuestaNuestra = Lunes9.AddHours(1),
            PrimeraRespuestaNuestra = Lunes9.AddHours(1)
        }, Lunes9.AddHours(5));
        Assert.Equal(TicketEsperandoA.Cliente, t.EsperandoA);
        Assert.Equal(TicketPlazoEstados.Frenado, t.PlazoEstado);
        Assert.Equal(0, t.Puntaje);
    }

    [Fact]
    public void ElClienteVolvioAEscribir_EsperaANosotrosDesdeSuMensaje()
    {
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            TieneConversacion = true,
            UltimaRespuestaNuestra = Lunes9.AddHours(1),
            PrimeraRespuestaNuestra = Lunes9.AddHours(1),
            UltimoMensajeCliente = Lunes9.AddHours(2),
            PrimerMensajeClienteSinResponder = Lunes9.AddHours(2)
        }, Lunes9.AddHours(4));
        Assert.Equal(TicketEsperandoA.Nosotros, t.EsperandoA);
        Assert.Equal(Lunes9.AddHours(2), t.EsperandoDesde);
    }

    [Fact]
    public void EnEspera_FrenaElPlazoYNoCuentaEseTiempo()
    {
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            CodigoEstado = TicketEstadoKeys.EnEspera,
            Prioridad = 3,
            PrimeraRespuestaNuestra = Lunes9.AddHours(1),
            PeriodosEspera = [(Lunes9.AddHours(1), null)]
        }, Lunes9.AddDays(3));
        Assert.Equal(TicketEsperandoA.Cliente, t.EsperandoA);
        // Resolución 8 h: solo corrió 1 h antes de pasar a espera.
        Assert.Equal(TicketPlazoEstados.Frenado, t.PlazoEstado);
        Assert.Equal(7, t.PlazoHorasRestantes, 3);
    }

    [Fact]
    public void ClienteEscribeEstandoEnEspera_VuelveANosotros()
    {
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            CodigoEstado = TicketEstadoKeys.EnEspera,
            TieneConversacion = true,
            PeriodosEspera = [(Lunes9.AddHours(1), null)],
            UltimoMensajeCliente = Lunes9.AddHours(3),
            PrimerMensajeClienteSinResponder = Lunes9.AddHours(3)
        }, Lunes9.AddHours(4));
        Assert.Equal(TicketEsperandoA.Nosotros, t.EsperandoA);
        Assert.Contains("El cliente volvió a escribir", t.Motivos);
    }

    [Fact]
    public void ClientePrioritario_SubeUnNivelYVence()
    {
        // 2 estrellas + prioritario = 3 → respuesta en 2 h hábiles; pasaron 3.
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            Prioridad = 2,
            ClientePrioritario = true,
            EstadoAbono = ConversacionAbonoEstados.Abonado
        }, Lunes9.AddHours(3));
        Assert.Equal(3, t.PrioridadEfectiva);
        Assert.Equal(TicketPlazoEstados.Vencido, t.PlazoEstado);
        Assert.Contains("Cliente con abono", t.Motivos);
        Assert.Contains("Urgente", t.Motivos);
        Assert.StartsWith("Plazo de respuesta vencido", t.Motivos[0]);
    }

    [Fact]
    public void PorVencer_DesdeElPorcentajeConfigurado()
    {
        // 1 estrella: respuesta 8 h; a las 6 h (75 %) ya está por vencer.
        var t = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 1 }, Lunes9.AddHours(6));
        Assert.Equal(TicketPlazoEstados.PorVencer, t.PlazoEstado);
        Assert.Equal(Lunes9.AddHours(8), t.PlazoVence);
    }

    [Fact]
    public void Seguimiento_SaleDeLaColaYVuelveSiElClienteEscribe()
    {
        var programado = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            TieneConversacion = true,
            FechaSeguimiento = Lunes9.AddDays(2),
            SeguimientoDesde = Lunes9.AddHours(1)
        }, Lunes9.AddHours(2));
        Assert.Equal(TicketEsperandoA.Seguimiento, programado.EsperandoA);

        var escribio = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            TieneConversacion = true,
            FechaSeguimiento = Lunes9.AddDays(2),
            SeguimientoDesde = Lunes9.AddHours(1),
            UltimoMensajeCliente = Lunes9.AddHours(2)
        }, Lunes9.AddHours(3));
        Assert.Equal(TicketEsperandoA.Nosotros, escribio.EsperandoA);
    }

    [Fact]
    public void SeguimientoVencido_VuelveArribaConMotivo()
    {
        var t = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            FechaSeguimiento = Lunes9.AddHours(2),
            SeguimientoDesde = Lunes9.AddHours(1)
        }, Lunes9.AddHours(4));
        Assert.Equal(TicketEsperandoA.Nosotros, t.EsperandoA);
        Assert.True(t.SeguimientoVencido);
        Assert.Contains("Seguimiento vencido", t.Motivos);
    }

    [Fact]
    public void Ordenar_PrimeroLoNuestroPorPuntajeDespuesLoQueEspera()
    {
        var cliente = Calcular(new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            Prioridad = 3,
            TieneConversacion = true,
            UltimaRespuestaNuestra = Lunes9.AddMinutes(30),
            PrimeraRespuestaNuestra = Lunes9.AddMinutes(30)
        }, Lunes9.AddHours(5));
        cliente.IdTicket = 1;
        var baja = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 0 }, Lunes9.AddHours(5));
        baja.IdTicket = 2;
        var urgente = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 3 }, Lunes9.AddHours(5));
        urgente.IdTicket = 3;

        var orden = TicketsAtencionCalculo.Ordenar([cliente, baja, urgente]).Select(t => t.IdTicket).ToList();
        Assert.Equal([3L, 2L, 1L], orden);
    }

    [Fact]
    public void VariosCasosDelCliente_SumaYSeMuestra()
    {
        var t = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, CasosAbiertosCliente = 4 }, Lunes9.AddHours(1));
        Assert.Contains("4 casos abiertos del cliente", t.Motivos);
    }

    [Theory]
    [InlineData(TicketBandejaKeys.Nosotros, true)]
    [InlineData(TicketBandejaKeys.Cliente, false)]
    [InlineData(TicketBandejaKeys.SinAsignar, true)]
    [InlineData(TicketBandejaKeys.Mios, false)]
    [InlineData(TicketBandejaKeys.Plazo, true)]
    public void Bandejas_TicketUrgenteVencidoSinAsignar(string bandeja, bool esperado)
    {
        var t = Calcular(new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 3 }, Lunes9.AddHours(5));
        Assert.Equal(esperado, TicketsAtencionCalculo.EnBandeja(t, bandeja, "T01"));
    }

    [Fact]
    public void Duracion_EnHorasODiasHabiles()
    {
        Assert.Equal("menos de 1 h", TicketsAtencionCalculo.Duracion(0.4, Horario));
        Assert.Equal("5 h", TicketsAtencionCalculo.Duracion(5.7, Horario));
        Assert.Equal("1 día hábil", TicketsAtencionCalculo.Duracion(9, Horario));
        Assert.Equal("3 días hábiles", TicketsAtencionCalculo.Duracion(28, Horario));
    }

    [Fact]
    public void Cerrado_ResueltoATiempoSinContarLaEspera()
    {
        // 3 estrellas: resolución 8 h. Abierto 10 h hábiles, de las cuales 4 esperando al cliente → 6 h: a tiempo.
        var d = new TicketAtencionDatos
        {
            FechaAlta = Lunes9,
            Prioridad = 3,
            PrimeraRespuestaNuestra = Lunes9.AddHours(1),
            PeriodosEspera = [(Lunes9.AddHours(2), Lunes9.AddHours(6))]
        };
        var (respuesta, resolucion, horas) = TicketsAtencionCalculo.EvaluarCerrado(d, Lunes9.AddDays(1).AddHours(1), Config(), Horario);
        Assert.True(respuesta);
        Assert.True(resolucion);
        Assert.Equal(6, horas, 3);
    }

    [Fact]
    public void Cerrado_SinRespuestaRegistrada_NoSeMideLaRespuesta()
    {
        var d = new TicketAtencionDatos { FechaAlta = Lunes9, Prioridad = 0 };
        var (respuesta, resolucion, _) = TicketsAtencionCalculo.EvaluarCerrado(d, Lunes9.AddDays(14), Config(), Horario);
        Assert.Null(respuesta);
        Assert.False(resolucion);
    }

    [Fact]
    public void TiempoPorEtapa_PromediaTramosTerminados()
    {
        var cambios = new List<(long, string, DateTime)>
        {
            (1, "NUEVO", Lunes9), (1, "EN_PROCESO", Lunes9.AddHours(2)), (1, "CERRADO", Lunes9.AddHours(5)),
            (2, "NUEVO", Lunes9), (2, "EN_PROCESO", Lunes9.AddHours(4))
        };
        var etapas = TicketsAtencionCalculo.TiempoPorEtapa(cambios, Horario).ToDictionary(x => x.Estado);
        Assert.Equal(3, etapas["NUEVO"].HorasPromedio, 3);
        Assert.Equal(2, etapas["NUEVO"].Tramos);
        Assert.Equal(3, etapas["EN_PROCESO"].HorasPromedio, 3);
        Assert.False(etapas.ContainsKey("CERRADO"));
    }

    [Theory]
    [InlineData(0.5, "0")]
    [InlineData(2, "1-3")]
    [InlineData(5, "3-7")]
    [InlineData(10, "7-30")]
    [InlineData(45, "30+")]
    public void TramoAntiguedad_PorDiasCorridos(double dias, string esperado)
        => Assert.Equal(esperado, TicketsAtencionCalculo.TramoAntiguedad(Lunes9, Lunes9.AddDays(dias)));
}

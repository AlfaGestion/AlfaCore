using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Campana de avisos (2026-10-05): reglas que convierten eventos del Calendario, reservas públicas y
/// novedades en avisos del usuario.
/// </summary>
public sealed class CentroAvisosTests
{
    private static readonly DateTime Ahora = new(2026, 10, 5, 15, 0, 0);
    private static readonly IReadOnlySet<string> SinLeidos = new HashSet<string>();

    private static AvisoEventoFuente Evento(long id, string tipo, DateTime inicio, string tecnico = "T1",
        int? minutosAntes = null, string usuarioAlta = "", DateTime? alta = null, bool todoElDia = false)
        => new()
        {
            IdEvento = id,
            Titulo = $"Evento {id}",
            Tipo = tipo,
            FechaInicio = inicio,
            FechaFin = inicio.AddHours(2),
            TodoElDia = todoElDia,
            IdTecnico = tecnico,
            MinutosAntes = minutosAntes,
            UsuarioAlta = usuarioAlta,
            FechaAlta = alta
        };

    private static IReadOnlyList<AvisoDto> Construir(IEnumerable<AvisoEventoFuente> eventos, IReadOnlySet<string>? leidos = null,
        IEnumerable<AvisoNovedadFuente>? novedades = null)
        => CentroAvisosService.ConstruirAvisos(eventos, novedades ?? [], ["T1"], "evelyn", leidos ?? SinLeidos, Ahora);

    [Fact]
    public void Guardia_AvisaElDiaAnteriorYElMismoDia()
    {
        var mañana = Construir([Evento(1, "GUARDIA", Ahora.Date.AddDays(1), todoElDia: true)]);
        var hoy = Construir([Evento(2, "GUARDIA", Ahora.Date.AddHours(16))]);
        var pasadoMañana = Construir([Evento(3, "GUARDIA", Ahora.Date.AddDays(2))]);

        Assert.Equal("cal:1:dia-antes", Assert.Single(mañana).Clave);
        Assert.Equal("Mañana tenés guardia", mañana[0].Titulo);
        Assert.Equal("cal:2:mismo-dia", Assert.Single(hoy).Clave);
        Assert.Empty(pasadoMañana);
    }

    [Fact]
    public void Capacitacion_UsaLaAnticipacionDelRecordatorio()
    {
        var enDosHoras = Ahora.AddHours(2);

        Assert.Empty(Construir([Evento(1, "CAPACITACION", enDosHoras, minutosAntes: 60)]));
        var aviso = Assert.Single(Construir([Evento(2, "CAPACITACION", enDosHoras, minutosAntes: 180)]));
        Assert.Equal(AvisoTipos.Capacitacion, aviso.Tipo);
        Assert.StartsWith("Próxima capacitación", aviso.Titulo);
        // Sin recordatorio: 24 h por defecto.
        Assert.Single(Construir([Evento(3, "REUNION", Ahora.AddHours(20))]));
        Assert.Empty(Construir([Evento(4, "REUNION", Ahora.AddHours(30))]));
    }

    [Fact]
    public void EventosDeOtroTecnico_NoSeAvisan()
        => Assert.Empty(Construir([Evento(1, "GUARDIA", Ahora.Date.AddDays(1), tecnico: "T2")]));

    [Fact]
    public void EventoAgendadoPorOtraPersona_AvisaTeAgendaron()
    {
        var avisos = Construir([Evento(1, "REUNION", Ahora.AddDays(5), usuarioAlta: "matias", alta: Ahora.AddDays(-1))]);

        var aviso = Assert.Single(avisos);
        Assert.Equal("cal:1:alta", aviso.Clave);
        Assert.Contains("por matias", aviso.Detalle);
        // Si lo cargó el mismo usuario, no se avisa.
        Assert.Empty(Construir([Evento(2, "REUNION", Ahora.AddDays(5), usuarioAlta: "EVELYN", alta: Ahora.AddDays(-1))]));
    }

    [Fact]
    public void ReservaPublica_SeAvisaAlTecnicoOATodosSiNoTieneTecnico()
    {
        var reserva = Evento(1, "CAPACITACION", Ahora.AddDays(3), tecnico: "");
        reserva.IdReserva = 45;
        reserva.ReservaCliente = "Juan";
        reserva.ReservaRazonSocial = "Ferretería Sur";
        reserva.ReservaTipoCapacitacion = "Facturación";
        reserva.ReservaFechaAlta = Ahora.AddHours(-3);

        var aviso = Assert.Single(Construir([reserva]));
        Assert.Equal("res:45", aviso.Clave);
        Assert.Equal("Nueva reserva: Facturación", aviso.Titulo);
        Assert.StartsWith("Juan (Ferretería Sur)", aviso.Detalle);

        reserva.IdTecnico = "T2";
        Assert.Empty(Construir([reserva]));
    }

    [Fact]
    public void Leidos_QuedanAlFinalYMarcados()
    {
        var avisos = Construir(
            [Evento(1, "GUARDIA", Ahora.Date.AddDays(1)), Evento(2, "REUNION", Ahora.AddHours(1))],
            new HashSet<string>(["cal:1:dia-antes"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["cal:2:aviso", "cal:1:dia-antes"], avisos.Select(a => a.Clave));
        Assert.False(avisos[0].Leido);
        Assert.True(avisos[1].Leido);
    }

    [Fact]
    public void Novedades_SeMuestranConSuContenido()
    {
        var aviso = Assert.Single(Construir([], novedades:
        [
            new AvisoNovedadFuente { IdNovedad = 7, Titulo = "Nueva versión", Bajada = "Mejoras en Conversaciones", Version = "4.2", FechaPublicacion = Ahora.AddDays(-1) }
        ]));

        Assert.Equal("nov:7", aviso.Clave);
        Assert.Equal("Mejoras en Conversaciones", aviso.Contenido);
        Assert.Contains("versión 4.2", aviso.Detalle);
        Assert.Empty(aviso.Ruta);
    }

    [Fact]
    public void EventosTerminados_NoGeneranAvisos()
        => Assert.Empty(Construir([Evento(1, "REUNION", Ahora.AddHours(-5))]));

    private static AvisoTicketFuente Ticket(long id, string tecnico = "T1", string usuarioAlta = "otro",
        DateTime? alta = null, DateTime? modificacion = null)
        => new()
        {
            IdTicket = id,
            Numero = (int)id + 1000,
            Titulo = $"Ticket {id}",
            IdTecnico = tecnico,
            EstadoNombre = "Abierto",
            UsuarioAlta = usuarioAlta,
            FechaAlta = alta ?? Ahora.AddHours(-1),
            FechaModificacion = modificacion
        };

    private static IReadOnlyList<AvisoDto> ConTickets(params AvisoTicketFuente[] tickets)
        => CentroAvisosService.ConstruirAvisos([], [], ["T1"], "evelyn", SinLeidos, Ahora, tickets);

    [Fact]
    public void TicketAsignado_AvisaAlTecnicoDelUsuario()
    {
        var aviso = Assert.Single(ConTickets(Ticket(5)));

        Assert.Equal("tick:5:T1", aviso.Clave);
        Assert.Equal(AvisoTipos.Ticket, aviso.Tipo);
        Assert.Equal("Ticket asignado: Ticket 5", aviso.Titulo);
        Assert.Equal("#1005 · Abierto", aviso.Detalle);
        Assert.Equal("/tickets?id=5", aviso.Ruta);
    }

    [Fact]
    public void TicketAsignado_IgnoraOtrosTecnicosViejosYPropiosSinTocar()
    {
        Assert.Empty(ConTickets(Ticket(1, tecnico: "T2")));
        Assert.Empty(ConTickets(Ticket(2, alta: Ahora.AddDays(-10))));
        Assert.Empty(ConTickets(Ticket(3, usuarioAlta: "EVELYN")));
        // Viejo pero con movimiento reciente, o propio pero después modificado: sí avisa.
        Assert.Single(ConTickets(Ticket(4, alta: Ahora.AddDays(-10), modificacion: Ahora.AddHours(-2))));
        Assert.Single(ConTickets(Ticket(6, usuarioAlta: "evelyn", modificacion: Ahora)));
    }

    private static AvisoCrmFuente Oportunidad(long id, string tecnico = "T1", string usuarioAlta = "otro",
        DateTime? alta = null, DateTime? modificacion = null)
        => new()
        {
            IdOportunidad = id,
            Titulo = $"Oportunidad {id}",
            Cliente = "Cliente SA",
            IdTecnico = tecnico,
            EtapaNombre = "Contactado",
            UsuarioAlta = usuarioAlta,
            FechaAlta = alta ?? Ahora.AddHours(-1),
            FechaModificacion = modificacion
        };

    private static IReadOnlyList<AvisoDto> ConOportunidades(params AvisoCrmFuente[] oportunidades)
        => CentroAvisosService.ConstruirAvisos([], [], ["T1"], "evelyn", SinLeidos, Ahora, oportunidades: oportunidades);

    [Fact]
    public void OportunidadAsignada_AvisaAlVendedorDelUsuario()
    {
        var aviso = Assert.Single(ConOportunidades(Oportunidad(9)));

        Assert.Equal("crm:9:T1", aviso.Clave);
        Assert.Equal(AvisoTipos.Crm, aviso.Tipo);
        Assert.Equal("Oportunidad asignada: Oportunidad 9", aviso.Titulo);
        Assert.Equal("Cliente SA · Contactado", aviso.Detalle);
        Assert.Equal("/crm?id=9", aviso.Ruta);
    }

    [Fact]
    public void OportunidadAsignada_IgnoraOtrosVendedoresViejasYPropiasSinTocar()
    {
        Assert.Empty(ConOportunidades(Oportunidad(1, tecnico: "T2")));
        Assert.Empty(ConOportunidades(Oportunidad(2, alta: Ahora.AddDays(-10))));
        Assert.Empty(ConOportunidades(Oportunidad(3, usuarioAlta: "EVELYN")));
        Assert.Single(ConOportunidades(Oportunidad(4, alta: Ahora.AddDays(-10), modificacion: Ahora.AddHours(-2))));
    }

    [Fact]
    public void TopeIa_AvisaAlPasarElPorcentajeYAlAlcanzarlo()
    {
        static IReadOnlyList<AvisoDto> ConTope(IaTopeEstadoDto? tope)
            => CentroAvisosService.ConstruirAvisos([], [], [], "evelyn", SinLeidos, Ahora, topeIa: tope);

        Assert.Empty(ConTope(null));
        Assert.Empty(ConTope(IaConsumoService.EvaluarTope("C1", 1000, 80, 500)));

        var aviso = Assert.Single(ConTope(IaConsumoService.EvaluarTope("C1", 1000, 80, 850)));
        Assert.Equal("ia-tope:202610:aviso", aviso.Clave);
        Assert.Equal("Usaste el 85% del tope de créditos de IA", aviso.Titulo);

        var tope = Assert.Single(ConTope(IaConsumoService.EvaluarTope("C1", 1000, 80, 1000)));
        Assert.Equal("ia-tope:202610:tope", tope.Clave);
        Assert.Equal(AvisoTipos.TopeIa, tope.Tipo);
        Assert.StartsWith("/conversaciones/configuracion", tope.Ruta);
    }
}

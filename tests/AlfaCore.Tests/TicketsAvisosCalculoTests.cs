using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

public sealed class TicketsAvisosCalculoTests
{
    private static readonly HorarioLaboral Horario = HorarioLaboral.Desde(new ConversacionAutomatizacionesConfigDto());
    private static readonly DateTime Lunes10 = new(2026, 11, 23, 10, 0, 0);

    private static TicketGridItemDto Ticket(long id, string estadoPlazo = "", string tecnico = "T1", string esperando = TicketEsperandoA.Nosotros)
        => new()
        {
            IdTicket = id,
            Numero = (int)id,
            CodigoVisible = $"#{id:D2}",
            Titulo = $"Problema {id}",
            ClienteNombre = "Cliente SA",
            IdTecnico = tecnico,
            TecnicoNombre = tecnico.Length == 0 ? string.Empty : "Juan",
            FechaHoraAlta = Lunes10.AddHours(-2),
            EsperandoA = esperando,
            EsperandoDesde = Lunes10.AddHours(-2),
            PlazoTipo = TicketPlazoTipos.Respuesta,
            PlazoEstado = estadoPlazo,
            PlazoTexto = estadoPlazo == TicketPlazoEstados.Vencido ? "Vencido hace 1 h" : "Quedan 30 min",
            EsperandoTexto = "2 h"
        };

    [Fact]
    public void Vencido_AvisaSoloVencidoAlTecnico()
    {
        var avisos = TicketsAvisosCalculo.Detectar([Ticket(1, TicketPlazoEstados.Vencido)], new TicketsAvisosConfigDto());
        var aviso = Assert.Single(avisos);
        Assert.Equal(TicketAvisoTipos.Vencido, aviso.Tipo);
        Assert.Equal(TicketPlazoTipos.Respuesta, aviso.Clave);
        Assert.Equal("T1", aviso.IdTecnico);
        Assert.False(aviso.ParaCoordinadores);
        Assert.Equal("/tickets?id=1", aviso.Url);
        Assert.Contains("respuesta", aviso.Titulo);
    }

    [Fact]
    public void PorVencer_SinTecnico_VaACoordinacion()
    {
        var aviso = Assert.Single(TicketsAvisosCalculo.Detectar([Ticket(2, TicketPlazoEstados.PorVencer, tecnico: "")], new TicketsAvisosConfigDto()));
        Assert.Equal(TicketAvisoTipos.PorVencer, aviso.Tipo);
        Assert.True(aviso.ParaCoordinadores);
    }

    [Fact]
    public void EsperandoAlCliente_NoAvisaAunqueEsteVencido()
    {
        var t = Ticket(3, TicketPlazoEstados.Vencido, esperando: TicketEsperandoA.Cliente);
        Assert.Empty(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto()));
    }

    [Fact]
    public void TiposApagados_NoAvisan()
    {
        var config = new TicketsAvisosConfigDto { AvisarVencido = false, AvisarPorVencer = false };
        Assert.Empty(TicketsAvisosCalculo.Detectar([Ticket(1, TicketPlazoEstados.Vencido), Ticket(2, TicketPlazoEstados.PorVencer)], config));
        Assert.Empty(TicketsAvisosCalculo.Detectar([Ticket(1, TicketPlazoEstados.Vencido)], new TicketsAvisosConfigDto { Activo = false }));
    }

    [Fact]
    public void SeguimientoVencido_ClaveCambiaConLaFecha()
    {
        var t = Ticket(4, TicketPlazoEstados.EnTermino);
        t.SeguimientoVencido = true;
        t.FechaSeguimiento = new DateTime(2026, 11, 23, 9, 0, 0);
        var a1 = Assert.Single(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto()));
        t.FechaSeguimiento = new DateTime(2026, 11, 25, 9, 0, 0);
        var a2 = Assert.Single(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto()));
        Assert.Equal(TicketAvisoTipos.Seguimiento, a1.Tipo);
        Assert.NotEqual(a1.Clave, a2.Clave);
    }

    [Fact]
    public void ClientePrioritario_AvisaACoordinacionDesdeLasHorasConfiguradas()
    {
        var t = Ticket(5, TicketPlazoEstados.EnTermino);
        t.ClientePrioritario = true;
        t.HorasHabilesEsperando = 3.5;
        Assert.Empty(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto { PrioritarioHoras = 4 }));

        t.HorasHabilesEsperando = 4;
        var aviso = Assert.Single(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto { PrioritarioHoras = 4 }));
        Assert.Equal(TicketAvisoTipos.Prioritario, aviso.Tipo);
        Assert.True(aviso.ParaCoordinadores);
        Assert.Contains("Asignado a Juan", aviso.Cuerpo);

        Assert.Empty(TicketsAvisosCalculo.Detectar([t], new TicketsAvisosConfigDto { PrioritarioHoras = 0 }));
    }

    [Fact]
    public void Resumen_UnoPorTecnicoConPendientes_DesdeLaHora()
    {
        var a = Ticket(1, TicketPlazoEstados.Vencido);
        var b = Ticket(2, TicketPlazoEstados.PorVencer);
        var c = Ticket(3, TicketPlazoEstados.EnTermino, tecnico: "T2", esperando: TicketEsperandoA.Cliente);
        var config = new TicketsAvisosConfigDto { ResumenHora = 9 };

        var resumen = Assert.Single(TicketsAvisosCalculo.Resumen([a, b, c], config, Horario, Lunes10));
        Assert.Equal(TicketAvisoTipos.Resumen, resumen.Tipo);
        Assert.Equal(0, resumen.IdTicket);
        Assert.Equal("20261123-T1", resumen.Clave);
        Assert.Equal("Tenés 2 tickets esperando tu respuesta: 1 vencido y 1 por vencer. Primero: #01 Problema 1.", resumen.Cuerpo);
        Assert.Equal("/tickets?bandeja=mios", resumen.Url);
    }

    [Fact]
    public void Resumen_NoSaleAntesDeHoraNiFueraDeHorarioNiApagado()
    {
        var t = Ticket(1, TicketPlazoEstados.Vencido);
        Assert.Empty(TicketsAvisosCalculo.Resumen([t], new TicketsAvisosConfigDto { ResumenHora = 11 }, Horario, Lunes10));
        Assert.Empty(TicketsAvisosCalculo.Resumen([t], new TicketsAvisosConfigDto { ResumenHora = null }, Horario, Lunes10));
        Assert.Empty(TicketsAvisosCalculo.Resumen([t], new TicketsAvisosConfigDto { ResumenHora = 9 }, Horario, new DateTime(2026, 11, 22, 10, 0, 0)));
    }

    [Fact]
    public void Resumen_IncluyeSeguimientosDeHoy()
    {
        var t = Ticket(6, tecnico: "T3", esperando: TicketEsperandoA.Seguimiento);
        t.FechaSeguimiento = Lunes10.AddHours(3);
        var resumen = Assert.Single(TicketsAvisosCalculo.Resumen([t], new TicketsAvisosConfigDto(), Horario, Lunes10));
        Assert.Contains("1 seguimiento para hoy", resumen.Cuerpo);
    }

    [Fact]
    public void Coordinadores_SeSeparanSinRepetir()
    {
        var config = new TicketsAvisosConfigDto { Coordinadores = " alberto, soporte;Alberto ,, " };
        Assert.Equal(["alberto", "soporte"], config.CoordinadoresLista());
    }
}

public sealed class TicketsIaPrioridadTests
{
    [Fact]
    public void Parsear_LeeJsonAunqueVengaConTextoAlrededor()
    {
        var s = TicketsIaPrioridad.Parsear("Claro: {\"prioridad\": 3, \"motivo\": \"No puede facturar\", \"resumen\": \"Error al emitir\"} listo");
        Assert.NotNull(s);
        Assert.True(s!.Disponible);
        Assert.Equal(3, s.Prioridad);
        Assert.Equal("No puede facturar", s.Motivo);
        Assert.Equal("Error al emitir", s.Resumen);
    }

    [Fact]
    public void Parsear_AjustaRangoYAceptaTexto()
    {
        Assert.Equal(3, TicketsIaPrioridad.Parsear("{\"prioridad\":7}")!.Prioridad);
        Assert.Equal(0, TicketsIaPrioridad.Parsear("{\"prioridad\":-2}")!.Prioridad);
        Assert.Equal(2, TicketsIaPrioridad.Parsear("{\"prioridad\":\"2\"}")!.Prioridad);
    }

    [Fact]
    public void Parsear_RespuestaInvalida_EsNull()
    {
        Assert.Null(TicketsIaPrioridad.Parsear(null));
        Assert.Null(TicketsIaPrioridad.Parsear("no sé"));
        Assert.Null(TicketsIaPrioridad.Parsear("{\"motivo\":\"x\"}"));
        Assert.Null(TicketsIaPrioridad.Parsear("{\"prioridad\":\"alta\"}"));
        Assert.Null(TicketsIaPrioridad.Parsear("{roto"));
    }

    [Fact]
    public void Contexto_PasaHtmlATextoYOrdenaLaConversacion()
    {
        var texto = TicketsIaPrioridad.ArmarContexto("No anda la factura", "<p>Sale un <b>error</b></p><ul><li>al emitir</li></ul>", "Cliente SA",
            [(true, "Hola, no puedo facturar"), (false, "Ya lo vemos"), (true, "  ")]);
        Assert.Contains("Título: No anda la factura", texto);
        Assert.Contains("Sale un error", texto);
        Assert.Contains("- al emitir", texto);
        Assert.DoesNotContain("<", texto);
        Assert.Contains("Cliente: Cliente SA", texto);
        Assert.True(texto.IndexOf("Cliente: Hola", StringComparison.Ordinal) < texto.IndexOf("Nosotros: Ya lo vemos", StringComparison.Ordinal));
        Assert.Equal(2, texto.Split('\n').Count(l => l.StartsWith("Cliente: ") || l.StartsWith("Nosotros: ")) - 1);
    }

    [Fact]
    public void Prompt_NoTieneDisparadoresPorPalabras()
    {
        // Regla del bot: la IA decide con contexto; nada de listas de palabras clave ni frases de ejemplo.
        Assert.Contains("no decidas por palabras sueltas", TicketsIaPrioridad.SystemPrompt);
        Assert.DoesNotContain("palabras clave:", TicketsIaPrioridad.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Globalization;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Lo que se sabe de un ticket abierto para decidir qué atender primero (lo arma TicketsService).</summary>
public sealed record TicketAtencionDatos
{
    public string CodigoEstado { get; init; } = string.Empty;
    public int Prioridad { get; init; }
    public DateTime FechaAlta { get; init; }
    /// <summary>El ticket tiene una conversación vinculada (hay mensajes del cliente para mirar).</summary>
    public bool TieneConversacion { get; init; }
    /// <summary>Último mensaje del cliente posterior al alta (conversación o respuesta "Tengo una consulta" a un informe).</summary>
    public DateTime? UltimoMensajeCliente { get; init; }
    /// <summary>Primer mensaje del cliente que todavía no tiene respuesta nuestra.</summary>
    public DateTime? PrimerMensajeClienteSinResponder { get; init; }
    /// <summary>Última respuesta nuestra al cliente posterior al alta (mensaje de una persona o informe publicado).</summary>
    public DateTime? UltimaRespuestaNuestra { get; init; }
    /// <summary>Primera acción de una persona del equipo posterior al alta (respuesta, nota o cambio).</summary>
    public DateTime? PrimeraRespuestaNuestra { get; init; }
    public DateTime? FechaSeguimiento { get; init; }
    public DateTime? SeguimientoDesde { get; init; }
    /// <summary>Períodos en estados de espera del cliente (Hasta null = sigue en espera).</summary>
    public IReadOnlyList<(DateTime Desde, DateTime? Hasta)> PeriodosEspera { get; init; } = [];
    public bool ClientePrioritario { get; init; }
    public string EstadoAbono { get; init; } = string.Empty;
    public int CasosAbiertosCliente { get; init; }
}

/// <summary>
/// Orden "Atender primero" de Tickets: a quién le toca mover el ticket, plazo (horas hábiles, frenado mientras se
/// espera al cliente), puntaje y motivos visibles. Funciones puras: todo lo que lee de la base viene en
/// <see cref="TicketAtencionDatos"/>, la configuración en <see cref="TicketsAtencionConfigDto"/>.
/// </summary>
public static class TicketsAtencionCalculo
{
    public static void Aplicar(TicketGridItemDto ticket, TicketAtencionDatos d, TicketsAtencionConfigDto config, HorarioLaboral horario, DateTime ahora)
    {
        var estadosEspera = EstadosEspera(config);
        var motivos = new List<string>();

        ticket.ClientePrioritario = d.ClientePrioritario;
        ticket.EstadoAbono = d.EstadoAbono;
        ticket.CasosAbiertosCliente = d.CasosAbiertosCliente;
        ticket.FechaSeguimiento = d.FechaSeguimiento;

        // 1. ¿A quién le toca?
        var clienteEscribioTrasSeguimiento = d.SeguimientoDesde is DateTime sd && d.UltimoMensajeCliente > sd;
        var enEstadoEspera = estadosEspera.Contains(d.CodigoEstado);
        var esperaDesde = d.PeriodosEspera.Where(p => p.Hasta is null).Select(p => (DateTime?)p.Desde).LastOrDefault();
        var clienteEscribioTrasEspera = esperaDesde is DateTime ed && d.UltimoMensajeCliente > ed;
        ticket.SeguimientoVencido = false;

        if (d.FechaSeguimiento is DateTime seguimiento && seguimiento > ahora && !clienteEscribioTrasSeguimiento)
        {
            ticket.EsperandoA = TicketEsperandoA.Seguimiento;
            ticket.EsperandoDesde = d.SeguimientoDesde ?? d.FechaAlta;
        }
        else if (enEstadoEspera && !clienteEscribioTrasEspera && !clienteEscribioTrasSeguimiento)
        {
            ticket.EsperandoA = TicketEsperandoA.Cliente;
            ticket.EsperandoDesde = esperaDesde ?? d.FechaAlta;
        }
        else
        {
            ticket.EsperandoA = TicketEsperandoA.Nosotros;
            if (d.TieneConversacion && d.UltimaRespuestaNuestra is DateTime nuestra && !(d.UltimoMensajeCliente > nuestra)
                && !clienteEscribioTrasSeguimiento && !clienteEscribioTrasEspera)
            {
                // Respondimos y el cliente no volvió a escribir: la pelota está del lado del cliente.
                ticket.EsperandoA = TicketEsperandoA.Cliente;
                ticket.EsperandoDesde = nuestra;
            }
            else
            {
                var desde = d.PrimerMensajeClienteSinResponder ?? d.FechaAlta;
                var salioDeEspera = d.PeriodosEspera.Where(p => p.Hasta is not null).Select(p => p.Hasta!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
                if (salioDeEspera > desde && d.PrimerMensajeClienteSinResponder is null)
                    desde = salioDeEspera;
                if (d.FechaSeguimiento is DateTime vencido && vencido <= ahora)
                {
                    ticket.SeguimientoVencido = true;
                    desde = vencido > desde ? vencido : desde;
                }
                ticket.EsperandoDesde = desde < d.FechaAlta ? d.FechaAlta : desde;
            }
        }
        ticket.HorasHabilesEsperando = ticket.EsperandoDesde is DateTime esp ? horario.HorasHabilesEntre(esp, ahora) : 0;

        // 2. Prioridad efectiva: la urgencia (estrellas) sube un nivel si el cliente es prioritario.
        ticket.PrioridadEfectiva = Math.Clamp(d.Prioridad + (d.ClientePrioritario ? 1 : 0), 0, 3);

        // 3. Plazo: primera respuesta mientras nadie respondió; después, resolución. El tiempo en espera del
        // cliente no cuenta. Vencido se muestra aunque ahora esté frenado (no se esconde).
        var plazo = config.Plazos.FirstOrDefault(p => p.Prioridad == ticket.PrioridadEfectiva);
        ticket.PlazoTipo = string.Empty;
        ticket.PlazoEstado = string.Empty;
        ticket.PlazoVence = null;
        ticket.PlazoHorasRestantes = 0;
        if (plazo is not null)
        {
            var esRespuesta = d.PrimeraRespuestaNuestra is null;
            var limite = esRespuesta ? plazo.HorasRespuesta : plazo.HorasResolucion;
            if (limite > 0)
            {
                var consumido = horario.HorasHabilesEntre(d.FechaAlta, ahora) - HorasEnEspera(d, horario, ahora);
                if (consumido < 0)
                    consumido = 0;
                var restante = limite - consumido;
                var frenado = ticket.EsperandoA != TicketEsperandoA.Nosotros;

                ticket.PlazoTipo = esRespuesta ? TicketPlazoTipos.Respuesta : TicketPlazoTipos.Resolucion;
                ticket.PlazoHorasRestantes = restante;
                ticket.PlazoEstado = restante < 0
                    ? TicketPlazoEstados.Vencido
                    : frenado
                        ? TicketPlazoEstados.Frenado
                        : consumido >= limite * Math.Clamp(config.PorVencerPorcentaje, 1, 100) / 100.0
                            ? TicketPlazoEstados.PorVencer
                            : TicketPlazoEstados.EnTermino;
                if (restante >= 0 && !frenado)
                    ticket.PlazoVence = horario.SumarHorasHabiles(ahora, restante);
            }
        }

        ticket.EsperandoTexto = Duracion(ticket.HorasHabilesEsperando, horario);
        ticket.PlazoTexto = ticket.PlazoEstado switch
        {
            TicketPlazoEstados.Vencido => $"Vencido hace {Duracion(-ticket.PlazoHorasRestantes, horario)}",
            TicketPlazoEstados.Frenado => $"Frenado · quedan {Duracion(ticket.PlazoHorasRestantes, horario)}",
            "" => string.Empty,
            _ => $"Quedan {Duracion(ticket.PlazoHorasRestantes, horario)}"
        };

        // 4. Puntaje y motivos. Lo que espera al cliente o a una fecha va después de lo nuestro.
        var nombrePlazo = ticket.PlazoTipo == TicketPlazoTipos.Respuesta ? "respuesta" : "resolución";
        if (ticket.EsperandoA == TicketEsperandoA.Nosotros)
        {
            double puntaje = 0;
            if (ticket.PlazoEstado == TicketPlazoEstados.Vencido)
            {
                puntaje += config.PesoVencido;
                motivos.Add($"Plazo de {nombrePlazo} vencido hace {Duracion(-ticket.PlazoHorasRestantes, horario)}");
            }
            else if (ticket.PlazoEstado == TicketPlazoEstados.PorVencer)
            {
                puntaje += config.PesoPorVencer;
                motivos.Add($"Plazo de {nombrePlazo}: quedan {Duracion(ticket.PlazoHorasRestantes, horario)}");
            }

            puntaje += ticket.PrioridadEfectiva * config.PesoPrioridad;
            if (ticket.PrioridadEfectiva >= 3) motivos.Add("Urgente");
            else if (ticket.PrioridadEfectiva == 2) motivos.Add("Prioridad alta");

            if (d.ClientePrioritario)
            {
                puntaje += config.PesoClientePrioritario;
                motivos.Add(string.Equals(d.EstadoAbono, ConversacionAbonoEstados.Abonado, StringComparison.OrdinalIgnoreCase)
                    ? "Cliente con abono"
                    : "Cliente prioritario");
            }

            if (config.VariosCasosDesde > 1 && d.CasosAbiertosCliente >= config.VariosCasosDesde)
            {
                puntaje += config.PesoVariosCasos;
                motivos.Add($"{d.CasosAbiertosCliente} casos abiertos del cliente");
            }

            puntaje += Math.Min(ticket.HorasHabilesEsperando * config.PesoHoraEspera, Math.Max(0, config.PesoHoraEsperaTope));
            if (ticket.SeguimientoVencido)
            {
                puntaje += config.PesoSeguimientoVencido;
                motivos.Add("Seguimiento vencido");
            }
            if (clienteEscribioTrasSeguimiento || clienteEscribioTrasEspera)
                motivos.Add("El cliente volvió a escribir");

            motivos.Add($"Esperando respuesta hace {Duracion(ticket.HorasHabilesEsperando, horario)}");
            ticket.Puntaje = Math.Round(puntaje, 1);
        }
        else
        {
            ticket.Puntaje = 0;
            if (ticket.EsperandoA == TicketEsperandoA.Seguimiento && d.FechaSeguimiento is DateTime f)
                motivos.Add($"Seguimiento el {f.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}");
            else
                motivos.Add($"Esperando al cliente hace {Duracion(ticket.HorasHabilesEsperando, horario)}");
            if (ticket.PlazoEstado == TicketPlazoEstados.Vencido)
                motivos.Add($"Plazo de {nombrePlazo} vencido");
        }

        ticket.Motivos = motivos;
    }

    /// <summary>Orden "Atender primero": primero lo que nos toca (mayor puntaje y más viejo), después lo que espera.</summary>
    public static IOrderedEnumerable<TicketGridItemDto> Ordenar(IEnumerable<TicketGridItemDto> tickets)
        => tickets
            .OrderBy(t => t.EsperandoA switch
            {
                TicketEsperandoA.Nosotros => 0,
                TicketEsperandoA.Cliente => 1,
                TicketEsperandoA.Seguimiento => 2,
                _ => 3
            })
            .ThenByDescending(t => t.Puntaje)
            .ThenBy(t => t.EsperandoDesde ?? t.FechaHoraAlta)
            .ThenBy(t => t.IdTicket);

    public static bool EnBandeja(TicketGridItemDto t, string bandeja, string? idTecnicoActual)
        => (bandeja ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            TicketBandejaKeys.Mios => !string.IsNullOrWhiteSpace(idTecnicoActual)
                                      && string.Equals(t.IdTecnico.Trim(), idTecnicoActual.Trim(), StringComparison.OrdinalIgnoreCase)
                                      && t.EsperandoA == TicketEsperandoA.Nosotros,
            TicketBandejaKeys.Nosotros => t.EsperandoA == TicketEsperandoA.Nosotros,
            TicketBandejaKeys.Plazo => t.EsperandoA == TicketEsperandoA.Nosotros
                                       && t.PlazoEstado is TicketPlazoEstados.Vencido or TicketPlazoEstados.PorVencer,
            TicketBandejaKeys.Prioritarios => t.ClientePrioritario,
            TicketBandejaKeys.SinAsignar => string.IsNullOrWhiteSpace(t.IdTecnico),
            TicketBandejaKeys.Seguimientos => t.SeguimientoVencido
                                              || (t.EsperandoA == TicketEsperandoA.Seguimiento && t.FechaSeguimiento is DateTime f && f.Date <= DateTime.Today),
            TicketBandejaKeys.Cliente => t.EsperandoA is TicketEsperandoA.Cliente or TicketEsperandoA.Seguimiento,
            TicketBandejaKeys.Bot => string.Equals(t.UsuarioAltaTicket.Trim(), "AlfaCore", StringComparison.OrdinalIgnoreCase),
            _ => true
        };

    /// <summary>
    /// Ticket cerrado: ¿respondimos y resolvimos dentro del plazo? Mismas reglas que los abiertos (horas hábiles,
    /// sin contar la espera del cliente, prioridad efectiva). Respuesta null = no hubo respuesta registrada.
    /// </summary>
    public static (bool? RespuestaEnPlazo, bool? ResolucionEnPlazo, double HorasResolucion) EvaluarCerrado(
        TicketAtencionDatos d, DateTime cierre, TicketsAtencionConfigDto config, HorarioLaboral horario)
    {
        var prioridad = Math.Clamp(d.Prioridad + (d.ClientePrioritario ? 1 : 0), 0, 3);
        var plazo = config.Plazos.FirstOrDefault(p => p.Prioridad == prioridad);
        var espera = d.PeriodosEspera.Sum(p =>
        {
            var desde = p.Desde < d.FechaAlta ? d.FechaAlta : p.Desde;
            var hasta = p.Hasta is DateTime h && h < cierre ? h : cierre;
            return horario.HorasHabilesEntre(desde, hasta);
        });
        var resolucion = Math.Max(0, horario.HorasHabilesEntre(d.FechaAlta, cierre) - espera);

        bool? respuesta = null;
        if (d.PrimeraRespuestaNuestra is DateTime primera && plazo is { HorasRespuesta: > 0 })
            respuesta = horario.HorasHabilesEntre(d.FechaAlta, primera <= cierre ? primera : cierre) <= plazo.HorasRespuesta;
        bool? resuelto = plazo is { HorasResolucion: > 0 } ? resolucion <= plazo.HorasResolucion : null;
        return (respuesta, resuelto, resolucion);
    }

    /// <summary>
    /// Horas hábiles promedio en cada etapa, a partir del historial de cambios (por ticket, en orden). Solo cuentan
    /// los tramos terminados (el ticket ya salió de esa etapa).
    /// </summary>
    public static IReadOnlyList<(string Estado, double HorasPromedio, int Tramos)> TiempoPorEtapa(
        IEnumerable<(long IdTicket, string Estado, DateTime Fecha)> cambios, HorarioLaboral horario)
        => cambios
            .GroupBy(c => c.IdTicket)
            .SelectMany(g =>
            {
                var lista = g.OrderBy(c => c.Fecha).ToList();
                return lista.Take(lista.Count - 1).Select((c, i) => (c.Estado, Horas: horario.HorasHabilesEntre(c.Fecha, lista[i + 1].Fecha)));
            })
            .GroupBy(t => t.Estado, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Math.Round(g.Average(x => x.Horas), 1), g.Count()))
            .ToList();

    /// <summary>Tramos de antigüedad (días corridos desde el alta) para el panel de seguimiento.</summary>
    public static readonly (string Clave, string Nombre, int HastaDias)[] TramosAntiguedad =
    [
        ("0", "Menos de 1 día", 1),
        ("1-3", "1 a 3 días", 3),
        ("3-7", "3 a 7 días", 7),
        ("7-30", "7 a 30 días", 30),
        ("30+", "Más de 30 días", int.MaxValue)
    ];

    public static string TramoAntiguedad(DateTime alta, DateTime ahora)
    {
        var dias = (ahora - alta).TotalDays;
        return TramosAntiguedad.First(t => dias < t.HastaDias).Clave;
    }

    /// <summary>"3 h", "2 días hábiles", "menos de 1 h".</summary>
    public static string Duracion(double horasHabiles, HorarioLaboral horario)
    {
        if (horasHabiles < 1)
            return "menos de 1 h";
        var jornada = horario.HorasPorJornada;
        if (horasHabiles < jornada)
            return $"{Math.Floor(horasHabiles).ToString(CultureInfo.InvariantCulture)} h";
        var dias = Math.Floor(horasHabiles / jornada);
        return dias == 1 ? "1 día hábil" : $"{dias.ToString(CultureInfo.InvariantCulture)} días hábiles";
    }

    public static HashSet<string> EstadosEspera(TicketsAtencionConfigDto config)
        => (config.EstadosEsperaCliente ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double HorasEnEspera(TicketAtencionDatos d, HorarioLaboral horario, DateTime ahora)
        => d.PeriodosEspera.Sum(p =>
        {
            var desde = p.Desde < d.FechaAlta ? d.FechaAlta : p.Desde;
            return horario.HorasHabilesEntre(desde, p.Hasta ?? ahora);
        });
}

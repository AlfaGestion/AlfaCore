using AlfaCore.Models;
using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Decide qué avisos de Tickets corresponden, a partir de los tickets abiertos ya calculados por
/// <see cref="TicketsAtencionCalculo"/>. No envía nada: la clave de cada aviso hace que se mande una sola vez
/// (TICK_AVISOS tiene índice único por ticket, tipo y clave).
/// </summary>
public static class TicketsAvisosCalculo
{
    /// <summary>Avisos por ticket: plazo por vencer o vencido, seguimiento vencido y cliente prioritario esperando.</summary>
    public static IReadOnlyList<TicketAviso> Detectar(IEnumerable<TicketGridItemDto> abiertos, TicketsAvisosConfigDto config)
    {
        var avisos = new List<TicketAviso>();
        if (!config.Activo)
            return avisos;

        foreach (var t in abiertos.Where(x => x.FechaHoraCierre is null && x.EsperandoA == TicketEsperandoA.Nosotros))
        {
            var url = UrlTicket(t.IdTicket);
            var quien = Referencia(t);
            var plazo = t.PlazoTipo == TicketPlazoTipos.Respuesta ? "respuesta" : "resolución";
            var tecnico = (t.IdTecnico ?? string.Empty).Trim();

            // Un vencido no manda además el "por vencer": la clave distingue respuesta de resolución.
            if (config.AvisarVencido && t.PlazoEstado == TicketPlazoEstados.Vencido)
            {
                avisos.Add(new TicketAviso(t.IdTicket, TicketAvisoTipos.Vencido, t.PlazoTipo, tecnico, tecnico.Length == 0,
                    $"Ticket {Codigo(t)}: venció el plazo de {plazo}",
                    $"{quien}. {t.PlazoTexto}.",
                    url, $"Plazo de {plazo} vencido"));
            }
            else if (config.AvisarPorVencer && t.PlazoEstado == TicketPlazoEstados.PorVencer)
            {
                avisos.Add(new TicketAviso(t.IdTicket, TicketAvisoTipos.PorVencer, t.PlazoTipo, tecnico, tecnico.Length == 0,
                    $"Ticket {Codigo(t)}: el plazo de {plazo} vence pronto",
                    $"{quien}. {t.PlazoTexto}.",
                    url, $"Plazo de {plazo} por vencer"));
            }

            if (config.AvisarSeguimiento && t.SeguimientoVencido && t.FechaSeguimiento is DateTime seguimiento)
            {
                avisos.Add(new TicketAviso(t.IdTicket, TicketAvisoTipos.Seguimiento, Marca(seguimiento), tecnico, tecnico.Length == 0,
                    $"Seguimiento de hoy: ticket {Codigo(t)}",
                    $"Quedó para retomar el {seguimiento.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}. {quien}.",
                    url, "Seguimiento vencido"));
            }

            // A coordinación: un cliente prioritario que lleva X horas hábiles esperando. Una vez por espera.
            if (config.PrioritarioHoras > 0 && t.ClientePrioritario && t.HorasHabilesEsperando >= config.PrioritarioHoras)
            {
                var asignado = string.IsNullOrWhiteSpace(t.TecnicoNombre) ? "Sin asignar" : $"Asignado a {t.TecnicoNombre.Trim()}";
                avisos.Add(new TicketAviso(t.IdTicket, TicketAvisoTipos.Prioritario, Marca(t.EsperandoDesde ?? t.FechaHoraAlta), string.Empty, true,
                    $"Cliente prioritario esperando: {Cliente(t)}",
                    $"Ticket {Codigo(t)} espera respuesta hace {t.EsperandoTexto}. {asignado}.",
                    url, $"Cliente prioritario esperando hace {t.EsperandoTexto}"));
            }
        }
        return avisos;
    }

    /// <summary>
    /// Resumen diario por técnico ("tus pendientes de hoy"), una vez por día hábil desde la hora configurada.
    /// Solo a quienes tienen algo que hacer: tickets que esperan su respuesta o seguimientos de hoy.
    /// </summary>
    public static IReadOnlyList<TicketAviso> Resumen(IEnumerable<TicketGridItemDto> abiertos, TicketsAvisosConfigDto config,
        HorarioLaboral horario, DateTime ahora)
    {
        var avisos = new List<TicketAviso>();
        if (!config.Activo || config.ResumenHora is not int hora || ahora.Hour < hora || !horario.EsHabil(ahora))
            return avisos;

        var dia = ahora.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var grupo in abiertos
                     .Where(t => t.FechaHoraCierre is null && !string.IsNullOrWhiteSpace(t.IdTecnico))
                     .GroupBy(t => t.IdTecnico.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var nuestros = grupo.Where(t => t.EsperandoA == TicketEsperandoA.Nosotros).ToList();
            var vencidos = nuestros.Count(t => t.PlazoEstado == TicketPlazoEstados.Vencido);
            var porVencer = nuestros.Count(t => t.PlazoEstado == TicketPlazoEstados.PorVencer);
            var seguimientos = grupo.Count(t => t.SeguimientoVencido
                                                || (t.EsperandoA == TicketEsperandoA.Seguimiento && t.FechaSeguimiento?.Date == ahora.Date));
            if (nuestros.Count == 0 && seguimientos == 0)
                continue;

            var partes = new List<string>();
            if (vencidos > 0) partes.Add(Cantidad(vencidos, "vencido", "vencidos"));
            if (porVencer > 0) partes.Add(Cantidad(porVencer, "por vencer", "por vencer"));
            if (seguimientos > 0) partes.Add(Cantidad(seguimientos, "seguimiento para hoy", "seguimientos para hoy"));

            var cuerpo = nuestros.Count == 1 ? "Tenés 1 ticket esperando tu respuesta" : $"Tenés {nuestros.Count} tickets esperando tu respuesta";
            if (partes.Count > 0)
                cuerpo += ": " + Unir(partes);
            cuerpo += ".";
            var primero = TicketsAtencionCalculo.Ordenar(nuestros).FirstOrDefault();
            if (primero is not null)
                cuerpo += $" Primero: {Codigo(primero)} {Recortar(primero.Titulo, 60)}.";

            avisos.Add(new TicketAviso(0, TicketAvisoTipos.Resumen, $"{dia}-{grupo.Key}", grupo.Key, false,
                "Tus pendientes de hoy en Tickets", cuerpo, $"/tickets?bandeja={TicketBandejaKeys.Mios}",
                $"Resumen del día: {nuestros.Count} esperando respuesta"));
        }
        return avisos;
    }

    public static string UrlTicket(long idTicket) => $"/tickets?id={idTicket.ToString(CultureInfo.InvariantCulture)}";

    private static string Codigo(TicketGridItemDto t)
        => string.IsNullOrWhiteSpace(t.CodigoVisible) ? $"#{t.Numero.ToString(CultureInfo.InvariantCulture)}" : t.CodigoVisible.Trim();

    private static string Cliente(TicketGridItemDto t)
        => !string.IsNullOrWhiteSpace(t.ClienteNombre) ? t.ClienteNombre.Trim()
            : !string.IsNullOrWhiteSpace(t.ContactoNombre) ? t.ContactoNombre.Trim()
            : "sin cliente";

    private static string Referencia(TicketGridItemDto t) => $"{Recortar(t.Titulo, 70)} · {Cliente(t)}";

    private static string Marca(DateTime fecha) => fecha.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

    private static string Cantidad(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    private static string Unir(List<string> partes)
        => partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1];

    private static string Recortar(string? texto, int largo)
    {
        var limpio = (texto ?? string.Empty).Trim();
        return limpio.Length <= largo ? limpio : limpio[..(largo - 1)].TrimEnd() + "…";
    }
}

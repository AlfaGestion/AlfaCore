using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace AlfaCore.Services;

/// <summary>
/// Panel de seguimiento de Tickets: cuánto entra y sale, atraso por antigüedad, cumplimiento de plazos, tiempo en
/// cada etapa, carga por técnico y clientes con más casos. Los abiertos se analizan con el mismo cálculo de
/// «Atender primero»; los cerrados del período con <see cref="TicketsAtencionCalculo.EvaluarCerrado"/>.
/// </summary>
public sealed partial class TicketsService
{
    private const int SemanasPanel = 12;

    public Task<TicketsSeguimientoDto> GetSeguimientoAsync(int dias, CancellationToken ct = default)
        => ExecuteLoggedAsync(ModuleName, "GetSeguimiento", async token =>
        {
            dias = Math.Clamp(dias <= 0 ? 90 : dias, 7, 730);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var esquema = await LeerEsquemaAtencionAsync(cn, token);
            var ahora = esquema.Ahora;
            var desde = ahora.Date.AddDays(-dias);
            var dto = new TicketsSeguimientoDto { Dias = dias };

            // 1. Abiertos con los datos de atención (mismo cálculo que la lista).
            var abiertos = await BuscarParaAtencionAsync(cn, new TicketsFilters { Bandeja = TicketBandejaKeys.Abiertos, IncluirCerrados = false }, "t.FechaHoraAlta DESC", token);
            dto.Truncado = abiertos.Count >= AtencionTope;
            dto.Abiertos = abiertos.Count;
            dto.EsperanNuestra = abiertos.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros);
            dto.Vencidos = abiertos.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros && t.PlazoEstado == TicketPlazoEstados.Vencido);
            dto.PorVencer = abiertos.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros && t.PlazoEstado == TicketPlazoEstados.PorVencer);
            dto.EsperandoCliente = abiertos.Count(t => t.EsperandoA is TicketEsperandoA.Cliente or TicketEsperandoA.Seguimiento);
            dto.SeguimientosVencidos = abiertos.Count(t => t.SeguimientoVencido);
            dto.SinAsignar = abiertos.Count(t => string.IsNullOrWhiteSpace(t.IdTecnico));
            dto.Prioritarios = abiertos.Count(t => t.ClientePrioritario);

            dto.Antiguedad = TicketsAtencionCalculo.TramosAntiguedad
                .Select(tramo => new TicketsAntiguedadTramoDto
                {
                    Clave = tramo.Clave,
                    Nombre = tramo.Nombre,
                    Nosotros = abiertos.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros && TicketsAtencionCalculo.TramoAntiguedad(t.FechaHoraAlta, ahora) == tramo.Clave),
                    Cliente = abiertos.Count(t => t.EsperandoA != TicketEsperandoA.Nosotros && TicketsAtencionCalculo.TramoAntiguedad(t.FechaHoraAlta, ahora) == tramo.Clave)
                })
                .ToList();

            dto.MasAtrasados = TicketsAtencionCalculo.Ordenar(abiertos.Where(t => t.EsperandoA == TicketEsperandoA.Nosotros)).Take(10).ToList();

            dto.Tecnicos = abiertos
                .GroupBy(t => t.IdTecnico.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var masViejo = g.OrderBy(t => t.FechaHoraAlta).First();
                    return new TicketsTecnicoCargaDto
                    {
                        IdTecnico = g.Key,
                        Nombre = string.IsNullOrWhiteSpace(g.Key) ? "Sin asignar" : FirstNonEmpty(masViejo.TecnicoNombre, g.Key),
                        Abiertos = g.Count(),
                        EsperanNuestra = g.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros),
                        Vencidos = g.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros && t.PlazoEstado == TicketPlazoEstados.Vencido),
                        MasViejoNumero = masViejo.Numero,
                        MasViejoId = masViejo.IdTicket,
                        MasViejoTexto = Antiguedad(masViejo.FechaHoraAlta, ahora)
                    };
                })
                .OrderByDescending(t => t.Vencidos).ThenByDescending(t => t.EsperanNuestra).ThenBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // 2. Entradas y cierres por semana, y por período.
            var lunesInicial = InicioSemana(ahora.Date).AddDays(-7 * (SemanasPanel - 1));
            var movimientos = new List<(DateTime Alta, DateTime? Cierre, string Cliente)>();
            var sqlMov = $"""
                SELECT t.FechaHoraAlta, t.FechaHoraCierre, UPPER(ISNULL(COALESCE(NULLIF(LTRIM(RTRIM(t.ClienteCodigo)), ''), contactoCuenta.Cuenta), ''))
                {TicketBaseFromSql(includeMessageCount: false)}
                WHERE ISNULL(t.Baja, 0) = 0
                  AND (t.FechaHoraAlta >= @Desde OR t.FechaHoraCierre >= @Desde);
                """;
            await using (var cmd = new SqlCommand(sqlMov, cn))
            {
                cmd.Parameters.AddWithValue("@Desde", lunesInicial < desde ? lunesInicial : desde);
                await using var rd = await cmd.ExecuteReaderAsync(token);
                while (await rd.ReadAsync(token))
                    movimientos.Add((rd.GetDateTime(0), rd.IsDBNull(1) ? null : rd.GetDateTime(1), GetString(rd, 2)));
            }

            dto.Semanas = Enumerable.Range(0, SemanasPanel)
                .Select(i => lunesInicial.AddDays(7 * i))
                .Select(lunes => new ComparativoMensualDto
                {
                    Periodo = lunes.ToString("dd/MM", CultureInfo.InvariantCulture),
                    Ventas = movimientos.Count(m => m.Alta >= lunes && m.Alta < lunes.AddDays(7)),
                    Compras = movimientos.Count(m => m.Cierre is DateTime c && c >= lunes && c < lunes.AddDays(7))
                })
                .ToList();
            dto.EntradasPeriodo = movimientos.Count(m => m.Alta >= desde);
            dto.CierresPeriodo = movimientos.Count(m => m.Cierre is DateTime c && c >= desde);

            // 3. Clientes con más casos abiertos (y cuántos casos abrieron en el período).
            var creadosPorCliente = movimientos.Where(m => m.Alta >= desde && m.Cliente.Length > 0)
                .GroupBy(m => m.Cliente).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            dto.Clientes = abiertos
                .Where(t => !string.IsNullOrWhiteSpace(t.ClienteCodigo))
                .GroupBy(t => t.ClienteCodigo.Trim().ToUpperInvariant())
                .Select(g => new TicketsClienteCasosDto
                {
                    Codigo = g.First().ClienteCodigo.Trim(),
                    Nombre = FirstNonEmpty(g.First().ClienteNombre, g.Key),
                    Abiertos = g.Count(),
                    Vencidos = g.Count(t => t.EsperandoA == TicketEsperandoA.Nosotros && t.PlazoEstado == TicketPlazoEstados.Vencido),
                    CreadosPeriodo = creadosPorCliente.TryGetValue(g.Key, out var n) ? n : 0,
                    Prioritario = g.First().ClientePrioritario,
                    EstadoAbono = g.First().EstadoAbono,
                    TipoClienteNombre = g.First().TipoClienteNombre,
                    TipoClienteColor = g.First().TipoClienteColor
                })
                .OrderByDescending(c => c.Abiertos).ThenByDescending(c => c.Vencidos).ThenBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Take(10)
                .ToList();

            // 4. Cumplimiento de plazos de los cerrados en el período.
            if (esquema.TieneSeguimiento)
                await CalcularCumplimientoAsync(cn, esquema, dto, desde, token);

            // 5. Tiempo en cada etapa (desde que existe el historial).
            if (esquema.TieneHistorial)
                await CalcularTiempoPorEtapaAsync(cn, dto, desde, token);

            return dto;
        }, "No se pudo armar el panel de seguimiento de tickets.", ct);

    private async Task CalcularCumplimientoAsync(SqlConnection cn, EsquemaAtencion esquema, TicketsSeguimientoDto dto, DateTime desde, CancellationToken ct)
    {
        var cerrados = new List<(long Id, int Prioridad, DateTime Alta, DateTime Cierre, string Cliente, bool TieneConversacion)>();
        var sql = $"""
            SELECT t.IdTicket, ISNULL(t.Prioridad, 0), t.FechaHoraAlta, t.FechaHoraCierre,
                   UPPER(ISNULL(COALESCE(NULLIF(LTRIM(RTRIM(t.ClienteCodigo)), ''), contactoCuenta.Cuenta), '')),
                   CASE WHEN t.IdConversacion IS NULL THEN 0 ELSE 1 END
            {TicketBaseFromSql(includeMessageCount: false)}
            WHERE ISNULL(t.Baja, 0) = 0 AND ISNULL(e.EsCerrado, 0) = 1
              AND t.FechaHoraCierre >= @Desde
            ORDER BY t.FechaHoraCierre DESC
            OFFSET 0 ROWS FETCH NEXT {AtencionTope} ROWS ONLY;
            """;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.AddWithValue("@Desde", desde);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                cerrados.Add((rd.GetInt64(0), GetInt(rd, 1), rd.GetDateTime(2), rd.GetDateTime(3), GetString(rd, 4), GetInt(rd, 5) == 1));
        }
        if (cerrados.Count == 0)
            return;

        var config = await LeerAtencionConfigAsync(cn, ct);
        var horario = await LeerHorarioAsync(ct);
        var ids = cerrados.Select(c => c.Id).ToList();
        var datos = await LeerDatosTicketsAsync(cn, esquema, ids, ct);
        var periodos = esquema.TieneHistorial
            ? await LeerPeriodosEsperaAsync(cn, ids, TicketsAtencionCalculo.EstadosEspera(config), ct)
            : new Dictionary<long, IReadOnlyList<(DateTime Desde, DateTime? Hasta)>>();
        var clientes = await LeerClientesAsync(cn, cerrados.Select(c => c.Cliente).ToList(), ct);

        var evaluados = cerrados.Select(c =>
        {
            datos.TryGetValue(c.Id, out var d);
            clientes.TryGetValue(c.Cliente, out var cli);
            var tipo = TicketsAtencionCalculo.TipoCliente(config, cli.Clasificacion,
                string.Equals(cli.Abono, ConversacionAbonoEstados.Abonado, StringComparison.OrdinalIgnoreCase));
            return TicketsAtencionCalculo.EvaluarCerrado(new TicketAtencionDatos
            {
                Prioridad = c.Prioridad,
                FechaAlta = c.Alta,
                TieneConversacion = c.TieneConversacion,
                PrimeraRespuestaNuestra = d?.PrimeraNuestra,
                PeriodosEspera = periodos.TryGetValue(c.Id, out var p) ? p : [],
                NivelesCliente = tipo.Niveles
            }, c.Cierre, config, horario);
        }).ToList();

        var conResolucion = evaluados.Where(e => e.ResolucionEnPlazo.HasValue).ToList();
        var conRespuesta = evaluados.Where(e => e.RespuestaEnPlazo.HasValue).ToList();
        dto.CerradosMedidos = conResolucion.Count;
        dto.RespuestaMedidos = conRespuesta.Count;
        dto.CumplimientoResolucion = conResolucion.Count == 0 ? null : conResolucion.Count(e => e.ResolucionEnPlazo == true) / (double)conResolucion.Count;
        dto.CumplimientoRespuesta = conRespuesta.Count == 0 ? null : conRespuesta.Count(e => e.RespuestaEnPlazo == true) / (double)conRespuesta.Count;
        dto.HorasResolucionPromedio = Math.Round(evaluados.Average(e => e.HorasResolucion), 1);
    }

    private async Task CalcularTiempoPorEtapaAsync(SqlConnection cn, TicketsSeguimientoDto dto, DateTime desde, CancellationToken ct)
    {
        var cambios = new List<(long IdTicket, string Estado, DateTime Fecha)>();
        var nombres = new Dictionary<string, (string Nombre, int Orden)>(StringComparer.OrdinalIgnoreCase);
        const string sql = """
            SELECT MIN(FechaHora) FROM dbo.TICK_ESTADOS_HIST;

            SELECT h.IdTicket, UPPER(LTRIM(RTRIM(h.EstadoNuevo))), h.FechaHora
            FROM dbo.TICK_ESTADOS_HIST h
            WHERE h.FechaHora >= @Desde
            ORDER BY h.IdTicket, h.FechaHora, h.IdHist;

            SELECT UPPER(LTRIM(RTRIM(CodigoEstado))), ISNULL(Nombre, ''), ISNULL(Orden, 0) FROM dbo.TICK_ESTADOS;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Desde", desde);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (await rd.ReadAsync(ct) && !rd.IsDBNull(0))
            dto.HistorialDesde = rd.GetDateTime(0);
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            cambios.Add((rd.GetInt64(0), GetString(rd, 1), rd.GetDateTime(2)));
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            nombres[GetString(rd, 0)] = (GetString(rd, 1), GetInt(rd, 2));
        await rd.CloseAsync();

        var horario = await LeerHorarioAsync(ct);
        dto.TiempoPorEtapa = TicketsAtencionCalculo.TiempoPorEtapa(cambios, horario)
            .Select(t => new TicketsEtapaTiempoDto
            {
                CodigoEstado = t.Estado,
                Nombre = nombres.TryGetValue(t.Estado, out var n) ? n.Nombre : t.Estado,
                HorasPromedio = t.HorasPromedio,
                Texto = TicketsAtencionCalculo.Duracion(t.HorasPromedio, horario),
                Tramos = t.Tramos
            })
            .OrderBy(t => nombres.TryGetValue(t.CodigoEstado, out var n) ? n.Orden : 999)
            .ToList();
    }

    private static DateTime InicioSemana(DateTime fecha)
        => fecha.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));

    private static string Antiguedad(DateTime alta, DateTime ahora)
    {
        var dias = (ahora.Date - alta.Date).Days;
        return dias <= 0 ? "hoy" : dias == 1 ? "1 día" : $"{dias} días";
    }
}

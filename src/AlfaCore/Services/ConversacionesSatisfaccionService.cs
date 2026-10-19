using System.Globalization;
using AlfaCore.Models;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IConversacionesSatisfaccionService
{
    Task<SatisfaccionInformeDto> GetInformeAsync(SatisfaccionFiltros filtros, int? expectedBaseId, CancellationToken ct = default);
    Task<SatisfaccionOpcionesDto> GetOpcionesAsync(int? expectedBaseId, CancellationToken ct = default);
    byte[] ExportarExcel(SatisfaccionInformeDto informe, SatisfaccionFiltros filtros);
}

/// <summary>
/// Informe de satisfacción (2026-10-19): resultados de la encuesta que se manda al cerrar una
/// conversación de WhatsApp (CONV_ENCUESTAS). Ver ConversacionesService.Encuestas.
/// </summary>
public sealed class ConversacionesSatisfaccionService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IConversacionesSatisfaccionService
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    public async Task<SatisfaccionInformeDto> GetInformeAsync(SatisfaccionFiltros filtros, int? expectedBaseId, CancellationToken ct = default)
    {
        try
        {
            return await CargarAsync(filtros, ResolveConnectionString(expectedBaseId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var incidentId = await appEvents.LogErrorAsync("Conversaciones", "SatisfaccionInforme", ex,
                "No se pudo armar el informe de satisfacción.", null, AppEventSeverity.Error, ct);
            throw new AppUserFacingException("No se pudo armar el informe de satisfacción.", incidentId, ex);
        }
    }

    /// <summary>Personas que atendieron alguna conversación encuestada y números de WhatsApp, para los filtros.</summary>
    public async Task<SatisfaccionOpcionesDto> GetOpcionesAsync(int? expectedBaseId, CancellationToken ct = default)
    {
        var opciones = new SatisfaccionOpcionesDto();
        try
        {
            const string sql = """
                IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
                    RETURN;

                SELECT DISTINCT LTRIM(RTRIM(e.IdTecnicoAtendio)), ISNULL(NULLIF(LTRIM(RTRIM(t.Nombre)), N''), LTRIM(RTRIM(e.IdTecnicoAtendio)))
                FROM dbo.CONV_ENCUESTAS e
                LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(e.IdTecnicoAtendio))
                WHERE e.AtendidoPorAsistente = 0 AND LTRIM(RTRIM(ISNULL(e.IdTecnicoAtendio, N''))) <> N'';

                IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NOT NULL
                    SELECT n.IdNumero, n.Nombre FROM dbo.CONV_WHATSAPP_NUMEROS n
                    WHERE EXISTS (SELECT 1 FROM dbo.CONV_ENCUESTAS e WHERE e.IdNumeroWhatsApp = n.IdNumero)
                    ORDER BY n.Nombre;
                """;
            await using var cn = new SqlConnection(ResolveConnectionString(expectedBaseId));
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                opciones.Personas.Add(new SatisfaccionOpcion(rd.GetString(0), rd.GetString(1)));
            if (await rd.NextResultAsync(ct))
            {
                while (await rd.ReadAsync(ct))
                    opciones.Numeros.Add(new SatisfaccionOpcion(rd.GetInt32(0).ToString(CultureInfo.InvariantCulture), rd.GetString(1)));
            }
            opciones.Personas = opciones.Personas.OrderBy(p => p.Texto, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Los filtros son accesorios: si fallan, el informe igual se puede ver sin filtrar.
            await appEvents.LogErrorAsync("Conversaciones", "SatisfaccionOpciones", ex,
                "No se pudieron cargar los filtros del informe de satisfacción.", null, AppEventSeverity.Warning, ct);
        }
        return opciones;
    }

    private string ResolveConnectionString(int? expectedBaseId)
    {
        var active = sessionService.GetActiveSession();
        if (expectedBaseId is > 0 && active?.BaseId != expectedBaseId.Value)
            throw new InvalidOperationException("La sesión activa no coincide con la base solicitada para el informe de satisfacción.");

        if (active is not null)
        {
            return new SqlConnectionStringBuilder
            {
                DataSource = active.Servidor,
                InitialCatalog = active.BaseDatos,
                UserID = active.Usuario,
                Password = active.Password,
                TrustServerCertificate = active.TrustServerCertificate,
                ApplicationName = "AlfaCore"
            }.ConnectionString;
        }

        return configuration.GetConnectionString("AlfaGestion")
            ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");
    }

    private static async Task<SatisfaccionInformeDto> CargarAsync(SatisfaccionFiltros filtros, string connectionString, CancellationToken ct)
    {
        var informe = new SatisfaccionInformeDto();
        const string sql = """
            IF OBJECT_ID(N'dbo.CONV_ENCUESTAS', N'U') IS NULL
                RETURN;

            SELECT e.IdEncuesta, e.IdConversacion, e.Estado, e.FechaEnvio, e.FechaRespuesta, e.Calificacion,
                   ISNULL(e.Comentario, N''), ISNULL(e.AtendidoPorAsistente, 0),
                   LTRIM(RTRIM(ISNULL(e.IdTecnicoAtendio, N''))), ISNULL(t.Nombre, N''),
                   ISNULL(NULLIF(LTRIM(RTRIM(c.NombreVisible)), N''), ISNULL(c.TelefonoWhatsApp, N''))
            FROM dbo.CONV_ENCUESTAS e
            INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = e.IdConversacion
            LEFT JOIN dbo.V_TA_Tecnicos t ON LTRIM(RTRIM(t.IdTecnico)) = LTRIM(RTRIM(e.IdTecnicoAtendio))
            WHERE e.Estado <> N'ERROR'
              AND e.FechaEnvio >= @Desde AND e.FechaEnvio < @Hasta
              AND (@IdNumero IS NULL OR e.IdNumeroWhatsApp = @IdNumero)
              AND (@Atendio IS NULL
                   OR (@Atendio = N'ASISTENTE' AND e.AtendidoPorAsistente = 1)
                   OR (@Atendio <> N'ASISTENTE' AND e.AtendidoPorAsistente = 0 AND LTRIM(RTRIM(ISNULL(e.IdTecnicoAtendio, N''))) = @Atendio))
            ORDER BY e.FechaEnvio;
            """;

        var filas = new List<(long Id, long Conv, DateTime Envio, DateTime? Respuesta, int? Calif, string Comentario, string Clave, string Nombre, string Cliente)>();
        await using (var cn = new SqlConnection(connectionString))
        {
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@Desde", filtros.Desde.Date);
            cmd.Parameters.AddWithValue("@Hasta", filtros.Hasta.Date.AddDays(1));
            cmd.Parameters.AddWithValue("@IdNumero", (object?)filtros.IdNumeroWhatsApp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Atendio", string.IsNullOrWhiteSpace(filtros.Atendio) ? DBNull.Value : filtros.Atendio.Trim());
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var asistente = rd.GetBoolean(7);
                var idTecnico = rd.GetString(8);
                var (clave, nombre) = asistente
                    ? (SatisfaccionFiltros.AtendioAsistente, "Asistente IA")
                    : idTecnico.Length == 0 ? (string.Empty, "Sin asignar") : (idTecnico, rd.GetString(9).Length > 0 ? rd.GetString(9) : idTecnico);
                filas.Add((rd.GetInt64(0), rd.GetInt64(1), rd.GetDateTime(3),
                    rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                    rd.IsDBNull(5) ? null : Convert.ToInt32(rd.GetValue(5), CultureInfo.InvariantCulture),
                    rd.GetString(6), clave, nombre, rd.GetString(10)));
            }
        }

        informe.Enviadas = filas.Count;
        var respondidas = filas.Where(f => f.Calif.HasValue).ToList();
        informe.Respondidas = respondidas.Count;
        informe.MuyBien = respondidas.Count(f => f.Calif == ConversacionEncuestaCalificaciones.MuyBien);
        informe.Regular = respondidas.Count(f => f.Calif == ConversacionEncuestaCalificaciones.Regular);
        informe.Mal = respondidas.Count(f => f.Calif == ConversacionEncuestaCalificaciones.Mal);
        informe.ConComentario = respondidas.Count(f => f.Comentario.Length > 0);

        informe.PorPersona = filas
            .GroupBy(f => (f.Clave, f.Nombre))
            .Select(g => new SatisfaccionPorPersonaDto
            {
                Clave = g.Key.Clave,
                Nombre = g.Key.Nombre,
                Enviadas = g.Count(),
                Respondidas = g.Count(f => f.Calif.HasValue),
                MuyBien = g.Count(f => f.Calif == ConversacionEncuestaCalificaciones.MuyBien),
                Regular = g.Count(f => f.Calif == ConversacionEncuestaCalificaciones.Regular),
                Mal = g.Count(f => f.Calif == ConversacionEncuestaCalificaciones.Mal)
            })
            .OrderByDescending(p => p.Respondidas > 0)
            .ThenByDescending(p => p.PorcentajeSatisfechos)
            .ThenByDescending(p => p.Respondidas)
            .ToList();

        informe.PorSemana = respondidas
            .GroupBy(f => InicioSemana(f.Respuesta!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new SatisfaccionSemanaDto
            {
                Desde = g.Key,
                Respondidas = g.Count(),
                MuyBien = g.Count(f => f.Calif == ConversacionEncuestaCalificaciones.MuyBien),
                Mal = g.Count(f => f.Calif == ConversacionEncuestaCalificaciones.Mal)
            })
            .ToList();

        // Primero las malas, después por fecha: lo que hay que mirar queda arriba.
        informe.Respuestas = respondidas
            .OrderBy(f => f.Calif)
            .ThenByDescending(f => f.Respuesta)
            .Take(300)
            .Select(f => new SatisfaccionRespuestaDto
            {
                IdEncuesta = f.Id,
                IdConversacion = f.Conv,
                FechaRespuesta = f.Respuesta!.Value,
                Cliente = f.Cliente,
                Atendio = f.Nombre,
                Calificacion = f.Calif!.Value,
                Comentario = f.Comentario
            })
            .ToList();

        return informe;
    }

    internal static DateTime InicioSemana(DateTime fecha)
    {
        var dia = (7 + (int)fecha.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return fecha.Date.AddDays(-dia);
    }

    public byte[] ExportarExcel(SatisfaccionInformeDto informe, SatisfaccionFiltros filtros)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Satisfacción");
        ws.Cell(1, 1).Value = $"Satisfacción de clientes — {filtros.Desde:dd/MM/yyyy} al {filtros.Hasta:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Enviadas {informe.Enviadas} · Respondidas {informe.Respondidas} ({informe.PorcentajeRespuesta.ToString("0.#", EsAr)}%) · " +
                              $"Satisfechos {informe.PorcentajeSatisfechos.ToString("0.#", EsAr)}% · Muy bien {informe.MuyBien} · Regular {informe.Regular} · Mal {informe.Mal}";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

        var headers = new[] { "Fecha", "Cliente", "Atendió", "Calificación", "Comentario" };
        const int headerRow = 4;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f2942");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = headerRow + 1;
        foreach (var r in informe.Respuestas.OrderByDescending(x => x.FechaRespuesta))
        {
            ws.Cell(row, 1).Value = r.FechaRespuesta;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            ws.Cell(row, 2).Value = r.Cliente;
            ws.Cell(row, 3).Value = r.Atendio;
            ws.Cell(row, 4).Value = ConversacionEncuestaCalificaciones.Etiqueta(r.Calificacion);
            ws.Cell(row, 5).Value = r.Comentario;
            row++;
        }

        var ws2 = wb.Worksheets.Add("Por persona");
        var h2 = new[] { "Atendió", "Enviadas", "Respondidas", "Muy bien", "Regular", "Mal", "% satisfechos" };
        for (var c = 0; c < h2.Length; c++)
        {
            ws2.Cell(1, c + 1).Value = h2[c];
            ws2.Cell(1, c + 1).Style.Font.Bold = true;
        }
        var r2 = 2;
        foreach (var p in informe.PorPersona)
        {
            ws2.Cell(r2, 1).Value = p.Nombre;
            ws2.Cell(r2, 2).Value = p.Enviadas;
            ws2.Cell(r2, 3).Value = p.Respondidas;
            ws2.Cell(r2, 4).Value = p.MuyBien;
            ws2.Cell(r2, 5).Value = p.Regular;
            ws2.Cell(r2, 6).Value = p.Mal;
            ws2.Cell(r2, 7).Value = p.PorcentajeSatisfechos / 100.0;
            ws2.Cell(r2, 7).Style.NumberFormat.Format = "0.0%";
            r2++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(5).Width = Math.Min(ws.Column(5).Width, 80);
        ws2.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string NombreArchivo(SatisfaccionFiltros filtros)
        => $"satisfaccion_{filtros.Desde:yyyyMMdd}_{filtros.Hasta:yyyyMMdd}.xlsx";
}

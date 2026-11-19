using System.Globalization;
using System.Net;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface ITicketInformesService
{
    bool IaDisponible { get; }
    Task<IReadOnlyList<TicketInformeDto>> ListarAsync(long idTicket, CancellationToken ct = default);
    Task<int> GuardarAsync(TicketInformeGuardarRequest request, CancellationToken ct = default);
    Task EliminarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default);
    /// <summary>Publica el informe y devuelve el link público (siempre el mismo mientras no se despublique).</summary>
    Task<string> PublicarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default);
    Task DespublicarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default);
    Task<string?> ObtenerLinkAsync(int idInforme, TicketInformeDestino destino, CancellationToken ct = default);
    /// <summary>Borrador armado con la plantilla y los datos del ticket, sin IA.</summary>
    Task<string> PlantillaAsync(long idTicket, CancellationToken ct = default);
    /// <summary>Borrador redactado por la IA con la descripción, actividad, mensajes y horas del ticket.</summary>
    Task<string> GenerarConIaAsync(long idTicket, string? indicaciones, CancellationToken ct = default);
    /// <summary>Informe publicado para la página pública (null si no existe o no está publicado).</summary>
    Task<TicketInformePublicoDto?> ObtenerPublicoAsync(int idInforme, CancellationToken ct = default);
    /// <summary>Anota una apertura del link público.</summary>
    Task RegistrarVistaAsync(int idInforme, string? navegador, CancellationToken ct = default);
    /// <summary>PDF del informe (A4). Con soloPublicado, solo si está publicado (para la página pública).</summary>
    Task<byte[]> PdfAsync(int idInforme, bool soloPublicado = false, CancellationToken ct = default);
    /// <summary>Contacto, mail, teléfono y conversación sugeridos para mandar el informe publicado.</summary>
    Task<TicketInformeEnvioDto> DatosEnvioAsync(int idInforme, TicketInformeDestino destino, CancellationToken ct = default);
    Task EnviarMailAsync(TicketInformeMailRequest request, TicketInformeDestino destino, CancellationToken ct = default);
    /// <summary>Manda el link por la conversación de WhatsApp del ticket (requiere la ventana de 24 h abierta).</summary>
    Task EnviarWhatsAppAsync(int idInforme, string texto, string usuario, TicketInformeDestino destino, CancellationToken ct = default);
    /// <summary>Deja en la actividad que se abrió WhatsApp con el link (envío desde el teléfono o WhatsApp Web).</summary>
    Task RegistrarEnvioManualAsync(int idInforme, string canal, string destinatario, string usuario, CancellationToken ct = default);
    Task<IReadOnlyList<TicketInformeRespuestaDto>> RespuestasAsync(int idInforme, CancellationToken ct = default);
    /// <summary>Respuesta del cliente desde la página pública: CONFORME o CONSULTA.</summary>
    Task ResponderPublicoAsync(int idInforme, string tipo, string? nombre, string? texto, CancellationToken ct = default);
}

/// <summary>
/// Informes al cliente de los tickets (TICK_INFORMES, update 2026-11-19-061). El HTML se limpia al guardar y al mostrar.
/// El link público es ALFA_PUBLIC_LINK tipo INFORME con IdReferencia = IdInforme.
/// </summary>
public sealed class TicketInformesService(
    IConfiguration configuration,
    ISessionService sessionService,
    ICentralPublicLinkService publicLinks,
    IConversacionAnalisisService ia,
    IDocumentPdfService? pdfService = null,
    CorreoSalienteService? correo = null,
    IConversacionesService? conversaciones = null) : ITicketInformesService
{
    private const int Timeout = 60;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public bool IaDisponible => ia.IsConfigured;

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL OR OBJECT_ID(N'dbo.TICK_INFORME_RESPUESTAS', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0)
        {
            await cn.DisposeAsync();
            throw new InvalidOperationException("Esta base todavía no tiene los informes de tickets. Corré las actualizaciones de AlfaCore.");
        }
        return cn;
    }

    public async Task<IReadOnlyList<TicketInformeDto>> ListarAsync(long idTicket, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        const string sql = """
            SELECT i.IdInforme, i.IdTicket, i.Titulo, ISNULL(i.Html, N'') AS Html, i.Estado, i.GeneradoIA, ISNULL(i.UsuarioAlta, N'') AS UsuarioAlta, i.FechaHoraAlta,
                   ISNULL(i.UsuarioModificacion, N'') AS UsuarioModificacion, i.FechaHoraModificacion, i.FechaHoraPublicacion,
                   (SELECT COUNT(*) FROM dbo.TICK_INFORME_VISTAS v WHERE v.IdInforme = i.IdInforme) AS Vistas,
                   (SELECT MAX(v.FechaHora) FROM dbo.TICK_INFORME_VISTAS v WHERE v.IdInforme = i.IdInforme) AS UltimaVista,
                   ISNULL(r.Tipo, '') AS UltimaRespuestaTipo, ISNULL(r.Nombre, N'') AS UltimaRespuestaNombre, r.FechaHora AS UltimaRespuestaFecha
            FROM dbo.TICK_INFORMES i
            OUTER APPLY (SELECT TOP (1) x.Tipo, x.Nombre, x.FechaHora FROM dbo.TICK_INFORME_RESPUESTAS x WHERE x.IdInforme = i.IdInforme ORDER BY x.FechaHora DESC) r
            WHERE i.IdTicket = @IdTicket AND i.Baja = 0
            ORDER BY i.FechaHoraAlta DESC, i.IdInforme DESC;
            """;
        return (await cn.QueryAsync<TicketInformeDto>(new CommandDefinition(sql, new { IdTicket = idTicket }, commandTimeout: Timeout, cancellationToken: ct))).ToList();
    }

    public async Task<int> GuardarAsync(TicketInformeGuardarRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var titulo = (request.Titulo ?? string.Empty).Trim();
        if (titulo.Length == 0) throw new InvalidOperationException("Poné un título al informe.");
        if (titulo.Length > 200) titulo = titulo[..200];
        var html = InformeHtmlSanitizer.Limpiar(request.Html);
        if (InformeHtmlSanitizer.ATexto(html).Length == 0) throw new InvalidOperationException("El informe está vacío.");
        var usuario = Usuario(request.Usuario);

        await using var cn = await AbrirAsync(ct);
        if (request.IdInforme is > 0)
        {
            var n = await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.TICK_INFORMES SET Titulo = @Titulo, Html = @Html, GeneradoIA = CASE WHEN @GeneradoIA = 1 THEN 1 ELSE GeneradoIA END,
                       UsuarioModificacion = @Usuario, FechaHoraModificacion = GETDATE()
                WHERE IdInforme = @IdInforme AND IdTicket = @IdTicket AND Baja = 0;
                """, new { request.IdInforme, request.IdTicket, Titulo = titulo, Html = html, request.GeneradoIA, Usuario = usuario }, cancellationToken: ct));
            if (n == 0) throw new InvalidOperationException("El informe ya no existe.");
            return request.IdInforme.Value;
        }

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.TICK_INFORMES (IdTicket, Titulo, Html, Estado, GeneradoIA, UsuarioAlta, FechaHoraAlta)
            VALUES (@IdTicket, @Titulo, @Html, 'BORRADOR', @GeneradoIA, @Usuario, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { request.IdTicket, Titulo = titulo, Html = html, request.GeneradoIA, Usuario = usuario }, tx, cancellationToken: ct));
        await ActividadAsync(cn, tx, request.IdTicket, $"Informe al cliente creado: {titulo}", usuario, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task EliminarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var info = await LeerAsync(cn, idInforme, ct);
        await RevocarLinkAsync(idInforme, destino, ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.TICK_INFORMES SET Baja = 1, Estado = 'BORRADOR', UsuarioModificacion = @Usuario, FechaHoraModificacion = GETDATE() WHERE IdInforme = @IdInforme;",
            new { IdInforme = idInforme, Usuario = Usuario(usuario) }, tx, cancellationToken: ct));
        await ActividadAsync(cn, tx, info.IdTicket, $"Informe al cliente eliminado: {info.Titulo}", Usuario(usuario), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string> PublicarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default)
    {
        Validar(destino);
        await using var cn = await AbrirAsync(ct);
        var info = await LeerAsync(cn, idInforme, ct);
        var link = await publicLinks.GetOrCreateAsync(destino.IdWeb, destino.IdBase, PublicLinkTipos.Informe, idInforme, "informe-" + info.Titulo, ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TICK_INFORMES SET Estado = 'PUBLICADO', FechaHoraPublicacion = ISNULL(FechaHoraPublicacion, GETDATE()),
                   UsuarioModificacion = @Usuario, FechaHoraModificacion = GETDATE()
            WHERE IdInforme = @IdInforme;
            """, new { IdInforme = idInforme, Usuario = Usuario(usuario) }, tx, cancellationToken: ct));
        if (!info.Publicado)
            await ActividadAsync(cn, tx, info.IdTicket, $"Informe al cliente publicado: {info.Titulo}", Usuario(usuario), ct);
        await tx.CommitAsync(ct);
        return Url(destino, link);
    }

    public async Task DespublicarAsync(int idInforme, string usuario, TicketInformeDestino destino, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var info = await LeerAsync(cn, idInforme, ct);
        await RevocarLinkAsync(idInforme, destino, ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.TICK_INFORMES SET Estado = 'BORRADOR', UsuarioModificacion = @Usuario, FechaHoraModificacion = GETDATE() WHERE IdInforme = @IdInforme;",
            new { IdInforme = idInforme, Usuario = Usuario(usuario) }, tx, cancellationToken: ct));
        await ActividadAsync(cn, tx, info.IdTicket, $"Informe al cliente despublicado (el link anterior ya no funciona): {info.Titulo}", Usuario(usuario), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string?> ObtenerLinkAsync(int idInforme, TicketInformeDestino destino, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destino.IdWeb) || destino.IdBase <= 0) return null;
        var link = await publicLinks.TryGetExistingAsync(destino.IdWeb, destino.IdBase, PublicLinkTipos.Informe, idInforme, ct);
        return link is { Activo: true } ? Url(destino, link) : null;
    }

    private async Task RevocarLinkAsync(int idInforme, TicketInformeDestino destino, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destino.IdWeb) || destino.IdBase <= 0) return;
        if (await publicLinks.TryGetExistingAsync(destino.IdWeb, destino.IdBase, PublicLinkTipos.Informe, idInforme, ct) is { } link)
            await publicLinks.RevokeAsync(link.IdPublicLink, ct);
    }

    public async Task<string> PlantillaAsync(long idTicket, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        return Plantilla(await LeerContextoAsync(cn, idTicket, ct));
    }

    public async Task<string> GenerarConIaAsync(long idTicket, string? indicaciones, CancellationToken ct = default)
    {
        if (!ia.IsConfigured) throw new InvalidOperationException("La IA no está configurada en este servidor.");
        await using var cn = await AbrirAsync(ct);
        var contexto = await LeerContextoAsync(cn, idTicket, ct);
        var html = await ia.GenerarTextoAsync(PromptSistema, ContenidoParaIa(contexto, indicaciones), IaUsoFunciones.InformeTicket, 120, ModeloInformes(), ct);
        var limpio = InformeHtmlSanitizer.Limpiar(QuitarCercoDeCodigo(html));
        if (InformeHtmlSanitizer.ATexto(limpio).Length == 0)
            throw new InvalidOperationException("La IA no devolvió un informe. Probá de nuevo en unos segundos.");
        return limpio;
    }

    public async Task<TicketInformePublicoDto?> ObtenerPublicoAsync(int idInforme, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var r = await cn.QueryFirstOrDefaultAsync<TicketInformePublicoDto>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TICK_INFORMES', N'U') IS NULL
                SELECT TOP (0) 1 AS IdInforme;
            ELSE
                SELECT i.IdInforme, i.Titulo, ISNULL(i.Html, N'') AS Html, t.Numero AS NumeroTicket, t.Titulo AS TituloTicket,
                       ISNULL((SELECT TOP (1) c.RAZON_SOCIAL FROM dbo.VT_CLIENTES c WHERE LTRIM(RTRIM(c.CODIGO)) = LTRIM(RTRIM(t.ClienteCodigo))), N'') AS Cliente,
                       i.FechaHoraPublicacion, i.FechaHoraModificacion
                FROM dbo.TICK_INFORMES i
                INNER JOIN dbo.TICK_TICKETS t ON t.IdTicket = i.IdTicket
                WHERE i.IdInforme = @IdInforme AND i.Baja = 0 AND i.Estado = 'PUBLICADO';
            """, new { IdInforme = idInforme }, commandTimeout: Timeout, cancellationToken: ct));
        if (r is null) return null;
        r.Html = InformeHtmlSanitizer.Limpiar(r.Html);
        return r;
    }

    public async Task RegistrarVistaAsync(int idInforme, string? navegador, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var nav = (navegador ?? string.Empty).Trim();
        await cn.ExecuteAsync(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.TICK_INFORME_VISTAS', N'U') IS NOT NULL
                INSERT INTO dbo.TICK_INFORME_VISTAS (IdInforme, FechaHora, Navegador) VALUES (@IdInforme, GETDATE(), @Navegador);
            """, new { IdInforme = idInforme, Navegador = nav.Length > 300 ? nav[..300] : nav }, cancellationToken: ct));
    }

    // ---- PDF, envíos y respuestas (etapa 2) ----

    public async Task<byte[]> PdfAsync(int idInforme, bool soloPublicado = false, CancellationToken ct = default)
    {
        if (pdfService is null) throw new InvalidOperationException("La generación de PDF no está disponible en este servidor.");
        var doc = await DocumentoAsync(idInforme, soloPublicado, ct) ?? throw new InvalidOperationException("El informe no existe o no está publicado.");
        return await pdfService.GenerateAsync(InformeDocumento.Html(doc), new DocumentPdfFooterOptions(true, true, "Alfa Gestión · Soporte"), ct);
    }

    private async Task<TicketInformePublicoDto?> DocumentoAsync(int idInforme, bool soloPublicado, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var r = await cn.QueryFirstOrDefaultAsync<TicketInformePublicoDto>(new CommandDefinition($"""
            SELECT i.IdInforme, i.Titulo, ISNULL(i.Html, N'') AS Html, t.Numero AS NumeroTicket, t.Titulo AS TituloTicket,
                   ISNULL((SELECT TOP (1) c.RAZON_SOCIAL FROM dbo.VT_CLIENTES c WHERE LTRIM(RTRIM(c.CODIGO)) = LTRIM(RTRIM(t.ClienteCodigo))), N'') AS Cliente,
                   i.FechaHoraPublicacion, i.FechaHoraModificacion
            FROM dbo.TICK_INFORMES i INNER JOIN dbo.TICK_TICKETS t ON t.IdTicket = i.IdTicket
            WHERE i.IdInforme = @IdInforme AND i.Baja = 0 {(soloPublicado ? "AND i.Estado = 'PUBLICADO'" : "")};
            """, new { IdInforme = idInforme }, commandTimeout: Timeout, cancellationToken: ct));
        if (r is not null) r.Html = InformeHtmlSanitizer.Limpiar(r.Html);
        return r;
    }

    public async Task<TicketInformeEnvioDto> DatosEnvioAsync(int idInforme, TicketInformeDestino destino, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var d = await cn.QueryFirstOrDefaultAsync<TicketInformeEnvioDto>(new CommandDefinition("""
            SELECT t.Numero AS NumeroTicket, i.Titulo AS TituloInforme, t.IdConversacion,
                   ISNULL(cli.RAZON_SOCIAL, N'') AS Cliente, ISNULL(co.Nombre_y_Apellido, N'') AS Contacto,
                   COALESCE(NULLIF(LTRIM(RTRIM(co.Email)), N''), NULLIF(LTRIM(RTRIM(cli.MAIL)), N''), N'') AS Mail,
                   COALESCE(NULLIF(LTRIM(RTRIM(co.Celular)), N''), NULLIF(LTRIM(RTRIM(co.Telefono)), N''), NULLIF(LTRIM(RTRIM(cli.TELEFONO)), N''), N'') AS Telefono
            FROM dbo.TICK_INFORMES i
            INNER JOIN dbo.TICK_TICKETS t ON t.IdTicket = i.IdTicket
            LEFT JOIN dbo.MA_CONTACTOS co ON co.id = t.IdContacto
            OUTER APPLY (SELECT TOP (1) c.RAZON_SOCIAL, c.MAIL, c.TELEFONO FROM dbo.VT_CLIENTES c WHERE LTRIM(RTRIM(c.CODIGO)) = LTRIM(RTRIM(t.ClienteCodigo))) cli
            WHERE i.IdInforme = @IdInforme AND i.Baja = 0;
            """, new { IdInforme = idInforme }, commandTimeout: Timeout, cancellationToken: ct))
            ?? throw new InvalidOperationException("El informe ya no existe.");
        d.Link = await ObtenerLinkAsync(idInforme, destino, ct) ?? string.Empty;
        if (d.IdConversacion is > 0 && conversaciones is not null)
        {
            try
            {
                var v = await conversaciones.GetWhatsAppSendWindowAsync(d.IdConversacion.Value, ct: ct);
                d.WhatsAppDisponible = v.Activa;
                d.WhatsAppVence = v.Vencimiento;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { d.WhatsAppDisponible = false; }
        }
        return d;
    }

    public async Task EnviarMailAsync(TicketInformeMailRequest request, TicketInformeDestino destino, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (correo is null) throw new InvalidOperationException("El envío de mails no está disponible en este servidor.");
        var (validos, invalidos) = CorreoSalienteService.SepararMails(request.Para);
        if (validos.Count == 0) throw new InvalidOperationException("Indicá al menos un mail válido.");
        if (invalidos.Count > 0) throw new InvalidOperationException("Revisá estos mails: " + string.Join(", ", invalidos));
        var link = await ObtenerLinkAsync(request.IdInforme, destino, ct) ?? throw new InvalidOperationException("Publicá el informe antes de enviarlo.");
        var doc = await DocumentoAsync(request.IdInforme, soloPublicado: true, ct) ?? throw new InvalidOperationException("El informe no está publicado.");
        var adjuntos = new List<CorreoSalienteService.Adjunto>();
        if (request.AdjuntarPdf && pdfService is not null)
            adjuntos.Add(new CorreoSalienteService.Adjunto(NombreArchivo(doc), await pdfService.GenerateAsync(InformeDocumento.Html(doc), new DocumentPdfFooterOptions(true, true, "Alfa Gestión · Soporte"), ct)));
        var asunto = string.IsNullOrWhiteSpace(request.Asunto) ? $"Informe del ticket #{doc.NumeroTicket}: {doc.Titulo}" : request.Asunto.Trim();
        var usuario = Usuario(request.Usuario);

        await using var cn = await AbrirAsync(ct);
        var cuenta = await correo.ResolverAsync(cn, usuario, ct);
        await correo.EnviarAsync(cn, cuenta, validos, asunto, InformeDocumento.CuerpoMail(request.Mensaje, link, doc.Titulo, adjuntos.Count > 0), adjuntos, ct);
        await ActividadInformeAsync(cn, request.IdInforme, $"Informe enviado por mail a {string.Join(", ", validos)}: {doc.Titulo}", usuario, ct);
    }

    public async Task EnviarWhatsAppAsync(int idInforme, string texto, string usuario, TicketInformeDestino destino, CancellationToken ct = default)
    {
        if (conversaciones is null) throw new InvalidOperationException("WhatsApp no está disponible en este servidor.");
        var datos = await DatosEnvioAsync(idInforme, destino, ct);
        if (string.IsNullOrEmpty(datos.Link)) throw new InvalidOperationException("Publicá el informe antes de enviarlo.");
        if (datos.IdConversacion is not > 0) throw new InvalidOperationException("El ticket no tiene una conversación de WhatsApp vinculada. Usá Abrir en WhatsApp.");
        if (!datos.WhatsAppDisponible) throw new InvalidOperationException("Pasaron más de 24 horas desde el último mensaje del cliente: WhatsApp no deja mandar texto libre. Usá Abrir en WhatsApp o una plantilla desde Conversaciones.");
        var mensaje = (texto ?? string.Empty).Trim();
        if (!mensaje.Contains(datos.Link, StringComparison.Ordinal)) mensaje = (mensaje + "\n" + datos.Link).Trim();
        await conversaciones.SendMessageAsync(new ConversacionSendMessageRequest { IdConversacion = datos.IdConversacion.Value, Texto = mensaje, UsuarioAccion = Usuario(usuario), SistemaAccion = "INFORME_TICKET" }, ct);
        await using var cn = await AbrirAsync(ct);
        await ActividadInformeAsync(cn, idInforme, $"Informe enviado por WhatsApp (conversación del ticket): {datos.TituloInforme}", Usuario(usuario), ct);
    }

    public async Task RegistrarEnvioManualAsync(int idInforme, string canal, string destinatario, string usuario, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var dest = (destinatario ?? string.Empty).Trim();
        await ActividadInformeAsync(cn, idInforme, $"Se abrió {canal} para enviar el informe{(dest.Length > 0 ? " a " + dest : "")}.", Usuario(usuario), ct);
    }

    public async Task<IReadOnlyList<TicketInformeRespuestaDto>> RespuestasAsync(int idInforme, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        return (await cn.QueryAsync<TicketInformeRespuestaDto>(new CommandDefinition(
            "SELECT IdRespuesta, IdInforme, Tipo, ISNULL(Nombre, N'') AS Nombre, ISNULL(Texto, N'') AS Texto, FechaHora FROM dbo.TICK_INFORME_RESPUESTAS WHERE IdInforme = @IdInforme ORDER BY FechaHora DESC;",
            new { IdInforme = idInforme }, cancellationToken: ct))).ToList();
    }

    public async Task ResponderPublicoAsync(int idInforme, string tipo, string? nombre, string? texto, CancellationToken ct = default)
    {
        var t = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (t is not (TicketInformeRespuestaTipos.Conforme or TicketInformeRespuestaTipos.Consulta)) throw new InvalidOperationException("Respuesta no válida.");
        var n = Recortar((nombre ?? string.Empty).Trim(), 120);
        var x = (texto ?? string.Empty).Trim();
        if (x.Length > 2000) x = x[..2000];
        if (t == TicketInformeRespuestaTipos.Consulta && x.Length == 0) throw new InvalidOperationException("Escribí tu consulta.");
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var idTicket = await cn.ExecuteScalarAsync<long?>(new CommandDefinition("""
            SELECT IdTicket FROM dbo.TICK_INFORMES WHERE IdInforme = @IdInforme AND Baja = 0 AND Estado = 'PUBLICADO';
            """, new { IdInforme = idInforme }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("El informe ya no está publicado.");
        // Freno simple contra repeticiones: hasta 10 respuestas por informe por hora.
        var recientes = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.TICK_INFORME_RESPUESTAS WHERE IdInforme = @IdInforme AND FechaHora > DATEADD(hour, -1, GETDATE());", new { IdInforme = idInforme }, tx, cancellationToken: ct));
        if (recientes >= 10) throw new InvalidOperationException("Ya recibimos varias respuestas a este informe. Si necesitás algo más, escribinos por el canal habitual.");
        await cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.TICK_INFORME_RESPUESTAS (IdInforme, Tipo, Nombre, Texto, FechaHora) VALUES (@IdInforme, @Tipo, @Nombre, @Texto, GETDATE());",
            new { IdInforme = idInforme, Tipo = t, Nombre = n, Texto = x }, tx, cancellationToken: ct));
        var quien = n.Length > 0 ? n : "El cliente";
        var descripcion = t == TicketInformeRespuestaTipos.Conforme
            ? $"{quien} marcó el informe como conforme{(x.Length > 0 ? ": " + x : ".")}"
            : $"{quien} hizo una consulta sobre el informe: {x}";
        await ActividadAsync(cn, tx, idTicket, descripcion, "Cliente (informe)", ct);
        await tx.CommitAsync(ct);
    }

    private static async Task ActividadInformeAsync(SqlConnection cn, int idInforme, string texto, string usuario, CancellationToken ct)
    {
        var idTicket = await cn.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT IdTicket FROM dbo.TICK_INFORMES WHERE IdInforme = @IdInforme;", new { IdInforme = idInforme }, cancellationToken: ct));
        if (idTicket is null) return;
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await ActividadAsync(cn, tx, idTicket.Value, texto, usuario, ct);
        await tx.CommitAsync(ct);
    }

    internal static string NombreArchivo(TicketInformePublicoDto doc)
    {
        var baseNombre = new string($"Informe ticket {doc.NumeroTicket} {doc.Titulo}".Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' ? c : ' ').ToArray());
        return string.Join('-', baseNombre.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant() is { Length: > 0 } n ? (n.Length > 80 ? n[..80] : n) + ".pdf" : "informe.pdf";
    }

    // ---- contexto, plantilla e IA ----

    /// <summary>Variable de entorno con el modelo de los informes (ej. gpt-5.6). Vacía: se usa OPENAI_MODEL como el resto de la IA.</summary>
    internal const string VariableModeloInformes = "OPENAI_MODEL_INFORMES";

    internal static string? ModeloInformes()
        => Environment.GetEnvironmentVariable(VariableModeloInformes) is { Length: > 0 } m && !string.IsNullOrWhiteSpace(m) ? m.Trim() : null;

    internal const string PromptSistema = """
        Redactás el informe que una empresa de software de gestión (Alfa Gestión) le entrega a su cliente sobre un ticket de soporte.
        Con los datos del ticket que te paso, escribí un informe claro, prolijo y tranquilizador, en español rioplatense formal (usá "usted" o forma impersonal).
        Reglas:
        - No inventes nada: usá solo hechos que estén en los datos. Si algo no está claro, no lo afirmes.
        - El lector no es técnico: no menciones nombres de tablas, campos, scripts, rutas de archivos de servidor, ids internos ni usuarios de base de datos. Sí podés nombrar comprobantes, artículos, montos y fechas.
        - Sé honesto con lo pendiente: decí qué falta y quién lo tiene que hacer, sin alarmar.
        - Estructura sugerida (adaptala a lo que haya): un párrafo de estado al principio; "Qué pasó"; "Qué se hizo"; "Estado actual"; "Pendientes" o "Próximos pasos". Usá tablas solo si hay varios ítems con los mismos datos.
        - Formato: devolvé SOLO un fragmento HTML (sin <html>, <head>, <body>, estilos ni bloques de código) usando únicamente: h2, h3, p, strong, em, ul, ol, li, table, thead, tbody, tr, th, td, hr.
        - No agregues saludos ni firma; la página ya los tiene.
        """;

    internal static string ContenidoParaIa(TicketInformeContexto c, string? indicaciones)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ticket #{c.Numero}: {c.Titulo}");
        sb.AppendLine($"Cliente: {c.Cliente}");
        sb.AppendLine($"Estado: {c.Estado} · Alta: {c.FechaHoraAlta:dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(indicaciones)) sb.AppendLine().AppendLine("Indicaciones de quien pide el informe: " + indicaciones.Trim());
        sb.AppendLine().AppendLine("== Descripción del ticket ==").AppendLine(Recortar(InformeHtmlSanitizer.ATexto(c.Descripcion), 12000));
        if (c.Actividad.Count > 0)
        {
            sb.AppendLine().AppendLine("== Actividad (notas y cambios) ==");
            foreach (var a in c.Actividad) sb.AppendLine($"{a.Fecha:dd/MM/yyyy HH:mm} [{a.Tipo}] {Recortar(a.Texto, 800)}");
        }
        if (c.Mensajes.Count > 0)
        {
            sb.AppendLine().AppendLine("== Mensajes con el cliente ==");
            foreach (var m in c.Mensajes) sb.AppendLine($"{m.Fecha:dd/MM/yyyy HH:mm} {(m.Direccion.StartsWith("IN", StringComparison.OrdinalIgnoreCase) ? "Cliente" : "Soporte")}: {Recortar(m.Texto, 800)}");
        }
        if (c.Partes.Count > 0)
        {
            sb.AppendLine().AppendLine("== Trabajo registrado ==");
            foreach (var p in c.Partes) sb.AppendLine($"{p.Fecha:dd/MM/yyyy} {p.Minutos} min: {Recortar(p.Texto, 400)}");
        }
        return Recortar(sb.ToString(), 40000);
    }

    internal static string Plantilla(TicketInformeContexto c)
    {
        static string E(string s) => InformeHtmlSanitizer.Codificar(s);
        var descripcion = InformeHtmlSanitizer.PareceHtml(c.Descripcion) ? InformeHtmlSanitizer.Limpiar(c.Descripcion) : InformeHtmlSanitizer.TextoAHtml(c.Descripcion);
        var sb = new StringBuilder();
        sb.Append("<p><strong>Estado:</strong> ").Append(E(c.Estado)).Append(". ").Append("Completar en una o dos frases cómo está el tema hoy.</p>");
        sb.Append("<h2>Qué pasó</h2>");
        sb.Append(descripcion.Length > 0 ? descripcion : "<p>Describir el problema que se informó.</p>");
        sb.Append("<h2>Qué se hizo</h2><ul>");
        var hechos = c.Actividad.Where(a => a.Tipo is "NOTA").Select(a => a.Texto).Concat(c.Partes.Select(p => p.Texto)).Where(t => !string.IsNullOrWhiteSpace(t)).Take(10).ToList();
        if (hechos.Count == 0) sb.Append("<li>Detallar las acciones realizadas.</li>");
        foreach (var h in hechos) sb.Append("<li>").Append(E(h.Trim())).Append("</li>");
        sb.Append("</ul><h2>Estado actual</h2><p>Indicar si quedó resuelto y cómo se verificó.</p>");
        sb.Append("<h2>Próximos pasos</h2><ul><li>Indicar qué queda pendiente y quién lo hace.</li></ul>");
        return sb.ToString();
    }

    internal static string QuitarCercoDeCodigo(string? texto)
    {
        var t = (texto ?? string.Empty).Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var primerSalto = t.IndexOf('\n');
            t = primerSalto > 0 ? t[(primerSalto + 1)..] : string.Empty;
            if (t.EndsWith("```", StringComparison.Ordinal)) t = t[..^3];
        }
        return t.Trim();
    }

    private static async Task<TicketInformeContexto> LeerContextoAsync(SqlConnection cn, long idTicket, CancellationToken ct)
    {
        const string sql = """
            SELECT t.IdTicket, t.Numero, t.Titulo, ISNULL(CAST(t.Descripcion AS nvarchar(max)), N'') AS Descripcion,
                   ISNULL(e.Nombre, t.CodigoEstado) AS Estado, t.FechaHoraAlta,
                   ISNULL((SELECT TOP (1) c.RAZON_SOCIAL FROM dbo.VT_CLIENTES c WHERE LTRIM(RTRIM(c.CODIGO)) = LTRIM(RTRIM(t.ClienteCodigo))), N'') AS Cliente
            FROM dbo.TICK_TICKETS t LEFT JOIN dbo.TICK_ESTADOS e ON e.CodigoEstado = t.CodigoEstado
            WHERE t.IdTicket = @IdTicket;

            SELECT TOP (60) a.FechaHora, ISNULL(a.TipoActividad, N''), ISNULL(a.Descripcion, N''), ISNULL(a.Usuario, N'')
            FROM dbo.TICK_ACTIVIDAD a WHERE a.IdTicket = @IdTicket ORDER BY a.FechaHora;

            SELECT TOP (60) m.FechaHora, ISNULL(m.Direction, N''), ISNULL(m.Texto, N'')
            FROM dbo.TICK_TICKET_MENSAJES tm INNER JOIN dbo.CONV_MENSAJES m ON m.IdMensaje = tm.IdMensaje
            WHERE tm.IdTicket = @IdTicket AND NULLIF(LTRIM(RTRIM(ISNULL(m.Texto, N''))), N'') IS NOT NULL
            ORDER BY m.FechaHora, m.IdMensaje;

            -- Partes de horas: la tabla puede no existir en bases sin el modulo; sp_executesql evita el error de compilacion.
            IF OBJECT_ID(N'dbo.ALFACORE_PARTES_HORAS', N'U') IS NOT NULL
                EXEC sp_executesql N'SELECT TOP (40) CAST(Fecha AS datetime), Minutos, ISNULL(Descripcion, N'''') FROM dbo.ALFACORE_PARTES_HORAS WHERE IdTicket = @Id AND ISNULL(Baja, 0) = 0 ORDER BY Fecha', N'@Id bigint', @Id = @IdTicket;
            """;
        await using var multi = await cn.QueryMultipleAsync(new CommandDefinition(sql, new { IdTicket = idTicket }, commandTimeout: Timeout, cancellationToken: ct));
        var c = await multi.ReadFirstOrDefaultAsync<TicketInformeContexto>() ?? throw new InvalidOperationException("El ticket ya no existe.");
        c.Actividad = (await multi.ReadAsync<(DateTime, string, string, string)>()).ToList();
        c.Mensajes = (await multi.ReadAsync<(DateTime, string, string)>()).ToList();
        if (!multi.IsConsumed) c.Partes = (await multi.ReadAsync<(DateTime, int, string)>()).ToList();
        return c;
    }

    private static async Task<TicketInformeDto> LeerAsync(SqlConnection cn, int idInforme, CancellationToken ct)
        => await cn.QueryFirstOrDefaultAsync<TicketInformeDto>(new CommandDefinition(
               "SELECT IdInforme, IdTicket, Titulo, Estado FROM dbo.TICK_INFORMES WHERE IdInforme = @IdInforme AND Baja = 0;", new { IdInforme = idInforme }, cancellationToken: ct))
           ?? throw new InvalidOperationException("El informe ya no existe.");

    private static Task ActividadAsync(SqlConnection cn, SqlTransaction tx, long idTicket, string texto, string usuario, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.TICK_ACTIVIDAD (IdTicket, TipoActividad, Descripcion, Usuario, FechaHora) VALUES (@IdTicket, 'INFORME', @Texto, @Usuario, GETDATE());",
            new { IdTicket = idTicket, Texto = texto.Length > 1000 ? texto[..1000] : texto, Usuario = usuario }, tx, cancellationToken: ct));

    private static void Validar(TicketInformeDestino d)
    {
        if (string.IsNullOrWhiteSpace(d.IdWeb) || d.IdBase <= 0)
            throw new InvalidOperationException("No se pudo identificar la empresa y la base para publicar el link. Volvé a entrar a la base.");
    }

    private static string Url(TicketInformeDestino d, PublicLinkDto link)
        => $"{d.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(d.IdWeb)}/informe/{link.RouteSegment}";

    private static string Usuario(string? u) { var x = (u ?? string.Empty).Trim(); return x.Length == 0 ? "AlfaCore" : x.Length > 100 ? x[..100] : x; }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    internal static string Fecha(DateTime? f) => f?.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-AR")) ?? string.Empty;
}

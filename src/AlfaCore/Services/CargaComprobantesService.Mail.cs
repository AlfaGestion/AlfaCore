using System.Net;
using System.Net.Mail;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Envío de comprobantes por mail como v_FacturacionAutomatica.vbp (CmdMail / FrmEnviar): asunto y mensaje
/// configurables con marcas, la factura en PDF adjunta, a los mails del cliente, y cada envío registrado en
/// V_MA_EMAILMOV con el mismo formato que el VB6 (así el historial es uno solo).
/// </summary>
public sealed partial class CargaComprobantesService
{
    private const string ClaveAsunto = "TP_V_FACTAUTOMATICA_TXTASUNTO";
    private const string ClaveMensaje = "TP_V_FACTAUTOMATICA_TXTMENSAJE";
    private const string ClaveEnviarAlEmitir = "TP_V_FACTAUTOMATICA_CHKEMAILAUTO";
    private const string ClaveMailPrueba = "ALFACORE_MAIL_PRUEBA";

    public Task<ComprobanteMailConfig> GetMailConfigAsync(CancellationToken ct = default)
        => ExecuteAsync("MailConfig", async (cn, _, token) => await LeerMailConfigAsync(cn, token), ct);

    public Task GuardarMailConfigAsync(ComprobanteMailConfig config, CancellationToken ct = default)
        => ExecuteAsync("GuardarMailConfig", async (cn, ctx, token) =>
        {
            var prueba = (config.MailPrueba ?? string.Empty).Trim();
            if (prueba.Length > 0 && !CorreoSalienteService.EsMailValido(prueba))
                throw new CargaComprobantesReglaException("El mail de prueba no es válido.");
            if ((config.Asunto ?? string.Empty).Trim().Length is 0 or > 250)
                throw new CargaComprobantesReglaException("Escribí el asunto del mail (hasta 250 caracteres).");
            await GuardarClaveAsync(cn, ClaveAsunto, config.Asunto!.Trim(), null, token);
            // El VB6 guarda el mensaje (largo) en ValorAux y deja VALOR vacío.
            await GuardarClaveAsync(cn, ClaveMensaje, string.Empty, config.Mensaje ?? string.Empty, token);
            await GuardarClaveAsync(cn, ClaveEnviarAlEmitir, config.EnviarAlEmitir ? "SI" : "NO", null, token);
            await GuardarClaveAsync(cn, ClaveMailPrueba, prueba, null, token);
            await events.LogAuditAsync("CargaComprobantes", "GuardarMailConfig", "TA_CONFIGURACION", ClaveAsunto,
                "Se actualizó el mail de facturación.", new { config.EnviarAlEmitir, MailPrueba = prueba }, CancellationToken.None);
            return true;
        }, ct);

    public Task<ComprobanteMailBorrador> PrepararMailComprobanteAsync(string tc, string idComprobante, CancellationToken ct = default)
        => ExecuteAsync("PrepararMail", async (cn, ctx, token) =>
        {
            var datos = await DatosMailAsync(cn, ctx, tc, idComprobante, token);
            var config = await LeerMailConfigAsync(cn, token);
            return new ComprobanteMailBorrador
            {
                Tc = datos.Tc, IdComprobante = datos.IdComprobante, Cliente = datos.Nombre,
                Para = datos.Mail, Asunto = await ArmarTextoAsync(cn, config.Asunto, datos, token),
                Mensaje = await ArmarTextoAsync(cn, config.Mensaje, datos, token),
                MailPrueba = config.MailPrueba.Length > 0 ? config.MailPrueba : null,
                UltimoEnvio = await UltimoEnvioAsync(cn, datos.Tc, datos.IdComprobante, token)
            };
        }, ct);

    public Task<ComprobanteMailResultado> EnviarMailComprobanteAsync(string tc, string idComprobante, string? para = null,
        string? asunto = null, string? mensaje = null, CancellationToken ct = default)
        => ExecuteAsync("EnviarMail", (cn, ctx, token) => EnviarMailAsync(cn, ctx, tc, idComprobante, para, asunto, mensaje, token), ct);

    /// <summary>Arma y envía; nunca lanza por un problema de envío: lo devuelve en el resultado y lo registra.</summary>
    private async Task<ComprobanteMailResultado> EnviarMailAsync(SqlConnection cn, CargaComprobantesContexto ctx, string tc, string idComprobante,
        string? para, string? asunto, string? mensaje, CancellationToken ct)
    {
        var datos = await DatosMailAsync(cn, ctx, tc, idComprobante, ct);
        var config = await LeerMailConfigAsync(cn, ct);
        var (validos, invalidos) = CorreoSalienteService.SepararMails(para ?? datos.Mail);
        if (validos.Count == 0)
            return new() { Enviado = false, Mensaje = invalidos.Count > 0
                ? $"El mail del cliente no es válido ({string.Join(", ", invalidos)}). Corregilo en la ficha del cliente."
                : "El cliente no tiene mail cargado. Agregalo en la ficha del cliente (podés poner varios separados por «;»)." };
        var asuntoFinal = await ArmarTextoAsync(cn, asunto ?? config.Asunto, datos, ct);
        var mensajeFinal = await ArmarTextoAsync(cn, mensaje ?? config.Mensaje, datos, ct);
        IReadOnlyList<string> destinos = validos;
        if (config.MailPrueba.Length > 0)
        {
            // Modo prueba: no se le escribe al cliente; se avisa a quién hubiera ido.
            asuntoFinal = $"[PRUEBA · para {string.Join(", ", validos)}] {asuntoFinal}";
            destinos = [config.MailPrueba];
        }
        if (correo is null) throw new CargaComprobantesReglaException("El envío de mails no está disponible en este servidor.");
        var archivo = $"{datos.Tc}_{datos.IdComprobante}.pdf";
        CorreoSalienteService.CuentaCorreo? cuenta = null;
        try
        {
            var pdfBytes = await documents.GeneratePdfAsync(datos.Tc, datos.IdComprobante, datos.Unidad.Length == 0 ? null : datos.Unidad, ct);
            cuenta = await correo.ResolverAsync(cn, ctx.Usuario, ct);
            var html = "<div style=\"font-family:Arial,sans-serif;font-size:14px\">" +
                       WebUtility.HtmlEncode(mensajeFinal).Replace("\r\n", "\n").Replace("\n", "<br />") + "</div>";
            await correo.EnviarAsync(cn, cuenta, destinos, asuntoFinal, html, [new(archivo, pdfBytes)], ct);
            await RegistrarEnvioAsync(cn, ctx, datos, cuenta, string.Join(";", destinos), asuntoFinal, mensajeFinal, archivo, "OK", ct);
            var texto = config.MailPrueba.Length > 0 ? $"Enviado en modo prueba a {config.MailPrueba} (iba a {string.Join(", ", validos)})." : $"Enviado a {string.Join(", ", destinos)}.";
            if (invalidos.Count > 0) texto += $" Se omitieron mails inválidos: {string.Join(", ", invalidos)}.";
            return new() { Enviado = true, Para = string.Join(", ", destinos), Mensaje = texto };
        }
        catch (Exception ex) when (ex is InvalidOperationException or SmtpException or AppUserFacingException or FormatException)
        {
            var motivo = MotivoEnvio(ex);
            await RegistrarEnvioAsync(cn, ctx, datos, cuenta, string.Join(";", destinos), asuntoFinal, mensajeFinal, archivo, $"ERROR: {motivo}", ct);
            if (ex is SmtpException)
                await events.LogErrorAsync("CargaComprobantes", "EnviarMail", ex, motivo, new { datos.Tc, datos.IdComprobante }, ct: CancellationToken.None);
            return new() { Enviado = false, Para = string.Join(", ", destinos), Mensaje = motivo };
        }
    }

    private static string MotivoEnvio(Exception ex)
    {
        if (ex is not SmtpException smtp) return ex.Message;
        var texto = (smtp.InnerException?.Message ?? smtp.Message);
        if (smtp.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed)
            return "El servidor de correo rechazó el destinatario. Revisá el mail del cliente.";
        if (texto.Contains("auth", StringComparison.OrdinalIgnoreCase) || texto.Contains("5.7", StringComparison.OrdinalIgnoreCase))
            return "El servidor de correo no aceptó el usuario o la clave de la cuenta de envío. Revisá la cuenta de correo de tu usuario.";
        return $"No se pudo enviar el mail: {texto} Revisá la cuenta de correo (servidor, puerto, usuario y clave).";
    }

    private async Task<ComprobanteMailConfig> LeerMailConfigAsync(SqlConnection cn, CancellationToken ct) => new()
    {
        Asunto = await CorreoSalienteService.LeerConfigAsync(cn, ClaveAsunto, ct) is { Length: > 0 } a ? a : "Adjunto comprobante correspondiente al periodo <PERIODO>",
        Mensaje = await CorreoSalienteService.LeerConfigAsync(cn, ClaveMensaje, ct),
        EnviarAlEmitir = (await CorreoSalienteService.LeerConfigAsync(cn, ClaveEnviarAlEmitir, ct)).Trim().Equals("SI", StringComparison.OrdinalIgnoreCase),
        MailPrueba = (await CorreoSalienteService.LeerConfigAsync(cn, ClaveMailPrueba, ct)).Trim()
    };

    private static Task GuardarClaveAsync(SqlConnection cn, string clave, string valor, string? valorAux, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, ValorAux = @ValorAux, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, FechaHora_Grabacion) VALUES (N'VENTAS', @Clave, @Valor, @ValorAux, GETDATE());
            """, new { Clave = clave, Valor = valor, ValorAux = valorAux }, cancellationToken: ct));

    private sealed class DatosComprobanteMail
    {
        public string Tc { get; set; } = "";
        public string IdComprobante { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string Cuenta { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Importe { get; set; }
        public string Unidad { get; set; } = "";
        public string Mail { get; set; } = "";
    }

    private static async Task<DatosComprobanteMail> DatosMailAsync(SqlConnection cn, CargaComprobantesContexto ctx, string tc, string idComprobante, CancellationToken ct)
    {
        var datos = await cn.QuerySingleOrDefaultAsync<DatosComprobanteMail>(new CommandDefinition("""
            SELECT TOP (1) LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) IdComprobante, c.FECHA Fecha,
                LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, COALESCE(NULLIF(LTRIM(RTRIM(cl.RAZON_SOCIAL)),''), ISNULL(c.NOMBRE,'')) Nombre,
                ISNULL(c.IMPORTE,0) Importe, LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) Unidad, LTRIM(RTRIM(ISNULL(cl.MAIL,''))) Mail
            FROM dbo.V_MV_CPTE c
            LEFT JOIN dbo.VT_CLIENTES cl ON LTRIM(RTRIM(cl.CODIGO)) = LTRIM(RTRIM(c.CUENTA))
            WHERE LTRIM(RTRIM(c.TC)) = @Tc AND c.IDCOMPROBANTE = @Id AND ISNULL(c.IDCOMPLEMENTO,0) = 0;
            """, new { Tc = tc.Trim(), Id = idComprobante.Trim() }, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("No se encontró el comprobante.");
        if (!ctx.AdminCentral && datos.Unidad.Length > 0 && datos.Unidad != ctx.UnidadNegocio.Trim())
            throw new CargaComprobantesReglaException("El comprobante pertenece a otra unidad de negocio.");
        return datos;
    }

    /// <summary>Marcas del VB6 (CmdMail_Click): &lt;PERIODO&gt; mes-año, &lt;NOMBRE&gt;, &lt;FORMADEPAGO&gt; y &lt;DETALLEFACTURA&gt;.</summary>
    private static async Task<string> ArmarTextoAsync(SqlConnection cn, string? plantilla, DatosComprobanteMail d, CancellationToken ct)
    {
        var texto = plantilla ?? string.Empty;
        texto = texto.Replace("<PERIODO>", $"{d.Fecha.Month}-{d.Fecha.Year}", StringComparison.OrdinalIgnoreCase)
                     .Replace("<NOMBRE>", d.Nombre.Trim(), StringComparison.OrdinalIgnoreCase);
        if (texto.Contains("<FORMADEPAGO>", StringComparison.OrdinalIgnoreCase))
            texto = texto.Replace("<FORMADEPAGO>", (await CorreoSalienteService.LeerConfigAsync(cn, $"FormaDePago_UN_{d.Unidad}", ct)).Trim(), StringComparison.OrdinalIgnoreCase);
        if (texto.Contains("<DETALLEFACTURA>", StringComparison.OrdinalIgnoreCase))
            texto = texto.Replace("<DETALLEFACTURA>",
                $"Fecha Cpte: {d.Fecha:dd/MM/yyyy} Nro: {d.IdComprobante} Total: {d.Importe.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}",
                StringComparison.OrdinalIgnoreCase);
        return texto;
    }

    private static async Task<string?> UltimoEnvioAsync(SqlConnection cn, string tc, string id, CancellationToken ct)
        => await cn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.V_MA_EMAILMOV', N'U') IS NOT NULL
                SELECT TOP (1) CONVERT(nvarchar(16), FechaHora, 103) + ' ' + CONVERT(nvarchar(5), FechaHora, 108) + ' · ' + ISNULL(Para,'') + ' · ' + ISNULL(Status,'')
                FROM dbo.V_MA_EMAILMOV WHERE TC = @Tc AND IdComprobante = @Id AND ISNULL(Tipo,'E') = 'E' ORDER BY FechaHora DESC;
            ELSE SELECT CAST(NULL AS nvarchar(10));
            """, new { Tc = tc, Id = id }, cancellationToken: ct));

    // Mismo registro que FrmEnviar (V_MA_EMAILMOV, Tipo E): el historial de envíos se ve igual desde los dos sistemas.
    private async Task RegistrarEnvioAsync(SqlConnection cn, CargaComprobantesContexto ctx, DatosComprobanteMail d, CorreoSalienteService.CuentaCorreo? cuenta,
        string para, string asunto, string mensaje, string adjunto, string estado, CancellationToken ct)
    {
        try
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                IF OBJECT_ID(N'dbo.V_MA_EMAILMOV', N'U') IS NOT NULL
                    INSERT INTO dbo.V_MA_EMAILMOV (De, Para, Asunto, Mensaje, Adjunto, FechaHora, Usuario, Status, Cuenta, TC, IdComprobante, Tipo, NombreDe, CuentaeMail)
                    VALUES (@De, @Para, @Asunto, @Mensaje, @Adjunto, GETDATE(), @Usuario, @Estado, @Cuenta, @Tc, @Id, 'E', @NombreDe, @CuentaMail);
                """, new
                {
                    De = Corte(cuenta?.ResponderA ?? cuenta?.Remitente ?? string.Empty, 300), Para = Corte(para, 500), Asunto = Corte(asunto, 500),
                    Mensaje = mensaje, Adjunto = Corte(adjunto, 500), Usuario = Corte(ctx.Usuario, 300), Estado = Corte(estado, 300),
                    d.Cuenta, d.Tc, Id = d.IdComprobante, NombreDe = Corte(cuenta?.NombreRemitente ?? string.Empty, 500),
                    CuentaMail = Corte(cuenta?.Login ?? string.Empty, 500)
                }, cancellationToken: CancellationToken.None));
        }
        catch (SqlException ex)
        {
            await events.LogErrorAsync("CargaComprobantes", "RegistrarMail", ex, "No se pudo registrar el envío del mail.", new { d.Tc, d.IdComprobante }, ct: CancellationToken.None);
        }
    }

    private static string Corte(string? texto, int largo) => (texto ?? string.Empty) is var t && t.Length > largo ? t[..largo] : texto ?? string.Empty;
}

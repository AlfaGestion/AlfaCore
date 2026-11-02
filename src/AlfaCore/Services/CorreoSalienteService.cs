using System.Net;
using System.Net.Mail;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Envío de mails de comprobantes con la misma cuenta que usa Alfa Gestión (FrmEnviar.frm):
///   1) la cuenta propia del usuario (TA_USUARIOS.email_*: Gmail / Hotmail / Otro),
///   2) la general de la base (TA_CONFIGURACION EMAIL_SERVER / EMAIL_CTA / EMAIL_PASS),
///   3) la cuenta de respaldo de Alfa (appsettings RegistroPublico:Email*), con el mismo tope mensual
///      que las cotizaciones (EMAIL_FALLBACK_CONTADOR) para no usarla como servidor masivo.
/// El remitente visible es el nombre/"De" del usuario cuando lo tiene, como en el VB6.
/// </summary>
public sealed class CorreoSalienteService(IConfiguration configuration)
{
    private const int LimiteMensualCuentaRespaldo = 200;
    private const string ClaveContadorRespaldo = "EMAIL_FALLBACK_CONTADOR";

    public sealed record CuentaCorreo(string Server, int Port, string Login, string Password, bool Ssl,
        string Remitente, string? NombreRemitente, string? ResponderA, bool EsRespaldo);

    public sealed record Adjunto(string Nombre, byte[] Contenido, string Tipo = "application/pdf");

    public async Task<CuentaCorreo> ResolverAsync(SqlConnection cn, string usuario, CancellationToken ct)
    {
        var u = await cn.QuerySingleOrDefaultAsync<UsuarioMail>(new CommandDefinition("""
            SELECT TOP (1) LTRIM(RTRIM(ISNULL(email_server,''))) Server, LTRIM(RTRIM(ISNULL(email_serverotro,''))) ServerOtro,
                   LTRIM(RTRIM(ISNULL(email_usuario,''))) Usuario, ISNULL(email_password,'') Password,
                   LTRIM(RTRIM(ISNULL(email_nombre,''))) Nombre, LTRIM(RTRIM(ISNULL(email_de,''))) De
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = UPPER(@Usuario);
            """, new { Usuario = (usuario ?? string.Empty).Trim() }, cancellationToken: ct)) ?? new UsuarioMail();
        var puertoBase = int.TryParse(await LeerConfigAsync(cn, "EMAIL_PORT", ct), out var pb) && pb > 0 ? pb : 0;
        var sslBase = EsSi(await LeerConfigAsync(cn, "EMAIL_SSL", ct));
        var nombre = u.Nombre.Length > 0 ? u.Nombre : NullSiVacio(await LeerConfigAsync(cn, "EMAIL_NOMBRE", ct));
        var responderA = u.De.Length > 0 ? u.De : NullSiVacio(await LeerConfigAsync(cn, "EMAIL_DE", ct));

        // 1) Cuenta del usuario (mismo criterio de servidor/puerto que FrmEnviar).
        if (u.Usuario.Length > 0 && u.Password.Length > 0)
        {
            var (server, port, ssl) = u.Server.ToUpperInvariant() switch
            {
                "GMAIL" => ("smtp.gmail.com", 587, true),
                "HOTMAIL" => ("smtp-mail.outlook.com", 587, true),
                _ => (u.ServerOtro, puertoBase > 0 ? puertoBase : 25, sslBase)
            };
            if (server.Length > 0)
                return new CuentaCorreo(server, port, u.Usuario, u.Password, ssl, u.Usuario, nombre, responderA, false);
        }

        // 2) Cuenta general de la base: todo o nada (servidor, cuenta y clave).
        var serverBase = (await LeerConfigAsync(cn, "EMAIL_SERVER", ct)).Trim();
        var cuentaBase = (await LeerConfigAsync(cn, "EMAIL_CTA", ct)).Trim();
        var claveBase = (await LeerConfigAsync(cn, "EMAIL_PASS", ct)).Trim();
        if (serverBase.Length > 0 && cuentaBase.Length > 0 && claveBase.Length > 0 && !serverBase.Equals("Otro", StringComparison.OrdinalIgnoreCase))
            return new CuentaCorreo(serverBase, puertoBase > 0 ? puertoBase : 587, cuentaBase, claveBase, sslBase, cuentaBase, nombre, responderA, false);

        // 3) Cuenta de respaldo de Alfa (la del VB6 cuando no hay cuenta propia), con tope mensual.
        var vServer = (configuration["RegistroPublico:EmailServer"] ?? string.Empty).Trim();
        var vCuenta = (configuration["RegistroPublico:EmailAccount"] ?? string.Empty).Trim();
        var vClave = (configuration["RegistroPublico:EmailPassword"] ?? string.Empty).Trim();
        if (vServer.Length == 0 || vCuenta.Length == 0 || vClave.Length == 0
            || !int.TryParse(configuration["RegistroPublico:EmailPort"], out var vPort) || vPort <= 0)
            throw new InvalidOperationException("No hay una cuenta de correo para enviar. Cargá tu cuenta en Usuarios (email del usuario) " +
                                                "o la cuenta general en Configuración General → Email.");
        if (await ContadorRespaldoAsync(cn, ct) >= LimiteMensualCuentaRespaldo)
            throw new InvalidOperationException($"Se alcanzó el tope de {LimiteMensualCuentaRespaldo} mails del mes con la cuenta de envío de Alfa. " +
                                                "Cargá una cuenta de correo propia en tu usuario o en Configuración General → Email.");
        return new CuentaCorreo(vServer, vPort, vCuenta, vClave, EsSi(configuration["RegistroPublico:EmailSsl"]), vCuenta,
            nombre ?? "Envio Automatico", responderA, true);
    }

    public async Task EnviarAsync(SqlConnection cn, CuentaCorreo cuenta, IReadOnlyList<string> para, string asunto, string cuerpoHtml,
        IReadOnlyList<Adjunto> adjuntos, CancellationToken ct)
    {
        using var mensaje = new MailMessage
        {
            From = new MailAddress(cuenta.Remitente, cuenta.NombreRemitente ?? string.Empty),
            Subject = asunto,
            Body = cuerpoHtml,
            IsBodyHtml = true
        };
        foreach (var destino in para) mensaje.To.Add(destino);
        if (!string.IsNullOrWhiteSpace(cuenta.ResponderA) && EsMailValido(cuenta.ResponderA)
            && !cuenta.ResponderA.Equals(cuenta.Remitente, StringComparison.OrdinalIgnoreCase))
            mensaje.ReplyToList.Add(cuenta.ResponderA);
        var streams = new List<MemoryStream>();
        try
        {
            foreach (var a in adjuntos)
            {
                var ms = new MemoryStream(a.Contenido); streams.Add(ms);
                mensaje.Attachments.Add(new Attachment(ms, a.Nombre, a.Tipo));
            }
            using var smtp = new SmtpClient(cuenta.Server, cuenta.Port)
            {
                EnableSsl = cuenta.Ssl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(cuenta.Login, cuenta.Password),
                Timeout = 60000
            };
            await smtp.SendMailAsync(mensaje, ct);
        }
        finally { foreach (var ms in streams) ms.Dispose(); }
        if (cuenta.EsRespaldo) await SumarRespaldoAsync(cn, ct);
    }

    /// <summary>Separa una lista de mails ("a@x.com; b@y.com, c@z.com") y descarta los inválidos.</summary>
    public static (IReadOnlyList<string> Validos, IReadOnlyList<string> Invalidos) SepararMails(string? texto)
    {
        var partes = (texto ?? string.Empty).Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (partes.Where(EsMailValido).ToList(), partes.Where(p => !EsMailValido(p)).ToList());
    }

    public static bool EsMailValido(string? mail)
    {
        if (string.IsNullOrWhiteSpace(mail) || !mail.Contains('@')) return false;
        try { return new MailAddress(mail.Trim()).Address.Equals(mail.Trim(), StringComparison.OrdinalIgnoreCase); }
        catch (FormatException) { return false; }
    }

    private static async Task<int> ContadorRespaldoAsync(SqlConnection cn, CancellationToken ct)
    {
        var partes = (await LeerConfigAsync(cn, ClaveContadorRespaldo, ct)).Split(':', 2);
        return partes.Length == 2 && partes[0] == DateTime.Now.ToString("yyyyMM") && int.TryParse(partes[1], out var n) ? n : 0;
    }

    private static async Task SumarRespaldoAsync(SqlConnection cn, CancellationToken ct)
    {
        var valor = $"{DateTime.Now:yyyyMM}:{await ContadorRespaldoAsync(cn, ct) + 1}";
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, FechaHora_Grabacion) VALUES (N'COTIZACIONES', @Clave, @Valor, GETDATE());
            """, new { Clave = ClaveContadorRespaldo, Valor = valor }, cancellationToken: ct));
    }

    internal static async Task<string> LeerConfigAsync(SqlConnection cn, string clave, CancellationToken ct)
        => await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''), CONVERT(nvarchar(max), ValorAux), '')
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
            """, new { Clave = clave }, cancellationToken: ct)) ?? string.Empty;

    private static bool EsSi(string? valor) => valor?.Trim().ToUpperInvariant() is "SI" or "TRUE" or "1";
    private static string? NullSiVacio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private sealed class UsuarioMail
    {
        public string Server { get; set; } = "";
        public string ServerOtro { get; set; } = "";
        public string Usuario { get; set; } = "";
        public string Password { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string De { get; set; } = "";
    }
}

using System.Net;
using System.Net.Mail;

namespace AlfaCore.Mcp.Auth;

/// <summary>
/// SMTP para los codigos de ingreso, avisos de conexion e invitaciones. Variables de entorno del pool (nunca en el repo):
/// Mcp__Email__Server, Mcp__Email__Port, Mcp__Email__Account, Mcp__Email__Password, Mcp__Email__Ssl, Mcp__Email__From (opcional).
/// Mismo esquema que RegistroPublico__Email* de AlfaCore.
/// </summary>
public sealed record EmailOptions(string? Server, int Port, string? Account, string? Password, bool Ssl, string? From)
{
    public bool Configurado => !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Account);

    public static EmailOptions Load(IConfiguration cfg)
    {
        var s = cfg.GetSection("Mcp:Email");
        return new EmailOptions(s["Server"], s.GetValue("Port", 587), s["Account"], s["Password"], s.GetValue("Ssl", false), s["From"]);
    }
}

public sealed record MensajeEmail(string Para, string Asunto, string Texto);

public interface IEmailSender
{
    /// <summary>False si el envio no esta configurado (el ingreso por mail queda deshabilitado).</summary>
    bool Disponible { get; }
    Task EnviarAsync(MensajeEmail mensaje, CancellationToken ct);
}

public sealed class SmtpEmailSender(EmailOptions opt) : IEmailSender
{
    public bool Disponible => opt.Configurado;

    public async Task EnviarAsync(MensajeEmail m, CancellationToken ct)
    {
        if (!opt.Configurado) throw new InvalidOperationException("El envio de mails del MCP no esta configurado (Mcp__Email__Server / Account).");
        using var msg = new MailMessage(new MailAddress(opt.Account!, string.IsNullOrWhiteSpace(opt.From) ? "AlfaCore" : opt.From), new MailAddress(m.Para))
        {
            Subject = m.Asunto,
            Body = m.Texto,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        using var smtp = new SmtpClient(opt.Server, opt.Port)
        {
            EnableSsl = opt.Ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(opt.Account, opt.Password),
            Timeout = 20000
        };
        await smtp.SendMailAsync(msg, ct);
    }
}

/// <summary>Solo Development: no envia nada, escribe el mail en el log (para probar el ingreso sin SMTP).</summary>
public sealed class ConsolaEmailSender(ILogger<ConsolaEmailSender> log) : IEmailSender
{
    public bool Disponible => true;
    public Task EnviarAsync(MensajeEmail m, CancellationToken ct)
    {
        log.LogWarning("MAIL DE PRUEBA (no se envia) para {Para} | {Asunto}\n{Texto}", m.Para, m.Asunto, m.Texto);
        return Task.CompletedTask;
    }
}

/// <summary>Textos de los mails. Sin HTML a proposito: llegan bien a cualquier cliente y no se confunden con phishing.</summary>
public static class Mails
{
    public static MensajeEmail Codigo(string email, string codigo, int minutos, string aplicacion) => new(email,
        $"Tu código de acceso a AlfaCore: {codigo}",
        $"""
        Hola:

        Tu código para conectar {aplicacion} con los datos de AlfaCore es:

            {codigo}

        Vence en {minutos} minutos y sirve una sola vez.

        Si no lo pediste vos, ignorá este mail: sin el código nadie puede entrar.

        ALFA NET - Soluciones Informáticas
        """);

    public static MensajeEmail AvisoConexion(string email, string aplicacion, string nombreBase, DateTime cuandoLocal) => new(email,
        "Nueva conexión a tus datos de AlfaCore",
        $"""
        Hola:

        {aplicacion} se conectó a los datos de "{nombreBase}" con tu mail el {cuandoLocal:dd/MM/yyyy} a las {cuandoLocal:HH:mm}.
        El acceso es solo de lectura: puede consultar, nunca modificar.

        Si fuiste vos, no hace falta que hagas nada.
        Si NO fuiste vos, avisanos enseguida a ALFA NET para cortar el acceso.

        ALFA NET - Soluciones Informáticas
        """);

    public static MensajeEmail Invitacion(string email, string nombreBase, string urlMcp) => new(email,
        "Tu acceso a AlfaCore desde Claude o ChatGPT",
        $"""
        Hola:

        Ya podés consultar los datos de "{nombreBase}" (ventas, clientes, compras, stock, cuenta corriente) conversando
        con Claude o ChatGPT. El acceso es solo de lectura.

        Cómo conectarlo (una sola vez):

        CLAUDE (claude.ai o la app):
          1. Configuración > Conectores > Agregar conector personalizado.
          2. Nombre: AlfaCore.  URL: {urlMcp}
          3. Tocá Conectar. Se abre nuestra página: escribí este mail ({email}).
          4. Te llega un código de 6 dígitos a este mail: escribilo y listo.

        CHATGPT:
          1. En Configuración, sección de conectores / aplicaciones, activar el modo desarrollador.
          2. Crear un conector. Nombre: AlfaCore.  URL: {urlMcp}  Autenticación: OAuth.
          3. Seguí los mismos pasos 3 y 4.

        Después preguntá, por ejemplo: "Usando AlfaCore, ¿cuánto vendí este mes por rubro?"

        Cada vez que se conecte una aplicación nueva te avisamos por mail.
        Ante cualquier duda, escribinos.

        ALFA NET - Soluciones Informáticas
        """);
}

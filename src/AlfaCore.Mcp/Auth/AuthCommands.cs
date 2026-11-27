using AlfaCore.Mcp.Infra;
using QRCoder;

namespace AlfaCore.Mcp.Auth;

/// <summary>Usuario autenticado de la peticion actual, para la auditoria (claim preferred_username del access token).</summary>
public sealed class HttpContextUserContext(IHttpContextAccessor accessor) : IMcpUserContext
{
    public string UserName => accessor.HttpContext?.User.FindFirst("preferred_username")?.Value ?? "anonimo";
}

/// <summary>
/// Administracion de usuarios y segundo factor por linea de comandos (nunca por HTTP): la clave se pide por consola y solo se guarda su hash;
/// el secreto TOTP se muestra UNA vez al administrador y se guarda cifrado. La inscripcion la hace el administrador a proposito:
/// si fuera por web, quien robe una clave podria inscribir su propio telefono.
/// </summary>
public static class AuthCommands
{
    private const string Emisor = "AlfaCore MCP";

    public static async Task<int> EjecutarAsync(string[] args, IServiceProvider sp, AuthOptions opt)
    {
        if (!opt.Enabled) { Console.Error.WriteLine("La autenticacion esta deshabilitada (Mcp:Auth:Enabled=false)."); return 2; }

        var posicionales = new List<string>();
        string? qr = null;
        var sinTotp = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--sin-totp") sinTotp = true;
            else if (args[i] == "--qr" && i + 1 < args.Length) qr = args[++i];
            else if (args[i].StartsWith("--qr=", StringComparison.Ordinal)) qr = args[i][5..];
            else posicionales.Add(args[i]);
        }

        try
        {
            return args[0] switch
            {
                "crear-usuario" => await CrearUsuarioAsync(posicionales, sinTotp, qr, sp),
                "configurar-totp" => await ConfigurarTotpAsync(posicionales, qr, sp),
                _ => Uso()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("No se pudo completar: " + ex.Message);
            return 1;
        }
    }

    private static int Uso()
    {
        Console.Error.WriteLine("""
            Uso:
              dotnet AlfaCore.Mcp.dll crear-usuario <usuario> ["Nombre a mostrar"] [base] [--sin-totp] [--qr archivo.png]
              dotnet AlfaCore.Mcp.dll configurar-totp <usuario> [--qr archivo.png]     (nuevo telefono / secreto perdido; corta sus sesiones)
            """);
        return 2;
    }

    private static async Task<int> CrearUsuarioAsync(List<string> p, bool sinTotp, string? qr, IServiceProvider sp)
    {
        if (p.Count < 1) return Uso();
        var usuario = p[0].Trim();
        var nombre = p.Count > 1 && !string.IsNullOrWhiteSpace(p[1]) ? p[1].Trim() : usuario;
        var baseId = p.Count > 2 ? p[2].Trim() : null;
        if (usuario.Length is < 3 or > 100 || usuario.Any(char.IsWhiteSpace)) { Console.Error.WriteLine("El usuario debe tener entre 3 y 100 caracteres y no llevar espacios."); return 2; }

        var store = sp.GetRequiredService<IAuthStore>();
        if (await store.GetUsuarioAsync(usuario, CancellationToken.None) is not null)
        {
            Console.Error.WriteLine($"El usuario '{usuario}' ya existe. Para un QR nuevo (telefono nuevo o perdido): configurar-totp {usuario} --qr archivo.png");
            return 1;
        }

        var clave = Pedir("Clave (minimo " + PasswordHasher.LargoMinimo + " caracteres): ");
        if (clave.Length < PasswordHasher.LargoMinimo) { Console.Error.WriteLine($"La clave debe tener al menos {PasswordHasher.LargoMinimo} caracteres."); return 2; }
        if (Pedir("Repetir clave: ") != clave) { Console.Error.WriteLine("Las claves no coinciden."); return 2; }

        var id = await store.CrearUsuarioAsync(usuario, nombre, PasswordHasher.Hash(clave), baseId, CancellationToken.None);
        Console.WriteLine($"Usuario '{usuario}' creado (id {id}).");
        if (sinTotp) Console.WriteLine("ATENCION: sin segundo factor. Si IA_MCP_OAUTH_REQUIERE_TOTP=SI no va a poder iniciar sesion.");
        else await InscribirTotpAsync(store, sp.GetRequiredService<SecretProtector>(), id, usuario, qr);
        return 0;
    }

    private static async Task<int> ConfigurarTotpAsync(List<string> p, string? qr, IServiceProvider sp)
    {
        if (p.Count < 1) return Uso();
        var store = sp.GetRequiredService<IAuthStore>();
        var u = await store.GetUsuarioAsync(p[0].Trim(), CancellationToken.None);
        if (u is null) { Console.Error.WriteLine($"No existe el usuario '{p[0]}'."); return 1; }
        await InscribirTotpAsync(store, sp.GetRequiredService<SecretProtector>(), u.Id, u.Usuario, qr);
        await store.RevocarUsuarioAsync(u.Id, CancellationToken.None);
        Console.WriteLine("Se cortaron las sesiones activas del usuario: tendra que iniciar sesion de nuevo con el nuevo segundo factor.");
        return 0;
    }

    private static async Task InscribirTotpAsync(IAuthStore store, SecretProtector protector, int id, string usuario, string? qr)
    {
        var secreto = Totp.NuevoSecreto();
        await store.SetTotpAsync(id, protector.Proteger(secreto), CancellationToken.None);
        var uri = Totp.UriOtpAuth(secreto, usuario, Emisor);
        Console.WriteLine();
        Console.WriteLine("Segundo factor (TOTP) configurado. Se muestra UNA sola vez y solo a la persona dueña de la cuenta:");
        Console.WriteLine($"  Clave para ingresar a mano en la app (tipo 'basado en tiempo'): {string.Join(' ', Enumerable.Range(0, secreto.Length / 4).Select(i => secreto.Substring(i * 4, 4)))}");
        Console.WriteLine($"  URI otpauth: {uri}");
        if (!string.IsNullOrWhiteSpace(qr))
        {
            var png = new PngByteQRCode(new QRCodeGenerator().CreateQrCode(uri, QRCodeGenerator.ECCLevel.M)).GetGraphic(10);
            await File.WriteAllBytesAsync(qr, png);
            Console.WriteLine($"  QR guardado en: {Path.GetFullPath(qr)}   (escanearlo con la app y BORRAR el archivo)");
        }
        else Console.WriteLine("  (agregar --qr archivo.png para generar el codigo QR)");
    }

    private static string Pedir(string texto)
    {
        Console.Write(texto);
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
    }
}

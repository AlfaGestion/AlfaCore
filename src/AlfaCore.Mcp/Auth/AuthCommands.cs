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

    public static readonly HashSet<string> Comandos = ["crear-usuario", "configurar-totp", "habilitar-base", "dar-acceso", "quitar-acceso", "invitar", "accesos"];

    public static async Task<int> EjecutarAsync(string[] args, IServiceProvider sp, AuthOptions opt)
    {
        if (!opt.Enabled) { Console.Error.WriteLine("La autenticacion esta deshabilitada (Mcp:Auth:Enabled=false)."); return 2; }

        var posicionales = new List<string>();
        string? qr = null;
        var sinTotp = false;
        var invitar = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--sin-totp") sinTotp = true;
            else if (args[i] == "--invitar") invitar = true;
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
                "habilitar-base" => await HabilitarBaseAsync(posicionales, sp),
                "dar-acceso" => await DarAccesoAsync(posicionales, invitar, sp, opt),
                "invitar" => await DarAccesoAsync(posicionales, true, sp, opt),
                "quitar-acceso" => await QuitarAccesoAsync(posicionales, sp),
                "accesos" => await ListarAccesosAsync(posicionales, sp),
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

            Multi-base (ingreso por mail, clientes de ALFA NET):
              habilitar-base <idbase> [usuario_sql]     registra el login de SOLO LECTURA de esa base (por defecto mcp_dir_ro); pide la clave
              dar-acceso <mail> <idbase> [--invitar]     el mail tiene que ser usuario del cliente dueno de la base (ALFA_CENTRAL.users)
              invitar <mail> <idbase>                    da el acceso (si falta) y manda el mail con las instrucciones
              quitar-acceso <mail> [idbase]              sin idbase: todas sus bases; corta sus sesiones
              accesos [idbase]                           lista quien tiene acceso
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

    // ---- multi-base ----------------------------------------------------------------------------------------------------
    private static async Task<int> HabilitarBaseAsync(List<string> p, IServiceProvider sp)
    {
        if (p.Count < 1 || !int.TryParse(p[0], out var idBase)) return Uso();
        var usuarioSql = p.Count > 1 ? p[1].Trim() : "mcp_dir_ro";
        var accesos = sp.GetRequiredService<IAccesoStore>();
        var b = await accesos.GetBaseCentralAsync(idBase, CancellationToken.None);
        if (b is null) { Console.Error.WriteLine($"No existe la base {idBase} en ALFA_CENTRAL.bases."); return 1; }
        Console.WriteLine($"Base {idBase}: {b.Value.Nombre} (cliente {b.Value.IdCliente}) en {b.Value.Servidor} / {b.Value.BaseDatos}. Login: {usuarioSql}");

        var clave = Pedir($"Clave del login {usuarioSql} en ese servidor: ");
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = b.Value.Servidor, InitialCatalog = b.Value.BaseDatos, UserID = usuarioSql, Password = clave,
            TrustServerCertificate = true, Encrypt = false, ConnectTimeout = 15, ApplicationName = "AlfaCore.Mcp"
        }.ConnectionString;
        try
        {
            await using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            // Tiene que leer el esquema dir y NO las tablas de la base (si no, no es el login de solo lectura del script 005).
            // IA_MCP_ESQUEMA_ERRORES: vistas que la actualizacion de AlfaCore no pudo crear en esta base (vacio = todas bien).
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM dir.vw_Config), HAS_PERMS_BY_NAME('dbo.TA_CONFIGURACION', 'OBJECT', 'SELECT'), " +
                              "(SELECT MAX(Valor) FROM dir.vw_Config WHERE Clave = 'IA_MCP_ESQUEMA_ERRORES')";
            await using var rd = await cmd.ExecuteReaderAsync();
            await rd.ReadAsync();
            if (!rd.IsDBNull(1) && rd.GetInt32(1) == 1)
            {
                Console.Error.WriteLine($"El login {usuarioSql} puede leer tablas de la base: no es de solo lectura. Usar el login creado con 005_login_solo_lectura.sql.");
                return 1;
            }
            Console.WriteLine($"Conexion OK ({rd.GetInt32(0)} claves IA_MCP_* en dir.vw_Config).");
            if (!rd.IsDBNull(2) && rd.GetString(2).Trim().Length > 0)
                Console.WriteLine("ATENCION: en esta base no se pudieron crear algunas vistas (esas herramientas van a fallar):\n" + rd.GetString(2));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Console.Error.WriteLine($"No se pudo conectar o falta el esquema dir. El esquema lo crea la actualizacion de AlfaCore (2026-11-27-040__ia_mcp_esquema_dir.sql) al entrar a esa base; el login, 005_login_solo_lectura.sql: {ex.Message}");
            return 1;
        }
        await accesos.GuardarBaseAsync(idBase, usuarioSql, sp.GetRequiredService<SecretProtector>().Proteger(clave), CancellationToken.None);
        Console.WriteLine($"Base {idBase} habilitada para el MCP (clave guardada cifrada). Ahora: dar-acceso <mail> {idBase} --invitar");
        return 0;
    }

    private static async Task<int> DarAccesoAsync(List<string> p, bool invitar, IServiceProvider sp, AuthOptions opt)
    {
        if (p.Count < 2 || !int.TryParse(p[1], out var idBase)) return Uso();
        var email = EmailNormalizado.Normalizar(p[0]);
        if (email is null) { Console.Error.WriteLine($"'{p[0]}' no es un mail valido."); return 2; }
        var accesos = sp.GetRequiredService<IAccesoStore>();
        var b = await accesos.GetBaseAsync(idBase, CancellationToken.None);
        if (b is not { Habilitada: true }) { Console.Error.WriteLine($"La base {idBase} no esta habilitada para el MCP: correr antes habilitar-base {idBase}."); return 1; }
        if (!await accesos.EsUsuarioDelClienteAsync(email, b.IdCliente, CancellationToken.None))
        {
            Console.Error.WriteLine($"{email} no es usuario del cliente {b.IdCliente} en ALFA_CENTRAL.users: darlo de alta primero en la administracion central.");
            return 1;
        }
        await accesos.DarAccesoAsync(email, idBase, CancellationToken.None);
        Console.WriteLine($"Acceso dado: {email} -> base {idBase} ({b.NombreBase}).");
        if (!invitar) { Console.WriteLine("(agregar --invitar para mandarle el mail con las instrucciones)"); return 0; }

        var mail = sp.GetRequiredService<IEmailSender>();
        if (!mail.Disponible) { Console.Error.WriteLine("No se envio la invitacion: falta configurar el SMTP (Mcp__Email__*)."); return 1; }
        await mail.EnviarAsync(Mails.Invitacion(email, b.NombreBase, opt.Resource), CancellationToken.None);
        await accesos.MarcarInvitadoAsync(email, idBase, CancellationToken.None);
        Console.WriteLine($"Invitacion enviada a {email}.");
        return 0;
    }

    private static async Task<int> QuitarAccesoAsync(List<string> p, IServiceProvider sp)
    {
        if (p.Count < 1) return Uso();
        var email = EmailNormalizado.Normalizar(p[0]);
        if (email is null) { Console.Error.WriteLine($"'{p[0]}' no es un mail valido."); return 2; }
        int? idBase = p.Count > 1 && int.TryParse(p[1], out var n) ? n : null;
        var cambiados = await sp.GetRequiredService<IAccesoStore>().QuitarAccesoAsync(email, idBase, CancellationToken.None);
        var store = sp.GetRequiredService<IAuthStore>();
        if (await store.GetUsuarioAsync(email, CancellationToken.None) is { } u) await store.RevocarUsuarioAsync(u.Id, CancellationToken.None);
        Console.WriteLine($"Accesos dados de baja: {cambiados}. Se cortaron las sesiones de {email} (las consultas en curso se cortan en menos de 30 s).");
        return 0;
    }

    private static async Task<int> ListarAccesosAsync(List<string> p, IServiceProvider sp)
    {
        int? idBase = p.Count > 0 && int.TryParse(p[0], out var n) ? n : null;
        var lista = await sp.GetRequiredService<IAccesoStore>().ListarAsync(idBase, CancellationToken.None);
        if (lista.Count == 0) { Console.WriteLine("No hay accesos."); return 0; }
        foreach (var a in lista)
            Console.WriteLine($"{(a.Activo ? "ACTIVO " : "baja   ")} {a.Email,-40} base {a.IdBase,-6} {a.NombreBase} ({a.Cliente})" +
                              $"  invitado: {a.InvitadoUtc?.ToString("yyyy-MM-dd") ?? "-"}  ultimo ingreso: {a.UltimoIngresoUtc?.ToString("yyyy-MM-dd HH:mm") ?? "-"} UTC");
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

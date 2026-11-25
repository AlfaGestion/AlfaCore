using AlfaCore.Mcp.Infra;

namespace AlfaCore.Mcp.Auth;

/// <summary>Usuario autenticado de la peticion actual, para la auditoria (claim preferred_username del access token).</summary>
public sealed class HttpContextUserContext(IHttpContextAccessor accessor) : IMcpUserContext
{
    public string UserName => accessor.HttpContext?.User.FindFirst("preferred_username")?.Value ?? "anonimo";
}

/// <summary>Administracion de usuarios por linea de comandos (nunca por HTTP): la clave se pide por consola y solo se guarda su hash.</summary>
public static class AuthCommands
{
    public static async Task<int> CrearUsuarioAsync(string[] args, IServiceProvider sp, AuthOptions opt)
    {
        if (!opt.Enabled) { Console.Error.WriteLine("La autenticacion esta deshabilitada (Mcp:Auth:Enabled=false)."); return 2; }
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: dotnet run --project src/AlfaCore.Mcp -- crear-usuario <usuario> [\"Nombre a mostrar\"] [base]");
            return 2;
        }
        var usuario = args[1].Trim();
        var nombre = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2].Trim() : usuario;
        var baseId = args.Length > 3 ? args[3].Trim() : null;
        if (usuario.Length is < 3 or > 100 || usuario.Any(char.IsWhiteSpace)) { Console.Error.WriteLine("El usuario debe tener entre 3 y 100 caracteres y no llevar espacios."); return 2; }

        var clave = Pedir("Clave (minimo " + PasswordHasher.LargoMinimo + " caracteres): ");
        if (clave.Length < PasswordHasher.LargoMinimo) { Console.Error.WriteLine($"La clave debe tener al menos {PasswordHasher.LargoMinimo} caracteres."); return 2; }
        if (Pedir("Repetir clave: ") != clave) { Console.Error.WriteLine("Las claves no coinciden."); return 2; }

        try
        {
            var id = await sp.GetRequiredService<IAuthStore>().CrearUsuarioAsync(usuario, nombre, PasswordHasher.Hash(clave), baseId, CancellationToken.None);
            Console.WriteLine($"Usuario '{usuario}' creado (id {id}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("No se pudo crear el usuario: " + ex.Message);
            return 1;
        }
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

using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Mcp.Auth;

/// <summary>Una base a la que un mail puede conectarse (MCP_ACCESO activo + base habilitada + el mail es usuario del cliente dueño de la base).</summary>
public sealed record AccesoBase(int IdBase, string NombreBase, string Cliente);

/// <summary>Datos de conexion de una base habilitada para el MCP. La clave del login de solo lectura viaja CIFRADA (SecretProtector).</summary>
public sealed record BaseMcp(int IdBase, string NombreBase, string IdCliente, string Servidor, string BaseDatos, string UsuarioSql, string ClaveCifrada, bool Habilitada);

/// <summary>Una fila de la lista de accesos (para el comando "accesos").</summary>
public sealed record AccesoListado(string Email, int IdBase, string NombreBase, string Cliente, bool Activo, DateTime? InvitadoUtc, DateTime? UltimoIngresoUtc);

/// <summary>
/// Multi-base: quien puede entrar a que base. Vive en ALFA_CENTRAL junto a las tablas MCP_* (script 014) y lee las tablas
/// centrales de AlfaCore (users, bases, Clientes) SOLO en las columnas necesarias: nunca dbpassword ni password.
/// </summary>
public interface IAccesoStore
{
    /// <summary>Bases a las que el mail puede entrar hoy (vacio si ninguna).</summary>
    Task<IReadOnlyList<AccesoBase>> AccesosAsync(string email, CancellationToken ct);
    Task<BaseMcp?> GetBaseAsync(int idBase, CancellationToken ct);
    Task RegistrarIngresoAsync(string email, int idBase, CancellationToken ct);

    // ---- codigos de un solo uso enviados por mail ----
    /// <summary>Invalida los codigos anteriores del mail y guarda el nuevo (solo su hash).</summary>
    Task GuardarCodigoEmailAsync(string email, string codigoHash, DateTime expiraUtc, string? ip, CancellationToken ct);
    Task<int> ContarCodigosEmailAsync(string email, DateTime desdeUtc, CancellationToken ct);
    /// <summary>Consume el codigo si coincide, no vencio, no se uso y no supero los intentos. Si no coincide, suma un intento al codigo vigente.</summary>
    Task<bool> ConsumirCodigoEmailAsync(string email, string codigoHash, DateTime ahoraUtc, int maxIntentos, CancellationToken ct);
    /// <summary>Ticket de un solo uso para elegir base despues de validar el codigo (cuando el mail tiene varias).</summary>
    Task GuardarTicketAsync(string email, string ticketHash, DateTime expiraUtc, CancellationToken ct);
    /// <summary>Devuelve el mail del ticket y lo invalida (atomico). Null si no existe, vencio o ya se uso.</summary>
    Task<string?> ConsumirTicketAsync(string ticketHash, DateTime ahoraUtc, CancellationToken ct);

    // ---- administracion (linea de comandos) ----
    Task<(string IdCliente, string Nombre, string Servidor, string BaseDatos)?> GetBaseCentralAsync(int idBase, CancellationToken ct);
    Task GuardarBaseAsync(int idBase, string usuarioSql, string claveCifrada, CancellationToken ct);
    Task<bool> EsUsuarioDelClienteAsync(string email, string idCliente, CancellationToken ct);
    Task DarAccesoAsync(string email, int idBase, CancellationToken ct);
    Task MarcarInvitadoAsync(string email, int idBase, CancellationToken ct);
    /// <summary>Desactiva el acceso (o todos los del mail si idBase es null). Devuelve cuantos cambio.</summary>
    Task<int> QuitarAccesoAsync(string email, int? idBase, CancellationToken ct);
    Task<IReadOnlyList<AccesoListado>> ListarAsync(int? idBase, CancellationToken ct);
}

public static class EmailNormalizado
{
    /// <summary>Recorta y pasa a minusculas. Null si no parece un mail (sin espacios, una @, dominio con punto, max 200).</summary>
    public static string? Normalizar(string? valor)
    {
        var s = (valor ?? "").Trim().ToLowerInvariant();
        if (s.Length is < 6 or > 200 || s.Any(char.IsWhiteSpace) || s.Any(char.IsControl)) return null;
        var at = s.IndexOf('@');
        if (at < 1 || at != s.LastIndexOf('@')) return null;
        var dominio = s[(at + 1)..];
        return dominio.Length >= 3 && dominio.Contains('.') && !dominio.StartsWith('.') && !dominio.EndsWith('.') ? s : null;
    }
}

// ---------------------------------------------------------------------------------------------------------------
/// <summary>Solo Development y pruebas.</summary>
public sealed class MemoryAccesoStore : IAccesoStore
{
    private readonly object _l = new();
    private readonly Dictionary<int, (string IdCliente, string Nombre, string Servidor, string BaseDatos)> _central = new();
    private readonly Dictionary<int, BaseMcp> _bases = new();
    private readonly HashSet<(string Email, string IdCliente)> _usuarios = new();
    private readonly Dictionary<(string Email, int IdBase), AccesoListado> _accesos = new();
    private readonly List<(string Email, string Hash, DateTime Expira, int Intentos, bool Usado, DateTime Alta, string? Ticket, DateTime? TicketExpira)> _codigos = new();

    /// <summary>Atajo de pruebas: base central + habilitada + usuario del cliente + acceso activo.</summary>
    public void Sembrar(int idBase, string nombre, string idCliente, string email, string? connectionString = null, bool habilitada = true, bool acceso = true)
    {
        lock (_l)
        {
            var e = email.ToLowerInvariant();
            _central[idBase] = (idCliente, nombre, connectionString ?? "srv", "db" + idBase);
            _bases[idBase] = new BaseMcp(idBase, nombre, idCliente, connectionString ?? "srv", "db" + idBase, "mcp_dir_ro", "", habilitada);
            _usuarios.Add((e, idCliente));
            if (acceso) _accesos[(e, idBase)] = new AccesoListado(e, idBase, nombre, idCliente, true, null, null);
        }
    }

    public Task<IReadOnlyList<AccesoBase>> AccesosAsync(string email, CancellationToken ct)
    {
        lock (_l)
        {
            var e = email.ToLowerInvariant();
            IReadOnlyList<AccesoBase> r = _accesos.Values
                .Where(a => a.Email == e && a.Activo && _bases.TryGetValue(a.IdBase, out var b) && b.Habilitada && _usuarios.Contains((e, b.IdCliente)))
                .Select(a => new AccesoBase(a.IdBase, a.NombreBase, a.Cliente)).OrderBy(a => a.NombreBase).ToList();
            return Task.FromResult(r);
        }
    }

    public Task<BaseMcp?> GetBaseAsync(int idBase, CancellationToken ct) { lock (_l) return Task.FromResult(_bases.GetValueOrDefault(idBase)); }

    public Task RegistrarIngresoAsync(string email, int idBase, CancellationToken ct)
    {
        lock (_l) if (_accesos.TryGetValue((email, idBase), out var a)) _accesos[(email, idBase)] = a with { UltimoIngresoUtc = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task GuardarCodigoEmailAsync(string email, string codigoHash, DateTime expiraUtc, string? ip, CancellationToken ct)
    {
        lock (_l)
        {
            for (var i = 0; i < _codigos.Count; i++) if (_codigos[i].Email == email && !_codigos[i].Usado) _codigos[i] = _codigos[i] with { Usado = true };
            _codigos.Add((email, codigoHash, expiraUtc, 0, false, DateTime.UtcNow, null, null));
        }
        return Task.CompletedTask;
    }

    public Task<int> ContarCodigosEmailAsync(string email, DateTime desdeUtc, CancellationToken ct)
    { lock (_l) return Task.FromResult(_codigos.Count(c => c.Email == email && c.Alta >= desdeUtc)); }

    public Task<bool> ConsumirCodigoEmailAsync(string email, string codigoHash, DateTime ahoraUtc, int maxIntentos, CancellationToken ct)
    {
        lock (_l)
        {
            var i = _codigos.FindLastIndex(c => c.Email == email && !c.Usado && c.Expira > ahoraUtc);
            if (i < 0) return Task.FromResult(false);
            var c = _codigos[i];
            if (c.Intentos >= maxIntentos) return Task.FromResult(false);
            if (c.Hash != codigoHash) { _codigos[i] = c with { Intentos = c.Intentos + 1 }; return Task.FromResult(false); }
            _codigos[i] = c with { Usado = true };
            return Task.FromResult(true);
        }
    }

    public Task GuardarTicketAsync(string email, string ticketHash, DateTime expiraUtc, CancellationToken ct)
    { lock (_l) _codigos.Add((email, "", DateTime.MinValue, 0, true, DateTime.UtcNow, ticketHash, expiraUtc)); return Task.CompletedTask; }

    public Task<string?> ConsumirTicketAsync(string ticketHash, DateTime ahoraUtc, CancellationToken ct)
    {
        lock (_l)
        {
            var i = _codigos.FindIndex(c => c.Ticket == ticketHash && c.TicketExpira > ahoraUtc);
            if (i < 0) return Task.FromResult<string?>(null);
            var email = _codigos[i].Email;
            _codigos[i] = _codigos[i] with { Ticket = null, TicketExpira = null };
            return Task.FromResult<string?>(email);
        }
    }

    public Task<(string IdCliente, string Nombre, string Servidor, string BaseDatos)?> GetBaseCentralAsync(int idBase, CancellationToken ct)
    { lock (_l) return Task.FromResult<(string, string, string, string)?>(_central.TryGetValue(idBase, out var b) ? b : null); }

    public Task GuardarBaseAsync(int idBase, string usuarioSql, string claveCifrada, CancellationToken ct)
    {
        lock (_l)
        {
            var c = _central[idBase];
            _bases[idBase] = new BaseMcp(idBase, c.Nombre, c.IdCliente, c.Servidor, c.BaseDatos, usuarioSql, claveCifrada, true);
        }
        return Task.CompletedTask;
    }

    public Task<bool> EsUsuarioDelClienteAsync(string email, string idCliente, CancellationToken ct)
    { lock (_l) return Task.FromResult(_usuarios.Contains((email.ToLowerInvariant(), idCliente))); }

    public Task DarAccesoAsync(string email, int idBase, CancellationToken ct)
    {
        lock (_l) { var b = _bases[idBase]; _accesos[(email, idBase)] = new AccesoListado(email, idBase, b.NombreBase, b.IdCliente, true, null, null); }
        return Task.CompletedTask;
    }

    public Task MarcarInvitadoAsync(string email, int idBase, CancellationToken ct)
    {
        lock (_l) if (_accesos.TryGetValue((email, idBase), out var a)) _accesos[(email, idBase)] = a with { InvitadoUtc = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task<int> QuitarAccesoAsync(string email, int? idBase, CancellationToken ct)
    {
        lock (_l)
        {
            var claves = _accesos.Keys.Where(k => k.Email == email && (idBase is null || k.IdBase == idBase) && _accesos[k].Activo).ToList();
            foreach (var k in claves) _accesos[k] = _accesos[k] with { Activo = false };
            return Task.FromResult(claves.Count);
        }
    }

    public Task<IReadOnlyList<AccesoListado>> ListarAsync(int? idBase, CancellationToken ct)
    { lock (_l) return Task.FromResult<IReadOnlyList<AccesoListado>>(_accesos.Values.Where(a => idBase is null || a.IdBase == idBase).ToList()); }
}

// ---------------------------------------------------------------------------------------------------------------
/// <summary>ALFA_CENTRAL: tablas MCP_ACCESO / MCP_BASE / MCP_CODIGO_EMAIL (script 014) + lectura de users, bases y Clientes.</summary>
public sealed class SqlAccesoStore(string connectionString) : IAccesoStore
{
    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    // El mail tiene que seguir siendo usuario del cliente dueño de la base: si lo dan de baja en la administracion central, pierde el acceso.
    private const string JoinVigente = """
        FROM dbo.MCP_ACCESO a
        JOIN dbo.MCP_BASE mb ON mb.IdBase = a.IdBase AND mb.Habilitada = 1
        JOIN dbo.bases b     ON b.id = a.IdBase
        JOIN dbo.users u     ON LOWER(LTRIM(RTRIM(u.[user]))) = a.Email AND LTRIM(RTRIM(u.idcliente)) = LTRIM(RTRIM(b.idcliente))
        LEFT JOIN dbo.Clientes c ON LTRIM(RTRIM(c.idcliente)) = LTRIM(RTRIM(b.idcliente))
        """;

    public async Task<IReadOnlyList<AccesoBase>> AccesosAsync(string email, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QueryAsync<AccesoBase>(new CommandDefinition($"""
            SELECT DISTINCT a.IdBase, ISNULL(b.nombre, N'') AS NombreBase, ISNULL(c.nombre, N'') AS Cliente
            {JoinVigente}
            WHERE a.Email = @email AND a.Activo = 1
            ORDER BY 2
            """, new { email }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<BaseMcp?> GetBaseAsync(int idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<BaseMcp>(new CommandDefinition("""
            SELECT mb.IdBase, ISNULL(b.nombre, N'') AS NombreBase, LTRIM(RTRIM(b.idcliente)) AS IdCliente,
                   ISNULL(b.dbserver, N'') AS Servidor, ISNULL(b.dbname, N'') AS BaseDatos, mb.UsuarioSql, mb.ClaveCifrada, mb.Habilitada
            FROM dbo.MCP_BASE mb JOIN dbo.bases b ON b.id = mb.IdBase
            WHERE mb.IdBase = @idBase
            """, new { idBase }, cancellationToken: ct));
    }

    public async Task RegistrarIngresoAsync(string email, int idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MCP_ACCESO SET UltimoIngreso = GETUTCDATE() WHERE Email = @email AND IdBase = @idBase",
            new { email, idBase }, cancellationToken: ct));
    }

    public async Task GuardarCodigoEmailAsync(string email, string codigoHash, DateTime expiraUtc, string? ip, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.MCP_CODIGO_EMAIL SET Usado = 1 WHERE Email = @email AND Usado = 0;
            INSERT INTO dbo.MCP_CODIGO_EMAIL (Email, CodigoHash, Expira, Ip) VALUES (@email, @codigoHash, @expiraUtc, @ip);
            """, new { email, codigoHash, expiraUtc, ip }, cancellationToken: ct));
    }

    public async Task<int> ContarCodigosEmailAsync(string email, DateTime desdeUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.MCP_CODIGO_EMAIL WHERE Email = @email AND Alta >= @desdeUtc AND CodigoHash <> ''", new { email, desdeUtc }, cancellationToken: ct));
    }

    public async Task<bool> ConsumirCodigoEmailAsync(string email, string codigoHash, DateTime ahoraUtc, int maxIntentos, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        // UPDATE condicionado: dos envios simultaneos del mismo codigo => solo uno gana.
        var ok = await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.MCP_CODIGO_EMAIL SET Usado = 1
            WHERE Email = @email AND CodigoHash = @codigoHash AND Usado = 0 AND Expira > @ahoraUtc AND Intentos < @maxIntentos
            """, new { email, codigoHash, ahoraUtc, maxIntentos }, cancellationToken: ct)) == 1;
        if (!ok)
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.MCP_CODIGO_EMAIL SET Intentos = Intentos + 1 WHERE Email = @email AND Usado = 0 AND Expira > @ahoraUtc",
                new { email, ahoraUtc }, cancellationToken: ct));
        return ok;
    }

    public async Task GuardarTicketAsync(string email, string ticketHash, DateTime expiraUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.MCP_CODIGO_EMAIL (Email, CodigoHash, Expira, Usado, TicketHash, TicketExpira) VALUES (@email, '', @expiraUtc, 1, @ticketHash, @expiraUtc)",
            new { email, ticketHash, expiraUtc }, cancellationToken: ct));
    }

    public async Task<string?> ConsumirTicketAsync(string ticketHash, DateTime ahoraUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            UPDATE dbo.MCP_CODIGO_EMAIL SET TicketHash = NULL, TicketExpira = NULL
            OUTPUT deleted.Email
            WHERE TicketHash = @ticketHash AND TicketExpira > @ahoraUtc
            """, new { ticketHash, ahoraUtc }, cancellationToken: ct));
    }

    public async Task<(string IdCliente, string Nombre, string Servidor, string BaseDatos)?> GetBaseCentralAsync(int idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QuerySingleOrDefaultAsync<(string IdCliente, string Nombre, string Servidor, string BaseDatos)?>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(idcliente)), ISNULL(nombre, N''), ISNULL(dbserver, N''), ISNULL(dbname, N'') FROM dbo.bases WHERE id = @idBase",
            new { idBase }, cancellationToken: ct));
        return r;
    }

    public async Task GuardarBaseAsync(int idBase, string usuarioSql, string claveCifrada, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.MCP_BASE WHERE IdBase = @idBase)
                UPDATE dbo.MCP_BASE SET UsuarioSql = @usuarioSql, ClaveCifrada = @claveCifrada, Habilitada = 1, Modificado = GETUTCDATE() WHERE IdBase = @idBase;
            ELSE
                INSERT INTO dbo.MCP_BASE (IdBase, UsuarioSql, ClaveCifrada) VALUES (@idBase, @usuarioSql, @claveCifrada);
            """, new { idBase, usuarioSql, claveCifrada }, cancellationToken: ct));
    }

    public async Task<bool> EsUsuarioDelClienteAsync(string email, string idCliente, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.users WHERE LOWER(LTRIM(RTRIM([user]))) = @email AND LTRIM(RTRIM(idcliente)) = @idCliente",
            new { email, idCliente }, cancellationToken: ct)) > 0;
    }

    public async Task DarAccesoAsync(string email, int idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.MCP_ACCESO WHERE Email = @email AND IdBase = @idBase)
                UPDATE dbo.MCP_ACCESO SET Activo = 1 WHERE Email = @email AND IdBase = @idBase;
            ELSE
                INSERT INTO dbo.MCP_ACCESO (Email, IdBase) VALUES (@email, @idBase);
            """, new { email, idBase }, cancellationToken: ct));
    }

    public async Task MarcarInvitadoAsync(string email, int idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MCP_ACCESO SET Invitado = GETUTCDATE() WHERE Email = @email AND IdBase = @idBase",
            new { email, idBase }, cancellationToken: ct));
    }

    public async Task<int> QuitarAccesoAsync(string email, int? idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.MCP_ACCESO SET Activo = 0 WHERE Email = @email AND Activo = 1 AND (@idBase IS NULL OR IdBase = @idBase)",
            new { email, idBase }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AccesoListado>> ListarAsync(int? idBase, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QueryAsync<AccesoListado>(new CommandDefinition("""
            SELECT a.Email, a.IdBase, ISNULL(b.nombre, N'') AS NombreBase, ISNULL(c.nombre, N'') AS Cliente, a.Activo,
                   a.Invitado AS InvitadoUtc, a.UltimoIngreso AS UltimoIngresoUtc
            FROM dbo.MCP_ACCESO a
            LEFT JOIN dbo.bases b ON b.id = a.IdBase
            LEFT JOIN dbo.Clientes c ON LTRIM(RTRIM(c.idcliente)) = LTRIM(RTRIM(b.idcliente))
            WHERE @idBase IS NULL OR a.IdBase = @idBase
            ORDER BY 4, 3, 1
            """, new { idBase }, cancellationToken: ct));
        return r.ToList();
    }
}

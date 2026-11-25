using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Mcp.Auth;

public sealed record UsuarioAuth(int Id, string Usuario, string Nombre, string ClaveHash, bool Activo, int Intentos, DateTime? BloqueadoHasta, string? Base);
public sealed record ClienteOAuth(string ClientId, string Nombre, IReadOnlyList<string> RedirectUris, DateTime AltaUtc);
public sealed record CodigoAuth(string Hash, string ClientId, int UsuarioId, string RedirectUri, string CodeChallenge, string Scope, string Resource, DateTime ExpiraUtc);
public sealed record RefreshAuth(string Hash, string ClientId, int UsuarioId, string Scope, string Resource, DateTime ExpiraUtc, Guid Familia);

/// <summary>
/// Persistencia del servidor OAuth. Los codigos y refresh tokens se guardan SOLO como hash; ninguna operacion devuelve el valor en claro.
/// Hay dos implementaciones: SQL (produccion, tablas MCP_* con un login propio) y memoria (solo Development y pruebas).
/// </summary>
public interface IAuthStore
{
    Task<UsuarioAuth?> GetUsuarioAsync(string usuario, CancellationToken ct);
    Task<UsuarioAuth?> GetUsuarioPorIdAsync(int id, CancellationToken ct);
    Task RegistrarLoginAsync(int id, bool ok, int intentos, DateTime? bloqueadoHastaUtc, CancellationToken ct);
    Task<int> CrearUsuarioAsync(string usuario, string nombre, string claveHash, string? baseId, CancellationToken ct);

    Task<int> ContarClientesAsync(CancellationToken ct);
    Task AddClienteAsync(ClienteOAuth cliente, CancellationToken ct);
    Task<ClienteOAuth?> GetClienteAsync(string clientId, CancellationToken ct);

    Task AddCodigoAsync(CodigoAuth codigo, CancellationToken ct);
    /// <summary>Consume el codigo de forma atomica. Reutilizado=true si ya estaba usado (posible robo: el llamador revoca).</summary>
    Task<(CodigoAuth? Codigo, bool Reutilizado)> ConsumirCodigoAsync(string hash, DateTime ahoraUtc, CancellationToken ct);

    Task AddRefreshAsync(RefreshAuth token, CancellationToken ct);
    /// <summary>Invalido=true si el token estaba revocado o ya reemplazado (reuso => se revoca toda la familia).</summary>
    Task<(RefreshAuth? Token, bool Invalido)> GetRefreshAsync(string hash, CancellationToken ct);
    /// <summary>Marca el viejo como reemplazado y graba el nuevo, de forma atomica. False si otro request ya lo uso.</summary>
    Task<bool> RotarRefreshAsync(string hashViejo, RefreshAuth nuevo, DateTime ahoraUtc, CancellationToken ct);
    Task RevocarFamiliaAsync(Guid familia, CancellationToken ct);
    Task RevocarUsuarioClienteAsync(int usuarioId, string clientId, CancellationToken ct);

    Task PurgarVencidosAsync(DateTime ahoraUtc, CancellationToken ct);
    Task EventoAsync(string evento, string? usuario, string? clientId, string? ip, string? detalle, CancellationToken ct);
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class MemoryAuthStore : IAuthStore
{
    private readonly object _l = new();
    private readonly Dictionary<int, UsuarioAuth> _usuarios = new();
    private readonly Dictionary<string, ClienteOAuth> _clientes = new();
    private readonly Dictionary<string, (CodigoAuth C, bool Usado)> _codigos = new();
    private readonly Dictionary<string, (RefreshAuth T, bool Revocado, bool Reemplazado)> _refresh = new();
    private int _seq;
    public List<(string Evento, string? Usuario, string? ClientId, string? Detalle)> Eventos { get; } = new();

    public Task<UsuarioAuth?> GetUsuarioAsync(string usuario, CancellationToken ct)
    { lock (_l) return Task.FromResult(_usuarios.Values.FirstOrDefault(u => string.Equals(u.Usuario, usuario, StringComparison.OrdinalIgnoreCase))); }

    public Task<UsuarioAuth?> GetUsuarioPorIdAsync(int id, CancellationToken ct)
    { lock (_l) return Task.FromResult(_usuarios.GetValueOrDefault(id)); }

    public Task RegistrarLoginAsync(int id, bool ok, int intentos, DateTime? bloqueadoHastaUtc, CancellationToken ct)
    { lock (_l) if (_usuarios.TryGetValue(id, out var u)) _usuarios[id] = u with { Intentos = intentos, BloqueadoHasta = bloqueadoHastaUtc }; return Task.CompletedTask; }

    public Task<int> CrearUsuarioAsync(string usuario, string nombre, string claveHash, string? baseId, CancellationToken ct)
    {
        lock (_l)
        {
            if (_usuarios.Values.Any(u => string.Equals(u.Usuario, usuario, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"El usuario '{usuario}' ya existe.");
            var id = ++_seq;
            _usuarios[id] = new UsuarioAuth(id, usuario, nombre, claveHash, true, 0, null, baseId);
            return Task.FromResult(id);
        }
    }

    public Task<int> ContarClientesAsync(CancellationToken ct) { lock (_l) return Task.FromResult(_clientes.Count); }
    public Task AddClienteAsync(ClienteOAuth c, CancellationToken ct) { lock (_l) _clientes[c.ClientId] = c; return Task.CompletedTask; }
    public Task<ClienteOAuth?> GetClienteAsync(string clientId, CancellationToken ct) { lock (_l) return Task.FromResult(_clientes.GetValueOrDefault(clientId)); }

    public Task AddCodigoAsync(CodigoAuth c, CancellationToken ct) { lock (_l) _codigos[c.Hash] = (c, false); return Task.CompletedTask; }

    public Task<(CodigoAuth? Codigo, bool Reutilizado)> ConsumirCodigoAsync(string hash, DateTime ahora, CancellationToken ct)
    {
        lock (_l)
        {
            if (!_codigos.TryGetValue(hash, out var e)) return Task.FromResult<(CodigoAuth?, bool)>((null, false));
            if (e.Usado) return Task.FromResult<(CodigoAuth?, bool)>((e.C, true));
            if (e.C.ExpiraUtc <= ahora) return Task.FromResult<(CodigoAuth?, bool)>((null, false));
            _codigos[hash] = (e.C, true);
            return Task.FromResult<(CodigoAuth?, bool)>((e.C, false));
        }
    }

    public Task AddRefreshAsync(RefreshAuth t, CancellationToken ct) { lock (_l) _refresh[t.Hash] = (t, false, false); return Task.CompletedTask; }

    public Task<(RefreshAuth? Token, bool Invalido)> GetRefreshAsync(string hash, CancellationToken ct)
    {
        lock (_l)
        {
            if (!_refresh.TryGetValue(hash, out var e)) return Task.FromResult<(RefreshAuth?, bool)>((null, false));
            return Task.FromResult<(RefreshAuth?, bool)>((e.T, e.Revocado || e.Reemplazado));
        }
    }

    public Task<bool> RotarRefreshAsync(string hashViejo, RefreshAuth nuevo, DateTime ahora, CancellationToken ct)
    {
        lock (_l)
        {
            if (!_refresh.TryGetValue(hashViejo, out var e) || e.Revocado || e.Reemplazado || e.T.ExpiraUtc <= ahora) return Task.FromResult(false);
            _refresh[hashViejo] = (e.T, e.Revocado, true);
            _refresh[nuevo.Hash] = (nuevo, false, false);
            return Task.FromResult(true);
        }
    }

    public Task RevocarFamiliaAsync(Guid familia, CancellationToken ct)
    {
        lock (_l) foreach (var k in _refresh.Where(x => x.Value.T.Familia == familia).Select(x => x.Key).ToList()) _refresh[k] = (_refresh[k].T, true, _refresh[k].Reemplazado);
        return Task.CompletedTask;
    }

    public Task RevocarUsuarioClienteAsync(int usuarioId, string clientId, CancellationToken ct)
    {
        lock (_l) foreach (var k in _refresh.Where(x => x.Value.T.UsuarioId == usuarioId && x.Value.T.ClientId == clientId).Select(x => x.Key).ToList()) _refresh[k] = (_refresh[k].T, true, _refresh[k].Reemplazado);
        return Task.CompletedTask;
    }

    public Task PurgarVencidosAsync(DateTime ahora, CancellationToken ct)
    {
        lock (_l)
        {
            foreach (var k in _codigos.Where(x => x.Value.C.ExpiraUtc <= ahora.AddHours(-1)).Select(x => x.Key).ToList()) _codigos.Remove(k);
            foreach (var k in _refresh.Where(x => x.Value.T.ExpiraUtc <= ahora).Select(x => x.Key).ToList()) _refresh.Remove(k);
        }
        return Task.CompletedTask;
    }

    public Task EventoAsync(string evento, string? usuario, string? clientId, string? ip, string? detalle, CancellationToken ct)
    { lock (_l) Eventos.Add((evento, usuario, clientId, detalle)); return Task.CompletedTask; }
}

// ---------------------------------------------------------------------------------------------------------------
/// <summary>Tablas MCP_* (script 010_oauth_tablas.sql). Todas las operaciones sensibles son UPDATE condicionados: son atomicas.</summary>
public sealed class SqlAuthStore(string connectionString, ILogger<SqlAuthStore> log) : IAuthStore
{
    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private sealed class UsuarioRow
    {
        public int Id { get; set; }
        public string Usuario { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string ClaveHash { get; set; } = "";
        public bool Activo { get; set; }
        public int Intentos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
        public string? Base { get; set; }
        public UsuarioAuth ToModel() => new(Id, Usuario, Nombre, ClaveHash, Activo, Intentos, BloqueadoHasta, Base);
    }

    private const string SelUsuario = "SELECT Id, Usuario, Nombre, ClaveHash, Activo, Intentos, BloqueadoHasta, Base FROM dbo.MCP_USUARIO";

    public async Task<UsuarioAuth?> GetUsuarioAsync(string usuario, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QuerySingleOrDefaultAsync<UsuarioRow>(new CommandDefinition(SelUsuario + " WHERE Usuario = @usuario", new { usuario }, cancellationToken: ct));
        return r?.ToModel();
    }

    public async Task<UsuarioAuth?> GetUsuarioPorIdAsync(int id, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QuerySingleOrDefaultAsync<UsuarioRow>(new CommandDefinition(SelUsuario + " WHERE Id = @id", new { id }, cancellationToken: ct));
        return r?.ToModel();
    }

    public async Task RegistrarLoginAsync(int id, bool ok, int intentos, DateTime? bloqueadoHastaUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.MCP_USUARIO SET Intentos = @intentos, BloqueadoHasta = @bloqueadoHastaUtc, " +
            "UltimoLogin = CASE WHEN @ok = 1 THEN GETUTCDATE() ELSE UltimoLogin END WHERE Id = @id",
            new { id, ok, intentos, bloqueadoHastaUtc }, cancellationToken: ct));
    }

    public async Task<int> CrearUsuarioAsync(string usuario, string nombre, string claveHash, string? baseId, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO dbo.MCP_USUARIO (Usuario, Nombre, ClaveHash, Base) VALUES (@usuario, @nombre, @claveHash, @baseId); SELECT CAST(SCOPE_IDENTITY() AS int)",
            new { usuario, nombre, claveHash, baseId }, cancellationToken: ct));
    }

    public async Task<int> ContarClientesAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.MCP_CLIENTE_OAUTH", cancellationToken: ct));
    }

    public async Task AddClienteAsync(ClienteOAuth c, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.MCP_CLIENTE_OAUTH (ClientId, Nombre, RedirectUris, Alta) VALUES (@ClientId, @Nombre, @Uris, @Alta)",
            new { c.ClientId, c.Nombre, Uris = string.Join("\n", c.RedirectUris), Alta = c.AltaUtc }, cancellationToken: ct));
    }

    public async Task<ClienteOAuth?> GetClienteAsync(string clientId, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QuerySingleOrDefaultAsync<(string ClientId, string Nombre, string RedirectUris, DateTime Alta)?>(new CommandDefinition(
            "SELECT ClientId, Nombre, RedirectUris, Alta FROM dbo.MCP_CLIENTE_OAUTH WHERE ClientId = @clientId", new { clientId }, cancellationToken: ct));
        return r is null ? null : new ClienteOAuth(r.Value.ClientId, r.Value.Nombre, r.Value.RedirectUris.Split('\n', StringSplitOptions.RemoveEmptyEntries), r.Value.Alta);
    }

    public async Task AddCodigoAsync(CodigoAuth c, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.MCP_CODIGO (CodigoHash, ClientId, UsuarioId, RedirectUri, CodeChallenge, Scope, Resource, Expira) " +
            "VALUES (@Hash, @ClientId, @UsuarioId, @RedirectUri, @CodeChallenge, @Scope, @Resource, @ExpiraUtc)", c, cancellationToken: ct));
    }

    private sealed class CodigoRow
    {
        public string CodigoHash { get; set; } = ""; public string ClientId { get; set; } = ""; public int UsuarioId { get; set; }
        public string RedirectUri { get; set; } = ""; public string CodeChallenge { get; set; } = ""; public string Scope { get; set; } = "";
        public string Resource { get; set; } = ""; public DateTime Expira { get; set; }
        public CodigoAuth ToModel() => new(CodigoHash, ClientId, UsuarioId, RedirectUri, CodeChallenge, Scope, Resource, Expira);
    }

    public async Task<(CodigoAuth? Codigo, bool Reutilizado)> ConsumirCodigoAsync(string hash, DateTime ahoraUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        // UPDATE condicionado: solo uno de dos requests simultaneos puede ganar.
        var filas = await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.MCP_CODIGO SET Usado = 1 WHERE CodigoHash = @hash AND Usado = 0 AND Expira > @ahoraUtc", new { hash, ahoraUtc }, cancellationToken: ct));
        var row = await cn.QuerySingleOrDefaultAsync<CodigoRow>(new CommandDefinition(
            "SELECT CodigoHash, ClientId, UsuarioId, RedirectUri, CodeChallenge, Scope, Resource, Expira FROM dbo.MCP_CODIGO WHERE CodigoHash = @hash", new { hash }, cancellationToken: ct));
        if (row is null) return (null, false);
        if (filas == 1) return (row.ToModel(), false);
        return row.Expira <= ahoraUtc ? (null, false) : (row.ToModel(), true); // existe, no se pudo consumir y no vencio => ya estaba usado
    }

    public async Task AddRefreshAsync(RefreshAuth t, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await InsertarRefreshAsync(cn, t, ct);
    }

    private static Task InsertarRefreshAsync(SqlConnection cn, RefreshAuth t, CancellationToken ct) => cn.ExecuteAsync(new CommandDefinition(
        "INSERT INTO dbo.MCP_REFRESH_TOKEN (TokenHash, ClientId, UsuarioId, Scope, Resource, Expira, Familia) VALUES (@Hash, @ClientId, @UsuarioId, @Scope, @Resource, @ExpiraUtc, @Familia)", t, cancellationToken: ct));

    private sealed class RefreshRow
    {
        public string TokenHash { get; set; } = ""; public string ClientId { get; set; } = ""; public int UsuarioId { get; set; }
        public string Scope { get; set; } = ""; public string Resource { get; set; } = ""; public DateTime Expira { get; set; }
        public Guid Familia { get; set; } public bool Revocado { get; set; } public bool Reemplazado { get; set; }
    }

    public async Task<(RefreshAuth? Token, bool Invalido)> GetRefreshAsync(string hash, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var r = await cn.QuerySingleOrDefaultAsync<RefreshRow>(new CommandDefinition(
            "SELECT TokenHash, ClientId, UsuarioId, Scope, Resource, Expira, Familia, Revocado, Reemplazado FROM dbo.MCP_REFRESH_TOKEN WHERE TokenHash = @hash", new { hash }, cancellationToken: ct));
        return r is null ? (null, false) : (new RefreshAuth(r.TokenHash, r.ClientId, r.UsuarioId, r.Scope, r.Resource, r.Expira, r.Familia), r.Revocado || r.Reemplazado);
    }

    public async Task<bool> RotarRefreshAsync(string hashViejo, RefreshAuth nuevo, DateTime ahoraUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var filas = await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.MCP_REFRESH_TOKEN SET Reemplazado = 1 WHERE TokenHash = @hashViejo AND Revocado = 0 AND Reemplazado = 0 AND Expira > @ahoraUtc",
            new { hashViejo, ahoraUtc }, tx, cancellationToken: ct));
        if (filas != 1) { await tx.RollbackAsync(ct); return false; }
        await cn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO dbo.MCP_REFRESH_TOKEN (TokenHash, ClientId, UsuarioId, Scope, Resource, Expira, Familia) VALUES (@Hash, @ClientId, @UsuarioId, @Scope, @Resource, @ExpiraUtc, @Familia)", nuevo, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RevocarFamiliaAsync(Guid familia, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MCP_REFRESH_TOKEN SET Revocado = 1 WHERE Familia = @familia", new { familia }, cancellationToken: ct));
    }

    public async Task RevocarUsuarioClienteAsync(int usuarioId, string clientId, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MCP_REFRESH_TOKEN SET Revocado = 1 WHERE UsuarioId = @usuarioId AND ClientId = @clientId", new { usuarioId, clientId }, cancellationToken: ct));
    }

    public async Task PurgarVencidosAsync(DateTime ahoraUtc, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.MCP_CODIGO WHERE Expira < DATEADD(hour, -1, @ahoraUtc); DELETE FROM dbo.MCP_REFRESH_TOKEN WHERE Expira < @ahoraUtc; " +
            "DELETE FROM dbo.MCP_EVENTO WHERE Fecha < DATEADD(day, -180, @ahoraUtc)", new { ahoraUtc }, cancellationToken: ct));
    }

    public async Task EventoAsync(string evento, string? usuario, string? clientId, string? ip, string? detalle, CancellationToken ct)
    {
        try
        {
            await using var cn = await AbrirAsync(ct);
            await cn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.MCP_EVENTO (Evento, Usuario, ClientId, Ip, Detalle) VALUES (@evento, @usuario, @clientId, @ip, @detalle)",
                new { evento, usuario, clientId, ip, detalle = detalle is { Length: > 1000 } ? detalle[..1000] : detalle }, cancellationToken: ct));
        }
        catch (Exception ex) { log.LogError(ex, "No se pudo grabar MCP_EVENTO ({Evento}).", evento); }
    }
}

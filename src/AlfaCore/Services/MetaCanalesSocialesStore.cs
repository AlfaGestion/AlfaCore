using System.Collections.Concurrent;
using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Acceso a ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES: qué cuenta de Instagram o página de Facebook
/// pertenece a qué base. Lo usa el webhook central para saber a qué cliente entregar cada mensaje.
/// </summary>
public sealed class MetaCanalesSocialesStore(IConfiguration configuration)
{
    private static volatile bool _esquemaListo;

    private const string SelectConexion = """
        SELECT IdConexion, IdBase, Canal, CuentaId, CuentaIdAlterno, CuentaNombre, CuentaUsuario, FotoUrl, Estado,
               TokenVenceUtc, FechaRenovacionUtc, UltimoError, ConectadoPor, FechaConexion
        FROM dbo.CONV_CANALES_SOCIALES
        """;

    public bool CentralDisponible => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AlfaCentral"));

    public async Task<MetaCanalConexionDto?> GetActivaPorBaseAsync(int idBase, string canal, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        return await cn.QueryFirstOrDefaultAsync<MetaCanalConexionDto>(new CommandDefinition(
            SelectConexion + " WHERE IdBase = @IdBase AND Canal = @Canal AND Estado = 'ACTIVA' ORDER BY IdConexion DESC;",
            new { IdBase = idBase, Canal = canal }, cancellationToken: ct));
    }

    /// <summary>Base dueña de la cuenta (por id principal o alterno). Null si nadie la conectó.</summary>
    public async Task<int?> ResolverBaseAsync(string canal, string cuentaId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuentaId))
            return null;
        await using var cn = await AbrirAsync(ct);
        return await cn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT TOP (1) IdBase FROM dbo.CONV_CANALES_SOCIALES
            WHERE Canal = @Canal AND Estado = 'ACTIVA' AND (CuentaId = @CuentaId OR CuentaIdAlterno = @CuentaId)
            ORDER BY CASE WHEN CuentaId = @CuentaId THEN 0 ELSE 1 END, IdConexion DESC;
            """, new { Canal = canal, CuentaId = cuentaId.Trim() }, cancellationToken: ct));
    }

    /// <summary>
    /// Deja la cuenta conectada a la base. Si la base tenía otra cuenta del mismo canal, la desconecta
    /// (una cuenta por canal y por base). Si la cuenta está activa en OTRA base, no la pisa: devuelve esa base.
    /// </summary>
    public async Task<(bool Ok, int? BaseQueLaTiene)> ConectarAsync(MetaCanalConexionDto conexion, string usuario, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var otraBase = await cn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT TOP (1) IdBase FROM dbo.CONV_CANALES_SOCIALES WITH (UPDLOCK, HOLDLOCK)
            WHERE Canal = @Canal AND CuentaId = @CuentaId AND Estado = 'ACTIVA' AND IdBase <> @IdBase;
            """, new { conexion.Canal, conexion.CuentaId, conexion.IdBase }, tx, cancellationToken: ct));
        if (otraBase is > 0)
        {
            await tx.RollbackAsync(ct);
            return (false, otraBase);
        }

        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.CONV_CANALES_SOCIALES
            SET Estado = 'DESCONECTADA', DesconectadoPor = @Usuario, FechaDesconexion = GETDATE()
            WHERE IdBase = @IdBase AND Canal = @Canal AND Estado = 'ACTIVA';

            INSERT dbo.CONV_CANALES_SOCIALES
                (IdBase, Canal, CuentaId, CuentaIdAlterno, CuentaNombre, CuentaUsuario, FotoUrl, Estado, TokenVenceUtc, ConectadoPor, FechaConexion)
            VALUES
                (@IdBase, @Canal, @CuentaId, @CuentaIdAlterno, @CuentaNombre, @CuentaUsuario, @FotoUrl, 'ACTIVA', @TokenVenceUtc, @Usuario, GETDATE());
            """, new
        {
            conexion.IdBase,
            conexion.Canal,
            CuentaId = conexion.CuentaId.Trim(),
            CuentaIdAlterno = string.IsNullOrWhiteSpace(conexion.CuentaIdAlterno) ? null : conexion.CuentaIdAlterno.Trim(),
            CuentaNombre = Recortar(conexion.CuentaNombre, 200),
            CuentaUsuario = Recortar(conexion.CuentaUsuario, 200),
            FotoUrl = Recortar(conexion.FotoUrl, 1000),
            conexion.TokenVenceUtc,
            Usuario = Recortar(usuario, 100)
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (true, null);
    }

    public async Task DesconectarAsync(int idBase, string canal, string usuario, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.CONV_CANALES_SOCIALES
            SET Estado = 'DESCONECTADA', DesconectadoPor = @Usuario, FechaDesconexion = GETDATE()
            WHERE IdBase = @IdBase AND Canal = @Canal AND Estado = 'ACTIVA';
            """, new { IdBase = idBase, Canal = canal, Usuario = Recortar(usuario, 100) }, cancellationToken: ct));
    }

    /// <summary>Conexiones de Instagram cuyo token vence antes de <paramref name="limiteUtc"/> (para renovarlas).</summary>
    public Task<IReadOnlyList<MetaCanalConexionDto>> GetInstagramPorVencerAsync(DateTime limiteUtc, CancellationToken ct)
        => GetPorVencerAsync(MetaCanalSocial.Instagram, limiteUtc, TimeSpan.FromHours(20), ct);

    /// <summary>
    /// Conexiones activas del canal cuyo token vence antes de <paramref name="limiteUtc"/>, salteando las que
    /// se intentaron renovar hace menos de <paramref name="esperaEntreIntentos"/> (así un error no se repite en cada vuelta).
    /// </summary>
    public async Task<IReadOnlyList<MetaCanalConexionDto>> GetPorVencerAsync(string canal, DateTime limiteUtc, TimeSpan esperaEntreIntentos, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        var rows = await cn.QueryAsync<MetaCanalConexionDto>(new CommandDefinition(
            SelectConexion + """
             WHERE Canal = @Canal AND Estado = 'ACTIVA'
               AND TokenVenceUtc IS NOT NULL AND TokenVenceUtc < @Limite
               AND (FechaRenovacionUtc IS NULL OR FechaRenovacionUtc < DATEADD(MINUTE, -@EsperaMinutos, SYSUTCDATETIME()));
            """, new { Canal = canal, Limite = limiteUtc, EsperaMinutos = (int)Math.Max(1, esperaEntreIntentos.TotalMinutes) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task RegistrarRenovacionAsync(int idConexion, DateTime? venceUtc, string? error, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.CONV_CANALES_SOCIALES
            SET FechaRenovacionUtc = SYSUTCDATETIME(),
                TokenVenceUtc = CASE WHEN @Error IS NULL AND @VenceUtc IS NOT NULL THEN @VenceUtc ELSE TokenVenceUtc END,
                UltimoError = @Error
            WHERE IdConexion = @IdConexion;
            """, new { IdConexion = idConexion, VenceUtc = venceUtc, Error = Recortar(error, 500) }, cancellationToken: ct));
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("AlfaCentral");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Falta configurar la conexión central (ConnectionStrings:AlfaCentral).");
        var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        if (!_esquemaListo)
        {
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(CentralCanalesSocialesSchema.VerificacionSql, cancellationToken: ct)) == 0)
                await cn.ExecuteAsync(new CommandDefinition(CentralCanalesSocialesSchema.Script, commandTimeout: 120, cancellationToken: ct));
            _esquemaListo = true;
        }

        return cn;
    }

    private static string? Recortar(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>
/// Conexiones en curso (en memoria, 20 minutos). Meta vuelve al callback en el mismo servidor desde
/// el que se tocó "Conectar" (la URL de vuelta usa ese host), así que no hace falta compartirlas.
/// </summary>
public sealed class MetaConexionesPendientes
{
    internal static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(20);
    private readonly ConcurrentDictionary<string, MetaConexionPendiente> _items = new(StringComparer.Ordinal);

    public MetaConexionPendiente Crear(string canal, int idBase, string usuario, string redirectUri, string returnPath)
    {
        Purgar();
        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var item = new MetaConexionPendiente
        {
            Id = id,
            Canal = canal,
            IdBase = idBase,
            Usuario = usuario,
            RedirectUri = redirectUri,
            ReturnPath = returnPath
        };
        _items[id] = item;
        return item;
    }

    public MetaConexionPendiente? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_items.TryGetValue(id.Trim(), out var item))
            return null;
        if (DateTime.UtcNow - item.CreadaUtc > Vigencia)
        {
            _items.TryRemove(item.Id, out _);
            return null;
        }

        return item;
    }

    public void Quitar(string id) => _items.TryRemove(id, out _);

    private void Purgar()
    {
        var limite = DateTime.UtcNow - Vigencia;
        foreach (var pair in _items)
            if (pair.Value.CreadaUtc < limite)
                _items.TryRemove(pair.Key, out _);
    }
}

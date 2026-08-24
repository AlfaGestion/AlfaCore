using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Resuelve y genera links públicos (ALFA_CENTRAL.dbo.ALFA_PUBLIC_LINK) para catálogo y carrito.
/// El Token es la única fuente de autorización — nunca IdBase, nunca el Slug. Vive
/// exclusivamente contra la conexión central; una vez resuelto Token → IdWeb/IdBase/IdReferencia,
/// el resto del flujo usa los servicios de catálogo/carrito existentes sin cambios.
/// </summary>
public sealed class CentralPublicLinkService(IConfiguration configuration, IAppEventService appEvents) : ICentralPublicLinkService
{
    // Longitud fija del token: 18 bytes (144 bits de entropía) en Base64Url = exactamente 24
    // caracteres, sin padding. Al ser longitud fija, resolver "slug-token" o "token" solo es
    // trivial: siempre son los últimos TokenLength caracteres del segmento de ruta.
    private const int TokenBytes = 18;
    private const int TokenLength = 24;

    private const string SelectColumns = """
        IdPublicLink, IdWeb, IdBase, Tipo, IdReferencia, Token, ISNULL(Slug, '') AS Slug,
        Activo, FechaCreacion, FechaVencimiento
        """;

    private string ConnectionString => configuration.GetConnectionString("AlfaCentral")
        ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaCentral'.");

    public async Task<PublicLinkDto?> ResolveAsync(string idWeb, string tipo, string routeSegment, CancellationToken ct = default)
    {
        var token = ExtractToken(routeSegment);
        if (string.IsNullOrWhiteSpace(idWeb) || string.IsNullOrWhiteSpace(tipo) || token is null)
            return null;

        const string sql = $"""
            SELECT TOP (1) {SelectColumns}
            FROM dbo.ALFA_PUBLIC_LINK
            WHERE Token = @Token
              AND Tipo = @Tipo
              AND IdWeb = @IdWeb
              AND Activo = 1
              AND (FechaVencimiento IS NULL OR FechaVencimiento > SYSDATETIME());
            """;

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct).ConfigureAwait(false);
            return await cn.QuerySingleOrDefaultAsync<PublicLinkDto>(new CommandDefinition(
                sql,
                new { Token = token, Tipo = tipo, IdWeb = idWeb.Trim() },
                cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No se loguea el token completo: sólo un prefijo corto, suficiente para correlacionar
            // sin que un log expuesto sirva para reconstruir un link válido.
            await appEvents.LogErrorAsync(
                "PublicLink",
                "Resolve",
                ex,
                "No se pudo resolver el link público.",
                new { IdWeb = idWeb, Tipo = tipo, TokenPreview = TokenPreview(token) },
                AppEventSeverity.Error,
                ct);
            return null;
        }
    }

    public async Task<PublicLinkDto> GetOrCreateAsync(string idWeb, int idBase, string tipo, int idReferencia, string? nombreParaSlug, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idWeb);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var existing = await QueryExistingAsync(cn, idWeb, idBase, tipo, [idReferencia], ct).ConfigureAwait(false);
        if (existing.TryGetValue(idReferencia, out var found))
            return found;

        return await InsertAsync(cn, idWeb, idBase, tipo, idReferencia, nombreParaSlug, ct).ConfigureAwait(false);
    }

    public async Task<PublicLinkDto?> TryGetExistingAsync(string idWeb, int idBase, string tipo, int idReferencia, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idWeb);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var existing = await QueryExistingAsync(cn, idWeb, idBase, tipo, [idReferencia], ct).ConfigureAwait(false);
        return existing.TryGetValue(idReferencia, out var found) ? found : null;
    }

    public async Task<IReadOnlyDictionary<int, PublicLinkDto>> GetOrCreateManyAsync(
        string idWeb,
        int idBase,
        string tipo,
        IReadOnlyList<(int IdReferencia, string? Nombre)> referencias,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idWeb);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);

        var result = new Dictionary<int, PublicLinkDto>();
        if (referencias.Count == 0)
            return result;

        var ids = referencias.Select(r => r.IdReferencia).Distinct().ToArray();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var existing = await QueryExistingAsync(cn, idWeb, idBase, tipo, ids, ct).ConfigureAwait(false);
        foreach (var (idReferencia, link) in existing)
            result[idReferencia] = link;

        foreach (var (idReferencia, nombre) in referencias)
        {
            if (result.ContainsKey(idReferencia))
                continue;

            result[idReferencia] = await InsertAsync(cn, idWeb, idBase, tipo, idReferencia, nombre, ct).ConfigureAwait(false);
        }

        return result;
    }

    public async Task RevokeAsync(int idPublicLink, CancellationToken ct = default)
    {
        const string sql = "UPDATE dbo.ALFA_PUBLIC_LINK SET Activo = 0 WHERE IdPublicLink = @IdPublicLink;";

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await cn.ExecuteAsync(new CommandDefinition(sql, new { IdPublicLink = idPublicLink }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<PublicLinkDto> RegenerateAsync(int idPublicLink, CancellationToken ct = default)
    {
        const string selectSql = $"""
            SELECT TOP (1) {SelectColumns}
            FROM dbo.ALFA_PUBLIC_LINK
            WHERE IdPublicLink = @IdPublicLink;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct).ConfigureAwait(false);

        var current = await cn.QuerySingleOrDefaultAsync<PublicLinkDto>(new CommandDefinition(
            selectSql, new { IdPublicLink = idPublicLink }, cancellationToken: ct)).ConfigureAwait(false);

        if (current is null)
            throw new InvalidOperationException($"No existe el link público #{idPublicLink}.");

        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.ALFA_PUBLIC_LINK SET Activo = 0 WHERE IdPublicLink = @IdPublicLink;",
            new { IdPublicLink = idPublicLink },
            cancellationToken: ct)).ConfigureAwait(false);

        return await InsertAsync(cn, current.IdWeb, current.IdBase, current.Tipo, current.IdReferencia, current.Slug, ct)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyDictionary<int, PublicLinkDto>> QueryExistingAsync(
        SqlConnection cn,
        string idWeb,
        int idBase,
        string tipo,
        IReadOnlyCollection<int> idReferencias,
        CancellationToken ct)
    {
        var sql = $"""
            SELECT {SelectColumns}
            FROM dbo.ALFA_PUBLIC_LINK
            WHERE IdWeb = @IdWeb
              AND IdBase = @IdBase
              AND Tipo = @Tipo
              AND Activo = 1
              AND IdReferencia IN @IdReferencias;
            """;

        var rows = await cn.QueryAsync<PublicLinkDto>(new CommandDefinition(
            sql,
            new { IdWeb = idWeb.Trim(), IdBase = idBase, Tipo = tipo, IdReferencias = idReferencias },
            cancellationToken: ct)).ConfigureAwait(false);

        // Si por algún motivo hay más de un link activo para la misma referencia, se prioriza el
        // más nuevo — no debería pasar (GetOrCreate siempre reutiliza), pero no confiamos en que
        // nunca se haya insertado a mano.
        return rows
            .GroupBy(r => r.IdReferencia)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.FechaCreacion).First());
    }

    private static async Task<PublicLinkDto> InsertAsync(
        SqlConnection cn,
        string idWeb,
        int idBase,
        string tipo,
        int idReferencia,
        string? nombreParaSlug,
        CancellationToken ct)
    {
        var token = GenerateToken();
        var slug = Slugify(nombreParaSlug);

        const string insertSql = $"""
            INSERT INTO dbo.ALFA_PUBLIC_LINK (IdWeb, IdBase, Tipo, IdReferencia, Token, Slug, Activo, FechaCreacion)
            OUTPUT {InsertedColumns}
            VALUES (@IdWeb, @IdBase, @Tipo, @IdReferencia, @Token, @Slug, 1, SYSDATETIME());
            """;

        return await cn.QuerySingleAsync<PublicLinkDto>(new CommandDefinition(
            insertSql,
            new { IdWeb = idWeb.Trim(), IdBase = idBase, Tipo = tipo, IdReferencia = idReferencia, Token = token, Slug = slug },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private const string InsertedColumns = """
        INSERTED.IdPublicLink, INSERTED.IdWeb, INSERTED.IdBase, INSERTED.Tipo, INSERTED.IdReferencia,
        INSERTED.Token, ISNULL(INSERTED.Slug, '') AS Slug, INSERTED.Activo, INSERTED.FechaCreacion,
        INSERTED.FechaVencimiento
        """;

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_');

        // 18 bytes codifican exacto en 24 caracteres base64, así que nunca debería haber padding —
        // se recorta igual por si el tamaño de token cambia en el futuro.
        return token.TrimEnd('=');
    }

    /// <summary>
    /// Extrae el token de un segmento de ruta "slug-token" o "token" solo: siempre son los
    /// últimos <see cref="TokenLength"/> caracteres, sin importar cuántos guiones tenga el slug.
    /// </summary>
    private static string? ExtractToken(string routeSegment)
    {
        if (string.IsNullOrWhiteSpace(routeSegment))
            return null;

        var trimmed = routeSegment.Trim();
        if (trimmed.Length < TokenLength)
            return null;

        var candidate = trimmed[^TokenLength..];
        return IsValidTokenShape(candidate) ? candidate : null;
    }

    private static bool IsValidTokenShape(string candidate)
    {
        foreach (var c in candidate)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }

        return true;
    }

    private static string TokenPreview(string? token)
        => string.IsNullOrEmpty(token) ? string.Empty : token[..Math.Min(6, token.Length)] + "…";

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].TrimEnd('-');

        return slug;
    }
}

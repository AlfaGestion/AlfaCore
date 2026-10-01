using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Link personal de catálogo para un Cliente identificado (p. ej. por WhatsApp): el catálogo público
/// /{idweb}/catalogo/{slug-token} + "?c={credencial}". La credencial es opaca (AES-GCM: cifrada y
/// autenticada), nunca lleva el código de cliente en claro, y su alcance es obligatorio:
/// IdBase + IdWeb + catálogo (IdReferencia del link público) + cliente + vencimiento.
///
/// Clave: derivada por base (HKDF-SHA256) del WebhookToken de ALFA_CENTRAL.bases -- un secreto que ya
/// existe por base, server-side y estable entre reinicios y hosts (AlfaWeb/AlfaCentral). Una
/// credencial de una base no se puede ni descifrar con la clave de otra. Sin WebhookToken no se emiten
/// links personales (se comparte el público, sin precios).
///
/// Cualquier problema (vencida, alterada, de otra base/catálogo/idweb, cliente inexistente) se trata
/// como "sin identidad": el catálogo se muestra como a un anónimo, nunca con precios por error.
/// </summary>
public interface ICatalogoClienteLinkService
{
    /// <summary>Credencial para el cliente, o null si la base no permite emitirla.</summary>
    Task<string?> CrearCredencialAsync(string idWeb, int idBase, int idReferenciaCatalogo, string codigoCliente, CancellationToken ct = default);

    /// <summary>Cliente identificado por la credencial dentro de ESTE alcance, o null.</summary>
    Task<CatalogoClienteIdentificado?> ValidarCredencialAsync(string? credencial, string idWeb, int idBase, int idReferenciaCatalogo, CancellationToken ct = default);
}

public sealed record CatalogoClienteIdentificado(string CodigoCliente, string RazonSocial);

public sealed class CatalogoClienteLinkService(
    ICentralBasesService centralBases,
    ISaaSTenantRouteGuard tenantRouteGuard,
    ILogger<CatalogoClienteLinkService> logger) : ICatalogoClienteLinkService
{
    public const string QueryParameter = "c";
    internal static readonly TimeSpan Vigencia = TimeSpan.FromDays(7);
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const string KeyInfoPrefix = "AlfaCore.CatalogoClienteLink.v1|";

    public async Task<string?> CrearCredencialAsync(string idWeb, int idBase, int idReferenciaCatalogo, string codigoCliente, CancellationToken ct = default)
    {
        var codigo = (codigoCliente ?? string.Empty).Trim();
        if (idBase <= 0 || idReferenciaCatalogo <= 0 || codigo.Length == 0 || string.IsNullOrWhiteSpace(idWeb))
            return null;

        var clave = await ObtenerClaveAsync(idBase, ct);
        if (clave is null)
            return null;

        return Crear(clave, new CatalogoClienteLinkPayload(idBase, idWeb.Trim(), idReferenciaCatalogo, codigo,
            DateTimeOffset.UtcNow.Add(Vigencia).ToUnixTimeSeconds()));
    }

    public async Task<CatalogoClienteIdentificado?> ValidarCredencialAsync(string? credencial, string idWeb, int idBase, int idReferenciaCatalogo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credencial) || idBase <= 0 || idReferenciaCatalogo <= 0)
            return null;

        try
        {
            var clave = await ObtenerClaveAsync(idBase, ct);
            if (clave is null)
                return null;

            var payload = Leer(clave, credencial.Trim());
            if (!EsValidoParaAlcance(payload, idWeb, idBase, idReferenciaCatalogo, DateTimeOffset.UtcNow))
                return null;

            // El cliente tiene que existir en ESTA base (conexión validada contra idBase).
            var connection = tenantRouteGuard.GetRequiredConnection(idBase, "CatalogoClienteLink.Validar");
            await using var cn = new SqlConnection(connection.ConnectionString);
            await cn.OpenAsync(ct);
            var razon = await cn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                """
                SELECT TOP (1) ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), N'')
                FROM dbo.Vt_Clientes
                WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
                """,
                new { Codigo = payload!.CodigoCliente },
                cancellationToken: ct));
            return razon is null ? null : new CatalogoClienteIdentificado(payload.CodigoCliente, razon);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "CatalogoClienteLink: no se pudo validar la credencial (se trata como anónimo). IdBase={IdBase}", idBase);
            return null;
        }
    }

    private async Task<byte[]?> ObtenerClaveAsync(int idBase, CancellationToken ct)
    {
        var baseCentral = await centralBases.GetByIdAsync(idBase, ct);
        if (baseCentral is null || baseCentral.IdBase != idBase || string.IsNullOrWhiteSpace(baseCentral.WebhookToken))
            return null;
        return DerivarClave(baseCentral.WebhookToken, idBase);
    }

    internal static byte[] DerivarClave(string secretoBase, int idBase)
        => HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secretoBase.Trim()),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes(KeyInfoPrefix + idBase.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    internal static string Crear(byte[] clave, CatalogoClienteLinkPayload payload)
    {
        var plano = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cifrado = new byte[plano.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(clave, TagBytes))
            aes.Encrypt(nonce, plano, cifrado, tag);

        var salida = new byte[NonceBytes + cifrado.Length + TagBytes];
        nonce.CopyTo(salida, 0);
        cifrado.CopyTo(salida, NonceBytes);
        tag.CopyTo(salida, NonceBytes + cifrado.Length);
        return Base64UrlEncode(salida);
    }

    /// <summary>null si la credencial está alterada, mal formada o es de otra clave (otra base).</summary>
    internal static CatalogoClienteLinkPayload? Leer(byte[] clave, string credencial)
    {
        try
        {
            var datos = Base64UrlDecode(credencial);
            if (datos is null || datos.Length <= NonceBytes + TagBytes)
                return null;

            var nonce = datos.AsSpan(0, NonceBytes);
            var cifrado = datos.AsSpan(NonceBytes, datos.Length - NonceBytes - TagBytes);
            var tag = datos.AsSpan(datos.Length - TagBytes, TagBytes);
            var plano = new byte[cifrado.Length];
            using (var aes = new AesGcm(clave, TagBytes))
                aes.Decrypt(nonce, cifrado, tag, plano);

            return JsonSerializer.Deserialize<CatalogoClienteLinkPayload>(plano);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    internal static bool EsValidoParaAlcance(CatalogoClienteLinkPayload? payload, string idWeb, int idBase, int idReferenciaCatalogo, DateTimeOffset ahora)
        => payload is not null
           && payload.IdBase == idBase
           && payload.IdReferencia == idReferenciaCatalogo
           && string.Equals((payload.IdWeb ?? string.Empty).Trim(), (idWeb ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(payload.CodigoCliente)
           && payload.ExpiraUnix > ahora.ToUnixTimeSeconds();

    private static string Base64UrlEncode(byte[] datos)
        => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string texto)
    {
        var s = texto.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", 0 => s, _ => string.Empty };
        return s.Length == 0 ? null : Convert.FromBase64String(s);
    }
}

/// <summary>Contenido cifrado de la credencial. Nombres cortos: viaja en la URL.</summary>
public sealed record CatalogoClienteLinkPayload(
    [property: System.Text.Json.Serialization.JsonPropertyName("b")] int IdBase,
    [property: System.Text.Json.Serialization.JsonPropertyName("w")] string IdWeb,
    [property: System.Text.Json.Serialization.JsonPropertyName("i")] int IdReferencia,
    [property: System.Text.Json.Serialization.JsonPropertyName("c")] string CodigoCliente,
    [property: System.Text.Json.Serialization.JsonPropertyName("e")] long ExpiraUnix);

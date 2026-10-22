using AlfaCore.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace AlfaCore.Services;

/// <summary>
/// Sesiones de usuario de AlfaCore (token que el navegador guarda en localStorage).
///
/// 2026-10-22: antes el token era un Guid que sólo existía en este diccionario en memoria. Cada reinicio
/// del proceso (IIS lo recicla por inactividad, a la noche, y en cada deploy) borraba todas las sesiones:
/// las pestañas que quedaban abiertas perdían el usuario (imágenes de Conversaciones que no cargan, "no te
/// identifica") hasta volver a entrar por la raíz con mail y clave, varias veces por día.
///
/// Ahora el token lleva la sesión adentro, cifrada y firmada con Data Protection y con vencimiento: si el
/// proceso se reinició, se reconstruye desde el token. El diccionario queda como caché y para los tokens
/// viejos (Guid) que estén en uso al momento del deploy.
/// </summary>
public sealed class AppUserSessionStore
{
    internal const string ProtectedPrefix = "p1.";
    internal static readonly TimeSpan Vigencia = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, AppUserSessionInfo> _store = new();
    private readonly ConcurrentDictionary<string, byte> _revocados = new();
    private readonly ITimeLimitedDataProtector? _protector;

    public AppUserSessionStore(IDataProtectionProvider? dataProtection = null)
    {
        _protector = dataProtection?.CreateProtector("AlfaCore.UserSession", "v1").ToTimeLimitedDataProtector();
    }

    public string Store(AppUserSessionInfo info)
    {
        var token = _protector is null
            ? Guid.NewGuid().ToString("N")
            : ProtectedPrefix + _protector.Protect(JsonSerializer.Serialize(info), Vigencia);
        _store[token] = info;
        return token;
    }

    public void Upsert(string token, AppUserSessionInfo info)
        => _store[token] = info;

    /// <summary>
    /// Actualiza la sesión de un token y devuelve uno nuevo que ya trae los datos actualizados (el viejo
    /// sigue sirviendo mientras el proceso no se reinicie, para las otras pestañas abiertas).
    /// </summary>
    public string Reissue(string token, AppUserSessionInfo info)
    {
        Upsert(token, info);
        return _protector is null ? token : Store(info);
    }

    public bool TryGet(string token, out AppUserSessionInfo? info)
    {
        if (_store.TryGetValue(token, out info))
            return true;

        info = null;
        if (_protector is null || !token.StartsWith(ProtectedPrefix, StringComparison.Ordinal) || _revocados.ContainsKey(token))
            return false;

        try
        {
            var json = _protector.Unprotect(token[ProtectedPrefix.Length..]);
            info = JsonSerializer.Deserialize<AppUserSessionInfo>(json);
            if (info is null)
                return false;
            _store[token] = info;
            return true;
        }
        catch (CryptographicException)
        {
            // Vencido, adulterado o cifrado con claves que este servidor no tiene.
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Remove(string token)
    {
        _store.TryRemove(token, out _);
        if (token.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            _revocados[token] = 0;
    }
}

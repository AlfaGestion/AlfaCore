using System.Collections.Concurrent;

namespace AlfaCore.Services;

/// <summary>
/// Evita que la verificación de actualizaciones pendientes (<see cref="IActualizacionesService.ExecutePendingAsync"/>)
/// se dispare más de una vez por base durante la vida del proceso cuando la sesión se activa por URL/ticket
/// (<c>MainLayout.EnsureActiveSessionFromRouteAsync</c>). Sin esto, cada usuario que entra a la misma base en
/// modo SaaS repetiría la verificación — <see cref="TryClaim"/> es atómico, así que si varios entran a la vez
/// solo uno la ejecuta y el resto se salta el chequeo por completo.
/// </summary>
public sealed class RouteSessionUpdatesGuard
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.OrdinalIgnoreCase);

    public bool TryClaim(string servidor, string baseDatos)
    {
        var key = $"{servidor?.Trim()}|{baseDatos?.Trim()}".ToUpperInvariant();
        return _claimed.TryAdd(key, 0);
    }
}

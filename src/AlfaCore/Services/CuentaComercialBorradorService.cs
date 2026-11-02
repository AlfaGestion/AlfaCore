namespace AlfaCore.Services;

/// <summary>
/// Fichas de cliente/proveedor que quedaron a medio cargar cuando el usuario salió a otra
/// pantalla desde un botón inteligente (por ejemplo, a la cuenta corriente). Es scoped: vive
/// lo que dura el circuito de Blazor de esa pestaña del navegador, así que al volver a la
/// ficha se recupera lo que se estaba cargando sin haberlo guardado.
/// </summary>
public sealed class CuentaComercialBorradorService
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(4);
    private readonly Dictionary<string, CuentaComercialBorrador> _borradores = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Guardar(string clave, CuentaComercialBorrador borrador)
    {
        lock (_lock)
            _borradores[clave] = borrador;
    }

    /// <summary>Devuelve el borrador y lo quita: se recupera una sola vez.</summary>
    public CuentaComercialBorrador? Tomar(string clave)
    {
        lock (_lock)
        {
            if (!_borradores.Remove(clave, out var borrador))
                return null;
            return DateTime.UtcNow - borrador.FechaUtc > Vigencia ? null : borrador;
        }
    }
}

public sealed record CuentaComercialBorrador(string FormJson, string ClavePortal, string Pestana, DateTime FechaUtc);

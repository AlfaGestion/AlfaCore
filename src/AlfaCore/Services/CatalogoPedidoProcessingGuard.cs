using System.Collections.Concurrent;

namespace AlfaCore.Services;

/// <summary>
/// Evita procesar dos confirmaciones de pedido simultáneas para el mismo catálogo+cliente
/// (doble clic, doble tab, reintento de red superpuesto). No es un mecanismo de idempotencia
/// completo (no existía ninguno reutilizable en AlfaCore) — solo bloquea la ventana de
/// concurrencia mientras una confirmación anterior sigue en curso.
/// </summary>
public sealed class CatalogoPedidoProcessingGuard
{
    private readonly ConcurrentDictionary<string, byte> _enProceso = new(StringComparer.OrdinalIgnoreCase);

    public bool TryStart(int idInsert, string codigoCliente)
        => _enProceso.TryAdd(BuildKey(idInsert, codigoCliente), 0);

    public void Finish(int idInsert, string codigoCliente)
        => _enProceso.TryRemove(BuildKey(idInsert, codigoCliente), out _);

    private static string BuildKey(int idInsert, string codigoCliente)
        => $"{idInsert}:{(codigoCliente ?? string.Empty).Trim().ToUpperInvariant()}";
}

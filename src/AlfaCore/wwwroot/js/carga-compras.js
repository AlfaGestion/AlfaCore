// El movimiento de foco reutiliza cargaDirecta.focus (Ventas). Cada editor posee su handler;
// cerrar una página anterior no elimina los atajos de la nueva.
const handlers = new Map();
export function init(id, ref) {
    dispose(id);
    const handler = event => {
        if (!document.getElementById(id) || event.ctrlKey || event.altKey || event.metaKey) return;
        if (event.target?.dataset?.compraArticulos === id && ['ArrowUp', 'ArrowDown', 'Enter'].includes(event.key)) { event.preventDefault(); return; }
        if (event.key !== 'F5' && event.key !== 'F2') return;
        event.preventDefault();
        event.stopPropagation();
        ref.invokeMethodAsync(event.key === 'F5' ? 'BuscarArticuloDesdeTeclado' : 'BuscarProveedorDesdeTeclado').catch(() => {});
    };
    handlers.set(id, handler);
    document.addEventListener('keydown', handler, true);
}
export function dispose(id) {
    const handler = handlers.get(id);
    if (handler) document.removeEventListener('keydown', handler, true);
    handlers.delete(id);
}

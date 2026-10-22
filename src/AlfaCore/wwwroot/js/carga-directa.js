// Carga directa de comprobantes (Ventas): F5 abre la búsqueda de artículos como en Alfa Gestión, en vez de
// recargar la página y perder la carga (2026-10-22). Además mueve el foco entre los campos de la grilla.
window.cargaDirecta = (function () {
    let handler = null;

    function init(dotNetRef) {
        dispose();
        handler = function (event) {
            if (event.key !== 'F5' || event.ctrlKey || event.altKey || event.metaKey) return;
            event.preventDefault();
            event.stopPropagation();
            dotNetRef.invokeMethodAsync('AbrirBusquedaDesdeTeclado').catch(() => { });
        };
        document.addEventListener('keydown', handler, true);
    }

    function dispose() {
        if (handler) document.removeEventListener('keydown', handler, true);
        handler = null;
    }

    function focus(id) {
        // Se espera un cuadro para que Blazor termine de pintar (el campo puede recién habilitarse).
        window.requestAnimationFrame(function () {
            const el = document.getElementById(id);
            if (!el || el.disabled) return;
            el.focus();
            if (typeof el.select === 'function') el.select();
        });
    }

    function scrollIntoView(id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: 'nearest' });
    }

    return { init: init, dispose: dispose, focus: focus, scrollIntoView: scrollIntoView };
})();

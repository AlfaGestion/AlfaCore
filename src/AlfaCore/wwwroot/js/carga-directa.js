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
        // Reintenta unos cuadros: el campo puede habilitarse recién en el próximo render, y una ventana
        // (AlfaDialog) se enfoca a sí misma al abrir y le robaba el foco al buscador (2026-10-22).
        let intentos = 0, seleccionado = false;
        function intentar() {
            const el = document.getElementById(id);
            if (el && !el.disabled && document.activeElement !== el) {
                el.focus();
                if (!seleccionado && typeof el.select === 'function') { el.select(); }
                seleccionado = true;
            } else if (el && document.activeElement === el) {
                seleccionado = true;
            }
            if (++intentos < 8) window.setTimeout(intentar, 50);
        }
        window.requestAnimationFrame(intentar);
    }

    function scrollIntoView(id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: 'nearest' });
    }

    return { init: init, dispose: dispose, focus: focus, scrollIntoView: scrollIntoView };
})();

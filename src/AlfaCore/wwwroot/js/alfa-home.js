// Ctrl+K (Cmd+K en Mac): buscador de aplicaciones desde cualquier pantalla.
// - En el Inicio lleva el foco a su buscador (#home-search).
// - En el resto abre el buscador global (BuscadorGlobal.razor), que se registra acá al cargar.
(function () {
    function posicionarPanelModulo() {
        var button = document.querySelector('.alfa-home__module.is-open');
        var panel = document.querySelector('.alfa-home__panel');
        if (!button || !panel)
            return;

        var rect = button.getBoundingClientRect();
        var topbar = document.querySelector('.alfa-app-topbar');
        var topMinimo = topbar
            ? topbar.getBoundingClientRect().bottom + 8
            : 12;
        var alto = Math.min(420, Math.max(220, window.innerHeight - topMinimo - 24));
        panel.style.height = alto + 'px';
        panel.style.maxHeight = alto + 'px';
        panel.style.visibility = 'hidden';

        var ancho = panel.getBoundingClientRect().width || 300;
        var espacio = 12;
        var top = rect.bottom + 6;
        if (top + alto > window.innerHeight - espacio)
            top = rect.top - alto - 6;
        top = Math.max(topMinimo, Math.min(top, window.innerHeight - alto - espacio));

        var left = rect.left + rect.width / 2;
        left = Math.max(ancho / 2 + espacio, Math.min(left, window.innerWidth - ancho / 2 - espacio));

        panel.style.top = top + 'px';
        panel.style.left = left + 'px';
        panel.style.transform = 'translateX(-50%)';
        panel.style.visibility = 'visible';
    }

    window.alfaHomePositionPanel = posicionarPanelModulo;
    window.addEventListener('resize', posicionarPanelModulo);

    var componente = null;
    // Mientras el buscador global se está abriendo, lo que se tipea se guarda acá (y no va a la caja
    // que tenía el cursor, ej. la respuesta de una conversación, donde un Enter enviaría el mensaje).
    var abriendo = false;
    var tipeado = '';

    window.alfaBuscadorGlobal = {
        registrar: function (ref) { componente = ref; },
        desregistrar: function (ref) { if (componente === ref || !ref) componente = null; },
        enfocar: function () {
            var input = document.getElementById('buscador-global-input');
            if (!input)
                return;
            input.focus();
            if (tipeado) {
                input.value = tipeado;
                input.dispatchEvent(new Event('input', { bubbles: true }));
            } else {
                input.select();
            }
            abriendo = false;
            tipeado = '';
        }
    };

    document.addEventListener('keydown', function (e) {
        if (abriendo && !document.getElementById('buscador-global-input')) {
            if (e.key && e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
                tipeado += e.key;
                e.preventDefault();
                e.stopPropagation();
                return;
            }
            if (e.key === 'Enter' || e.key === 'Backspace') {
                if (e.key === 'Backspace')
                    tipeado = tipeado.slice(0, -1);
                e.preventDefault();
                e.stopPropagation();
                return;
            }
        }

        if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey)
            return;
        if ((e.key || '').toLowerCase() !== 'k')
            return;

        var input = document.getElementById('home-search');
        if (input) {
            e.preventDefault();
            input.focus();
            input.select();
            return;
        }

        if (componente) {
            e.preventDefault();
            if (document.activeElement && typeof document.activeElement.blur === 'function')
                document.activeElement.blur();
            abriendo = true;
            tipeado = '';
            componente.invokeMethodAsync('AbrirAsync');
        }
    }, true);
})();

// Ctrl+K (Cmd+K en Mac): buscador de aplicaciones desde cualquier pantalla.
// - En el Inicio lleva el foco a su buscador (#home-search).
// - En el resto abre el buscador global (BuscadorGlobal.razor), que se registra acá al cargar.
(function () {
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

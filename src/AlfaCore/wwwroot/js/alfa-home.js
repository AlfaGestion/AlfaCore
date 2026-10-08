// Inicio AlfaDesign: Ctrl+K (Cmd+K en Mac) lleva el foco al buscador de aplicaciones.
// Solo actúa si el buscador está en pantalla; en el resto de las páginas no hace nada.
(function () {
    document.addEventListener('keydown', function (e) {
        if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey)
            return;
        if ((e.key || '').toLowerCase() !== 'k')
            return;

        var input = document.getElementById('home-search');
        if (!input)
            return;

        e.preventDefault();
        input.focus();
        input.select();
    });
})();

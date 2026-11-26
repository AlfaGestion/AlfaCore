// Seleccionar texto arrastrando desde adentro de una ventana y soltar el mouse afuera (sobre el fondo oscuro)
// hace que el navegador dispare un "click" en el elemento que contiene a los dos: el fondo, que cierra la
// ventana (2026-11-26, la ayuda y muchas ventanas más). Se descarta ese click cuando hubo texto seleccionado
// y el mouse se apretó dentro del elemento que recibe el click, no sobre él. Un click normal en el fondo
// (apretar y soltar sobre el fondo) sigue cerrando como siempre.
(function () {
    let elementoApretado = null;

    window.addEventListener('mousedown', function (e) {
        elementoApretado = e.target;
    }, true);

    window.addEventListener('click', function (e) {
        const inicio = elementoApretado;
        elementoApretado = null;
        if (!inicio || inicio === e.target || !(e.target instanceof Node) || !e.target.contains(inicio))
            return;

        const seleccion = window.getSelection ? window.getSelection() : null;
        if (!seleccion || seleccion.isCollapsed || !seleccion.toString().trim())
            return;

        // En la fase de captura de window: ni Blazor ni otros scripts llegan a ver este click.
        e.stopImmediatePropagation();
        e.preventDefault();
    }, true);
})();

// Menú superior de módulo: las secciones que no entran en la barra pasan al menú «Más».
// El layout dibuja cada sección dos veces (barra y menú); este script muestra cada una donde corresponde.
// La sección activa nunca se manda a «Más».
(function () {
    let pendiente = false;

    function acomodar(nav) {
        const more = nav.parentElement && nav.parentElement.querySelector("[data-topnav-more]");
        if (!more) return;
        const items = Array.from(nav.querySelectorAll(":scope > [data-topnav-item]"));
        const menuItems = Array.from(more.querySelectorAll("[data-topnav-more-menu] > [data-topnav-item]"));

        items.forEach(i => { i.hidden = false; });
        more.hidden = true;
        if (nav.scrollWidth <= nav.clientWidth + 1) {
            menuItems.forEach(m => { m.hidden = true; });
            cerrar(more);
            return;
        }

        more.hidden = false;
        const activo = items.findIndex(i => i.classList.contains("is-active"));
        const visibles = new Set(items.map((_, k) => k));
        for (let k = items.length - 1; k >= 0 && nav.scrollWidth > nav.clientWidth + 1; k--) {
            if (k === activo) continue;
            items[k].hidden = true;
            visibles.delete(k);
        }
        menuItems.forEach((m, k) => { m.hidden = visibles.has(k); });
    }

    function acomodarTodo() {
        pendiente = false;
        document.querySelectorAll("[data-topnav]").forEach(acomodar);
    }

    function programar() {
        if (pendiente) return;
        pendiente = true;
        requestAnimationFrame(acomodarTodo);
    }

    function cerrar(more) {
        const menu = more.querySelector("[data-topnav-more-menu]");
        const toggle = more.querySelector("[data-topnav-more-toggle]");
        if (menu) menu.hidden = true;
        if (toggle) toggle.setAttribute("aria-expanded", "false");
    }

    document.addEventListener("click", function (e) {
        const toggle = e.target.closest && e.target.closest("[data-topnav-more-toggle]");
        document.querySelectorAll("[data-topnav-more]").forEach(more => {
            if (toggle && more.contains(toggle)) {
                const menu = more.querySelector("[data-topnav-more-menu]");
                const abrir = menu.hidden;
                menu.hidden = !abrir;
                toggle.setAttribute("aria-expanded", abrir ? "true" : "false");
            } else {
                // Clic afuera o en una sección del menú: se cierra.
                cerrar(more);
            }
        });
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") document.querySelectorAll("[data-topnav-more]").forEach(cerrar);
    });

    window.addEventListener("resize", programar);

    // Solo cambios dentro de la barra superior (cambio de módulo, secciones, ítem activo).
    const observer = new MutationObserver(registros => {
        for (const r of registros) {
            const t = r.target instanceof Element ? r.target : r.target.parentElement;
            if (t && (t.closest(".alfa-app-topbar") || t.querySelector && t.querySelector(":scope > .alfa-app-topbar, [data-topnav]"))) {
                programar();
                return;
            }
        }
    });

    function iniciar() {
        observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["class"] });
        if ("ResizeObserver" in window) new ResizeObserver(programar).observe(document.documentElement);
        programar();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", iniciar, { once: true });
    else iniciar();
})();

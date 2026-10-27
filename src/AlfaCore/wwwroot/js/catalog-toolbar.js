window.catalogToolbar = (() => {
    const states = new WeakMap();

    function init(toolbar) {
        if (!toolbar || states.has(toolbar)) {
            return;
        }

        const root = toolbar.closest('[data-catalog-toolbar-root]');
        if (!root) {
            return;
        }

        const sentinel = root.querySelector('.catalog-toolbar-sentinel');
        if (!sentinel) {
            return;
        }

        const updateState = () => {
            const rect = sentinel.getBoundingClientRect();
            root.classList.toggle('is-toolbar-collapsed', rect.bottom < 0);
        };

        const observer = new IntersectionObserver(() => {
            updateState();
        }, { threshold: 0 });

        observer.observe(sentinel);
        // Algunos WebView de iOS/WhatsApp no actualizan IntersectionObserver al desplazar
        // el documento. El listener en captura también cubre scrolls internos.
        window.addEventListener('scroll', updateState, { passive: true, capture: true });
        window.addEventListener('resize', updateState, { passive: true });
        updateState();
        states.set(toolbar, { observer, updateState });
    }

    return { init };
})();

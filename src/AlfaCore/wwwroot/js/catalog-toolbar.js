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

        const observer = new IntersectionObserver(([entry]) => {
            root.classList.toggle('is-toolbar-collapsed', !entry.isIntersecting);
        }, { threshold: 0 });

        observer.observe(sentinel);
        states.set(toolbar, observer);
    }

    return { init };
})();

(function () {
    const gap = 6;
    const gutter = 8;

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function position(root, results) {
        if (!root || !results)
            return;

        const rootRect = root.getBoundingClientRect();
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        const availableBelow = Math.max(0, viewportHeight - rootRect.bottom - gap - gutter);
        const availableAbove = Math.max(0, rootRect.top - gap - gutter);
        const preferredHeight = Math.min(results.scrollHeight, 280);
        // Si el contenido no entra debajo, priorizar la apertura hacia arriba.
        // Esto evita que el listado quede cortado en formularios bajos o en pantallas chicas.
        const openAbove = availableBelow < preferredHeight && availableAbove > availableBelow;
        const availableHeight = Math.max(80, openAbove ? availableAbove : availableBelow);
        const height = Math.min(preferredHeight, availableHeight);
        const left = clamp(rootRect.left, gutter, Math.max(gutter, viewportWidth - rootRect.width - gutter));

        results.style.position = "fixed";
        results.style.left = `${Math.round(left)}px`;
        results.style.right = "auto";
        results.style.width = `${Math.round(rootRect.width)}px`;
        results.style.maxHeight = `${Math.round(height)}px`;
        results.style.top = openAbove
            ? `${Math.round(Math.max(gutter, rootRect.top - height - gap))}px`
            : `${Math.round(rootRect.bottom + gap)}px`;
        results.style.bottom = "auto";
    }

    function containsFocus(root) {
        return !!root && root.contains(document.activeElement);
    }

    window.AlfaLookupPopover = { position, containsFocus };
})();

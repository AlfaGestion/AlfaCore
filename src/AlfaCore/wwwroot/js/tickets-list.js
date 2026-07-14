function syncHeader(root) {
    const body = root.querySelector('[data-tickets-list-body]');
    const header = root.querySelector('[data-tickets-list-head]');
    const viewport = root.querySelector('.tickets-list-sticky-head__viewport');
    if (!body || !header || !viewport) {
        return;
    }

    const bodyRect = body.getBoundingClientRect();
    viewport.style.left = `${bodyRect.left}px`;
    viewport.style.width = `${bodyRect.width}px`;
    header.style.transform = `translateX(${-body.scrollLeft}px)`;

    const selectAll = root.querySelector('[data-tickets-select-all]');
    if (selectAll) {
        selectAll.indeterminate = selectAll.dataset.partial === 'true';
    }
}

export function wireListHeader(root) {
    if (!root) {
        return;
    }

    const body = root.querySelector('[data-tickets-list-body]');
    if (!body) {
        return;
    }

    if (body.dataset.ticketsListHeaderBound !== '1') {
        body.dataset.ticketsListHeaderBound = '1';
        body.addEventListener('scroll', () => syncHeader(root), { passive: true });
        window.addEventListener('resize', () => syncHeader(root), { passive: true });

        if ('ResizeObserver' in window) {
            const resizeObserver = new ResizeObserver(() => syncHeader(root));
            resizeObserver.observe(body);
            resizeObserver.observe(root);
            body.ticketsListResizeObserver = resizeObserver;
        }
    }

    window.requestAnimationFrame(() => syncHeader(root));
}

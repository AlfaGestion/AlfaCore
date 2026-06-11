window.alfaCoreSidebarHover = (function () {
    let label;
    let hideTimer;
    let currentLink;

    function ensureLabel() {
        if (label) return label;

        label = document.createElement('div');
        label.className = 'sidebar-hover-label';
        document.body.appendChild(label);
        return label;
    }

    function getLabel(link) {
        return Array.from(link.querySelectorAll('span:not(.menu__icon)'))
            .map(x => (x.textContent || '').trim())
            .find(Boolean) || '';
    }

    function show(link) {
        const shell = document.querySelector('.shell.shell--collapsed');
        if (!shell || shell.classList.contains('shell--mobile-open')) return;
        if (!link || !link.closest('.shell__sidebar')) return;

        const text = getLabel(link);
        if (!text) return;

        window.clearTimeout(hideTimer);
        currentLink = link;

        const rect = link.getBoundingClientRect();
        const bubble = ensureLabel();
        const width = Math.min(220, Math.max(150, window.innerWidth - rect.left - 8));
        bubble.textContent = text;
        bubble.style.left = `${rect.left}px`;
        bubble.style.top = `${rect.top + (rect.height / 2)}px`;
        bubble.style.width = `${width}px`;
        bubble.style.minHeight = `${Math.max(42, rect.height - 2)}px`;
        bubble.style.paddingLeft = `${Math.max(58, rect.width + 10)}px`;
        bubble.classList.add('is-visible');
    }

    function hide(link) {
        if (link && currentLink && link !== currentLink) return;
        currentLink = null;
        if (!label) return;

        window.clearTimeout(hideTimer);
        hideTimer = window.setTimeout(() => {
            if (!currentLink && label) {
                label.classList.remove('is-visible');
            }
        }, 40);
    }

    function init() {
        if (document.body.dataset.sidebarHoverReady === '1') return;
        document.body.dataset.sidebarHoverReady = '1';

        document.addEventListener('pointerover', event => {
            const link = event.target?.closest?.('.shell--collapsed .shell__sidebar .menu__link');
            if (link) show(link);
        }, true);

        document.addEventListener('pointerout', event => {
            const link = event.target?.closest?.('.shell--collapsed .shell__sidebar .menu__link');
            if (link && !link.contains(event.relatedTarget)) hide(link);
        }, true);

        document.addEventListener('scroll', () => hide(), true);
        window.addEventListener('resize', () => hide(), { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }

    return { init };
})();

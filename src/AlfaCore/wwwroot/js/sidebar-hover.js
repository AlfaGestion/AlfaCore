window.alfaCoreSidebarHover = (function () {
    let card;
    let hideTimer;
    let currentLink;

    function ensureCard() {
        if (card) return card;

        card = document.createElement('div');
        card.className = 'sidebar-hover-card';
        card.innerHTML = '<span class="sidebar-hover-card__icon"><i></i></span><span class="sidebar-hover-card__label"></span>';
        document.body.appendChild(card);
        return card;
    }

    function getLabel(link) {
        const textNode = Array.from(link.querySelectorAll('span:not(.menu__icon)'))
            .map(x => (x.textContent || '').trim())
            .find(Boolean);
        return textNode || (link.textContent || '').trim();
    }

    function show(link) {
        const shell = document.querySelector('.shell.shell--collapsed');
        if (!shell || shell.classList.contains('shell--mobile-open')) return;
        if (!link || !link.closest('.shell__sidebar')) return;

        const label = getLabel(link);
        if (!label) return;

        window.clearTimeout(hideTimer);
        currentLink = link;

        const icon = link.querySelector('.menu__icon i');
        const iconClass = icon ? icon.className : 'bi bi-grid';
        const linkRect = link.getBoundingClientRect();
        const sidebarRect = link.closest('.shell__sidebar').getBoundingClientRect();
        const tooltip = ensureCard();

        tooltip.querySelector('i').className = iconClass;
        tooltip.querySelector('.sidebar-hover-card__label').textContent = label;
        tooltip.style.left = `${Math.max(72, sidebarRect.right - 18)}px`;
        tooltip.style.top = `${linkRect.top + (linkRect.height / 2)}px`;
        tooltip.classList.add('is-visible');
    }

    function hide(link) {
        if (link && currentLink && link !== currentLink) return;
        currentLink = null;
        if (!card) return;

        window.clearTimeout(hideTimer);
        hideTimer = window.setTimeout(() => {
            if (!currentLink && card) {
                card.classList.remove('is-visible');
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

    return { init };
})();

window.alfaCoreSidebarHoverState = (function () {
    let leaveTimer;

    function setLeaving(sidebar) {
        if (!sidebar) return;

        window.clearTimeout(leaveTimer);
        sidebar.classList.add('is-hover-leaving');
        leaveTimer = window.setTimeout(() => {
            sidebar.classList.remove('is-hover-leaving');
        }, 260);
    }

    function init() {
        if (document.body.dataset.sidebarHoverStateReady === '1') return;
        document.body.dataset.sidebarHoverStateReady = '1';

        document.addEventListener('pointerout', event => {
            const link = event.target?.closest?.('.shell--collapsed .shell__sidebar .menu__link');
            if (!link || link.contains(event.relatedTarget)) return;

            setLeaving(link.closest('.shell__sidebar'));
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }

    return { init };
})();

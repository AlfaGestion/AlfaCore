(function () {
    const WRAPPER_SELECTOR = '.usuarios-table-wrap, .table-wrap, .data-table-wrap, .consulta-result__table-wrap, .result-group-table-wrap';
    const MIN_OVERFLOW = 12;
    let host = null;
    let track = null;
    let content = null;
    let activeWrapper = null;
    let syncingFromBar = false;
    let syncingFromWrapper = false;
    let rafId = 0;
    let mutationObserver = null;
    let currentPath = '';

    function handleRouteChanged() {
        const nextPath = `${window.location.pathname}${window.location.search}`;
        if (nextPath === currentPath) {
            return;
        }

        currentPath = nextPath;
        activeWrapper = null;
        scheduleRefresh();
        window.setTimeout(scheduleRefresh, 60);
        window.setTimeout(scheduleRefresh, 220);
    }

    function ensureHost() {
        if (host) {
            return;
        }

        host = document.createElement('div');
        host.id = 'alfacore-sticky-x-scroll';
        host.setAttribute('aria-hidden', 'true');

        track = document.createElement('div');
        track.className = 'alfacore-sticky-x-scroll__track';

        content = document.createElement('div');
        content.className = 'alfacore-sticky-x-scroll__content';

        track.appendChild(content);
        host.appendChild(track);
        document.body.appendChild(host);

        track.addEventListener('scroll', () => {
            if (!activeWrapper || syncingFromWrapper) {
                return;
            }

            syncingFromBar = true;
            activeWrapper.scrollLeft = track.scrollLeft;
            syncingFromBar = false;
        });
    }

    function getWrappers() {
        return Array.from(document.querySelectorAll(WRAPPER_SELECTOR))
            .filter(wrapper =>
                wrapper instanceof HTMLElement
                && wrapper.offsetParent !== null
                && wrapper.dataset.stickyXScroll !== 'off');
    }

    function isWrapperEligible(wrapper) {
        return wrapper.scrollWidth - wrapper.clientWidth > MIN_OVERFLOW;
    }

    function getVisibleScore(wrapper) {
        const rect = wrapper.getBoundingClientRect();
        const viewTop = 0;
        const viewBottom = window.innerHeight;
        const visibleTop = Math.max(rect.top, viewTop);
        const visibleBottom = Math.min(rect.bottom, viewBottom);
        const visibleHeight = Math.max(0, visibleBottom - visibleTop);
        if (visibleHeight <= 0) {
            return 0;
        }

        return visibleHeight * Math.max(rect.width, 1);
    }

    function pickActiveWrapper() {
        const wrappers = getWrappers().filter(isWrapperEligible);
        if (wrappers.length === 0) {
            return null;
        }

        if (activeWrapper && wrappers.includes(activeWrapper)) {
            return activeWrapper;
        }

        let best = null;
        let bestScore = 0;

        for (const wrapper of wrappers) {
            const score = getVisibleScore(wrapper);
            if (score > bestScore) {
                best = wrapper;
                bestScore = score;
            }
        }

        return best || wrappers[0];
    }

    function syncWrapperToBar() {
        if (!activeWrapper || !track) {
            return;
        }

        if (syncingFromBar) {
            return;
        }

        syncingFromWrapper = true;
        track.scrollLeft = activeWrapper.scrollLeft;
        syncingFromWrapper = false;
    }

    function attachWrapperListeners(wrapper) {
        if (wrapper.dataset.stickyXScrollBound === '1') {
            return;
        }

        wrapper.dataset.stickyXScrollBound = '1';

        wrapper.addEventListener('scroll', () => {
            if (wrapper === activeWrapper) {
                syncWrapperToBar();
            }
        }, { passive: true });

        wrapper.addEventListener('mouseenter', scheduleRefresh, { passive: true });
        wrapper.addEventListener('touchstart', scheduleRefresh, { passive: true });
        wrapper.addEventListener('focusin', scheduleRefresh, { passive: true });
    }

    function updateHost() {
        ensureHost();

        getWrappers().forEach(attachWrapperListeners);

        const nextWrapper = pickActiveWrapper();
        activeWrapper = nextWrapper;

        if (!activeWrapper) {
            host.classList.remove('is-visible');
            host.style.width = '0px';
            return;
        }

        const rect = activeWrapper.getBoundingClientRect();
        const horizontalPadding = 12;
        const left = Math.max(horizontalPadding, rect.left);
        const maxWidth = Math.max(0, window.innerWidth - left - horizontalPadding);
        const width = Math.min(rect.width, maxWidth);

        if (width <= 0) {
            host.classList.remove('is-visible');
            host.style.width = '0px';
            return;
        }

        host.style.left = `${left}px`;
        host.style.width = `${width}px`;
        content.style.width = `${activeWrapper.scrollWidth}px`;
        track.scrollLeft = activeWrapper.scrollLeft;
        host.classList.add('is-visible');
    }

    function scheduleRefresh() {
        if (rafId) {
            return;
        }

        rafId = window.requestAnimationFrame(() => {
            rafId = 0;
            updateHost();
        });
    }

    function installObservers() {
        if (mutationObserver) {
            return;
        }

        mutationObserver = new MutationObserver(() => {
            scheduleRefresh();
        });

        mutationObserver.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class', 'style']
        });
    }

    function init() {
        ensureHost();
        installObservers();
        currentPath = `${window.location.pathname}${window.location.search}`;
        scheduleRefresh();

        window.addEventListener('resize', scheduleRefresh, { passive: true });
        window.addEventListener('scroll', scheduleRefresh, { passive: true });
        window.addEventListener('popstate', handleRouteChanged, { passive: true });
        window.addEventListener('hashchange', handleRouteChanged, { passive: true });
        window.addEventListener('click', () => {
            window.setTimeout(handleRouteChanged, 0);
        }, { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();

(function () {
    const TABLE_SELECTOR = 'table[data-sticky-list-header]';
    const SCROLL_HOST_SELECTOR = '.usuarios-table-wrap, .table-wrap, .data-table-wrap, .consulta-result__table-wrap, .result-group-table-wrap, .table-responsive';
    let host = null;
    let cloneTable = null;
    let sourceTable = null;
    let sourceHeaderMarkup = '';
    let mutationObserver = null;
    let rafId = 0;
    let currentPath = '';

    function ensureHost() {
        if (host) return;

        host = document.createElement('div');
        host.className = 'alfacore-sticky-list-header no-print';
        host.setAttribute('aria-hidden', 'true');
        document.body.appendChild(host);
        host.addEventListener('click', forwardClick);
        host.addEventListener('change', forwardChange);
    }

    function getHeaderBottom() {
        const headers = Array.from(document.querySelectorAll('.main-page-header'))
            .filter(element => element instanceof HTMLElement && element.offsetParent !== null);
        return headers.length === 0
            ? 0
            : Math.max(...headers.map(element => element.getBoundingClientRect().bottom), 0);
    }

    function getVisibleTables() {
        return Array.from(document.querySelectorAll(TABLE_SELECTOR))
            .filter(table => table instanceof HTMLTableElement
                && table.offsetParent !== null
                && table.tHead
                && table.tHead.rows.length > 0);
    }

    function pickActiveTable(stickyTop) {
        let active = null;
        let bestTop = Number.NEGATIVE_INFINITY;

        for (const table of getVisibleTables()) {
            const tableRect = table.getBoundingClientRect();
            const headRect = table.tHead.getBoundingClientRect();
            if (headRect.top > stickyTop + 1 || tableRect.bottom <= stickyTop + headRect.height) continue;
            if (headRect.top >= bestTop) {
                active = table;
                bestTop = headRect.top;
            }
        }

        return active;
    }

    function getScrollHost(table) {
        return table.closest(SCROLL_HOST_SELECTOR) || table.parentElement;
    }

    function copyHeaderGeometry(originalHead, clonedHead) {
        const originalCells = Array.from(originalHead.querySelectorAll('th, td'));
        const clonedCells = Array.from(clonedHead.querySelectorAll('th, td'));
        clonedCells.forEach((cell, index) => {
            const original = originalCells[index];
            if (!original) return;
            const width = original.getBoundingClientRect().width;
            cell.style.boxSizing = 'border-box';
            cell.style.width = `${width}px`;
            cell.style.minWidth = `${width}px`;
            cell.style.maxWidth = `${width}px`;
        });
    }

    function syncControlState() {
        if (!sourceTable || !cloneTable) return;
        const originals = Array.from(sourceTable.tHead.querySelectorAll('input, select, button'));
        const clones = Array.from(cloneTable.tHead.querySelectorAll('input, select, button'));
        clones.forEach((control, index) => {
            const original = originals[index];
            if (!original) return;
            if (control instanceof HTMLInputElement && original instanceof HTMLInputElement) {
                control.checked = original.checked;
                control.indeterminate = original.indeterminate;
                control.disabled = original.disabled;
            } else if (control instanceof HTMLSelectElement && original instanceof HTMLSelectElement) {
                control.value = original.value;
                control.disabled = original.disabled;
            } else if (control instanceof HTMLButtonElement && original instanceof HTMLButtonElement) {
                control.disabled = original.disabled;
            }
        });
    }

    function rebuildClone(table) {
        host.replaceChildren();
        sourceTable = table;
        sourceHeaderMarkup = table.tHead.innerHTML;
        cloneTable = table.cloneNode(false);
        cloneTable.removeAttribute('id');
        cloneTable.removeAttribute('data-sticky-list-header');
        cloneTable.classList.add('alfacore-sticky-list-header__table');
        const clonedHead = table.tHead.cloneNode(true);
        cloneTable.appendChild(clonedHead);
        host.appendChild(cloneTable);
        copyHeaderGeometry(table.tHead, clonedHead);
        syncControlState();
    }

    function updateHost() {
        ensureHost();
        const stickyTop = getHeaderBottom();
        const table = pickActiveTable(stickyTop);
        if (!table) {
            sourceTable = null;
            sourceHeaderMarkup = '';
            cloneTable = null;
            host.replaceChildren();
            host.classList.remove('is-visible');
            return;
        }

        if (table !== sourceTable || !cloneTable || table.tHead.innerHTML !== sourceHeaderMarkup) {
            rebuildClone(table);
        } else {
            copyHeaderGeometry(table.tHead, cloneTable.tHead);
            syncControlState();
        }
        const tableRect = table.getBoundingClientRect();
        const headRect = table.tHead.getBoundingClientRect();
        const scrollHost = getScrollHost(table);
        const scrollRect = scrollHost instanceof HTMLElement ? scrollHost.getBoundingClientRect() : tableRect;
        const left = Math.max(0, scrollRect.left);
        const right = Math.min(window.innerWidth, scrollRect.right);
        const width = Math.max(0, right - left);
        if (width === 0 || headRect.height === 0) {
            host.classList.remove('is-visible');
            return;
        }

        host.style.top = `${stickyTop}px`;
        host.style.left = `${left}px`;
        host.style.width = `${width}px`;
        host.style.height = `${headRect.height}px`;
        cloneTable.style.width = `${tableRect.width}px`;
        cloneTable.style.height = `${headRect.height}px`;
        cloneTable.style.transform = `translateX(${tableRect.left - left}px)`;
        host.classList.add('is-visible');
    }

    function scheduleRefresh() {
        if (rafId) return;
        rafId = window.requestAnimationFrame(() => {
            rafId = 0;
            updateHost();
        });
    }

    function findOriginalControl(target) {
        if (!sourceTable || !cloneTable || !(target instanceof Element)) return null;
        const clonedCell = target.closest('th, td');
        if (!clonedCell) return null;
        const clonedCells = Array.from(cloneTable.tHead.querySelectorAll('th, td'));
        const originalCells = Array.from(sourceTable.tHead.querySelectorAll('th, td'));
        const originalCell = originalCells[clonedCells.indexOf(clonedCell)];
        if (!originalCell) return null;
        const interactive = target.closest('button, input, select, a, label');
        if (!interactive) return originalCell;
        const selector = interactive.tagName.toLowerCase();
        const clonedControls = Array.from(clonedCell.querySelectorAll(selector));
        const originalControls = Array.from(originalCell.querySelectorAll(selector));
        return originalControls[clonedControls.indexOf(interactive)] || originalCell;
    }

    function forwardClick(event) {
        const original = findOriginalControl(event.target);
        if (!(original instanceof HTMLElement)) return;
        event.preventDefault();
        original.click();
        window.setTimeout(scheduleRefresh, 0);
    }

    function forwardChange(event) {
        const original = findOriginalControl(event.target);
        if (!(original instanceof HTMLInputElement || original instanceof HTMLSelectElement)) return;
        if (event.target instanceof HTMLInputElement && original instanceof HTMLInputElement) {
            original.checked = event.target.checked;
        }
        if (event.target instanceof HTMLSelectElement && original instanceof HTMLSelectElement) {
            original.value = event.target.value;
        }
        original.dispatchEvent(new Event('change', { bubbles: true }));
        window.setTimeout(scheduleRefresh, 0);
    }

    function handleRouteChanged() {
        const nextPath = `${window.location.pathname}${window.location.search}`;
        if (nextPath === currentPath) return;
        currentPath = nextPath;
        sourceTable = null;
        sourceHeaderMarkup = '';
        scheduleRefresh();
        window.setTimeout(scheduleRefresh, 80);
        window.setTimeout(scheduleRefresh, 240);
    }

    function installObservers() {
        mutationObserver = new MutationObserver(mutations => {
            if (mutations.some(mutation => !host.contains(mutation.target))) scheduleRefresh();
        });
        mutationObserver.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class', 'style', 'aria-sort']
        });
    }

    function init() {
        ensureHost();
        installObservers();
        currentPath = `${window.location.pathname}${window.location.search}`;
        scheduleRefresh();
        window.addEventListener('resize', scheduleRefresh, { passive: true });
        window.addEventListener('scroll', scheduleRefresh, { passive: true, capture: true });
        window.addEventListener('popstate', handleRouteChanged, { passive: true });
        window.addEventListener('hashchange', handleRouteChanged, { passive: true });
        window.addEventListener('click', () => window.setTimeout(handleRouteChanged, 0), { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();

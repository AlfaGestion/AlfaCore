(function () {
    const TABLE_SELECTOR = 'table[data-sticky-list-header]';
    const SCROLL_HOST_SELECTOR = '.usuarios-table-wrap, .table-wrap, .data-table-wrap, .consulta-result__table-wrap, .result-group-table-wrap, .table-responsive';
    const NON_SORTABLE_LABELS = new Set([
        '', 'accion', 'acciones', 'seleccion', 'conversacion'
    ]);
    let host = null;
    let cloneTable = null;
    let sourceTable = null;
    let sourceHeaderMarkup = '';
    let mutationObserver = null;
    let rafId = 0;
    let currentPath = '';

    function normalizeLabel(value) {
        return value
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .replace(/\s+/g, ' ')
            .trim()
            .toLocaleLowerCase('es');
    }

    function isAlreadySortable(cell) {
        return cell.matches('.sortable')
            || cell.querySelector('button, a, input, select, textarea') !== null;
    }

    function createSortButton(table, cell) {
        const label = cell.textContent.replace(/\s+/g, ' ').trim();
        if (NON_SORTABLE_LABELS.has(normalizeLabel(label))
            || cell.dataset.sortDisabled === 'true'
            || isAlreadySortable(cell)) return;

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'table-sort table-sort--client';
        button.dataset.clientTableSort = 'true';
        button.title = `Ordenar por ${label}`;
        button.setAttribute('aria-label', `Ordenar por ${label}`);

        const labelElement = document.createElement('span');
        labelElement.className = 'table-sort__label';
        while (cell.firstChild) labelElement.appendChild(cell.firstChild);

        const icon = document.createElement('i');
        icon.className = 'bi bi-arrow-down-up table-sort__icon';
        icon.setAttribute('aria-hidden', 'true');

        button.append(labelElement, icon);
        button.addEventListener('click', event => {
            event.preventDefault();
            event.stopPropagation();
            sortTable(table, cell.cellIndex);
        });
        cell.appendChild(button);
        cell.dataset.clientSortReady = 'true';
    }

    function enhanceSortableTable(table) {
        if (!table.tHead) return;
        const headerRows = Array.from(table.tHead.rows);
        if (headerRows.length === 0) return;

        for (const row of headerRows) {
            for (const cell of row.cells) createSortButton(table, cell);
        }
    }

    function readCellValue(row, columnIndex) {
        const cell = row.cells[columnIndex];
        if (!cell) return '';
        if (cell.dataset.sortValue) return cell.dataset.sortValue.trim();

        const control = cell.querySelector('input, select, textarea');
        if (control instanceof HTMLInputElement && control.type === 'checkbox') {
            return control.checked ? '1' : '0';
        }
        if (control instanceof HTMLInputElement
            || control instanceof HTMLSelectElement
            || control instanceof HTMLTextAreaElement) {
            return control.value.trim();
        }
        return cell.textContent.replace(/\s+/g, ' ').trim();
    }

    function parseDate(value) {
        const latinDate = value.match(/^(\d{1,2})[\/-](\d{1,2})[\/-](\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
        if (latinDate) {
            const [, day, month, year, hour = '0', minute = '0', second = '0'] = latinDate;
            return new Date(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute), Number(second)).getTime();
        }

        const isoDate = value.match(/^(\d{4})-(\d{1,2})-(\d{1,2})(?:[T\s](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/);
        if (!isoDate) return null;
        const [, year, month, day, hour = '0', minute = '0', second = '0'] = isoDate;
        return new Date(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute), Number(second)).getTime();
    }

    function parseNumber(value) {
        let normalized = value
            .replace(/\u00a0/g, ' ')
            .replace(/[$%]/g, '')
            .replace(/\s+/g, '')
            .trim();
        if (!/^-?[\d.,]+$/.test(normalized) || !/\d/.test(normalized)) return null;

        const comma = normalized.lastIndexOf(',');
        const dot = normalized.lastIndexOf('.');
        if (comma >= 0 && dot >= 0) {
            normalized = comma > dot
                ? normalized.replace(/\./g, '').replace(',', '.')
                : normalized.replace(/,/g, '');
        } else if (comma >= 0) {
            normalized = normalized.replace(/\./g, '').replace(',', '.');
        } else if ((normalized.match(/\./g) || []).length > 1) {
            normalized = normalized.replace(/\./g, '');
        }

        const parsed = Number(normalized);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function comparableValue(value) {
        const trimmed = value.trim();
        if (trimmed === '' || trimmed === '-') return { empty: true, value: '' };
        const date = parseDate(trimmed);
        if (date !== null) return { empty: false, kind: 'number', value: date };
        const number = parseNumber(trimmed);
        if (number !== null) return { empty: false, kind: 'number', value: number };
        return { empty: false, kind: 'text', value: trimmed };
    }

    function compareRows(left, right, columnIndex, descending) {
        const a = comparableValue(readCellValue(left.row, columnIndex));
        const b = comparableValue(readCellValue(right.row, columnIndex));
        if (a.empty !== b.empty) return a.empty ? 1 : -1;

        let result = 0;
        if (a.kind === 'number' && b.kind === 'number') {
            result = a.value - b.value;
        } else {
            result = String(a.value).localeCompare(String(b.value), 'es', {
                numeric: true,
                sensitivity: 'base'
            });
        }
        if (result === 0) return left.index - right.index;
        return descending ? -result : result;
    }

    function isGroupRow(row) {
        return row.cells.length === 1 && row.cells[0].colSpan > 1;
    }

    function sortBody(body, columnIndex, descending) {
        const rows = Array.from(body.rows);
        let segment = [];

        function flushSegment() {
            if (segment.length === 0) return;
            segment
                .map((row, index) => ({ row, index }))
                .sort((left, right) => compareRows(left, right, columnIndex, descending))
                .forEach(item => body.appendChild(item.row));
            segment = [];
        }

        for (const row of rows) {
            if (isGroupRow(row)) {
                flushSegment();
                body.appendChild(row);
            } else {
                segment.push(row);
            }
        }
        flushSegment();
    }

    function updateSortHeader(table, columnIndex, descending) {
        for (const cell of table.tHead.querySelectorAll('th')) {
            const active = cell.cellIndex === columnIndex && cell.dataset.clientSortReady === 'true';
            cell.setAttribute('aria-sort', active ? (descending ? 'descending' : 'ascending') : 'none');
            const button = cell.querySelector('button[data-client-table-sort]');
            const icon = button?.querySelector('.table-sort__icon');
            button?.classList.toggle('is-active', active);
            if (icon) icon.className = `bi ${active ? (descending ? 'bi-arrow-down' : 'bi-arrow-up') : 'bi-arrow-down-up'} table-sort__icon`;
        }
    }

    function sortTable(table, columnIndex) {
        const sameColumn = Number(table.dataset.clientSortColumn) === columnIndex;
        const descending = sameColumn ? table.dataset.clientSortDirection !== 'desc' : false;
        table.dataset.clientSortColumn = String(columnIndex);
        table.dataset.clientSortDirection = descending ? 'desc' : 'asc';
        for (const body of table.tBodies) sortBody(body, columnIndex, descending);
        updateSortHeader(table, columnIndex, descending);
        scheduleRefresh();
    }

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
            .filter(element => element instanceof HTMLElement
                && element.getClientRects().length > 0
                && window.getComputedStyle(element).visibility !== 'hidden');
        return headers.length === 0
            ? 0
            : Math.max(...headers.map(element => element.getBoundingClientRect().bottom), 0);
    }

    function getVisibleTables() {
        return Array.from(document.querySelectorAll(TABLE_SELECTOR))
            .filter(table => table instanceof HTMLTableElement
                && table.offsetParent !== null
                && table.tHead
                && table.tHead.rows.length > 0)
            .map(table => {
                enhanceSortableTable(table);
                return table;
            });
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

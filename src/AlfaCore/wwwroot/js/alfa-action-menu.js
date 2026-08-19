const selectable = panel => Array.from(panel.querySelectorAll('[role="menuitem"]:not(:disabled)'));

export function position(trigger, panel, align) {
    const triggerRect = trigger.getBoundingClientRect();
    const panelRect = panel.getBoundingClientRect();
    const gap = 6;
    const viewportGap = 8;
    const spaceBelow = window.innerHeight - triggerRect.bottom - viewportGap;
    const openAbove = panelRect.height > spaceBelow && triggerRect.top > spaceBelow;

    let top = openAbove ? triggerRect.top - panelRect.height - gap : triggerRect.bottom + gap;
    let left = align === 'start' ? triggerRect.left : triggerRect.right - panelRect.width;

    top = Math.max(viewportGap, Math.min(top, window.innerHeight - panelRect.height - viewportGap));
    left = Math.max(viewportGap, Math.min(left, window.innerWidth - panelRect.width - viewportGap));

    panel.style.top = `${Math.round(top)}px`;
    panel.style.left = `${Math.round(left)}px`;
}

export function focusFirst(panel) {
    selectable(panel)[0]?.focus({ preventScroll: true });
}

export function focusRelative(panel, direction) {
    const items = selectable(panel);
    if (!items.length) return;
    const current = items.indexOf(document.activeElement);
    const next = current < 0 ? 0 : (current + direction + items.length) % items.length;
    items[next].focus({ preventScroll: true });
}

export function focusEdge(panel, end) {
    const items = selectable(panel);
    (end ? items.at(-1) : items[0])?.focus({ preventScroll: true });
}

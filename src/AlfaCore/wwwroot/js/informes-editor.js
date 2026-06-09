const wiredRoots = new WeakMap();

export function wireInformesEditor(root) {
    if (!root || wiredRoots.has(root)) {
        return;
    }

    const closeFromDocument = (event) => {
        if (!root.isConnected) {
            disposeInformesEditor(root);
            return;
        }

        const target = event.target;
        if (root.contains(target)) {
            if (typeof target.closest === 'function'
                && target.closest('.ticket-editor-emoji-picker, .ticket-link-popover, .informes-blockbar, .informes-command-menu')) {
                return;
            }
        }

        closeInformesPopups(root);
    };

    const closeFromEscape = (event) => {
        if (event.key === 'Escape') {
            closeInformesPopups(root);
        }
    };

    document.addEventListener('pointerdown', closeFromDocument, true);
    document.addEventListener('keydown', closeFromEscape, true);
    wiredRoots.set(root, { closeFromDocument, closeFromEscape });
}

export function disposeInformesEditor(root) {
    const handlers = wiredRoots.get(root);
    if (!handlers) {
        return;
    }

    document.removeEventListener('pointerdown', handlers.closeFromDocument, true);
    document.removeEventListener('keydown', handlers.closeFromEscape, true);
    wiredRoots.delete(root);
}

export function closeInformesPopups(root) {
    root?.querySelectorAll('.ticket-link-popover, .ticket-editor-emoji-picker')
        .forEach((popup) => popup.remove());
}

export function normalizeInformesBlocks(editor) {
    if (!editor) {
        return;
    }

    editor.querySelectorAll('.ticket-editor-banner').forEach((banner) => {
        const icon = banner.querySelector('.ticket-editor-banner__icon');
        if (icon) {
            icon.setAttribute('contenteditable', 'false');
        }

        let body = banner.querySelector('.ticket-editor-banner__body');
        if (!body) {
            body = document.createElement('div');
            body.className = 'ticket-editor-banner__body';
            const movableNodes = Array.from(banner.childNodes)
                .filter((node) => node !== icon);
            movableNodes.forEach((node) => body.appendChild(node));
            banner.appendChild(body);
        }
    });
}

export function placeCaretAfterInformesBanner(editor) {
    if (!editor) {
        return;
    }

    const banners = Array.from(editor.querySelectorAll('.ticket-editor-banner'));
    const banner = findActiveBanner(editor) ?? banners[banners.length - 1];
    if (!banner) {
        return;
    }

    const paragraph = ensureParagraphAfter(banner);
    placeCaretInside(paragraph);
}

function findActiveBanner(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) {
        return null;
    }

    const node = selection.anchorNode;
    if (!node || !editor.contains(node)) {
        return null;
    }

    const element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
    return element?.closest?.('.ticket-editor-banner') ?? null;
}

function ensureParagraphAfter(block) {
    let next = block.nextSibling;
    while (next && next.nodeType === Node.TEXT_NODE && !next.textContent.trim()) {
        next = next.nextSibling;
    }

    if (next instanceof HTMLParagraphElement) {
        if (!next.innerHTML.trim()) {
            next.innerHTML = '<br>';
        }

        return next;
    }

    const paragraph = document.createElement('p');
    paragraph.innerHTML = '<br>';
    block.parentNode.insertBefore(paragraph, next);
    return paragraph;
}

function placeCaretInside(element) {
    element.focus?.();
    const selection = window.getSelection();
    if (!selection) {
        return;
    }

    const range = document.createRange();
    range.selectNodeContents(element);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
}

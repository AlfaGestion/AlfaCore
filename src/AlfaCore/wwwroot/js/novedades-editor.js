const editorStates = new WeakMap();
let previewEscapeHandler = null;

export function wireNovedadesEditor(editor) {
    if (!editor || editorStates.has(editor)) {
        refreshMedia(editor);
        return;
    }

    const state = { dragged: null };

    const observer = new MutationObserver(() => refreshMedia(editor));
    observer.observe(editor, { childList: true, subtree: true });

    editor.addEventListener('dragstart', (event) => {
        const block = event.target?.closest?.('.novedades-media-block');
        if (!block || !editor.contains(block) || event.target?.closest?.('.ticket-editor-file__resize')) {
            return;
        }

        state.dragged = block;
        block.classList.add('is-dragging');
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', 'novedades-media');
    });

    editor.addEventListener('dragover', (event) => {
        if (!state.dragged) {
            return;
        }

        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
    });

    editor.addEventListener('drop', (event) => {
        if (!state.dragged) {
            return;
        }

        event.preventDefault();
        const target = getDirectEditorChild(editor, event.target)
            || findClosestEditorChild(editor, state.dragged, event.clientY);
        if (!target) {
            editor.appendChild(state.dragged);
            dispatchInput(editor);
            return;
        }

        if (target === state.dragged || state.dragged.contains(target)) {
            return;
        }

        const rect = target.getBoundingClientRect();
        const insertAfter = event.clientY > rect.top + rect.height / 2;
        editor.insertBefore(state.dragged, insertAfter ? target.nextSibling : target);
        dispatchInput(editor);
    });

    editor.addEventListener('dragend', () => {
        state.dragged?.classList.remove('is-dragging');
        state.dragged = null;
    });

    editorStates.set(editor, { observer, state });
    refreshMedia(editor);
}

export function refreshMedia(editor) {
    if (!editor) {
        return;
    }

    editor.querySelectorAll('.ticket-editor-file--media').forEach((block) => {
        const preview = block.querySelector('.ticket-editor-file__preview');
        const images = preview ? [...preview.querySelectorAll('img')] : [];
        if (!preview || images.length === 0) {
            return;
        }

        block.classList.add('novedades-media-block');
        block.setAttribute('draggable', 'true');
        block.setAttribute('contenteditable', 'false');

        images.forEach((image) => {
            image.draggable = false;
            image.setAttribute('contenteditable', 'false');
        });

        if (preview.parentElement !== block) {
            block.appendChild(preview);
        }

        [...block.children].forEach((child) => {
            if (child !== preview) {
                child.remove();
            }
        });

        [...preview.children].forEach((child) => {
            if (child.tagName !== 'IMG' && !child.classList.contains('ticket-editor-file__resize')) {
                child.remove();
            }
        });
    });
}

export function wirePreviewEscape(dotNetReference) {
    unwirePreviewEscape();

    previewEscapeHandler = async (event) => {
        if (event.key !== 'Escape') {
            return;
        }

        event.preventDefault();
        const reference = dotNetReference;
        unwirePreviewEscape();
        await reference.invokeMethodAsync('ClosePreviewFromKeyboardAsync');
    };

    document.addEventListener('keydown', previewEscapeHandler);
}

export function unwirePreviewEscape() {
    if (!previewEscapeHandler) {
        return;
    }

    document.removeEventListener('keydown', previewEscapeHandler);
    previewEscapeHandler = null;
}

function getDirectEditorChild(editor, node) {
    let current = node instanceof Element ? node : node?.parentElement;
    while (current && current.parentElement !== editor) {
        current = current.parentElement;
    }
    return current?.parentElement === editor ? current : null;
}

function findClosestEditorChild(editor, dragged, clientY) {
    const children = [...editor.children].filter((child) => child !== dragged);
    if (children.length === 0) {
        return null;
    }

    return children.find((child) => clientY <= child.getBoundingClientRect().bottom)
        || children[children.length - 1];
}

function dispatchInput(editor) {
    editor.dispatchEvent(new Event('input', { bubbles: true }));
}

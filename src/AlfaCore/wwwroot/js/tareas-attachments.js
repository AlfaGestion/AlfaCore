function attachFiles(input, files) {
    const supported = Array.from(files || []).filter(isSupported);
    if (!supported.length) return false;

    const transfer = new DataTransfer();
    for (const file of supported) {
        transfer.items.add(file);
    }

    input.value = '';
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
}

function isSupported(file) {
    return file && (
        file.type.startsWith('image/')
        || file.type.startsWith('audio/')
        || file.type.startsWith('video/')
    );
}

function filesFromPaste(event) {
    return Array.from(event.clipboardData?.items || [])
        .filter(item => item.kind === 'file')
        .map(item => item.getAsFile())
        .filter(Boolean);
}

export function bindTareasAttachments(dropzoneId, inputId) {
    const dropzone = document.getElementById(dropzoneId);
    const input = document.getElementById(inputId);
    if (!dropzone || !input) {
        return { dispose() { } };
    }

    let lastPasteKey = '';
    let lastPasteAt = 0;

    const buildPasteKey = files => files
        .map(file => `${file.name || 'clipboard'}:${file.type}:${file.size}:${file.lastModified || 0}`)
        .join('|');

    const onPaste = event => {
        const files = filesFromPaste(event);
        if (!files.length) return;

        const isInsideModal = Boolean(document.querySelector('.tareas-modal'));
        if (!isInsideModal) return;

        const key = buildPasteKey(files);
        const now = Date.now();
        if (key === lastPasteKey && now - lastPasteAt < 900) {
            event.preventDefault();
            event.stopPropagation();
            return;
        }

        if (!attachFiles(input, files)) return;

        lastPasteKey = key;
        lastPasteAt = now;
        event.preventDefault();
        event.stopPropagation();
    };

    const onDrop = event => {
        event.preventDefault();
        dropzone.classList.remove('is-file-dragging');
        attachFiles(input, event.dataTransfer?.files);
    };

    const onDragEnter = event => {
        event.preventDefault();
        dropzone.classList.add('is-file-dragging');
    };

    const onDragOver = event => {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
    };

    const onDragLeave = event => {
        if (event.relatedTarget && dropzone.contains(event.relatedTarget)) return;
        dropzone.classList.remove('is-file-dragging');
    };

    const onClick = () => dropzone.focus();

    dropzone.addEventListener('drop', onDrop);
    dropzone.addEventListener('dragenter', onDragEnter);
    dropzone.addEventListener('dragover', onDragOver);
    dropzone.addEventListener('dragleave', onDragLeave);
    dropzone.addEventListener('click', onClick);
    document.addEventListener('paste', onPaste, true);

    return {
        dispose() {
            dropzone.removeEventListener('drop', onDrop);
            dropzone.removeEventListener('dragenter', onDragEnter);
            dropzone.removeEventListener('dragover', onDragOver);
            dropzone.removeEventListener('dragleave', onDragLeave);
            dropzone.removeEventListener('click', onClick);
            document.removeEventListener('paste', onPaste, true);
        }
    };
}

export function bindTareasMenus() {
    const closeAllExcept = current => {
        document.querySelectorAll('details.tareas-menu[open]').forEach(menu => {
            if (menu !== current) {
                menu.open = false;
            }
        });
    };

    const onPointerDown = event => {
        const menu = event.target?.closest?.('details.tareas-menu');
        if (menu) return;
        closeAllExcept(null);
    };

    const onToggle = event => {
        const menu = event.target;
        if (!menu?.matches?.('details.tareas-menu') || !menu.open) return;
        closeAllExcept(menu);
    };

    const onKeyDown = event => {
        if (event.key !== 'Escape') return;
        closeAllExcept(null);
    };

    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('toggle', onToggle, true);
    document.addEventListener('keydown', onKeyDown);

    return {
        dispose() {
            document.removeEventListener('pointerdown', onPointerDown, true);
            document.removeEventListener('toggle', onToggle, true);
            document.removeEventListener('keydown', onKeyDown);
        }
    };
}

export function bindTareasInteractions() {
    const menus = bindTareasMenus();
    const drag = bindTareasTaskDrag();

    return {
        dispose() {
            menus.dispose();
            drag.dispose();
        }
    };
}

export function bindTareasTaskDrag() {
    let dragImage = null;

    const removeDragImage = () => {
        if (dragImage) {
            dragImage.remove();
            dragImage = null;
        }
    };

    const onDragStart = event => {
        const card = event.target?.closest?.('[data-tareas-drag-card="true"]');
        if (!card || !event.dataTransfer) return;

        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', 'move-task');

        removeDragImage();
        const rect = card.getBoundingClientRect();
        dragImage = card.cloneNode(true);
        dragImage.classList.add('tareas-row--drag-preview');
        dragImage.style.width = `${rect.width}px`;
        dragImage.style.position = 'fixed';
        dragImage.style.left = '-10000px';
        dragImage.style.top = '-10000px';
        dragImage.style.pointerEvents = 'none';
        dragImage.style.opacity = '1';
        document.body.appendChild(dragImage);
        event.dataTransfer.setDragImage(dragImage, Math.min(36, rect.width / 2), Math.min(28, rect.height / 2));
    };

    const onDragOver = event => {
        if (!event.target?.closest?.('.tareas-list-row')) return;
        event.preventDefault();
        if (event.dataTransfer) {
            event.dataTransfer.dropEffect = 'move';
        }
    };

    const onDrop = event => {
        if (!event.target?.closest?.('.tareas-list-row')) return;
        event.preventDefault();
        removeDragImage();
    };

    const onDragEnd = () => removeDragImage();

    document.addEventListener('dragstart', onDragStart, true);
    document.addEventListener('dragover', onDragOver, true);
    document.addEventListener('drop', onDrop, true);
    document.addEventListener('dragend', onDragEnd, true);

    return {
        dispose() {
            removeDragImage();
            document.removeEventListener('dragstart', onDragStart, true);
            document.removeEventListener('dragover', onDragOver, true);
            document.removeEventListener('drop', onDrop, true);
            document.removeEventListener('dragend', onDragEnd, true);
        }
    };
}

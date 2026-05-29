function attachFiles(input, files) {
    const supported = Array.from(files || []).filter(isSupported);
    if (!supported.length) return false;

    const transfer = new DataTransfer();
    for (const file of supported) {
        transfer.items.add(file);
    }

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

    const onPaste = event => {
        const files = filesFromPaste(event);
        if (!files.length) return;
        if (attachFiles(input, files)) {
            event.preventDefault();
        }
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

    dropzone.addEventListener('paste', onPaste);
    dropzone.addEventListener('drop', onDrop);
    dropzone.addEventListener('dragenter', onDragEnter);
    dropzone.addEventListener('dragover', onDragOver);
    dropzone.addEventListener('dragleave', onDragLeave);
    dropzone.addEventListener('click', onClick);
    document.addEventListener('paste', onPaste);

    return {
        dispose() {
            dropzone.removeEventListener('paste', onPaste);
            dropzone.removeEventListener('drop', onDrop);
            dropzone.removeEventListener('dragenter', onDragEnter);
            dropzone.removeEventListener('dragover', onDragOver);
            dropzone.removeEventListener('dragleave', onDragLeave);
            dropzone.removeEventListener('click', onClick);
            document.removeEventListener('paste', onPaste);
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

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

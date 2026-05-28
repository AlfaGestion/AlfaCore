function isSupported(file) {
    return file && (
        file.type.startsWith('image/')
        || file.type.startsWith('audio/')
        || file.type.startsWith('video/')
    );
}

function fileToPayload(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onerror = () => reject(reader.error || new Error('No se pudo leer el archivo.'));
        reader.onload = () => {
            const result = String(reader.result || '');
            const comma = result.indexOf(',');
            resolve({
                nombreArchivo: file.name || `pegado-${Date.now()}`,
                mimeType: file.type || 'application/octet-stream',
                contenidoBase64: comma >= 0 ? result.slice(comma + 1) : result
            });
        };
        reader.readAsDataURL(file);
    });
}

async function sendFiles(dotNetRef, files) {
    const supported = Array.from(files || []).filter(isSupported);
    if (!supported.length) return false;

    const payload = await Promise.all(supported.map(fileToPayload));
    await dotNetRef.invokeMethodAsync('ReceiveBrowserAttachmentsAsync', payload);
    return true;
}

function filesFromPaste(event) {
    return Array.from(event.clipboardData?.items || [])
        .filter(item => item.kind === 'file')
        .map(item => item.getAsFile())
        .filter(Boolean);
}

export function bindTareasAttachments(dropzoneId, dotNetRef) {
    const dropzone = document.getElementById(dropzoneId);
    if (!dropzone) {
        return { dispose() { } };
    }

    const onPaste = async event => {
        const files = filesFromPaste(event);
        if (!files.length) return;
        event.preventDefault();
        await sendFiles(dotNetRef, files);
    };

    const onDrop = async event => {
        event.preventDefault();
        dropzone.classList.remove('is-file-dragging');
        await sendFiles(dotNetRef, event.dataTransfer?.files);
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

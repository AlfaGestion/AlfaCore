(function () {
    const registry = new Map();
    let globalPasteBound = false;

    function attachFilesToInput(input, files) {
        if (!input || !files || files.length === 0) return;

        const dataTransfer = new DataTransfer();
        for (const file of files) {
            dataTransfer.items.add(file);
        }

        input.files = dataTransfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function extractFilesFromPasteEvent(event) {
        const items = Array.from(event.clipboardData?.items || []);
        return items
            .filter(item => item.kind === 'file')
            .map(item => item.getAsFile())
            .filter(file => !!file);
    }

    function extractFilesFromDropEvent(event) {
        return Array.from(event.dataTransfer?.files || []).filter(file => !!file);
    }

    function bindPasteDrop(dropzoneSelector, inputSelector) {
        const dropzone = document.querySelector(dropzoneSelector);
        const input = document.querySelector(inputSelector);
        if (!dropzone || !input) return;

        const key = `${dropzoneSelector}|${inputSelector}`;
        const existing = registry.get(key);
        if (existing) {
            existing.input = input;
            return;
        }

        dropzone.setAttribute('data-upload-paste-ready', '1');

        const onPaste = event => {
            const files = extractFilesFromPasteEvent(event);
            if (!files.length) return;
            event.preventDefault();
            attachFilesToInput(input, files);
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
            const nextTarget = event.relatedTarget;
            if (nextTarget && dropzone.contains(nextTarget)) return;
            dropzone.classList.remove('is-file-dragging');
        };

        const onDrop = event => {
            event.preventDefault();
            dropzone.classList.remove('is-file-dragging');
            const files = extractFilesFromDropEvent(event);
            if (!files.length) return;
            attachFilesToInput(input, files);
        };

        const onClick = () => dropzone.focus();

        dropzone.addEventListener('paste', onPaste);
        dropzone.addEventListener('dragenter', onDragEnter);
        dropzone.addEventListener('dragover', onDragOver);
        dropzone.addEventListener('dragleave', onDragLeave);
        dropzone.addEventListener('drop', onDrop);
        dropzone.addEventListener('click', onClick);

        registry.set(key, {
            key,
            dropzone,
            input,
            handlers: { onPaste, onDragEnter, onDragOver, onDragLeave, onDrop, onClick }
        });

        bindGlobalPaste();
    }

    function getFirstVisibleBinding() {
        for (const entry of registry.values()) {
            if (!entry?.dropzone || !entry?.input) continue;
            const zone = entry.dropzone;
            if (!(zone instanceof HTMLElement) || zone.offsetParent === null) continue;
            return entry;
        }

        return null;
    }

    function bindGlobalPaste() {
        if (globalPasteBound) return;
        globalPasteBound = true;

        document.addEventListener('paste', event => {
            const files = extractFilesFromPasteEvent(event);
            if (!files.length) return;

            const binding = getFirstVisibleBinding();
            if (!binding) return;

            event.preventDefault();
            attachFilesToInput(binding.input, files);
        });
    }

    window.alfaCoreInterfacesUpload = {
        bindPasteDrop,
        async pasteImageFromClipboard(inputSelector) {
            const input = document.querySelector(inputSelector);
            if (!input) {
                return { ok: false, message: 'No se encontró el selector de adjuntos.', addedCount: 0 };
            }

            if (!navigator.clipboard || typeof navigator.clipboard.read !== 'function') {
                return { ok: false, message: 'Tu navegador no permite leer imágenes del portapapeles con este botón. Usá Ctrl+V.', addedCount: 0 };
            }

            try {
                const clipboardItems = await navigator.clipboard.read();
                const files = [];

                for (const item of clipboardItems) {
                    const imageType = item.types.find(type => type.startsWith('image/'));
                    if (!imageType) continue;

                    const blob = await item.getType(imageType);
                    const extension = imageType.split('/')[1] || 'png';
                    const fileName = `pegado-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`;
                    files.push(new File([blob], fileName, { type: imageType, lastModified: Date.now() }));
                }

                if (!files.length) {
                    return { ok: false, message: 'El portapapeles no tiene una imagen para pegar.', addedCount: 0 };
                }

                attachFilesToInput(input, files);
                return { ok: true, message: '', addedCount: files.length };
            } catch (error) {
                const message = error && error.message
                    ? error.message
                    : 'No se pudo leer el portapapeles.';
                return { ok: false, message, addedCount: 0 };
            }
        },
        downloadBlob(base64, mimeType, fileName) {
            if (!base64) return;

            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i += 1) {
                bytes[i] = binary.charCodeAt(i);
            }

            const blob = new Blob([bytes], { type: mimeType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = url;
            anchor.download = fileName || 'adjunto';
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 30000);
        },
        openBlob(base64, mimeType, fileName) {
            if (!base64) return;

            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i += 1) {
                bytes[i] = binary.charCodeAt(i);
            }

            const blob = new Blob([bytes], { type: mimeType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const win = window.open(url, '_blank', 'noopener,noreferrer');
            if (!win) {
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = fileName || 'adjunto';
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
            }

            window.setTimeout(() => URL.revokeObjectURL(url), 30000);
        }
    };
})();

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

    function bindImageCompression(inputSelector, maxDimension = 2400, quality = 0.82) {
        const input = document.querySelector(inputSelector);
        if (!input || input.getAttribute('data-image-compression-ready') === '1') return;

        let processing = false;
        input.addEventListener('change', async event => {
            // El evento que generamos después de comprimir debe llegar a Blazor,
            // pero no volver a entrar en este mismo compresor.
            if (event.__alfaCoreImageCompressed) return;
            if (processing || !input.files?.length) return;
            processing = true;
            event.stopImmediatePropagation();
            try {
                const compressed = [];
                for (const file of Array.from(input.files)) {
                    if (!file.type.startsWith('image/')) { compressed.push(file); continue; }
                    const bitmap = await createImageBitmap(file);
                    const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height));
                    const canvas = document.createElement('canvas');
                    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
                    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
                    canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
                    bitmap.close();
                    compressed.push(blob ? new File([blob], file.name.replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg', lastModified: Date.now() }) : file);
                }
                const dataTransfer = new DataTransfer();
                compressed.forEach(file => dataTransfer.items.add(file));
                input.files = dataTransfer.files;
            } finally {
                processing = false;
                const compressedChange = new Event('change', { bubbles: true });
                compressedChange.__alfaCoreImageCompressed = true;
                input.dispatchEvent(compressedChange);
            }
        }, true);
        input.setAttribute('data-image-compression-ready', '1');
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
        bindImageCompression,
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
        async readImageFromClipboard() {
            if (!navigator.clipboard || typeof navigator.clipboard.read !== 'function') {
                return { ok: false, message: 'Tu navegador no permite leer imágenes del portapapeles.', mimeType: '', dataUrl: '', previewUrl: '', fileName: '' };
            }

            try {
                const clipboardItems = await navigator.clipboard.read();

                for (const item of clipboardItems) {
                    const imageType = item.types.find(type => type.startsWith('image/'));
                    if (!imageType) continue;

                    const blob = await item.getType(imageType);
                    const bytes = new Uint8Array(await blob.arrayBuffer());
                    let binary = '';
                    const chunkSize = 0x8000;
                    for (let i = 0; i < bytes.length; i += chunkSize) {
                        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
                    }

                    const base64 = btoa(binary);
                    const previewUrl = URL.createObjectURL(blob);
                    const extension = imageType.split('/')[1] || 'png';
                    const fileName = `pegado-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`;
                    return {
                        ok: true,
                        message: '',
                        mimeType: imageType,
                        fileName,
                        dataUrl: `data:${imageType};base64,${base64}`,
                        previewUrl
                    };
                }

                return { ok: false, message: 'El portapapeles no tiene una imagen para pegar.', mimeType: '', dataUrl: '', previewUrl: '', fileName: '' };
            } catch (error) {
                const message = error && error.message
                    ? error.message
                    : 'No se pudo leer el portapapeles.';
                return { ok: false, message, mimeType: '', dataUrl: '', previewUrl: '', fileName: '' };
            }
        },
        openUrl(url) {
            if (!url) return;

            const win = window.open(url, '_blank', 'noopener,noreferrer');
            if (win) {
                win.opener = null;
            }
        },
        async downloadUrl(url, fileName) {
            if (!url) return;

            try {
                const response = await fetch(url);
                if (!response.ok) throw new Error('No se pudo descargar la imagen.');
                const blob = await response.blob();
                const objectUrl = URL.createObjectURL(blob);
                const anchor = document.createElement('a');
                anchor.href = objectUrl;
                anchor.download = fileName || 'imagen-articulo';
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
                URL.revokeObjectURL(objectUrl);
            } catch {
                window.open(url, '_blank', 'noopener,noreferrer');
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

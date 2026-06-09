const wiredRoots = new WeakMap();
let previewState = null;

export function wireInformesEditor(root) {
    if (!root || wiredRoots.has(root)) {
        return;
    }
    const editor = root.querySelector?.('.informes-editor-surface');

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

    const pasteImage = async (event) => {
        if (!editor || !editor.contains(event.target)) {
            return;
        }

        const imageFile = Array.from(event.clipboardData?.items || [])
            .find((item) => item.kind === 'file' && item.type?.startsWith('image/'))
            ?.getAsFile();
        if (!imageFile) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        const range = getCurrentEditorRange(editor);
        try {
            const image = await readCompressedImage(imageFile);
            restoreEditorRange(editor, range);
            insertHtml(buildPastedImageBlock(image.src, image.name));
            normalizeInformesBlocks(editor);
            placeCaretAfterLastMedia(editor);
            dispatchEditorInput(editor);
        } catch (error) {
            console.error('No se pudo pegar la imagen en Informes.', error);
        }
    };

    const openImagePreview = (event) => {
        const image = event.target?.closest?.('.ticket-editor-file__preview img, .informes-editor-surface > img');
        if (!image || !editor?.contains(image)) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        openInformesImagePreview(image.currentSrc || image.src, image.alt || 'Imagen');
    };

    document.addEventListener('pointerdown', closeFromDocument, true);
    document.addEventListener('keydown', closeFromEscape, true);
    editor?.addEventListener('paste', pasteImage);
    editor?.addEventListener('dblclick', openImagePreview, true);
    wiredRoots.set(root, { closeFromDocument, closeFromEscape, pasteImage, openImagePreview, editor });
}

export function disposeInformesEditor(root) {
    const handlers = wiredRoots.get(root);
    if (!handlers) {
        return;
    }

    document.removeEventListener('pointerdown', handlers.closeFromDocument, true);
    document.removeEventListener('keydown', handlers.closeFromEscape, true);
    handlers.editor?.removeEventListener('paste', handlers.pasteImage);
    handlers.editor?.removeEventListener('dblclick', handlers.openImagePreview, true);
    closeInformesImagePreview();
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

    editor.querySelectorAll('.ticket-editor-file--media .ticket-editor-file__preview img').forEach((image) => {
        image.title = 'Doble click para previsualizar';
        image.style.maxWidth = '100%';
        image.style.height = 'auto';
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

function placeCaretAfterLastMedia(editor) {
    const mediaBlocks = editor.querySelectorAll('.ticket-editor-file--media');
    const media = mediaBlocks[mediaBlocks.length - 1];
    if (!media) {
        return;
    }

    const paragraph = ensureParagraphAfter(media);
    placeCaretInside(paragraph);
}

function getCurrentEditorRange(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return null;
    }

    return selection.getRangeAt(0).cloneRange();
}

function restoreEditorRange(editor, range) {
    editor.focus();
    if (!range) {
        return;
    }

    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
}

function insertHtml(html) {
    document.execCommand('insertHTML', false, html);
}

function buildPastedImageBlock(src, name) {
    const safeName = escapeHtml(name || 'Imagen pegada');
    const safeSrc = escapeAttribute(src);
    return `
        <div class="ticket-editor-file ticket-editor-file--media ticket-editor-file--has-preview" contenteditable="false" tabindex="0" data-ticket-media-block="true">
            <button type="button" class="ticket-editor-file__button" data-ticket-file-picker="true" data-ticket-upload-kind="media">
                <i class="bi bi-file-earmark-image ticket-editor-file__icon"></i>
                <span>Elegir medio</span>
            </button>
            <div class="ticket-editor-file__content">
                <strong>Imagen</strong>
                <span class="ticket-editor-file__status">${safeName}</span>
                <div class="ticket-editor-file__files"><span><i class="bi bi-check2-circle"></i>${safeName}</span></div>
                <div class="ticket-editor-file__preview">
                    <img src="${safeSrc}" alt="${safeName}" style="width: 520px; max-width: 100%; height: auto;" title="Doble click para previsualizar">
                    <span class="ticket-editor-file__resize" contenteditable="false"></span>
                </div>
                <small class="ticket-editor-file__replace-hint">Doble click para previsualizar</small>
            </div>
        </div>
        <p><br></p>`;
}

function readCompressedImage(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.addEventListener('load', () => {
            const image = new Image();
            image.addEventListener('load', () => {
                resolve({
                    name: file.name || `imagen_${Date.now()}.jpg`,
                    src: compressImage(image)
                });
            }, { once: true });
            image.addEventListener('error', () => reject(new Error('No se pudo leer la imagen.')), { once: true });
            image.src = reader.result;
        }, { once: true });
        reader.addEventListener('error', () => reject(reader.error), { once: true });
        reader.readAsDataURL(file);
    });
}

function compressImage(image) {
    const maxSide = 960;
    const width = image.naturalWidth || image.width;
    const height = image.naturalHeight || image.height;
    const ratio = Math.min(1, maxSide / Math.max(width, height));
    const targetWidth = Math.max(1, Math.round(width * ratio));
    const targetHeight = Math.max(1, Math.round(height * ratio));
    const canvas = document.createElement('canvas');
    canvas.width = targetWidth;
    canvas.height = targetHeight;

    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, targetWidth, targetHeight);
    ctx.drawImage(image, 0, 0, targetWidth, targetHeight);

    let quality = 0.82;
    let dataUrl = canvas.toDataURL('image/jpeg', quality);
    while (dataUrl.length > 160000 && quality > 0.46) {
        quality -= 0.08;
        dataUrl = canvas.toDataURL('image/jpeg', quality);
    }

    return dataUrl;
}

function openInformesImagePreview(src, label) {
    closeInformesImagePreview();
    previewState = { zoom: 1, rotation: 0 };

    const overlay = document.createElement('div');
    overlay.className = 'attachment-preview-overlay informes-image-preview';
    overlay.innerHTML = `
        <div class="attachment-preview-modal" role="dialog" aria-modal="true">
            <header class="attachment-preview-header">
                <span>${escapeHtml(label)}</span>
                <div class="attachment-preview-actions">
                    <button type="button" class="attachment-preview-action" data-preview-action="zoom-out" title="Alejar" aria-label="Alejar"><i class="bi bi-zoom-out"></i></button>
                    <button type="button" class="attachment-preview-action attachment-preview-zoom-button" data-preview-action="reset" title="Restablecer vista" aria-label="Restablecer vista">100%</button>
                    <button type="button" class="attachment-preview-action" data-preview-action="zoom-in" title="Acercar" aria-label="Acercar"><i class="bi bi-zoom-in"></i></button>
                    <button type="button" class="attachment-preview-action" data-preview-action="rotate-left" title="Rotar a la izquierda" aria-label="Rotar a la izquierda"><i class="bi bi-arrow-counterclockwise"></i></button>
                    <button type="button" class="attachment-preview-action" data-preview-action="rotate-right" title="Rotar a la derecha" aria-label="Rotar a la derecha"><i class="bi bi-arrow-clockwise"></i></button>
                    <button type="button" class="attachment-preview-action" data-preview-action="close" title="Cerrar" aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>
                </div>
            </header>
            <div class="attachment-preview-body informes-image-preview__body">
                <div class="attachment-preview-canvas">
                    <img src="${escapeAttribute(src)}" alt="${escapeHtml(label)}" draggable="false">
                </div>
            </div>
        </div>`;

    overlay.addEventListener('click', (event) => {
        if (event.target === overlay) {
            closeInformesImagePreview();
        }
    });
    overlay.addEventListener('click', handlePreviewAction);
    overlay.querySelector('.attachment-preview-canvas')?.addEventListener('wheel', (event) => {
        event.preventDefault();
        setPreviewZoom(previewState.zoom + (event.deltaY < 0 ? .15 : -.15));
    }, { passive: false });
    document.addEventListener('keydown', handlePreviewKeydown);
    document.body.appendChild(overlay);
    updatePreviewImage();
}

function handlePreviewAction(event) {
    const action = event.target?.closest?.('[data-preview-action]')?.dataset.previewAction;
    if (!action || !previewState) {
        return;
    }

    if (action === 'close') closeInformesImagePreview();
    if (action === 'zoom-in') setPreviewZoom(previewState.zoom + .25);
    if (action === 'zoom-out') setPreviewZoom(previewState.zoom - .25);
    if (action === 'reset') {
        previewState.zoom = 1;
        previewState.rotation = 0;
        updatePreviewImage();
    }
    if (action === 'rotate-left') {
        previewState.rotation = ((previewState.rotation - 90) % 360 + 360) % 360;
        updatePreviewImage();
    }
    if (action === 'rotate-right') {
        previewState.rotation = (previewState.rotation + 90) % 360;
        updatePreviewImage();
    }
}

function handlePreviewKeydown(event) {
    if (event.key === 'Escape') {
        closeInformesImagePreview();
    }
}

function setPreviewZoom(value) {
    if (!previewState) {
        return;
    }

    previewState.zoom = Math.min(4, Math.max(.5, value));
    updatePreviewImage();
}

function updatePreviewImage() {
    const overlay = document.querySelector('.informes-image-preview');
    const image = overlay?.querySelector('.attachment-preview-canvas img');
    const zoomButton = overlay?.querySelector('.attachment-preview-zoom-button');
    if (!image || !previewState) {
        return;
    }

    image.style.setProperty('--preview-zoom', previewState.zoom.toFixed(3));
    image.style.setProperty('--preview-rotation', `${previewState.rotation}deg`);
    if (zoomButton) {
        zoomButton.textContent = `${Math.round(previewState.zoom * 100)}%`;
    }
}

function closeInformesImagePreview() {
    document.querySelector('.informes-image-preview')?.remove();
    document.removeEventListener('keydown', handlePreviewKeydown);
    previewState = null;
}

function dispatchEditorInput(editor) {
    editor.dispatchEvent(new Event('input', { bubbles: true }));
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

function escapeAttribute(value) {
    return escapeHtml(value).replace(/`/g, '&#096;');
}

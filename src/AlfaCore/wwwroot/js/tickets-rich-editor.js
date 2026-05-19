const allowedTags = new Set([
    'A', 'B', 'BLOCKQUOTE', 'BR', 'BUTTON', 'CODE', 'DETAILS', 'DIV', 'EM', 'H1', 'H2', 'H3',
    'HR', 'I', 'IMG', 'INPUT', 'LI', 'OL', 'P', 'PRE', 'SMALL', 'SPAN', 'STRONG', 'SUMMARY', 'UL'
]);

const allowedClasses = new Set([
    'ticket-editor-banner',
    'ticket-editor-banner__icon',
    'ticket-editor-banner--info',
    'ticket-editor-banner--success',
    'ticket-editor-banner--warning',
    'ticket-editor-banner--danger',
    'ticket-editor-button',
    'ticket-editor-emoji-picker',
    'ticket-editor-emoji-picker__grid',
    'ticket-editor-file',
    'ticket-editor-file--document',
    'ticket-editor-file--media',
    'ticket-editor-file--upload',
    'ticket-editor-file__button',
    'ticket-editor-file__content',
    'ticket-editor-file__files',
    'ticket-editor-file__icon',
    'ticket-editor-file__preview',
    'ticket-editor-file__replace-hint',
    'ticket-editor-file__status',
    'ticket-editor-file--selected',
    'ticket-editor-index',
    'ticket-editor-media',
    'ticket-editor-stars',
    'ticket-editor-todo',
    'ticket-link-popover',
    'ticket-link-popover__actions',
    'ticket-link-popover__fields',
    'bi',
    'bi-check-circle-fill',
    'bi-check2-circle',
    'bi-exclamation-triangle-fill',
    'bi-file-earmark-image',
    'bi-files',
    'bi-info-circle-fill',
    'bi-upload',
    'bi-x-octagon-fill'
]);

const wiredEditors = new WeakSet();
const savedRanges = new WeakMap();
let selectedMediaBlock = null;

const emojiCodePoints = [
    0x1F600, 0x1F603, 0x1F604, 0x1F601, 0x1F606, 0x1F605, 0x1F602, 0x1F642,
    0x1F643, 0x1F609, 0x1F60A, 0x1F607, 0x1F970, 0x1F60D, 0x1F929, 0x1F618,
    0x1F617, 0x1F61A, 0x1F619, 0x1F972, 0x1F60B, 0x1F61B, 0x1F61C, 0x1F92A,
    0x1F61D, 0x1F911, 0x1F917, 0x1F92D, 0x1F92B, 0x1F914, 0x1F910, 0x1F928,
    0x1F610, 0x1F611, 0x1F636, 0x1F60F, 0x1F612, 0x1F644, 0x1F62C, 0x1F62E,
    0x1F925, 0x1F60C, 0x1F614, 0x1F62A, 0x1F924, 0x1F634, 0x1F637, 0x1F912,
    0x1F915, 0x1F922, 0x1F92E, 0x1F927, 0x1F975, 0x1F976, 0x1F974, 0x1F635,
    0x1F92F, 0x1F920, 0x1F973, 0x1F978, 0x1F60E, 0x1F913, 0x1F9D0, 0x1F615,
    0x1F61F, 0x1F641, 0x2639, 0x1F62F, 0x1F632, 0x1F633, 0x1F97A, 0x1F626,
    0x1F627, 0x1F628, 0x1F630, 0x1F625, 0x1F622, 0x1F62D, 0x1F631, 0x1F616,
    0x1F623, 0x1F61E, 0x1F613, 0x1F629, 0x1F62B, 0x1F971, 0x1F624, 0x1F621,
    0x1F620, 0x1F92C, 0x1F608, 0x1F47F, 0x1F480, 0x1F44B, 0x1F91A, 0x1F590,
    0x270B, 0x1F596, 0x1F44C, 0x1F90C, 0x1F90F, 0x270C, 0x1F91E, 0x1F91F,
    0x1F918, 0x1F919, 0x1F448, 0x1F449, 0x1F446, 0x1F447, 0x261D, 0x1F44D,
    0x1F44E, 0x270A, 0x1F44A, 0x1F91B, 0x1F91C, 0x1F44F, 0x1F64C, 0x1F450,
    0x1F932, 0x1F91D, 0x1F64F, 0x270D, 0x1F485, 0x1F4AA, 0x1F9E0, 0x1F9D1,
    0x1F468, 0x1F469, 0x1F4BB, 0x1F527, 0x1F4A1, 0x1F4CC, 0x1F4CE, 0x1F4C4,
    0x1F4C1, 0x1F4E7, 0x1F4DE, 0x1F4AC, 0x1F4A5, 0x1F4AF, 0x2705, 0x274C,
    0x26A0, 0x2757, 0x2753, 0x2139, 0x2B50, 0x1F525, 0x1F680, 0x1F6E0,
    0x1F4B0, 0x1F4C8, 0x1F4C9, 0x1F512, 0x1F513, 0x1F50D, 0x1F514, 0x23F0
];

export function setHtml(editor, html) {
    if (!editor) {
        return;
    }

    editor.innerHTML = sanitizeHtml(html || '');
    ensureEditableBase(editor);
    wireFilePickers(editor);
}

export function getHtml(editor) {
    if (!editor) {
        return '';
    }

    const clone = editor.cloneNode(true);
    cleanupTransientEditorState(clone);
    return sanitizeHtml(clone.innerHTML || '');
}

export function shouldShowCommands(editor) {
    if (!editor) {
        return false;
    }

    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return false;
    }

    const text = getTextBeforeCaret(editor);
    const slashIndex = text.lastIndexOf('/');
    if (slashIndex < 0) {
        return false;
    }

    const before = slashIndex === 0 ? '' : text[slashIndex - 1];
    const query = text.slice(slashIndex + 1);
    return (slashIndex === 0 || /\s/.test(before)) && !/\s/.test(query);
}

export function runCommand(editor, command) {
    if (!editor) {
        return;
    }

    wireFilePickers(editor);
    editor.focus();
    removeSlashTrigger(editor);

    switch (command) {
        case 'h1':
        case 'h2':
        case 'h3':
            document.execCommand('formatBlock', false, command.toUpperCase());
            break;
        case 'paragraph':
            document.execCommand('formatBlock', false, 'P');
            break;
        case 'bulletList':
            document.execCommand('insertUnorderedList');
            break;
        case 'numberedList':
            document.execCommand('insertOrderedList');
            break;
        case 'separator':
            insertHtml('<hr><p><br></p>');
            break;
        case 'checkList':
            insertHtml('<ul class="ticket-editor-todo"><li><input type="checkbox"> Tarea pendiente</li><li><input type="checkbox"> Tarea pendiente</li></ul><p><br></p>');
            break;
        case 'toggle':
            insertHtml('<details open><summary>Resumen</summary><p>Contenido del detalle...</p></details><p><br></p>');
            break;
        case 'infoBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--info"><i class="bi bi-info-circle-fill ticket-editor-banner__icon"></i><p>Mensaje informativo...</p></div><p><br></p>');
            break;
        case 'successBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--success"><i class="bi bi-check-circle-fill ticket-editor-banner__icon"></i><p>Resultado correcto...</p></div><p><br></p>');
            break;
        case 'warningBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--warning"><i class="bi bi-exclamation-triangle-fill ticket-editor-banner__icon"></i><p>Revisar este punto...</p></div><p><br></p>');
            break;
        case 'dangerBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--danger"><i class="bi bi-x-octagon-fill ticket-editor-banner__icon"></i><p>Punto critico...</p></div><p><br></p>');
            break;
        case 'codeBlock':
            insertHtml('<pre><code>Detalle tecnico</code></pre><p><br></p>');
            break;
        case 'media':
            insertHtml(buildFilePickerBlock('media'));
            break;
        case 'upload':
            insertHtml(buildFilePickerBlock('upload'));
            break;
        case 'file':
            insertHtml(buildFilePickerBlock('document'));
            break;
        case 'link':
            showLinkPopover(editor, 'link');
            break;
        case 'button':
            showLinkPopover(editor, 'button');
            break;
        case 'article':
            insertHtml('<div class="ticket-editor-file">Articulo: referencia interna</div><p><br></p>');
            break;
        case 'quote':
            insertHtml('<blockquote>Cita o referencia...</blockquote><p><br></p>');
            break;
        case 'index':
            insertHtml('<div class="ticket-editor-index"><strong>Indice</strong><ol><li>Seccion 1</li><li>Seccion 2</li></ol></div><p><br></p>');
            break;
        case 'emoji':
            showEmojiPicker(editor);
            break;
        case 'stars3':
            insertHtml('<span class="ticket-editor-stars">&#9733;&#9733;&#9733;&#9734;&#9734;</span>');
            break;
        case 'stars5':
            insertHtml('<span class="ticket-editor-stars">&#9733;&#9733;&#9733;&#9733;&#9733;</span>');
            break;
    }

    ensureEditableBase(editor);
    wireFilePickers(editor);
}

function insertHtml(html) {
    document.execCommand('insertHTML', false, html);
}

function ensureEditableBase(editor) {
    if (!editor.innerHTML.trim()) {
        editor.innerHTML = '<p><br></p>';
    }
}

function saveCurrentRange(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return;
    }

    savedRanges.set(editor, selection.getRangeAt(0).cloneRange());
}

function restoreSavedRange(editor) {
    const range = savedRanges.get(editor);
    if (!range) {
        editor.focus();
        return;
    }

    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    editor.focus();
}

function showLinkPopover(editor, mode) {
    saveCurrentRange(editor);
    closeEditorPopup(editor);

    const selectedText = getSelectedText(editor) || getSavedRangeText(editor);
    const config = mode === 'button'
        ? { title: 'Boton', textLabel: 'Texto del boton', defaultText: selectedText || 'Abrir enlace' }
        : { title: 'Enlace', textLabel: 'Texto del enlace', defaultText: selectedText || 'Texto del enlace' };

    const popup = document.createElement('div');
    popup.className = 'ticket-link-popover';
    popup.innerHTML = `
        <strong>${config.title}</strong>
        <div class="ticket-link-popover__fields">
            <label>${config.textLabel}<input type="text" data-ticket-link-text value="${escapeAttribute(config.defaultText)}"></label>
            <label>URL<input type="url" data-ticket-link-url value="https://"></label>
        </div>
        <div class="ticket-link-popover__actions">
            <button type="button" class="btn btn--ghost btn--sm" data-ticket-popup-cancel>Cancelar</button>
            <button type="button" class="btn btn--primary btn--sm" data-ticket-popup-apply>Insertar</button>
        </div>`;

    editor.parentElement.appendChild(popup);

    const textInput = popup.querySelector('[data-ticket-link-text]');
    const urlInput = popup.querySelector('[data-ticket-link-url]');
    popup.querySelector('[data-ticket-popup-cancel]').addEventListener('click', () => popup.remove());
    popup.querySelector('[data-ticket-popup-apply]').addEventListener('click', () => {
        const text = textInput.value.trim() || config.defaultText;
        const url = normalizeUrl(urlInput.value.trim());
        if (!url) {
            urlInput.focus();
            return;
        }

        restoreSavedRange(editor);
        const className = mode === 'button' ? ' class="ticket-editor-button"' : '';
        insertHtml(`<a${className} href="${escapeAttribute(url)}" target="_blank" rel="noopener">${escapeHtml(text)}</a>`);
        if (mode === 'button') {
            insertHtml('<p><br></p>');
        }
        popup.remove();
        dispatchEditorInput(editor);
    });

    urlInput.focus();
    urlInput.select();
}

function showEmojiPicker(editor) {
    saveCurrentRange(editor);
    closeEditorPopup(editor);

    const popup = document.createElement('div');
    popup.className = 'ticket-editor-emoji-picker';
    popup.innerHTML = `
        <strong>Emoji</strong>
        <div class="ticket-editor-emoji-picker__grid">
            ${emojiCodePoints.map((codePoint) => `<button type="button" data-ticket-emoji="${codePoint}">${String.fromCodePoint(codePoint)}</button>`).join('')}
        </div>`;

    editor.parentElement.appendChild(popup);
    popup.querySelectorAll('[data-ticket-emoji]').forEach((button) => {
        button.addEventListener('click', () => {
            restoreSavedRange(editor);
            insertHtml(String.fromCodePoint(Number(button.dataset.ticketEmoji)));
            popup.remove();
            dispatchEditorInput(editor);
        });
    });
}

function closeEditorPopup(editor) {
    editor.parentElement
        ?.querySelectorAll('.ticket-link-popover, .ticket-editor-emoji-picker')
        .forEach((popup) => popup.remove());
}

function getSelectedText(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return '';
    }

    return selection.toString().trim();
}

function getSavedRangeText(editor) {
    const range = savedRanges.get(editor);
    return range ? range.toString().trim() : '';
}

function normalizeUrl(value) {
    if (!value) {
        return '';
    }

    if (/^(https?:\/\/|mailto:|tel:|#|\/)/i.test(value)) {
        return value;
    }

    return `https://${value}`;
}

function wireFilePickers(editor) {
    if (wiredEditors.has(editor)) {
        return;
    }

    ['keyup', 'mouseup', 'focus', 'input'].forEach((eventName) => {
        editor.addEventListener(eventName, () => saveCurrentRange(editor));
    });

    editor.addEventListener('click', (event) => {
        const button = event.target?.closest?.('[data-ticket-file-picker="true"]');
        if (!button || !editor.contains(button)) {
            selectMediaBlock(editor, event.target?.closest?.('.ticket-editor-file--media'));
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        openTicketFilePicker(editor, button);
    });

    editor.addEventListener('dblclick', (event) => {
        const block = event.target?.closest?.('.ticket-editor-file--media');
        if (!block || !editor.contains(block)) {
            return;
        }

        const button = block.querySelector('[data-ticket-file-picker="true"]');
        if (button) {
            event.preventDefault();
            openTicketFilePicker(editor, button);
        }
    });

    editor.addEventListener('keydown', (event) => {
        if (!['Backspace', 'Delete'].includes(event.key)) {
            return;
        }

        const block = selectedMediaBlock && editor.contains(selectedMediaBlock)
            ? selectedMediaBlock
            : getMediaBlockFromSelection(editor);
        if (!block) {
            return;
        }

        event.preventDefault();
        const next = block.nextElementSibling;
        block.remove();
        if (next?.matches('p') && next.textContent.trim() === '') {
            next.remove();
        }
        selectedMediaBlock = null;
        ensureEditableBase(editor);
        dispatchEditorInput(editor);
    });

    wiredEditors.add(editor);
}

function selectMediaBlock(editor, block) {
    if (selectedMediaBlock && selectedMediaBlock !== block) {
        selectedMediaBlock.classList.remove('ticket-editor-file--selected');
    }

    selectedMediaBlock = block && editor.contains(block) ? block : null;
    if (selectedMediaBlock) {
        selectedMediaBlock.classList.add('ticket-editor-file--selected');
        selectedMediaBlock.focus();
    }
}

function getMediaBlockFromSelection(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return null;
    }

    const node = selection.anchorNode.nodeType === Node.ELEMENT_NODE
        ? selection.anchorNode
        : selection.anchorNode.parentElement;
    return node?.closest?.('.ticket-editor-file--media') ?? null;
}

function buildFilePickerBlock(kind) {
    const config = getFilePickerConfig(kind);
    return `
        <div class="ticket-editor-file ticket-editor-file--${config.kind}" contenteditable="false" tabindex="0" data-ticket-media-block="${config.kind === 'media'}">
            <button type="button" class="ticket-editor-file__button" data-ticket-file-picker="true" data-ticket-upload-kind="${config.kind}">
                <i class="bi ${config.icon} ticket-editor-file__icon"></i>
                <span>${escapeHtml(config.button)}</span>
            </button>
            <div class="ticket-editor-file__content">
                <strong>${escapeHtml(config.title)}</strong>
                <span class="ticket-editor-file__status">${escapeHtml(config.status)}</span>
                <div class="ticket-editor-file__files"></div>
                <div class="ticket-editor-file__preview"></div>
                ${config.kind === 'media' ? '<small class="ticket-editor-file__replace-hint">Doble click para reemplazar</small>' : ''}
            </div>
        </div>
        <p><br></p>`;
}

function getFilePickerConfig(kind) {
    if (kind === 'media') {
        return {
            kind: 'media',
            icon: 'bi-file-earmark-image',
            title: 'Medio',
            button: 'Elegir medio',
            status: 'Imagen, audio o video',
            accept: 'image/*,audio/*,video/*',
            multiple: true
        };
    }

    if (kind === 'upload') {
        return {
            kind: 'upload',
            icon: 'bi-upload',
            title: 'Archivo',
            button: 'Elegir archivo',
            status: 'Selecciona un archivo',
            accept: '',
            multiple: true
        };
    }

    return {
        kind: 'document',
        icon: 'bi-files',
        title: 'Documento',
        button: 'Elegir documento',
        status: 'Selecciona un documento',
        accept: '.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv',
        multiple: true
    };
}

function openTicketFilePicker(editor, button) {
    const config = getFilePickerConfig(button.dataset.ticketUploadKind);
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = config.multiple;
    if (config.accept) {
        input.accept = config.accept;
    }

    input.addEventListener('change', () => {
        const files = [...input.files];
        if (files.length === 0) {
            return;
        }

        const block = button.closest('.ticket-editor-file');
        const status = block?.querySelector('.ticket-editor-file__status');
        const list = block?.querySelector('.ticket-editor-file__files');
        const preview = block?.querySelector('.ticket-editor-file__preview');
        if (status) {
            status.textContent = `${files.length} archivo(s) seleccionado(s)`;
        }
        if (list) {
            list.innerHTML = files
                .map((file) => `<span><i class="bi bi-check2-circle"></i>${escapeHtml(file.name)} <small>${formatFileSize(file.size)}</small></span>`)
                .join('');
        }
        if (preview) {
            preview.innerHTML = '';
            Promise.all(files
                .filter((file) => file.type.startsWith('image/'))
                .map((file) => readFileAsDataUrl(file).then((src) => ({ file, src }))))
                .then((images) => {
                    images.forEach(({ file, src }) => {
                        const image = document.createElement('img');
                        image.src = src;
                        image.alt = file.name;
                        preview.appendChild(image);
                    });
                    dispatchEditorInput(editor);
                });
        }

        dispatchEditorInput(editor);
    }, { once: true });

    input.click();
}

function dispatchEditorInput(editor) {
    editor.dispatchEvent(new Event('input', { bubbles: true }));
}

function readFileAsDataUrl(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.addEventListener('load', () => resolve(reader.result));
        reader.addEventListener('error', () => reject(reader.error));
        reader.readAsDataURL(file);
    });
}

function cleanupTransientEditorState(root) {
    root.querySelectorAll('.ticket-editor-file--selected').forEach((item) => {
        item.classList.remove('ticket-editor-file--selected');
    });
}

function formatFileSize(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) {
        return '';
    }

    const units = ['B', 'KB', 'MB', 'GB'];
    let size = bytes;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
        size /= 1024;
        unit += 1;
    }

    return `${size.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function removeSlashTrigger(editor) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.anchorNode)) {
        return;
    }

    const text = getTextBeforeCaret(editor);
    const slashIndex = text.lastIndexOf('/');
    if (slashIndex < 0) {
        return;
    }

    const charsToDelete = text.length - slashIndex;
    for (let i = 0; i < charsToDelete; i += 1) {
        document.execCommand('delete', false);
    }
}

function getTextBeforeCaret(root) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) {
        return '';
    }

    const range = selection.getRangeAt(0).cloneRange();
    range.selectNodeContents(root);
    range.setEnd(selection.anchorNode, selection.anchorOffset);
    return range.toString();
}

function sanitizeHtml(html) {
    const template = document.createElement('template');
    template.innerHTML = html || '';
    sanitizeNode(template.content);
    return template.innerHTML;
}

function sanitizeNode(node) {
    [...node.childNodes].forEach((child) => {
        if (child.nodeType === Node.TEXT_NODE) {
            return;
        }

        if (child.nodeType !== Node.ELEMENT_NODE || !allowedTags.has(child.tagName)) {
            child.remove();
            return;
        }

        [...child.attributes].forEach((attr) => {
            const name = attr.name.toLowerCase();
            if (name === 'class') {
                child.className = child.className
                    .split(/\s+/)
                    .filter((item) => allowedClasses.has(item))
                    .join(' ');
                return;
            }

            if (child.tagName === 'A' && ['href', 'target', 'rel'].includes(name)) {
                if (name === 'href' && !isAllowedUrl(attr.value)) {
                    child.removeAttribute(attr.name);
                }
                return;
            }

            if (child.tagName === 'IMG' && ['src', 'alt'].includes(name)) {
                if (name === 'src' && !isAllowedImageUrl(attr.value)) {
                    child.removeAttribute(attr.name);
                }
                return;
            }

            if (child.tagName === 'INPUT' && ['type', 'checked'].includes(name)) {
                if (name === 'type' && attr.value !== 'checkbox') {
                    child.removeAttribute(attr.name);
                }
                return;
            }

            if (child.tagName === 'BUTTON' && ['type', 'data-ticket-file-picker', 'data-ticket-upload-kind'].includes(name)) {
                return;
            }

            if (child.tagName === 'DIV' && name === 'data-ticket-media-block') {
                return;
            }

            if (child.tagName === 'DETAILS' && name === 'open') {
                return;
            }

            if (['contenteditable', 'tabindex'].includes(name)) {
                return;
            }

            child.removeAttribute(attr.name);
        });

        sanitizeNode(child);
    });
}

function isAllowedUrl(value) {
    return /^(https?:\/\/|mailto:|tel:|#|\/)/i.test(value || '');
}

function isAllowedImageUrl(value) {
    return /^(https?:\/\/|\/|data:image\/|blob:)/i.test(value || '');
}

function escapeHtml(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function escapeAttribute(value) {
    return escapeHtml(value).replaceAll('`', '&#96;');
}

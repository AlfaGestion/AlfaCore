const allowedTags = new Set([
    'A', 'B', 'BLOCKQUOTE', 'BR', 'BUTTON', 'CODE', 'DETAILS', 'DIV', 'EM', 'H1', 'H2', 'H3',
    'HR', 'I', 'INPUT', 'LI', 'OL', 'P', 'PRE', 'SMALL', 'SPAN', 'STRONG', 'SUMMARY', 'UL'
]);

const allowedClasses = new Set([
    'ticket-editor-banner',
    'ticket-editor-banner__icon',
    'ticket-editor-banner--info',
    'ticket-editor-banner--success',
    'ticket-editor-banner--warning',
    'ticket-editor-banner--danger',
    'ticket-editor-button',
    'ticket-editor-file',
    'ticket-editor-file--document',
    'ticket-editor-file--media',
    'ticket-editor-file--upload',
    'ticket-editor-file__button',
    'ticket-editor-file__content',
    'ticket-editor-file__files',
    'ticket-editor-file__icon',
    'ticket-editor-file__status',
    'ticket-editor-index',
    'ticket-editor-media',
    'ticket-editor-stars',
    'ticket-editor-todo',
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

    return sanitizeHtml(editor.innerHTML || '');
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
            createLink();
            break;
        case 'button':
            insertHtml('<a class="ticket-editor-button" href="https://" target="_blank" rel="noopener">Boton</a><p><br></p>');
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
            insertHtml('&#128578;');
            break;
        case 'stars3':
            insertHtml('<span class="ticket-editor-stars">&#9733;&#9733;&#9733;&#9734;&#9734;</span>');
            break;
        case 'stars5':
            insertHtml('<span class="ticket-editor-stars">&#9733;&#9733;&#9733;&#9733;&#9733;</span>');
            break;
        case 'aiDraft':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--info"><strong>Borrador IA</strong><p>Redactar respuesta o resumen del caso.</p></div><p><br></p>');
            break;
    }

    ensureEditableBase(editor);
    wireFilePickers(editor);
}

function insertHtml(html) {
    document.execCommand('insertHTML', false, html);
}

function createLink() {
    const selection = window.getSelection();
    const selectedText = selection && selection.toString() ? selection.toString() : 'Texto del enlace';
    const url = window.prompt('URL del enlace', 'https://');
    if (!url) {
        return;
    }

    insertHtml(`<a href="${escapeAttribute(url)}" target="_blank" rel="noopener">${escapeHtml(selectedText)}</a>`);
}

function ensureEditableBase(editor) {
    if (!editor.innerHTML.trim()) {
        editor.innerHTML = '<p><br></p>';
    }
}

function wireFilePickers(editor) {
    if (wiredEditors.has(editor)) {
        return;
    }

    editor.addEventListener('click', (event) => {
        const button = event.target?.closest?.('[data-ticket-file-picker="true"]');
        if (!button || !editor.contains(button)) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        openTicketFilePicker(editor, button);
    });

    wiredEditors.add(editor);
}

function buildFilePickerBlock(kind) {
    const config = getFilePickerConfig(kind);
    return `
        <div class="ticket-editor-file ticket-editor-file--${config.kind}" contenteditable="false">
            <button type="button" class="ticket-editor-file__button" data-ticket-file-picker="true" data-ticket-upload-kind="${config.kind}">
                <i class="bi ${config.icon} ticket-editor-file__icon"></i>
                <span>${escapeHtml(config.button)}</span>
            </button>
            <div class="ticket-editor-file__content">
                <strong>${escapeHtml(config.title)}</strong>
                <span class="ticket-editor-file__status">${escapeHtml(config.status)}</span>
                <div class="ticket-editor-file__files"></div>
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
        if (status) {
            status.textContent = `${files.length} archivo(s) seleccionado(s)`;
        }
        if (list) {
            list.innerHTML = files
                .map((file) => `<span><i class="bi bi-check2-circle"></i>${escapeHtml(file.name)} <small>${formatFileSize(file.size)}</small></span>`)
                .join('');
        }

        dispatchEditorInput(editor);
    }, { once: true });

    input.click();
}

function dispatchEditorInput(editor) {
    editor.dispatchEvent(new Event('input', { bubbles: true }));
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

            if (child.tagName === 'INPUT' && ['type', 'checked'].includes(name)) {
                if (name === 'type' && attr.value !== 'checkbox') {
                    child.removeAttribute(attr.name);
                }
                return;
            }

            if (child.tagName === 'BUTTON' && ['type', 'data-ticket-file-picker', 'data-ticket-upload-kind'].includes(name)) {
                return;
            }

            if (child.tagName === 'DETAILS' && name === 'open') {
                return;
            }

            if (name === 'contenteditable') {
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

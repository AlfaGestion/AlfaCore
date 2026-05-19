const allowedTags = new Set([
    'A', 'B', 'BLOCKQUOTE', 'BR', 'CODE', 'DETAILS', 'DIV', 'EM', 'H1', 'H2', 'H3',
    'HR', 'I', 'INPUT', 'LI', 'OL', 'P', 'PRE', 'SMALL', 'SPAN', 'STRONG', 'SUMMARY', 'UL'
]);

const allowedClasses = new Set([
    'ticket-editor-banner',
    'ticket-editor-banner--info',
    'ticket-editor-banner--success',
    'ticket-editor-banner--warning',
    'ticket-editor-banner--danger',
    'ticket-editor-button',
    'ticket-editor-file',
    'ticket-editor-index',
    'ticket-editor-media',
    'ticket-editor-stars',
    'ticket-editor-todo'
]);

export function setHtml(editor, html) {
    if (!editor) {
        return;
    }

    editor.innerHTML = sanitizeHtml(html || '');
    ensureEditableBase(editor);
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
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--info"><strong>Información</strong><p>Mensaje informativo...</p></div><p><br></p>');
            break;
        case 'successBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--success"><strong>Éxito</strong><p>Resultado correcto...</p></div><p><br></p>');
            break;
        case 'warningBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--warning"><strong>Advertencia</strong><p>Revisar este punto...</p></div><p><br></p>');
            break;
        case 'dangerBanner':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--danger"><strong>Peligro</strong><p>Punto crítico...</p></div><p><br></p>');
            break;
        case 'codeBlock':
            insertHtml('<pre><code>Detalle técnico</code></pre><p><br></p>');
            break;
        case 'media':
            insertHtml('<div class="ticket-editor-media">Medio: pegá una URL de imagen, icono o video</div><p><br></p>');
            break;
        case 'upload':
            insertHtml('<div class="ticket-editor-file">Archivo para subir: adjuntar desde la conversación o indicar ruta/enlace</div><p><br></p>');
            break;
        case 'file':
            insertHtml('<div class="ticket-editor-file">Documento: referencia o enlace</div><p><br></p>');
            break;
        case 'link':
            createLink();
            break;
        case 'button':
            insertHtml('<a class="ticket-editor-button" href="https://" target="_blank" rel="noopener">Botón</a><p><br></p>');
            break;
        case 'article':
            insertHtml('<div class="ticket-editor-file">Artículo: referencia interna</div><p><br></p>');
            break;
        case 'quote':
            insertHtml('<blockquote>Cita o referencia...</blockquote><p><br></p>');
            break;
        case 'index':
            insertHtml('<div class="ticket-editor-index"><strong>Índice</strong><ol><li>Sección 1</li><li>Sección 2</li></ol></div><p><br></p>');
            break;
        case 'emoji':
            insertHtml('🙂');
            break;
        case 'stars3':
            insertHtml('<span class="ticket-editor-stars">★★★☆☆</span>');
            break;
        case 'stars5':
            insertHtml('<span class="ticket-editor-stars">★★★★★</span>');
            break;
        case 'aiDraft':
            insertHtml('<div class="ticket-editor-banner ticket-editor-banner--info"><strong>Borrador IA</strong><p>Redactar respuesta o resumen del caso.</p></div><p><br></p>');
            break;
    }

    ensureEditableBase(editor);
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

            if (child.tagName === 'DETAILS' && name === 'open') {
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

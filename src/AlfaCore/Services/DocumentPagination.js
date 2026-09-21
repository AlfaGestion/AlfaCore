// Código de impresión propio, integrado como recurso: nunca proviene del JSON editable.
window.alfaDocumentReady = (async () => {
    await document.fonts.ready;
    await Promise.all(Array.from(document.images, img => img.decode().catch(() => {})));
    const root = document.querySelector('.doc-content');
    if (!root) return;
    const closing = root.querySelector(':scope > .document-closing');
    if (!closing) return;
    const blocks = Array.from(root.children).filter(node => node !== closing);
    root.replaceChildren();
    let sheet, body;
    function newPage() {
        sheet = document.createElement('section');
        sheet.className = 'document-sheet';
        body = document.createElement('div');
        body.className = 'document-sheet-body';
        sheet.append(body);
        root.append(sheet);
    }
    function fits() { return body.getBoundingClientRect().height <= sheet.getBoundingClientRect().height - 1; }
    function freshTable(source) {
        const table = source.cloneNode(false);
        for (const child of source.children) if (child.tagName !== 'TBODY') table.append(child.cloneNode(true));
        const tbody = document.createElement('tbody');
        table.append(tbody);
        body.append(table);
        return { table, tbody };
    }
    function addTable(source) {
        let target = freshTable(source);
        for (const row of Array.from(source.tBodies).flatMap(t => Array.from(t.rows))) {
            target.tbody.append(row);
            if (fits()) continue;
            row.remove();
            if (!target.tbody.rows.length) target.table.remove();
            if (body.children.length) newPage();
            target = freshTable(source);
            target.tbody.append(row);
            if (fits()) continue;
            // Descripción más alta que una hoja: continuar el texto sin duplicar importes.
            row.remove();
            let remaining = Array.from(row.cells, cell => cell.textContent);
            while (remaining.some(text => text.length)) {
                const part = row.cloneNode(true);
                Array.from(part.cells).forEach(cell => cell.textContent = '');
                target.tbody.append(part);
                let progress = 0;
                remaining = remaining.map((text, index) => {
                    let lo = 0, hi = text.length;
                    const cell = part.cells[index];
                    while (lo < hi) {
                        const mid = Math.ceil((lo + hi) / 2);
                        cell.textContent = text.slice(0, mid);
                        if (fits()) lo = mid; else hi = mid - 1;
                    }
                    cell.textContent = text.slice(0, lo);
                    progress += lo;
                    return text.slice(lo);
                });
                // Si una celda individual no puede partirse, conservamos la fila
                // completa para no dejar el comprobante en blanco.
                if (!progress) {
                    part.remove();
                    target.tbody.append(row);
                    break;
                }
                if (remaining.some(text => text.length)) {
                    newPage(); target = freshTable(source);
                }
            }
        }
    }
    function addBlock(block) {
        if (block.matches('table.items')) { addTable(block); return; }
        body.append(block);
        if (fits()) return;
        block.remove();
        if (body.children.length) newPage();
        body.append(block);
        if (fits()) return;
        // Propuestas extensas se fragmentan por párrafo conservando el contenedor y estilos.
        if (block.children.length > 1 && !block.querySelector('img,table')) {
            const children = Array.from(block.children);
            block.remove();
            for (const child of children) {
                const wrapper = block.cloneNode(false);
                wrapper.append(child);
                addBlock(wrapper);
            }
            return;
        }
        // Hay bloques del diseñador que no admiten fragmentación (por ejemplo una
        // imagen o una tabla). Se deja el bloque completo en una hoja nueva para
        // que la vista previa y el PDF sigan mostrando el comprobante.
        body.append(block);
    }
    newPage();
    for (const block of blocks) addBlock(block);
    sheet.append(closing);
    const closingHeight = closing.getBoundingClientRect().height;
    const pageHeight = sheet.getBoundingClientRect().height;
    if (closingHeight > pageHeight - 1) {
        closing.style.marginTop = '0';
    }
    if (body.getBoundingClientRect().height + closingHeight > pageHeight - 1) {
        closing.remove(); newPage(); sheet.append(closing);
    }
    document.documentElement.dataset.paginationReady = 'true';
})().catch(error => {
    const notice = document.createElement('p');
    notice.setAttribute('role', 'alert');
    notice.textContent = 'No se pudo acomodar el documento. Revisá el tamaño de los bloques o desactivá Totales al pie de página.';
    document.body.prepend(notice);
    document.documentElement.dataset.paginationError = 'true';
    throw error;
});

export async function printPreview(frame) {
    if (!frame?.contentWindow || !frame.contentDocument) {
        throw new Error("La vista previa del comprobante no está disponible.");
    }
    const doc = frame.contentDocument;
    await doc.fonts.ready;
    await Promise.all(Array.from(doc.images, img => img.decode().catch(() => {})));
    frame.contentWindow.focus();
    frame.contentWindow.print();
}

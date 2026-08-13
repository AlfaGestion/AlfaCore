window.cargaViajes = window.cargaViajes || {};

window.cargaViajes.positionViajeTarifasPanel = function (triggerElement, panelElement) {
    if (!triggerElement || !panelElement) {
        return;
    }

    if (window.innerWidth <= 720) {
        return;
    }

    const body = panelElement.closest('.carga-viajes-modal__body, .cuentas-modal__body');
    if (!body) {
        return;
    }

    const bodyRect = body.getBoundingClientRect();
    const triggerRect = triggerElement.getBoundingClientRect();
    const preferredWidth = 840;
    const margin = 12;
    const maxWidthByBody = Math.max(0, bodyRect.width - (margin * 2));
    const maxWidthByAnchor = Math.max(0, triggerRect.right - bodyRect.left - margin);
    const width = Math.min(preferredWidth, maxWidthByBody, maxWidthByAnchor);

    if (width <= 0) {
        return;
    }

    panelElement.style.position = 'absolute';
    panelElement.style.width = `${width}px`;
    panelElement.style.maxWidth = `${width}px`;
    panelElement.style.right = '0';
    panelElement.style.left = 'auto';
    panelElement.style.top = 'calc(100% + 8px)';
    panelElement.style.zIndex = '40';
};

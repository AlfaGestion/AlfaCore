(function () {
    if (window.__alfaCoreReconnectModalInitialized)
        return;

    window.__alfaCoreReconnectModalInitialized = true;
    window.__alfaCoreReconnectReloadTimer = window.__alfaCoreReconnectReloadTimer || null;

    function getModal() {
        return document.getElementById('components-reconnect-modal');
    }

    function getStatusElement(modal) {
        return modal ? modal.querySelector('#components-reconnect-status') : null;
    }

    function setStatus(modal, message) {
        const status = getStatusElement(modal);
        if (status)
            status.textContent = message;
    }

    function clearReloadTimer() {
        if (window.__alfaCoreReconnectReloadTimer) {
            window.clearTimeout(window.__alfaCoreReconnectReloadTimer);
            window.__alfaCoreReconnectReloadTimer = null;
        }
    }

    function scheduleReload(delayMs) {
        clearReloadTimer();
        window.__alfaCoreReconnectReloadTimer = window.setTimeout(function () {
            window.__alfaCoreReconnectReloadTimer = null;
            window.location.reload();
        }, delayMs);
    }

    function tryReconnect() {
        try {
            if (window.Blazor && typeof window.Blazor.reconnect === 'function') {
                window.Blazor.reconnect();
                return;
            }
        } catch {
        }

        window.location.reload();
    }

    function setVisible(modal, visible) {
        if (!modal)
            return;

        modal.hidden = !visible;
        modal.setAttribute('aria-hidden', visible ? 'false' : 'true');
    }

    function applyState(state) {
        const modal = getModal();
        if (!modal)
            return;

        const normalizedState = (state || '').toLowerCase();
        modal.dataset.state = normalizedState;

        if (normalizedState === 'hide') {
            if (modal.dataset.hadDisconnect === 'true') {
                setStatus(modal, 'La conexión volvió. Vamos a recargar la página para reactivar los botones.');
                scheduleReload(250);
            }

            setVisible(modal, false);
            modal.dataset.hadDisconnect = 'false';
            return;
        }

        modal.dataset.hadDisconnect = 'true';
        setVisible(modal, true);

        if (normalizedState === 'show' || normalizedState === 'paused') {
            setStatus(modal, 'Reconectando con el servidor. Esperá un momento.');
        } else if (normalizedState === 'retrying') {
            setStatus(modal, 'La sesión sigue recuperándose. Reintentamos automáticamente.');
        } else if (normalizedState === 'failed') {
            setStatus(modal, 'No se pudo reconectar todavía. Vamos a reintentar automáticamente.');
        } else if (normalizedState === 'rejected') {
            setStatus(modal, 'La conexión volvió, pero la sesión anterior ya no se puede reutilizar. Hay que recargar.');
        } else {
            setStatus(modal, 'Reconectando con el servidor. Esperá un momento.');
        }

        if (normalizedState === 'failed') {
            window.setTimeout(tryReconnect, 250);
        }

        if (normalizedState === 'rejected') {
            const reloadButton = modal.querySelector('[data-reconnect-action="reload"]');
            if (reloadButton && typeof reloadButton.focus === 'function') {
                window.setTimeout(() => reloadButton.focus({ preventScroll: true }), 0);
            }
        }
    }

    function bindModal() {
        const modal = getModal();
        if (!modal || modal.dataset.reconnectWired === 'true')
            return;

        modal.dataset.reconnectWired = 'true';
        modal.dataset.hadDisconnect = 'false';
        setVisible(modal, false);
        setStatus(modal, 'Preparando la reconexión...');
        clearReloadTimer();

        const retryButton = modal.querySelector('[data-reconnect-action="retry"]');
        const reloadButton = modal.querySelector('[data-reconnect-action="reload"]');

        if (retryButton) {
            retryButton.addEventListener('click', function () {
                setStatus(modal, 'Reintentando la reconexión ahora...');
                tryReconnect();
            });
        }

        if (reloadButton) {
            reloadButton.addEventListener('click', function () {
                clearReloadTimer();
                window.location.reload();
            });
        }

        modal.addEventListener('components-reconnect-state-changed', function (event) {
            applyState(event.detail && event.detail.state);
        });

        window.addEventListener('online', function () {
            if (modal.hidden)
                return;

            setStatus(modal, 'Volvió la conexión. Estamos recuperando la sesión...');
            tryReconnect();
        });

        window.addEventListener('focus', function () {
            if (modal.hidden)
                return;

            setStatus(modal, 'La ventana volvió al frente. Reintentando conexión...');
            tryReconnect();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bindModal, { once: true });
    } else {
        bindModal();
    }
})();

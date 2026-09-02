(function () {
    if (window.__alfaCoreReconnectModalInitialized)
        return;

    window.__alfaCoreReconnectModalInitialized = true;
    window.__alfaCoreReconnectReloadTimer = window.__alfaCoreReconnectReloadTimer || null;

    // Blazor maneja este modal en forma nativa poniendo/sacando clases CSS
    // (components-reconnect-show / -paused / -failed / -rejected) sobre el propio
    // elemento #components-reconnect-modal — no dispara ningún evento personalizado.
    // Por eso todo esto se resuelve observando la clase real del elemento, en vez de
    // esperar un evento que Blazor nunca emite.
    var STATE_CLASSES = ['show', 'paused', 'failed', 'rejected'];

    function getModal() {
        return document.getElementById('components-reconnect-modal');
    }

    function getStatusElement(modal) {
        return modal ? modal.querySelector('#components-reconnect-status') : null;
    }

    function setStatus(modal, message) {
        var status = getStatusElement(modal);
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

    function currentState(modal) {
        for (var i = 0; i < STATE_CLASSES.length; i++) {
            if (modal.classList.contains('components-reconnect-' + STATE_CLASSES[i]))
                return STATE_CLASSES[i];
        }

        return 'hide';
    }

    function applyState(modal, state) {
        modal.setAttribute('aria-hidden', state === 'hide' ? 'true' : 'false');

        if (state === 'hide') {
            if (modal.dataset.hadDisconnect === 'true') {
                setStatus(modal, 'La conexión volvió. Vamos a recargar la página para reactivar los botones.');
                scheduleReload(250);
            }

            modal.dataset.hadDisconnect = 'false';
            return;
        }

        modal.dataset.hadDisconnect = 'true';

        if (state === 'show' || state === 'paused') {
            setStatus(modal, 'Reconectando con el servidor. Esperá un momento.');
        } else if (state === 'failed') {
            setStatus(modal, 'No se pudo reconectar todavía. Vamos a reintentar automáticamente.');
            window.setTimeout(tryReconnect, 250);
        } else if (state === 'rejected') {
            // El servidor descartó el circuito (reinicio, deploy, crash): no hay sesión que
            // reconectar, así que en vez de esperar a que el usuario note el cartel y apriete
            // "Recargar", recargamos solos después de darle un instante para leer el mensaje.
            setStatus(modal, 'La sesión anterior ya no está disponible. Vamos a recargar la página automáticamente...');
            var reloadButton = modal.querySelector('[data-reconnect-action="reload"]');
            if (reloadButton && typeof reloadButton.focus === 'function')
                window.setTimeout(function () { reloadButton.focus({ preventScroll: true }); }, 0);
            scheduleReload(2000);
        }
    }

    function bindModal() {
        var modal = getModal();
        if (!modal || modal.dataset.reconnectWired === 'true')
            return;

        modal.dataset.reconnectWired = 'true';
        modal.dataset.hadDisconnect = 'false';
        setStatus(modal, 'Preparando la reconexión...');
        clearReloadTimer();

        var retryButton = modal.querySelector('[data-reconnect-action="retry"]');
        var reloadButton = modal.querySelector('[data-reconnect-action="reload"]');

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

        var observer = new MutationObserver(function () {
            applyState(modal, currentState(modal));
        });
        observer.observe(modal, { attributes: true, attributeFilter: ['class'] });

        // Estado inicial (por si Blazor ya puso una clase antes de que este script corriera).
        applyState(modal, currentState(modal));

        window.addEventListener('online', function () {
            if (currentState(modal) === 'hide')
                return;

            setStatus(modal, 'Volvió la conexión. Estamos recuperando la sesión...');
            tryReconnect();
        });

        window.addEventListener('focus', function () {
            if (currentState(modal) === 'hide')
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

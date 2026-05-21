window.alfaCorePwa = (function () {
    let deferredInstallPrompt = null;
    let dotNetRef = null;

    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        deferredInstallPrompt = event;
        notifyInstallState();
    });

    window.addEventListener('appinstalled', () => {
        deferredInstallPrompt = null;
        notifyInstallState();
    });

    async function registerServiceWorker() {
        if (!('serviceWorker' in navigator))
            return false;

        try {
            await navigator.serviceWorker.register('/service-worker.js');
            return true;
        } catch {
            return false;
        }
    }

    function isStandalone() {
        return window.matchMedia('(display-mode: standalone)').matches
            || window.navigator.standalone === true;
    }

    function isIos() {
        return /iphone|ipad|ipod/i.test(window.navigator.userAgent || '');
    }

    function isPushSupported() {
        return window.isSecureContext === true
            && 'PushManager' in window
            && 'Notification' in window
            && 'serviceWorker' in navigator;
    }

    function getNotificationPermission() {
        return 'Notification' in window ? Notification.permission : 'unsupported';
    }

    function getDeviceId() {
        const key = 'alfacore.push.deviceId';
        let value = localStorage.getItem(key);
        if (!value) {
            value = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
            localStorage.setItem(key, value);
        }
        return value;
    }

    function getToken() {
        return localStorage.getItem('alfacore_user_token') || '';
    }

    function urlBase64ToUint8Array(value) {
        const normalized = (value || '').trim();
        if (!/^[A-Za-z0-9_-]+$/.test(normalized))
            throw new Error('La clave publica VAPID tiene caracteres no validos.');

        const padding = '='.repeat((4 - normalized.length % 4) % 4);
        const base64 = (normalized + padding).replace(/-/g, '+').replace(/_/g, '/');
        const raw = window.atob(base64);
        const output = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; ++i)
            output[i] = raw.charCodeAt(i);

        if (output.length !== 65 || output[0] !== 4)
            throw new Error('La clave publica VAPID no tiene el formato P-256 esperado.');

        return output;
    }

    function sameApplicationServerKey(subscription, keyBytes) {
        const existing = subscription?.options?.applicationServerKey;
        if (!existing)
            return true;

        const current = new Uint8Array(existing);
        if (current.length !== keyBytes.length)
            return false;

        for (let i = 0; i < current.length; i++) {
            if (current[i] !== keyBytes[i])
                return false;
        }

        return true;
    }

    function buildPushSubscribeError(error) {
        const message = error?.message || '';
        if (window.isSecureContext !== true)
            return 'Las notificaciones push requieren abrir AlfaCore por HTTPS.';
        if (/Registration failed - push service error/i.test(message))
            return 'El navegador no pudo registrarse en el servicio push. Verifica HTTPS, permisos del navegador y que el servicio push del navegador este habilitado.';
        if (/string did not match the expected pattern/i.test(message))
            return 'Safari rechazo la suscripcion push. Reinstala AlfaCore desde pantalla de inicio y vuelve a activar las notificaciones.';
        if (/applicationServerKey|VAPID|valid/i.test(message))
            return 'La clave publica VAPID no fue aceptada por el navegador.';

        return message || 'No se pudo activar la suscripcion push en este navegador.';
    }

    function subscriptionToDto(subscription) {
        const json = subscription.toJSON();
        return {
            endpoint: json.endpoint || '',
            p256dh: json.keys?.p256dh || '',
            auth: json.keys?.auth || ''
        };
    }

    async function api(path, options) {
        const headers = Object.assign({
            'Content-Type': 'application/json',
            'X-AlfaCore-User-Token': getToken()
        }, options?.headers || {});

        const response = await fetch(path, Object.assign({}, options, { headers }));
        if (!response.ok) {
            const text = await response.text();
            console.error('AlfaCore PWA API error', { path, status: response.status, body: text });
            throw new Error(text || 'No se pudo completar la operacion.');
        }

        if (response.status === 204)
            return null;

        return response.json();
    }

    async function getRegistration() {
        if (!('serviceWorker' in navigator))
            throw new Error('Este navegador no soporta service workers.');

        const registration = await navigator.serviceWorker.ready;
        if (!registration)
            throw new Error('El service worker no esta disponible.');

        return registration;
    }

    function notifyInstallState() {
        if (!dotNetRef)
            return;

        dotNetRef.invokeMethodAsync('OnPwaInstallStateChanged', {
            canInstall: !!deferredInstallPrompt,
            installed: isStandalone(),
            isIos: isIos(),
            pushSupported: isPushSupported(),
            notificationPermission: getNotificationPermission()
        }).catch(() => {});
    }

    return {
        init: async function (reference) {
            dotNetRef = reference || null;
            await registerServiceWorker();
            notifyInstallState();
            return {
                canInstall: !!deferredInstallPrompt,
                installed: isStandalone(),
                isIos: isIos(),
                pushSupported: isPushSupported(),
                notificationPermission: getNotificationPermission(),
                deviceId: getDeviceId()
            };
        },

        install: async function () {
            if (!deferredInstallPrompt)
                return { accepted: false, reason: 'not-available' };

            deferredInstallPrompt.prompt();
            const choice = await deferredInstallPrompt.userChoice;
            deferredInstallPrompt = null;
            notifyInstallState();
            return { accepted: choice.outcome === 'accepted' };
        },

        getDeviceId: getDeviceId,

        subscribePush: async function (publicKey) {
            if (!publicKey)
                throw new Error('Falta configurar la clave publica VAPID.');
            if (window.isSecureContext !== true)
                throw new Error('Las notificaciones push requieren abrir AlfaCore por HTTPS.');
            if (!isPushSupported())
                throw new Error('Este navegador no soporta notificaciones push web.');
            if (isIos() && !isStandalone())
                throw new Error('En iOS las notificaciones web requieren instalar AlfaCore en pantalla de inicio. Primero usá Compartir > Agregar a pantalla de inicio.');

            const permission = await Notification.requestPermission();
            if (permission !== 'granted')
                throw new Error('El navegador no otorgo permiso para notificaciones.');

            const registration = await getRegistration();
            const applicationServerKey = urlBase64ToUint8Array(publicKey);
            let subscription = await registration.pushManager.getSubscription();
            if (subscription && !sameApplicationServerKey(subscription, applicationServerKey)) {
                await subscription.unsubscribe();
                subscription = null;
            }

            if (!subscription) {
                try {
                    subscription = await registration.pushManager.subscribe({
                        userVisibleOnly: true,
                        applicationServerKey: applicationServerKey.buffer
                    });
                } catch (error) {
                    console.error('AlfaCore PWA push subscription error', error);
                    throw new Error(buildPushSubscribeError(error));
                }
            }

            return subscriptionToDto(subscription);
        },

        getPushStatus: async function () {
            if (!isPushSupported()) {
                return {
                    supported: false,
                    permission: getNotificationPermission(),
                    subscribed: false,
                    installed: isStandalone(),
                    isIos: isIos()
                };
            }

            const registration = await getRegistration();
            const subscription = await registration.pushManager.getSubscription();
            return {
                supported: true,
                permission: getNotificationPermission(),
                subscribed: !!subscription,
                installed: isStandalone(),
                isIos: isIos()
            };
        },

        unsubscribePush: async function () {
            const registration = await getRegistration();
            const subscription = await registration.pushManager.getSubscription();
            if (subscription)
                await subscription.unsubscribe();
            return true;
        },

        fetchPushSettings: async function () {
            return api(`/api/notificaciones-push/settings?deviceId=${encodeURIComponent(getDeviceId())}`, { method: 'GET' });
        },

        savePushSubscription: async function (subscription) {
            return api('/api/notificaciones-push/subscription', {
                method: 'POST',
                body: JSON.stringify({ deviceId: getDeviceId(), subscription })
            });
        },

        deletePushSubscription: async function () {
            return api(`/api/notificaciones-push/subscription?deviceId=${encodeURIComponent(getDeviceId())}`, { method: 'DELETE' });
        },

        savePushPreferences: async function (preferences) {
            return api('/api/notificaciones-push/preferences', {
                method: 'POST',
                body: JSON.stringify({ deviceId: getDeviceId(), preferences })
            });
        },

        sendTestPush: async function () {
            const status = await this.getPushStatus();
            if (!status.supported)
                throw new Error('Este navegador no soporta notificaciones push web.');
            if (status.isIos && !status.installed)
                throw new Error('En iOS las notificaciones web requieren instalar AlfaCore en pantalla de inicio.');
            if (status.permission !== 'granted')
                throw new Error('Falta otorgar permiso de notificaciones en este navegador.');
            if (!status.subscribed)
                throw new Error('No hay una suscripcion push activa para este dispositivo.');

            return api('/api/notificaciones-push/test', {
                method: 'POST',
                body: JSON.stringify({ deviceId: getDeviceId() })
            });
        }
    };
})();

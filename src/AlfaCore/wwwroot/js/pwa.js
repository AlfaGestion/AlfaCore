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
        const padding = '='.repeat((4 - value.length % 4) % 4);
        const base64 = (value + padding).replace(/-/g, '+').replace(/_/g, '/');
        const raw = window.atob(base64);
        const output = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; ++i)
            output[i] = raw.charCodeAt(i);
        return output;
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
            isIos: isIos()
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
                pushSupported: 'PushManager' in window && 'Notification' in window && 'serviceWorker' in navigator,
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

            const permission = await Notification.requestPermission();
            if (permission !== 'granted')
                throw new Error('El navegador no otorgo permiso para notificaciones.');

            const registration = await getRegistration();
            let subscription = await registration.pushManager.getSubscription();
            if (!subscription) {
                subscription = await registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: urlBase64ToUint8Array(publicKey)
                });
            }

            return subscriptionToDto(subscription);
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
            return api('/api/notificaciones-push/test', {
                method: 'POST',
                body: JSON.stringify({ deviceId: getDeviceId() })
            });
        }
    };
})();

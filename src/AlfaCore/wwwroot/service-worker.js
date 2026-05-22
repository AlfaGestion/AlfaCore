const CACHE_NAME = 'alfacore-static-v3';
const STATIC_ASSETS = [
    '/app.css',
    '/bootstrap/bootstrap.min.css',
    '/favicon.png',
    '/logo.png',
    '/icons/icon-192.png',
    '/icons/icon-512.png',
    '/icons/badge-96.png',
    '/audio/conversaciones/mixkit-alert-quick-chime-766.mp3',
    '/js/conversaciones.js',
    '/js/pwa.js'
];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(STATIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET')
        return;

    const url = new URL(request.url);
    if (url.origin !== self.location.origin)
        return;

    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/_blazor') || url.pathname.startsWith('/_framework/'))
        return;

    event.respondWith(
        fetch(request)
            .then(response => {
                if (response && response.ok && IsSafeStaticAsset(url.pathname)) {
                    const copy = response.clone();
                    caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
                }
                return response;
            })
            .catch(() => caches.match(request))
    );
});

self.addEventListener('push', event => {
    let payload = {};
    try {
        payload = event.data ? event.data.json() : {};
    } catch {
        payload = {};
    }

    console.log('[AlfaCore SW] Push recibido', payload);

    const title = payload.title || 'Nuevo mensaje';
    const options = {
        body: payload.body || 'Tenes un mensaje nuevo.',
        icon: payload.icon || '/icons/icon-192.png',
        badge: payload.badge || '/icons/badge-96.png',
        data: {
            url: payload.url || '/conversaciones',
            idConversacion: payload.idConversacion || null,
            idMensaje: payload.idMensaje || null,
            canal: payload.canal || ''
        },
        tag: payload.idConversacion ? `conversacion-${payload.idConversacion}` : 'alfacore-mensaje',
        renotify: true
    };

    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const targetUrl = new URL(event.notification.data?.url || '/conversaciones', self.location.origin).href;

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clientList => {
            for (const client of clientList) {
                if (client.url.startsWith(self.location.origin) && 'focus' in client) {
                    client.navigate(targetUrl);
                    return client.focus();
                }
            }

            if (clients.openWindow)
                return clients.openWindow(targetUrl);
        })
    );
});

function IsSafeStaticAsset(pathname) {
    return pathname.endsWith('.css')
        || pathname.endsWith('.js')
        || pathname.endsWith('.png')
        || pathname.endsWith('.ico')
        || pathname.endsWith('.webmanifest')
        || pathname.endsWith('.mp3');
}

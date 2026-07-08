const CACHE_NAME = 'alfacore-static-v15';
const STATIC_ASSETS = [
    '/app.css',
    '/bootstrap/bootstrap.min.css',
    '/favicon.png',
    '/logo.png',
    '/icons/icon-192.png',
    '/icons/icon-512.png',
    '/icons/badge-96.png',
    '/audio/conversaciones/mixkit-alert-quick-chime-766.mp3',
    '/js/conversaciones.js'
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
                if (response && response.ok && response.status === 200 && response.type === 'basic' && IsSafeStaticAsset(url.pathname) && !request.headers.has('range')) {
                    const copy = response.clone();
                    caches.open(CACHE_NAME)
                        .then(cache => cache.put(request, copy))
                        .catch(() => { });
                }
                return response;
            })
            .catch(() => caches.match(request))
    );
});

self.addEventListener('push', event => {
    console.log('[AlfaCore SW] push recibido', event);
    let payload = {};
    try {
        payload = event.data ? event.data.json() : {};
    } catch {
        const raw = event.data ? event.data.text() : '';
        payload = raw ? { body: raw } : {};
    }

    console.log('[AlfaCore SW] payload', payload);

    const idConversacion = payload.idConversacion || null;
    const pushUrl = payload.url
        || (idConversacion ? `/conversaciones?id=${encodeURIComponent(idConversacion)}` : '/conversaciones');
    const title = payload.contactName || payload.title || 'AlfaCore';
    const preview = payload.preview || payload.body || 'Tenés un nuevo mensaje';
    const actions = [];
    if (idConversacion) {
        actions.push({ action: 'open-conversation', title: 'Abrir conversación' });
    }
    actions.push({ action: 'view-later', title: 'Ver después' });

    const options = {
        body: preview,
        icon: payload.icon || '/icons/icon-192.png',
        badge: payload.badge || '/icons/badge-96.png',
        timestamp: typeof payload.timestampUnixMs === 'number' ? payload.timestampUnixMs : Date.now(),
        data: {
            url: pushUrl,
            idConversacion: idConversacion,
            idMensaje: payload.idMensaje || null,
            canal: payload.canal || ''
        },
        tag: idConversacion ? `conversacion-${idConversacion}` : 'alfacore-mensaje',
        renotify: !!payload.renotify,
        actions: actions
    };

    console.log('[AlfaCore SW] showNotification', title, options);
    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    if (event.action === 'view-later')
        return;

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
    if (pathname === '/js/pwa.js')
        return false;

    return pathname.endsWith('.css')
        || pathname.endsWith('.js')
        || pathname.endsWith('.png')
        || pathname.endsWith('.ico')
        || pathname.endsWith('.webmanifest')
        || pathname.endsWith('.mp3');
}

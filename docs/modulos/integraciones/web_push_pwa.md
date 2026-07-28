# Integración: Web Push y PWA

## Alcance en AlfaCore

AlfaCore usa Web Push para notificaciones de conversaciones en dispositivos registrados por usuario. El navegador crea una suscripción Push usando service worker y clave pública VAPID; el servidor guarda la suscripción en `TA_CONFIGURACION` y envía notificaciones firmadas con VAPID.

Código relacionado:

- `src/AlfaCore/Services/NotificacionesPushService.cs`
- `src/AlfaCore/Configuration/PushNotificationsOptions.cs`
- `src/AlfaCore/Models/NotificacionesPushModels.cs`
- `src/AlfaCore/wwwroot/js/pwa.js`
- `src/AlfaCore/wwwroot/service-worker.js`
- `tools/push/generate-vapid-keys.ps1`
- `tools/push/generate-vapid-keys.sh`

Documento relacionado: `docs/arquitectura/PWA_PUSH_NOTIFICACIONES.md`.

## Configuración

La configuración vive en `appsettings`, variables de entorno o `.env`, sección `PushNotifications`.

| Clave | Uso |
|---|---|
| `PushNotifications:Subject` | Identidad VAPID, por ejemplo `mailto:admin@alfagestion.com` |
| `PushNotifications:PublicKey` | Clave pública entregada al navegador |
| `PushNotifications:PrivateKey` | Clave privada usada solo por backend |

Las suscripciones se guardan como JSON en `TA_CONFIGURACION` con claves prefijadas por `PUSH-SUB-`.

## Flujo técnico

1. El navegador registra `service-worker.js`.
2. El usuario concede permiso de notificaciones.
3. `pwa.js` solicita suscripción con `PushManager.subscribe`.
4. AlfaCore guarda endpoint, `p256dh`, `auth`, usuario, dispositivo y preferencias.
5. Ante un evento notificable, `NotificacionesPushService` evalúa preferencias.
6. El servidor envía el payload con `WebPushClient` y VAPID.
7. El service worker muestra la notificación.

## Problemas frecuentes

- Web Push requiere HTTPS en producción.
- En iOS, las notificaciones web requieren PWA instalada en pantalla de inicio y abierta desde el icono.
- Si cambia el par VAPID, los dispositivos deben volver a suscribirse.
- Si `PublicKey` y `PrivateKey` no pertenecen al mismo par, el proveedor rechaza autenticación con 401/403.
- Algunos navegadores bloquean permisos si el usuario los denegó previamente.
- Endpoints vencidos o rechazados deben desactivarse para evitar reintentos inútiles.

## Lecciones aplicadas en AlfaCore

- Validar el formato P-256 de claves VAPID evita fallas opacas del navegador.
- Guardar preferencias por dispositivo permite que un usuario tenga reglas distintas en PC y celular.
- El diagnóstico debe mostrar soporte de navegador, permiso, service worker y estado del proveedor.
- Mantener un único par VAPID estable reduce reinscripciones.

## Fuentes oficiales

- [MDN: Push API](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [MDN: Service Worker API](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)
- [MDN: Notifications API](https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API)
- [W3C: Push API](https://www.w3.org/TR/push-api/)

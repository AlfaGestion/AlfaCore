# Integración: Facebook Messenger Platform

## Alcance en AlfaCore

Facebook Messenger está preparado como canal social separado dentro de Conversaciones. Usa webhooks de Meta para recibir mensajes de una página, registra payloads, crea conversaciones por remitente, consulta perfil básico cuando está disponible y permite responder por API.

Código relacionado:

- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`
- `src/AlfaCore/App_Data/updates/2026-07-21-001__crm_conversaciones_canales_sociales.sql`

## Configuración

La configuración vive en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_FACEBOOK_APP_ID` | App ID de Meta |
| `CONV_FACEBOOK_APP_SECRET` | Secreto para validaciones de seguridad |
| `CONV_FACEBOOK_VERIFY_TOKEN` | Token de verificación del webhook |
| `CONV_FACEBOOK_ACCESS_TOKEN` | Page Access Token para llamadas salientes |
| `CONV_FACEBOOK_PAGE_ID` | ID de la página |
| `CONV_FACEBOOK_PAGE_USERNAME` | Nombre público de la página, informativo |
| `CONV_FACEBOOK_API_VERSION` | Versión de Graph API |
| `CONV_FACEBOOK_PUBLIC_BASE_URL` | Base pública HTTPS de AlfaCore |
| `CONV_FACEBOOK_WEBHOOK_PATH` | `/api/conversaciones/facebook/webhook` |

## Flujo técnico

1. Meta verifica el endpoint con `GET /api/conversaciones/facebook/webhook`.
2. Los eventos llegan por `POST /api/conversaciones/facebook/webhook`.
3. AlfaCore registra payloads de webhook.
4. El parser separa mensajes, postbacks y adjuntos.
5. La conversación se identifica por el `sender.id`.
6. Las respuestas salen con el token de página mediante la API de Messenger.

## Requisitos del proveedor

- Página de Facebook administrada por el negocio.
- App de Meta for Developers.
- Page Access Token válido.
- Webhook configurado para el objeto Page.
- Permisos requeridos por Meta para mensajería y administración de webhook.

## Problemas frecuentes

- Usar un token de usuario cuando se necesita un Page Access Token provoca errores de permisos.
- Si el webhook no responde `200 OK`, Meta reintenta y puede generar duplicados.
- En modo desarrollo, los eventos suelen limitarse a administradores, desarrolladores o testers.
- La configuración de suscripción se hace sobre la página; tener la app creada no alcanza.
- Adjuntos pueden llegar con URLs temporales o con permisos; no conviene depender de ellas como almacenamiento permanente.

## Lecciones aplicadas en AlfaCore

- Aunque el payload se parezca al de Instagram, Facebook tiene identidad y permisos propios.
- Conviene mantener canal, color, claves y proveedor separados para soporte.
- La deduplicación por ID externo es clave para tolerar reintentos de Meta.
- Los mensajes no textuales deben representarse con un resumen claro si todavía no se descargan como adjuntos locales.

## Fuentes oficiales

- [Messenger Platform: webhooks](https://developers.facebook.com/documentation/business-messaging/messenger-platform/webhooks)
- [Messenger Platform: envío de mensajes](https://developers.facebook.com/documentation/business-messaging/messenger-platform/send-messages)
- [Permisos de Meta](https://developers.facebook.com/docs/permissions/)

# Integración: Instagram Messaging API

## Alcance en AlfaCore

Instagram está integrado como canal separado dentro de Conversaciones. AlfaCore recibe webhooks de mensajes y postbacks, valida firma HMAC, registra payloads, crea conversaciones por remitente, guarda textos y referencias a adjuntos, consulta perfil disponible del remitente y permite enviar respuestas de texto.

Código relacionado:

- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`
- `src/AlfaCore/App_Data/updates/2026-07-21-001__crm_conversaciones_canales_sociales.sql`

Documento histórico relacionado: `docs/modulos/conversaciones_instagram_meta.md`.

## Configuración

La configuración vive en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_INSTAGRAM_APP_ID` | App ID de Meta |
| `CONV_INSTAGRAM_APP_SECRET` | Secreto usado para validar `X-Hub-Signature-256` |
| `CONV_INSTAGRAM_VERIFY_TOKEN` | Token de verificación del webhook |
| `CONV_INSTAGRAM_ACCESS_TOKEN` | Token de acceso para llamadas salientes |
| `CONV_INSTAGRAM_ACCOUNT_ID` | ID de la cuenta profesional de Instagram |
| `CONV_INSTAGRAM_FACEBOOK_PAGE_ID` | Page ID cuando aplica por vinculación con Facebook |
| `CONV_INSTAGRAM_API_VERSION` | Versión de API, por ejemplo `v22.0` |
| `CONV_INSTAGRAM_PUBLIC_BASE_URL` | Base pública HTTPS de AlfaCore |
| `CONV_INSTAGRAM_WEBHOOK_PATH` | `/api/conversaciones/instagram/webhook` |

## Flujo técnico

1. Meta verifica el webhook con `GET /api/conversaciones/instagram/webhook`.
2. Los eventos llegan por `POST /api/conversaciones/instagram/webhook`.
3. AlfaCore valida `X-Hub-Signature-256` usando `CONV_INSTAGRAM_APP_SECRET`.
4. Se guarda el payload en `CONV_WEBHOOK_LOG` como `META_INSTAGRAM`.
5. Se parsean eventos `messages` y `messaging_postbacks`.
6. La conversación se identifica por el `sender.id` de Instagram.
7. La respuesta sale por `POST https://graph.instagram.com/{version}/{instagram-account-id}/messages`.

## Requisitos del proveedor

- Cuenta de Instagram profesional, Business o Creator.
- App de Meta for Developers compatible con mensajería.
- Permisos de mensajería aprobados para producción.
- URL pública HTTPS.
- Suscripción del webhook a eventos de Instagram Messaging.

## Problemas frecuentes

- Una cuenta personal de Instagram no alcanza; debe ser profesional.
- En modo desarrollo, Meta suele entregar eventos solo para usuarios, cuentas o activos de prueba autorizados.
- La App Review y los permisos determinan si llegan mensajes reales de clientes fuera del entorno de prueba.
- Las URLs de perfil/foto pueden ser temporales; AlfaCore las refresca al recibir mensajes.
- La API no entrega toda la biografía o descripción del perfil del remitente; no debe asumirse como dato disponible.
- Los `mid` reales pueden ser largos; AlfaCore amplió identificadores externos para evitar truncamientos.
- Para pruebas locales se necesita túnel HTTPS. En AlfaCore se usó `cloudflared` y se restringió el host temporal al webhook.

## Lecciones aplicadas en AlfaCore

- Instagram no debe reutilizar claves ni endpoint de WhatsApp aunque ambos sean de Meta.
- Conviene registrar el proveedor como `META_INSTAGRAM` para separar diagnósticos.
- Validar firma HMAC desde el inicio evita aceptar payloads falsos.
- La conversación se debe basar en `sender.id`, no en nombre visible o username.
- Adjuntos y media requieren tratamiento posterior; inicialmente es más seguro guardar referencia textual que simular descarga completa.

## Fuentes oficiales

- [Instagram Platform](https://developers.facebook.com/documentation/instagram-platform)
- [Instagram Messaging](https://developers.facebook.com/documentation/business-messaging/instagram-messaging)
- [Webhooks de Instagram Messaging](https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks)
- [Permisos de Meta](https://developers.facebook.com/docs/permissions/)

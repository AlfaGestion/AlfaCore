# Integración: WhatsApp Cloud API

Manual operativo para configuración, migración y pruebas:
`src/AlfaCore/Docs/manual_conversaciones_whatsapp_saas.md`.

## Alcance en AlfaCore

WhatsApp es el canal principal externo del módulo Conversaciones. AlfaCore recibe mensajes por webhook, registra payloads en `CONV_WEBHOOK_LOG`, crea o actualiza conversaciones, permite responder mensajes de texto, enviar plantillas, enviar adjuntos y enviar reacciones cuando la configuración está completa.

Código relacionado:

- `src/AlfaCore/Services/ConversacionesService.cs`
- `src/AlfaCore/Services/ConversacionesConfigService.cs`
- `src/AlfaCore/Models/ConversacionesConfiguracionModels.cs`
- `src/AlfaCore/Configuration/WhatsAppOptions.cs`
- `src/AlfaCore/App_Data/updates/2026-05-17-999__conversaciones_modelo_base.sql`

Documento histórico relacionado: `docs/modulos/conversaciones_whatsapp_conexion.md`.

## Configuración

La configuración principal vive en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_WHATSAPP_VERIFY_TOKEN` | Token que AlfaCore compara durante la verificación del webhook |
| `CONV_WHATSAPP_ACCESS_TOKEN` | Token Bearer para llamadas salientes a Meta |
| `CONV_WHATSAPP_PHONE_NUMBER_ID` | ID técnico del número de WhatsApp |
| `CONV_WHATSAPP_BUSINESS_ACCOUNT_ID` | ID de la cuenta WhatsApp Business |
| `CONV_WHATSAPP_APP_SECRET` | Se guarda para validación de firma o endurecimiento futuro |
| `CONV_WHATSAPP_API_VERSION` | Versión de Graph API, por ejemplo `v22.0` |
| `CONV_WHATSAPP_PUBLIC_BASE_URL` | Base pública HTTPS de AlfaCore |
| `CONV_WHATSAPP_WEBHOOK_PATH` | Ruta del webhook, hoy `/api/conversaciones/whatsapp/webhook` |

`appsettings` puede operar como fallback inicial mediante la sección `WhatsApp`, pero la operación normal debe quedar parametrizada en `TA_CONFIGURACION`.

## Flujo técnico

1. En SaaS, Meta verifica el webhook con `GET /api/conversaciones/whatsapp/webhook/{token}`. En instalaciones monobase o legacy puede usarse la ruta sin token.
2. Si llega `{token}`, AlfaCore resuelve primero la base en `ALFA_CENTRAL.dbo.bases.WebhookToken` y fuerza esa conexión para el request.
3. AlfaCore compara el `hub.verify_token` recibido con `CONV_WHATSAPP_VERIFY_TOKEN` de la base resuelta.
4. Meta envía eventos por `POST /api/conversaciones/whatsapp/webhook/{token}`.
5. AlfaCore guarda el payload en `CONV_WEBHOOK_LOG`.
6. El servicio deduplica por `WhatsAppMessageId` cuando corresponde.
7. El mensaje se transforma en una conversación y un registro en `CONV_MENSAJES`.
8. Las respuestas salen por `POST https://graph.facebook.com/{version}/{phone-number-id}/messages`.

## Requisitos del proveedor

- App de Meta for Developers con producto WhatsApp.
- WhatsApp Business Account.
- Número de teléfono conectado.
- Token de acceso válido para producción.
- URL pública HTTPS accesible desde Meta.
- En SaaS, URL con token propio de la base: `/api/conversaciones/whatsapp/webhook/{WebhookTokenDeLaBase}`.
- Suscripción del webhook a eventos de mensajes y estados.

## Problemas frecuentes

- `localhost` o IP privada no sirven para webhooks de Meta; se necesita HTTPS público.
- En SaaS, la ruta sin token no identifica el cliente/base; si se configura en Meta, los mensajes pueden entrar a la base por defecto del proceso.
- El `Verify Token` no lo entrega Meta: lo define AlfaCore/equipo y debe coincidir exactamente en ambos lados.
- Los tokens temporales de prueba vencen; para producción hay que usar un token estable y administrado.
- Si cambia el `Phone Number ID`, el envío falla aunque el token siga siendo válido.
- Las plantillas requieren aprobación y tienen reglas distintas de los mensajes libres de la ventana de atención.
- Cuando Meta reintenta webhooks, la deduplicación por ID externo evita mensajes duplicados en la bandeja.
- Los errores técnicos deben revisarse en `AUX_ERR` y en el log de webhooks, no solo en la UI.

## Lecciones aplicadas en AlfaCore

- Separar `VerifyToken`, `AccessToken`, `PhoneNumberId` y URL pública evita mezclar validación entrante con envío saliente.
- Guardar payloads crudos de webhook ayuda mucho para diagnosticar cambios de estructura de Meta.
- La configuración en `TA_CONFIGURACION` permite operar por cliente/base sin redeploy.
- Los IDs externos pueden ser largos; conviene evitar límites chicos en columnas de mensajes y conversaciones.
- El webhook debe responder rápido; el procesamiento debe ser tolerante a payloads incompletos o eventos no soportados.

## Fuentes oficiales

- [WhatsApp Business Platform: información general](https://developers.facebook.com/documentation/business-messaging/whatsapp/about-the-platform)
- [WhatsApp Cloud API: primeros pasos](https://developers.facebook.com/documentation/business-messaging/whatsapp/get-started)
- [WhatsApp Business Platform: webhooks](https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/overview)
- [WhatsApp Cloud API: envío de mensajes](https://developers.facebook.com/documentation/business-messaging/whatsapp/messages/send-messages)
- [Colección oficial de Meta en Postman](https://www.postman.com/meta/whatsapp-business-platform/collection/wlk6lh4/whatsapp-cloud-api)

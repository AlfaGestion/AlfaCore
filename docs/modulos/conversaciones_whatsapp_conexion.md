# Conversaciones + WhatsApp Cloud API

## Objetivo

Este documento explica:

- qué parámetros necesita AlfaCore para conectarse a WhatsApp Cloud API
- dónde cargarlos dentro del sistema
- cómo mapear cada dato que entrega Meta
- qué configurar del lado de Meta para webhook y envío

## Dónde se configura en AlfaCore

Pantalla interna:

- `/conversaciones/configuracion`

Persistencia principal:

- `dbo.TA_CONFIGURACION`

`appsettings` queda solo como respaldo mínimo para escenarios iniciales o de contingencia.

## Claves usadas en TA_CONFIGURACION

| CLAVE | Uso |
|---|---|
| `CONV_WHATSAPP_VERIFY_TOKEN` | Token que AlfaCore compara cuando Meta verifica el webhook |
| `CONV_WHATSAPP_ACCESS_TOKEN` | Token usado para enviar mensajes por Cloud API |
| `CONV_WHATSAPP_PHONE_NUMBER_ID` | Identificador técnico del número conectado |
| `CONV_WHATSAPP_BUSINESS_ACCOUNT_ID` | Identificador de la cuenta de negocio de WhatsApp |
| `CONV_WHATSAPP_APP_SECRET` | Reservado para validación futura de firma del webhook |
| `CONV_WHATSAPP_API_VERSION` | Versión del Graph API, por ejemplo `v22.0` |
| `CONV_WHATSAPP_PUBLIC_BASE_URL` | URL pública HTTPS donde Meta puede llegar al sistema |
| `CONV_WHATSAPP_WEBHOOK_PATH` | Ruta interna del webhook. Hoy el módulo usa `/api/conversaciones/whatsapp/webhook` |

## Qué necesitás obtener en Meta

### 1. App de Meta

Necesitás una app de Meta for Developers con el producto:

- `WhatsApp`

### 2. Cuenta de negocio y número

Necesitás:

- una `WhatsApp Business Account`
- un número vinculado al canal

### 3. Datos que vas a copiar a AlfaCore

Desde Meta tenés que identificar y copiar:

- `Phone Number ID`
- `WhatsApp Business Account ID`
- `Access Token`
- `Verify Token`
- `App Secret` de la app, si querés dejar listo el endurecimiento futuro de seguridad

### 4. URL pública del webhook

Meta no puede llamar a `localhost` ni a una IP privada inaccesible desde Internet.

Por eso necesitás:

- una URL pública HTTPS
- que esa URL llegue a AlfaCore
- que la ruta final responda en:
  `/api/conversaciones/whatsapp/webhook`

Ejemplo:

```text
https://midominio.com/api/conversaciones/whatsapp/webhook
```

## Mapa Meta → AlfaCore

| Meta | AlfaCore |
|---|---|
| Phone Number ID | `CONV_WHATSAPP_PHONE_NUMBER_ID` |
| WhatsApp Business Account ID | `CONV_WHATSAPP_BUSINESS_ACCOUNT_ID` |
| Access Token | `CONV_WHATSAPP_ACCESS_TOKEN` |
| Verify Token | `CONV_WHATSAPP_VERIFY_TOKEN` |
| App Secret | `CONV_WHATSAPP_APP_SECRET` |
| Callback URL | `CONV_WHATSAPP_PUBLIC_BASE_URL` + `CONV_WHATSAPP_WEBHOOK_PATH` |
| API version | `CONV_WHATSAPP_API_VERSION` |

## Flujo recomendado de configuración

1. Entrar a `/conversaciones/configuracion`.
2. Cargar la `Base pública HTTPS`.
3. Cargar `Phone Number ID`.
4. Cargar `WhatsApp Business Account ID`.
5. Cargar `Access Token`.
6. Definir un `Verify Token` propio.
7. Revisar la `URL de callback` que arma AlfaCore.
8. Guardar.
9. Ir a Meta y usar esa misma URL + ese mismo `Verify Token`.
10. Suscribir el webhook a eventos de mensajes y estados.
11. Probar verificación del webhook.
12. Probar mensaje entrante.
13. Probar mensaje saliente.

## Qué hace AlfaCore con cada dato

### Verify Token

Se usa en:

- `GET /api/conversaciones/whatsapp/webhook`

Cuando Meta intenta validar el webhook, AlfaCore compara el token recibido con `CONV_WHATSAPP_VERIFY_TOKEN`.

### Access Token

Se usa para:

- `POST https://graph.facebook.com/{version}/{phone_number_id}/messages`

Ese es el token que habilita el envío saliente.

### Phone Number ID

Se usa para construir la URL del endpoint de envío.

### Public Base URL

Se usa para mostrar la URL exacta que tenés que registrar en Meta.

## Recomendaciones operativas

- Usar un token estable y no uno temporal de pruebas para producción.
- Mantener el `Verify Token` como secreto interno del equipo.
- Registrar primero una URL pública real antes de probar con Meta.
- Verificar que firewall, DNS y certificado HTTPS estén resueltos antes de activar el webhook.
- Si algo falla, revisar `AUX_ERR` y el diagnóstico JSON en `App_Data/diagnostics`.

## Limitaciones actuales del módulo

- La ruta del webhook está implementada en la app y hoy se considera fija:
  `/api/conversaciones/whatsapp/webhook`
- El módulo ya guarda `App Secret`, pero todavía no valida firma HMAC del webhook.
- La pantalla administra parámetros y deja la integración lista, pero no reemplaza la publicación de una URL pública real.

## Qué configurar en Meta

En términos prácticos, del lado de Meta necesitás completar:

- la app con producto `WhatsApp`
- el número conectado
- el webhook callback URL
- el verify token
- la suscripción a eventos

Para envío productivo también necesitás:

- token válido
- permisos del negocio y del número
- pruebas de mensaje con el número habilitado

## Fuentes oficiales recomendadas

- Meta overview: https://developers.facebook.com/docs/whatsapp/cloud-api/overview
- Meta get started: https://developers.facebook.com/docs/whatsapp/cloud-api/get-started
- Meta webhooks: https://developers.facebook.com/docs/graph-api/webhooks/getting-started/webhooks-for-whatsapp
- Colección oficial de Meta en Postman: https://www.postman.com/meta/whatsapp-business-platform/documentation/wlk6lh4/whatsapp-cloud-api

Nota:

La interfaz de Meta cambia con bastante frecuencia. Si al configurar encontrás diferencias visuales en el panel, tomá como referencia los conceptos y IDs de este documento y validá el paso puntual contra la documentación oficial actual.

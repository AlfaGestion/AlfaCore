# Conversaciones: integración Instagram / Meta

## Objetivo

Preparar AlfaCore para recibir y enviar mensajes de Instagram desde el módulo CRM / Conversaciones, compartiendo la lógica operativa de chat, tickets, leads, asignaciones y trazabilidad.

## Conexión guiada con la app de ALFA NET (desde 2026-11-05)

Igual que WhatsApp: el cliente **no crea ninguna app de Meta ni copia claves**. En
`Conversaciones > Configuración > Canales > Instagram` (o `Facebook`) toca **Conectar**, entra con su
cuenta en Meta, acepta y vuelve a AlfaCore a confirmar. **Instagram y Facebook se conectan por
separado**: un cliente puede tener uno solo.

- Instagram usa el *inicio de sesión de Instagram*: no hace falta página de Facebook, sí cuenta profesional.
- Facebook usa el *inicio de sesión de Facebook*; si la persona administra varias páginas, elige una.
- Los mensajes de todos los clientes llegan a un único webhook por canal; AlfaCore busca en
  `ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES` a qué base pertenece la cuenta (`entry.id`) y lo procesa ahí.
- El token queda en `TA_CONFIGURACION` de la base (mismas claves de siempre), así que el envío, la
  bandeja y el bot no cambian. El de Instagram dura 60 días y se renueva solo (`MetaCanalesSocialesHostedService`).
- La configuración manual de abajo sigue funcionando para clientes con app propia (sección plegada "avanzado").

### Qué configurar una sola vez (ALFA NET)

En el appsettings **del servidor** (no en el repo), sección `MetaCanalesSociales`:

| Clave | Valor |
|---|---|
| `Enabled` | `true` para mostrar los botones |
| `FacebookAppId` / `FacebookAppSecret` | vacío = usa los de `WhatsAppEmbeddedSignup` (misma app 1436083307772786) |
| `InstagramAppId` / `InstagramAppSecret` | Meta → app → Instagram → *Configuración de la API con inicio de sesión de Instagram* (no es el App ID de Meta) |
| `WebhookVerifyToken` | texto secreto que se carga igual en Meta |
| `CallbackBaseUrl` | vacío = el host desde el que se abrió la pantalla |

En el panel de la app de Meta:

1. **Instagram → Configuración de la API con inicio de sesión de Instagram**
   - *Configurar webhooks*: URL `https://<host>/api/conversaciones/meta/instagram/webhook`, el Verify Token, y suscribir `messages` y `messaging_postbacks`.
   - *Configurar el inicio de sesión de empresa de Instagram*: URL de redireccionamiento `https://<host>/api/conversaciones/meta/oauth/instagram/callback`.
2. **Messenger → Webhooks** (objeto *Page*): URL `https://<host>/api/conversaciones/meta/facebook/webhook`, el Verify Token, campos `messages` y `messaging_postbacks`.
3. **Inicio de sesión con Facebook → Configuración**: URI de redireccionamiento OAuth válida `https://<host>/api/conversaciones/meta/oauth/facebook/callback`.

`<host>` = cada dirección pública desde la que se usa AlfaCore (`alfacentral.ddns.net` y, para pruebas,
`alfanetweb.ddns.net`). Meta admite una sola URL de webhook por producto: apuntarla a producción.

### Permisos a pedir en la revisión de Meta (acceso avanzado)

Al 05/11/2026 la app ya tiene acceso avanzado a `pages_messaging`. Falta pedir:

- Instagram: `instagram_business_basic`, `instagram_business_manage_messages`.
- Facebook: `pages_manage_metadata`, `pages_show_list`, `pages_read_engagement`, `business_management`.

Mientras no estén aprobados, la conexión guiada solo funciona con cuentas que tengan un rol en la app
(administrador, desarrollador o evaluador). Para la revisión, Meta pide un video de la conexión desde
AlfaCore y del envío y la recepción de un mensaje.

## Conexión manual (app propia del cliente)

## Requisitos en Meta

1. Tener una cuenta de Instagram profesional: Business o Creator.
2. Para el flujo con Facebook Login, vincular la cuenta a una página de Facebook. El flujo actual de AlfaCore usa Instagram Login y no requiere esa página.
3. Tener acceso a Meta for Developers con una app del negocio.
4. Tener una URL pública HTTPS de AlfaCore para webhooks.
5. Solicitar permisos de mensajería para uso en producción mediante App Review.

Documentación oficial:

- Instagram Platform: https://developers.facebook.com/documentation/instagram-platform
- Instagram Messaging: https://developers.facebook.com/documentation/business-messaging/instagram-messaging
- Webhooks de Instagram Messaging: https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks
- Permisos Meta: https://developers.facebook.com/docs/permissions/

## Pasos para conseguir credenciales

### 1. Preparar Instagram

Desde Instagram:

1. Convertir la cuenta a profesional si todavía es personal.
2. Elegir tipo Business o Creator.
3. Vincularla a una página de Facebook administrada por el negocio.

Meta exige cuenta profesional para usar las APIs de Instagram orientadas a mensajería.

### 2. Crear la app en Meta

1. Entrar a https://developers.facebook.com/.
2. Ir a My Apps.
3. Crear una app nueva.
4. Elegir un tipo de app compatible con negocio / mensajería.
5. Guardar:
   - App ID
   - App Secret

Estos valores se cargan en AlfaCore como:

- `CONV_INSTAGRAM_APP_ID`
- `CONV_INSTAGRAM_APP_SECRET`

### 3. Configurar productos y permisos

En la app de Meta, agregar los productos de Instagram/Messaging que correspondan al flujo elegido.

Permisos esperados para mensajería:

- `instagram_manage_messages` o el permiso vigente equivalente mostrado por Meta para Instagram Messaging.
- Permisos de página necesarios para acceder a la página vinculada y sus webhooks.

Meta puede cambiar nombres o requerimientos de permisos. Antes de pasar a producción, revisar App Review y la sección Permissions and Features de la app.

### 4. Configurar webhook

En AlfaCore, entrar a:

```text
Conversaciones > Configuración
```

Completar:

- Base pública HTTPS
- Verify Token

AlfaCore mostrará la URL:

```text
https://TU_DOMINIO/api/conversaciones/instagram/webhook
```

En Meta, cargar:

- Callback URL: la URL pública de AlfaCore
- Verify Token: el mismo valor cargado en AlfaCore
- Campos/eventos: mensajes de Instagram

### 4.1. URL de redireccionamiento OAuth

Para "Inicio de sesión de empresa de Instagram", cargar esta URL:

```text
https://TU_DOMINIO/api/conversaciones/instagram/oauth/callback
```

Debe usar el mismo dominio público HTTPS que AlfaCore. No usar `localhost` para una app publicada en Meta; para pruebas locales usar un túnel HTTPS estable y cargar esa URL exacta.

### 4.2. Pruebas locales sin publicar AlfaCore

Para probar cambios del webhook sin actualizar el servidor utilizado por otros usuarios:

1. Instalar `cloudflared` con `winget install Cloudflare.cloudflared`.
2. Ejecutar `cloudflared tunnel --url http://localhost:5055 --no-autoupdate`.
3. Copiar la URL temporal `https://...trycloudflare.com` informada por la herramienta.
4. En Meta, reemplazar temporalmente la Callback URL por:

```text
https://...trycloudflare.com/api/conversaciones/instagram/webhook
```

5. Usar el mismo Verify Token configurado en AlfaCore.
6. Enviar mensajes desde otra cuenta hacia la cuenta profesional y verificar la bandeja local.

AlfaCore restringe los hosts `*.trycloudflare.com` al endpoint del webhook; el resto de la aplicación responde `404`. La URL de Quick Tunnel cambia cada vez que se reinicia `cloudflared` y no debe utilizarse en producción.

### 5. Obtener IDs y token

Guardar en AlfaCore:

- Facebook Page ID
- Instagram Account ID
- Access Token

Claves usadas:

- `CONV_INSTAGRAM_FACEBOOK_PAGE_ID`
- `CONV_INSTAGRAM_ACCOUNT_ID`
- `CONV_INSTAGRAM_ACCESS_TOKEN`

Para pruebas iniciales, Meta permite trabajar con usuarios/activos de desarrollo. Para uso real con clientes o cuentas fuera del entorno de prueba, hay que completar App Review.

## Estado en AlfaCore

Implementado:

- canal `INSTAGRAM` separado en la UI de Conversaciones
- acento visual propio para Instagram
- configuración `CONV_INSTAGRAM_*` en `TA_CONFIGURACION`
- endpoint de verificación:

```text
GET /api/conversaciones/instagram/webhook
```

- endpoint POST para recibir y procesar webhooks:

```text
POST /api/conversaciones/instagram/webhook
```

- validación HMAC `X-Hub-Signature-256` con `CONV_INSTAGRAM_APP_SECRET`
- registro del payload en `CONV_WEBHOOK_LOG` como proveedor `META_INSTAGRAM`
- parser de eventos `messages` y `messaging_postbacks`
- alta o reutilización de conversaciones por `sender.id`
- deduplicación por `message.mid`
- ingreso de texto y referencias de adjuntos en la bandeja del canal `INSTAGRAM`
- ampliación de los identificadores externos de mensajes a 500 caracteres, porque los `mid` reales de Instagram pueden superar los 150
- consulta y persistencia del perfil público disponible para el remitente: nombre, usuario, foto, seguidores, verificación y relación de seguimiento
- acceso directo desde la cabecera de la conversación al perfil `instagram.com/{username}`
- envío de respuestas de texto por `POST https://graph.instagram.com/{version}/{instagram-account-id}/messages`
- persistencia del `message_id` saliente de Instagram en `CONV_MENSAJES.MessageIdExterno`
- validación independiente de la ventana estándar de 24 horas de Instagram

La API de perfil del remitente no expone la biografía/descripción. La URL de la foto de perfil es temporal y se refresca al recibir nuevos mensajes.

La integración de Instagram utiliza exclusivamente las claves `CONV_INSTAGRAM_*`, el IGSID guardado en `IdentificadorExternoContacto` y el host `graph.instagram.com`. No reutiliza configuración, teléfono, identificadores ni endpoints de WhatsApp.

Pendiente:

- descarga y persistencia local de adjuntos de Instagram
- asociación automática con contactos/leads
- creación de tickets/leads desde mensajes de Instagram

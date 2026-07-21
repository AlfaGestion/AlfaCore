# Conversaciones: integración Instagram / Meta

## Objetivo

Preparar AlfaCore para recibir y enviar mensajes de Instagram desde el módulo CRM / Conversaciones, compartiendo la lógica operativa de chat, tickets, leads, asignaciones y trazabilidad.

## Requisitos en Meta

1. Tener una cuenta de Instagram profesional: Business o Creator.
2. Vincular esa cuenta de Instagram a una página de Facebook.
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

- endpoint POST preparado para recibir webhooks y registrar auditoría inicial:

```text
POST /api/conversaciones/instagram/webhook
```

Pendiente:

- parser real de eventos de Instagram
- alta/búsqueda de conversación por `sender.id`
- envío real por Graph API
- asociación automática con contactos/leads
- creación de tickets/leads desde mensajes de Instagram

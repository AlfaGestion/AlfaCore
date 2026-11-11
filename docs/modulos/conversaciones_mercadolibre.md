# Conversaciones - Mercado Libre

## Conexión guiada con la app de ALFA NET (desde 2026-11-11)

Igual que Instagram y Facebook: el cliente no crea ninguna app ni copia claves. En
`Conversaciones > Configuración > Canales > Mercado Libre` toca **Conectar Mercado Libre**, entra con
**su** cuenta vendedora, acepta y confirma en AlfaCore. La app (App ID `6296866990333297`) es de ALFA NET;
cada cliente solo autoriza su cuenta.

- Tokens en `TA_CONFIGURACION` de la base (`CONV_MELI_ACCESS_TOKEN`, `CONV_MELI_REFRESH_TOKEN`,
  `CONV_MELI_SELLER_ID`, `CONV_MELI_CLIENT_ID` = el de ALFA NET). El **Client Secret no se graba en la base**:
  `ConversacionesConfigService` lo completa en memoria desde la sección `MercadoLibreCentral` del servidor
  cuando el Client ID de la base es el de la app central (`CredencialesCentrales`).
- `ALFA_CENTRAL.dbo.CONV_CANALES_SOCIALES` (canal `MERCADOLIBRE`, cuenta = seller_id) dice a qué base va cada aviso.
- Avisos: `POST /api/conversaciones/mercadolibre/central/webhook`. Se contesta 200 enseguida (Mercado Libre
  reintenta si tarda) y se procesa aparte en la base dueña. Se ignoran avisos de otra app (`application_id`).
- Vuelta de la autorización: `GET /api/conversaciones/mercadolibre/central/oauth/callback`.
- El acceso dura 6 horas: `MercadoLibreCentralHostedService` lo renueva cada 30 minutos cuando faltan menos de
  90, y además cualquier llamada que recibe 401 renueva y reintenta una vez (vale también para bases manuales).
  El refresh token es de un solo uso: se graba el nuevo enseguida.
- **Mensajes de postventa** (tópico `messages`): una venta = una conversación con
  `IdentificadorExternoConversacion = pack-{id}`. Las respuestas van a
  `POST /messages/packs/{pack}/sellers/{seller}?tag=post_sale`, partidas en mensajes de hasta 350 caracteres.
  A diferencia de una pregunta, responder no cierra la conversación.
- La carga manual queda oculta donde la conexión guiada está habilitada.

### Configuración (una sola vez, ALFA NET)

appsettings del **servidor** (no del repo), sección `MercadoLibreCentral`: `Enabled`, `ClientId`,
`ClientSecret`, `SiteId` (MLA), `AuthorizationUrl` (`https://auth.mercadolibre.com.ar/authorization`),
`CallbackBaseUrl` (vacío = el host desde el que se abrió la pantalla).

En la app de developers.mercadolibre.com.ar:

- Redirect URI: `https://<host>/api/conversaciones/mercadolibre/central/oauth/callback` (una por host).
- Flujos: Authorization Code y Refresh Token. PKCE apagado.
- Permisos: Usuarios (lectura), Comunicaciones pre y post venta (lectura y escritura), Publicación y
  sincronización (lectura), Venta y envíos (lectura).
- Tópicos: Questions y Messages.
- Notificaciones: `https://<host>/api/conversaciones/mercadolibre/central/webhook` (una sola por app:
  apuntarla al servidor que corresponda).

## Integración manual (app propia del cliente)

## Alcance

La integración inicial agrega el canal `MERCADOLIBRE` al módulo Conversaciones para atender preguntas de publicaciones.

Incluye:

- configuración en `TA_CONFIGURACION`;
- callback OAuth para vincular una cuenta vendedora;
- webhook de notificaciones;
- procesamiento del topic `questions`;
- consulta del detalle de pregunta en la API de Mercado Libre;
- creación o actualización de conversaciones;
- respuesta a preguntas desde AlfaCore mediante `POST /answers`.

## Claves de configuración

Todas viven en `TA_CONFIGURACION`, grupo `CONVERSACIONES`.

| Clave | Uso |
|---|---|
| `CONV_MELI_CLIENT_ID` | App ID / Client ID de Mercado Libre |
| `CONV_MELI_CLIENT_SECRET` | Client Secret de la app |
| `CONV_MELI_ACCESS_TOKEN` | Token OAuth vigente |
| `CONV_MELI_REFRESH_TOKEN` | Token para renovar el acceso |
| `CONV_MELI_SELLER_ID` | ID de la cuenta vendedora |
| `CONV_MELI_SITE_ID` | Sitio, por defecto `MLA` |
| `CONV_MELI_PUBLIC_BASE_URL` | URL pública HTTPS de AlfaCore |
| `CONV_MELI_WEBHOOK_PATH` | `/api/conversaciones/mercadolibre/webhook` |
| `CONV_MELI_OAUTH_CALLBACK_PATH` | `/api/conversaciones/mercadolibre/oauth/callback` |
| `CONV_MELI_API_BASE_URL` | `https://api.mercadolibre.com` |

## URLs

Webhook:

```text
{CONV_MELI_PUBLIC_BASE_URL}/api/conversaciones/mercadolibre/webhook
```

Redirect URI OAuth:

```text
{CONV_MELI_PUBLIC_BASE_URL}/api/conversaciones/mercadolibre/oauth/callback
```

## Flujo operativo

1. Crear una app en Mercado Libre Developers.
2. Cargar en la app la Redirect URI OAuth que muestra AlfaCore.
3. En AlfaCore, cargar `Client ID`, `Client Secret`, base pública y guardar.
4. Usar el link "Abrir autorización OAuth" desde la configuración.
5. Al volver del login, AlfaCore intenta guardar `access_token`, `refresh_token` y `seller_id`.
6. Configurar notificaciones de Mercado Libre usando el webhook de AlfaCore.
7. Suscribir el topic `questions`.

## Notas técnicas

Mercado Libre envía una notificación con el recurso afectado. AlfaCore registra el webhook y, si tiene token, consulta `/questions/{id}` para obtener el texto real, comprador, publicación y estado.

Las conversaciones se identifican por:

- `Canal = MERCADOLIBRE`;
- `IdentificadorExternoConversacion = id de pregunta`;
- `IdentificadorExternoContacto = id comprador`;
- `UsuarioExterno = item_id`.

El script `src/AlfaCore/App_Data/updates/2026-07-23-003__crm_conversaciones_mercadolibre_canal.sql` agrega el canal al catálogo y actualiza el constraint de `CONV_CONVERSACIONES`.

# Conversaciones - Chat del sitio web (WEBCHAT)

## Alcance

Widget de chat embebible en un sitio externo (WordPress u otro), estilo Livechat de Odoo. Los mensajes
entran a la misma bandeja de Conversaciones que WhatsApp/Instagram/Facebook/Mercado Libre, como canal
`WEBCHAT`.

- El visitante es **anónimo**: no se exige login ni email. Puede dejar su nombre (opcional).
- El **asistente IA responde primero**, con la misma cadena de respuestas automáticas que el resto de
  los canales (`TryAutoReplyReglasAsync` → bienvenida → fuera de horario → `TryAutoReplyBotAsync`).
- Un agente puede responder desde la bandeja; el visitante lo ve en el widget por polling (~3,5 s).

Fuera de alcance del MVP: transporte en tiempo real (SignalR/WebSocket), identificación del visitante
a mitad de charla (habilitaría saldo/pedidos reales vía el asistente), varias claves/dominios por base,
adjuntos que no sean imágenes o audios (ver "Imágenes y notas de voz") y branding avanzado.

## Snippet para el sitio

Se genera en **Configuración de Conversaciones → Canales → Chat del sitio web**:

```html
<script src="https://<alfacore>/webchat/widget.js" data-site="AC-<32 hex>" data-contexto="" defer></script>
```

- `data-site`: clave pública de la base (`WebChatSiteKey`). Es visible en el HTML del sitio: **no** es
  el `WebhookToken` de la base y solo habilita los endpoints del chat web.
- `data-contexto` (opcional, hasta 200 caracteres): distingue desde qué página/entrada escribe el
  visitante (ej. `actualizacion-v3-2`). Se guarda en la conversación junto con la URL de la página.
- La URL base sale de `ServidorWeb:UrlBasePublica` (appsettings) o, si no está, de la URL actual.

## Configuración (`TA_CONFIGURACION`, grupo `CONVERSACIONES`)

| Clave | Uso |
|---|---|
| `CONV_WEBCHAT_ACTIVO` | `1` habilita el canal. Inactivo = los endpoints responden 404 y el widget no se muestra |
| `CONV_WEBCHAT_DOMINIOS_PERMITIDOS` | Hosts separados por coma. Vacío = cualquier origen. Incluye subdominios |
| `CONV_WEBCHAT_TITULO` | Título del widget |
| `CONV_WEBCHAT_COLOR` | Color `#RRGGBB` (cualquier otro valor cae al default: se inyecta en el CSS del widget) |
| `CONV_WEBCHAT_MENSAJE_BIENVENIDA` | Primer mensaje que muestra el widget (local, no se graba) |
| `CONV_WEBCHAT_AUDIOS` | `0` = el visitante no puede mandar notas de voz. Sin valor o `1` = puede (default) |
| `CONV_WEBCHAT_IMAGENES` | `0` = el visitante no puede mandar imágenes. Sin valor o `1` = puede (default) |
| `CONV_WEBCHAT_SITE_KEY` | Solo instalaciones **no SaaS**: clave del sitio. En SaaS vive en la base central |

El widget saluda con su propio mensaje (`CONV_WEBCHAT_MENSAJE_BIENVENIDA`), así que en este canal
**no** se envía la bienvenida de Automatizaciones (`CONV_BIENVENIDA_*`): el visitante recibe un solo
saludo. Si el saludo del widget quedara vacío, se usa el de Automatizaciones.

Cada base genera su propia clave y su propio código desde Configuración: no hay ninguna base fija en
el widget. Pegar el código de una base en un sitio conecta ese sitio con esa base.

## Base de datos

### Tenant (automático)

`App_Data/updates/2026-10-07-001__conversaciones_webchat_canal.sql` (idempotente):

- agrega `WEBCHAT` al `CHECK` `CK_CONV_CONVERSACIONES_Canal` (sin esto el `INSERT` falla);
- agrega la fila `WEBCHAT` en `CONV_CANALES`;
- agrega `CONV_CONVERSACIONES.OrigenPaginaUrl nvarchar(1000)` y `OrigenContexto nvarchar(200)`.

La conversación se identifica por `Canal = 'WEBCHAT'` + `IdentificadorExternoContacto = visitorId`
(id aleatorio que genera el widget y guarda en `localStorage`). Cada mensaje guarda en
`CONV_MENSAJES.PayloadJson` la página/contexto/nombre con que se envió, y `MessageIdExterno` = id del
mensaje generado por el widget (hace idempotente el reintento de un POST).

### Base central `ALFA_CENTRAL` (manual, solo SaaS)

No hay motor de migraciones para la base central. Antes de usar el canal en SaaS ejecutar una vez:

```sql
IF COL_LENGTH(N'dbo.bases', N'WebChatSiteKey') IS NULL
    ALTER TABLE dbo.bases ADD WebChatSiteKey nvarchar(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_bases_WebChatSiteKey' AND object_id = OBJECT_ID(N'dbo.bases'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_bases_WebChatSiteKey
        ON dbo.bases (WebChatSiteKey)
        WHERE WebChatSiteKey IS NOT NULL;
```

`WebChatSiteKeyService` consulta esta columna en queries propias, fuera del `SELECT` general de
`CentralBasesService`: si falta, solo falla el chat web (la pantalla avisa al generar la clave), no
el login ni los demás webhooks.

## Endpoints públicos (`Program.cs`)

| Método | Ruta | Uso |
|---|---|---|
| `GET` | `/api/webchat/{siteKey}/config` | Título, color y bienvenida del widget |
| `GET` | `/api/webchat/{siteKey}/mensajes?visitorId=&since=` | Polling. `since` = último `IdMensaje` recibido (0 = últimos 50) |
| `POST` | `/api/webchat/{siteKey}/mensajes` | `{ visitorId, clientMessageId, texto, nombre?, paginaUrl?, contexto? }` |
| `POST` | `/api/webchat/{siteKey}/imagenes` | `{ visitorId, clientMessageId, imagenBase64, nombreArchivo?, texto?, nombre?, paginaUrl?, contexto? }` |
| `POST` | `/api/webchat/{siteKey}/audios` | `{ visitorId, clientMessageId, audioBase64, nombreArchivo?, texto?, ... }` |
| `GET` | `/api/webchat/{siteKey}/adjuntos/{idAdjunto}?visitorId=` | Imagen o audio de una conversación del visitante |
| `OPTIONS` | `/api/webchat/{siteKey}/*` | Preflight CORS |

- Resolución de tenant: `IWebChatSiteKeyService.TryResolveTenantAsync` (SaaS → `dbo.bases.WebChatSiteKey`
  + `SetWebhookOverride`; una sola base → compara con `CONV_WEBCHAT_SITE_KEY`).
- Clave inexistente, canal inactivo u `Origin` fuera de los dominios permitidos → `404` sin detalle.
- CORS manual: se refleja el `Origin` (sin credenciales). El control de acceso real es la lista de
  dominios permitidos.
- Rate limiting (`AddRateLimiter`), partición clave de sitio + IP: lectura 120/min, escritura 20/min.
  Responde `429`. Si AlfaCore queda detrás de un proxy que no preserva la IP del cliente, todos los
  visitantes comparten partición: revisar antes de publicar.
- Body del POST limitado a 16 KB; texto hasta 2000 caracteres.
- El GET nunca devuelve notas internas, mensajes `SYSTEM`, envíos con error ni datos del agente.

## Envío desde la bandeja

`SendMessageAsync` trata `WEBCHAT` como `INTERNO`: el mensaje queda en estado `ENVIADO` y el widget lo
levanta por polling. Las respuestas del asistente IA, reglas y bienvenida usan el mismo camino. No hay
ventana de 24 h. Adjuntos: solo imágenes (JPG, PNG, GIF o WEBP) y audios (notas de voz grabadas en la
bandeja, MP3, etc.), de hasta 5 MB, validados por contenido; otro tipo de archivo se rechaza con un
mensaje para el agente. Queda `ENVIADO` y el
widget lo muestra sin el nombre del archivo (el texto del mensaje es el nombre, para la bandeja).

## Widget (`wwwroot/webchat/widget.js`)

Vanilla JS sin dependencias, en Shadow DOM (no le afecta el CSS del tema ni lo afecta). Todo el texto
se pinta con `textContent`. Hace polling solo con el panel abierto y la pestaña visible.

El `visitorId` vive en `sessionStorage`: la charla se mantiene al navegar por el sitio o recargar y se
descarta al cerrar la pestaña (la próxima visita empieza de cero). El botón "Nueva conversación" del
encabezado genera otro `visitorId`: en la bandeja queda como conversación nueva y la anterior se
conserva. Si `sessionStorage` no está disponible, usa un id en memoria (no sobrevive a recargar).

## Asistente IA por canal y tickets

| Clave (`TA_CONFIGURACION`) | Uso |
|---|---|
| `CONV_BOT_CANALES` | Canales en que responde el bot, separados por coma (`WHATSAPP,INSTAGRAM,FACEBOOK,MERCADOLIBRE,WEBCHAT`). Sin valor = todos (compatibilidad); `NINGUNO` = ninguno. En un canal sin bot, fuera de horario sale el mensaje fijo aunque `CONV_ASISTENTE_FUERA_HORARIO` esté activo |
| `CONV_BOT_GENERA_TICKET` | `1` (default): cuando el bot deriva (`DERIVA` o contención), crea un ticket con la consulta vinculado a la conversación y le pasa el número al cliente. Si la conversación ya tiene un ticket abierto no crea otro. `TicketsService` se resuelve con `IServiceProvider` porque depende de `ConversacionesService` |
| `CONV_BOT_WEB_PIDE_DATOS` | `1` (default): solo `WEBCHAT`, pedido de WhatsApp y razón social (ver arriba) |

## Imágenes y notas de voz

El widget tiene un botón de clip y acepta pegar una captura con Ctrl+V (si `CONV_WEBCHAT_IMAGENES`
no es `0`; `/config` devuelve `permiteImagenes`).

- Formatos JPG, PNG, GIF y WEBP, hasta 5 MB (`WebChatImagenes`). El tipo se decide por los primeros
  bytes del archivo, nunca por el nombre ni por el MIME que manda el navegador: un SVG o HTML
  renombrado se rechaza. El nombre del archivo se limpia y la extensión sale del contenido.
- El widget achica en el navegador las fotos de más de 1920 px de lado o más de 1,5 MB (JPEG 85 %);
  las capturas chicas y los GIF van tal cual.
- Se graba como cualquier imagen entrante de WhatsApp: mensaje `IMAGE` con texto `[image]` (o el texto
  que la acompañe) y adjunto en `CONV_ADJUNTOS` (archivo + contenido durable), con
  `PayloadJson.MediaId = clientMessageId` para que un reintento no la duplique. Se graba **antes** de
  las respuestas automáticas: el asistente la ve si "lee imágenes" está activo.
- La bandeja la muestra como los adjuntos de los demás canales.
- El polling devuelve en cada mensaje `imagenes` (ids de adjunto); el widget las pide a
  `/adjuntos/{id}?visitorId=` con `fetch` (mismo control de dominios) y las muestra como object URL.
  El servidor solo entrega adjuntos de una conversación WEBCHAT de ese `visitorId`, de mensajes
  visibles, y vuelve a validar que el contenido sea imagen (`nosniff`, `inline`).
- El agente también puede mandar imágenes y audios desde la bandeja (ver "Envío desde la bandeja").

Notas de voz (si `CONV_WEBCHAT_AUDIOS` no es `0`; `/config` devuelve `permiteAudios`):

- Botón de micrófono en el widget (solo si el navegador tiene `MediaRecorder`): se toca para grabar
  (contador, botón para descartar), se toca de nuevo o se usa la flecha de enviar para mandarla; corta
  sola a los 2 minutos. El navegador pide permiso de micrófono la primera vez.
- Formatos aceptados por contenido (`WebChatAudios`): WebM/Opus (Chrome, Edge), MP4/M4A (Safari),
  OGG (Firefox), WAV y MP3, hasta 5 MB (2 minutos de WebM pesan unos 500 KB).
- Se graba como un audio entrante de WhatsApp: mensaje `AUDIO` con texto `[audio]` + adjunto. Si en el
  Asistente IA está activo "Transcribir audios", el bot lo transcribe y responde como si fuera texto.
- La bandeja lo reproduce con el reproductor de siempre; el widget muestra un reproductor para los
  audios propios y los del agente (`audios` en el polling, mismo endpoint `/adjuntos/{id}`).

## Chat con soporte desde Alfa Gestión (VB6)

Los usuarios de Alfa Gestión chatean con el soporte de ALFA NET sin salir del sistema. Es el mismo
widget, en **modo ventana**, servido por AlfaCore en una página propia:

```text
GET /webchat/soporte?contexto=<quién escribe>&nombre=<nombre precargado>
```

- La clave del sitio la pone el servidor: `WebChat:SoporteSiteKey` en `appsettings.json` (la clave
  `AC-...` de la base de ALFA NET, generada en Configuración de Conversaciones → Canales → Chat del
  sitio web). Sin clave válida la página muestra "Chat no disponible". `WebChat:SoporteTitulo` es el
  título de la pestaña.
- Si la base de ALFA NET tiene `CONV_WEBCHAT_DOMINIOS_PERMITIDOS` cargado, agregar el host del propio
  AlfaCore (ej. `alfacentral.ddns.net`): el widget corre en esa página.
- Atributos nuevos del widget (no cambian el uso en sitios de clientes): `data-modo="ventana"` (ocupa
  toda la ventana, arranca abierto, sin burbuja ni botón de cerrar) y `data-nombre` (nombre precargado).
- Alfa Gestión (CN000PR) abre `msedge --app=<url>` con `contexto = "ALFA GESTION | CLI:<código de
  cliente> | <razón social> | USR:<usuario> | BASE:<base>"` y `nombre = "<usuario> (<razón social>)"`.
  Accesos: la línea "Chateá con nosotros" del encabezado del menú y una opción de menú con
  `PROCESO = ChatSoporte` (se agrega a mano en `TA_MENU`, nombre terminado en "(web)"). En la base del
  cliente, `TA_CONFIGURACION.CHAT_SOPORTE_URL` cambia la dirección o, con `NO`, oculta el chat.

## Limitaciones conocidas

- Visitante anónimo: `ResolverCuentaVinculadaAsync` no encuentra cuenta, el asistente responde en
  modo general (AlfaKnowledge/texto) y deriva a un humano; no informa saldos ni pedidos. Con
  `CONV_BOT_WEB_PIDE_DATOS = 1` (default) el bot pide una vez el WhatsApp y la razón social
  (`PedidoDatosVisitanteWeb`, marca `NotaBotPidioDatosWeb`); al recibirlos,
  `GuardarDatosVisitanteWebAsync` graba `TelefonoWhatsApp`/`NombreVisible` y, si el teléfono coincide
  con un contacto (`TryFindContactByPhoneAsync`), `IdContacto`/`ClienteCodigo`. Desde ahí el visitante
  queda identificado y el asistente puede usar sus datos reales.
- El POST espera a que termine la cadena de respuestas automáticas (puede tardar varios segundos con
  el bot); el widget no se bloquea porque la respuesta llega por polling.
- Sin caché de la resolución `siteKey → base`: cada polling consulta la base central.

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
adjuntos y branding avanzado.

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
ventana de 24 h. Adjuntos: no soportados (mismo mensaje que Instagram/Facebook).

## Widget (`wwwroot/webchat/widget.js`)

Vanilla JS sin dependencias, en Shadow DOM (no le afecta el CSS del tema ni lo afecta). Todo el texto
se pinta con `textContent`. Hace polling solo con el panel abierto y la pestaña visible. Si
`localStorage` no está disponible, usa un `visitorId` en memoria (la charla no sobrevive a recargar).

## Limitaciones conocidas

- Visitante anónimo: `ResolverCuentaVinculadaAsync` no encuentra cuenta, el asistente responde en
  modo general (AlfaKnowledge/texto) y deriva a un humano; no informa saldos ni pedidos.
- El POST espera a que termine la cadena de respuestas automáticas (puede tardar varios segundos con
  el bot); el widget no se bloquea porque la respuesta llega por polling.
- Sin caché de la resolución `siteKey → base`: cada polling consulta la base central.

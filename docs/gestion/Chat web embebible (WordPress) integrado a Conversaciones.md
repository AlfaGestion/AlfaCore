# Chat web embebible (WordPress) integrado a Conversaciones

## Contexto

El usuario quiere ofrecer, como hace Odoo con su Livechat, un widget de chat embebible en un sitio
externo (WordPress) que caiga en la misma bandeja de **Conversaciones** donde hoy se atienden
WhatsApp/Instagram/Facebook/Mercado Libre. Como AlfaCore es multi-base (modo SaaS: muchos clientes,
cada uno con su propia base de datos), el snippet que se pega en el sitio del cliente necesita indicar
a qué base apunta, y opcionalmente en qué página/entrada del sitio está (para diferenciar, por ejemplo,
"consulta sobre la actualización v3.2" de "consulta general" en la home).

Decisiones ya confirmadas con el usuario:
- **Visitante anónimo por defecto**, con nombre opcional (no se exige login ni email para escribir).
- **El agente IA responde primero** (reutilizando el asistente de Conversaciones ya implementado con
  tool-calling de precios/saldos/pedidos, ver memoria `conversaciones-agente-ia-plan`), derivando a un
  humano si no puede resolver la consulta.

## Investigación previa (verificado contra el código real, no asumido)

- **Canales existentes**: `ConversacionCanales` (`src/AlfaCore/Models/ConversacionesModels.cs:5-12`)
  define `WHATSAPP/INSTAGRAM/FACEBOOK/MERCADOLIBRE/INTERNO` como discriminador de
  `dbo.CONV_CONVERSACIONES.Canal`. Se suma `WEBCHAT` a esa lista.
- **Patrón "resolver o crear conversación por id externo"**: cada canal tiene su propio
  `Ensure*ConversationAsync` en `ConversacionesService.cs` con la misma forma — transacción
  `Serializable` + `SELECT TOP (1) IdConversacion FROM dbo.CONV_CONVERSACIONES WITH (UPDLOCK, HOLDLOCK)
  WHERE Canal = @Canal AND IdentificadorExternoContacto = @IdExterno`, y si no existe, `INSERT`. El más
  cercano a copiar es `EnsureInstagramConversationAsync` (línea 6627). Para WebChat, `IdentificadorExternoContacto`
  será el `visitorId` que genera el propio widget (aleatorio, persistido en `localStorage` del
  navegador del visitante) — mismo rol que el número de teléfono en WhatsApp o el `SenderId` de
  Instagram. No hace falta tabla nueva de mapeo.
- **Cadena de respuesta automática ya genérica por canal**: tras insertar el mensaje entrante y
  refrescar la conversación, cada handler llama en secuencia `NotifyIncomingMessageAsync` →
  `TryAutoReplyReglasAsync` → (si no aplicó) `TryAutoReplyWelcomeAsync` → `TryAutoReplyOutOfHoursAsync`
  → `TryAutoReplyBotAsync` (ver por ejemplo `ConversacionesService.cs:4023-4038`). **Esta cadena no
  depende del canal** — con solo llamarla desde un nuevo método `RegistrarMensajeEntranteWebChatAsync`
  se obtiene gratis la respuesta del asistente IA (tool-calling de precios/saldo/pedidos) exactamente
  como ya sucede en WhatsApp. Importante: como el visitante es anónimo, `ResolverCuentaVinculadaAsync`
  no va a encontrar una cuenta Cliente/Proveedor vinculada salvo que el visitante se identifique más
  adelante — el bot va a responder en modo general (AlfaKnowledge/texto libre) y derivar a un humano
  cuando no pueda resolver la consulta. Esto es una consecuencia esperable de "anónimo por defecto", no
  un bug; queda anotado como limitación conocida del MVP.
- **Multi-tenant por token**: `dbo.bases` (base central `AlfaCentral`, `CentralBasesService.cs`) ya
  tiene una columna `WebhookToken` resuelta por `GetByWebhookTokenAsync`/`EnsureWebhookTokenAsync` y
  usada por `TryResolveWebhookTenantAsync` (`Program.cs:3429`) para que cualquier webhook público sepa
  a qué base conectarse. **No se reutiliza el mismo `WebhookToken`** para el widget: ese token hoy solo
  lo ven Meta/Mercado Libre en configuraciones servidor-a-servidor; exponerlo en el HTML público de un
  sitio (visible por cualquiera con "ver código fuente") permitiría a cualquier visitante golpear
  también los webhooks de WhatsApp/Instagram/Facebook de esa base. Se agrega una columna nueva y
  separada, `WebChatSiteKey`, exclusiva de este canal.
- **Sin motor de migraciones para la base central**: las migraciones idempotentes en
  `App_Data/updates/*.sql` (via `ActualizacionesService`) corren contra la conexión del tenant
  (`AlfaGestion`), nunca contra `AlfaCentral`. Agregar `WebChatSiteKey` a `dbo.bases` es un cambio de
  esquema manual (un `ALTER TABLE` directo contra la base central) — no hay script versionado para eso
  hoy. Se documenta el `ALTER TABLE` exacto a correr a mano.
- **Archivos estáticos**: `app.UseStaticFiles(...)` ya sirve `wwwroot` tal cual (`Program.cs:472-476`),
  y `.js` ya tiene content-type correcto por defecto — un archivo nuevo en `wwwroot/webchat/widget.js`
  queda servido en `/webchat/widget.js` sin tocar nada más.
- **Sin rate limiting hoy**: no hay `AddRateLimiter`/`UseRateLimiter` en todo `Program.cs`. Los
  endpoints nuevos de este feature son los primeros totalmente anónimos y de escritura pública de todo
  el sistema (los webhooks de Meta/ML exigen un token difícil de adivinar y solo los llama el proveedor;
  acá cualquier visitante de la web puede pegarle directo) — se agrega rate limiting básico scopeado a
  estos dos endpoints.
- **Patrón de configuración (3 archivos)**: toggles nuevos siguen el triángulo ya usado por
  `AsistenteHerramientaPrecios` — DTO en `ConversacionesConfiguracionModels.cs`, lectura/escritura en
  `ConversacionesConfigService.cs` contra `TA_CONFIGURACION` (claves `CONV_ASISTENTE_HERRAMIENTA_*` de
  ejemplo), checkbox/input en `ConversacionesConfiguracion.razor` (`conv-config-tool-row`).

## Alcance del MVP

**Incluye:**
1. Canal `WEBCHAT` nuevo en `ConversacionCanales`, visible en el inbox de Conversaciones igual que los
   demás (filtro por canal, ícono propio).
2. Columna `WebChatSiteKey` en `dbo.bases` (central) + `EnsureWebChatSiteKeyAsync`/
   `GetByWebChatSiteKeyAsync` en `CentralBasesService`, calcado de `EnsureWebhookTokenAsync`.
3. Sección nueva "Chat del sitio web" en `ConversacionesConfiguracion.razor`:
   - Activar/desactivar el canal (`CONV_WEBCHAT_ACTIVO`).
   - Dominios permitidos, opcional (`CONV_WEBCHAT_DOMINIOS_PERMITIDOS`, texto separado por comas) — si
     está vacío, se acepta cualquier origen (con una advertencia en la UI de que conviene completarlo).
   - Mensaje de bienvenida y color/título básico del widget (`CONV_WEBCHAT_MENSAJE_BIENVENIDA`,
     `CONV_WEBCHAT_TITULO`, `CONV_WEBCHAT_COLOR`).
   - Botón "Generar clave" (si no existe todavía) + snippet listo para copiar con la clave ya insertada.
4. Dos endpoints públicos nuevos en `Program.cs` (junto a los demás webhooks de Conversaciones):
   - `POST /api/webchat/{siteKey}/mensajes` — recibe `{ visitorId, texto, nombre?, pageUrl?, contexto? }`.
     Resuelve la base por `siteKey` (mismo mecanismo que `TryResolveWebhookTenantAsync`, pero por
     `WebChatSiteKey`), valida que el canal esté activo (`CONV_WEBCHAT_ACTIVO`) y, si hay dominios
     permitidos configurados, que el header `Origin` matchee alguno; si no, `404`/`403` silencioso (no
     revelar por qué). Llama a `RegistrarMensajeEntranteWebChatAsync` (nuevo, en `ConversacionesService`,
     mismo shape que `EnsureInstagramConversationAsync` + inserción de mensaje + la cadena
     `NotifyIncomingMessageAsync`/`TryAutoReply*`).
   - `GET /api/webchat/{siteKey}/mensajes?visitorId=...&since=...` — el widget hace polling corto (cada
     3-4s mientras está abierto) para traer la respuesta del bot o de un agente humano. `Cache-Control:
     no-store`. Se elige polling en vez de una conexión persistente (SignalR/WebSocket) para el MVP:
     más simple de exponer públicamente, sin conexiones que mantener vivas para visitantes anónimos, y
     el retraso de unos segundos es aceptable para un chat de consultas (no trading). Documentado como
     mejora futura si se necesita respuesta más instantánea.
   - Rate limiting básico (partitioned por `siteKey`+`visitorId`) sobre ambos endpoints, usando
     `Microsoft.AspNetCore.RateLimiting` (paquete ya incluido en ASP.NET Core, sin dependencia nueva).
5. Widget embebible `wwwroot/webchat/widget.js` (vanilla JS, sin frameworks, para no chocar con lo que
   ya cargue WordPress): burbuja flotante → panel con historial + input. Lee `data-site`/`data-contexto`
   del propio `<script>` que lo cargó, genera/persiste `visitorId` en `localStorage`, arranca sin pedir
   nombre (puede completarse más adelante, opcional, si el visitante quiere que le respondan por otro
   medio). Snippet final para el cliente:
   ```html
   <script src="https://tu-alfacore.com/webchat/widget.js" data-site="AC-xxxxxxxx" data-contexto="opcional"></script>
   ```
6. En el inbox de Conversaciones: mostrar `PaginaUrl`/`Contexto` del visitante en el panel de detalle de
   la conversación (mismo lugar donde hoy se ve el teléfono/usuario de otros canales), para que el
   agente (o el contexto que recibe el asistente IA) sepa desde qué página vino la consulta.

**Explícitamente fuera de este MVP:**
- Identificación obligatoria (nombre/email) — el usuario pidió anónimo con nombre opcional.
- Transporte en tiempo real (SignalR/WebSocket) — queda polling para esta primera entrega.
- Vincular al visitante anónimo con una cuenta Cliente/Proveedor real (lo que habilitaría saldo/pedidos
  reales vía el asistente) — requeriría un paso de identificación a mitad de charla, fuera de alcance.
- Múltiples dominios/sitios por base con claves distintas — una sola `WebChatSiteKey` por base alcanza
  para el caso planteado (una entrada puntual + la home del mismo sitio, diferenciadas por `contexto`,
  no por dominio).
- Branding avanzado (logo, posición configurable, temas) — placeholder simple con color+título.

## Diseño técnico

**Modelos** (`src/AlfaCore/Models/ConversacionesModels.cs` y `ConversacionesConfiguracionModels.cs`):
- `ConversacionCanales.WebChat = "WEBCHAT"`.
- DTOs de request/response para los endpoints (`WebChatMensajeEntranteDto`, `WebChatMensajeSalienteDto`).
- Nuevos campos en `ConversacionAutomatizacionesConfigDto` (o DTO de config específico si el archivo ya
  está muy cargado): `WebChatActivo`, `WebChatDominiosPermitidos`, `WebChatMensajeBienvenida`,
  `WebChatTitulo`, `WebChatColor`.

**`CentralBasesService.cs`**: `EnsureWebChatSiteKeyAsync(idBase, ct)` / `GetByWebChatSiteKeyAsync(siteKey, ct)`,
calcados línea por línea de `EnsureWebhookTokenAsync`/`GetByWebhookTokenAsync` (líneas 27-69), mismo
`UPDATE ... WHERE WebChatSiteKey IS NULL` para generación idempotente.

**`ConversacionesService.cs`**:
- `EnsureWebChatConversationAsync(visitorId, nombreOpcional, pageUrl, contexto, ct)` — calcado de
  `EnsureInstagramConversationAsync` (línea 6627): mismo `UPDLOCK/HOLDLOCK` + `SELECT`/`INSERT` sobre
  `CONV_CONVERSACIONES`, `Canal = 'WEBCHAT'`, `IdentificadorExternoContacto = visitorId`. Persistir
  `pageUrl`/`contexto` en las columnas que ya existan para metadata libre de conversación (a confirmar
  el nombre exacto contra el schema real de `CONV_CONVERSACIONES` al implementar — no asumir un nombre
  de columna sin verificarlo primero).
- `RegistrarMensajeEntranteWebChatAsync(siteKeyResuelto/idBase, request, ct)` — inserta el mensaje,
  llama `RefreshConversationAsync`, `NotifyIncomingMessageAsync`, y la cadena
  `TryAutoReplyReglasAsync`/`TryAutoReplyWelcomeAsync`/`TryAutoReplyOutOfHoursAsync`/`TryAutoReplyBotAsync`
  igual que los demás canales (ej. líneas 4023-4038).
- `ObtenerMensajesNuevosWebChatAsync(visitorId, since, ct)` — para el polling del widget: trae mensajes
  de la conversación de ese `visitorId` posteriores a `since`.

**`Program.cs`**: dos `app.MapPost/MapGet("/api/webchat/{siteKey}/mensajes", ...)` junto a los demás
mapeos de Conversaciones, resolviendo tenant por `WebChatSiteKey` (mismo patrón que
`TryResolveWebhookTenantAsync`, se puede generalizar o duplicar esa función con el nuevo lookup), más
`AddRateLimiter`/`.RequireRateLimiting(...)` sobre ambos.

**Central DB**: documentar el `ALTER TABLE dbo.bases ADD WebChatSiteKey nvarchar(50) NULL;` a correr a
mano contra `AlfaCentral` antes de desplegar — no hay migración automática para esta tabla.

**Frontend**:
- `wwwroot/webchat/widget.js` + CSS inline (para no depender de un archivo `.css` adicional que el
  tema de WordPress pueda pisar).
- `Components/Pages/ConversacionesConfiguracion.razor`: sección "Chat del sitio web" (activar, dominios,
  mensaje de bienvenida, color/título, botón generar clave + snippet copiable).
- Inbox: filtro de canal + ícono para `WEBCHAT`, panel de detalle mostrando página/contexto de origen.

## Fasificación

- **Fase 1** (backend + config): modelo de datos (`WebChatSiteKey`, canal `WEBCHAT`), los dos endpoints
  públicos con rate limiting, wiring a la cadena de respuesta automática existente, sección de
  configuración con generación de clave. Verificable con `curl`/Postman simulando el widget, sin HTML
  todavía.
- **Fase 2** (widget real): `widget.js` embebible + snippet, probado insertado en una página HTML de
  prueba (no hace falta WordPress real para validar esta fase).
- **Fase 3** (pulido en el inbox): ícono/filtro de canal, panel de contexto de página, mensajes de error
  más claros en el widget (base inactiva, canal desactivado, límite de tasa alcanzado).
- **Futuro, no en este alcance**: pasar a SignalR/WebSocket para respuesta instantánea; permitir
  identificarse a mitad de charla para desbloquear saldo/pedidos reales vía el asistente; múltiples
  claves/dominios por base.

## Verificación

1. `ALTER TABLE` manual en `AlfaCentral.dbo.bases`, confirmar que `EnsureWebChatSiteKeyAsync` genera una
   clave nueva la primera vez y la reutiliza después.
2. Simular con `curl` un `POST /api/webchat/{siteKey}/mensajes` con un `visitorId` inventado: confirmar
   que crea la conversación en `CONV_CONVERSACIONES` con `Canal='WEBCHAT'`, aparece en el inbox de
   Conversaciones, y que el asistente IA responde (con `CONV_WEBCHAT_ACTIVO` y el bot activos).
3. Confirmar que un dominio no listado en "dominios permitidos" (cuando está configurado) es
   rechazado, y que uno permitido funciona.
4. Insertar el snippet en una página HTML estática de prueba, chatear desde el navegador, confirmar que
   el polling trae tanto la respuesta del bot como la de un agente humano que responde manualmente desde
   el inbox de AlfaCore.
5. Probar con dos `visitorId` simultáneos (dos pestañas/navegadores) para confirmar que no se cruzan
   conversaciones.
6. `dotnet build` limpio y `python tools/catalogo/check_catalogo.py` sin diferencias relacionadas.

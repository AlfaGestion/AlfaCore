# Conversaciones

## Descripción del módulo

`Conversaciones` centraliza la gestión de mensajes dentro de `AlfaCore`. Soporta dos canales:

- **WHATSAPP** — inbox operativo para atender clientes por WhatsApp Cloud API
- **INTERNO** — chat entre técnicos del equipo, sin dependencia de WhatsApp

Funcionalidades disponibles hoy:

- inbox con filtros por canal, estado y técnico asignado
- historial de mensajes con soporte de texto, imágenes, archivos, audio y notas internas
- asignación y reasignación de conversaciones a técnicos
- gestión de estados (Abierta, Pendiente, En gestión, Cerrada, Archivada)
- adjuntos: imágenes, documentos y mensajes de voz grabados en el navegador
- hilos internos entre técnicos (sin número de teléfono requerido)
- configuración del canal WhatsApp desde pantalla interna
- copiloto de IA ("Sugerencia IA"): sugiere una respuesta al técnico usando la base de conocimiento de AlfaKnowledge, sin enviarla automáticamente

> Nota: la lista de arriba (WHATSAPP/INTERNO) quedó desactualizada — ver "Novedades recientes"
> debajo para los canales y funcionalidades agregados después.

## Novedades recientes (2026-08)

- **Canales sociales unificados**: Instagram, Facebook y WhatsApp se filtran desde un combo
  único "Mensajes" (`Todos` / WhatsApp / Instagram / Facebook) en vez de una pestaña por canal.
  Mercado Libre y Chat interno siguen como pestañas separadas por tener una lógica de trabajo
  distinta. El módulo también soporta el canal `MERCADOLIBRE` (preguntas de publicaciones).
- **Clasificación y abono del cliente** visibles en el header del chat (badge de clasificación de
  `TA_CLASIFICACIONES` + estado Abonado/Suspendido/Sin abono/Contacto sin cuenta/Sin
  identificar, resuelto desde `MA_CUENTAS_AUTOCPTES`).
- **Cola de espera**: orden alternativo del inbox por prioridad de cliente (3 niveles,
  configurables en `/conversaciones/configuracion` reusando las claves `CLASIFICA1/2/3` que ya
  usa Desktop) y luego por tiempo esperando respuesta. Se activa con el botón "Tiempo de
  espera"; cada fila muestra hace cuánto espera si el último mensaje fue entrante.
  Badge de origen (WhatsApp/Instagram/Facebook/Mercado Libre/Interno) superpuesto en el avatar
  de cada fila del inbox.
- **Búsqueda dentro de una conversación abierta** (resaltado + navegación entre coincidencias),
  además de la búsqueda de conversaciones/contactos ya existente.
- **Filtro "Ver de un técnico"** dentro del combo "Pendientes ▾" (además de "Asignadas a mí"):
  deja elegir cualquier técnico, no solo el propio.
- **Múltiples números de WhatsApp** (opcional, un cliente con un solo número no nota nada
  distinto): ver `docs/modulos/conversaciones_whatsapp_conexion.md#múltiples-números-de-whatsapp-opcional`
  para la configuración completa.
- **Módulo Archivos > Tablas**: editor genérico de tablas de referencia chicas (forma
  Codigo/Descripcion/Color), sin costo/no licenciado, con `TA_CLASIFICACIONES` como primera
  tabla registrada. No es parte de Conversaciones en sí, pero nació de esta misma tanda de
  trabajo (la usa la configuración de clasificaciones/prioridad).

Integra estructuras existentes del sistema:

- clientes desde `VT_CLIENTES`
- técnicos desde `V_TA_Tecnicos`
- contactos desde `MA_CONTACTOS`
- logging técnico en `AUX_ERR`

## Canales

### WHATSAPP

Conversaciones iniciadas por clientes o por el equipo vía WhatsApp Cloud API de Meta. Requieren número de teléfono. El envío usa el endpoint de Meta configurado en `TA_CONFIGURACION`.

### INTERNO

Hilos de chat entre técnicos. No requieren número de teléfono ni configuración de Meta. Los mensajes se guardan localmente con `EstadoEnvio = ENVIADO` sin pasar por la API de WhatsApp. Soportan todos los tipos de adjunto igual que el canal WhatsApp.

## Arquitectura implementada

### Frontend

- Blazor Server sobre la estructura existente de `AlfaCore`
- pantalla `/conversaciones` con layout de tres paneles:
  - lista de conversaciones con canal switcher (WhatsApp / Chat interno)
  - chat central con historial, compositor, adjuntos y grabación de voz
  - panel lateral con contexto, asignación y estado
- modal para crear hilos internos
- `IJSRuntime` para grabación de audio via `MediaRecorder`

### Backend

- `ConversacionesService` — servicio principal con acceso a SQL Server
- `ConversacionesConfigService` — gestión de parámetros del canal WhatsApp
- minimal API endpoints registrados en `Program.cs`
- archivos de adjuntos almacenados en `App_Data/uploads/conversaciones/{IdConversacion}/`
- script JS en `wwwroot/js/conversaciones.js` para grabación de audio

### Base de datos

Tablas del módulo (`CONV_*`):

- `CONV_ESTADOS` — catálogo de estados (Abierta, Pendiente, En gestión, Cerrada, Archivada)
- `CONV_CONVERSACIONES` — conversaciones; `Canal` admite `WHATSAPP` e `INTERNO`; `TelefonoWhatsApp` es nullable
- `CONV_MENSAJES` — mensajes; `TelefonoWhatsApp` es nullable; `MessageType` admite TEXT, IMAGE, AUDIO, VIDEO, DOCUMENT y más
- `CONV_ASIGNACIONES` — historial de asignaciones por técnico
- `CONV_ADJUNTOS` — metadata de archivos adjuntos con ruta local y mime type
- `CONV_ETIQUETAS` / `CONV_CONVERSACION_ETIQUETAS` — etiquetas (estructura lista, UI pendiente)
- `CONV_WEBHOOK_LOG` — log de payloads recibidos desde Meta

## Plantillas con archivo en el encabezado (PDF o imagen)

- El encabezado de una plantilla puede ser texto, **documento (PDF)** o **imagen (JPG/PNG)**.
- Cada plantilla puede guardar su archivo de encabezado (`CONV_PLANTILLAS_ENCABEZADO`, en SQL; script `2026-11-04-050`): la oferta o la imagen.
  - Al pedir la aprobación en Meta se usa ese archivo como ejemplo (`header_handle`). Sin archivo, un documento usa un PDF de ejemplo propio y una imagen no se puede enviar a aprobar.
  - Al enviar desde el chat, el archivo guardado viaja solo; se puede elegir otro en el momento. Una plantilla remota (sincronizada desde Meta) con encabezado de archivo pide elegirlo.
- Cierre de caja → WhatsApp sigue mandando su Excel/PDF como documento del encabezado.
- Límite por archivo: 16 MB (Meta: imagen hasta 5 MB).

## Adjuntos y mensajes de voz

### Almacenamiento

Los archivos se guardan en:

```
App_Data/uploads/conversaciones/{IdConversacion}/{guid}{ext}
```

El nombre físico es un GUID para evitar colisiones. El nombre original se persiste en `CONV_ADJUNTOS.NombreArchivo`.

### Tipos soportados

| TipoArchivo | Cómo se muestra |
|---|---|
| `IMAGE` | imagen inline con preview |
| `AUDIO` | reproductor `<audio controls>` |
| `VIDEO` | link de descarga (visualización futura) |
| `DOCUMENT` | link de descarga con tamaño |

### Grabación de voz

El navegador captura audio via `MediaRecorder` (script `conversaciones.js`). Al detener la grabación, el blob se convierte a base64, se envía al servicio como `MemoryStream` y se guarda como `audio/webm`.

Tamaño máximo por archivo: **25 MB**.

## Endpoints

### Inbox y conversaciones

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/conversaciones` | lista con filtros (modo, search, técnico, estado, canal) |
| GET | `/api/conversaciones/{id}` | detalle de una conversación |
| GET | `/api/conversaciones/{id}/mensajes` | mensajes de una conversación |
| POST | `/api/conversaciones/{id}/mensajes` | enviar mensaje (texto) |
| POST | `/api/conversaciones/{id}/notas` | agregar nota interna |
| POST | `/api/conversaciones/{id}/asignacion` | asignar técnico |
| POST | `/api/conversaciones/{id}/estado` | cambiar estado |

### Adjuntos

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/conversaciones/{id}/adjuntos` | subir archivo (multipart/form-data, campo `archivo`) |
| GET | `/api/conversaciones/adjuntos/{idAdjunto}` | servir archivo para display o descarga |

### Chat interno

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/conversaciones` | — (se crea desde Blazor via `CreateInternalThreadAsync`) |

### WhatsApp webhook

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/conversaciones/whatsapp/webhook` | verificación del webhook (Meta) |
| POST | `/api/conversaciones/whatsapp/webhook` | recepción de mensajes entrantes |

**Estados que llegan antes que el mensaje (coexistencia).** La copia de un mensaje escrito en la app del celular
(`smb_message_echoes`) y su estado (enviado, entregado, leído) llegan en webhooks separados. Si el estado se
procesa primero, queda en `CONV_MENSAJES_ESTADO_PENDIENTE` y se aplica cuando se graba el mensaje con ese
`WhatsAppMessageId`: la copia de la app o el `wamid` de un envío propio. Lo que no encuentra mensaje en 7 días se borra
solo. Script `2026-11-12-050__conversaciones_estados_whatsapp_pendientes.sql`. Meta no manda estado para todos los
mensajes escritos en la app: los que nunca reciben uno quedan en "Enviado".

## Puesta en marcha en una base nueva

Ejecutar antes de usar el módulo:

```
docs/conversaciones_modelo_inicial.sql
```

Si falta alguna tabla `CONV_*`, la aplicación registra el incidente en `AUX_ERR` y muestra un mensaje operativo con los pasos sugeridos.

## Configuración del canal WhatsApp

Pantalla disponible: `/conversaciones/configuracion`

Persistencia: `TA_CONFIGURACION`

Claves del módulo:

- `CONV_WHATSAPP_VERIFY_TOKEN`
- `CONV_WHATSAPP_ACCESS_TOKEN`
- `CONV_WHATSAPP_PHONE_NUMBER_ID`
- `CONV_WHATSAPP_BUSINESS_ACCOUNT_ID`
- `CONV_WHATSAPP_APP_SECRET`
- `CONV_WHATSAPP_API_VERSION`
- `CONV_WHATSAPP_PUBLIC_BASE_URL`
- `CONV_WHATSAPP_WEBHOOK_PATH`

Manual operativo: `docs/conversaciones_whatsapp_conexion.md`

### Manejo de errores y conservación de estado

La pantalla de configuración usa un patrón base del proyecto para errores de UI:

- el error técnico completo sigue registrándose en `AUX_ERR` y diagnóstico local
- la UI muestra un mensaje claro para el usuario
- cuando existe, se conserva un código de incidente para soporte
- si falla una operación, el formulario no debe perder automáticamente lo que el usuario ya escribió

Caso ya contemplado:

- si la sesión SQL activa no puede conectarse, la UI informa que no se pudo conectar a la base activa y sugiere revisar sesión, red, instancia y credenciales

Este patrón debe extenderse al resto de módulos para mantener una experiencia consistente.

## Copiloto de IA (Sugerencia IA)

Botón "Sugerencia IA" en la barra de acciones del chat (junto a "Contexto" y "Crear ticket"). Abre un panel lateral que le sugiere al técnico una respuesta redactada por IA para el último mensaje entrante del cliente, con las fuentes de la base de conocimiento que la respaldan. El técnico decide: usarla tal cual, editarla o escribir la suya — es asistencia durante un período de entrenamiento, **no** envío automático.

Detalle técnico completo (configuración, endpoint consumido, autenticación) en `docs/modulos/integraciones/alfaknowledge_copiloto_ia.md`.

### Comportamiento del panel

- **Persistente**: a diferencia del panel de Contexto (que colapsa a overlay flotante en pantallas angostas), el panel de Sugerencia IA siempre se acopla como columna propia del layout — nunca tapa la conversación — y queda abierto hasta que el técnico lo cierra explícitamente (botón de cerrar, "Usar esta respuesta" o "Descartar").
- **Atento a la conversación activa**: mientras está abierto, se regenera solo al cambiar de conversación seleccionada y cada vez que llega un mensaje nuevo del cliente durante el polling automático (`AutoRefreshAsync`), sin que el técnico tenga que tocar "Regenerar" a mano.
- Acciones disponibles: **Usar esta respuesta** (vuelca el texto al compositor y registra `isHelpful=true`), **Regenerar** (vuelve a pedir la sugerencia) y **Descartar** (registra `isHelpful=false` y cierra el panel).

## Asistente vendedor: alta de clientes y dirección de entrega (2026-11-17)

Configuración → Asistente IA → Herramientas suma dos opciones, pensadas para que el bot tome pedidos de
punta a punta (primer caso: un reparto de agua):

- **Reconocer clientes por teléfono** (`CONV_ASISTENTE_IDENTIFICA_POR_TELEFONO`): si el chat no está vinculado
  a un cliente y el número coincide con el teléfono cargado de **un solo** cliente activo, lo atiende como ese
  cliente. Con *Confirmar identidad* activo, antes del primer dato privado le pregunta si es él; al confirmar,
  la conversación queda vinculada (`CONV_CONVERSACIONES.ClienteCodigo`) con una nota 📞.
- **Dar de alta clientes** (`CONV_ASISTENTE_HERRAMIENTA_ALTA_CLIENTES`): a quien escribe sin cuenta le pide
  nombre y apellido, DNI, calle y número, localidad y una referencia (tool `registrar_cliente`). Antes de
  proponer el alta busca si ya es cliente:
  - mismo DNI, mismo teléfono, o mismo nombre **y** misma calle y altura, y uno solo → vincula la conversación
    a ese cliente (nota 👤 para revisar), sin crear otro;
  - coincidencias dudosas o varias → no crea nada, deriva y deja una nota 👤 con los posibles clientes;
  - sin coincidencias → le muestra los datos y lo da de alta **solo si responde "sí"** (como los pedidos). Queda
    una nota 🆕 para revisar zona o día de reparto y lista de precios.
  Los valores comerciales salen del cliente modelo `CONV_ASISTENTE_ALTA_CLIENTE_MODELO` (opcional) o de los más
  usados por los clientes activos.
- **Dirección de entrega** en *Tomar pedidos*: el resumen del pedido muestra "Entrega en: …" (la dirección de la
  cuenta, o la que el cliente pidió en el chat) y queda en las observaciones de la Nota de Pedido.

- **Precios solo a clientes identificados** (sin *precio consumidor* en el catálogo): es una política que se le
  pasa a la IA mientras quien escribe no esté identificado; si el mensaje es una consulta de precio lo decide la
  IA leyendo la conversación (campo `pide_precio` del JSON), no una lista de palabras. Con *Dar de alta clientes*
  activo, además de pedirle el nombre si ya es cliente le ofrece registrarse. La plantilla de respaldo solo se
  usa si la IA dijo `pide_precio` y derivó o dio una respuesta indebida, y nunca mientras está registrando al
  cliente (2026-11-17, Las Vertientes: el interesado nuevo quedaba en un loop de "si ya sos cliente…").
- **Chat web: pedir WhatsApp y nombre** (`CONV_BOT_WEB_PIDE_DATOS`): mientras el visitante no dejó un teléfono,
  la IA tiene la tool `guardar_contacto_web` y se los pide en un momento natural de la charla; el servidor solo
  valida el teléfono (8 a 15 dígitos). Reemplaza la pregunta fija de "WhatsApp y razón social" y su lectura por
  expresiones (guardaba "no tengo" como razón social).

Borradores en `CONV_BOT_CLIENTES` y `CONV_BOT_PEDIDOS.DireccionEntrega` (script
`2026-11-17-053__conversaciones_bot_alta_clientes.sql`). Código: `ConversacionBotClientesService`.

## Bot: mensajes seguidos del cliente (2026-11-25)

- El cliente suele escribir en varios mensajes ("Buen día" + "Buenas tardes", "no me actualizan los precios en
  las cajas" + "2,3y4"). Antes el bot contestaba cada uno por separado: dos saludos, una pregunta ya contestada y
  un saludo fijo a "2,3y4" (Supermercado Manantial, ALFANET).
- Ahora espera `CONV_BOT_AGRUPAR_SEGUNDOS` (6 por defecto, 0 = no espera; Configuración → Asistente IA →
  Límites, «Segundos para que el cliente termine de escribir»). Si en ese tiempo llega otro mensaje, contesta
  el llamado de ese último, y a la IA le llega todo lo que el cliente escribió desde nuestra última respuesta,
  un mensaje por renglón (`UnirMensajesSinResponder`; si el último es audio o imagen va solo ese). Lo mismo en
  el job de «Minutos de espera antes de responder» y en los mensajes que llegan mientras el bot está contestando.
- Un mensaje con números nunca es un saludo (`EsMensajeSocial`): "2,3y4" quedaba reducido a la "y".

## Bot: interpreta la IA, no listas de palabras (2026-11-25, rama `bot-ia-decide`)

Lo que quiere decir el cliente lo decide la IA leyendo la conversación completa. Se sacaron las reglas por
palabras que pisaban su respuesta:

| Antes (regla por palabras) | Ahora |
|---|---|
| Saludo / agradecimiento → texto fijo "¡Hola! ¿En qué te puedo ayudar?" | Lo contesta la IA (criterio en el prompt: no se deriva ni abre ticket) |
| Mensaje de prueba, "sí"/"no" sueltos, mensajes sin contenido | La IA contesta o pregunta qué necesita (ACLARA) |
| "¿Novedades?" por frases | Tipo `SEGUIMIENTO` que devuelve la IA |
| "Ya se arregló" por frases | Campo `tema_resuelto` del JSON; la respuesta al cliente la redacta la IA |
| "¿Tu pedido o tu consulta?": elección por palabras y reescritura del mensaje | La IA lee la conversación; sólo se evita repetir la pregunta |
| Sí/no a "¿escribís por tal cuenta?" por listas | Herramienta `confirmar_identidad` (sólo si ya se le preguntó) |
| Confirmar/cancelar pedido o alta de cliente por listas | Herramientas `confirmar_pedido` / `confirmar_alta_cliente` (sólo con borrador pendiente) |
| Ticket sólo si el mensaje "parece consulta" | Lo decide `abrir_ticket` de la IA |
| DERIVA sin nombrar al equipo y con "?" → ACLARA | Criterio en el prompt |

Quedan, porque no interpretan al cliente sino que cuidan qué se graba o se muestra: un pedido o alta se graba
sólo si existe el borrador y el cliente recibe el texto que devuelve la grabación; sin identidad confirmada no
se dan datos privados; tope de créditos; nunca dos aclaraciones seguidas; filtros sobre lo que escribe la IA
(no pedir que contacte a otro, no afirmar que algo no se vende). Las palabras de escalado y de urgencia
siguen siendo configuración de cada empresa. `EsMensajeSocial` queda sólo para no buscar en manuales y
AlfaKnowledge ante un saludo (no decide la respuesta).

### Prueba con charlas reales de ALFANET (2026-11-25/26)

Se repitieron contra la base de pruebas (copia local, IA real, configuración del asistente de ALFANET copiada
por un rato) 550 tramos de charlas reales del último mes: 1077 intervenciones del cliente con 339 fotos y
audios, hasta la primera respuesta de una persona del equipo. Salieron dos fallas, corregidas:

- **Fotos y audios de una tanda.** "[foto] / me sale este cartel" se contestaba "¿qué mensaje te aparece?":
  la IA sólo veía el adjunto del último mensaje, y si el último era una foto se perdía el texto anterior.
  Ahora lo pendiente va completo y en orden (`UnirMensajesSinResponder`), la IA ve hasta 4 fotos de la
  tanda (`CargarImagenesSinResponderAsync`) y cada "[audio]" se cambia por su transcripción
  (`ReemplazarAudios`).
- **Bot mudo después de derivar.** Con el tema ya en manos del equipo el bot calla a propósito, pero sin
  respuesta no quedaba marca de lo atendido: cada mensaje nuevo se juntaba con la consulta vieja, la IA
  volvía a derivar y no contestaba más, ni un "gracias". Ahora lo pendiente arranca después del último
  mensaje que el bot ya atendió, contestara o no (`MarcarEntranteAtendidoBot`, en memoria del proceso).
- **Tickets con títulos sueltos** ("4", "Ok", "En bariloche"): el título era el último mensaje. Ahora la IA
  devuelve `resumen_caso` (de qué se trata, con toda la charla) y es el título del ticket
  (`TituloTicketBot`); si viene vacío, queda el mensaje como antes.

### Después de derivar y tickets (2026-11-26, pedido de ALFA NET)

- **Si alguien del equipo tomó la charla, el bot se apaga.** Una respuesta de una persona o una asignación
  hecha por una persona (las automáticas de reglas, SLA y el bot se graban "AlfaCore" y no cuentan) en las
  últimas `CONV_BOT_PAUSA_TECNICO_HORAS` (24 por defecto; 0 = sólo calla ante el mensaje que ya contestó la
  persona), sin cierre ni reactivación después, hace que el bot no responda (`GetEstadoAsistenteCoreAsync`,
  evento `BotPausadoPorTecnico`). Se configura en Asistente IA → Límites.
- **Pausar / reactivar a mano.** Botón "Asistente activo / Asistente pausado" en el encabezado de la charla
  (en celular, en el menú ⋮). Deja el evento "X pausó / reactivó el asistente en esta conversación"; la pausa
  dura hasta reactivarlo o cerrar la conversación. Reactivar también le devuelve la charla al bot aunque el
  equipo la haya atendido (`SetAsistentePausadoAsync`, `GetEstadoAsistenteAsync`).
- **En horario primero el equipo.** `CONV_BOT_ESPERA_MINUTOS` ahora vale sólo dentro del horario de atención
  (sin horario cargado, siempre): el bot espera y contesta si nadie lo tomó. Fuera de horario, enseguida.
- **Qué lee.** Desde el último cierre y como mucho `CONV_BOT_CONTEXTO_HORAS` (48 por defecto; 0 = sin límite).
- **Modelo por base.** `CONV_ASISTENTE_MODELO` (Asistente IA → General → Modelo de IA): vacío = el del servidor
  (`OPENAI_MODEL` o gpt-4o-mini), o gpt-4o-mini / gpt-4.1-mini / gpt-4.1. Sólo modelos de esa lista, porque cada
  uno necesita su precio en `ALFA_CENTRAL.IA_PRECIO_MODELO` para contar en el tope de créditos; `CentralIaSchema`
  carga los de gpt-4.1 y gpt-4.1-mini (precio de lista de OpenAI, verificar) y se vuelve a aplicar si faltan.

**Cómo lo ve el cliente (2026-11-26):** dos opciones, "Estándar" (gpt-6-luna, recomendado, ≈ 0,4 créditos por
respuesta) y "Avanzado" (gpt-6.1-terra, ≈ 3,3). Sin elegir, el asistente usa el Estándar
(`AsistenteModeloPorDefecto`; `OPENAI_MODEL` ya no cambia el modelo del asistente, sí el de las otras funciones
de IA). Pasar a un modelo que consume más pide confirmación (`AlfaConfirmDialog` con `Tone=Primary`) avisando
que se descuenta del plan y que el excedente se factura al cierre del mes.

#### Comparación de modelos (2026-11-26)

Mismas 6 charlas (impresora con datos sumados, pedido de desarrollo, "2,3y4", calculadora, foto de un error,
charla social) con el bot completo en la copia local, 20 respuestas por modelo. Costo medido por respuesta
(1 = gpt-4o-mini) y precios de lista de https://developers.openai.com/api/docs/pricing:

| Modelo | Costo | Resultado |
|---|---|---|
| gpt-6-luna | 0,4 | El mejor: entiende datos sueltos, lee bien fotos, suma al caso, corto. **Recomendado.** |
| gpt-6.1-terra | 3,3 | Igual de bien, más seco. Avanzado. |
| gpt-4o-mini | 1 | Genérico; "2,3y4" lo tomó como módulos. Anterior. |
| gpt-4.1 | 8,9 | Bien, pero se ofrece a "revisarlo con vos". Fuera de la lista. |
| gpt-5-nano / gpt-5-mini | 0,2 / 1,2 | Respuestas largas con listas de preguntas; gpt-5-mini se ofrece a calcular. Fuera. |
| gpt-5.4-nano | 0,8 | Al saludo responde con la lista de módulos. Fuera. |
| gpt-5.6-luna | 0,8 | Bien, pero una respuesta salió con una palabra en cirílico. Fuera. |

Los modelos que "piensan" (gpt-5, gpt-5-mini/nano, gpt-5.6, gpt-6) necesitan `reasoning_effort`
(`EsfuerzoRazonamiento`): sin él tardaban 10-13 s y gastaban hasta 1.800 tokens de más, y gpt-5.6 / gpt-6
rechazan las herramientas en chat/completions.
- **Si derivó y todavía no contestó nadie, responde según lo nuevo.** Antes callaba todo. Ahora la IA recibe
  que el caso ya está con el equipo (sección "CASO YA PASADO AL EQUIPO" del prompt) y contesta: confirma que
  suma el dato al caso, agradece, resuelve lo que puede o deriva un tema distinto. Sólo calla si la IA no
  respondió o si repite tal cual una contención ya mandada hoy.
- **Ticket sólo para trabajo a fondo.** La IA decide `abrir_ticket` con el criterio de
  `CONV_BOT_TICKET_CRITERIO` (Asistente IA → Si no encuentra respuesta; vacío =
  `ConversacionAsistenteService.CriterioTicketPorDefecto`). Si duda, o si no informa el campo, no se abre.
- **Menos textos fijos.** La mitad de las respuestas eran frases fijas o la frase de ejemplo del prompt copiada
  tal cual. Ahora: la contención nombra de qué se trata; la IA recibe el "ESTADO DEL CASO" (ticket abierto,
  alguien del equipo lo venía atendiendo, o nada pendiente, y si su último mensaje ya fue una pregunta) y
  redacta ella la respuesta al "¿novedades?" (`ResponderSeguimientoAsync` usa su texto). Se permiten hasta
  dos preguntas seguidas (`MaxAclaracionesSeguidas`, `ContarAclaracionesSeguidas`); recién en la tercera
  sale la contención fija.

Para leer los resultados: cada tramo arranca como charla nueva del chat web, sin el historial ni el ticket
abierto que tenía el cliente real. Por eso hay más tickets nuevos que en la realidad (en 166 de 219 casos el
cliente ya tenía uno abierto) y algunos "¿a qué consulta te referís?". El reconocimiento por celular,
videos y documentos no se pueden repetir por el chat web.

## Issues pendientes

### Issue 8. Completar flujo de envío WhatsApp

- Confirmar credenciales Meta reales en producción
- Validar formato final del payload contra API real
- Manejar respuestas y errores de Meta con reintentos
- Persistir `whatsapp_message_id` en respuestas exitosas

### Issue 9. Completar flujo de webhook

- Validar payload real recibido desde Meta con firma HMAC (`App Secret`)
- Confirmar parseo de mensajes entrantes en todos los tipos (imagen, audio, documento)
- Descargar y guardar en `CONV_ADJUNTOS` los adjuntos que envíe el cliente

### Issue 11. Tiempo real con SignalR

- Evaluar incorporación de SignalR
- Actualizar inbox y chat sin refresh manual
- Notificar cambios de asignación y estado en tiempo real

### Issue 12. Pruebas manuales y de integración

- Alta de conversación entrante por webhook real
- Respuesta saliente contra Meta
- Envío y recepción de adjuntos por WhatsApp
- Chat interno entre dos técnicos con adjunto y audio
- Cambio de técnico y estado
- Verificar registro correcto en `AUX_ERR` ante fallos

### Issue 13. Etiquetas

- UI para alta y asignación de etiquetas desde el panel de contexto
- Filtro por etiqueta en el inbox
- La estructura de tablas (`CONV_ETIQUETAS`, `CONV_CONVERSACION_ETIQUETAS`) ya existe

### Issue 14. Mejoras de visualización de adjuntos

- Preview de video inline
- Lightbox para imágenes
- Indicador de progreso durante la subida de archivos grandes

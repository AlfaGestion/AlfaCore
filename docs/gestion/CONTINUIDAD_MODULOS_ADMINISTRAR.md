# Continuidad — AlfaCore modular (catálogo de módulos + panel Administrar)

## Objetivo de este archivo

Resumir el estado de un análisis de arquitectura (todavía sin código escrito) para poder
retomarlo desde otra PC o en una nueva conversación, sin reconstruir todo el razonamiento.

Uso sugerido al retomar:

```text
Leé docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md y continuemos desde ahí.
```

Este archivo también está enlazado desde `docs/gestion/CONTINUIDAD_CODEX.md` (el continuidad
general del repo).

---

## Actualización 2026-08-06: corrección de estado + cola de aprobación de solicitudes

**Corrección importante**: al retomar este tema se verificó directo contra `ALFA_CENTRAL` (en vez
de confiar en este documento) y varias cosas anotadas como "pendiente" ya estaban resueltas:

- Las 3 migraciones que quedaban pendientes (`modulos_modelo_inicial.sql`,
  `modulos_menukeyraiz_nullable.sql`, `webhook_token_bases.sql`) **ya están corridas** en
  `ALFA_CENTRAL` — confirmado con `sys.tables`/`sys.columns`.
- El catálogo tiene 7 módulos cargados: `CLIENTES`, `TECNICOS`, `CONVERSACIONES`, `TICKETS`,
  `ALFAKNOWLEDGE`, `PARTES_HORAS`, `AUTOMATIZACIONES`.
- El cliente de prueba (112012786) ya tiene activos `CLIENTES`/`TECNICOS`/`CONVERSACIONES`/
  `TICKETS`, confirmado en vivo (filtro de menú, Autorización de tareas, botón "Crear ticket").
- Siguen **sin activar/probar en vivo** para el cliente de prueba: `ALFAKNOWLEDGE`,
  `PARTES_HORAS`, `AUTOMATIZACIONES` — están en el catálogo pero nadie los prendió todavía.
  Sigue siendo el próximo paso natural si se retoma la prueba en vivo.

**Nuevo: cola de solicitud/aprobación de módulos** (resuelve el punto 2 de "Próximos pasos" de
más abajo). Diseño confirmado con el dueño del producto:

- 3 preguntas de diseño cerradas: (1) la solicitud se carga desde el panel Cliente → Módulos que
  ya existía (botón nuevo "Solicitar", no un formulario aparte); (2) "Aprobar" activa directo, sin
  paso intermedio de "aprobado pendiente de pago" (se descartó el 4to estado del boceto original
  por simplicidad); (3) rechazar deja la fila marcada como `Rechazado` (no se borra, keep para
  historial).
- `dbo.ClienteModulos.Estado` pasa de 2 a 4 valores posibles: `Solicitado` / `Activo` /
  `Suspendido` / `Rechazado` — migración nueva
  `docs/base-datos/sql-referencia/modulos_estado_solicitud.sql` (ALTER TABLE, **pendiente de
  correr manualmente contra `ALFA_CENTRAL`**, bloqueado para mí por el clasificador). Suma
  columnas `SolicitadoUtc`/`SolicitadoPor`/`DecididoUtc`/`DecididoPor`.
- `ICentralAdminService`: `SolicitarModuloAsync` (dejar pedido sin activar), `RechazarModuloAsync`
  (marca `Rechazado`), `GetSolicitudesPendientesAsync` (cola cruzando todos los clientes). Reusa
  `ActivarModuloAsync` tal cual para "Aprobar" — mismo efecto que activar directo (cascada de
  dependencias incluida), no hizo falta un método nuevo.
- `AdminHome.razor` (panel Cliente → Módulos): columna Estado ahora muestra
  Solicitado/Activo/Suspendido/Rechazado/Sin contratar en vez de solo Activo/Inactivo; botones
  nuevos "Solicitar" (cuando no hay nada pendiente) y "Rechazar" (cuando está Solicitado), más un
  botón "Solicitudes pendientes" en el header que lleva a la pantalla nueva.
- `src/AlfaCore/Components/Pages/AdminSolicitudesModulos.razor` (nuevo, `/admin/solicitudes`):
  cola cruzando todos los clientes con quién/cuándo pidió cada módulo, botones Aprobar/Rechazar.
  Mismo gate `superadmin=1` que el resto de Administrar.

Compiló limpio y pasó `check_catalogo.py`. **Pendiente**: correr
`modulos_estado_solicitud.sql` contra `ALFA_CENTRAL`, y probar en vivo el flujo completo
(Solicitar → aparece en la cola → Aprobar → queda Activo) con el cliente de prueba.

---

## Actualización 2026-08-05 (2): primera versión construida — catálogo de módulos + activación

Se construyó y compiló (0 errores/advertencias) una primera versión funcional del sistema de
módulos, siguiendo el diseño de las secciones de abajo. Todavía no probada en vivo (falta correr
la migración SQL en `ALFA_CENTRAL` de producción).

**Confirmado en esta sesión**: `ALFACORE_MENU_WEB` es igual en todas las bases de clientes —
resuelve el punto de verificación que quedaba pendiente y habilita con seguridad el diseño de
catálogo central de módulos referenciando `MenuKey`.

**Qué se construyó:**

- `docs/base-datos/sql-referencia/modulos_modelo_inicial.sql` (nuevo, ejecutar manualmente
  contra `ALFA_CENTRAL`): tablas `dbo.Modulos`, `dbo.ModulosDependencias`, `dbo.ClienteModulos`,
  y columna `dbo.Clientes.EsClienteLegacy` (con backfill automático a `1` para todos los
  clientes existentes en el mismo script).
- `ICentralAdminService`/`CentralAdminService` extendido (no se creó un servicio nuevo — se
  reusó el mismo que ya consumen las páginas `/admin/*`, según el patrón ya establecido en el
  repo): catálogo de módulos (alta/edición/baja lógica), dependencias en cascada al activar
  (`ResolveDependenciasTransitivas`), y consulta del estado de módulos por cliente
  (`GetClienteModulosAsync`, que ya tiene en cuenta `EsClienteLegacy`).
- `src/AlfaCore/Components/Pages/AdminModulos.razor` (nuevo, `/admin/modulos`): el "armador de
  módulos" — elegir código/nombre/precio, el nodo de menú raíz (combo poblado desde
  `ALFACORE_MENU_WEB` de la base por defecto) y tildar de qué otros módulos depende.
- Panel nuevo "Módulos" en `AdminHome.razor` (junto a Usuarios/Bases del cliente seleccionado):
  lista los módulos del catálogo con su estado para ese cliente puntual, con botón
  Activar/Suspender. Sin autoservicio, como se decidió: lo carga un superadmin de Alfa.

**Pendiente para que funcione en producción:**

1. Ejecutar `docs/base-datos/sql-referencia/modulos_modelo_inicial.sql` contra `ALFA_CENTRAL`
   (mismo mecanismo manual que `webhook_token_bases.sql`).
2. No probado en vivo todavía — falta cargar el primer módulo real (Conversaciones) desde
   `/admin/modulos` y activarlo para un cliente de prueba.

---

## Actualización 2026-08-05 (3): dependencias opcionales + ocultar "Crear ticket"

Se resolvió el punto 3 de arriba (ya **no** queda pendiente): se sumó una distinción entre
dependencias **obligatorias** (se activan gratis en cascada, como Clientes/Técnicos) y
**opcionales** (solo un enganche informativo — no se activan solas, y la pantalla que usa esa
función la oculta si el cliente no la tiene activada aparte).

**Cambios:**

- `ModulosDependencias.EsObligatoria` (bit, default 1) — nueva columna, ya incluida en
  `modulos_modelo_inicial.sql` (todavía no corrido en producción, así que se editó el script
  original en vez de sumar uno nuevo).
- `ICentralAdminService.IsModuloActivoParaClienteActualAsync(codigoModulo)`: chequeo para
  pantallas normales (no solo Administrar) — resuelve para el cliente del usuario logueado.
  **Fail-open siempre** (devuelve `true`) en modo legacy/on-premise, cliente legacy, módulo
  todavía no definido en el catálogo, o ante cualquier error — nunca oculta una función por un
  problema de infraestructura o porque todavía no se configuró el módulo.
- `/admin/modulos`: al tildar una dependencia, aparece un combo para elegir
  Obligatoria/Opcional.
- `Conversaciones.razor`: los 3 botones de "Crear ticket" (desktop, barra móvil, menú móvil)
  ahora están detrás de `_ticketsModuloActivo`, cargado una vez con
  `IsModuloActivoParaClienteActualAsync("TICKETS")`. Partes de horas no tiene un botón propio
  — se genera como efecto secundario de crear el ticket, así que ocultar "Crear ticket" ya lo
  cubre; no se tocó el panel de "Ver tickets relacionados" del contexto (es una búsqueda de
  tickets existentes, no la creación).
- **Importante para cuando se cargue el módulo Tickets real**: el código que espera Conversaciones
  es literalmente `TICKETS` (mayúsculas) — hay que crear el módulo en `/admin/modulos` con ese
  código exacto para que el chequeo lo encuentre.

Compiló limpio y pasó `check_catalogo.py`. No probado en vivo (necesita el módulo `TICKETS`
real cargado + un cliente sin activarlo, para confirmar que el botón efectivamente desaparece).

---

## Actualización 2026-08-05 (4): módulo AlfaKnowledge + aprovisionamiento automático al activar

Se armó el módulo `ALFAKNOWLEDGE` (dependencia **Opcional** de Conversaciones, USD 10/mes — igual
patrón que Tickets) y, más importante, se conectó `ActivarModuloAsync` para que **al activarlo
para un cliente se cree su base de conocimiento sola**, en vez de cargarla a mano como se hacía
hasta ahora.

**Hallazgo clave (antes de tocar código)**: en AlfaKnowledge ya existía un servicio de
aprovisionamiento real y funcionando (`KnowledgeBaseProvisioningService.ProvisionAsync`) — crea
la base SQL Server de cero (`CREATE DATABASE` + corre los scripts de esquema) y registra la
colección de Qdrant (la base vectorial, corre en Linux, es lo que el dueño no recordaba). Lo único
que faltaba era que alguien lo llamara automáticamente: el endpoint existente
(`POST /api/knowledge-bases`) exige sesión de admin humano en AlfaKnowledge, no servía para un
llamado servidor-a-servidor desde AlfaCore.

**Diseño:**

- AlfaKnowledge: nuevo endpoint `POST /api/external/knowledge-bases` (mismo patrón que
  `ExternalSuggestionsController` — protegido con el `X-Api-Key` global de
  `ALFAKNOWLEDGE_EXTERNAL_API_KEY`, no cookie de admin). Excluido del
  `KnowledgeBaseResolutionMiddleware` (crea una base nueva, no opera sobre una existente).
- AlfaCore: los módulos ahora pueden no tener nodo de menú propio (`MenuKeyRaiz` nullable) — hacía
  falta porque AlfaKnowledge no es una sección navegable, es un panel dentro de Conversaciones.
- `CentralAdminService.ActivarModuloAsync`: si el módulo activado directamente (no arrastrado como
  dependencia) tiene código `ALFAKNOWLEDGE`, después de confirmar la activación:
  1. Resuelve la conexión del cliente **directo desde `ALFA_CENTRAL.dbo.bases`** (no desde la
     sesión activa del admin — el admin puede estar parado en la base de otro cliente cuando
     activa el módulo, ver decisión 9 de este mismo documento).
  2. Si ya tiene `CONV_ALFAKNOWLEDGE_KNOWLEDGE_BASE_ID` cargado, no hace nada (evita reprovisionar
     si ya estaba configurado a mano o de una corrida anterior).
  3. Llama a `POST /api/external/knowledge-bases` en AlfaKnowledge con un `DatabaseName` derivado
     del `IdCliente` (`AK_<idcliente>`, saneado a alfanumérico).
  4. Guarda el `KnowledgeBaseId` devuelto (+ el `BaseUrl`/`ApiKey` globales) directo en el
     `TA_CONFIGURACION` del cliente, sin pasar por la sesión activa
     (`IConversacionesConfigService.SaveAlfaKnowledgeConfigForConnectionAsync`, overload nuevo).
  - Si algo falla (AlfaKnowledge no responde, falta configurar `AlfaKnowledge:BaseUrl`/
    `ExternalApiKey`, el cliente no tiene base de ERP todavía), el módulo **queda activo igual**
    (ya se confirmó/commiteó antes de intentar aprovisionar) pero se tira un
    `AppUserFacingException` explicando qué faltó — el admin lo ve en la pantalla y puede
    reintentar (volver a apretar "Activar" es idempotente: si ya está configurado, no reprovisiona
    de nuevo; si no, reintenta el aprovisionamiento).

**Archivos tocados:**

- AlfaKnowledge: `AlfaKnowledge.Web/Controllers/ExternalKnowledgeBasesController.cs` (nuevo),
  `AlfaKnowledge.Web/Middleware/KnowledgeBaseResolutionMiddleware.cs` (exclusión de ruta).
- AlfaCore: `docs/base-datos/sql-referencia/modulos_menukeyraiz_nullable.sql` (nuevo — ejecutar
  manualmente contra `ALFA_CENTRAL`, igual que los anteriores),
  `Services/CentralAdminService.cs` (hook de aprovisionamiento + validación de `MenuKeyRaiz`
  relajada), `Services/ConversacionesConfigService.cs` /
  `Services/IConversacionesConfigService.cs` (`SaveAlfaKnowledgeConfigForConnectionAsync`),
  `Components/Pages/AdminModulos.razor` (nodo de menú opcional en el formulario y en el listado),
  `appsettings.json` (sección `AlfaKnowledge:BaseUrl`/`ExternalApiKey` nueva).

**Pendiente para que funcione en producción:**

1. Ejecutar `docs/base-datos/sql-referencia/modulos_menukeyraiz_nullable.sql` contra
   `ALFA_CENTRAL` (bloqueado para mí por el clasificador de seguridad al ser un `ALTER TABLE` —
   los `INSERT`/`UPDATE` de datos sí los pude correr yo mismo).
2. Completar `AlfaKnowledge:ExternalApiKey` en `appsettings.json` con el mismo valor que ya usa
   `CONV_ALFAKNOWLEDGE_API_KEY` en el cliente actual (no lo cargué yo — es un secreto, y además el
   clasificador bloqueó imprimirlo). `AlfaKnowledge:BaseUrl` ya quedó con el valor real
   (`http://10.8.0.32:5000`, no es sensible).
3. El módulo `ALFAKNOWLEDGE` ya está en el catálogo (`Precio` USD 10/mes, dependencia opcional de
   `CONVERSACIONES`), cargado directo por SQL con `MenuKeyRaiz = ''` (todavía no se pudo dejar en
   `NULL` porque falta el punto 1). Una vez corrida esa migración no hace falta tocar esta fila —
   `''` y `NULL` se tratan igual en todos lados (`string.IsNullOrWhiteSpace`).
4. No probado en vivo — falta activar `ALFAKNOWLEDGE` para un cliente de prueba desde
   `/admin/modulos` → panel de módulos del cliente, y confirmar que la base se crea sola y que
   `ConversacionesConfiguracion` muestra la conexión cargada.
5. **Nota de producto, sin resolver todavía**: el precio de $150/mes de Conversaciones se pensó
   con un tope de 5 usuarios/agentes incluidos, y el de Tickets/AlfaKnowledge con algo de
   variación por cantidad de agentes — hoy el modelo de datos (`Modulos.Precio`) es un precio
   plano por módulo, **no hay ningún control de cantidad de asientos/agentes todavía**. No se
   construyó nada de eso en esta pasada (fuera de alcance de "activar/desactivar el módulo") — si
   se necesita hacer cumplir el límite de asientos, es una pieza aparte a diseñar.

---

## Actualización 2026-08-05 (5): el menú lateral ahora también se filtra por módulo

Hasta acá el sistema de módulos solo gateaba UI puntual (el botón "Crear ticket"). El menú
lateral (`MenuService.cs`) seguía armándose solo por permisos de tarea (`ALFACORE_TAREAS_WEB`),
sin ninguna noción de módulos — brecha que ya estaba anotada desde el análisis inicial. Se probó
en vivo con el cliente de prueba (AW_112012786, ya no legacy) y efectivamente seguía viendo el
menú completo (Ventas, Compras, Stock, Punto de venta, etc.) a pesar de tener activados solo
Clientes/Técnicos/Conversaciones — confirmó la brecha.

**Decisión de producto tomada para esto**: a diferencia de `IsModuloActivoParaClienteActualAsync`
(que es fail-open — un módulo sin definir no oculta nada), acá se eligió lo **contrario**: una
sección del menú que no tiene NINGÚN módulo asociado en el catálogo también se oculta. Es decir,
el menú de un cliente no-legacy muestra únicamente lo que cae bajo un módulo que tiene activo —
todo lo no modularizado todavía (Ventas, Compras, Stock, Gestión Contable, Utilidades, Informes,
Punto de venta, Logística...) desaparece del todo hasta que se module.

**Cómo funciona:**

- `ICentralAdminService.GetModuloMenuFiltroParaClienteActualAsync()` (nuevo): devuelve `null` si
  no corresponde filtrar (on-premise, o cliente `EsClienteLegacy=1` — el cliente real de hoy
  sigue viendo todo, sin cambios). Si corresponde filtrar, devuelve dos sets de `MenuKeyRaiz`:
  todos los definidos en el catálogo, y de esos cuáles están activos para el cliente.
- `MenuService.LoadVisibleMenuAsync`: antes de aplicar el filtro de permisos de tarea (que ya
  existía), filtra los nodos candidatos (`mapped`) con `IsNodeAllowedByModulos` — camina la
  cadena de ancestros de cada nodo; si él o algún ancestro coincide con un `MenuKeyRaiz`
  definido, se muestra solo si ese módulo está activo; si nadie en la cadena coincide con nada
  del catálogo, se oculta.
- Los contenedores intermedios (ej. "CRM", "Archivos") no necesitan su propio `MenuKeyRaiz` — se
  siguen mostrando solos porque el mecanismo existente de `AddAncestors` los agrega en cuanto
  algún hijo suyo queda visible. Si ningún hijo de una sección queda visible, la sección entera
  desaparece del todo (ej.: si algún día "Ventas" no tiene ningún módulo activo debajo, el ícono
  "Ventas" del menú directamente no aparece).
- **"Administrar" queda exento a propósito** — se agrega aparte, solo por `superadmin=1`, después
  de este filtro. Si no fuera así, un superadmin de un cliente no-legacy sin módulos que cubran
  Administrar podría quedarse sin forma de entrar a `/admin/modulos` a activarse módulos a sí
  mismo — riesgo de lockout que había que evitar.

**Archivos tocados:** `Services/CentralAdminService.cs` / `Services/ICentralAdminService.cs`
(`GetModuloMenuFiltroParaClienteActualAsync`), `Models/ModulosModels.cs` (`ModuloMenuFiltroDto`),
`Services/MenuService.cs` (inyecta `ICentralAdminService`, aplica el filtro, nuevo
`IsNodeAllowedByModulos`).

Compiló limpio y pasó `check_catalogo.py`. **No probado todavía en el navegador** — falta
recargar la sesión del cliente de prueba y confirmar que el menú efectivamente se reduce a
Clientes/Técnicos/Conversaciones (+ Administrar, por ser superadmin).

**Corrección posterior, encontrada al probar en vivo**: el filtro no aplicaba nunca — el log
(agregado como diagnóstico, `LogAuditAsync("ModuloMenuFiltroCheck", ...)`) mostró que se estaba
resolviendo `IdCliente = 112010001` (la cuenta central propia con la que se logueó `albert`, que
además es legacy) en vez de `112012786` (el cliente dueño de la base AW_112012786 que en
realidad se estaba mirando). Es decir: `appUserSession.CurrentUser.IdCliente` es "quién soy yo",
no "de qué cliente son los datos que estoy mirando ahora" — para un superadmin parado en la base
de otro cliente (el caso normal en Administrar) son cosas distintas. Mismo error conceptual que
ya se había resuelto para el aprovisionamiento de AlfaKnowledge, pero se filtró acá también.

Se corrigió con `CentralAdminService.ResolveIdClienteDeBaseActivaAsync` — resuelve el `IdCliente`
a partir de `sessionService.GetActiveSession().BaseId` contra `ALFA_CENTRAL.dbo.bases`, con fallback
a `appUserSession.CurrentUser.IdCliente` si todavía no hay base activa. Se aplicó tanto en
`GetModuloMenuFiltroParaClienteActualAsync` como en `IsModuloActivoParaClienteActualAsync` (el
chequeo del botón "Crear ticket" tenía el mismo bug latente, nunca se había detectado porque
tampoco se probó en vivo todavía). Compiló limpio, pendiente de reprobar en el navegador.

**Ajuste 2026-08-06: "Autorización de tareas" también respeta los módulos contratados.**

Esa pantalla (`AutorizacionTareas.razor` → `AutorizacionTareasService`) arma su árbol leyendo
`ALFACORE_MENU_WEB` directamente (con `Proceso` siempre vacío en esa consulta, o sea que ahí
todo es web — no hay que preocuparse por nodos de escritorio mezclados). Se aplicó el mismo
filtro:

- `ModuloMenuFiltroDto.PermiteClave(clave, parentByKey)` — se subió acá la lógica que antes
  vivía adentro de `MenuService` (incluyendo `NucleoSiempreVisible`), para que el menú y la
  autorización de tareas usen exactamente el mismo criterio y no queden inconsistentes entre sí.
- `GetAutorizacionAsync`: filtra las filas antes de armar el árbol — un usuario no-legacy ya no
  puede ni ver, y por lo tanto ni autorizar, una opción que el cliente no tiene contratada.
- `GuardarAutorizacionAsync`: **hallazgo importante antes de tocar esto** — el guardado borra y
  rearma los permisos de *todas* las claves válidas del menú (`managedKeys`) para ese usuario. Si
  solo filtraba la pantalla y no el guardado, cualquier permiso previo sobre una sección no
  contratada se hubiera borrado solo al guardar cualquier otra cosa. Se filtró `managedKeys` con
  el mismo criterio, así el guardado nunca toca lo no contratado — queda preservado por si el
  cliente lo contrata más adelante.

Compiló limpio y pasó `check_catalogo.py`. No probado en vivo todavía.

**Corrección 2026-08-06: Partes de horas pasa a ser módulo propio, no parte de Tickets.**

El plan original (`2026-08-05-001`) había puesto Partes de horas (`D010186`) colgando de Tickets
para que quedara cubierta por ese módulo sin crear uno nuevo. Al usarlo en vivo se vio el
problema: entrando directo a Tickets no daba opción de elegir Partes de horas por separado, y
además a futuro se va a usar también desde Órdenes de trabajo/Producción — no tiene sentido
atarla solo a Tickets.

- `App_Data/updates/2026-08-06-002__partes_horas_menu_principal.sql` (nuevo — revierte
  `PadreClave` de `D010186` a `D`, vuelve a ser nodo de primer nivel).
- Módulo nuevo `PARTES_HORAS` en el catálogo (USD 10/mes, `MenuKeyRaiz = D010186`), dependencia
  **Opcional** de `CONVERSACIONES` — mismo patrón que `TICKETS`/`ALFAKNOWLEDGE`.
- Cargado directo por SQL en `ALFA_CENTRAL` (dato, no requiere el clasificador de seguridad).
  Pendiente: correr el script de menú de arriba en cada base (por ahora solo en la de prueba).

No probado en vivo todavía — falta confirmar que "Partes de horas" aparece como opción propia en
el menú principal, y que se oculta/muestra junto con el módulo `PARTES_HORAS` de forma
independiente de `TICKETS`.

**Nuevo 2026-08-06: módulo `AUTOMATIZACIONES` — respuestas automáticas "Nivel 0" (sin IA).**

Primer paso de un plan más amplio de auto-respuestas (Nivel 0 reglas fijas → Nivel 1 sugerencia
IA con aprobación, ya existente → Nivel 2 IA auto-envía para intents chicos, todavía no
construido). Se armó el Nivel 0: respuesta fija cuando llega un WhatsApp fuera del horario de
atención configurado.

- Módulo `AUTOMATIZACIONES` en el catálogo (USD 10/mes, opcional de `CONVERSACIONES`, sin
  `MenuKeyRaiz` — vive como pestaña de configuración, no como sección de menú propia, mismo
  criterio que `ALFAKNOWLEDGE`).
- `ConversacionAutomatizacionesConfigDto` (`Models/ConversacionesConfiguracionModels.cs`):
  activo/inactivo, mensaje, días de atención (Lun-Dom), horario desde/hasta. Persistido en
  `TA_CONFIGURACION` (`CONV_AUTOMATIZACIONES_*`), mismo patrón que WhatsApp/AlfaKnowledge.
  Nueva pestaña en `ConversacionesConfiguracion.razor`.
- `ConversacionesService.TryAutoReplyOutOfHoursAsync`: se dispara al final de
  `RegisterIncomingWebhookAsync` para cada mensaje de WhatsApp entrante real (no reacciones). Solo
  manda si: el módulo está contratado (`IsModuloActivoParaClienteActualAsync`, fail-open), la
  config tiene `Activo=true` y mensaje cargado, y el horario actual cae fuera de los días/horas
  configurados. Anti-spam simple: no repite mientras el último mensaje saliente de la conversación
  ya sea esta misma auto-respuesta (`SistemaAutor = 'AUTOMATIZACION'`) — vuelve a poder mandarla
  si un humano contesta en el medio. Nunca bloquea el procesamiento del webhook si falla (try/catch
  con log de warning).

Compiló limpio y pasó `check_catalogo.py`. No probado en vivo — falta activar el módulo para un
cliente de prueba, cargar horario+mensaje en Configuración → Automatizaciones, y mandar un
WhatsApp de prueba fuera de esa ventana.

**Pendiente, fuera de alcance de esta pasada:** Nivel 2 (la IA auto-envía sola para preguntas
frecuentes de alta confianza) — se decidió no construirlo todavía, es un paso separado y más
grande que requiere pensar umbral de confianza, límite de repeticiones y opt-out por cliente.

**Ajustes después de confirmar que el filtro ya funciona en vivo:**

- `MenuService.NucleoSiempreVisible`: Usuarios (`D015001`), Autorización de tareas (`D015002`) y
  Actualizaciones (`D989003`) se muestran siempre, tengan o no módulo asociado — son funciones de
  base del sistema, no algo que se contrata por módulo (mismo criterio que ya se usaba para
  Administrar).
- `WorkspaceService.RefreshHomeAsync`/`RefreshModuleWorkspaceAsync`: Recientes y Favoritos ahora
  se filtran contra `menuService.GetSearchItemsAsync()` (el menú ya filtrado) antes de mostrarse —
  antes podían quedar apuntando a opciones ya no visibles (por permisos, y ahora también por
  módulos), mostrando accesos rotos.

---

## Estado (histórico): solo análisis, cero código

No se tocó ni un archivo de código para este tema. Todo lo de abajo es diseño conversado y
decisiones ya tomadas, pendiente de plan de implementación detallado y de arrancar a construir.

---

## Objetivo del proyecto

Punto de partida: la idea original era separar el módulo **Conversaciones** (inbox de
WhatsApp/Instagram/Facebook/MercadoLibre para soporte técnico) como producto independiente,
vendible por separado, con landing propia.

Al analizarlo, la decisión evolucionó hacia algo más general y más alineado con cómo el dueño
del producto ya piensa el sistema (por experiencia con VB6, donde tenía cientos de .exe
independientes que se combinaban en una base de datos siempre completa) y con el modelo de
**Odoo** (apps que se activan/desactivan sobre una plataforma común):

> Convertir **AlfaCore entero** en un ERP modular: base de datos siempre completa (todas las
> tablas existen siempre, aunque un cliente no use todas — "no molesta"), pero con
> **visibilidad y venta por módulo**. Conversaciones sería el primer módulo nuevo armado con
> este esquema, no un producto aparte.

---

## Decisiones ya tomadas (confirmadas en la conversación)

1. **Camino elegido: extender AlfaCore, no separar Conversaciones como producto aparte.**
   Se descartó explícitamente la idea de un repo/deploy independiente tipo AlfaKnowledge — no
   escala bien para cuando se sumen más módulos (Ventas, Compras, Logística...), cada uno
   necesitaría reconstruir toda la infraestructura de cero.

2. **La base de datos sigue siendo completa y única por cliente**, como ya funciona hoy
   (aprovisionamiento restaura el backup completo del ERP + corre todos los scripts). Esto
   **no es un problema a resolver**, es el diseño elegido a propósito. Simplifica mucho: no
   hace falta un template de base "liviano" por módulo, ni gestionar qué tablas existen en
   qué base.

3. **Qué es un "módulo"**: un nodo de **primer nivel del menú** + todo lo que cuelga de él,
   más una lista de **módulos de los que depende** (no tablas sueltas — otro módulo entero,
   ej. "Conversaciones" depende de "Clientes" y "Técnicos", que son módulos en sí mismos).
   Análogo directo a los `menu_restaurant.sql` que el dueño armaba en VB6 para agrupar
   opciones de menú en un módulo.

4. **Módulo "armador de módulos"**: hace falta una pantalla admin donde se pueda:
   - elegir el nodo de menú raíz (arrastra automáticamente todo lo que cuelga de él, sin
     tildar ítem por ítem);
   - tildar de qué otros módulos ya definidos depende;
   - cargar nombre, descripción, precio.

   Esto evita que cada módulo nuevo requiera tocar código — se define por configuración,
   igual que el `.sql` de menú en VB6 pero editable desde una pantalla.

5. **Dependencias compartidas entre módulos**: si dos módulos comparten una dependencia (ej.
   Conversaciones y Ventas comparten "Clientes"), la dependencia debería aparecer igual como
   módulo activo en la lista del cliente (aunque sea a precio incluido/$0), para que si el
   cliente compra el segundo módulo el sistema vea que la dependencia ya está activa y no la
   vuelva a activar/cobrar dos veces.

6. **Flujo de activación: solicitud → aprobación + pago manual → activo.** No es autoservicio
   instantáneo. El cliente pide un módulo (queda en estado "Solicitado"), alguien de Alfa
   revisa y confirma el pago, recién ahí pasa a "Activo". Pensado para arrancar manual; deja
   lugar para enganchar un pago automático (ej. Mercado Pago) más adelante sin cambiar el
   modelo.

7. **Migración de clientes existentes**: quedan con **todos los módulos habilitados** por
   defecto. No hace falta backfill fila por fila en una tabla de módulos activos — alcanza con
   un flag simple (ej. `EsClienteLegacy`) que la lógica de filtrado interprete como "no
   filtres nada, mostrá todo".

8. **Todo esto vive dentro del módulo "Administrar" existente**, gateado por
   `superadmin=1` (gate que ya existe hoy, ver `MenuService.cs:203-220`). Va a ser el panel de
   control central — exclusivo para el equipo de Alfa, no para clientes — donde se maneja:
   - alta/acceso de clientes externos y sus bases;
   - configuración del catálogo de módulos (el "armador de módulos" del punto 4);
   - aprobación de solicitudes de módulos + confirmación de pago.

9. **Acceso a Administrar sin elegir base**: hoy, en modo SaaS, el login obliga a elegir una
   base si el usuario tiene más de una (`CentralAuthService.RequiresBaseSelection`). Lo que
   maneja Administrar (catálogo de módulos, aprobaciones, clientes) vive en `ALFA_CENTRAL`, no
   pertenece a ninguna base de cliente en particular — entrar "a la base de un cliente
   cualquiera" solo para llegar al panel central es fricción rara.
   - **Decisión: arrancar simple.** Administrar cuelga de una base cualquiera, igual que hoy
     (Opción A). No se resuelve todavía el acceso "sin elegir base" (Opción B, una zona/sesión
     central separada) — queda como mejora futura si la fricción se vuelve un problema real de
     uso diario.

---

## Hallazgos de la investigación técnica (ya verificados en código)

Referencias archivo:línea del repo `AlfaCore` en el momento de esta investigación.

### Lo que ya sirve tal cual

- **Alta pública ya funciona en producción**: `/registrarme` (`Register.razor`) →
  verificación por email → `/verify/{code}` (`Verify.razor`). Servicio:
  `CentralRegistrationService.cs`. Confirmado productivo en
  `docs/arquitectura/INFRAESTRUCTURA_SERVIDORES.md:37`.
- **Aprovisionamiento automático de base por cliente**: `CentralProvisioningService.cs:40-139`
  — restore de backup completo + corrida de todos los scripts de `App_Data/updates/*.sql` +
  alta del login/usuario SQL + registro en `ALFA_CENTRAL.dbo.bases`. Se dispara solo al
  verificar el email (`CentralRegistrationService.VerifyAsync`, líneas 171-264).
- **Aislamiento real por cliente**: bases SQL físicamente separadas, con login propio por
  base (no un esquema compartido con filtro por `IdCliente`) — `ConexionClienteService.cs`,
  `CentralProvisioningService.cs:321-342`.
- **Menú dinámico** por usuario: `MenuService.cs` arma el árbol leyendo
  `dbo.ALFACORE_MENU_WEB` de la base activa del usuario, filtrado por
  `IPermissionService.GetAllowedTaskKeysAsync()` (permisos por tarea, tabla
  `ALFACORE_TAREAS_WEB`). Hoy es "todo o lo permitido explícitamente por tarea" — **no existe
  ningún filtro por "módulo contratado"**, es la capa que hay que sumar.
- **`superadmin=1` ya gatea el módulo Administrar**: `MenuService.cs:203-220`.

### Brechas encontradas

- **🔴 Brecha crítica, no relacionada con el sistema de módulos — resolver antes de vender a
  un segundo cliente real**: los endpoints de webhook de WhatsApp/Instagram/Facebook/
  MercadoLibre (`Program.cs:1041-1200`) son anónimos, sin sesión de usuario. La conexión SQL
  efectiva se resuelve igual que en el resto del sistema
  (`sessionService.GetConnectionString()`), que sin sesión activa cae al fallback
  `configuration.GetConnectionString("AlfaGestion")`. **Hoy, todos los webhooks entrantes de
  todos los tenants terminarían escribiendo en la misma base por defecto.** Hace falta
  resolver el tenant por el número de WhatsApp / Phone Number ID (u otro identificador) que
  llega en el payload del webhook, independiente de todo el tema de módulos.
- **No hay concepto de "módulos habilitados" en ningún lado del código actual** — ni en
  `ALFA_CENTRAL`, ni en la lógica de `MenuService`/`PermissionService`. Es la pieza central a
  construir.
- **El campo `type='M'` que se graba hoy en `ALFA_CENTRAL.dbo.clientes` al registrarse
  (`CentralRegistrationService.cs:26,405`) no lo lee ni lo usa nadie** — candidato a
  reutilizar como base de la clasificación de cliente en vez de sumar un campo nuevo.
- **No hay white-labeling / dominio propio por cliente** en AlfaCore (a diferencia de
  AlfaKnowledge, que sí tiene branding por base vía `KnowledgeBaseBranding`). Si en algún
  momento se quiere una landing con marca propia, hay que construirlo desde cero — pero con
  el camino elegido (Camino A) esto ya no es bloqueante: alcanza con una landing de marketing
  que apunte al mismo registro/selector de módulos.
- **Conversaciones tiene dependencias de esquema reales con otras partes del ERP**, no es un
  módulo aislado hoy:
  - `dbo.V_TA_Tecnicos` (tabla, no vista pese al nombre) — ligada a `TA_USUARIOS`
    (autenticación legacy).
  - `dbo.VT_CLIENTES` (clientes comerciales/CRM).
  - `dbo.MA_CONTACTOS`, `MA_CONTACTOS_CUENTAS`, `MA_CONTACTOS_ADIC` (Contactos).
  - `dbo.TICK_TICKETS` — única dependencia ya protegida con `IF OBJECT_ID(...) IS NOT NULL`
    antes de usarse.
  - Funcional: `ITicketsService` (crear tickets desde una conversación) e
    `IPartesHorasService` (partes de horas) inyectados directo en `Conversaciones.razor`.
  - Con el Camino A esto deja de ser un problema de "portar código" (la base sigue completa,
    esas tablas/servicios siguen ahí) — pero si Conversaciones se vende a un cliente que NO
    tiene el módulo Técnicos/Clientes/Tickets activo, hay que decidir qué pasa con esas
    funciones (¿se ocultan? ¿el módulo Conversaciones fuerza esas dependencias siempre
    activas, aunque sea a costo $0?).
  - Este punto está resuelto conceptualmente por la decisión 5 de la sección anterior
    (dependencias = módulos, se activan en cascada), pero falta decidir el detalle de qué
    pasa con las funciones de Tickets/Partes de horas si esos módulos específicos no están
    contratados.

---

## Modelo de datos propuesto (boceto — no implementado, no confirmado en detalle)

Todo en `ALFA_CENTRAL` (donde ya viven `dbo.Clientes`, `dbo.users`, `dbo.bases`):

```
dbo.Modulos
  Id, Codigo, Nombre, Descripcion, Precio, Activo

dbo.ModulosMenu            -- qué nodo(s) de menú raíz pertenecen a un módulo
  IdModulo, MenuKey         -- MenuKey = clave del nodo en ALFACORE_MENU_WEB

dbo.ModulosDependencias    -- de qué otros módulos depende un módulo
  IdModulo, IdModuloDependeDe

dbo.ClienteModulos         -- qué módulos tiene cada cliente y en qué estado
  IdCliente, IdModulo, Estado, FechaSolicitud, FechaAprobacion, FechaActivacion, AprobadoPor
  -- Estado: Solicitado / AprobadoPendientePago / Activo / Suspendido
```

Punto sin verificar todavía: si `ALFACORE_MENU_WEB` tiene **exactamente la misma estructura
de claves en todas las bases de clientes** (porque todas salen del mismo template de
aprovisionamiento) o si alguna base pudo haberse desviado con el tiempo. Si es siempre igual,
un catálogo central de módulos referenciando `MenuKey` funciona para todos los clientes por
igual sin excepciones. Si hay desvíos, hay que resolverlo antes de confiar en este modelo.

---

## Actualización 2026-08-05: resuelta la brecha de webhooks (punto 4 del análisis)

Se resolvió, con código, la brecha crítica de tenant-resolution en los webhooks de
WhatsApp/Instagram/Facebook/MercadoLibre (antes: todos los mensajes entrantes de todos los
clientes caían a la misma base por defecto, `ConnectionStrings:AlfaGestion`).

**Diseño final** (más profundo de lo previsto originalmente — leer un identificador del payload
no alcanzaba, porque la *verificación* del webhook de Meta tampoco tiene forma de saber a qué
cliente pertenece):

- Cada base tiene ahora un `WebhookToken` propio (columna nueva en `ALFA_CENTRAL.dbo.bases`,
  random, generado la primera vez que se pide).
- Nuevas rutas `/api/conversaciones/{canal}/webhook/{token}` para los 4 canales (GET de
  verificación + POST de mensaje, según aplique por canal) que resuelven la base desde el
  token, **antes** de tocar cualquier tabla — sin depender de sesión de usuario.
- Mecanismo nuevo `ISessionService.SetWebhookOverride(session)` que fuerza la base activa del
  scope de ese request puntual — reutiliza automáticamente los ~50+ call sites existentes que
  ya leían `ConnectionString` desde la sesión, sin tocarlos uno por uno.
- **Las rutas viejas sin token siguen andando exactamente igual que antes** (fallback a
  `AlfaGestion`) — no se rompió nada de lo ya configurado en Meta para el cliente actual.
- Configuración → Conversaciones ahora muestra la URL con token para cada canal, con una nota
  explicando qué es.
- De paso se corrigió que el webhook POST de WhatsApp no validaba firma
  (`X-Hub-Signature-256`), a diferencia de Instagram/Facebook. Se valida solo si hay `AppSecret`
  cargado, para no romper al cliente actual si todavía no lo cargó.

**Archivos tocados:**

- `docs/base-datos/sql-referencia/webhook_token_bases.sql` (nuevo — ejecutar manualmente contra
  `ALFA_CENTRAL`, no se aplica solo)
- `src/AlfaCore/Models/CentralAuthModels.cs` (`BaseCentralDto.WebhookToken`)
- `src/AlfaCore/Services/ICentralBasesService.cs` / `CentralBasesService.cs`
  (`GetByWebhookTokenAsync`, `EnsureWebhookTokenAsync`)
- `src/AlfaCore/Services/IConexionClienteService.cs` / `ConexionClienteService.cs` /
  `ISessionService.cs` / `SessionService.cs` (`SetWebhookOverride`)
- `src/AlfaCore/Program.cs` (rutas nuevas + refactor de los handlers existentes a métodos
  nombrados para poder reusarlos)
- `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor` (URL con token en cada canal)

**Pendiente para que funcione en producción:**

1. Ejecutar `docs/base-datos/sql-referencia/webhook_token_bases.sql` contra `ALFA_CENTRAL` (no
   se aplica solo, igual que pasó con `backups_control_modelo_inicial.sql`).
2. Compiló limpio (`dotnet build`, 0 errores/advertencias) y pasó
   `python tools/catalogo/check_catalogo.py`, pero no se probó en vivo contra un webhook real de
   Meta/MercadoLibre — conviene validarlo con un segundo cliente de prueba antes de confiar en
   esto para un cliente real.
3. Cuando se dé de alta un segundo cliente real de Conversaciones, hay que cargar la URL *con
   token* (visible en Configuración → Conversaciones) en su Meta App / MercadoLibre, no la
   genérica.

---

## Próximos pasos a definir (todavía abiertos)

Decisiones adicionales ya cerradas (además de las 9 de la sección de arriba):

10. **Sin pantalla de autoservicio de solicitud para la v1.** El cliente no pide el módulo desde
    una pantalla propia — el equipo de Alfa lo activa directo desde Administrar cuando confirma
    el pago por fuera del sistema. La pantalla de "pedir módulo" queda como mejora futura.
11. **Catálogo inicial confirmado: solo Conversaciones.** No se modulariza todo el ERP de una.
    El resto de los clientes actuales queda con el flag de legacy (todo habilitado); cada módulo
    nuevo (Ventas, Compras...) se suma después, uno por vez.
12. **Los módulos de los que depende Conversaciones (Clientes, Técnicos) se instalan sin
    cargo.** Aparecen activos igual en la lista del cliente, pero no se cobran aparte —
    resuelve además qué pasa si el cliente después compra otro módulo que también depende de
    ellos: ya están activos, no se vuelven a activar ni cobrar.
13. **No hay cliente esperando todavía.** Esto no cambia el diseño, pero bajaba la urgencia del
    punto de los webhooks — igual se decidió resolverlo ya (ver actualización 2026-08-05
    arriba), en paralelo al resto del análisis.

En el orden que sigue quedando pendiente:

1. Verificar el supuesto de `ALFACORE_MENU_WEB` uniforme entre bases (punto de arriba).
2. Diseñar la pantalla de "módulos pendientes de aprobación" para el admin — aunque no haya
   autoservicio de pedido (decisión 10), igual hace falta una pantalla donde Alfa carga/activa
   el módulo del cliente.
3. ~~Definir el catálogo inicial~~ — resuelto por la decisión 11.
4. ~~Resolver la brecha de los webhooks~~ — resuelto, ver "Actualización 2026-08-05" arriba.
   Falta correr el script SQL en producción y probarlo con un webhook real.
5. Con esto: plan de implementación concreto del sistema de módulos (tablas, servicios,
   pantallas, orden de construcción) — es el siguiente paso real de la lista.

---

## Cómo retomar rápido

```text
Leé docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md.
Quiero continuar el análisis desde ese estado.
El próximo paso es: [describir cuál de los "Próximos pasos a definir"].
```

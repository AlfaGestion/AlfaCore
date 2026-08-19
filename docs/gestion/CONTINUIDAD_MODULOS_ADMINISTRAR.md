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

## Actualización 2026-08-18 (4): planes reales en landing pages + selección de plan en el registro público

**Contexto de negocio** (decidido con el dueño del producto en esta misma conversación, no estaba
escrito en ningún doc hasta esta entrada): hasta esta tarea, "probar un módulo gratis" era: el
cliente tildaba checkboxes de módulos en `Verify.razor` (o llegaba desde una landing con un módulo
pre-elegido) y arrancaba una prueba fija de `PruebaModuloDefaults.DiasDuracion` (30 días), sin
relación con ningún `Plan`. Ahora que existen Planes reales (Fase 2/3/4 ya cerradas más arriba en
este documento — `dbo.Planes`, `ContratarPlanAsync`, etc.), el dueño del producto pidió tres cosas:

1. Que las landing pages muestren precios/planes reales cuando el módulo ya los tenga cargados, en
   vez de (o además de) el precio hardcodeado de `LandingContenidoCatalogo`.
2. Que el cliente elija un plan al registrarse, no solo tilde el módulo — y que los días de prueba
   salgan de `Plan.DiasPrueba` en vez del default fijo de 30 días.
3. **Regla de seguridad de negocio, no negociable**: elegir un plan en el registro público SOLO
   puede dejar al cliente en `Prueba` (autoservicio, igual que ya funcionaba la prueba gratuita) —
   NUNCA en `Activo` directo, porque no hay ninguna integración de pago online todavía
   (`ManualPaymentProvider` es el único proveedor, requiere que alguien de Alfa confirme el pago a
   mano). Si el plan elegido no tiene `DiasPrueba > 0`, o el cliente ya usó una prueba antes para
   ese módulo, la elección queda como pedido pendiente de aprobación (mismo mecanismo que ya
   existía: `Estado = Solicitado`, cola en `/admin/solicitudes`) — nunca se activa directo sin pago
   confirmado.

**Archivos nuevos**: ninguno — todo se hizo extendiendo servicios/páginas ya existentes de la
Fase 3/4, sin duplicar mecanismos.

**Archivos modificados**:
- `src/AlfaCore/Models/ModulosModels.cs` — `SolicitarModuloRequest.IdPlan` (nullable, opcional) y
  `SolicitudModuloDto.IdPlan`/`PlanNombre` (nullable): permiten guardar y mostrar qué plan pidió el
  cliente cuando una solicitud viene de un módulo con Planes reales, reusando la columna
  `ClienteModulos.IdPlan` que ya existía desde la Fase 2 (no hizo falta ninguna columna nueva).
- `src/AlfaCore/Models/PlanesModels.cs` — `PlanDisplayHelper.FormatPeriodoCorto(tipoFacturacion)`,
  helper estático chico (switch de `TipoFacturacion` a sufijo tipo "/mes"/"/año"/"/ciclo") usado
  por las 3 pantallas nuevas (`LandingModulos.razor`, `LandingModulo.razor`, `Verify.razor`) para no
  repetir el mismo switch tres veces.
- `src/AlfaCore/Services/IPlanesService.cs` / `PlanesService.cs` — método nuevo
  `GetPlanesVisiblesPorCodigoModuloAsync()`: un solo `JOIN` entre `dbo.Planes` y `dbo.Modulos`
  (`Activo = 1 AND VisibleCatalogo = 1` del lado del plan, módulo también activo), agrupado por
  `Modulos.Codigo` (case-insensitive) — evita que cada pantalla pública tenga que resolver
  `Codigo -> IdModulo` a mano y haga N llamadas a `GetByModuloAsync`. Un módulo sin ningún plan
  cargado simplemente no aparece en el diccionario.
- `src/AlfaCore/Services/ICentralAdminService.cs` / `CentralAdminService.cs` — método nuevo
  `ClienteYaUsoPruebaModuloAsync(idCliente, idModulo)`: extrae a un método público la consulta que
  `ContratarPlanAsync` ya usaba internamente (`PruebaVenceUtc` cargado alguna vez en
  `ClienteModulos`, sea cual sea el estado actual) para no duplicar esa regla en
  `CentralRegistrationService` — el registro público la necesita para decidir, ANTES de llamar a
  `ContratarPlanAsync`, si puede autoservirse en Prueba o si tiene que quedar como solicitud
  pendiente (nunca llama a `ContratarPlanAsync` si eso resultaría en `Activo` directo).
  `ContratarPlanAsync` se refactorizó para llamar a este método nuevo en vez de repetir la consulta
  inline (elimina duplicación, mismo comportamiento). `SolicitarModuloAsync` ahora acepta
  `request.IdPlan` opcional: si viene cargado, valida que el plan pertenezca al módulo pedido (reusa
  el `GetPlanRowAsync` privado que ya usaban `ContratarPlanAsync`/`CambiarPlanAsync`) y lo persiste
  en `ClienteModulos.IdPlan` aunque la fila quede en `Solicitado`. `GetSolicitudesPendientesAsync`
  ahora hace `LEFT JOIN dbo.Planes` para traer `IdPlan`/`PlanNombre` a la cola de aprobación.
- `src/AlfaCore/Services/CentralRegistrationService.cs` — inyecta `IPlanesService` (nueva
  dependencia del constructor). Método nuevo `ElegirPlanesAsync(code, idsPlanes)` en
  `ICentralRegistrationService`: para cada plan elegido en el selector de `Verify.razor`, aplica la
  regla de seguridad de negocio (si `DiasPrueba > 0` y el cliente nunca usó la prueba de ese módulo
  → `ContratarPlanAsync`, que arranca en `Prueba`; en cualquier otro caso → `SolicitarModuloAsync`
  con el `IdPlan` guardado, queda `Solicitado`) — nunca deja nada en `Activo` directo. Devuelve un
  mensaje que distingue qué planes arrancaron en prueba y cuáles quedaron pendientes de aprobación,
  para que el cliente sepa cuáles puede usar ya mismo. `TryActivarModuloDeLandingAsync` (la
  activación automática al confirmar el email cuando se vino de `/landing/{slug}`) ahora consulta
  los Planes del módulo antes de decidir: sin Planes reales → comportamiento histórico (prueba fija
  de 30 días vía `IniciarPruebaModulosAsync`, sin cambios); con Planes y exactamente UNO que ofrece
  prueba y el cliente nunca la usó → se autoactiva ese plan directo con `ContratarPlanAsync` (mismo
  resultado visible que antes: "Ya activamos tu prueba de X"); con 0, 2+ planes con prueba, o prueba
  ya usada → no autoactiva nada, `Verify.razor` muestra el selector de planes para ese módulo como
  con cualquier otro (no hay forma segura de elegir sola por el usuario).
- `src/AlfaCore/Components/Pages/Verify.razor` / `.razor.css` — el selector de módulos que aparece
  al confirmar el email ahora es mixto: para cada módulo con Planes reales cargados (consulta única
  a `GetPlanesVisiblesPorCodigoModuloAsync`) se muestra un selector de planes (radios con
  nombre/precio+moneda/período/días de prueba, más una opción "No, gracias") en vez del checkbox;
  los módulos sin Planes siguen mostrando el checkbox de siempre, sin cambios de comportamiento. Al
  confirmar, se llaman `IniciarPruebaModulosAsync` (módulos por checkbox, sin cambios) y
  `ElegirPlanesAsync` (módulos por plan) en la misma acción, y se combinan los mensajes de
  resultado. Si `GetPlanesVisiblesPorCodigoModuloAsync` falla por cualquier motivo (ej. `dbo.Planes`
  todavía no existe en una base que no corrió la migración de Fase 2), se cae en silencio al
  comportamiento 100% checkbox de siempre — nunca rompe el selector completo. CSS nuevo:
  `.plan-option-list`/`.plan-option`/etc. en `Verify.razor.css`, mismo estilo visual que
  `.modulo-card`.
- `src/AlfaCore/Components/Pages/LandingModulo.razor` — si el módulo tiene Planes reales
  (`_planes.Count > 0`), oculta el precio hardcodeado del hero y agrega una sección "Elegí tu plan"
  con una tarjeta por plan (nombre, precio+moneda, período, días de prueba si tiene). Sin Planes:
  comportamiento histórico sin cambios (precio hardcodeado de `LandingContenidoCatalogo`). El CTA
  final ajusta el copy ("elegí tu plan" vs. "prueba gratuita de 30 días") según corresponda.
- `src/AlfaCore/Components/Pages/LandingModulos.razor` (índice `/modulos`) — la tarjeta de cada
  módulo muestra "Desde {moneda} {precio del plan más barato}{período}" si el módulo ya tiene Planes
  reales, o el precio hardcodeado de siempre si no.
- `src/AlfaCore/wwwroot/css/landing.css` — sección nueva `.landing-planes`/`.landing-plan-card`/etc.
  para la grilla de planes de `LandingModulo.razor`, mismo lenguaje visual que `.landing-features`.
- `src/AlfaCore/Components/Pages/AdminSolicitudesModulos.razor` — columna nueva "Plan" en la tabla
  (nombre del plan pedido, o "—" si la solicitud no tiene uno). "Aprobar" ahora llama a
  `ContratarPlanAsync` cuando la solicitud tiene `IdPlan` guardado (deja el contrato con ese plan,
  precio y próximo cobro calculados) en vez de `ActivarModuloAsync` — mismo criterio que "Confirmar
  pago" en el panel Cliente → Módulos: es acá donde un humano de Alfa confirma el pago. Las
  solicitudes viejas sin `IdPlan` (o los módulos que todavía no tienen Planes) siguen usando
  `ActivarModuloAsync` exactamente como antes — compatibilidad total con la cola existente.

**Compatibilidad con módulos sin Planes todavía**: en cada punto de esta tarea se comprobó
explícitamente el camino "el módulo no tiene ningún plan cargado" y se dejó el comportamiento
histórico intacto (checkbox de 30 días fijos en `Verify.razor`, precio hardcodeado en las landings,
`ActivarModuloAsync` en la cola de solicitudes). Como solo `CONVERSACIONES` tiene Planes reales
cargados hoy (MENSUAL $150 ARS con 30 días de prueba y 5 de gracia; ANUAL $1500 ARS sin prueba y 10
de gracia — cargados en una sesión anterior desde `/admin/modulos/{id}/planes`), el resto del
catálogo (`TICKETS`, `ALFAKNOWLEDGE`, `PARTES_HORAS`, `AUTOMATIZACIONES`, `LOGISTICA`,
`POS - PUNTO DE VENTA`) sigue viéndose y comportándose exactamente igual que antes de esta tarea.

**Se puede probar en vivo** (a diferencia de las Fases 2/3/4, acá SÍ — la migración de Fase 2 ya
corrió contra `ALFA_CENTRAL` y `CONVERSACIONES` ya tiene los 2 planes reales cargados):
1. `/modulos` y `/landing/conversaciones` deberían mostrar "Desde ARS 150/mes" en vez de
   "USD 150/mes", y la landing de detalle debería mostrar la sección "Elegí tu plan" con las 2
   tarjetas (MENSUAL con badge "30 días de prueba gratis", ANUAL sin badge).
2. Registrarse desde `/registrarme?modulo=conversaciones` (un solo plan con prueba de
   CONVERSACIONES — el MENSUAL) → al confirmar el email debería autoactivarse solo en `Prueba`
   (mismo mensaje "Ya activamos tu prueba de Conversaciones" que antes), sin pasar por el selector.
3. Registrarse desde `/registrarme` (sin módulo de landing) → en el paso de Verify, el módulo
   Conversaciones debería aparecer con el selector de 2 planes (no checkbox); elegir el MENSUAL para
   un cliente que nunca usó la prueba debería dejarlo en `Prueba` de inmediato; elegir el ANUAL (sin
   `DiasPrueba`) debería dejarlo como solicitud pendiente visible en `/admin/solicitudes` con la
   columna "Plan" mostrando "Anual" — y "Aprobar" ahí debería llamar a `ContratarPlanAsync` en vez
   de `ActivarModuloAsync` (contrato con `PrecioContratado`/`FechaProximoCobro` calculados, no solo
   `Estado = Activo`).
4. Ningún módulo distinto de `CONVERSACIONES` debería mostrar cambios visibles en ningún paso —
   confirmar que el checkbox viejo sigue intacto para `TICKETS`/`ALFAKNOWLEDGE`/etc.
5. Nada de esto se ejecutó en vivo en esta sesión — solo se compiló (`dotnet build`, 0 errores,
   mismas 3 advertencias preexistentes de `InterfacesCatalogosService.cs`) y pasó
   `check_catalogo.py` (68 rutinas, 0 advertencias, 0 errores).

---

## Actualización 2026-08-18 (3): Fase 4 implementada — pantallas de administración

Implementación de la Fase 4 descripta más abajo ("plan de implementación" → "Fase 4 —
Administración (Blazor)"). Compiló limpio (`dotnet build`, 0 errores, mismas 3 advertencias
preexistentes de `InterfacesCatalogosService.cs`, no tocado en esta tarea) y pasó
`check_catalogo.py` (68 rutinas, 0 advertencias, 0 errores — el conteo no cambió porque el script
valida consistencia interna de `docs/CATALOGO_RUTINAS.md`, no que cada página nueva esté
catalogada). **Sigue sin poder probarse en vivo**: el script de la Fase 2
(`planes_cargos_pagos_modelo_inicial.sql`) todavía no corrió contra `ALFA_CENTRAL` — sin
`dbo.Planes`/`dbo.Cargos`/`dbo.Pagos` ni las columnas nuevas de `ClienteModulos`, las pantallas
nuevas van a fallar contra la base real apenas se toque algo real. Ese sigue siendo el paso manual
previo obligatorio.

**Archivos nuevos**:
- `src/AlfaCore/Components/Pages/AdminPlanes.razor` (`/admin/modulos/{idModulo:int}/planes`) —
  ABM de planes de un módulo puntual, mismo layout de dos columnas (formulario + listado) que
  `AdminModulos.razor`. Botón "Planes" agregado a cada fila de `AdminModulos.razor` que navega
  acá. Alta/edición/baja lógica vía `IPlanesService` (ya existía de la Fase 3, sin cambios). Gate
  `superadmin=1`. Sin paginación ni "Configurar vista" — ver decisión 1 más abajo.
- `src/AlfaCore/Components/Pages/AdminCargos.razor` (`/admin/cargos`) — listado paginado
  server-side (OFFSET/FETCH, regla 27) de `dbo.Cargos` con filtros (cliente/estado/vencimiento) y
  "Configurar vista" (columnas Cliente/Concepto/Período/Importe/Moneda/Vencimiento/Estado,
  activables y reordenables, persistida por usuario). Sin alta manual ni botón "Anular" — ver
  decisión 2. Gate `superadmin=1`.
- `src/AlfaCore/Components/Pages/AdminPagos.razor` (`/admin/pagos`) — mismo patrón de listado
  paginado + "Configurar vista" (Cliente/Fecha/Importe/Moneda/Medio de
  pago/Estado/Referencia) sobre `dbo.Pagos`, más un modal "Registrar pago" (Cliente → combo que
  carga los cargos `PENDIENTE`/`VENCIDO` de ese cliente → Cargo, Fecha, Importe, Moneda, Medio de
  pago, Referencia, Observaciones) que llama a `IBillingService.RegistrarPagoManualAsync` (ya
  existía de la Fase 3, sin cambios). Gate `superadmin=1`.

**Archivos modificados**:
- `src/AlfaCore/Models/BillingModels.cs` — `CargosFilters`/`PagosFilters` (con
  `PageNumber`/`PageSize`, regla 27); `ClienteNombre` agregado como propiedad enriquecida (no es
  columna de `dbo.Cargos`/`dbo.Pagos`) a `CargoDto`/`PagoDto`, poblada solo por los métodos de
  búsqueda nuevos; DTOs de "Configurar vista" para ambos listados
  (`CargosViewSettingsDto`/`CargosViewColumnDto`/`CargosViewColumnKeys`/`CargosViewGroupKeys` y
  el equivalente para Pagos), siguiendo al pie de la letra la estructura que pide la regla 26.3 de
  `CODEX_RULES.md`.
- `src/AlfaCore/Services/IBillingService.cs` / `BillingService.cs` — se agregó
  `SearchCargosAsync`/`SearchPagosAsync` (mismo patrón OFFSET/FETCH + COUNT que
  `PlanesService.SearchAsync`, con `LEFT JOIN dbo.Clientes` para resolver el nombre a mostrar en
  la grilla) y el mecanismo completo de "Configurar vista" por usuario
  (`Get/SaveCargosViewSettingsAsync`, `Get/SavePagosViewSettingsAsync`), calcado de
  `ContactosService`/`UsuariosService` (misma clave hasheada `USUVIEW-{MODULO}-{hash24}`, misma
  resolución de columna de detalle `VALORAUX`/`DESCRIPCION` en `TA_CONFIGURACION`). Diferencia
  importante de diseño: esa configuración de vista es una preferencia de UI del usuario, no un
  dato de negocio de ALFA_CENTRAL — así que se guarda en `dbo.TA_CONFIGURACION` de la base de
  **cliente activa en la sesión** (`ISessionService.GetConnectionString()`, nuevo parámetro del
  constructor), exactamente igual que `ContactosService`/`UsuariosService`, mientras que
  Cargos/Pagos/Planes en sí siguen leyéndose de ALFA_CENTRAL como el resto de la Fase 3. `BillingService`
  ahora depende de dos connection strings distintas a la vez (`ConnectionString` → ALFA_CENTRAL,
  `TenantConnectionString` → base de cliente activa), documentado con comentarios en el código
  para que no se confunda con un error.
- `src/AlfaCore/Models/ModulosModels.cs` — `ClienteModuloDto` extendido con `IdPlan`,
  `PlanNombre`, `PlanTipoFacturacion`, `PrecioContratado`, `MonedaContratada`,
  `FechaProximoCobro`, `RenovacionAutomatica` — todos nullable/con default salvo
  `RenovacionAutomatica` (default `true`), aditivo, no rompe nada existente.
- `src/AlfaCore/Services/CentralAdminService.cs` — `GetClienteModulosAsync` ahora hace `LEFT JOIN
  dbo.Planes` sobre `ClienteModulos.IdPlan` para traer el nombre/tipo de facturación del plan
  contratado y lo mapea al DTO extendido. `ContratarPlanAsync`/`CambiarPlanAsync` (ya existían de
  la Fase 3) no se tocaron.
- `src/AlfaCore/Components/Pages/AdminModulos.razor` — botón "Planes" nuevo por fila, navega a
  `/admin/modulos/{id}/planes`.
- `src/AlfaCore/Components/Pages/AdminHome.razor` — panel Cliente → Módulos extendido: nueva
  columna "Plan" (nombre del plan contratado + próximo vencimiento, o "—" si no tiene), botón
  "Contratar plan"/"Cambiar plan" (oculto para módulos que son dependencia obligatoria de otro,
  que no llevan plan) que abre un modal con el catálogo de planes activos de ese módulo
  (`IPlanesService.GetByModuloAsync`) y llama a `ContratarPlanAsync`/`CambiarPlanAsync` según
  corresponda. Botones nuevos "Cargos"/"Pagos" en el header principal, junto al que ya llevaba a
  "Módulos". Nada de lo existente (Activar/Suspender/Solicitar/Rechazar) se tocó.

**Decisiones tomadas durante la implementación (no estaban 100% cerradas en el plan)**:

1. **Planes: sin paginación ni "Configurar vista".** El plan original ("Fase 4" más abajo) sugería
   la ruta `/admin/modulos/{codigo}/planes` (se usó `/admin/modulos/{idModulo:int}/planes` — id
   numérico en vez de código, más simple para el route constraint de Blazor) pero no era
   explícito sobre paginación/vista configurable. Se decidió NO agregarlas, siguiendo el
   precedente real más cercano en el propio código: el catálogo de módulos en `AdminModulos.razor`
   (mismo tipo de pantalla — catálogo chico administrado por superadmin) tampoco las tiene. La
   cantidad de planes por módulo está acotada por diseño (unos pocos por módulo del piloto), así
   que no se justifica ese costo. Cargos/Pagos sí las llevan (pedido explícito de la tarea,
   volumen genuinamente transaccional).
2. **"Anular cargo": fuera de alcance, confirmado.** `BillingService` no tiene ningún método para
   anular/cancelar un `Cargo` (solo `GenerarCargoAsync`, `RegistrarPagoManualAsync`,
   `ProcesarVencimientosAsync`, `ProcesarGraciaYSuspensionAsync` — nada que mueva `Estado` a
   `ANULADO`). Agregarlo hoy hubiera significado inventar una regla de negocio nueva (¿qué pasa
   con `ClienteModulos.FechaProximoCobro` si se anula el cargo que lo iba a mover? ¿hace falta un
   motivo?) sin que el dueño del producto la haya confirmado — CODEX_RULES lo prohíbe
   explícitamente ("no inventar reglas no confirmadas"). `AdminCargos.razor` queda sin ese botón;
   queda anotado como pendiente para una fase futura si se necesita en producción.
3. **No se creó el script SQL de menú (`ALFACORE_MENU_WEB`/`TA_TAREAS`) para `/admin/cargos`,
   `/admin/pagos` ni `/admin/modulos/.../planes`, a pesar de que el pedido original lo sugería.**
   Investigación previa a escribir código: no existe ningún script así para `/admin/modulos` ni
   `/admin/solicitudes` (los dos precedentes más directos, ambos ya en producción). La razón,
   confirmada leyendo `MenuService.cs`: el nodo "Administrar" del menú lateral **no es una fila
   real de `ALFACORE_MENU_WEB`** — se inyecta a mano en `LoadVisibleMenuAsync`
   (`MenuService.cs` ~línea 215-223) como un `ShellModuleDto` sintético con
   `RutaWeb = "/admin"` **hardcodeado**, exclusivamente para usuarios con `SuperAdmin = true`. Al
   navegar, el click va directo a `/admin` (`AdminHome.razor`) — nunca pasa por
   `/shell/{ModuleKey}` (`ShellWorkspacePage.razor`), que es la única pantalla que efectivamente
   lista filas de `ALFACORE_MENU_WEB` agrupadas por `PadreClave`. Como "ADMINISTRAR" no existe
   como `Clave` real en esa tabla, cualquier fila que se insertara con `PadreClave = 'ADMINISTRAR'`
   quedaría huérfana: no la muestra ningún camino de navegación real, porque nada resuelve
   `/shell/ADMINISTRAR`. Insertar esas filas igual habría sido "trabajo muerto" — CODEX_RULES 12.2
   ("no rehacer/reestructurar sin necesidad") y la regla de oro de este documento
   ("reusar antes de inventar mecanismos nuevos") apuntan en la misma dirección: se replicó en
   cambio el patrón real que ya usan `AdminModulos.razor`/`AdminSolicitudesModulos.razor` —
   navegación por botones dentro de `AdminHome.razor` (`GoCargos`/`GoPagos`, más `GoPlanes` en
   `AdminModulos.razor`), sin fila de menú. Si en el futuro se decide que "Administrar" pase a ser
   un nodo real navegable por `/shell/ADMINISTRAR` (cambiando el `RutaWeb` hardcodeado a un valor
   vacío para que caiga en el fallback `/shell/{clave}`), ahí sí tendría sentido escribir el
   script de menú para colgar estas pantallas — no antes.
4. **Botón "Plan" oculto para dependencias obligatorias.** En el panel Cliente → Módulos, el
   botón "Contratar plan"/"Cambiar plan" no se muestra para filas con `EsDependenciaDeOtro = true`
   (ej. Clientes/Técnicos) porque esos módulos son gratuitos por diseño (decisión de producto ya
   confirmada en la Fase 2) y no tienen — ni deberían tener — un `Plan` asociado.

**Pendiente antes de poder probar en vivo** (todo lo de la Fase 3 sigue pendiente, más lo nuevo):
1. Correr `docs/base-datos/sql-referencia/planes_cargos_pagos_modelo_inicial.sql` contra
   `ALFA_CENTRAL` (sigue bloqueado para el agente).
2. Cargar al menos un `Plan` real para un módulo del piloto — ahora sí se puede hacer desde
   `/admin/modulos/{id}/planes` en vez de a mano por SQL.
3. Probar en vivo: `/admin/modulos/{id}/planes` (alta/edición/baja de plan) → `AdminHome.razor`
   "Contratar plan" (llama a `ContratarPlanAsync`, ya cubierto por la Fase 3) → `/admin/cargos`
   muestra el cargo generado por el job diario → `/admin/pagos` → "Registrar pago" lo marca pagado
   → columna "Plan" de `AdminHome.razor` refleja el `FechaProximoCobro` actualizado.
4. Probar "Configurar vista" en `/admin/cargos`/`/admin/pagos` en vivo (activar/desactivar
   columnas, reordenar, guardar, recargar la página y confirmar que persiste) — solo se revisó por
   código, no se ejecutó contra una base real.
5. "Anular cargo" sigue sin implementar (ver decisión 2) — pendiente de que el dueño del producto
   confirme la regla de negocio si hace falta en producción.

---

## Actualización 2026-08-18 (2): Fase 3 implementada — servicios de dominio de comercialización

Implementación de la Fase 3 descripta en la entrada de más abajo ("plan de implementación").
Compiló limpio (`dotnet build`, 0 errores, mismas 3 advertencias preexistentes de
`InterfacesCatalogosService.cs`, no tocado en esta tarea) y pasó `check_catalogo.py` (68 rutinas,
0 advertencias, 0 errores). **Nada de esto se puede probar en vivo todavía**: el script SQL de la
Fase 2 (`planes_cargos_pagos_modelo_inicial.sql`) sigue sin correrse contra `ALFA_CENTRAL` — sin
`dbo.Planes`/`dbo.Cargos`/`dbo.Pagos` y las columnas nuevas de `ClienteModulos`, cualquier consulta
de los servicios nuevos va a fallar contra la base real. Este es el próximo paso manual antes de
poder probar algo de esta fase.

**Archivos nuevos**:
- `src/AlfaCore/Models/PlanesModels.cs` — `PlanDto`, `PlanTipoFacturacion` (8 valores del CHECK),
  `MonedaValores`, `PlanesFilters` (con `PageNumber`/`PageSize`, regla 27), `CrearPlanRequest`.
- `src/AlfaCore/Models/BillingModels.cs` — `CargoDto`, `CargoEstados`, `PagoDto`, `PagoEstados`,
  `MedioPagoValores`, `RegistrarPagoManualRequest`.
- `src/AlfaCore/Services/IPlanesService.cs` / `PlanesService.cs` — CRUD de planes por módulo
  (listar paginado, listar por módulo, obtener, crear, editar, activar/desactivar = baja lógica).
  Mismo patrón de connection string a `ALFA_CENTRAL` que `CentralAdminService`.
- `src/AlfaCore/Services/IPaymentProvider.cs` / `ManualPaymentProvider.cs` — abstracción mínima del
  medio de cobro (un solo método, `RegistrarPagoAsync`, recibe la conexión/transacción ya abiertas
  por `BillingService` para que la creación del pago sea atómica con la actualización de
  Cargo/ClienteModulos). `ManualPaymentProvider` es la única implementación: el pago nace
  `APROBADO` directo porque ya fue confirmado por fuera del sistema. Mercado Pago queda sin
  implementar, tal como decidió el dueño del producto.
- `src/AlfaCore/Services/IBillingService.cs` / `BillingService.cs` — `GenerarCargoAsync`
  (idempotente por `(IdClienteModulo, PeriodoDesde)`, con gracia ante el conflicto de la constraint
  `UNIQUE` en vez de excepción cruda), `RegistrarPagoManualAsync` (transacción Dapper: pago
  aprobado → cargo pagado → `FechaProximoCobro` avanzado → reactiva si estaba `Suspendido` por
  mora), `ProcesarVencimientosAsync` (genera cargos de módulos `Activo` vencidos sin cargo abierto)
  y `ProcesarGraciaYSuspensionAsync` (marca `VENCIDO` y suspende fuera de gracia, con auditoría).
- `src/AlfaCore/Services/PlanBillingHelper.cs` — cálculo de `FechaProximoCobro` según
  `TipoFacturacion`, compartido entre `CentralAdminService.ContratarPlanAsync` (primer cobro) y
  `BillingService.RegistrarPagoManualAsync` (avance de período). `internal static`, sin DI.
- `src/AlfaCore/Services/BillingHostedService.cs` — job diario (no cada 6hs como el de pruebas,
  calcado de `ModuloPruebaRecordatorioHostedService` en todo lo demás: gate `IsSaaSMode` +
  `ConnectionStrings:AlfaCentral`, `CreateScope()` por ciclo). Llama
  `ProcesarVencimientosAsync` y después `ProcesarGraciaYSuspensionAsync`.

**Archivos modificados**:
- `src/AlfaCore/Services/ICentralAdminService.cs` / `CentralAdminService.cs` — se agregaron
  `ContratarPlanAsync` y `CambiarPlanAsync` (no se creó un `SubscriptionService` nuevo, tal como
  indicaba el plan). `ContratarPlanAsync` reusa el núcleo privado `ActivarConEstadoAsync` (mismo que
  usan `ActivarModuloAsync`/`IniciarPruebaModulosAsync`) para la cascada de dependencias
  obligatorias y el aprovisionamiento de AlfaKnowledge, y después sella el contrato
  (`IdPlan`/`PrecioContratado`/`MonedaContratada`/`FechaProximoCobro`/`RenovacionAutomatica`) con un
  `UPDATE` aparte. `CambiarPlanAsync` es un único `UPDATE` sin prorrateo (decisión de producto 3).
- `src/AlfaCore/Program.cs` — registro de `IPlanesService`/`PlanesService`,
  `IPaymentProvider`/`ManualPaymentProvider`, `IBillingService`/`BillingService` (mismo scope que
  `ICentralAdminService`) y `AddHostedService<BillingHostedService>()` junto a
  `ModuloPruebaRecordatorioHostedService`.

**Decisiones tomadas durante la implementación (no estaban 100% cerradas en el plan)**:

1. **Duración del ciclo para `TipoFacturacion = DIAS`**: el modelo de datos de la Fase 2 no define
   un campo dedicado a "cantidad de días del ciclo" para este tipo de facturación. Se reusa
   `Planes.CantidadIncluida` (ya existía para otro propósito — cupo de uso en `POR_USO`) como
   cantidad de días cuando el plan es `DIAS`, con `30` como default si queda sin cargar
   (`PlanBillingHelper.DiasCicloPorDefecto`). Ninguno de los módulos del piloto usa `DIAS` todavía,
   así que esto no bloquea nada real hoy, pero si se llega a necesitar un ciclo de días distinto de
   "cupo de uso" habría que agregar un campo propio en una fase de datos futura.
2. **"No usó prueba antes" (para decidir si `ContratarPlanAsync` arranca en `Prueba`)**: se
   interpretó como "la fila de `ClienteModulos` de ese cliente+módulo nunca tuvo `PruebaVenceUtc`
   cargado", sin importar el estado actual. Si ya lo tuvo alguna vez (incluso si terminó
   `Suspendido` por vencimiento), la contratación nueva arranca directo en `Activo`, no repite la
   prueba.
3. **Fecha base del primer cobro tras una prueba**: `FechaProximoCobro` se calcula desde
   `PruebaVenceUtc` (no desde "ahora") cuando la contratación arranca en `Prueba` — el primer cobro
   real cae recién cuando termina la prueba, no un ciclo completo después.
4. **Suspensión por mora dentro de `ProcesarGraciaYSuspensionAsync`**: se hace con un `UPDATE`
   directo dentro de la misma transacción Dapper que marca el `Cargo` como `VENCIDO`, en vez de
   llamar a `CentralAdminService.SuspenderModuloAsync` (que abre su propia conexión) — mantiene
   atómica la pareja Cargo+ClienteModulos sin salir de la transacción a mitad de camino. Es la
   misma sentencia SQL que usa `SuspenderModuloAsync`, solo que inline.
5. **Límite de agentes**: tal como pedía el alcance de esta fase, no se escribió ningún método que
   cuente agentes reales ni bloquee por cupo — los campos (`CantidadAgentesIncluida`,
   `CantidadAgentesContratados`, `PrecioPorAgenteExcedente`) solo existen en el modelo de datos y
   los DTOs nuevos, listos para cargarse desde una pantalla de administración futura.

**Pendiente antes de poder probar en vivo**:
1. Correr `docs/base-datos/sql-referencia/planes_cargos_pagos_modelo_inicial.sql` contra
   `ALFA_CENTRAL` (bloqueado para el agente por el clasificador de seguridad, igual que todas las
   migraciones anteriores de este sistema).
2. Cargar al menos un `Plan` real para un módulo del piloto (`CONVERSACIONES`/`TICKETS`/
   `ALFAKNOWLEDGE`/`PARTES_HORAS`/`AUTOMATIZACIONES`) — hoy no hay pantalla de administración para
   eso (es la Fase 4, todavía no arrancada), así que habría que insertarlo a mano o esperar a la
   Fase 4.
3. Probar el flujo completo: `ContratarPlanAsync` → `BillingHostedService.ProcesarVencimientosAsync`
   genera el cargo del período → `RegistrarPagoManualAsync` lo marca pagado y avanza
   `FechaProximoCobro` → si no se paga y se pasa la gracia, `ProcesarGraciaYSuspensionAsync`
   suspende el módulo. Nada de esto se ejecutó todavía, solo se compiló y se revisó por código.
4. No hay pantallas todavía para nada de esto (Fase 4 — `AdminPlanes`/`AdminCargos`/`AdminPagos`),
   así que estos servicios no son alcanzables desde la UI hasta que se construyan.

---

## Actualización 2026-08-18: plan de implementación — motor de comercialización/suscripciones

Diagnóstico solicitado a partir de un prompt externo (GPT) que pedía construir de cero un motor
completo de módulos/planes/suscripciones/licencias/cobranzas/pagos/consumos/créditos para
AlfaCore. Se comparó ese prompt contra el estado real del repo (ver secciones de abajo de este
mismo documento) y contra `docs/CODEX_RULES.md`/`AGENTS.md`. Conclusión: la separación conceptual
que proponía (Módulo ≠ Plan ≠ Suscripción ≠ Entitlement ≠ Cargo ≠ Pago) es correcta y es la misma
dirección en la que este sistema ya venía — pero el prompt asumía un módulo de módulos "muy
básico" que no existe más: `Modulos`/`ClienteModulos` ya cubren catálogo, dependencias en
cascada, trial de 30 días, cola de aprobación, filtro de menú por módulo y un
`BillingService`-en-miniatura (`ModuloPruebaRecordatorioHostedService`). Lo que falta de verdad es
la capa de dinero: **Plan (con modalidad), Cargo, Pago, Consumo y Créditos**. Este plan solo
construye eso, reusando lo existente sin romperlo.

**Regla de trabajo para todas las fases**: seguir el patrón de este mismo documento — cada fase
se implementa, se compila limpio, se corre `check_catalogo.py`, y se anota acá qué quedó pendiente
de correr manualmente en `ALFA_CENTRAL` (todo `ALTER TABLE`/`CREATE TABLE` nuevo en producción
sigue bloqueado para el agente por el clasificador de seguridad, igual que las migraciones
anteriores de este sistema). No se arranca una fase nueva sin cerrar o anotar explícitamente lo
pendiente de la anterior.

### Decisiones de producto — confirmadas 2026-08-18

1. **Moneda: explícita desde ya.** `Planes`, `Cargos` y `Pagos` llevan columna `Moneda`
   (`ARS`/`USD`, `CHECK` constraint) en vez del criterio implícito que usa hoy `Modulos.Precio`.
2. **Asientos/agentes: se resuelve ahora.** `Planes.CantidadAgentesIncluida` +
   `Planes.PrecioPorAgenteExcedente` (tope incluido en el plan + precio por excedente) y
   `ClienteModulos.CantidadAgentesContratados` (excedente comprado aparte del incluido). **Punto
   abierto real, no de producto sino técnico**: para validar el límite en runtime hace falta saber
   cómo se identifica hoy un "agente" de Conversaciones en el esquema (¿fila en
   `TA_USUARIOS`/`V_TA_Tecnicos` con algún flag, tabla de asignación aparte?) — no se inventa esa
   estructura; se investiga como paso previo a construir `ValidarLimiteAgentesAsync` en la Fase 3.
3. **Sin prorrateo.** Cambio de plan queda efectivo recién en el próximo período, confirmado.
4. **Piloto: `CONVERSACIONES` y los módulos que ya tienen precio propio hoy** (`TICKETS`,
   `ALFAKNOWLEDGE`, `PARTES_HORAS`, `AUTOMATIZACIONES`). `CLIENTES`/`TECNICOS` siguen como
   dependencias obligatorias gratuitas — no llevan `Plan`.
5. **Tests: no por ahora.** No se bootstrapea xUnit para esta iniciativa. Se compensa con el mismo
   criterio manual que ya usa este documento en cada actualización ("compiló limpio +
   `check_catalogo.py`, pendiente probar en vivo: ...").

### Fase 2 — cerrada (script SQL listo, falta correrlo)

`docs/base-datos/sql-referencia/planes_cargos_pagos_modelo_inicial.sql` (nuevo, ejecutar
manualmente contra `ALFA_CENTRAL`, igual que las migraciones anteriores de este sistema — bloqueado
para el agente por el clasificador de seguridad al ser `CREATE TABLE`/`ALTER TABLE`). Crea
`dbo.Planes`, `dbo.Cargos`, `dbo.Pagos`; extiende `dbo.ClienteModulos` con `IdPlan`,
`PrecioContratado`, `MonedaContratada`, `FechaProximoCobro`, `RenovacionAutomatica`,
`CantidadAgentesContratados`. No toca `dbo.Modulos` ni `dbo.ModulosDependencias`. Idempotente,
mismo patrón `BEGIN TRY/TRAN` + FKs en batch separado que los scripts anteriores.

`Consumos`/`BilleteraCreditos`/`MovimientosCreditos` quedan fuera de este script — ninguno de los
módulos del piloto (decisión 4) factura por uso todavía, se agregan en un script aparte el día que
haga falta un plan `POR_USO`/`PAQUETE_USOS`/`CREDITOS` real.

**Pendiente para que funcione en producción**: correr el script contra `ALFA_CENTRAL`; después,
Fase 3 (servicios de dominio).

---

### Revisión manual de la Fase 3 (2026-08-18)

Se revisó a mano el diff completo de la Fase 3 (el skill de revisión automática corrió en un
entorno aislado que no veía los cambios sin commitear, devolvió falso negativo). Dos hallazgos:

1. **Corregido**: `BillingService.RegistrarPagoManualAsync` calculaba `FechaProximoCobro` desde la
   fecha del pago (`DateTime.UtcNow`) en vez de desde el fin del período ya facturado
   (`Cargo.PeriodoHasta`). Un pago atrasado (dentro de la gracia) corría el aniversario de
   facturación para siempre — el ciclo se desalineaba cada vez más con cada mora. Fix: se agregó
   `PlanBillingHelper.EsRecurrente` y ahora se usa `cargo.PeriodoHasta + 1 día` como base del
   próximo ciclo. `dotnet build` limpio después del fix.
2. **Confirmado, se deja como está**: `ContratarPlanAsync` no genera un `Cargo` para el primer
   período de un plan pago sin trial — `FechaProximoCobro` queda apuntando al final del primer
   ciclo, así que ese primer período nunca aparece en `Cargos`/`Pagos`. Decisión del dueño del
   producto: el flujo real ya cobra el primer período por fuera del sistema antes de contratar en
   Admin, así que el hueco es solo de registro contable, no de dinero — no se cambia por ahora.

### Fase 1 — Diagnóstico (cerrada, este mismo bloque)

Ya hecho: comparación del prompt externo contra el código real (`Modulos`, `ClienteModulos`,
`ICentralAdminService`, `MenuService`, `PermissionService`, `ModuloPruebaRecordatorioHostedService`,
patrón ABM de `Contactos`) y contra `CODEX_RULES.md`. Sin código tocado.

### Fase 2 — Modelo de datos (nuevas tablas en `ALFA_CENTRAL`, no tocar `Modulos`/`ClienteModulos`)

Mapeo de conceptos: **no se crea una tabla "Suscripciones" separada** — `dbo.ClienteModulos` ya
es, conceptualmente, la suscripción+entitlement combinados (Cliente + Módulo + Estado + vigencia).
Se la extiende con una FK a `Planes` en vez de duplicar el concepto. Esto respeta la regla 1 de
`CODEX_RULES.md` ("no rehacer desde cero si ya existe una base funcional").

```
dbo.Planes
  Id, IdModulo (FK Modulos), Codigo, Nombre, Descripcion
  TipoFacturacion        -- catálogo: GRATIS/MENSUAL/ANUAL/DIAS/PAGO_UNICO/POR_USO/PAQUETE_USOS/CREDITOS
  Precio, Moneda
  DiasPrueba, DiasGracia
  CantidadIncluida, PermiteExcedentes, PrecioExcedente   -- para POR_USO
  RenovacionAutomaticaDefault
  Activo, VisibleCatalogo
  FechaCreacion, FechaModificacion

dbo.ClienteModulos                       -- YA EXISTE — solo se agrega:
  + IdPlan (FK Planes, nullable al principio para no romper filas actuales)
  + PrecioContratado, Moneda             -- precio histórico, no depende del precio actual del plan
  + FechaProximoCobro, RenovacionAutomatica

dbo.Cargos
  Id, IdCliente, IdClienteModulo (FK ClienteModulos)
  Concepto, PeriodoDesde, PeriodoHasta
  Importe, Moneda
  FechaEmision, FechaVencimiento
  Estado            -- BORRADOR/PENDIENTE/PAGADO/PAGO_PARCIAL/VENCIDO/ANULADO
  FechaCreacion, FechaModificacion

dbo.Pagos
  Id, IdCliente, IdCargo (FK Cargos, nullable — puede haber pago sin cargo, ej. compra de créditos)
  Fecha, Importe, Moneda
  Estado            -- CREADO/PENDIENTE/APROBADO/RECHAZADO/CANCELADO/REEMBOLSADO
  MedioPago         -- catálogo: EFECTIVO/TRANSFERENCIA/MERCADO_PAGO/TARJETA/DEBITO_AUTOMATICO/OTRO
  Provider, ProviderPaymentId, ProviderTransactionId    -- NULL en v1 (solo ManualPaymentProvider)
  Referencia, Observaciones, RegistradoPor
  FechaCreacion, FechaModificacion

dbo.Consumos                             -- solo si algún plan usa POR_USO/PAQUETE_USOS
  Id, IdCliente, IdModulo, IdClienteModulo
  Fecha, TipoConsumo, Cantidad
  ReferenciaTipo, ReferenciaId, Descripcion
  FechaCreacion

dbo.BilleteraCreditos
  IdCliente, IdModulo, SaldoActual

dbo.MovimientosCreditos
  Id, IdCliente, IdModulo, TipoMovimiento   -- COMPRA/CONSUMO/BONIFICACION/AJUSTE/VENCIMIENTO/REVERSA
  Cantidad, SaldoAnterior, SaldoNuevo, Referencia, Fecha, RegistradoPor
```

`Consumos`/`Créditos` solo se crean si en la Fase de piloto (decisión 4 de arriba) el módulo
elegido realmente factura por uso — si el piloto es `CONVERSACIONES` con planes mensuales simples,
estas dos tablas se posponen a cuando haya un módulo POR_USO real, para no construir en el vacío.

Auditoría: no se crea `AUDITORIA_SUSCRIPCIONES` aparte — se reusa el mecanismo centralizado que ya
pide `AGENTS.md` (`AUX_ERR` + `AppAuditRepository`, el mismo que usa `LogAuditAsync` en
`CentralAdminService` hoy para activaciones de módulo).

### Fase 3 — Servicios de dominio

No se crea un `SubscriptionService` nuevo desde cero — se extiende `ICentralAdminService` /
`CentralAdminService` (mismo patrón ya usado en las 6 actualizaciones anteriores de este
documento: "no se creó un servicio nuevo, se reusó el que ya consumen las páginas `/admin/*`").
Se agregan:

- `PlanesService` (nuevo, por ser una entidad nueva sin dueño previo): CRUD de planes por módulo,
  duplicar plan, activar/desactivar.
- `CentralAdminService`: `ContratarPlanAsync` (crea/actualiza `ClienteModulos` con `IdPlan` +
  `PrecioContratado` + calcula `FechaFinPrueba`/`FechaProximoCobro` según `TipoFacturacion`),
  `CambiarPlanAsync` (efectivo en el próximo período, sin prorrateo — ver decisión 3).
- `BillingService` (nuevo): `GenerarCargoAsync`, `ProcesarVencimientoAsync`,
  `ProcesarGraciaAsync`, `RegistrarPagoManualAsync` (aprueba pago → actualiza cargo → actualiza
  `ClienteModulos`, todo en una transacción Dapper). Reemplaza y generaliza lo que hoy hace
  a mano `ModuloPruebaRecordatorioHostedService` para el caso trial.
- `CreditService` (nuevo, solo si Fase 2 incluyó créditos): `ConsumirCredito`,
  `AcreditarCredito`, siempre por movimiento (nunca `UPDATE` directo del saldo).
- `IPaymentProvider` (interfaz nueva) + `ManualPaymentProvider` (única implementación v1 — refleja
  el flujo real de hoy: alguien de Alfa confirma la transferencia y carga el pago a mano). Mercado
  Pago queda como interfaz preparada, sin implementar, igual que pide el prompt original (punto 11).

Idempotencia: `GenerarCargoAsync`/`ProcesarVencimientoAsync` deben poder correr todos los días sin
duplicar cargos — clave única `(IdClienteModulo, PeriodoDesde)`.

### Fase 4 — Administración (Blazor)

Nuevas páginas, siguiendo el patrón ABM ya usado en `Contactos`/`AdminModulos`:

- `AdminPlanes.razor` (`/admin/modulos/{codigo}/planes`) — colgado del módulo, no independiente.
- `AdminCargos.razor` (`/admin/cargos`) — listado paginado (OFFSET/FETCH, regla 27 de
  `CODEX_RULES.md`), filtro por cliente/estado/vencimiento.
- `AdminPagos.razor` (`/admin/pagos`) — registrar pago manual (Cliente, Cargo, Fecha, Importe,
  Medio, Referencia), botón Aprobar.
- Extender `AdminHome.razor` (panel Cliente → Módulos, ya existe): mostrar plan contratado y
  próximo vencimiento en vez de solo Activo/Suspendido.

### Fase 5 — Portal del cliente ("Mi Cuenta")

Nueva sección, reusando el layout/menú existente — no es prioridad de la v1 salvo que el piloto
(decisión 4) la necesite para probar el flujo end-to-end. Si se hace: "Mis módulos" (plan, estado,
próximo vencimiento), historial de pagos, botón "Cancelar renovación" (marca
`RenovacionAutomatica = false`, no corta acceso hasta `FechaProximoCobro` — distinción explícita
del punto 31 del prompt original).

### Fase 6 — Scheduler

Un solo `BackgroundService` nuevo, `BillingHostedService`, mismo patrón que
`ModuloPruebaRecordatorioHostedService` (gateado por `ModoSaaS` + `ConnectionStrings:AlfaCentral`
configurados, `IServiceProvider.CreateScope()` por ciclo). Corre 1 vez por día: genera cargos de
suscripciones que vencen, procesa gracia, procesa suspensión por falta de pago. No se agrega
Quartz/Hangfire — no hace falta, el proyecto ya resolvió esto con `BackgroundService` puro.

### Fase 7 — Tests

Depende de la decisión 5 de arriba. Si se aprueba: bootstrap mínimo de xUnit apuntando solo a los
servicios de dominio nuevos (`BillingService`, `CreditService`, cálculo de vencimientos/gracia) —
sin tocar UI. Si no se aprueba: documentar acá la decisión y compensar con pruebas manuales
guiadas (mismo criterio que ya usa este documento: "compiló limpio + `check_catalogo.py`, no
probado en vivo, pendiente: ...").

### Fase 8 — Documentación

Actualizar `docs/modulos/` (si no existe la carpeta, crearla — ya está prevista en la convención
de `AGENTS.md`) con el modelo final de datos + diagrama de estados de `ClienteModulos`/`Cargos`.
Seguir anotando avances en este mismo documento, sección por sección, como hasta ahora.

### Orden real recomendado

Fase 1 (cerrada) → confirmar las 5 decisiones abiertas → Fase 2 (piloto acotado a un solo módulo)
→ Fase 3 → Fase 6 (scheduler, antes que las pantallas — sin generación de cargos automática las
pantallas de admin no tienen nada que mostrar) → Fase 4 → Fase 5 (si el piloto lo requiere) →
Fase 7/8 en paralelo a medida que cada pieza se prueba en vivo.

---

## Actualización 2026-08-08: landing pages públicas por módulo + activación automática al confirmar

Primer paso hacia un acceso público tipo "sitio de marketing" (a futuro, algo manejable como el
Website de Odoo — por ahora todo el contenido está hardcodeado en `LandingModulos.cs`, no hay CMS).

**Landing por módulo** (`/landing/{slug}`, índice en `/modulos`):
- `Models/LandingModels.cs` define `LandingContenidoCatalogo.Todos` — copy de marketing (tagline,
  descripción, features, dependencias) para cada módulo vendible (Precio > 0). Vive separado del
  catálogo real (`dbo.Modulos`) porque no hay editor todavía; el `Slug` es propio (no el `Codigo`)
  para que las URLs queden limpias (ej. `punto-de-venta`, no `pos%20-%20punto%20de%20venta`).
- Estilos en `wwwroot/css/landing.css` (global, no scoped) porque Blazor CSS isolation
  (`.razor.css`) no comparte estilos entre `LandingModulo.razor` y `LandingModulos.razor`.

**El CTA de cada landing activa la prueba sola, sin preguntar de nuevo**: el botón de cada landing
apunta a `/registrarme?modulo={slug}`. `Register.razor` lee ese query string, resuelve el
`LandingContenido` y lo manda como `PublicRegistrationRequest.ModuloSlug`, que viaja guardado en
`RegistroPublicoPendiente.ModuloSlug` (columna agregada en `landing_modulos.sql`) hasta la
confirmación. `CentralRegistrationService.VerifyAsync` llama a `TryActivarModuloDeLandingAsync`
después de aprovisionar la base — resuelve el `ModuloDto.Id` por código y llama al mismo
`IniciarPruebaModulosAsync` que ya usaba el selector manual — y devuelve el nombre activado como
`PublicVerificationResult.ModuloPreactivado`. `Verify.razor` salta el selector de módulos por
completo cuando `ModuloPreactivado` viene cargado, mostrando en cambio "Ya activamos tu prueba de
X. Iniciá sesión para empezar." El registro directo a `/registrarme` (sin pasar por una landing)
sigue mostrando el selector manual de siempre, sin cambios.

**Administración de landings** (sección nueva en `AdminModulos.razor`, debajo del catálogo de
módulos): lista todas las landings del catálogo hardcodeado con acciones "Ver" / "Suspender" /
"Reactivar". El estado se guarda en `dbo.LandingModulos` (Slug, Activo, ModificadoUtc,
ModificadoPor — tabla creada en `landing_modulos.sql`); sin fila = activa por defecto. Una landing
suspendida no aparece en `/modulos` y muestra "no disponible" si se entra directo a su URL.
**Nota de diseño no confirmada con el usuario todavía**: se implementó solo "suspender/reactivar"
(un booleano), no un "borrar" real — como el contenido es hardcodeado en código (no hay filas que
eliminar), "borrar" una landing equivaldría a sacarla del código fuente, algo que no tiene sentido
exponer como botón en Administrar. Si en algún momento el copy deja de ser hardcodeado (CMS real),
ahí sí tendría sentido un borrado de verdad.

**Fix del mismo día**: la activación automática se perdía cuando el link de confirmación se
pre-visitaba dos veces (el mismo bug de webmails/scanners que ya afectaba el mensaje de "cuenta
sin confirmar", ver más abajo) — el segundo visiteo (el clic real del usuario) caía en el camino
de "ya estaba confirmada" de `VerifyAsync`, que no sabía qué módulo se había elegido en la landing
porque `dbo.RegistroPublicoPendiente` ya se había borrado. Se agregó la columna
`dbo.Clientes.ModuloSlugLanding` (`clientes_landing_slug.sql`, ejecutada contra ALFA_CENTRAL) para
persistir el slug más allá del borrado de la fila pendiente; ese camino ahora reintenta
`TryActivarModuloDeLandingAsync` (idempotente) y también devuelve `ModuloPreactivado`.

---

## Actualización 2026-08-07: alta oficial pospuesta a la confirmación + CUIT/IVA opcionales

Dos decisiones de producto confirmadas tras encontrar datos basura en `ALFANET2007` durante las
pruebas de la prueba gratuita (ver actualización anterior): cuentas de test nunca confirmadas
(`pconway@gmail.com`, `alfredopinero@gmail.com`) que ya habían ocupado un `idCliente` y un CUIT
para siempre.

- **CUIT y Condición de IVA pasan a ser opcionales** en `/registrarme` — son datos fiscales
  argentinos que no hacen falta para una prueba gratuita, solo cuando alguien va a contratar de
  verdad. `sp_web_altaClienteAlfa` ya aceptaba ambos como nullable; el único ajuste real fue en
  `CreateOfficialCustomerAsync`: `@pIva` ahora se manda como `''` (no `DBNull`) cuando viene vacío,
  porque el SP dispara su propio default (`IF @pIva = '' SET @pIva = '   1'`) comparando contra
  string vacío, no contra NULL — con `DBNull` esa rama nunca se ejecutaba y la columna quedaba
  NULL en vez del default.
- **El alta oficial en `ALFANET2007` (`sp_web_altaClienteAlfa`, ocupa un CUIT y un `idCliente`) se
  pospuso de "al registrarse" a "al confirmar el email"**. Antes, cualquier intento sin confirmar
  (bot, typo, arrepentimiento) ya dejaba basura permanente en `MA_CUENTAS`/`MA_CUENTASADIC`. Ahora:
  - Nueva tabla `dbo.RegistroPublicoPendiente` en `ALFA_CENTRAL`
    (`docs/base-datos/sql-referencia/registro_publico_pendientes.sql`, **ya corrida** — este
    `CREATE TABLE` sí lo pudo ejecutar el propio Claude, a diferencia de los `ALTER TABLE`
    anteriores que quedan bloqueados por el clasificador de seguridad): guarda
    nombre/teléfono/email/password/CUIT/IVA + un `IdClienteReservado` nullable mientras el email
    no se confirma. `UNIQUE (Email)` es la garantía real contra duplicados en esta etapa.
  - `RegisterAsync` ya NO llama a `CreateOfficialCustomerAsync` ni toca `dbo.Clientes`/`dbo.users`
    — solo hace upsert en `RegistroPublicoPendiente` (por email) y manda el mail. `dbo.users` pasa
    a contener SOLO cuentas ya confirmadas, así que el chequeo de "¿ya existe?" es un simple
    `COUNT(1)` sin el JOIN ambiguo que causaba bugs la vez pasada.
  - `VerifyAsync` es donde ahora se llama a `sp_web_altaClienteAlfa` (con los datos de la fila
    pendiente) para recién ahí obtener el `idCliente`, seguido de `ProvisionAsync` y el insert en
    `dbo.Clientes`/`dbo.users` (con `verified = 1` directo, ya no hace falta un paso aparte). Si
    `ProvisionAsync` falla, el `idCliente` ya emitido se guarda en `IdClienteReservado` de la fila
    pendiente (que NO se borra) — un reintento del mismo link reusa ese mismo `idCliente` en vez de
    pedirle uno nuevo a la SP y acumular altas oficiales huérfanas por cada intento fallido. La fila
    pendiente solo se borra tras un `ProvisionAsync` exitoso.
  - Se sacaron `BaseAlreadyProvisionedAsync`, `RefreshPendingRegistrationAsync`,
    `LoadRegistrationByEmailAsync` y `DeleteOrphanedUserByEmailAsync` — existían para parchear
    problemas del diseño viejo (`dbo.Clientes`/`users` cumpliendo doble función de "pendiente" y
    "confirmado" a la vez) que ya no aplican con la tabla separada.
  - `IniciarPruebaModulosAsync` (selector de módulos en Verify.razor) no cambió — sigue resolviendo
    por `dbo.Clientes.verified_code` una vez que la cuenta ya está confirmada.
- `Register.razor`: labels actualizados a "CUIT (opcional)" / "Cond. IVA (opcional)".

Compiló limpio y pasó `check_catalogo.py`. **Pendiente**: probar en vivo el flujo completo
(registrarse sin CUIT → confirmar → que aparezca la base) y confirmar que un `ProvisionAsync`
fallido realmente reusa `IdClienteReservado` en el reintento (no se forzó ese camino en esta
sesión, solo se revisó el código).

---

## Actualización 2026-08-06 (2): prueba gratuita autoservicio de 30 días al registrarse

Nuevo estado `Prueba` en `dbo.ClienteModulos` (se suma a Solicitado/Activo/Suspendido/Rechazado).
Decisión de producto confirmada: **se incluyen todos los módulos por igual en la prueba**,
incluido `ALFAKNOWLEDGE` pese a que aprovisiona infraestructura real (base + colección externa)
al activarse — se evaluó excluirlo pero se prefirió simplicidad.

- `docs/base-datos/sql-referencia/modulos_estado_prueba.sql` (nuevo — ejecutar manualmente contra
  `ALFA_CENTRAL`, bloqueado para mí por el clasificador de seguridad al ser `ALTER TABLE`, mismo
  patrón que las migraciones anteriores de este sistema): reemplaza el CHECK de `Estado` para
  sumar `Prueba`, agrega `PruebaVenceUtc`/`UltimoRecordatorioUtc` (datetime, nullable).
- `CentralAdminService.ActivarModuloAsync` se refactorizó: ahora delega en un núcleo privado
  `ActivarConEstadoAsync(idCliente, idModulo, estado, activadoPor, pruebaVenceUtc, ct)` compartido
  con el nuevo `IniciarPruebaModulosAsync` (autoservicio). Reglas del núcleo: las dependencias
  obligatorias siempre se activan como `Activo` sin cargo (igual que antes); el módulo elegido
  directamente recibe el estado pedido; **nunca se retrocede un módulo ya `Activo` a `Prueba`**
  (protege un contrato pago si alguien reintenta la prueba por error); el hook de aprovisionamiento
  de AlfaKnowledge se dispara igual para ambos casos.
- Los 3 lugares que preguntan "¿está prendido?" (`GetClienteModulosAsync`, chequeo puntual
  `IsModuloActivoParaClienteActualAsync`, filtro de menú `GetModuloMenuFiltroParaClienteActualAsync`)
  ahora tratan `Prueba` con `PruebaVenceUtc` todavía no cumplida como activo — mismo criterio
  centralizado en `CentralAdminService.EstaEnPruebaVigente`.
- `Verify.razor` (paso final del registro público, después de confirmar el email): nuevo bloque
  "Elegí qué querés probar gratis por 30 días" con checkboxes de todo el catálogo pago (se excluyen
  Clientes/Técnicos, que son $0 y se arrastran solos), botón "Empezar prueba gratis". Llama a
  `ICentralRegistrationService.IniciarPruebaModulosAsync(code, idsModulos)` — identifica al cliente
  por el mismo código de verificación (no por un `IdCliente` que mande el navegador), para no
  exponer un endpoint público que active módulos de cualquier cliente sin haber probado nada. Si
  falla, no bloquea el alta — el mensaje de error ofrece pedirlo después por soporte.
- `ModuloPruebaRecordatorioHostedService` (nuevo, registrado en `Program.cs` junto a los otros
  `BackgroundService`): corre cada 6hs, solo si `ModoSaaS=true` y `ConnectionStrings:AlfaCentral`
  está configurado (no hace nada en una instalación on-premise). Cada ciclo: (1)
  `ExpirarPruebasVencidasAsync` — pasa a `Suspendido` toda prueba con `PruebaVenceUtc` cumplida;
  (2) `GetPruebasPorVencerAsync(5)` — trae las que vencen en ≤5 días y no recibieron aviso en las
  últimas 24hs, manda un email (mismo SMTP de `RegistroPublico:Email*` que ya usa la verificación
  de cuenta) y marca `UltimoRecordatorioUtc`. El email sale de `dbo.users` (no hay columna de email
  en `dbo.Clientes`).
- `AdminHome.razor` (panel Cliente → Módulos): la columna Estado muestra `Prueba (vence dd/mm/aaaa)`
  mientras está vigente y `Prueba vencida` si ya pasó la fecha pero el job diario todavía no corrió;
  fila con dos botones nuevos cuando está en prueba — "Cortar prueba" (suspende antes de tiempo,
  reusa `SuspenderModuloAsync`) y "Confirmar pago" (reusa `ActivarModuloAsync` tal cual, que
  convierte a `Activo` real y limpia `PruebaVenceUtc`).

Compiló limpio (`dotnet build`, 0 errores/advertencias) y pasó `check_catalogo.py`. **Pendiente
para producción**: (1) ejecutar `modulos_estado_prueba.sql` contra `ALFA_CENTRAL` — sin esto, el
selector de Verify.razor va a fallar al intentar guardar porque el CHECK constraint todavía no
admite `Prueba`; (2) probar en vivo el flujo completo (registrarse → verificar → elegir un módulo
→ confirmar que queda en `Prueba` con fecha de vencimiento → ver que aparece activo en el menú);
(3) confirmar que el job de recordatorio manda el email de verdad contra la config SMTP real
(no se probó en vivo, solo compilado); (4) decidir si 6hs de intervalo y 5 días de aviso previo
son los valores finales, o si el dueño del producto quiere ajustarlos (`PruebaModuloDefaults` en
`Models/ModulosModels.cs` centraliza `DiasDuracion`/`DiasAvisoPrevio`).

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

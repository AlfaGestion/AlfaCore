# Rediseño Conversaciones → Configuración (AlfaDesign)

**Estado del documento:** MIGRACIÓN EN PLANIFICACIÓN
**C0 — Baseline + contrato de producto:** COMPLETADO (commit `3fd78b5`)
**C1 — Shell + Information Architecture + navegación interna:** COMPLETADO (commit `2d56b70`, pusheado a `origin/main`)
**C2 — WhatsApp UX foundation:** TODAVÍA PENDIENTE DE APROBACIÓN FINAL (incluye C2.1 — migración visual, C2.2 — pulido de width/help/status, C2.3 — flujo de conexión Business + fix de backend, y C2.4 — QR lifecycle + scroll vertical + cierre del flujo Business; sin commit todavía, pendiente de aprobación visual **y funcional** — ver limitaciones de entorno en las secciones C2.3 y C2.4)

**WhatsApp es, a la fecha, la única sección de Configuración migrada visualmente a AlfaDesign v1** (Business y API). Funciona como referencia de patrón para las siguientes secciones. El resto de Configuración (Automatización, Integraciones IA, Operación y accesos, Soporte) sigue con estilo legacy — deuda visual registrada más abajo, no implementada.
**Rama de trabajo:** `main` (se trabaja directamente sobre `main`, sin rama nueva)

---

## C1 — Decisiones de implementación

### Shell

- `ConversacionesConfiguracion.razor` ahora inyecta `IPageHeaderService` y `IRouteContextService`, implementa `IDisposable` y llama `PageHeader.Set(...)` con `ShellMode = PageHeaderShellMode.AlfaDesignPilot`. Esto activa automáticamente `ShowSidebar => !IsAlfaDesignShellActive` en `MainLayout`, ocultando el sidebar global legacy sin tocar `MainLayout.razor`.
- App Top Bar: `TopNavigationItems` reconstruye los destinos que antes vivían en el sidebar legacy de Conversaciones (Conversaciones, Contactos, Clientes, Plantillas, Configuración activa, Estadísticas, Informes), usando `RouteContext.BuildRoute` (multi-base/multi-tenant). Contactos/Clientes se ocultan en `?directo=1`, igual que hacía el sidebar legacy.
- Context Toolbar: `Title = "Configuración"`, `Breadcrumb = ["Conversaciones", "Configuración"]`, acción única `Recargar` (mapea a `LoadAsync` existente), `OnBack` navega a `/conversaciones` (antes era el link "Volver al inbox"). No se agregó Guardar/Descartar/dirty status porque esa lógica no existe todavía (regla C1 §7).
- **Deuda documentada:** el botón `Recargar` del Context Toolbar solo refleja `Loading` (carga completa de la pantalla), no `Saving` de cada sección individual — evita tocar los ~16 puntos donde distintos métodos `Save*Async` alternan `Saving = true/false`. Es una simplificación menor, no una regresión funcional (los botones de guardado de cada sección siguen deshabilitándose igual que antes).

### Navegación interna (Settings Workspace)

Patrón nuevo, implementado localmente en este componente (markup + CSS scoped, sin crear componente compartido todavía — ver `docs/ui/alfadesign-components.md`, candidato a formalizar como "Settings Internal Navigation" recién cuando exista un segundo módulo que lo necesite):

```
settings-workspace
├─ settings-nav (rail vertical, 6 items)
└─ settings-content
```

### IA final Nivel 1

`Resumen | Canales | Automatización | Integraciones IA | Operación y accesos | Soporte`. Se eliminó `Herramientas` (estaba vacía; no existía contenido que reclasificar). `Resumen` es un landing mínimo (tarjetas hacia cada categoría, sin métricas ni health checks inventados).

### Canales

Header + `AlfaTabs` (componente AlfaDesign existente) para `WhatsApp | Instagram | Facebook | Mercado Libre`. Instagram/Facebook/Mercado Libre no cambiaron de contenido, solo la condición que los muestra sigue siendo `_activePrimaryTab == "canales"` (sin cambios).

### WhatsApp

Sub-navegación nueva con `AlfaTabs`: `Números` (default) | `General`. "Números de WhatsApp" (antes en "Operación") ahora es la vista por defecto del canal WhatsApp, cumpliendo la regla de prioridad QR > Meta Cloud (C1 §12). El bloque "Parámetros del canal" (Modo del canal + Meta Cloud API completo: Access Token, App Secret, Verify Token, Graph API version, etc.) quedó agrupado bajo "General" tal cual estaba — **no se separó Meta Cloud de "Modo del canal" dentro de ese mismo formulario** porque ambos comparten un único save (`SaveAsync`) y separarlos visualmente en dos destinos de navegación distintos requeriría forkear ese contrato de persistencia, fuera de alcance de C1 (regla C1 §3: "si mover una sección requiere cambiar su modelo o contrato de persistencia, no hacerlo, reportarlo"). Queda reportado como candidato de una fase SEC/C3 posterior.

Ni el modelo `ConversacionWhatsAppNumeroDto`, ni `ConversacionWhatsAppConfigDto`, ni ningún service, fueron modificados. El bloque "Números de WhatsApp" no se movió físicamente en el archivo: se le cambió únicamente la condición `@if` que lo muestra (ahora `_activePrimaryTab == "canales" && _activeChannelTab == "whatsapp" && _activeWhatsAppSection == "numeros"`), aprovechando que un `@if` en falso no genera nodos DOM — el resultado visual es idéntico a haberlo movido, sin el riesgo de un corte/pegado de ~180 líneas.

### Automatización vs Integraciones IA

- **Integraciones IA** = bloque AlfaKnowledge íntegro (tenía su propio save, `SaveAlfaKnowledgeAsync`, separable sin tocar backend).
- **Automatización** = bloque "Automatizaciones" (horario, bienvenida, Asistente IA, auto-cierre, SLA, informe mensual) + "Reglas / palabras clave".
- **Deuda documentada:** dentro del bloque "Automatizaciones" hay dos `<details>` — "Asistente IA" e "Informe mensual" — que conceptualmente pertenecen más a Integraciones IA que a Automatización. No se separaron porque todo el bloque comparte un único save (`SaveAutomatizacionesAsync`, transacción con `CONV_*` + `CONV_ASISTENTE`); separarlos requeriría forkear ese contrato de persistencia. Reportado como candidato de fase posterior (probablemente C5 o una fase SEC/backend explícita), no ejecutado en C1.

### Operación y accesos / Soporte

- **Operación y accesos** = Prioridad de atención + Administradores de conversaciones (ninguno movido físicamente, solo recondicionados).
- **Soporte** = AnyDesk (reclasificado desde la extinta "Herramientas"/"Operación"; no se creó diagnóstico, logs viewer ni ninguna acción inexistente, cumpliendo la regla de no fingir funciones).
- "Usuarios por número" no se extrajo como sección aparte: sigue viviendo únicamente dentro del manager de cada número (Canales → WhatsApp → Números), evitando una segunda fuente de edición (regla C1 §16).

### Responsive

Breakpoint en 1024px: el rail de navegación (`settings-nav`) pasa de columna sticky a fila horizontal con wrap (`flex-direction: row`, `position: static`). No se validó visualmente todavía (pendiente de validación visual del usuario) — la regla CSS está escrita pero no capturada en captura de pantalla.

### Deep links

No implementados. `_activePrimaryTab`, `_activeChannelTab` y el nuevo `_activeWhatsAppSection` siguen siendo estado interno de componente (no sobreviven a un reload ni son enlazables). Deuda documentada explícitamente, sin cambios de ruta en C1 (regla C1 §27).

### Contenido que sigue legacy internamente

Todo el contenido *dentro* de cada `panel-card` (formularios de WhatsApp/Instagram/Facebook/Mercado Libre/AlfaKnowledge/Automatizaciones/Reglas/Prioridad/Números/Administradores/AnyDesk) permanece sin cambios: mismos campos, mismo binding, mismos saves, mismo CSS legacy claro (`#hex`, gradientes) en los estilos scoped no relacionados con el shell/nav. Eso es rediseño profundo de formulario, explícitamente fuera de alcance de C1.

### Servicios/backend

No se modificó ningún archivo bajo `Services/`, `Models/` (excepto los tipos de `PageHeaderModels.cs` que ya existían), SQL, `Program.cs`, `worker.mjs` ni ningún contrato de persistencia. Los únicos archivos tocados en código fueron `ConversacionesConfiguracion.razor` y `ConversacionesConfiguracion.razor.css`.

---

## C2 — Decisiones de implementación (WhatsApp UX foundation)

Alcance: exclusivamente `Canales → WhatsApp` (Business y API). No se tocó Instagram, Facebook, Mercado Libre, Automatización, Integraciones IA, Operación ni Soporte, salvo la reducción de prominencia del banner global de webhook (afecta a los 4 canales, ver más abajo).

### Nomenclatura (solo UI, sin tocar código interno)

- Tab "Números" → **WhatsApp Business** (clave interna sigue siendo `"numeros"`).
- Tab "General" → **WhatsApp API** (clave interna sigue siendo `"general"`).
- Ningún enum, provider constant, clave `CONV_*`, propiedad de modelo o método fue renombrado. `ConversacionWhatsAppProviderModes`, `ConversacionWhatsAppWebSessionModes`, `ConversacionWhatsAppWebSessionStatuses` quedaron intactos. Los `<option>` de los selects de "Modo del canal"/"Proveedor predeterminado" solo cambiaron su texto visible ("WhatsApp Web" → "WhatsApp Business (QR)"), no su `value`.

### Status superior

Se eliminó el grid de 5 cards grandes (Modo del canal, Envío Cloud API, Webhook de verificación, Publicación, Sesión WhatsApp Web) que mezclaba configuración + readiness + runtime. Reemplazado por una línea compacta que cambia según la sub-sección activa:

- **WhatsApp Business:** `● N números · M conectados` (un solo chip, sin inventar "operativo").
- **WhatsApp API:** 3 chips compactos con punto de color — `API: Listo/Pendiente`, `Webhook: Listo/Pendiente`, y el texto real de `GetPublicationLabel()` ("Lista para configurar en Meta" / "Falta completar datos"). Ningún término nuevo fue inventado; se reutilizan `GetStatusLabel`/`GetPublicationLabel` ya existentes.

"Modo del canal" (select) se movió dentro de WhatsApp API → sección "Conexión" (deja de ser headline).

### WhatsApp API — progressive disclosure

El formulario único se reorganizó en 3 `<details class="conv-config-collapsible">` (mismo patrón visual ya usado en Automatizaciones, sin CSS nuevo): **Conexión** (modo, proveedor, Phone Number ID, WABA ID), **Credenciales** (Access Token, Verify Token, App Secret) y **Webhook** (base pública, versión Graph API, path, URL de callback, resumen de sesión WhatsApp Business). Un único botón "Guardar configuración" sigue llamando a `SaveAsync` sin cambios — no se separó el save porque romper ese contrato está fuera de alcance de C2 (regla §2). La ayuda ("Qué necesitás en Meta", "Dónde queda guardado", "Manual interno") se movió a un `<details>` colapsado por defecto ("¿Necesitás ayuda para configurar Meta Cloud API?"), sin borrar contenido.

### WhatsApp Business — tarjetas por número

Cada número pasó de un formulario plano de 6 campos + 3 botones iguales a una tarjeta (`.wa-number-card`) con:

- Encabezado: nombre + teléfono + checkbox "Activo" (input real, no status).
- Estado real vía `AlfaTag` (`GetNumeroEstadoTone`/`GetNumeroEstadoLabel`): Conectado (success) / Esperando escaneo (warning, mapea `PENDING_QR`) / Desconectado (neutral) / Error (danger, si `WebLastError` no está vacío). Solo 4 estados, todos derivados de `WebSessionStatus`/`WebLastError` reales — ninguno inventado.
- Acción principal **"Conectar WhatsApp"** (llama a `GenerateWhatsAppWebPairingAsync`, el mismo handler que antes decía "Generar QR" — sin cambio de lógica).
- "Refrescar estado" pasó a icon-button secundario (mismo `RefreshWhatsAppWebSessionAsync`).
- "Detener sesión" ahora pasa por `AlfaConfirmDialog` (`RequestStopWhatsAppWebSession` → confirmar → `ConfirmStopWhatsAppWebSessionAsync` → llama a `ClearWhatsAppWebPairingAsync`, el handler original, sin tocar su cuerpo). No se ejecutó Stop durante la validación de esta fase.
- QR: cuando hay pairing activo, se agregó una instrucción breve ("Abrí WhatsApp Business → Dispositivos vinculados → Vincular dispositivo → escaneá este código") en lugar de mezclar ayuda técnica.
- Campos técnicos (Nombre, Phone Number ID editable, Nombre visible sesión Web, Instancia sesión Web, Método de inicio, Número para sesión Web, checkbox de código de texto) se movieron a `<details>` "Opciones avanzadas", colapsado. Mismo binding (`@bind`), ningún campo se quitó.
- "Usuarios con acceso" se movió a `<details>` "Usuarios con acceso (N)" colapsado, mismo checklist/binding/`ToggleNumeroUsuario` de siempre.
- El bloque runtime (PID, estado, última actualización) se movió a `<details>` "Soporte / runtime", solo visible si hay datos reales (`WebRuntimeState` o `WebWorkerProcessId`).
- El error (`WebLastError`) se muestra como texto corto con ícono de advertencia, no como bloque técnico "Estado runtime" con la palabra "Error:" concatenada.
- "Cómo funciona" (ayuda multi-número) se colapsó igual que en WhatsApp API.

### Deuda / cleanup

`GetWhatsAppWebOverviewLabel`, `GetNumeroWhatsAppWebSummary` y `GetWhatsAppWebStatusLabel` quedaron sin ningún caller tras el rediseño (reemplazados por `GetWhatsAppNumerosSummary`/`GetNumeroEstadoLabel`/`GetNumeroEstadoTone`) y se eliminaron como código muerto — no se tocó ningún método que sí tuviera un caller real.

### Webhook banner global

El banner "Webhook identificado por base" / "Webhook sin identificación de base" sigue siendo **global** (aparece arriba de cualquier sección de Configuración) porque el token de routing aplica a los 4 canales (WhatsApp/Instagram/Facebook/Mercado Libre), no solo a WhatsApp — moverlo dentro de WhatsApp API habría sido incorrecto funcionalmente. Se agregó la clase `settings-webhook-banner` (padding y peso de fuente reducidos) sin tocar la clase global `.conversations-inline` (compartida con otras pantallas) ni el token ni su lógica de generación.

### Content width

`.wa-number-card` tiene `max-width: 720px` para no estirarse a todo el ancho en 2048px. No se tocó `.conv-config-layout`/`.conv-config-form` (compartidos con Instagram/Facebook/Mercado Libre) para no afectar esos canales en C2.

### Responsive

Sin reglas nuevas de breakpoint más allá de las ya definidas en C1 (`.settings-nav` a 1024px). Las tarjetas de número y los `<details>` son de flujo normal (`display: block`), no requieren reglas adicionales para no producir overflow horizontal. **Pendiente: validación visual real en 2048/1440/1024.**

### Servicios/backend

Sin cambios. Los únicos archivos tocados en C2 fueron `ConversacionesConfiguracion.razor` y `ConversacionesConfiguracion.razor.css`.

---

## C2.1 — Migración visual completa de WhatsApp a AlfaDesign

Motivo: tras revisar capturas reales de C2, el contenido de WhatsApp (especialmente WhatsApp API) seguía mezclando shell AlfaDesign con superficies claras/legacy heredadas de `.conv-config-collapsible` (definida con `background:#fbfcfe`, gradientes claros y texto azul oscuro — pensada para un tema claro que ya no existe en este shell). C2.1 corrige eso sin tocar backend.

### Causa raíz identificada

`.field input/select/textarea` y `.context-card` (clases globales de `app.css`) **ya eran oscuras** (fondos `rgba` sobre dark). El problema real era exclusivamente `.conv-config-collapsible` y sus hijos (`__summary`, `__title`, `__hint`), con colores hardcodeados claros. Esa clase se deja intacta porque Automatización todavía la usa y sigue fuera de alcance — el fix fue dejar de usarla dentro de WhatsApp, no modificarla.

### Patrón nuevo: `.wa-settings-section`

Reemplaza `.conv-config-collapsible` únicamente dentro de WhatsApp Business y WhatsApp API. Es un `<details>` con:

- `.wa-settings-section` (contenedor, surface `--alfa-bg-surface`, borde `--alfa-border-default`, radius `--alfa-radius-8`);
- `.wa-settings-section__summary` (header compacto, hover sutil `--alfa-state-hover`);
- `__icon` / `__title` / `__hint` / `__chevron` (rota 180° al abrir vía `[open]`);
- `.wa-settings-section__body` (mismo dark, separador superior sutil).

Se usa en ambas pestañas (Conexión/Credenciales/Webhook en API; Usuarios con acceso/Opciones avanzadas/Soporte-runtime/Agregar número en Business; y en ambas ayudas). No se formalizó todavía como componente Razor compartido (`AlfaDesign/`) — sigue siendo CSS+markup local en `ConversacionesConfiguracion.razor(.css)`, candidato a extraerse cuando un segundo módulo lo necesite (regla: no crear componentes especulativos).

### Component-first aplicado

Convertidos a componentes AlfaDesign, preservando el mismo `@bind`/valor real (sin tocar el modelo):

- `AlfaInput` (`@bind-Value`): Phone Number ID, WABA ID, Verify Token, App Secret, Base pública HTTPS, Graph API version, y en Business: Nombre, Phone Number ID, Nombre visible sesión Web, Instancia sesión Web, Número para sesión Web, y los campos de "Agregar número".
- `AlfaSelect` (`@bind-Value` + `Options`): Modo del canal, Proveedor predeterminado, Método de inicio — con listas `_providerModeOptions`/`_defaultProviderOptions`/`_webSessionModeOptions` que usan las mismas constantes (`ConversacionWhatsAppProviderModes`, etc.) como `Value`, no strings nuevos.
- `AlfaCheckbox` (`Value`/`ValueChanged`, tipo `bool?`): "Activo" por número, "Generar también código de texto", cada checkbox de usuario con acceso.
- `AlfaTag`: estado de conexión por número (ya existía desde C2).
- `AlfaNotification`: feedback contextual de WhatsApp (ver más abajo).

**No migrados, con motivo documentado (regla §10 — sin equivalente directo):**

- Access Token: `<textarea>` — `AlfaInput` no tiene variante multilínea. Estilizado con clase local `.wa-textarea` (mismos tokens `--alfa-*` que `AlfaInput`, pero definida en este componente, no reutilizando la clase scoped de `AlfaInput.razor.css` — ver nota de bug abajo).
- Webhook interno: `<input readonly>` — `AlfaInput` no soporta `readonly` (solo `Disabled`, que impediría seleccionar/copiar el texto en algunos navegadores). Estilizado con clase local `.wa-input-readonly`.

### Bug evitado: CSS scoping cruzado

Primer intento reutilizó literalmente las clases `alfa-field__label`/`alfa-input__control` (definidas en `AlfaInput.razor.css`) en markup de `ConversacionesConfiguracion.razor` para los dos campos no migrados. Blazor aplica CSS isolation por atributo autogenerado (`b-xxxxx`) solo a los elementos renderizados *por ese componente*; una clase con el mismo nombre escrita en otro archivo `.razor` no recibe el estilo, así que esos dos inputs habrían quedado sin estilo (fondo claro del navegador) — el mismo bug que se intentaba corregir. Se corrigió creando `.wa-field`/`.wa-field__label` locales en `ConversacionesConfiguracion.razor.css` en vez de tomar prestadas clases scoped de otro componente.

### WhatsApp Business — refinamiento de la tarjeta de número

- Encabezado eliminado el título "WhatsApp Business" duplicado dentro del panel (ya está en el header de sección) — evita la repetición Canales/WhatsApp/WhatsApp/WhatsApp Business señalada.
- Badge técnico `CONV_WHATSAPP_NUMEROS` removido de la vista principal (bajó de prioridad, ya no compite con el heading).
- QR: contenedor propio `.wa-pairing` (grid QR + detalle), fondo blanco solo en el QR mismo (`.wa-pairing__qr`, necesario para que el QR sea escaneable — no es "surface" de la UI, es la imagen).
- "Usuarios con acceso" ahora es grid (`.wa-user-grid`, `auto-fill minmax(200px,1fr)`) de `AlfaCheckbox`, no una tira horizontal.
- "Agregar número" pasó de formulario suelto al final a su propia `.wa-settings-section` colapsable.

### WhatsApp API — reorganización final

`Conexión` / `Credenciales` / `Webhook` como `.wa-settings-section` (Conexión abierta por defecto, las otras dos colapsadas). El badge `TA_CONFIGURACION` y "Origen actual" bajaron de prioridad: ahora viven dentro de una sección de ayuda "Información técnica" colapsada, no como subtítulo del heading principal.

### Ownership contextual del feedback (WhatsApp)

Problema reportado: el banner de error ("No se pudo iniciar la sesión real de WhatsApp Web...") aparecía en Soporte, Integraciones IA, etc. porque `_feedback` es un único campo de página compartido por *todas* las secciones.

Solución no invasiva (sin tocar el resto de los handlers de otras secciones):

- Nuevo campo `_feedbackScope` (`string?`). Los 6 handlers de WhatsApp que setean `_feedback` (`GenerateWhatsAppWebPairingAsync`, `RefreshWhatsAppWebSessionAsync`, `ClearWhatsAppWebPairingAsync`, `SaveAsync`, `SaveNumeroAsync`, `AddNumeroAsync`) ahora también hacen `_feedbackScope = "whatsapp";` como primera línea.
- `SelectPrimaryTab`/`SelectChannelTab` limpian `_feedbackScope` a `null` en cuanto el usuario navega fuera de Canales→WhatsApp.
- El banner **global** (`.conversations-inline`, usado por Instagram/Facebook/Mercado Libre/Automatización/etc., sin cambios) ahora solo se muestra si `_feedbackScope != "whatsapp"` — para todo lo que no es WhatsApp, comportamiento idéntico a antes.
- El feedback de WhatsApp se migró a `AlfaNotification` (toast flotante, `position: fixed`, auto-dismiss), montado una sola vez cerca de la raíz del componente vía la propiedad computada `WhatsAppFeedback`. Esto resuelve simultáneamente "no debe dominar la pantalla" (es un toast pequeño, no un banner de ancho completo) y "no debe contaminar otras categorías" (desaparece solo o al navegar).
- No se tocó ningún backend: `UiOps.RunAsync`, los mensajes de error reales y su contenido siguen exactamente iguales.

### Webhook — ya no global

El banner "Webhook identificado por base" / "Webhook sin identificación de base" se sacó de la posición global (arriba de toda Configuración) y se movió a la pestaña **Soporte** (antes de AnyDesk), no a WhatsApp API, porque el token de routing aplica a los 4 canales (WhatsApp/Instagram/Facebook/Mercado Libre), no solo a WhatsApp — ponerlo únicamente en WhatsApp API habría sido funcionalmente incorrecto para los otros 3 canales. Dentro de WhatsApp API → Webhook se agregó una línea de estado ("Routing por base: configurado/sin token") que no depende de la posición del banner global y no cambia `EnsureWebhookTokenAsync`, el token ni el endpoint.

### Content width

`.wa-layout` (reemplaza `.conv-config-layout` solo dentro de WhatsApp) define `max-width: 1400px` y grid `minmax(0,1fr) minmax(240px,320px)` para el panel principal + ayuda, para no estirarse a todo el ancho en 2048px ni dejar una columna de ayuda vacía permanente. No se tocó `.conv-config-layout` (todavía usado por Instagram/Facebook/Mercado Libre/Automatización/Operación).

### CSS legacy eliminado (solo lo que quedó sin uso)

`.conv-waweb-pairing`, `.conv-waweb-pairing__qr`, `.conv-waweb-pairing__details`, `.checkbox-inline`, y las reglas de `.wa-help-details`/`.wa-number-card__advanced summary` de C2 (superadas por `.wa-settings-section`). Se verificó con grep que ninguna quedara referenciada en el markup antes de borrarla. `.conv-config-collapsible` y su media query (720px) se mantienen intactos porque Automatización los sigue usando.

### Validación técnica ejecutada

`dotnet build` → 0 errores. `check_catalogo.py` → 68 rutinas, 0 advertencias, 0 errores. `git diff --check` → limpio. **No se pudo ejecutar la app en este entorno** (requiere SQL Server real configurado); la validación visual con capturas reales queda para el entorno del usuario.

### Servicios/backend (C2.1)

Sin cambios. Los únicos archivos tocados fueron `ConversacionesConfiguracion.razor` y `ConversacionesConfiguracion.razor.css`.

---

## C2.2 — Pulido final de WhatsApp y cierre de C2

Ajustes puntuales tras la validación visual de C2.1. No se tocó arquitectura, navegación ni backend.

### Content width

`.wa-layout` (compartido por WhatsApp Business y WhatsApp API) pasó de `max-width: 1400px` con columnas `minmax(0,1fr) minmax(240px,320px)` a `max-width: 1280px`, `width: 100%`, columnas `minmax(0,1fr) minmax(280px,340px)`. Se eliminó el `max-width: 720px` fijo de `.wa-number-card`, que era la causa real de "card angosta + espacio vacío": la card ahora ocupa todo el ancho de la columna principal (~900px a 1280px totales), dejando que `.wa-fields` (grid de 2 columnas) respire en vez de comprimirse.

### Ayuda integrada

`.wa-help` (columna secundaria de ambas pestañas) pasó a `position: sticky; top: 0`, igual que `.settings-nav`, para que se sienta anclada al contenido en vez de flotar en el espacio vacío al hacer scroll. La reducción del ancho total del layout (1280px en vez de 1400px) también reduce la sensación de "ayuda perdida a la derecha".

### Status compacto

Causa del bug "Desconectado como barra ancha": `.wa-number-card` es `display:flex; flex-direction:column`, y por default (`align-items: stretch`) cualquier hijo directo —incluido el `<span>` interno de `AlfaTag`— se estira al ancho completo de la columna. Fix: se envolvió el `<AlfaTag>` en un `<div class="wa-number-card__status">` (elemento renderizado por este mismo componente, así que su CSS scoped sí aplica) con `align-self: flex-start`, sin tocar `AlfaTag.razor` ni su CSS compartido. Mismo criterio aplicaría a cualquier otro componente inline-flex usado directo como hijo de un contenedor flex-column.

### Guardar número vs Conectar WhatsApp

"Guardar número" pasó de `btn--primary` a `btn--secondary` para diferenciarse visualmente de "Conectar WhatsApp" (que sigue `btn--primary`, acción principal de conexión). Ningún handler cambió (`SaveNumeroAsync` intacto).

### Feedback contextual — confirmado, no modificado

Se revisó la lógica de `_feedbackScope` (C2.1): al navegar fuera de Canales→WhatsApp, `SelectPrimaryTab`/`SelectChannelTab` limpian el scope a `null`, por lo que el toast/error de una operación WhatsApp no reaparece en Automatización/Integraciones IA/Operación/Soporte. Funciona según lo diseñado; no se tocó código.

### Deuda visual registrada para próximas fases (NO implementada en C2.2)

**Automatización:** `.conv-config-collapsible` claro/legacy (mismo bug de fondo que tenía WhatsApp antes de C2.1), checkboxes legacy, fields legacy, ayuda lateral con el mismo peso que el formulario.

**Integraciones IA:** `panel-card` legacy, metadata `TA_CONFIGURACION`/claves `CONV_*` visibles como protagonistas, **API Key de AlfaKnowledge visible en texto plano** (deuda de seguridad, no solo visual — no tocar en la próxima fase de UI; corresponde a una fase de contrato de secretos, ver `docs/ui/conversaciones-configuracion-redesign.md` sección "Contrato de secretos" y las fases `SEC-1`/`SEC-4` del plan), manual lateral dominante.

**Operación y accesos:** `panel-card` legacy, listas/checks legacy, ayuda lateral con el mismo peso que la configuración, badges técnicos (`CONV_ADMINISTRADORES`, etc.) sin jerarquía reducida.

**Soporte:** `panel-card` legacy, ayuda lateral legacy. El aviso de webhook ya está correctamente contextualizado aquí desde C2.1 — no requiere cambios.

Ninguna de estas categorías se tocó en C2/C2.1/C2.2. Quedan como estaban al cierre de C1.

### Validación técnica

`dotnet build` → 0 errores. `check_catalogo.py` → 68 rutinas, 0 advertencias, 0 errores. `git diff --check` → limpio.

### Servicios/backend (C2.2)

Sin cambios. Archivos tocados: `ConversacionesConfiguracion.razor`, `ConversacionesConfiguracion.razor.css`.

---

## C2.3 — WhatsApp Business Connection Flow

**A diferencia de C2.1/C2.2, esta fase modificó backend** (`WhatsAppWebSessionService.cs`, `worker.mjs`), con permiso explícito del alcance de C2.3, solo en lo necesario para que la conexión QR/pairing code funcione y se auto-provisione. No se tocó Meta API, Instagram, Facebook, Mercado Libre, OAuth, webhooks, routing multibase ni automatizaciones.

### ⚠️ Limitación de entorno — no se pudo probar en vivo

Se confirmó empíricamente (`where node`, `Get-Command node`, búsqueda en todo el PATH) que **Node.js no está instalado en esta máquina**, ni en el PATH de este entorno de trabajo ni a nivel de sistema. Tampoco existe `node_modules` en `src/AlfaCore/Node/WhatsAppWebWorker/` (nunca se corrió `npm install`). Esto significa:

- Es la causa raíz real y verificada de "No se pudo iniciar la sesión real de WhatsApp Web." (ver más abajo).
- **No fue posible ejecutar ninguno de los casos de prueba A/B (QR real, pairing code real) pedidos en esta fase.** No hay Node.js, no hay dependencias del worker, y tampoco hay un teléfono disponible para escanear.
- Todo lo implementado en C2.3 fue verificado por lectura de código + `dotnet build`, no por ejecución real. Antes de aprobar funcionalmente esta fase hace falta, como mínimo: instalar Node.js en el servidor, ejecutar `npm install` en `Node/WhatsAppWebWorker/`, y probar con un teléfono real los casos A y B de la sección 36 del pedido original.

### Causa raíz del error "No se pudo iniciar la sesión real de WhatsApp Web"

Auditado `AppUiOperationService.BuildMessage`: preserva el mensaje real para `InvalidOperationException` y para varios casos de `SqlException`/`Win32Exception` ya mapeados, pero **no tenía un caso para un `Win32Exception` directo lanzado por `Process.Start`** (por ejemplo, "no se encuentra node.exe") — esos caen al mensaje genérico final ("Ocurrió un problema inesperado..."), que es exactamente el texto que aparecía en las capturas. Confirmado: sin Node.js instalado, `Process.Start(startInfo)` en `WhatsAppWebSessionService.StartSessionAsync` lanza ese `Win32Exception`.

**Fix aplicado** (`WhatsAppWebSessionService.cs`): se envolvió `Process.Start` en un `try/catch` específico para `Win32Exception` que relanza un `InvalidOperationException` con mensaje accionable ("no se encontró 'node' en este servidor. Instalá Node.js y ejecutá 'npm install'..."). Como `AppUiOperationService` ya preserva el mensaje de `InvalidOperationException` tal cual, este mensaje SÍ llega al usuario. Cambio acotado a este único método; no se tocó `AppUiOperationService` (compartido por toda la app).

### Auditoría previa a tocar backend (obligatoria, sección 3 del pedido)

- `CONV_WHATSAPP_NUMEROS.PhoneNumberId` es `nvarchar(50) NOT NULL` con `UNIQUE` (migración `2026-08-10-003`). No puede quedar vacío ni duplicado entre filas — esto es lo que impide "crear el registro sin pedir nada" de forma trivial.
- `SaveWhatsAppNumeroAsync` exige `PhoneNumberId` y `Nombre` no vacíos (valida y lanza si faltan) — validación existente, no se tocó.
- `SaveWhatsAppNumeroWebSessionAsync` (la que se llama automáticamente en cada poll de estado) **actualiza solo columnas `Web*`**, nunca `Nombre`/`PhoneNumberId` — confirmado leyendo `FillWhatsAppNumeroParameters`/`BuildWhatsAppNumeroWebUpdateAssignments`. Esto define qué se puede persistir automáticamente después de conectar y qué no.
- `worker.mjs`, al recibir `connection === "open"`, **no capturaba ningún dato de identidad** (ni JID ni teléfono) — solo escribía `state: "CONNECTED"`. Sin esto, no había manera de mostrar el teléfono real ni de deduplicar.
- `StartSessionAsync(idNumero, ...)` siempre relee el número desde la base por `idNumero` — el objeto en memoria de Razor no alcanza; hay que persistir `WebSessionMode`/`WebPhoneNumber` ANTES de llamar a esta función para que el método elegido en la UI realmente se use.
- `StartSessionAsync` llama siempre a `PrepareSessionDirectory(sessionDir, clearAuth: true)`, **incluso sobre una sesión ya conectada** — confirma el riesgo que señalaba la auditoría original (§32 de este pedido). No se modificó el método (sigue haciendo lo mismo), pero la UI ahora nunca ofrece "Conectar WhatsApp" como acción visible sobre un número ya conectado (ver más abajo) — mitigación por el lado de la UI, no del backend.

### Identidad real después de conectar (backend, aditivo)

- `worker.mjs`: en `connection === "open"`, ahora también captura `sock.user.id` (JID) y `sock.user.name` (nombre de WhatsApp), y los escribe en `status.json` como `phoneJid`, `phoneNumber` (dígitos normalizados con `+`) y `accountName`. Campos nuevos, aditivos — un `status.json` viejo sin estos campos sigue deserializando igual.
- `WhatsAppWebSessionService.cs`: `WhatsAppWebWorkerStatus` agrega `PhoneJid`/`PhoneNumber`/`AccountName`. `ApplyStatus` ahora, cuando el estado es `CONNECTED` y hay `PhoneNumber`, escribe `numero.WebPhoneNumber = status.PhoneNumber`. Como esto pasa por el mismo camino ya existente (`LoadStatusAndPersistAsync` → `SaveWhatsAppNumeroWebSessionAsync`), **no hizo falta ningún método ni columna nueva**: el teléfono real queda persistido automáticamente, y la UI ya mostraba `WebPhoneNumber` con prioridad sobre `PhoneNumberId` desde C2.1.
- No se implementó renombrado automático del campo `Nombre` (el placeholder "WhatsApp Business N" queda como está) porque requeriría un segundo `SaveWhatsAppNumeroAsync` completo no verificado en esta fase; se prefirió no arriesgar un write adicional sin poder probarlo. Reportado como posible mejora futura.

### Auto-provisioning (sin backend nuevo)

En vez de crear un método de servicio nuevo, `ConectarWhatsAppBusinessAsync`/`IniciarNuevoWhatsAppBusinessAsync` (código nuevo, solo en el componente Razor) orquestan métodos **ya existentes y probados**:

1. Genera `PhoneNumberId = "WEBPENDING-" + Guid` (único, cumple `NOT NULL`+`UNIQUE`, reconocible como placeholder) y `Nombre = "WhatsApp Business {N}"`.
2. Llama a `ConfigSvc.SaveWhatsAppNumeroAsync(...)` (sin modificar) para crear la fila.
3. Recarga números y llama a `GenerateWhatsAppWebPairingAsync(...)` (sin modificar) para iniciar el pairing real.

Ningún servicio ni SQL nuevo. El "Phone Number ID" técnico nunca se le pide al cliente en el flujo Business; sigue existiendo internamente porque la columna lo exige, pero es invisible para el usuario.

### Deduplicación — parcial, verificable por código

Antes de crear una cuenta nueva por el flujo de **teléfono** (el único caso donde se conoce el número de destino antes de conectar), se compara contra los números existentes con `IsWebSessionReady = true` por dígitos normalizados; si coincide, se avisa y no se crea una fila nueva. **No se implementó deduplicación por JID/estado post-conexión** (por ejemplo, si dos pairings QR distintos terminan siendo la misma cuenta real) porque depende de datos que solo puede confirmar el worker en vivo y no hay forma de probarlo en este entorno — deuda explícita, no implementada.

### Polling mientras el pairing está pendiente

`StartPolling`/`StopPolling`/`PollPairingStatusAsync`: cada 4 segundos llaman a `RefreshWhatsAppWebSessionAsync(numero, silent: true)` (nuevo parámetro opcional, default `false`, no cambia el botón manual de refresco) mientras el número no esté conectado ni tenga error. Se detiene al conectar, fallar, cancelar (`ClearWhatsAppWebPairingAsync`), desvincular, o al destruirse el componente (`Dispose` llama `StopPolling`). No es un timer global: vive solo mientras hay un pairing en curso.

### UI Business — resultado

- **Sin número:** estado vacío ("Conectá tu WhatsApp para atender conversaciones desde AlfaCore") + botón "Conectar WhatsApp" → selector Código QR / Número de teléfono.
- **"Agregar número" (Nombre + Phone Number ID manual) se quitó de Business** y se movió a WhatsApp API bajo "Números por Phone Number ID", con una nota explícita de que los números por QR se administran en Business.
- **Número conectado:** ya no muestra "Conectar WhatsApp" (evita el borrado accidental de auth de §32); muestra solo "Desvincular" con `AlfaConfirmDialog` (renombrado de "Detener sesión" a "Desvincular", mensaje actualizado).
- **Pairing pendiente:** "Actualizar código" (icon button) + "Cancelar" (sin confirmación — cancelar algo que nunca llegó a conectar es de bajo riesgo).
- **Desconectado sin pairing:** "Conectar WhatsApp" → selector de método.
- Con ≥1 número ya existente: "Conectar otro WhatsApp" (ya no "Agregar número").
- Todos los botones de esta pantalla (Business y API) usan `AlfaButton`/`AlfaIconButton`; no queda ningún `<button class="btn ...">` dentro de WhatsApp.

### Botón "Recargar" del Context Toolbar — revisado, sin cambios

`MainPageHeader.razor` (compartido por Usuarios/Técnicos/Clientes y ahora Conversaciones) renderiza sus acciones con un `<button class="main-page-header__action">` propio, con CSS scoped `--alfa-*` — no es Bootstrap `.btn` legacy, es la implementación de referencia del propio Context Toolbar que usan todos los módulos migrados. No se creó ninguna excepción visual para Configuración; se comprobó que es exactamente el mismo mecanismo que ya usa Usuarios.razor. No se modificó `MainPageHeader.razor` (es compartido por toda la app, fuera de alcance de esta fase).

### Backward compatibility

- Números/filas existentes (con `PhoneNumberId` real de Meta o ya conectados) siguen funcionando igual: `WebInstanceName`, `WebSessionMode`, runtime, todo intacto.
- Ningún número existente necesita "recrearse"; simplemente aparece con su estado real (Conectado/Desconectado/Error) apenas carga la pantalla.
- Los prefijos `WEBPENDING-` son solo un identificador técnico interno del placeholder — no afectan ninguna lectura/escritura existente de `PhoneNumberId` real.

### Archivos modificados en C2.3

Backend: `src/AlfaCore/Services/WhatsAppWebSessionService.cs`, `src/AlfaCore/Node/WhatsAppWebWorker/worker.mjs`.
UI: `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`, `ConversacionesConfiguracion.razor.css`.
Docs: este archivo.

No se tocó ningún archivo de Meta/Instagram/Facebook/Mercado Libre/OAuth/webhooks/routing/automatizaciones.

### Validación técnica ejecutada

`dotnet build` → 0 errores. `check_catalogo.py` → 68 rutinas, 0 advertencias, 0 errores. `git diff --check` → limpio.

### Validación funcional — EN CURSO (probado en vivo contra ALFANET2007, número de prueba real)

Node.js instalado (`winget install OpenJS.NodeJS.LTS`, v24.19.0) y dependencias del worker instaladas (`npm ci`). Se probó en vivo contra la base de prueba **ALFANET2007** (número "AlfaNet Pruebas 3647") con un teléfono real. Bugs reales encontrados y corregidos durante la prueba (no eran hipótesis, se reprodujeron en vivo):

- **Migración SQL** (`2026-08-18-010__conversaciones_whatsapp_web_por_numero.sql`): faltaba un `GO` entre el `ALTER TABLE ADD` y el `UPDATE` que referenciaba las columnas nuevas → error 207 "Invalid column name". Corregido agregando el `GO`.
- **`SaveWhatsAppNumeroWebSessionAsync`**: SQL inválido `SET 1 = 1, ...`. Corregido para empezar el `SET` con una columna real.
- **`SaveWhatsAppNumeroAsync`**: `GetTableColumnsAsync` se llamaba después de `BeginTransactionAsync` sobre la misma conexión sin pasar la transacción al `SqlCommand` interno → "BeginExecuteReader requires a transaction". Corregido reordenando la llamada antes de abrir la transacción.
- **JSON case-sensitivity** en `ProcessWhatsAppWebInboxAsync`: el worker escribe camelCase, el deserializer por default es case-sensitive → nombre/teléfono/texto llegaban vacíos. Corregido con `JsonSerializerDefaults.Web`.
- **`RequireConversationAsync`** (bug de plataforma, no específico de esta feature): nunca seleccionaba `IdNumeroWhatsApp` en el `SELECT` pese a que la propiedad existía en el DTO → **todos** los envíos por WhatsApp Web fallaban con "La conversación no tiene un número de WhatsApp Web asociado." en las 135 bases de SaaS. Corregido agregando la columna al `SELECT` y al mapeo.
- **LID (linked ID) de WhatsApp**: `remoteJid` en mensajes entrantes puede ser un identificador pseudónimo `@lid`, no el teléfono real; el teléfono real está en `remoteJidAlt`. El worker usaba siempre `remoteJid`, mostrando números como `+232083003801723`. Corregido en `worker.mjs` para preferir `remoteJidAlt`. **Nota:** conversaciones ya creadas antes del fix (ej. conversación 10330) mantienen el teléfono incorrecto; no se autocorrigen, solo las conversaciones nuevas quedan bien.
- **Race de envío justo después de conectar**: `processOutbox()` en `worker.mjs` solo chequeaba `!sock` (el objeto existe aunque el socket esté cerrado durante una reconexión transitoria), no si el socket estaba realmente abierto. Baileys hace una resync breve justo después de "connection: open" que cierra y reabre el socket; los mensajes en cola durante esa ventana fallaban con `"Error: Connection Closed"` y quedaban marcados `ERROR_ENVIO` aunque el número mostrara "Conectado". Corregido agregando un flag `isSocketOpen` que se pone en `true`/`false` en los eventos `connection.update`; `processOutbox` ahora espera a que esté abierto antes de intentar enviar, dejando el comando en la cola en vez de descartarlo con error.

**Gaps de producto identificados, no corregidos (fuera de alcance de C2.3, requieren decisión de producto):**

- `ResolveWhatsAppDeliveryProvider` (lee `ProviderMode`/`DefaultProvider` globales) no tiene en cuenta el estado de conexión por número — afecta el ruteo Meta Cloud vs WhatsApp Web en las 135 bases. Se le indicó al usuario cambiar "Modo del canal" a "Convivencia" y "Proveedor predeterminado" a "WhatsApp Business (QR)" manualmente vía UI como workaround, sin cambio de código.
- **No hay reconexión automática de sesiones ya conectadas**: si el worker de un número ya "Conectado" se cae (crash, conflicto de dispositivo, reinicio del servidor), nadie lo vuelve a levantar — no hay watchdog ni relanzamiento en el arranque de la app. El único camino hoy es "Desvincular" + "Conectar WhatsApp" de nuevo, que **siempre** pide un QR/código nuevo (`StartSessionAsync` hace `PrepareSessionDirectory(clearAuth: true)` incondicionalmente) — no hay manera de reintentar reutilizando las credenciales ya vinculadas.
- **Conflicto de multi-dispositivo real observado**: escanear el QR repetidamente con el mismo teléfono en varios ciclos de prueba (antes de tener el fix de arriba) generó varios dispositivos vinculados en simultáneo, y WhatsApp terminó desconectando/deslogueando la sesión más vieja por conflicto (`Stream Errored (conflict)` → luego `loggedOut` real, confirmado reintentando con las credenciales guardadas). Además, cada intento fallido deja un proceso Node huérfano corriendo en el servidor esperando un QR que nunca se escanea (se limpiaron manualmente 6 procesos + 5 carpetas de sesión vacías durante esta prueba). No existe today una función de "eliminar número"/limpieza de sesiones huérfanas en la UI.
- **Meta marcó el número de prueba como sospechoso de spam** tras las múltiples vinculaciones/desvinculaciones seguidas en la misma sesión de prueba — la validación funcional quedó pausada por esto, a reintentar más adelante (espaciando los reintentos, y probando con el método de código de 8 dígitos en vez de QR para reducir la frecuencia de "nuevo dispositivo").

**Pendiente para cerrar C2.3:**

1. Reintentar la vinculación (una sola vez, sin reintentos seguidos) una vez que el número de prueba deje de estar marcado por Meta.
2. Confirmar envío/recepción end-to-end estable con el fix de `isSocketOpen` en efecto.
3. Confirmar persistencia del estado "Conectado" tras recargar la página.
4. Probar el método B (pairing code de 8 dígitos) al menos una vez.
5. Decidir si los gaps de producto listados arriba (reconexión automática, limpieza de huérfanos, `ResolveWhatsAppDeliveryProvider` por número) se resuelven en esta fase o se documentan como deuda para una fase futura.

Sin commit todavía — sigue pendiente de aprobación funcional final del usuario.

---

## C2.4 — QR lifecycle + scroll vertical + cierre del flujo Business

Continuación de C2.3. No se rediseñó arquitectura: se corrigió el lifecycle del QR (mostrarlo/renovarlo solo cuando corresponde) y se agregó scroll vertical usable a `Settings Workspace`. `Program.cs` **no se tocó en esta fase** — el fix de `ResolveStaticAsset()` (bundle `AlfaCore.styles.css` más reciente por `LastWriteTimeUtc` en vez de priorizar Debug ciegamente, aplicado durante el diagnóstico de CSS isolation de esta misma sesión) queda preservado tal cual.

### Problema real antes de C2.4

El polling (`StartPolling`/`PollPairingStatusAsync`, ya existente desde C2.3) nunca generaba QR solo, y `LoadNumerosAsync` nunca arrancaba el polling — eso ya estaba bien. Pero el markup mostraba `numero.HasWebPairingQr`/`HasWebPairingCode` **directo desde lo que quedó guardado en base**, sin chequear si:

1. ese QR/código seguía vigente (`WebPairingExpiresAtUtc` pudo haber quedado en el pasado, guardado por el último poll antes de cerrar el navegador);
2. el pairing fue iniciado en la sesión de UI actual, o es un resto de una sesión anterior (recarga de página, u otra pestaña).

Resultado: abrir/recargar WhatsApp Business podía mostrar un QR viejo/vencido como si fuera válido, sin ningún mecanismo para renovarlo (nadie volvía a pollear porque `StartPolling` nunca se re-disparaba solo).

### Gate de visibilidad: `_pairingFlowActiveIds`

Nuevo `HashSet<int> _pairingFlowActiveIds`, en memoria del componente (no persiste, no sobrevive a un reload — eso es intencional). Un `IdNumero` entra al set únicamente en `StartPolling`, que solo se llama desde `GenerateWhatsAppWebPairingAsync` (es decir, tras un click explícito en "Código QR"/"Generar código"/"Actualizar código"). Sale del set al conectar, cancelar (`ClearWhatsAppWebPairingAsync`) o desvincular.

El QR/código guardado en base **solo se renderiza** si se cumplen las tres condiciones a la vez (`ShouldShowPairingSurface`): el `IdNumero` está en `_pairingFlowActiveIds`, no está conectado, y `WebPairingExpiresAtUtc > DateTime.UtcNow`. Si cualquiera de las tres falla (sesión nueva, recarga, o vencido), la tarjeta cae al estado neutral `[ Conectar WhatsApp ]`/selector de método — nunca precarga ni regenera solo. `CONNECTED` sigue teniendo prioridad absoluta sobre cualquier dato de pairing residual (ya lo garantizaba `ApplyStatus` en `WhatsAppWebSessionService`, que limpia `qrPayload`/`pairingCode` al conectar; ahora además `IsWebSessionReady` corta la rama de pairing explícitamente en la UI).

### Auto-renovación durante pairing activo — ya la hacía Baileys, faltaba reflejarla bien

Confirmado leyendo `worker.mjs`: mientras el socket de Baileys sigue abierto sin escanear, WhatsApp empuja un `qr` nuevo cada ~2 minutos y el worker lo escribe en `status.json` con `expiresAtUtc` fresco (`handleConnectionUpdate`, sin cambios en esta fase). El polling de 4s (sin cambios en su timing) ya reflejaba ese nuevo QR al refrescar — lo que faltaba era una transición visual clara mientras tanto. Se agregó:

- **Actualizando código QR...** (`IsPairingRegenerating`): se muestra cuando el pairing sigue activo pero el QR/código actual ya venció y todavía no llegó uno nuevo — evita dejar un QR vencido visible.
- **Guard de staleness**: `PollPairingStatusAsync` compara `WebPairingGeneratedAtUtc` entre ciclos; si el QR sigue vencido y no cambió durante `PairingStaleCycleLimit` (5) ciclos de 4s (~20s) tras vencer, se considera que la renovación falló.
- **Guard de fallos de red/IO**: si `RefreshSessionAsync` falla `PairingFailureLimit` (3) veces seguidas, mismo resultado.
- **Estado de error terminal**: ambos guards detienen el polling (`StopPollingWithRegenerateError`) y muestran "No pudimos actualizar el código QR." con `AlfaButton` **Reintentar** (llama a `GenerateWhatsAppWebPairingAsync` de nuevo — sesión nueva, no un simple refresh) y **Cancelar** (`ClearWhatsAppWebPairingAsync`, ya existente). Nunca reintenta solo.
- `RefreshWhatsAppWebSessionAsync` (usado también por el botón manual "Actualizar código", sin cambio de firma pública) ahora delega en `RefreshWhatsAppWebSessionCoreAsync`, que devuelve `bool` para que el polling pueda distinguir éxito/fallo sin duplicar la llamada al servicio.

### Navegación — detener y retomar el polling

`SelectPrimaryTab`, `SelectChannelTab`/`OnChannelTabChanged` y `OnWhatsAppSectionChanged` ahora llaman a `SyncWhatsAppPollingWithNavigation()`: si el usuario deja la vista exacta Canales→WhatsApp→WhatsApp Business, corta el polling (`StopPolling`) — no sigue generando QR en background en Automatización, WhatsApp API, Instagram, etc. Si vuelve a esa vista, `ResumePollingIfPairingActive()` retoma el polling solo si queda un pairing propio de esta sesión sin conectar y sin error terminal registrado (si hay error, no reintenta solo — coherente con "el reintento vuelve a ser explícito"). `Dispose()` (recarga, salir de la pantalla) no cambió: sigue llamando `StopPolling()`; como `_pairingFlowActiveIds` vive en memoria del componente, una instancia nueva (F5) arranca vacía — CTA neutral, nunca retoma un pairing pendiente entre recargas.

### Multinúmero — verificado, no requirió cambios

Cada número usa su propio `WebInstanceName`/carpeta de sesión/proceso Node (`WhatsAppWebSessionService`, sin cambios). `_pairingFlowActiveIds` es un `HashSet<int>` por `IdNumero`: conectar la Cuenta A no borra su entrada al iniciar un pairing B (solo el polling en primer plano es de a uno a la vez, ya era así desde C2.3 — no afecta el estado persistido de A). No se tocó backend para esto.

### Texto orientado al cliente

Se quitó el timestamp crudo "Vence: dd/MM/yyyy HH:mm:ss" (`GetWhatsAppWebPairingExpirationLabel`, eliminado por quedar sin uso) del QR y del código por teléfono; se reemplazó por "Si el código vence, lo actualizamos automáticamente." en ambos. El resto de la copy (instrucciones de escaneo, pairing code) no cambió.

### Scroll vertical — causa real y fix

El shell `AlfaDesignPilot` (`alfacore-design.css`) ya define `.shell.shell--alfa-design .shell__content { flex:1 1 auto; overflow:hidden; }` — el `body`/`html` **nunca hicieron scroll** en esta pantalla; el contenido que excedía el viewport quedaba directamente cortado por ese `overflow:hidden`, no "scrolleable pero incómodo". El fix es enteramente local a este componente (mismo patrón que ya usa `Conversaciones.razor.css` para pisar `.page-grid` con más especificidad vía `[b-scope]`, confirmado con grep sobre el bundle compilado):

- `.settings-page` (nueva regla): `display:flex; flex-direction:column; flex:1 1 auto; height:100%; overflow:hidden;` — reemplaza el `display:grid` heredado de `::deep .page-grid` (MainLayout) para este componente puntual, sin tocar esa regla compartida.
- `.settings-workspace`: pasa de `align-items:flex-start` a `align-items:stretch` + `flex:1 1 auto; overflow:hidden;` — reparte la altura completa entre nav y contenido en vez de dejar que ambos crezcan a su alto de contenido.
- `.settings-nav`: `align-self:flex-start` (no se estira a la altura completa, queda a su alto natural arriba) + `max-height:100%; overflow-y:auto;` como red de seguridad si algún día crece más que el viewport (hoy no ocurre con 6 items).
- `.settings-content`: agrega `min-height:0; overflow-y:auto;` — es el **único** contenedor que hace scroll real. `min-height:0` es necesario porque sin eso un hijo flex no se encoge por debajo de su contenido y el overflow nunca se activa.

Un solo scroll container (`.settings-content`), aplica a las 6 categorías por igual (Resumen, Canales, Automatización, Integraciones IA, Operación y accesos, Soporte) porque todas viven dentro del mismo `.settings-content` — no fue necesario tocar cada categoría por separado, ni migrarlas visualmente.

`.wa-help` ya tenía `position:sticky; top:0` desde C2.2, pero no tenía efecto real porque nada scrolleaba. Con `.settings-content` como scroll ancestor real, ahora sticky funciona sin cambios adicionales en esa regla.

En el breakpoint de 1024px (`.settings-workspace{flex-direction:column}`, ya existente desde C1), `.settings-nav` pasa a fila horizontal con wrap y `.settings-content` sigue siendo el único scroll vertical — no se crea scroll anidado.

No se tocó `body`/`html` en ningún punto (ownership del scroll queda contenido dentro del shell/workspace de Configuración, sin riesgo para otras pantallas).

### Build con la app abierta

Igual que en el diagnóstico de CSS isolation de esta sesión: `dotnet build` con `AlfaCore.exe` corriendo bloquea la copia del `.exe` (`MSB3027`, no un error de compilación — la compilación en sí terminó con 0 errores). Se pidió confirmación antes de detener la instancia en ambos casos (fix de `Program.cs` y luego este build), siguiendo la regla de no matar procesos en uso sin avisar.

### Archivos modificados en C2.4

`src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`, `ConversacionesConfiguracion.razor.css`, este documento. **No se tocó** `WhatsAppWebSessionService.cs`, `worker.mjs` ni `Program.cs` en esta fase — la auto-renovación cada ~2 minutos ya la hacía Baileys; solo hacía falta que la UI la respetara correctamente.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln` → 0 errores (3 warnings preexistentes, sin relación). `check_catalogo.py` → 68 rutinas, 0 advertencias, 0 errores. `git diff --check` → limpio.

### ⚠️ Validación funcional — NO ejecutada en este entorno

No hay navegador ni teléfono disponible en este entorno de trabajo para ejecutar los casos de prueba A–F pedidos (QR inicial manual, auto-renovación real al vencer, cancelar, navegar fuera, escanear con teléfono real, F5 tras conectar, QR vencido al reabrir, multicuenta, pairing code). Todo lo de esta fase fue verificado por lectura de código + build, igual que en C2.3. Falta la prueba real del usuario contra `ALFANET2007` (o la base de prueba que corresponda) antes de dar C2/C2.4 por aprobado funcionalmente.

Sin commit todavía — C2 sigue **PENDIENTE DE APROBACIÓN FINAL** hasta la validación visual y funcional real del usuario.

---

## C2.4a — Fix distribución del worker + estado UI consistente ante fallo

Bloqueo reproducido: al tocar "Conectar WhatsApp" en el build Release corriendo desde `bin\Release\net8.0\AlfaCore.exe`, aparecía "No existe el worker de WhatsApp Web en C:\...\bin\Release\net8.0\Node\WhatsAppWebWorker\worker.mjs" pese a que el worker se había probado manualmente y sí llega a `QR_READY` con un QR real de Baileys corriendo directo desde `src/AlfaCore/Node/WhatsAppWebWorker/`.

### Causa raíz (confirmada leyendo código + verificando disco, no hipótesis)

Dos problemas independientes, ambos necesarios para el bug completo:

1. **El worker nunca se copiaba al output.** `AlfaCore.csproj` no tenía ningún item `Content`/`None` con `CopyToOutputDirectory` para `Node\WhatsAppWebWorker\**` — se confirmó con `ls` que `bin\Debug\net8.0\Node\` y `bin\Release\net8.0\Node\` no existían antes del fix.
2. **`WhatsAppWebSessionService.GetWorkerDirectory()` resolvía por `environment.ContentRootPath`**, que en ASP.NET Core por defecto es el *current working directory* del proceso al arrancar (no la carpeta del `.exe`). Corriendo `AlfaCore.exe` con cwd = su propia carpeta de salida (como lo hace un doble click, una tarea programada, o `Start-Process -WorkingDirectory`), `ContentRootPath` coincide con esa carpeta y el bug (1) queda expuesto directamente. Si además alguna vez se lanzara con otro cwd, `ContentRootPath` apuntaría a un tercer lugar más, agravando el problema — la ruta calculada nunca era determinística.

El caso que "sí funcionó" (worker probado manualmente) fue corriendo `node worker.mjs` directo dentro de `src/AlfaCore/Node/WhatsAppWebWorker/` — nunca pasó por `AlfaCore.exe` ni por `GetWorkerDirectory()`, por eso no exponía ninguno de los dos bugs.

### Fix de distribución (`AlfaCore.csproj`)

Se agregaron 3 items `<None Update>` (no `<Content Include>`, para no duplicar con el glob por defecto del SDK que ya incluye estos archivos como `None`) que copian `worker.mjs`, `package.json` y `package-lock.json` a la salida (`CopyToOutputDirectory`/`CopyToPublishDirectory = PreserveNewest`), preservando la carpeta `Node\WhatsAppWebWorker\`. Se agregó también `<None Remove="Node\WhatsAppWebWorker\node_modules\**" />` (mismo criterio ya usado para `App_Data\whatsapp-web`) para que MSBuild no evalúe `node_modules` como items de proyecto si un dev lo tiene instalado localmente.

**`node_modules` NO se copia** (deliberado, no es un olvido): son miles de archivos, no está versionado (`.gitignore` ya lo excluía) y copiarlo vía glob de MSBuild en cada build sería lento y frágil. Verificado con `dotnet build` en Debug y Release: `worker.mjs`/`package.json`/`package-lock.json` aparecen correctamente en ambas carpetas de salida.

### Contrato de distribución (para cerrar C2.4a)

- **Desarrollo (`dotnet run`/F5):** si el build ya copió el worker a `bin\Debug\net8.0\Node\WhatsAppWebWorker\`, se usa esa copia. Si no (primer clone sin build todavía), `GetWorkerDirectory()` cae al árbol fuente `src/AlfaCore/Node/WhatsAppWebWorker/` **solo si `IHostEnvironment.IsDevelopment()`** — mismo patrón ya usado en `Program.cs` para `wwwroot`/`scopedcss`, no es una búsqueda nueva ni arbitraria por disco.
- **Release/publish/servicio:** siempre usa `AppContext.BaseDirectory\Node\WhatsAppWebWorker` (la carpeta real de salida, determinística sin importar cwd ni modo de arranque).
- **En ambos casos hace falta `npm ci` una vez** dentro de la carpeta `Node\WhatsAppWebWorker` que corresponda (`src/AlfaCore/Node/WhatsAppWebWorker` en desarrollo, o la carpeta de salida real — `bin/Release/net8.0/Node/WhatsAppWebWorker` o el `publish/.../Node/WhatsAppWebWorker` correspondiente — para Release/publish/servicio). No se automatizó ese paso (instalar dependencias npm como parte del build de un `.csproj` de C# no es apropiado); queda como paso manual de setup, igual que ya estaba documentado en C2.3.

### Bug UI — "Esperando escaneo" falso tras un fallo de arranque

`GetNumeroEstadoLabel`/`GetNumeroEstadoTone` leen `numero.WebSessionStatus` directo desde lo persistido, sin relación con si hay un pairing activo de verdad en esta sesión. Si `StartSessionAsync` falla **antes** de tocar el worker (exactamente el caso de "no existe el worker": `EnsureWorkerFilesExist()` tira la excepción antes de `PrepareSessionDirectory`/`Process.Start`), un `WebSessionStatus = PENDING_QR` que hubiera quedado de un intento anterior (ej. de la validación en vivo documentada en C2.3) seguía mostrando "Esperando escaneo" con el `AlfaTag` en warning, aunque el intento actual falló por completo y no hay ningún proceso corriendo. Coincide con la captura reportada: notificación de error correcta + tag "Esperando escaneo" inconsistente al mismo tiempo.

**Fix acotado** en `GenerateWhatsAppWebPairingAsync` (rama de fallo, `ConversacionesConfiguracion.razor`): al fallar, se saca el `IdNumero` de `_pairingFlowActiveIds` (defensivo) y, si el estado mostrado seguía en `PENDING_QR`, se corrige a `DISCONNECTED` **solo en memoria** (no se persiste, no se toca DB). No se borra `auth`, credenciales, `PhoneNumberId`, usuarios ni el registro del número — nada de eso está en juego en esta rama, que corre antes de que el servicio toque el filesystem de la sesión. El próximo `LoadNumerosAsync`/recarga vuelve a leer el estado real persistido, así que esta corrección es puramente para no confundir al usuario en el momento del fallo. Tras el fix, la tarjeta vuelve a mostrar `[ Conectar WhatsApp ]` y permite reintentar.

No se tocó ningún otro punto del lifecycle de C2.4 (`_pairingFlowActiveIds`, polling, `expiresAtUtc`, auto-renew) — el resto de esa lógica no estaba involucrada en este bug.

### `Program.cs` — preservado, no tocado en esta sub-fase

El fix de `ResolveStaticAsset()` (bundle `AlfaCore.styles.css` más reciente por `LastWriteTimeUtc`) sigue intacto. `Program.cs` no aparece en el diff de C2.4a.

### Archivos modificados en C2.4a

`src/AlfaCore/AlfaCore.csproj`, `src/AlfaCore/Services/WhatsAppWebSessionService.cs`, `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor`, este documento.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln` (Debug y Release) → 0 errores. `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio. Se confirmó por lectura + inspección de disco que `worker.mjs` queda exactamente en la ruta que `AppContext.BaseDirectory` resuelve para el `.exe` en ejecución (`bin/Release/net8.0/Node/WhatsAppWebWorker/worker.mjs`), eliminando el error "No existe el worker..." en el próximo intento.

### ⚠️ Limitación de entorno — smoke test NO ejecutado, `npm ci` pendiente

Se buscó Node.js exhaustivamente en este entorno de trabajo (`where.exe node`/`npm`, PATH del proceso, registro de desinstalación de Windows, `winget list`, `Program Files`/`LocalAppData\Programs`): **no se encontró en ninguna parte**, pese a que C2.3 documentó una instalación exitosa (`winget install OpenJS.NodeJS.LTS`, v24.19.0) durante una sesión de validación anterior. No fue posible determinar si esa instalación ya no está disponible en este entorno de trabajo puntual, o si corresponde a otra sesión/máquina — se reporta como hallazgo, no se asume ninguna de las dos.

Consecuencia concreta: **no se pudo ejecutar `npm ci` ni el smoke test desde la carpeta de salida** (`bin/Release/net8.0/Node/WhatsAppWebWorker` no tiene `node_modules` todavía), ni hacer click real en "Conectar WhatsApp" en el navegador (sin herramienta de automatización de browser disponible en este entorno). El fix de distribución/resolución de rutas está verificado por código + presencia de archivos en disco, no por ejecución end-to-end.

**Pendiente para que el usuario complete la validación de C2.4a:**

1. Confirmar que Node.js está disponible en el entorno real donde corre `AlfaCore.exe`.
2. Ejecutar `npm ci` dentro de `src/AlfaCore/bin/Release/net8.0/Node/WhatsAppWebWorker/` (la carpeta de salida real, no el árbol fuente, ya que Release usa esa ruta).
3. Entrar a WhatsApp Business → Conectar WhatsApp → Código QR y confirmar que **ya no aparece** "No existe el worker..." y que se ve un QR real.
4. Recién ahí seguir con la batería completa de C2.4 (expiración, auto-renew, cancelar, scan, reload, multicuenta) — no se ejecuta automáticamente, según lo pedido.

Sin commit todavía.

---

## C2.4b — Runtime real preparado + smoke test del worker desde output real

Continuación directa de C2.4a. Objetivo: dejar el runtime de esta PC en condiciones de generar un QR real desde AlfaCore. No se tocó código de aplicación en esta sub-fase (0 archivos `.razor`/`.cs`/`.csproj` modificados) — todo lo hecho acá es preparación de entorno (Node, `npm ci`, verificación).

### Instancia real verificada

`AlfaCore.exe`, `bin\Release\net8.0\`, confirmado por PID/CommandLine — no se asumió Debug/Release, se verificó. `AppContext.BaseDirectory` de esa instancia = `C:\dev\AlfaCore\src\AlfaCore\bin\Release\net8.0\`, por lo tanto `GetWorkerDirectory()` resuelve a `...\bin\Release\net8.0\Node\WhatsAppWebWorker\`. Confirmado por disco: `worker.mjs`, `package.json`, `package-lock.json` presentes ahí (heredado de C2.4a).

### Node.js — instalado en esta sesión

No estaba instalado: se verificó con `where.exe`/`Get-Command` (sin resultado) y, de forma más concluyente, leyendo directamente `Path` de **Machine** y **User** desde el registro (`[System.Environment]::GetEnvironmentVariable('Path','Machine'/'User')`) — ninguno tenía ninguna entrada de Node. No era un caso de "PATH no refrescado" (§4 del pedido): Node genuinamente no estaba instalado en el sistema.

Instalado con `winget install --id OpenJS.NodeJS.LTS --source winget` (confirmado, con autorización) → **Node v24.19.0**, **npm 11.17.0**, en `C:\Program Files\nodejs\node.exe`.

### PATH heredado — proceso viejo vs proceso nuevo

Confirmado el escenario exacto del §4/§7 del pedido: la instancia de AlfaCore que ya estaba corriendo (arrancada antes de instalar Node) conservó el PATH viejo — un proceso Windows no recibe cambios de PATH posteriores a su arranque. Se avisó explícitamente antes de tocar nada ("Necesito reiniciar AlfaCore para que Process.Start encuentre Node"), se pidió autorización, y solo entonces se detuvo **únicamente** el PID que servía `localhost:5055` y se relanzó el mismo build (`bin\Release\net8.0\AlfaCore.exe`) desde una sesión con el PATH ya releído del registro — así el proceso nuevo lo hereda correctamente. No se reinició Windows, no se tocaron otros procesos.

**Nota de entorno:** cada invocación nueva de la herramienta con la que trabajo en esta sesión sigue heredando un PATH desactualizado (no ve Node salvo que yo lo reconstruya explícitamente desde el registro en ese mismo bloque de comandos) — es una característica del proceso que hospeda esta sesión de trabajo, no del sistema operativo ni de AlfaCore. El registro (Machine/User Path) ya tiene a Node correctamente desde la instalación.

### `npm ci` — dependencias instaladas en la carpeta de salida REAL

Ejecutado en `C:\dev\AlfaCore\src\AlfaCore\bin\Release\net8.0\Node\WhatsAppWebWorker\` (la salida real, no el árbol fuente): **69 paquetes instalados, 0 vulnerabilidades**, usando el `package-lock.json` ya copiado por el build. No se corrió `npm update`/`audit fix`/instalación global. `node_modules` confirmado presente (`@whiskeysockets/baileys` y `pino` verificados explícitamente) y confirmado **ausente de `git status`** — no se tocó `.gitignore` (ya lo excluía).

npm avisó que dos scripts de instalación (`baileys` preinstall de chequeo de engine, `protobufjs` postinstall) quedaron sin correr por la política `allow-scripts` de npm reciente. No se aprobaron manualmente (no fue necesario): el smoke test siguiente confirmó que Baileys carga y genera QR real sin esos scripts.

### Smoke test — worker real desde el output real

`node worker.mjs start <sesión temporal en scratchpad> QR "" 0 wa-smoke-test`, ejecutado directamente en `bin\Release\net8.0\Node\WhatsAppWebWorker\` (mismo `WorkingDirectory` que usa `WhatsAppWebSessionService`). Resultado en ~15s:

```
state=QR_READY
hasQrPayload=True (277 caracteres)
generatedAtUtc / expiresAtUtc reales (ventana de 2 minutos)
error=(vacío)
```

No se usó auth real ni se tocó `App_Data\whatsapp-web`. Sesión temporal y proceso de prueba limpiados después (carpeta borrada, proceso detenido). Esto confirma Node + dependencias + worker + Baileys funcionando end-to-end **desde la ruta física exacta que usa el build Release** — no desde el árbol fuente.

### Lo que NO pude verificar yo mismo (limitación de esta sesión, no del fix)

No tengo herramienta de automatización de navegador en esta sesión de trabajo, y `StartSessionAsync` solo se invoca desde el circuito interactivo de Blazor (no hay endpoint HTTP para dispararlo por `curl`). Por lo tanto **no pude hacer clic en "Conectar WhatsApp" dentro de AlfaCore yo mismo** para confirmar `Process.Start` end-to-end vía la UI real. Lo que sí queda demostrado por partes independientes:

- la instancia corriendo resuelve el worker en la ruta correcta (C2.4a + verificado de nuevo acá);
- esa ruta tiene todo lo necesario para que Baileys llegue a `QR_READY` (smoke test);
- la instancia fue reiniciada después de instalar Node, con el PATH corregido pasado explícitamente al proceso nuevo.

Falta el último eslabón (el click real en el navegador) — pedido al usuario para cerrar el checkpoint del §17 del pedido ("QR real generado desde AlfaCore").

### `dotnet publish` — verificado explícitamente

`dotnet publish AlfaCore.csproj --configuration Release -o <carpeta temporal>`: `worker.mjs`, `package.json`, `package-lock.json` aparecen en `<publish>\Node\WhatsAppWebWorker\`, `node_modules` correctamente ausente (tal cual documentado en C2.4a). Carpeta temporal borrada después, no se desplegó nada.

### Deuda de deployment (registrada, no resuelta ahora)

Automatizar la preparación completa del worker en un deploy (Node + `npm ci`) sin versionar `node_modules` — hoy es un paso manual documentado. Candidato a script de bootstrap de deployment en una fase futura; no se resuelve ahora porque el QR local ya está en condiciones de probarse.

### Archivos modificados en C2.4b

Ninguno de código. Solo este documento. (Cambios de entorno: Node.js instalado a nivel de sistema, `node_modules` instalado en `bin\Release\net8.0\Node\WhatsAppWebWorker\` — no versionado, no forma parte del diff de git).

### Validación técnica ejecutada

`dotnet build AlfaCore.sln --configuration Release` → 0 errores. `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio. `git status` → mismos 6 archivos de C2.4/C2.4a, sin cambios nuevos.

Sin commit todavía. **Falta el clic real del usuario en "Conectar WhatsApp" para confirmar el checkpoint del §17** antes de seguir con expiración/cancelar/scan/reload/multicuenta.

---

## C2.5 — WhatsApp Business operativo: envío real + routing multicuenta + filtrado de Status

**Avance confirmado por prueba real del usuario:** QR real, sesión conectada, mensajes entrantes reales llegando a Conversaciones. C2.5 corrige los dos bugs funcionales bloqueantes que aparecieron con uso real: mensajes salientes que quedaban en "Sin configuración" y un Estado de WhatsApp que creó una conversación fantasma ("Springfield").

### Bug 1 — "Sin configuración" en salientes

**Causa raíz confirmada por lectura de código** (no hipótesis): `ResolveWhatsAppDeliveryProvider(config)` en `ConversacionesService.cs` decide Meta Cloud vs WhatsApp Web leyendo **únicamente** `ProviderMode`/`DefaultProvider` — dos ajustes **globales de la base**, en Configuración → WhatsApp API. No mira en ningún momento de qué número (`IdNumeroWhatsApp`) es la conversación que se está respondiendo. Este gap ya estaba documentado como deuda conocida en C2.3 ("`ResolveWhatsAppDeliveryProvider` no tiene en cuenta el estado de conexión por número").

Efecto concreto: si el select global "Proveedor predeterminado" de esa base seguía en Meta Cloud API (el default), **toda** conversación de WhatsApp Business quedaba evaluada como si fuera Meta — y como Meta no está configurado con credenciales reales, el mensaje se insertaba directo con `EstadoEnvio = "PENDIENTE_CONFIG"` (→ "Sin configuración" en la UI) **sin siquiera intentar** el envío real por la sesión Web ya conectada.

**Fix aplicado** (`ConversacionesService.cs`, solo mensajes de texto — `SendMessageAsync`): nuevo `ResolveWhatsAppDeliveryProviderForNumero(config, numero)` que, si el número de la conversación (`conversation.IdNumeroWhatsApp`) tiene `WebInstanceName` seteado (se setea una única vez al conectar por QR/código en `WhatsAppWebSessionService.StartSessionAsync`, nunca para números agregados solo para Meta Cloud), fuerza el routing a WhatsApp Web **sin importar el select global**. Si el número no tiene instancia Web, cae exactamente al comportamiento anterior (`ResolveWhatsAppDeliveryProvider` global) — cero cambio de comportamiento para conversaciones de Meta Cloud reales. Se calcula una sola vez y se reutiliza tanto para decidir el estado inicial como para la rama de envío real (antes se llamaba al resolver global dos veces por separado, con el mismo resultado pero de forma redundante).

El camino de envío real (`whatsAppWebSessionService.SendTextAsync(conversation.IdNumeroWhatsApp, ...)`) **ya existía y ya estaba bien implementado** desde C2.3 — nunca hizo falta tocarlo. El bug era exclusivamente de ruteo/decisión, no de la mecánica de envío en sí.

**Adjuntos (`UploadAttachmentAsync`) quedaron sin tocar a propósito** (alcance C2.5 = solo texto, punto §25 del pedido): ese método sigue llamando al resolver global y sigue bloqueado explícitamente por `EnsureWhatsAppProviderImplemented` para WhatsApp Web ("ese conector todavía no está implementado") — comportamiento preexistente, deuda ya conocida, no ampliada ni corregida ahora.

**No fallback a otra cuenta ni a Meta:** no se tocó `WhatsAppWebSessionService.SendTextAsync`, que ya lanza `InvalidOperationException` si la sesión no existe/no está conectada/no responde a tiempo — nunca reintenta con otra cuenta ni con Meta. El mensaje queda con `ERROR_ENVIO` (ya existente) y el usuario ve el error real, no un reloj eterno.

### Bug 2 — Status de WhatsApp crea conversación fantasa ("Springfield")

**Causa raíz confirmada leyendo `worker.mjs`:** Baileys entrega los Estados de WhatsApp por el **mismo evento** `messages.upsert` que los mensajes reales, con `remoteJid = "status@broadcast"`. `normalizeIncomingMessage` filtraba grupos (`@g.us`) y `fromMe`, pero no `@broadcast`. Para resolver el teléfono, el código prioriza `remoteJidAlt` sobre `remoteJid` (necesario para el caso real de `addressingMode:"lid"`) — y Baileys expone en `remoteJidAlt` el JID **real de quien publicó el estado** (para poder mostrar su nombre). Resultado: el filtro de teléfono de más abajo encontraba dígitos válidos ahí, y el Status pasaba como si fuera un mensaje directo normal del contacto que lo publicó — con su nombre real (pushName) como "Springfield" — creando contacto + conversación + mensaje + unread como si hubiera escrito de verdad.

**Fix de dos capas** (defensa en profundidad, no redundancia decorativa — ver nota abajo):

1. **`worker.mjs` (`normalizeIncomingMessage`), el más temprano posible:** se agregó `if (remoteJid.endsWith("@broadcast") || remoteJid.endsWith("@newsletter")) return null;` **antes** de mirar `remoteJidAlt`, y `if (entry?.message?.protocolMessage) return null;` (mensajes de protocolo — revoke, cambios de ephemeral, edits — que tampoco son conversación real, mismo patrón de bug). No se tocó el filtro de grupos (`@g.us`, sin cambios, sin regla de producto nueva para grupos) ni el de `fromMe` (se audita aparte, ver más abajo).
2. **`ConversacionesService.cs` (`RegisterIncomingWhatsAppWebMessageAsync`), red de seguridad necesaria (no opcional):** nuevo `IsWhatsAppWebNonConversationalEvent(rawJson)` que inspecciona el JSON crudo de Baileys (`key.remoteJid`, `message.protocolMessage`) y descarta **antes** de tocar contacto/conversación/mensaje/contadores/automatizaciones. Es necesaria porque un worker que ya estaba corriendo (como la sesión real conectada de esta prueba) sigue vivo con el `worker.mjs` **viejo** hasta que se desvincule y reconecte — el fix del punto 1 solo protege conexiones nuevas. Se agrega un log de auditoría liviano (`_appEvents.LogAuditAsync`, evento `WhatsAppWebEventIgnored`, con `InstanceName` — sin remoteJid ni contenido) para diagnóstico, sin duplicar el árbol de decisión en dos lenguajes: la fuente de verdad de "qué es Status" es la misma condición (`remoteJid` termina en `@broadcast`/`@newsletter`, o `protocolMessage` presente) implementada una vez en cada lado por necesidad de proceso, no por preferencia de diseño.

Otros eventos de Baileys (recibos, presence, typing, sync/history) **ya estaban excluidos por diseño**: el worker solo se suscribe a `messages.upsert` (`sock.ev.on("messages.upsert", handleMessagesUpsert)`) — nunca escuchó esos otros eventos, así que no hacía falta agregar nada para ellos. No se tocó el manejo de grupos: sigue sin definición de producto, tal cual estaba.

### Conversación "Springfield" ya creada — diagnóstico, sin borrar

**No se ejecutó ningún `DELETE`.** No pude confirmar el registro exacto contra la base real: la arquitectura es multi-tenant con ~135 bases SaaS (una base de datos real por tenant, resuelta por `IdBase`/`CentralBasesService`), y no tengo forma segura de saber a qué base de datos física corresponden los `IdBase` 106/4271 vistos en `App_Data\whatsapp-web\` sin arriesgarme a apuntar a la base equivocada.

**Diagnóstico por código** (alta confianza dado el root cause confirmado arriba): el registro es casi con certeza una fila en `CONV_CONVERSACIONES` con `Canal = 'WHATSAPP'`, creada por `EnsureConversationAsync` a partir de un `IncomingWhatsAppMessage` con `SistemaAutor = 'WHATSAPP_WEB'`, nombre de contacto "Springfield" (el pushName de quien publicó el estado), y un único mensaje en `CONV_MENSAJES` con `Direction = 'ENTRANTE'`, `MessageType = 'TEXT'` y `Text` vacío o sin relación con una conversación real (los Status no llevan el texto real del status, ya que `normalizeIncomingMessage` solo lee `message.conversation`/`extendedTextMessage`/`imageMessage.caption`/`videoMessage.caption`, campos que un Status no llena de esa forma).

**Query de verificación sugerida (solo lectura, para correr contra la base correcta una vez identificada):**

```sql
SELECT c.IdConversacion, c.Canal, c.NombreContacto, c.TelefonoWhatsApp, c.FechaHoraUltimoMensaje,
       m.IdMensaje, m.Direction, m.MessageType, m.Text, m.SistemaAutor, m.FechaHora
FROM dbo.CONV_CONVERSACIONES c
JOIN dbo.CONV_MENSAJES m ON m.IdConversacion = c.IdConversacion
WHERE c.NombreContacto = 'Springfield' AND c.Canal = 'WHATSAPP';
```

Si el resultado confirma un único mensaje entrante con texto vacío/inconsistente y `SistemaAutor = 'WHATSAPP_WEB'`, es seguro borrarlo manualmente (no vía UI, no automatizado) — pero **queda pendiente de tu autorización explícita y de que me confirmes o corras vos esa query contra la base correcta**.

### Multicuenta — verificado, sin tocar código

Durante esta fase se confirmó en vivo la existencia de **dos sesiones reales conectadas simultáneas** (`IdBase` 106 y 4271, mismo número físico `+5491153859509` vinculado como dos dispositivos distintos de WhatsApp) — cada una con su propio proceso Node independiente (`WebInstanceName`/carpeta de sesión/PID propios), confirmando en la práctica el aislamiento multicuenta ya documentado en C2.4. El fix de routing (Bug 1) es por `IdNumeroWhatsApp`, así que escala sin cambios a N cuentas.

**Hallazgo colateral, no corregido ahora (fuera de alcance de C2.5):** el worker de la base 106 (PID 1728 según su `status.json`, que seguía marcando `CONNECTED`) ya no existía como proceso del sistema operativo al momento de esta revisión — se cayó de forma independiente a cualquier acción de esta fase (no se tocó ese proceso ni antes ni durante C2.5). Es el mismo gap ya documentado en C2.3: "no hay reconexión automática de sesiones ya conectadas... no hay watchdog." Reportado, no resuelto.

### No regresión — verificado

- **QR/conexión:** no se tocó `WhatsAppWebSessionService.StartSessionAsync`/`RefreshSessionAsync`/`StopSessionAsync`, ni el lifecycle de `ConversacionesConfiguracion.razor` (C2.4/C2.4a/C2.4b). Se confirmó explícitamente antes de reiniciar `AlfaCore.exe` que el proceso Node de la sesión real conectada (PID 6508) es independiente y sobrevivió el reinicio sin desconectarse (mismo `StartTime` antes y después).
- **Meta Cloud API:** `ResolveWhatsAppDeliveryProvider` (el resolver global) no se modificó; `ResolveWhatsAppDeliveryProviderForNumero` es aditivo y cae exactamente a ese mismo resolver para cualquier número sin `WebInstanceName`. `SendToWhatsAppAsync` (envío real por Meta) no se tocó.
- **`Program.cs`:** no aparece en el diff de C2.5 — sigue con el fix de `ResolveStaticAsset()` de la fase de CSS isolation, sin mezclar.
- **Usuarios por número:** no se tocó ninguna tabla/relación de usuarios ni el binding de "Usuarios con acceso" en Configuración.

### Schema

**No se creó ninguna migración.** El fix de routing usa `WebInstanceName`, campo que ya existía. El filtro de Status no persiste nada nuevo (al contrario, evita persistir). No hizo falta ningún campo nuevo (JID de envío, message id de Baileys, receipts) para dejar el envío de texto operativo — quedan registrados como deuda para una fase posterior si se necesita ack/dedup/receipts más finos (ver "Pendiente" abajo).

### Alcance NO cubierto en C2.5 (deuda explícita, no resuelta ahora)

- **Eco/dedup de `fromMe`** (mensaje enviado por AlfaCore que vuelve por el inbound de Baileys) y **mensajes `fromMe` enviados desde el teléfono vinculado fuera de AlfaCore**: el filtro de `fromMe` en `normalizeIncomingMessage` sigue descartando **todo** `fromMe` sin distinguir estos dos casos (comportamiento preexistente, sin cambios en C2.5). Auditar y decidir el comportamiento correcto requiere observar casos reales (`entry.key.id` de Baileys vs `WhatsAppMessageId` ya guardado) — no se tocó a ciegas, según lo pedido explícitamente (§31-33, §47).
- **Receipts (sent/delivered/read):** el worker no escucha ningún evento de acks de Baileys hoy; `EstadoEnvio` para WhatsApp Web queda en lo que devuelva `SendTextAsync` (que hoy es `"ENVIADO"` fijo en caso de éxito, ver `worker.mjs` → `writeCommandResult`). No se inventaron ticks.
- **Message ID real de Baileys → AlfaCore:** `SendTextAsync` ya guarda `sent?.key?.id` como `ExternalMessageId` (preexistente); no se auditó si el modelo lo persiste en una columna dedicada o solo en el payload — no se tocó schema sin confirmar primero, según lo pedido.
- **Icono reloj / estado en UI:** no se tocó `Conversaciones.razor` (fuera de alcance salvo que hiciera falta, y con el fix de Bug 1 el mensaje ya no debería quedarse en "Sin configuración" — el binding de estado existente debería reflejar `ENVIADO`/`ERROR_ENVIO` correctamente sin cambios, dado que ya lee `EstadoEnvio` en tiempo real).

### Archivos modificados en C2.5

`src/AlfaCore/Services/ConversacionesService.cs`, `src/AlfaCore/Node/WhatsAppWebWorker/worker.mjs`, este documento. No se tocó `WhatsAppWebSessionService.cs`, `Program.cs`, `ConversacionesConfiguracion.razor(.css)`, ni ningún archivo de Instagram/Facebook/Mercado Libre/OAuth.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln --configuration Release` → 0 errores (mismos 3 warnings preexistentes). `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio. Reinicio de `AlfaCore.exe` confirmado sin afectar la sesión Web real conectada (PID 6508 verificado antes/después, mismo `StartTime`).

### ⚠️ Tests reales — NO ejecutados por mí (requieren teléfono/UI real)

Igual que en C2.4/C2.4a/C2.4b: no tengo herramienta de automatización de navegador ni un teléfono para probar. Los tests §42-48 del pedido (inbound directo, outbound real, segunda cuenta, Status, mensaje posterior al Status, `fromMe`, sesión caída) **quedan pendientes de que los corras vos**. Los dos fixes de esta fase están verificados por lectura de código + build, no por ejecución end-to-end.

C2 sigue pendiente hasta: **OUTBOUND real PASS + STATUS filtering PASS**, confirmados por vos.

Sin commit todavía.

---

## C2.5A — Diagnóstico y fix del error real de envío ("no respondió a tiempo")

Continuación directa de C2.5: la máquina de estados `PENDING → ERROR` ya funcionaba (confirmado por el usuario), pero el mensaje "prueba" a Alberto Antunez no llegó al teléfono. Esta fase encontró y corrigió la causa raíz real, con evidencia directa de archivo, no inferencia.

### Identificación del mensaje "prueba" (solo lectura, autorizada por el usuario)

El clasificador de auto-mode bloqueó inicialmente una consulta SQL directa contra la base real del tenant (dato sensible de cliente); se pidió autorización explícita al usuario antes de continuar, y se obtuvo. Se consultó primero `ALFA_CENTRAL.dbo.bases` (solo metadata: nombre/servidor/nombre de base, sin credenciales en el reporte) para resolver qué base física corresponde a los `IdBase` 106 y 4271 vistos en `App_Data\whatsapp-web\`:

- `IdBase 106` → base `ALFANET2007` ("ALFA NET").
- `IdBase 4271` → base `AW_112012807` ("Alberto conv") — la base de prueba de esta conversación.

En `AW_112012807`: conversación `IdConversacion = 24`, `NombreVisible = "Alberto Antunez"`, `TelefonoWhatsApp = +5491156955241`, `IdNumeroWhatsApp = 2`. Mensaje "prueba" = `IdMensaje 150`, `EstadoEnvio = ERROR_ENVIO`, `PayloadJson`:

```json
{"Error":"WhatsApp Web no respondió a tiempo al comando de envío.","Type":"System.InvalidOperationException","FechaHora":"2026-08-19T11:59:58.09"}
```

Ese es el texto exacto que lanza `WhatsAppWebSessionService.SendTextAsync` al agotar su timeout de 35s esperando el archivo de resultado — confirmado, no inferido.

### Mensaje "Si" — histórico, no tocado

`IdMensaje 60` ("Si", conversación 24) sigue en DB como `EstadoEnvio = PENDIENTE_CONFIG`. Es de **antes** del fix de routing de C2.5: se creó cuando `ResolveWhatsAppDeliveryProvider` todavía resolvía Meta Cloud globalmente para esta conversación. No es cache de UI ni un bug nuevo — es un mensaje real que quedó atascado por el bug ya corregido. **No se reenvió, no se corrigió manualmente**, según lo pedido explícitamente.

### Causa raíz real — confirmada con archivo, no hipótesis

`IdNumeroWhatsApp = 2` (Alberto conv) tiene `WebInstanceName = "waweb-af78fa05"`, con un worker Node **realmente vivo** en el momento del diagnóstico (PID 6508, `status.json` con `lastUpdatedAtUtc` de hace 40 segundos al momento de revisarlo — heartbeat de 15s activo, sesión genuinamente sana, no un `CONNECTED` fantasma). Esto descartó de entrada la hipótesis "worker muerto" para este caso puntual.

`outbox/`/`results/` en la carpeta real del worker (`bin\Release\net8.0\App_Data\whatsapp-web\4271\waweb-af78fa05\`) estaban vacías — ni rastro del comando de "prueba". Buscando en la copia paralela que existe en el árbol fuente (`src\AlfaCore\App_Data\whatsapp-web\4271\waweb-af78fa05\`, detectada ya en C2.4a como resto de un lanzamiento anterior con otro *working directory*), apareció el comando huérfano exacto:

```json
{
  "id": "4726aff7699c47aebd4a17f212ce21be",
  "type": "send_text",
  "phone": "+5491156955241",
  "text": "prueba",
  "replyToMessageId": "",
  "createdAtUtc": "2026-08-19T14:59:22.92Z"
}
```

Timestamp y destinatario coinciden exactamente con el mensaje 150. **Confirmado: el comando se escribió en una carpeta física distinta a la que el worker real está mirando.**

La causa es el mismo patrón de bug de C2.4a, pero en un método que nunca se corrigió: `WhatsAppWebSessionService.EnsureSessionDirectory()` seguía usando `environment.ContentRootPath` (por defecto, el *cwd* del proceso al arrancar) en vez de `AppContext.BaseDirectory` (la carpeta real del `.exe`, invariante). `StartSessionAsync` calculó la carpeta de la sesión **una vez**, al conectar (bajo un proceso cuyo `ContentRootPath` coincidía con `bin\Release\net8.0`, por eso el worker real vive ahí) — pero `SendTextAsync`/`RefreshSessionAsync`/`StopSessionAsync` **recalculan esa misma carpeta en cada llamada**. Se confirmó además, por evidencia directa de proceso, que el `AlfaCore.exe` que atendió el envío de "prueba" (PID 18412, arrancado a las 11:58:18 — **un minuto antes del envío, y no iniciado por mí en esta sesión**) es una instancia distinta de la que yo había dejado corriendo, consistente con haber sido lanzada con un *working directory* diferente. Con `ContentRootPath` distinto, `EnsureSessionDirectory()` recalculó una carpeta distinta a la del worker ya vivo — el comando fue a parar a `src\AlfaCore\App_Data\...` en vez de `bin\Release\net8.0\App_Data\...`, el worker real nunca lo vio, y a los 35s `SendTextAsync` tiró el timeout.

**Se detectó el mismo patrón también en la recepción**: `ConversacionesService.ProcessWhatsAppWebInboxAsync` construye `sessionsRoot` con `environment.ContentRootPath` también — con la instancia mismatched (PID 18412) corriendo, los mensajes entrantes **nuevos** habrían dejado de detectarse silenciosamente (sin excepción visible, solo `Directory.Exists(sessionsRoot) == false` devolviendo 0 procesados). La recepción que sí funcionó durante la prueba del usuario ocurrió **antes** del cambio de PID (mensaje entrante de las 11:28, el cambio a PID 18412 fue a las 11:58) — coincide exactamente.

**Interpretación según el árbol de casos del pedido:** ni Caso A (Baileys/socket falla) ni Caso C (ack mal mapeado) — es un **Caso B, pero no de payload sino de ruteo de archivos**: el comando nunca llegó físicamente al worker por una carpeta mal resuelta, no por un problema de Baileys ni de `.NET → worker` en el sentido de protocolo/IPC (el mecanismo en sí — archivos JSON en `outbox`/`results`, polling cada 1.2s en el worker, timeout de 35s en `.NET` — está bien diseñado y ya funcionaba correctamente en C2.3; nunca hizo falta tocarlo).

### Fix aplicado (mínimo, mismo patrón ya usado en C2.4a)

- `WhatsAppWebSessionService.EnsureSessionDirectory()`: `environment.ContentRootPath` → `AppContext.BaseDirectory`.
- `ConversacionesService.ProcessWhatsAppWebInboxAsync()` (`sessionsRoot`): mismo cambio, mismo motivo — evita que la recepción sufra el mismo bug bajo una instancia con *cwd* distinto.

No se tocó ninguna otra ocurrencia de `environment.ContentRootPath` en `ConversacionesService.cs` (uploads/adjuntos de conversaciones son un subsistema aparte, sin relación con el worker, fuera de alcance). No se tocó el mecanismo `.NET ↔ worker` en sí (archivos + polling), el JID/destino (`+5491156955241`, tomado directo de `conversation.TelefonoWhatsApp`, sin reconstrucción — no hizo falta tocar identidad/JID, la causa nunca fue esa), ni `sock.sendMessage` en `worker.mjs`.

**No se generó QR, no se desvinculó nada, no se tocó `auth`, no se hizo fallback a otra cuenta ni a Meta, no se reintentó "prueba" automáticamente, no se tocó "Si".** El comando huérfano en `src\AlfaCore\App_Data\...\outbox\` se dejó tal cual (es basura de runtime sin valor, nunca se va a leer de nuevo con el fix; no se borró por precaución, queda a criterio del usuario limpiarlo).

### Reinicio de una instancia que no había iniciado yo

El PID que bloqueaba el build (18412) no lo había lanzado yo en esta sesión — evidencia de que alguien (probablemente el propio usuario, probando en paralelo) reinició AlfaCore de forma independiente. Se avisó explícitamente antes de tocarlo, se pidió confirmación, y solo entonces se detuvo. El worker real (PID 6508) se verificó **antes y después** del reinicio — mismo `StartTime`, sin desconexión.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln --configuration Release` → 0 errores (mismos 3 warnings preexistentes). `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio.

### ⚠️ Test real post-fix — pendiente del usuario

No reenvié "prueba" (según lo pedido). **Falta que el usuario envíe un mensaje nuevo** ("C2.5A salida final" o similar) desde la conversación de Alberto Antunez y confirme: llega al teléfono, sale por la cuenta correcta (`IdNumeroWhatsApp = 2`, worker PID 6508), no queda "Sin configuración" ni "Enviando" ni "Error", pasa a `ENVIADO`, y no aparece duplicado por eco de `fromMe`. Sin este test no se puede dar por cerrado el fix, igual que en fases anteriores — no tengo forma de hacer clic en la UI ni de escanear con un teléfono real.

### Alcance NO tocado en C2.5A (confirmado, no explorado)

`fromMe`/eco/dedup (§30-33 del pedido original de C2.5) no se auditó en esta pasada — el foco fue exclusivamente el timeout de envío. Queda para cuando el test real post-fix esté confirmado.

Sin commit todavía.

---

## C2.5B — Acks reales: SENT / DELIVERED / READ

Continuación de C2.5A: outbound real ya funciona (mensaje "Prueba" confirmado llegando al teléfono, check simple en AlfaCore, doble check en WhatsApp). Esta fase completa el lifecycle con acks reales de Baileys — sin fingir estados.

### 1. Semántica del ✓ actual — confirmada, no asumida

`IdMensaje 187` ("Prueba", conversación 24) = `EstadoEnvio = "ENVIADO"`, `WhatsAppMessageId = "3EB0998CF292728D6F76DD"` (id real de Baileys, ya persistido desde `SendTextAsync` → `sent?.key?.id`). Confirmado por consulta read-only (autorización ya vigente de C2.5A, misma base `AW_112012807`): el check ✓ = `GetDeliveryIcon("ENVIADO")` → `bi-check2` en `Conversaciones.razor`. Nada que corregir ahí — ya estaba bien.

### 2. Eventos reales de Baileys (versión instalada, no documentación vieja)

Confirmado en `node_modules/@whiskeysockets/baileys` (v7.0.0-rc14, `lib/Types/Events.d.ts`): existen `messages.update: WAMessageUpdate[]` (`{ key, update: Partial<WAMessage> }`) y `message-receipt.update` (más pensado para receipts por-usuario en grupos). Se usó `messages.update`, que alcanza para 1:1. El estado real viene de `proto.WebMessageInfo.Status` (`WAProto/index.d.ts`): `ERROR=0, PENDING=1, SERVER_ACK=2, DELIVERY_ACK=3, READ=4, PLAYED=5` — enum real de esta versión, no inventado ni de memoria.

### 3. Mapeo de ack (mínimo, sin inventar estados)

`SERVER_ACK` no se reporta (ya cubierto por "ENVIADO", seteado apenas `sock.sendMessage` resuelve — no hace falta esperar ningún ack para eso). Mapeo real:

- `DELIVERY_ACK (3)` → `"ENTREGADO"`
- `READ (4)` y `PLAYED (5)` → `"LEIDO"` (WhatsApp no distingue "reproducido" en la UI de AlfaCore; no existe un cuarto estado hoy y no hacía falta crear uno)
- `ERROR (0)` → `"ERROR_ENVIO"` (reutiliza el estado ya existente, no uno nuevo)
- `PENDING (1)` → ignorado (no aporta nada nuevo)

Si Baileys no llega a emitir `DELIVERY_ACK`/`READ` para un mensaje puntual (ej. el destinatario tiene desactivada la confirmación de lectura), el mensaje simplemente se queda en "Enviado" — **no se simula ni se fuerza** ningún estado.

### 4. Mecanismo worker → .NET (mismo patrón filesystem, sin infraestructura nueva)

`worker.mjs`: nuevo listener `sock.ev.on("messages.update", handleMessagesUpdate)`. Por cada update con `key.fromMe === true` y `update.status` mapeable, escribe un archivo a una carpeta nueva `acks/` (hermana de `inbox`/`outbox`/`results`, mismo patrón) con `{ externalMessageId, status, timestampUtc }`. No se creó servidor HTTP, socket ni ningún IPC nuevo — mismo mecanismo de archivos ya validado en C2.3/C2.4/C2.5.

`ConversacionesService.ProcessWhatsAppWebAcksAsync` (nuevo, en `IConversacionesService`): mismo patrón que `ProcessWhatsAppWebInboxAsync` pero escaneando `acks/` en vez de `inbox/`. **Nunca crea contacto/conversación/mensaje ni toca contadores/automatizaciones** (§10 del pedido) — busca el mensaje existente por `WhatsAppMessageId` (reutiliza `GetExistingMessageIdByWhatsAppIdAsync`-equivalente, con filtro `Direction='SALIENTE'`) y actualiza solo `EstadoEnvio` vía `UpdateMessageDeliveryAsync` (método ya existente, reutilizado sin cambios — se le pasan cadenas vacías para no tocar `WhatsAppMessageId`/`PayloadJson`).

Registrado en el `WhatsAppWebInboxHostedService` **ya existente** (mismo ciclo de 5s por base, sin hosted service nuevo): llama a `ProcessWhatsAppWebAcksAsync` justo después de `ProcessWhatsAppWebInboxAsync`, por cada base SaaS.

### 5. Guard de orden (sin sobre-ingeniería)

`WhatsAppWebDeliveryRank` (Pendiente=0 < Enviado=1 < Entregado=2 < Leído=3): un ack solo se aplica si su rango es `>=` al estado actual del mensaje — evita que un `DELIVERY_ACK` tardío pise un `READ` que ya llegó antes. Un `ERROR` post-envío siempre se aplica (rango no acotado), porque es señal real de fallo, no un retroceso cosmético.

### 6. Multicuenta / scope (§24)

Cada base SaaS es una base de datos físicamente separada (confirmado en C2.5A vía `ALFA_CENTRAL.dbo.bases`) — no hay forma de que un ack de la Cuenta A actualice un mensaje de la Cuenta B aunque coincidiera el `WhatsAppMessageId` (imposible en la práctica, es un id aleatorio de Baileys). Dentro de la misma base, la carpeta `acks/` vive bajo la instancia (`WebInstanceName`) que la escribió — el ack ya viene acotado a esa cuenta antes de llegar a `.NET`.

### 7. UI — no se tocó nada

`Conversaciones.razor` **ya tenía** soporte completo para `ENTREGADO`/`LEIDO`: `GetDeliveryIcon`/`GetDeliveryClass`/`GetDeliveryLabel` (líneas ~11457-11495) ya mapean ambos estados a ✓✓ (`bi-check2-all`), clase `is-read` diferenciada para Leído, y tooltips "Entregado"/"Leído" ya en español. Estos mismos helpers ya se reutilizan tanto en la burbuja del mensaje (línea ~1781) como en el preview de la lista de conversaciones (línea ~920, vía `item.EstadoUltimoMensaje`) — sin lógica duplicada. **No hizo falta ningún cambio de UI.**

### 8. Tiempo real sin F5 — ya existía

`Conversaciones.razor` ya tiene un `PeriodicTimer` de 3s (`StartPolling`, ~línea 6486) que refresca conversaciones y mensajes. `EstadoUltimoMensaje` del preview de lista no es una columna persistida — se deriva en la query de conversaciones directo del último mensaje, así que se actualiza solo en el próximo poll sin ningún cambio adicional. **No se creó ningún mecanismo de tiempo real nuevo** — bastó con que el backend actualizara `CONV_MENSAJES.EstadoEnvio`; la UI ya lo recoge sola.

### 9. `fromMe`/eco — auditado, confirmado sin duplicado (no ampliado)

`normalizeIncomingMessage` sigue descartando **todo** `fromMe` en `messages.upsert` (sin cambios en C2.5B) — por eso "Prueba" (IdMensaje 187) aparece como **una sola fila** en `CONV_MENSAJES` (confirmado por consulta), sin eco duplicado. Distinguir "eco de AlfaCore" vs "mensaje enviado desde el teléfono vinculado" (§20-21, §32 del pedido) requeriría dejar pasar `fromMe` hacia `.NET` (el único lado con acceso a DB para decidir por `WhatsAppMessageId` si es eco o mensaje nuevo) — cambio real de alcance, no de una línea. **Reportado, no implementado en C2.5B**, según lo pedido explícitamente si "requiere demasiado cambio."

### 10. Preservado, sin tocar

`GetWorkerDirectory`/`EnsureSessionDirectory`/`ProcessWhatsAppWebInboxAsync` (fix de `AppContext.BaseDirectory` de C2.4a/C2.5A), el filtro `status@broadcast`/`protocolMessage` + guard defensivo en C# (C2.5), routing multicuenta (`ResolveWhatsAppDeliveryProviderForNumero`, C2.5), lifecycle de QR (C2.4), `Program.cs`. Ninguno de estos archivos requirió cambios adicionales en C2.5B más allá de lo ya hecho.

### Schema

**No se creó ninguna migración.** `WhatsAppMessageId` ya existía y ya se usaba (confirmado con el mensaje 187). Los tres nuevos estados de `EstadoEnvio` (`ENTREGADO`, `LEIDO`, reutilizando `ERROR_ENVIO`) son valores de texto libre en una columna que ya los aceptaba — la UI ya los mapeaba antes de esta fase.

### Archivos modificados en C2.5B

`src/AlfaCore/Node/WhatsAppWebWorker/worker.mjs`, `src/AlfaCore/Services/ConversacionesService.cs`, `src/AlfaCore/Services/IConversacionesService.cs`, `src/AlfaCore/Services/WhatsAppWebInboxHostedService.cs`, `src/AlfaCore/Models/ConversacionesModels.cs` (nuevo DTO), este documento. No se tocó `Conversaciones.razor`/`.razor.css`, `WhatsAppWebSessionService.cs`, ni ningún archivo de Meta/Instagram/Facebook/Mercado Libre.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln --configuration Release` → 0 errores (mismos 3 warnings preexistentes). `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio. Reinicio de `AlfaCore.exe` confirmado sin afectar la sesión Web real (PID 6508, mismo `StartTime` antes/después).

### ⚠️ Limitación importante para los tests reales — sesión ya conectada corre con worker viejo

El worker real de la conversación de Alberto Antunez (PID 6508) sigue vivo desde **antes** de este cambio — como todo proceso Node ya iniciado, no puede "recargar" su propio código: seguirá corriendo `worker.mjs` sin el listener de `messages.update` hasta que la sesión se desvincule y reconecte. Confirmado por disco: la carpeta `acks/` **todavía no existe** en la sesión real (`bin\Release\net8.0\App_Data\...\waweb-af78fa05\`) — señal directa de que el worker viejo nunca la creó.

**No generé QR ni desvinculé nada** — está explícitamente prohibido en el pedido de esta fase. Esto significa:

- **Test SENT (§28):** ya se puede dar por confirmado — no depende del fix nuevo, "Prueba" ya lo demostró.
- **Test DELIVERED/READ (§29-30):** **no van a funcionar todavía** contra la sesión actual, no porque el fix esté mal, sino porque el worker que las generaría no tiene el código nuevo cargado. Van a funcionar recién en el **próximo mensaje enviado después de que esa sesión se reconecte** (o en cualquier cuenta que se conecte de cero de ahora en más).

**Queda a tu criterio decidir si querés reconectar la sesión de prueba ahora** (implica un nuevo QR/código, fuera de lo que esta fase tenía permitido hacer sola) para poder validar DELIVERED/READ hoy, o si preferís dejarlo para la próxima vez que esa sesión se reconecte naturalmente.

Sin commit todavía. C2 sigue sin cierre final hasta validar también Status (pendiente desde C2.5) y, ahora, DELIVERED/READ reales.

---

## C2.6A — Pulido final: layout de Conversaciones + WhatsApp Business simplificado

Cinco problemas reales detectados en validación visual, sobre una base ya funcional (QR, envío/recepción, ACKs). No se rediseñó la arquitectura (Settings Navigation, App Top Bar, Context Toolbar, tabs principales intactos).

### 1. Espacio vacío en Conversaciones (causa raíz confirmada, no `height:100vh` a ciegas)

`app.css` tiene ~27000 líneas con reglas repetidas para las mismas clases en distintos `@media`. Se usó un agente Explore para trazar la cadena completa `.conversations-layout` → `.conversations-chat-workspace` → `.conversations-chat` → `.conversations-thread-shell` (flex:1) antes de tocar nada.

**Causa real**: `.conversations-page--odoo .conversations-chat-workspace` (`app.css` ~26966) es `display:grid` con `grid-template-columns` pero **sin `grid-template-rows`** — la fila implícita queda `auto` (se ajusta al contenido) en vez de estirarse al `height:100%` que el propio workspace sí tiene. Con pocos mensajes, `.conversations-chat` mide solo su contenido (header + hilo corto + composer) y el resto del alto queda vacío, mostrando el fondo oscuro de `.conversations-layout` por debajo del composer/nota interna.

**Fix**: una línea, `grid-template-rows: minmax(0, 1fr) !important;` agregada a esa regla. Verificado que `.conversations-chat-workspace` es exclusiva de `Conversaciones.razor` (no la usa ninguna otra página) antes de tocar el archivo compartido.

### 2. Texto faltante en burbujas (dos causas distintas, no una)

Diagnosticado con SQL de solo lectura contra la base real (autorización ya vigente de fases anteriores), siguiendo el flujo pedido: DB → payload → parser.

- **Mensaje entrante sin texto (real)**: el `PayloadJson` de Baileys no tenía objeto `message` en absoluto — solo `"messageStubType":2, "messageStubParameters":["Message absent from node"]`. Es WhatsApp reportando que el contenido cifrado nunca llegó (típico de `addressingMode:"lid"`). No había nada que extraer: no era CSS ni un bug de parsing de un campo equivocado. **Fix**: `worker.mjs` (`normalizeIncomingMessage`) descarta cualquier entry sin `entry.message` — mismo punto donde ya se filtraba Status/protocolMessage. Guard defensivo espejo en `ConversacionesService.IsWhatsAppWebNonConversationalEvent` para sesiones ya conectadas con worker viejo.
- **Mensaje saliente "."**: el DB tiene literalmente un punto — la UI lo renderiza correctamente. **No es un bug**, es un mensaje real de un carácter.

### 3-5. WhatsApp Business — modelo simplificado

**Removido de la UI de cliente** (el campo/columna sigue existiendo en DB, sin tocar schema):
- Checkbox "Activo" del header de la card.
- Botón global "Guardar número" — reemplazado por "Guardar opciones" contextual dentro de "Opciones avanzadas" (mismo `SaveNumeroAsync`, sin cambios de backend).
- "Desvincular"/"Quitar WhatsApp Web" → unificados como **"Cerrar sesión"** (mismo ícono, mismo handler `RequestStopWhatsAppWebSession`).
- "Soporte / runtime" → **"Soporte y diagnóstico"** (mismo `<details>` colapsado por defecto, sin cambios de contenido).
- Ayuda lateral (`<aside class="wa-help">`, columna fija de 280-340px) → movida a ser el último `<details>` dentro de `.wa-panel`. `.wa-layout` pasó de grid de 2 columnas a una sola columna (`max-width:760px`) — elimina el espacio muerto reservado permanentemente para una ayuda colapsada (confirmado en captura: rectángulo vacío a la derecha de la lista).
- Nombres genéricos: si `Nombre` matchea el patrón `"WhatsApp Business {N}"` (placeholder de `IniciarNuevoWhatsAppBusinessAsync`) y ya hay `WebPhoneNumber` real, se muestra el teléfono en vez del nombre genérico (`GetNumeroDisplayName`). No se inventa ningún nombre; si no hay teléfono real tampoco, se mantiene el placeholder.

**"Esperando escaneo" obsoleto (bug real, confirmado)**: `GetNumeroEstadoTone`/`GetNumeroEstadoLabel` leían `WebSessionStatus` directo de la DB, sin relación con si había un pairing activo en la sesión de UI actual — un `PENDING_QR` viejo (de un pairing abandonado, sin que el último poll llegara a refrescarlo) quedaba mostrando "Esperando escaneo" para siempre. **Fix**: nuevo `IsStalePendingQr(numero)` — si el estado es `PENDING_QR` pero el número no está en `_pairingFlowActiveIds` (pairing de esta sesión), se trata visualmente como Desconectado.

### "Cerrar sesión" — semántica real, auditada antes de reusar

`ClearWhatsAppWebPairingAsync` → `WhatsAppWebSessionSvc.StopSessionAsync` ya (desde un cambio de otra sesión, ya committeado en `main`) detiene el worker, limpia campos de pairing/runtime y **vacía `WebInstanceName`** — esto último es lo que evita que `ResolveWhatsAppDeliveryProviderForNumero` siga ruteando por Web hacia una sesión que ya no existe. No se tocó ese método.

**Agregado en C2.6A**: tras un cierre exitoso, se pone `numero.Activo = false` y se persiste (reusando `SaveWhatsAppNumeroAsync`, sin migración). La lista Business (`ActiveBusinessNumeros`) filtra por `Activo` **solo en esa pantalla** — `GetWhatsAppNumeroAsync`/`GetWhatsAppNumeroByInstanceNameAsync` (envío, recepción, resolución de conversaciones históricas) **no se tocaron**, siguen viendo todas las filas sin excepción. No hay DELETE: la fila, sus conversaciones, mensajes y usuarios asociados quedan intactos — solo deja de listarse como cuenta operativa activa. Mismo mecanismo (sin feedback visible, silencioso) se dispara al abandonar un pairing nunca conectado navegando fuera de la vista (`CleanupAbandonedPairingsAsync`, nuevo) — evita que un worker de pairing abandonado quede corriendo indefinidamente y que la fila provisional quede pegada en la lista.

**Diálogo de confirmación**: texto reescrito sin mencionar auth/worker/Baileys/JID — "Cerrar sesión de WhatsApp" / "\"{nombre}\" dejará de estar conectado a AlfaCore." / "Las conversaciones anteriores se conservarán."

### Hallazgo colateral durante la validación (no causado por estos cambios)

Al inspeccionar la base de prueba "Alberto conv" antes del fix: los números "WhatsApp Business 1" y "WhatsApp Business 2" ya tenían `Activo=0` en la base **desde antes** de este cambio (probablemente desactivados manualmente con el checkbox viejo, ya removido). Con el filtro nuevo, **van a dejar de aparecer en la lista Business** apenas se recargue la pantalla — sus filas, auth y conversaciones no se tocaron, pero visualmente desaparecen. Reportado para que no sea una sorpresa, no revertido.

También se observó que "WhatsApp Business 3" tiene el estado persistido en DB como `CONNECTED`, pero su `status.json` real en disco muestra `RECONNECTING` con `"Error: Connection Failure"` (actualizado hoy) — parece un problema de conectividad transitorio que el propio `worker.mjs` reintenta solo (`scheduleReconnect`), no algo causado por estos cambios. Queda como observación, no se intervino.

### Preservado, sin tocar

QR lifecycle (C2.4), routing multicuenta (C2.5), filtro `status@broadcast`/`protocolMessage` (C2.5, extendido acá solo con el caso de `message` ausente), ACKs SENT/DELIVERED/READ (C2.5B), `AppContext.BaseDirectory` en las tres funciones (C2.4a/C2.5A), distribución del worker. `fromMe`/sincronización cross-device: **no tocado**, queda para la fase siguiente tal como se pidió explícitamente.

### Archivos modificados en C2.6A

`src/AlfaCore/wwwroot/app.css` (una regla, `grid-template-rows`), `src/AlfaCore/Node/WhatsAppWebWorker/worker.mjs` (filtro de stub sin `message`), `src/AlfaCore/Services/ConversacionesService.cs` (guard defensivo espejo), `src/AlfaCore/Components/Pages/ConversacionesConfiguracion.razor` y `.razor.css` (todo el modelo Business), este documento.

### Schema

**Sin migraciones.** `Activo` ya existía en `CONV_WHATSAPP_NUMEROS` y ya se leía/escribía — se cambió quién lo controla (sistema en vez de checkbox manual) y se agregó un filtro de lectura acotado a una sola pantalla.

### Validación técnica ejecutada

`dotnet build AlfaCore.sln --configuration Release` → 0 errores (mismos 3 warnings preexistentes). `check_catalogo.py` → 68 rutinas, 0 errores. `git diff --check` → limpio.

### ⚠️ Sin validación visual/funcional en vivo — no tengo navegador en este entorno

Todo lo de esta fase fue verificado por lectura de código + diagnóstico con evidencia (agente Explore para CSS, SQL de solo lectura para el mensaje sin texto) + build limpio — **no por captura de pantalla ni click real**, según las limitaciones ya documentadas en fases anteriores. Quedan pendientes las 46 validaciones visuales y los tests §47-51 del pedido original (cancel pairing, navegar fuera, QR vencido, cerrar sesión real, F5) — a cargo del usuario.

Sin commit todavía.

---

## 1. Base de partida

- Rama: `main`
- HEAD al iniciar C0: `d0e4d4dd4ea104b09035b4a5ea625aea2ef67e8a`
- `origin/main` al iniciar C0: `d0e4d4dd4ea104b09035b4a5ea625aea2ef67e8a` (coincide con HEAD)
- `main` es superconjunto estricto de la rama `Evelyn`: contiene AlfaDesign más reciente (Clientes Browse, Smart Search popover compartido, Data View footer, column width persistence), Conversaciones más reciente (multinúmero, WhatsApp Web por número, worker Node), y todas las migraciones SQL vigentes.
- Este documento parte de la auditoría previa de Conversaciones → Configuración (alcance solo lectura) y de la reconciliación de estado Git (Fase 0), ambas ya aprobadas conceptualmente.

## 2. Objetivo del rediseño

Rediseñar la pantalla `Conversaciones → Configuración` aplicando AlfaDesign v1, con foco en:

- navegación interna clara y escalable;
- separación real de categorías (canales, automatización, integraciones IA, operación, soporte);
- reorganización de formularios, credenciales y estados;
- baja carga cognitiva y progressive disclosure.

No es un cambio de colores/CSS: es una reorganización de arquitectura de información y de patrones de interacción, manteniendo el comportamiento funcional y el storage existentes.

## 3. Restricciones de producto (obligatorias durante todo el rediseño)

### 3.1 Flujo principal de WhatsApp — experiencia QR

Para el cliente final, el camino principal debe ser:

```
WhatsApp → Números → Agregar/configurar número → WhatsApp Business / WhatsApp Web
→ Escanear QR → Conectado → Usuarios habilitados → Operación
```

La conexión por QR debe sentirse simple, directa, guiada y comprensible para un usuario no técnico. Es el flujo más visible de la experiencia principal.

### 3.2 Meta Cloud API — no es el flujo principal

Meta Cloud API existe y puede estar en uso real. **No se elimina nada**: Access Token, App Secret, Verify Token, Graph API Version, WABA ID, Phone Number ID, Webhook, proveedor Meta, configuración Cloud y cualquier valor existente se preservan íntegramente.

Meta Cloud API deja de ser parte de la experiencia principal del cliente común y pasa a vivir conceptualmente en **Avanzado** y/o **Soporte**, según la arquitectura de permisos que se defina en fases posteriores.

**Ocultar de la experiencia principal ≠ eliminar del sistema.**

## 4. Regla de preservación de datos (dato productivo)

Toda configuración existente se considera dato productivo y potencialmente en uso. Durante el rediseño:

- no borrar;
- no resetear;
- no reemplazar;
- no inicializar en blanco;
- no migrar silenciosamente.

Alcance mínimo a preservar: `TA_CONFIGURACION`, claves `CONV_*`, `CONV_WHATSAPP_NUMEROS`, `CONV_WHATSAPP_NUMERO_USUARIOS`, `CONV_ADMINISTRADORES`, `CONV_ASISTENTE`, `CONV_REGLAS`, prioridades, horarios, SLA, prompts, automatizaciones, AlfaKnowledge, credenciales Meta/Instagram/Facebook/Mercado Libre, OAuth tokens, refresh tokens, webhooks, webhook routing token, App Secrets, Verify Tokens, Phone Number IDs, WABA IDs, sesiones WhatsApp Web, pairing, runtime, providers, administradores, usuarios habilitados, configuración legacy, appsettings fallback, compatibilidad histórica.

## 5. Regla crítica — oculto no significa vacío

Si una configuración no está visible en la sección que el usuario edita actualmente:

- no debe enviarse como `null`;
- no debe enviarse como string vacío;
- no debe volver a default;
- no debe eliminarse;
- no debe sobrescribirse accidentalmente.

Ejemplo: guardar la configuración de un número WhatsApp Web no debe modificar Meta Cloud API, Access Token, App Secret, Verify Token, webhook, Instagram, Facebook, Mercado Libre, automatizaciones, otros números ni configuración global.

## 6. Scopes reales a respetar

El guardado debe respetar el scope real de cada configuración, sin crear un "Guardar todo" que mezcle scopes heterogéneos:

- Base / empresa
- Base + canal
- Base + número
- Base + usuario
- Base central (routing multitenant)
- Runtime local / filesystem

## 7. Compatibilidad legacy

No retirar durante el restyle: Phone Number ID global legacy, claves `CONV_WHATSAPP_WEB_*` legacy, rutas webhook sin token, fallback en `appsettings`, columnas históricas de storage, bridges de compatibilidad, almacenamiento alternativo existente. Pueden documentarse como legacy, pero su retiro es una fase de migración explícita y separada, no parte del restyle.

## 8. Principios UX a aplicar

Claridad, consistencia, baja carga cognitiva, progressive disclosure, jerarquía, descubribilidad, separación de responsabilidades.

Evitar: formularios enormes, scroll extremo, exceso de tabs horizontales, configuración técnica junto a opciones simples, estados mezclados con inputs, ayuda técnica ocupando media pantalla, secretos visibles permanentemente, acciones peligrosas con el mismo peso visual que Guardar, datos SQL visibles para usuarios comunes.

## 9. Niveles de experiencia (conceptuales, sin permisos todavía)

- **Principal**: canales, números, conectar WhatsApp por QR, estado de conexión, usuarios habilitados, horarios, bienvenida, automatizaciones habituales, reglas, estado general.
- **Avanzado**: Graph API version, IDs técnicos, parámetros técnicos, APIs, webhooks, configuración Meta Cloud, timeouts, parámetros avanzados.
- **Soporte**: runtime, PID, paths, storage, errores, logs, compatibilidad legacy, appsettings/database, diagnóstico técnico.

No se crean permisos nuevos en C0; solo se define la arquitectura conceptual.

## 10. Arquitectura auditada (hipótesis de IA, sujeta a refinamiento en C1)

```
Configuración
├─ Resumen
├─ Canales
│  ├─ WhatsApp
│  │  ├─ Números
│  │  │  ├─ Número A (Estado, Vincular QR, Usuarios, Sesión)
│  │  │  └─ Número B ...
│  │  ├─ General
│  │  └─ Avanzado / Soporte (Meta Cloud API)
│  ├─ Instagram
│  ├─ Facebook
│  └─ Mercado Libre
├─ Automatización
├─ Integraciones IA
├─ Operación y accesos
└─ Soporte
```

Esta hipótesis de IA **no modifica modelos ni storage**. La UI se adapta a los datos reales existentes, no al revés.

WhatsApp Web (QR) y Meta Cloud API son dos experiencias distintas y no deben mezclarse en el mismo formulario principal.

Instagram y Facebook pueden compartir patrón UX Meta cuando corresponda, sin unificar storage ni crear un modelo global de cuenta Meta sin una fase backend explícita.

Mercado Libre permanece dentro de Canales, separando conceptualmente Cuenta/OAuth, Credenciales, Webhook y Diagnóstico. No se modifica el OAuth actual en esta fase.

Automatización (horarios, bienvenida, reglas, auto-cierre, SLA) se separa conceptualmente de Integraciones IA (AlfaKnowledge, informes).

Operación y accesos agrupa prioridades, usuarios por número y administradores, sin mezclar runtime técnico (que va a Soporte).

La pestaña "Herramientas" actual está vacía; sus funciones reales existentes (AnyDesk, runtime Web) deben reclasificarse en Operación/Soporte, no mantenerse vacía por compatibilidad visual.

## 11. Navegación futura

Subnavegación lateral interna para Configuración (no es un sidebar global, es navegación propia del workspace), sobre:

```
App Top Bar (compartida AlfaDesign)
+ Context Toolbar (compartida)
+ Configuración Workspace
    ├─ Internal Settings Navigation
    └─ Settings Content
```

No se implementa en C0. Deep links futuros a evaluar en C1 (ejemplo conceptual: `/conversaciones/configuracion/canales/whatsapp`, `/conversaciones/configuracion/canales/whatsapp/numeros`), sin implementarse todavía.

## 12. Estado vs configuración vs runtime vs diagnóstico vs acción vs ayuda

La UI futura debe distinguir visualmente estos conceptos sin mezclarlos:

- **Configuración**: ej. proveedor = WhatsApp Web.
- **Estado de configuración**: ej. credenciales completas.
- **Runtime**: ej. sesión conectada.
- **Diagnóstico**: ej. PID/error/última conexión.
- **Acción**: ej. generar QR.
- **Ayuda**: ej. cómo configurar.

"Configurado" no equivale a "Operativo": muchos estados actuales solo indican presencia de campos, no un health check real. No se debe llamar "Operativo" sin verificación real.

## 13. Contrato de secretos (objetivo, sin implementar en C0)

Por defecto no se muestra el valor completo; se muestra "Configurado"/"No configurado". Acciones futuras (según permisos): Reemplazar, Mostrar, Copiar. El modelo futuro debe permitir "mantener valor existente" sin devolver el secreto completo al navegador — esto requiere una fase backend específica (no en C0). El rediseño visual no debe borrar un valor existente solo porque el input no lo carga.

## 14. Ayuda y documentación

Reclasificar la ayuda actual (hoy dominante) en: Help inline (una frase junto al campo), Help drawer/collapsible (guías extensas), Documentación (manual externo/interno), Soporte (detalle SQL/storage). No se borra documentación existente; se reubica progresivamente.

## 15. Acciones de riesgo

Acciones como Stop sesión, reset, desvincular, regenerar QR, eliminar regla, reemplazar credencial deben usar `AlfaConfirmDialog` y jerarquía visual destructiva adecuada en fases futuras. No se ejecutan automáticamente ni se implementan en C0.

## 16. Contrato AlfaDesign a reutilizar

`AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaTabs`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaLookup`, `AlfaNotification`, `AlfaEmptyState`. No crear versiones específicas de Conversaciones sin necesidad real.

Patrones posibles todavía no formalizados por AlfaDesign (a decidir en C1, no crear en C0): Settings Internal Navigation, Secret Input, Settings Section, Configuration Status, Diagnostic Result, Help Drawer, Number/Session Manager, dirty-state contextual.

Configuración **no** copia el layout de Contactos/Usuarios/Técnicos/Clientes; reutiliza shell, componentes, tokens, dialogs y notificaciones, pero su Information Architecture es propia.

## 17. Guardado (estrategia aprobada conceptualmente)

Híbrido por sección. Ejemplos: Guardar WhatsApp General, Guardar Automatizaciones, Guardar un número, Guardar accesos, Guardar credencial avanzada. Test/Verify es siempre una acción separada de Save. No se implementa "Guardar todo".

## 18. Dirty state (objetivo futuro, no implementado en C0)

Dirty state por sección, `NavigationLock`, Guardar, Descartar, detección de no-op, guard de doble submit.

## 19. Responsive objetivo

Diseñar para 2048 / 1440 / 1024. En 1024: compact shell, navegación interna usable, sin scroll horizontal global, formularios legibles, secretos no desbordados, cards contenidas, acciones accesibles. La subnav interna puede necesitar colapsarse o convertirse en selector — a decidir en C1.

## 20. Riesgos de seguridad confirmados (registro, no corrección)

Confirmados en el código real de `main` durante la Fase 0 de reconciliación:

1. `WebInstanceName` / path traversal en sesiones WhatsApp Web (sin containment de path).
2. Configuración de Conversaciones sin autorización robusta (`[Authorize]`/`RequireAuthorization` ausente).
3. Secretos completos enviados y renderizados en el cliente.
4. OAuth de Mercado Libre sin `state` ni identificador de tenant.
5. WhatsApp acepta webhook sin validar firma cuando no hay App Secret configurado.
6. Webhook de Mercado Libre sin verificación de autenticidad.
7. Rutas webhook legacy con fallback siguen habilitadas.
8. Secretos almacenados en texto plano (sin cifrado en reposo).

Estos riesgos **no se corrigen dentro de esta fase ni accidentalmente durante el restyle**. Se corrigen en fases backend explícitas (ver sección 22). El rediseño no debe empeorar ni ampliar la exposición existente, ni ocultar estos problemas fingiendo que están resueltos.

## 21. Funcionalidad que no debe tocarse durante el rediseño

Routing de webhooks, token multibase, claves `CONV_*`, fallback legacy, tablas y relaciones existentes, asociación `IdNumeroWhatsApp`, usuarios por número, administradores, providers, integración Meta, WhatsApp Web, worker Node, OAuth actual (hasta fase explícita), hosted services, runtime, filesystem, auditoría (`AUX_ERR`/`IAppEventService`), compatibilidad con bases antiguas, regla `?directo=1`, navegación funcional completa del módulo.

## 22. Plan de fases (preliminar, sujeto a reorganización en C1)

**Fases UX/producto:**

- **C0** — Baseline + contrato de producto (esta fase).
- **C1** — Shell + Information Architecture + navegación interna.
- **C2** — Resumen de Configuración + Canales.
- **C3** — WhatsApp orientado a números + experiencia QR.
- **C4** — Instagram / Facebook / Mercado Libre.
- **C5** — Automatizaciones + Integraciones IA.
- **C6** — Operación + accesos.
- **C7** — Soporte / ayuda / diagnóstico.
- **C8** — Dirty state + guardado contextual.
- **C9** — Responsive + auditoría legacy + documentación final.

**Fases de seguridad backend (separadas, explícitas, no acopladas al restyle):**

- **SEC-1** — Autorización de Configuración + contrato de secretos.
- **SEC-2** — Path containment de WhatsApp Web (`WebInstanceName`).
- **SEC-3** — OAuth state/tenant + autenticidad de webhooks (Mercado Libre, firma WhatsApp obligatoria).
- **SEC-4** — Storage de secretos / migración a almacenamiento protegido, si se aprueba.

El orden definitivo de las fases SEC se decide antes de ejecutar cada una. Esta lista es preliminar; C1 puede reorganizarla una vez definida la IA final.

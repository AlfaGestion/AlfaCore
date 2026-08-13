# Catálogo de componentes AlfaDesign

[Índice](./README.md) · [Tokens](./alfadesign-tokens.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Guía](./alfadesign-module-guide.md)

La implementación oficial vive en `src/AlfaCore/Components/Shared/AlfaDesign/`. Los componentes reciben contenido y callbacks; no contienen reglas de negocio del módulo. La ruta de cada componente es esa carpeta más el nombre `.razor` (y su `.razor.css` aislado cuando existe).

| Componente | Uso y variantes reales | Estados / accesibilidad | Figma |
|---|---|---|---|
| `AlfaButton` | acción Primary, Secondary, Ghost o Danger; tamaños Sm/Md; icono | disabled, loading, `aria-busy` | Button `48:104` |
| `AlfaIconButton` | acción compacta solo icono, tooltip/label obligatorio | active, disabled, `aria-pressed` | Button `48:104` |
| `AlfaInput` | texto con label, help, required y error | input inmediato, disabled, `aria-invalid` | Input Field `49:192` |
| `AlfaSelect` | catálogo corto con opciones reales | placeholder, disabled, error | Input Field `49:192` |
| `AlfaCheckbox` | booleano o mixed | teclado, disabled, `role=checkbox` | Checkbox `50:65` |
| `AlfaTabs` | navegación entre secciones, badge opcional | active, disabled, `role=tablist` | Tab `50:116` |
| `AlfaTag` | estado/categoría semántica | Neutral, Accent, Success, Warning, Danger | Tag `54:62` |
| `AlfaActionMenu` | overflow contextual con separadores | foco, Escape, backdrop, danger | Action Menu `79:383` |
| `AlfaConfirmDialog` | confirmación destructiva o descarte | loading, error, focus trap, Escape | Confirmation Dialog `80:535` |
| `AlfaDialog` | superficie modal genérica; contenido/footer del consumidor | Sm/Md/Lg, close, backdrop, Escape, focus trap, loading/disabled/error, responsive | patrón Confirmation/Export Dialog `80:535`, `83:951`; falta nodo genérico dedicado |
| `AlfaLookup<TItem>` | búsqueda reusable de relaciones/catálogos grandes | debounce, loading, empty, error, selected, disabled | Lookup / Autocomplete `77:209` |
| `AlfaNotification` | feedback flotante no bloqueante basado en `AppUiMessage` | Success/Info/Warning/Error, auto-dismiss semántico, hover pause, cierre, live region | no hay Toast dedicado; Notification de panel global `86:1146` es otro patrón |
| `AlfaEmptyState` | vacío, sin resultados o error, con acción opcional | estado anunciado con `aria-live` | Activity Panel `61:102`; no hay set dedicado |

Parámetros principales: Button/IconButton reciben variante, tamaño, icono, estado y callback; Input/Select/Checkbox reciben label, valor/cambio, disabled y error; Tabs recibe items, clave activa y callback; Tag recibe tono; ActionMenu recibe items y callbacks; ConfirmDialog/Dialog reciben apertura, textos, contenido/acciones, estado y cierre; Lookup recibe función asíncrona, claves/template, selección, debounce y mínimo de caracteres; EmptyState recibe icono, título, descripción y acción. No usar componentes de formulario como contenedores de reglas de negocio ni Dialog como sustituto de navegación. En viewport estrecho los dialogs limitan alto, el body hace scroll y el footer puede envolver.

`AlfaDialog` es el único shell genérico para overlays de módulo. Su contrato exige render fijo fuera del flujo, backdrop, surface raised sólida, border, shadow, z-index modal, header/body/footer, scroll interno y manejo de foco/cierre. `AlfaLookup<TItem>` ofrece combobox con resultados, estado activo/seleccionado, flechas, Enter y Escape; el primer Escape cierra resultados y el siguiente puede cerrar el dialog. Ningún componente se considera validado solo por aparecer en markup.

`AlfaNotification` recibe `Message`, `OnDismiss` y una `Duration` opcional. Sin override aplica 4/5/8/8 segundos a Success/Info/Warning/Error. El hover pausa el contador y X cierra inmediatamente. Usarlo para resultados y errores no bloqueantes; no reemplaza validación inline, confirmaciones ni errores que requieren una decisión modal.

## Infraestructura compartida relacionada

| Patrón | Implementación | Estado |
|---|---|---|
| App Top Bar | `MainLayout.razor` + `PageHeaderConfig.TopNavigationItems` | compartido; Figma `105:1517` |
| Context Toolbar | `MainPageHeader.razor`, `PageHeaderService`, `PageHeaderModels` | compartido; Figma `32:104` |
| Smart Search | configuración de `MainPageHeader` y estilos en `alfacore-design.css` | patrón compartido, sin componente Razor AlfaDesign aislado; Figma Search Bar `37:2` |
| Feedback | `AppUiMessage`/`IAppUiOperationService`; hosts globales y surface sólida | infraestructura compartida, presentación aún repartida |
| Loading | `AlfaEmptyState` y `AppLoadingFrame` | dos alcances; falta un spinner/status único de catálogo |
| Table | `DataTable`, listados específicos y reglas CSS | patrón, no componente AlfaDesign v1 único; Figma Data Table Row `58:96` |
| Kanban | vistas específicas por módulo | patrón, no componente AlfaDesign v1 único; Figma Kanban Card `59:86` |
| Popover de filtros | Smart Search / MainPageHeader | patrón sin componente AlfaDesign aislado; Figma `81:560` |

## Tabla semántica

| Intención | Implementación oficial |
|---|---|
| Primary action | `AlfaButton` Primary |
| Secondary action | `AlfaButton` Secondary |
| Ghost action | `AlfaButton` Ghost |
| Danger action | `AlfaButton` Danger |
| Icon action | `AlfaIconButton` |
| Input / Select / Checkbox / Tabs / Tag | componente Alfa homónimo |
| Action Menu | `AlfaActionMenu` |
| Confirmación | `AlfaConfirmDialog` |
| Dialog/Modal | `AlfaDialog` |
| Lookup | `AlfaLookup<TItem>` |
| Empty / Error recuperable | `AlfaEmptyState` |
| Feedback de operación | `AlfaNotification` + `AppUiMessage` |
| Popover | Smart Search/MainPageHeader; deuda de componente aislado |
| Loading | `AppLoadingFrame` o estado de `AlfaEmptyState`; deuda de patrón único |
| Smart Search | configuración compartida de `MainPageHeader` |
| Table/List | `DataTable` + patrón CSS; deuda de componente AlfaDesign único |
| Kanban | vista específica del módulo; deuda de componente compartido |

No usar `.btn`, `.form-control`, modal Bootstrap o dropdown Bootstrap cuando exista esta semántica. Las excepciones legacy deben constar en el checklist de la pantalla.

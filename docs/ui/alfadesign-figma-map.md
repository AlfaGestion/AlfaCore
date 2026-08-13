# Mapa Figma ↔ código

[Índice](./README.md) · [Componentes](./alfadesign-components.md) · [Contactos](./alfadesign-contactos-reference.md)

## Archivo oficial

- Nombre: AlfaCore
- URL: <https://www.figma.com/design/nNMmjOZSl1w5hlPzbfhJs4/AlfaCore>
- File key: `nNMmjOZSl1w5hlPzbfhJs4`
- Auditoría directa: 2026-08-13, solo lectura mediante Figma MCP.

Páginas reales: `00 — Auditoría` (`0:1`), `01 — Foundations` (`2:2`), `02 — Components` (`2:3`), `03 — Templates` (`24:2`, vacía), `04 — Modules` (`2:5`), `05 — Prototype` (`2:6`) y `99 — Coverage` (`2:7`).

## Patrones y componentes

| Figma | Nodo | Código / contrato | Estado |
|---|---:|---|---|
| App Top Bar | `105:1517` | `MainLayout.razor`; [v1](./alfadesign-v1.md#1-app-top-bar) | implementado |
| Context Toolbar | `32:104` | `MainPageHeader.razor`, `PageHeaderService`; [v1](./alfadesign-v1.md#2-context-toolbar) | implementado |
| Search Bar | `37:2` | Smart Search en `MainPageHeader`/CSS | patrón implementado, sin componente aislado |
| Button | `48:104` | `AlfaButton`, `AlfaIconButton` | implementado |
| Input Field | `49:192` | `AlfaInput`, `AlfaSelect` | implementado |
| Checkbox | `50:65` | `AlfaCheckbox` | implementado |
| Tab | `50:116` | `AlfaTabs` | implementado |
| Tag | `54:62` | `AlfaTag` | implementado |
| Smart Button | `55:47` | patrón específico de header de Contactos | sin componente compartido |
| Data Table Row | `58:96` | listados/DataTable | deuda de unificación |
| Kanban Card | `59:86` | `ContactosKanbanView` | implementación de módulo |
| Contact Header | `60:38` | `ContactosRecordView`, `ContactosEditView` | implementación de referencia |
| Activity Panel | `61:102` | `ContactosRecordView` | implementación de referencia |
| Lookup / Autocomplete | `77:209` | `AlfaLookup<TItem>`; `LookupAutocomplete` queda como componente anterior específico | implementado compartido |
| Action Menu | `79:383` | `AlfaActionMenu` | implementado |
| Confirmation Dialog | `80:535` | `AlfaConfirmDialog` | implementado |
| Filter Popover | `81:560` | Smart Search | patrón, sin componente aislado |
| Export Dialog | `83:951` | sin componente AlfaDesign v1 | deuda |
| App/global header panels | `85:1090`, `86:1563` | shell/hosts globales | parcial |
| Notification de panel global | `86:1146` | no equivale a toast; referencia de superficie/contenido | implementado en su dominio |
| Notification/Toast flotante | no existe nodo dedicado | `AlfaNotification` + `AppUiMessage` | deuda Figma; usa lenguaje AlfaDesign aprobado |
| Dialog genérico | no existe nodo dedicado | `AlfaDialog` basado en patrones `80:535`/`83:951` | falta referencia específica en Figma |

La re-auditoría directa del 2026-08-13 confirmó además:

- Lookup abierto de documentación `77:221`: 320 px, Field + Results panel de 160 px; estados del set `77:209`: Closed, Open, Searching, Empty, Loading y Selected.
- Confirmation Dialog de referencia `80:540`: 460×240, surface `#16181D`, borde `#333640`, radio 8, padding 20/20/16, sombra y estructura Header/Message/Context note/Footer.
- Export Dialog de referencia `83:955`: 780×610, surface `#16181D`, borde `#333640`, radio 8, sombra, Header/contenido/Footer.
- Overlay en Modules: `Modal scrim` `108:2277` y `Confirmation Dialog` `108:2278`; otro scrim real `113:3646`.

No apareció un component set llamado Dialog genérico: `AlfaDialog` debe seguir los patrones compartidos verificados de Confirmation y Export Dialog, sin inventar una variante local.

La búsqueda directa por Notification/Toast/Feedback/Alert/Message/Success/Error/Warning/Info confirmó `Kind=Notification, State=Default` (`86:1146`, 388×78), Unread (`86:1156`) y Hover (`86:1166`) dentro del panel global. No apareció un componente dedicado de toast flotante con variantes semánticas; `AlfaNotification` queda documentado como deuda de sincronización Figma, no como equivalencia de esos items.

## Pantallas Contactos reales

Todas son frames 1440×900 en `04 — Modules`:

| Pantalla | Nodo | Estructura comprobada |
|---|---:|---|
| Listado | `62:2` | App Top Bar `62:3`, Context Toolbar `62:28`, Content `62:68` |
| Selección múltiple | `62:385` | App Top Bar `62:386`, Context Toolbar `62:411`, Content `62:455` |
| Kanban | `62:772` | App Top Bar `62:773`, Context Toolbar `62:798`, Content `62:838` |
| Ficha lectura | `64:837` | App Top Bar `64:838`, Context Toolbar `64:863`, Record content `64:872` |
| Nuevo | `66:954` | App Top Bar `66:955`, Context Toolbar `66:980`, New contact form `66:987` |
| Edición | `67:1073` | App Top Bar `67:1074`, Context Toolbar `67:1099`, Record content `67:1106` |
| Validación | `68:1242` | App Top Bar `68:1243`, Context Toolbar `68:1268`, banner `68:1277`, form `68:1281` |

También existen prototipos y flows de Contactos en `05 — Prototype`, y coverage `Coverage / Contactos / V1` (`70:2`).

## Cuándo consultar Figma

No hace falta abrir Figma para reutilizar un componente ya documentado sin cambiar estructura. Es obligatorio para nuevas pantallas, cambios de jerarquía/layout, componente reutilizable nuevo, dudas visuales o divergencias. No se copian screenshots como fuente primaria ni se inventan node IDs.

## Code Connect

La consulta directa fue rechazada porque el usuario autenticado no posee asiento Dev/Full en un plan Organization/Enterprise. Por eso el estado de mappings existentes es **no verificable con el acceso actual**, no “inexistente”. No se crearon mappings. Una fase posterior debería priorizar App Top Bar, Context Toolbar, Button, Input, Checkbox, Tabs, Lookup, Action Menu y dialogs.

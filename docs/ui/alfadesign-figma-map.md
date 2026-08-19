# Mapa Figma - Código

[Índice](./README.md) · [Componentes](./alfadesign-components.md) · [Contactos](./alfadesign-contactos-reference.md) · [Usuarios](./alfadesign-usuarios-reference.md) · [Técnicos](./alfadesign-tecnicos-reference.md)

## Archivo Oficial

- Nombre: AlfaCore
- URL: <https://www.figma.com/design/nNMmjOZSl1w5hlPzbfhJs4/AlfaCore>
- File key: `nNMmjOZSl1w5hlPzbfhJs4`
- Auditoría directa disponible: 2026-08-13, solo lectura mediante Figma MCP.

Páginas reales: `00 - Auditoría` (`0:1`), `01 - Foundations` (`2:2`), `02 - Components` (`2:3`), `03 - Templates` (`24:2`, vacía), `04 - Modules` (`2:5`), `05 - Prototype` (`2:6`) y `99 - Coverage` (`2:7`).

## Componentes Y Patrones

| Figma | Nodo | Código / contrato | Estado |
|---|---:|---|---|
| App Top Bar | `105:1517` | `MainLayout`, [v1](./alfadesign-v1.md#app-top-bar) | implementado |
| Context Toolbar | `32:104` | `MainPageHeader`, `PageHeaderService` | implementado |
| Search Bar | `37:2` | Smart Search en toolbar + CSS/JS | patrón implementado, sin componente aislado |
| Button | `48:104` | `AlfaButton`, `AlfaIconButton` | implementado |
| Input Field | `49:192` | `AlfaInput`, `AlfaSelect` | implementado |
| Checkbox | `50:65` | `AlfaCheckbox` | implementado |
| Tab | `50:116` | `AlfaTabs` | implementado |
| Tag | `54:62` | `AlfaTag` | implementado |
| Smart Button | `55:47` | patrón específico de header/acciones | sin componente compartido |
| Data Table Row | `58:96` | listados/DataTable | patrón; deuda de unificación |
| Kanban Card | `59:86` | `ContactosKanbanView` | implementación de módulo |
| Contact Header | `60:38` | `ContactosRecordView`, `ContactosEditView` | referencia Contactos |
| Activity Panel | `61:102` | `ContactosRecordView` | referencia Contactos |
| Lookup / Autocomplete | `77:209` | `AlfaLookup<TItem>` | implementado |
| Action Menu | `79:383` | `AlfaActionMenu` | implementado |
| Confirmation Dialog | `80:535` | `AlfaConfirmDialog` | implementado |
| Filter Popover | `81:560` | Smart Search popover | patrón; comportamiento viewport-aware implementado |
| Export Dialog | `83:951` | sin componente AlfaDesign v1 dedicado | deuda |
| Notification de panel global | `86:1146` | referencia visual, no toast flotante | deuda de equivalencia |
| Notification/Toast flotante | no existe nodo dedicado | `AlfaNotification` + `AppUiMessage` | Figma debt |
| Dialog genérico | no existe nodo dedicado | `AlfaDialog` basado en Confirmation/Export | Figma debt |
| Data View Footer | no existe nodo dedicado | `.alfa-data-view-footer` | implementation contract / Figma debt |
| Column resize | no existe nodo dedicado | `AlfaDataViewColumnSizing` + JS helper | implementation contract / Figma debt |
| Smart Search compact/standard/wide | no existe set semántico dedicado | variantes CSS/JS por complejidad | implementation contract / Figma debt |

No inventar nuevos node IDs. Cuando Figma no tiene componente semántico dedicado, documentar como deuda Figma o contrato de implementación, no como equivalencia falsa.

## Pantallas Contactos

Frames 1440x900 en `04 - Modules`:

| Pantalla | Nodo | Estructura |
|---|---:|---|
| Listado | `62:2` | App Top Bar `62:3`, Context Toolbar `62:28`, Content `62:68` |
| Selección múltiple | `62:385` | App Top Bar `62:386`, Context Toolbar `62:411`, Content `62:455` |
| Kanban | `62:772` | App Top Bar `62:773`, Context Toolbar `62:798`, Content `62:838` |
| Ficha lectura | `64:837` | App Top Bar `64:838`, Context Toolbar `64:863`, Record content `64:872` |
| Nuevo | `66:954` | App Top Bar `66:955`, Context Toolbar `66:980`, form `66:987` |
| Edición | `67:1073` | App Top Bar `67:1074`, Context Toolbar `67:1099`, content `67:1106` |
| Validación | `68:1242` | App Top Bar `68:1243`, Context Toolbar `68:1268`, banner `68:1277`, form `68:1281` |

## Pantallas Usuarios

| Pantalla | Nodo | Adaptación productiva |
|---|---:|---|
| Listado | `107:1363` | conserva campos reales, sin rol ficticio |
| Nuevo | `108:1640` | editor completo, no split browse/editor |
| Edición | `108:1840` | compatibilidad de contraseña, metadata y relación técnica reales |
| Validación | `108:2036` | errores inline y feedback compartido |
| Confirmación de baja | `108:2235` | `AlfaConfirmDialog` |

## Técnicos

No existe frame específico de Técnicos. Usa referencias de Usuarios (`107:1363`, `108:1640`, `108:1840`, `108:2036`, `108:2235`) y componentes/patrones: Data Table Row `58:96`, Context Toolbar `32:104`, Search `37:2`, Input/Select `49:192`, Checkbox `50:65`, Tag `54:62`, Dialog `80:535`.

## Code Connect

La consulta directa fue rechazada por falta de asiento Dev/Full en plan Organization/Enterprise. El estado de mappings existentes es no verificable con el acceso actual. No se crearon mappings.

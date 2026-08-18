# Componentes Y Patrones AlfaDesign

[Índice](./README.md) · [Tokens](./alfadesign-tokens.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Guía](./alfadesign-module-guide.md)

La implementación reusable vive en `src/AlfaCore/Components/Shared/AlfaDesign/`. Los componentes reciben contenido, estado y callbacks; no contienen reglas de negocio del módulo.

## Componentes Razor

| Componente | Uso | Estado |
|---|---|---|
| `AlfaButton` | Primary, Secondary, Ghost, Danger; Sm/Md; icono/loading | compartido |
| `AlfaIconButton` | acción compacta con label/tooltip obligatorio | compartido |
| `AlfaInput` | texto con label, help, required y error | compartido |
| `AlfaSelect` | catálogo corto con opciones reales | compartido |
| `AlfaCheckbox` | booleano o mixed | compartido |
| `AlfaTabs` | navegación de secciones | compartido |
| `AlfaTag` | estado/categoría semántica | compartido |
| `AlfaActionMenu` | overflow contextual | compartido |
| `AlfaConfirmDialog` | confirmación destructiva/descarte | compartido |
| `AlfaDialog` | shell modal genérico | compartido |
| `AlfaLookup<TItem>` | búsqueda de relaciones/catálogos grandes | compartido |
| `AlfaNotification` | feedback flotante con `AppUiMessage` | compartido |
| `AlfaEmptyState` | vacío, sin resultados, error recuperable | compartido |

No usar `.btn`, `.form-control`, modal Bootstrap o dropdown Bootstrap cuando exista esta semántica. Que un componente aparezca en Razor no acredita cumplimiento: debe renderizar e integrarse según contrato.

## Patrones Compartidos

| Patrón | Implementación actual | Contrato |
|---|---|---|
| App Top Bar | `MainLayout` + `PageHeaderConfig.TopNavigationItems` | global, no se duplica |
| Context Toolbar | `MainPageHeader`, `PageHeaderService`, modelos de header | acciones, búsqueda, vistas y paginación reales |
| Smart Search | `MainPageHeader` + markup de módulo + `alfacore-design.css` | patrón compartido, no componente Razor único |
| Filter Popover | Smart Search + `alfa-smart-search-popover.js` | anclado al trigger, viewport-aware, clamped |
| Data View | estructura de cada Browse/List/Kanban | Header + content/rows + Footer |
| Data View Footer | `.alfa-data-view-footer` | resumen/status y controles secundarios; no repite nombre de módulo |
| Data View Column Sizing | `AlfaDataViewColumnSizing`, CSS scoped y `alfa-data-view-columns.js` | opcional, con min/default/max, preview/commit y persistencia |
| Sticky Actions | CSS scoped de tabla | opcional para tablas con scroll horizontal |
| Table/List | tablas específicas, `DataTable` donde aplique | patrón, no DataTable AlfaDesign universal |
| Kanban | vistas específicas por módulo | patrón, no componente compartido universal |

## Smart Search

Variantes semánticas:

- compact: 408 px preferred, filtros simples y acciones.
- standard: 520 px preferred, contenido medio en dos filas conceptuales.
- wide: 760 px preferred, contenido adicional real como filtro personalizado full-row.

El ancho siempre se clampa al viewport. Compact/standard ubican `Acciones | Aplicar/Limpiar` como unidad funcional, sin separación artificial full-row. Wide puede usar más columnas y reflow responsive.

## Data View Footer

Clases oficiales:

- `.alfa-data-view-footer`
- `.alfa-data-view-footer__summary`
- `.alfa-data-view-footer__controls`
- `.alfa-data-view-footer__page-size`

Formato de texto: `14 registros · Página 1 de 1`; con estado adicional: `311 registros · Página 1 de 7 · 2 seleccionados · Sin agrupar · 1 filtro`.

## Column Sizing

No crear un componente DataTable prematuro. El módulo define metadata por columna y usa helper compartido si necesita drag. Persistencia por usuario via configuración existente (`TA_CONFIGURACION`), no `localStorage`. Reset de widths conserva visibilidad, orden y agrupación.

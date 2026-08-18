# AlfaDesign v1

[Índice](./README.md) · [Tokens](./alfadesign-tokens.md) · [Componentes](./alfadesign-components.md) · [Checklist](./alfadesign-checklist.md)

AlfaDesign v1 es desktop-first, dark, compacto y orientado a ERP. Prioriza densidad, jerarquía clara, superficies sólidas, navegación estable, accesibilidad y preservación de funcionalidad productiva. La UI se migra sin reescribir backend ni simular capacidades que el dominio real no tiene.

## Fuentes

- Código, servicios, permisos y SQL oficial: comportamiento real.
- Figma: intención visual y nodos de referencia.
- `docs/ui`: contrato de implementación.
- Componentes AlfaDesign: implementación reutilizable.

Ante conflicto, no se inventa una variante local: se registra la diferencia y se decide qué fuente actualizar.

## Shell

El shell AlfaDesign ocupa el viewport completo. App Top Bar y Context Toolbar quedan fuera del scroll; el contenido usa `min-height: 0` y scroll interno. No hay sidebar global, barras `fixed` superpuestas, compensaciones por `padding-top` ni overflow horizontal global.

### App Top Bar

Global y obligatoria. En desktop mide aproximadamente 44 px; en modo compacto, alrededor de 40 px. La implementa el shell (`MainLayout`) y contiene AlfaCore, módulo activo, navegación permitida, base y usuario. La bajada `AlfaCore` + nombre del módulo se conserva también en compacto: se ajustan spacing, line-height y densidad, no se oculta la identidad del módulo.

Con `?directo=1`, AlfaCore debe quedar encerrado en el módulo y ocultar Aplicaciones/accesos a otros módulos según la regla global.

### Context Toolbar

Compartida y obligatoria. La implementan `MainPageHeader`, `PageHeaderService` y modelos de header. En desktop mide aproximadamente 44 px; en compacto, alrededor de 40 px. El módulo publica solo acciones y estado reales:

- Browse: acciones de colección, Smart Search si hay colección consultable, filtros, View Switcher real y paginación principal.
- Record: volver, editar/overflow y navegación anterior/posición/siguiente cuando existe.
- Edit/New: guardar, cancelar y acciones reales; normalmente sin búsqueda ni paginación.

No se reconstruye una toolbar local dentro del módulo.

## Smart Search Y Filter Popover

Smart Search es un patrón compartido configurado desde la Context Toolbar y estilos/JS comunes. No es un componente Razor monolítico.

### Popover Anclado

El Filter Popover está visualmente anclado al Smart Search real. No es un modal centrado. La geometría pertenece al helper JS compartido (`alfa-smart-search-popover.js`); la apariencia, spacing y grid pertenecen a CSS.

El helper calcula conceptualmente `triggerRect`, ancho preferido del popover, viewport y gutter seguro. Ubica `top` debajo del trigger y `left` clamped dentro del viewport; también limita `max-height` por espacio vertical disponible. No usar como arquitectura final: `left:50%`, fixed centrado, top hardcodeado por barras, `!important` como ownership o posicionamiento absoluto sin viewport clamp.

### Sizing Semántico

Las variantes representan complejidad del contenido, no módulo ni breakpoint:

| Variante | Preferred width actual | Uso |
|---|---:|---|
| compact | 408 px | pocos grupos principales y acciones |
| standard | 520 px | contenido medio, dos filas conceptuales |
| wide | 760 px | contenido adicional real, por ejemplo filtro personalizado full-row |

Siempre se clampa al viewport. Implementación actual: Usuarios compact, Técnicos compact, Clientes standard, Contactos wide.

### Grid Y Acciones

Compact usa 2 columnas conceptuales: `Estado | Tipo/Agrupar` y `Acciones | Aplicar/Limpiar`. Standard usa 2 columnas: `Estado | Bloqueo`, `Ubicación | Agrupar`, `Acciones | Aplicar/Limpiar`. Wide puede usar más columnas si el contenido lo justifica; Contactos usa Filtros, Agrupar, Favoritos y filtro personalizado full-row.

`Acciones` y `Aplicar/Limpiar` forman una unidad funcional. No se separan artificialmente con `width:100%` y `justify-content:flex-end` si eso los manda a extremos opuestos. A viewport reducido el panel reflowea; alrededor de 720 px puede pasar a una columna.

## Data View

Data View es la representación navegable de una colección.

```text
Data View
├─ Data View Header
├─ Scrollable content / Rows
└─ Data View Footer
```

### Data View Header

Pertenece a List/Table/Grid. Contiene encabezados de columna o estructura propia de la vista. No es Context Toolbar, no es navegación y no aparece en Record/Edit/New.

### Rows / Content

Las filas deben ser densas, legibles y con overflow interno. El scroll horizontal, si existe, pertenece al contenedor de Data View, nunca al documento global.

### Data View Footer

Contrato aprobado: el footer pertenece al listado, aparece al final real de la Data View, no es sticky al viewport, no es toolbar y no es header.

Usa un único patrón visual compartido: `.alfa-data-view-footer`, `.alfa-data-view-footer__summary`, `.alfa-data-view-footer__controls`. No repite nombres de módulo como Contactos, Usuarios, Técnicos o Clientes. La identidad ya vive en App Top Bar y navegación activa.

Formato:

```text
14 registros · Página 1 de 1
311 registros · Página 1 de 7 · 2 seleccionados · Sin agrupar · 1 filtro
```

Reglas: separador `·`, texto secondary/muted, peso medio, altura compacta, borde superior sutil, misma jerarquía visual en todos los módulos. Preferir pluralización natural (`1 registro`, `14 registros`) cuando la lógica lo permita.

La paginación principal (`‹ 1/7 ›`) permanece en Context Toolbar. El selector `25/50/100 por página` puede vivir en el footer como control secundario alineado a la derecha. No duplicar paginación arriba y abajo.

## Column Sizing

Column resize es una capacidad AlfaDesign opcional. Se usa cuando el volumen o heterogeneidad de columnas lo justifica; actualmente el ejemplo real es Browse de Clientes.

Modelo conceptual: `Key`, `MinWidth`, `DefaultWidth`, `MaxWidth`, `Resizable`. Infraestructura actual: `AlfaDataViewColumnSizing` y `alfa-data-view-columns.js`.

Reglas:

- Columnas estructurales (checkbox/selector, Actions, icon-only) no son redimensionables.
- Toda columna redimensionable tiene min/default/max; no se permite width 0, columnas ilegibles ni expansión infinita.
- Al reducir, usar `nowrap`, `overflow:hidden` y `text-overflow:ellipsis` cuando corresponde, sin cortar tags/actions.
- Resize no reemplaza horizontal scroll: conviven min widths, ellipsis y scroll interno como fallback.
- Persistencia por usuario usa la configuración existente en `TA_CONFIGURACION`; no `localStorage`.
- `WidthPx` es opcional. Configuración antigua sin `WidthPx` usa default; configuración con `WidthPx` se clampa min/max.
- Durante drag hay preview visual; al soltar hay commit/persistencia. No escribir DB por cada pixel.
- Configurar vista puede ofrecer Restablecer anchos: `WidthPx -> null/default` conservando visibilidad, orden y agrupación.

## Sticky Actions

Patrón opcional de Data View. Cuando una tabla tiene scroll horizontal y acciones por fila, Actions puede ser sticky right. Debe tener ancho fijo, no ser resizable, usar background opaco, header sticky, separador izquierdo sutil y respetar zebra, hover y selected. No debe quedar pegada o cortada contra el scrollbar.

## Configurar Vista

Configurar vista administra visibilidad, orden, agrupación y, si aplica, reset de widths. Usa `AlfaDialog`, `AlfaSelect`, `AlfaCheckbox`, `AlfaIconButton` y `AlfaButton`. No es un filtro, no es toolbar y no debe volver como panel inline legacy en un módulo AlfaDesign migrado.

## Component-First

Si existe componente AlfaDesign, se reutiliza: Button, Dialog, Confirm, Notification, Lookup, Tabs, Input, Select, Checkbox, Tag, EmptyState y ActionMenu. Tabla y Smart Search siguen siendo patrones/infraestructura, no un único componente Razor monolítico.

## Overlays Y Feedback

Los modales usan `AlfaDialog` o `AlfaConfirmDialog`; deben renderizar como overlay real con backdrop, surface sólida, z-index correcto, foco y scroll interno. El feedback no bloqueante usa `AlfaNotification` + `AppUiMessage`; nunca comunica solo por color.

## Responsive

Validar 2048, 1440 y 1024 px. Desktop amplio no debe alterarse innecesariamente por arreglos de 1024. En compacto se reduce densidad del shell sin ocultar la identidad del módulo. No debe haber overflow horizontal global; paneles y dialogs deben permanecer utilizables.

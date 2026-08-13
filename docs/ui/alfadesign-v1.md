# AlfaDesign v1

[Índice](./README.md) · [Tokens](./alfadesign-tokens.md) · [Componentes](./alfadesign-components.md) · [Checklist](./alfadesign-checklist.md)

## Principios

AlfaDesign v1 es desktop-first, dark, compacto y orientado a ERP: prioriza alta densidad de información, jerarquía clara, superficies sólidas, accent moderado, legibilidad, navegación estable y comportamiento accesible. Puede tomar referencias estructurales de productos como Odoo, sin copiar su identidad visual. La presentación se migra sin reescribir la funcionalidad y los componentes prevalecen sobre estilos locales. Un concepto debe conservar la misma familia visual en todos sus usos.

## Jerarquía de fuentes

- Comportamiento y datos: código, servicios, permisos y objetos SQL oficiales.
- Apariencia visual: Figma oficial y decisiones visuales aprobadas en producto.
- Reglas de implementación: este contrato.
- Patrones reutilizables: componentes compartidos AlfaDesign.

## Las tres capas de pantalla

El shell ocupa el viewport completo como columna flex. App Top Bar y Context Toolbar quedan fuera del scroll; el contenido usa `min-height: 0` y scroll interno. No hay sidebar global, barras `fixed` superpuestas, compensaciones con `padding-top` ni scroll horizontal global. Un sidebar interno solo aparece cuando el contexto de la pantalla lo necesita.

### 1. App Top Bar

Global y obligatoria en toda pantalla AlfaDesign. Mide 44 px y la implementa `MainLayout.razor`; contiene identidad de AlfaCore, módulo activo, icono, navegación principal permitida, acciones globales, base activa y usuario. Un módulo configura la infraestructura existente, nunca crea otra Top Bar ni hardcodea comportamiento de Contactos como universal. Con `?directo=1` el shell debe ocultar Aplicaciones y accesos a otros módulos según la regla global.

### 2. Context Toolbar

Compartida y obligatoria, inmediatamente debajo de la App Top Bar. La implementan `MainPageHeader` y `PageHeaderService`; el módulo publica acciones, búsqueda, filtros, paginación, selector de vista y contexto que realmente existan.

- Browse: acciones de colección, Smart Search cuando hay colección consultable, filtros, contador/paginación y vistas reales.
- Record: volver, editar/acciones reales y navegación anterior/posición/siguiente en la misma fila.
- Edit/New: guardar, cancelar y acciones reales; normalmente sin búsqueda ni paginación.

Smart Search, paginación y View Switcher son contextuales: no se simulan. La barra mide 44 px y no se reconstruye localmente.

- Smart Search solo aparece ante una colección realmente consultable, normalmente Browse/List y Browse/Kanban.
- Paginación muestra páginas en Browse o anterior/posición/siguiente en Record cuando existe navegación real; Edit/New no la simulan.
- View Switcher muestra únicamente vistas implementadas.
- Las acciones dependen de estado, permisos y selección; el módulo publica configuración, no HTML de una toolbar paralela.

### 3. Data View Header

Es el encabezado de una representación de datos, no una tercera barra global. Sus variantes conceptuales son List Header, Table Header o Grid Header. Es opcional y aparece únicamente en List/Table/Grid que lo necesiten. Kanban, Record, Edit y New no arrastran un encabezado de tabla ni una tercera barra vacía.

La estructura resultante siempre se razona como:

```text
App Top Bar
Context Toolbar
Content
  └─ Data View Header (opcional: List/Table/Grid)
```

## Estados estándar

- Browse: listado, Kanban o consulta de una colección.
- Record: lectura de un registro con identidad, tabs y contexto relacionado.
- Edit: preferentemente la arquitectura de Record convertida a edición.
- New: preferentemente reutiliza Edit con modelo inicial limpio.

No todos los módulos necesitan los cuatro estados. Los estados de carga, vacío, error, validación, procesamiento y confirmación deben mantener vivo el shell.

## Dialogs y lookups

Todo modal de módulo usa `AlfaDialog`; no se construyen overlays manuales. Debe renderizar backdrop fijo sobre el viewport y una surface sólida centrada, con header/body/footer contenidos, z-index sobre el shell, ancho y alto controlados, scroll interno del body, foco encerrado, Escape y restauración razonable de foco. Que `<AlfaDialog>` exista en Razor no acredita cumplimiento: debe verificarse su render y comportamiento reales.

Los selectores de relaciones o catálogos grandes usan `AlfaLookup<TItem>` cuando corresponda. Su panel de resultados queda contenido por el dialog, no expande el documento y cubre hover, selección, búsqueda, loading, empty, error y teclado.

## Notificaciones

El feedback flotante no bloqueante usa `AlfaNotification` con `AppUiMessage`. Mantiene surface-raised sólida, borde y sombra AlfaDesign, título y explicación textual, cierre manual y una línea semántica fina: success, accent para info y danger para warning/error. Nunca comunica el estado únicamente por color.

Duraciones predeterminadas: Success 4 segundos, Info 5, Warning 8 y Error 8. El hover pausa el tiempo restante y al salir continúa; una nueva notificación reinicia su propia duración. Warning/Error usan anuncio assertive y Success/Info polite. La notificación queda sobre shell y dialogs sin bloquear la interacción global.

## Escala y responsive

La escala base está en [tokens](./alfadesign-tokens.md). Las pantallas se validan a 2048, 1440 y 1024 px, sin scroll horizontal. A 1024 los paneles laterales pueden bajar debajo; no se reducen tipografías hasta volverlas miniatura.

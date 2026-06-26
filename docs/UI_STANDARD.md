# Estandar visual AlfaCore

Este documento define el criterio unico de interfaz para AlfaCore. La normalizacion es visual: no cambia reglas de negocio, eventos, consultas, permisos ni navegacion.

## Plantilla base

Todas las pantallas de gestion deben seguir este orden:

1. Barra de acciones de modulo.
2. Barra de busqueda y filtros.
3. Contenido principal: tabla, formulario, listado, tablero o detalle.

La barra de acciones usa botones `btn`, `btn--primary`, `btn--ghost`, `btn--danger` y tamanos `btn--sm` cuando corresponde. Las acciones principales de edicion, como Guardar y Cancelar, deben estar en el encabezado del editor o en una barra visible antes del formulario.

## Componentes y clases comunes

- Layout: `page-grid`, `page-intro`, `panel-card`, `panel-card__header`.
- Acciones: `actions`, `btn`, `btn--primary`, `btn--ghost`, `btn--secondary`, `btn--danger`, `btn--sm`.
- Filtros: `smart-search-card`, `smart-search`, `filters-grid`, `field`.
- Tablas: `usuarios-table-wrap`, `table-wrap`, `data-table`, `result-pagination`.
- Formularios: `field`, `usuarios-editor`, `usuarios-editor__grid`, `usuarios-editor__actions`.
- Estados vacios y mensajes: `usuarios-empty`, `empty-state`, `editor-msg`.

## Medidas visuales

- Radio base: 8px para contenedores, botones, inputs y tablas.
- Separacion base: 8px entre controles y 12px entre bloques.
- Altura minima de controles: 36px.
- Tablas compactas: filas legibles, encabezados consistentes, hover uniforme y scroll horizontal controlado.
- Formularios: etiquetas arriba del campo, grillas de 2 columnas en escritorio y 1 columna en pantallas chicas.

## Comportamiento responsive

En tablets y notebooks chicas, las barras de acciones y filtros pasan a una columna sin perder orden. Las tablas mantienen scroll horizontal dentro del contenedor, no en toda la pagina.

## Excepciones justificadas

- Punto de venta: mantiene layout full-screen operativo porque funciona como caja/catalogo/carrito y no como ABM.
- Conversaciones: mantiene inbox/chat/composer porque es una herramienta de mensajeria.
- Tickets Kanban: mantiene columnas arrastrables; la vista lista si debe usar tablas y filtros estandar.
- Calendario: mantiene grilla de calendario y panel lateral por naturaleza del modulo.
- Carga de viajes: mantiene modales operativos y edicion rapida de importes; se normalizan botones, filtros, tablas y densidad visual sin cambiar el flujo de teclado.
- Reuniones publicas: usa `PublicLayout` y no forma parte del shell administrativo.

Estas excepciones igual deben respetar tokens visuales, botones, inputs, mensajes y espaciados comunes cuando no afecte su flujo especifico.

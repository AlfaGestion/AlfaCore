# Guía de módulos AlfaDesign

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Componentes](./alfadesign-components.md) · [Checklist](./alfadesign-checklist.md)

## Proceso obligatorio

1. Auditar la lógica existente.
2. Inventariar pantallas.
3. Inventariar estados: Browse, Record, Edit, New y estados transitorios reales.
4. Identificar queries y objetos oficiales.
5. Identificar operaciones reales, permisos, persistencia y auditoría.
6. Identificar estilos y componentes legacy.
7. Revisar componentes AlfaDesign y el catálogo.
8. Revisar Figma cuando la composición o el patrón lo requiera.
9. Migrar el shell mediante la infraestructura global.
10. Migrar/configurar la Context Toolbar compartida.
11. Migrar contenido, Data View Header y overlays usando tokens/componentes.
12. Mantener handlers, rutas, URL/history y comportamiento.
13. Validar responsive a 2048/1440/1024, teclado, foco y scroll.
14. Ejecutar el checklist técnico y visual.
15. Documentar excepciones, deuda, regresiones y legacy restante.

Antes de crear UI, auditar explícitamente shell, encabezado global, toolbar, buscador, filtros, paginación, acciones, selector de vista, encabezado table/grid, navegación Record y estados Edit/New. Clasificar cada uno como AlfaDesign reutilizable, legacy a migrar, faltante requerido por funcionalidad real o innecesario. App Top Bar, Context Toolbar, Smart Search, paginador y View Switcher se configuran desde componentes/servicios compartidos; no se reconstruyen dentro del módulo.

Migrar presentación no significa reescribir funcionalidad. No se crean servicios V2, SQL ad hoc, búsquedas falsas, acciones sin backend ni componentes de dominio dentro de shells genéricos.

## Regla component-first

Antes de crear button, input, select, checkbox, tabs, tag, menú, confirmación, dialog, lookup, empty state o feedback:

1. buscar en `Components/Shared/AlfaDesign`;
2. revisar [catálogo](./alfadesign-components.md);
3. revisar el nodo real en el [mapa Figma](./alfadesign-figma-map.md);
4. reutilizar;
5. solo si falta y el patrón es general, crear un componente compartido, tokenizado y documentado.

No crear barras locales que dupliquen App Top Bar o Context Toolbar. Data View Header solo pertenece a List/Table/Grid.

## Guardas de dominio para Usuarios y seguridad

Una referencia visual no autoriza a inventar modelo de seguridad. Antes de trasladar patrones de Usuarios:

- confirmar los campos y operaciones reales de `TA_USUARIOS` y los servicios oficiales;
- tratar `EsGrupo` como la clasificación legacy que es, no como un rol;
- mantener roles y permisos fuera del editor mientras no exista un contrato backend explícito;
- no deducir permisos a partir de una maqueta Figma ni crear opciones sin persistencia real;
- no ampliar la exposición de contraseñas, logs, auditoría, JavaScript, documentación o snapshots serializados;
- documentar por separado las deudas funcionales y de seguridad que una migración visual no resuelve.

La [referencia de Usuarios](./alfadesign-usuarios-reference.md) registra las diferencias deliberadas entre Figma y el dominio productivo actual.

## Elegir arquitectura según el dominio real

AlfaDesign no impone un único recorrido:

- CRUD con ficha: `Browse → Record → Edit/New`. Referencia: [Contactos](./alfadesign-contactos-reference.md).
- ABM administrativo: `Browse → Edit/New`, sin Record artificial. Referencia: [Usuarios](./alfadesign-usuarios-reference.md).

El inventario funcional determina qué estados existen. No agregar una Ficha, lista lateral, Smart Search, permisos o navegación intermedia sólo para imitar otro módulo o un frame de Figma.

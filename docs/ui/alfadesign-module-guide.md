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

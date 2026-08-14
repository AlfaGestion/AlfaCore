# Guía De Módulos AlfaDesign

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Componentes](./alfadesign-components.md) · [Checklist](./alfadesign-checklist.md)

## Proceso

1. Auditar dominio, datos, permisos, rutas, servicios y operaciones reales.
2. Elegir arquitectura: CRUD con ficha, ABM administrativo, ABM con entidad relacionada, u otra derivada del dominio.
3. Inventariar estados: Browse, Record, Edit, New y transitorios reales.
4. Usar App Top Bar y Context Toolbar compartidas.
5. Elegir Smart Search por complejidad de contenido: compact, standard o wide.
6. Definir Data View: Header, rows/content y Footer.
7. Evaluar si column resize aporta valor.
8. Decidir sticky Actions según overflow horizontal y acciones por fila.
9. Usar Data View Footer compartido.
10. Migrar overlays y feedback con componentes AlfaDesign.
11. Mantener backend, semántica, auditoría, URL/history y callbacks reales.
12. Validar 2048/1440/1024, teclado, foco y scroll.
13. Ejecutar checklist y documentar excepciones.

No copiar módulos literalmente. Contactos, Usuarios y Técnicos son referencias de arquitectura, no plantillas universales.

## Component-First

Antes de crear button, input, select, checkbox, tabs, tag, menú, confirmación, dialog, lookup, empty state o feedback:

1. Buscar en `Components/Shared/AlfaDesign`.
2. Revisar el catálogo.
3. Revisar Figma si cambia la estructura o jerarquía.
4. Reutilizar.
5. Solo crear componente compartido si el patrón es general.

Smart Search, tablas y Data View son patrones; pueden tener markup de módulo mientras respeten contrato compartido.

## Smart Search

No se decide por módulo ni por breakpoint solamente. Se decide por contenido:

- compact: dos grupos principales y acciones.
- standard: varios grupos en dos filas conceptuales.
- wide: contenido adicional real, como filtro personalizado.

El popover debe estar anclado al trigger real y clamped al viewport mediante la infraestructura JS/CSS compartida. No usar modal centrado, top hardcodeado por barras ni posicionamiento sin clamp.

## Data View

Todo Browse/List debe razonar su contenido como:

```text
Data View Header
Scrollable content / Rows
Data View Footer
```

Header es encabezado de tabla/list/grid. Footer es status de la colección, no título de módulo ni toolbar. No repetir `LISTADO DE...`, `CONTACTOS`, `USUARIOS`, etc. en el footer.

## Page Size

La paginación principal vive en Context Toolbar. El selector `25/50/100 por página` puede ser control secundario de Data View Footer cuando la implementación lo permita. No duplicar paginadores.

## Column Sizing

Aplicar solo si aporta valor. Requiere:

- metadata `Key`, `MinWidth`, `DefaultWidth`, `MaxWidth`, `Resizable`;
- columnas estructurales fijas;
- ellipsis y horizontal scroll interno;
- preview durante drag;
- persistencia al commit;
- `WidthPx` opcional en configuración por usuario;
- reset que conserve visibilidad, orden y agrupación.

## Sticky Actions

Usar solo si hay tabla ancha con scroll horizontal y acciones por fila. Debe tener ancho fijo, background opaco, z-index, separador y estados zebra/hover/selected correctos.

## Guardas De Dominio

Una referencia visual no autoriza a inventar datos ni seguridad. En Usuarios, `EsGrupo` no es rol; en Técnicos, Usuario asociado es opcional y no transaccional; en Clientes/Proveedores, `CuentasComercialesPage` es compartido y toda migración debe proteger `Tipo == Cliente && !editor` para no afectar Proveedores.

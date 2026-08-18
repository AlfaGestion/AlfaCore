# AlfaDesign v1

Este directorio es la fuente contractual de UI para crear o migrar pantallas AlfaCore. Figma expresa intención visual, el código expresa comportamiento real y `docs/ui` define cómo reconciliarlos sin inventar variantes locales.

## Lectura Recomendada

1. [Contrato AlfaDesign v1](./alfadesign-v1.md): norma principal.
2. [Tokens](./alfadesign-tokens.md): escala productiva y diferencias Figma/CSS.
3. [Componentes y patrones](./alfadesign-components.md): catálogo component-first.
4. [Guía de módulos](./alfadesign-module-guide.md): proceso para migrar o crear pantallas.
5. [Checklist](./alfadesign-checklist.md): Definition of Done.
6. [Mapa Figma - código](./alfadesign-figma-map.md): nodos reales y deuda Figma.

## Referencias Por Módulo

| Módulo | Estado documental | Uso como referencia |
|---|---|---|
| [Contactos](./alfadesign-contactos-reference.md) | AlfaDesign cerrado | CRUD con Browse/List/Kanban, Record, Edit/New, relaciones y actividad. Smart Search wide. Column resize no implementado. |
| [Usuarios](./alfadesign-usuarios-reference.md) | AlfaDesign cerrado | ABM administrativo Browse -> Edit/New. Smart Search compact. Column resize no implementado. |
| [Técnicos](./alfadesign-tecnicos-reference.md) | AlfaDesign cerrado | ABM administrativo con relación opcional y alta auxiliar. Smart Search compact. Column resize no implementado. |
| Clientes | migración en curso | Browse AlfaDesign implementado/en validación, Smart Search standard, column resize, WidthPx y sticky Actions. Editor pendiente. |
| Proveedores | legacy | Comparte `CuentasComercialesPage` con Clientes; debe protegerse para no migrarlo accidentalmente. |

No existe todavía una referencia final de Clientes porque la migración no cerró 10.2-10.5.

## Gobierno

- Una decisión AlfaDesign aprobada debe quedar en Figma, docs o código compartido; no solo en prompts.
- Si existe componente AlfaDesign, se reutiliza antes de crear UI local.
- Smart Search, Data View, Data View Footer y Data View Column Sizing son patrones/infraestructura, no un componente Razor monolítico.
- Data View Footer usa un único estilo compartido y no repite el nombre del módulo.
- Column resize es opcional: solo se usa cuando la densidad de columnas lo justifica.
- La paginación principal vive en Context Toolbar; el selector de page-size puede vivir en el Footer como control secundario.
- Todo cambio compartido en `CuentasComercialesPage` debe preservar la rama legacy de Proveedores.

## Prompt Base Para Migraciones

> Migrá este módulo siguiendo AlfaDesign. Leé `docs/ui/README.md` y los documentos enlazados. Conservá lógica, datos, permisos, rutas y operaciones reales. Usá App Top Bar y Context Toolbar compartidas. Elegí Smart Search compact/standard/wide por complejidad de contenido. Definí Data View Header, rows y Data View Footer. Evaluá column resize y sticky Actions solo si aportan valor. Reutilizá componentes AlfaDesign y documentá excepciones. Validá 2048/1440/1024 sin overflow horizontal global.

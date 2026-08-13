# AlfaDesign v1

Este directorio es el punto de entrada obligatorio para crear o migrar interfaces de AlfaCore. AlfaDesign se sostiene con tres fuentes complementarias, no intercambiables:

- [Figma oficial](https://www.figma.com/design/nNMmjOZSl1w5hlPzbfhJs4/AlfaCore) define la intención y la referencia visual.
- `docs/ui` define el contrato de implementación y su gobernanza.
- los componentes compartidos de `src/AlfaCore/Components/Shared/AlfaDesign/` son la implementación reutilizable.

La lógica, los datos, permisos y operaciones reales siguen perteneciendo al código, servicios y base de datos. Ante una inconsistencia no se crea una variante local: se informa y se decide qué fuente actualizar.

Una pantalla se considera migrada cuando conserva su funcionalidad real, usa el shell y la Context Toolbar compartidos, reemplaza dependencias visuales legacy por componentes/tokens AlfaDesign, cubre sus estados y responsive, y declara el resultado del checklist con cualquier excepción pendiente.

## Para crear o migrar un módulo

1. Leer [AlfaDesign v1](./alfadesign-v1.md).
2. Leer [tokens](./alfadesign-tokens.md).
3. Leer el [catálogo de componentes](./alfadesign-components.md).
4. Seguir la [guía de módulos](./alfadesign-module-guide.md).
5. Consultar el [mapa Figma ↔ código](./alfadesign-figma-map.md).
6. Revisar [Contactos como referencia](./alfadesign-contactos-reference.md), sin copiar su lógica de dominio.
7. Implementar preservando el comportamiento existente.
8. Ejecutar el [checklist](./alfadesign-checklist.md).
9. Reportar toda excepción y deuda restante.

## Prompt oficial reutilizable

> Implementá o migrá este módulo siguiendo AlfaDesign. Leé primero `docs/ui/README.md` y los documentos enlazados. Aplicá la regla component-first: buscá y reutilizá componentes compartidos antes de crear UI. Conservá lógica, datos, permisos y operaciones reales. Usá la App Top Bar global y la Context Toolbar compartida; agregá Data View Header solo para List/Table/Grid cuando corresponda. Contrastá cambios visuales estructurales con el Figma oficial, usá tokens AlfaDesign, validá 2048/1440/1024 y ejecutá `docs/ui/alfadesign-checklist.md`. No inventes funcionalidades ni variantes locales; documentá excepciones.

## Gobernanza

- Las decisiones aprobadas deben quedar en Figma, docs o componentes; nunca únicamente en chats o capturas.
- Nuevo componente reutilizable: actualizar código, [catálogo](./alfadesign-components.md) y [mapa](./alfadesign-figma-map.md); actualizar Figma cuando corresponda y haya aprobación.
- Nuevo patrón: actualizar la norma o guía. Nueva condición de aceptación: actualizar el checklist.
- Todo módulo nuevo o migrado debe reportar el checklist. Contactos es la referencia v1, no una plantilla para copiar indiscriminadamente.

# Técnicos: Referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Técnicos es referencia para un ABM administrativo con relación opcional a otra entidad y alta auxiliar no transaccional: `Browse -> Edit/New`.

## Estado

- AlfaDesign cerrado: 13/13.
- Smart Search: compact.
- Filter Popover: anclado al Smart Search real y viewport-safe.
- Compact shell: aprobado; conserva AlfaCore + nombre de módulo.
- Data View Footer: patrón compartido `.alfa-data-view-footer`, sin repetir "Técnicos".
- Column resize: no implementado; no es requisito de cierre.

## Arquitectura

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`.
- Browse: Smart Search compact, filtros, chips, tabla compacta, agrupación, Configurar vista, paginación y acciones por fila.
- New/Edit: `TecnicosEditView`, formulario continuo sin tabs, validación inline y scroll interno.
- Baja: lógica, confirmada con `AlfaConfirmDialog`.
- Usuario asociado: relación opcional; alta auxiliar con `AlfaDialog`.

No existe Record ni URL estable propia para New/Edit.

## Data View

Browse usa Data View Header de tabla, rows densas y Data View Footer compartido. La paginación principal vive en Context Toolbar; el selector 25/50/100 vive como control secundario del Footer en la implementación actual.

Footer esperado: `13 registros · Página 1 de 1`. No mostrar `LISTADO DE TÉCNICOS` ni instrucciones de edición en el Footer.

## Usuario Asociado

Un Técnico puede existir sin Usuario y un Usuario puede existir sin Técnico. Crear Usuario y guardar Técnico no son una única transacción. La UI conserva dos estados: dirty state del Técnico y Usuario persistido pendiente de asociación. Cancelar/navegar resuelve primero el Usuario pendiente y luego el descarte si corresponde.

## Component-First

Técnicos reutiliza `AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaNotification` y `AlfaEmptyState`. No usa `AlfaTabs` porque el dominio cabe en formulario continuo; no usa `AlfaLookup` porque el catálogo actual de Usuarios sigue siendo manejable como select.

## Figma

No existe frame específico de Técnicos. Usa referencias de Usuarios (`107:1363`, `108:1640`, `108:1840`, `108:2036`, `108:2235`) y componentes/patrones: Data Table Row `58:96`, Context Toolbar `32:104`, Search `37:2`, Input/Select `49:192`, Checkbox `50:65`, Tag `54:62`, Dialog `80:535`.

## Deudas Separadas

MAX+1 para código, alta auxiliar no transaccional, posible Usuario pendiente si interrupción externa, unicidad no estricta de asociación, baja no sincronizada, auditoría histórica incompleta, sin reactivación y sin URL estable New/Edit.

## Archivos Productivos

- `Components/Pages/Tecnicos.razor`.
- `Components/Pages/Tecnicos.razor.css`.
- `Components/Pages/Tecnicos/TecnicosEditView.razor`.
- Servicios/modelos/validador de Técnicos.

# Contactos: Referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Contactos es referencia para un CRUD con ficha: `Browse -> Record -> Edit/New`. Es una referencia de arquitectura y comportamiento, no código para copiar sin auditar otro dominio.

## Estado

- AlfaDesign cerrado: 13/13.
- Smart Search: wide.
- Filter Popover: anclado al Smart Search real y viewport-safe.
- Data View Footer: patrón compartido `.alfa-data-view-footer`, sin repetir "Contactos".
- Column resize: no implementado; no declararlo como parte del cierre de Contactos.
- Sticky Actions: tabla con acciones por fila y sticky/local behavior aprobado donde aplica.

## Arquitectura

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`, configurada por `Contactos.razor`.
- Browse: Listado/Kanban, Smart Search wide, filtros, chips, selección, paginación server-side, View Switcher y Configurar vista.
- Record: `ContactosRecordView`, navegación anterior/siguiente, header de identidad, tabs, información, actividad y cuentas.
- Edit/New: `ContactosEditView`, validación inline, dirty state, confirmación de descarte y bloqueo de doble submit.

## Data View

Listado usa Data View Header de tabla, rows densas y Data View Footer compartido. Kanban no arrastra Table Header; comparte colección y Smart Search. El Footer es status de colección: registros, página, selección y filtros si corresponde. No muestra título de módulo.

El page-size no está documentado como requisito cerrado de Contactos si el código actual no lo ofrece en Footer.

## Patrones Aprobados

- Configurar vista usa `AlfaDialog`, `AlfaSelect`, `AlfaCheckbox`, `AlfaIconButton` y `AlfaButton`.
- Confirmaciones usan `AlfaConfirmDialog`.
- Feedback usa `AlfaNotification`.
- Relaciones grandes usan `AlfaLookup<TItem>`.
- Estados loading/error/empty usan `AlfaEmptyState`.
- El Context panel aparece solo con teléfono/cuentas reales y puede bajar debajo en 1024.

## Implementación Real

- Orquestación y toolbar: `Components/Pages/Contactos.razor`.
- Browse/Listado: `Contactos.razor` + `Contactos.razor.css`.
- Kanban: `Components/Pages/Contactos/ContactosKanbanView.razor`.
- Record/Actividad/Cuentas: `ContactosRecordView.razor`.
- Edit/New: `ContactosEditView.razor`.
- Servicios: `IContactosService`, `ContactosService`, `ICuentasComercialesService`.

## Deudas Separadas

- Column resize no implementado.
- Alta de Cliente desde Contactos sigue pendiente de contrato seguro de Clientes.
- No existe operación pública de desvinculación en `ICuentasComercialesService`.
- `ConversacionesService.cs` conserva mojibake histórico fuera del alcance visual cerrado.

## Figma

Referencia principal: Contactos Listado `62:2`, Selección múltiple `62:385`, Kanban `62:772`, Record `64:837`, Nuevo `66:954`, Edición `67:1073`, Validación `68:1242`. Figma no tiene nodo dedicado para Data View Footer ni sizing semántico de Smart Search; esos son contrato de implementación.

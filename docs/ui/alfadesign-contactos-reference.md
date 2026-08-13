# Contactos: referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Contactos es la primera implementación integral de AlfaDesign. Es referencia de arquitectura y comportamiento visual, no código para copiar sin auditar el dominio del próximo módulo.

## Arquitectura

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`, configurada por `Contactos.razor` según Browse/Record/Edit/New.
- Data View Header: solo en Listado; Kanban, Record, Edit y New no agregan una tercera barra.
- Browse: Listado/Kanban, Smart Search, filtros, selección, paginación server-side y configuración por usuario.
- Record: `ContactosRecordView`, navegación anterior/siguiente en la misma toolbar, header de identidad, tabs, información, actividad y cuentas.
- Edit/New: `ContactosEditView` compartida, validación inline, dirty state y confirmación de descarte.

## Patrones aprobados

- Listado conserva zebra, sticky header, selección individual/masiva, chips y menú contextual; el Data View Header pertenece a la tabla. Kanban reutiliza la misma colección y Smart Search, sin Table Header.
- Configurar vista persiste columnas visibles, orden y agrupación mediante `AlfaDialog`, `AlfaSelect`, `AlfaCheckbox`, `AlfaIconButton` y `AlfaButton`.
- RecordView conserva escala propia legible, header de 60 px, contenido interno acotado y sidebar que baja a anchos menores.
- Actividad muestra un evento con varios cambios, anterior → nuevo, Create/Update/Deactivate, y empty state aprobado.
- Edit y New comparten `ContactosEditView`: binding inmediato, dirty state sin depender de blur, confirmación de descarte, errores inline y bloqueo de doble submit. New expone solo Información/Observaciones.
- Confirmaciones usan `AlfaConfirmDialog`; feedback usa superficie sólida elevada, no vidrio translúcido. Los estados loading/error mantienen vivo el shell.
- El feedback flotante no bloqueante usa `AlfaNotification`; no se descarta al hacer click en el contenido y respeta duración según severidad con pausa al hover.
- El Context panel aparece solo con teléfono o cuentas reales; en 1024 puede bajar debajo. La navegación Record anterior/siguiente vive en la única Context Toolbar.
- Cuentas vinculadas se leen desde `MA_CONTACTOS_CUENTAS` y el legado `CuentaRel`, identificando tipo mediante `VT_CLIENTES`/`VT_PROVEEDORES`.
- La vinculación reutiliza `ICuentasComercialesService.SearchAsync` y `LinkContactoAsync`; permite varias cuentas por identidad lógica de contacto y no duplica la relación existente. El success se emite recién después de persistir, recargar y comprobar el vínculo; el dialog se cierra antes de publicar la notificación.
- No existe una operación pública de desvinculación en `ICuentasComercialesService`; por eso esta fase no expone una acción de desvincular.
- El alta de Cliente desde Contactos queda pendiente: Clientes no ofrece todavía contrato de prefill + return URL + vínculo seguro. No se muestra una acción ficticia.

## Component-first aplicado

Campos, checkbox, select, tabs de formulario, botones, action menu, confirmaciones y empty states reutilizan componentes AlfaDesign. `AlfaDialog` normaliza Configurar vista; `AlfaLookup<TItem>` resuelve la selección de Cliente/Proveedor. Los Smart Buttons, timeline, tabla y Kanban siguen siendo patrones de módulo aprobados y candidatos a extracción solo cuando otra implementación real justifique la abstracción.

## Implementación real

- Orquestación, estados, toolbar, URLs y operaciones: `Components/Pages/Contactos.razor`.
- Browse/Listado: `Contactos.razor` y sus estilos aislados.
- Kanban: `Components/Pages/Contactos/ContactosKanbanView.razor`.
- Record, Actividad, Context y cuentas: `ContactosRecordView.razor`.
- Edit/New: `ContactosEditView.razor`.
- Servicios: `IContactosService`/`ContactosService` y, para buscar/vincular cuentas, `ICuentasComercialesService`.
- Auditoría: generada por backend; la UI solo consulta y representa eventos reales.

Otros módulos pueden reutilizar la arquitectura de estados, configuración de toolbar, componentes, feedback, dirty state y responsive. No deben copiar queries, modelos, permisos, rutas ni reglas de Contactos.

## Auditoría legacy de referencia

En el markup visible no quedan clases Bootstrap `.btn`/`btn-*`, `.form-control`, `.dropdown-menu`, `modal fade`, atributos `data-bs-*` ni estilos inline. Configurar vista, unificación y envío masivo usan el dialog compartido; los controles que requieren eventos o textarea aún no soportados por los componentes base conservan HTML nativo, pero con clase local tokenizada y sin dependencia Bootstrap. Smart Search options, selección de tabla/Kanban y Smart Buttons son patrones semánticos propios, no botones genéricos recreados.

Los hex restantes pertenecen a la paleta estable de tonos de avatar y su texto blanco en Record/Edit/Kanban. Se mantienen como excepción hasta que exista un token semántico de avatar; no representan superficies legacy.

## Estado de checklist pre-checkpoint — 2026-08-13

Cumple **13/13**. La validación manual autenticada aprobó shell, Browse, Actividad, Context, cuentas, Configurar vista dentro de `AlfaDialog`, el área útil/overflow de `AlfaLookup`, las variantes Error y Success de `AlfaNotification` y Responsive a 2048, 1440 y 1024 px. No se detectaron solapamientos ni scroll horizontal global; el Context panel permanece lateral en 2048/1440 y baja debajo del contenido en 1024.

Deuda separada: `ConversacionesService.cs` conserva 124 líneas históricas con mojibake fuera del alcance de Fase 7.5. No se corrigen en este checkpoint.

## Las tres capas en cada estado

| Estado | App Top Bar | Context Toolbar | Data View Header |
|---|---|---|---|
| Listado | sí, global | búsqueda/filtros/acciones/vistas/paginación reales | sí |
| Kanban | sí, global | búsqueda/filtros/acciones/vistas reales | no |
| Record | sí, global | volver/editar/overflow + navegación de registros | no |
| Edit | sí, global | cancelar/guardar/acciones reales | no |
| New | sí, global | cancelar/guardar | no |

## Qué reutilizar y qué no

Reutilizar shell, configuración de toolbar, componentes AlfaDesign, estados, contratos de error y checklist. No copiar queries, permisos, campos, operaciones de conversación, auditoría o relaciones de Contactos a otra entidad.

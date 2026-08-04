# Módulo CRM

## Objetivo

El módulo `CRM` centraliza leads y oportunidades comerciales en AlfaCore. Está inspirado en el flujo de CRM de Odoo: pipeline visual, etapas configurables, oportunidades, actividades, etiquetas y seguimiento de ganadas/perdidas.

## Alcance inicial

Implementado:

- pantalla `/crm`;
- tablero Kanban por etapas;
- listado paginado server-side;
- configuración de columnas del listado por usuario;
- alta y edición de oportunidades;
- administración de etapas del pipeline;
- eliminación lógica de etapas;
- administración de etiquetas;
- notas y actividad de cada oportunidad;
- preparación del vínculo con conversaciones mediante `IdConversacion` e `IdMensaje`.

Pendiente para la siguiente etapa:

- botón desde `Conversaciones` para crear lead desde mensajes seleccionados;
- autocompletar cliente/contacto desde conversación;
- búsqueda asistida de cliente/contacto dentro del formulario CRM;
- actividades programadas con vencimiento;
- reportes de conversión y forecast.

## Inspiración funcional

Odoo CRM organiza el trabajo comercial en oportunidades dentro de un pipeline, con etapas, actividades y seguimiento. AlfaCore toma ese patrón, pero con modelo propio y simple para mantener independencia de Tickets y Conversaciones.

Referencias oficiales:

- https://www.odoo.com/es/app/crm
- https://www.odoo.com/documentation/19.0/applications/sales/crm.html
- https://www.odoo.com/documentation/19.0/applications/sales/crm/pipeline.html

## Tablas

Todas las tablas nuevas usan prefijo `CRM_*`.

| Tabla | Uso |
|---|---|
| `CRM_ETAPAS` | etapas configurables del pipeline |
| `CRM_OPORTUNIDADES` | lead u oportunidad comercial |
| `CRM_ETIQUETAS` | etiquetas activas del módulo |
| `CRM_OPORTUNIDAD_ETIQUETAS` | relación oportunidad-etiqueta |
| `CRM_OPORTUNIDAD_MENSAJES` | mensajes de conversaciones vinculados como origen |
| `CRM_ACTIVIDAD` | timeline de alta, edición, movimientos y notas |

## Etapas

Las etapas son configurables por usuario con permisos al módulo:

- nombre;
- color;
- orden;
- marca `EsGanada`;
- marca `EsPerdida`;
- activa/inactiva.

Eliminar una etapa la desactiva. Esto evita romper oportunidades históricas y permite mantener trazabilidad.

## Oportunidades

Campos principales:

- título;
- descripción;
- etapa;
- prioridad;
- probabilidad;
- importe estimado;
- fecha de cierre estimada;
- técnico/vendedor asignado;
- cliente;
- contacto;
- canal origen;
- conversación origen;
- usuario de alta.

## Relación con Conversaciones

El módulo CRM no depende físicamente de `CONV_*` para poder inicializarse solo. La relación se deja preparada con:

- `CRM_OPORTUNIDADES.IdConversacion`;
- `CRM_OPORTUNIDAD_MENSAJES.IdMensaje`.

Cuando se conecte el botón desde `Conversaciones`, el flujo recomendado es:

1. seleccionar uno o varios mensajes;
2. abrir modal "Crear lead";
3. sugerir título y descripción desde el texto seleccionado;
4. copiar cliente/contacto/canal desde la conversación;
5. crear oportunidad en CRM;
6. registrar actividad de origen;
7. permitir volver desde CRM a la conversación.

## Archivos

- `src/AlfaCore/Models/CrmModels.cs`
- `src/AlfaCore/Services/ICrmService.cs`
- `src/AlfaCore/Services/CrmService.cs`
- `src/AlfaCore/Components/Pages/Crm.razor`
- `src/AlfaCore/Components/Pages/Crm.razor.css`
- `src/AlfaCore/App_Data/updates/2026-07-28-001__crm_oportunidades_modelo_inicial.sql`
- `src/AlfaCore/App_Data/updates/2026-07-28-002__crm_oportunidades_menu_web.sql`

## Decisiones técnicas

- Se usa modelo propio `CRM_*`, no `TICK_*`, para separar CRM comercial de soporte.
- La pantalla no usa drag/drop en esta primera etapa; los cambios de etapa se hacen desde edición o acciones controladas.
- No hay dependencia dura contra Conversaciones; el vínculo es lógico y se completa cuando existen mensajes de origen.
- La configuración de columnas se guarda en `TA_CONFIGURACION` con clave `USUVIEW-CRM-{hash}`.
- Los errores se registran por la capa común mediante `IAppEventService`.

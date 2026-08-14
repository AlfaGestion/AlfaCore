# Técnicos: referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Técnicos es la referencia AlfaDesign para un ABM administrativo con una relación opcional a otra entidad y un alta auxiliar dentro del editor. La migración conserva el circuito productivo `Browse → New/Edit → Browse`, sin crear una Ficha artificial ni modificar la relación real entre Técnico y Usuario.

## Propósito y arquitectura

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`, configurada por `Tecnicos.razor` según Browse, New y Edit.
- Browse: Smart Search, filtros, chips, tabla compacta, agrupación, configuración de columnas, paginación y acciones por fila.
- New/Edit: `TecnicosEditView` compartido, formulario continuo sin tabs, validación inline y scroll interno.
- Flujo seguro: dirty state inmediato, `NavigationLock`, confirmación de descarte, no-op Save y guardas contra reentrada.
- Baja: lógica, siempre confirmada; no reactiva ni modifica automáticamente al Usuario asociado.

No existe modo Record. New/Edit tampoco poseen URL estable propia: el estado continúa dentro del circuito actual de `Tecnicos.razor`.

## Browse

El Browse publica Nuevo, Recargar, Configurar vista, agrupación, búsqueda y paginación en la Context Toolbar. El contenido presenta un Data View Header compacto con resumen y selector 25/50/100, seguido por las filas de la tabla.

Smart Search conserva búsqueda inmediata, Enter, Escape, click exterior, filtro por estado, agrupación y chips removibles. La tabla utiliza paginación server-side; la agrupación visual opera sobre los registros de la página actual. El doble click y `AlfaActionMenu` abren Edit; la baja disponible usa `AlfaConfirmDialog`.

Configurar vista reutiliza `AlfaDialog`, `AlfaSelect`, `AlfaCheckbox`, `AlfaIconButton` y `AlfaButton`. Persiste agrupación, visibilidad y orden mediante el servicio existente, sin toolbar local ni modal legacy.

## New y Edit

`TecnicosEditView` reutiliza el mismo formulario en ambos estados. Su header representa identidad, código y estado; una única superficie agrupa:

- Datos generales: código, nombre y cargo.
- Ubicación y contacto: domicilio, localidad, provincia y teléfono.
- Configuración: costo por hora, Usuario asociado y Ocultar datos del cliente.

El código es editable únicamente en New e inmutable en Edit. Un Técnico dado de baja continúa editable según el comportamiento productivo previo; no se agregó Reactivar.

Provincia usa `AlfaSelect` sobre el catálogo real `TA_ESTADOS.CODIGO`. El código se conserva exactamente porque puede contener semántica legacy y no se reemplaza por un lookup nuevo.

## Usuario asociado y alta auxiliar

La asociación con Usuario es opcional: un Técnico puede existir sin Usuario y un Usuario puede existir sin Técnico. El catálogo disponible sigue siendo manejable como `AlfaSelect`; `AlfaLookup` no aporta valor en este flujo y alteraría innecesariamente el contrato existente.

Crear usuario abre `AlfaDialog` y conserva `UsuariosService.SaveAsync`. El formulario auxiliar solicita únicamente nombre, email, contraseña y confirmación compatibles con el servicio existente. La key de validación backend `contrasena` se presenta en el campo visual `clave`; no se modificaron reglas del validador.

Crear Usuario y guardar Técnico no forman una transacción única. Por eso se mantienen dos estados distintos:

- cambios dirty del Técnico;
- Usuario ya persistido pendiente de que el Técnico se guarde con esa asociación.

Cancelar, navegar, iniciar otro New o abrir otro Técnico resuelven primero la consecuencia del Usuario pendiente y después, si corresponde, el descarte del formulario. La confirmación habla de **desactivar**, nunca de eliminar, y ejecuta `UsuariosService.DeactivateAsync`. Un fallo conserva el estado para reintentar; un Save fallido del Técnico también conserva el Usuario pendiente. Solo un Save exitoso del Técnico o una desactivación exitosa limpian ese estado.

## Dirty state y seguridad del flujo

El snapshot compara directamente los campos editables. Incluye código solo en New, textos normalizados según el `Trim()` real del servicio, provincia exacta, costo por valor decimal, Usuario asociado y Ocultar cliente. Excluye estado de baja, identificadores internos, catálogos, errores, dialogs, loading y feedback.

Los cambios se detectan sin depender de blur. Restaurar texto, select, checkbox o costo semánticamente equivalente vuelve a clean. La matriz de salida es:

- clean sin Usuario pendiente: salida directa;
- dirty sin Usuario pendiente: confirmar descarte;
- clean con Usuario pendiente: confirmar desactivación;
- dirty con Usuario pendiente: resolver primero Usuario y luego descarte.

`NavigationLock` protege rutas internas y navegación externa mientras exista cualquiera de los dos estados. La navegación confirmada respeta el destino original. Edit sin cambios no llama `TecnicosService.SaveAsync`, vuelve a Browse y publica “Sin cambios”. Guardar, crear Usuario, desactivar y confirmar baja tienen guardas internas contra reentrada además del estado disabled/loading visual.

## Feedback, validación y baja

- Errores de campo: inline mediante `AlfaInput`/`AlfaSelect`.
- Feedback global: `AlfaNotification` para creación, actualización, sin cambios, Usuario creado/desactivado y errores reales.
- Confirmaciones: `AlfaConfirmDialog` para baja, descarte y Usuario pendiente.
- Alta auxiliar: `AlfaDialog` con backdrop, surface, header/body/footer y loading compartidos.
- Baja de Técnico: usa el servicio existente y no sincroniza ni desactiva automáticamente al Usuario.

## Component-first aplicado

Técnicos reutiliza `AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaNotification` y `AlfaEmptyState`.

`AlfaTabs` no es necesario porque el dominio cabe en un formulario continuo. `AlfaLookup` no es necesario para el catálogo actual de Usuarios. Smart Search y tabla son patrones compartidos/de módulo admitidos por AlfaDesign v1; sus controles nativos están tokenizados y no recrean componentes genéricos del catálogo.

## Estructura por estado

| Estado | App Top Bar | Context Toolbar | Data View Header |
|---|---|---|---|
| Browse | sí, global | Nuevo, Recargar, Smart Search, filtros, acciones y paginación reales | sí, resumen y tamaño de página |
| New | sí, global | Guardar/Cancelar | no |
| Edit | sí, global | Guardar/Cancelar y baja en overflow cuando corresponde | no |

No existe toolbar local, header paralelo, Smart Search ni cabecera de tabla dentro del editor.

## Referencias Figma

No existe un diseño específico de Técnicos. Se usaron como referencias estructurales AlfaDesign:

- Usuarios Listado `107:1363`.
- Usuarios Nuevo `108:1640`.
- Usuarios Edición `108:1840`.
- Usuarios Validación `108:2036`.
- Confirmación `108:2235`.
- Data Table Row `58:96`.
- Context Toolbar `32:104`.
- Search `37:2`.
- Input/Select `49:192`.
- Checkbox `50:65`.
- Tag `54:62`.
- Dialog `80:535`.

Estos nodos expresan jerarquía, densidad y contratos de componentes; no se presentan como pantallas diseñadas específicamente para Técnicos.

## Diferencias deliberadas con Usuarios y Contactos

Usuarios organiza Información/Acceso en tabs e incluye contraseña, foto, grupo y cambio en próximo inicio. Técnicos utiliza formulario continuo con código, cargo, ubicación, provincia, costo, visibilidad de cliente, Usuario opcional y alta auxiliar de Usuario. Compartir AlfaDesign no implica copiar la composición ni el dominio.

Contactos posee Browse/Listado/Kanban/Record/Edit/New, actividad y cuentas vinculadas. Técnicos no necesita Record, Kanban, actividad ni navegación entre fichas; conserva el ABM administrativo directo.

## Responsive

La estructura define editor contenido en dos columnas para desktop y una columna desde 1024 px, scroll vertical interno y overflow horizontal limitado a la tabla. Los dialogs y notifications provienen de componentes compartidos contenidos en viewport.

La validación autenticada final aprobó Browse, New y Edit a 2048, 1440 y 1024 px. En 2048 la tabla y el editor aprovechan el espacio sin estiramientos problemáticos; en 1440 mantienen la distribución desktop principal; en 1024 el editor pasa a una columna, el último campo permanece accesible, dialogs y notifications quedan contenidos y el overflow horizontal se limita a la tabla cuando corresponde.

## Auditoría legacy visible

En `Tecnicos.razor`, su CSS aislado, `TecnicosEditView.razor` y su CSS no quedan `.btn`/`btn-*`, `.form-control`, `.dropdown-menu`, modal Bootstrap, `data-bs-*`, `panel-card`, `editor-msg`, `result-pagination`, `interfaces-*`, `usuarios-input`, `usuarios-editor`, `contactos-form__grid`, estilos inline ni colores hex/rgba locales.

Los botones e input HTML nativos restantes pertenecen a Smart Search —chips, opciones, backdrop y campo de búsqueda—, un patrón compartido documentado. No se eliminaron reglas globales históricas que otros módulos todavía pueden consumir.

## Deuda técnica fuera de AlfaDesign

1. El siguiente código se obtiene mediante MAX + 1 y conserva riesgo de concurrencia.
2. Crear Usuario y guardar Técnico no es transaccional.
3. Puede existir un Usuario huérfano si el proceso externo queda interrumpido; la UI mitiga abandono conocido, no implementa rollback.
4. La asociación Usuario/Técnico no posee unicidad estricta.
5. `SistemaAsociado` no es gestionado por `TecnicosService`.
6. La baja de Técnico y Usuario no está sincronizada.
7. El payload histórico de auditoría no incluye todos los campos modificables.
8. Update no verifica con fuerza la cantidad de filas afectadas.
9. La agrupación visual opera sobre la página actual.
10. No existe reactivación.
11. New/Edit no tienen URL estable y refrescable propia.

La migración no agregó sincronización automática, eliminación física, cambios de PK/FK, permisos, roles, writes adicionales ni logs sensibles.

## Archivos productivos de referencia

- Orquestación, Browse, toolbar, dirty state y flujo pendiente: `Components/Pages/Tecnicos.razor`.
- Browse y responsive de tabla: `Components/Pages/Tecnicos.razor.css`.
- New/Edit: `Components/Pages/Tecnicos/TecnicosEditView.razor` y su CSS aislado.
- Dominio y persistencia: `ITecnicosService`, `TecnicosService`, `TecnicosModels` y `TecnicosValidator`.

## Estado final — 2026-08-14

Cumple **13/13**. Browse, New, Edit, dirty state, navegación, usuario pendiente, baja, dialogs, feedback, componentes, auditoría legacy y responsive están aprobados o verificados.

Excepciones: ninguna dentro del alcance aprobado. Deuda visual: ninguna. Regresiones: ninguna detectada. Legacy visual restante: ninguno; Smart Search y tabla conservan patrones nativos AlfaDesign justificados. Las deudas funcionales y técnicas enumeradas arriba permanecen separadas del cumplimiento visual.

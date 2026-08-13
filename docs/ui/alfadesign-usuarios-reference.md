# Usuarios: referencia AlfaDesign v1

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Mapa Figma](./alfadesign-figma-map.md) · [Checklist](./alfadesign-checklist.md)

Usuarios es la referencia AlfaDesign para Browse y edición de cuentas de acceso. Su migración conserva el dominio y la seguridad legacy existentes; no convierte `EsGrupo` en rol ni incorpora permisos que no tengan contrato backend.

## Arquitectura productiva

- App Top Bar global: `MainLayout`.
- Context Toolbar compartida: `MainPageHeader`, configurada por `Usuarios.razor` para Browse/New/Edit y selección.
- Browse: Data View Header compacto, Smart Search, filtros reales, selección individual/masiva, agrupación, configuración de columnas, paginación y menú contextual.
- New/Edit: `UsuariosEditView` de página completa, tabs Información/Acceso, metadata sólo en Edit, foto JPG/JPEG y validación inline.
- Flujo seguro: dirty state inmediato, restauración a clean, `NavigationLock`, confirmación de descarte, bloqueo de doble submit y no-op al guardar Edit sin cambios.
- Baja: lógica e individual/masiva, siempre detrás de `AlfaConfirmDialog`.

No existe modo Record independiente: al guardar o cancelar se vuelve a Browse. New/Edit tampoco tienen URL estable propia; el estado vive en el circuito actual de `Usuarios.razor`.

## Component-first aplicado

Usuarios reutiliza `AlfaButton`, `AlfaIconButton`, `AlfaInput`, `AlfaSelect`, `AlfaCheckbox`, `AlfaTag`, `AlfaTabs`, `AlfaActionMenu`, `AlfaDialog`, `AlfaConfirmDialog`, `AlfaNotification` y `AlfaEmptyState`. Smart Search y tabla siguen siendo patrones compartidos/de módulo admitidos por AlfaDesign v1.

Los checkbox nativos de selección pertenecen a la tabla compacta. `InputFile` permanece nativo porque es el control de carga real; su interacción visual queda integrada con `AlfaButton`, tokens y foco visible. No son recreaciones de controles AlfaDesign genéricos.

## Estructura por estado

| Estado | App Top Bar | Context Toolbar | Data View Header |
|---|---|---|---|
| Browse | sí, global | Nuevo, selección, Smart Search, filtros y paginación reales | sí, resumen y tamaño de página |
| New | sí, global | Guardar/Cancelar | no |
| Edit | sí, global | Guardar/Cancelar y baja en overflow cuando corresponde | no |

No hay tercera toolbar, buscador o cabecera de tabla dentro del editor.

## Contraste directo con Figma

Se revisaron en modo lectura los nodos `107:1363` (Listado), `108:1640` (Nuevo), `108:1840` (Edición), `108:2036` (Validación) y `108:2235` (confirmación de baja).

Coincidencias preservadas: shell de dos barras, densidad compacta, tabla de 32 px, acciones Guardar/Cancelar, tabs, inputs de 36–38 px, errores inline, scrim/surface de confirmación y jerarquía Danger para la baja.

Diferencias deliberadas por dominio real:

- Figma separa nombre completo y usuario; producción dispone de `Nombre` como identificador de cuenta.
- Figma incluye Rol y permisos integrados; producción no tiene ese contrato en este editor. `EsGrupo` no equivale a Rol y `EsTecnico` es una relación real separada.
- `AutorizacionTareas` continúa como circuito separado y `PermissionService` no fue modificado; no se inventó ningún permiso desde Figma.
- Producción conserva cambio de contraseña en próximo inicio, compatibilidad de contraseña existente, foto y metadata porque son capacidades reales ausentes de la maqueta.
- Figma propone lista lateral durante New/Edit; producción usa editor de página completa para no duplicar Browse ni Smart Search dentro del formulario.
- El estado Activo se representa en Browse/Edit, pero la creación/edición no inventa una edición de estado: la operación real disponible es baja lógica.
- La validación usa errores inline y feedback compartido; la baja usa `AlfaConfirmDialog` tanto desde fila/selección como desde Edit.

## Auditoría legacy visible

En `Usuarios.razor`, su CSS aislado, `UsuariosEditView.razor` y su CSS no quedan `.btn`/`btn-*`, `.form-control`, `.dropdown-menu`, modal Bootstrap, `data-bs-*`, `panel-card`, `modal-overlay/card`, `editor-msg`, `result-pagination`, `interfaces-*`, `usuarios-input`, estilos inline ni colores hex/rgba locales. Los `position` restantes corresponden al sticky header de tabla y al input de archivo contenido, no a overlays manuales.

No se eliminaron reglas globales potencialmente usadas por otros módulos.

## Seguridad y deudas fuera del cierre visual

- El servicio decodifica la contraseña legacy para mantener compatibilidad con Edit; no existe todavía un flujo de reset que evite esa exposición.
- No existe un reset de contraseña dedicado.
- La UI no amplía la contraseña a logs, notifications, auditoría, JavaScript, documentación ni snapshots serializados. El snapshot de dirty state es privado y sólo vive en memoria del circuito.
- No existe reactivación de usuarios.
- La baja masiva es secuencial y no transaccional entre usuarios.
- Los payloads históricos de auditoría no son completamente uniformes: `Activo`, `EsTecnico`, foto y vínculo técnico no siempre reflejan todos los cambios con la misma granularidad.
- La foto se aplica en filesystem después del commit SQL; una falla de archivo puede dejar persistencia parcial.
- La detección/servicio de foto realiza I/O de filesystem por usuario.
- New/Edit no tienen URLs estables y refrescables propias.

Estas deudas no se corrigen en Fase 8.4 y no deben ocultarse mediante cambios visuales.

## Archivos productivos de referencia

- Orquestación, estados, toolbar, búsqueda, selección, acciones y dirty state: `Components/Pages/Usuarios.razor`.
- Browse y responsive de tabla: `Components/Pages/Usuarios.razor.css`.
- New/Edit: `Components/Pages/Usuarios/UsuariosEditView.razor` y su CSS aislado.
- Dominio y persistencia: `IUsuariosService`, `UsuariosService`, `UsuariosModels` y `UsuariosValidator`.

## Estado final — 2026-08-13

Cumple **13/13**. Browse, New, Edit, validación, dirty state, navegación, baja, dialogs, feedback y auditoría legacy están aprobados o verificados. La validación autenticada confirmó 2048, 1440 y 1024 px: distribución consistente, editor de una columna en 1024, tabla con overflow interno, toolbar utilizable, dialogs/notifications contenidos, scroll vertical completo y ausencia de scroll horizontal global.

Excepciones: ninguna dentro del alcance aprobado. Deuda visual: ninguna. Regresiones: ninguna detectada. Legacy visual restante en Usuarios: ninguno. Las deudas funcionales, técnicas y de seguridad enumeradas arriba permanecen separadas del cumplimiento AlfaDesign.

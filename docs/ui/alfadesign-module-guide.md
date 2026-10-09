# Guía De Módulos AlfaDesign

[Índice](./README.md) · [Norma](./alfadesign-v1.md) · [Componentes](./alfadesign-components.md) · [Checklist](./alfadesign-checklist.md)

## Proceso

1. Auditar dominio, datos, permisos, rutas, servicios y operaciones reales.
2. Elegir arquitectura: CRUD con ficha, ABM administrativo, ABM con entidad relacionada, u otra derivada del dominio.
3. Inventariar estados: Browse, Record, Edit, New y transitorios reales.
4. Usar App Top Bar y Context Toolbar compartidas. Además de llamar `PageHeader.Set(new PageHeaderConfig { ShellMode = PageHeaderShellMode.AlfaDesignPilot, ... })`, agregar el módulo (primer segmento de la ruta) al `HashSet<string> AlfaDesignManagedModules` en `MainLayout.razor` — sin ese paso el sidebar legacy no se oculta y el topbar/toolbar nunca se renderiza, sin ningún error visible.
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

Una referencia visual no autoriza a inventar datos ni seguridad. En Usuarios, `EsGrupo` no es rol; en Técnicos, Usuario asociado es opcional y no transaccional; en Clientes/Proveedores, `CuentasComercialesPage` es compartido y el Browse y la ficha AlfaDesign aplican a ambos Tipo (desde 2026-10-08); lo propio de cada uno va gateado por `Tipo`: Cliente tiene ARCA, Portal Cliente e invitaciones; Proveedor tiene local/compartido y descuentos por condición de compra.

## Inicio Y Páginas De Módulo

- `Launcher.razor` (Inicio) y `ShellWorkspacePage.razor` (`/shell/{clave}`) usan la barra superior global sin sidebar legacy. No llaman a `PageHeader.Set(...)`: `MainLayout` los detecta con `IsAlfaHomeActive` (ruta de Inicio o `shell/`, sesión autorizada y sin `?directo=1`).
- Boceto aprobado: Figma `06 — Bocetos`, v2 simple. Estilos en `wwwroot/css/home-alfadesign.css` (`alfa-home__*`); `Ctrl+K` lleva el foco al buscador (`wwwroot/js/alfa-home.js`).
- La búsqueda de opciones del menú es una sola: `ShellMenuSearch.Filter(...)`, compartida con el buscador del sidebar legacy.
- Lo que estaba en el pie del sidebar del Inicio viejo (Instalar, Tareas, Ayuda) y la tarjeta de Portal Cliente pasaron al menú de usuario de la barra superior (`TopbarUsuarioMenu.razor`), disponible en todo el shell AlfaDesign.

## Módulos Operativos Migrados Con Markup Legacy

- Tickets, CRM, Auditoría, Costos, Consultas, Interfaces, Carritos de compra (y Carrito general), Actualizaciones, Tablas de referencia, Ayuda, Autorización de tareas, Informes/Novedades, Calendario/Reuniones, Tareas, Partes de horas, visor de comprobantes y Carga de viajes usan el wrapper `gd-page gd gd-legacy`: conservan su markup (`.btn`, `.panel-card`, `.field`, tablas) y toman el aspecto AlfaDesign desde `wwwroot/css/gestion-alfadesign.css` (piezas comunes y remapeo de las variables del tema legacy dentro de `.gd`) y `wwwroot/css/modulos-alfadesign.css` (piezas propias de cada módulo y reglas `.gd-legacy`). Los tableros de gestión no llevan `gd-legacy` y no cambian.
- Las ventanas emergentes que quedan fuera del wrapper se envuelven en `<div class="gd gd-legacy gd-modal-scope">` (`display: contents`).
- Cada módulo tiene su preset en `ModuleTopNavPresets` con las mismas entradas que tenía su sidebar legacy, y su clave en `AlfaDesignManagedModules`.
- Si el buscador o las acciones del header dependen del estado de la página, la página guarda un `HeaderSnapshot()` y en `OnAfterRender` republica el header solo cuando cambia (evita que el Context Toolbar quede desactualizado sin llamar `UpdatePageHeader()` en cada handler).
- Las apps de pantalla completa que no entran en `gd-page` (Informes, Novedades, vista previa de viaje, liquidación de choferes) se envuelven en `gd gd-modal-scope <modulo>-ad`. Como `shell__content` es flex, el contenedor propio de la página necesita `flex: 1; min-width: 0` para ocupar todo el ancho.
- `MainPageHeader` envuelve el `SearchContent` en `gd gd-modal-scope` cuando el shell AlfaDesign está activo: los botones y campos legacy dentro del panel de filtros del header toman el aspecto AlfaDesign sin tocar cada página.
- Las barras de botones internas (Volver, Imprimir, Exportar, Nuevo...) pasan al Context Toolbar como `Actions`; no se duplican dentro de la página ni en los estados vacíos. Los botones deshabilitados "Próximamente" no se migran.
- Los estados de error o sin datos usan `AlfaEmptyState` (Kind `Error`/`NoResults`/`NoData`), no títulos `<h1>` ni `editor-msg` sueltos. Los mensajes de operación (guardado, error, aviso) usan `AlfaNotification` con la severidad correcta: aviso amarillo, error rojo, éxito verde.
- Al migrar una hoja de estilos legacy, los colores navy/acento/slate y los radios grandes se reemplazan por tokens `--alfa-*` (bordes `--alfa-border-default`, superficies `--alfa-bg-surface`, radios `--alfa-radius-sm/md`). No usar degradés en tarjetas ni botones de selección: estado activo con borde de acento y fondo de acento al 12 %.
- Clases genéricas que choquen con otras hojas globales se renombran (ej. `auth-page` de Autorización chocaba con el login y pasó a `autorizacion-page`).
- En una página con `IAsyncDisposable`, el `PageHeader.Clear()` va dentro de `DisposeAsync`: Blazor no llama a `Dispose` si existe la versión asíncrona.
- En el shell AlfaDesign el Context Toolbar (franja debajo de la barra superior) solo aparece si la página publica acciones, búsqueda, paginador, vistas o breadcrumb; el título solo no la muestra.
- Al tokenizar colores, las capas que oscurecen detrás de modales y paneles (`*-backdrop`, `*-overlay`) llevan `rgba(0, 0, 0, .6)`, nunca un token de superficie opaco (tapaba la pantalla de fondo).
- El diálogo global de mensajes y confirmaciones (`AppMsgBoxHost`) tiene estilos en su CSS aislado y una copia con `!important` en `app.css`: cualquier cambio visual va en los dos.
- `InputFile` y otros componentes hijos no reciben el CSS aislado de la página que los usa: para ocultar el `<input type="file">` detrás de un botón hace falta una regla global (ver `modulos-alfadesign.css`).
- Grillas con `repeat(auto-fit, …)` dentro de un contenedor flex en columna pueden reservar el alto de las tarjetas apiladas en una columna (queda un hueco debajo); usar columnas fijas con media query.
- Los buscadores con chips propios de un módulo (ej. Carga de viajes) se pensaron para la página: al pasarlos al header hay que alinearlos al Smart Search estándar (sin margen superior, alto 36px, centrado, botón de despliegue neutro) y darles un placeholder corto.
- Barras de solapas internas propias (ej. Configuración > Ventas) se reemplazan por `AlfaTabs`.

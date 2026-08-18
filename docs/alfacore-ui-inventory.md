# Inventario de interfaz actual de AlfaCore

Fecha del relevamiento: 2026-08-04
Alcance: interfaz Blazor presente en `src/AlfaCore/Components`, sin inferir funciones que no estén en el repositorio.

## Criterio de conteo

- Se encontraron **81 archivos Razor enrutables**. Cada archivo se cuenta como una pantalla diferente, aunque tenga varios alias de URL (la mayoría dispone de ruta corta y ruta contextual `/{idweb}/{idbase:int}/...`).
- No se cuentan como pantallas independientes los componentes compartidos sin `@page`, los modales internos ni las vistas internas de una pantalla multipropósito.
- `CargaViajes.razor`, `Conversaciones.razor`, `Informes.razor` y `VentasPuntoVenta.razor` contienen varias vistas internas, pero se cuentan una vez porque son una única implementación enrutable con estado interno.
- Las páginas residuales `Counter` y `Weather`, el puente `SaasRouteBridge` y el workspace dinámico se incluyen porque realmente son enrutables.
- Salvo declaración expresa, las pantallas usan el `DefaultLayout` de `Routes.razor`: **MainLayout**. Las excepciones públicas se señalan en la matriz.

## Infraestructura transversal observada

### Layouts y navegación

- `MainLayout.razor`: shell autenticado por defecto. Contiene navegación de aplicaciones y módulos, menú lateral, accesos globales, paneles contextuales de tareas/conversaciones, exportación global a PDF y monta `<MainPageHeader />` antes del cuerpo de la página. También aplica el encierro de módulo para `?directo=1`.
- `PublicLayout.razor`: login, registro, verificación, selección pública y reserva pública; no usa el shell autenticado.
- `MainShellLayout.razor`: existe como layout, pero ninguna de las 81 páginas relevadas lo declara directamente.
- `NavMenu.razor`: componente lateral complementario. La navegación operativa principal está integrada en `MainLayout`.
- `Routes.razor`: usa `MainLayout` como layout predeterminado; el 404 y el `ErrorBoundary` también se renderizan dentro de ese layout.
- La navegación atrás/adelante del encabezado no es la del navegador: `PageHeaderNavigationService` mantiene una lista de URI en memoria, recorta la rama posterior al navegar desde el medio y expone `CanGoBack`/`CanGoForward`. Cada pantalla puede reemplazarla con `OnBack`/`OnForward`.
- Conviven además botones locales “Volver”, enlaces directos con `NavigationManager.NavigateTo` y `GoBackOrLauncherAsync` del layout. Por eso el significado de “volver” no es uniforme.

### MainPageHeader y contrato de encabezado

- `IPageHeaderService` expone `Current`, `Changed`, `Set` y `Clear`; `PageHeaderService` conserva una configuración scoped y notifica cambios.
- `PageHeaderModels.cs` define estados `Idle`, `List`, `Kanban`, `Detail`, `Loading`, `Editing`, `Creating`, `ReadOnly`, `Selection` y `SelectionMode`; acciones con estilos y prioridades; buscador; botones de cambio de vista; breadcrumb; inicio; navegación histórica y callbacks atrás/adelante.
- `MainPageHeader.razor` renderiza navegación, breadcrumb/título, buscador o `SearchContent`, cambio de vista, acciones esenciales/normales y menú overflow.
- Solo configuran actualmente `PageHeader.Set`: **Auditoría de errores, Auditoría de usuarios, Calendario, Carga de viajes, Contactos, CRM, Técnicos, Tickets y Usuarios** (9 archivos; Carga de viajes cubre varias subvistas). El resto depende de títulos/acciones internos aunque el host global exista.
- `MainPageHeader` no representa por sí mismo los estados “sin resultados”, “error”, “validación”, “sin permisos” o “confirmación de eliminación”; esos estados se implementan dentro de cada página.

### Componentes con potencial de reutilización

| Componente actual | Uso comprobado | Potencial / límite |
|---|---|---|
| `MainPageHeader` + servicios/modelos | 9 páginas lo configuran; el layout lo hospeda globalmente | Base clara para título, búsqueda, vistas y acciones. Falta adopción general y una convención única de estados. |
| `ModuleListToolbar` | Subvistas de Carga de viajes | Reutilizable para listados densos, pero hoy está concentrado en un solo módulo. |
| `DataTable` | Dashboards de Compras/Ventas, reportes, POS admin y delivery | Reutilizable para tablas presentacionales; no sustituye un ABM con selección, edición y paginación server-side. |
| `DataGrid` | Detalle/ejecución de Consultas | Incluye paginación propia basada en página cero; patrón distinto de los ABM. |
| `GlobalFiltersBar` | Analítica de Compras e Informes IA | Filtros analíticos compartidos. |
| `VentasFiltersBar` | Analítica de Ventas | Duplica el concepto de barra analítica con un modelo específico de Ventas. |
| `CuentasComercialesPage` | Clientes y Proveedores maestro | Reutilización real: listado, smart-search, configuración de vista, editor, selección masiva y paginación para dos entidades. |
| `ComprobanteViewer` | Página visualizadora | Ficha compleja reutilizable de comprobante con múltiples tablas/secciones. |
| `KpiCard`, gráficos, `DetailCard`, `ResumenIvaTable` | Dashboards | Buen conjunto para plantilla Dashboard/Detalle analítico. |
| `LookupAutocomplete`, `QuickClienteModal` | Viajes, POS y delivery | Búsquedas contextuales reutilizables, no buscador general de listado. |
| `LoggedErrorView`, hosts de diálogos/mensajes/loading | Shell y operaciones | Estados transversales reutilizables; su adopción visual no es uniforme en páginas antiguas. |

## Buscadores, filtros y paginación

### Buscadores duplicados

1. Buscador integrado de `MainPageHeader` (`PageHeaderSearchConfig` o `SearchContent`) en los ABM nuevos y módulos que adoptaron el encabezado.
2. Smart-search interno con chips/panel desplegable en `CuentasComercialesPage`, Contactos, Técnicos, Tickets y Usuarios; en varios casos coexiste conceptualmente con el buscador del header.
3. `GlobalFiltersBar` para Compras y `VentasFiltersBar` para Ventas: misma ubicación funcional, contratos y presentación separados.
4. Buscadores propios en Consultas, Ayuda, Auditorías, Costos, Interfaces, Informes, Conversaciones, Partes de horas, POS y pantallas Admin.
5. Búsquedas contextuales dentro de formularios (clientes, artículos, cuentas, contactos) que sí tienen una finalidad distinta y no deben confundirse con la búsqueda del listado.

### Paginación

- ABM con paginación visible propia: Clientes/Proveedores (`CuentasComercialesPage`), Contactos, Técnicos, Tickets, Usuarios, Interfaces y Partes de horas.
- `DataGrid` implementa otra paginación (índice base cero, primera/anterior/números/siguiente/última y tamaño).
- `DataTable` es principalmente presentacional; algunas páginas calculan o limitan resultados fuera del componente.
- Comprobantes de Compras y Reporte de Compras muestran paginación/listados paginados con controles propios.
- Muchos listados Admin, analíticos y de configuración no muestran paginación consistente.

## Inventario por área

En las listas siguientes, “estados base” significa: cargando, error y sin resultados cuando el archivo los implementa; no implica que todos tengan idéntica presentación.

### Shell, acceso y páginas técnicas

- **Launcher** (`/`, `/{idweb}/{idbase:int}`; `Launcher.razor`; MainLayout): dashboard/launcher con favoritos y recientes. Estados cargando/error/vacío. Navega a módulos; depende de menús laterales/globales del shell.
- **Login** (`/login`; `Login.razor`; PublicLayout): formulario; ingresar, selección de base/cliente y flujos auxiliares. Estados carga, error y validación del formulario.
- **Registro** (`/registrarme`; `Register.razor`; PublicLayout): formulario especial de alta SaaS; confirmar/volver. No comparte el editor ABM.
- **Verificación** (`/verify/{Code}`; `Verify.razor`; PublicLayout): detalle de resultado; cargando, éxito/error y acceso a nuevo registro.
- **Seleccionar base** (`/seleccionar-base`, `/{idweb}`; `SeleccionarBase.razor`; PublicLayout): selección/búsqueda y navegación; cargando, vacío y error.
- **Inicio redirect** (`/inicio`; `Inicio.razor`; MainLayout): pantalla puente que redirige al inicio principal.
- **Workspace dinámico** (`/shell/{ModuleKey}`; `ShellWorkspacePage.razor`; MainLayout): pantalla especial generada desde metadatos, con secciones y navegación; cargando/error.
- **SaaS route bridge** (`/{idweb}/{idbase:int}/{*path}`; `SaasRouteBridge.razor`; MainLayout): puente/fallback dinámico; componente dinámico o ruta no disponible.
- **Error**, **Counter** y **Weather** (`/Error`, `/counter`, `/weather`; archivos homónimos; MainLayout): error global y páginas residuales/demo. Weather tiene tabla/carga; Counter solo contador.

### Administración, seguridad y auditoría

- **Administrar**, **Admin clientes**, **Admin usuarios**, **Admin bases** (`/admin`, `/admin/clientes`, `/admin/users`, `/admin/bases`; archivos `Admin*.razor`; MainLayout explícito): ABM/listados con formularios locales, búsquedas y tablas. Acciones nuevo/editar/guardar/cancelar/eliminar según entidad y volver a Admin. Encabezados y botones Bootstrap propios; no usan `MainPageHeader`.
- **Usuarios**, **Técnicos** (`/usuarios`, `/tecnicos`; archivos homónimos; MainLayout): listado ABM + editor en la misma página, smart-search/filtros, configuración de vista, selección, paginación, nuevo/guardar/cancelar/recargar y baja con confirmación. Usan `MainPageHeader`; estados creación/edición/cargando/vacío/error/validación/selección/confirmación.
- **Autorización de tareas** (`/seguridad/autorizacion-tareas`; `AutorizacionTareas.razor`): configuración por usuario/sistema con árbol lateral/contextual, búsqueda/filtros, recargar, guardar y cancelar. Tiene cambios sucios y confirmaciones; encabezado local.
- **Auditoría** (`/auditoria`; `Auditoria.razor`): dashboard con accesos a errores/usuarios, indicadores y tabla.
- **Auditoría de errores** (`/auditoria/errores`; `AuditoriaErrores.razor`): listado filtrable con búsqueda y detalle navegable; usa `MainPageHeader`.
- **Detalle de error** (`/auditoria/error/{Id:int}`; `AuditoriaErrorDetalle.razor`): ficha de solo lectura con volver, cargando/error.
- **Auditoría de usuarios** (`/auditoria/usuarios`; `AuditoriaUsuarios.razor`): listado, detalle de actividad/alerta y configuración; filtros, recarga, exportación, guardar/cancelar/volver. Usa `MainPageHeader` y tiene varias vistas internas.
- **Auditoría de comprobantes** (`/auditoria/comprobantes`; `AuditoriaComprobantes.razor`): listado filtrable + panel de detalle, buscar/recargar/volver; estados base.

### Maestros, CRM, tickets y trabajo

- **Clientes** y **Proveedores maestro** (`/clientes`, `/proveedores`; wrappers `Clientes.razor`, `ProveedoresMaestro.razor` + `CuentasComercialesPage.razor`): mismo ABM reutilizado con listado, smart-search, filtros/chips, configuración de vista, paginación, selección masiva, editor por pestañas, nuevo/editar/guardar/cancelar/recargar/baja. El componente conserva encabezado interno, a diferencia de otros ABM que usan `MainPageHeader`.
- **Contactos** (`/contactos`; `Contactos.razor`): ABM listado/editor, cuentas vinculadas y unificación; `MainPageHeader`, smart-search, filtros, configuración de vista, paginación, selección y confirmación.
- **CRM** (`/crm`; `Crm.razor`): kanban por etapas con configuración de etapas/etiquetas/vista y editor contextual; búsqueda/filtros, guardar/cancelar/eliminar/volver. Usa `MainPageHeader`. Su drag/drop y columnas impiden tratarlo como tabla simple.
- **Tickets** (`/tickets`; `Tickets.razor`): listado/kanban, detalle/editor, etiquetas, búsquedas guardadas, selección y acciones masivas; nuevo/guardar/cancelar/eliminar/recargar. Usa `MainPageHeader`; es más complejo que un listado estándar.
- **Tareas** (`/tareas`; `Tareas.razor`): tablero/listas por grupos con notas, adjuntos y editor; nuevo/guardar/cancelar/eliminar/recargar, filtros y confirmación de cierre sin guardar. Encabezado local y paneles contextuales del shell relacionados.
- **Partes de horas** (múltiples alias, principal `/partes-horas`; `PartesHoras.razor`): formulario + listado paginado + resumen por cliente; nuevo/editar/guardar/cancelar/recargar, búsqueda/filtros y validación/error.

### Compras, costos e informes IA

- **Dashboard Compras**, **Actividad**, **Artículos**, **Comprobantes**, **Familias**, **Rubros**, **Proveedores analítico** (`/compras` y subrutas; `Home.razor`, `Actividad.razor`, `Articulos.razor`, `Comprobantes.razor`, `Familias.razor`, `Rubros.razor`, `Proveedores.razor`): dashboards/listados analíticos con `GlobalFiltersBar`, KPIs, gráficos, `DataTable` y paneles de detalle. Acciones predominantes filtros, selección/drill-down y paginación en Comprobantes; no son ABM.
- **Reportes de Compras** (`/compras/reportes`; `ReporteCompras.razor`): pantalla de reporte parametrizable con tablas alternativas, búsqueda/filtros, guardar parámetros, paginar, exportar e imprimir.
- **Informes IA** y **Resultado Informes IA** (`/compras/informesia`, `/compras/informesia/resultado/{ExecutionId:guid}`; dos archivos): formulario de consulta con sugerencias/historial y pantalla de resultado con tabla/gráficos/exportación.
- **Costos**, **Historial**, **Nueva importación**, **Perfiles**, **Detalle de lote** (`/costos...`; cinco archivos): flujo especial de importación. Incluye dashboards/listados, ABM de perfiles, carga de archivo/vista previa y conciliación manual de lote. Acciones nuevo, guardar, cancelar, eliminar, volver, buscar y confirmar varían por etapa; no cabe en una única plantilla CRUD.

### Ventas y punto de venta

- **Dashboard Ventas**, **Artículos**, **Clientes**, **Comprobantes**, **Familias**, **Rubros** (`/ventas` y subrutas analíticas; seis archivos): dashboards/listados analíticos con `VentasFiltersBar`, KPIs, gráficos y `DataTable`; filtros y drill-down, sin ABM.
- **Comparativo Ventas/Compras** (`/ventas/comparativo`; `Comparativo.razor`): dashboard comparativo con período, gráficos y detalle mensual.
- **Selector Punto de Venta** (`/ventas/punto-venta`; `VentasPuntoVentaSelector.razor`): selección especial de entidad/modo y navegación.
- **Punto de Venta mostrador** (`/ventas/punto-venta/mostrador`; `VentasPuntoVenta.razor`): pantalla especial transaccional con catálogo, buscadores/filtros, carrito, cobro, recibo, caja, configuración y múltiples modales. Nuevo/guardar/cancelar/eliminar/imprimir. Requiere diseño específico.
- **Delivery/Take Away** (`/ventas/delivery`; `VentasDelivery.razor`): listado de pedidos por estado + modal de nuevo pedido y navegación al POS; filtros, nuevo, guardar/cancelar y recargar.
- **Administración de Puntos de Venta** (`/ventas/puntos-venta`; `PuntoVentaAdministracion.razor`): ABM jerárquico punto de venta → sectores → mesas con tablas y formularios inline.

### Carga de viajes

- **Carga de viajes** (`/carga-viajes`, `/carga-viajes/{Vista}`; `CargaViajes.razor`): multipantalla con listado de viajes, alta/edición, tarifas, choferes, destinos, tipos de vehículo, reportes, liquidaciones y configuración. Usa `MainPageHeader` y `ModuleListToolbar`; ofrece búsqueda, filtros, configuración de vista, cambio de vista, paginación/listados, nuevo/guardar/cancelar/recargar, exportar/imprimir y confirmaciones. Sus subdominios no comparten todos los campos ni acciones.
- **Liquidación de choferes/fleteros** (`/carga-viajes/liquidacion/vista`; `CargaViajesLiquidacion.razor`): reporte de solo lectura con filtros, volver, exportar e imprimir.
- **Vista previa de viaje** (`/carga-viajes/viaje/{Id:int}`; `ViajePreview.razor`): ficha imprimible con volver, imprimir/exportar; cargando/error/no disponible.

### Conversaciones y calendario

- **Conversaciones** (`/conversaciones`; `Conversaciones.razor`): inbox + hilo + panel contextual, mensajes, adjuntos, audio, contactos compartidos, plantillas y vínculos con tickets/partes. Búsqueda/filtros, selección, enviar/guardar, eliminar adjuntos u otras acciones contextuales, recargar y volver. Requiere plantilla específica de tres paneles.
- **Configuración de canales** (`/conversaciones/configuracion`; `ConversacionesConfiguracion.razor`): configuración/formulario con guardar, recargar y volver.
- **Estadísticas de conversaciones** (`/conversaciones/estadisticas`; `ConversacionesEstadisticas.razor`): dashboard con filtros, tablas e indicadores.
- **Plantillas de WhatsApp** (`/conversaciones/plantillas`; `ConversacionesPlantillas.razor`): listado + formulario de alta/edición con búsqueda/filtros, guardar y confirmación.
- **Calendario** (`/calendario`; `Calendario.razor`): calendario con búsqueda/filtros y formulario/modal de evento; nuevo/guardar/cancelar/eliminar y confirmaciones. Usa `MainPageHeader`.
- **Reuniones (administración)** (`/calendario/reuniones`; `CalendarioReuniones.razor`): listado + formulario de reuniones públicas; guardar/cancelar y confirmación.
- **Reserva pública de reuniones** (`/reuniones`, `/reuniones/{Slug}`; `ReunionesPublicas.razor`; PublicLayout): selector de fecha/hora y formulario público de confirmación; flujo especial sin shell.

### Consultas, contabilidad, stock, caja e interfaces

- **Consultas**, **Editor de consulta**, **Detalle/ejecución** (`/consultas`, `/consultas/nueva`, `/consultas/{Id}/editar`, `/consultas/{Id}`; tres archivos): catálogo, constructor complejo y resultado jerárquico/tabular con `DataGrid`, búsqueda, filtros, nuevo/guardar/cancelar/eliminar/volver/recargar/imprimir. El constructor necesita plantilla especial, no formulario genérico.
- **Contabilidad**, **Posición de IVA**, **Caja y Bancos**, **Stock** (rutas homónimas; cuatro archivos): dashboards con filtros, KPIs/gráficos/tablas, estados base; sin ABM.
- **Interfaces**, **Editor de Interfaces**, **Configuración** (`/interfaces...`; tres archivos): inbox paginado con filtros/configuración de vista y cola IA; ficha/alta documental con adjuntos y `EditForm`; configuración de recepción. Acciones nuevo, editar, guardar/cancelar/eliminar/recargar/volver según pantalla.
- **Comprobante viewer** (`/comprobantes/viewer/{Tc}/{IdComprobante}`; wrapper + `ComprobanteViewer.razor`): ficha compleja de comprobante con secciones/tablas; solo lectura.
- **Actualizaciones** (`/actualizaciones`; `Actualizaciones.razor`): configuración/operación de updates con recargar, guardar, filtros/selección y estados cargando/error.

### Contenido, ayuda y novedades

- **Informes** (`/informes`; `Informes.razor`): editor documental especial con árbol lateral, artículo, comentarios, versiones, archivos y comandos de edición; nuevo/guardar/cancelar/eliminar, búsqueda/filtros y confirmaciones.
- **Novedades** (`/informes/novedades`; `Novedades.razor`): editor/listado de novedades con búsqueda/filtros, guardar/cancelar/eliminar/recargar.
- **Ayuda** (`/ayuda`; `Ayuda.razor`): lector documental con tabla de contenidos lateral y búsqueda, resultados/sin resultados; no es ABM.

## Estados y permisos

- **Cargando, error y sin resultados** aparecen ampliamente, pero con estructuras diferentes: texto inline, cards, `LoggedErrorView`, filas vacías o bloque completo.
- **Elemento seleccionado/detalle** se resuelve como fila resaltada + panel, editor en la misma página, modal, ruta dedicada o panel lateral según módulo.
- **Creación/edición/validación** son explícitos en ABM nuevos, Interfaces, Consultas, Costos, Calendario, Informes y administración; la validación puede ser por campo, mensaje general o ambas.
- **Confirmación de eliminación/baja** existe en ABM, Calendario, Tickets, CRM, Informes, Costos y otros, pero se implementa con hosts globales, modales propios o bloques inline.
- **Sin permisos** no tiene una presentación de página uniforme. El control suele ocurrir en el shell/menú o deshabilitando acciones; `AutorizacionTareas` es la pantalla de administración de permisos, no un estado visual compartido. No se encontró un componente de “sin permisos” adoptado universalmente.

## Barras, menús y paneles contextuales

- El shell aporta encabezado superior, navegación de aplicaciones/módulos, menú lateral responsive y paneles globales (sesión, tareas y conversaciones/notificaciones).
- Los encabezados internos `page-intro`, `panel-card__header`, toolbars Bootstrap y barras analíticas conviven con `MainPageHeader`.
- Menús laterales propios: árbol de Informes, tabla de contenidos de Ayuda, inbox de Conversaciones, selector/árbol de autorización, navegación interna de módulos multipanel y paneles del POS.
- Paneles contextuales: detalles de filas en dashboards, ficha/editor lateral de ABM, detalle de conversación/ticket, carrito/cobro/caja en POS, árbol y versiones/comentarios en Informes, cola IA en Interfaces y panel global de tareas.

## Inconsistencias de acciones y navegación

1. **Nuevo** aparece en `MainPageHeader`, en `page-intro`, dentro de cards, toolbars Bootstrap, modales y botones flotantes/contextuales.
2. **Guardar/Cancelar** se ubican alternativamente en el header global, pie del editor, pie de modal, card lateral o barra inline. A veces “Cancelar” significa cerrar editor; en Delivery también significa anular pedido.
3. **Volver** puede usar historial interno del header, callback específico, botón local con ruta fija, historial del navegador o retorno al launcher.
4. Solo 9/81 implementaciones configuran el encabezado global; títulos duplicados y barras locales dominan el resto.
5. Los filtros pueden estar siempre visibles, desplegables bajo el buscador, en una barra global, dentro de un modal o en una card independiente.
6. La paginación difiere en base de índice, cantidad de controles, selector de tamaño, texto de totales y ubicación.
7. “Eliminar” y “Dar de baja/desactivar” comparten estilos/confirmaciones no uniformes; algunos módulos usan baja lógica y otros acciones específicas.
8. El shell ofrece exportación global a PDF mientras ciertas páginas agregan Exportar PDF/Excel e Imprimir localmente, a veces como dos botones que ejecutan el mismo flujo.

## Diferencias que impiden una única plantilla

- Los dashboards requieren KPIs, gráficos y drill-down, no herramientas CRUD.
- CRM y Tickets necesitan alternar tabla/kanban, drag/drop, selección masiva y panel de detalle.
- Conversaciones requiere tres zonas coordinadas, tiempo real y composición multimedia.
- POS preserva carrito y contexto transaccional mientras abre cobro, caja, cliente y configuración.
- Informes/Consultas son editores estructurados con árboles, versiones o constructores dinámicos.
- Carga de viajes agrupa ABM, tarifa, liquidación, reporte y ficha en una sola ruta con acciones heterogéneas.
- Calendario necesita navegación temporal y geometría de eventos.
- Flujos públicos (login, registro, verificación, reservas) no pueden heredar el shell autenticado.

## Matriz de pantallas

Abreviaturas: `ML` = MainLayout; `PL` = PublicLayout. “Base” = cargando/error/sin resultados según implementación.

| Módulo | Ruta | Tipo de vista | Acciones principales | Estados | Componentes actuales | Plantilla futura recomendada |
|---|---|---|---|---|---|---|
| Launcher | `/` | dashboard/launcher | abrir módulo | carga, error, vacío | `Launcher`, `AppLoadingFrame`, ML | 8. Dashboard |
| Login | `/login` | formulario | ingresar, cancelar | carga, error, validación | `LoginShell`, auth inputs, PL | 9. Pantalla especial |
| Registro | `/registrarme` | formulario público | registrar, volver | edición, validación, error | `Register`, PL | 9. Pantalla especial |
| Verificación | `/verify/{Code}` | detalle público | nuevo registro | carga, éxito, error | PL | 9. Pantalla especial |
| Seleccionar base | `/seleccionar-base` | selección/configuración | buscar, seleccionar, volver | carga, vacío, error | `LoginShell`, PL | 9. Pantalla especial |
| Inicio redirect | `/inicio` | otro/puente | redirigir | carga | ML | 9. Pantalla especial |
| Workspace dinámico | `/shell/{ModuleKey}` | otro | abrir sección | carga, error, vacío | ML | 9. Pantalla especial |
| SaaS route bridge | `/{idweb}/{idbase:int}/{*path}` | otro/puente | resolver ruta | no disponible, error | `DynamicComponent`, ML | 9. Pantalla especial |
| Error | `/Error` | otro | — | error | ML | 9. Pantalla especial |
| Counter | `/counter` | otro/demo | incrementar | normal | ML | 9. Pantalla especial |
| Weather | `/weather` | listado/demo | — | carga | tabla local, ML | 1. Listado estándar |
| Administrar | `/admin` | configuración ABM | nuevo, editar, guardar, cancelar, eliminar, volver | lista, selección, edición, confirmación, error | tablas/formularios locales, ML | 5. Pantalla de configuración ABM |
| Admin clientes | `/admin/clientes` | configuración ABM | buscar, nuevo, guardar, volver | carga, vacío, error, edición | tabla/form local, ML | 5. Pantalla de configuración ABM |
| Admin usuarios | `/admin/users` | configuración ABM | nuevo, editar, guardar, volver | carga, vacío, error, edición | tabla/form local, ML | 5. Pantalla de configuración ABM |
| Admin bases | `/admin/bases` | configuración ABM | nuevo, editar, guardar, volver | carga, vacío, error, edición | tabla/form local, ML | 5. Pantalla de configuración ABM |
| Usuarios | `/usuarios` | listado + formulario | nuevo, editar, guardar, cancelar, recargar, baja, filtros | lista, selección, creación, edición, carga, vacío, error, validación, confirmación | `MainPageHeader`, smart-search, tabla, paginación | 5. Pantalla de configuración ABM |
| Técnicos | `/tecnicos` | listado + formulario | nuevo, editar, guardar, cancelar, recargar, baja | igual Usuarios | `MainPageHeader`, tabla, paginación | 5. Pantalla de configuración ABM |
| Autorización tareas | `/seguridad/autorizacion-tareas` | configuración | recargar, guardar, cancelar, filtros | carga, vacío, error, cambios, confirmación | árbol, selector usuario/sistema | 5. Pantalla de configuración ABM |
| Auditoría | `/auditoria` | dashboard | navegar, filtros | carga, error | KPIs/gráfico/tabla | 8. Dashboard |
| Auditoría errores | `/auditoria/errores` | listado | buscar, filtros, abrir detalle | carga, vacío, error | `MainPageHeader`, tabla | 1. Listado estándar |
| Detalle error | `/auditoria/error/{Id:int}` | detalle | volver | carga, error, detalle | cards/textarea solo lectura | 4. Ficha o detalle de registro |
| Auditoría usuarios | `/auditoria/usuarios` | listado + detalle/configuración | filtros, recargar, exportar, guardar, cancelar, volver | base, detalle, edición | `MainPageHeader`, tablas, paneles | 9. Pantalla especial |
| Auditoría comprobantes | `/auditoria/comprobantes` | listado + detalle | buscar, filtros, recargar, volver | base, selección, detalle | tabla/panel local | 1. Listado estándar |
| Clientes | `/clientes` | listado + formulario | nuevo, editar, guardar, cancelar, recargar, baja, masivas | lista, selección, creación, edición, base, validación, confirmación | `CuentasComercialesPage`, paginación | 5. Pantalla de configuración ABM |
| Proveedores maestro | `/proveedores` | listado + formulario | igual Clientes | igual Clientes | `CuentasComercialesPage`, paginación | 5. Pantalla de configuración ABM |
| Contactos | `/contactos` | listado + formulario | nuevo, editar, guardar, cancelar, recargar, unificar | base, selección, creación, edición, validación, confirmación | `MainPageHeader`, smart-search, paginación | 5. Pantalla de configuración ABM |
| CRM | `/crm` | kanban/configuración | filtros, cambio vista, guardar, cancelar, eliminar | kanban, selección, edición, carga, vacío, error | `MainPageHeader`, columnas/etapas | 2. Listado con vista kanban |
| Tickets | `/tickets` | listado/kanban + detalle | nuevo, guardar, cancelar, eliminar, recargar, masivas | lista, kanban, selección, creación, edición, detalle, base | `MainPageHeader`, smart-search, paginación | 2. Listado con vista kanban |
| Tareas | `/tareas` | tablero | nuevo, guardar, cancelar, eliminar, recargar, filtros | tablero, edición, carga, vacío, error, confirmación | grupos, notas, adjuntos | 2. Listado con vista kanban |
| Partes de horas | `/partes-horas` | formulario + listado | nuevo, editar, guardar, cancelar, recargar, filtros | base, creación, edición, validación | tabla, paginación, resumen | 5. Pantalla de configuración ABM |
| Compras | `/compras` | dashboard | filtros, drill-down | base | `GlobalFiltersBar`, KPIs, gráficos, `DataTable` | 8. Dashboard |
| Actividad Compras | `/compras/actividad` | dashboard/listado | filtros, seleccionar detalle | base, selección, detalle | filtros, gráficos, `DataTable` | 8. Dashboard |
| Artículos Compras | `/compras/articulos` | listado analítico | filtros, seleccionar detalle | carga, vacío, selección, detalle | filtros, KPIs, tabla, gráficos | 1. Listado estándar |
| Comprobantes Compras | `/compras/comprobantes` | listado + detalle | filtros, paginar, seleccionar | base, selección, detalle | filtros, `DataTable` | 1. Listado estándar |
| Familias Compras | `/compras/familias` | listado analítico | filtros, seleccionar | carga, vacío, detalle | filtros, gráficos, `DataTable` | 1. Listado estándar |
| Rubros Compras | `/compras/rubros` | listado analítico | filtros, seleccionar | carga, vacío, detalle | filtros, gráficos, `DataTable` | 1. Listado estándar |
| Proveedores Compras | `/compras/proveedores` | listado analítico | filtros | base | `GlobalFiltersBar`, tabla | 1. Listado estándar |
| Reportes Compras | `/compras/reportes` | reporte | filtros, buscar, paginar, exportar, imprimir | carga, vacío, error | `DataTable`, inputs, KPIs | 9. Pantalla especial |
| Informes IA | `/compras/informesia` | formulario | filtros, ejecutar consulta | edición, carga, historial | `GlobalFiltersBar`, sugerencias | 3. Formulario de alta o edición |
| Resultado Informes IA | `/compras/informesia/resultado/{ExecutionId:guid}` | detalle/reporte | exportar | carga, vacío, error | gráficos, `DataTable` | 4. Ficha o detalle de registro |
| Costos | `/costos` | dashboard/listado | nueva importación, abrir historial/perfiles | carga, error | cards/tablas | 8. Dashboard |
| Historial Costos | `/costos/historial` | listado | nueva importación, abrir lote | carga, vacío | tabla | 1. Listado estándar |
| Nueva importación Costos | `/costos/nueva` | formulario/asistente | cargar, buscar, cancelar, volver | edición, vista previa, error | `InputFile`, tabla | 9. Pantalla especial |
| Perfiles Costos | `/costos/perfiles` | configuración ABM | nuevo, guardar, eliminar, buscar | lista, edición, carga, vacío, error | tabla/form local | 5. Pantalla de configuración ABM |
| Detalle lote Costos | `/costos/lotes/{Id:int}` | detalle/revisión | buscar, vincular, cancelar, volver | carga, detalle, error, confirmación | tabla/panel matching | 9. Pantalla especial |
| Ventas | `/ventas` | dashboard | filtros, drill-down | base | `VentasFiltersBar`, gráficos, `DataTable` | 8. Dashboard |
| Artículos Ventas | `/ventas/articulos` | listado analítico | filtros | base | filtros, KPIs, gráficos, `DataTable` | 1. Listado estándar |
| Clientes Ventas | `/ventas/clientes` | listado analítico | filtros | base | filtros, gráfico/tabla | 1. Listado estándar |
| Comprobantes Ventas | `/ventas/comprobantes` | listado analítico | filtros | base | filtros, KPIs, tabla | 1. Listado estándar |
| Familias Ventas | `/ventas/familias` | listado analítico | filtros | base | filtros, gráficos, `DataTable` | 1. Listado estándar |
| Rubros Ventas | `/ventas/rubros` | listado analítico | filtros | base | filtros, gráficos, `DataTable` | 1. Listado estándar |
| Comparativo | `/ventas/comparativo` | dashboard | filtros/período | base | gráficos y tabla | 8. Dashboard |
| Selector POS | `/ventas/punto-venta` | selección | seleccionar/abrir | carga, vacío, error | cards de entidad | 9. Pantalla especial |
| POS mostrador | `/ventas/punto-venta/mostrador` | transaccional | buscar, filtros, nuevo, guardar, cancelar, eliminar, imprimir | catálogo, carrito, cobro, validación, confirmaciones, carga/error | catálogo, carrito, modales, caja | 9. Pantalla especial |
| Delivery | `/ventas/delivery` | listado + formulario modal | filtros, nuevo, guardar, cancelar, recargar | base, creación | `DataTable`, KPIs, cliente modal | 1. Listado estándar |
| Administración POS | `/ventas/puntos-venta` | configuración ABM jerárquica | nuevo, editar, guardar, cancelar | lista, selección, edición, base | tres `DataTable`, `DetailCard` | 5. Pantalla de configuración ABM |
| Carga de viajes | `/carga-viajes/{Vista?}` | multipantalla | nuevo, editar, guardar, cancelar, filtros, vistas, recargar, exportar, imprimir | lista, detalle, creación, edición, selección, base, validación, confirmación | `MainPageHeader`, `ModuleListToolbar`, tablas/lookups | 9. Pantalla especial |
| Liquidación viajes | `/carga-viajes/liquidacion/vista` | reporte | filtros, volver, exportar, imprimir | carga, vacío, error | tabla | 9. Pantalla especial |
| Vista previa viaje | `/carga-viajes/viaje/{Id:int}` | detalle imprimible | volver, exportar, imprimir | carga, error, no disponible | ficha | 4. Ficha o detalle de registro |
| Conversaciones | `/conversaciones` | conversación | buscar, filtros, seleccionar, enviar, adjuntar, eliminar, recargar | inbox, hilo, selección, carga, vacío, error | inbox/hilo/contexto | 7. Conversaciones |
| Config. conversaciones | `/conversaciones/configuracion` | configuración | guardar, recargar, volver | edición, carga, error | formulario local | 5. Pantalla de configuración ABM |
| Estadísticas conversaciones | `/conversaciones/estadisticas` | dashboard | buscar, filtros | base | indicadores/tabla | 8. Dashboard |
| Plantillas WhatsApp | `/conversaciones/plantillas` | configuración ABM | buscar, filtros, nuevo/editar, guardar | lista, edición, base, confirmación | tabla/form local | 5. Pantalla de configuración ABM |
| Calendario | `/calendario` | calendario | buscar, filtros, nuevo, guardar, cancelar, eliminar | calendario, selección, creación, edición, base, confirmación | `MainPageHeader`, grilla/modal | 6. Calendario |
| Reuniones admin | `/calendario/reuniones` | configuración ABM | guardar, cancelar | lista, edición, base, confirmación | tabla/form local | 5. Pantalla de configuración ABM |
| Reserva pública | `/reuniones/{Slug?}` | calendario/form público | seleccionar fecha/hora, confirmar | carga, selección, confirmación, error | selector slots, PL | 9. Pantalla especial |
| Consultas | `/consultas` | listado | buscar, abrir | carga, vacío, error | tabla | 1. Listado estándar |
| Editor consulta | `/consultas/nueva`, `/consultas/{Id}/editar` | formulario/constructor | nuevo, guardar, cancelar, eliminar, recargar, volver | creación, edición, validación, base, confirmación | builders de filtros/columnas/orden | 9. Pantalla especial |
| Detalle consulta | `/consultas/{Id}` | detalle/resultado | buscar, paginar, volver, imprimir | carga, vacío, error, detalle | árbol, `DataGrid`, gráfico | 4. Ficha o detalle de registro |
| Contabilidad | `/contabilidad` | dashboard | filtros | base | inputs, tabla/KPIs | 8. Dashboard |
| Posición IVA | `/contabilidad/posicion-iva` | dashboard | filtros | base | gráficos, `ResumenIvaTable` | 8. Dashboard |
| Caja y Bancos | `/caja-bancos` | dashboard | filtros | carga, error | gráficos/tabla | 8. Dashboard |
| Stock | `/stock` | dashboard | filtros | carga, error | gráficos/tabla | 8. Dashboard |
| Interfaces | `/interfaces` | listado | buscar, filtros, nuevo, editar, recargar, guardar vista, eliminar | lista, selección, base, confirmación | tabla, paginación, cola IA | 1. Listado estándar |
| Editor Interfaces | `/interfaces/nuevo`, `/interfaces/{Id}` | formulario/ficha | guardar, cancelar, eliminar, volver | creación, edición/detalle, carga, error, validación, confirmación | `EditForm`, adjuntos, tabla | 3. Formulario de alta o edición |
| Config. Interfaces | `/interfaces/configuracion` | configuración | guardar, recargar, volver | edición, carga, error | formulario/tabla local | 5. Pantalla de configuración ABM |
| Visualizador comprobante | `/comprobantes/viewer/{Tc}/{IdComprobante}` | detalle | navegación contextual | carga/vacío, detalle | `ComprobanteViewer` | 4. Ficha o detalle de registro |
| Actualizaciones | `/actualizaciones` | configuración/operación | recargar, guardar | carga, vacío, error | cards/form local | 5. Pantalla de configuración ABM |
| Informes | `/informes` | editor documental | buscar, filtros, nuevo, guardar, cancelar, eliminar | selección, creación, edición, carga, vacío, error, confirmación | árbol, editor, comentarios/versiones | 9. Pantalla especial |
| Novedades | `/informes/novedades` | listado/editor | buscar, filtros, guardar, cancelar, eliminar, recargar | selección, edición, carga, vacío, confirmación | árbol/lista, editor | 9. Pantalla especial |
| Ayuda | `/ayuda` | detalle/documentación | buscar, navegar | carga, resultados, sin resultados | TOC lateral, lector | 4. Ficha o detalle de registro |

## Resultado cuantitativo y recomendación

- **Pantallas diferentes existentes:** 81 implementaciones Razor enrutables.
- **Resolubles con las ocho plantillas compartidas (1 a 8):** 60.
- **Clasificadas como “Pantalla especial” (plantilla 9):** 21. Esto no significa 21 diseños totalmente independientes: varias pueden compartir shell, feedback, encabezado, campos, tablas y acciones, pero su composición principal no encaja sin pérdida en las plantillas 1–8.
- **Diseño específico prioritario:** Login/Registro/Verificación/Selección de base, Launcher y workspace dinámico, Auditoría de usuarios, Reportes de Compras, importación y revisión de Costos, selector y mostrador POS, Carga de viajes/liquidación, reserva pública, editor de Consultas, Informes/Novedades y puentes/estados técnicos.
- **Inconsistencias de navegación más importantes:** adopción parcial de `MainPageHeader`; tres mecanismos de volver; acciones Nuevo/Guardar/Cancelar en ubicaciones distintas; títulos y toolbars locales duplicados; filtros y paginaciones con contratos diferentes; exportación global y local superpuesta; ausencia de un estado “sin permisos” común.

## Recomendación de consolidación previa al rediseño

Sin modificar funcionalidad, la base futura debería separar: (a) shell y navegación, (b) encabezado/acciones/estado, (c) plantilla de contenido y (d) componentes de datos. La primera decisión de rediseño debería ser adoptar un contrato único para `MainPageHeader` y para los estados de página; después unificar smart-search/filtros y paginación. Las pantallas especiales deben reutilizar esos contratos transversales sin forzar su contenido a una grilla o formulario genérico.

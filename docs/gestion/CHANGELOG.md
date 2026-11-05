# Changelog — Dashboard de Compras - Alfa Gestión

Formato: `[versión] — fecha — descripción`

---

## [Sin versión] — 2026-11-04

### Filtros, consulta de comprobantes y autorización de tareas

Se reutilizan el shell AlfaDesign, sus controles, el visor central y los servicios de consulta existentes. No hay rutas, tablas, permisos ni dependencias nuevos; no requiere actualización SQL.

| Archivo (relativo a `src/AlfaCore/`) | Cambio |
|---|---|
| `Components/Shared/FiltrosSmartSearch.razor` (nuevo) | Contenedor de búsqueda y filtros con cierre por Escape y aplicación por Enter; usa el posicionamiento compartido. |
| `Components/Shared/ContabilidadReportePanel.razor` | Filtros de Plan de cuentas, Diario, Subdiarios, Mayor y balances en el Context Toolbar; rangos rápidos y acceso al visor. |
| `Components/Shared/ContabilidadReportePanel.razor.css` | Filtros ocupan el ancho del panel y conservan el ajuste de campos. |
| `Components/Pages/ContabilidadAsientos.razor` | Integración posterior al pull: filtros en Smart Search del encabezado con período, origen, unidad y cuenta; rangos rápidos y Ver en comprobantes de origen. Se conserva la apertura del asiento y sus reglas de edición. |
| `Components/Pages/ContabilidadAsientos.razor.css` | Panel de filtros de Asientos al ancho del popover y rangos con ajuste de línea. |
| `Models/ContabilidadReportesModels.cs` | Texto de búsqueda del plan de cuentas. |
| `Services/ContabilidadReportesService.Libros.cs` | Búsqueda por código/descripción con el mismo filtro en pantalla, Excel y PDF; conserva niveles calculados sobre el plan completo. |
| `Components/Shared/GestionRangosFecha.cs` | Últimos siete días inclusivos, trimestre actual y trimestre anterior; conserva los demás rangos. |
| `wwwroot/js/alfa-smart-search-popover.js` | Abre el selector nativo al pulsar un campo de fecha; conserva fallback del navegador. |
| `Components/App.razor` | Actualiza la versión del recurso JavaScript. |
| `Components/Pages/VentasCargaComprobantes.razor` | Rangos rápidos para los pendientes por período; conserva el tratamiento especial de fecha de facturación automática. |
| `Components/Pages/VentasVisorComprobantes.razor` | Rangos compartidos y Excel/PDF de todas las páginas del filtro aplicado; cancela exportación al cambiar sesión o usuario. |
| `Services/ComprobantesVisorExportador.cs` (nuevo) | Excel con códigos de texto e importes numéricos y HTML escapado para el generador PDF existente. |
| `Components/Shared/ReporteComercialPanel.razor` | Unidades en desplegable múltiple cerca del período; acción Ver compartida para compras y ventas. |
| `Components/Shared/VentasSmartSearch.razor` | Unidades en desplegable múltiple acotado. |
| `Components/Shared/CuentaCorrienteVentasTabla.razor` | Inicio y cierre de cada cuenta diferenciados; saldo previo solo al continuar una cuenta; Ver en movimientos. |
| `Components/Shared/ComprobantesCuentaCorrienteTabla.razor` | Separación de cuentas y Ver para documentos/aplicaciones de resumida y analítica. |
| `wwwroot/css/gestion-alfadesign.css` | Apariencia compartida de desplegables y separación de cuentas. |
| `Components/Shared/ComprobanteVerAccion.razor` (nuevo) | Botón Ver y diálogo AlfaDesign con visor existente; convierte número visible contable al ID interno. |
| `Components/Shared/ComprobanteViewer.razor` | Conserva `directo=1` al abrir el visor en otra pestaña. |
| `Components/Pages/Actividad.razor` | Ver desde el listado de comprobantes del usuario. |
| `Components/Pages/Comprobantes.razor` | Ver desde el listado de compras. |
| `Components/Pages/Familias.razor` | Ver en el detalle de comprobantes de compras. |
| `Components/Pages/Rubros.razor` | Ver en el detalle de comprobantes de compras. |
| `Components/Shared/LibroIvaPanel.razor` | Ver desde libros de IVA, con identificador interno completo. |
| `Components/Pages/ContabilidadRetenciones.razor` | Ver comprobante de origen de retenciones. |
| `Components/Pages/AutorizacionTareas.razor` | Selector de usuario y búsqueda en Smart Search; Grabar/Cancelar en toolbar. No cambia el usuario cargado hasta consultar correctamente y exige resolver cambios pendientes antes de consultar otro. |

Pruebas nuevas: `tests/AlfaCore.Tests/GestionRangosFechaTests.cs` cubre límites de fechas, trimestre anterior y bisiestos. `tests/AlfaCore.Tests/ComprobantesVisorExportadorTests.cs` verifica códigos, importes, texto seguro para Excel/PDF y conversión del número para el visor.

Revisión de accesos existentes: auditoría, análisis de ventas, visor de ventas, conciliación bancaria, punto de venta y Portal Cliente ya cuentan con acciones propias de consulta; se mantienen sus recorridos y permisos. No se agrega un acceso genérico a filas sin clave de comprobante confirmada (totales por TC, documentos ARCA aún no ingresados, referencias de extractos o presentaciones de tarjetas).

Validación posterior al pull: 167 pruebas dirigidas aprobadas y 59 omitidas por sus condiciones de entorno. Compilación final con cero errores y cinco advertencias preexistentes, en salida aislada `.tmp/ui-validation`. La instancia existente responde HTTP 200 en `http://localhost:5055`; no se interrumpió su sesión y requiere reinicio para cargar el último ajuste de Asientos. Cinco casos del handler de calendario aprobados con Node (fecha, deshabilitado, texto, fallback y otro elemento). Catálogo: 107 rutinas, sin advertencias ni errores. UTF-8 sin BOM y `git diff --check` revisados.

Integración: se realizó pull fast-forward de ocho commits hasta `abf7abcd`, conservando los cambios locales sin conflictos. Llegaron Asientos y el visor de Compras; se completó la migración de los filtros de Asientos al encabezado. Se conservó un stash de resguardo previo a la integración.

Limitaciones de verificación visual: la instancia local arrancó y la extensión de Chrome quedó instalada, pero la herramienta no consiguió conectar su host de navegador; la apertura explícita con Navegador venció por timeout. No se validaron aún las resoluciones 2048/1440/1024, calendarios nativos ni apertura de comprobantes reales. Las pruebas automáticas se ejecutan con salida aislada para no sobrescribir el ejecutable de la instancia local en uso.

### Revisión contra Figma, prueba con datos y ayuda por pantalla

Prueba con datos en ALFA NET - PRUEBAS (base 4283) y comparación con el archivo Figma AlfaCore (02 Components: Filter Popover, Filter Item, Context Toolbar, Data Table Row). No hay tablas, permisos ni scripts SQL nuevos.

| Archivo (relativo a `src/AlfaCore/`) | Cambio |
|---|---|
| `Components/Shared/FiltroPeriodo.razor` (nuevo) | Columna "Período" única para todo el Smart Search: título con ícono, rangos verticales con el activo en azul, Desde/Hasta y contenido opcional. Reemplaza cinco copias distintas. |
| `Components/Shared/ContabilidadReportePanel.razor` y `.razor.css` | El panel de filtros se veía desarmado (estilos de bloque viejo dentro del popover). Ahora dos columnas, Período y Opciones. El chip de Mayor quedaba en «01/01/01» al entrar: se refresca al cargar catálogos. El chip del Plan de cuentas resume las opciones aplicadas. |
| `Components/Pages/ContabilidadAsientos.razor` y `.razor.css` | Columnas Período y Asientos con selectores estándar; el rango sigue aplicando al tocarlo. |
| `Components/Pages/VentasVisorComprobantes.razor` y `.razor.css` | Rangos como lista vertical (antes, botones redondeados distintos al resto). |
| `Components/Pages/VentasCargaComprobantes.razor` | Usa `FiltroPeriodo`; conserva «Fecha a facturar» de facturación automática. |
| `Components/Shared/ReporteComercialPanel.razor` | Usa `FiltroPeriodo`; Unidades de negocio con etiqueta y desplegable de 32 px; muestra el nombre si hay una sola unidad elegida. |
| `Components/Shared/FiltrosSmartSearch.razor` | Ancho del panel configurable (2 o 3 columnas) y foco en el panel al abrirlo con el embudo, para que Escape lo cierre. |
| `Components/Shared/ComprobanteVerAccion.razor`, `ComprobanteVerHost.razor` (nuevo), `Services/ComprobanteVerService.cs` (nuevo), `Components/Layout/MainLayout.razor`, `Program.cs` | «Ver» compacto (20 px, no agranda la fila) y un único diálogo por sesión. Antes cada fila creaba su propio diálogo (1190 en un Libro diario anual). |
| `Services/ReporteComercialService.CuentaCorrienteVentas.cs` | Cuenta corriente: no lista cuentas sin movimientos cuyo saldo inicial redondeado a centavos es cero (restos de milésimos se veían como «$ 0,00»). |
| `wwwroot/css/gestion-alfadesign.css` | Estilos de `FiltroPeriodo`, desplegable múltiple y acción Ver. Variantes de panel `--dos` (640 px, 2 columnas) y `--uno` (380 px): antes el bloque Aplicar sumaba una tercera columna vacía y angostaba los selectores. |
| `Components/Shared/LibroIvaPanel.razor` y `.razor.css` | Libros de IVA: filtros al Smart Search (Período con los últimos seis meses y «Otro mes» con flechas; Libro con la unidad/CUIT). Se quitan el bloque de filtros de la página y el «Generar» duplicado; elegir mes o unidad regenera al instante. |
| `wwwroot/js/alfa-smart-search-popover.js`, `Components/App.razor` | Escape cierra el panel de filtros abierto en cualquier pantalla (salvo que haya un diálogo encima). El calendario nativo también se abre en campos de mes. Versión de recursos actualizada para evitar caché. |
| `Services/AyudaManuales.cs`, `AlfaCore.csproj` | Nuevos manuales Contabilidad, Ventas: comprobantes, Reportes y Seguridad. Mapa pantalla → artículo: el ícono de ayuda abre la explicación de la pantalla actual. |
| `Docs/manual_contabilidad.md` (nuevo), `Docs/manual_seguridad.md` (nuevo) | Un artículo por pantalla: reportes contables, Plan de cuentas, Asientos, Retenciones, Libros de IVA y Autorización de tareas. |
| `Docs/manual_carga_comprobantes.md`, `Docs/manual_reportes_comerciales.md`, `Docs/manual_usuario.md` | Artículos Buscar pendientes, Ver comprobantes, Cuenta corriente, Actividad de compras, buscador superior, rangos rápidos y botón Ver. |

Prueba nueva: `AyudaContextualTests.CadaPantallaDocumentadaAbreUnArticuloQueExiste`. Suite completa: 2566 aprobadas, 132 omitidas, 12 fallidas preexistentes (11 de WhatsApp Embedded Signup sin conexión central y 1 del carrito). Catálogo sin errores.

Verificación visual en una instancia aislada (puerto 5065, procesos en segundo plano deshabilitados) contra ALFA NET - PRUEBAS: los diez paneles de filtros con columnas, rango activo y cierre con Escape; filas con Ver de 33 px (antes 47 px); un solo diálogo de detalle; Libro diario con Mes anterior (71 asientos); Cuenta corriente sin cuentas en cero; Libro de IVA por mes; ayuda abriendo el artículo de cada pantalla.

Ajustes tras la prueba de la usuaria:

| Archivo (relativo a `src/AlfaCore/`) | Cambio |
|---|---|
| `Components/Shared/ComprobanteViewer.razor` y `.razor.css` | Detalle de comprobante con estética AlfaDesign v1 (Figma Ficha / Lectura): tokens en lugar de colores fijos, botón primario azul, estados como Tag con punto, secciones con título simple y divisor. Totales sin renglones fijos en 0,00: Subtotal, IVA y Total siempre; descuentos, impuestos internos y recargo solo con importe. |
| `Components/Shared/ContabilidadReportePanel.razor` y `.razor.css` | Mayor: al entrar lista las cuentas del período (sumas y saldos agrupado), el buscador superior las filtra, un clic abre el mayor, Enter con una coincidencia la abre y «Cuentas» vuelve a la lista. Plan de cuentas: introducción, KPIs, árbol plegable (resumido hasta rubros), tipo como Tag y clic en una cuenta abre su Mayor. |
| `Components/Shared/FiltrosSmartSearch.razor` | Si el buscador admite texto, enfocarlo ya no abre el panel de filtros (tapaba la lista filtrada). |
| `Components/Shared/ReporteComercialPanel.razor` | Cartera del cliente: se quitan dos párrafos de ayuda (quedan en un ícono de información) y el campo pasa a llamarse «Cliente». |
| `Components/Shared/CuentaCorrienteVentasTabla.razor`, `ComprobantesCuentaCorrienteTabla.razor`, `wwwroot/css/gestion-alfadesign.css` | Encabezado de cada cuenta con franja azul, nombre en azul y código en gris; saldo al cierre sobre superficie elevada. |
| `Docs/manual_contabilidad.md`, `Docs/manual_reportes_comerciales.md` | Textos de Mayor, Plan de cuentas y Cliente acordes al nuevo funcionamiento. |
| `tests/AlfaCore.Tests/ReporteVentasFiltrosTests.cs`, `ReportesComercialesTests.cs` | Etiqueta y texto de ayuda del campo Cliente. |

Segunda ronda de ajustes:

| Archivo (relativo a `src/AlfaCore/`) | Cambio |
|---|---|
| `wwwroot/css/sticky-list-headers.css`, `Components/App.razor` | Los encabezados fijos de **todos** los listados usaban azul marino y celeste con `!important`; ahora usan los tokens AlfaDesign (con el color anterior como respaldo). Versiones de recursos actualizadas para evitar caché. |
| `Components/Shared/ComprobanteViewer.razor` y `.razor.css` | Sin restos de azul marino (fondo de `page-grid`, encabezados de tabla, subtarjeta de relacionados). La barra de acciones fija tiene fondo y divisor. Subtítulo de relacionados en lenguaje claro. |
| `Components/Shared/ComprobanteViewerModal.razor` (se elimina su `.razor.css`) | Pasa a usar la ventana estándar `AlfaDialog`: Ver comprobantes y el informe de Punto de Venta muestran el detalle igual que el resto. |
| `Components/Shared/BusquedaDiferida.cs` (nuevo), `FiltrosSmartSearch.razor`, `GestionSmartSearch.razor`, `ContabilidadReportePanel.razor`, `VentasVisorComprobantes.razor` | Filtrado mientras se escribe (350 ms de pausa) en Plan de cuentas y Ver comprobantes (opción `BuscarAlEscribir`); enfocar el buscador ya no abre el panel. `GestionSmartSearch` usa la columna `FiltroPeriodo` compartida. Asientos no usa búsqueda en vivo: sin período, la búsqueda por texto recorre toda la historia y llega al tiempo límite (ver pendientes). |
| `Docs/manual_contabilidad.md`, `Docs/manual_carga_comprobantes.md` | Búsqueda sin Enter en Plan, Asientos y Ver comprobantes. |

Integración con origin/main (4 commits: ABM del plan de cuentas, Asientos con `GestionSmartSearch` y últimos asientos, libro de IVA consolidado): en Asientos se adopta el buscador del equipo; en Plan de cuentas el árbol suma «Modificar» por fila y «Nueva cuenta» del ABM.

Menú superior y listas de búsqueda:

| Archivo (relativo a `src/AlfaCore/`) | Cambio |
|---|---|
| `Components/Layout/MainLayout.razor`, `wwwroot/js/alfa-topbar-nav.js` (nuevo), `wwwroot/css/alfacore-design.css`, `Components/App.razor` | El menú de secciones del módulo quedaba debajo de los íconos de la derecha cuando tenía muchas secciones (se ocultaban sin aviso). Ahora las que no entran pasan a «Más ▾»; la sección activa siempre queda visible y se recalcula al cambiar el ancho. |
| `wwwroot/app.css` | La animación de apertura de los paneles de filtros usaba `both` y dejaba un `transform` permanente: las listas de búsqueda (por ejemplo, Cliente en Cartera del cliente) se dibujaban fuera de la pantalla. Pasa a `backwards`. |
| `wwwroot/js/alfa-lookup-popover.js` | La lista de búsqueda se corrige sola si un contenedor animado o con transform vuelve a desplazarla. |

Pendientes detectados (no introducidos por este cambio): en ALFA NET - PRUEBAS (base 4283) la actualización `2026-11-03-004__crm_oportunidades_adjuntos.sql` falla con error 8152 (datos truncados) y frena las siguientes, incluida `2026-11-04-051`. En Asientos, buscar texto sin período agota los 120 s de espera (AUX_ERR `Asientos/Buscar`).

Decisiones: **Guardar** se mantiene en azul en todo el sistema (Figma lo muestra en verde). Retenciones conserva su panel de filtros dentro de la página.

## [Sin versión] — 2026-10-28

### Tableros — GENESIS (base 97)

- Ventas: el incidente `AUX_ERR` 7244 se debía a `OFFSET/FETCH` en SQL Server 2008. Vendedores y Unidades de negocio conservan la paginación de 50 comprobantes mediante `ROW_NUMBER`.
- Compras: actualización `2026-10-28-001__compras_vistas_dashboard_faltantes.sql`, que recupera las vistas de cabecera, detalle y jerarquía de familias omitidas por distribuirse en scripts sin versión. Solo crea vistas faltantes, sin reemplazar las existentes ni modificar movimientos.
- Aplicada la creación de vistas en GENESIS y comprobada su idempotencia. Verificadas allí las consultas del tablero de Compras y el análisis de Ventas con detalle y segunda página; Ventas requiere publicar el cambio de aplicación.
- Reportes de Compras: corregida la incompatibilidad con SQL Server 2008 en Detalle, Cuenta corriente, Saldos, Resumida y Analítica (incidentes 7330–7337). Paginación con `ROW_NUMBER`, acumulados mediante temporales indexadas y CTE recursivas, y rechazo de duplicados con `RAISERROR`/`RETURN`. Se conserva el servicio compartido con Ventas y se agregan pruebas SQL de lectura para contrastar saldos, páginas y aplicaciones. Requiere publicar AlfaCore.
- Clientes/Proveedores: corregido el listado en SQL Server 2008 (incidente GENESIS 7617) mediante paginación por `ROW_NUMBER` según versión del motor. SQL Server 2012 o posterior conserva `OFFSET/FETCH`; se mantienen los filtros, el orden y los totales. Se agregan pruebas SQL de lectura para ambos maestros. Requiere publicar AlfaCore.
- Portal Cliente, Pedidos: corregido el incidente GENESIS 7848 (`OFFSET/FETCH` no disponible en SQL Server 2008). `PortalClienteService.GetPedidosClienteAsync` pagina con `ROW_NUMBER`, manteniendo el orden fecha/ID descendente y el filtro de cuenta, NP, sucursal 9999, fechas y número. Se agregan pruebas SQL de lectura de páginas y filtros; no modifica pedidos ni esquema. Requiere publicar AlfaCore.
- Archivos — Artículos, Técnicos y Contactos: corregidos los incidentes GENESIS 7865, 7893 y 7887 por paginación incompatible con SQL Server 2008. Se mantiene `OFFSET/FETCH` desde SQL Server 2012 y se usa rango de `ROW_NUMBER` en motores anteriores. Contactos pagina antes de resolver la conversación WhatsApp y conserva filtros/orden. Técnicos agrega el código como desempate de nombres iguales. `Common/SqlServerCompatibilidad.cs` reúne la paginación y la conversión segura de códigos enteros sin `TRY_CAST`, usada para ordenar artículos por código, proponer códigos de artículo/técnico y resolver referencias de artículos; descarta alfanuméricos y desbordes, con signos y ceros iniciales. Nuevas pruebas SQL de lectura para los tres maestros y límites numéricos. No modifica maestros ni esquema; requiere publicar AlfaCore.
- Verificación de Archivos: cuatro pruebas aprobadas en GENESIS y las mismas cuatro en DISTRIWALTERP. Se contrastaron páginas, totales, búsquedas, activos/bajas, orden alternativo y próximos códigos; la conversión se contrastó con límites `int`/`bigint`, ceros iniciales, signos, alfanuméricos y desbordes. Catálogo: 107 rutinas, sin advertencias ni errores.

## [Sin versión] — 2026-10-26

### Carga de viajes — tarifas compartidas entre clientes

- En el editor de tarifas de cliente se agregó **Clientes que comparten estas tarifas**. Al guardar, todas las tarifas del cliente principal se duplican en los clientes agregados.
- Desde ese momento, cada alta, edición, cambio de importe (incluidos la edición rápida y el aumento masivo), cambio de adicionales o baja de una tarifa del principal se copia automáticamente a los vinculados (`CargaViajesService.TarifasVinculadas.cs`).
  - El cálculo de viajes no cambia: sigue resolviendo por `IDCLIENTE`.
- Botón **Duplicar** en la grilla de tarifas: abre una tarifa nueva con los mismos datos, para cambiar cliente, destino o tipo de vehículo.
- Script `2026-10-26-001__carga_viajes_tarifas_clientes_vinculados.sql`: crea `TA_TARIFA_CLIENTES_VINCULADOS` (principal → vinculado, un vinculado sólo puede tener un principal) y agrega `TA_TARIFA.IdTarifaOrigen`. El mismo esquema se asegura en `EnsureViajesSchemaAsync`.

---

## [1.7.0] — 2026-07-02

### Menú Web — independencia total de tablas legacy y reestructuración

**Independencia de TA_MENU / TA_TAREAS**
- `MenuService` y `AutorizacionTareasService` ya no leen de `TA_MENU` ni `TA_TAREAS`. Usan exclusivamente `ALFACORE_MENU_WEB` y `ALFACORE_TAREAS_WEB`.
- Eliminada la lógica condicional de fallback (`hasLegacyMenu`, `legacyJoin`, `parentExpr`, `nameExpr`, etc.) que generaba SQL dinámico en tiempo de ejecución.
- La columna `PadreClave` de `ALFACORE_MENU_WEB` reemplaza al JOIN con `TA_MENU` para resolver la jerarquía de módulos.
- Script `2026-07-01-001__seguridad_menu_web_y_tareas_web.sql`: agrega `PadreClave`, `NombreWeb` y `DescripcionWeb` a `ALFACORE_MENU_WEB` y migra los datos desde `TA_MENU`.

**Reestructuración del árbol de menú**
- Nuevo módulo raíz **Tableros** (`DTABLEROS`, orden 5): agrupa los cinco dashboards operativos que antes estaban dispersos bajo sus módulos de origen.
  - D5001 (Compras), D6000 (Ventas), D7510 (Caja y Bancos), D8079 (Stock), D9502 (Contabilidad).
  - D6000 reclasificado: deja de ser un workspace y pasa a ser "Dashboard de Ventas" (`/ventas`).
  - D6054 (Punto de venta) pasa directamente bajo el módulo Ventas (`D60`).
  - D75 (Caja y Bancos) y D80 (Stock) deshabilitados al quedar sin subitems.
- **Logística** (`D010180`) promovido a módulo raíz (orden 92, antes de Gestión Contable).
  - Corregido bug: `D010181` (Carga de viaje) tenía `PadreClave=D0101`; corregido a `D010180`.
- **CRM** (`D010185`) promovido a módulo raíz (orden 97, después de Gestión Contable).
  - Conversaciones y Tickets pasan directamente bajo CRM (se elimina el agrupador intermedio `D010185WEB`).
- Script `2026-07-02-001__menu_reestructuracion_tableros_crm_logistica.sql`: idempotente, preserva las claves existentes para no afectar autorizaciones vigentes.

**Módulo Logística — estructura TA_TARIFA**
- Script `2026-06-30-001__carga_viajes_ta_tarifa_estructura.sql`: crea `TA_TARIFA` si no existe, o agrega las columnas faltantes de forma segura (sin DROP).
  - Nuevas columnas: `PorcentajeAdic0`–`PorcentajeAdic4`, `ACTIVO`, `FECHAHORA_ALTA`, `FECHAHORA_MODIFICACION`, `AdicionalFijo1–3 Descripcion/Importe`, `USUARIOMODIFICACION`.
- Script `2026-07-01-002__carga_viajes_ta_tarifa_usuario_modificacion.sql`: complemento para bases donde la tabla ya existía sin esas columnas.

### Correcciones

**NotificacionesPushService — error 8537 en `/api/notificaciones-push/settings`**
- La query en `UserCanUseConversacionesAsync` tenía `AND ( OR UPPER(...) LIKE ...)` — sintaxis SQL inválida que causaba una excepción en cualquier llamada al endpoint de configuración de notificaciones push.
- Corregido: la primera condición de cada grupo `AND (...)` ya no incluye el `OR` espurio.

**Ayuda.razor — encoding mojibake**
- Ocho strings con caracteres españoles almacenados como secuencias Latin-1 (vocales acentuadas y eñe, entre otros) corregidos a UTF-8 literal.
- Afectaba los títulos y subtítulos de los tópicos "Ayuda" y "Diseñador de Consultas".

**AGENTS.md — regla de encoding UTF-8**
- Agregada sección con la regla de que todos los archivos fuente deben estar en UTF-8 sin BOM y usar caracteres Unicode correctos en literales de texto. Incluye tabla de sustitución mojibake → correcto.

---

## [1.6.2] — 2026-06-16

### Carga de Viajes: autocomplete compacto

- El componente reutilizable `LookupAutocomplete` fue ajustado para mostrar resultados en un dropdown compacto sin empujar el layout.
- Los resultados ahora se renderizan en una sola línea por opción, con altura máxima y scroll interno.
- Se agregó debounce simple y cancelación de búsquedas en curso para evitar consultas por cada tecla.
- Los errores de consulta se presentan como mensaje controlado dentro del desplegable.

---

## [1.6.1] — 2026-04-30

### Renombrado técnico y de instalación a AlfaCore

**Branding y artefactos**
- El nombre visible del producto en instalador, accesos directos, textos de arranque y shell pasó de `Dashboard de Compras` a `AlfaCore`.
- Los binarios publicados ahora se generan como `AlfaCore` y `AlfaCoreShell` mediante `AssemblyName`, sin rehacer namespaces ni mover la estructura fuente existente.
- La hoja de estilos aislada referenciada por la app pasó a `AlfaCore.styles.css`.

**Servicio Windows y publicación**
- El servicio Windows ahora se registra como `AlfaCore`.
- Se mantiene compatibilidad de migración: los scripts de instalación y desinstalación detectan y eliminan el servicio anterior `AlfaCore` si existe.
- Las carpetas de salida de publicación e instalador fueron renombradas a `publish\AlfaCoreLAN` y `publish\AlfaCoreInstaller`.
- El instalador genera ahora `AlfaCoreSetup_<versión>.exe`.

**Shell, launcher y utilitarios**
- `LauncherOptions` y `BackendLauncher` fueron ajustados para iniciar `AlfaCore.exe` y `AlfaCoreShell.exe`.
- La regla de Firewall pasó a nombrarse `AlfaCoreLAN-<puerto>`.
- `ApplicationName` de SQL Server pasó a `AlfaCore` en la conexión inicial y en las sesiones persistidas.

---

## [1.6.0] — 2026-04-30

### Módulo Contabilidad — Balance General de Saldos y Posición de IVA

**Balance General de Saldos (`/contabilidad`)**
- La página de Contabilidad fue reemplazada: el contenido anterior basado en `MV_ASIENTOS` (KPIs de debe/haber, gráficos de evolución, tabla de últimos asientos) fue removido.
- Nuevo bloque principal: Balance General de Saldos ejecutando el SP `NW_SALDOSCUENTAS`.
- El nivel de agrupación del plan de cuentas **no está hardcodeado**: se lee dinámicamente desde `TA_CONFIGURACION` (claves `DIGITOS_CAPITULO`, `DIGITOS_SUBCAPITULO`, `DIGITOS_RUBRO`, `DIGITOS_SUBRUBRO`) y se suma según el nivel elegido.
- Selector de nivel en la UI: 1 (Capítulo), 2 (Sub-capítulo, default), 3 (Rubro), 4 (Sub-rubro). El botón "Actualizar" recarga solo el balance sin afectar el resto de la página.
- Tabla con **una columna por nivel**: el saldo de cada cuenta aparece en la columna correspondiente a su profundidad en el plan (el nivel más detallado queda a la izquierda, el Capítulo a la derecha), igual que en el reporte impreso del sistema.
- Filas ordenadas por código alfabético, lo que preserva el orden jerárquico correcto del plan de cuentas (`"1" < "11" < "12" < "2" < "21"...`).
- Cuentas de capítulo destacadas en negrita; cuentas hijo con color secundario.
- Saldos negativos en rojo entre paréntesis (ej.: `($ 176.578.335,49)`).
- KPI cards: Total Activo, Total Pasivo, Patrimonio Neto, Resultados — derivados de las cuentas de capítulo (código de `DIGITOS_CAPITULO` dígitos).
- Filtros simplificados: fecha desde/hasta, accesos rápidos de período, y sucursal (mapeada a `UNEGOCIO` numérico en el `@WHERE` del SP). El filtro de sucursal solo se aplica si el valor es un entero válido (prevención de inyección SQL en parámetro dinámico).
- Nuevos modelos: `BalanceSaldoFilaDto`, `BalanceSaldosDto` (incluye `DigitosCumulativos` para que la UI calcule el índice de columna sin lógica adicional).
- Nuevo método de servicio: `GetBalanceSaldosAsync` + helpers privados `LeerDigitosPlanCuentasAsync`, `CalcTdigitos`, `BuildSaldosWhereClause`, `DigitosPlanCuentas`.

**Nueva página: Posición de IVA (`/contabilidad/posicion-iva`)**
- Comparativa débito fiscal (ventas) vs. crédito fiscal (compras) con saldo a pagar o a favor.
- KPI cards: IVA Ventas, IVA Compras, Saldo (colorea según si es a pagar o a favor).
- Gráfico de barras horizontal con la comparativa del período y gráfico de líneas con evolución del saldo en los últimos 12 meses (independiente del filtro de fechas).
- Tabla de detalle por concepto: IVA Resp. Inscripto, IVA Monotributo, IVA Cons. Final.
- **Resumen por alícuota IVA** (ventas y compras por separado): tabla agrupada por condición IVA (C.F. → EX. → MONO. → R.I.) y alícuota (10,50 / 21,00 / 27,00 / 17,35), con columnas FC / NC / ND / Total y subtotales por grupo.
- Nuevo componente reutilizable `ResumenIvaTable.razor`.

**Correcciones en el cálculo del IVA**
- El total de IVA fiscal (TotalIvaVentas / TotalIvaCompras) ahora suma solo los campos fiscales: `IVA_RESP_INSC + IVA_MONOTRIBUTO + IVA_CONS_FINAL`. Los campos `IVA_21`, `IVA_105`, `IVA_27`, `IVA_1735` son informativos para el resumen por alícuota y **no** se incluyen en el total de posición (evita doble conteo).
- La misma corrección aplica a la consulta de evolución mensual del saldo.

**Corrección en la consulta de resumen por alícuota**
- La SQL que genera el resumen por alícuota fue reescrita con un CTE (`WITH base AS (...)`): los parámetros `@FechaDesde`, `@FechaHastaExclusive`, `@Sucursal` se referencian una sola vez. La versión anterior con `UNION ALL` repetía cada parámetro 4 veces causando errores de conexión (Error 64, Class 20) en SQL Server.
- La clasificación por tipo de comprobante en el pivot fue corregida: la columna FC captura ahora todos los TC que **no** sean NC ni ND (`NOT IN ('NC','ND')`). Antes se usaba `LEFT(TC,2)='FC'`, lo que excluía `LIQC` y `LT` (sus importes se perdían silenciosamente en las tres columnas).

---

## [1.5.0] — 2026-04-27

### Módulo Ventas — Correcciones de datos y nuevas páginas

**Fuente de datos corregida — importes contables**
- Todas las consultas de KPIs, clientes y comprobantes migradas de `V_MV_Cpte.IMPORTE` (campo operativo) a `Libro_VentasConFP.IMPORTE` (total contable con signo). Los valores del dashboard ahora coinciden con el libro IVA ventas.
- Los módulos Rubros, Familias y Artículos ahora leen montos desde `VT_DETALLEIVAPROFORMA.ValorVtaCIVA` y filtran el conjunto de comprobantes válidos via `EXISTS` sobre `Libro_VentasConFP`, garantizando consistencia con el resto del módulo.

**TC NCFP incluido en ventas**
- Eliminado el `INNER JOIN dbo.V_TA_Cpte` con filtro `SISTEMA='VENTAS'` que excluía la Nota de Crédito de Factura Proforma (NCFP) por no estar registrada en esa tabla. Las 16 ocurrencias fueron removidas del servicio; `V_MV_Cpte` y `C_MV_Cpte` son tablas separadas por módulo, por lo que el filtro era innecesario.

**Página Comprobantes rediseñada**
- Reemplazado el listado de comprobantes individuales por una tabla resumen agrupada por tipo de comprobante (TC).
- Columnas: TC · Cantidad · Neto Gravado · No Gravado · IVA 21% · IVA 10,5% · IVA Rec. · Ret. IIBB · Ret. Ganancias · Ret. IVA · **Total**.
- Para FP y NCFP (proforma, no van al libro IVA) sólo se muestra el Total; el resto de columnas queda vacío.
- Fila de totales al pie de la tabla.
- Los IVA 21% e IVA 10,5% se calculan sumando los 4 slots de alícuota de `Libro_VentasConFP` (`LIVA_AlicIVA`, `LIVA_AlicIva2`, `LIVA_AlicIVA3`, `LIVA_AlicIVA4`).

**Gráfico de evolución mensual mejorado**
- Eje Y: valores abreviados con sufijo M/K/B (`$ 107M`, `$ 74K`) — ya no se desbordan ni quedan cortados.
- Eje X: períodos formateados como `Abr '25` en lugar del formato crudo `2025-04`.
- Margen izquierdo del SVG ampliado de 56 a 70 unidades.

**Vistas SQL de compras — TC NCCP incluido**
- `vw_compras_cabecera_dashboard` y `vw_compras_detalle_dashboard` actualizadas para incluir `NCCP` (Nota de Crédito de Compras Proforma) en los `CASE` de signo (`SignoBase = -1`) y en `TipoMovimiento = 'Proforma'`. Ejecutar los scripts en la base para aplicar el cambio.

---

## [1.4.0] — 2026-04-24

### Módulo Costos — Revisión operativa, matching y auditoría

**Actualización de Costos (`/costos`)**
- Nuevo módulo integrado al launcher como aplicación independiente.
- Pantallas base incorporadas: inicio, nueva importación, perfiles, historial y detalle de lote.
- Importación estructurada de archivos `.xlsx`, `.csv` y `.txt`.
- Preselección automática del último perfil utilizado.
- Búsqueda de perfiles por contenido en `Nueva Importación`, ya no limitada al prefijo del combo nativo.

**Perfiles de importación**
- ABM web completo sobre `V_Ta_InterODBC`: alta, edición, duplicado y baja.
- Terminología ajustada al uso real: `Proveedor` pasa a representar el nombre del perfil y `CuentaProveedor` el código del proveedor.
- Búsqueda asistida de proveedor contra `VT_PROVEEDORES`, con validación de código existente y posibilidad de guardar sin código.
- Ocultados en la UI los campos no usados por ahora: `Lista`, `Hoja`, `Campos clave`, `Rango desde`, `Rango hasta` y `Política de precios`.

**Lotes y matching**
- Matching inicial por `CuentaProveedor + CodigoArtProveedor` como coincidencia exacta prioritaria.
- Matching secundario por descripción, con score visible y penalización por diferencias de costo no razonables.
- Se incorpora `% variación` entre costo leído y costo actual, con alertas visuales para subas y bajas.
- Revisión de lote rediseñada como grilla operativa única con orden por columnas, checks por fila y aplicación masiva solo sobre filas chequeadas.
- El check solo queda habilitado cuando ya hay artículo elegido.
- Panel de candidatos movido a apertura a demanda en modal grande, evitando tapar columnas de la grilla principal.
- Búsqueda manual de candidatos dentro del proveedor y opción `Plan B` para ampliar fuera del proveedor.
- Scroll horizontal contenido dentro de la misma vista de la tabla y encabezados fijos para mejorar revisión intensiva.

**Aplicación y deshacer**
- Aplicación transaccional de costos a `V_MA_ARTICULOS` solo sobre filas seleccionadas.
- Registro por fila del resultado de aplicación, incluyendo `OK`, `SIN_CAMBIO` y `ERROR`.
- Historial de actualización persistido en `IA_Costos_Actualizacion_Hist`.
- Nueva opción de deshacer aplicación masiva por lote, restaurando el costo anterior cuando existe historial.

**Errores y auditoría**
- Infraestructura transversal de logging y auditoría incorporada para la aplicación.
- Middleware global para capturar excepciones no controladas y registrar incidentes.
- Mensajes amigables al usuario con código de incidente en lugar de pantallas rotas.
- Registro de eventos y errores en archivos `jsonl` dentro de `App_Data/diagnostics`.
- Base preparada para extender este esquema a los próximos módulos.

**Consultas y robustez general**
- Ajustes en `Compras` y `Costos` para no dejar pantallas colgadas cuando faltan vistas o tablas SQL en una base determinada.
- El módulo `Compras` ahora captura mejor errores de inicialización y muestra estado controlado en pantalla.

---

## [1.3.0] — 2026-04-23

### Módulo Costos — Mejoras de UX e importación

**CostosNueva (`/costos/nueva`)**
- Al abrir la página se pre-selecciona automáticamente el último perfil utilizado (consulta `IA_Costos_Importacion_CAB`).
- La barra de progreso ahora es visible (corregido uso de variable CSS `--color-accent` que no existía → cambiado a `--color-primary`).
- Botón renombrado: "Crear corrida" → "Iniciar importación".
- Eliminado el subtítulo técnico con los nombres de las tablas SQL.
- Botón "Abrir lote" movido al área de acciones (junto a "Volver a correr") en lugar del header del panel.

**CostosLoteDetalle (`/costos/lotes/{id}`)**
- Header rediseñado: muestra el nombre del proveedor como título principal y el perfil, archivo y estado como subtítulo.
- Botón "Archivo original" incorporado en el header para descargar el archivo importado directamente desde el lote.
- "Procesar matching" ahora muestra barra de progreso por fila y botón Cancelar durante el proceso.

**CostosService — Matching**
- `ProcessMatchingAsync` acepta `IProgress<int>?` y `CancellationToken`; reporta progreso por fila (5–95%) y respeta cancelación entre filas.
- `NormalizeCode` mejorado: detecta valores numéricos con decimales exportados por Excel (ej. "1915.00") y los normaliza a entero ("1915") antes de comparar con `CodigoArtProveedor`.
- Nuevo método `GetLastUsedProfileIdAsync`: retorna el `IdInterODBC` de la importación más reciente.

---

## [1.2.0] — 2026-04-22

### Ajustado
- Resultado principal de Consultas: el ordenamiento ahora detecta números y fechas para no ordenar importes o cantidades como texto.
- Editor de consultas: ahora permite guardar `CamposGrupo` además de `CamposTotaliza`, y al editar precarga la tabla fuente actual detectada desde `TABLA` o el `FROM` del SQL.
- Exportación a Excel: si el usuario tiene abierto el agrupador dinámico, el botón Excel exporta esa tabla agrupada y no el resultado crudo.
- Tabla agrupada: suma paginación propia para consultas con muchos grupos.

### Agregado

**Módulo Diseñador de Consultas** (`/consultas`):
- Árbol jerárquico organizado por campo CLAVE (2 dígitos por nivel, hasta 6 niveles). Nodos expandibles/colapsables, buscador en tiempo real por nombre o código.
- Ejecución con parámetros tipados (fecha, número, texto). Sin parámetros se ejecuta automáticamente al abrir.
- Resultado interactivo: buscador sobre todas las columnas (Enter/Esc), ordenamiento por encabezado (asc/desc/original), paginación configurable 25/50/100/200 filas.
- **Gráficos SVG** sin dependencias JS: barras agrupadas y líneas Catmull-Rom. Ejes configurables, preselección de columnas desde metadato `CamposTotaliza`, límite 60 puntos.
- **Tabla dinámica de agrupación**: columna agrupadora seleccionable, operación por columna (Suma / Conteo / Promedio / Mínimo / Máximo / excluir), fila de totales globales, ordenamiento numérico. Se recalcula automáticamente al aplicar filtro.
- Editor: campos básicos + metadatos opcionales (tabla, campos a totalizar, orden por defecto). Validación de instrucciones de escritura. Token `<P>` para parámetros en tiempo de ejecución.
- Constructor visual: búsqueda de tablas/vistas en `INFORMATION_SCHEMA`, selector de columnas con resaltado de búsqueda, filtros con soporte `<P>`, ORDER BY configurable. SQL generado es editable.
- Duplicar consulta con pre-llenado completo.
- Exportación a Excel sin límite de filas (ClosedXML tabla formateada) y PDF por impresión del navegador.
- **Gestor de sesiones**: panel lateral para cambiar conexión activa, alta/baja de sesiones, persistencia en `App_Data/sessions.json`. Árbol se recarga al cambiar sesión.

**Ayuda contextual por módulo**: `/ayuda?topic=consultas` carga `manual_consultas.md`; sin parámetro carga `manual_usuario.md`. Sidebar y botón `?` del header apuntan al manual del módulo activo.

**Botón PDF condicional**: visible solo en páginas del módulo `compras`; oculto en Consultas, Ayuda e Inicio.

---

## [1.1.0] — 2026-04-21

### Agregado
- **Launcher de aplicaciones** (`/`): página principal con grilla de módulos estilo Odoo. Compras activo, Ventas / Stock / Caja y Bancos / Contabilidad / Diseñador de consultas con badge "Próximamente".
- **Estructura de rutas por módulo**: todas las páginas de Compras ahora bajo `/compras/*` (ej. `/compras/proveedores`, `/compras/comprobantes`).
- **Sidebar context-aware**: muestra la navegación del módulo activo. Incluye botón "← Aplicaciones" para volver al launcher.
- **Logo de Alfa Gestión** en el sidebar (reemplaza el texto "AG").
- **Modo claro / oscuro**: toggle con ícono luna/sol en el header. Persistido en localStorage. Default: modo claro. Sin flash al cargar.
- **InformesIA** (`/compras/informesia`): consultas en lenguaje natural, dictado por voz, sugerencias rápidas, historial, resultado en nueva pestaña con URL propia.
- **Servicio de Windows**: `instalar_servicio.bat` y `desinstalar_servicio.bat`. El instalador lo instala automáticamente con reinicio automático ante fallas.
- **Installer con .NET 8 Hosting Bundle**: se descarga y cachea en `installer\prereqs\`. Se instala automáticamente durante el setup si falta el runtime.
- **Primera configuración de conexión**: si `appsettings.Production.json` no tiene credenciales, la app las solicita por consola al primer inicio y las guarda.
- **Gráfico LineChart mejorado**: curvas Catmull-Rom (suaves), etiquetas X posicionadas en SVG, máximo 8 etiquetas visibles, puntos solo cuando hay ≤ 20 datos.

### Cambiado
- `AlfaCoreShell` eliminó WebView2: ahora abre el navegador por defecto con `Process.Start` y se cierra.
- Installer limpia credenciales de conexión antes de empaquetar (`appsettings.json` con string vacío, elimina `appsettings.Production.json`).
- `GetCurrentPageTitle()` en MainLayout actualizado para rutas con prefijo `/compras/`.
- Sidebar colapsable: la etiqueta "Próximamente" se oculta al colapsar.
- `BackendLauncher` redirige stdout/stderr a `backend_startup.log` para diagnóstico de errores de inicio.

### Corregido
- Flag `checked` inválido en tarea de Inno Setup → cambiado a `checkedonce`.
- Variable `ErrorCode` no declarada en `InitializeSetup()` de Inno Setup.
- Bloqueo de instalación cuando .NET SDK está instalado pero el registro del runtime difiere del Hosting Bundle: el check ahora es no bloqueante (avisa pero permite continuar).
- Etiquetas SVG `<text>` con atributos en Blazor Razor → resuelto con `MarkupString`.

---

## [1.0.0] — 2026-04-20

### Agregado
- **Módulo Compras completo**:
  - Inicio con KPIs ejecutivos, evolución mensual, top proveedores y rubros
  - Proveedores con ranking, concentración, variación y ficha con drill down
  - Comprobantes con paginación, filtros rápidos, alertas automáticas y modal de detalle
  - Rubros con análisis de concentración, crecimiento/caída y ficha
  - Familias con estructura jerárquica, concentración y ficha
  - Artículos con variación de precio, proveedor principal y ficha
  - Actividad operativa por usuario con series diarias y ficha
- **Filtros globales persistentes** (`FilterStateService`): fecha, proveedor, artículo, rubro, familia, usuario, sucursal, depósito, estado, tipo comprobante
- **Accesos rápidos de fecha**: Hoy, Esta semana, Este mes, Mes anterior, Últimos 3 meses, Año actual
- **Componentes SVG propios**: `LineChart`, `HorizontalBarChart` (sin dependencias externas)
- **Componentes reutilizables**: `KpiCard`, `DataTable`, `DetailCard`, `GlobalFiltersBar`
- **Exportar PDF** de cualquier página (`window.print()` con CSS de impresión)
- **Sidebar colapsable** con estado persistido en localStorage
- **Insights automáticos** en Rubros, Familias y Artículos
- **Alertas operativas** en Comprobantes (importe cero, sin detalle, IVA cero)
- **Vistas SQL**: `vw_compras_cabecera_dashboard`, `vw_compras_detalle_dashboard`
- **Instalador Inno Setup** (`AlfaCore.iss`) con shortcuts, firewall, readme
- **Scripts de publicación**: `publicar_release.bat`, `publicar_instalador.bat`
- **Ayuda / Manual de usuario** con índice, búsqueda y anclas por sección

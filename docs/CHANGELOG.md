# Changelog — Dashboard de Compras - Alfa Gestión

Formato: `[versión] — fecha — descripción`

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
- Ocho strings con caracteres españoles almacenados como secuencias Latin-1 (`Ã¡`, `Ã©`, `Ã³`, `Ã±`, etc.) corregidos a UTF-8 literal.
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

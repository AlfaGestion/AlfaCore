# Reportes comerciales y búsqueda de Configuración

## Cambios y fuentes confirmadas

La versión anterior de ReporteCompras consultaba columnas inexistentes en DISTRIWALTERP: `RAZON_SOCIAL`, `NetoSignado` e `IvaSignado` en `Libro_ComprasConFP`; en cuenta corriente usaba `DEBEHABER`, `IDCOMPROBANTE` y el join `MA_CUENTAS.CUENTA`, que tampoco corresponden a este esquema. Además devolvía todos los asientos, incluso cuentas ajenas a proveedores.

Se verificaron tipos y columnas en `sys.objects`/`sys.columns` y definición de las vistas antes de reescribir la consulta. `Libro_ComprasConFP` y `Libro_VentasConFP` son vistas; `MV_ASIENTOS` es la fuente contable oficial; `VT_CLIENTES` y `VT_PROVEEDORES` son vistas oficiales de terceros. No se alteraron esos objetos.

`ReporteComercialService` normaliza las vistas mediante CTE: razón social desde `CABNOMBRE`, importe desde `IMPORTE`, netos fiscales desde los campos `LIVA_ImpNetoGrav`, no gravado y exento; IVA desde sus cinco componentes registrados. Estos campos ya tienen signo en esta base, incluidas NC: no se invierten otra vez. En proformas el neto usa `IMPORTE_SINIVA` si existe, o las bases registradas; no inventa un neto a partir del total si el origen no lo informa. No deriva IVA como diferencia entre total y neto ni transforma proformas en asientos de libro IVA.

La cuenta corriente utiliza `[DEBE-HABER]`, `[NUMERO ASIENTO]`, `SECUENCIA` y la concatenación sucursal/número/letra. `EXISTS` sobre `CTACODIGO` de la vista de terceros evita incluir cuentas contables ajenas y multiplicar asientos por joins. Ventas: D positivo/H negativo. Compras: H positivo/D negativo. Ambas áreas incluyen apertura, saldo parcial por línea y cierre, agrupados por código y orden cronológico estable usando la PK completa. Las pestañas separadas Resumida y Analítica analizan los comprobantes y sus aplicaciones actuales.

DTO, interfaz y exportador de Compras conservan sus nombres históricos por compatibilidad. Compras y Ventas comparten el panel y la infraestructura; la cuenta corriente ampliada, Saldos y los reportes por aplicaciones reutilizan consultas/tablas parametrizadas por área, con signos y fuentes propios. Los filtros de unidad multiselección, vendedor principal y zona del maestro actual se aplican también a resumen/detalle de Ventas.

## Contexto, permisos e interfaz

El servicio exige sesión autorizada y permiso `D5005` (Compras) o `D60RPTWEB` (Ventas), respetando la política existente de usuarios sin restricciones explícitas. No utiliza conexión global de respaldo. Revalida sesión/usuario al terminar, propaga cancelación y registra errores técnicos por `IAppEventService` en AUX_ERR. El panel cancela y limpia al cambiar sesión.

Se usa el shell AlfaDesign con `ModuleKey`, Smart Search y pie compartido; `compras`, `ventas` y `configuracion` ya están en la lista de módulos gestionados. Los filtros editados se separan de los aplicados, para no paginar/exportar otro conjunto sin generar. Excel recorre las páginas del filtro aplicado en el circuito autorizado y reutiliza la descarga de blobs del sistema. PDF imprime la página visible. El visor mantiene el prefijo SaaS en navegación normal. Con `directo=1` se oculta el enlace al visor de otro módulo; Configuración tampoco ofrece accesos a otros módulos en ese modo.

Nueva ruta `/ventas/reportes`, con prefijo SaaS cuando corresponda. Actualización `2026-10-15-009__ventas_reporte_ventas_menu_web.sql`: guardias, NombreWeb, alta de clave nueva bajo D60, mapeo y permisos explícitos idempotentes. No altera opciones legacy existentes; una colisión con otro proceso detiene la instalación.

ConfiguracionHub mueve la búsqueda al toolbar y agrega filtro por área. El catálogo incorpora accesos a las pestañas de ventas y palabras clave de configuraciones confirmadas. En la pestaña Carga de comprobantes, la búsqueda filtra campos sin perder cambios ni modificar valores.

## Validación y límites

Compras: [cuenta corriente con apertura, Saldos, Resumida y Analítica](reportes-compras-cuenta-corriente.md), incluyendo reglas de signo y fuentes CO verificadas.

Ampliación de cartera: [cuenta corriente, saldos y cobranzas — relevamiento VB6/RPT y plan](reportes-ventas-cuenta-corriente.md). Implementadas cuenta corriente con apertura/acumulados, Saldos, Control de Crédito contable, Cuenta Corriente Resumida (solo deuda positiva) y Analítica con aplicaciones. Resumida/analítica usan VE_CPTES_SALDOS_UNEG y aplicaciones actuales; las fechas filtran emisión. El disponible de crédito usa deuda global al corte y límite actual; la exposición adicional por cheques/remitos sigue pendiente. Las restantes opciones del plan siguen pendientes.

Ver [registro de esta pasada](../gestion/configuracion-reportes-comerciales-avances.md). La comparación automatizada usa las fuentes actuales de DISTRIWALTERP; no certifica la calidad de todos los datos históricos ni corrige las vistas legacy. Se conserva su cobertura histórica; en Ventas, la inclusión de proformas depende del filtro y la autorización descritos a continuación. No hay conversión monetaria ni certificación multimoneda en esta entrega. Ninguna prueba emite comprobantes.

[Manual de usuario](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).


## Jerarquía web del menú

Reporte de Ventas comparte navegación con el dashboard gerencial. La pestaña Inicio apunta a `/ventas-dashboard`, conservando el prefijo de la base; `/ventas` redirige históricamente al POS. Punto de Venta mantiene su navegación independiente y no aparece entre las pestañas del dashboard/reporte.

El instalador declara `ALFACORE_MENU_WEB.PadreClave = D60`, además del padre legacy en TA_MENU. Las instalaciones anteriores se reparan con `2026-10-16-001__ventas_reporte_ventas_padre_menu_web.sql`. [Incidencia confirmada y reparación en las bases 106/153](../gestion/reporte-ventas-menu-2026-10-16.md).

## Filtros de Ventas (25/10/2026)

`CuentaExacta` compara por igualdad tras `LTRIM(RTRIM(...))`; no usa LIKE ni razón social. `Provincia` usa `VT_CLIENTES.PROVINCIA`, confirmado en el relevamiento `ventas-carga-comprobantes/sql-distriwalterp-metadatos.json`, y opciones de `TA_ESTADOS`. Se aplican mediante EXISTS para no multiplicar movimientos y también a cuentas sin movimientos de Saldos.

`IncluirProforma` se activa por defecto en el panel al cargar opciones o limpiar filtros si configuración/usuario lo permiten; desmarcarlo excluye FP, NCFP y CBFP en libros, movimientos, saldos y documentos. También se excluyen referencias a estos tipos en las aplicaciones analíticas, sin recalcular los importes de la vista oficial. La apertura y el disponible se calculan sobre los movimientos incluidos. Compras conserva su comportamiento anterior.

El servicio ofrece la opción únicamente con `UsaFacturaProforma=SI` (VALOR o VALORAUX cuando VALOR está vacío) y `TA_USUARIOS.VerProforma=1` para el usuario/sistema activo. Si falta la columna, no habilita la opción. Revalida en cada consulta y antes de exportar, incluido el resumen ya cargado. No requiere migración ni modifica configuración/permisos. El panel recarga opciones al cambiar de sesión y muestra el resumen de filtros aplicados en resultados, impresión y Excel.

El selector de cliente reutiliza `AlfaLookup`: búsqueda diferida por código/razón social sobre `VT_CLIENTES`, hasta 25 sugerencias con parámetros y escape de comodines. Elegir una sugerencia aplica `CuentaExacta`; editar la búsqueda invalida la selección anterior y generar exige seleccionar o limpiar. Enter/Escape del lookup no se propagan al panel de filtros.

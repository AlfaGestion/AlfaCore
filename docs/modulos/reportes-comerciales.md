# Reportes comerciales y búsqueda de Configuración

## Cambios y fuentes confirmadas

La versión anterior de ReporteCompras consultaba columnas inexistentes en DISTRIWALTERP: `RAZON_SOCIAL`, `NetoSignado` e `IvaSignado` en `Libro_ComprasConFP`; en cuenta corriente usaba `DEBEHABER`, `IDCOMPROBANTE` y el join `MA_CUENTAS.CUENTA`, que tampoco corresponden a este esquema. Además devolvía todos los asientos, incluso cuentas ajenas a proveedores.

Se verificaron tipos y columnas en `sys.objects`/`sys.columns` y definición de las vistas antes de reescribir la consulta. `Libro_ComprasConFP` y `Libro_VentasConFP` son vistas; `MV_ASIENTOS` es la fuente contable oficial; `VT_CLIENTES` y `VT_PROVEEDORES` son vistas oficiales de terceros. No se alteraron esos objetos.

`ReporteComercialService` normaliza las vistas mediante CTE: razón social desde `CABNOMBRE`, importe desde `IMPORTE`, netos fiscales desde los campos `LIVA_ImpNetoGrav`, no gravado y exento; IVA desde sus cinco componentes registrados. Estos campos ya tienen signo en esta base, incluidas NC: no se invierten otra vez. En proformas el neto usa `IMPORTE_SINIVA` si existe, o las bases registradas; no inventa un neto a partir del total si el origen no lo informa. No deriva IVA como diferencia entre total y neto ni transforma proformas en asientos de libro IVA.

La cuenta corriente utiliza `[DEBE-HABER]`, `[NUMERO ASIENTO]`, `SECUENCIA` y la concatenación sucursal/número/letra. `EXISTS` sobre `CTACODIGO` de la vista de terceros evita incluir cuentas contables ajenas y multiplicar asientos por joins. Ventas: D positivo/H negativo. Compras: H positivo/D negativo. El saldo mostrado es el movimiento neto del período filtrado; no es un estado de deuda por aplicación ni incluye apertura.

DTO, interfaz y exportador de Compras conservan sus nombres históricos por compatibilidad. Compras y Ventas comparten el panel y las consultas; no hay copia independiente del módulo.

## Contexto, permisos e interfaz

El servicio exige sesión autorizada y permiso `D5005` (Compras) o `D60RPTWEB` (Ventas), respetando la política existente de usuarios sin restricciones explícitas. No utiliza conexión global de respaldo. Revalida sesión/usuario al terminar, propaga cancelación y registra errores técnicos por `IAppEventService` en AUX_ERR. El panel cancela y limpia al cambiar sesión.

Se usa el shell AlfaDesign con `ModuleKey`, Smart Search y pie compartido; `compras`, `ventas` y `configuracion` ya están en la lista de módulos gestionados. Los filtros editados se separan de los aplicados, para no paginar/exportar otro conjunto sin generar. Excel recorre las páginas del filtro aplicado en el circuito autorizado y reutiliza la descarga de blobs del sistema. PDF imprime la página visible. El visor mantiene el prefijo SaaS en navegación normal. Con `directo=1` se oculta el enlace al visor de otro módulo; Configuración tampoco ofrece accesos a otros módulos en ese modo.

Nueva ruta `/ventas/reportes`, con prefijo SaaS cuando corresponda. Actualización `2026-10-15-006__ventas_reporte_ventas_menu_web.sql`: guardias, NombreWeb, alta de clave nueva bajo D60, mapeo y permisos explícitos idempotentes. No altera opciones legacy existentes; una colisión con otro proceso detiene la instalación.

ConfiguracionHub mueve la búsqueda al toolbar y agrega filtro por área. El catálogo incorpora accesos a las pestañas de ventas y palabras clave de configuraciones confirmadas. En la pestaña Carga de comprobantes, la búsqueda filtra campos sin perder cambios ni modificar valores.

## Validación y límites

Ver [registro de esta pasada](../gestion/configuracion-reportes-comerciales-avances.md). La comparación automatizada usa las fuentes actuales de DISTRIWALTERP; no certifica la calidad de todos los datos históricos ni corrige las vistas legacy. Se conserva su cobertura histórica y la inclusión de proformas. No hay conversión monetaria ni certificación multimoneda en esta entrega. Ninguna prueba emite comprobantes.

[Manual de usuario](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).

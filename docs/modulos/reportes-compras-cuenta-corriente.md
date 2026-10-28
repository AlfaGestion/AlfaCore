# Compras — Cuenta corriente, Saldos, Resumida y Analítica

## Compatibilidad con GENESIS — 28/10/2026

En la base central 97, GENESIS, SQL Server 2008 con compatibilidad 80 rechazaba las cinco pestañas de Detalle, Cuenta corriente, Saldos, Resumida y Analítica. Se confirmaron en `AUX_ERR` los incidentes 7330, 7333, 7335, 7336 y 7337: sintaxis `OFFSET/FETCH`, `THROW` y acumulados con `ROWS UNBOUNDED PRECEDING`.

El servicio compartido conserva la paginación en SQL mediante `ROW_NUMBER`. Los acumulados se calculan antes de paginar mediante CTE recursivas y temporales indexadas por cuenta/orden o documento/orden de aplicación; no se recorre toda la historia por cada renglón. `MAXRECURSION 0` permite cuentas con más de cien movimientos. Las conversiones decimales conservan cuatro decimales durante la suma y al calcular saldos parciales. El control de referencias duplicadas usa `RAISERROR` seguido de `RETURN`, manteniendo el rechazo y el logging centralizado. El autocompletado de clientes compartido con Ventas conserva el límite de 25 resultados con `TOP`.

`ReportesCompatibilidadSqlTests` usa `ALFACORE_REPORTES_SQL` para pruebas de lectura explícitas en una base real: detalle y segunda página, cantidades contra resumen, continuidad y acumulados por renglón, saldos contra asientos H-D, resumida contra la fuente oficial, analítica con aplicaciones y filtros sin resultados. No modifica movimientos ni configuración. El cambio requiere publicar la aplicación; no necesita actualización de esquema ni cambio del nivel de compatibilidad.

Verificación del cambio: tres pruebas aprobadas en GENESIS y las mismas tres en DISTRIWALTERP, sin omisiones. Regresión de Ventas en DISTRIWALTERP: cuatro pruebas de lectura aprobadas (apertura/acumulados, filtros de cartera/unidad, saldos y resumida/analítica). También se ejecutaron las pruebas de interfaz de reportes/Vendedores disponibles sin conexión (cinco aprobadas). Catálogo asistido: 107 rutinas, sin advertencias ni errores.

## Verificación original — 16/10/2026

Pasada del 2026-10-16. Implementación sobre `/compras/reportes`, incluyendo el prefijo SaaS. Única base de verificación SQL: **SERVER-ALFAVB6 / DISTRIWALTERP**, identidad comprobada con @@SERVERNAME/DB_NAME(). No se restauró la base ni se usaron otras bases para pruebas.

## Comportamiento entregado

- Cuenta corriente agrupada por código de proveedor, orden cronológico, apertura anterior a FechaDesde, saldo por línea y cierre. Haber aumenta deuda; Debe la reduce. Incluye proveedores sin movimientos del período cuando mantienen apertura no nula. El acumulado se calcula antes de paginar y conserva la continuidad entre páginas.
- Saldos: una fila por proveedor y saldo a la fecha de corte, incluyendo toda la historia anterior. FechaDesde y TC no recortan el cálculo. Permite incluir saldos cero y proveedores sin movimientos. El corte vacío usa el día actual.
- Cuenta corriente resumida: solo comprobantes con deuda positiva, con emisión, vencimiento, original, aplicado y pendiente. No muestra cancelados, sobreaplicados ni créditos/anticipos sin aplicar como facturas deudoras.
- Analítica: todos los comprobantes acreedores de la fuente, también cancelados y sobreaplicados, y detalle de PG/PGFP, NCC/NCCP y demás aplicaciones. Muestra saldo restante después de cada aplicación. Las fechas filtran emisión y el estado de aplicaciones es el actual, como el circuito NW del VB6; no reconstruye aplicaciones históricas.

Compras conserva Resumen/Detalle e incorpora las tres pestañas nuevas. No incorpora Control de Crédito de clientes. Cuenta/proveedor, fechas, tipo y unidades de negocio se mantienen en el Smart Search existente; la unidad se aplica al documento/movimiento, conservando pagos aplicados desde otras unidades o sin unidad. Los filtros de vendedor/zona del cliente siguen siendo propios de Ventas.

TC en la cuenta corriente cronológica solo oculta líneas, sin quitar pagos del acumulado. En resumida/analítica filtra el documento original, no los tipos de pago/crédito. El rango se conserva al alternar entre esas dos pestañas.

## Fuentes verificadas y referencia VB6

Se inspeccionaron tipos, columnas y definiciones en SQL Server antes de reutilizar las consultas:

| Fuente | Uso confirmado |
|---|---|
| MV_ASIENTOS, tabla | Contabilidad de proveedores: H positivo, D negativo; EsResumen=0. Orden estable con fecha y PK completa. |
| VT_PROVEEDORES, vista | Maestro oficial; CTACODIGO y RAZON_SOCIAL para selección y presentación. |
| CO_CPTES_SALDOS_UNEG, vista | Deuda de compras H de tipos PR/PE, original/PAGO/SALDO, vencimiento y unidad. Agrupa por secuencia y otros campos de asiento. |
| CO_CPTES_SALDOS_TODOS_UNEG, vista | Circuito completo del generador NW, que también incorpora OPG pendientes y créditos. Estas variantes no se mezclan automáticamente con la lista de facturas deudoras solicitada. |
| VE_SALDOAPLICACION, vista | La vista CO también utiliza esta fuente de aplicación, pese al prefijo VE. Se confirmó en su definición, sin deducirlo por nombre. |
| MV_APLICACION, tabla | Detalle de aplicaciones en ambos sentidos, conservando la exclusión de viajes y relaciones operativas de la vista original. |
| Libro_ComprasConFP, vista | Resumen/detalle; UNEGOCIO confirmado para agregar filtro de unidades. |

La carpeta VB6 sigue fallando por autenticación. Se usaron las fuentes ya relevadas del proyecto RP_VentasCompras y los RPT extraídos previamente, más las definiciones SQL vigentes del circuito de Compras. Queda pendiente ejecutar el original con las mismas opciones para comparación visual; no se afirma una certificación completa de todas sus variantes.

## Reutilización, seguridad y exportación

Se generalizó la infraestructura ya implementada de Ventas con un parámetro interno de área y fuentes constantes verificadas. No se copió el módulo. Los nombres históricos de varios archivos/DTO terminados en Ventas se conservan por compatibilidad; ahora la consulta, tabla o exportador compartidos reciben el área cuando corresponde. Los métodos públicos de Compras pasan por EjecutarAsync(false), con permiso D5005, sesión/base activas, revalidación posterior, cancelación y logging AUX_ERR. Ventas conserva D60RPTWEB.

Solo se usan temporales de conexión para calcular/paginar, sin escribir AUX_MAYORES. Las aplicaciones no se multiplican por joins de renglones; referencias ausentes/ambiguas y diferencias de importes se señalan. Las referencias documentales duplicadas abortan con logging para evitar presentar deuda duplicada. No se modifican aplicaciones ni se compensan créditos automáticamente.

Excel de cuenta corriente incluye apertura, movimientos y cierre; Saldos incluye todas las cuentas del filtro y total general; Analítica tiene Comprobantes y Aplicaciones en hojas separadas para no repetir originales. Los encabezados identifican Proveedor. PDF imprime la página visible. Se conserva directo=1 y el aislamiento del visor. Son pestañas de una ruta existente, sin menú, objetos SQL persistentes ni migración nueva.

## Verificación y pendientes

14 pruebas únicas aprobadas, cero fallas y cero omitidas: dos nuevas específicas de Compras más las doce de regresión existentes. Comprobaciones contra asientos (apertura/cierre con signo H-D), continuidad de página, cuenta sin movimientos con apertura, Saldos y saldos cero, filtro de unidad, conciliación de resumida con CO_CPTES_SALDOS_UNEG, documento cancelado con NCC/NCCP, aplicaciones y Excel. El test de interfaz recorre las nuevas pestañas de ambas áreas en 2048/1440/1024 y comprueba la conservación del rango y la separación de borradores/aplicados.

Las pruebas nuevas de Compras son de lectura. La suite incluye los ensayos transaccionales existentes de crédito/aplicaciones de Ventas, revertidos en finally. No hubo emisiones ni cambios comerciales permanentes. Compilación correcta; advertencias existentes de campos/eventos no usados. Catálogo asistido: 99 rutinas, cero advertencias/errores.

Pendientes: aceptación visual/PDF del operador, comparación ejecutando VB6 cuando se recupere la autenticación, casos multimoneda y aperturas/cierres de ejercicio, variantes legacy de OPG/créditos no aplicados y cualquier exigencia de reconstrucción histórica. Se conserva la semántica de importes de las fuentes, sin conversión monetaria.

[Manual](../../src/AlfaCore/Docs/manual_reportes_comerciales.md) · [Avances por pasada](../gestion/configuracion-reportes-comerciales-avances.md).

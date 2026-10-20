# Reportes de Compras y Ventas

Entrá en **Compras → Reportes** o **Ventas → Reporte de Ventas**. Ambas pantallas tienen la misma forma de trabajo y consultan la base activa.

## Buscar y generar

Usá la barra superior para buscar por nombre o cuenta. Abrí el embudo para seleccionar período y tipo de comprobante. Hay rangos habituales y fechas desde/hasta. Presioná **Generar reporte** o Enter para aplicar los filtros.

- **Resumen**: importes del período agrupados por cuenta, neto, IVA, total y participación.
- **Detalle**: comprobantes con fecha, cuenta, motivo, neto, IVA, total y usuario. El botón del ojo abre el visor conservando la base; no se ofrece al entrar en modo de módulo aislado (`directo=1`).
- **Cuenta corriente**: movimientos contables de proveedores en Compras y de clientes en Ventas. En ventas, Debe suma deuda y Haber la resta; en compras ocurre al revés.

En **Ventas**, la cuenta corriente agrupa por código de cliente y muestra saldo inicial, movimientos cronológicos, saldo parcial en cada línea y saldo al cierre. Al cambiar de página, el saldo sigue desde la anterior. Los clientes sin movimientos pero con saldo inicial también aparecen. Un saldo positivo es deuda a cobrar; uno negativo es saldo a favor del cliente. No es todavía un listado de comprobantes pendientes por aplicación.

En el embudo de Ventas podés seleccionar una o varias **unidades de negocio**, **vendedor del cliente** y **zona del cliente**. Vendedor y zona se toman del maestro actual, aunque el comprobante haya sido emitido por otro vendedor. Sin seleccionar unidades se incluyen todas, incluso movimientos sin unidad; también podés seleccionar únicamente los movimientos sin unidad. Los filtros se aplican a Resumen, Detalle y Cuenta corriente; en esta última afectan tanto la apertura como los movimientos. Si filtrás por tipo de comprobante, solo se ocultan líneas: los saldos siguen contemplando los demás tipos, incluidos los pagos.

En **Compras**, la cuenta corriente también agrupa por código de proveedor e incluye saldo inicial, acumulado por línea y cierre. **Haber suma deuda y Debe la resta**. Conserva saldos de proveedores sin movimientos en el período y mantiene el acumulado al cambiar de página. Un saldo positivo es deuda a pagar; uno negativo es saldo a favor de la empresa. El filtro por tipo solo oculta líneas y no recorta los saldos. El embudo permite seleccionar unidades de negocio también en Compras.

## Cuenta corriente resumida y analítica

Estas dos opciones están disponibles tanto en **Compras** como en **Ventas**, agrupadas por código de proveedor o cliente.

En **Cuenta corriente resumida** se muestran únicamente los comprobantes con deuda pendiente positiva. Se agrupan por código de cliente y se informa fecha de emisión, vencimiento, importe original, importe aplicado y pendiente. Los comprobantes cancelados y los sobreaplicados no aparecen en esta vista.

En **Cuenta corriente analítica** también aparecen los comprobantes cancelados. Debajo de cada uno se detallan sus pagos o recibos, notas de crédito y otras aplicaciones, con fecha del documento aplicado, importe y saldo restante después de cada aplicación. Una aplicación puede ser una cancelación por crédito o transferencia, no necesariamente una entrada de dinero. Los saldos negativos se identifican como sobreaplicados.

Ambas opciones usan las **aplicaciones registradas actualmente**, como el reporte VB6. Las fechas del buscador filtran la **emisión de la factura o débito**; no reconstruyen cuánto se debía en esa fecha pasada. Al entrar se consulta toda la historia emitida hasta hoy; podés cambiar el rango. Al pasar entre resumida y analítica se conserva el rango elegido. También se respetan cuenta/proveedor o cliente, tipo de comprobante y unidades de negocio. En Ventas se agregan vendedor/zona actuales del maestro del cliente.

Los créditos y anticipos sin aplicar no se descuentan de una factura hasta que se imputen. Por eso el total de esta vista puede diferir del saldo contable global del cliente. Si el detalle de aplicaciones difiere del saldo registrado, o no se puede confirmar una referencia, el informe lo indica y no ajusta datos automáticamente.

La paginación cuenta **comprobantes completos** y conserva todas sus aplicaciones. Excel descarga todos los comprobantes del filtro; en la analítica agrega una hoja **Aplicaciones** para evitar sumar varias veces el original de una factura. PDF imprime la página visible con su detalle.

## Saldos de Compras/Ventas y control de crédito de Ventas

**Saldos** está disponible en Compras y Ventas, con una fila por código de proveedor o cliente. **Saldos Control de Crédito** está disponible solo en Ventas. Elegí **Al día** en el embudo para indicar la fecha de corte. Incluyen toda la historia anterior a esa fecha, aunque antes hayas elegido otro período o tipo de comprobante en otra pestaña. Si dejás la fecha vacía, usan el día de hoy. Conservan los filtros de cuenta/proveedor o cliente y unidades; en Ventas también vendedor y zona del maestro.

**Saldos** muestra el saldo contable de las unidades seleccionadas. Por defecto oculta saldos cero; activá **Incluir saldos cero** para verlos también, incluso clientes sin movimientos. **Saldos Control de Crédito** incluye además las cuentas con límite asignado aunque su saldo seleccionado sea cero.

El control presenta saldo de la selección, deuda global del cliente, límite actual del maestro y disponible global. Como el límite es único para el cliente, el disponible se calcula como límite menos deuda de **todas** las unidades al corte. Un disponible negativo indica exceso. Con límite cero devuelve cero, siguiendo el reporte original; no significa crédito ilimitado. El límite mostrado es el actual, incluso si consultás una fecha pasada.

Este control mide deuda contable: no agrega exposición adicional por cheques pendientes de acreditación ni remitos sin facturar. No cambia límites ni bloquea la emisión. Excel exporta todas las cuentas del filtro con total general; PDF imprime la página visible.

Los informes incluyen proformas según las vistas del sistema. El IVA corresponde a importes registrados fiscalmente; una proforma no genera libro IVA. Los totales pueden incluir impuestos distintos del IVA. Los importes son los registrados en la fuente; este informe no convierte monedas.

## Páginas y archivos

Para Detalle y Cuenta corriente, usá las flechas superiores y seleccioná 25, 50 o 100 registros por página en el pie. La paginación y Excel usan los filtros del último reporte generado. Cambiar un campo del buscador requiere generar nuevamente para aplicarlo.

**Excel** descarga todos los registros del filtro aplicado, incluyendo Cuenta corriente. **PDF** abre la impresión de los resultados visibles; en una vista paginada imprime la página actual. No crea comprobantes ni cambia movimientos.

Si cambiás de base o cerrás sesión durante una consulta, el resultado se descarta. Volvé a generar en la base activa. Si aparece un error con código, conservalo para soporte.

## Búsqueda de opciones de Configuración

Entrá en **Configuración** y escribí en la barra superior. Podés buscar por nombre, descripción, módulo o palabra clave, sin distinguir mayúsculas ni acentos. El embudo permite limitar por área; Limpiar quita texto y área.

Hay accesos a Carga de comprobantes, Punto de Venta, Facturación electrónica y Padrón ARCA. Podés buscar una clave como **MaxDto** para localizar su sección. Dentro de Configuración → Ventas → Carga de comprobantes también hay búsqueda de campos por nombre, clave, grupo o alcance; filtrar no borra cambios ni guarda parámetros.

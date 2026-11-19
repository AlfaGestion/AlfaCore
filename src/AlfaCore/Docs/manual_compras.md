# Compras

El módulo Compras tiene dos partes:

- **Análisis** (tablero, reportes, proveedores, comprobantes, rubros, familias, artículos, actividad e InformesIA): consultas sobre lo que ya se compró.
- **Operación**: carga de comprobantes de compras, órdenes de pago y consulta de órdenes. Estas pantallas comparten una misma barra: **Inicio de Compras**, **Carga de comprobantes**, **Ver comprobantes**, **Pagos y retenciones**, **Orden de pago** y **Ver órdenes de pago**. La carga de comprobantes tiene su propio manual (**Carga de comprobantes de compras**).

En todas las pantallas de análisis el **período** y los filtros se eligen en el buscador de la barra superior (ícono del embudo). Los rangos rápidos (este mes, mes anterior, últimos 12 meses…) actualizan la pantalla enseguida.

## Inicio de Compras (tablero)

Muestra el **Resumen general** del período: total comprado, cantidad de comprobantes y proveedores, la **Evolución mensual**, el **Top proveedores** y el **Top rubros**. Con **Ver** en cada bloque pasás al detalle.

## Proveedores del período

Ranking de proveedores con lo comprado en el período. Hacé clic en uno para ver su evolución y sus últimos comprobantes. No es el maestro de proveedores: para darlos de alta o editarlos, usá **Archivos → Maestros → Proveedores**.

## Comprobantes de compras

Lista de comprobantes del período con la **Evolución semanal**, los **Tipos predominantes** y el **Top comprobantes**. Los **Filtros rápidos de control** ayudan a encontrar casos a revisar. **Detalle** abre el comprobante con sus ítems.

## Rubros y familias

Lo comprado agrupado por rubro o por familia de artículos. **Detalle** muestra la **Evolución del rubro** y sus **Últimos comprobantes**.

## Artículos comprados

Artículos comprados en el período. **Buscar en el listado** filtra por código, descripción, proveedor o fecha. **Detalle** muestra la **Evolución del precio** y el **Historial** de compras del artículo. **Exportar Excel** descarga el listado. La sección **Cómo se calculan los importes** explica qué incluye cada columna.

## Actividad de compras

Muestra quién carga comprobantes y cuánto: **Comprobantes por día**, **Ítems por día**, **Tipo de carga** (manual, desde recepción, etc.) y **Actividad por usuario**. **Ver detalle** en un usuario muestra su evolución y sus últimos comprobantes.

## InformesIA

Pedí un informe escribiendo la consulta en lenguaje natural, por ejemplo: «compras por proveedor del último trimestre». También podés **Dictar** la consulta o elegir una de las **Sugerencias rápidas**. En **Historial reciente** podés **Cargar** una consulta anterior, **Abrir** su resultado, **Repetir** o **Borrar**. Los informes solo consultan datos: no modifican nada.

## Orden de pago

Registra el pago a un proveedor: qué comprobantes se cancelan, con qué medios y con qué retenciones.

### Registrar una orden de pago

1. En **Compras → Orden de Pago**, buscá el **Proveedor** por código, nombre o CUIT.
2. En **Comprobantes a pagar** marcá las facturas que vas a pagar y el importe de cada una (podés pagar una parte).
3. Si corresponde retención de Ganancias, se calcula sola y se descuenta del neto a pagar. Si cambiás importes, usá **Recalcular**.
4. En **Medios de pago**, pulsá **Agregar medio** y elegí el **Medio** (efectivo, banco o transferencia, cheque propio o cheque de terceros) con su **Importe**. Para cheque propio completá **Serie**, **N° de cheque**, **Fecha de pago** y **Clearing (hs)**. **Quitar** saca un medio.
5. Revisá la **Fecha** y el **Detalle** y pulsá **Grabar orden de pago**.

Si lo pagado supera lo que cancelás, el sistema avisa que **se genera un anticipo**: confirmá con **Grabar con anticipo** o corregí los importes. **Ver PDF** muestra la orden para imprimir o guardar.

### Pago proforma

Si trabajás con comprobantes proforma, la misma pantalla graba un **pago proforma** (PGFP) en lugar de una orden de pago.

## Ver órdenes de pago

Consulta de las órdenes y pagos proforma ya registrados.

1. Elegí el período (**Desde** / **Hasta**), el **Tipo** (OPG orden de pago o PGFP pago proforma) y el **Estado** (activas o anuladas). Podés escribir el **Proveedor / comprobante**.
2. Pulsá **Buscar**.

En cada fila:

- **Ver**: muestra el detalle (comprobantes cancelados, medios y retenciones).
- **PDF**: descarga la orden.
- **Mail**: envía la orden y sus retenciones al proveedor. Marcando varias, se envían de a una; los proveedores sin email válido quedan sin enviar y se informan al terminar.
- **Anular**: pide el **Motivo de la anulación** (obligatorio). Al anular, los comprobantes pagados vuelven a tener saldo.

Para entrar hace falta el permiso **Ver órdenes de pago** (o el de cargar órdenes de pago). Para anular hace falta el de cargar órdenes de pago.

## Libro de IVA Compras

Está explicado en el manual de **Gestión contable**, sección Libros de IVA.

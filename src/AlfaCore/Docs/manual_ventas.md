# Ventas

El módulo Ventas tiene:

- **Análisis**: Inicio (tablero), Clientes, Comprobantes, Rubros, Familias, Artículos, Comparativo, Vendedores y Unidades de negocio.
- **Operación**: Carga de comprobantes, Punto de venta, Cobranzas y Reclamo de saldos. Cada una tiene su propio manual en la sección Ventas de la ayuda.
- **Configuración**: la pestaña **Configuración** del módulo.

En las pantallas de análisis el **período** y los filtros se eligen en el buscador de la barra superior (ícono del embudo). Los rangos rápidos actualizan la pantalla enseguida.

## Inicio de Ventas (tablero)

Resumen del período: total vendido, cantidad de comprobantes, la **Evolución mensual** y las ventas por unidad de negocio. Si no hay datos por unidad para el período, se informa con un aviso.

## Clientes del período

Ranking de clientes por lo vendido en el período. Hacé clic en un cliente para ver su evolución y sus comprobantes. No es el maestro de clientes: para darlos de alta o editarlos usá **Archivos → Maestros → Clientes**.

## Comprobantes de ventas

Lista de comprobantes del período con el **Resumen por tipo de comprobante** (facturas, notas de crédito, etc.). Abrí un comprobante para ver su detalle.

## Rubros, familias y artículos

Lo vendido agrupado por rubro, por familia o por artículo (**Artículos más vendidos**). Hacé clic en una fila para ver su evolución.

## Comparativo de ventas y compras

Compara **Ventas vs. Compras por mes**, con la **Evolución ventas**, la **Evolución compras** y el **Detalle mensual**.

## Vendedores y unidades de negocio

Ventas del período agrupadas por vendedor o por unidad de negocio, para comparar el rendimiento de cada uno.

## Reclamo de saldos

Envía a los clientes con deuda el detalle de sus comprobantes pendientes por email y/o WhatsApp. El mensaje cambia según los días de atraso (tramos).

### Elegir a quién reclamar

1. En la pestaña **Clientes**, ajustá los filtros del buscador: **Saldo mínimo**, **Atraso mínimo (días)**, **Tramo** y **Unidad de negocio**.
2. Revisá los indicadores: clientes con saldo, saldo total, vencido, cantidad por tramo de atraso y clientes **sin medio de contacto**.
3. Tildá los clientes a reclamar (o **Seleccionar todos**). **Deseleccionar** limpia la selección.
4. **Ver mensaje** muestra exactamente lo que va a recibir cada cliente. **Historial** muestra los reclamos anteriores.

### Enviar el reclamo

Pulsá **Enviar reclamo** y elegí el **Canal** (email, WhatsApp o ambos). En **Para** podés cambiar el email (varios separados con ;). Para probar sin molestar al cliente, completá **Enviar de prueba a este email** o **a este celular**: las pruebas no se registran.

### Ver lo enviado

En **Enviados** filtrá por fecha (**Desde** / **Hasta**), **Cliente, destino o asunto**, **Canal** y **Estado** y pulsá **Buscar**. **Ver** muestra el mensaje enviado.

### Diferencias mínimas

Lista los comprobantes con saldos chicos (hasta la tolerancia configurada) que no se reclaman ni cuentan para los días de atraso. Sirve para detectar centavos que conviene cancelar.

### Mensaje y configuración

Define los **tramos** de atraso (**Tramo 1: hasta (días)**, **Tramo 2: hasta (días)**), el **Saldo mínimo a reclamar**, la **Tolerancia por comprobante**, los **Días mínimos entre reclamos** (para no reclamar dos veces seguidas), la **Vigencia del enlace al portal**, el **Asunto del email**, el **Número de WhatsApp que envía** y la **Plantilla de reclamo** de WhatsApp.

Para usar esta pantalla hace falta el permiso **Reclamo de saldos**.

## Configuración de ventas

En **Ventas → Configuración** están todos los parámetros de ventas, en solapas. Los cambios se aplican con **Guardar**; **Cancelar** vuelve a lo grabado.

### Carga de comprobantes

Opciones de la carga de comprobantes: precios, IVA, descuentos, proforma y fechas operativas. Usá el buscador de la barra (por ejemplo «precio», «IVA», «descuento») para encontrar una opción.

### Portal del Cliente

Qué ve el cliente en el portal de autogestión (cuenta corriente, comprobantes, pedidos) y cómo ingresa.

### Punto de Venta

Operación del punto de venta: comprobantes que emite, mostrador, medios de pago y proforma. Los puntos de venta, cajas y comprobantes habilitados se administran en **Ventas → Puntos de venta**.

### Facturación electrónica

Habilita la factura electrónica (ARCA). Elegí el **Ambiente** (homologación para pruebas o producción) y qué hacer **si ARCA rechaza o no responde**. En **CUIT general** cargá el CUIT, el punto de venta y subí el **certificado (.crt)** y la **clave privada (.key)**. Si una unidad de negocio factura con su propio CUIT, activala en **Unidades de negocio con factura electrónica propia** y subí su certificado. En una base de prueba siempre se usa homologación.

### Padrón ARCA

Certificado para consultar el padrón de ARCA por CUIT (se usa para dar de alta clientes y proveedores con sus datos fiscales).

### Mail de facturación

Asunto y texto del email con el que se envían los comprobantes. **Mail de prueba** manda un ejemplo a tu casilla.

### Mercado Pago Point

Conexión con las terminales físicas de Mercado Pago: **Medios de pago habilitados para Point**, **Terminales Point por punto de venta**, **Diagnóstico** y **Cobro de prueba**.

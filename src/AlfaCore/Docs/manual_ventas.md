# Ventas

El módulo Ventas tiene:

- **Análisis**: Inicio (tablero), Clientes, Comprobantes, Rubros, Familias, Artículos, Comparativo, Vendedores y Unidades de negocio.
- **Operación**: Carga de comprobantes, Punto de venta, Cobranzas y Reclamo de saldos. Cada una tiene su propio manual en la sección Ventas de la ayuda.
- **Configuración**: la pestaña **Configuración** del módulo.

En las pantallas de análisis, el **período** y los filtros se eligen en el buscador de la barra de arriba: tocá el embudo o la flecha de la derecha del buscador. Los rangos rápidos actualizan la pantalla enseguida.

## Inicio de Ventas (tablero)

Resumen del período: total vendido, cantidad de comprobantes, la **Evolución mensual** y las ventas por unidad de negocio. Arriba del gráfico de evolución, los botones [[bi:bar-chart-line]] **Área**, [[bi:graph-up]] **Línea** y [[bi:bar-chart-fill]] **Barras** cambian el tipo de gráfico. Si no hay datos por unidad para el período, se informa con un aviso.

## Clientes del período

Ranking de clientes por lo vendido en el período. Hacé clic en un cliente para ver su evolución y sus comprobantes. No es el maestro de clientes: para darlos de alta o editarlos usá **Archivos → Maestros → Clientes**.

## Comprobantes de ventas

Lista de comprobantes del período con el **Resumen por tipo de comprobante** (facturas, notas de crédito, etc.). Abrí un comprobante para ver su detalle.

## Rubros, familias y artículos

Lo vendido agrupado por rubro, por familia o por artículo (**Artículos más vendidos**). Hacé clic en una fila para ver su evolución. [[bi:file-earmark-excel]] **Excel** descarga la tabla.

## Comparativo de ventas y compras

Compara **Ventas vs. Compras por mes**, con la **Evolución ventas**, la **Evolución compras** y el **Detalle mensual**.

## Vendedores y unidades de negocio

Ventas del período agrupadas por vendedor o por unidad de negocio, para comparar el rendimiento de cada uno.

## Reclamo de saldos

Envía a los clientes con deuda el detalle de sus comprobantes pendientes por email y/o WhatsApp. El mensaje cambia según los días de atraso (tramos).

### Elegir a quién reclamar

Está en **Ventas → Reclamo de saldos**. Tiene las pestañas **Clientes**, **Enviados**, **Diferencias mínimas** y **Mensaje y configuración**.

1. En la pestaña **Clientes**, tocá el embudo [[bi:funnel-fill]] (**Filtros del reclamo**) y ajustá los filtros: **Saldo mínimo**, **Atraso mínimo (días)**, **Tramo** y **Unidad de negocio**.
2. Revisá los indicadores: clientes con saldo, saldo total, vencido, cantidad por tramo de atraso y clientes **sin medio de contacto**.
3. Tildá la casilla de los clientes a reclamar (la casilla del título de la tabla los marca todos). **Deseleccionar**, abajo de la tabla, limpia la selección.
4. En cada cliente, **Ver mensaje** muestra exactamente lo que va a recibir y [[bi:clock-history]] **Historial** muestra los reclamos anteriores.

### Enviar el reclamo

Tocá [[bi:send]] **Enviar reclamo** (el botón azul de arriba a la izquierda; dice cuántos clientes marcaste) y elegí el **Canal** (email, WhatsApp o ambos). En **Para** podés cambiar el email (varios separados con ;). Para probar sin molestar al cliente, completá **Enviar de prueba a este email** o **a este celular**: las pruebas no se registran. Tocá [[bi:send]] para enviar o [[bi:x-circle]] para cancelar.

### Ver lo enviado

En la pestaña **Enviados** filtrá por fecha (**Desde** / **Hasta**), **Cliente, destino o asunto**, **Canal** y **Estado** y tocá [[bi:search]] **Buscar**. **Ver** muestra el mensaje enviado, [[bi:clock-history]] **Historial** los reclamos de ese cliente y [[bi:chat-dots]] **Conversación** abre la respuesta del cliente en Conversaciones.

### Diferencias mínimas

La pestaña **Diferencias mínimas** lista los comprobantes con saldos chicos (hasta la tolerancia configurada) que no se reclaman ni cuentan para los días de atraso. Sirve para detectar centavos que conviene cancelar.

### Mensaje y configuración

En la pestaña **Mensaje y configuración** se definen los **tramos** de atraso (**Tramo 1: hasta (días)**, **Tramo 2: hasta (días)**), el **Saldo mínimo a reclamar**, la **Tolerancia por comprobante**, los **Días mínimos entre reclamos** (para no reclamar dos veces seguidas), la **Vigencia del enlace al portal**, el **Asunto del email**, el **Número de WhatsApp que envía** y la **Plantilla de reclamo** de WhatsApp.

Para usar esta pantalla hace falta el permiso **Reclamo de saldos**.

## Configuración de ventas

En **Ventas → Configuración** están todos los parámetros de ventas, en solapas. Los cambios se aplican con [[bi:floppy]] **Guardar** (arriba a la izquierda); [[bi:x-lg]] **Cancelar** vuelve a lo grabado.

### Carga de comprobantes

Opciones de la carga de comprobantes: precios, IVA, descuentos, proforma y fechas operativas. Usá el buscador de la barra (por ejemplo «precio», «IVA», «descuento») para encontrar una opción.

### Portal del Cliente

Qué ve el cliente en el portal de autogestión (cuenta corriente, comprobantes, pedidos) y cómo ingresa.

## Pedidos del Portal Cliente

El cliente puede consultar sus pedidos registrados y usar [[bi:plus-lg]] **Nuevo pedido** para cargar una Nota de pedido con la pantalla de Carga directa. [[bi:file-earmark-excel]] **Exportar Excel** baja la lista y [[bi:eye]] **Ver comprobante** abre un pedido.

### Nuevo pedido

El cliente autenticado queda seleccionado automáticamente. No puede cambiar cliente, tipo de comprobante, unidad, depósito, talonario, lista, clase de precio, fecha ni vendedor. Solo puede agregar artículos, modificar cantidades y escribir observaciones. **Validar** revisa la carga y **Grabar** registra el pedido.

### Editar un pedido

En un pedido web con estado **Registrado**, [[bi:pencil]] **Editar** abre la misma pantalla con sus artículos y cantidades. Al confirmar, se registra el nuevo pedido y se anula el anterior.

### Borrar un pedido

En un pedido no anulado, [[bi:trash3]] **Borrar** solicita confirmación y realiza una anulación lógica auditada. Los pedidos anulados no muestran acciones de edición ni borrado.

### Punto de Venta

Operación del punto de venta: comprobantes que emite, mostrador, medios de pago y proforma. Los puntos de venta, cajas y comprobantes habilitados se administran en **Ventas → Puntos de venta**.

### Facturación electrónica

Habilita la factura electrónica (ARCA). Elegí el **Ambiente** (homologación para pruebas o producción) y qué hacer **si ARCA rechaza o no responde**. En **CUIT general** cargá el CUIT, el punto de venta y subí el **certificado (.crt)** y la **clave privada (.key)** con los botones [[bi:upload]] **Subir .crt** y [[bi:upload]] **Subir .key** (si ya hay uno, dicen **Reemplazar**). Si una unidad de negocio factura con su propio CUIT, activala en **Unidades de negocio con factura electrónica propia** y subí su certificado. En una base de prueba siempre se usa homologación.

### Padrón ARCA

Certificado para consultar el padrón de ARCA por CUIT (se usa para dar de alta clientes y proveedores con sus datos fiscales).

### Mail de facturación

Asunto y texto del email con el que se envían los comprobantes. **Mail de prueba** manda un ejemplo a tu casilla.

### Mercado Pago Point

Conexión con las terminales físicas de Mercado Pago. Tiene sus propias solapas: [[bi:key]] **Token**, [[bi:credit-card-2-front]] **Medios de pago** y [[bi:phone]] **Terminales Point** (con [[bi:arrow-clockwise]] **Actualizar terminales**). [[bi:book]] **Ver instructivo** explica cómo conseguir el token. También están el **Diagnóstico** y el **Cobro de prueba**.

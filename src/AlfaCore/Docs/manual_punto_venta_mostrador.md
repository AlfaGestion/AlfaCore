# Punto de Venta: Mostrador, delivery y take away

El **Mostrador** es la pantalla de venta rápida del Punto de Venta: cargás los artículos, elegís el cliente, cobrás con uno o varios medios de pago y sale el comprobante (factura electrónica con CAE de ARCA, proforma, remito o nota de pedido). Desde el mismo lugar se manejan la caja (apertura, corte X y cierre) y los pedidos de **delivery** y **take away**.

El modo **Salón** (mesas, cocina y carta QR) tiene su propio manual: **Punto de Venta: Salón y carta QR**. Cuando cobrás una mesa, el cobro se hace en esta misma pantalla.

## Antes de empezar

Lo prepara una sola vez el administrador, en **Ventas → Punto de Venta → Agregar o editar puntos**:

1. Tocá **+ Nuevo** y completá **Código** (hasta 4 caracteres, por ejemplo 001) y **Nombre**.
2. Elegí el **Modo**: *Mostrador* para la venta en caja, *Delivery* o *Take Away* para pedidos, *Salón* para mesas.
3. En **Comprobante habitual** elegí con qué comprobante arranca la pantalla (normalmente factura electrónica).
4. En **Sucursal** elegí el punto de venta fiscal dado de alta en ARCA. No puede repetirse entre dos puntos de venta. Si lo dejás en *Usar configuración general*, se usa la sucursal configurada para el POS.
5. Dejá marcado **Activo** y tocá **Guardar**.

Además conviene revisar:

- **Caja del usuario:** el turno de caja del mostrador usa la caja asignada a cada usuario (en la ficha del usuario). Si no tiene ninguna, usa la caja 1.
- **Medios de pago:** son las cuentas contables marcadas como medio de pago. Si se configuran los *medios más utilizados*, la cobranza muestra solo esos, en ese orden.
- **Consumidor final:** tiene que estar configurada la cuenta de consumidor final; es el cliente con el que arranca cada venta.
- **Correo saliente:** para mandar comprobantes por email tienen que estar cargados los datos del servidor de correo en la configuración.
- **Mercado Pago Point:** si cobrás con lector Point, configurá los lectores de cada punto de venta.

## Elegir el punto de venta

En **Ventas → Punto de Venta** aparecen como tarjetas los puntos de venta activos, con su modo. Tocá el tuyo:

- **Mostrador** abre la pantalla de venta.
- **Salón** abre el plano de mesas.
- **Delivery / Take Away** abre el tablero de pedidos.

El sistema recuerda el último punto de venta que usaste en ese equipo.

Arriba, en la pantalla de venta, ves el punto de venta, la sucursal, tu usuario, el comprobante elegido y los datos del cliente (documento, condición de IVA, lista de precios y clase).

## Abrir la caja

Para cobrar tiene que haber un **turno de caja abierto** para tu caja en ese punto de venta. Si no lo hay, arriba aparece el aviso *No hay un turno abierto…* y el botón **Cobrar** queda deshabilitado.

1. Tocá **Abrir caja** (en el aviso o en el menú ☰).
2. Cargá el **Saldo inicial**: el efectivo con el que arranca la caja. Si preferís, completá el **conteo de billetes y monedas** y el total contado pasa solo al saldo.
3. Si querés, agregá una observación y tocá **Abrir caja**.

Solo puede haber un turno abierto por caja. El saldo inicial no puede ser negativo.

## Buscar y cargar artículos

- **Buscar:** escribí en *Buscar artículos…* y tocá **Buscar** (o Enter). Podés escribir varias palabras: aparecen los artículos que tienen todas, en el código, la descripción o los códigos de barras. Los artículos suspendidos no aparecen.
- **Rubros:** con *Todos los rubros* filtrás el catálogo por rubro.
- **Agregar:** tocá la tarjeta del artículo. Cada toque suma una unidad.
- Al bajar se van cargando más artículos.

Cada tarjeta muestra el precio según la lista y la clase de precio del cliente. Si el artículo está en oferta vigente, se ve el precio anterior tachado y el de oferta. Las etiquetas *Pesable* y *Sin stock* avisan esas situaciones (se puede vender igual).

## Lector de códigos y cantidades

El campo **Código o código de barras** (junto al carrito) es para el lector o para tipear el código y apretar Enter. El cursor vuelve solo a ese campo después de cada acción, así que podés escanear un producto tras otro.

- Busca por código exacto del artículo o por cualquiera de sus códigos de barras.
- Para cargar varias unidades de una vez escribí **cantidad*código**, por ejemplo `3*7791728001723`.
- Para cargar una cantidad negativa (por ejemplo en una nota de crédito) escribí `-3*código`. Si hay clave de cancelación configurada, te la pide.

## Artículos sin precio o manuales

- Si un artículo tiene precio 0, al agregarlo se abre **Ingresar precio** para que cargues el precio de esa venta.
- Los códigos especiales `*`, `***` y `****` abren **Cargar artículo manual**: escribís la descripción y el precio unitario. Sirve para vender algo que no está en el maestro.

## Modificar el carrito

- Con **−** y **+** de cada línea cambiás la cantidad; si llega a cero, la línea se quita. El ícono de papelera la elimina.
- Tocando la línea se abre el detalle: ahí podés escribir la cantidad exacta (admite decimales, por ejemplo para pesables) o eliminarla.
- **Vaciar carrito** (menú ☰ o Ctrl+B) borra todos los artículos y los pagos cargados, previa confirmación.

Si se cierra el navegador o se corta la conexión en medio de una venta, al volver a entrar el sistema pregunta si querés **recuperar el comprobante pendiente** con sus artículos. Los pagos no se recuperan: hay que cargarlos de nuevo.

En el celular la pantalla cambia: primero ves los artículos con − / + en cada tarjeta y abajo **Ver carrito**; desde el carrito tocás **Cobrar**.

## Elegir el cliente

Cada venta arranca con **Consumidor final**. Para cambiarlo tocá el botón del cliente, debajo del carrito:

- En **Buscar cliente** escribí código, CUIT, nombre o teléfono. Con las flechas ↑ ↓ te movés y con Enter lo elegís.
- La **x** del botón vuelve a consumidor final.

Al cambiar de cliente se recalculan los precios del carrito con su lista y clase de precio. La letra de la factura (A o B) la decide el sistema según la condición de IVA del cliente; no se elige a mano. Al terminar la venta el cliente vuelve solo a consumidor final.

## Cargar un cliente nuevo

En la pestaña **Nuevo cliente** (o con **Ctrl+F**, que abre directo con el cursor en el CUIT):

- **Habitual:** se da de alta como cliente. Obligatorios: CUIT/DNI, nombre o razón social, domicilio, provincia, país y localidad.
- **Eventual:** no se crea un cliente; la venta va a la cuenta de consumidor final, pero los datos que cargues (nombre, documento, etc.) salen impresos en el comprobante y se informan a ARCA.

Con el CUIT de 11 dígitos tocá **Consultar ARCA** (o Enter en el campo): completa razón social, domicilio, provincia, condición de IVA y tipo de documento.

Si el CUIT ya existe, te avisa y podés **Usar este cliente** o **Ignorar y crear nuevo**.

## Tipo de comprobante

En **Comprobante:**, arriba, elegís qué vas a emitir. Arranca con el comprobante habitual del punto de venta:

- **EFC · Factura electrónica** y **NC · Nota de crédito:** se autorizan en ARCA y llevan CAE.
- **FP · Factura proforma** y **NCFP · Nota de crédito proforma:** solo aparecen si la empresa usa proforma y tu usuario tiene permiso.
- **RM · Remito interno** y **NP · Nota de pedido:** el botón pasa a decir **Generar**. Quedan pendientes de facturar: no piden medio de pago ni generan cobranza ni asiento.

Para una nota de crédito cargá los artículos que se devuelven y cobrá como siempre. No hace falta indicar la factura original.

## Cobrar

Tocá **Cobrar** (o **Ctrl+G**). Se abre la **Cobranza** con el total arriba y, a medida que cargás pagos, el **vuelto** o lo que **falta**.

1. Elegí el medio de pago: tocá uno de los botones o escribí su código o descripción y apretá Enter.
2. El **Importe** ya viene con lo que falta; cambialo si te pagan una parte con otro medio. Enter o **Agregar** lo suma a *Pagos aplicados*.
3. Repetí si paga con varios medios (por ejemplo una parte en efectivo y otra con tarjeta).
4. Tocá **ACEPTAR E IMPRIMIR** o **ACEPTAR** (Ctrl+G).

Reglas de la cobranza:

- Lo cobrado tiene que cubrir el total.
- Solo el **efectivo** puede superar el total y dar vuelto. Con otros medios no podés pasarte de lo pendiente.
- Si no agregaste ningún pago, **ACEPTAR** usa el medio y el importe que están en pantalla.
- Las proformas pueden estar limitadas a cobrarse solo en efectivo, según la configuración.
- Para quitar un pago tocá la **×** de la línea. Si hay clave de cancelación configurada, te la pide (también para cancelar la cobranza o salir del punto de venta).

Mientras graba, el botón dice *Grabando…*: no lo vuelvas a tocar.

## Mercado Pago Point

Si el medio de pago elegido es el lector **Mercado Pago Point**, al aceptar el cobro se envía al lector:

1. Si el punto de venta tiene más de un lector, elegí cuál usar.
2. Aparece *Cobrando con Mercado Pago*: el cliente paga en el lector.
3. Cuando el pago cubre el total, la venta se graba sola.

Si el cliente pagó en cuotas con recargo, la diferencia aparece como **Recargo Point** y se agrega a la factura. Si el pago se cancela o falla, podés **Reintentar** o **Cancelar**. El importe mínimo es de $15.

Si el pago quedó aprobado en Mercado Pago pero la venta no se pudo grabar, la cobranza no se puede cerrar: tocá **Aceptar** para reintentar solo la grabación (no se vuelve a cobrar).

## Factura electrónica (ARCA)

Al aceptar una factura o nota de crédito electrónica aparece el panel de **ARCA** con los pasos: numeración, grabación, envío a ARCA para obtener el CAE, asiento y cobranza. Indica si se está trabajando en **producción** o en **prueba / homologación**.

**No cierres la ventana ni vuelvas a tocar Aceptar** mientras dure.

Si ARCA no autoriza el comprobante, según la configuración de la empresa:

- La venta no se completa y te muestra el motivo; el comprobante queda grabado sin cobranza para reintentar.
- O la venta se completa con el **CAE pendiente** y se reintenta después (ver **Después de la venta**).

## Después de la venta

Arriba aparece un aviso verde con el comprobante y el total, y se abre el **Recibo**:

- **Imprimir recibo:** imprime el comprobante con el diseño de la empresa. Con **ACEPTAR E IMPRIMIR** se imprime directo.
- **Enviar por email:** viene con el email del cliente; podés cambiarlo y tocar **Enviar**.
- **QR:** el cliente lo escanea con el celular y ve su factura.
- **CAE:** muestra el número y su vencimiento. Si dice *CAE pendiente* o *sin autorización*, **no entregues el comprobante como factura** hasta regularizarlo: tocá **Reintentar CAE**.
- **Nuevo comprobante** deja todo listo para la venta siguiente.

Si el navegador muestra el cuadro de impresión cada vez, se puede configurar el equipo para que imprima directo (Chrome en modo *kiosk printing*); consultá con soporte.

## Reimprimir comprobantes

Menú ☰ → **Reimprimir comprobantes** (o **Ctrl+P**). Elegí el rango de fechas (por defecto hoy) y tocá **Buscar**. Se listan del más reciente al más viejo; al tocar uno se abre el recibo para imprimirlo o mandarlo por email. Las copias salen marcadas como **REIMPRESION**.

## Corte X y cierre de caja

Desde el menú ☰:

- **Corte X:** arqueo parcial, por ejemplo en un cambio de cajero. Muestra el saldo inicial y el **saldo teórico** (saldo inicial más el efectivo cobrado en el turno). Podés contar el efectivo, cargar una observación (por ejemplo *relevo a Juan*) y tocar **Registrar corte X**. Te muestra la diferencia. **No cierra la caja.**
- **Cerrar caja:** al final del turno. Contá el efectivo (con la grilla de billetes o cargando el total) en **Saldo final declarado**; se muestra la **diferencia** contra el saldo teórico. Tocá **Cerrar caja**. Para volver a vender hay que abrir un turno nuevo.
- **Ver saldos por caja:** las cajas abiertas en ese punto de venta, con su usuario, desde cuándo están abiertas y su saldo teórico.

## Ingresos y egresos de caja

Menú ☰ → **Ingresos/Egresos de caja**, para registrar plata que entra o sale de la caja sin ser una venta (un retiro, un pago a un proveedor, un aporte de cambio):

1. Elegí **Ingreso** o **Egreso** y cargá el importe.
2. En **Imputación** elegí la cuenta contra la que va el movimiento.
3. Si querés, escribí el motivo y tocá **Confirmar**.

En **Detalle de caja** ves los movimientos del día: el saldo por cuenta y las listas de ingresos y egresos, con hora y usuario.

## Pedidos de delivery y take away

Los puntos de venta en modo **Delivery** o **Take Away** trabajan desde el **tablero de pedidos**, que se abre al elegir ese punto de venta en el selector:

1. Tocá **+ Nuevo pedido**.
2. Elegí la **modalidad**: *Delivery* (envío) o *Take Away* (retira en el local).
3. Buscá el **cliente** (desde 3 letras, por nombre, CUIT o teléfono).
4. En delivery son obligatorios el **teléfono**, la **dirección** y la **fecha y hora de entrega**. En take away la hora de retiro es opcional (vacía = lo retira ahora).
5. Agregá observaciones si hace falta y tocá **Aceptar y cargar artículos**.

Se abre el Mostrador con un aviso arriba con los datos del pedido. Cargá los artículos y tocá **Guardar pedido**: se guarda sin cobrar y volvés al tablero.

En el tablero cada pedido muestra su estado: *Pendiente*, *En preparación*, *Listo*, *En camino* (solo delivery), *Entregado* o *Anulado*. Desde ahí:

- Cambiás el estado a medida que avanza.
- **Cargar artículos** agrega o cambia lo pedido.
- **Cerrar** abre directamente la cobranza en el Mostrador. Al grabar, el pedido pasa a *Entregado* con su comprobante. Necesita un turno de caja abierto.
- **Cancelar** anula el pedido.

## Informe del punto de venta

En **Ventas → Punto de Venta → Informe punto de venta** ves cuánto se vendió:

- Arriba: total vendido, cantidad de comprobantes y puntos de venta.
- La tabla de ventas por unidad de negocio, con comprobantes, clientes, ticket promedio y total.
- Con la barra de búsqueda elegís el período (hoy, mes actual, mes anterior, últimos 30 días, etc., o un rango de fechas), y filtrás por cliente, usuario, comprobante o unidad. Tocá **Aplicar**.
- Con las opciones agregás el **resumen mensual**, la **venta por productos**, el **detalle por rubro**, el **detalle de cobranzas** y el **detalle de ventas** (hasta 500 comprobantes; tocando uno se abre el comprobante).

## Atajos de teclado

- **Ctrl+F:** cliente nuevo, con el cursor en el CUIT para consultar ARCA.
- **Ctrl+B:** vaciar el carrito.
- **Ctrl+G:** cobrar; con la cobranza abierta, aceptar.
- **Ctrl+P:** reimprimir comprobantes.
- **Esc:** cierra la ventana abierta; sin ventanas abiertas ofrece vaciar el carrito y, con el carrito vacío, salir.

La lista también está en el menú ☰ → **Atajos rápidos**. Desde ese menú, **Instalar Punto de Venta** lo agrega como aplicación en el equipo, y **Salir** vuelve al selector.

## Preguntas frecuentes

**No me deja cobrar.** Falta abrir el turno de caja (ver **Abrir la caja**). El turno es de tu caja en ese punto de venta: si otro usuario tiene otra caja, abre su propio turno.

**No aparece la factura proforma.** Solo se muestra si la empresa usa proforma y tu usuario tiene permiso para hacerlas.

**No sale la letra que esperaba.** La letra la define la condición de IVA del cliente. Revisá en el encabezado que el cliente sea el correcto y su condición de IVA.

**Los precios no coinciden.** Salen de la lista y la clase de precio del cliente elegido; al cambiar de cliente se recalculan. Las ofertas solo valen dentro de sus fechas de vigencia.

**El email no sale.** Falta configurar el correo saliente (servidor, puerto, cuenta y clave). Consultá al administrador.

**ARCA no respondió.** Esperá unos minutos y tocá **Reintentar CAE** en el recibo (o buscalo en **Reimprimir comprobantes**). No entregues el comprobante como factura hasta que tenga CAE.

**Se cortó la conexión con una venta a medio cargar.** Al volver a entrar, aceptá *recuperar el comprobante pendiente* y volvé a cargar los pagos.

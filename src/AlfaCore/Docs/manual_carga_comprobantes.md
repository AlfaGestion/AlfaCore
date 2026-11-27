# Carga de comprobantes

Se entra desde **Ventas → Carga de Comprobantes**. Arriba hay dos pestañas: **Carga de Comprobantes** (para facturar) y **Ver comprobantes** (los ya emitidos).

Hay tres formas de facturar:

- **Desde pedidos o remitos** pendientes: la factura se arma con lo que quedó sin facturar.
- **Comprobante directo**: una factura, nota de crédito, nota de débito o proforma cargada a mano.
- **Facturación automática**: los abonos y conceptos fijos de los clientes.

Si la empresa factura con ARCA en producción, los comprobantes electrónicos son **reales**: piden CAE y tienen validez fiscal. En una base de pruebas se usa homologación y los botones dicen «de prueba».

## Buscar pendientes y filtrar

La pantalla lista los documentos pendientes de facturar (pedidos, remitos o clientes con facturación automática).

- En el buscador de la barra de arriba buscá por cliente, cuenta o número de comprobante.
- Tocá el embudo [[bi:funnel-fill]] (o la flecha de la derecha del buscador) para abrir los filtros:
  - **Origen**: **Pedidos**, **Remitos** o **Facturación automática**.
  - **Período**: rangos rápidos (incluye **Todo**) o **Fecha desde** y **Fecha hasta**. Sin fechas se ve todo lo pendiente.
  - **Comercial**: vendedor y zona del cliente.
  - **Documentos**: unidad de negocio y depósito.
- Tocá [[bi:search]] **Aplicar**. [[bi:eraser]] **Limpiar** saca los filtros. Los filtros en uso quedan como etiquetas al lado del buscador: su [[bi:x]] los quita.

## Facturar pedidos o remitos

1. Filtrá por **Origen** **Pedidos** o **Remitos** y buscá el cliente.
2. Tocá **Cargar** en el documento. Para juntar varios del mismo cliente en una factura, marcá sus casillas y tocá [[bi:check2-square]] **Cargar selección (N)** en la barra de arriba (hasta 30 documentos compatibles).
3. Elegí el **talonario** de la factura.
4. En cada renglón ajustá **A facturar** (cero lo deja afuera). Precio y descuento se pueden cambiar si tenés permiso; **Restablecer** vuelve a los originales.
5. Tocá [[bi:calculator]] **Simular factura** (arriba). Se arma la **Vista previa** con neto, IVA y total, sin grabar nada.
6. Tocá [[bi:cloud-check]] **Verificar electrónica**: revisa el punto de venta y la numeración en ARCA. No emite.
7. Tocá [[bi:check2-square]] **Validar grabación**: controla cuentas, fechas, vencimiento y stock. Tampoco graba.
8. Tocá [[bi:save]] **Emitir factura** y confirmá. Se pide el CAE y se graban la factura, la cuenta corriente, el asiento, el IVA, el stock y la aplicación al pedido o remito.
9. En **…** (más opciones) está [[bi:file-earmark-pdf]] **PDF factura**.

Si facturaste solo una parte, lo demás queda pendiente: **…** → **Nueva factura sobre pendientes** prepara otra. **Cambiar origen** vuelve a la lista.

Si cambiás una cantidad, un precio o el talonario después de simular, hay que simular otra vez.

## Comprobante directo

1. En la barra de arriba, tocá [[bi:plus-lg]] **Nuevo comprobante directo**.
2. En **Datos de la factura**, elegí:
   - **Cliente**: escribí código, nombre o CUIT y elegí con las flechas y Enter. También está [[bi:search]] **Buscar cliente** arriba. Si no existe, [[bi:person-plus]] **Nuevo cliente / CUIT** lo da de alta consultando ARCA. [[bi:person-lines-fill]] **Ver / editar cliente** abre su ficha.
   - **Tipo de comprobante** (factura electrónica, nota de crédito, nota de débito, proforma…).
   - **Unidad de negocio**, **Depósito**, **Talonario**, **Lista de precios**, **Clase de precio**, **Fecha** y **Vendedor**.
3. Cargá los artículos: escribí el **Código del artículo** o tocá [[bi:search]] (**Buscar artículo**, también con la tecla F5). En cada renglón ajustá **Cantidad** (o **Bultos**), **Precio neto** y **Dto. %**. En cada renglón:
   - [[bi:tags]] **Precios de la lista** muestra los precios de ese artículo.
   - [[bi:arrow-counterclockwise]] **Restablecer precio y descuento** vuelve a los de la lista.
   - [[bi:trash]] **Quitar línea** lo saca.
4. Para cobrar algo que no es un artículo, en **Otros conceptos** tocá [[bi:plus-circle]] **Agregar concepto** y completá **Detalle**, **Importe sin IVA** o **No gravado**.
5. Si la condición de venta es de contado, en **Cobranza de contado** tocá [[bi:plus-circle]] **Agregar medio de pago** y cargá el **Medio de pago** y el **Importe**.
6. Escribí las **Observaciones** si hace falta.
7. Tocá [[bi:check2-square]] **Validar** (arriba). Revisá el neto, el IVA, el total y el vencimiento. Todavía no se graba nada.
8. Tocá **Emitir** (o **Grabar proforma**, o **Grabar**) y confirmá. Si es un comprobante electrónico real, la ventana dice **Confirmar comprobante REAL**.
9. Tocá [[bi:file-earmark-pdf]] **PDF** para bajarlo o [[bi:plus-lg]] **Nuevo comprobante** para hacer otro.

Si cambiás cualquier dato después de validar, hay que **Validar** otra vez.

### Precio, descuento y bultos

- **Precio neto** es el precio unitario **sin IVA y antes del descuento**. Solo se puede cambiar si tu usuario tiene permiso (se da en **Usuarios**), y siempre dentro de los límites del vendedor y de la condición de venta. **Validar** controla que la rebaja total no pase el máximo.
- **Bultos**: si cargás bultos, el sistema los convierte a unidades según las equivalencias del artículo. Mientras haya bultos, la **Cantidad** no se puede editar. Si falta la equivalencia, pide revisar el artículo.
- **Lista de precios** y **Clase de precio** vienen del cliente. Si tenés permiso, podés elegir otras: los precios manuales de los renglones se borran y hay que validar de nuevo.

## Notas de crédito y débito

1. En el comprobante directo, elegí el **Tipo de comprobante** **Nota de crédito electrónica** o **Nota de débito electrónica**.
2. Elegí la **Factura asociada** (del mismo cliente y unidad de negocio).
3. En una nota de crédito, agregá los artículos que se devuelven y sus cantidades: se usan los precios de la factura y no se puede acreditar más de lo facturado. Con [[bi:arrow-down-circle]] **Traer conceptos de la factura** se copian sus otros conceptos para ajustar el importe.
4. **Validar** y **Emitir** como en la factura.

La nota de crédito devuelve el stock e invierte el asiento y el IVA. La nota de débito usa los precios actuales del cliente.

## Factura proforma

En el comprobante directo, elegí **Factura proforma**. No pide CAE ni entra al libro de IVA, pero sí genera asiento y mueve stock según el talonario. Después de **Validar**, tocá **Grabar proforma**. El PDF se baja desde la carga o desde el historial.

## Facturación automática

Sirve para los conceptos que se facturan todos los períodos (abonos), cargados en la ficha de cada cliente, solapa **Facturación automática**.

1. En el embudo, elegí **Origen** **Facturación automática** y la **Fecha a facturar**. Se listan los clientes a los que les corresponde factura en esa fecha, según su periodicidad y su última factura.
2. Marcá los clientes.
3. Tocá [[bi:eye]] **Vista previa (N)** para revisar, y después [[bi:send-check]] **Emitir (N)** en la barra de arriba. Confirmá.

En cada cliente: [[bi:file-earmark-pdf]] muestra el PDF previo, [[bi:file-earmark-text]] **Emitir proforma** la emite como proforma (sin CAE) y [[bi:envelope]] manda la factura por mail. [[bi:pencil-square]] **Asunto y mensaje del mail** cambia el texto del mail.

## Historial y recuperación

En la barra de arriba, **…** → [[bi:clock-history]] **Últimas emisiones** (en bases de prueba, **Comprobantes de prueba**) muestra las últimas facturas, notas y proformas. Desde ahí se baja el PDF de las grabadas o se recuperan las pendientes.

Si una emisión da error o un resultado dudoso, **no hagas otra factura**: tocá [[bi:arrow-repeat]] **Recuperar operación**. Consulta el comprobante en ARCA y, si ya tenía CAE, termina de grabarlo con ese mismo CAE. La misma operación nunca se graba dos veces.

Si ARCA rechazó el comprobante, [[bi:pencil]] **Corregir comprobante** conserva los datos para corregirlos y emitir de nuevo.

## Ver comprobantes

En la pestaña **Ver comprobantes** (arriba) se listan los comprobantes de venta emitidos.

**Buscar**

- En el buscador de arriba escribí el cliente (nombre o cuenta) o el número de comprobante: la lista se actualiza mientras escribís.
- En el embudo [[bi:funnel-fill]], **Período**: al tocar un rango rápido la lista se actualiza enseguida. Si escribís las fechas a mano, tocá [[bi:search]] **Aplicar**.
- **Comprobante**: elegí el **Tipo** y si querés **Incluir anulados**.

Las tarjetas muestran la cantidad de comprobantes, el importe sin anulados y cuántos hay en la página. Tocá el título de una columna para ordenar.

**Acciones por comprobante** (a la derecha de cada fila)

- [[bi:eye]] **Ver** (o doble clic en la fila) abre el detalle: artículos, otros conceptos, comprobantes aplicados, asientos e historial.
- [[bi:printer]] **Reimprimir** abre el PDF del comprobante.
- [[bi:envelope]] **Mail** lo envía al cliente. Revisá el asunto y el mensaje y tocá [[bi:send]] **Enviar**.

**Enviar varios por mail**

Marcá las casillas de la izquierda (la del título marca toda la página). Arriba aparece **Enviar por mail (N)**: cada comprobante se envía a los mails de su cliente con el asunto y el mensaje de **Ventas → Configuración → Mail de facturación**. [[bi:x-lg]] **Quitar selección** desmarca todo. Los anulados no se pueden seleccionar.

**Exportar**

[[bi:file-earmark-excel]] **Excel** y [[bi:printer]] **PDF** (en la barra de arriba) exportan **todas las páginas** del filtro aplicado, no solo la visible. [[bi:arrow-clockwise]] **Actualizar** vuelve a consultar con el mismo filtro.

## Configuración

Los parámetros están en **Ventas → Configuración**, solapa **Carga de comprobantes**: precios, IVA, descuentos, proforma y fechas. Por ejemplo:

- **Exigir lista de precios** y **Permitir cambiar clase de precio**.
- **Artículos exentos en columna Exento**: cómo se informan en el libro de IVA.

La factura electrónica (certificado, punto de venta, ambiente) está en la solapa **Facturación electrónica** y el texto del mail en **Mail de facturación**. Los permisos para cambiar precios o descuentos se dan en **Usuarios**. Talonarios y puntos de venta se configuran en **Archivos → Tablas → Tabla de comprobantes**.

## Qué todavía no se puede facturar desde acá

Si el comprobante necesita algo de esta lista, el sistema lo avisa al validar y no deja grabar (se hace desde Alfa Gestión):

- Clientes o artículos con percepciones (en comprobantes electrónicos).
- Artículos con lotes, series o imputaciones contables particulares.
- Impuestos internos sin la cuenta contable configurada.
- Moneda extranjera.
- Mezclar pedidos con remitos, o documentos de clientes distintos.
- Un mismo artículo repetido en varios renglones del pedido (hay que elegir el renglón).

Ante un error, lo que cargaste se conserva para corregirlo.

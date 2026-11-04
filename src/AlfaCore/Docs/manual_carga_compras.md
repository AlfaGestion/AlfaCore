# Carga y consulta de comprobantes de compras

Desde **Compras → Carga de comprobantes de compras** se cargan facturas, notas de crédito y notas de débito A/C en pesos, con artículos y otros conceptos. Se carga directamente, como en el legacy: el total y el asiento se calculan mientras cargás y se confirma al registrar. La pantalla tiene dos solapas, igual que Carga de Comprobantes de Ventas: **Carga de comprobantes** y **Ver comprobantes** (buscar, ver el detalle, facturar una OC o remito, copiar y anular). Los pagos no se registran en esta pantalla.

Usá **Guardar borrador** antes de cerrar si necesitás continuar después. **Borradores** recupera las cargas guardadas por tu usuario en esta base, incluyendo importes incompletos. Guardar un borrador no registra el comprobante ni reserva numeración. Si otra ventana modificó el mismo borrador, se informa el conflicto para evitar reemplazarlo inadvertidamente.

1. Buscá un proveedor activo por código, razón social o CUIT.
2. Elegí unidad de negocio, comprobante/talonario, motivo y fechas. El número se carga como en el legacy: sucursal (4 dígitos), número (8) y letra; se completan con ceros al salir del campo y la letra debe estar entre las habilitadas del comprobante. Los tipos y letras se leen de la configuración de esa unidad.
3. Para mercadería, ingresá el código y pulsá Enter, completá cantidad y costo y volvé a pulsar Enter para agregar el renglón. F5 abre la búsqueda por código, descripción o código de barras; podés elegir con flechas y Enter. Para servicios o gastos, usá Agregar concepto. Informá neto, tratamiento fiscal y alícuota. Podés elegir una cuenta por concepto; si queda automática se utiliza la imputación del proveedor cuando es elegible y luego la del motivo.
4. IVA informado vacío calcula el impuesto por tasa. Si lo completás, se conserva el importe del documento y se informa cualquier diferencia con el cálculo. No se agrega un ajuste para cuadrar.
5. El **total del documento** se calcula solo a medida que cargás renglones, percepciones e impuestos internos. Editalo únicamente si querés cotejarlo con el total del comprobante del proveedor: si difiere, se informa sin agregar ajustes. **Volver al total calculado** lo recupera. Mientras cargás, el asiento, las cuentas Debe/Haber y las bases fiscales se actualizan solos en la vista de abajo; no hay un paso previo de revisión.
6. Pulsá **Registrar compra**: se valida todo y, si no hay errores, se abre la confirmación con proveedor, número, importe y advertencias; luego **Confirmar registración**. Si hay algo para corregir se muestra el motivo. Al finalizar informa el comprobante y el número de asiento; **Nueva compra** abre otra carga.

Las tasas salen de la configuración de la base, las cuentas de IVA/percepciones del motivo y los valores generales de IVA cuando corresponde. El costo propuesto sale del maestro del artículo y puede corregirse contra el comprobante del proveedor; no es un precio de venta. Elegí el depósito y revisá el stock informativo de esa unidad. El total calculado se muestra durante la carga; Usar este total completa el control del documento, que debés cotejar con el original.

Las cuentas mostradas son imputables, activas, en pesos, excluyendo clientes, proveedores y medios de caja/bancos. Revisá siempre que la imputación automática corresponda al gasto.

## Nuevo proveedor sin salir de la compra

Con permiso de Proveedores aparece **Nuevo proveedor** en la barra superior. Ingresá el CUIT y pulsá **Consultar ARCA / buscar existente**. Si ya está cargado, podés seleccionarlo; si está bloqueado o inactivo, debe revisarse en el maestro.

Para darlo de alta, revisá razón social, condición de IVA, domicilio, condición de compra y motivo habitual. Opcionalmente elegí su imputación contable. Revisá la casilla **Permitir facturas de este proveedor sin orden de compra**; se guarda en su ficha y permite este circuito de servicios/gastos. Podés completar los datos manualmente si no está disponible la consulta ARCA. **Guardar y seleccionar proveedor** crea realmente el proveedor en el maestro, con código automático, y lo selecciona en la compra conservando los conceptos e importes cargados. Cancelar cierra el alta sin crear el proveedor. La compra sigue sin registrar.

## Otros tipos de comprobante

El selector muestra los tipos configurados en Compras para la unidad, incluyendo remitos, órdenes, créditos y débitos cuando existen en esa base. Se indican sus circuitos pendientes; FCC, NCC y NDC A/C permiten servicios y gastos. El crédito invierte el asiento y el signo del libro de IVA; los importes se ingresan positivos. En NCC podés elegir una factura pendiente del mismo proveedor y unidad e informar cuánto aplicar. Al confirmar se vuelve a consultar su saldo. Sin factura asociada, queda a cuenta. Los pagos pertenecen a un circuito propio.

Al cambiar de tipo se conservan los conceptos e importes. Al regresar al tipo anterior se recuperan su letra, punto de venta y número. Para las notas con numeración interna configurada, la carga propone el siguiente número sin reservarlo. Se vuelve a comprobar y se consume únicamente al confirmar; si otra terminal lo utilizó, hay que revisar nuevamente.

## Desde Recepción documental

Abrí una ficha que ya tenga lectura de compra y pulsá **Cargar compra**. Se utiliza el JSON guardado, sin volver a ejecutar IA. El original permanece disponible al lado del editor. Debés confirmar expresamente que la carga corresponde a servicios o gastos sin mercadería.

Se precarga el resumen fiscal; no se convierten artículos a servicios automáticamente. Si hay varias tasas y la lectura no separa sus bases, deben completarse manualmente. Las notas de crédito/débito, moneda extranjera y documentos sin lectura válida no se convierten en facturas. Los impuestos internos se pueden informar expresamente; su cuenta se toma del motivo, la configuración de IVA o CuentaImpuestoInterno, sin inventar códigos. Otros tributos específicos todavía requieren su circuito propio.

La pantalla advierte sobre comprobantes operativos existentes y asientos con el mismo número. Una factura ya existente, incluso importada para IVA o anulada, bloquea una nueva registración para ese proveedor. No se reemplazan registros existentes. Al confirmar una compra desde un documento recibido, se actualizan juntos el comprobante, su asiento y el estado de recepción. La lectura de IA queda aprobada como en el desktop. Si falla la grabación, la recepción no queda registrada.

Con **directo=1** no se ofrecen accesos a otros módulos. Para acceder se requiere el permiso web de Carga de comprobantes de compras; el origen documental requiere además Recepción documental.

## Condiciones para registrar

Se admiten FCC/NCC/NDC A de proveedores responsables inscriptos y C de monotributistas, en pesos. Las facturas directas requieren habilitación del proveedor para facturar sin OC; también se pueden preparar desde un origen comercial controlado. El total del documento es obligatorio y debe coincidir. Si cambian importes o configuración después de la revisión, hay que volver a revisar el asiento. En **Distribución por centros de costos**, asigná las cuentas que lo requieren a uno o más centros imputables. Sus porcentajes deben sumar 100 %, sin repeticiones. Las diferencias de redondeo se asignan al último centro para conservar exactamente el importe del asiento.

Podés informar CAE/CAI de 14 dígitos y vencimiento, o dejar ambos vacíos; se conservan en los datos fiscales del asiento. Esto no realiza una validación de autorización contra ARCA. Para C no hay crédito fiscal; su inclusión en el libro respeta MonotributoAlIVA. Los importes exentos/no gravados se conservan en el neto no gravado según el esquema legacy; Los impuestos internos se conservan en LIVA_EXENTO, conforme al esquema legacy; no generan crédito fiscal.

Un error conserva el borrador. Reintentar la misma confirmación no crea otra factura. Al cambiar de base se cancela la operación pendiente. Desde Recepción documental se guarda la referencia del origen y se marca **Registrado**. El original y el historial se conservan; un documento registrado no puede eliminarse ni volver a procesarse.

Para habilitar el circuito deben aplicarse las actualizaciones de menú y de operaciones de compras (2026-11-03-015, 017, 019, 020 y 021; los menús de los libros de IVA están en 005 y 006).

## Control en Recepción documental

En la búsqueda de la bandeja, **Registración de compras** permite elegir Todas, Pendientes de registrar o Registradas, respetando los demás filtros y la paginación. Pendiente significa que todavía no se registró una compra; puede faltar procesar o corregir la lectura antes de prepararla. Las liquidaciones de tarjetas no forman parte de este filtro.

Las compras generadas desde AlfaCore ofrecen **Ver comprobante** en el listado y en la ficha. Se abre dentro de Recepción documental, también en modo directo. Con **Ver asiento** se muestran todos sus renglones, incluida la deuda del proveedor. La consulta requiere permisos de Compras y de la unidad de negocio. Las aprobaciones históricas informadas por el desktop se reconocen como registradas; el acceso al comprobante de este circuito requiere el vínculo de compra web guardado.

Al volver de registrar una compra, recargá la bandeja o reabrí la ficha para ver el estado actualizado. La actualización 010 (alias de 004) completa los estados de documentos que ya habían generado una compra web en la etapa anterior, sin modificar sus facturas o asientos.

## Consulta, anulación y corrección

La solapa **Ver comprobantes** permite buscar por fechas, proveedor/comprobante, tipo y estado. Incluye compras legacy y web de las unidades autorizadas. **Ver completo** muestra cabecera, artículos, conceptos, historial y todos los renglones del asiento. Si hay más de 500 resultados se pide acotar la búsqueda.

Las compras web ofrecen **Copiar**. Se conservan los datos e importes pero se limpia el número y el vínculo documental: es una carga nueva para revisar, no una modificación del original.

**Anular** requiere motivo y solo se habilita para compras de este circuito con control del asiento original. Se impide si hubo cambios contables externos, cierres, contabilización, conciliación, pagos, aplicaciones o mercadería. Como en el legacy de Compras, retira el asiento y sus datos del libro en una transacción. Conserva cabecera anulada, conceptos, original recibido y un snapshot de asiento/centros con usuario, fecha y motivo. No reutiliza automáticamente el número de una compra anulada. La corrección integral del mismo documento/identidad sigue en el circuito legacy; copiar sirve para otra carga, no para reemplazar una factura ya registrada. Las compras históricas sin huella de control deben anularse desde su circuito legacy.

## Facturar mercadería desde un origen

En **Ver comprobantes**, una OC o remito de compra activo ofrece **Facturar**. Se precargan sus artículos con cantidades pendientes de facturar. Podés reducir las cantidades o quitar artículos para facturar una parte; el saldo queda disponible para otra factura. Completá identificación del proveedor, fecha, importes y total de la factura y revisá el asiento.

Esta modalidad admite un único origen con artículos de unidad simple, completo o parcial. Al guardar se vuelve a comprobar su saldo y no se permite superarlo. Desde OC se registra únicamente la cantidad facturada en stock; desde remito se comprueba el stock existente y no se repite el ingreso. Se guarda la aplicación comercial y se finaliza el origen solo cuando no quedan cantidades pendientes. En una OC con artículos pendientes podés tildar **Dar por finalizada la orden de compra**, como la pregunta del legacy: el saldo no facturado deja de estar disponible. No aplica a remitos. Si otra terminal factura el origen, hay que volver a prepararlo. Cuando IR_Oblig exige recepción previa, la OC no se factura directamente desde esta modalidad: se utiliza el circuito de recepción del legacy. El costo de adquisición queda en los renglones del comprobante; no se modifican listas de precios ni el costo del maestro.

Siguen fuera de esta modalidad las recepciones de mercadería parciales desde una OC (remito aplicado a la orden), la reunión de varios orígenes en una factura, series, lotes, talles, equivalencias y artículos con imputación especial. Esos casos muestran una explicación y se resuelven en el legacy. Tampoco se habilitan por aproximación moneda extranjera, letras fuera de A/C ni otros regímenes fiscales.

## Orden de compra y remito directos

En **Carga de comprobantes de compras** elegí el comprobante **OC** o **RMC** para cargarlos sin pasar por una factura. Se cargan solo artículos (sin otros conceptos, percepciones ni centros de costos) y **no generan asiento, libro de IVA ni movimiento en la cuenta corriente**.

- **Orden de compra (OC):** letra X, numerada por el sistema al revisar. Se graba aprobada, con el depósito como destino y el vencimiento como fecha a vencer. No mueve stock. Queda disponible en Ver comprobantes → Facturar.
- **Remito de compra (RMC):** lleva el punto de venta y el número del remito del proveedor (letra R por defecto). Ingresa el stock de los artículos que controlan existencias. Al facturarlo desde Consultar compras no se repite el stock. Si el proveedor requiere orden de compra no se puede cargar un remito directo: debe generarse desde la OC, circuito que todavía no está disponible aquí.

El total del documento es opcional; si lo informás se coteja con el cálculo. El IVA se discrimina por la tasa del artículo salvo proveedores monotributistas. Estos comprobantes no se pueden anular desde AlfaCore; usá el circuito legacy.

## Búsqueda y carga como en Ventas

El proveedor se busca por código, razón social o CUIT, con o sin guiones. Al hacer clic en el campo ya aparece la lista y se filtra mientras escribís: primero lo que empieza igual que lo escrito y luego lo que lo contiene; podés escribir varias palabras en cualquier orden. La selección queda visible como código y nombre. F2 abre una ventana con proveedores activos; ingresá el texto y Enter para buscar. Cambiar el encabezado del sistema no reinicia el formulario ni borra lo que estabas escribiendo.

La grilla de artículos es independiente de Otros conceptos. Podés combinar mercadería con servicios en la misma factura. Cada artículo muestra cantidad, costo unitario neto, IVA, neto e imputación; IVA informado permite conservar el impuesto exacto del original. Cantidad y costo admiten hasta cuatro decimales. No se modifican los costos ni las listas del maestro al guardar.

Una factura o débito registra entrada de stock; un crédito de mercadería registra salida, según el tipo configurado. Los artículos marcados No controla stock se guardan en el detalle sin movimiento de existencias. Los artículos suspendidos no se ofrecen; series, lotes, equivalencias, talles, moneda extranjera e impuestos específicos indican que requieren su circuito especial. Se vuelve a validar el maestro al registrar: si cambió desde la selección hay que buscar el artículo otra vez.

FCC_Sin_OC se exige para facturas directas como en el desktop. Si el proveedor requiere OC se informa al seleccionarlo y debe prepararse desde el origen comercial. Las notas de crédito y débito no requieren esa marca de facturación. Motivo, fechas adicionales y autorización fiscal se pueden desplegar en Datos adicionales para dejar más espacio a la grilla.

## Enviar por mail: órdenes de compra, pagos y retenciones

Es la «impresión diferida» del sistema anterior. Se manda siempre un PDF al mail que el proveedor tiene en su ficha (podés poner varios separados por «;» o escribir otro en «Para»).

- **Órdenes de compra y remitos:** en **Ver comprobantes** cada orden de compra o remito tiene **Mail** (abre el mensaje para revisarlo) y **PDF**. Marcá varias y usá **Enviar por mail** para mandarlas juntas; cada una va a su proveedor.
- **Pagos y retenciones:** es la tercera solapa. En **Documento** elegís qué se ve y qué se manda: el pago u orden de pago, o el certificado de una retención (Ganancias, IVA, Ingresos Brutos o SUSS). Con un certificado elegido solo aparecen los pagos que tienen esa retención. La grilla muestra lo retenido de cada impuesto, el mail del proveedor (marcado en rojo si falta) y el último envío.
- **Reenviar:** si un documento ya se envió, el botón pasa a **Reenviar** y el mensaje muestra cuándo y a quién se mandó.
- Cada envío queda registrado en el mismo historial de mails de Alfa Gestión, con el resultado. Si el servidor rechaza el mail, el motivo se informa y queda registrado.
- La cuenta de correo es la de tu usuario (o la general de la base), igual que en Ventas. Si está cargada la clave `ALFACORE_MAIL_PRUEBA`, todos los mails van a esa dirección y no al proveedor.
- Los textos se pueden cambiar en la configuración: `COMPRAS-MAIL-ASUNTO-OC`, `COMPRAS-MAIL-MENSAJE-OC`, `COMPRAS-MAIL-ASUNTO-PAGO`, `COMPRAS-MAIL-MENSAJE-PAGO`, `COMPRAS-MAIL-ASUNTO-RETENCION` y `COMPRAS-MAIL-MENSAJE-RETENCION`. Se completan las marcas `<NOMBRE>`, `<COMPROBANTE>`, `<FECHA>`, `<IMPORTE>` y `<IMPUESTO>`.

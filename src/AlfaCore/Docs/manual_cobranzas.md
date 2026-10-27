# Cobranzas desde conciliación

## Ayuda y preguntas sobre el módulo

Abrí el icono **?** junto a las notificaciones. En **Guías** podés buscar una tarea; en **Preguntar a IA**, consultar cómo realizarla y abrir las secciones del manual que respaldan la respuesta. El chat explica los procedimientos documentados y no consulta ni modifica movimientos. Al cerrar la ayuda volvés a la operación con sus datos y selecciones.

Esta guía explica la cobranza rápida disponible desde los movimientos bancarios. Para empezar, abrí **Caja y Bancos → Conciliación bancaria → Revisión de movimientos**.

## Antes de cobrar: revisar si el pago ya está registrado

Abrí **Cuenta corriente** desde la fila. Compará los recibos, fechas, importes y medios de pago. **Ver asiento** abre todos los renglones contables y el historial. **Ver comprobante** permite revisar el recibo y sus facturas aplicadas.

Si encontrás el recibo correcto, usá **Marcar como cobrado** desde el detalle del movimiento, elegí el recibo, marcá la confirmación y aceptá. Esto reconoce un cobro existente: no genera otro recibo. Si hay diferencia de importe, necesitás explicar el motivo. Un saldo cero o un aviso de posible duplicado no bastan para reconocer un cobro sin revisarlo.

## Consultar el saldo actual del cliente

La cabecera de **Cobranza rápida** muestra el saldo actual por moneda y la fecha de consulta. Incluye la historia hasta esa fecha y solo las unidades autorizadas, antes de registrar este recibo. Es distinto del pendiente de una factura y del saldo de las facturas seleccionadas. Para refrescar después de otra registración, cerrá y abrí la cobranza.

## Registrar una cobranza

La cabecera de **Cobranza rápida** muestra el cliente, su **saldo actual** y el importe recibido en el banco. El saldo se consulta al abrir, desde los asientos hasta la fecha indicada, con toda la historia anterior y únicamente las unidades autorizadas. Las monedas se presentan separadas. Es el saldo antes de confirmar este cobro y no se limita a las facturas seleccionadas; para refrescarlo después de otra registración, cerrá y abrí nuevamente la cobranza.

La pantalla separa **Facturas a cancelar**, **Banco y retenciones** y **Datos del recibo y comentario**. Este último bloque se despliega para cambiar fecha, talonario o detalle. **Pendiente de factura** corresponde a cada comprobante; el pie muestra cuánto aplicás y cuánto queda pendiente en las facturas seleccionadas.

En un ingreso clasificado como **Cobranza**, pulsá **Cobrar** desde la fila o el detalle. Se necesita un cliente identificado y una cuenta bancaria asociada a la importación.

1. Elegí el comprobante pendiente del cliente. La ventana muestra su saldo y unidad de negocio.
2. Revisá fecha y talonario **CB X**. Se propone el de la unidad de la factura o uno general si no hay uno específico. El número se asigna al confirmar.
   En **Detalle de la cobranza** se propone **Cob. [nombre del cliente]**. Podés cambiarlo por un comentario de hasta 100 caracteres; se guarda como detalle del asiento y comentario del recibo.
3. La cuenta y el importe bancario se completan con el movimiento importado. Si queda una diferencia, se propone la última cuenta de retención usada en una cobranza del mismo cliente y unidad de negocio, por el importe que falta cubrir. Se indica el recibo de referencia. Revisá la propuesta: podés corregir cuenta e importe o **Quitar** la retención si corresponde dejar un pago parcial. Si no hay antecedentes, usá **Agregar retención** y elegí la cuenta. Podés agregar también el número de certificado; no se copia el de la cobranza anterior.
4. Revisá el total y el saldo restante, y pulsá **Confirmar cobranza CB**. Banco más retenciones no puede superar el saldo pendiente. Si es menor, queda un pago parcial.

La cuenta de retención que efectivamente confirmes queda como referencia para la próxima cobranza de ese cliente y unidad, incluso al volver a ingresar al sistema. Cancelar no cambia esa referencia. Si una cobranza posterior no tiene retenciones, se conserva la última conocida. También se consideran los recibos de Alfa Gestión; se excluyen los anulados y las cuentas inactivas.

Ejemplo: transferencia de $97.000 y retención de $3.000 generan una CB por $100.000 aplicada al comprobante. El asiento debita banco y retenciones, y acredita al cliente.

Al confirmar, se muestra el número de CB y el movimiento queda **Cobrado**. No vuelvas a registrar la misma transferencia por otro circuito. Una importación con cobranzas generadas no puede eliminarse desde esta pantalla. Ante error, conservá los datos y reintentá; el sistema controla operaciones repetidas.

Este circuito admite facturas, proformas y notas de débito en pesos, con una o varias facturas por recibo del mismo cliente y unidad. Si no hay cliente identificado, saldo autorizado o talonario válido, se informa el motivo. Los anticipos sin factura pendiente y otros medios como cheques/tarjetas no están incluidos en esta cobranza rápida.

## Pagar varias facturas con un depósito

En **Cobrar**, la sección **Facturas a cancelar** permite marcar varias facturas del mismo cliente y unidad de negocio. Si la conciliación propone **Múltiple**, se precargan los comprobantes que siguen vigentes y con saldo; revisá la selección. Podés agregar o quitar facturas y corregir el importe de cada aplicación. Si la propuesta ya no coincide con los saldos disponibles, elegí manualmente los comprobantes.

El banco conserva el importe del depósito. Podés agregar retenciones; **Total CB** debe coincidir con **Aplicado**, dejando **Diferencia por aplicar** en cero. Al confirmar se emite **un solo recibo CB**, se aplica el importe indicado a cada factura y se registra un solo asiento. Las facturas de distintas unidades requieren recibos separados para respetar sus talonarios. La selección automática no graba nada hasta confirmar.

## Cuando no permite confirmar

- **Falta cliente:** abrí Detalle → Clasificación y cliente, buscá y asigná la cuenta correcta. Con CUIT compartido, compará también las facturas y sus importes.
- **No hay comprobantes pendientes:** revisá si el recibo ya existe en Cuenta corriente. Esta cobranza rápida no genera anticipos sin factura.
- **Falta talonario:** desplegá Datos del recibo y comentario y elegí un talonario CB válido para la unidad.
- **Diferencia por aplicar:** el Total CB (banco más retenciones) debe coincidir con lo aplicado a las facturas. Revisá la selección y los importes.
- **Factura de otra unidad:** requiere un recibo separado y su talonario correspondiente.
- **Posible duplicado:** revisá la cuenta corriente antes de crear otra cobranza.

## Qué significa Cobrado y qué significa Conciliado

**Cobrado** indica que se registró o reconoció el recibo. **Conciliado** indica que se vinculó el movimiento del banco con la contabilidad de la cuenta bancaria. Son controles separados: un cobro puede estar cargado y todavía pendiente de conciliar.

## Cancelar y corregir

Cerrar la ventana antes de confirmar no registra la cobranza ni cambia la última retención recordada. Después de confirmar, conservá el número de CB. No repitas el cobro por otro circuito; si necesitás corregir un recibo ya generado, revisá su comprobante y el procedimiento de corrección autorizado de la empresa.

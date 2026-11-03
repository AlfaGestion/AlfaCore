# Preparación de compras de servicios y gastos

Desde **Compras → Preparar compra** se puede preparar una factura A o C en pesos, revisar sus bases e impuestos y consultar el asiento propuesto. La acción Registrar compra graba la factura, el asiento y sus datos fiscales juntos. No afecta stock, no genera pagos ni reserva numeración del proveedor. El borrador de la pantalla no se guarda al salir.

1. Buscá un proveedor activo por código, razón social o CUIT.
2. Elegí unidad de negocio, comprobante/talonario, motivo, letra, punto de venta, número y fechas. Los tipos y letras se leen de la configuración de esa unidad.
3. Agregá conceptos de servicios o gastos. Informá neto, tratamiento fiscal y alícuota. Podés elegir una cuenta por concepto; si queda automática se utiliza la imputación del proveedor cuando es elegible y luego la del motivo.
4. IVA informado vacío calcula el impuesto por tasa. Si lo completás, se conserva el importe del documento y se informa cualquier diferencia con el cálculo. No se agrega un ajuste para cuadrar.
5. Completá percepciones y total del documento y pulsá **Revisar asiento**. Se muestran las cuentas al Debe/Haber, bases fiscales y observaciones. Los errores indican datos que deben corregirse; revisar el asiento todavía no registra la compra.
6. Pulsá **Registrar compra**, verificá proveedor, número, importe y advertencias y luego **Confirmar registración**. Mientras guarda se muestra un aviso. Al finalizar informa el comprobante y el número de asiento; **Nueva compra** abre otro borrador.

Las tasas salen de la configuración de la base, las cuentas de IVA/percepciones del motivo y los valores generales de IVA cuando corresponde. Las cuentas mostradas son imputables, activas, en pesos, excluyendo clientes, proveedores y medios de caja/bancos. Revisá siempre que la imputación automática corresponda al gasto.

## Nuevo proveedor sin salir de la compra

Con permiso de Proveedores aparece **Nuevo proveedor** en la barra superior. Ingresá el CUIT y pulsá **Consultar ARCA / buscar existente**. Si ya está cargado, podés seleccionarlo; si está bloqueado o inactivo, debe revisarse en el maestro.

Para darlo de alta, revisá razón social, condición de IVA, domicilio, condición de compra y motivo habitual. Opcionalmente elegí su imputación contable. Revisá la casilla **Permitir facturas de este proveedor sin orden de compra**; se guarda en su ficha y permite este circuito de servicios/gastos. Podés completar los datos manualmente si no está disponible la consulta ARCA. **Guardar y seleccionar proveedor** crea realmente el proveedor en el maestro, con código automático, y lo selecciona en la compra conservando los conceptos e importes cargados. Cancelar cierra el alta sin crear el proveedor. La compra sigue sin registrar.

## Otros tipos de comprobante

El selector muestra los tipos configurados en Compras para la unidad, incluyendo remitos, órdenes, créditos y débitos cuando existen en esa base. Se indican sus circuitos pendientes; esta etapa registra únicamente FCC A/C de servicios y gastos. Los pagos pertenecen a un circuito propio.

Al cambiar de tipo se conservan los conceptos e importes. Al regresar al tipo anterior se recuperan su letra, punto de venta y número. Para los comprobantes de numeración interna se informa que el número se asignará al registrar; aquí no se consume numeración.

## Desde Recepción documental

Abrí una ficha que ya tenga lectura de compra y pulsá **Preparar compra**. Se utiliza el JSON guardado, sin volver a ejecutar IA. El original permanece disponible al lado del editor. Debés confirmar expresamente que la carga corresponde a servicios o gastos sin mercadería.

Se precarga el resumen fiscal; no se convierten artículos a servicios automáticamente. Si hay varias tasas y la lectura no separa sus bases, deben completarse manualmente. Las notas de crédito/débito, moneda extranjera y documentos sin lectura válida no se convierten en facturas. Los impuestos internos y otros tributos requieren ampliar el circuito; se informa su presencia.

La pantalla advierte sobre comprobantes operativos existentes y asientos con el mismo número. Una factura ya existente, incluso importada para IVA o anulada, bloquea una nueva registración para ese proveedor. No se reemplazan registros existentes. No se modifica el estado de Recepción documental ni se marca aprobado el documento.

Con **directo=1** no se ofrecen accesos a otros módulos. Para acceder se requiere el permiso web de Preparar compra; el origen documental requiere además Recepción documental.

## Condiciones para registrar

Se admiten facturas A de proveedores responsables inscriptos y C de monotributistas, en pesos, habilitados para facturar sin orden de compra. El total del documento es obligatorio y debe coincidir. Si cambian importes o configuración después de la revisión, hay que volver a revisar el asiento. Las cuentas que requieren centros de costos necesitan su circuito específico y bloquean esta registración.

Podés informar CAE/CAI de 14 dígitos y vencimiento, o dejar ambos vacíos; se conservan en los datos fiscales del asiento. Esto no realiza una validación de autorización contra ARCA. Para C no hay crédito fiscal; su inclusión en el libro respeta MonotributoAlIVA. Los importes exentos/no gravados se conservan en el neto no gravado según el esquema legacy; LIVA_EXENTO está reservado allí a impuestos internos, fuera de este alcance.

Un error conserva el borrador. Reintentar la misma confirmación no crea otra factura. Al cambiar de base se cancela la operación pendiente. Desde Recepción documental se guarda la referencia del origen para impedir que vuelva a generar una compra desde esta pantalla; la actualización del estado de la bandeja queda para la siguiente etapa.

Para habilitar el circuito deben aplicarse las actualizaciones de menú y de operaciones de compras (2026-11-03-002 y 2026-11-03-003).

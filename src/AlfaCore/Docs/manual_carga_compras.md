# Carga y consulta de comprobantes de compras

Desde **Compras → Carga de comprobantes de compras** se cargan facturas, notas de crédito y notas de débito A o C en pesos, con artículos y otros conceptos, y también órdenes de compra y remitos. El total y el asiento se calculan mientras cargás y se confirman al registrar.

La pantalla tiene tres solapas arriba: **Carga de comprobantes**, **Ver comprobantes** (buscar, ver, facturar una orden de compra o remito, copiar y anular) y **Pagos y retenciones**. Los pagos se hacen en **Compras → Orden de pago**.

## La barra de arriba

- [[bi:save]] **Registrar compra** (el botón azul): valida y graba el comprobante.
- [[bi:search]] **Buscar proveedor (F2)** y [[bi:box]] **Buscar artículo (F5)**.
- [[bi:person-plus]] **Nuevo proveedor**: lo da de alta sin salir de la compra.
- [[bi:save2]] **Guardar borrador** y [[bi:folder]] **Borradores**: para seguir la carga más tarde.
- [[bi:file-earmark-arrow-up]] **Desde Recepción**: carga una factura leída en Recepción documental.
- [[bi:plus]] **Nueva compra**: empieza otra carga.

Algunos están en **…** (más opciones) si la pantalla es angosta.

## Cargar una factura de compra

1. Buscá el **proveedor** por código, razón social o CUIT (con o sin guiones). Al hacer clic en el campo aparece la lista y se filtra mientras escribís. También podés tocar [[bi:search]] **Buscar proveedor (F2)**.
2. Elegí **unidad de negocio**, **comprobante** (talonario), **motivo** y **fechas**.
3. Cargá el número como está en la factura: sucursal (4 dígitos), número (8) y letra. Se completa con ceros al salir del campo.
4. **Mercadería**: escribí el código del artículo y pulsá Enter, completá cantidad y costo y pulsá Enter otra vez para agregar el renglón (o tocá [[bi:plus-lg]] **Agregar artículo**). Con F5 o [[bi:search]] **Buscar artículo · F5** buscás por código, descripción o código de barras. [[bi:trash]] **Quitar artículo** saca un renglón.
5. **Servicios o gastos**: tocá [[bi:plus]] **Agregar concepto** e informá el neto, el tratamiento fiscal y la alícuota. Podés elegir una cuenta para cada concepto; si la dejás en automático se usa la del proveedor o la del motivo. [[bi:trash]] **Quitar** lo saca.
6. **IVA informado**: si lo dejás vacío, el IVA se calcula por la tasa. Si lo completás, se respeta el importe de la factura y se avisa si difiere del cálculo.
7. Revisá el **total del documento**: se calcula solo. Escribí el total de la factura del proveedor solo para cotejarlo; si difiere, se avisa. **Volver al total calculado** lo restablece.
8. Abajo se ve el **asiento** (cuentas Debe y Haber) y las bases fiscales, que se actualizan mientras cargás.
9. Tocá [[bi:save]] **Registrar compra**. Si hay algo para corregir, se muestra el motivo. Si está todo bien, revisá la confirmación (proveedor, número, importe y avisos) y tocá **Confirmar registración**.
10. Al terminar se informa el comprobante y el número de asiento. [[bi:plus]] **Nueva compra** abre otra carga.

**Datos adicionales** (motivo, fechas extra, CAE/CAI) se despliegan para dejar más lugar a la grilla. El CAE o CAI es de 14 dígitos con su vencimiento, o se dejan los dos vacíos.

Si alguna cuenta lo pide, completá **Distribución por centros de costos**: los porcentajes tienen que sumar 100 %.

### Guardar y retomar

Tocá [[bi:save2]] **Guardar borrador** antes de cerrar si necesitás seguir después. [[bi:folder]] **Borradores** abre las cargas guardadas por tu usuario. Un borrador no registra el comprobante ni reserva número.

## Nuevo proveedor sin salir de la compra

1. Tocá [[bi:person-plus]] **Nuevo proveedor** en la barra de arriba (hace falta permiso de Proveedores).
2. Escribí el CUIT y tocá **Consultar ARCA / buscar existente**. Si ya está cargado, elegilo.
3. Revisá razón social, condición de IVA, domicilio, condición de compra y motivo habitual. Si querés, elegí su imputación contable.
4. Marcá **Permitir facturas de este proveedor sin orden de compra** si le vas a cargar facturas de servicios o gastos sin orden.
5. Tocá **Guardar y seleccionar proveedor**: se crea en el maestro y queda elegido en la compra, sin perder lo que ya cargaste.

## Notas de crédito y débito

Elegí el comprobante de nota de crédito o débito. Los importes se cargan en positivo: el sistema invierte el asiento y el libro de IVA. En una nota de crédito podés elegir una factura pendiente del mismo proveedor e informar cuánto aplicar; sin factura, queda a cuenta.

## Alta de artículos nuevos

Si el comprobante trae artículos que no existen (por ejemplo, desde Recepción documental), en cada renglón aparecen [[bi:search]] **Buscar** (para elegir un artículo existente), [[bi:plus-circle]] **Alta** (para crearlo) y [[bi:trash]] **Descartar renglón**. [[bi:magic]] **Alta de todo lo nuevo** crea todos los que faltan juntos.

## Desde Recepción documental

1. Tocá [[bi:file-earmark-arrow-up]] **Desde Recepción** (o, en Recepción documental, abrí la ficha y tocá **Cargar compra**).
2. Se precarga lo que la IA leyó del documento; el original queda a la vista al lado.
3. Confirmá que la carga corresponde a servicios o gastos, revisá los importes y registrá como una factura normal.

Si hay varias tasas de IVA y la lectura no separó las bases, completalas a mano. Las notas de crédito o débito, la moneda extranjera y los documentos sin lectura válida no se cargan por este camino. Al registrar, el documento queda **Registrado** en Recepción documental.

La pantalla avisa si ya existe un comprobante con ese número para el proveedor: en ese caso no deja registrarlo de nuevo.

## Ver comprobantes

En la solapa **Ver comprobantes** (arriba) buscá por fechas, proveedor o comprobante, tipo y estado, y tocá [[bi:search]] **Buscar**. Si hay más de 500 resultados, se pide acotar.

En cada comprobante:

- [[bi:eye]] **Ver**: cabecera, artículos, conceptos, historial y todos los renglones del asiento.
- [[bi:envelope]] **Mail** y [[bi:printer]] **PDF**: en órdenes de compra y remitos.
- [[bi:receipt]] **Facturar**: en órdenes de compra o remitos activos (ver **Facturar una orden de compra o remito**).
- [[bi:copy]] **Copiar**: arma una carga nueva con los mismos datos e importes, sin el número. No modifica el original.
- [[bi:x-circle]] **Anular**: pide el motivo. Solo se puede si la compra se cargó en AlfaCore y no tuvo pagos, aplicaciones, conciliación, cierre ni cambios contables. Las compras viejas se anulan desde Alfa Gestión.

Para mandar varias órdenes o remitos juntos, marcalos y tocá [[bi:envelope]] **Enviar por mail (N)**: cada uno va a su proveedor.

## Facturar una orden de compra o remito

1. En **Ver comprobantes**, buscá la orden de compra o el remito y tocá [[bi:receipt]] **Facturar**.
2. Se cargan sus artículos con las cantidades pendientes. Podés bajar cantidades o quitar artículos para facturar una parte: el resto queda pendiente.
3. Completá el número de la factura, la fecha, los importes y el total, y revisá el asiento.
4. En una orden de compra con artículos pendientes podés marcar **Dar por finalizada la orden de compra**: lo no facturado deja de estar pendiente.
5. Tocá [[bi:save]] **Registrar compra**.

Desde una orden de compra, el stock entra al facturar. Desde un remito no se repite el ingreso de stock.

## Orden de compra y remito directos

En el selector de comprobante elegí **OC** (orden de compra) o **RMC** (remito de compra). Se cargan solo artículos y **no generan asiento, libro de IVA ni cuenta corriente**.

- **Orden de compra**: la numera el sistema. No mueve stock. Después se factura desde **Ver comprobantes → Facturar**.
- **Remito de compra**: lleva el número del remito del proveedor e ingresa el stock. Si el proveedor exige orden de compra, el remito no se puede cargar directo.

Estos comprobantes no se anulan desde AlfaCore.

## Pagos y retenciones

En la solapa **Pagos y retenciones** se mandan por mail las órdenes de pago y los certificados de retención.

1. En **Documento** elegí qué ver y mandar: el pago u orden de pago, o el certificado de una retención (**Ganancias**, **IVA**, **Ingresos Brutos** o **SUSS**).
2. Elegí el período y tocá [[bi:search]] **Buscar**. La grilla muestra lo retenido de cada impuesto, el mail del proveedor (en rojo si falta) y el último envío.
3. En cada fila, [[bi:printer]] **Ver PDF** o [[bi:envelope]] para mandarlo. Si ya se mandó, el botón dice **Reenviar** y muestra cuándo y a quién.
4. Para varios, marcalos y tocá [[bi:envelope]] **Enviar por mail (N)**.

El mail va a la dirección de la ficha del proveedor (podés escribir otra en **Para**). Cada envío queda registrado con su resultado.

## Qué no se carga desde acá

Estos casos se resuelven en Alfa Gestión; el sistema lo avisa:

- Moneda extranjera y letras que no sean A, C, R o X.
- Artículos con series, lotes, talles o equivalencias, o con imputación especial.
- Juntar varias órdenes o remitos en una sola factura.
- Recepciones parciales de mercadería desde una orden de compra.
- Proveedores que exigen orden de compra (la factura se prepara desde la orden).

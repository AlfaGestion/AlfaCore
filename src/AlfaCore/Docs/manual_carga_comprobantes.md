# Carga de Comprobantes — facturación de prueba

## Acceso

En la base indicada por el usuario, la ruta es `/ALFANET/153/ventas/carga-comprobantes`.

Ingresar en **Ventas → Carga de Comprobantes**. La ruta es `/ventas/carga-comprobantes`, también disponible dentro de la dirección de la empresa. Usar la base activa y un usuario interno autorizado.

Esta entrega prepara una factura a partir de **uno o varios pedidos, o uno o varios remitos compatibles**. Permite emitir y grabar un circuito comercial simple en **homologación**, con sus movimientos en la base de pruebas. No admite emisión en producción. Los casos todavía no incorporados se informan al validar; no se omiten sus controles para permitir grabarlos.

## Preparar la factura

1. Abrir el embudo de la barra de búsqueda superior y elegir **Pedidos** o **Remitos** en Origen.
2. Buscar por nombre del cliente, cuenta o número de comprobante. El panel permite combinar fechas desde/hasta, vendedor, zona del cliente, unidad de negocio y depósito. Presionar **Aplicar** o Enter para aplicar; **Limpiar** vuelve a consultar el tipo de origen seleccionado sin restricciones adicionales. Los filtros aplicados se muestran como chips en la barra: la cruz quita ese filtro; el chip de origen abre el panel. Escape o un clic fuera cierra el panel sin aplicar los cambios.
3. Usar el paginador de la barra superior para recorrer los documentos.
4. Presionar **Cargar** en un documento, o marcar varios y elegir **Cargar selección** en la barra superior. Hasta 30 documentos del mismo tipo, cliente, unidad, depósito, moneda, lista, clase, vendedor y condiciones compatibles. La selección se conserva entre páginas.
5. Elegir el talonario de factura compatible con la unidad de negocio del origen.
6. Ajustar **A facturar** en cada renglón. Se admiten cantidades fraccionarias; cero excluye un renglón. Cada origen incluido debe conservar al menos una cantidad positiva. Precio neto y descuento son editables según permisos; **Restablecer** recupera sus valores originales. En facturas agrupadas, cada renglón indica su origen.
7. Presionar **Simular factura** en la barra superior. Durante la consulta se indica que la operación está en curso; al terminar aparece **Simulación lista** y se actualiza **Vista previa**, sin grabar la factura comercial.

El listado ofrece únicamente NP/RM abiertos que conservan saldo positivo según la consulta de pendientes del sistema. Un documento parcialmente aplicado puede seguir apareciendo si queda cantidad por facturar. Las fechas vacías consultan todo el historial, incluidos documentos antiguos con saldo; la fecha hasta incluye el día completo. Vendedor corresponde al comprobante y zona a la asignación actual del cliente.

La simulación vuelve a consultar el origen y valida las cantidades contra los pendientes actuales. Si otro operador cambió el documento, puede rechazar una cantidad que antes estaba disponible.

## Interpretar la vista previa

En un origen sin edición, el importe de referencia se proyecta proporcionalmente desde el importe histórico de cada renglón seleccionado. En una selección múltiple o con precios editados se usa la liquidación calculada de la selección. No es todavía una liquidación fiscal definitiva: cambios de precio, descuentos globales, ofertas por cantidad, percepciones y redondeos completos deben resolverse en el motor de emisión.

En documentos compatibles con precios finales se muestra además una **liquidación con precios de origen**: neto gravado, no gravado, IVA por alícuota, impuestos internos y total calculado. Incluye las reglas de redondeo incorporadas desde Alfa Gestión. Si faltan reglas para ese origen, el panel explica los pendientes en lugar de presentar un total fiscal incompleto. Simular no habilita por sí solo la grabación: hay que verificar electrónica y validar el circuito comercial.

El panel indica si el remito ya movió stock, si se prevé una salida al facturar o si una devolución anterior requiere revisión. No mueve stock ni reserva cantidades.

Cambiar una cantidad, precio, descuento o el talonario descarta la simulación anterior para evitar mostrar importes desactualizados. **Cambiar origen** vuelve al listado.

## Límites de esta entrega

- Uno o varios documentos compatibles del mismo tipo, en moneda local. No se mezclan pedidos con remitos ni clientes diferentes.
- Simulación del detalle de artículos. Orígenes con servicios u otros conceptos requieren el circuito completo posterior.
- Reservas, preparación y centros de costo no se incorporan en esta etapa; no se modifican sus datos existentes.
- El circuito separado de autorización de emisión no se utiliza. Los permisos de acceso al módulo se mantienen.
- Si un artículo aparece en varios renglones, su pendiente agregado no se distribuye automáticamente: el renglón no permite cargar cantidades hasta certificar esa distribución.
- Documentos anulados, finalizados, bloqueados, cerrados, complementos o comodatos no se ofrecen como orígenes normales.
- La grabación admite hasta tres alícuotas de IVA y artículos exentos, unidad base, emisor responsable inscripto y cuenta corriente simple. Percepciones, contado/cobranza, límites de crédito, bloqueo por vencidos, reparto, impuestos internos/ITC, lotes/series, cambio de unidad de venta e imputaciones particulares requieren ampliar el circuito antes de grabar.

Ante un error, las cantidades ingresadas se conservan para corregirlas. Al cambiar de usuario o base se descarta el contexto anterior y se debe volver a cargar el módulo, para evitar mezclar documentos de distintas sesiones.

## Emitir y grabar una factura de prueba

1. Cargar un pedido/remito, elegir el talonario electrónico de la unidad y ajustar cantidades. En DISTRIWALTERP, el talonario **10** corresponde al punto **1007** de la unidad **4**.
2. Presionar **Simular factura** y revisar importes y avisos.
3. Presionar **Verificar electrónica**. El resultado muestra ambiente, punto y numeración; no emite.
4. Presionar **Validar grabación**. Comprueba cuentas, fecha, vencimiento, stock y controles del circuito; el ensayo se revierte y no consume el pedido/remito. La vista previa muestra el plan validado.
5. Presionar **Grabar factura de prueba** y revisar la confirmación. **Emitir y grabar** solicita CAE en homologación y registra factura, artículos, cuenta corriente, asiento/IVA, aplicación al origen y stock cuando corresponde. Estos movimientos sí quedan en la base de pruebas.
6. El resultado indica comprobante, asiento, CAE y vencimiento. **PDF factura**, en el menú de más acciones, descarga el documento con la marca de homologación.

Si cambian datos después de validar, el sistema pide volver a revisar la factura antes de solicitar CAE. Una factura parcial deja el resto pendiente; **Nueva factura sobre pendientes**, en más acciones, vuelve a consultar ese saldo para preparar otra operación.

## Historial y recuperación

En el listado, **Comprobantes de prueba** muestra las últimas operaciones de facturas, notas y proformas. Permite descargar el PDF de las grabadas o recuperar las pendientes.

Si una operación informa error o resultado incierto, usar **Recuperar operación**. Conserva la solicitud original: consulta el comprobante en ARCA si hace falta y reutiliza el mismo CAE para completar la grabación local. No emitir otra factura para reemplazar una respuesta desconocida. Un cambio posterior del origen, cliente o configuración puede requerir revisión antes de completar la operación.

La misma operación no se graba dos veces. Volver a descargar el PDF tampoco emite una factura nueva.

## Facturar directamente

1. Abrir **Ventas → Carga de Comprobantes → Nuevo comprobante directo**.
2. Elegir **Factura electrónica**, el cliente, la unidad, el depósito y el talonario. Para la unidad 4 de DISTRIWALTERP, usar el talonario 10 (punto 1007).
3. Buscar artículos por código, descripción o código de barras y ajustar las cantidades. Agregar un artículo ya elegido incrementa su cantidad. Se usan los precios actuales de la lista y clase del cliente.
4. Presionar **Validar carga** y revisar neto, IVA, total y vencimiento. La validación ensaya todos los movimientos y los revierte; todavía no emite.
5. Presionar **Emitir comprobante de prueba**, revisar la confirmación y aceptar **Emitir y grabar**. Se graban factura, artículos, stock y asiento, sin crear un pedido o remito auxiliar.
6. Descargar **PDF** o iniciar **Nuevo comprobante**. Un error después de iniciar la emisión se revisa con **Recuperar operación**; el historial también permite recuperarla al volver a abrir el módulo.

Cambiar cliente, tipo, unidad, talonario, observaciones, cantidades, precio o descuento obliga a validar nuevamente. Ante configuraciones no incorporadas informa el motivo y conserva la carga.

### Editar precio y descuento

En facturas directas, proformas y notas de débito, la grilla muestra **Neto de lista**, **Precio neto** y **Dto. %**. Si tu usuario tiene permiso, podés editar el precio unitario **sin IVA y antes del descuento**, o ingresar un descuento por artículo. Se admiten hasta cuatro decimales; el precio debe ser positivo y el descuento menor a 100 %. Los campos bloqueados indican que la edición no está disponible para tu usuario o circuito.

**Validar carga** vuelve a consultar la lista, los permisos y los límites del vendedor/condición de venta. Controla también la rebaja acumulada: bajar el precio y aplicar otro descuento no permite superar el máximo. Si rechaza el cambio, conserva los valores para corregirlos. Revisá el total validado antes de emitir.

**Restablecer** elimina el precio y descuento manuales del renglón y exige validar otra vez para usar los valores del sistema. Cambiar cliente o tipo de comprobante descarta esas ediciones manuales para revisar el nuevo contexto. La edición no modifica las listas ni los maestros.

NC conserva los precios de la factura original y no admite su edición manual. ND usa los precios actuales del cliente y permite editarlos respetando los mismos permisos y límites.

Si la emisión falla antes de iniciarse, **Recuperar operación** verifica que no haya operación fiscal ni comercial y conserva el borrador para validar nuevamente. Si ARCA rechazó el comprobante, **Corregir comprobante** conserva los datos y prepara otra operación; el rechazo anterior sigue en el historial. Las operaciones aprobadas o inciertas no se corrigen ni reenvían por esta vía.

### Ingresar bultos

En FC, FP y ND, completar **Bultos** permite que el sistema convierta esa cantidad a la unidad de venta del artículo usando sus equivalencias. La columna Cantidad queda bloqueada mientras haya bultos informados; vaciar Bultos permite volver a la cantidad manual. El precio sigue siendo por la unidad de venta, no por bulto.

Ejemplo: un bulto contiene cinco unidades. Ingresar dos bultos liquida diez unidades. **Validar carga** muestra la cantidad convertida, bultos y kilos calculados; revisarlos antes de emitir. Si falta una equivalencia o su coeficiente no es válido, el sistema conserva la carga y pide revisar el maestro. No crea equivalencias automáticamente.

La validación relee las equivalencias: si cambian antes de emitir, se debe validar otra vez. NC conserva las cantidades de la factura asociada y no utiliza la entrada por bultos.

### IVA y artículos exentos

La validación muestra IVA por alícuota y el importe de artículos exentos. Puede incluir hasta tres alícuotas distintas. La opción **Artículos exentos en columna Exento**, en Configuración → Ventas → Carga de comprobantes, conserva la clasificación del libro de IVA del sistema original; no cambia la condición fiscal del artículo. Las percepciones e impuestos no incorporados siguen impidiendo grabar.

## Notas de crédito y débito electrónicas

En la carga directa, elegir **Nota de crédito electrónica** o **Nota de débito electrónica** y seleccionar una **Factura asociada** del mismo cliente y unidad. El selector ofrece facturas comerciales aprobadas en homologación desde este módulo; las facturas históricas de producción no se usan como asociados de prueba.

En DISTRIWALTERP, la unidad 4 usa talonario **7** para NC y **6** para ND, ambos en el punto **1007**. La letra se resuelve según el cliente y debe coincidir con la factura asociada.

Para una NC, agregar los artículos a devolver y sus cantidades. Se conservan precio, descuento, alícuota, costo y equivalencia de la factura original, y se descuentan las cantidades acreditadas previamente. No se permite acreditar más unidades de las facturadas. Si la factura repite un artículo en varios renglones, la NC informa que falta seleccionar su renglón específico; no distribuye automáticamente la devolución. La ND usa precios actuales del cliente. El stock sigue el sentido del talonario; en esta base, la NC devuelve stock y la ND genera salida. La NC invierte el asiento y los importes del libro de IVA.

Validar, revisar y confirmar como en la factura directa. La solicitud electrónica incluye la factura asociada. La recuperación compara también esa asociación, evitando atribuir a la nota un CAE de otro documento.

## Factura proforma

Elegir **Factura proforma**. En esta base corresponde el talonario **0**, letra **X**, sucursal **4**; no utiliza el punto electrónico 1007.

La proforma siempre genera asiento en este módulo y mueve stock según el talonario. **No solicita CAE ni entra al libro de IVA.** Su total suma IVA si así está configurado en la base; cuando no corresponde sumarlo, se toma el precio neto. La validación muestra el resultado antes de grabar.

Después de **Validar carga**, presionar **Grabar proforma** y confirmar. La numeración es local y se graba en la misma transacción que el asiento y los movimientos. Puede descargarse su PDF desde la carga o el historial.

La carga directa, notas y proformas de este incremento admiten artículos simples en moneda local y cuenta corriente, con hasta tres alícuotas, artículos exentos o proforma sin IVA. Ofertas/escalas, descuentos especiales, percepciones, impuestos internos, lotes, series, cambio de unidad de venta y otros circuitos no incorporados se informan antes de grabar.

## Solo probar la autorización fiscal

El menú de más acciones conserva **Solo probar autorización ARCA** para el ensayo fiscal separado. A diferencia de la grabación anterior, no genera movimientos comerciales ni consume el origen.

1. Cargar el pedido/remito y simular una selección compatible.
2. Presionar **Verificar electrónica**: comprueba el emisor, homologación, punto de venta y consulta numeración; no solicita CAE.
3. Presionar **Solo probar autorización ARCA** y leer la confirmación: pide CAE en homologación y guarda un registro de prueba, sin consumir el origen ni generar stock, asientos o aplicaciones comerciales.
4. El resultado muestra número, importe, CAE y vencimiento. Al volver a cargar ese origen se recupera su última prueba.

La acción admite únicamente homologación y el punto global de prueba configurado. Una unidad con emisor propio y otro punto requiere revisar su contexto; no se fuerza el punto global. **Conciliar prueba ARCA** consulta y compara el comprobante antes de recuperar su resultado; no reenvía la solicitud de autorización.


## Configuración y búsqueda de clientes

Entrá en **Configuración General → Ventas → Carga de comprobantes**. Allí están los parámetros de precios, IVA, descuentos, proforma y fechas operativas, con su alcance indicado en cada campo. Guardar modifica solo los campos editados y requiere un administrador central. Cancelar vuelve a leer la configuración guardada. Si alguien cambió los mismos parámetros mientras los editabas, recargá y revisalos antes de volver a guardar.

Las opciones específicas de **Punto de Venta**, **Facturación electrónica** y **Padrón ARCA** siguen en las pestañas del mismo nombre, accesibles desde la nueva subopción. El inventario de claves del programa original es una referencia: algunas funciones siguen pendientes y así se indican. La opción de proforma del POS es distinta de la opción de proforma de Carga de comprobantes.

En carga directa, escribí desde la primera letra en **Cliente** (con las flechas y Enter elegís el resultado): código, nombre o CUIT, con o sin guiones. También podés usar **Buscar cliente** del menú superior. Para darlo de alta, elegí **Nuevo cliente / CUIT**, ingresá el CUIT y consultá ARCA; revisá los datos y guardá el cliente. Es el mismo circuito de consulta y alta de Punto de Venta. Al seleccionarlo vuelve al comprobante; cualquier cambio de cliente requiere volver a validar la carga.

Los permisos individuales para cambiar precios o descuentos se administran en **Usuarios**. Los límites particulares del vendedor y de la condición de venta se mantienen en sus respectivos maestros; el máximo general de esta pestaña no reemplaza esos límites. Talonarios, depósitos y unidades de negocio conservan sus tablas de referencia.

## Elegir lista y clase de precios en la carga directa

**Lista de precios** y **Clase de precio** toman inicialmente los valores del cliente. En factura directa, proforma y nota de débito podés elegir otros valores si tenés permiso. La lista permite también **Maestro de artículos (sin lista)** cuando la configuración no exige lista. Solo se ofrecen listas vigentes; al grabar se vuelve a comprobar su vigencia con la fecha del servidor.

Al cambiar la selección se restablecen los precios y descuentos manuales de las líneas. Presioná **Validar carga** para actualizar sus importes y revisar la lista y clase efectivas en la liquidación antes de emitir. Las notas de crédito conservan los precios de su factura asociada.

En **Configuración → Ventas → Carga de comprobantes → Precios** están **Exigir lista de precios** y **Permitir cambiar clase de precio**. Si el selector de clase está deshabilitado, revisá esa configuración y las clases habilitadas al usuario; el permiso para cambiar lista pertenece también al usuario. No hace falta cambiar estas políticas para seguir facturando con los precios heredados del cliente.

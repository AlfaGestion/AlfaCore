# Cobranzas: base compartida con Conciliación

## Estado

Relevamiento e implementación inicial del 19/10/2026. Cobranza rápida CB desde Conciliación con servicio y editor compartidos para el futuro módulo Cobranzas. Validación en ALFANET2007_PRUEBAS, servidor 10.8.0.31, mediante la conexión de sesión, sin credenciales en código. Ver resultados de pruebas al final de este documento.

## Compatibilidad de códigos con Alfa Gestión (2026-10-21)

`CobranzasService` y `CargaComprobantesGrabador` graban los tipos de comprobante y las cuentas de movimientos sin relleno. La regla alcanza a las cabeceras, asientos, aplicaciones y tipos de origen/impresión. El formato de códigos de maestros permanece con `CodigoPk.Format`; no debe trasladarse a `TC` ni `CUENTA` en movimientos. El relleno rompe las claves concatenadas que utiliza «Ver asiento» del sistema legacy.

Las regresiones comparan `DATALENGTH` contra el valor recortado, porque la igualdad SQL y `LEN` no detectan espacios finales. Validación: cuatro pruebas aprobadas en el circuito de cobranzas, incluida la grabación real con rollback en ALFANET2007_PRUEBAS. La prueba de Carga de comprobantes requiere sus fixtures de DISTRIWALTERP y quedó omitida al no disponer de esa conexión de pruebas.

La reparación histórica está preparada en [el script manual de CB](../base-datos/sql-referencia/2026-10-21__reparar_cb_relleno_movimientos.sql). Por defecto ensaya con rollback, valida el alcance relevado de 26 cabeceras, 58 renglones de asiento y 27 aplicaciones, y conserva los triggers activos. No forma parte de las actualizaciones automáticas. La corrección histórica fue ejecutada y confirmada por el usuario en ALFANET2007. Verificación posterior desde una conexión independiente el 21/10/2026: 26 cabeceras, 58 renglones de asiento y 27 aplicaciones, sin relleno final en TC, CUENTA ni TC_PRINT (este último en aplicaciones). El bloqueo de la transacción previa ya no impide la consulta. El asistente no ejecutó la reparación histórica; verificó su resultado.

## Evidencia legacy

Raíz consultada: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\`. Las líneas corresponden a las fuentes consultadas en esta fecha.

| Fuente | Comportamiento observado |
|---|---|
| `V_MV_VENTAS.vbp` | Referencia `V_mv_vta.frm`, `VtaTot.frm` y `RT_MediosDePago.bas`. |
| `V_mv_vta.frm:15579–15730` | `GeneraAsientoCobranzas`: contado FP/NCFP usa CBFP; el resto CBCT. Resuelve equivalencia contable/tipo de registro, numera con `Obtener_Nro_Comprobante(...gCodUnegocio...)`, contempla colisiones y actualiza contador. Acredita al cliente por el total cobrado. |
| `V_mv_vta.frm:15734–15860` | Recorre medios y resuelve cuentas MA_CUENTAS mediante CodigoOpcional; distingue efectivo, documentos, cheques, tarjetas y banco. Aplicar créditos/anticipos existentes no equivale a ingresar dinero nuevamente. |
| `VtaTot.frm:5165–5220` | Retenciones incluidas en la grilla y total cobrado. `Tot_Retenciones` acumula RI (IVA), RB (Ingresos Brutos), RG (Ganancias). No sumarlas nuevamente como conceptos separados. |
| `RT_MediosDePago.bas:5–107` | Grilla compartida con cuenta, descripción, serie, número, vencimiento, importe, banco, autorización, observaciones, cuenta de origen, tipo auxiliar, moneda y cotización. No es por sí solo el grabador. |
| `COBRANZAS.vbp` | Referencia `Cobranzas.frm` y el mismo `RT_MediosDePago.bas`. |
| `Cobranzas.frm:1631,3971,5476` | Circuito CB y numeración comercial por unidad del contexto. |
| `Cobranzas.frm:3242–3340` | Equivalencia contable, numeración contable, tipo de registro, comparación de medios/créditos/aplicaciones y tratamiento de anticipo. |
| `Cobranzas.frm:3475,3804–3806` | Toma certificado desde Autorizacion del medio y lo escribe en TJ_AUTORIZO de MV_ASIENTOS para líneas sin tratamiento de tarjeta. Verificar metadatos y condiciones completas antes de portar. |
| `Cobranzas.frm:5876–5897` | Totaliza RI, RB, RG y RO; otras retenciones RO se acumulan con IVA en ese formulario. |

La fuente pasa `gCodUnegocio` a numeración; no demuestra que siempre derive ese contexto de la factura. **Regla solicitada para Conciliación:** tomar la unidad de la factura, validar permisos y resolver su talonario CB. No copiar automáticamente la sucursal de la factura como sucursal del recibo.

## Servicio compartido

Servicio de Cobranzas independiente de la página POS y del importador, consumido desde Conciliación y luego desde Cobranzas. Reutilizar reglas verificadas; no ejecutar una venta completa para cobrar una factura existente.

Entrada conceptual:

- Cliente, fecha, unidad, talonario y tipo CB en este circuito.
- Lista de aplicaciones con identidad del comprobante e importe. Primera interfaz: uno; contrato preparado para varios.
- Lista de medios con cuenta real, importe, moneda/cotización y datos específicos, incluido certificado de retención.
- Identificador estable de operación y origen opcional de conciliación. La carga manual no requiere movimiento bancario.

Salida: identidad y número de cobranza, asiento, aplicaciones e identificador para recuperar el resultado ante un reintento.

`CobranzasService` prepara movimientos y valida/graba el recibo; `CobranzaRapidaDialog` contiene el editor compartido. Las reglas del POS se mantienen independientes. La API admite origen bancario opcional y requiere actualmente permiso de Conciliación. Admite una o varias aplicaciones del mismo cliente y unidad; las solicitudes anteriores, sin lista de aplicaciones, siguen aplicando el total a `FacturaId`.

## Flujo de cobranza rápida

1. Cobrar desde la grilla o el detalle abre el mismo modal, con cliente, factura, unidad y saldo releído. La propuesta no reemplaza la validación.
2. Precargar cuenta bancaria de la importación por el importe recibido; separar importe bancario del total cobrado.
3. Permitir agregar retenciones como medios, con cuenta e información requerida. Banco 97.000 + retención 3.000 = CB por 100.000; Debe banco/retención, Haber cliente.
4. Admitir aplicación parcial explícita; no inferir retenciones por diferencia ni cancelar saldos usando la tolerancia de matching.
5. Mostrar talonario/sucursal; asignar número definitivo bajo bloqueo al confirmar.
6. Grabar cabecera, asiento balanceado, aplicaciones, contador y vínculo bancario en una misma transacción, con protección contra doble cobro y reintentos.
7. Mostrar número de CB, actualizar saldos/estado y conservar filtros al regresar.

Alcance actual: una o varias facturas del mismo cliente y unidad, banco y retenciones adicionales. Para Cobranzas completo: anticipos, aplicación de créditos, cheques y tarjetas con sus efectos auxiliares. No habilitar tipos solo por existir en el maestro si faltan sus validaciones o persistencia.

## Cobranza de varios comprobantes (2026-10-21)

La preparación relee la última propuesta de `BK_MovimientoMatch/BK_MovimientoAplicacion` y resuelve los IDs actuales por tipo, sucursal, número, letra y cliente. Precarga la selección únicamente si todos los comprobantes están disponibles, autorizados, con saldo suficiente y dentro de una misma unidad. Una propuesta incompleta o vencida no se precarga parcialmente. El editor permite marcar/quitar comprobantes y corregir el importe aplicado a cada uno. Una selección sugerida cuyo total coincide con los medios conserva su distribución original; el resto se distribuye por orden de los documentos, sin exceder sus saldos. Las retenciones sugeridas utilizan el saldo conjunto de los seleccionados.

`CobranzaSolicitud.Aplicaciones` es opcional (hasta 50 IDs distintos, importes positivos con dos decimales). Su suma debe coincidir exactamente con banco más retenciones. La propiedad nula se omite al serializar, conservando el hash de solicitudes antiguas. `FacturaId` sigue identificando una factura seleccionada, usada como referencia de cabecera, vendedor y unidad; el detalle completo queda en `MV_APLICACION` y en el hash de la solicitud, sin migración de la tabla de operaciones.

Antes de escribir se bloquean y revalidan todas las cabeceras, rangos contables/aplicaciones y saldos actuales. Se exige mismo cliente, unidad autorizada y moneda pesos. Se emite una única CB, con un único asiento (cliente por el total contra banco/retenciones), una aplicación por comprobante y una actualización del contador y estado bancario. Se mantienen `TC_PRINT=CB`, `IDCOMPROBANTE_PRINT`, caja del usuario, detalle/comentarios, idempotencia y atomicidad existentes. No se generan anticipos ni se cancelan diferencias usando tolerancias.

Regresión: `CobranzaMultipleTests` valida el contrato anterior y rechazos de duplicados, importes y sumas; renderiza las dos facturas del ejemplo por 857.903,92 y conserva su distribución. La prueba SQL usa `ALFANET2007_PRUEBAS` con rollback: dos facturas, un banco y una retención, un recibo, dos aplicaciones, saldos individuales, balance, reintento idempotente y rechazo de mezcla de clientes/unidades. La pantalla se verifica a 2048/1440/1024; capturas locales en `artifacts/conciliacion/cobranza-multiple-{ancho}.png`. Conserva el estado AlfaDesign 10/13 y pendientes de homologación integral documentados en conciliación bancaria.

## Configuración verificada e implementación

Se consultaron metadatos, triggers y procedimientos instalados en la base de pruebas. Existe CB en Ventas, sentido H, letra X, talonario 0 general, sucursal 0001; EQU_CB=CB y subdiario CB mediante MA_SUBDIARIOSCOMPROBANTES/MA_SUBDIARIOS. El ejercicio 20 cubre 2026 y está abierto. No se modifican configuraciones ni talonarios existentes para habilitar la funcionalidad.

Se siguieron `cnStuffContable.bas:6331–6430,6432–6535,6694–6780,7259–7280`: primero talonario de unidad, luego fallback. En web se limita el fallback a talonarios generales (unidad vacía/0); nunca se toma el de otra unidad. El contador guarda el próximo número, no el último usado. Se combina con el máximo existente en cabeceras/asientos bajo bloqueos. En esta entrega solo se admite CB X con EQU_CB=CB; otras equivalencias requieren ampliar el circuito explícitamente.

La grabación utiliza Dapper en una única transacción Serializable, incluyendo recibo, asiento, aplicación, contador, journal y log bancario. La vista oficial VE_CPTES_SALDOS_VENTAS contiene NOLOCK internamente: se protegen previamente las filas/rangos de asientos y aplicaciones del comprobante. Se usan además bloqueos de contador y numeración de asiento y un applock transaccional del servicio.

El journal `ALFACORE_COBRANZA_OPERACION` guarda operación, hash de solicitud y resultado; índice único por movimiento bancario evita repetirlo. Mismo identificador y mismos datos recuperan el resultado. Otro identificador para el mismo movimiento o cambio de datos de una operación confirmada se rechaza. La importación cobrada no se elimina: el guard existente detecta el log COBRANZA.

La cuenta bancaria y el importe completo provienen del extracto y se validan nuevamente; otros medios en este flujo solo pueden ser retenciones RI/RB/RG/RO activas en pesos. El certificado es opcional, como en `FrmNroCertificadoRet.frm`, cuyo Aceptar admite texto vacío. No se adivinan retenciones por diferencias. Se aplica banco más retenciones sin superar el saldo actual. Asiento Debe banco/retenciones, Haber cliente; certificado en TJ_AUTORIZO. La primera entrega admite FC/FP/ND vigentes, moneda 1 y movimientos ARS, sin anticipos ni descuentos. El usuario de TA_USUARIOS debe estar autorizado por unidad o ser AdminCentral.

Archivos nuevos: `Models/CobranzasModels.cs`, `Services/CobranzasService.cs`, `Components/Shared/CobranzaRapidaDialog.razor` y `.razor.css`, actualización `2026-10-19-004__caja_cobranzas_operacion.sql`. Integración en `Components/Pages/ConciliacionBancaria.razor`, registro DI en `Program.cs`, pruebas en `ConciliacionBancariaTests.cs`.

## Pendientes para el módulo completo

- Múltiples facturas y unidades por recibo, anticipos, aplicación de créditos y medios con valores auxiliares (cheques/tarjetas), impresión/PDF y permiso propio del módulo Cobranzas.
- Ampliar otras equivalencias, monedas, letras y validación operativa de configuraciones ajenas a esta base. Los procedimientos POS instalados también usan CBCT/CBFP, sucursal 9999 e importe completo; no se llaman desde este servicio.
- Certificar convivencia concurrente con ejecutables VB6 y todas sus bibliotecas: los bloqueos web no constituyen por sí solos prueba de equivalencia completa con legacy.

Referencias: [Conciliación](conciliacion-bancaria.md), [matriz legacy de Carga de comprobantes](ventas-carga-comprobantes/matriz-compatibilidad.md). Referencia web: `PuntoVentaService` y sus métodos `CreateCollectionAsync`, `CreatePaymentSeedAsync`, `CreatePaymentLineAsync`, `CreateCollectionApplicationAsync`. La grabación web de Carga de comprobantes mantiene restricciones de contado en `CargaComprobantesPlanificador`.

## Validación de esta entrega

### Campos del recibo, asiento y aplicación (20/10/2026)

Reglas indicadas por el usuario: todas las líneas de MV_ASIENTOS graban CABCUENTA y CABNOMBRE en NULL. IdVendedor se obtiene de la factura y, si está vacío, del cliente en VT_CLIENTES; se usa el mismo en recibo y asiento, formateado al ancho 4. IdCajas se toma de TA_USUARIOS.IdCaja y, si falta, se graba `   1`.

El editor propone `Cob. ` seguido del nombre del cliente mostrado en el comprobante y permite modificarlo. CobranzaSolicitud transporta el texto; si falta, el servidor reconstruye el valor por defecto. Se conserva un comentario editado al cambiar de factura dentro de la misma preparación. El límite compartido es 100 caracteres, confirmado por V_MV_CPTE.COMENTARIOS (nvarchar(100)); MV_ASIENTOS.DETALLE admite 200. Se guarda el mismo texto en ambos campos, incluido en el hash de idempotencia.

MV_APLICACION graba además TC_PRINT=CB e IDCOMPROBANTE_PRINT=IDCOMPROBANTE del recibo. Se confirmaron ambos campos en la base de pruebas; no requiere migración SQL ni cambios en registros históricos.

Archivos modificados: `Models/CobranzasModels.cs`, `Services/CobranzasService.cs`, `Components/Shared/CobranzaRapidaDialog.razor`, `tests/AlfaCore.Tests/ConciliacionBancariaTests.cs`, manual funcional y esta nota. Ensayo transaccional ampliado para detalle personalizado/default, comentarios iguales, cabeceras nulas, vendedor factura/cliente, caja configurada/default y datos de impresión; cambios en usuario/factura y recibos de ensayo se revierten.

Validación: compilación y 25 pruebas aprobadas, ninguna omitida. El ensayo también prepara temporalmente el vendedor asociado en MA_CUENTASADIC y revierte ese cambio. Chromium con HTML renderizado a 2048/1440/1024/390 px verifica el campo de detalle editable, límite 100 y botón Confirmar visibles, sin overflow horizontal. Catálogo sin errores ni advertencias. No se actualizaron cobranzas históricas.

### Corrección de cabeceras legacy sin moneda (20/10/2026)

El rechazo informado con código 5731 correspondía a FC A 0002-00003841: V_MV_CPTE.MONEDA era NULL, mientras que el asiento de cliente tenía MONEDA=1 y cotización 1. Se elimina la dependencia exclusiva de la cabecera para este caso: si MONEDA es NULL, vacía o blancos, Documentos obtiene una moneda única no vacía del asiento del cliente identificado por cuenta/TC/sucursal/número/letra. Se usa la misma resolución al preparar y al validar bajo bloqueo. No se asigna pesos por defecto: una moneda extranjera o indeterminada sigue rechazándose, y el selector solo ofrece comprobantes resueltos en pesos. No se modifican cabeceras históricas ni configuración para corregir el caso.

Regresión ampliada en `ConciliacionBancariaTests`: cabecera NULL y blancos con asiento en pesos, recibo resultante en moneda 1; rechazo de cabecera explícita en dólares, cabecera vacía con asiento en dólares y moneda no determinable. Los cambios y recibos del ensayo se revierten al terminar. Archivos de la corrección: `Services/CobranzasService.cs`, prueba existente y esta nota.

Validación del 20/10: compilación y 25 pruebas aprobadas, ninguna omitida; consulta de la factura 81360 con la nueva resolución devuelve moneda 1. Catálogo sin errores ni advertencias. No se confirmó la cobranza del usuario durante la corrección.

- Compilación y 25 pruebas de ConciliacionBancariaTests aprobadas, ninguna omitida. Permanecen las advertencias preexistentes de Ayuda y eventos de fakes de otros módulos.
- En ALFANET2007_PRUEBAS: preparación pública de una propuesta real sin confirmar; ensayo reversible con CB por 12 (banco 10, retención 2), tres líneas de asiento balanceado, certificado, aplicación y reducción del saldo. Reintento idéntico recupera el resultado; otro cobro del mismo movimiento, solicitud alterada, exceso de saldo y cuenta inválida se rechazan.
- En la misma transacción se crea un talonario específico de unidad 2/sucursal 9001, se verifica su prioridad y se rechaza el general para esa unidad. Rollback final elimina los recibos, aplicaciones, asientos, journal, extracto y talonario de ensayo. El ensayo no confirma recibos operativos; SQL Server puede consumir identities.
- Editor probado con banco precargado, retención y certificado; selección incompleta no envía la solicitud. La prueba cubre el handler compartido que utilizan fila y detalle, no una sesión de usuario completa en navegador.
- Chromium con HTML renderizado y CSS real a 2048/1440/1024/390 px: modal, total y confirmación visibles, sin overflow horizontal. Se ejecutó el JS compartido para comprobar Tab/Shift+Tab, solicitud de cierre con Esc y retorno de foco. No constituye prueba de extremo a extremo con conexión Blazor activa.
- Actualización 004 aplicada y repetida para verificar idempotencia en la base de pruebas. Catálogo validado: 101 rutinas, sin errores ni advertencias. Archivos nuevos en UTF-8 sin BOM.


### Propuesta de última retención (20/10/2026)

La preparación consulta `CobranzasService.UltimaRetencionAsync` por cliente y unidad de las facturas autorizadas. Lee la contrapartida de retención en MV_ASIENTOS del último recibo CB que contenga una: RI/RB/RG/RO, cuenta activa, imputable y en pesos, débito positivo. Excluye resumen, apertura, cierre y recibos anulados en V_MV_CPTE; admite recibos legacy sin cabecera. Identifica el asiento por su clave completa y el recibo por TC/sucursal/número/letra y unidad, con contrapartida acreedora del cliente. Ordena por fecha/hora de grabación (fecha del recibo como respaldo), fecha y clave contable; ante varias retenciones en el mismo recibo elige la última secuencia. No replica el certificado ni el importe histórico.

CobranzaPreparacion transporta las referencias por unidad. El editor agrega una fila sugerida solo cuando el saldo de la factura supera el importe bancario y existe una cuenta válida en los medios disponibles. Importe = saldo actual menos pagos ya cargados. Muestra fecha y número del recibo de referencia; permite corregir o quitar. Cambiar factura recalcula una sugerencia intacta; conserva filas editadas manualmente y evita duplicarlas. Reintentos y renders no reemplazan lo ingresado.

La memoria proviene del asiento confirmado, sin configuración paralela ni migración. Al guardar una retención corregida, esa cuenta pasa a ser la referencia para la próxima preparación; cancelar no escribe nada y un recibo posterior sin retención no borra el antecedente. La diferencia es una propuesta revisable, nunca una confirmación automática de que existe retención.

Archivos: Models/CobranzasModels.cs (referencias), Services/CobranzasService.cs (consulta y preparación), Components/Shared/CobranzaRapidaDialog.razor y su CSS (propuesta editable), pruebas de ConciliacionBancariaTests y manual funcional.


Validación de esta mejora: compilación correcta y 39 pruebas de conciliación aprobadas, ninguna omitida. Incluye propuesta positiva, sin diferencia, exceso bancario, cliente/unidad distintos, cuenta no disponible, cambio de factura, conservación de ediciones, certificado nuevo, eliminación de la sugerencia y confirmación explícita. El ensayo SQL en ALFANET2007_PRUEBAS verifica referencia tras guardar una retención, cambio de cuenta recordado, recibo sin retención, exclusión de anulado y aislamiento por cliente/unidad; revierte los recibos y cambios del ensayo. Consulta de lectura real: cliente 112010853/unidad 2 devuelve cuenta 11221 desde CB 0001-00007540 X del 16/10/2026.

Chromium con HTML del componente y CSS real, a 2048/1440/1024/390 px: cuenta e importe propuestos correctos y editables, Quitar y Confirmar visibles, modal dentro del viewport, sin overflow global. Catálogo: 101 rutinas, cero advertencias/errores. UTF-8 sin BOM verificado en los ocho archivos de la mejora. AlfaDesign: Cumple 11/13, conserva las excepciones del módulo (shell completo con sesión autenticada y recorrido de teclado/foco pendiente de validación integrada). No se detectó deuda visual nueva ni se incorporaron componentes legacy. Esta comprobación estática no equivale a un flujo Blazor autenticado de extremo a extremo.

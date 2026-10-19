# Ampliación: carga directa, notas electrónicas y proforma

Estado al 15/10/2026: carga directa FC/NC/ND/FP conectada a validación, grabación, historial, recuperación y PDF. Los circuitos electrónicos siguen limitados a homologación; no se habilita producción ni se certifican todas las configuraciones legacy.

## Reglas confirmadas en VB6

Fuente consultada en modo lectura: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\V_mv_vta.frm`, `clsTotales.cls` y `ModGlobal.bas`. Acceso restablecido el 15/10; no se modificó la carpeta original.

| Comportamiento | Evidencia |
|---|---|
| FP genera asiento y no afecta libro IVA | `V_mv_vta.frm:14175–14185` |
| El legacy permite desactivar el asiento con `UsaFacturaProformaSinRegistracion` | `V_mv_vta.frm:14182`; el alcance solicitado para AlfaCore exige asiento |
| FP sin IVA usa el precio neto, aunque el precio de lista incluya IVA | `V_mv_vta.frm:18112`, `24809–24824`; `clsTotales.cls:1502`, `1534–1537` |
| `RobaIvaProforma=SI` permite calcular IVA; `NO` activa exención de artículos, servicios y conceptos | `V_mv_vta.frm:24809–24824` |
| FP sin IVA guarda `AlicIVA=0` en el detalle | `V_mv_vta.frm:12462–12465` |
| Excluir del libro IVA evita completar los campos de cabecera/fiscales del asiento | `V_mv_vta.frm:15128–15130`, `16181` en adelante: `ActualizoLibroIVA=True` salta ese bloque |
| La NC invierte debe/haber de la factura; la ND conserva el sentido | `V_mv_vta.frm:15102–15118`; comportamiento configurado por `V_TA_CPTE.DEBEHABER` |
| La nota electrónica requiere informar el comprobante aplicado | `V_mv_vta.frm`, bloque `PedirNroFacturaAplicada` |
| La proforma desactiva factura electrónica | `V_mv_vta.frm:27263` |

`ModGlobal.bas:7392–7406` contiene además una conversión para **impresión** cuando el importe guardado es neto. No debe tomarse como un segundo cálculo de IVA al emitir.

## Configuración consultada en DISTRIWALTERP

- `UsaFacturaProforma=SI`, `RobaIvaProforma=SI`, `UsaFacturaProformaSinRegistracion=NO`.
- Equivalencias contables: `EQU_FP=FP`, `EQU_NC=NC`, `EQU_ND=ND`.
- Unidad 4: FC talonario 10; NC talonario 7; ND talonario 6, con punto electrónico A/B 1007.
- FP: talonario 0, global, letra X, sucursal 4. **No usar el punto electrónico 1007 ni consumir numeración ARCA para FP.**
- Sentidos configurados: FC/ND/FP deben; NC haber. NC indica entrada de stock; los otros, salida. Releer talonario/unidad al validar y grabar.

Los FP históricos contienen alícuotas/importes diferentes entre sí. No se deduce de esos totales una tasa fija ni un porcentaje especial de proforma: corresponde tomar artículo, exención y configuración del circuito actual.

## Código incorporado

- `CargaComprobantesRepository` lee las tres claves de proforma usando el mismo acceso existente a configuración. No se cambian valores en la base.
- `CargaComprobantesCalculador` admite cálculo FP habilitado: con IVA mantiene alícuota y exenciones; sin IVA usa el neto y no genera grupos de IVA. Conserva Currency, descuentos y redondeos existentes. Esto es cálculo, no una habilitación de grabación.
- `ArcaCaeSolicitudDto` incluye `ComprobantesAsociados`, con lista vacía por defecto para solicitudes/journals anteriores.
- `Wsfev1Client` envía `CbtesAsoc/CbteAsoc` y lee los asociados de `FECompConsultar`. CUIT y fecha son opcionales según el WSDL; una fecha presente inválida impide conciliar.
- La recuperación comercial compara también los documentos asociados. Un CAE con otro documento aplicado no puede atribuirse a la operación aunque coincidan cliente e importes.

Contrato contrastado con el [WSDL oficial de WSFEv1 de homologación](https://wswhomo.afip.gov.ar/wsfev1/service.asmx?WSDL), tipos `FEDetRequest` y `CbteAsoc`. Los tipos fiscales se deben resolver mediante `TiposDocumentoCore`: FC A/B 1/6, NC A/B 3/8, ND A/B 2/7.

## Implementación comercial

1. **Carga nueva.** `VentasCargaDirecta` usa `AlfaLookup`, selects y feedback AlfaDesign dentro del módulo existente. El borrador contiene cliente, unidad, depósito, tipo, artículos y cantidades. Condición, motivo, vendedor, lista/clase y precios se resuelven desde los maestros actuales; no crea NP/RM auxiliares. La [ampliación de edición de precios/descuentos](edicion-precios-descuentos.md) admite overrides manuales en FC directa y FP con permisos y límites revalidados en servidor.
2. **Planificador compartido.** El circuito directo y NP/RM usan las mismas validaciones de cliente, cuentas, fecha, período y talonario. La preparación directa consulta artículos en lote y reutiliza contexto/lista/clase y `SplitPrice` del resolutor compartido. El snapshot incluye cliente, artículos y borrador; notas agregan cabecera, detalle y aplicaciones de la factura asociada. Se valida antes del CAE y dentro de la transacción final.
3. **Grabador compartido.** Cambia la fuente SQL de cabecera/detalle para una carga nueva; reutiliza stock, asiento, electrónica y numeración. La carga directa no consume orígenes ni crea aplicaciones ficticias. NC/ND sí se aplican a su factura por el total. NC conserva precio, descuento, tasa, costo, equivalencia, referencia de renglón y métricas proporcionales de la factura original. El sentido contable/stock se lee del talonario.
4. **Proforma.** Siempre genera asiento en el alcance solicitado, incluso si el legacy permitía omitirlo mediante `UsaFacturaProformaSinRegistracion`; no cambia esa configuración. Con IVA genera la contrapartida correspondiente; sin IVA, el asiento contiene cliente/ventas por el neto. En ambos casos omite cabecera y campos fiscales del libro IVA, CAE y fila electrónica. El coordinador conserva la guarda de ambiente de prueba del módulo; no autentica WSAA ni solicita CAE para FP.
5. **Notas.** Requieren FC comercial aprobada en homologación de este módulo, del mismo cliente, unidad, letra y emisor. La NC limita cantidades por artículo descontando NC anteriores; exige renglón y unidad no ambiguos. Antes del envío se consulta también la factura en ARCA y se comprueban documento/fecha/emisor. Una nota pendiente bloquea otras sobre la misma factura, mientras la recuperación mantiene su operación original.
6. **Identidad y numeración.** Migraciones idempotentes `2026-10-15-001/002` agregan GUID único de borrador, tipo y factura asociada al journal. `OrigenId=0` solo indica ausencia de NP/RM; no identifica ni bloquea cargas directas. Los bloqueos usan el borrador o factura asociada. FP asigna número con bloqueo del talonario y consulta la cabecera máxima de su sucursal; número, movimientos, contador y estado GRABADA se confirman en la misma transacción.
7. **Pantalla/PDF.** Ruta `/{idweb}/{idbase}/ventas/carga-comprobantes/nuevo`, accesible desde **Nuevo comprobante directo**; conserva `?directo=1`. Header declara `ModuleKey=ventas`; no agrega sidebar ni otro permiso de menú. Los cambios invalidan el plan. El cambio de sesión descarta el borrador y evita reaplicar resultados anteriores. Historial y PDF usan el TC real; FP se representa con la plantilla no fiscal de proforma existente.

El primer alcance directo conserva las restricciones de artículos simples, moneda local, cuenta corriente sin recargos/límites especiales y una alícuota. Ofertas/escalas, descuentos especiales, exenciones fiscales electrónicas, percepciones, impuestos internos/ITC, series/lotes, unidades distintas y servicios/conceptos requieren ampliar y contrastar el circuito. Estas restricciones se informan antes de emitir; no se interpretan como importes cero.

La comparación completa ejecutada contra VB6 sigue pendiente; el relevamiento de fuentes, los ensayos transaccionales y las pruebas de homologación no equivalen a certificar todas las configuraciones.

## Verificación del incremento inicial de cálculo/SOAP

- Suite `CargaComprobantes` con SQL de DISTRIWALTERP: **40 correctas, 5 omitidas, 0 fallos** (45 en total). Los casos de emisión fiscal real requieren activación separada y no se activaron.
- Casos nuevos: FP con/sin IVA, descuentos y exención; bloqueo de FP deshabilitada; solicitudes SOAP de NC/ND A/B; lectura de asociados con/sin campos opcionales; rechazo de conciliación con otra factura asociada.
- Los casos SQL existentes ejercitan validación/grabación con rollback y recuperación; no prueban todavía grabación directa, notas o FP.
- Catálogo sin errores ni advertencias, `git diff --check` limpio y archivos editados en UTF-8 sin BOM.
- Preview local reiniciado; la ruta `/ALFANET/153/ventas/carga-comprobantes` responde HTTP 200.

## Prueba comercial completa en homologación

El 15/10 se emitió y conservó el circuito siguiente en DISTRIWALTERP, cliente 112011541, unidad 4, depósito 1, artículo 403. Las cuatro operaciones tienen GUID estable y su prueba no se ejecuta por defecto.

| Tipo | Comprobante | Total | Asiento | Stock | Total libro IVA | CAE |
|---|---|---:|---:|---:|---:|---|
| FC | A 1007-00000187 | 202,40 | 3 | -2 | 202,40 | 86420999316071 |
| NC | A 1007-00000162 | 101,20 | 4 | +1 | -101,20 | 86420999316128 |
| ND | A 1007-00000002 | 101,20 | 5 | -1 | 101,20 | 86420999316173 |
| FP | X 0004-00006478 | 101,20 | 6 | -1 | NULL | No aplica |

Los cuatro asientos balancean a cero. NC y ND se asociaron a FC 1007-187; sus asociados fueron consultados y conciliados nuevamente mediante FECompConsultar. Repetir la misma operación y recuperarla devuelve el comprobante existente. La FP no tiene fila en `V_MV_CPTE_ELECTRONICOS`; sus campos fiscales de `MV_ASIENTOS` quedan nulos. La prueba SQL de FP con/sin IVA modifica únicamente claves precisas dentro de una transacción siempre revertida y confirma que la configuración original queda restaurada.

Resultados y PDF reales en la carpeta ignorada `artifacts/ventas/homologacion/`: `circuito-carga-directa.json`, `FC-directa-100700000187A.pdf`, `NC-directa-100700000162A.pdf`, `ND-directa-100700000002A.pdf`, `FP-directa-000400006478X.pdf`. Se revisaron los PDF de NC/FP: título, letra, importes y marca de homologación; FP no incluye CAE ni QR. Conservan la plantilla existente del cliente y sus datos de maestro.

La prueba explícita `CargaComprobantesDirectaHomologacionTests` requiere `ALFACORE_TEST_SQL` y `ALFACORE_TEST_DIRECTA_EMITIR=1`. Guarda las cuatro operaciones `e282dd4d-88b6-4f15-a12d-4a4a0e7aef34` a `…37`; **no cambiar esos GUID para repetir una verificación**, porque una identidad nueva representa otra emisión.

## Consultas y recursos SQL

Durante la regresión se observaron timeouts por `RESOURCE_SEMAPHORE`. SQL Server informó `process_physical_memory_low=True`; una consulta solicitaba aproximadamente 30 MB mientras el semáforo disponía de unos 19 MB. Se confirmó espera de recursos sin sesión bloqueadora. La configuración global del servidor no se modificó.

Se redujo el trabajo del módulo:

- Cabecera directa consulta una sola vez `VT_CLIENTES`, sin unir dos expansiones de esa vista para recuperar los mismos datos.
- Listado calcula una vez los documentos pendientes de la vista oficial y materializa los IDs filtrados para compartirlos entre página y conteo.
- Carga de un origen busca primero por PK y agrupa una vez los saldos del documento para unirlos al detalle, en lugar de recalcular la vista por renglón.
- Los prefiltros de TC incluyen las variantes de blancos posibles en ancho 4 para NP/RM; se mantiene `LTRIM/RTRIM`. Para números que ya ocupan el ancho completo de 13 caracteres se agrega igualdad directa, conservando también la comparación normalizada.

Se conservan las definiciones oficiales de `VT_V_MV_INSUMOS_SALDOSRM` y `VT_V_MV_INSUMOS_APLICACION`, las cantidades por depósito y los controles de pertenencia/permisos. Las tablas temporales son de la consulta; no se crea una copia persistente de saldos ni se cambia un trigger o índice legacy.

## Cierre de verificación del 15/10

- Circuito opt-in real FC → NC/ND + FP: correcto, con CAE de homologación para los tres electrónicos, conciliación de asociados, repetición/recuperación sin duplicados y cuatro PDF conservados.
- Última regresión completa: 45 correctas, 6 omitidas por opt-in y un timeout en la consulta auxiliar del propio test que buscaba cabeceras históricas sin saldo. Esa comprobación se cambió a lecturas por lotes de la misma vista oficial; su reejecución pasó en 20 segundos. No se cambiaron sus aserciones de exclusión por listado y apertura.
- Reejecución de ambas pruebas de interfaz: correctas, incluyendo conservar datos ante error, verificar ausencia de operación antes de desbloquear y exigir revalidación de cambios. Chromium verificó 2048/1440/1024, foco de cantidad y ausencia de desbordes; captura del componente en `artifacts/ventas/homologacion/carga-directa-1024.png`.
- Catálogo: 91 rutinas, sin errores ni advertencias. `git diff --check` limpio; archivos del incremento en UTF-8 sin BOM y sin mojibake.
- Preview levantado en loopback, puerto 5055, con procesos de segundo plano deshabilitados. HTTP 200 para el listado, `/ALFANET/153/ventas/carga-comprobantes/nuevo` y la misma ruta con `?directo=1`.

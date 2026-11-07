# Órdenes de Pago (OPG / PGFP)

## Estado

v1 implementada el 06/11/2026 (fases 0-5). Pendiente: recorrido de la pantalla en navegador con sesión real.
Verificación de base: ALFANET_DEMO en SERVER-ALFAVB6 (solo lectura + ensayos con rollback).

Origen legacy: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT\PAGOS.vbp`, formulario de arranque `FrmMvPagos.frm`. Líneas citadas de esa fuente a esta fecha.

## Decisiones (Alberto, 06/11/2026)

1. Una sola opción de menú, **Orden de Pago**, con interruptor **Proforma** (visible solo si la base usa proformas y el usuario tiene `VerProforma`). Con el interruptor, el comprobante es `PGFP`.
2. Medios de pago de la v1: efectivo, banco/transferencia y cheque propio. Cheques de terceros: v1.1.
3. Se respeta `MezclarProformas` (por defecto no mezclar).
4. La OPG **no tiene vencimiento**: solo fecha de emisión. (El legacy graba `FechaEstInicio`; no se pide ni se muestra.)
5. Retenciones (Ganancias, IIBB, IVA, SUSS) quedan para la v2.

## Cómo funciona el legacy

`FrmMvPagos` cambia de modo por parámetro: sin parámetro `PG`; `OPGConMedios` y `OPGSinMedios` generan `OPG`; `OPGLiquidacion` liquida una OPG existente generando un `PG`; `@PGFP` arranca con el tilde Proforma.

- La OPG con medios es la "orden de pago y liquidación": si se cargan medios (`Parcial > 0`, líneas 6055-6062) se graba finalizada y con asiento; sin medios queda pendiente.
- `FNTC` (línea 6899): con proforma el tipo es `PGFP` (salvo OPG sin medios); si no, `OPG` en modo orden de pago.
- Listas (líneas 7061-7275): con el tilde solo se eligen `FPC`/`NCCP`/`PGFP`; sin tilde, el resto. `MezclarProformas=SI` quita la restricción; el tipo grabado sigue dependiendo del tilde.
- `PGFP` no graba retenciones (líneas 3079, 4154). Las vistas contables excluyen `PGFP`.
- Cabecera `V_MV_CPTE`: `TC`, `IdComprobante`, `Finalizada=1`, `TCOrigen`/`ComprobanteOrigen` = sí mismo, `ImpDescuento1`, `Comentarios`, `UNegocio`, datos del proveedor (`VT_PROVEEDORES`).
- Asiento `MV_ASIENTOS`: Debe proveedor por el total pagado; Haber por cada medio (cuenta = `MA_CUENTAS.CodigoOpcional` del medio) y por descuentos/retenciones. Debe = Haber o se aborta.
- Aplicaciones `MV_APLICACION`: una por factura (`FCC`/`NDC`/`FPC`) con `TC_PRINT` e `IdComprobante_PRINT`. Anticipos y NC usados como medio se aplican a las facturas sin línea de asiento.
- Si los medios superan las facturas: anticipo previa confirmación. Si los débitos superan lo pagado: se ajusta o se aborta (siempre aborta si es agente de retención).
- Numeración: talonario `COMPRAS`, equivalencia `NUM_<TC>`; contable por `EQU_OPG`/`EQU_PG` (`PGFP` usa su código).
- Pendientes: vistas `CO_Cptes_Impagos` y `CO_Cptes_Creditos_Pendientes`; `chequesxunegocio` limita por unidad; `PagosAplicacionAuto` tilda todo.

## Lo que ya existe en AlfaCore

- Patrón a seguir: `CobranzasService` (transacción Serializable, journal de idempotencia, asiento balanceado, aplicaciones, contador). Numeración: `NumeracionAsiento.cs`, `CompraCargaService.Numeracion.cs`.
- `CO_CPTES_IMPAGOS_2026` ya incluye `FCC`, `FPC`, `NDC`.
- No existe módulo de pagos. La carga de compras **no soporta `FPC`** (pendiente), por lo que hoy las proformas de compra vienen solo del VB6.

## Plan

0. **Verificar base** con `docs/base-datos/sql-referencia/2026-11-06__verificar_ordenes_de_pago.sql` (solo lectura): configs, vistas de pendientes, tablas de talonarios, muestra de OPG reales.
1. **Servicio** `OrdenPagoService` (Preparar / Validar / Grabar) + modelos. Journal `ALFACORE_PAGO_OPERACION`. Todo configurable en `TA_CONFIGURACION`.
2. **Pantalla** Blazor en shell AlfaDesign (`AlfaDesignManagedModules` + `ModuleKey`): proveedor, comprobantes, medios, totales.
3. **Menú/permisos**: script idempotente en `App_Data/updates/` sobre `ALFACORE_MENU_WEB`/`ALFACORE_TAREAS_WEB` (nunca `TA_MENU`/`TA_TAREAS`).
4. **Impresión** PDF de la OPG (QuestPDF).
5. **Pruebas** con rollback en `ALFANET2007_PRUEBAS`: pago exacto, parcial, anticipo, proforma, mezcla rechazada, reintento idempotente. Luego `python tools/catalogo/check_catalogo.py`.

## Fuera de la v1

Retenciones, OPG sin medios y su liquidación posterior, descuentos, centros de costo, otras monedas, cheques de terceros.

## Implementación (06/11/2026)

Archivos: `Models/OrdenPagoModels.cs`, `Services/OrdenPagoService.cs` (reglas puras, consultas) y `OrdenPagoService.Grabar.cs` (grabación), `Components/Pages/OrdenPago.razor` (+ `.css`), update `2026-11-06-003__compras_orden_pago_menu_web.sql` (journal `ALFACORE_PAGO_OPERACION` + menú `D509012`), DI en `Program.cs`, pruebas `tests/AlfaCore.Tests/OrdenPagoTests.cs`. Ruta `/compras/pagos/orden-de-pago`, permiso `D509012`.

Hallazgos de la base (ALFANET_DEMO):
- `EQU_OPG=OPG`, `EQU_PG=PG`; no hay `NUM_OPG`. `MEZCLARPROFORMAS=NO`, `USAFACTURAPROFORMA=SI`, `CHEQUESXUNEGOCIO=NO`. `TIPOREGFACTURACION` no existe: el legacy graba `TIPO_REG` con 4 espacios (se respeta).
- Contador: `V_TA_CPTE.X_ULTIMO_NRO` guarda el **próximo** número (OPG 41, PGFP 220, PG 224); talonario 0, sucursal 1, letra X, sistema Compras.
- Pendientes: `CO_CPTES_IMPAGOS` (haber en cuentas `TipoVista='PR'`, `Estado=1` = bloqueado) y `CO_CPTES_CREDITOS_PENDIENTES` (debe en PR/PE: NC, anticipos, OPG sobrantes). `CO_CPTES_IMPAGOS_2026` no trae `MONEDA`.
- Medios: `MA_CUENTAS.MedioDePago` EF / DB / CH; cheque propio = `CH` con `TipoVista='BC'` y chequera en `V_TA_CTRL_CHEQUERAS` (`CuentaBancoDiferido` = cuenta del medio). `MV_ASIENTOS.ChPropio` no admite NULL.
- Las OPG históricas de la demo son sin medios (sin asiento); no hay ejemplo de OPG con asiento ni FPC cargadas para pagar.

Reglas implementadas:
- OPG (`TC=OPG`) o, con el interruptor Proforma, `PGFP`. El interruptor aparece solo con `USAFACTURAPROFORMA=SI` y `TA_USUARIOS.VerProforma`. Sin mezcla (salvo `MEZCLARPROFORMAS=SI`): proforma paga solo `FPC` y aplica solo `NCCP`/`PGFP`; el pago normal excluye esos tipos. La lista ya viene filtrada; la grabación lo revalida.
- Asiento: Debe proveedor por el dinero (suma de medios), Haber por cada medio; primera línea con CAB* del proveedor y `LIVA_Ret_*` en 0 (como el legacy). Los créditos no tienen línea: se aplican a las facturas por orden (`MV_APLICACION` con el crédito como pagador) y la orden aplica el resto. Lo que sobra es anticipo (queda como crédito pendiente de la orden), con confirmación explícita.
- Se exige al menos un medio con importe; moneda 1; comprobantes bloqueados o en otra moneda no se seleccionan.
- Numeración bajo bloqueo: `max(contador, máximo existente + 1)`; actualiza `X_ULTIMO_NRO`. Idempotencia por `Operacion` + hash (journal). Una transacción Serializable.
- Sin vencimiento en la OPG (decisión de Alberto).

Pruebas: 23 (20 puras: reparto, anticipo, créditos, reglas de proforma; 3 contra ALFANET_DEMO con rollback: pago exacto con asiento/aplicación/saldo/contador/idempotencia, crédito + anticipo + rechazo de mezcla, cheque propio con chequera). Requieren `ORDENPAGO_TEST_SQL` (ALFANET_DEMO o ALFANET2007_PRUEBAS); sin ella se omiten. Tras correrlas no quedó nada en la demo.

No cubierto todavía: pago real de una `FPC` (no hay proformas de compra cargadas; solo se probó el rechazo de la mezcla), impresión, y la pantalla en un navegador autenticado.

## Pendiente
- Fase 4: PDF de la orden de pago (reporte legacy `OPAGO`, `OPAGO-X.rpt`).
- Verificar la pantalla de punta a punta con sesión (AlfaDesign, teclado, 390/1024/1440 px).
- v2: retenciones (GAN, IIBB, IVA, SUSS; `PGFP` nunca retiene), OPG sin medios y su liquidación, descuentos, centros de costo, otras monedas, cheques de terceros, tarjetas.

## Verificación con datos reales (PRUEBAS02122025, 06/11/2026)

Base con 5.757 OPG, 3.206 PG y 1.259 PGFP reales. Se comparó una OPG real con asiento contra lo que graba el servicio y se alineó:
`VENCIMIENTO` del asiento solo en cheques; `CuentaOrigen='-'` en transferencias (cheque propio: la cuenta del medio); `LIVA_Ret_*` en 0 en todas las líneas; cabecera con `FECHAESTINICIO=FECHA` e `IDMOTIVOCPRAVTA` del proveedor (si existe en `V_Ta_MotivoVta`).
Dos talonarios de OPG (general sucursal 0001 y de la unidad sucursal 0003): se usa el de la unidad, como el legacy. En los PGFP reales hay dos poblaciones: con `Finalizada=1`/`TCOrigen=PGFP` (vienen de la orden de pago, como los de este módulo) y con `Finalizada=0` (pago directo).

Se grabaron de verdad en esa base (ensayo `GrabaDeVerdadOpgCreditoProformaYChequePropioEnBaseDePruebas`, solo con `ORDENPAGO_TEST_COMMIT=1`): OPG exacta en efectivo, OPG con NC aplicada, **PGFP de una FPC** y OPG con cheque propio. Cabecera, asiento (balanceado), aplicaciones, saldos, contadores y journal coinciden con el patrón real. El update `2026-11-06-003` también se aplicó ahí.

PDF: el generador de pagos nunca mostraba «Comprobantes cancelados» ni los medios, porque dependía de datos que el visor de comprobantes no completa (`AplicaA`, `Asientos`) y de que los pagos figuran como «Compras» aunque se graban en `V_MV_CPTE`. Ahora `DetallePagoAsync` los arma desde `MV_APLICACION` y el asiento; con créditos aplicados agrega la línea «Cancelado con créditos y anticipos aplicados». Las capturas de OPG y PGFP salen correctas.

## Diseñador de comprobantes (06/11/2026)

Tipos nuevos en `TiposDocumentoCore`: `ORDEN_PAGO` (OPG) y `PAGO_PROFORMA` (PGFP), con plantilla propia por unidad de negocio o global, igual que las facturas.

- **Datos:** `OrdenPagoDocumentService` (solo lee) arma una `FacturaDocumentData` desde `V_MV_CPTE` (cabecera y proveedor), `MV_APLICACION` (comprobantes cancelados, uno por comprobante, incluido lo cancelado con créditos; y los créditos aplicados) y `MV_ASIENTOS` (medios: líneas Haber, con cheque y fecha de pago). Se dibuja con `DocumentRenderer.RenderFactura`, así que hereda empresa con logo, tema, papel (A4/A5/Carta/ticket), márgenes, pie y tamaños de letra.
- **Plantilla estándar** (`CrearOrdenPagoEstandar`): Logo + Empresa, Comprobante, Proveedor, comprobantes cancelados (columnas Tipo / Comprobante / Importe aplicado, editables) y el bloque nuevo **Medios de pago** (`TiposBloqueDocumento.MediosPago`: medios, créditos aplicados y «Total pagado»). Pie opcional. Sin plantilla guardada usa esa estándar embebida; no hace falta script SQL.
- **Diseñador** (Utilidades → Diseñador de comprobantes): opciones «Orden de pago (OPG)» y «Pago proforma (PGFP)», vista previa y PDF beta con una orden real, panel «Datos del proveedor» y panel «Medios de pago». El logo sale de la configuración general de la empresa (`/configuracion-general`, `TA_LOGOS`).
- **Impresión:** «Ver PDF» de Orden de Pago y los PDF/mails de «Pagos y retenciones» para OPG y PGFP usan esa plantilla. PG (pago directo) y los certificados de retención siguen con el PDF anterior.
- Pruebas: `OrdenPagoDocumentTests` (tipos, plantilla válida, render con logo/proveedor/medios/créditos, bloque ocultable, facturas sin cambios, y las órdenes reales de la base de pruebas con captura). 319 pruebas de documentos verdes.

## Ver comprobantes: órdenes de pago cargadas (06/11/2026)

Pantalla `/compras/pagos/ordenes` (solapa «Ver comprobantes» junto a «Orden de Pago» y opción de menú «Ver órdenes de pago», clave `D50-OPG-VER-WEB`, update `2026-11-06-005`; recibe el permiso quien ya tiene `D509012`). Lista las OPG y PGFP (activas, anuladas o todas) con filtros de fecha, proveedor/comprobante, tipo y estado; un usuario que no es administrador central ve solo su unidad de negocio.

- **Columnas:** fecha, tipo, número, proveedor, importe, estado (Pagada / Pendiente de liquidar / Anulada), unidad, origen (AlfaCore o legacy) y último mail enviado.
- **Acciones:** Ver (doble clic), PDF (reimpresión con la plantilla del diseñador), Mail (uno o varios, con los textos configurados de Compras).
- **Ver** (`OrdenPagoDetalleDialog`, servicio `OrdenPagoService.DetalleAsync`): Resumen (proveedor, estado, quién y cuándo la cargó, medios de pago con cheque y fecha), **Trazabilidad** (qué canceló: por la orden y con créditos; créditos y anticipos aplicados; saldo a favor que dejó la orden y dónde se usó después), Asiento (debe/haber) e **Historial** (altas, modificaciones y bajas de `V_MV_CpteAcciones`, impresiones y mails de `V_MA_EMAILMOV`, ordenados por fecha).
- **Historial desde AlfaCore:** al grabar una orden se registra el alta (`A`, con asiento, período y cuenta) y cada PDF queda como «Impresión / PDF» (`IM`). Los mails ya los registra el envío (`V_MA_EMAILMOV`). Las órdenes cargadas antes de este cambio no tienen alta en el historial.
- Se probó contra PRUEBAS02122025 (listado, detalle de órdenes con crédito, mails del legacy, alta en el historial con rollback). El «último envío» se calcula en una sola consulta: por fila tardaba ~160 ms por la red.

El visor genérico de comprobantes no se usa porque está pensado para ventas/cobranzas (para pagos mostraría «OPG / OPG» en aplicaciones y no encuentra los pagos, que figuran como Compras pero se graban en `V_MV_CPTE`).

## Anular una orden (06/11/2026)

Botón **Anular** en «Ver órdenes de pago» (solo órdenes creadas desde AlfaCore, no vencidas por ejercicio cerrado). Pide un motivo de 5 a 500 caracteres.
Hace lo mismo que el legacy: borra el asiento (`MV_ASIENTOS`) y las aplicaciones (`MV_APLICACION` con `TC_PRINT` = la orden), marca `ANULADA=1` en `V_MV_CPTE` y anota una baja (`B`) en `V_MV_CpteAcciones` con usuario y motivo. Las facturas y los créditos vuelven a tener saldo porque `CO_CPTES_IMPAGOS` y `CO_CPTES_CREDITOS_PENDIENTES` se calculan sobre las aplicaciones.
Antes de borrar guarda una copia del asiento y de las aplicaciones en `ALFACORE_PAGO_ANULACION` (update `2026-11-06-006`).

No se puede anular si: la orden no es de AlfaCore (usar el legacy), el asiento está contabilizado o conciliado (por ejemplo un cheque ya conciliado), el ejercicio está cerrado, o el saldo a favor (anticipo) que dejó la orden ya se aplicó a otros comprobantes. Anular dos veces es inocuo.
El trigger legacy `FMT_NODEL` de `MV_ASIENTOS` agrega solo una baja «ELIMINACION FISICA» por línea borrada; es el mismo rastro que deja el sistema viejo.
Los cheques propios quedan liberados (se calculan desde los asientos). Pendiente: anular órdenes legacy y deshacer la aplicación de un anticipo ya usado.

## v2: retenciones — análisis del legacy (06/11/2026)

Fuente: `FrmMvPagos.frm` (`CalculaRetenciones` ~6283, `CalculaBaseImponible` ~2487, `ObtieneImporte` ~6494, `UltimoNroRet` ~7277). Datos: PRUEBAS02122025.

**Uso real:** de 5.757 OPG, 2.592 retienen **Ganancias**; IIBB, IVA y SUSS no se usaron nunca en esta base. `V_TA_RETENCIONES` tiene un solo régimen (id 1, mínimo no imponible 224.000, mínimo excluido 240, 2 %) y `V_TA_ESCALARETENCIONES` está vacía.

**Cómo calcula Ganancias el legacy**
1. Solo si `AGENTEDERETENCIONGAN=SI` y el proveedor tiene `RETGAN_IdRetencion` (en `Vt_Proveedores`). Si `MA_CUENTASADIC.FhVtoExento` es posterior a la fecha, está exento. PGFP nunca retiene.
2. Base del comprobante: neto gravado de la factura (`MV_ASIENTOS.LIVA_ImpNetoGrav`; con `CALCULARETENCIONSOBREIMPINT=SI` suma exento); si el pago es parcial, importe pagado / (1 + alícuota IVA). Aparte se suma el no gravado. Proveedores con condición IVA 5 usan el total.
3. Se descuenta de la base lo cancelado con créditos (NC/ND/anticipos) y nunca queda por debajo del pago real en medios (EF/CH/DB).
4. **Acumulativa por mes y por CUIT**: suma las bases y retenciones ya practicadas en el mes (OPG finalizadas, PG y PGFP no anuladas con el mismo CUIT y régimen; el día de inicio es el del vencimiento de exención si cae en el mes).
5. Retención = (acumulado − mínimo no imponible) × % (+ escala si hay) − retenido antes del mes; si es menor al mínimo excluido (240) es 0. Si es el primer pago del mes y no supera el mínimo no imponible, 0.
6. Graba en `V_MV_CPTE`: `RETGAN_IdRetencion/BaseImponible/Importe/NRO` (nro = máximo de certificados + 1), `IMPORTE` = total cancelado (con la retención), `RetGan_Importe`; en `MV_ASIENTOS` una línea al Haber con la cuenta de retención (`TA_CUENTASIVA.PG_RETGAN`, o `V_TA_RETENCIONES.CUENTA` si está cargada; en esta base 21304) y `LIVA_Ret_Ganancias` en la línea del proveedor. Los medios suman total − retención.
7. Alícuota: la del régimen, o la del padrón/archivo (`ARCH_RET_<id>`) si está configurado.

**Plan propuesto (v2a → v2b)**
- v2a: Ganancias completo (cálculo acumulativo, asiento, certificado nº, PDF con la retención, visor y anulación que la contemplen) con un validador que **recalcula las 2.592 OPG históricas** y compara contra lo que grabó el legacy antes de habilitarlo.
- v2b: IIBB, IVA, SUSS y alícuotas por padrón, solo si hay una base que los use para validar.

## v2a: retención de Ganancias — implementado (06/11/2026)

**Qué hace.** Si la empresa es agente (`AGENTEDERETENCIONGAN=SI`), el proveedor tiene régimen (`Vt_Proveedores.RETGAN_IdRetencion`), tiene CUIT y no está exento (`MA_CUENTASADIC.FhVtoExento` posterior a la fecha), la pantalla muestra **Calcular retención**. Hay que calcularla antes de grabar; si cambia un importe, se invalida y hay que recalcular. Las OPG retienen; las PGFP nunca.
- Base de cada factura = neto gravado × pagado / total (+ exento si `CALCULARETENCIONSOBREIMPINT=SI`; total si el proveedor es condición IVA 5; lo pagado si la factura no discrimina IVA). **Decisión de Alberto (06/11):** proporcional al neto, no la rama de pagos parciales del legacy.
- Créditos aplicados: se descuenta su parte neta (NC) o el total (anticipos). Nunca queda la base por debajo del pago real en medios.
- **Anticipo sin factura / sobre el pago real: igual que el legacy** (base = pago real en medios).
- Acumulativa por mes y CUIT, mínimo no imponible, escala (`V_TA_ESCALARETENCIONES`), mínimo excluido; `NoAcumulativa` ignora lo anterior.
- Al grabar se recalcula en el servidor: si difiere de la que vio el usuario, se rechaza. Graba `RETGAN_IdRetencion/BaseImponible/Importe/NRO/ALICUOTA` (certificado = máximo + 1), `IMPORTE` = total cancelado (medios + retención), línea al Haber con la cuenta de retención (`V_TA_RETENCIONES.CUENTA` o `TA_CUENTASIVA.PG_RETGAN`) y `LIVA_Ret_Ganancias` en la línea del proveedor.
- El visor muestra la retención con su certificado y base (los medios ya no la incluyen), y el PDF la lista como «Retención de Ganancias · Certificado N° …».
- Si el régimen usa alícuota por padrón (`ARCH_RET_<id>` configurado) se rechaza con un mensaje: se carga desde el legacy.

**Validación contra el histórico** (`OrdenPagoRetencionTests.RecalculaOrdenesHistoricasYLasCompara`, `ORDENPAGO_VALIDAR_N`): sobre 80 OPG de 2026 de PRUEBAS02122025, sin créditos coinciden **64 de 66** (los 2 restantes, por ≤ $26, en pagos parciales: es la diferencia buscada). Con créditos hay diferencias de 0,5 a 4 % porque el legacy descuenta del crédito solo el porcentaje de IVA sobre el bruto y la regla nueva descuenta su neto real, que es lo correcto. Un anticipo puro de 2026 difirió por una base que el legacy guardó dividida por 1,19 (probable carga manual). Con fechas de 2025 hay diferencias adicionales porque el mínimo no imponible y las exenciones de hoy no son las de entonces.

**Sigue pendiente (v2b):** IIBB, IVA y SUSS (sin uso en esta base), alícuota por padrón, retención manual, OPG sin medios + liquidación, otras monedas, cheques de terceros y tarjetas, anular órdenes del legacy, certificado de retención en PDF aparte, anticipo puro sin factura desde la pantalla.

## Comprobante impreso y anticipo puro (06/11/2026)

Tomando como referencia el comprobante de pagos del legacy, la plantilla de OPG/PGFP suma (cada uno se activa en el diseñador, bloque «Medios de pago y firmas»): **total en letras** («Un Mil Quinientos Pesos Con 00/100»), **En concepto de** (el detalle de la orden), **Anticipo generado**, y los recuadros **Autorizado por** y **Recibí de <empresa> el importe de la presente liquidación**, cada uno con firma y aclaración. Las plantillas ya guardadas los muestran por defecto.
**Anticipo puro:** se puede pagar sin elegir comprobantes; lo pagado queda como crédito del proveedor (hay que confirmarlo y no se mezclan créditos). Si el proveedor retiene, la base es el pago real en medios, como el legacy.
Pendiente de la v2b: IIBB/IVA/SUSS y padrón (sin datos para validar), retención manual, OPG sin medios + liquidación, otras monedas, cheques de terceros y tarjetas, anular órdenes del legacy.

## Cheques de terceros (06/11/2026)

La cartera es `V_ChequesEnCartera`: líneas al Debe con banco (cuenta de tipo CH que no sea cheque propio) menos las líneas al Haber con la misma cuenta, número, banco e importe (`V_ChequesEntregados`). Entregar un cheque es grabar esa línea al Haber: no hay otra tabla.
En la pantalla, en «Medios de pago» aparece la lista **Cheques de terceros en cartera** (banco, número, origen, vencimiento, importe; filtrada por unidad si `CHEQUESXUNEGOCIO=SI`). Los tildados suman a los medios, al pago real para la retención y a los totales. Al grabar se revalida con bloqueo que el cheque siga en cartera y se asienta como el legacy: misma cuenta, banco (sin recortar, es clave foránea de `V_TA_Bancos`), número, vencimiento y clearing originales, `ChPropio=0`, `CuentaOrigen` = serie + `-` + cuenta de origen del cheque, `CuentaDestino` = proveedor. Anular la orden borra la línea y el cheque vuelve a la cartera.
**Verificación:** ni PRUEBAS02122025 ni ALFANET_DEMO tienen cheques en cartera (0), y solo 1 de 5.757 OPG históricas pagó con un cheque de terceros; el ensayo con rollback crea un cheque recibido de prueba, lo entrega, comprueba que sale de la cartera y que no se puede entregar dos veces. Conviene probarlo con una base con cartera real.

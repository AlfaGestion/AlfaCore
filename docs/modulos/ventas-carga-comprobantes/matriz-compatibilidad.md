# Matriz de compatibilidad — Carga de Comprobantes

Fecha inicial: 2026-10-14. Actualización: 2026-10-15. Alcance acordado: papeleras, mayoristas y distribuidoras. Esta matriz conserva el contrato de relevamiento; no representa una certificación general de todos los caminos de cada fila.

La [grabación comercial en homologación](grabacion-comercial-homologacion.md) implementa un subconjunto de CC-01/02/06/07/16/18/19/20/23/25/28: origen individual, artículos simples en moneda local, una alícuota, cuenta corriente, efectos locales transaccionales, CAE recuperable y PDF. Se ensayó la grabación completa con triggers y rollback. Los demás caminos y la comparación ejecutada VB6/web continúan pendientes; las capacidades no incorporadas se rechazan explícitamente.

El [incremento de cálculo y efectos de origen](incremento-calculo-grabacion.md) agrega pruebas de un circuito acotado y ensayos de rollback. No cambia el estado de certificación de emisión de esta matriz.

La [corrección de precisión y diagnóstico del cierre](comparacion-grabaciones.md#corrección-de-precisión-y-cierre-reproducible-del-1510) elimina el redondeo prematuro del IVA unitario antes de cantidad, respaldada por `clsTotales.cls:324–331` y `1144/1153`. La muestra retrospectiva queda en 81 coincidencias completas y sin diferencias de netos a dos decimales; quedan 19 diferencias de IVA/total y la comparación ejecutada con VB6. No modifica los estados `Fuente` ni declara equivalencia general.

La [ampliación de carga directa, NC/ND y FP](carga-directa-notas-proformas.md) incorpora la grabación acotada de artículos simples sin NP/RM auxiliar, notas electrónicas asociadas y proforma con asiento sin libro IVA. Conserva las restricciones indicadas y no convierte las filas de esta matriz en certificación general del legacy.

Actualización de evidencia: [relevamiento de DISTRIWALTERP](sql-distriwalterp-hallazgos.md) e [incremento de consulta/simulación](incremento-consulta-simulacion.md). Se probaron consultas reales, permisos y una simulación proporcional sin escrituras comerciales; eso no equivale a certificar los resultados de una grabación.

Estado `Fuente`: comportamiento localizado en VB6; requiere cerrar mapeo por campo, objetos SQL y prueba. Estado `Diseño`: requisito propuesto para el producto. Estado `Diferido`: excluido de la etapa actual. Las líneas se fijan respecto de las fuentes identificadas en `inventario-fuentes.json`.

## Ampliación del 17/10/2026

Se implementó un subconjunto adicional de CC-02/03/04/05/10/11/12: varios orígenes compatibles conservando líneas, edición de precios con permisos, tres alícuotas/exentos y conversión de bultos a la unidad de venta. Ver [alcance y restricciones](consolidacion-iva-unidades.md). El estado Fuente de la matriz no se transforma en certificación general por esta ampliación.

## Circuitos y reglas

| ID | Circuito/regla | Evidencia VB6 | Configuración o dependencia relevante | Efecto que se debe comprobar | Estado |
|---|---|---|---|---|---|
| CC-01 | Factura directa de artículos | `V_mv_vta.frm:13926`, `12374`, `12596` | `V_TA_Cpte`, maestro, lista, motivo | Cabecera, artículos, stock y asientos coherentes con TC | Fuente |
| CC-02 | Pedido → factura | `VerTipoFc:13815`, `ActualizarDatos:14261`, `VerSiDescargaStock:13622` | Pedido, condiciones comerciales y depósito | Pendientes, relación de origen, stock y aplicaciones | Fuente |
| CC-03 | Remito → factura | `VerSiDescargaStock:13581` | `DescargaStockRemitos`, `DescargaStockxReparto` | No duplicar stock ya descargado; considerar historia de devoluciones | Fuente |
| CC-04 | Varios remitos → factura | `ConsultaRemitosConsolidados:26089`, `GeneraValoresAplicacionRMCons:26913` | `FacRMConsDefault`, `FacRMConsOpcional` | Conservar relaciones y cantidades por documento y línea | Fuente |
| CC-05 | Entregas/cantidades parciales | `AsignarDatosComprobantePendientesENtrega:28116`, `ActualizaInsumosComprobante:12374` | `EntregaParcialRemito`, estados del origen | Pendiente residual correcto, sin sobrefacturar origen | Fuente |
| CC-06 | Consolidar artículos | `ConsolidarArticulos_MSFG:26761`, `DatosValidos:17425` | Precio, IVA, unidad, origen, lote, descuentos | Definir condiciones reales de agrupación; conservar trazabilidad | Fuente |
| CC-07 | Presupuesto → documento | `TbrCargaCpte_ButtonClick:8969`, `ActualizarDatos:14266` | `VigenciaPresupuestos`, `FacPRConsOpcional` | Revalorización, aplicación y finalización según circuito | Fuente |
| CC-08 | Cabecera fiscal/comercial | `ActualizaDetalleComprobante:12596` | Cliente, condición IVA, sucursal, vendedor, lista | Copia de datos del cliente y valores históricos del comprobante | Fuente |
| CC-09 | Lista/clase de precio y ofertas | `clsTotales.cls:1767`, `V_mv_vta.frm:20358`, `26050` | Permisos de lista/clase, ofertas, moneda | Precio elegido, vigencia y escalas sin reinterpretar | Fuente |
| CC-10 | Descuentos y recargos | `clsTotales.cls:411`, `1485`; `V_mv_vta.frm:20657` | `MaxDto`, `MaxDtoProforma`, `V_Descuentos`, permisos y límites del vendedor | Orden de cálculo y cuentas de descuento/recargo | Fuente |
| CC-11 | Bultos/unidades/equivalencias | `ActualizaInsumosComprobante:12422`, `12431`; `PrecioSegunUD:22962` | Unidades y coeficientes del artículo | Cantidad, coeficiente, unidad base, precio por UD y stock | Fuente |
| CC-12 | IVA, exento y no gravado | `clsTotales.cls:411`, `1485`; `DatosValidos:17264` | Condición cliente/empresa, TC/letra, `ArtExentoaNoGrav` | Desglose por alícuota y correspondencia contable/fiscal | Fuente |
| CC-13 | Impuestos internos y percepciones | `clsTotales.cls:334`, `411`; `PreparaFCElectronica:11739` | Percepciones, padrones y cuentas IVA | Bases, mínimos, distribución y datos de electrónica | Fuente |
| CC-14 | Redondeos | `ModGlobal.bas:5990`, `clsTotales.cls:1485` | `CantidadDecimales`, `CantidadDecimalesVentas` | Conversión Currency, redondeo intermedio/final y punto medio | Fuente |
| CC-15 | Servicios y otros conceptos | `Actualiza_Tareas:13549`, `ActualizarDatos:14838` | Exenciones por sección, cuentas y origen | `V_MV_CpteTareas`, `V_MV_CPTE_OBSERV` y totales/asientos | Fuente |
| CC-16 | Stock por TC y origen | `DatosCpte:13895`, `ActualizarDatos:14106`, `ClsCmnCptes.cls:1417` | `ES`, depósito, motivo, reparto, movimientos previos | Cantidades con signo, equivalencia, origen y costo | Fuente |
| CC-17 | Lotes y series manuales | `ActualizaInsumosComprobante:12404`, `ClsCmnCptes.cls:1417` | `UsaNroLote`, `PideSerie`, artículo | Representación agrupada y desglose de movimientos | Fuente |
| CC-18 | Numeración | `AsignarNumeroComprobante:16790`, `ActualizarDatos:14364`, `14564` | TC, letra, unidad negocio, numeración basada en otro TC | Identidad, correlativo y concurrencia, sin colisiones | Fuente |
| CC-19 | Contabilidad de venta | `ActualizarDatos:14996`, `CompletaDatosLineaAsiento:16054` | `TA_CUENTASIVA`, motivos y cuentas comerciales | Cuentas, signos, importes, secuencias y campos del libro | Fuente |
| CC-20 | Aplicaciones | `ActualizarDatos:14926`, `26913` | Documentos de origen y moneda | `MV_APLICACION`, importes y estados finales | Fuente |
| CC-21 | Cobranza opcional | `ActualizarDatos:15579`, `VtaTot.frm`, `RT_MediosDePago.bas` | Condición de venta, caja, medio de pago | Cuenta corriente sin cobrar automáticamente toda factura | Fuente |
| CC-22 | Crédito y vencidos | `FiltroCreditoExcedido:13697`, `FiltroCptesVencidos:13635` | `NoFacturaVencidos`, límite y avisos | Separar validación comercial de autorización diferida | Fuente |
| CC-23 | Facturación electrónica | `PreparaFCElectronica:11739`, `ActualizarDatos:14503` | `USAFCELECTRONICA`, UNegocio, TC y ambiente | Request, CAE, rechazo y recuperación sin duplicar | Fuente |
| CC-24 | Multimoneda | `ActualizarDatos:14007`, `PreparaFCElectronica:11807` | `UsaMultilesMonedas`, `TA_COTIZACION` | Conversión, cotización histórica y desglose fiscal | Fuente |
| CC-25 | Auditoría | `ClsCmnCptes.cls:1242`, `ActualizarDatos:14916` | Usuario, proceso, PC/terminal | Acciones funcionales compatibles y errores en `AUX_ERR` | Fuente |
| CC-26 | Modificar comprobantes | `SqlEliminaCpte:13516`, `CampoValida_ModificacionPedidos:20043` | Aplicaciones y restricciones del documento | Reconstrucción sin huérfanos; separar caminos que tocan reservas | Fuente |
| CC-27 | Anulación y liberación de origen | `AnulacionCompleta:21942`, `LiberaAnulacion:16582` | Cobranza, documentos aplicados, fiscalidad | Reversión consistente; no habilitar borrado fiscal indiscriminado | Fuente |
| CC-28 | Impresión y entrega | `ImprimirCpte:13822`, `verificarEnvioRemoto:16460` | Destinos de impresión/correo y conexión remota | Mismo contenido operativo; entrega reintentable sin nueva factura | Fuente |
| CC-29 | Borradores | `ModAutoSaveCarga.bas:109`, `201` | Identidad base/usuario/terminal | Recuperar carga en el contexto correcto | Fuente |
| CC-30 | Reservas/preparación | `GeneraReservas:13243`, `VerificaFinNPPreparada:13212` | `FacturaNPPreparadas`, reservas existentes | No implementar ni alterar; identificar circuitos dependientes | Diferido |
| CC-31 | Centros de costo | `ActualizarDatos:14410`, `ClsCCosto.cls` | `USACCOSTO`, cuentas que exigen CC | No implementar distribución; detectar operaciones dependientes | Diferido |
| CC-32 | Autorización de emisión | `ClaveAutorizacion:13405`, `frmClaveAutorizacion.frm` | PIN, `MA_CLAVEPIN` y motivos de autorización | No implementar solicitud/espera de autorización de emisión | Diferido |
| CC-33 | Balanzas/OCX de equipos | `FrmPesajeVentas.frm`, `frmPropsPuertoCOMMventas.frm` | Puerto y equipo físico | Entrada manual de cantidades, sin lectura de dispositivos | Diferido |
| CC-34 | Perfiles por capacidades | Requisito del usuario; ver `README.md` | Configuración central y datos reales del rubro | Un motor común, perfiles explícitos, sin sobrescribir legacy | Diseño |
| CC-35 | Doble grabación y concurrencia | Reintento de duplicado `ActualizarDatos:14470`; requisito web | Identidad de operación y bloqueo por numeración/origen | Un solo comprobante y no consumir dos veces un pendiente | Diseño |
| CC-36 | Varios pedidos → factura | Requisito de producto; el consolidado revisado maneja RM/OT/PR explícitamente | Pendientes por pedido y secuencia | Cerrar el camino NP múltiple antes de prometer compatibilidad | Diseño |

Las referencias a `V_mv_vta.frm` sin nombre de archivo en la columna de evidencia pertenecen al formulario principal. Las fuentes compartidas contienen circuitos ajenos a este módulo: su presencia no confirma su ejecución en cada caso.

## Dependencias de las exclusiones

1. Un pedido con reservas no es equivalente a un pedido simple. Antes de habilitar su facturación, comprobar si generar la factura requiere mover/liberar esas reservas. De ser necesario, el camino queda diferido; no se toca su reserva.
2. Un remito asociado a reparto puede haber descargado stock, comprometido stock o aplazado su movimiento. Leer la historia necesaria para decidir no duplica la implementación del módulo de reservas/repartos. Los caminos que requieran modificarlo quedan pendientes.
3. Los controles de descuento, precio, stock y crédito pueden llamar a PIN. Separar la detección de la condición comercial de la autorización de emisión omitida. Definir por caso si la condición bloquea, avisa o corresponde a otro circuito; no fabricar registros de autorización.
4. Un asiento con cuentas que exigen centros de costo no se certifica ignorando esa distribución. La primera entrega declara expresamente los contextos compatibles.
5. Modificaciones y anulaciones que borran reservas en VB6 no se trasladan parcialmente. Se habilitan después de cerrar el efecto completo para los documentos admitidos.

## Casos de comparación prioritarios

Cada caso parte de dos copias independientes del mismo estado de la base; una para VB6 y otra para AlfaCore. Fecha, usuario, configuraciones, listas y cotización quedan fijados. No probar emisión real dos veces: electrónica mediante homologación o simulación. Estado actual de todos los casos: **pendiente de ejecución**.

| Caso | Entrada/variación | Criterio principal |
|---|---|---|
| P-01 | Pedido simple completo → factura | Cantidades, stock, aplicación y finalización iguales |
| P-02 | Parte de un pedido → factura | Residual exacto por línea; no marcar todo finalizado |
| P-03 | Segunda factura del saldo del pedido | No duplicar cantidades previamente consumidas |
| P-04 | Remito que ya movió stock → factura | Cero segunda descarga |
| P-05 | Remito que no movió stock → factura | Una descarga conforme a configuración/historia |
| P-06 | Varios remitos del mismo cliente | Aplicaciones y pendiente individual, aun con artículos repetidos |
| P-07 | Remitos con precios/descuentos distintos | Verificar agrupación real y diferencias; cambios respecto a VB6 requieren decisión explícita |
| P-08 | Orígenes con clientes, monedas o depósitos diferentes | Reproducir restricciones y mensajes verificados |
| P-09 | Venta de bulto con equivalencia a unidades | Coeficiente, cantidad base y precio sin confundir `CantidadUD` |
| P-10 | Cantidades fraccionarias y tres decimales | Mismo resultado de cálculo intermedio y persistido |
| P-11 | Lista con IVA/sin IVA y cambio de condición cliente | Precio/desglose/letra compatibles |
| P-12 | IVA mixto, exento y no gravado | Bases e impuestos exactos, no comparar solo el total |
| P-13 | Descuentos por línea y globales con recargo | Orden de cálculo y cuentas iguales |
| P-14 | Precio/oferta por cantidad y vigencia | Misma escala y tratamiento del límite de oferta |
| P-15 | Factura en cuenta corriente | No generar cobranza inmediata por defecto |
| P-16 | Factura con cobro parcial o varios medios | Asientos y aplicación de cobranza exactos |
| P-17 | Lote/serie capturado manualmente | Trazabilidad y movimiento del lote correctos |
| P-18 | Falla después de cabecera o primer detalle | No quedan efectos locales parciales |
| P-19 | Dos operadores facturan el mismo pendiente | No sobrefacturar; respuesta funcional al segundo |
| P-20 | Doble clic/reconexión tras confirmar | Recuperar un solo resultado, sin nueva numeración |
| P-21 | ARCA aprueba pero se pierde la respuesta | Consultar/recuperar autorización, sin duplicar emisión |
| P-22 | ARCA rechaza | Estado y reintento definidos antes de habilitar electrónica |
| P-23 | Pedido preparado/con reservas | Detectar dependencia y preservar registros diferidos |
| P-24 | Cuenta que exige centros de costo | Detectar contexto no certificado antes de persistir |
| P-25 | Configuración ausente, vacía, SI y NO | Defaults por clave conforme al contrato |
| P-26 | Permisos restringidos, sin autorización de emisión | Conservar permiso de módulo/operación; no pedir PIN de emisión |
| P-27 | Cambio de sesión SQL con carga abierta | No grabar en otra base ni mezclar resultados de búsqueda |
| P-28 | Impresión/correo falla tras grabación | Reentregar el mismo documento |
| P-29 | Varios pedidos en una factura | Validar cada origen; pendiente de definir si reproduce un camino legacy o agrega capacidad nueva |

Comparar filas insertadas, modificadas y eliminadas, además de estados derivados por triggers. Revisar `V_MV_CPTE`, insumos/tareas/observaciones, `MV_ASIENTOS`, `MV_APLICACION`, `V_MV_STOCK`, auditoría, control de numeración y electrónica según caso. Para las capacidades diferidas, comprobar que no haya efectos nuevos en reservas o centros de costo.

La comparación normaliza únicamente diferencias aprobadas (identificadores técnicos, fecha de auditoría y origen web del proceso). Nunca normalizar blancos de PK, redondeos, signos, cuentas, unidades, estados o relaciones para ocultar una discrepancia.

## Hallazgos que requieren decisión

- `ActualizarDatos:14348` utiliza una tolerancia de balanceo de venta que no coincide con su comentario; cobranza utiliza otra tolerancia. Confirmar con datos y no copiar un valor arbitrario.
- `GeneraValoresAplicacion:14961`/`14963` repite un componente del importe en la expresión. Verificar qué caminos ejecutan esta expresión y los efectos de triggers antes de calificarlo o cambiarlo.
- `PreparaFCElectronica` se llama con transacción abierta; sus sentencias electrónicas se ejecutan después del commit en el camino revisado. El contrato de recuperación necesita cubrir ambos puntos de fallo.
- `Cfg` devuelve `VALOR` y entrega `ValorAux` por un argumento adicional; no todas las llamadas consumen el auxiliar. Resolver la diferencia con la norma central por clave.
- `SqlEliminaCpte` reconstruye movimientos y también elimina reservas para algunos caminos. No habilitar modificaciones usando solo una parte de esa rutina.
- `ConsolidarArticulos_MSFG:26761` puede agrupar por código si hay escalas de cantidad, conservando datos del primer renglón; no lo hace si detecta descuento global. Probar precios distintos, unidades, lotes y orígenes antes de reemplazarlo por una agrupación más estricta. Una mejora debe quedar identificada como diferencia deliberada.
- `GeneraValoresAplicacionRMCons:26913` utiliza el importe del documento original para sus aplicaciones. La cantidad parcial/consolidada no puede deducirse de ese importe: relevar los campos de detalle y triggers. El camino explícito revisado trata RM/OT/PR; NP múltiple queda por confirmar.

## Evidencia pendiente para cerrar el primer circuito

- Base DISTRIWALTERP confirmada por el usuario como pruebas modificables; falta preparar estados iniciales equivalentes para comparaciones de grabación.
- Definiciones reales y triggers iniciales consultados; falta cerrar dependencias de cada camino y comparar resultados.
- Campos que representan cantidades aplicadas/preparadas/facturadas, identificadores de origen y cálculo exacto de pendiente.
- Política de precios al traer pedidos/remitos (mantener, revalorizar, confirmar) por configuración.
- Circuitos efectivos de impresión/electrónica, defaults y excepciones de los clientes prioritarios.
- Referencia de operador para comprobar acciones de teclado, orden de carga y mensajes.

## Ampliación del 18/10/2026: selección de precios

CC-09 incorpora selección de lista y clase en FC directa, FP y ND con permisos, vigencia y obligatoriedad; la NC conserva precios históricos. Ver [contrato y reglas VB6](seleccion-lista-clase.md). Ofertas, escalas, venta al costo y selección/revalorización sobre NP/RM continúan pendientes. CC-10 no incorpora todavía descuento general ni su asiento; los descuentos por línea mantienen el alcance previo. Las pruebas y limitaciones efectivas se registran en el historial de avances.

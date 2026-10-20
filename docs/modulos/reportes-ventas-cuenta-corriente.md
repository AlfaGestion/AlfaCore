# Reportes de Ventas: cuenta corriente, saldos y cobranzas

Relevamiento del 2026-10-16. Implementadas: cuenta corriente cronológica con apertura/acumulados, Saldos, Saldos Control de Crédito contable, Cuenta Corriente Resumida y Cuenta Corriente Analítica. Las restantes opciones siguen pendientes. El análisis inicial consultó metadatos de ALFANET/106; desde la instrucción posterior del usuario, todas las verificaciones SQL de desarrollo se realizan exclusivamente en **SERVER-ALFAVB6 / DISTRIWALTERP**, sin tocar otras bases. El usuario traerá una copia más actualizada; no se asumió que esa restauración ya ocurrió.

## Referencias verificadas

Menú legacy informado: ALFA/D6080/D6010, `Shell:RP_VentasCompras @ParametroAdicional=V`. Se leyeron `RP_VentasCompras.vbp`, `Rp_VentasCompras.frm` y `stuff.bas` en `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT`, sin modificaciones ni ejecución del programa.

Se abrieron 13 RPT con Crystal Runtime 8.5 de 32 bits para leer tablas/campos, fórmulas, agrupaciones, orden y nombres de subreportes. No se solicitaron datos, conexiones, refrescos, impresiones ni guardados. Evidencia con hash SHA256: [metadatos RPT](../legacy/reportes-ventas/rpt-metadatos.json). Herramienta reproducible: [relevador](../../tools/ventas/relevar_reportes_cuenta_corriente.ps1), requiere Windows PowerShell de 32 bits y Crystal Runtime registrado. Los subreportes se inventariaron por nombre; queda revisar su contenido interno cuando se implemente cada detalle.

| Opción solicitada | RPT / circuito confirmado | Resultado propuesto |
|---|---|---|
| Cuenta Corriente | RP_CtaCte2, RP_CtaCte2UNeg; fórmulas SALDO_ANTERIOR y SALDO_PARCIAL | Código de cuenta, cliente, saldo inicial; movimientos cronológicos con debe, haber y saldo por línea; saldo al cierre por cliente. |
| Cuenta Corriente Resumido | RP_CtaCteResumidoNW, variante _UNEG; AUX_MAYORES, SaldoComprobante | Una fila por comprobante pendiente, con original, aplicado y pendiente; total por cuenta; créditos sin aplicar identificados. |
| Cuenta Corriente Analítico | RP_AnaliticoCtaCteNW; MV_APLICACION, MV_ASIENTOS y AUX_MAYORES | Factura/débito y sus cobranzas, notas de crédito y otras aplicaciones; original, aplicado y pendiente sin duplicar importes de cabecera. |
| Saldos | RP_CtaCteSaldos02 y variantes 02a/02b | Una fila por cuenta y saldo a la fecha de corte; conservar clientes con saldo aunque no tengan movimientos en el período. |
| Saldos Control de Crédito | RP_CtaCteSaldos01; CreditoDisponible | Saldo, límite del maestro y disponible; distinguir deuda de exposición adicional por cheques/remitos. |
| Detalle de Ventas | RP_VentasCompras2 | Comprobantes ordenados por fecha, subtotal diario, total general; NC y demás devoluciones con signo correcto. |
| Detalle de cobranzas | RP_CobPag, RP_CobPag_Detallado | Recibos por fecha con subtotal diario; expansión para comprobantes cancelados y medios de pago, manteniendo una sola suma del recibo. |
| Proyección de Cobranzas | RP_CtaCteProyeccion | Pendientes por vencimiento, vencidos/a vencer, días de atraso, totales por cuenta y franjas temporales. |

## Diferencias del reporte web actual

`ReporteComercialService.GetCuentaCorrienteAsync` consulta MV_ASIENTOS, restringe a cuentas existentes en VT_CLIENTES, filtra fechas y ordena por fecha descendente. Su SaldoTotal es únicamente el neto del período, sin apertura, sin saldo por línea y sin aplicaciones. El panel lo advierte expresamente. Los DTO y exportadores actuales tampoco llevan apertura/acumulado.

El servicio/panel se comparte con Compras. La ampliación de Ventas debe evitar alterar silenciosamente las reglas de Compras: separar contratos y consultas específicos cuando la lógica diverja y conservar infraestructura compartida de permisos, sesión, errores AUX_ERR, cancelación y exportación.

## Reglas propuestas para cuenta corriente

- Agrupar por código de cuenta normalizado con LTRIM/RTRIM; la razón social es descriptiva y no debe dividir una cuenta si cambió de nombre. Maestro oficial VT_CLIENTES.
- Apertura = suma contable anterior a FechaDesde; movimientos entre FechaDesde inclusive y el día siguiente a FechaHasta exclusivo. Saldo por línea = apertura más acumulado firmado; para clientes, debe suma y haber resta. Sin FechaDesde, apertura cero e historia completa disponible.
- Orden estable por cuenta, fecha y clave contable completa confirmada (no solo número de asiento: puede repetirse entre períodos). Calcular acumulados en SQL antes de paginar; nunca reiniciarlos al cambiar página. Mostrar apertura/continuación del cliente cuando una cuenta atraviese páginas.
- Aplicar el mismo conjunto de unidades de negocio, cuentas y reglas de inclusión a apertura y movimientos. Vendedor/zona seleccionan clientes por su maestro actual y no recortan pagos por vendedor del recibo.
- No aplicar una búsqueda de texto o filtro de tipo de comprobante antes del acumulado si eso produce un saldo ficticio. Distinguir filtro de presentación de un saldo parcial expresamente rotulado.
- Contemplar cuentas sin movimientos en el período pero con apertura no nula. Separar movimientos del período, saldo al corte y saldo actual; no llamar saldo actual al neto del período.
- Confirmar tratamiento de ESRESUMEN, ES_SALDO_APERTURA y EsCierre con ejemplos de cambio de ejercicio. Las ramas legacy no filtran todos estos indicadores igual; no basta copiar una suma indiscriminada ni excluir aperturas globalmente.

## Filtros comunes solicitados

Smart Search AlfaDesign existente, chips y filtros aplicados persistentes en paginación/exportación. Período, cuenta/código/cliente, unidades de negocio multiselección, vendedor y zona. Agregar opciones explícitas sin vendedor/sin zona para datos vacíos.

En ALFANET/106 se confirmaron `VT_CLIENTES.IdVendedor`, `VT_CLIENTES.ZONA`, `VT_CLIENTES.Limite_Credito`, además de IdVendedor1..5 y Limite_Credito1..5. El requerimiento actual usa **IdVendedor principal del maestro**, no MV_ASIENTOS.IdVendedor ni otros vendedores secundarios. La zona también corresponde al maestro actual: un cambio de cartera reclasifica la historia al consultar.

Unidad de negocio procede del movimiento/documento, no del maestro del cliente. Se confirmaron MV_ASIENTOS.UNEGOCIO, MV_APLICACION.UNegocio y Libro_CobranzasConFP.UNEGOCIO. Filtrar unidades no equivale a agrupar por unidad; ofrecer ambas funciones por separado. Una aplicación entre unidades exige evitar contar dos veces la cobranza y definir atribución a la factura para la deuda.

## SQL confirmado y límites detectados

| Objeto real en ALFANET/106 | Uso / precaución |
|---|---|
| MV_ASIENTOS, USER_TABLE | Fuente contable. Tiene FECHA, VENCIMIENTO, IdVendedor y UNEGOCIO; identificar filas de cuenta cliente sin multiplicarlas por asientos contrapartida. |
| VT_CLIENTES, VIEW | Filtros comerciales y límite del maestro actual. |
| MV_APLICACION, USER_TABLE | Relaciones en ambos sentidos entre comprobantes. No tiene fecha propia en las columnas consultadas: para un corte histórico hay que resolver la fecha del documento aplicador. Una aplicación modificada/eliminada no puede reconstruirse solo con su estado actual. |
| VE_SALDOAPLICACION, VIEW | Agrega aplicaciones en ambos sentidos, excluye viajes y diversos orígenes operativos. No recibe fecha de corte ni segmenta por unidad: no asumir pendientes históricos correctos por agregar WHERE a la fecha de la factura. |
| VE_CPTES_SALDOS, VE_CPTES_SALDOS_UNEG, VIEW | Débitos menos aplicaciones; la variante UNEG expone unidad del asiento, pero sigue usando aplicaciones agregadas sin unidad. |
| VE_CPTES_SALDOS_HABER / VE_CPTES_CREDITOS_PENDIENTES, VIEW | Créditos/pagos pendientes; contemplarlos evita informar deuda bruta ignorando anticipos o NC sin aplicar. |
| NW_RP_ANALITICOCTACTE, SQL_STORED_PROCEDURE | Rama de pendientes del legacy, crea #Ttemp y calcula apertura; retorna filas desde VE_CPTES_SALDOS_TODOS_UNEG y usa vendedor de esas filas. Se inspeccionó definición, no se ejecutó. No reutilizar sin adaptar filtros requeridos y validar resultados. |
| VE_SALDOSCTA, VIEW | Saldo agregado global sin corte ni unidad; referencia para conciliación actual, no apertura filtrada. |
| VE_SaldosCtaFhAcum, VIEW | Varias subconsultas correlacionadas por cuenta/fecha; apertura del RPT usa acumulado menos saldo del día. Medir y comparar antes de reemplazar por agregaciones y funciones de ventana. |
| Libro_CobranzasConFP, VIEW | CB/CBCT/CBFP, SECUENCIA=1 y devolución con signo inverso. En esta base contiene **FECHA >= 01/01/2024**: no ofrece historia anterior. Advertir su cobertura si se reutiliza; cualquier cambio de vista debe ser explícito. |

`GeneraAuxiliarCtaCte` en stuff.bas (206–1462) borra/rellena AUX_MAYORES y AUX_TMP por usuario y llama NW_RP_ANALITICOCTACTE en su circuito. No ejecutar ese generador para consultar: proponer SQL parametrizado con CTE/temporales de conexión cuando correspondan, sin auxiliares persistentes compartidos entre pestañas/sesiones.

RP_CtaCte2 tiene grupos por CuentaNombre y fecha; CuentaNombre puede concatenar descripción/código, y el formulario sobrescribe fórmulas de orden. La mejora solicitada fija agrupación por código, con orden alfabético solo como alternativa de presentación. Las fórmulas conservadas incluyen referencias antiguas a auxiliares: su mera presencia no certifica que se ejecuten o se impriman en la versión activa.

RP_CtaCteSaldos01 calcula disponible = Limite_Credito - (SALDO_ACTUAL + SALDO_ANTERIOR); si límite=0 devuelve 0. Mantener esa semántica inicial, mostrando claramente límite cero; no asumir que significa crédito ilimitado. El generador también incorpora cheques pendientes y puede incorporar remitos; se debe determinar la exposición exacta de cada columna antes de prometer equivalencia.

## Mejoras y orden de implementación

1. Cuenta corriente cronológica, apertura, saldo por fila, agrupación por cuenta y los tres filtros comerciales. Actualizar Excel/PDF y retirar la advertencia de falta de apertura solo cuando el cálculo esté validado.
2. Saldos al corte y control de crédito sobre la misma base contable; validar aperturas/cierres, límites cero y cheques/remitos por separado.
3. Pendientes resumidos y analítico con aplicaciones en ambos sentidos; comparar pagos parciales, NC, anticipos, sobreaplicaciones y documentos totalmente cancelados. Definir corte histórico frente a estado de aplicación actual y mostrar la limitación si no puede reconstruirse.
4. Detalles de ventas/cobranzas con subtotal diario; expansión de recibo y documentos sin inflar totales por joins de renglones/medios de pago.
5. Proyección por vencimiento con vencidos/a vencer, sin vencimiento identificado y filtros de cartera. Grilla expandible, exportación completa y acceso al comprobante reutilizan la infraestructura existente y respetan directo=1.

Aceptación por etapa: apertura + debe - haber = saldo final; acumulado idéntico entre páginas/exportaciones; saldo por cuenta conciliado con movimientos equivalentes; original - aplicado = pendiente con signo correcto; total de aplicaciones sin multiplicación; comprobantes cancelados fuera de pendientes, visibles en el analítico; filtros de vendedor/zona toman el maestro incluso cuando el asiento tiene otro vendedor; una o varias unidades y pagos cruzados. Comparar contra VB6 con mismas fechas, ejercicio, moneda, proformas y opciones.

No sumar monedas diferentes sin criterio explícito. Proponer moneda seleccionada o subtotales por moneda, verificando antes si IMPORTE del circuito está expresado en moneda contable y cómo usa COTIZACION. Mantener proformas como opción explícita; remitos sin facturar como exposición separada, nunca mezclados silenciosamente con deuda contable.

## Pendientes de esta pasada

### Primera etapa de desarrollo

Se agregó una consulta específica de Ventas en `ReporteComercialService.CuentaCorrienteVentas.cs`, conservando el circuito de Compras. Materializa movimientos filtrados en temporales de la conexión, calcula apertura/acumulados y pagina en SQL; no escribe auxiliares persistentes. Respeta guardas de sesión/permisos, cancelación y logging AUX_ERR existentes. El texto de búsqueda selecciona cuentas; TC solo oculta líneas después del cálculo, sin producir deuda ficticia.

Se confirmó la PK de MV_ASIENTOS en DISTRIWALTERP: CUENTA, SECUENCIA, MES_OPERATIVO, NUMERO ASIENTO, TC, SUCURSAL, NUMERO, LETRA, PERIODO y TIPO_REG. El orden de acumulación incluye fecha y todos esos discriminantes. El saldo inicial incorpora la historia anterior a FechaDesde, no solo el ejercicio seleccionado en una pantalla legacy. Se excluye EsResumen como la vista acumulada; aperturas y cierres se conservan conjuntamente para mantener continuidad. La copia inspeccionada tiene 69.009 asientos, sin filas marcadas como resumen/apertura/cierre: la equivalencia en bases que sí tengan esos indicadores sigue pendiente.

La grilla y Excel presentan apertura, acumulados y cierre por código de cuenta, también para cuentas sin movimientos con apertura no nula. Al partir una cuenta entre páginas se muestra el saldo previo a las líneas visibles. PDF conserva el comportamiento de imprimir la página actual. Los filtros de unidad multiselección y vendedor/zona principales del cliente también se aplican al resumen y detalle de Ventas; sin filtro de cartera se conserva la cobertura histórica del libro, incluso referencias que ya no estén en el maestro.

Pruebas nuevas aprobadas con lecturas de DISTRIWALTERP: conciliación independiente contra asientos, continuidad entre páginas, apertura sin movimientos, exportación y filtros de maestro/unidad. También se verificaron renderizado de los nuevos filtros, separación entre borrador y filtros aplicados, y regresión de Compras/Ventas. Resultados finales y pendientes se registran en el [seguimiento de avances](../gestion/configuracion-reportes-comerciales-avances.md).

### Alcance del relevamiento inicial

No se cambiaron pantallas, servicios, reportes instalados ni datos SQL. No se ejecutaron informes legacy ni se certificaron importes reales. Quedan implementación por etapas, lectura interna de subreportes requeridos, confirmación de clave contable completa, tratamiento de ejercicio/apertura/cierre, monedas, exposición de crédito, capacidad de reconstrucción histórica y pruebas comparativas de datos/rendimiento. Las ocho opciones son viables; la equivalencia numérica debe aprobarse con esos casos, no solo con metadatos.

Verificación del relevamiento: 13 archivos RPT extraídos sin errores, 510 fórmulas, agrupaciones y orden incluidos. JSON válido, enlaces existentes y archivos nuevos UTF-8 sin BOM. `git diff --check` correcto. Catálogo asistido: 99 rutinas, sin advertencias ni errores. No se corrieron pruebas funcionales de las opciones nuevas porque aún no están implementadas.


## Segunda etapa — Saldos y Saldos Control de Crédito

Se incorporan ambas pestañas dentro de Reporte de Ventas, con shell/filtros existentes, paginación SQL, Excel completo con total y PDF de la página visible. No aparecen en Compras. Reutilizan autorización D60RPTWEB, aislamiento de sesión/directo=1, cancelación y errores AUX_ERR. No agregan rutas, menús ni objetos SQL persistentes, por lo que no requieren una actualización de esquema nueva.

Saldos agrega MV_ASIENTOS.EsResumen=0 hasta el final del día de corte, sin recortar por FechaDesde ni TC. El corte vacío se resuelve al día actual. Devuelve una fila por código normalizado, vendedor/zona del maestro actual y saldo de las unidades seleccionadas. Se puede incluir saldo cero y clientes sin movimientos. El control también incluye cuentas con límite distinto de cero aunque el saldo seleccionado sea cero.

Control de crédito usa VT_CLIENTES.Limite_Credito principal actual. Disponible = límite actual - deuda global al corte; con límite cero devuelve cero como CreditoDisponible de RP_CtaCteSaldos01. Valores negativos de disponible se conservan. Exceso se informa frente a límites positivos. Se muestran separados saldo seleccionado y deuda global, para que filtrar unidades no aumente artificialmente el disponible de un límite único por cliente. El límite mostrado no es histórico. No se asume crédito ilimitado con cero y no se bloquean ni modifican operaciones.

La exposición adicional por cheques pendientes y remitos sin facturar no se suma a este control **contable**; el alcance aparece expresamente en pantalla y manual. Sigue pendiente certificar el control ampliado de riesgo del legacy. En esta pasada la carpeta de fuentes volvió a fallar por autenticación, tanto en Z: como en UNC; se utilizó el relevamiento previo de los RPT conservado con sus hashes, sin afirmar una nueva ejecución del programa original.

Conexión de pruebas verificada: SERVER-ALFAVB6 / DISTRIWALTERP. La lectura de esta pasada encontró 1.950 clientes y ningún límite positivo. Para verificar límite positivo, disponible/excedido y límite cero se usan cambios sobre un único cliente dentro de una transacción, con rollback obligatorio y comparación del valor original posterior; no se cambian datos en forma permanente. No se restauró la base ni se asumió que el usuario ya haya terminado su copia.

Se detectó y corrigió una incompatibilidad SQL con listas vacías de unidades: la expansión Dapper de IN vacío introduce una subconsulta que SQL Server rechaza dentro de SUM(CASE). Ahora la pertenencia a unidades se materializa antes de agregar. Los fallos de prueba se registraron por el servicio central en AUX_ERR; no se borraron.

Pendientes: aplicaciones/resumido/analítico, detalle diario de ventas y cobranzas, proyección, exposición adicional de crédito, moneda/ejercicio y aceptación del operador. Validación final en el registro de avances de esta pasada.

Verificación final de esta etapa: 10 pruebas aprobadas sin fallas ni omitidas, incluidos SQL, rollback de límites, Excel y renderizado de ambas pestañas en 2048/1440/1024. Compilación correcta y catálogo de 99 rutinas sin advertencias ni errores.


## Tercera etapa — Cuenta Corriente Resumida y Analítica

Ambas pestañas se agregan a Reporte de Ventas, sin rutas ni menús nuevos y sin incorporarlas a Compras. Las guardas de sesión/permisos, AUX_ERR, cancelación, aislamiento directo=1 y el shell/filtros existentes se conservan. Se documentó el comportamiento funcional en el manual de usuario.

### Paridad y alcance

La fuente oficial para los documentos es **VE_CPTES_SALDOS_UNEG**, la misma vista de débitos usada por el circuito NW del VB6. Se verificaron definición, tipo y columnas en SERVER-ALFAVB6 / DISTRIWALTERP, junto con VE_SALDOAPLICACION, VE_CPTES_SALDOS_TODOS_UNEG, MV_APLICACION y VT_MV_APLICACION. Los RPT extraídos anteriormente siguen siendo la referencia de agrupación/fórmulas; la carpeta VB6 no se pudo reabrir en esta pasada. No se ejecutó el generador legacy ni se modificaron sus auxiliares persistentes.

La resumida cumple el alcance pedido: **solo saldo deudor positivo**. Excluye cancelados, importes cero sin deuda y sobreaplicados; no agrega filas de créditos/anticipos sin imputar. Esta última es una diferencia explícita frente a variantes del legacy que también incorporan créditos pendientes desde VE_CPTES_SALDOS_TODOS_UNEG: no se compensan de oficio las facturas deudoras.

La analítica incluye todos los documentos deudores de la fuente, también cancelados y sobreaplicados, y sus aplicaciones. FechaDesde/Hasta filtran **emisión**; SALDO/PAGO reflejan el estado actual de aplicaciones. Un cobro posterior al rango de emisión sigue cancelando el documento. No se promete una reconstrucción histórica de aplicaciones eliminadas o modificadas. Al entrar desde otra vista se propone historia completa hasta hoy; al alternar entre ambas se conserva el rango elegido.

Los filtros de unidad se aplican a la factura/débito, sin recortar cobros de otra unidad ni perder aplicaciones con UNegocio vacío. Vendedor y zona provienen del maestro actual VT_CLIENTES, no del recibo. El tipo filtra el documento original, no los tipos canceladores. Los importes mantienen la semántica de las vistas, incluyendo proformas; no se convierte moneda.

### Aplicaciones y controles

Se usa MV_APLICACION en ambos sentidos, siguiendo VE_SALDOAPLICACION. Se excluye TC=VJ y, en el sentido documento-aplicador, los orígenes operativos OT/PR/RM/RMC/NP/OC. Así se evita tratar el vínculo factura-pedido/remito como un cobro. Cada aplicación se identifica por ID; las referencias de asiento se agrupan por cuenta/TC/sucursal/número/letra antes del join, para no multiplicar importes por renglones o períodos. Referencias ausentes o ambiguas se marcan; su fecha no se inventa.

Original/aplicado/pendiente se toman de la vista oficial. El detalle lleva saldo restante por fecha del documento cancelador e ID de aplicación, dejando sin fecha confirmada al final. Se compara el aplicado oficial con la suma del detalle y se muestra diferencia si existe. El servicio aborta con logging central cuando encuentra referencias documentales duplicadas en la fuente, en lugar de duplicar deuda. No corrige, elimina ni reaplica movimientos automáticamente.

La paginación se realiza en SQL sobre documentos, con todas las aplicaciones de cada documento en la misma página. Los totales son del filtro completo y la agrupación por código de cuenta conserva su total aun cuando atraviesa páginas. Excel tiene hoja Comprobantes con cada original una sola vez y, en analítico, hoja Aplicaciones con importes y saldos restantes. PDF conserva el comportamiento de página visible.

### Pruebas y pendientes

Se compararon cantidad, original, aplicado y pendiente de la resumida contra la vista oficial. Se verificó un documento cancelado por NC, suma de aplicaciones y saldo restante, exclusión de cancelados en resumida, filtros y exportación a dos hojas. Las pruebas controladas reutilizan una aplicación existente: dentro de una transacción se aíslan los vínculos de un único comprobante y se ensayan pago parcial, total, sobreaplicación y vínculo invertido. Se inspeccionó el trigger de MV_APLICACION antes del ensayo; no se insertaron identidades ni se emitieron documentos. Siempre se revierte la transacción y se comprueban cantidad/suma originales de aplicaciones.

La carpeta compartida sigue sin autenticar, por lo que queda pendiente la comparación visual ejecutando el VB6 con exactamente las mismas opciones. También quedan la aceptación del operador, certificación multimoneda/ejercicio y reconstrucción histórica si se exige como alcance distinto. Los próximos informes pendientes del plan son detalle diario de ventas/cobranzas y proyección; permanece pendiente exposición ampliada de crédito por cheques/remitos.

Validación final: 12 pruebas únicas aprobadas, sin fallas ni omitidas. Luego del ajuste de fechas se repitieron tres pruebas de reportes, todas correctas. Compilación final correcta y catálogo de 99 rutinas sin advertencias/errores. Instancia local reiniciada; detalles de la evidencia y límites en el registro de avances enlazado anteriormente.


## Reutilización posterior en Compras

La [ampliación de Compras](reportes-compras-cuenta-corriente.md) reutiliza los cálculos, tablas y exportadores de cuenta corriente, Saldos y aplicaciones. Las fuentes y signos se seleccionan por área: VT_PROVEEDORES/CO_CPTES_SALDOS_UNEG y H-D para Compras; VT_CLIENTES/VE_CPTES_SALDOS_UNEG y D-H para Ventas. Los archivos/DTO históricos con nombre Ventas se conservan por compatibilidad. Control de Crédito, vendedor y zona del cliente siguen siendo propios de Ventas. Las 14 pruebas de esta pasada también verificaron la regresión del comportamiento ya implementado de Ventas.

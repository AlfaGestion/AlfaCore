# Incremento 2 — liquidación de artículos y efectos sobre el origen

Evidencia histórica de este incremento. El estado posterior de emisión y persistencia está en [Grabación comercial en homologación](grabacion-comercial-homologacion.md).

Fecha: 2026-10-14. Continúa sobre el incremento de consulta/simulación. La autorización del usuario permite escribir en DISTRIWALTERP; los ensayos de detalle de origen terminan en rollback. La interfaz todavía no permite emitir una factura.

## Implementación

`CargaComprobantesCalculador` incorpora el camino de artículos de `clsTotales.AcumulaInsumos`, con precios históricos ya resueltos, `PreciosFinales=SI` y `PreciosFinalesNeto` distinto de `SI`. La interfaz muestra el desglose cuando los datos pertenecen a ese alcance. La referencia proporcional permanece separada y no sustituye la liquidación.

| Regla | Fuente | Implementación |
|---|---|---|
| Precio neto y descuento por artículo | `clsTotales.cls:1124-1129`, `2248-2253` | Descuento sobre precio sin IVA; resultado intermedio Currency antes de multiplicar cantidad |
| Cantidad e importes intermedios | `clsTotales.cls:1126`, `1144`, `1190` | Conversión a cuatro decimales; redondeo al par |
| IVA y exenciones | `clsTotales.cls:324-331`, `1102`, `1140-1166`, `1222-1243` | Exención de artículo o cabecera; IVA sin redondeo unitario previo a cantidad; alícuota histórica y fallback explícito `PIVA`; agrupación hasta tres alícuotas |
| Ajuste de precio final | `clsTotales.cls:1192-1219` | Si la diferencia supera 0,001 y no hay descuento positivo, recalcular neto/IVA a partir del bruto |
| Impuestos internos | `clsTotales.cls:1131-1137`, `1177` | Quitar internos del precio final antes del ajuste; acumularlos por cantidad, sin tratarlos como base de IVA |
| Formateo monetario | `ModGlobal.bas:5990-6035` | Currency de cuatro decimales antes del redondeo a precisión configurada |
| Componentes y total | `VtaTot.frm:2636-2686` | Sumar los componentes formateados, conservando el cálculo del total exacto |
| Ajuste final opcional | `VtaTot.frm:2707-2748` | `AjustaRedondeoTotal`: activo si falta la clave; solo NO lo desactiva. Una alícuota, sin ajustes globales/anticipos, diferencia de hasta dos centavos imputada al IVA |

Los DTOs conservan los importes intermedios del neto para diagnóstico; la presentación aplica los decimales del contexto. Este cálculo usa `decimal`; no demuestra igualdad de todos los casos límite del `Double` de VB6. El conjunto completo requiere muestras ejecutadas por el programa legacy.

El repositorio incorpora `IMPORTE_S_IVA`, `AlicIVA`, `AlicIvaRec`, `EXENTO`, `IMPUESTOS`, `ITC` y el coeficiente `CANTIDADUD`, con esquema previamente confirmado. Las configuraciones se leen de `TA_CONFIGURACION`, sin modificar claves del cliente. Las percepciones se detectan tanto en el origen como en `VT_CLIENTES`, incluido el identificador de padrón en línea.

La liquidación no se presenta para revalorización de remitos, descuentos/recargos globales, anticipos, percepciones, ITC, IVA de recargo, moneda extranjera, reservas/preparación o servicios/otros conceptos. Se informan motivos explícitos. Todavía faltan la resolución fiscal de letra/condición actual del cliente y los cambios de precio/lista del circuito de destino.

## Actualización del documento de origen

`CargaComprobantesActualizadorOrigen` exige una conexión y una transacción SQL del llamador. No abre una conexión independiente, no hace commit y no está conectado a una acción de grabación en la interfaz.

| Origen | Efectos sobre el renglón |
|---|---|
| NP simple | Sumar cantidad a `CANTIDADPREPARADA`, sumar coeficiente a `CANTIDADPREPARADAUD`, poner contadores a facturar en cero y `RECEPCION=-999` |
| RM | Restar cantidad a `CANTIDADAFACTURAR`, restar coeficiente a `CANTIDADAFACTURARUD`, limitar negativos a cero y `RECEPCION=-999` |

Fuente: `V_mv_vta.frm:12547-12590`. El coeficiente se toma del renglón; no se convierte en cantidad base ni se multiplica arbitrariamente por unidades. La suma del contador de un NP simple pertenece a este efecto legacy; no incorpora el módulo de reservas ni sus movimientos.

Se bloquea la cabecera con `UPDLOCK/HOLDLOCK` y se revalidan disponibilidad, secuencia, cantidad original, coeficiente y saldo de la vista oficial. El recuento del UPDATE utiliza `OUTPUT INTO` para no incluir las filas adicionales informadas por triggers que no usan `NOCOUNT`.

El ensayo elige un NP real apto. El relevamiento no encontró RM normales pendientes con saldo de al menos una unidad en esta base: para ese camino se crea un remito de prueba copiando el esquema confirmado y un renglón del NP dentro de la transacción. Ambos caminos modifican contadores, comprueban el resultado, provocan un rechazo por snapshot alterado y ejecutan rollback en `finally`. Se comprueba que el NP recupera sus contadores y que el RM de prueba desaparece. SQL Server puede consumir valores identity aunque la transacción se revierta; no se reseedan esos contadores. La finalización del origen permanece pendiente de la inserción de `MV_APLICACION` y sus triggers. Este componente aislado no asegura concurrencia con todos los caminos VB6 ni idempotencia de una factura completa; ambas deben probarse con el coordinador final.

## Comparación y pruebas

La comparación retrospectiva carga hasta cien FC existentes sin componentes adicionales y contrasta total, netos, IVA e internos. No identifica clientes/comprobantes en el artefacto: [resultado agregado](calculo-historicos-resultados.json). Resultado inicial: 79 coincidencias completas y 21 documentos con diferencias; 19 diferían en total, 4 en netos y 20 en IVA (categorías superpuestas). La corrección del 15/10 del redondeo prematuro de IVA unitario deja **81 coincidencias, 19 diferencias de IVA/total y ninguna diferencia de netos**; el detalle y la distinción entre cierre histórico y actual están en [comparación de grabaciones](comparacion-grabaciones.md). La diferencia máxima del total sigue siendo 0,01. Se requiere ejecutar los casos en VB6 y confirmar fuente/configuración usada; el [relevamiento del ejecutable y menú](arranque-ejecutable-menu.md) identifica la versión instalada 409, la de desarrollo 412 y el contexto de pendientes. El test de diagnóstico no exige cien coincidencias ni certifica el cálculo completo.

Las pruebas unitarias cubren cálculo parcial independiente de `TotalFinal`, descuento con Currency, ajuste lineal, exentos/internos, acumulación previa al redondeo, orden y límite de alícuotas, ajuste final configurable y rechazo de capacidades incompletas. La prueba visual renderiza el desglose y comprueba conservación de carga y resoluciones 2048/1440/1024.

Para repetir SQL se usa `ALFACORE_TEST_SQL` en el proceso, sin persistir credenciales. `ALFACORE_CALCULO_REPORTE` permite guardar exclusivamente el agregado anonimizado. Los diagnósticos SQL se ejecutan secuencialmente para no competir con la transacción de escritura y su rollback.

## Validación de esta entrega

- Build .NET/Razor correcto; avisos preexistentes de campos/eventos sin uso.
- Diecinueve pruebas correctas, sin omisiones, con SQL de pruebas y Chromium.
- NP real y RM creado en la transacción: efectos comprobados y rollback verificado.
- Segunda conexión intentando adquirir el bloqueo del NP: rechazo por timeout SQL 1222 mientras la primera transacción sigue abierta. Esto verifica el bloqueo de cabecera; no certifica concurrencia de toda la factura.
- Comparación histórica agregada de cien FC, con diferencias pendientes explícitas.
- Catálogo: 82 rutinas, cero errores y cero advertencias.
- Archivos del módulo verificados en UTF-8 sin BOM; `git diff --check` correcto.

## Contrato para conectar la grabación completa

| Orden | Acción | Condición para habilitar |
|---|---|---|
| 1 | Capturar base/sesión/usuario, capacidades y operación estable | Reutilizar las guardas existentes; resolver persistencia de idempotencia antes de confirmar |
| 2 | Resolver cliente actual, letra, talonario, fecha y vencimiento | Comparar fuente VB6 y configuración fiscal/comercial; no inferir letra desde el NP/RM |
| 3 | Iniciar transacción y bloquear origen/numeración | Revalidar saldo, stock y cierres dentro de la misma transacción |
| 4 | Calcular y construir cabecera/detalle de FC | Cerrar descuentos/percepciones y snapshots de campos; contrastar con ejecución VB6 |
| 5 | Actualizar renglones de origen | Usar el componente ensayado dentro de la misma transacción |
| 6 | Insertar stock cuando corresponde | Respetar remitos ya descargados, devoluciones, unidades, lote/serie y triggers |
| 7 | Generar `MV_ASIENTOS` y datos fiscales | Resolver equivalencia contable, motivo, cuentas IVA/ventas y balance. No instalar el SP ausente del POS como sustituto |
| 8 | Insertar `MV_APLICACION` y auditoría | Preservar PK, importes, secuencias y finalización por triggers |
| 9 | Resolver electrónica y numeración recuperable | No mantener un rollback ficticio sobre ARCA; ambiente de homologación o adaptador de prueba |
| 10 | Commit y entrega del documento | Impresión/correo reintentables; cualquier fallo local previo debe revertir el conjunto completo |

No se modificaron configuraciones, triggers ni procedimientos comerciales para adaptar la base al código. No se generó una FC comercial ni se solicitó CAE. Continúan excluidos balanzas/OCX, centros de costo, reservas y autorización de emisión.

# Mostrador / Producción: decisión técnica y límites vigentes

## Alcance implementado

El modo `MOSTRADOR_PRODUCCION` usa la `NP` comercial de Alfa Gestión como fuente del encargo. El listado y el detalle leen `V_MV_Cpte` y `V_MV_CpteInsumos`; las cobranzas se consultan únicamente al abrir una NP. La producción y la entrega que se muestran son estados operativos derivados de las líneas comerciales:

- `CANTIDADPREPARADA` determina el avance de producción.
- `EntregadoMostrador` determina la entrega pendiente, parcial o completa.
- `FechaEntrega` y `FechaPreparacion` se muestran como información operativa.

El monitor se actualiza periódicamente, pero permanece en solo lectura. La impresión de la NP reutiliza `ICotizacionDocumentService` y la facturación abre `Carga de Comprobantes` con la NP preseleccionada.

## Auditoría de compatibilidad con Alfa Gestión

La inspección de los scripts SQL disponibles confirma estas estructuras oficiales:

- `V_MV_Cpte` conserva la identidad comercial de la NP (`TC`, `IDCOMPROBANTE`, `IDCOMPLEMENTO`) y los campos `FechaEntrega` y `FechaPreparacion`. La definición está en `api/python/stores/db_create.sql:10554-10640`.
- `V_MV_CpteInsumos` conserva cantidades comerciales, `CANTIDADPREPARADA`, `CANTIDADPREPARADAUD` y `EntregadoMostrador` en la base relevada (`docs/legacy/sql-dumps/COMPLETO_042026.sql:1902`, con default en la línea 30702). La clave incluye comprobante, complemento, artículo y secuencia; por eso no es seguro actualizarla solamente por número visible de NP.
- `MV_PREPARACION` y `MV_PREPARACION_DETALLES` existen (`api/python/stores/db_create.sql:5195-5260`). El encabezado tiene fechas, usuario, técnico, depósito y observaciones; el detalle relaciona `IDPREPARACION` con `TC`, comprobante, artículo, unidad y cantidades preparadas. No aparece un campo de sector ni una relación a un catálogo de sectores.
- La vista `VT_FaltanteMercaderiaNew` deriva faltantes de `CANTIDAD - CANTIDADPREPARADA` y expone `FechaPreparacion`, fecha de finalización y depósito (`api/python/stores/db_create.sql:23370-23380`). Esto respalda la lectura del avance, no una autorización para escribirlo.
- `V_MV_CpteAcciones` se usa para historial/auditoría y se relaciona con `MV_ASIENTOS` en la rutina web relevada (`api/python/stores/Stores_alfaweb.sql:2654-2660`). No se encontró allí una operación de preparación o entrega.
- `MV_APLICACION` vincula comprobantes financieros con el comprobante origen mediante `TCO_ORIGEN`, sucursal, número, letra y cuenta; las vistas relevadas también unen esos datos con `MV_ASIENTOS` (`docs/legacy/sql-dumps/COMPLETO_042026.sql:3274-3300`, `3419-3427`). Esto permite auditar aplicaciones existentes, pero no habilita crear anticipos sin el algoritmo completo de numeración, asiento, balance y aplicación.

También se verificó que `SP_CreaCpte_DesdeOtro` copia cabecera, insumos, tareas y observaciones comerciales, incluyendo campos de preparación existentes (`api/python/stores/db_create.sql:31968-32260`), y que `wsSysMobileSPPedidosV_MV_CPTE` crea una cabecera `NP` comercial (`api/python/stores/db_create.sql:36176-36275`). Ninguno de los dos implementa sectores, preparación parcial, entrega parcial ni cobranzas.

## Límites deliberados

No se escribe `MV_PREPARACION`, `MV_PREPARACION_DETALLES`, `V_MV_CpteInsumos` ni `EntregadoMostrador` desde este circuito. El relevamiento disponible no identifica una rutina legacy homologada que permita asociar sectores, preparar líneas o registrar entregas de una NP sin riesgo de duplicar movimientos o romper la trazabilidad de VB6.

Tampoco se habilita la grabación real de `CBCT` para señas. La relación de una cobranza con una NP se considera utilizable solo cuando `PRVENTA`/observación y las aplicaciones permiten verificarla; los importes no verificables se presentan como tales.

## Próximo paso de homologación

Antes de habilitar escritura se necesita una base de homologación autorizada y evidencia de Alfa Gestión para:

1. crear y finalizar una preparación de NP;
2. asignar líneas a sectores y registrar preparación parcial;
3. registrar entrega parcial/total con usuario y fecha efectiva;
4. generar una seña `CBCT` balanceada, numerada e idempotente;
5. facturar una NP con recuperación y aplicación real de la seña.

La habilitación debe reutilizar la rutina legacy identificada o contar con un diseño SQL aditivo, con índices, auditoría, rollback y pruebas de concurrencia. No debe ejecutarse sobre producción durante esta etapa.

## Código VB6 faltante para continuar

El workspace no contiene archivos fuente `.frm`, `.bas` o `.cls` de Alfa Gestión; solo hay un inventario de referencias. Para cerrar la equivalencia bidireccional se necesitan los siguientes archivos, con las rutinas indicadas y sus dependencias:

- `V_mv_vta.frm`: `ActualizaInsumosComprobante` (líneas 12383-12542), contadores NP/RM (12547-12590), aplicaciones (14926-14969), efectos contables (14996-15208 y 16054-16445), `ActualizaEntregaMostrador` (25717), `AsignarDatosComprobanteCons` (26210) y `AsignarDatosComprobantePendientesENtrega` (28154).
- `ClsCmnCptes.cls`: `AsignarDatosComprobante` (2924), controles de stock (1417 y referencias llamadas desde allí) y la inicialización/configuración de `entregado_mostrador` (4918).
- `ModGlobal.bas`: `GeneraAuxiliarComprobantes` (6835), donde el inventario ubica la configuración de `entregado_mostrador`, además de las funciones de numeración y generación de asientos invocadas por la grabación.
- `ConCompr.frm`: `ConsultaCptes` (297), por la configuración `EntregaParcialRemito`.
- `VtaTot.frm`, `clsTotales.bas`/equivalente y `cnStuffContable.bas`: cálculo de totales, efectos financieros y asociación contable usados por la grabación.
- El proyecto VB6 (`.vbp`/`.vbg`), módulos referenciados y el texto de los procedimientos almacenados/triggers llamados por esas rutinas, especialmente los que generan numeración, asientos, stock, auditoría y aplicaciones.

Mientras esos archivos no estén disponibles, quedan resueltos de forma segura la lectura/listado/detalle, el avance derivado, la impresión y la apertura de facturación. Quedan bloqueadas la creación de preparaciones, sectores, entregas parciales/totales y anticipos reales. No se propone agregar una tabla AlfaCore: si el legacy no posee un campo/catálogo de sectores compatible, primero debe definirse una ampliación compartida y homologarla con ambos clientes.

# Consolidación, edición de precios, IVA y bultos

Pasada del 17/10/2026. Base exclusiva de pruebas: **SERVER-ALFAVB6 / DISTRIWALTERP**. Emisión limitada a homologación. Este incremento amplía los puntos 1, 2, 3, 5 y 6 solicitados; no declara terminado todo su alcance.

## Varios orígenes

El listado permite marcar documentos, incluso entre páginas, y elegir **Cargar selección** en el Context Toolbar. Hasta 30 pedidos o hasta 30 remitos compatibles por factura. No se mezclan NP y RM en este incremento.

Los documentos deben compartir cliente, sucursal del cliente, unidad de negocio, depósito, moneda, lista, clase de precio, vendedor, exención y circuito de stock. El plan confirma además condición de venta, vencimiento, motivo e imputaciones contables. Las reservas y preparación siguen excluidas.

Cada origen conserva su plan, snapshot y cantidades. No se fusionan renglones aunque repitan artículo en documentos diferentes; se muestra su procedencia en la grilla. La ambigüedad de artículos repetidos dentro de un mismo origen sigue rechazada.

`CargaComprobantesConsolidacion` combina los planes y calcula el total sobre todos los renglones. `CargaComprobantesGrabador` relee todos los orígenes dentro de la transacción Serializable, toma bloqueos en orden de ID y actualiza cada pendiente. Los movimientos de stock conservan el origen y el ID de su propio renglón. Se genera una aplicación por origen, con neto de sus líneas y ajuste residual a centavos en la última aplicación. La cabecera consolidada no afirma tener un único TC/documento de origen.

`CargaComprobantesBloqueo` toma los mismos recursos de sesión para emitir y recuperar, en orden estable, y libera adquisiciones parciales si no consigue todos. Una operación pendiente sobre cualquier integrante impide volver a facturarlo. Para bases con compatibilidad SQL antigua se leen y analizan en C# exclusivamente los planes activos; no se exige `OPENJSON` ni se modifica la compatibilidad de SQL Server.

El journal existente conserva las partes en `PlanJson`; no se crean menú ni ruta nuevos. La tabla auxiliar de trazabilidad y su actualización se describen a continuación. El historial muestra la operación por su origen principal. La recuperación se realiza desde esa operación. Queda pendiente una búsqueda específica del historial por cada origen secundario.

### Corrección de pendientes por renglón

La prueba de dos orígenes con el mismo artículo detectó una brecha de la vista oficial: `VT_V_MV_INSUMOS_APLICACION` unía todos los artículos de una factura con cada fila de `MV_APLICACION`. Una entrega de la mitad de cada origen descontaba ambas mitades de cada uno y el trigger los finalizaba anticipadamente. No era suficiente guardar dos aplicaciones ni las referencias de stock.

El script `2026-10-17-001__ventas_carga_comprobantes_trazabilidad_origen.sql` agrega `ALFACORE_VENTAS_CARGA_ORIGEN_LINEA`, con identidad del detalle destino, cabecera origen y detalle origen. No duplica cantidades: se lee la cantidad real del detalle destino. El grabador inserta esas relaciones antes de las aplicaciones, en la misma transacción.

La vista oficial conserva su rama histórica para detalles sin trazabilidad; para los trazados calcula cantidades por origen y requiere la existencia de ambos detalles y la aplicación. Agrupa las dos ramas para conservar su contrato de salida. No agrega FK que bloquee el mantenimiento legacy. La edición posterior de estos consolidados desde VB6 requiere validar la conservación de esas referencias; no se certifica ese flujo por esta migración.

La actualización verifica la definición original antes de reemplazarla. Si encuentra una variante de cliente no relevada, se detiene sin adaptarla automáticamente. El plan impide consolidar si falta la tabla/vista actualizada. Al restaurar otra copia de DISTRIWALTERP es necesario ejecutar las actualizaciones antes de probar consolidaciones.

El 27/10/2026 se relevó además la variante de `ELBOLSON2011_PRUEBAS` (10.8.0.160): nombres de tabla calificados y ausencia del filtro PR/RM. El mismo script reconoce expresamente ambas definiciones y conserva su política: mantiene el filtro donde existía y no lo introduce donde faltaba. No se admite cualquier definición arbitraria ni se salta la actualización. Prueba completa con rollback: 4.826.112 grupos históricos conservados; 247 diferencias de suma binaria `float`, de hasta 4,55×10⁻¹³, sin diferencias mayores a 10⁻⁷. La repetición del script no vuelve a modificar la vista. `ActualizacionOrigenLegacyTests` permite verificar ambas variantes en esta base mediante `ORIGEN_LEGACY_TEST_SQL`, revirtiendo todo el ensayo.

Aplicación efectivamente realizada solo en SERVER-ALFAVB6/DISTRIWALTERP: **84.009 filas históricas comparadas por EXCEPT en ambos sentidos, cero diferencias**, con transacción exterior Serializable; segunda ejecución idempotente correcta. No se alteraron movimientos comerciales para aplicar el script.

## Precios y descuentos

Pedidos y remitos incorporan precio unitario neto, descuento por renglón y Restablecer. Los overrides viajan junto a las cantidades y forman parte de la huella; no modifican el precio del documento original ni el maestro. Simular usa los overrides releídos y validados en servidor.

Los límites de condición de venta y vendedor se toman del **origen**, y los permisos del usuario actual. Se conservan `V_ModificaPrecio`, `V_Descuentos`, `MaxDto` y el control conjunto de rebaja de precio y porcentaje. Se habilita también la edición de artículos en ND. NC conserva su política histórica. Si una factura consolidada repite el mismo artículo en varios renglones, la NC actual rechaza esa ambigüedad; falta seleccionar el renglón de factura a acreditar.

Cambiar cantidades, precio o descuento invalida la validación. Luego de intentar emitir, los editores quedan bloqueados hasta resolver la operación. La acción fiscal aislada no admite consolidaciones ni precios editados: esos casos pasan por la validación y grabación comercial completa.

**Falta:** selección de lista/clase con sus permisos específicos, descuentos globales y sus cuentas, ofertas, escalas, bonificaciones completas, precios libres y recargos. No se habilitan eliminando las guardas actuales.

## IVA y exentos

`CargaComprobantesDesgloseFiscal` cierra importes a centavos y sirve como fuente común de cabecera, asiento, libro y solicitud fiscal. Admite hasta tres alícuotas soportadas por el adaptador ARCA, artículos exentos puros y mezcla de gravados/exentos. Mantiene el orden de alícuotas y la política existente de distribuir un residual de hasta dos centavos en el primer IVA; no cambia la política histórica de cálculo para forzar los 100 documentos retrospectivos.

Cabecera: `ImporteIva`/`AlicIva`, `ImporteIva2`/`AlicIva2`, `ImpIVA3`/`AlicIVA3`. Libro: los tres slots `LIVA_*`; la cuenta contable de IVA recibe su suma. La solicitud y el registro electrónico separan neto gravado, IVA y operaciones exentas. Exento puro no inventa una alícuota cero.

La clave legacy **`ArtExentoaNoGrav`** se consulta y queda reflejada en el plan: SI lleva los artículos exentos a `LIVA_EXENTO`; otro valor los deja en `LIVA_ImpNetoNGrav`, como el original. Se agregó a Configuración → Ventas → Carga de comprobantes con esa explicación. La clasificación del libro no convierte un artículo exento en una operación gravada en ARCA.

Evidencia consultada, en modo lectura:

- `V_mv_vta.frm:12797–12835`: importes y alícuotas de cabecera.
- `V_mv_vta.frm:13992`: lectura de `ArtExentoaNoGrav`.
- `V_mv_vta.frm:16215–16328`: netos, exentos y slots del libro IVA.
- Metadatos reales de las columnas en DISTRIWALTERP.

**Falta:** percepciones, impuestos internos, ITC, IVA de recargo, operaciones no gravadas distintas de exención de artículos, NC exentas históricas y conciliación fiscal completa de todos estos caminos. Las guardas de esos casos siguen activas. No se solicitó un nuevo CAE real por esta ampliación.

## Bultos y cantidades por rubro

La carga directa de FC/FP/ND permite ingresar Bultos. Vacío conserva la cantidad manual. Con bultos informados, el servidor convierte a la unidad de venta del maestro y liquida el precio de esa unidad; la validación muestra la cantidad resultante. No se cambia la unidad de venta ni se asume que el precio es por bulto.

`CargaComprobantesUnidades` lee en lote las equivalencias de los artículos seleccionados desde `S_TA_EQUIV`, y las claves existentes `UNIDADDEMEDIDA_BL`, `UNIDADDEMEDIDA_KG`, `UNIDADDEMEDIDA_M3`. Respeta búsqueda directa/inversa y conversión Currency de cuatro decimales. Rechaza coeficientes inválidos y relaciones duplicadas; no inventa una equivalencia faltante. Calcula además bultos, kilos y m³ cuando existe una relación directa/inversa.

Ejemplo: maestro en UD, relación BL → UD con coeficiente 5. Dos bultos producen 10 UD; detalle comercial `CANTIDAD=10`, `CANTIDADUD=1`; salida de stock de 10 UD; `CANT_BL=2`. El precio continúa siendo por UD. Cambiar el coeficiente después de validar invalida la huella.

Evidencia: `Rt_Stock.bas:236–350` y `354–385`, `ModGlobal.bas:4215`, `V_mv_vta.frm:12431–12442`, `ClsCmnCptes.cls:2553–2593`. La base confirma `S_TA_EQUIV` como tabla y las unidades BL/KG/M3 configuradas.

**Falta:** equivalencias encadenadas especiales de kilos, cambio de unidad/precio por unidad alternativa, medidas variables, lotes y series manuales, perfiles de rubro con vista previa de configuración. No se incorporan balanzas, OCX, reservas ni centros de costos.

## Comparación con el original

El exportador incluye también la sección opcional `TrazabilidadOrigen` y recorre las aplicaciones además de la referencia de cabecera: conserva todos los orígenes y sus detalles, sin duplicarlos. Incluye las claves fiscales, de unidades y descuentos pertinentes. El comparador conserva controles exactos de importes y multiplicidad; se agregó un caso que detecta la pérdida de una aplicación secundaria.

El ejecutable instalado se pudo leer: `NMC_CONT/v_mv_ventas.exe`, versión de archivo **2.02.0409**, modificación 09/10/2026. Esto identifica el binario disponible; no demuestra que todas las fuentes y DLL correspondan a ese binario ni prueba sus grabaciones.

Sigue pendiente ejecutar el mismo caso desde VB6 y AlfaCore partiendo de estados equivalentes, comparar sus capturas y revisar numeración, saldos y auditoría antes/después. Se conservan las 19 diferencias retrospectivas de hasta $0,01 como pendiente de certificación. No se ejecutó el EXE contra una base no confirmada ni se alteraron archivos originales.

Resultados efectivos y pendientes de cada pasada: [registro central](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md). Procedimiento: [comparación de grabaciones](comparacion-grabaciones.md).

## Verificación de la pasada

- Compilación completa de AlfaCore y pruebas, sin errores; permanecen las tres advertencias conocidas ajenas al módulo.
- Regresión completa `FullyQualifiedName~CargaComprobantes`: **112 casos, 105 correctos, 7 opt-in omitidos, cero fallos**, en 1 min 51 s. Incluye SQL con rollback, selección múltiple y parcialidades por origen, precio editado de pedido sin alterar su precio original, exentos con ambas clasificaciones del libro, conversión de bultos, cambio concurrente de equivalencia, origen secundario pendiente/bloqueado, recuperación y UI en 2048/1440/1024.
- Nueve pruebas Python del comparador correctas. La nueva sección de trazabilidad detecta la sustitución de un renglón origen aunque sus importes sean iguales.
- Dos verificaciones adicionales correctas: dos remitos con stock previo sin nueva descarga y exportación de lectura de los cinco documentos conservados. Resultados y ruta de artefactos en el [seguimiento](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md).
- Preview reiniciado con procesos de segundo plano deshabilitados; listado, carga directa y configuración respondieron HTTP 200. Esto verifica disponibilidad, no aceptación autenticada del operador.
- Sin nuevas solicitudes reales de CAE. No se certifica la comparación de grabaciones VB6/AlfaCore.

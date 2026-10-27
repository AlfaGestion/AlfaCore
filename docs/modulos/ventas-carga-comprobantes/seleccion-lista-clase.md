# Selección de lista y clase de precios

Incremento del 18/10/2026. Amplía la carga directa de FC, FP y ND; la NC conserva los precios históricos de la factura asociada. No cambia el motor compartido de precios de Punto de Venta/CRM ni habilita emisión en producción.

## Contrato

- `ListaPrecio = null`: heredar la lista del cliente. `""`: maestro de artículos sin lista. Un código: lista de ventas elegida explícitamente.
- `ClasePrecio = null`: heredar la clase resuelta del cliente/configuración. Se admiten clases 1–8; clase 0 (costo) continúa pendiente.
- Ambos campos se omiten del JSON cuando son nulos, conservando el formato anterior de los borradores y solicitudes sin estas opciones.
- El buscador recibe una copia de cliente, TC y selección de precios; la validación y el grabador vuelven a resolverlos en servidor. El plan conserva la lista y clase efectivas; cabecera/detalle/stock usan los valores del plan.
- Cambiar lista o clase invalida la liquidación y restablece las ediciones manuales de precio/descuento. Las referencias visuales se actualizan al validar. Cambiar cliente restablece ambas selecciones a la herencia.

## Reglas contrastadas en las fuentes

`V_mv_vta.frm`, `ModGlobal.bas`, consultados en modo lectura desde NMC_CONT_DEV:

| Regla | Fuente | Implementación |
|---|---|---|
| Permiso para cambiar lista | `UserModificaListaPrecios`, línea 26050; habilitación de IDLISTA, 7427 | `TA_USUARIOS.ModificaListaPrecios`; nulo no concede permiso; AdminCentral no lo reemplaza |
| Cambiar clase | `ModGlobal.bas:4239`; habilitación CLASEPR, 7422 | `V_ModificaCPrecio`: vacío/NO deshabilitan; se respeta además `CLASEPUSA` |
| Clases definidas y permitidas | `CampoValida`, 17891; `ValidaClasePRe`, 17996 | Existencia de `NOM_PCLASE<n>` y tokens exactos separados por coma en `CLASEPUSA`; vacío permite todas las definidas |
| Vigencia | `ValidaLista`, 17643; validación IDLISTA, 18689 | Lista única de tipo V, desde/hasta inclusivos respecto de la fecha del servidor SQL; nulos sin límite |
| Lista obligatoria | lectura `ListaObligatorio`, 5725 | Impide usar el maestro sin lista si la configuración exige lista |
| Artículo fuera de lista | `ValidaArtLista`, 26075; semántica de `nin` contrastada con `ModGlobal.bas:12565` | FC/ND exigen pertenencia incluso con permiso para cambiar lista. En FP se exige si el usuario no puede modificar lista |

Los tipos y campos se confirmaron en SERVER-ALFAVB6 / DISTRIWALTERP: `TA_USUARIOS`, `V_MA_PRECIOSCAB` y `V_MA_PRECIOS`. No se agregan tablas de configuración.

En Configuración → Ventas → Carga de comprobantes se agregan **Exigir lista de precios** y **Permitir cambiar clase de precio**, sobre las mismas claves legacy. Los permisos individuales siguen en el maestro de usuarios. En la base relevada `V_ModificaCPrecio=NO`: el selector de clase aparece deshabilitado hasta que se habilite esa política. Este incremento no cambia esa configuración persistente.

## Límites y próximo incremento

La selección manual sobre pedidos/remitos, revalorización de remitos, ofertas, escalas y venta al costo no están incorporadas. La selección de clase/lista no evita los bloqueos existentes por configuraciones comerciales todavía no soportadas.

El descuento general sigue pendiente. Se confirmó en `VtaTot.frm:2907` que se calcula después del subtotal, con reducción de netos por alícuota y recálculo de IVA (`2804`, `2912`). `V_mv_vta.frm:12843` conserva porcentaje/importe en cabecera y `15217–15238` agrega su contrapartida al asiento usando `V_TA_INPUT_VENTAS.V_DFI_DESCUENTO`. Antes de habilitarlo se deben cerrar precisión por letra, netos comerciales/fiscales, asiento y herencia proporcional en NC. Los descuentos de cliente/globales continúan bloqueados, sin omitirlos silenciosamente.

Las pruebas efectivas y los pendientes generales se registran en [avances por pasada](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md). La comparación pareada con una grabación ejecutada en VB6 continúa pendiente.

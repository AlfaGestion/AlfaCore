# Edición de precios y descuentos en carga directa

Fecha: 2026-10-15. Alcance implementado: FC directa y FP, artículos simples, precios de lista actuales, moneda local y cuenta corriente. No amplía la habilitación fiscal de homologación.

## Contrato confirmado en el original

| Regla | Evidencia VB6 |
|---|---|
| Permiso de editar precio: `TA_USUARIOS.V_ModificaPrecio`; NULL permite | `V_mv_vta.frm:5397`, `24542–24548` |
| Permiso de descuento: `TA_USUARIOS.V_Descuentos`; NULL permite | `V_mv_vta.frm:5399`, `24502–24506` |
| `ModificaDescArt` permite editar la **descripción**, no el descuento | `V_mv_vta.frm:24488–24492` |
| Límites de condición y vendedor | `V_mv_vta.frm:19896–19908`, `20674–20702`, `21746–21772` |
| Máximo general y de proforma | `MaxDto`, `MaxDtoProforma`, bloque final de `AnalizaPrecioForzado` |
| Descuento de columna se valida contra `gMaxDto` | `V_mv_vta.frm:19481–19491` |
| Precio de referencia neto | Asignaciones de `Aux_PrecioOld` en `19462`, `21465`, `22885` |
| Rebaja efectiva combina precio y porcentaje de línea | `V_mv_vta.frm:21779–21785`, `21810–21840` |

`VentasModificaPrecio` gobierna otros caminos de actualización de precios del original. La edición manual de la celda revisada se autoriza con el permiso del usuario; no se interpreta esa clave como un permiso sustituto. En DISTRIWALTERP se observó `VentasModificaPrecio=NO`, mientras Administrador tiene ambos permisos de edición habilitados. No se cambiaron esos valores.

No hay excepción automática para `AdminCentral`. No se incorpora solicitud de PIN ni autorización para superar límites: se rechaza la edición fuera de alcance.

## Implementación

- `CargaComprobantesDirectaLinea` agrega `PrecioUnitarioNeto` y `Descuento` opcionales. NULL significa usar el sistema, no precio cero. Los atributos JSON omiten los campos NULL para conservar la forma de borradores/journals anteriores sin edición.
- `CargaComprobantesEdicionPrecios` lee permisos del usuario real, condición y vendedor **del cliente actual**, y máximos de `TA_CONFIGURACION`, con fallback de `VALOR` vacío a `ValorAux`. Las consultas usan Dapper y parámetros. No recibe del navegador el precio de referencia ni el límite.
- `CargaComprobantesDirectaPreparador` mantiene la resolución de lista/clase vigente y los bloqueos de artículos especiales. Si hay edición, valida y sustituye el precio neto/descuento; convierte el precio manual a precio con IVA mediante el resolutor compartido. La referencia original se conserva en `PrecioReferenciaNeto` solo cuando se editó.
- El planificador incluye el borrador y la referencia en la huella. Repreparar el plan al emitir/grabar relee permisos, límites, cliente y maestro. El snapshot del artículo también invalida el plan si cambian sus precios. Una restricción posterior al CAE deja la operación pendiente de revisión/recuperación; no autoriza a solicitar otro CAE.
- El grabador compartido conserva precio neto/con IVA antes del descuento, `PorcDto`, descuento unitario, total liquidado y `DtoItem` en stock. Contabilidad y solicitud fiscal reciben el total del motor existente. No se escriben maestros ni configuraciones al editar una venta.

Los permisos del contexto alimentan la presentación; **no son la autorización definitiva**: el servicio relee la fila del usuario al aplicar una edición. Cambiar el contexto enviado por un llamador no permite eludir una revocación.

### Máximos y precisión

Se conserva la rama revisada del original: condición positiva y vendedor positivo toman el menor; vendedor cero deja la condición; ambos cero usan el máximo general. Condición cero y vendedor positivo conserva límite cero, según `CampoValida:19896–19908`; no se inventa un permiso más amplio. Un máximo general positivo limita el resultado. Un cero ausente/vacío no se convierte en ilimitado.

Para FP, el límite de rebaja efectiva usa `MaxDtoProforma`; el porcentaje de columna mantiene el límite derivado de `MaxDto`, como la validación de columna del original. Se controlan ambos. No se puede subir el precio para introducir un porcentaje de columna mayor que su máximo.

La rebaja efectiva considera conjuntamente precio y descuento. Por ejemplo, contra neto de lista 100, precio 95 y descuento 10 % producen 85,50: superan un máximo de 10 %, aunque cada edición aislada parezca admisible. Se conserva el orden de `AnalizaPrecioForzado`: precio neto formateado, descuento de línea, comparación por cantidad y tolerancias/redondeos de 0,1 / uno y dos decimales. Esas tolerancias no son un segundo porcentaje configurable.

El precio manual debe ser positivo, como máximo 99.999.999,9999, con hasta cuatro decimales. El porcentaje admitido en esta etapa va de cero a menos de 100, también con hasta cuatro decimales; se rechazan negativos y exceso de precisión, sin redondearlos silenciosamente. Bonificaciones del 100 %, precios gratuitos y recargos por porcentaje negativo siguen pendientes.

## Interfaz

La grilla conserva cantidad y agrega **Neto de lista**, **Precio neto**, **Dto. %** y **Restablecer**. El precio editable es siempre sin IVA y antes del descuento, también en FP; el motor aplica u omite IVA según la configuración vigente de proforma. La referencia no es editable y la validación la refresca desde servidor.

Los campos respetan permisos y se bloquean durante validación/emisión o cuando la operación debe recuperarse. Se usan los componentes AlfaDesign existentes; los botones de validación/emisión siguen en Context Toolbar. La tabla mantiene desplazamiento horizontal propio a resoluciones pequeñas.

Cada edición invalida el plan y exige validar otra vez. Restablecer elimina ambos overrides y vuelve al precio del sistema. El error conserva el borrador; corregir un rechazo conserva los nuevos campos por la misma copia JSON existente. Cambiar cliente o tipo descarta ediciones manuales para exigir una revisión en el nuevo contexto.

## Verificación

Se agregaron pruebas unitarias de combinaciones de máximos, precio más descuento, permisos independientes, rango/precisión, límites de FP y compatibilidad JSON. Las pruebas SQL en DISTRIWALTERP usan transacciones con rollback y comprueban:

- Precio neto 90, descuento 10 % y cantidad 2: detalle total 196,02 con IVA 21 %, descuento unitario 9, stock con `DtoItem=10` y asiento balanceado.
- Revocar precio o descuento impide escribir, incluso con contexto de administrador y un plan previamente preparado.
- El máximo actual de la condición de venta se aplica; cambiar el precio del maestro invalida el plan validado.

El componente verifica conservación de precio/descuento ante error, invalidación del plan y campos habilitados en Chromium a 2048/1440/1024. Regresión completa posterior al cambio: **84 correctas, 7 opcionales omitidas, 0 fallos**, 91 casos. Preview reiniciado; carga directa HTTP 200. Controles finales de catálogo: ver [registro de esta pasada](../../gestion/ventas-carga-comprobantes-avances-y-pendientes.md).

No se activaron pruebas de emisión real para este incremento ni se modificó la política de cierre/redondeo investigada por separado.

## Pendientes

- Edición de precios/descuentos al facturar NP/RM y en ND; NC conserva los datos de la factura original.
- Selección manual de lista/clase, descuentos globales, ofertas y escalas, y descuentos automáticos de maestros/listas que siguen bloqueados por el preparador.
- Equivalencias, artículos/impuestos especiales y otras capacidades ya fuera del circuito inicial.
- Comparación ejecutada con el binario original y aceptación funcional del operador; este incremento no certifica toda la matriz legacy.

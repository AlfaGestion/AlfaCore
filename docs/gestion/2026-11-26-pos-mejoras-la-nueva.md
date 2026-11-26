# Punto de Venta La Nueva — relevamiento y cambios del 26/11/2026

## Cambios implementados

- El panel de ingreso rápido admite las modalidades `DESHABILITADO`, `COMPACTO` y `COMPLETO` por punto de venta. La modalidad compacta conserva el valor y el modo al mostrar u ocultar el teclado.
- Las observaciones individuales de una NP se editan desde el detalle de cada línea del carrito y se guardan en `V_MV_CPTEINSUMOS.OBSERVACIONES`, la estructura legacy existente. No se creó una tabla AlfaCore.
- Las líneas de NP con observaciones no se agrupan automáticamente con otra línea del mismo artículo, para conservar instrucciones diferentes.
- El catálogo incorpora un editor en modal, con grilla estilo VB6 y detección de hasta 100 artículos más vendidos. La detección usa la vista oficial `VT_DETALLEIVAPROFORMA` de los últimos 12 meses y, al guardar, actualiza la lista legacy `GOUR_TOPVTAS`.
- La actualización `2026-11-26-003__ventas_pos_catalogo_top.sql` amplía `TA_CONFIGURACION.VALOR` a `nvarchar(max)`, conservando la misma clave legacy y evitando una tabla paralela para el catálogo Top.
- Se agregaron scripts idempotentes para la modalidad del panel y para el valor predeterminado de condición de venta de NP cuando la base contiene clientes con condición vacía.

## Equivalencia VB6 verificada

En `C:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\V_mv_vtaPDVG.frm`:

- `CargarConfTopVtas`, aproximadamente líneas 20440–20476, lee `Cfg("GOUR_TOPVTAS")`. La configuración es una lista explícita de artículos separados por `|`; no es una consulta histórica automática de artículos más vendidos.
- `CargarArtGraf`, aproximadamente líneas 20478–20561, carga esa lista como `OptThumb1` para `@TOP@` y carga los artículos por rubro ordenados por `DESCRIPCION` para el resto.
- `OptThumb1_Click`, aproximadamente línea 6202, conserva las ramas de artículo nuevo/existente y de ingreso por importe/cantidad. La integración existente de AlfaCore se preservó.
- `RelacionCBNP`, aproximadamente línea 4130, se habilita con `Cfg("RelacionaCBaNP") = "SI"`.
- `actualizaCBNP`, aproximadamente líneas 11882–11889, vincula el CBCT con la NP mediante `V_MV_CPTE.PRVENTA`.
- El recibo legacy de cobranza se genera después de grabar el CBCT con `GeneraAuxiliarRecibo_PagoCobranza("CBCT", ...)` e `ImprimirComprobante`, aproximadamente líneas 9863–9872 de `V_mv_vtaPDVG.frm` y 8911–8922 de `em_V_mv_vta_npfp.frm`.

## Pendientes con evidencia

- `GOUR_TOPVTAS` conserva su alcance global legacy: el editor de AlfaCore muestra y guarda la misma lista que utiliza VB6. Los rubros continúan siendo una configuración independiente del punto de venta; no se repiten dentro del editor de artículos Top.
- La impresión específica del recibo de anticipo todavía debe conectarse al renderizador/reporte de `CBCT` existente. La creación del anticipo y su acción `A` ya usan el circuito real; no se agrega una impresión ficticia ni se registra nuevamente la cobranza ante un error de impresión.
- La preparación, entrega, sectores y transformaciones de NP requieren continuar el relevamiento de las rutinas que escriben `MV_PREPARACION` y `MV_PREPARACION_DETALLES`; en esta etapa no se habilitaron escrituras nuevas.

## Validaciones

- `dotnet build src/AlfaCore/AlfaCore.csproj --no-restore --nologo -v:minimal`: correcto, 0 errores; permanecen advertencias preexistentes de nulabilidad y métodos sin `await`.
- `python tools/catalogo/check_catalogo.py`: correcto, 143 rutinas relevadas, 0 advertencias y 0 errores.
- `git diff --check`: correcto; Git solo informa la conversión normal de fin de línea de un archivo Razor.
- No se realizaron escrituras de prueba en producción, commits ni push.

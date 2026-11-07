# Reportes contables: identidad de movimientos legacy

La PK verificada de `MV_ASIENTOS` en FERNANDEZF2 incluye CUENTA, SECUENCIA, MES_OPERATIVO, NUMERO ASIENTO, TC, SUCURSAL, NUMERO, LETRA, PERIODO y TIPO_REG. Mes y número no identifican por sí solos un asiento: pueden repetirse entre subdiarios y también entre comprobantes del mismo subdiario.

El control de desbalances de Sumas y saldos agrupa los documentos identificados por período, mes operativo, tipo de registro y comprobante completo (TC, sucursal, número y letra). El legacy puede haber grabado líneas del mismo documento con números de asiento diferentes: en ese caso se reúnen, como en el visor. Los movimientos sin TC/número de comprobante conservan además el número de asiento en la agrupación. El tipo y comprobante mostrados salen de esa misma identidad; no se eligen con `MIN` de un conjunto de comprobantes distintos. La pantalla y el Excel incluyen el subdiario.

Libro diario usa la misma separación al armar los grupos. Sus filtros de subdiario y diario general delimitan la identidad completa: un recibo CB no puede hacer que una factura con el mismo número entre en el subdiario de cobranzas o desaparezca del diario general.

Para mostrar el número de asiento se prefiere el de la cabecera con CABCUENTA; si no existe se usa el mayor del grupo. Los totales monetarios, los filtros de período/unidad/proformas y el umbral de medio centavo para señalar un desbalance se conservan. No se modifican movimientos ni se compensan diferencias reales.

## Regresión verificada

En FERNANDEZF2, ejercicio 16, enero de 2026, el número de asiento 1 aparece en distintos documentos. `CBCT 0001-00001412 B` tiene dos líneas por $45.600 y diferencia cero. El grupo anterior por mes/número mezclaba 24 líneas filtradas y le asignaba -$10.000 al CBCT; esa diferencia provenía de dos líneas de `FC 0015-00018923 B` grabadas con número de asiento 1. Su línea del cliente tiene número 1018: las tres líneas juntas también balancean. Ambos documentos deben quedar fuera de las observaciones.

La prueba de solo lectura comprueba la identificación correcta del desbalance, los totales contra SQL, el detalle exportado a Excel, las dos líneas del CBCT en el diario y los filtros de subdiario CB/diario general. Requiere `ALFACORE_TEST_SQL` hacia FERNANDEZF2 y `ALFACORE_IVA_FERNANDEZF2=1`.

## Archivos

- `src/AlfaCore/Services/ContabilidadReportesService.cs`: agrupación del diagnóstico y columna de subdiario en el Excel.
- `src/AlfaCore/Services/ContabilidadReportesService.Libros.cs`: agrupación y filtros del diario por identidad completa.
- `src/AlfaCore/Models/ContabilidadReportesModels.cs`: subdiario del asiento observado.
- `src/AlfaCore/Components/Shared/ContabilidadReportePanel.razor`: identificación del subdiario en el aviso.
- `src/AlfaCore/Docs/manual_contabilidad.md`: explicación funcional.
- `tests/AlfaCore.Tests/ContabilidadIdentidadAsientoTests.cs`: regresión contra la base indicada, sin modificar datos.

# Primera autorización electrónica en homologación

Este documento conserva la evidencia del primer ensayo fiscal aislado. La continuación comercial está en [Grabación comercial en homologación](grabacion-comercial-homologacion.md); no convierte retrospectivamente esta autorización en una FC comercial.

Fecha: 2026-10-14. El usuario autorizó probar una factura electrónica en homologación, punto de venta 1007, usando el último pedido disponible. Se implementó y ejecutó una prueba de autorización WSFE; todavía no equivale a la grabación comercial completa de Alfa Gestión.

## Resultado registrado

| Dato | Resultado |
|---|---|
| Base | DISTRIWALTERP |
| Ambiente | HOMOLOGACION |
| Origen | NP 000400008674X, unidad de negocio 4 |
| Tipo ARCA | 1, Factura A |
| Punto/número | 1007 / 185 |
| Importe autorizado | 113.798,74 |
| CAE | 86410998448850 |
| Vencimiento | 2026-10-24 |
| Estado | APROBADA |
| Operación | f25166b6-e0ec-4d91-a2a1-e7f432aa00fd |

La autorización proviene de una llamada real a WSFE en homologación. Se repitió la consulta a la misma operación y se obtuvo el mismo resultado sin otro envío. Se verificó que las cantidades pendientes del origen y los recuentos de cabeceras comerciales, asientos, stock y aplicaciones no cambiaron.

El resultado queda en `ALFACORE_VENTAS_HOMO_PRUEBA`, con solicitud/respuesta fiscal e identidad de operación. No se insertó una FC en `V_MV_CPTE`, no se movió stock ni se generaron asientos o aplicaciones. Esta separación mantiene disponible el pedido mientras se completa el contrato comercial legacy. La pantalla lo informa en la confirmación y en el resultado; no presenta esta prueba como circuito comercial certificado.

El importe histórico del pedido es 113.798,48 y la liquidación de prueba autorizada es 113.798,74: diferencia de 0,26. La aprobación por ARCA verifica el formato/desglose enviado, no la equivalencia con una factura emitida desde el mismo pedido por VB6. Ese caso se incorpora al trabajo pendiente de comparación; no se modificó el histórico para hacerlo coincidir.

## Configuración y certificado

Se comprobó `USAFCELECTRONICA=SI`, `ARCA_AMBIENTE=HOMOLOGACION`, `PV_EFACTURA=1007` y los endpoints de homologación en la base. La unidad 4 utiliza el emisor global. La unidad 3 tiene electrónica propia con punto 1010: no se fuerza a 1007 ni se sobrescribe su configuración.

AlfaCore tenía `ARCA_CERTIFICADO` vacía; VB6 usaba archivos configurados en `RUTA_CRT` y `RUTA_ARCHIVOPRIVADA`. Se importaron en memoria a la fila GLOBAL del almacén existente el certificado `alfavb6.crt` y su clave correspondiente, desde la carpeta de pruebas de la instalación. Se comprobó vigencia hasta 2026-10-31, correspondencia criptográfica entre certificado/clave y CUIT del emisor. Los archivos de la carpeta compartida se conservaron en lectura. No se copiaron certificados, claves privadas ni tickets a Git/documentación.

Se detectó un fallo real de DNS contra el default `wsaa.homo.arca.gov.ar`. Se corrigió el default de homologación al endpoint publicado en la [documentación oficial de WSAA](https://www.arca.gob.ar/ws/documentacion/wsaa.asp): `https://wsaahomo.afip.gov.ar/ws/services/LoginCms`. También se incorporaron `WSAA_URL` y `WSFE_URL` al lector central, que antes omitía esas claves aunque el resolutor intentaba usarlas. No se alteraron las URLs guardadas en la base.

## Implementación y límites

- `CargaComprobantesElectronicaVerificador`: comprueba homologación/punto/endpoints, autentica y consulta numeración. Nunca solicita CAE.
- `CargaComprobantesElectronicaEmisor`: prepara el desglose desde el calculador y cliente fiscal actual, bloquea la operación por emisor/punto/tipo, consulta número y persiste ENVIANDO antes de pedir CAE. Guarda APROBADA, RECHAZADA o INCIERTA después.
- `CargaComprobantesService`: aplica las guardas de sesión/permisos y reconsulta el origen; consulta también el último resultado para mostrarlo al cargar el pedido.
- `2026-10-14-003__ventas_homologacion_pruebas.sql`: tabla operativa nueva, sin certificados/tickets, guardia idempotente e índice único de número por emisor/punto/tipo. Se ejecutó dos veces sin duplicar objetos. No es una tabla de configuración.
- Página: acciones **Verificar electrónica** y **Emitir prueba ARCA**; la segunda requiere simulación compatible y diagnóstico previo. Confirmación explícita del efecto de prueba. Un resultado existente se recupera al cargar el origen.

Primer alcance de prueba: moneda local, productos gravados, sin percepciones, ajustes globales, internos, ITC, IVA recargo ni circuitos adicionales del origen. Se resuelve receptor A/B desde el cliente actual. No se habilita producción ni se garantiza equivalencia comercial total.

Una autorización incierta no se reenvía: el registro queda bloqueando nuevas pruebas del mismo emisor/punto/tipo. La consulta y conciliación automática de ese número, el historial completo de pruebas y la grabación comercial siguen pendientes. Una recarga de página no debe interpretarse como permiso para reenviar una operación desconocida.

## Pruebas y exportación

La prueba de emisión real requiere `ALFACORE_TEST_ARCA_EMITIR=1`, conexión de la base de pruebas y `ALFACORE_ARCA_OPERACION` con un GUID estable. Se omite por defecto, incluso al ejecutar la suite completa, para evitar emisiones involuntarias. Una nueva prueba intencional usa otra operación; un reintento de la misma conserva su GUID.

`ALFACORE_TEST_ARCA_LECTURA=1` habilita autenticación y consulta de números sin emisión. La exportación de una autorización existente usa `ALFACORE_ARCA_DOCUMENTO_OP`; genera HTML/PDF en `artifacts/ventas/homologacion/`, carpeta ignorada por Git. No vuelve a solicitar CAE. El documento exportado es un resumen de la autorización fiscal de prueba, no una factura comercial completa con todos los renglones.

Se agregaron pruebas de bloqueo de producción/endpoints/punto incorrecto, ausencia de solicitudes de CAE en el diagnóstico, igualdad exacta entre total/neto/IVA, llamada real a WSAA/WSFE, idempotencia de una operación aprobada y ausencia de efectos comerciales. La comparación histórica de cálculo sigue pendiente, independiente de la aceptación WSFE.

Validación final: 26 pruebas aprobadas en la suite con SQL, WSAA/WSFE de lectura, Chromium y exportación PDF. La prueba de emisión se omitió en esa repetición para no emitir otra factura; ya había pasado separadamente con la operación autorizada. Se repitieron las dos pruebas de UI/servicio después de agregar la recuperación del resultado, ambas aprobadas. Catálogo: 84 rutinas, sin errores ni advertencias; encoding y `git diff --check` correctos.

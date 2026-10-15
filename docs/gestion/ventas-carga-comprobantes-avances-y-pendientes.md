# Carga de Comprobantes: avances y pendientes

Última actualización: 2026-10-15. Base de pruebas: DISTRIWALTERP. Ambiente fiscal habilitado en este desarrollo: **homologación**.

## Cómo continuar en cada pasada

Por pedido del usuario, al cerrar cada pasada de este módulo se debe actualizar este registro, incluso si el trabajo quedó parcial:

1. Registrar qué cambió y qué quedó utilizable, con su alcance concreto.
2. Indicar las verificaciones efectivamente ejecutadas, sus resultados y las que faltan. Separar implementación, prueba automatizada, emisión real de homologación y aceptación del operador.
3. Actualizar pendientes y prioridades; conservar los que todavía no se resolvieron.
4. Agregar una entrada fechada al historial con decisiones, limitaciones y siguiente paso. Si hay varias pasadas el mismo día, agregar entradas distintas.
5. Actualizar el documento técnico o manual correspondiente si cambió el comportamiento. Este registro enlaza la evidencia; no reemplaza esos documentos.

No marcar como certificado un circuito por haber localizado su código VB6 o por aprobar un único caso. No guardar contraseñas, certificados ni conexiones con credenciales en la documentación.

## Estado utilizable

| Circuito | Avance comprobado | Límite actual |
|---|---|---|
| Pedidos y remitos | Consulta de pendientes, filtros con Smart Search, cantidades parciales, validación, grabación, recuperación y PDF; factura comercial de homologación A 1007-186 conservada | Un origen por operación y combinaciones admitidas por el validador; consolidación pendiente |
| Factura directa | Cliente y artículos, precios de lista resueltos en servidor, edición de precio neto/descuento con permisos y límites, validación previa, CAE, grabación, historial y PDF; A 1007-187 conservada | Artículos simples, moneda local, cuenta corriente y una alícuota; edición manual ensayada con rollback, sin nueva emisión real |
| Nota de crédito electrónica | Asociada a factura del módulo, precio histórico, límite de cantidades acreditables, asiento y stock inversos según talonario; A 1007-162 conservada | Factura asociada aprobada en homologación del módulo; no cubre todas las notas del legacy |
| Nota de débito electrónica | Asociada a factura, CAE, aplicación, asiento, stock según talonario y PDF; A 1007-2 conservada | Carga de artículos; conceptos financieros y servicios pendientes |
| Proforma | X 0004-6478 conservada, asiento balanceado, sin libro IVA ni CAE; cálculo con/sin IVA según configuración probado con rollback | Circuito directo acotado; asiento obligatorio por requerimiento del usuario |
| Recuperación | Identidad estable de operación/borrador, consulta de estado y recuperación sin duplicados verificadas | Una respuesta incierta exige recuperar la operación; no iniciar otra emisión para el mismo intento |
| Interfaz | Shell AlfaDesign, contexto SaaS, aislamiento `?directo=1`, filtros al estilo Clientes y revisión visual en 2048/1440/1024 | Falta aceptación funcional del usuario en su uso habitual |

Accesos locales, sujetos a que el preview esté iniciado:

- Listado: <http://localhost:5055/ALFANET/153/ventas/carga-comprobantes>.
- Carga directa: <http://localhost:5055/ALFANET/153/ventas/carga-comprobantes/nuevo>.
- Configuración: <http://localhost:5055/ALFANET/153/configuracion-general/ventas?tab=carga-comprobantes>.

La aplicación permite probar estos circuitos acotados. **No está habilitada la emisión en producción ni certificada la equivalencia general de grabación con Alfa Gestión.**

## Pendientes priorizados

| Prioridad | Trabajo | Criterio de cierre |
|---|---|---|
| 1 | Ejecutar comparación controlada VB6/AlfaCore de FC desde pedido, FC desde remito, FC directa, NC, ND y FP; exportador y comparador preparados | Mismo estado inicial en copias equivalentes; comparar cabecera, detalle, precios/redondeos, stock, asiento, libro IVA, aplicaciones, numeración y estados; explicar cada diferencia |
| 1 | Contrastar el cierre con ejecutable/configuración antes de declarar equivalencia | Motor actual: 81 coincidencias y 19 diferencias de IVA/total de hasta 0,01, sin diferencias de netos. Modelo diagnóstico completo para esta muestra: sin ajuste final 100/100; con ajuste final 81/100. Falta ejecución pareada; no aplicar automáticamente la política histórica al grabador |
| 1 | Completar aceptación funcional del operador | Probar filtros, parcialidades, edición, validación, emisión, PDF y recuperación; registrar resultado por circuito. Pendiente de disponibilidad del usuario, sin impedir preparar la comparación técnica |
| 2 | Completar precios y descuentos | Edición manual de neto/porcentaje en FC directa y FP implementada con permisos y límites; pendientes NP/RM, ND, selección de lista/clase, descuentos globales, ofertas y escalas, con pruebas originales |
| 2 | Ampliar impuestos y conceptos fiscales | Varias alícuotas, exenciones, percepciones e impuestos aplicables con evidencia VB6, prueba contable/fiscal y conciliación electrónica; no omitirlos ni asumir cero |
| 2 | Facturar varios pedidos/remitos en una operación | Selección múltiple, compatibilidad de cliente/contexto, parciales por línea, trazabilidad de todos los orígenes y control de concurrencia sin sobrefacturar |
| 3 | Cubrir particularidades de los rubros priorizados | Unidades/bultos/equivalencias y otras capacidades efectivamente usadas por papeleras, mayoristas y distribuidoras; habilitación por configuración confirmada |
| 3 | Extender notas y formas de cobro | Notas sobre documentos legacy, conceptos financieros/servicios y contado/cobranza, cada circuito con contrato y pruebas propios |
| 3 | Materializar perfiles configurables por rubro | Capacidades y presentación reutilizables, configuración central y vista previa de cambios; no crear motores separados por cliente |
| Transversal | Medir rendimiento y revisar recursos SQL | Comparativas reproducibles con volúmenes representativos. Se observaron esperas `RESOURCE_SEMAPHORE`; las optimizaciones locales no equivalen a resolver la capacidad del servidor |
| Antes de producción | Cerrar alcance certificado y preparar habilitación | Circuitos aceptados, diferencias resueltas/documentadas, regresión correspondiente, configuración fiscal revisada y autorización para habilitar producción |

La próxima pasada debe avanzar en facturación de varios pedidos/remitos compatibles, primero definiendo selección, aplicaciones y cantidades por línea de cada origen según los objetos oficiales y el original. La primera edición manual de FC/FP ya está implementada; quedan las ampliaciones de precios indicadas en la tabla. El diagnóstico del cierre cubre los 100 casos de la muestra; no repetirlo sin nueva evidencia ni cambiar la configuración para forzar históricos. La comparación ejecutada sigue pendiente: confirmar versión/configuración del original, obtener capturas pareadas y completar controles antes/después de saldos, numeración y auditoría. No emitir nuevos documentos solo para repetir una verificación ya conservada.

## Fuera de alcance acordado

Se mantienen excluidos balanzas/OCX, centros de costo, reservas y el circuito separado de autorización para emitir. Esto no elimina permisos de acceso ni validaciones comerciales/fiscales. No reintroducir estas capacidades como pendientes obligatorios sin un cambio de alcance del usuario.

Las carpetas originales VB6 y del ejecutable instalado se consultan en modo lectura. La base autorizada para modificaciones y pruebas es DISTRIWALTERP; no trasladar esa autorización a otras bases o a producción.

## Historial por pasada

### 2026-10-15 — Consolidación del estado y continuidad

**Trabajo de esta pasada.** Se creó este registro central por pedido del usuario, se ordenaron avances y pendientes y se enlazó desde el README técnico. Se corrigió en ese README la descripción desactualizada que solo mencionaba consulta/simulación. No se modificó el código funcional ni se emitieron comprobantes en esta pasada.

**Evidencia heredada del incremento anterior.** Se conservan FC A 1007-187, NC A 1007-162, ND A 1007-2 y FP X 0004-6478, con cuatro asientos balanceados. Los tres electrónicos tienen CAE de homologación; FP no tiene CAE ni registro electrónico ni campos de libro IVA. NC/ND se conciliaron con su factura asociada. Repetición y recuperación devolvieron los documentos existentes.

La última regresión completa registrada obtuvo **45 correctas, 6 omitidas por activación explícita y 1 fallo por timeout** en la consulta auxiliar de un test. Se optimizó esa consulta auxiliar por lotes y su reejecución pasó; ambas pruebas de interfaz también pasaron en reejecución. El circuito real FC/NC/ND/FP pasó por separado con activación explícita. No describir estos resultados como una nueva corrida completa sin fallos después de la corrección.

**Verificación de esta pasada.** Enlaces locales correctos y ambos documentos en UTF-8 sin BOM, sin mojibake. `git diff --check` sin errores. Catálogo: 91 rutinas, 0 advertencias y 0 errores. No se repitieron las pruebas funcionales ni las emisiones por tratarse de cambios de documentación.

**Pendiente y siguiente paso.** Comparación controlada con el original y explicación de las diferencias de centavos, según prioridades anteriores. Aceptación funcional del usuario todavía pendiente. No se amplió el alcance ni se certificaron configuraciones adicionales.

### 2026-10-15 — Diagnóstico detallado y comparador de grabaciones

**Avances.** Se amplió el diagnóstico histórico para conservar los 100 casos, entradas por línea, configuración actual y diferencias. Se agregó un exportador Dapper de lectura y un comparador local exacto de once secciones, incluyendo origen, stock, asiento, aplicaciones y electrónica. Las exclusiones de columnas son explícitas y quedan registradas. No se modificaron la pantalla, el motor monetario ni las configuraciones.

**Resultado.** Se capturaron los cinco documentos AlfaCore ya conservados: FC 186/187, NC 162, ND 2 y FP 6478. La nueva lectura mantiene 79 coincidencias y 21 diferencias históricas. En las 21, IVA guardado coincide con neto guardado por tasa; en 17 el neto recalculado también coincide a dos decimales. Es un patrón para investigar, sin demostración todavía de la rama VB6 ejecutada. Cuatro casos requieren explicar también el neto.

**Pruebas.** Compilación de pruebas correcta (dos advertencias preexistentes de eventos no usados); 14 pruebas de cálculo/diagnóstico correctas y exportador SQL correcto para los cinco documentos (15 verificaciones .NET seleccionadas). Ocho pruebas Python del comparador correctas. Autocomprobación de las cinco capturas: verifica la herramienta, no equivalencia entre programas. Controles CLI: salida 1 para documentos distintos y salida 2 al intentar sobrescribir una entrada. Catálogo: 91 rutinas, cero advertencias/errores; enlaces locales, UTF-8 sin BOM y `git diff --check` correctos. Artefactos confirmados como ignorados por Git. Sin nuevas emisiones, sin cambios de movimientos comerciales ni archivos legacy.

**Faltantes.** Ejecución pareada VB6/AlfaCore, correspondencia de binario/fuentes, valores intermedios del cálculo y controles antes/después de pendientes, numeración y auditoría. Capturar el estado actual no prueba todos los efectos indirectos del original. Aceptación funcional y resto del backlog siguen pendientes.

**Siguiente paso.** Instrumentar/reproducir un caso de diferencia en entornos de pruebas equivalentes y utilizar las capturas para comparar sus efectos. Ver [procedimiento, archivos y límites](../modulos/ventas-carga-comprobantes/comparacion-grabaciones.md). Los artefactos identificables permanecen en `artifacts/ventas/comparacion/`, ignorado por Git.

### 2026-10-15 — Corrección de IVA por cantidad y reproducción del cierre

**Avances.** Se siguió el primer caso con diferencia en las fuentes y se encontró una conversión Currency prematura en AlfaCore: `clsTotales.ImporteIVA` retorna Double y se convierte después de multiplicar por cantidad. Se corrigió ese paso del motor compartido y se agregaron dos pruebas que fallaban antes del cambio. Se implementó además un modelo de cierre exclusivamente diagnóstico con seis pruebas, distinguiendo letra A/B y ajuste final. El reporte histórico ahora conserva fecha y ambos recorridos por caso.

**Resultado.** En los mismos 100 documentos: 81 coincidencias completas, 19 diferencias de IVA/total de hasta 0,01 y **ninguna diferencia de netos** (antes había cuatro). Los 19 documentos con diferencia son de noviembre de 2024. El cierre diagnóstico sin ajuste final coincide en los 89 casos simples compatibles; los 11 restantes requieren otras capacidades, como descuentos. Las fuentes actuales agregaron un ajuste final en octubre de 2026. Falta demostrar el camino ejecutado en el original; no se modificó la configuración para reproducir históricos ni se conectó el modelo diagnóstico al circuito comercial.

**Pruebas.** Compilación correcta y regresión completa posterior al cambio: **54 correctas, 7 omitidas por activación explícita, 0 fallos** (61 casos, aproximadamente 84 segundos). Incluye cálculo, ensayos SQL con rollback, UI, recuperación, notas, proforma y SOAP. No se activaron nuevas emisiones reales. Preview reiniciado con procesos de segundo plano deshabilitados; carga directa HTTP 200. Catálogo: 91 rutinas, cero advertencias/errores; `git diff --check` correcto. Once archivos verificados en UTF-8 sin BOM, sin mojibake; enlaces locales y JSON correctos. Artefactos de evidencia confirmados como ignorados por Git.

**Faltantes.** Comparación pareada ejecutada VB6/AlfaCore, correspondencia binario/fuentes/configuración y validación del cierre por letra, además de descuentos y resto del backlog. Los documentos ya emitidos se conservaron. Una operación previamente aprobada que todavía requiera grabación debe respetar su solicitud/snapshot original; si una corrección de cálculo cambia la huella, requiere revisión y no otra solicitud de CAE.

**Siguiente paso.** Contrastar el cierre en el ejecutable original y completar el caso con descuento; conservar la separación entre precisión aritmética corregida y política de cierre pendiente de certificar. Ver [evidencia y ejemplo numérico](../modulos/ventas-carga-comprobantes/comparacion-grabaciones.md#corrección-de-precisión-y-cierre-reproducible-del-1510).

### 2026-10-15 — Cierre diagnóstico con descuentos y exentos

**Avances.** Se confirmó en fuentes la diferencia entre descuento por artículo y descuentos globales; el cierre no vuelve a aplicar el primero. Se amplió el modelo diagnóstico con exentos puros y mezcla de gravados/exentos, con netos separados. La comparación verifica explícitamente neto gravado, no gravado, IVA y total. Se agregaron once casos de prueba y un resumen agregado sin datos identificables.

**Resultado.** El modelo diagnóstico sin ajuste final coincide en **100 de los 100 históricos**; con ajuste final coincide en 81. La muestra incluye cinco documentos con descuentos por artículo y seis con exenciones. El motor comercial conserva sus 81 coincidencias/19 diferencias: esta pasada no cambió su cierre ni las configuraciones. El resultado numérico todavía no certifica ejecutable, versión ni efectos completos de grabación.

**Pruebas.** Compilación de pruebas correcta, con dos advertencias preexistentes de eventos no usados. Las **33 pruebas seleccionadas pasaron**, sin fallos ni omitidas; incluyen diagnóstico SQL sobre la muestra. No se repitió la suite comercial completa porque los cambios fueron en diagnóstico y pruebas. Sin nuevas emisiones ni modificaciones de movimientos/archivos legacy. Catálogo: 91 rutinas, cero advertencias/errores; `git diff --check` correcto. Seis archivos verificados en UTF-8 sin BOM, sin mojibake/espacios finales, con enlaces locales y JSON correctos; artefacto identificable ignorado por Git.

**Faltantes.** Comparación pareada ejecutada y correspondencia del binario/configuración; aceptación funcional del usuario y resto de capacidades del backlog. Los documentos electrónicos exentos, precios y descuentos manuales no se habilitan por haber ampliado un modelo de pruebas.

**Siguiente paso.** Preparar la edición funcional de precios/descuentos respetando permisos y límites originales, manteniendo validación en servidor y recuperación. La evidencia de cierre queda lista para contrastar el ejecutable; no repetir análisis histórico sin nueva evidencia. Ver [ampliación y resultados](../modulos/ventas-carga-comprobantes/comparacion-grabaciones.md#ampliación-posterior-descuentos-y-exentos).

### 2026-10-15 — Edición manual de precio y descuento en FC directa / FP

**Avances.** Se agregaron precio unitario neto, descuento de renglón y Restablecer en la grilla AlfaDesign. Se validan `V_ModificaPrecio` y `V_Descuentos` (NULL permite, como el original), límites de condición/vendedor y `MaxDto`/`MaxDtoProforma`. La rebaja de precio y el porcentaje se controlan juntos, además del tope de la columna. No hay excepción por AdminCentral ni PIN para superar el máximo. `ModificaDescArt` corresponde a descripción; se corrigió su interpretación en la matriz.

**Integridad.** Los overrides y referencia resuelta en servidor forman parte del plan; cambiar los campos exige revalidar. Se releen permisos y datos al grabar, y se conserva el bloqueo si cambiaron después del CAE. Precio, porcentaje, descuento unitario, total y stock/asiento utilizan el grabador existente. Los NULL nuevos se omiten en JSON para mantener compatibilidad con borradores anteriores sin edición. No se modifican maestros/listas al editar una venta.

**Pruebas.** Primera selección: 21 correctas, incluyendo UI y SQL con rollback. Regresión completa final: **84 correctas, 7 opcionales omitidas, 0 fallos** (91 casos, unos 73 segundos). Se verificó grabación simulada de neto 90, descuento 10 % y cantidad 2: total 196,02, descuento unitario 9, stock con descuento y asiento balanceado. Revocar permisos, bajar el máximo de condición o cambiar el maestro invalida la edición/plan. Chromium verificó 2048/1440/1024 y campos habilitados; error conserva ediciones. Compilación correcta con tres advertencias preexistentes. Preview reiniciado y carga directa HTTP 200. Sin nuevas emisiones reales; los cambios de usuarios, condición y artículo de las pruebas terminaron en rollback. Catálogo: **92 rutinas**, cero advertencias/errores; `git diff --check` correcto. Dieciséis archivos verificados en UTF-8 sin BOM, sin mojibake y con enlaces locales correctos.

**Faltantes.** Edición en NP/RM y ND, descuentos globales, cambio de lista/clase, ofertas/escalas y descuentos automáticos especiales siguen pendientes. NC conserva el precio/descuento original. Siguen pendientes la aceptación del operador y la comparación ejecutada de la grabación con VB6.

**Siguiente paso.** Facturar varios pedidos/remitos compatibles, conservando parciales y trazabilidad de cada origen. El manual funcional y el [contrato técnico de edición](../modulos/ventas-carga-comprobantes/edicion-precios-descuentos.md) quedaron actualizados.

## Evidencia y referencias

- [Índice técnico del módulo](../modulos/ventas-carga-comprobantes/README.md).
- [Matriz de compatibilidad](../modulos/ventas-carga-comprobantes/matriz-compatibilidad.md).
- [Comparación de grabaciones y diagnóstico detallado](../modulos/ventas-carga-comprobantes/comparacion-grabaciones.md).
- [Carga directa, notas y proformas: implementación y pruebas](../modulos/ventas-carga-comprobantes/carga-directa-notas-proformas.md).
- [Edición de precios/descuentos y límites](../modulos/ventas-carga-comprobantes/edicion-precios-descuentos.md).
- [Grabación comercial desde orígenes y recuperación](../modulos/ventas-carga-comprobantes/grabacion-comercial-homologacion.md).
- [Ejecutable original y llamadas del menú](../modulos/ventas-carga-comprobantes/arranque-ejecutable-menu.md).
- [Manual funcional](../../src/AlfaCore/Docs/manual_carga_comprobantes.md).

Los resultados y PDF reales se conservan localmente en `artifacts/ventas/homologacion/`, carpeta ignorada por Git. Los identificadores estables y las instrucciones para repetir consultas sin emitir otros comprobantes están en el documento técnico de carga directa.


## Pasada 2026-10-15 — configuración de ventas y cliente/CUIT

### Implementado

- Configuración General → Ventas incorpora «Carga de comprobantes», con 35 claves confirmadas, agrupadas e indicando alcance, permisos y funciones todavía pendientes. Acceso directo: `/ALFANET/153/configuracion-general/ventas?tab=carga-comprobantes`.
- Edición sobre `TA_CONFIGURACION`, únicamente claves modificadas, transacción y auditoría central, validación por tipo, versión para conflictos concurrentes y revalidación de administrador central y base activa. No cambia Portal Cliente/ARCA/POS/Mercado Pago al guardar esta pestaña.
- Inventario consultable de claves del VB6 sin exponer valores sensibles ni presentarlas como implementadas. `USAPROFORMA` (POS) y `UsaFacturaProforma` (Carga/legacy) son claves diferentes.
- Corregida la búsqueda de cliente: el evento de selección nula de cada tecla ya no borra el texto enlazado. Misma corrección en búsqueda de factura asociada. CUIT normalizado con o sin guiones/puntos/espacios; comodines como texto literal; coincidencia exacta prioritaria.
- Acciones «Buscar cliente» y «Nuevo cliente / CUIT» en carga directa reutilizan el modal, padrón y alta del POS. Cliente real, sin cuenta eventual; volver a seleccionar invalida la liquidación.
- Corregido `ModuleKey` de la pantalla de configuración para no conservar el header de otro módulo.
- Detalle técnico en [configuración y clientes](../modulos/ventas-carga-comprobantes/configuracion-y-clientes.md); instrucciones en el [manual](../../src/AlfaCore/Docs/manual_carga_comprobantes.md).

### Validación y pendientes de esta pasada

- Compilación de aplicación y tests: correcta, sin errores; tres advertencias existentes ajenas a esta entrega.
- Regresión `FullyQualifiedName~CargaComprobantes` sobre DISTRIWALTERP: **100 pruebas, 93 aprobadas, 7 opcionales omitidas, 0 fallidas** (75 s). Las emisiones y consultas fiscales reales opt-in permanecieron deshabilitadas.
- Configuración: render del panel, inventario embebido, tipos/rangos/claves permitidas, escritura de `MaxDto` y auditoría dentro de transacción, preservación de otra clave, conflicto de versión, revocación de administrador y rechazo de otra sesión. Rollback verificado por la versión original: no se dejó cambiada la configuración.
- Cliente: escritura real del texto «papelera» mediante el handler del lookup, conservación del texto, apertura desde el header de Nuevo cliente/CUIT, consulta de padrón y alta mediante servicios simulados, selección de regreso al borrador. Consulta SQL real por CUIT con y sin guiones, y búsqueda vacía sin resultados. No se creó un cliente real ni se pidió otro CAE.
- Las pruebas UI existentes revisaron carga directa y filtros en 2048/1440/1024. El nuevo panel tiene prueba de render y layout adaptable; queda pendiente su revisión visual autenticada por el operador.
- `python tools/catalogo/check_catalogo.py`: **93 rutinas, 0 advertencias y 0 errores**. `git diff --check`, encoding UTF-8 sin BOM y enlaces de documentación revisados.
- Preview reiniciado en localhost:5055 (proceso lanzador 61412); configuración con `?tab=carga-comprobantes` y carga directa respondieron HTTP 200. Esto verifica disponibilidad HTTP, no aceptación funcional con sesión autenticada.

Queda pendiente aceptación del operador y ampliar gradualmente el editor a otras configuraciones confirmadas; el inventario legacy no certifica soporte funcional. Se mantienen los pendientes generales anteriores, incluidas comparación pareada VB6/AlfaCore, impuestos avanzados y múltiples orígenes.


## Cambio de foco 2026-10-15 — Configuración y reportes

Por pedido del usuario se dejó en espera el desarrollo de Carga. Esta pasada agregó búsqueda de campos en su pestaña de configuración, sin modificar el circuito de emisión ni sus parámetros. Compras, Reporte de Ventas y búsqueda general de Configuración tienen su [registro separado](configuracion-reportes-comerciales-avances.md). Se mantienen los pendientes anteriores de este módulo.

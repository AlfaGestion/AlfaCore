# Configuración y reportes comerciales — 2026-10-15

Se dejó en espera el desarrollo funcional de Carga de Comprobantes para atender esta pasada.

## Implementado

- Barra Smart Search de Configuración en Context Toolbar, búsqueda sin acentos, filtro por área y accesos puntuales a secciones de Ventas. Búsqueda de campos en Configuración → Ventas → Carga de comprobantes.
- Reparación de ReporteCompras frente al esquema real: columnas de libro, cuenta corriente, signos, vista de proveedores, sesión activa y logging central.
- Reporte de Ventas con Resumen, Detalle y Cuenta corriente, filtros, paginación, visor, Excel completo y PDF visible. Panel y servicio compartidos con Compras.
- Opción D60RPTWEB bajo Ventas y actualización idempotente. Aplicada dos veces en DISTRIWALTERP: una fila TA_MENU y un mapeo web; sin duplicados. Se agregaron solo permisos requeridos por la actualización, sin escribir movimientos comerciales ni configuraciones.

## Evidencia y cierre

- Compilación de aplicación y tests: correcta, sin errores; tres advertencias existentes ajenas al cambio.
- Verificación final: **24 pruebas aprobadas, 0 omitidas, 0 fallidas**, filtro `ReportesComercialesTests|ConfiguracionCatalogoTests|CargaComprobantesConfiguracionTests`, con SQL de DISTRIWALTERP (22 s).
- Comparación independiente de cantidades, importes e IVA contra libros; filtro por cuenta, paginación, NC no vacías con total/IVA negativos y cuenta corriente limitada a cuentas oficiales de proveedores/clientes. Sumas de cuenta corriente coinciden con consulta independiente de MV_ASIENTOS.
- Archivos Excel de resumen, detalle y cuenta corriente abiertos con ClosedXML y encabezados verificados para ambos circuitos. El render genera las tres vistas desde acciones del header. Con `directo=1` no ofrece enlace al visor de otro módulo.
- Chrome/Playwright revisó el HTML renderizado de la cuenta corriente de ambos reportes en **2048/1440/1024**: indicador visible y sin desborde global. No equivale a una sesión de aceptación interactiva del operador.
- Configuración: búsqueda desde toolbar por MaxDto, destino correcto, exclusión de opciones no coincidentes y filtro por área; catálogo de rutas, palabras sin acentos y editor compartido sin regresiones.
- Script final de menú reejecutado dos veces dentro de transacción revertida: mapeo único; la instalación efectiva anterior quedó conservada.
- Preview reiniciado en localhost:5055, proceso lanzador **28140**; las rutas SaaS de Configuración, Compras/Reportes y Ventas/Reportes respondieron HTTP 200. La disponibilidad HTTP no prueba login ni sustituye aceptación del usuario.
- Catálogo: **98 rutinas, sin advertencias ni errores**. Revisados diff, UTF-8 sin BOM, mojibake y enlaces de los documentos de esta pasada.

No se emitieron comprobantes, no se modificaron movimientos comerciales ni se dejaron cambios de configuración en la base.

## Pendientes

Pasada posterior de Compras: también se implementaron apertura/acumulados, Saldos, Cuenta Corriente Resumida y Analítica para proveedores. Ver el registro final y la documentación específica de Compras; las notas históricas sobre sus tres pestañas/neto del período quedan superadas por esa entrega.

Actualización posterior: Saldos y Saldos Control de Crédito contable quedaron implementados en la segunda etapa detallada al final. Permanecen pendientes el circuito por aplicaciones y los demás informes indicados allí.

Tercera etapa: Cuenta Corriente Resumida y Analítica por aplicaciones implementadas; ver última pasada de este registro. Permanecen pendientes detalle diario de ventas/cobranzas, proyección y los límites de certificación indicados allí.

Nuevo alcance solicitado el 2026-10-16: ocho vistas de cartera/ventas/cobranzas, agrupación por cuenta, apertura y acumulados, filtros por unidad y por vendedor/zona del cliente. [Relevamiento y plan](../modulos/reportes-ventas-cuenta-corriente.md). Revisados VB6 y 13 RPT (510 fórmulas) y definiciones SQL en ALFANET/106, solo lectura. Se agregó herramienta de extracción de metadatos y evidencia con hashes. Esta pasada es de análisis: no implementa todavía las nuevas opciones ni certifica importes. Pendientes específicos y orden de entrega en el documento enlazado.

- Aceptación del operador con sus rangos y cuentas habituales; verificar impresión PDF en su navegador.
- Implementar pendientes por comprobante y aplicaciones. La cuenta corriente de Ventas ya incorpora apertura y acumulados en la primera etapa del 2026-10-16, detallada al final; Compras conserva el neto del período.
- Conversión o separación multimoneda y certificación de todos los históricos no forman parte de esta pasada.
- Se conservan los pendientes de Carga de Comprobantes del [registro existente](ventas-carga-comprobantes-avances-y-pendientes.md).

[Detalle técnico](../modulos/reportes-comerciales.md) · [Manual](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).


## Incidencia posterior de Actualizaciones

Se corrigió la colisión de números tras integrar cambios remotos. El script de menú de Reporte de Ventas ahora se llama `2026-10-15-009__ventas_reporte_ventas_menu_web.sql`; su contenido no cambia. [Causa, pruebas y límites](actualizaciones-colision-versiones-2026-10-15.md).


## Pasada 2026-10-16 — opción ausente en Ventas

Se corrigió el padre web ausente de Reporte de Ventas en ALFANET/106 y 153, sin tocar permisos ni movimientos. Instalador corregido y actualización de reparación para otras bases. Nueve pruebas aprobadas; queda confirmación visual del usuario tras recargar. [Detalle y evidencia](reporte-ventas-menu-2026-10-16.md).

## Pasada 2026-10-16 — navegación del dashboard y reporte

Se conserva Reporte de Ventas dentro de las pestañas del dashboard, se retira Punto de Venta de ese menú y se corrige Inicio para ir a `/ventas-dashboard`. La causa era el enlace a `/ventas`, que redirige al POS. Se conserva el acceso independiente al POS y el prefijo de la base activa. Cambio de navegación, sin cambios SQL ni emisión de comprobantes. Pendiente: confirmación visual del operador en ALFANET/106; siguen vigentes los límites funcionales indicados arriba.

Verificación: cinco pruebas aprobadas, ninguna omitida ni fallida (navegación sin prefijo, bases 106/153, búsqueda de configuración y validación de filtros). Compilación correcta, con tres advertencias existentes en Ayuda y tests de conversaciones/webhook. Catálogo: 99 rutinas, cero advertencias y errores. Instancia local reiniciada en el puerto 5055 con procesos de fondo deshabilitados.

## Pasada 2026-10-16 — cuenta corriente, primera etapa

Por indicación del usuario, desarrollo y verificaciones SQL exclusivamente en **SERVER-ALFAVB6 / DISTRIWALTERP**. Conexión confirmada con @@SERVERNAME/DB_NAME(). El usuario traerá una copia más actualizada; no se restauró ni modificó la actual. Esta pasada no consulta ALFANET/106.

Implementado: agrupación por código de cliente, orden cronológico por fecha/PK completa, apertura anterior a FechaDesde, acumulado por línea antes de paginar, saldo final por cuenta y totales generales. Se conservan cuentas sin movimientos en el período si tienen apertura no nula. Grilla muestra continuidad cuando una cuenta cruza páginas. Excel incluye apertura, movimientos y cierre por cuenta; PDF imprime la página visible.

Smart Search de Ventas incorpora unidades multiselección, vendedor principal y zona del maestro del cliente, incluidos sin asignación. Se aplican a apertura y movimientos, y también a resumen/detalle. Los borradores se copian con sus arrays antes de consultar/paginar/exportar. TC filtra presentación sin recalcular saldos incompletos. Compras conserva su circuito anterior.

Las verificaciones SQL son lecturas; las consultas usan temporales de conexión y no escriben AUX_MAYORES ni movimientos. Sin migraciones nuevas ni emisión de comprobantes. Se actualizó el manual de usuario y el [diseño técnico con reglas y límites](../modulos/reportes-ventas-cuenta-corriente.md).

Pendientes siguientes: saldos/control de crédito, pendientes resumidos/analítico por aplicaciones, subtotales diarios de ventas/cobranzas y proyección. Repetir comparación tras la copia actualizada y certificar casos de apertura/cierre (la copia actual no tiene filas marcadas), monedas y reconstrucción histórica de aplicaciones. Falta aceptación del operador e impresión desde su navegador.

Validación final: ocho pruebas únicas aprobadas, cero fallas y cero omitidas (dos nuevas SQL de cuenta corriente, tres de reportes comerciales y tres casos de navegación). Tras ampliar las comprobaciones de interfaz/filtros se repitieron las tres de reportes, todas aprobadas. Se verifican renderizado en 2048/1440/1024, saldo inicial/parcial/cierre, controles de cartera y que editar unidades no modifique filtros aplicados. Las comprobaciones de navegación con prefijos 106/153 no realizan consultas SQL; todos los accesos de datos de esta pasada usan exclusivamente DISTRIWALTERP en SERVER-ALFAVB6. Compilación correcta, con advertencias existentes de campos/eventos no usados. Catálogo: 99 rutinas, sin advertencias ni errores; diff y UTF-8 sin BOM/mojibake correctos. Preview reiniciado sin procesos de fondo en 5055; ruta /ALFANET/153/ventas/reportes responde HTTP 200 (disponibilidad, no certificación de la sesión del operador).

## Pasada 2026-10-16 — Saldos y Saldos Control de Crédito

Se agregaron ambas pestañas solo en Ventas. Una fila por código de cliente, saldo a la fecha de corte incluyendo la historia anterior, cuentas con saldo cero opcionales y filtros de unidad/vendedor/zona existentes. No se recorta deuda por FechaDesde ni TC, y los controles irrelevantes se ocultan. Paginación SQL, totales globales, Excel completo y PDF de la página visible. Compras conserva sus tres pestañas.

Control contable: límite principal actual de VT_CLIENTES, deuda global al corte y disponible global; las unidades seleccionadas limitan el saldo mostrado de selección, sin falsear el disponible de un límite único del cliente. Límite cero devuelve disponible cero, siguiendo la fórmula extraída del RPT. No suma exposición adicional por cheques o remitos; ese límite aparece en pantalla y manual. No cambia límites ni bloquea la facturación. [Reglas, evidencia y pendientes](../modulos/reportes-ventas-cuenta-corriente.md).

Única base usada: SERVER-ALFAVB6 / DISTRIWALTERP, identidad verificada en SQL. Se encontró el maestro con 1.950 clientes y sin límites positivos. Se ensayaron tres límites (cero, superior e inferior a la deuda) sobre un cliente, dentro de transacción y con rollback en finally; luego se comprobó el valor original. Ningún movimiento ni límite quedó cambiado. Los fallos detectados al probar IN vacío dentro de SUM fueron registrados en AUX_ERR y corregidos materializando primero la selección. No se borraron los logs.

Validación final: **10 pruebas aprobadas, cero fallas y cero omitidas**. Conciliación con asientos/cuenta corriente, fechas de corte sin truncamiento por Desde/TC, saldos cero/clientes sin movimientos, filtros de cartera/unidad, deuda global para crédito, límites/disponible/exceso con rollback, Excel y regresión de Compras/Ventas. Pantallas de ambas opciones verificadas en 2048/1440/1024. Compilación correcta; advertencias existentes de campos/eventos sin uso. Catálogo: 99 rutinas, cero advertencias/errores. Sin cambios de esquema ni migraciones nuevas. Preview reiniciado en 5055 con procesos de fondo deshabilitados.

Pendientes: cuenta corriente resumida/analítica por aplicaciones, subtotales diarios de ventas/cobranzas y proyección, exposición ampliada de crédito, certificación de moneda/ejercicio y aceptación visual/PDF por el operador. La carpeta VB6 no se pudo reabrir por autenticación; se usó la evidencia RPT ya extraída con hashes. Repetir comparaciones después de actualizar la copia de pruebas.

## Pasada 2026-10-16 — Cuenta Corriente Resumida y Analítica

Se implementaron ambas pestañas en Reporte de Ventas, conservando el módulo y menú existentes. La resumida muestra exclusivamente comprobantes con deuda pendiente positiva, agrupados por código de cliente. La analítica incluye cancelados y sobreaplicados y detalla recibos, NC y demás aplicaciones con fecha, importe y saldo restante. Fechas de emisión y aplicaciones actuales, como el circuito NW del VB6; no reconstruye aplicaciones históricas. Anticipos/créditos sin aplicar no se compensan automáticamente contra facturas.

Se confirmó en **SERVER-ALFAVB6 / DISTRIWALTERP** la fuente oficial VE_CPTES_SALDOS_UNEG y las definiciones del circuito de aplicaciones. Se usa la vista para originales/aplicados/pendientes; MV_APLICACION aporta el detalle en ambos sentidos, excluyendo relaciones operativas/viajes como la vista original. El filtro de unidad corresponde al documento deudor y conserva cobros de otras unidades o con unidad vacía; vendedor/zona se toman del maestro actual. Se conserva el rango al alternar entre las dos pestañas.

Se pagina por documentos completos en SQL, con todas sus aplicaciones. Los totales de cabecera y por cliente son del filtro completo. Excel exporta Comprobantes sin duplicar originales y, en analítico, una segunda hoja Aplicaciones. PDF imprime la página visible. Referencias ausentes/ambiguas y diferencias entre aplicado oficial/detalle se señalan; no se inventan fechas ni se corrigen saldos. Referencias documentales duplicadas abortan con logging AUX_ERR.

Pruebas de esta etapa: conciliación de la resumida contra vista VB6; analítico de factura cancelada con NC; aplicación/saldo restante y Excel de dos hojas; filtros/paginación. Ensayo controlado de una aplicación existente con cambios dentro de transacción: parcial, cancelación total, sobreaplicación y orientación inversa. El rollback se ejecuta en finally y se verifican cantidad/suma originales; no hubo altas de identidades, emisiones ni cambios comerciales permanentes. Se inspeccionó el trigger de aplicaciones antes del ensayo. Única base SQL utilizada: DISTRIWALTERP en SERVER-ALFAVB6.

Pasaron **12 pruebas únicas**, sin fallas ni omitidas: seis de cuenta corriente/saldos/aplicaciones, tres de reportes comerciales y tres casos de navegación. El test de pantalla incluye ambas opciones y comprueba 2048/1440/1024, además de regresión de Compras y aislamiento del visor. Catálogo: 99 rutinas, cero advertencias/errores. Compilación y comprobación final del ajuste de conservación de fechas se registran al cerrar esta pasada.

Cierre: compilación final correcta (solo advertencias existentes); tras el ajuste de conservación de fechas y rotulado de sobreaplicación se repitieron las tres pruebas de reportes, todas aprobadas sin omitidas, incluida conservación del rango entre ambas pestañas. UTF-8 sin BOM/mojibake, enlaces y diff correctos. Preview reiniciado en 5055 con procesos de fondo deshabilitados; /ALFANET/153/ventas/reportes responde HTTP 200. Esta comprobación de disponibilidad no sustituye la aceptación visual de la sesión del operador.

Pendientes: validación visual/PDF del operador y comparación ejecutando el VB6 (la carpeta compartida sigue sin autenticar; se usaron RPT previamente extraídos y definiciones SQL vigentes), multimoneda/ejercicio, eventual reconstrucción histórica de aplicaciones y exposición ampliada de crédito. Próximos informes del alcance: subtotales diarios de ventas/cobranzas y proyección. [Detalle técnico y reglas](../modulos/reportes-ventas-cuenta-corriente.md) · [Manual](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).


## Pasada 2026-10-16 — ampliación de cuenta corriente de Compras

Por pedido del usuario, Compras → Reportes ahora tiene cuenta corriente cronológica con apertura/acumulado/cierre y las nuevas pestañas Saldos, Cuenta Corriente Resumida y Analítica. Se reutilizó la infraestructura de Ventas con selección interna de área/fuentes, en vez de duplicar el módulo. Compras usa **Haber suma deuda / Debe resta**, VT_PROVEEDORES y CO_CPTES_SALDOS_UNEG. Control de Crédito permanece solo en Ventas.

Se confirmaron objetos, definiciones y columnas en la única base de pruebas autorizada: **SERVER-ALFAVB6 / DISTRIWALTERP**. El circuito CO usa VE_SALDOAPLICACION y la misma MV_APLICACION, según su definición real. Se revisó CO_CPTES_SALDOS_TODOS_UNEG para distinguir variantes de OPG/créditos no aplicados. La carpeta de fuentes sigue sin autenticar: se usaron el relevamiento previo VB6/RPT y las definiciones SQL vigentes, sin afirmar una nueva comparación visual ejecutando el original.

Se agregó filtro de unidades a las seis pestañas de Compras, conservando la cobertura histórica del libro cuando no se filtra cartera. Los pagos de otras unidades no se pierden del analítico de una factura seleccionada. Resumida muestra solo deuda positiva; Analítica incluye cancelados y pagos/NC con saldo restante. Fechas de emisión/aplicaciones actuales, igual que Ventas. Excel usa encabezados Proveedor y hojas separadas de aplicaciones; PDF imprime página visible. No hay rutas nuevas, cambios de esquema ni scripts de menú nuevos: se amplía una página existente.

**Validación: 14 pruebas aprobadas, cero fallas y cero omitidas.** Dos nuevas específicas de Compras: asientos/apertura/cierre con signo H-D, continuidad entre páginas, proveedor sin movimientos, Saldos y saldos cero, unidad y Excel; conciliación con CO_CPTES_SALDOS_UNEG y factura cancelada con NCC/NCCP, pagos/aplicaciones y Excel de dos hojas. Se ampliaron las pruebas de pantalla para las nuevas opciones de ambas áreas en 2048/1440/1024. Las doce de regresión previas también aprobaron. Las nuevas pruebas de Compras son lecturas; los ensayos transaccionales previos de la suite mantienen rollback. No hubo emisión ni cambios comerciales permanentes.

Compilación correcta, con advertencias existentes de campos/eventos sin uso. Catálogo: 99 rutinas, cero advertencias/errores. Preview reiniciado en 5055 sin procesos de fondo. Comprobaciones finales de disponibilidad, encoding y diff al cierre. [Detalle técnico y pendientes](../modulos/reportes-compras-cuenta-corriente.md) · [Manual](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).

Pendientes: aceptación visual/PDF del operador, comparación con el VB6 ejecutado cuando se recupere acceso, certificación multimoneda/ejercicio y variantes de OPG/créditos no imputados; aplicaciones históricas si se solicitan como alcance adicional. En Ventas continúan pendientes los detalles diarios y la proyección ya registrados.

Cierre de esta pasada: /ALFANET/153/compras/reportes respondió HTTP 200 tras reiniciar. Encoding UTF-8 sin BOM/mojibake, enlaces del documento técnico y git diff --check correctos. La disponibilidad HTTP no sustituye la prueba con la sesión del operador. No se realizó commit/push en esta pasada.

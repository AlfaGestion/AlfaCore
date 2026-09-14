# Cierre de caja

Ruta: `/caja-bancos/cierre`, también con prefijo SaaS `/{idweb}/{idbase}`.
Menú: Caja y Bancos (`D75`), después de Ventas, opción Cierre de caja (`D75-CIERRE`).
La raíz D75 estaba deshabilitada desde la reorganización del 02/07/2026; se reactiva sin
mover el dashboard existente de Tableros. El shell AlfaDesign mantiene el aislamiento de `?directo=1`.

## Origen y alcance

Se relevaron en AlfaWeb `app/Controllers/User/CashBox.php`, `app/Views/cashbox/detail.php`,
`close_detail_filters.php` y `public/assets/js/dist/Cash/{CashBoxDetail,CashboxDetailColumns,cashbox.main}.js`.
La pantalla llama a `v2/cashbox/reporte_cierre` y `cajas_fhoperativa`, implementados en
wsAlfa `routes/v2/cashbox.py` y `functions/caja.py`.

AlfaCore implementa su propio servicio Dapper y doce consultas SQL embebidas. No llama a
AlfaWeb, wsAlfa, procedimientos `sp_web_*`, COM ni procesos Python. Los SQL originales
se contrastaron con `docs/legacy/sql-dumps/COMPLETO_042026.sql`. Los tipos y campos se
contrastaron con la base local: se ejecutaron las doce consultas y las dieciséis variantes
paginadas, incluyendo el mensual con filas reales, sin errores SQL.

Es un informe de lectura, como la opción original. No confirma cierres ni modifica movimientos.
Los parámetros son fecha operativa, caja (o todas), saldo inicial, diario, mensual,
cancelados, venta por productos, rubros, detalle de ventas y detalle de cobranzas.
El informe conserva las dieciséis secciones y sus columnas. Rubros y productos abarcan
todas las cajas de la fecha dentro de la unidad seleccionada, y se indica en pantalla.

## Datos y consultas

| Consulta propia | Rutina original | Fuente principal |
|---|---|---|
| consolidado | sp_web_getSaldoConsolidadoCaja | VT_CONSOLIDADO_CAJA, MV_ASIENTOS, VT_TRANSFERENCIASCAJA, V_MV_CierreCaja |
| tarjetas | sp_web_getSaldoTJCaja | MV_ASIENTOS, MA_CUENTAS |
| acumulado | sp_web_getAcumuladoVentasCaja | libroivaventas, MV_ASIENTOS |
| ctacte | sp_web_getComprobantesCtaCte | MV_ASIENTOS, MV_APLICACION, MA_CUENTAS |
| transferencias | sp_web_getTransferenciasCaja | MV_ASIENTOS, MA_CUENTAS |
| cancelados | sp_web_getComprobantesCanceladosCaja | V_MV_CpteAcciones, MV_ASIENTOS |
| efectivo | sp_web_getDetalleEfectivoCaja | MV_ASIENTOS, V_MV_CierreCaja, VT_CONSOLIDADO_CAJA |
| movimientos | sp_web_getIngresosEgresosCaja | MV_ASIENTOS, MA_CUENTAS |
| rubros / productos | sp_web_getDetallePorRubroCaja / sp_web_getDetallePorProductoCaja | Aux_AC_VentasDiaria, VT_MA_Articulos |
| diario (también mensual) | sp_web_getDetalleDiarioCaja | V_MV_Stock, V_MV_Cpte, MV_ASIENTOS, MV_APLICACION, V_COBRANZASPORUSUARIO, VE_CPTES_SALDOS |

`MV_ASIENTOS` es la fuente contable. La cuenta de efectivo se resuelve desde la clave
existente `MedioDePagoContado` de `TA_CONFIGURACION` y `MA_CUENTAS.CodigoOpcional`.
Se conservan las familias de comprobantes relevadas en la rutina para cada sección;
el signo contable se toma del asiento. No se infiere el signo del stock por el TC.

La rutina web de rubros ejecutaba `NW_RECONS_ESTADISTICAS_PASO1 'V'`, que reconstruye
`AC_VentasDiaria` y elimina pendientes en `AUX_ESTADISTICAFH`. En AlfaCore se consulta
directamente `Aux_AC_VentasDiaria`, la vista usada por PASO2 para alimentar el acumulador.
Así se leen datos actuales sin modificar estadísticas compartidas. No se incluyen fuentes
remotas especiales que pudiera tener una instalación en su acumulador histórico.

## Adaptaciones de la migración

- SQL parametrizado, sin concatenar fecha/caja en SQL dinámico.
- Paginación SQL de 50 registros por sección, máximo 200 en el servicio; total general
  calculado sobre todo el resultado, no solamente sobre la página visible.
- Tablas temporales locales a la consulta y cursores locales. No se crean objetos permanentes.
- Fondo fijo sumado por caja para evitar errores de subconsulta con varios cierres;
  aplicado al efectivo y no repetido en otros medios de pago del consolidado.
- En efectivo, inicial se inicializa en cero y las transferencias no se cuentan dos veces
  en la diferencia. Diferencia = rendido - efectivo esperado; positiva es sobrante,
  negativa es faltante. La columna egresos del resumen ya descuenta las transferencias.
- En diario/mensual, se reinician variables por comprobante, se distingue TC al evitar
  repeticiones y se muestra el saldo del comprobante una sola vez entre sus artículos.
- Se conserva la selección legacy de aplicaciones de diario/mensual. La paridad con
  cobranzas parciales o múltiples necesita prueba con casos reales antes de ampliar esa lógica.
- Errores por sección con reintento; conserva lo cargado ante fallos. Registro central por
  `IAppEventService` en AUX_ERR. Permiso web comprobado antes de consultar SQL.
- Se usan los permisos de AlfaCore; no se importa la sesión ni las restricciones de caja
  del login independiente de AlfaWeb.

## Archivos

- `Models/CierreCajaModels.cs`: filtros, catálogo cerrado de secciones/columnas y página.
- `Services/ICierreCajaService.cs`, `CierreCajaService.cs`, `CierreCajaSql/*.sql`: lectura propia.
- `Components/Pages/CierreCaja.razor` y CSS: pantalla y filtros.
- `Components/Shared/CierreCajaTabla.razor` y CSS: secciones, totales, errores y paginación.
- `Program.cs` y `AlfaCore.csproj`: registro del servicio y recursos SQL.
- `App_Data/updates/2026-09-14-002__caja_cierre_caja_menu_web.sql`: menú y permisos,
  idempotente. Solo inserta en TA_MENU claves ausentes; no modifica filas legacy existentes.
- `tests/AlfaCore.Tests/CierreCajaTests.cs`: opciones, contratos SQL/parámetros y denegación de acceso.

## Validación en una base real

### Extensión: unidad de negocio y estado de lectura

`CierreCajaFiltros.UnidadNegocio` agrega el filtro opcional por local. El selector lee
`V_TA_UnidadNegocio` y el combo de cajas filtra los asientos por fecha y unidad. Las
dieciséis secciones reciben `@unidad` parametrizado, con comparación `LTRIM(RTRIM(...))`.
La selección vacía mantiene todas las unidades. El resultado muestra el filtro utilizado,
que permanece fijo al paginar aunque se cambien los controles antes de volver a cargar.

Las lecturas de asientos, cierres, libro IVA, artículos y estadísticas se filtran antes
de sumar. Las vistas `VT_CONSOLIDADO_CAJA` y `VT_TRANSFERENCIASCAJA` no exponen unidad;
`CierreCajaSql/alcance.sql` reproduce sus agregaciones desde las fuentes verificadas,
filtrando `V_COBRANZASPORUSUARIO.UNEGOCIO` y `MV_ASIENTOS.UNEGOCIO`. Las transferencias
se atribuyen al local de origen y conservan su destino aunque pertenezca a otro local.
La rama sin filtro usa las vistas originales. No requiere migración de base de datos.

Cada tabla muestra una franja de alto contraste y un indicador animado durante la lectura.
No muestra un resultado vacío anterior mientras está cargando. Se respeta la preferencia
de movimiento reducido y el estado se anuncia mediante `role=status`.

Aplicar el script mediante Actualizaciones de AlfaCore. Comparar una caja y todas las cajas,
con y sin saldo inicial, un día sin actividad, un mes completo y cada opción adicional.
Incluir transferencias, notas de crédito, monedas/cotizaciones distintas, varios cierres
y cobranzas con varios medios de pago. Comprobar total general al pasar de página,
usuario sin permiso y ruta con `directo=1`. No se ejecutó el script ni se modificaron datos
durante la implementación. Se verificaron las consultas con fecha 01/09/2026, caja 1 y
saldo inicial desactivado, y las dieciséis variantes con fecha 25/08/2026, todas las cajas
y saldo inicial activo. La validación cubre ejecución, columnas, totales y paginación;
queda pendiente comparar importes con cierres operativos representativos y probar la UI
con una sesión real después de aplicar la actualización del menú.

Validación de la extensión: 18 pruebas del módulo aprobadas; las dieciséis variantes SQL
se ejecutaron con unidad 1 y con una unidad sin movimientos. Se probó además rubros y
productos con tablas temporales de dos locales vendiendo el mismo artículo: 121 y 363
con IVA por local, 484 al seleccionar todas las unidades. No se modificaron datos de negocio.

## Exportación y envío

La barra AlfaDesign reúne Cargar, Excel, Email y WhatsApp. Los filtros se editan desde el embudo y se aplican con Cargar. Las acciones usan una copia de los filtros del informe cargado. CierreCajaExportService genera un XLSX con hoja de filtros y una hoja por sección activa; ExportarSeccionAsync mantiene permisos, consultas y totales, sin paginación. Un error cancela la generación completa.

Email adjunta el XLSX mediante la configuración general de SMTP. WhatsApp busca conversaciones autorizadas y usa UploadAttachmentAsync, respetando permisos, proveedor y ventana de atención existentes. El usuario elige el destinatario y confirma el envío; la UI remite a la conversación para consultar su estado real. No se usan servicios legacy.

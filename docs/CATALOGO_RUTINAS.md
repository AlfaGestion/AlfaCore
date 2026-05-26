# Catálogo de rutinas - Alfa Gestión

Catálogo inicial generado sobre el código actual del repositorio `AlfaCore`.

Criterio aplicado:

- Se documentaron rutinas con impacto funcional, de negocio, operación o acceso a datos.
- Se omitieron archivos demo o triviales sin valor funcional para producción.
- Solo se incluyó comportamiento verificable en código.
- Cuando un objeto SQL tiene prefijo ambiguo (`V_`, `MV_`, `TA_`), no se asumió su tipo técnico real sin validar en SQL Server.

## Navegación y base operativa

### Launcher

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Launcher.razor`
- Propósito: pantalla inicial que centraliza el acceso a los módulos principales del sistema web.
- Datos que usa: no usa objetos SQL; navega a Compras, Ventas, Stock, Caja y Bancos, Contabilidad, Consultas, Costos y Auditoría.
- Observaciones: funciona como portal de entrada general de Alfa Gestión Web.

### Inicio

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Inicio.razor`
- Propósito: atajo de navegación que redirige al inicio principal de compras.
- Datos que usa: no usa objetos SQL.
- Observaciones: redirige automáticamente a `/compras`.

### Ayuda

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Ayuda.razor`
- Propósito: centro de ayuda navegable y buscable para uso funcional de la aplicación.
- Datos que usa: contenido Markdown embebido/documental del proyecto.
- Observaciones: no resuelve lógica de negocio, pero sí es parte funcional del producto para soporte y adopción.

### SessionService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/SessionService.cs`
- Propósito: administra sesiones de conexión SQL activas y persiste la selección en `App_Data/sessions.json`.
- Datos que usa: `App_Data/sessions.json`
- Observaciones: define qué conexión usa cada servicio de datos; si no hay sesión persistida, inicializa desde `ConnectionStrings:AlfaGestion`.

## Compras

### ComprasDashboardService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ComprasDashboardService.cs` y parciales `ComprasDashboardService.*.cs`
- Propósito: concentra el dashboard de compras y las consultas de resumen, comprobantes, proveedores, rubros, familias, artículos y actividad.
- Datos que usa: `vw_compras_cabecera_dashboard`, `vw_compras_detalle_dashboard`, `vw_familias_jerarquia`
- Observaciones: aplica filtros globales por fecha, proveedor, artículo, rubro, familia, usuario, sucursal, depósito, estado y tipo de comprobante; resuelve KPIs y detalles directamente con SQL.

### DashboardModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/DashboardModels.cs`
- Propósito: define los DTO del módulo de compras para KPIs, filtros, comprobantes, proveedores, rubros, familias, artículos y actividad.
- Datos que usa: modela resultados de `vw_compras_cabecera_dashboard`, `vw_compras_detalle_dashboard`, `vw_familias_jerarquia`
- Observaciones: `DashboardFilters` es la base de filtros del módulo y también se reutiliza en Informes IA.

### FilterStateService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/FilterStateService.cs`
- Propósito: conserva durante la sesión web el último conjunto de filtros usado en el módulo compras.
- Datos que usa: DTO `DashboardFilters`
- Observaciones: inicializa por defecto con el mes actual para evitar cargas históricas completas.

### Home

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Home.razor`
- Propósito: muestra el resumen ejecutivo de compras con KPIs, evolución y accesos directos.
- Datos que usa: `IComprasDashboardService`
- Observaciones: es la entrada principal del módulo `/compras`.

### Comprobantes

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Comprobantes.razor`
- Propósito: presenta la grilla filtrable de comprobantes de compras y abre su detalle.
- Datos que usa: `IComprasDashboardService`
- Observaciones: depende de `FilterStateService` para persistir el estado de filtros.

### Proveedores

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Proveedores.razor`
- Propósito: muestra ranking, concentración, variación y ficha resumida por proveedor.
- Datos que usa: `IComprasDashboardService`
- Observaciones: trabaja sobre métricas consolidadas del dashboard de compras.

### Rubros

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Rubros.razor`
- Propósito: expone análisis de gasto por rubro, concentración y variaciones.
- Datos que usa: `IComprasDashboardService`
- Observaciones: usa DTO específicos del archivo `DashboardModels.cs`.

### Familias

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Familias.razor`
- Propósito: muestra la estructura y el comportamiento de gasto por familias y subfamilias.
- Datos que usa: `IComprasDashboardService`
- Observaciones: depende de la jerarquía disponible en `vw_familias_jerarquia`.

### Articulos

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Articulos.razor`
- Propósito: analiza artículos por impacto económico, variación de precio y últimas compras.
- Datos que usa: `IComprasDashboardService`
- Observaciones: permite bajar al detalle por artículo dentro del mismo módulo.

### Actividad

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Actividad.razor`
- Propósito: muestra actividad operativa de carga por usuario y volumen de trabajo.
- Datos que usa: `IComprasDashboardService`
- Observaciones: usa los mismos filtros globales del dashboard de compras.

### vw_compras_cabecera_dashboard

- Tipo: View
- Ubicación: objeto SQL consumido desde `src/AlfaCore/Services/ComprasDashboardService.cs`, `ComprasDashboardService.Dashboard.cs`, `ComprasDashboardService.Comprobantes.cs`, `ComprasDashboardService.Entities.cs` e `InformesIaService.Queries.cs`
- Propósito: fuente principal de cabecera y totales de comprobantes de compras para dashboards, filtros e informes IA.
- Datos que usa: no verificable desde este repositorio
- Observaciones: es la vista más usada del módulo de compras; el código la trata como fuente autorizada de lectura.

### vw_compras_detalle_dashboard

- Tipo: View
- Ubicación: objeto SQL consumido desde `src/AlfaCore/Services/ComprasDashboardService.cs`, `ComprasDashboardService.Dashboard.cs`, `ComprasDashboardService.Comprobantes.cs`, `ComprasDashboardService.Entities.cs` e `InformesIaService.Queries.cs`
- Propósito: fuente principal del detalle por artículo, rubro, familia y renglón de comprobante para compras.
- Datos que usa: no verificable desde este repositorio
- Observaciones: se utiliza para filtros de artículo y para los análisis por rubro, familia, actividad e informes IA.

### vw_familias_jerarquia

- Tipo: View
- Ubicación: objeto SQL consumido desde `src/AlfaCore/Services/ComprasDashboardService.Entities.cs`, `GestionDashboardService.cs`, `InformesIaService.Queries.cs`
- Propósito: aporta la jerarquía de familias para consolidaciones y descripciones.
- Datos que usa: no verificable desde este repositorio
- Observaciones: su uso visible es de lectura y enriquecimiento jerárquico.

## Informes IA de compras

### InformesIaService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/InformesIaService.cs`, `InformesIaService.Helpers.cs` y `InformesIaService.Queries.cs`
- Propósito: genera informes de compras a partir de consultas en lenguaje natural, resuelve intenciones, arma SQL seguro, ejecuta y guarda resultados.
- Datos que usa: `vw_compras_cabecera_dashboard`, `vw_compras_detalle_dashboard`, `vw_estadisticas_ingresos_diarias`, `vw_familias_jerarquia`
- Observaciones: trabaja en modo solo lectura; combina heurísticas propias con integración HTTP; persiste historial y resultados en `App_Data`.

### InformesIaModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/InformesIaModels.cs`
- Propósito: define solicitudes, preferencias, columnas, filas, gráficos, historial y resultados del módulo Informes IA.
- Datos que usa: modela resultados generados desde vistas autorizadas del dashboard de compras
- Observaciones: `InformeIaResultDto` conserva también el SQL generado y las fuentes autorizadas usadas.

### InformesIaSqlValidator

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/InformesIaSqlValidator.cs`
- Propósito: valida que el SQL generado por Informes IA sea solo de lectura y use exclusivamente vistas autorizadas.
- Datos que usa: `vw_compras_cabecera_dashboard`, `vw_compras_detalle_dashboard`, `vw_estadisticas_ingresos_diarias`, `vw_familias_jerarquia`
- Observaciones: rechaza `sp_`, `EXEC`, comentarios, múltiples sentencias y cualquier origen fuera de la lista blanca.

### InformesIaHistoryStore

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/InformesIaHistoryStore.cs`
- Propósito: guarda y recupera el historial de consultas IA por usuario.
- Datos que usa: `App_Data/informesia-history.json`
- Observaciones: mantiene hasta 20 elementos por usuario.

### InformesIaResultStore

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/InformesIaResultStore.cs`
- Propósito: persiste y recupera resultados ejecutados de Informes IA.
- Datos que usa: `App_Data/informesia-results/*.json`
- Observaciones: cada ejecución queda asociada a un `ExecutionId`.

### InformesIA

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/InformesIA.razor`
- Propósito: interfaz para pedir informes en lenguaje natural y comparar resultados.
- Datos que usa: `IInformesIaService`, `IComprasDashboardService`
- Observaciones: reutiliza los filtros del módulo compras mediante `FilterStateService`.

### InformesIAResultado

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/InformesIAResultado.razor`
- Propósito: muestra el resultado ejecutado de un informe IA, incluyendo tabla, resumen, gráfico y SQL generado.
- Datos que usa: `IInformesIaService`
- Observaciones: recibe el resultado por `ExecutionId`.

### vw_estadisticas_ingresos_diarias

- Tipo: View
- Ubicación: objeto SQL autorizado en `src/AlfaCore/Services/InformesIaSqlValidator.cs` y `InformesIaService.Helpers.cs`
- Propósito: fuente de series diarias para ciertos informes IA.
- Datos que usa: no verificable desde este repositorio
- Observaciones: no aparece consumida por el dashboard tradicional, pero sí está habilitada para Informes IA.

## Consultas guardadas

### ConsultasService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConsultasService.cs`
- Propósito: lista, organiza, ejecuta, crea, actualiza y elimina consultas guardadas del sistema.
- Datos que usa: `V_TA_SCRIPT`, `V_TA_SCRIPT_CFG`, `INFORMATION_SCHEMA.TABLES`, `INFORMATION_SCHEMA.COLUMNS`
- Observaciones: valida que el SQL sea de solo lectura antes de ejecutarlo; permite exportación a Excel desde endpoint en `Program.cs`.

### ConsultasModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/ConsultasModels.cs`
- Propósito: define nodos del árbol, consultas guardadas, parámetros, columnas y resultados del módulo Consultas.
- Datos que usa: modela registros de `V_TA_SCRIPT` y `V_TA_SCRIPT_CFG`
- Observaciones: `NodoArbolDto` refleja la jerarquía armada por prefijos de `CLAVE`.

### ConsultasSqlValidator

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConsultasSqlValidator.cs`
- Propósito: valida que las consultas guardadas ejecutables sean de solo lectura.
- Datos que usa: SQL libre definido en `V_TA_SCRIPT`
- Observaciones: permite `SELECT` y `WITH`; bloquea operaciones destructivas o administrativas.

### ConsultasExcelExporter

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConsultasExcelExporter.cs`
- Propósito: exporta resultados de consultas a Excel, incluyendo variantes agrupadas.
- Datos que usa: `ConsultaGuardadaDto`, `ConsultaResultadoDto`
- Observaciones: se usa desde el endpoint `/consultas/{id}/descargar-excel`.

### Consultas

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Consultas.razor`
- Propósito: lista y ejecuta consultas guardadas del sistema.
- Datos que usa: `IConsultasService`, `ISessionService`
- Observaciones: es la pantalla principal del módulo `/consultas`.

### ConsultaDetalle

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/ConsultaDetalle.razor`
- Propósito: ejecuta una consulta guardada, muestra el resultado y permite exportarlo.
- Datos que usa: `IConsultasService`
- Observaciones: usa JavaScript para descarga/exportación.

### ConsultaEditor

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/ConsultaEditor.razor`
- Propósito: alta y edición de consultas guardadas, incluyendo SQL, agrupaciones y parámetros.
- Datos que usa: `IConsultasService`
- Observaciones: expone utilidades para explorar tablas y columnas visibles vía `INFORMATION_SCHEMA`.

## Costos

### CostosService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CostosService.cs`
- Propósito: administra perfiles de importación, detecta columnas de archivos, importa listas de costos, matchea artículos, confirma decisiones, aplica costos y permite revertir la última aplicación.
- Datos que usa: `V_Ta_InterODBC`, `VT_PROVEEDORES`, `IA_Costos_Importacion_CAB`, `IA_Costos_Importacion_DET`, `IA_Costos_Actualizacion_Hist`, `V_MA_ARTICULOS`
- Observaciones: escribe auditoría funcional con `IAppEventService`; además guarda archivos fuente en `App_Data/CostosImports`.

### CostosModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/CostosModels.cs`
- Propósito: define perfiles, lotes, filas importadas, candidatos de match, historial y resultados de aplicación/reversión del módulo costos.
- Datos que usa: modela información de `V_Ta_InterODBC`, `IA_Costos_Importacion_CAB`, `IA_Costos_Importacion_DET`, `IA_Costos_Actualizacion_Hist`, `V_MA_ARTICULOS`
- Observaciones: concentra el contrato de datos de todo el flujo de importación.

### Costos

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Costos.razor`
- Propósito: muestra el tablero principal de actualización de costos y el estado de las corridas recientes.
- Datos que usa: `ICostosService`
- Observaciones: se apoya en perfiles de `V_Ta_InterODBC`.

### CostosPerfiles

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/CostosPerfiles.razor`
- Propósito: administra perfiles que definen cómo interpretar listas de proveedores.
- Datos que usa: `ICostosService`
- Observaciones: opera sobre `V_Ta_InterODBC`.

### CostosNueva

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/CostosNueva.razor`
- Propósito: inicia una nueva importación de costos desde archivo estructurado.
- Datos que usa: `ICostosService`
- Observaciones: admite `.xlsx`, `.csv` y `.txt` según el texto visible en la UI.

### CostosLoteDetalle

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/CostosLoteDetalle.razor`
- Propósito: revisa un lote importado fila por fila, confirma o descarta matches y aplica actualizaciones.
- Datos que usa: `ICostosService`
- Observaciones: trabaja sobre el detalle persistido del lote.

### CostosHistorial

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/CostosHistorial.razor`
- Propósito: consulta el historial de actualizaciones ya aplicadas.
- Datos que usa: `ICostosService`
- Observaciones: la propia UI declara que los registros provienen de `IA_Costos_Actualizacion_Hist`.

## Gestión comercial

### GestionDashboardService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/GestionDashboardService.cs`
- Propósito: resuelve dashboards y listados de ventas, stock, caja y bancos, contabilidad, posición de IVA y balance de saldos.
- Datos que usa: `V_MV_Cpte`, `Libro_VentasConFP`, `V_MV_CpteInsumos`, `VT_DETALLEIVAPROFORMA`, `VT_DETALLEIVAPROFORMA_COMPLETO`, `V_MA_ARTICULOS`, `V_MV_STOCK`, `VT_CONSOLIDADO_CAJA`, `V_EstadoBancario`, `MV_ASIENTOS`, `VE_CPTES_IMPAGOS`, `CO_CPTES_IMPAGOS`, `LibroIvaVentas_Contadores`, `LibroIvaCompras_Contadores`, `TA_CONFIGURACION`, `vw_familias_jerarquia`
- Observaciones: centraliza varios submódulos de gestión; registra errores con `IAppEventService` y usa `TA_CONFIGURACION` para no hardcodear la estructura del plan de cuentas.

### GestionDashboardModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/GestionDashboardModels.cs`
- Propósito: define DTO para ventas, stock, caja y bancos, contabilidad, IVA y balance de saldos.
- Datos que usa: modela resultados provenientes de los objetos SQL de `GestionDashboardService`
- Observaciones: incluye DTO específicos para aperturas por cliente, rubro, familia, artículo, comprobante y alícuota.

### GestionFilterModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/GestionFilterModels.cs`
- Propósito: define filtros y opciones de filtros para ventas, stock, caja y bancos y contabilidad.
- Datos que usa: se llena desde `GestionDashboardService`
- Observaciones: separa explícitamente los filtros del módulo gestión de los filtros de compras.

### GestionFilterStateService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/GestionFilterStateService.cs`
- Propósito: conserva en memoria el último estado de filtros usado por ventas, stock, caja y bancos y contabilidad.
- Datos que usa: `VentasDashboardFilters`, `StockDashboardFilters`, `CajaBancosDashboardFilters`, `ContabilidadDashboardFilters`
- Observaciones: mantiene estados separados por submódulo.

### Ventas

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Ventas.razor`
- Propósito: dashboard comercial de facturación, cartera pendiente y concentración.
- Datos que usa: `IGestionDashboardService`
- Observaciones: usa `GestionFilterStateService` para persistencia de filtros.

### VentasClientes

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasClientes.razor`
- Propósito: ranking y detalle comercial por cliente.
- Datos que usa: `IGestionDashboardService`
- Observaciones: deriva del mismo conjunto de filtros del dashboard de ventas.

### VentasRubros

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasRubros.razor`
- Propósito: análisis comercial por rubro.
- Datos que usa: `IGestionDashboardService`
- Observaciones: prioriza concentración y categorías líderes.

### VentasFamilias

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasFamilias.razor`
- Propósito: análisis comercial por familia.
- Datos que usa: `IGestionDashboardService`
- Observaciones: cruza ventas con la jerarquía de familias.

### VentasArticulos

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasArticulos.razor`
- Propósito: ranking de artículos más vendidos por importe y cantidad.
- Datos que usa: `IGestionDashboardService`
- Observaciones: expone lectura rápida de actividad reciente.

### VentasComprobantes

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasComprobantes.razor`
- Propósito: resumen por tipo de comprobante y apertura de importes del período.
- Datos que usa: `IGestionDashboardService`
- Observaciones: puede abrir ítems de comprobante por `TC` e `IdComprobante`.

## Gestión operativa y financiera

### Stock

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Stock.razor`
- Propósito: seguimiento de valorización, movimiento y artículos críticos para reposición.
- Datos que usa: `IGestionDashboardService`
- Observaciones: usa `V_MV_STOCK` como fuente principal de movimientos, coherente con la regla documental del proyecto.

### CajaBancos

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/CajaBancos.razor`
- Propósito: vista financiera corta de caja, bancos y pendientes de cobro/pago.
- Datos que usa: `IGestionDashboardService`
- Observaciones: combina saldos y pendientes en una sola pantalla.

### Contabilidad

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Contabilidad.razor`
- Propósito: presenta el balance general de saldos agrupado por niveles del plan de cuentas.
- Datos que usa: `IGestionDashboardService`
- Observaciones: la agrupación depende de configuración real en `TA_CONFIGURACION`.

### ContabilidadIvaPosicion

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/ContabilidadIvaPosicion.razor`
- Propósito: compara débito fiscal de ventas contra crédito fiscal de compras.
- Datos que usa: `IGestionDashboardService`
- Observaciones: resume saldo a pagar o a favor según período.

## Auditoría, errores y trazabilidad

### AuditoriaService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/AuditoriaService.cs`
- Propósito: consulta `AUX_ERR` para resumen, búsqueda avanzada, filtros y detalle de incidentes.
- Datos que usa: `AUX_ERR`, `V_MV_CpteAcciones`, `CONTROL_ACCESO`, `V_MV_Cpte`, `MV_APLICACION`, `C_MV_Cpte`, `MV_ASIENTOS`, `TA_CONFIGURACION`
- Observaciones: si la tabla no existe, devuelve un error explícito; registra incidentes del propio módulo mediante `IAppEventService`. En auditoría de usuarios incorpora el control `CPRA_DUPLICADO` para detectar posibles comprobantes de compras duplicados agrupando por proveedor, TC exacto, número normalizado e importe; `MV_ASIENTOS` se consulta solo para informar impacto contable y no para bloquear el hallazgo.

### AuditoriaModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/AuditoriaModels.cs`
- Propósito: define filas, filtros, series y rankings del módulo de auditoría.
- Datos que usa: modela datos de `AUX_ERR` y de las búsquedas del módulo Auditoría
- Observaciones: `AuditoriaErrorFilterDto` concentra todos los filtros visibles en pantalla. `AuditoriaUsuarioRowDto` ahora incluye campos opcionales para grupos sospechosos de compras duplicadas, impacto contable y detalle expandido de los comprobantes involucrados.

### AppEventService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/AppEventService.cs`
- Propósito: servicio centralizado de logging para errores y auditoría funcional.
- Datos que usa: `AUX_ERR`, `App_Data/diagnostics/app-events-YYYYMM.jsonl`
- Observaciones: cumple la regla del proyecto de centralizar el registro técnico; además escribe trazas estructuradas para seguimiento operativo.

### AppExceptionLoggingMiddleware

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/AppExceptionLoggingMiddleware.cs`
- Propósito: captura excepciones HTTP no controladas y las deriva al servicio centralizado de eventos.
- Datos que usa: `IAppEventService`
- Observaciones: protege el pipeline web completo antes de que el error llegue al usuario.

### AuxErrRepository

- Tipo: Repository
- Ubicación: `src/AlfaCore/Repositories/AuxErrRepository.cs`
- Propósito: inserta registros técnicos en `AUX_ERR`.
- Datos que usa: `AUX_ERR`
- Observaciones: trunca campos para adaptarlos al esquema visible en código y devuelve el `ID` insertado.

### AuxErrModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/AuxErrModels.cs`
- Propósito: define la estructura mínima usada para registrar errores en `AUX_ERR`.
- Datos que usa: modela la escritura hacia `AUX_ERR`
- Observaciones: es el contrato entre `AppEventService` y `AuxErrRepository`.

### Auditoria

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Auditoria.razor`
- Propósito: tablero general de auditoría de errores.
- Datos que usa: `IAuditoriaService`
- Observaciones: resume incidentes y accesos al detalle.

### AuditoriaErrores

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/AuditoriaErrores.razor`
- Propósito: búsqueda filtrada de errores técnicos.
- Datos que usa: `IAuditoriaService`
- Observaciones: filtra por fecha, usuario, proceso, equipo, código y texto técnico.

### AuditoriaErrorDetalle

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/AuditoriaErrorDetalle.razor`
- Propósito: muestra el detalle completo de un error específico.
- Datos que usa: `IAuditoriaService`
- Observaciones: expone el campo `Sql` de `AUX_ERR` para inspección/copiado.

## Stored Procedures usados

No se identificaron stored procedures invocados directamente desde el código actual de `src/AlfaCore`. La búsqueda realizada sobre `.cs` y `.razor` no mostró usos de `EXEC`, `EXECUTE` ni llamadas concretas a objetos `sp_`.

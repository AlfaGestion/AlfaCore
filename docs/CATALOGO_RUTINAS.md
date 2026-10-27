# Catálogo de rutinas - Alfa Gestión

Catálogo inicial generado sobre el código actual del repositorio `AlfaCore`.

Criterio aplicado:

- Se documentaron rutinas con impacto funcional, de negocio, operación o acceso a datos.
- Se omitieron archivos demo o triviales sin valor funcional para producción.
- Solo se incluyó comportamiento verificable en código.
- Cuando un objeto SQL tiene prefijo ambiguo (`V_`, `MV_`, `TA_`), no se asumió su tipo técnico real sin validar en SQL Server.

## Navegación y base operativa

### CobranzasService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CobranzasService.cs`
- Propósito: prepara y registra cobranzas rápidas CB desde Conciliación, con banco, retenciones y aplicación parcial a un comprobante; base compartida para el futuro módulo Cobranzas.
- Datos que usa: `V_MV_CPTE`, `MV_ASIENTOS`, `MV_APLICACION`, `VE_CPTES_SALDOS_VENTAS`, `MA_CUENTAS`, `V_TA_CPTE`, `MA_SUBDIARIOS`, `MA_SUBDIARIOSCOMPROBANTES`, `MV_EJERCICIOS`, `TA_USUARIOS`, `TA_CONFIGURACION`, `ALFACORE_COBRANZA_OPERACION` y tablas `BK_` de conciliación.
- Observaciones: sesión y permisos por unidad, transacción Serializable, numeración bloqueada, hash/idempotencia y control único por movimiento. Medios en pesos DB/RI/RB/RG/RO, certificado en TJ_AUTORIZO. Sugiere la última retención confirmada del cliente/unidad desde MV_ASIENTOS, con diferencia editable y memoria en el propio recibo. Ver `docs/modulos/cobranzas.md`.

### ConciliacionBancariaService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConciliacionBancariaService.cs`
- Propósito: importa extractos de Provincia, Santander y Mercado Pago, evita duplicados y prepara clasificación y propuestas para revisión en Caja y Bancos.
- Datos que usa: `BK_ImportacionExtracto`, `BK_ExtractoMovimiento`, `BK_MovimientoClasificado`, `BK_MovimientoMatch`, `BK_MovimientoAplicacion`, `BK_ReglaInterpretacion`, `BK_AliasClienteBanco`, `BK_GeneracionAlfaLog`, `MA_CUENTAS`, `VT_CLIENTES`, `VE_CPTES_SALDOS_VENTAS`, `VE_COBRANZAS_REALIZADAS`, `TA_CONFIGURACION`.
- Observaciones: usa sesión activa, permisos, Dapper y transacción; permite corrección manual versionada de clasificación y consulta contextual de facturas/orígenes en `ConciliacionBancariaService.Revision.cs` y cuenta corriente con recibos legacy, contrapartidas y aplicaciones en `ConciliacionBancariaService.CuentaCorriente.cs` (MV_ASIENTOS, MV_APLICACION, V_MV_CPTE, TA_MONEDAS y TA_USUARIOS). El parcial `ConciliacionBancariaService.Vinculos.cs` compara banco/contabilidad, confirma y deshace vínculos auditados en BK_Conciliacion/Banco/Alfa, respetando MV_CONCILIACION legacy y MV_ASIENTOS.Conciliado. El parcial `ConciliacionBancariaService.CobroExistente.cs` reconoce recibos legacy en BK_CobroExistente, con confirmación e idempotencia, sin generar nuevas registraciones ni conciliar. ConciliacionExtractoLector admite XLSX, XLS y CSV, con normalización mediante ConciliacionArchivoTabular/ExcelDataReader y detección de la variante Provincia con cabecera en fila 6. Mercado Pago admite archivos renombrados con fecha de referencia explícita, sugerida desde creación del XLSX cuando existe y revalidada al importar; las conversiones no inventan fechas de descarga. El parcial ConciliacionBancariaService.Clientes.cs permite asignar cliente con prioridad manual, regenerar la propuesta y recordar un alias bancario en BK_AliasClienteBanco para próximas importaciones, con auditoría, reevaluación individual y controles de conflicto. Normaliza CUIT y distingue cuentas del mismo titular por coincidencia única de saldo/importe, incluso con alias recordado. No genera cobranzas ni asientos. Ver `docs/modulos/conciliacion-bancaria.md`.

### Launcher

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Launcher.razor`
- Propósito: pantalla inicial que centraliza el acceso a los módulos principales del sistema web.
- Datos que usa: no usa objetos SQL; navega a Compras, Ventas, Stock, Caja y Bancos, Contabilidad, Consultas, Costos y Auditoría.
- Observaciones: funciona como portal de entrada general de Alfa Gestión Web.

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

## Informes internos

### NovedadesService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/NovedadesService.cs`
- Propósito: administra novedades internas, boletines de patch, programación de anuncios, publicación, archivado y registro de lectura por usuario.
- Datos que usa: `ALFACORE_NOVEDADES`, `ALFACORE_NOVEDADES_LECTURAS`
- Observaciones: usa Dapper y registra errores técnicos mediante `IAppEventService`; los anuncios publicados se consultan por fecha de programación, vigencia y lectura pendiente.

### NovedadesModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/NovedadesModels.cs`
- Propósito: define los DTO de listado, detalle, métricas, filtros, estados, tipos, prioridades y solicitudes de guardado del módulo Novedades.
- Datos que usa: modela datos de `ALFACORE_NOVEDADES` y `ALFACORE_NOVEDADES_LECTURAS`.
- Observaciones: diferencia comunicados manuales de boletines de patch asistidos, con soporte para popup, lectura obligatoria y repetición hasta lectura.

### Novedades

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/Novedades.razor`
- Propósito: pantalla interna para crear, editar, previsualizar, programar, publicar, duplicar y archivar novedades y boletines de AlfaCore.
- Datos que usa: `INovedadesService`
- Observaciones: reutiliza el editor rico de bloques para contenido con llamados, listas, imágenes, portadas y estructura de boletín.

### NovedadesLectura

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/NovedadesLectura.razor`
- Propósito: página `/novedades` para todos los usuarios, con dos secciones: "Novedades de AlfaCore" (notas de versión) y "Comunicados" (los que publica la empresa). Cada una es una tarjeta que se abre y se cierra; abrirla la marca como leída.
- Datos que usa: `INovedadesSistemaService` (catálogo `NovedadesSistemaCatalogo` + `App_Data/novedades-sistema/*.html`, lectura en `ALFACORE_AVISOS_LEIDOS` con clave `sis:`), `INovedadesService.GetPublicadasAsync` / `MarkReadAsync` para los comunicados.
- Observaciones: reemplaza al popup global (2026-10-13). Lo sin leer llega a la campana (`CentroAvisosService`), que abre esta página en la novedad (`?sis=` o `?id=`). Estilos en `wwwroot/css/novedades-lectura.css`, incluidos los bloques "Antes / Después" y "Tarjetas con ícono" del editor de Comunicados (`Novedades.razor`).

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
- Observaciones: centraliza varios submódulos de gestión; registra errores con `IAppEventService` y usa `TA_CONFIGURACION` para no hardcodear la estructura del plan de cuentas. El parcial `src/AlfaCore/Services/GestionDashboardService.AnalisisVentas.cs` agrega comparación por vendedor histórico/unidad de negocio, evolución y comprobantes paginados con Dapper sobre el libro oficial; ver `docs/modulos/dashboard-ventas-analisis.md`.

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

### ConversacionAsistenteService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConversacionAsistenteService.cs`
- Propósito: llamadas a OpenAI del asistente de Conversaciones: respuesta del bot con tool-calling (`ResponderAsync`), resumen en texto plano (`ResumirAsync`) y transcripción de audios a texto en español (`TranscribirAudioAsync`).
- Datos que usa: variables de entorno `OPENAI_API_KEY`, `OPENAI_MODEL` y `OPENAI_TRANSCRIBE_MODEL` (por defecto `gpt-4o-mini-transcribe`); consumo vía `IIaUsoRecorder` (funciones `BOT`, `RESUMEN`, `TRANSCRIPCION`).
- Observaciones: reutilizar para cualquier transcripción o resumen con IA en vez de llamar a OpenAI directo. El bot transcribe notas de voz solo si `CONV_ASISTENTE_TRANSCRIBE_AUDIOS` = 1 (Configuración → Asistente IA → Herramientas → Escuchar audios); la transcripción queda como nota interna.

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
- Datos que usa: `AUX_ERR`, `SYS_EventosAplicacion`, `SYS_EventosAplicacionCambios`, `App_Data/diagnostics/app-events-YYYYMM.jsonl`
- Observaciones: mantiene el diagnóstico técnico centralizado y ofrece escritura, disponibilidad y lectura paginada de auditoría funcional persistente. La lectura genérica no se expone como endpoint: cada módulo debe autorizar primero el acceso a su entidad.

### AppAuditRepository

- Tipo: Repository
- Ubicación: `src/AlfaCore/Repositories/AppAuditRepository.cs`
- Propósito: persiste eventos funcionales y cambios por campo, y recupera actividad paginada sin consultas N+1.
- Datos que usa: `SYS_EventosAplicacion`, `SYS_EventosAplicacionCambios`
- Observaciones: pagina primero las cabeceras por entidad y recupera en una sola consulta adicional los cambios de todos los eventos de la página. Incluye un control explícito de disponibilidad del esquema.

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

## Diseñador de comprobantes

### DocumentTemplateService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/DocumentTemplateService.cs`
- Propósito: administra plantillas JSON de documentos, su resolución por unidad de negocio y el snapshot de versiones previo a una edición.
- Datos que usa: `CORE_DocumentTemplate`, `CORE_DocumentTemplateVersion`, `V_TA_UnidadNegocio`
- Observaciones: valida el esquema v1 y mantiene separada la plantilla global de las específicas por `UNegocio`.

### CotizacionDocumentService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CotizacionDocumentService.cs`
- Propósito: adapta una versión de Cotización al contrato de impresión y coordina su render HTML o PDF beta.
- Datos que usa: `COT_COTIZACION`, `COT_VERSION`, `COT_DET`, `TA_CONFIGURACION`, `TA_LOGOS`, `CORE_DocumentTemplate`
- Observaciones: consume `ICotizacionesService`; no expone tablas legacy directamente al renderer y no reemplaza QuestPDF.

### DocumentRenderer

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/DocumentRenderer.cs`
- Propósito: genera HTML autocontenido desde la definición JSON v1 y `CotizacionDocumentData`.
- Datos que usa: DTOs de documentos y definición serializada de `CORE_DocumentTemplate`
- Observaciones: renderiza únicamente bloques y columnas permitidos; no ejecuta JavaScript de plantilla.

### DocumentPdfService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/DocumentPdfService.cs`
- Propósito: convierte HTML ya validado a PDF mediante Chromium headless de Microsoft Playwright.
- Datos que usa: HTML en memoria y `IAppEventService`
- Observaciones: informa errores en `AUX_ERR`; Chromium requiere instalación por ambiente.

### DiseñadorComprobantes

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/DisenadorComprobantes.razor`
- Propósito: permite elegir unidad, duplicar/editar plantilla, configurar papel, logo y columnas, y usar una Cotización real para preview/PDF beta.
- Datos que usa: `IDocumentTemplateService`, `ICotizacionesService`, `ICotizacionDocumentService`
- Observaciones: se publica bajo Utilidades y mantiene la edición de plantillas del sistema bloqueada hasta que se duplican.

### DocumentosModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/DocumentosModels.cs`
- Propósito: define el contrato JSON v1, bloques permitidos, datos de impresión y solicitudes de guardado del motor de documentos.
- Datos que usa: modela `TemplateJson` y datos adaptados de Cotización.
- Observaciones: es independiente de Dapper, UI y Playwright.

## Ventas — Carga de Comprobantes

### CargaComprobantesService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesService.cs`
- Propósito: consulta, simula, valida, graba y recupera facturas de homologación desde pedidos/remitos, controlando sesión, permiso y talonario.
- Datos que usa: `ICargaComprobantesService`, `CargaComprobantesRepository`, sesión y permisos del usuario.
- Observaciones: limita la emisión comercial al circuito simple de homologación; recupera operaciones y genera PDF con marca de prueba. Errores técnicos mediante `IAppEventService` hacia `AUX_ERR`.

### CargaComprobantesRepository

- Tipo: Repository
- Ubicación: `src/AlfaCore/Services/CargaComprobantesRepository.cs`
- Propósito: obtiene contexto, documentos paginados con saldo positivo y filtros de fechas/vendedor/zona/unidad/depósito, y renglones pendientes mediante Dapper.
- Datos que usa: tablas confirmadas `V_MV_CPTE`, `V_MV_CPTEINSUMOS`, `MV_APLICACION`, `V_MV_STOCK`, `V_MV_RESERVAS`, `TA_USUARIOS`, `TA_CONFIGURACION`, `V_TA_CPTE`; vista confirmada `VT_V_MV_INSUMOS_SALDOSRM`.
- Observaciones: lectura únicamente; tipos verificados en DISTRIWALTERP, sin asumirlos por prefijo; cantidades repetidas requieren distribución posterior.

### CargaComprobantesSimulador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesSimulador.cs`
- Propósito: valida selección y proyecta importes históricos proporcionales a la cantidad a facturar.
- Datos que usa: DTOs `CargaComprobantesOrigen`, `CargaComprobantesContexto`, `CargaComprobantesCantidad`.
- Observaciones: no es una liquidación fiscal ni sustituye el totalizador VB6 completo.

### VentasCargaComprobantes

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/VentasCargaComprobantes.razor`
- Propósito: prepara, simula, valida y confirma facturas de prueba desde pedidos/remitos individuales; incluye historial, recuperación y PDF.
- Datos que usa: `ICargaComprobantesService`, `IPageHeaderService`, `IAppUiOperationService` y componentes AlfaDesign.
- Observaciones: ruta `/ventas/carga-comprobantes`, clave D60CCW; conserva la carga ante errores y limpia contexto al cambiar base/usuario.

### CargaComprobantesModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/CargaComprobantesModels.cs`
- Propósito: transporta contexto, filtros, talonarios, documentos, renglones y resultados de simulación.
- Datos que usa: datos adaptados por el repositorio; no accede a SQL ni depende de UI.
- Observaciones: diferencia el precio histórico del importe de referencia y conserva ID/secuencia del origen.

### CargaComprobantesCalculador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesCalculador.cs`
- Propósito: liquida artículos con precios finales históricos, Currency de cuatro decimales y ajuste lineal de neto/IVA por renglón.
- Datos que usa: origen, cantidades y configuración de precios/precisión/alícuota general; FP consulta habilitación y `RobaIvaProforma`.
- Observaciones: conserva la conversión Currency del IVA después de multiplicar cantidad (el retorno unitario de `clsTotales.ImporteIVA` es Double); informa capacidades pendientes y no certifica ni habilita emisión fiscal.

### CargaComprobantesEdicionPrecios

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesEdicionPrecios.cs`
- Propósito: autoriza la edición manual de precio neto/descuento en FC directa y FP, y controla el porcentaje de línea y la rebaja efectiva combinada.
- Datos que usa: `TA_USUARIOS`, `VT_CLIENTES`, `V_TA_Cpra_Vta`, `V_TA_VENDEDORES`, claves `MaxDto`/`MaxDtoProforma` de `TA_CONFIGURACION` y referencia de lista resuelta en servidor.
- Observaciones: relee permisos y límites en preparación/grabación, sin excepción para administrador, sin modificar maestros ni admitir autorización por PIN. Se invoca desde el preparador y reutiliza validación/cálculo transaccional; errores técnicos se registran por la capa de aplicación central.

### CargaComprobantesActualizadorOrigen

- Tipo: Repository
- Ubicación: `src/AlfaCore/Services/CargaComprobantesActualizadorOrigen.cs`
- Propósito: actualiza los contadores del detalle de NP/RM con las reglas legacy dentro de la transacción provista por el llamador.
- Datos que usa: `V_MV_Cpte`, `V_MV_CpteInsumos`, `VT_V_MV_INSUMOS_SALDOSRM`, snapshot de origen y cantidades.
- Observaciones: no genera factura ni aplicación; requiere transacción y rollback ante cualquier fallo, sin conexión independiente.

### CargaComprobantesElectronicaVerificador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesElectronicaVerificador.cs`
- Propósito: valida ambiente/punto de homologación, autentica WSAA y consulta los últimos números A/B/C sin solicitar CAE.
- Datos que usa: `IArcaConfigService`, `IWsaaClient`, `IWsfev1Client`, conexión autorizada del llamador.
- Observaciones: rechaza producción, endpoints ajenos a homologación y emisores de unidad con otro punto de venta.

### CargaComprobantesElectronicaEmisor

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesElectronicaEmisor.cs`
- Propósito: solicita y registra una autorización de prueba en homologación a partir de un origen validado, con identidad estable de operación.
- Datos que usa: calculador, cliente fiscal oficial, WSAA/WSFE y `ALFACORE_VENTAS_HOMO_PRUEBA`.
- Observaciones: no consume el origen ni genera el comprobante comercial por sí solo. Emite FC/NC/ND en homologación con el plan del coordinador; valida la factura asociada en ARCA. Concilia respuestas inciertas mediante FECompConsultar y comparación de solicitud completa, incluidos comprobantes asociados; nunca reenvía automáticamente.

### CargaComprobantesPlanificador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesPlanificador.cs`
- Propósito: prepara el plan comercial y su snapshot para un NP/RM o carga directa FC/NC/ND/FP, con cuentas, vencimiento, período, talonario y efectos de stock.
- Datos que usa: repositorio, configuración central, cliente oficial, `V_TA_INPUT_VENTAS`, `MA_CUENTAS`, subdiarios y `MV_EJERCICIOS`.
- Observaciones: relee contexto dentro de la transacción final; rechaza capacidades no incorporadas y compara huellas antes de grabar.

### CargaComprobantesDirectaPreparador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesDirectaPreparador.cs`
- Propósito: resuelve cliente, lista/clase y artículos de un borrador sin insertar pedidos/remitos auxiliares.
- Datos que usa: cliente oficial, configuración y resolutor de precios compartido, artículos y precios consultados en lote, unidades y depósitos.
- Observaciones: valida alcance y cantidades; rechaza circuitos especiales pendientes de portar. No permite sobreescribir precios del servidor.

### CargaComprobantesAsociadoPreparador

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesAsociadoPreparador.cs`
- Propósito: valida la factura asociada a NC/ND y prepara la referencia fiscal y el snapshot de sus datos/aplicaciones.
- Datos que usa: factura, artículos originales, aplicaciones previas y journals comerciales/fiscales de homologación.
- Observaciones: NC conserva precios, costo y equivalencias originales, controla cantidades acreditadas y rechaza renglones ambiguos; bloquea otras notas pendientes sobre la factura.

### CargaComprobantesDirectaModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/CargaComprobantesDirectaModels.cs`
- Propósito: transporta el borrador estable de FC/NC/ND/FP y sus artículos/cantidades.
- Datos que usa: GUID de borrador, tipo, factura asociada opcional, cliente, unidad, depósito y observaciones.
- Observaciones: los precios y cuentas se resuelven en el servidor; el GUID no representa un pedido/remito y evita duplicar el borrador.

### CargaComprobantesGrabador

- Tipo: Repository
- Ubicación: `src/AlfaCore/Services/CargaComprobantesGrabador.cs`
- Propósito: registra cabecera, artículos, stock, asiento con IVA, aplicación y autorización fiscal dentro de una única transacción del llamador.
- Datos que usa: plan, `V_MV_CPTE`, `V_MV_CPTEINSUMOS`, `V_MV_STOCK`, `MV_ASIENTOS`, `MV_APLICACION`, `V_MV_CPTE_ELECTRONICOS`, `V_TA_CPTE`.
- Observaciones: no llama a ARCA ni hace commit; numera asiento con bloqueo y exige balance cero; actualiza el siguiente número legacy. NC invierte el asiento/libro IVA; FP siempre genera asiento, omite el libro IVA y la fila electrónica.

### CargaComprobantesEmisionService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CargaComprobantesEmisionService.cs`
- Propósito: coordina ensayo con rollback, confirmación del snapshot, solicitud de CAE y grabación comercial recuperable.
- Datos que usa: planificador, grabador, emisor electrónico, `ALFACORE_VENTAS_CARGA_OPERACION` y `ALFACORE_VENTAS_HOMO_PRUEBA`.
- Observaciones: solo homologación; no mantiene transacción SQL durante HTTP; conserva operación aprobada pendiente de grabación ante fallo local o cambio de sesión. FP asigna numeración local y confirma movimientos/journal en una sola transacción sin WSAA/CAE. Identifica cargas directas por borrador y serializa notas por factura asociada.

### CargaComprobantesGrabacionModels

- Tipo: DTO
- Ubicación: `src/AlfaCore/Models/CargaComprobantesGrabacionModels.cs`
- Propósito: transporta el plan comercial inmutable por operación y el resultado de grabación o recuperación.
- Datos que usa: origen, liquidación, cantidades, cuentas, huellas y datos de autorización.
- Observaciones: no contiene certificados, claves privadas ni tickets; el journal es operativo, no configuración paralela.

### ConfiguracionCargaComprobantesService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ConfiguracionCargaComprobantesService.cs`
- Propósito: consulta y edición acotada de parámetros compartidos de ventas en la base activa, con referencia al inventario VB6.
- Datos que usa: `TA_CONFIGURACION` (TABLE), `TA_USUARIOS` (TABLE), `IAppEventService`, sesión y usuario autorizados; CSV técnico embebido.
- Observaciones: solo administrador central puede guardar; claves permitidas, validación, control de versión, transacción y auditoría; inventario legacy sin valores sensibles. `USAPROFORMA` y `UsaFacturaProforma` se conservan como claves diferentes.

## Reportes comerciales

### ReporteComercialService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ReporteComercialService.cs`
- Propósito: resumen, detalle y cuenta corriente de compras/ventas con filtros y paginación.
- Datos que usa: `Libro_ComprasConFP` (VIEW), `Libro_VentasConFP` (VIEW), `MV_ASIENTOS` (TABLE), `VT_CLIENTES` (VIEW), `VT_PROVEEDORES` (VIEW), `TA_TAREAS` (TABLE), `ALFACORE_TAREAS_WEB` (TABLE), sesión, permisos e `IAppEventService`.
- Observaciones: Dapper; sin conexión de respaldo; verifica tenant antes/después de leer y registra errores en AUX_ERR. Usa campos fiscales signados existentes; no vuelve a invertir NC. Cuenta corriente limitada a cuentas comerciales y movimiento neto del período.

### ReporteComprasService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ReporteComprasService.cs`
- Propósito: conservar el contrato de compras delegando en el servicio compartido.
- Datos que usa: `ReporteComercialService`, DTO existentes de reportes.
- Observaciones: elimina las consultas a columnas inexistentes y el acceso sin sesión mediante conexión global.

### ReporteComercialPanel

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Shared/ReporteComercialPanel.razor`
- Propósito: interfaz compartida de ambos reportes, Smart Search, pestañas, indicadores, tablas, paginación, Excel y PDF.
- Datos que usa: `ReporteComercialService`, `ReporteComprasExcelExporter`, PageHeader y contexto de ruta/sesión.
- Observaciones: encabezado AlfaDesign y filtros aplicados independientes del borrador; descarta lecturas/descargas al cambiar base y conserva rutas SaaS en el visor.

### ReporteVentas

- Tipo: Page
- Ubicación: `src/AlfaCore/Components/Pages/ReporteVentas.razor`
- Propósito: acceso a Reporte de Ventas dentro del módulo Ventas.
- Datos que usa: `ReporteComercialPanel` en modo ventas.
- Observaciones: ruta `/ventas/reportes`, prefijo SaaS y clave nueva `D60RPTWEB`; menú instalado mediante actualización idempotente.

### ReporteComprasExcelExporter

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ReporteComprasExcelExporter.cs`
- Propósito: exportación de las tres vistas de compras/ventas con títulos y columnas según el área.
- Datos que usa: DTO de reportes comerciales, ClosedXML.
- Observaciones: nombre histórico preservado por compatibilidad; el panel exporta todos los registros filtrados desde el circuito autorizado, sin navegar a una descarga fuera del contexto activo.

## Actualizaciones de base de datos

### ActualizacionesService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/ActualizacionesService.cs`
- Propósito: descubrir scripts, validar versiones y coordinar panel, historial y ejecución de actualizaciones de la base activa.
- Datos que usa: `TA_CONFIGURACION` (TABLE), `AUX_ACTUALIZACION_HIST` (TABLE creada/verificada por el servicio), archivos SQL, `IAppEventService`, motor SQL y estado de ejecución.
- Observaciones: rechaza versiones duplicadas y las registra en AUX_ERR; solo ignora alias antiguos explícitos si existe la nueva copia con idéntico SQL. Incidencia y límites en `docs/gestion/actualizaciones-colision-versiones-2026-10-15.md`.

## Stored Procedures usados

No se identificaron stored procedures invocados directamente desde el código actual de `src/AlfaCore`. La búsqueda realizada sobre `.cs` y `.razor` no mostró usos de `EXEC`, `EXECUTE` ni llamadas concretas a objetos `sp_`.

### MenuService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/MenuService.cs`
- Propósito: construye el árbol dinámico de navegación web reutilizando `TA_MENU`, permisos y mapeos de `ALFACORE_MENU_WEB`.
- Datos que usa: `TA_MENU`, `TA_TAREAS`, `ALFACORE_MENU_WEB`, `IActualizacionesService`, `IPermissionService`
- Observaciones: intenta auto-reparar el shell web ejecutando actualizaciones pendientes si faltan tablas o mapeos; centraliza módulos, secciones, nodos y búsqueda del menú.

### CierreCajaService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CierreCajaService.cs`
- Propósito: consulta propia del cierre de caja por fecha, caja y opciones, con paginación SQL y totales generales por sección.
- Datos que usa: `MV_ASIENTOS`, `V_Ta_Cajas`, `V_MV_CierreCaja`, `MV_APLICACION`, vistas de consolidado, transferencias, cobranzas y ventas.
- Observaciones: no llama a AlfaWeb/wsAlfa ni modifica estadísticas; documentado en `docs/modulos/CIERRE_CAJA.md`.

### CierreCajaExportService

- Tipo: Service
- Ubicación: `src/AlfaCore/Services/CierreCajaExportService.cs`
- Propósito: genera el cierre de caja completo en Excel y lo adjunta a envíos de email.
- Datos que usa: `ICierreCajaService`, `IConfiguracionGeneralService`, `IAppEventService`, ClosedXML.
- Observaciones: conserva filtros y totales sin paginación; WhatsApp reutiliza `IConversacionesService.UploadAttachmentAsync` desde la pantalla.

# CLAUDE.md — Contexto permanente de AlfaCore

> Leer primero `AGENTS.md`, `docs/CODEX_RULES.md`, `docs/DATABASE_OBJETOS_SQL_PRIORITARIOS.md`
> y `docs/CONFIGURACION_GLOBAL.md`. Este archivo no los reemplaza: agrega el contexto del
> proyecto "Director IA" y lo descubierto en el repo.

## Quién y para qué

- Alberto, dueño y programador de **ALFA NET - Soluciones Informáticas** (desde 1993). Equipo de 5 personas; una se encargará de la administración.
- AlfaCore es el core web sobre **Alfa Gestión**. Los datos de la empresa están en el SQL Server de SERVER-ALFACORE.
- La empresa es un solo negocio económico con **varias unidades fiscales (distintos CUIT)**. Hace falta ver todo consolidado y por unidad.
- Proyecto en curso: **Director IA** (control fiscal, de plata y, después, de soporte/ventas/programación). Plan en `docs/director-ia/PLAN.md`.
- Objetivo secundario: que parte del Director IA sea un **producto vendible** a otros clientes de ALFA NET (ver sección Producto).

## Cómo trabajar (obligatorio)

- Todo lo configurable va en `TA_CONFIGURACION` (clave descriptiva, GRUPO organizativo, valor >150 caracteres a `ValorAux`). Nada hardcodeado. **Nunca** vaciar ni borrar masivamente esa tabla.
- Rutinas utilitarias reutilizables y centralizadas; no duplicar lógica. Revisar `docs/CATALOGO_RUTINAS.md` antes de crear una.
- Explicar como consultor, paso a paso, con una recomendación jerarquizada (no listas de opciones sin criterio).
- **Seguridad:** nunca escribir contraseñas, tokens ni claves fiscales en código ni en archivos del repo. Para la base, usar un login de **solo lectura** salvo autorización explícita para escribir.
- Stack fijo: Blazor Server + .NET 8 + SQL Server + **Dapper** (sin EF). Lógica de datos en vistas/SQL; la UI solo muestra.
- Pantallas nuevas: shell AlfaDesign (ver AGENTS.md: `AlfaDesignManagedModules` + `ModuleKey`). Todo módulo nuevo lleva script en `src/AlfaCore/App_Data/updates/` (`AAAA-MM-DD-NNN__area_modulo_menu_web.sql`, idempotente). Tras un pull verificar que no haya dos scripts con la misma versión.
- **Menú/permisos web independientes del legacy:** AlfaCore usa solo `ALFACORE_MENU_WEB` / `ALFACORE_TAREAS_WEB`; **nunca** escribe `TA_MENU` ni `TA_TAREAS` (menú y permisos de Alfa Gestión VB6). `LegacyTablesUpdateGuard` revierte lo que un script intente cambiar ahí. Detalle en AGENTS.md.
- Errores relevantes a `AUX_ERR` vía `IAppEventService`; al usuario, mensaje corto en español con código de incidente.
- UTF-8 sin BOM. Al correr `.sql` a mano con `sqlcmd`, usar `-f i:65001`.
- Antes de cerrar una tarea: `python tools/catalogo/check_catalogo.py`.
- PKs de texto: `CodigoPk.Format(codigo, ancho)` al grabar; `LTRIM(RTRIM())` al leer.

## Stack y arquitectura (confirmado en código)

- .NET 8, Blazor Server. App principal `src/AlfaCore/AlfaCore.csproj`; tests en `tests/AlfaCore.Tests`. `Program.cs` (~4600 líneas) concentra DI y endpoints minimal API (webhooks, descargas, `/api/vb6/*`).
- Paquetes: Dapper, Microsoft.Data.SqlClient, ClosedXML, ExcelDataReader, QuestPDF, Playwright. Sin Hangfire/Quartz.
- Servicios en `src/AlfaCore/Services/` (interfaz + clase). Repositories solo para `AUX_ERR` y auditoría.
- **Multi-base / SaaS:** `ModoSaaS=true`. `ALFA_CENTRAL` guarda `Clientes`, `bases` (conexión de cada tenant), `Modulos`, `ClienteModulos`, `Planes`, `Cargos`, `IA_USO`, etc. `ConexionClienteService` resuelve la base activa por sesión, ruta `/{idweb}/{idbase}/...` o `WebhookOverride`. Los servicios no deben usar una conexión global de respaldo.
- **Jobs:** `BackgroundService` con `PeriodicTimer`/`Task.Delay`, registrados en `Program.cs` (~495-515); se apagan con `ProcesosSegundoPlano:Deshabilitados`. Los jobs multi-base iteran las bases de `ALFA_CENTRAL` y fuerzan cada una con `SetWebhookOverride` (modelo: `ConversacionesProgramadosHostedService`). Ahí va el job de vencimientos fiscales.
- **Actualización de esquema:** scripts de `App_Data/updates/` aplicados al arrancar por `DatabaseUpdatesHostedService`. Las bases de clientes pueden tener compatibility level 80/100: no usar `THROW`, `OPENJSON` ni `STRING_SPLIT`. Índices filtrados solo con guarda `compatibility_level >= 90`: en compat 80 el `CREATE` falla con un `bit = 1` y, aunque se cree, todo INSERT/UPDATE de SqlClient falla con 1934 (ARITHABORT). GENESIS (10.8.0.70\alfanet) es SQL Server 2008 RTM en compat 80.
- **WhatsApp (Meta):** webhooks `/api/conversaciones/whatsapp/webhook/{token}` (el token resuelve la base). Envío con plantillas: `ConversacionesService.SendTemplateMessageAsync`. Tablas `CONV_*`. Claves en `TA_CONFIGURACION` grupo `CONVERSACIONES`.
- **Email:** SMTP con `System.Net.Mail`, claves `EMAIL_*`; no hay servicio unificado.
- **Agente IA existente:** OpenAI Chat Completions con tool-calling (`ConversacionAsistenteService`, herramientas en `ConversacionAsistenteHerramientasService`). La API key está en la variable de entorno `OPENAI_API_KEY` (no en el repo). El consumo se registra con `IIaUsoRecorder` en `ALFA_CENTRAL.IA_USO` y tiene tope por cliente. El Director IA debe reutilizar este patrón.

## Lo fiscal que ya existe

- Facturación electrónica WSFE v1 y WSAA (`Wsfev1Client`, `WsaaClient`, `ArcaFacturacionElectronicaService`, `ArcaConfigService`), certificados en `ARCA_CERTIFICADO` (por `UNegocio`, `GLOBAL` como respaldo). Padrón por `ws_sr_constancia_inscripcion` (`ArcaPadronService`). Config en `TA_CONFIGURACION` grupo `ARCA` (`ARCA_AMBIENTE`, `WSFE_CUIT`, `PV_EFACTURA`...).
- Libros de IVA: `Libro_Ventas`, `Libro_Compras`, `LibroIvaCompras_Contadores`, `VT_AFIP_CITI_Compras`. Pantalla `/contabilidad/posicion-iva` con IVA ventas vs compras (`GestionDashboardService`).
- Retenciones/percepciones: `V_TA_RETENCIONES`, `C_MV_CPTE_PERCEPCIONES`, usadas en el recibo CB de Cobranzas.
- **Unidad fiscal:** no existe como concepto. Lo más cercano es `V_TA_UnidadNegocio` (`Codigo`, `CUIT`, `PDV`, `USAEFC`, certificado propio). Una unidad puede tener su CUIT; si no, cae al CUIT global. `UNEGOCIO` está en casi todas las tablas de movimiento.
- **No existe:** DDJJ, VEP, moratorias/planes, Domicilio Fiscal Electrónico, vencimientos impositivos, ni importador de Mis Comprobantes dentro de AlfaCore (ese importador es VB6 legado y no está ubicado).
- Ojo: `docs/modulos/integraciones/arca_afip.md` está desactualizado (dice que no hay WSFE).

## Datos de ventas, compras y bancos

- Contabilidad: `MV_ASIENTOS` (fuente principal, incluye campos `LIVA_*`), `MV_APLICACION` (zona sensible), `MV_CONCILIACION` (legacy). Maestros: `MA_CUENTAS`, `VT_CLIENTES`, `VT_PROVEEDORES`.
- Ventas: `V_MV_CPTE`, `V_MV_CPTEINSUMOS`, `V_MV_CPTE_ELECTRONICOS` (CAE). Saldos: `VE_CPTES_SALDOS_VENTAS`; cobranzas: `VE_COBRANZAS_REALIZADAS`.
- Compras: `C_MV_Cpte`, `CO_CPTES_IMPAGOS_2026`, `CO_PAGOS_REALIZADOS`. Staging IA de facturas: `IA_Compras_CAB/DET`.
- Bancos: tablas `BK_*` (`BK_ImportacionExtracto`, `BK_ExtractoMovimiento`, `BK_MovimientoClasificado`, `BK_MovimientoMatch`, `BK_Conciliacion*`, `BK_CobroExistente`, `BK_AliasClienteBanco`, `BK_ReglaInterpretacion`). Journal de cobranzas: `ALFACORE_COBRANZA_OPERACION`.
- **No hay tabla de compras ARCA importadas** definida en AlfaCore.

## Scrapers (repo aparte: `C:\dev\scraping`, Python + Playwright)

| Script | Fuente | Estado al 21/10/2026 |
|---|---|---|
| `mercadopago_actividad.py` | Web de actividad de Mercado Pago (sesión en perfil Chrome dedicado) | Activo, última corrida OK 19/10 |
| `provincia_extracto.py` | Banco Provincia (BIP) | Activo, OK 19/10 |
| `santander_extracto.py` | Santander | **Falló 19/10**: cambió el selector del menú |
| `arca_comprobantes.py` | ARCA Mis Comprobantes emitidos/recibidos, 1 CUIT por corrida | **No corre**: falta `pywin32` en el venv |
| `arca_cuenta_corriente.py` | ARCA cuenta corriente y planes (Mis Facilidades) | Experimental, nunca corrió OK |

- Ninguno escribe a SQL. Dejan `.xlsx/.xls/.csv` en `C:\dev\Extractos\<Fuente>\`; alguien los sube a mano en `/caja-bancos/conciliacion-bancaria` (parsers en `ConciliacionBancaria/Infrastructure/Excel/`). No hay tareas programadas.
- Riesgos conocidos: clave fiscal en texto plano en los `.bat` de ARCA (está en `.gitignore`, igual hay que sacarla); MP por scraping y no por API; un solo CUIT por corrida en ARCA.
- Hay un importador `.xls` de ARCA en VB6 (hoja `Sheet1`) que no está ubicado.

## Conciliación y cobranzas (implementado 19-20/10/2026)

- Pantalla `ConciliacionBancaria.razor`, servicios `ConciliacionBancariaService*`, `CobranzasService`. Docs: `docs/modulos/conciliacion-bancaria.md`, `docs/modulos/cobranzas.md`.
- Clasificación determinística por reglas (`BK_ReglaInterpretacion`), matching solo de cobranzas contra `VE_CPTES_SALDOS_VENTAS` (exacto, múltiple 2-4, parcial, anticipo, ya registrada), score 0-100. Las propuestas son sugerencias.
- Claves: `CONCILIACION-TOLERANCIA` (1.00) y `CONCILIACION-DIAS-BANCO` (3), grupo `CAJA`.
- Pendientes documentados: varias facturas por recibo, anticipos, cheques/tarjetas, otras monedas, asientos por comisiones/impuestos.
- No usa el CUIT de la empresa: el CUIT que cruza es el de la contraparte.

## Convenciones del Director IA

- Esquema propio, prefijo **`FIS_`** para lo fiscal (`FIS_Obligaciones`, `FIS_Planes`, `FIS_PlanCuotas`, `FIS_Notificaciones`...), siempre con unidad fiscal / CUIT. Decisión de prefijo pendiente de confirmar (ver PLAN.md).
- Vistas de solo lectura para la IA en esquema **`dir.`** (`dir.vw_*`). La IA nunca lee tablas operativas directo ni escribe.
- Claves de configuración con prefijo `DIRECTOR-` / `FISCAL-` en `TA_CONFIGURACION` (ej. `FISCAL-ALERTA-DIAS-ANTES`).
- Consumo de IA siempre vía `IIaUsoRecorder`.

## Producto (vender a otros clientes de ALFA NET)

- Ya existe la infraestructura para venderlo como **módulo**: `Modulos` / `ClienteModulos` / `Planes` / `BillingService` en `ALFA_CENTRAL`, scripts idempotentes de update por base, instalador Inno Setup y servicio Windows, medición y tope de IA por cliente.
- Regla de diseño: nada específico de ALFA NET en el código (CUIT, bancos, nombres). Todo por `TA_CONFIGURACION` o tablas por cliente, y por unidad fiscal.
- Los scrapers con clave fiscal **no** son vendibles tal cual (custodia de clave fiscal de terceros, fragilidad). Ver PLAN.md, sección Producto.

## Qué NO está verificado

- No se consultó la base de datos en vivo. Los nombres de tablas y vistas salen de código, scripts y documentación. Antes de crear vistas `dir.` confirmar el tipo real de cada objeto en SQL Server.
- Régimen de cada CUIT, moratorias vigentes y qué envía el contador son datos que solo tiene Alberto (preguntas en PLAN.md).

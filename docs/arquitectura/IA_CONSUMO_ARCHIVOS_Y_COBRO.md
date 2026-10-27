# IA: archivos del asistente, medición de consumo y cobro por créditos

Decisión del equipo (2026-10-05): archivos en **OpenAI vector store**, **medición propia en ALFA_CENTRAL con conciliación mensual** y **cobro por créditos IA** sobre todo el uso de IA.

## 1. Medición (`Services/IaUsoService.cs`, `Models/IaUsoModels.cs`)

- Cada llamada a OpenAI registra tokens de entrada, cacheados y de salida, el modelo efectivo y la función: `BOT`, `RESUMEN`, `ANALISIS`, `OPORTUNIDAD`, `REESCRITURA`, `INFORMES_IA`, `COTIZACION`, `PROXY`, `ARCHIVOS_BUSQUEDA`, `ARCHIVOS_ALMACENAMIENTO`.
- La base sale de la sesión activa (incluida la base resuelta por un webhook). El proxy por licencia informa el cliente directamente.
- `IIaUsoRecorder` (por solicitud) encola; `IaUsoFlushService` (en segundo plano) graba cada 10 s por lotes en `ALFA_CENTRAL.dbo.IA_USO`, calculando el costo con el precio vigente de `IA_PRECIO_MODELO` (coincidencia por prefijo más larga). Si la central no está o falta el script, el lote queda en `App_Data/ia-uso/ia-uso-pendiente-AAAAMM.jsonl` y se **reimporta solo** cuando la central vuelve (cada archivo en una transacción; después se renombra a `.importado-<fecha>.jsonl`). La medición nunca interrumpe la función que consumió IA.
- Script central: `docs/base-datos/sql-referencia/2026-10-05-002__alfa_central_ia_uso.sql` (`IA_PRECIO_MODELO`, `IA_USO`, `IA_CONFIG`, módulo `IA_CREDITOS`, `IA_TOPE_CREDITOS`, vista `V_IA_USO_DIARIO`). **La app lo aplica sola** al arrancar (`CentralIaSchema`, desde `IaUsoFlushService`; reintenta cada 10 min si falla). Requiere que el usuario de la conexión central tenga permiso de DDL; si no, aplicarlo a mano.
- **Precios:** el script carga `gpt-4o-mini`, `file_search` y `vector_store`. Si producción usa otro modelo (`OPENAI_MODEL`), cargar su precio en `IA_PRECIO_MODELO`; mientras falte, esos usos quedan con costo NULL y se marcan "sin precio".

## 2. Archivos del asistente (`Services/ConversacionAsistenteConocimientoService.cs`)

- Asistente IA → Información del negocio: **Información general** (texto libre, se guarda con el asistente), **Información por tema** (bloques en `CONV_ASISTENTE_BLOQUES`) y **Archivos** (`CONV_ASISTENTE_ARCHIVOS`). Script por base `App_Data/updates/2026-10-05-003__...` (se aplica solo al entrar a cada base).
- Archivos: PDF, Word, PowerPoint, texto, Markdown, HTML, JSON; hasta 20 MB. Se suben a OpenAI (`/v1/files`) y a un **vector store propio de la base** (`/v1/vector_stores`, id en `TA_CONFIGURACION` clave `CONV_ASISTENTE_VECTOR_STORE_ID`). El estado (Procesando/Listo/Error) se consulta en OpenAI. Al eliminar, se borra también en OpenAI.
- Bot: antes de responder arma la información con la general + bloques activos + hasta 5 fragmentos de `/v1/vector_stores/{id}/search` (no en saludos; máximo 6.000 caracteres de fragmentos).
- **Aislamiento:** el vector store se crea con metadata `alfacore_base=<IdBase>` y se verifica contra la base activa antes de usarlo (cacheado por proceso). Si no coincide, no se usa y queda en `AUX_ERR`.
- Medición: cada búsqueda (`ARCHIVOS_BUSQUEDA`) y una vez por día los bytes del vector store (`ARCHIVOS_ALMACENAMIENTO`, al usarse la base).
- No lee PDFs escaneados (solo imagen).

## 3. Consumo y cobro (`Services/IaConsumoService.cs`)

- **1 crédito = `IA_CONFIG.USD_POR_CREDITO` de costo de OpenAI** (por defecto USD 0,001). El margen va en el precio del plan.
- **Planes aprobados (2026-10-06)**, creados por el esquema central si no existen y editables en Administrar → Módulos → Créditos de IA → Planes:

  | Plan | Créditos/mes | Abono | Excedente |
  |---|---|---|---|
  | `IA_INICIAL` (por defecto) | 3.000 | incluido | no tiene: corta en 3.000 |
  | `IA_ESTANDAR` | 20.000 | USD 35 | USD 3 cada 1.000 |
  | `IA_PRO` | 80.000 | USD 140 | USD 3 cada 1.000 |

- Cobro: el cliente tiene el módulo **Créditos de IA** (`IA_CREDITOS`) con un plan de tipo **CREDITOS**: `CantidadIncluida` = créditos incluidos por mes, `Precio` (o `PrecioContratado`) = abono fijo, `PermiteExcedentes` + `PrecioExcedente` = **precio por cada 1.000 créditos** adicionales (la columna tiene 2 decimales y un crédito vale milésimas de dólar).
- **Plan por defecto:** los clientes sin plan asignado usan el plan de `IA_CONFIG.PLAN_DEFAULT_CODIGO` (`IA_INICIAL`). No genera cargo (no hay `ClienteModulos`), pero define créditos incluidos y tope.
- **Configuración** (`IA_CONFIG`, editable en Consumo IA → Configuración de créditos): `USD_POR_CREDITO`, `PLAN_DEFAULT_CODIGO`, `TOPE_FACTOR_EXCEDENTES` (2) y `AVISO_PORCENTAJE` (80).
- Administrar → **Consumo IA** (`/admin/consumo-ia`, superadmin): consumo por cliente y función, créditos, plan, excedentes e importe estimado; **Generar cargos del mes** (solo meses terminados, idempotente, un cargo en `Cargos` por cliente: abono + excedentes × precio).
- **Conciliación:** compara el costo medido con `/v1/organization/costs` de OpenAI. Requiere la variable `OPENAI_ADMIN_KEY` (clave de administración de la organización) en el servidor.
- Cliente: Asistente IA → General muestra **Consumo de IA este mes** (créditos de la base, del cliente, incluidos y % usado) y **Plan de IA**: los planes activos y visibles, el suyo marcado y "Pedir este plan".
- **Pedidos de cambio de plan** (`IA_SOLICITUD_PLAN`): el cliente pide; en Consumo IA aparecen arriba para **Aprobar** (contrata el plan o lo cambia con `ICentralAdminService.ContratarPlanAsync`/`CambiarPlanAsync`; sin prorrateo, rige para el cargo del mes) o **Rechazar**. Un pedido nuevo reemplaza al pendiente.
- **Tope mensual vigente:** el manual de `IA_TOPE_CREDITOS` si existe (0 = sin tope); si no, el **automático del plan**: sin excedentes, los créditos incluidos; con excedentes, incluidos × `TOPE_FACTOR_EXCEDENTES` (0 = sin tope automático). En Consumo IA, fila del cliente: "Guardar tope", "Usar el del plan" o "Sin tope" (por ejemplo, para las bases propias de Alfa). Al pasar el porcentaje aparece un aviso en la campana; al alcanzarlo **el bot deja de responder**: agrega una nota interna (una por día y conversación), sube la prioridad a media y la conversación queda para el equipo. Las demás funciones de IA no se cortan. El estado se cachea 2 minutos por base y, si la central no responde, el bot sigue respondiendo (falla abierta). Reglas: `IaConsumoService.EvaluarTope` (tests: `IaConsumoTests`).

# Conversaciones — Roadmap de automatizaciones e IA

Documento de planificación (no es documentación de algo ya construido). Define la
visión de automatización del módulo Conversaciones, por niveles de madurez, con foco
en el uso de **AlfaKnowledge** como base de conocimiento del asistente. Más adelante
la misma lógica se extenderá a CRM y otros módulos.

## Estado actual (baseline)

- **Automatización Nivel 0** (única existente): respuesta fija **fuera de horario** por
  WhatsApp (`ConversacionAutomatizacionesConfigDto`, claves en `TA_CONFIGURACION`).
  El código la etiqueta explícitamente como "sin IA, sin aprobación de operador".
- **Copiloto de IA ya integrado**: el panel de sugerencias (`_showAiSuggestionPanel`)
  consulta **AlfaKnowledge (RAG)** y devuelve respuestas **con citas**, permite
  **corregir** la respuesta y esa corrección **vuelve a la base** (mejora continua).
  Modos actuales: `ReplySuggestion` y `TextImprovement`.
- Piezas reutilizables como disparadores: clasificación/prioridad del cliente
  (`TA_CLASIFICACIONES` → badge P1-P4), asignación (+ auto-asignación al responder),
  ventana de 24h de WhatsApp, multi-número, estados de conversación.
- **AlfaKnowledge** es un RAG completo (ingesta, embeddings, Qdrant, búsqueda semántica,
  respuesta con citas, conocimiento curado, **multi-proyecto**). Selecciona la base con
  el header `X-Knowledge-Base-Id`, o sea **ya soporta "sectores" de conocimiento**.

## Referentes (cómo lo hacen los sistemas grandes)

- **Intercom Fin**: bot RAG que responde solo con respaldo del KB, con **umbral de
  confianza** y **handoff** a humano cuando no está seguro.
- **Zendesk AI / Freshchat Freddy**: **intención + sentimiento**, sugerencia de macros
  al agente, deflection (resolver FAQ sin humano).
- **Respond.io / ManyChat**: constructor de flujos para WhatsApp (bienvenida, menús,
  keywords, seguimientos).
- Patrón común: **niveles de autonomía** (reglas → copiloto → bot supervisado → bot
  autónomo con guardarraíles).

## Modelo por niveles

### Nivel 0 — Reglas (extender lo actual)
Bienvenida, "ya te respondemos", fuera de horario (hecho), respuesta por
**palabras clave/menú**, auto-cierre por inactividad. Bajo esfuerzo, alto uso.

### Nivel 1 — Copiloto proactivo (mejor ROI, casi todo ya está) — **EN CURSO**
- Sugerencia de respuesta **proactiva** (al abrir la conversación / llegar un mensaje),
  no solo a demanda. (El panel ya se abre solo al entrar a Conversaciones.)
- **Resumen** de la conversación + **intención** + **sentimiento**.
- **Extracción de datos** (pedido, dirección, cliente) para prellenar oportunidad/cotización.
- Borrador con **tono configurable** y traducción.

### Nivel 2 — Bot supervisado (human-in-the-loop)
El bot **redacta y propone**, el operador **aprueba antes de enviar**; o responde solo
FAQ de **alta confianza con citas** y **escala** el resto.

### Nivel 3 — Bot autónomo con guardarraíles
Responde solo dentro del **"sector Conversaciones" de AlfaKnowledge**, con **handoff
automático** por triggers (pide humano, enojo, tema no cubierto, intención de
compra/reclamo, baja confianza), respetando ventana 24h y horario.

## El rol de AlfaKnowledge (el "sector Conversaciones")

1. **Un KB dedicado "Conversaciones"** con: persona/tono, **reglas de actuación**
   (qué responder, qué escalar, qué NUNCA decir), FAQ/respuestas canónicas y datos de
   empresa (horarios, precios, políticas). El bot/copiloto consulta *ese* KB vía
   `X-Knowledge-Base-Id`.
2. **Editor de "instrucciones del asistente"** (system prompt curado) por cliente/canal,
   versionado, editable desde la config de Conversaciones — sin tocar código para
   cambiar cómo responde el asistente.
3. **Loop de corrección** ya existente = mejora continua; cada corrección enriquece el sector.
4. **Confianza + citas como condición**: el bot solo responde autónomo si hay respaldo.

## Guardarraíles (imprescindibles antes de soltar un bot)

- Nunca inventar precios/datos → siempre **RAG con citas**.
- **Temas de escalado obligatorio** (reclamos, pagos, datos sensibles).
- **Límite de intentos** del bot → handoff.
- **Auditoría** de cada respuesta automática (qué, confianza, fuente).
- **Kill switch** por conversación y global; respetar 24h y horario.

## Modelo de datos / config a futuro (no ahora)

- Tabla de **reglas de automatización** (disparador → condición → acción), reutilizable en CRM.
- **Estado de bot por conversación** (activo / pausado / handoff).
- Config de **persona + KB por canal/número**.

## Orden de ataque recomendado

1. **Nivel 1** primero (copiloto proactivo + resumen/intención/sentimiento): reutiliza casi
   todo lo existente, valor inmediato, riesgo bajo.
2. En paralelo, armar el **"sector Conversaciones" en AlfaKnowledge** + el **editor de
   instrucciones**.
3. Con eso montado, Nivel 2 (bot supervisado) y luego Nivel 3 (autónomo con guardarraíles),
   de forma incremental.

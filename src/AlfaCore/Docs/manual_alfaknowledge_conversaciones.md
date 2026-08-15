# Manual — Asistente de IA y Automatizaciones en Conversaciones
### Alfa Gestión · WhatsApp, Instagram, Facebook, Mercado Libre y Chat interno

> Manual de usuario con notas técnicas para el equipo de soporte. Cubre el copiloto de IA
> (AlfaKnowledge), el análisis de la conversación, el envío asistido y el bot automático.

---

## Índice

1. [Panorama: qué hace la IA en Conversaciones](#1-panorama-qué-hace-la-ia-en-conversaciones)
2. [Antes de empezar: asignate la conversación](#2-antes-de-empezar-asignate-la-conversación)
3. [El panel del asistente (se abre solo)](#3-el-panel-del-asistente-se-abre-solo)
4. [Análisis de la conversación (resumen, intención, sentimiento)](#4-análisis-de-la-conversación)
5. [La respuesta sugerida y el indicador de confianza](#5-la-respuesta-sugerida-y-el-indicador-de-confianza)
6. [Aprobar y enviar, o editar antes](#6-aprobar-y-enviar-o-editar-antes)
7. [Tono y traducción del borrador](#7-tono-y-traducción-del-borrador)
8. [Fuentes de la respuesta](#8-fuentes-de-la-respuesta)
9. [Hablarle a la IA sobre el caso](#9-hablarle-a-la-ia-sobre-el-caso)
10. [Configuración (admin/soporte): instrucciones del asistente](#10-configuración-instrucciones-del-asistente)
11. [Automatizaciones: fuera de horario y bot autónomo](#11-automatizaciones-fuera-de-horario-y-bot-autónomo)
12. [Prioridad, asignación automática e íconos del inbox](#12-prioridad-asignación-automática-e-íconos-del-inbox)
13. [Qué hace y qué NO hace](#13-qué-hace-y-qué-no-hace)
14. [Requisitos técnicos y diagnóstico](#14-requisitos-técnicos-y-diagnóstico)
15. [Preguntas frecuentes](#15-preguntas-frecuentes)

---

## 1. Panorama: qué hace la IA en Conversaciones

El módulo tiene tres capas de ayuda, de menor a mayor autonomía:

- **Copiloto (asistente):** lee el hilo, **analiza** la conversación, busca en la base de
  conocimiento (**AlfaKnowledge**) y **propone** una respuesta con fuentes. El agente decide.
- **Envío asistido (supervisado):** el agente puede **aprobar y enviar** la respuesta del asistente
  en un clic, con un **semáforo de confianza** que avisa cuándo conviene revisar o escalar.
- **Bot automático (opcional):** si el admin lo habilita, el bot **responde solo** los WhatsApp
  entrantes que cumplen guardarraíles estrictos; si no, **deja la conversación para un humano**.

---

## 2. Antes de empezar: asignate la conversación

Conviene tomar la conversación con **Asignarme** (junto al nombre del contacto). Evita que dos
agentes respondan a la vez y deja registro de quién atiende.

> **Novedad:** si respondés una conversación **sin asignar**, queda **asignada a vos
> automáticamente** (auto-asignación al responder).

---

## 3. El panel del asistente (se abre solo)

Al entrar a Conversaciones, si el módulo AlfaKnowledge está habilitado, el panel del asistente
**se abre automáticamente** a la derecha (tercera columna en escritorio). Podés cerrarlo con la
**X**; se vuelve a abrir la próxima vez que entrás (así se usa).

Dos accesos:

| Ubicación | Ícono | Qué hace |
|---|---|---|
| Barra superior de la conversación | ✨ | Abre/cierra el panel del asistente |
| Barra del compositor (donde escribís) | ✨ | Mejora el borrador que ya escribiste |

---

## 4. Análisis de la conversación

Arriba del panel, la tarjeta **Análisis de la conversación** resume el caso automáticamente:

- **Resumen** — 1-2 frases de qué necesita/pasó.
- **Intención** — etiqueta corta (Consulta de precio, Reclamo, Soporte, Pedido, Seguimiento…).
- **Sentimiento** — Positivo / Neutro / Negativo del cliente (con color).

Botón **↻** para volver a analizar. *Nota técnica: el análisis se genera con IA sobre el propio
hilo (no usa la base de conocimiento) y necesita `OPENAI_API_KEY` en el server.*

---

## 5. La respuesta sugerida y el indicador de confianza

El asistente propone una **respuesta sugerida** basada en la base de conocimiento, con un
**semáforo de confianza**:

| Color | Significado | Qué conviene |
|---|---|---|
| 🟢 **Con respaldo** | Contexto suficiente + fuentes citadas | Podés aprobar y enviar |
| 🟡 **Sin respaldo suficiente** | Falta base para el tema | Revisá bien antes de enviar |
| 🔴 **Escalar** | La IA pidió una aclaración | Conviene pasar a un humano / pedir más datos |

El semáforo es una guía: **la decisión de enviar siempre es del agente.**

---

## 6. Aprobar y enviar, o editar antes

Debajo de la sugerencia:

| Botón | Qué hace |
|---|---|
| **Aprobar y enviar** | Manda la respuesta del asistente al cliente en un clic (respeta canal y la ventana de 24h de WhatsApp; se deshabilita si no corresponde). |
| **Editar antes** | Copia la sugerencia al compositor para revisarla/ajustarla y enviar cuando quieras. |
| **Regenerar** | Pide una sugerencia nueva con el mismo contexto. |
| **Corregir respuesta** | Le indicás a la IA qué cambiar; la corrección se guarda en AlfaKnowledge (mejora la base). |
| **Descartar** | Cierra la sugerencia sin usarla. |

> **Importante:** "Aprobar y enviar" **sí manda el mensaje** — es una decisión humana en un clic.
> Si preferís revisar, usá **Editar antes**.

También podés elegir **qué contexto lee la IA** (selector "Contexto que leerá la IA"): *Tramo
actual* (por defecto), *Toda la conversación* o *Mensaje marcado* (marcado con ⭐).

---

## 7. Tono y traducción del borrador

En el compositor, junto al botón de "mejorar con IA" (✨), el botón **Aa** abre un menú para
reescribir el borrador que estás escribiendo:

- **Tono:** Formal · Cordial · Más breve · Más amable.
- **Traducir:** Inglés · Portugués.

Reescribe el texto manteniendo la intención y los datos (sin agregar cosas). Requiere tener algo
escrito en el compositor.

---

## 8. Fuentes de la respuesta

La sección **Fuentes** lista los documentos de la base que la IA usó. Cada fuente abre en pestaña
nueva. Revisá las fuentes cuando la respuesta trate **precios, condiciones o políticas**.

---

## 9. Hablarle a la IA sobre el caso

El campo **"Hablá con la IA sobre este caso"** es un chat aparte (no se envía al cliente). Sirve
para pedir resúmenes, aclaraciones o versiones alternativas.

---

## 10. Configuración: instrucciones del asistente

*(Sección para admin/soporte — Configuración de Conversaciones → sección AlfaKnowledge.)*

- **Base URL / API Key / Knowledge Base Id:** conexión con AlfaKnowledge. El **Knowledge Base Id**
  es el "sector Conversaciones" — la base/colección de conocimiento que consulta el asistente.
- **Instrucciones del asistente:** un texto donde definís **cómo debe actuar y responder** el
  asistente: persona, tono, reglas, qué escalar, qué **no** decir. Ej: *"Sos el asistente de Alfa
  Net. Respondé cordial y breve en español rioplatense. No inventes precios: si preguntan precio,
  ofrecé pasar con un vendedor."* Se **anteponen** a cada sugerencia de respuesta.

Cambiar estas instrucciones **no requiere tocar código**: se guardan en la configuración de la base
activa (`TA_CONFIGURACION`, claves `CONV_ALFAKNOWLEDGE_*`).

---

## 11. Automatizaciones: fuera de horario y bot autónomo

*(Sección para admin/soporte — Configuración de Conversaciones → Automatizaciones. Requiere el
módulo AUTOMATIZACIONES activo.)*

### Fuera de horario (Nivel 0, regla fija)
Respuesta automática fija cuando llega un WhatsApp fuera del horario/días configurados. Sin IA.

### Bot autónomo (Nivel 3, con IA) — **apagado por defecto**
El bot responde WhatsApp entrantes **por sí solo**, pero **solo si cumple TODOS** estos
guardarraíles; si falla cualquiera, **escala silenciosamente a un humano** (queda sin asignar):

1. **Bot activo** (interruptor) + AlfaKnowledge configurado.
2. **Sin palabras de escalado** en el mensaje (configurable: humano, persona, reclamo, operador…).
3. **Conversación sin asignar** (si un humano la tomó, el bot calla) — opción recomendada.
4. **Ventana de 24h de WhatsApp activa.**
5. **No superó el tope** de respuestas por conversación (configurable, evita loops).
6. **La IA tiene respaldo suficiente** (contexto + fuentes citadas, sin pedir aclaración).
7. **"Responder solo fuera de horario"** (opcional): si está tildado, el bot solo contesta fuera del
   horario de atención configurado; en horario calla y lo dejan los agentes humanos.
8. **"Esperar N minutos antes de responder"** (opcional): si tiene minutos cargados, el bot no
   contesta apenas llega el mensaje — espera ese tiempo (para darle margen a un agente humano) y
   recién ahí vuelve a chequear todos los guardarraíles con el último mensaje del cliente. Si un
   agente ya respondió o tomó la conversación mientras tanto, el bot no dice nada. 0 = responde
   de inmediato.

Cada respuesta automática (`BotAutoReply`) y cada escalado (`BotHandoff`) quedan **auditados**.

> **Recomendación:** habilitarlo primero en una base de prueba, con el "sector Conversaciones"
> curado y las **instrucciones del asistente** cargadas, y con **"solo sin asignar"** tildado.
> Es la única función donde la IA **manda mensajes sin un humano** — usarla con criterio.

---

## 12. Prioridad, asignación automática e íconos del inbox

- **Badge de prioridad P1-P4:** sobre el avatar, un badge con el **color de la clasificación** del
  cliente (Archivos → Tablas → Clasificaciones). P1 = más importante. Sin clasificación → sin badge.
- **Línea del usuario asignado:** una línea vertical de color a la izquierda de la fila indica el
  técnico asignado (no se pinta toda la fila).
- **Avatar:** contactos sin foto muestran las **iniciales** del nombre y un **color propio por
  contacto**. El ícono de canal (WhatsApp/Instagram/…) va sobre el avatar con su color.
- **Auto-asignación:** el primero que responde una conversación sin dueño queda asignado.

---

## 13. Qué hace y qué NO hace

**Hace:**
- Analiza la conversación (resumen, intención, sentimiento).
- Propone respuestas con fuentes y un semáforo de confianza.
- Permite aprobar-y-enviar, editar, cambiar el tono o traducir.
- (Opcional) responde solo con guardarraíles, o escala a un humano.

**No hace:**
- **En modo copiloto**, no envía nada solo: el agente aprueba o edita.
- No inventa precios ni políticas: si no tiene respaldo, lo indica en vez de arriesgar.
- No reemplaza el criterio del agente.
- El **bot autónomo** sí puede enviar solo, pero **solo si está habilitado** y **solo** dentro de
  los guardarraíles; ante la duda, escala.

---

## 14. Requisitos técnicos y diagnóstico

| Función | Requiere |
|---|---|
| Panel del asistente | Módulo **ALFAKNOWLEDGE** activo |
| Sugerencia de respuesta (RAG) + fuentes | AlfaKnowledge configurado (URL, API key, KB id) |
| Análisis / tono / traducción / extracción a CRM | `OPENAI_API_KEY` en el server |
| Fuera de horario / bot autónomo | Módulo **AUTOMATIZACIONES** activo |
| Badge de prioridad | `TA_CLASIFICACIONES` con color |

Diagnóstico rápido:
- *No aparece el asistente* → módulo ALFAKNOWLEDGE inactivo o AlfaKnowledge sin configurar.
- *"El análisis por IA no está configurado"* → falta `OPENAI_API_KEY` (reiniciar la app).
- *La sugerencia no trae fuentes* → el sector de conocimiento no tiene contenido indexado.
- *El bot no responde* → revisar los 8 guardarraíles y la auditoría `BotHandoff`.

---

## 15. Preguntas frecuentes

**¿La IA le contesta al cliente sola?**
En modo copiloto, no: el agente aprueba o edita. Solo el **bot autónomo** (si el admin lo habilita)
responde solo, y únicamente dentro de sus guardarraíles.

**¿Qué diferencia hay entre "Aprobar y enviar" y "Editar antes"?**
"Aprobar y enviar" manda la respuesta del asistente tal cual, en un clic. "Editar antes" la copia
al compositor para que la ajustes y la envíes vos.

**¿Cómo cambio la forma en que responde el asistente?**
Con las **Instrucciones del asistente** (Config → AlfaKnowledge). No hace falta tocar código.

**¿El bot puede mandar algo incorrecto?**
Solo responde con respaldo de la base; ante baja confianza, palabras de escalado, ventana vencida o
tope alcanzado, **no responde** y escala. Aun así, conviene curar bien el conocimiento antes de
activarlo en producción.

**¿Puedo seguir atendiendo si la IA no está disponible?**
Sí. La atención normal funciona igual aunque el asistente no responda.

---

## Cierre

La IA en Conversaciones está para **ahorrar tiempo** y **no perder información**, no para reemplazar
el criterio humano. En copiloto, la palabra final es del agente; el bot autónomo es una herramienta
potente que se habilita con cuidado y guardarraíles.

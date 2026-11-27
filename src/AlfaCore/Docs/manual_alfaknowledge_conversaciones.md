# Manual — Asistente de IA y Automatizaciones en Conversaciones
### Alfa Gestión · WhatsApp, Instagram, Facebook, Mercado Libre y Chat interno

> Manual de usuario con notas técnicas para el equipo de soporte. Cubre el copiloto de IA
> (AlfaKnowledge), el análisis de la conversación, el envío asistido y el bot automático.

---

## Índice

1. [Panorama: qué hace la IA en Conversaciones](#1-panorama-qué-hace-la-ia-en-conversaciones)
2. [Antes de empezar: asignate la conversación](#2-antes-de-empezar-asignate-la-conversación)
3. [El panel del asistente (apertura manual)](#3-el-panel-del-asistente-apertura-manual)
4. [Análisis de la conversación (resumen, intención, sentimiento)](#4-análisis-de-la-conversación)
5. [La respuesta sugerida y el indicador de confianza](#5-la-respuesta-sugerida-y-el-indicador-de-confianza)
6. [Aprobar y enviar, o editar antes](#6-aprobar-y-enviar-o-editar-antes)
7. [Tono y traducción del borrador](#7-tono-y-traducción-del-borrador)
8. [Fuentes de la respuesta](#8-fuentes-de-la-respuesta)
9. [Hablarle a la IA sobre el caso](#9-hablarle-a-la-ia-sobre-el-caso)
10. [Configuración del asistente](#10-configuración-del-asistente)
11. [Cuándo responde solo y cuándo no](#11-cuándo-responde-solo-y-cuándo-no)
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

## 3. El panel del asistente (apertura manual)

El asistente no se abre automáticamente al entrar a una conversación.

Se abre de forma manual cuando el operador hace clic en el botón de sugerencia IA. Esto evita consumir créditos o tokens cada vez que se navega entre conversaciones.

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
| [[bi:send-check]] **Aprobar y enviar** | Manda la respuesta del asistente al cliente en un clic (respeta canal y la ventana de 24h de WhatsApp; se deshabilita si no corresponde). |
| [[bi:pencil]] **Editar antes** | Copia la sugerencia al compositor para revisarla/ajustarla y enviar cuando quieras. |
| [[bi:arrow-repeat]] **Regenerar** | Pide una sugerencia nueva con el mismo contexto. |
| [[bi:pencil-square]] **Corregir respuesta** | Le indicás a la IA qué cambiar; la corrección se guarda en AlfaKnowledge (mejora la base). |
| [[bi:x-lg]] **Descartar** | Cierra la sugerencia sin usarla. |

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

## 10. Configuración del asistente

Está en **Conversaciones → Configuración → Asistente IA** (hace falta el permiso **Configurar Conversaciones** o ser administrador). Las secciones de la izquierda son:

- **General**: el estado del asistente y sus límites (ver abajo).
- **Personalidad**: las **Instrucciones de comportamiento** (cómo habla y qué no tiene que decir) y la política **Cuando no sabe la respuesta**.
- **Información del negocio**: los datos que puede usar para responder (horarios, direcciones, formas de pago, precios y catálogo).
- **Lo que aprendió**: las correcciones que le enseñaron desde las conversaciones (ver **Enseñarle al asistente**).
- **Herramientas**: los datos del sistema que puede consultar (por ejemplo, el saldo o los comprobantes del cliente).
- **Derivación y urgencias**: cuándo pasa la charla a una persona y qué técnicos reciben las alertas de urgencia (con la plantilla de WhatsApp que se usa).
- **Conocimiento externo**: la conexión con AlfaKnowledge (**URL de AlfaKnowledge**, **API Key**, **Knowledge Base Id**, **Tiempo máximo de espera**) y las **Instrucciones para las sugerencias de la bandeja**. También se cargan bloques de **Información por tema** ([[bi:plus-circle]] **Agregar bloque**) y archivos.

Los cambios se guardan con [[bi:floppy]] **Guardar**, arriba a la izquierda.

### Estado: en qué canales y cuándo responde

En **General → Estado**:

- **Canales**: en qué canales responde solo (WhatsApp, chat del sitio web…).
- **Si no encuentra respuesta**: qué hace cuando no sabe (pedir una aclaración o pasar a una persona).
- **Cuándo abrir un ticket**: el criterio para abrir un ticket. Por defecto, solo cuando hace falta un desarrollo o un arreglo en el sistema, no para consultas.
- **Cuándo responde**: siempre, o solo fuera del horario de atención.

### Modelo de IA

En **General → Modelo de IA** se elige el modelo que usa el asistente en esta base:

- **Estándar** (recomendado): rápido y económico; alcanza para la mayoría de las consultas.
- **Avanzado**: entiende mejor los casos difíciles, pero consume varias veces más créditos por respuesta.

Cada opción muestra cuántos créditos consume por respuesta, aproximadamente. Al elegir un modelo más caro aparece una ventana para confirmar: el consumo se descuenta de los créditos del plan y, si se pasa, el excedente se factura a fin de mes. El cambio se aplica al tocar **Guardar**.

### Límites

En **General → Límites**:

- **Máximo de respuestas por conversación**: después de esa cantidad, el asistente pasa la charla a una persona (evita que se quede respondiendo sin fin). Se reinicia cuando la conversación se cierra.
- **Minutos de espera en horario de atención**: cuánto espera antes de responder, para darle tiempo al equipo. Fuera de horario responde enseguida. 0 = responde enseguida siempre.
- **Horas de charla que lee**: cuántas horas hacia atrás de la conversación lee para entender el contexto.
- **Segundos para que el cliente termine de escribir**: si el cliente manda varios mensajes seguidos, espera y responde todo junto.
- **Horas que se calla cuando contesta alguien del equipo**: si alguien del equipo responde o toma la conversación, el asistente no responde durante ese tiempo.

### Consumo de IA

En **Asistente IA → Consumo** se ve el **Consumo de IA este mes** y el **Plan de IA**. Si hace falta más, **Pedir plan**.

## 11. Cuándo responde solo y cuándo no

El asistente responde solo en los canales activados y **solo si**:

1. Está activo en esa base y en ese canal.
2. La conversación no está **pausada a mano** (ver abajo).
3. Nadie del equipo contestó ni tomó la conversación en las últimas horas configuradas.
4. No llegó al **Máximo de respuestas por conversación**.
5. En WhatsApp, la ventana de 24 horas está abierta.
6. Si está configurado **Cuándo responde: solo fuera de horario**, estamos fuera del horario de atención.

Lee la conversación completa (las horas configuradas) y decide con ese contexto: responde, pide una aclaración, pasa la charla a una persona o, si de verdad hace falta un desarrollo, abre un ticket. Si la charla ya se pasó al equipo pero nadie la tomó, sigue acompañando al cliente sin prometer lo que no puede hacer.

Si el cliente manda otro mensaje o una foto mientras el asistente arma la respuesta, la descarta y responde todo junto.

Cada respuesta automática y cada paso a una persona quedan registrados.

### Pausar el asistente (el bot) en una conversación

Para que el bot no conteste en un chat: En la conversación, arriba a la derecha, debajo del botón de asignarse, está [[bi:robot]] **Asistente activo**. Tocalo para pausarlo en esa conversación: pasa a [[bi:pause-circle-fill]] **Asistente pausado** y no responde hasta que lo vuelvas a tocar. Si lo pausó el sistema porque alguien del equipo está atendiendo, el botón lo indica.

### Enseñarle al asistente

En cada respuesta del asistente, en la conversación:

- [[bi:hand-thumbs-up]] **Esta respuesta del asistente estuvo bien**: la marca como correcta.
- [[bi:mortarboard]] **Enseñarle al asistente**: escribí **Qué tiene que hacer** o **Qué tiene que responder** en ese caso y tocá **Guardar y aprobar**.

Las correcciones se revisan en **Asistente IA → Lo que aprendió**, con las solapas **Aprendido** y **Rechazadas**: cada una se puede **Aprobar**, **Editar**, **Rechazar** o **Eliminar**.

> **Recomendación:** antes de activarlo en un canal, cargá bien la **Información del negocio** y las **Instrucciones de comportamiento**, y probalo en una base de prueba.
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
Con las **Instrucciones de comportamiento** (Configuración → Asistente IA → Personalidad) y enseñándole desde las conversaciones con [[bi:mortarboard]]. No hace falta tocar código.

**¿El bot puede mandar algo incorrecto?**
Responde con lo que sabe del negocio y de la charla; si no tiene respaldo, llegó al tope o la ventana de WhatsApp está vencida, **no responde** y pasa la charla a una persona. Aun así, conviene curar bien el conocimiento antes de
activarlo en producción.

**¿Puedo seguir atendiendo si la IA no está disponible?**
Sí. La atención normal funciona igual aunque el asistente no responda.

---

## Cierre

La IA en Conversaciones está para **ahorrar tiempo** y **no perder información**, no para reemplazar
el criterio humano. En copiloto, la palabra final es del agente; el bot autónomo es una herramienta
potente que se habilita con cuidado y guardarraíles.

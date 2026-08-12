# Guía de pruebas — Automatizaciones e IA de Conversaciones (+ Cotizaciones CRM)

Guía operativa para configurar y probar, paso a paso, lo construido en el módulo
Conversaciones (Niveles 0-3 de automatización + IA) y la cotización del CRM. Pensada para
hacer las pruebas en una **base de prueba** antes de habilitar nada en producción.

> Regla de oro: **el bot autónomo (Nivel 3) manda mensajes reales a clientes.** No lo
> actives en producción hasta validar todo lo demás y curar el conocimiento.

---

## 0. Prerrequisitos de configuración

| Qué | Dónde | Necesario para |
|---|---|---|
| `OPENAI_API_KEY` (y opcional `OPENAI_MODEL`) en el `.env` del server | raíz del repo / server | Análisis, extracción, tono/traducción, propuesta de servicio de cotización |
| AlfaKnowledge: Base URL + API Key + **Knowledge Base Id** + **Instrucciones del asistente** | Config de Conversaciones → sección AlfaKnowledge | Sugerencia de respuesta (RAG), bot supervisado y autónomo |
| Módulo **ALFAKNOWLEDGE** activo para el cliente | Admin de módulos | Que aparezca el panel del asistente |
| Módulo **AUTOMATIZACIONES** activo | Admin de módulos | Fuera de horario y bot autónomo |
| `TA_CLASIFICACIONES` con código, descripción y **color** | Archivos → Tablas → Clasificaciones de clientes | Badge de prioridad P1-P4 en el inbox |
| `EMAIL_SERVER/PORT/CTA/PASS/SSL` en `TA_CONFIGURACION` | Config del sistema | Enviar cotización por email |
| `ServidorWeb:UrlBasePublica` (SaaS) | `.env` / config | Link público de la cotización (WhatsApp) |

Después de tocar el `.env`: **cerrar la app → recompilar → reiniciar** (el `.env` se lee al arrancar).

---

## 1. Conversaciones — cambios visuales y de asignación

1. **Badge de prioridad P1-P4**: abrí el inbox. Cada fila con cliente clasificado debe mostrar,
   sobre el avatar (arriba a la derecha), un badge **P1/P2/P3/P4** con el **color** de su
   clasificación (TA_CLASIFICACIONES). Sin clasificación → sin badge. Tooltip: "Prioridad P1 · ...".
2. **Línea del usuario asignado**: una conversación asignada a un técnico muestra una **línea
   vertical a la izquierda** con el color del técnico — **no** toda la fila pintada. Seleccionala:
   la fila usa su fondo de selección normal + la línea, sin confundirse.
3. **Avatar por nombre**: contactos sin foto muestran **iniciales** (1-2 letras: "Fernando" → F,
   "Fernando Fernandes" → FF) con un **color estable por nombre** (mismo contacto = mismo color).
4. **Auto-asignación al responder**: tomá una conversación **sin asignar** y respondé. Debe quedar
   **asignada a vos** automáticamente (registro en `CONV_ASIGNACIONES` como "Auto-asignada al
   responder"). Si ya tenía dueño, no se pisa.

---

## 2. Asistente de IA — copiloto (Niveles 1 y 2)

Prerrequisito: módulo ALFAKNOWLEDGE activo + AlfaKnowledge configurado + `OPENAI_API_KEY`.

1. **Auto-apertura**: entrá a Conversaciones. El panel del asistente debe **abrirse solo**
   (si el módulo está activo). Se puede cerrar; vuelve a abrirse la próxima vez que entrás.
2. **Análisis de la conversación** (Nivel 1): al abrir/seleccionar una conversación con contenido,
   arriba del asistente aparece la tarjeta **Análisis** con: **resumen** (1-2 frases), **intención**
   (etiqueta) y **sentimiento** (Positivo/Neutro/Negativo, con color). Botón de **refresco** ↻.
3. **Sugerencia de respuesta** (RAG): el asistente propone una respuesta con **fuentes/citas**.
4. **Indicador de confianza** (Nivel 2): sobre la sugerencia, un semáforo:
   - 🟢 **Con respaldo** (contexto suficiente + citas) → "podés aprobar y enviar".
   - 🟡 **Sin respaldo suficiente** → "revisá antes de enviar".
   - 🔴 **Escalar** (la IA pidió aclaración) → "conviene pasar a un humano".
5. **Aprobar y enviar** (Nivel 2): con la sugerencia en pantalla, tocá **Aprobar y enviar** →
   la respuesta se manda en un clic (respeta canal y ventana de 24h; se deshabilita si no
   corresponde). Verificá que llegó al cliente y quedó en el hilo.
6. **Editar antes**: en vez de aprobar, tocá **Editar antes** → copia la sugerencia al compositor
   para ajustarla y enviar cuando quieras.
7. **Tono / Traducir** (Nivel 1): escribí un borrador en el compositor, tocá el botón **Aa** →
   elegí **Formal / Cordial / Más breve / Más amable** o **Inglés / Portugués**. El texto del
   compositor se reescribe manteniendo la intención. Probá cada opción.
8. **Instrucciones del asistente** (sector Conversaciones): en Config de Conversaciones →
   AlfaKnowledge → **Instrucciones del asistente**, cargá una persona/reglas (ej: "Respondé cordial
   y breve, no des precios, ofrecé pasar con un vendedor"). Guardá. Pedí una sugerencia nueva y
   confirmá que **respeta** esas instrucciones (tono, reglas).

---

## 3. Bot autónomo (Nivel 3) — ¡en base de prueba!

Prerrequisito: todo lo del punto 2 andando + conocimiento curado en el "sector Conversaciones".

1. En Config de Conversaciones → Automatizaciones → **Bot autónomo (IA)**:
   - Activá **Bot autónomo**.
   - Dejá **"Responder solo conversaciones sin asignar"** tildado.
   - Revisá **palabras de escalado** (humano, persona, reclamo, operador…).
   - **Máx. respuestas por conversación**: dejá 5 (o 2 para probar el tope).
2. **Camino feliz**: mandá (desde un WhatsApp de prueba) una pregunta **cubierta por la base**,
   a una conversación **sin asignar**, con **ventana activa**. El bot debería **responder solo**.
   Verificá auditoría `BotAutoReply`.
3. **Escalado por baja confianza**: preguntá algo que **no está en la base**. El bot **no** debe
   responder; queda sin asignar para un humano. Verificá auditoría `BotHandoff`.
4. **Escalado por palabra clave**: mandá "quiero hablar con un humano". El bot **no** debe
   responder.
5. **Handoff por asignación**: asigná la conversación a un técnico y mandá otro mensaje. El bot
   **calla** (porque ya tiene dueño).
6. **Tope**: con máx=2, mandá 3 preguntas cubiertas. A la 3ª el bot deja de responder.
7. **Ventana**: con ventana de 24h vencida, el bot no envía.
8. **Kill switch**: desactivá **Bot autónomo** y confirmá que deja de responder al instante.

---

## 4. CRM — Cotización

Prerrequisito: módulo CRM activo. Migraciones CRM aplicadas.

1. **Cotización de artículos**: en una oportunidad → panel Cotizaciones → **Artículos**.
   - Buscá un artículo: el precio debe salir de la **lista/clase del cliente** (o consumidor final
     si no hay cliente). Agregá líneas, editá cantidad/precio, mirá totales neto/IVA/total.
   - Probá el **asistente IA**: "todas las harinas, 10kg de cada una" → **Sugerir** → elegí
     artículos → **Agregar**. Los precios salen del maestro (la IA no los inventa).
   - Guardá.
2. **Cotización de servicio (IA)**: **Servicio (IA)** → escribí "cotizar Alfa Gestión para 5
   usuarios" → **Generar**. La IA redacta la propuesta en el editor (negrita, listas, imágenes).
   Ajustá con la barra de formato. Guardá.
3. **Motivo de pérdida**: mové una oportunidad a etapa "Perdida" (o marcala en el editor). Debe
   **exigir** elegir un motivo. El desglose aparece en la vista **Resumen**.
4. **Enviar cotización**: en la lista de cotizaciones → botón **Enviar** (avión):
   - **Email**: poné un correo → **Enviar**. Debe llegar la cotización en HTML.
   - **Link público**: en modo SaaS aparece el link (copiar). En modo legacy avisa que no está
     disponible (es esperado).
   - **WhatsApp**: si la oportunidad tiene conversación de origen, manda un texto (con link si hay).
5. **Extracción desde conversación** (IA): desde una conversación con contenido, botón **Crear
   oportunidad**. El formulario abre al instante y, un momento después, la IA completa **título** y
   **descripción** con lo que necesita el cliente ("completando con IA…").

---

## 5. Tablas de referencia (Clasificaciones)

1. Archivos → Tablas → **Clasificaciones de clientes**. Debe **cargar** (sin el error de "columna
   Id"). Verás las filas con su color.
2. **Editar** una fila: cambiá descripción/color → **Guardar cambios**. Debe **grabar** (el código
   queda solo-lectura, se mantiene con su padding).
3. **Nueva fila**: el **código se propone solo** (siguiente número). Guardá y confirmá que quedó
   alineado (numérico a la derecha).
4. **Borrar** una fila (que no esté en uso).

---

## Checklist rápido

- [ ] `.env` con `OPENAI_API_KEY`; app reiniciada.
- [ ] AlfaKnowledge configurado (URL, API key, KB id, instrucciones).
- [ ] Módulos ALFAKNOWLEDGE y AUTOMATIZACIONES activos.
- [ ] Badge P1-P4 con color / línea de usuario / avatar por nombre / auto-asignación.
- [ ] Panel del asistente auto-abre; análisis muestra resumen/intención/sentimiento.
- [ ] Confianza + Aprobar y enviar + Editar antes + Tono/Traducir.
- [ ] Instrucciones del asistente respetadas.
- [ ] Bot autónomo: responde con respaldo, escala sin respaldo / palabra clave / asignado; respeta tope, ventana y kill switch.
- [ ] Cotización artículos + servicio IA + motivo de pérdida + envío email/WhatsApp + extracción desde conversación.
- [ ] Tablas de referencia: crear/editar/borrar clasificaciones.

---

## Dónde mirar si algo falla

- **El asistente no aparece**: módulo ALFAKNOWLEDGE inactivo, o AlfaKnowledge sin configurar.
- **"El análisis por IA no está configurado"**: falta `OPENAI_API_KEY` en el server (reiniciar).
- **La sugerencia no trae citas**: el "sector Conversaciones" (KB) no tiene contenido indexado.
- **El bot no responde**: revisá los 7 guardarraíles (activo, AlfaKnowledge, sin palabra clave, sin
  asignar, ventana activa, tope, respaldo suficiente) y la auditoría `BotHandoff`.
- **Errores**: quedan registrados por el logging centralizado (`AUX_ERR`/eventos de app).

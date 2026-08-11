# CRM — Análisis de estado y roadmap

> Documento de trabajo para planificar la evolución del módulo CRM. El detalle descriptivo del
> módulo tal como está hoy vive en [`crm_modulo.md`](crm_modulo.md); este documento se enfoca en
> **qué falta** y **hacia dónde ir**. Fecha del análisis: 2026-08-11.

## 1. Estado actual (lo que ya está hecho)

El módulo tiene una base sólida, no es un esqueleto vacío.

**Modelo de datos** (`CRM_*`, 6 tablas): pipeline de etapas configurables (color, orden, flags
ganada/perdida), oportunidades, etiquetas (N:N), bitácora de actividad, y vínculo a mensajes de
Conversaciones.

**Oportunidades** con: título, descripción, etapa, prioridad (0-3), probabilidad (0-100 %),
importe estimado, fecha estimada de cierre, técnico/vendedor asignado, cliente (`VT_CLIENTES`),
contacto (`MA_CONTACTOS`), canal de origen y conversación/mensajes vinculados.

**UI** (`/crm`):

- Vista **Kanban** con drag & drop real (mover oportunidades entre etapas y reordenar etapas).
  > Nota: `crm_modulo.md` dice "sin drag/drop en la primera etapa" — quedó desactualizado, el
  > drag & drop ya está implementado.
- Vista **Lista** con columnas configurables y agrupación (etapa / técnico / probabilidad).
- Editor de oportunidad, ABM de etapas y etiquetas, filtros (texto, etapa, técnico, sin asignar,
  cliente, etiqueta, incluir cerradas), paginación server-side.
- Preferencias de vista guardadas por usuario (`TA_CONFIGURACION`, clave `USUVIEW-CRM-{hash}`).

**Diferencial**: el modelo está preparado para que una oportunidad nazca desde un chat de
WhatsApp (campos `IdConversacion`, `CanalOrigen`, mensajes de origen enlazados). Es un diferencial
fuerte frente a CRMs genéricos.

## 2. Qué falta (gaps)

Ordenados aproximadamente por impacto:

1. **Tareas / próxima acción con recordatorio** — hay `FechaCierreEstimada`, pero no existe el
   concepto de "próxima actividad agendada" (llamar tal día, mandar propuesta el viernes).
   `CRM_ACTIVIDAD` es solo bitácora de lo pasado, no agenda de lo futuro. **Es el gap más
   importante**: es lo que convierte el CRM de "lista linda" en herramienta de trabajo diario.
2. **Métricas / dashboard / forecast** — no hay ningún reporte: ni valor del pipeline por etapa,
   ni tasa de conversión, ni pronóstico ponderado (importe × probabilidad), ni ranking por
   vendedor, ni detección de oportunidades estancadas.
3. **Integración con Conversaciones incompleta** — el modelo guarda el origen, pero **no hay
   botón en Conversaciones** para crear la oportunidad desde mensajes seleccionados. Hoy es
   unidireccional a nivel datos, sin flujo de UI que lo dispare.
4. **Motivo de pérdida** — al marcar "Perdida" no se captura por qué (precio, competencia,
   timing…), dato clave para el análisis "por qué perdemos".
5. **Tipos de actividad estructurados** — el contacto (llamada / email / reunión) no queda
   registrado como actividad tipificada, solo como nota de texto libre.
6. **Cotizaciones / líneas de producto** — la oportunidad tiene un importe único a mano, no un
   detalle de productos/servicios (que AlfaCore ya modela en Ventas).
7. **Automatizaciones** — nada de "al pasar a etapa X, crear tarea Y" o "avisar si una
   oportunidad lleva N días sin moverse".
8. **Email integrado** — no hay envío/registro de mails desde la oportunidad (WhatsApp sí, vía
   Conversaciones, una vez que se conecte el punto 3).

## 3. Sugerencias según CRMs más usados

Tomando lo mejor de **Pipedrive** (simpleza operativa), **HubSpot** (automatización) y
**Zoho/Salesforce** (profundidad), adaptado a las fortalezas propias de AlfaCore:

- **"Actividad pendiente" siempre visible (Pipedrive)**: cada oportunidad muestra su próxima
  acción y su fecha; las vencidas se pintan en rojo. Pipedrive construyó todo su producto
  alrededor de "nunca dejar una oportunidad sin próximo paso". Encaja reusando el módulo de
  **Tareas** que AlfaCore ya tiene, vinculando tarea ↔ oportunidad.
- **Forecast ponderado**: por etapa, mostrar `Σ(importe × probabilidad)` y el total bruto. Barato
  (una query de agregación), altísimo valor para decisiones del dueño.
- **Motivos de pérdida configurables**: tabla chica `CRM_MOTIVOS_PERDIDA` + selección obligatoria
  al mover a "Perdida". Habilita el reporte "por qué perdemos".
- **Detección de oportunidades estancadas ("rotting")**: badge de alerta en el Kanban para las que
  llevan más de N días sin actividad (HubSpot/Pipedrive).
- **Timeline unificado en la oportunidad**: mezclar en una sola línea de tiempo notas, cambios de
  etapa, mensajes de WhatsApp vinculados y tareas — aprovechando el vínculo con Conversaciones.
- **Cotización desde la oportunidad reusando Ventas**: generar un presupuesto con líneas de
  producto y que el importe estimado salga de ahí en vez de tipearlo a mano.
- **Automatización simple estilo HubSpot**: "al ganar → crear tarea de alta de cliente"; "al pasar
  a Propuesta → agendar seguimiento a 3 días".

## 4. Roadmap propuesto (por impacto / esfuerzo)

### Fase 1 — Operatividad diaria (alto impacto, esfuerzo medio)
- Tareas / próxima acción con vencimiento, vinculadas a la oportunidad (reusar módulo Tareas).
- Próxima acción visible en Kanban y Lista, con resaltado de vencidas.
- Botón "Crear oportunidad" desde Conversaciones (cerrar el gap 3), autocompletando
  cliente/contacto/canal y enlazando los mensajes seleccionados.

### Fase 2 — Visibilidad para el que decide (bajo esfuerzo, alto valor)
- Mini-dashboard: pipeline por etapa (bruto y ponderado), tasa de conversión, ranking por
  vendedor, oportunidades estancadas.
- Motivo de pérdida obligatorio + su reporte.

### Fase 3 — Profundidad comercial
- Tipos de actividad estructurados (llamada / email / reunión / nota) con su timeline unificado.
- Cotización con líneas de producto reusando Ventas.

### Fase 4 — Automatización
- Reglas simples por cambio de etapa (crear tarea, agendar seguimiento, avisar).

## 5. Punto de partida acordado

Se arranca por la **Fase 1**, priorizando **tareas / próxima acción** (corazón operativo del CRM)
y, en paralelo o inmediatamente después, el **mini-dashboard de forecast** de la Fase 2 (bajo
esfuerzo, mucho valor). El resto se suma después, una etapa por vez.

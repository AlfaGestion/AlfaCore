# Tickets — Mesa de ayuda

## «Atender primero» (2026-11-24)

La lista y el Kanban no solo muestran los tickets: ordenan qué resolver primero y explican por qué.

### Qué calcula AlfaCore para cada ticket abierto

| Dato | Cómo se obtiene |
|---|---|
| **Esperando a** | *Nosotros* si el cliente escribió después de nuestra última respuesta (o nadie le respondió); *Cliente* si respondimos y no volvió a escribir, o si el ticket está en una etapa de espera; *Seguimiento* si tiene «Recordarme…» con fecha futura. Si el cliente escribe, vuelve a *Nosotros* aunque esté en espera o con seguimiento. |
| **Tipo de cliente** | La clasificación del cliente en Alfa Gestión (`TA_CLASIFICACIONES`, nombre y color). En **Tickets → Configuración → Tipo de cliente** cada clasificación sube 0 a 3 niveles (`TICKETS-CLASIFICACION-NIVELES-{código}`); un cliente con abono activo (`ClienteAtencionSql`) sube como mínimo `TICKETS-ABONO-ACTIVO-NIVELES` (1 por defecto). Prioritario = sube al menos un nivel. Sin configurar, una clasificación sube 1 si está marcada como prioritaria en Conversaciones (como antes). |
| **Prioridad efectiva** | Las estrellas del ticket más los niveles del tipo de cliente (tope 3). |
| **Plazo** | `TICK_PLAZOS`: horas hábiles de primera respuesta y de resolución por prioridad efectiva. Se cuentan en el horario de Conversaciones → Automatizaciones (`HorarioLaboral`) y el tiempo en etapas de espera no cuenta (`TICK_ESTADOS_HIST`). Estados: en término, por vencer (75 %), vencido, frenado. Un vencido se sigue mostrando aunque ahora se espere al cliente. |
| **Puntaje** | Suma de pesos (`TA_CONFIGURACION` grupo `TICKETS`, `TICKETS-PESO-*`): vencido / por vencer, prioridad, cliente prioritario, varios casos abiertos, horas hábiles esperando (con tope) y seguimiento vencido. Lo que espera al cliente o a una fecha va después. |

### Pantalla

- **Bandejas** con contador arriba de la lista y del Kanban: Para mí, Esperan respuesta nuestra, Vencidos y por vencer, Clientes prioritarios, Sin asignar, Seguimientos de hoy, Esperando al cliente, Creados por el bot y Todos los abiertos. Respetan los demás filtros.
- **Lista**: el orden por defecto es «Atender primero»; columnas Plazo, Esperando a, Tipo de cliente, Antigüedad (y opcionales Casos del cliente y Seguimiento). Debajo del título, los motivos.
- **Kanban**: dentro de cada etapa, primero lo que hay que atender; la tarjeta muestra plazo y espera.
- **Ficha**: franja con plazo, espera, tipo de cliente y motivos; enlace a los otros casos abiertos del cliente; botón **Recordarme…** (mañana, en 3 días, la semana que viene o fecha a elección).
- **Tickets → Configuración** (`/tickets/configuracion`): plazos por prioridad, porcentaje de «por vencer», etapas de espera y pesos del orden.

### Datos

- Script `2026-11-24-001__tickets_atencion.sql`: `TICK_TICKETS.FechaSeguimiento` / `SeguimientoDesde`, `TICK_ESTADOS_HIST`, `TICK_PLAZOS`, claves `TICKETS-*` y menú `D010185-WEB-TICKETS-CONFIG`.
- El historial de estados se graba desde el alta, la ficha, el Kanban y la acción masiva. Los tickets que ya estaban en espera antes del script toman como inicio de la espera su última modificación.
- El orden y las bandejas se calculan en memoria sobre los primeros 2000 tickets que cumplen los filtros.

### Correcciones de la lista y el Kanban (2026-11-24)

- El encabezado de columnas de la lista era `position: fixed` a 3.45rem (shell viejo) y quedaba debajo de la barra de acciones: no se veía ni se podía ordenar. Ahora es sticky dentro de la página.
- El Kanban traía los primeros N tickets en total ordenados por etapa: con muchos «Nuevo», las demás columnas quedaban vacías y una tarjeta movida desaparecía. Ahora trae los abiertos (hasta 2000) y los cerrados más recientes, y limita cada columna por separado.

### Tipo de cliente (2026-11-25)

- Cada empresa tiene sus propias clasificaciones (no hay nombres fijos en el código). En ALFA NET: A-Abono Full, B-Abonado Medio, C-Abonado Básico, D-No abonado; los servicios que incluye cada plan están en `MA_CUENTAS_SERVICIOS`.
- La lista, el Kanban, la ficha y el panel muestran el nombre de la clasificación; el borde y la estrella toman su color. El puntaje suma el peso «Cliente prioritario» por cada nivel.
- `TicketsAtencionCalculo.TipoCliente` resuelve niveles, nombre y color (pruebas en `TicketsTipoClienteTests`).

## Panel de seguimiento (`/tickets/seguimiento`)

- Indicadores (abiertos, esperan respuesta nuestra, vencidos / por vencer, esperando al cliente, seguimientos vencidos, de clientes prioritarios): cada uno abre la lista en la bandeja correspondiente (`/tickets?bandeja=...`).
- Entradas contra cierres por semana (12 semanas) y en el período elegido (30, 90 o 180 días).
- Abiertos por antigüedad (días corridos), separando los que esperan respuesta nuestra de los que esperan al cliente.
- Cumplimiento de plazos de los cerrados del período (`TicketsAtencionCalculo.EvaluarCerrado`): primera respuesta y resolución a tiempo, tiempo promedio de resolución sin la espera del cliente.
- Tiempo promedio en cada etapa (`TICK_ESTADOS_HIST`, se mide desde que existe el historial).
- Carga por técnico (`/tickets?bandeja=abiertos&tecnico=...`) y clientes con más casos abiertos (`&cliente=...&clienteNombre=...`).
- Los 10 que hay que atender primero.
- Script `2026-11-24-002__tickets_seguimiento_menu_web.sql` (menú `D010185-WEB-TICKETS-SEGUIMIENTO`).

## Avisos automáticos y prioridad con IA

### Avisos

- Proceso automático `TicketsAvisosHostedService`: cada 5 minutos, por base (mismo recorrido que los mensajes programados), solo en horario hábil. En **Tickets → Configuración** el botón **Revisar avisos ahora** hace lo mismo en el momento, aunque sea fuera de horario.
- Qué avisa (`TicketsAvisosCalculo`), solo de tickets que esperan respuesta nuestra:
  - plazo por vencer y plazo vencido (uno por plazo: respuesta y resolución);
  - seguimiento vencido («Recordarme…»; si se programa otro, avisa de nuevo);
  - cliente prioritario esperando más de N horas hábiles, a coordinación (uno por cada espera);
  - resumen diario «Tus pendientes de hoy» a cada técnico con algo pendiente, desde la hora elegida.
- A quién: al técnico asignado (push al usuario vinculado en `V_TA_Tecnicos.UsuarioAsociado`). Lo que no tiene técnico va a los **usuarios que coordinan**; si no hay ninguno cargado, ese aviso no se marca y sale cuando se carguen.
- `TICK_AVISOS` guarda cada aviso con clave única (ticket + tipo + clave): nunca se manda dos veces. Cada aviso queda también en la actividad del ticket (`AVISO`). Configuración muestra los últimos 15.
- Claves (`TA_CONFIGURACION`, grupo `TICKETS`): `TICKETS-AVISOS-ACTIVO`, `TICKETS-AVISO-POR-VENCER`, `TICKETS-AVISO-VENCIDO`, `TICKETS-AVISO-SEGUIMIENTO`, `TICKETS-AVISO-COORDINADORES` (máx. 150 caracteres), `TICKETS-AVISO-PRIORITARIO-HORAS` (0 = no), `TICKETS-RESUMEN-DIARIO-HORA` (vacío = no), `TICKETS-IA-PRIORIDAD-BOT`.

### Prioridad sugerida por IA

- En la ficha (también en un ticket nuevo), **Sugerir con IA** al lado de las estrellas: la IA lee el título, la descripción y los últimos mensajes de la conversación, y propone prioridad, motivo y resumen. **Aplicar** pone las estrellas (se guarda con el ticket); **Descartar** la cierra.
- Tickets que crea el bot: en cada revisión de avisos la IA revisa los del último día (hasta 10 por vez, una sola vez cada uno). Si nadie trabajó el ticket todavía, la prioridad se aplica; si no, solo se anota en la actividad (`IA`).
- La IA decide con el contexto, sin palabras clave (`TicketsIaPrioridad`). El tipo de cliente no se le pide porque AlfaCore ya lo suma a la prioridad efectiva. El consumo se registra como «Prioridad sugerida de tickets» y respeta el tope mensual de créditos.
- `OPENAI_BASE_URL` (variable de entorno del servidor, opcional) permite apuntar a un proxy compatible o a un servidor de prueba local.

### Datos

- Script `2026-11-24-003__tickets_avisos.sql`: `TICK_AVISOS` y las claves `TICKETS-AVISO*`, `TICKETS-RESUMEN-DIARIO-HORA`, `TICKETS-IA-PRIORIDAD-BOT`.

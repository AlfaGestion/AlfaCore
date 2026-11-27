# Tickets y partes de horas

**Tickets** registra los pedidos de soporte de los clientes y su seguimiento, y ayuda a decidir qué atender primero. **Partes de horas** registra el trabajo hecho para cada cliente y lo controla contra su bolsa de horas.

En la barra de arriba de Tickets están las pestañas **Mesa de ayuda** (los tickets), **Seguimiento** (el tablero con los números) y **Configuración** (plazos, orden y avisos).

## La mesa de ayuda

Debajo de las pestañas hay una barra con:

- A la izquierda: [[bi:plus-circle]] **Nuevo** (crea un ticket), [[bi:arrow-clockwise]] **Recargar** y **…** (más opciones: [[bi:tags]] **Administrar etiquetas** y, en la vista Lista, [[bi:sliders]] **Configurar vista**).
- En el medio, el buscador **Buscar por ticket, cliente, contacto, teléfono, email o técnico**. La flecha de la derecha del buscador abre los filtros.
- A la derecha, la forma de ver los tickets: [[bi:kanban]] **Kanban** (columnas por etapa) o [[bi:list-ul]] **Lista** (una tabla).

### Bandejas

Debajo de la barra hay botones con un número al lado: son las **bandejas**. Cada una muestra un grupo de tickets abiertos y el número dice cuántos hay:

- [[bi:list-ul]] **Todos**.
- [[bi:person-check]] **Para mí**: los asignados a vos que esperan respuesta nuestra, más tus seguimientos de hoy.
- [[bi:reply]] **Esperan respuesta nuestra**: el cliente escribió o todavía nadie le contestó.
- [[bi:alarm]] **Vencidos y por vencer**: se pasaron o están por pasarse del plazo.
- [[bi:star]] **Clientes prioritarios**: clientes con abono o con una clasificación prioritaria.
- [[bi:person-dash]] **Sin asignar**: ningún técnico los tiene.
- [[bi:calendar-check]] **Seguimientos de hoy**: tickets con un recordatorio para hoy o ya pasado.
- [[bi:hourglass-split]] **Esperando al cliente**: el reloj del plazo está frenado.
- [[bi:robot]] **Creados por el bot**: los que abrió el asistente de IA.
- [[bi:inbox]] **Todos los abiertos**.

### Qué atender primero

Dentro de cada bandeja, los tickets están ordenados por **Atender primero**: arriba va lo más urgente (vencido, cliente prioritario, más horas esperando, cliente con muchos casos abiertos). Debajo del título de cada ticket se ve por qué está en ese lugar, por ejemplo «Prioridad alta · 28 casos abiertos del cliente».

En la vista Lista, si ordenás por otra columna tocando su título, aparece el botón [[bi:sort-down]] **Atender primero** para volver a este orden.

Cada ticket muestra también:

- El **plazo**, con un reloj [[bi:alarm]]: en rojo **Vencido hace…**, en amarillo si está por vencer, y frenado mientras se espera al cliente.
- **Esperando a**: [[bi:reply]] **Nosotros** (nos toca responder) o el cliente, y desde hace cuánto (en días u horas hábiles).

Los plazos se cuentan en horario laboral y se cambian en **Configuración**.

### Vistas y filtros

- **Kanban**: los tickets en columnas por etapa (**Nuevo**, **En proceso**, **En espera**, **Escalado**, **Cerrado**). Arrastrá una tarjeta a otra columna para cambiarla de etapa. Tocá la tarjeta para abrir el ticket.
- **Lista**: una tabla con **Ticket**, **Plazo**, **Prioridad**, **Asignado**, **Cliente**, **Tipo de cliente** y otras columnas. Tocá el título de una columna para ordenar. Las columnas se eligen en **…** → [[bi:sliders]] **Configurar vista** (con [[bi:arrow-up]] y [[bi:arrow-down]] cambiás el orden, y **Guardar vista**).

Para filtrar, tocá la flecha a la derecha del buscador. Ahí están los filtros rápidos (**Todos**, **Abiertos**, **Sin asignar**, **Con mensajes**, **Creados hoy**, **Actualizados 7 días**), el cliente, el contacto, las búsquedas guardadas (**Alta prioridad abiertos**, **Seguimiento soporte**) y **Guardar búsqueda actual**. Tocá [[bi:search]] **Aplicar** o [[bi:eraser]] **Limpiar**. Los filtros que están en uso se ven al lado del buscador; tocá su [[bi:x]] para sacarlos.

## Crear un ticket

1. En la barra de arriba, tocá [[bi:plus-circle]] **Nuevo** (el botón azul de la izquierda).
2. Completá **Nombre del ticket**.
3. En **Cliente / empresa** buscá y elegí el cliente; en **Usuario / contacto**, la persona que lo pidió.
4. En **Asignado a**, elegí el técnico.
5. En **Prioridad**, tocá las estrellas: una es **Baja**, dos **Media** y tres **Alta**. Con [[bi:stars]] **Sugerir con IA** la IA propone la prioridad según el pedido: tocá **Aplicar** para usarla o **Descartar**.
6. En **Etiquetas**, tocá [[bi:tag]] para agregar etiquetas.
7. En la solapa **Descripción**, escribí el problema. Escribí **/** (o tocá [[bi:three-dots-vertical]]) para insertar títulos, listas, casillas o enlaces.
8. Tocá **Guardar**, arriba a la izquierda.

También se crea desde una conversación con el botón [[bi:life-preserver]] **Crear ticket desde mensajes** (ver el manual de **Conversaciones**).

## Trabajar un ticket

Tocá un ticket (en el Kanban o en la Lista) para abrir su ficha. Arriba de todo está el número y el título; abajo, los datos y las solapas.

- **Etapa**: la barra de flechas (**Nuevo**, **En proceso**, **En espera**, **Escalado**, **Cerrado**): tocá la etapa a la que pasa el ticket.
- Arriba, el recuadro de atención muestra el plazo, a quién se espera y los motivos. **Ver los N casos abiertos de…** abre los otros tickets de ese cliente.
- Solapas:
  - **Descripción**: el detalle del problema.
  - **Mensajes**: los mensajes de la conversación que se guardaron en el ticket. [[bi:chat-dots]] **Ver chat** abre la conversación y [[bi:person-lines-fill]] **Ver contacto**, el contacto.
  - **Actividad**: el historial. Escribí en **Nueva nota** y tocá [[bi:chat-left-text]] **Registrar nota** para dejar una nota interna.
  - **Partes de horas**: las horas cargadas a este ticket. [[bi:stopwatch]] **Cargar parte de horas** carga una nueva.
  - **Informes**: los informes para el cliente (ver **Informes para el cliente**).
- Al terminar, tocá **Guardar** (arriba a la izquierda). **Cancelar** vuelve sin guardar.

## Recordarme: que un ticket vuelva a aparecer otro día

Si un ticket tiene que esperar (por ejemplo, el cliente prueba algo y avisa), posponelo con un recordatorio (seguimiento) en vez de dejarlo en la lista:

1. En la ficha del ticket, arriba a la derecha, tocá [[bi:alarm]] **Recordarme…**.
2. Elegí [[bi:sunrise]] **Mañana**, [[bi:calendar3]] **En 3 días**, [[bi:calendar-week]] **La semana que viene**, o elegí una fecha y tocá **Programar**.

El ticket sale de **Esperan respuesta nuestra** y vuelve solo el día elegido, en **Seguimientos de hoy**. Si el cliente escribe antes, vuelve enseguida. Para sacar el recordatorio, [[bi:x-circle]] **Quitar seguimiento**.

## Informes para el cliente

En la ficha del ticket, solapa **Informes**:

1. Tocá [[bi:file-earmark-plus]] **Nuevo informe** para escribirlo, o [[bi:stars]] **Generar con IA** para que la IA lo redacte con lo que tiene el ticket.
2. Revisalo y tocá [[bi:save]] **Guardar**.
3. Tocá [[bi:link-45deg]] **Publicar y copiar link**: el informe queda disponible en un link para el cliente y el link se copia. [[bi:box-arrow-up-right]] **Ver como cliente** muestra cómo lo ve.
4. Mandalo con [[bi:envelope]] **Enviar por mail** o [[bi:whatsapp]] **WhatsApp** ([[bi:send]] **Enviar por la conversación del ticket** o [[bi:box-arrow-up-right]] **Abrir en WhatsApp**).

El cliente responde desde el link con **Conforme** o **Tengo una consulta**: el informe muestra [[bi:check2-circle]] o [[bi:question-circle]] según lo que eligió. [[bi:file-earmark-pdf]] **Descargar PDF** lo baja, [[bi:eye-slash]] **Despublicar** corta el link y [[bi:trash]] **Eliminar** lo borra.

## Trabajar varios tickets a la vez

En la vista [[bi:list-ul]] **Lista**, marcá la casilla a la izquierda de cada ticket (la casilla del título marca todos). Arriba aparecen los botones:

- [[bi:arrow-left-right]] **Cambiar etapa**.
- [[bi:person-check]] **Asignar**.
- [[bi:flag]] **Prioridad**.
- [[bi:x-circle]] **Quitar selección**.

Elegí el valor nuevo en la ventana y confirmá: se aplica a todos los marcados.

## Etiquetas

En la barra de arriba, tocá **…** → [[bi:tags]] **Administrar etiquetas**. Con [[bi:plus-circle]] **Nueva** creás una, y en cada etiqueta [[bi:pencil]] **Editar** o [[bi:trash]] **Eliminar**. Guardá con [[bi:floppy]].

## Seguimiento de tickets

En la pestaña **Seguimiento** (barra de arriba) está el tablero de la mesa de ayuda. Arriba a la derecha elegí el período: **Últimos 30 días**, **Últimos 90 días** o **Últimos 180 días**.

- Recuadros de arriba: **Abiertos**, **Esperan respuesta nuestra**, **Plazo vencido**, **Esperando al cliente**, **Seguimientos vencidos** y **De clientes prioritarios**.
- **Entradas y cierres por semana**: si entran más de los que se cierran, la pila crece.
- **Abiertos por antigüedad**: cuántos días hace que se abrieron.
- **Cumplimiento de plazos**: **Primera respuesta a tiempo** y **Resueltos a tiempo**, y el tiempo promedio de resolución en horas hábiles.

[[bi:list-ul]] **Ir a la lista** (arriba a la izquierda) vuelve a la mesa de ayuda.

## Configuración de tickets

En la pestaña **Configuración** (barra de arriba). Al terminar, tocá [[bi:floppy]] **Guardar configuración**, arriba a la izquierda.

- **Plazos por prioridad**: para cada prioridad (**Alta**, **Media**, **Baja**, **Sin prioridad**), las horas hábiles para la **Primera respuesta** y para la **Resolución**. **«Por vencer» desde** dice a qué porcentaje del plazo se marca en amarillo. **Estados en los que se espera al cliente** son las etapas en las que el reloj se frena (por ejemplo, EN_ESPERA).
- **Tipo de cliente**: cuántos niveles sube la prioridad según la clasificación del cliente (**No sube**, **Sube 1 nivel**…). **Cualquier cliente con abono activo** fija un mínimo para los clientes con abono.
- **Orden «Atender primero»**: los puntos que suma cada cosa (**Plazo vencido**, **Plazo por vencer**, **Por cada nivel de prioridad**, **Cliente prioritario**, **Cliente con varios casos abiertos**, **Por cada hora hábil esperando**, **Seguimiento vencido**). Más puntos, más arriba en la lista.
- **Avisos automáticos**: notificaciones (las de la campana) cuando un ticket está por vencer, vencido o con el seguimiento vencido. En **Usuarios que coordinan** van los usuarios que reciben lo que no tiene técnico. También se elige a qué hora sale el **Resumen diario a cada técnico**. [[bi:bell]] **Revisar avisos ahora** manda en el momento lo pendiente. Abajo se ven los **Últimos avisos enviados**.

## Partes de horas

Se entra desde el menú, en **Partes de horas**. Arriba se ven los totales del período: **Total**, **Facturable**, **Excedente** y **Pendientes**.

### Cargar un parte

1. Tocá [[bi:plus-circle]] **Nuevo parte** (el botón azul de arriba a la izquierda).
2. Elegí la **Fecha** y el **Cliente**.
3. En **Ticket**, tocá [[bi:search]] **Buscar** y después **Usar** en el ticket que corresponde.
4. En **Trabajo realizado**, completá una línea por tarea: **Técnico**, **Tipo**, **Tarea** (la descripción) y los **Minutos**. Con [[bi:plus-circle]] **Agregar línea** sumás otra; [[bi:trash]] la quita. Cada línea tiene que ser mayor a cero y de hasta 24 horas.
5. Tocá [[bi:floppy]] **Guardar**.

También se carga desde el ticket (solapa **Partes de horas** → [[bi:stopwatch]] **Cargar parte de horas**) o al cerrar una conversación.

### Consultar y aprobar partes

En **Partes registrados** se ven los partes del período. Para filtrar, usá el buscador de arriba (**Buscar por cliente o número de ticket**) y la flecha de su derecha (fechas, cliente, técnico, facturables, excedentes); después **Aplicar**.

En cada parte, en **Acciones**: **Editar**, **Aprobar**, **Rechazar** o **Anular**.

### Consumo y bolsa de horas por cliente

- **Consumo por cliente** muestra, por cliente, las horas **Trabajado**, **Facturable**, **Incluido** y **Excedente** del período.
- **Bolsa mensual por cliente**: elegí el **Cliente**, cargá las **Horas incluidas** por mes y la **Tolerancia minutos**, marcá **Activa** y tocá **Guardar bolsa**. Las horas que pasan la bolsa se marcan como excedente.

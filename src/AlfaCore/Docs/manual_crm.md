# CRM: oportunidades y cotizaciones

El módulo CRM reúne las **oportunidades** de venta, las **cotizaciones** y el **calendario** (que tiene su propio manual).

## Oportunidades

En **CRM → CRM oportunidades** se sigue cada posible venta desde el primer contacto hasta que se gana o se pierde.

### La barra de arriba

- [[bi:plus-circle]] **Nueva oportunidad** (el botón azul de la izquierda).
- [[bi:eraser]] **Limpiar filtros** y, en **…**, [[bi:sliders]] **Configurar vista**, [[bi:columns-gap]] **Etapas**, [[bi:tags]] **Etiquetas** y [[bi:emoji-frown]] **Motivos de pérdida**.
- En el medio, el buscador; el embudo [[bi:funnel-fill]] abre los filtros (por ejemplo **Sin asignar**, **Incluye ganadas/perdidas** o las **Archivadas**).
- A la derecha, la vista: [[bi:kanban]] **Tablero**, [[bi:table]] **Lista** o [[bi:bar-chart]] **Resumen** (el **Pipeline por etapa**, el **Ranking por vendedor** y los **Motivos de pérdida**).

### El tablero

Cada columna es una **etapa** del embudo de ventas. Arrastrá una tarjeta a otra columna para cambiarla de etapa; cuando llega a una etapa marcada como ganada, la oportunidad queda **ganada**.

Arriba de cada columna:

- [[bi:plus-lg]] **Carga rápida**: crea una oportunidad en esa etapa con pocos datos.
- [[bi:gear]] **Ajustes de etapa**: [[bi:pencil]] **Editar**, [[bi:magic]] **Automatizaciones**, [[bi:arrows-collapse]] **Plegar** (achica la columna) o [[bi:trash]] **Eliminar**.

Al final del tablero, [[bi:plus-lg]] **Etapa** agrega una etapa nueva; [[bi:check-lg]] la guarda.

### Crear y trabajar una oportunidad

1. Tocá [[bi:plus-circle]] **Nueva oportunidad**.
2. Completá el cliente o contacto, el título, el importe estimado, el vendedor y la etapa. La **prioridad** se marca con las estrellas [[bi:star-fill]].
3. Tocá [[bi:floppy]] **Guardar**.

En la ficha de la oportunidad (tocá la tarjeta, o [[bi:pencil]] **Abrir** en la lista):

- **Etiquetas**: [[bi:tag]] **Agregar o quitar etiquetas**.
- **Notas**: escribí y tocá [[bi:journal-plus]] **Registrar nota**. [[bi:whatsapp]] **WhatsApp** escribe al cliente.
- **Tareas**: [[bi:plus-lg]] **Agendar** una tarea con fecha; [[bi:circle]] la marca hecha y [[bi:trash]] la elimina.
- **Cotizaciones**: [[bi:box-seam]] **Cotización de artículos**, [[bi:stars]] **Cotización de servicio con IA**, o [[bi:file-earmark-text]] **Nueva cotización** (abre el módulo de Cotizaciones con esta oportunidad cargada). En cada cotización, [[bi:send]] **Compartir / enviar**.
- **Adjuntos**: [[bi:download]] descarga y [[bi:trash]] quita.
- Si vino de una conversación, [[bi:chat-dots]] **Ver conversación** la abre.
- [[bi:archive]] **Archivar**: deja de verse en el tablero y en la lista (se recupera desde el filtro **Archivadas**).

Para darla por perdida, tocá **Marcar como perdida**, elegí el **Motivo de pérdida** y confirmá con [[bi:check2]].

También se crea una oportunidad desde una conversación, con el botón [[bi:graph-up-arrow]] **Crear oportunidad de CRM desde esta conversación** (ver el manual de **Conversaciones**).

### Configurar etapas, etiquetas y motivos

En **…** de la barra de arriba están [[bi:columns-gap]] **Etapas**, [[bi:tags]] **Etiquetas** y [[bi:emoji-frown]] **Motivos de pérdida**: ahí se administran las etapas del embudo (y cuál cuenta como ganada), las etiquetas y los motivos por los que se pierde una venta.

## Cotizaciones

En **CRM → Cotizaciones** se arman presupuestos para enviar al cliente.

### Armar una cotización

1. Tocá [[bi:plus-circle]] **Nueva cotización**, el botón azul de arriba a la izquierda.
2. Elegí el **Cliente** (**Buscar cliente existente**, o [[bi:pencil]] **Cambiar** para reemplazarlo).
3. Tocá [[bi:plus-lg]] **Agregar** para sumar líneas:
   - Artículos o servicios: **Buscar artículo por descripción o código**.
   - [[bi:plus-square]] **Línea libre** (texto e importe a mano) o [[bi:info-circle]] **Línea informativa** (solo texto).
   - [[bi:stars]] **Asistente IA**: escribí lo que pide el cliente y tocá [[bi:magic]] **Sugerir**; marcá las líneas propuestas y [[bi:plus-lg]] **Agregar**.
4. Organizá las líneas en secciones con [[bi:folder-plus]] **Sección**, y movelas con [[bi:arrow-up]] **Subir** y [[bi:arrow-down]] **Bajar**.
5. Con [[bi:magic]] **Generar**, la IA arma una propuesta escrita a partir de lo que pide el cliente.
6. Tocá [[bi:floppy]] **Guardar borrador** (arriba).

### Enviar y seguir la cotización

Con la cotización abierta, en la barra de arriba:

- [[bi:send]] **Compartir**: la envía en PDF por email o WhatsApp, o copia un enlace.
- [[bi:check2]] **Marcar enviada**, [[bi:check-circle]] **Marcar aceptada** o [[bi:x-circle]] **Marcar rechazada** registran cómo sigue.
- [[bi:file-earmark-plus]] **Nueva versión**: crea otra versión si el cliente pide cambios, conservando la anterior. Las versiones se eligen en la lista **v1, v2…** del encabezado.
- [[bi:slash-circle]] **Anular**: anula la cotización. En el listado, marcando varias, se anulan todas juntas.
- [[bi:arrow-left]] **Volver al listado**.

### Configuración de cotizaciones

En **Cotizaciones → Configuración** se definen los **Descuentos**, el **Diseño e imagen del PDF**, el **Email saliente**, el **Mensaje predeterminado** y el configurador de módulos y packs de implementación. Tocá **Guardar configuración**.

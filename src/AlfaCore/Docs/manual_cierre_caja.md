# Caja y Bancos

El módulo Caja y Bancos tiene el tablero, el cierre de caja y las conciliaciones (bancaria y de tarjetas), que tienen su propio manual.

## Tablero de Caja y Bancos

Resumen del período elegido en el buscador de la barra superior: el **Flujo diario de caja** (ingresos y egresos por día) y los **Movimientos recientes**. El campo **Buscar en el detalle** filtra los movimientos por texto.

## Cierre de caja

Entrá a **Caja y Bancos → Cierre de caja**.

1. Seleccioná la fecha operativa. La lista de cajas muestra las que tienen movimientos ese día.
2. Elegí una **Unidad de negocio** para consultar un local, o **Todas las unidades**. La lista de cajas se ajusta a esa selección.
3. Elegí una caja o **Todas las cajas**.
4. Marcá las secciones que necesitás y tocá [[bi:arrow-clockwise]] **Cargar** (el botón azul de arriba a la izquierda).

El informe muestra saldos consolidados, tarjetas, cobranzas y ventas en cuenta corriente,
efectivo, acumulados de ventas, transferencias, ingresos y egresos. Las casillas agregan
comprobantes cancelados, ventas por producto o rubro, detalle de ventas/cobranzas y
detalle diario o mensual. Mensual toma el mes de la fecha seleccionada.

Las ventas por rubro y por producto corresponden a todas las cajas de la unidad seleccionada y de la fecha.
Cada sección tiene sus propias flechas [[bi:chevron-left]] **Página anterior** y [[bi:chevron-right]] **Página siguiente**. **Total general**
incluye todos los registros de esa sección, aunque se muestre una sola página.

Cambiar filtros no altera el resultado ya cargado hasta presionar **Cargar**.
Si falla una sección, tocá **Reintentar** en esa sección. Este informe consulta movimientos existentes;
no graba ni confirma un cierre.

Mientras se consulta una sección aparece una franja amarilla con **Leyendo…** y un indicador
animado. Esperá a que desaparezca: una consulta en curso todavía no es un resultado vacío.

## Filtros, Excel y envíos

Tocá el embudo [[bi:funnel-fill]] (**Filtros del cierre**) en la barra de arriba para elegir fecha, unidad de negocio, caja y secciones, y tocá [[bi:arrow-clockwise]] **Cargar**.

Los botones de la barra de arriba:

- [[bi:file-earmark-excel]] **Excel** descarga todas las filas de las secciones seleccionadas, con una hoja por sección y otra con los filtros. Se usan los filtros del último informe cargado. 
- [[bi:envelope]] **Email** permite indicar un destinatario y enviar el Excel adjunto, usando el correo configurado en Configuración general. 
- [[bi:whatsapp]] **WhatsApp**: buscá la conversación por nombre o teléfono con [[bi:search]] **Buscar**, elegila y tocá [[bi:send]] **Enviar Excel**. Si hace falta una plantilla aprobada y no hay, [[bi:plus-circle]] **Crear plantilla** te lleva a crearla. Después revisá el estado del envío en Conversaciones.

 Estos envíos requieren la configuración y los permisos correspondientes.

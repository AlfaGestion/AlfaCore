# Movimientos de stock

Pantalla **Stock → Movimientos de stock** (`/stock/movimientos`). Reemplaza en la web a la carga de comprobantes de stock de Alfa Gestión (V_MV_STOCK) y graba exactamente igual, así que lo que se carga acá se ve en Alfa Gestión y al revés.

## Tipos de movimiento

Los tipos salen de la tabla de comprobantes, los que tienen sistema **Stock**. Si un tipo no aparece, revisá que exista para tu unidad de negocio.

- **IR · Informe de recepción (ingreso):** suma la cantidad al depósito.
- **VS · Vale de salida:** resta la cantidad del depósito.
- **TR · Transferencia entre depósitos:** en un solo comprobante resta del depósito de origen y suma al de destino.
- **AJP · Ajuste de stock:** cargás lo que contaste y el sistema graba la diferencia, para que el saldo del depósito a la fecha del ajuste quede igual a lo contado.
- **SI · Saldo inicial:** funciona igual que el ajuste.

La pantalla abre en la **lista de comprobantes de stock** del mes. Arriba, en la barra de búsqueda, escribís el número, un código de artículo o parte de las observaciones; con el embudo elegís el período, el tipo, el depósito y si se ven los anulados. El paginador también está arriba. Tocá un comprobante para verlo.

Para entrar directo a cargar un tipo podés usar `?tipo=TR` (o IR, VS, AJP, SI) en la dirección.

## Cargar un movimiento

1. Tocá **Nuevo movimiento** (arriba). Elegí el tipo, la unidad de negocio, la fecha y el depósito (en una transferencia, también el depósito de destino). El motivo de stock viene completado con el que está configurado por defecto.
2. Escribí el código o el código de barras del artículo y apretá Enter. Con `3*código` se agrega directo con cantidad 3.
3. Escribí la cantidad y apretá Enter: la línea se agrega y el cursor vuelve al código.
4. Si el artículo se maneja por bultos, cargá los **Bultos** en la línea y el sistema calcula las unidades con la equivalencia del artículo.
5. Tocá **Grabar** (arriba). El sistema vuelve a calcular todo, muestra un resumen y graba cuando confirmás; después vuelve a la lista. **Cancelar** descarta la carga (pide confirmación si cargaste artículos).

Si cargás el mismo artículo dos veces, se suman las cantidades. En un conteo eso sirve para lo que contaste en dos lugares.

La carga se guarda en el navegador mientras trabajás: si se corta la conexión o recargás la página, la recuperás.

## Buscar artículos (F5)

Con **F5** (o la lupa de la línea nueva) se abre la búsqueda por código, descripción o código de barras, con el stock del depósito elegido. Te movés con las flechas, elegís con Enter y cerrás con Esc.

Algunos artículos todavía no se pueden mover desde esta pantalla: los que usan lotes, series o talle y color, los que piden equivalencia o medidas al cargar y, en IR, VS y TR, los que tienen insumos. Aparecen marcados en la búsqueda; esos se siguen moviendo desde Alfa Gestión.

## Ajustes y saldo inicial

En AJP y SI la columna **Contado** es lo que hay físicamente. Mientras cargás, la grilla muestra:

- **Saldo al dd/mm:** el stock del depósito con todos los movimientos hasta ese día inclusive.
- **Diferencia:** lo que se va a grabar (en verde si suma, en rojo si resta).

Se graba un solo comprobante AJP o SI: las diferencias positivas suman y las negativas restan. Si lo contado coincide con el saldo, la línea se graba con diferencia cero.

Si el ajuste tiene una fecha pasada y después hay otros movimientos, el saldo de hoy va a ser lo contado más esos movimientos. La pantalla te avisa cuando pasa.

Si alguien mueve stock de esos artículos entre que calculaste y grabaste, el sistema no graba y te pide volver a calcular. Así la diferencia nunca queda mal.

## Transferencias

El depósito de destino tiene que ser distinto del de origen. La columna **Stock origen** muestra lo que hay en el depósito de origen y **Queda**, cómo queda después de la salida.

Los depósitos de otra empresa (otra base) no aparecen: esas transferencias se siguen haciendo desde Alfa Gestión.

## Modificar o anular

Buscalo en la lista y tocalo: se abre en modo consulta. Tocá **Modificar**, cambiá lo que haga falta y tocá **Grabar cambios** (o **Cancelar** para volver sin cambios). **Anular** está en el menú … de la barra de arriba. Anular no borra el comprobante: lo marca como anulado y deja de contar en el stock, igual que en Alfa Gestión.

No se pueden modificar ni anular desde acá los comprobantes que:

- tienen lotes o series, o artículos en distintos depósitos;
- se generaron desde otro comprobante (por ejemplo, la recepción de una orden de compra);
- generaron un parte de consumo de insumos o un asiento contable;
- son transferencias con viajes asociados;
- son ajustes que salieron de una toma de inventario.

## Controles

- La fecha tiene que estar dentro de la fecha operativa y ser posterior al cierre de stock (Contabilidad → Fechas de cierre).
- El depósito tiene que ser de la unidad de negocio o estar marcado como compartido.
- Si la base tiene activo **NoFacturaSinStock**, no se graban salidas que dejen el depósito en negativo.
- Si la base genera asientos de stock (**AsientosXStock**) y el motivo elegido tiene cuenta contable, el movimiento todavía se carga desde Alfa Gestión.

## Productos perecederos

Se activa en **Configuración general → Artículos → Trabajar con productos perecederos** (clave `UsaPerecederos`). Si está apagada, no se ve nada de esto.

- En el maestro de artículos, solapa Datos generales, se marca **Producto perecedero** y se cargan los **días de vencimiento**. Es el mismo dato que usa Alfa Gestión (campo Largo), así que se ve igual en los dos sistemas.
- Un artículo que pide medidas al cargar no puede ser perecedero, porque las medidas usan ese mismo dato. Si el artículo usaba lotes, al marcarlo perecedero deja de usarlos, igual que en Alfa Gestión.
- En **Movimientos de stock**, los ingresos de un perecedero (IR, SI y AJP cuando la diferencia suma) muestran la columna **Vence**. Propone la fecha del movimiento más los días del artículo y se puede cambiar. Se graba como en Alfa Gestión, en el número de lote con formato año-mes-día.
- Las salidas (VS) y las transferencias (TR) no registran vencimiento: el reporte supone que sale primero lo más viejo.

## Reportes de stock

Pantalla **Stock → Reportes de stock** (`/stock/reportes`). Reemplaza en la web a la Consulta de Stock de Alfa Gestión (ficha de stock). Cada solapa se genera sola al entrar. Los filtros están en la barra de búsqueda de arriba (texto del artículo y, con el embudo, fecha o período, depósito, rubro, familia y opciones) y el paginador también está arriba. Con **Excel** bajás la solapa completa.

- **Saldos:** saldo de cada artículo por depósito a la fecha elegida (**Saldo al**), con los movimientos hasta ese día inclusive. Si la base tiene configurada la unidad de bultos, muestra también los **Bultos**, calculados con la equivalencia del artículo. Con **Depósitos en columnas** sale un renglón por artículo y una columna por depósito.
- **Movimientos del período:** saldo inicial, entradas, salidas y saldo final de cada artículo entre dos fechas.
- **Ficha de stock:** todos los movimientos de un artículo en el período, con el saldo inicial y el saldo acumulado renglón por renglón. Abajo muestra el saldo por depósito a la fecha final. Escribí el código en la barra de búsqueda y Enter, o buscalo por descripción con **F5**. Desde cualquier otra solapa, el doble clic en un renglón abre su ficha.
- **Perecederos:** el stock de los productos perecederos repartido por fecha de vencimiento, con su estado: **vencido**, **por vencer** (dentro de los días que elijas) o **vigente**. Si un ingreso no tiene el vencimiento registrado, se estima con la fecha de ingreso más los días del artículo y se marca como estimado.

El reporte no cuenta los comprobantes anulados ni los artículos suspendidos o que no controlan stock.

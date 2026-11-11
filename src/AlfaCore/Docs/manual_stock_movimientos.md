# Movimientos de stock

Pantalla **Stock → Movimientos de stock** (`/stock/movimientos`). Reemplaza en la web a la carga de comprobantes de stock de Alfa Gestión (V_MV_STOCK) y graba exactamente igual, así que lo que se carga acá se ve en Alfa Gestión y al revés.

## Tipos de movimiento

Los tipos salen de la tabla de comprobantes, los que tienen sistema **Stock**. Si un tipo no aparece, revisá que exista para tu unidad de negocio.

- **IR · Informe de recepción (ingreso):** suma la cantidad al depósito.
- **VS · Vale de salida:** resta la cantidad del depósito.
- **TR · Transferencia entre depósitos:** en un solo comprobante resta del depósito de origen y suma al de destino.
- **AJP · Ajuste de stock:** cargás lo que contaste y el sistema graba la diferencia, para que el saldo del depósito a la fecha del ajuste quede igual a lo contado.
- **SI · Saldo inicial:** funciona igual que el ajuste.

Para entrar con un tipo ya elegido podés usar `?tipo=TR` (o IR, VS, AJP, SI) en la dirección.

## Cargar un movimiento

1. Elegí el tipo, la unidad de negocio, la fecha y el depósito (en una transferencia, también el depósito de destino). El motivo de stock viene completado con el que está configurado por defecto.
2. Escribí el código o el código de barras del artículo y apretá Enter. Con `3*código` se agrega directo con cantidad 3.
3. Escribí la cantidad y apretá Enter: la línea se agrega y el cursor vuelve al código.
4. Si el artículo se maneja por bultos, cargá los **Bultos** en la línea y el sistema calcula las unidades con la equivalencia del artículo.
5. Tocá **Grabar**. El sistema vuelve a calcular todo, muestra un resumen y graba cuando confirmás.

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

Tocá **Modificar / anular**, elegí el comprobante en la lista (se puede filtrar por número) y se carga en la grilla. Cambiá lo que haga falta y tocá **Grabar cambios**, o tocá **Anular**. Anular no borra el comprobante: lo marca como anulado y deja de contar en el stock, igual que en Alfa Gestión.

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

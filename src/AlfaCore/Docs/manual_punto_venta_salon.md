# Punto de Venta: Salón, cocina y carta QR

El modo **Salón** del Punto de Venta es para bares, cafeterías y restaurantes. Tiene tres partes que trabajan juntas:

- **Salón** (`Ventas → Punto de Venta` → el punto de venta de salón): el plano de mesas. Lo usan los mozos y el encargado.
- **Cocina** (botón **Cocina** del salón): la pantalla de comandas de la cocina y de la barra. También puede imprimir en una comandera.
- **Carta QR**: cada mesa tiene un QR. El cliente lo escanea con la cámara del celular y ve la carta; si el local lo habilita, también pide, llama al mozo y pide la cuenta. No tiene que descargar nada ni registrarse.

El cobro es el mismo del Mostrador: los mismos comprobantes, medios de pago, Mercado Pago Point y turno de caja.

## Antes de empezar

Lo prepara una sola vez el administrador:

1. En **Agregar o editar puntos**, creá un punto de venta con modo **Salón** y elegí la caja y la sucursal.
2. En el mismo lugar, cargá los **sectores** (Salón, Terraza, Barra…) y las **mesas** de cada sector, con su capacidad.
3. Entrá al salón y acomodá las mesas en el plano (ver **Editar el plano**).
4. En cada artículo que se prepara, indicá a dónde va: en el campo de destino del artículo (**TalleDefault**) poné **C** para cocina, **B** para barra o **R** para barra 2. Es el mismo dato que usa el salón de Alfa Gestión. Lo que no tiene letra (gaseosas, agua, cubiertos) no pasa por la cocina.
5. Los **mozos** son los vendedores de Alfa Gestión. Si querés que sea obligatorio elegir mozo al abrir una mesa, activá **Requiere mozo** en la configuración del salón.
6. Si cobrás **cubierto**, configurá el artículo de cubierto: se agrega uno por persona al abrir la mesa.
7. Si vas a usar la carta en el celular, configurala desde **Carta QR** e imprimí los QR (ver **Carta QR: configuración**).

## Trabajar con el salón

Arriba están los sectores (por ejemplo, Salón principal y Terraza), cada uno con cuántas mesas ocupadas tiene. Tocá un sector para ver su plano.

Cada mesa muestra su estado con un color:

- **Borde punteado:** libre. Debajo dice cuántos lugares tiene.
- **Azul:** ocupada. Muestra el total, hace cuánto se abrió y los cubiertos.
- **Borde amarillo:** tiene cosas cargadas que todavía no se mandaron a la cocina.
- **Verde que late:** hay platos **listos para servir**.
- **Naranja:** pidió la cuenta.
- **Insignias:** *sin enviar*, *QR* (pedido desde el celular para confirmar), *Llama* (pidió al mozo) y *Cuenta*.

Arriba también ves cuántas mesas hay libres, ocupadas, pidiendo la cuenta y con platos para servir. El salón se actualiza solo cada pocos segundos, así que todos los mozos ven lo mismo.

## Abrir una mesa

1. Tocá una mesa libre.
2. Elegí el mozo, la cantidad de personas (cubiertos) y, si querés, una observación.
3. Tocá **Abrir y cargar** para ir directo a cargar lo que piden, o **Solo abrir** para dejarla abierta.

Si dos mozos abren la misma mesa al mismo tiempo, no se duplica: el segundo ve la mesa que abrió el primero.

Tocá una mesa ocupada para ver su detalle a la derecha: lo cargado, el estado de cada plato y el **código de mesa** (4 números) que usan los clientes en la carta QR.

## Cargar lo que piden

1. En el detalle de la mesa, tocá **Agregar artículos**.
2. Se abre el Mostrador con el carrito vacío. Buscá y agregá los artículos como siempre.
3. Tocá **Enviar comanda**. Lo cargado se suma a la mesa y sale a la cocina o a la barra, según el artículo.

Cada vez que agregás es una **ronda** nueva: lo que ya se mandó a la cocina no se toca.

## Enviar a cocina y barra

Lo que se carga y todavía no salió aparece como **Sin enviar**. Tocá **Enviar a cocina/barra** para mandarlo. Se arma una comanda para cada destino: lo de cocina va a la cocina y lo de barra a la barra.

Los artículos sin destino (gaseosas, agua, cubiertos) quedan como servidos al enviar.

Para quitar algo, tocá el tachito de la línea:

- Si todavía no se envió, se borra.
- Si ya se envió, te pide el **motivo** y queda registrado como anulado (no se borra), así queda constancia.

Cuando el mozo lleva un plato, puede tocar **Servido** en la línea. La cocina también lo marca al tocar **Entregado**.

## Pedidos que llegan por la carta QR

Cuando un cliente pide desde el celular, la mesa muestra la insignia **QR** y las líneas aparecen resaltadas como **Sin enviar**. El mozo revisa el pedido y toca **Aceptar pedido QR y enviar**: sale a la cocina como cualquier otro.

Si algo no corresponde, se quita con el tachito antes de aceptar.

Si en la configuración de la carta elegiste que los pedidos vayan directo a la cocina, no hace falta aceptarlos.

## Llamados de las mesas

Cuando un cliente toca **Llamar al mozo** o **Pedir la cuenta** en el celular, aparece un aviso arriba del plano (por ejemplo, *Mesa 5 llama al mozo*), suena un aviso y la mesa se marca. Tocá **Voy** cuando lo atiendas: el cliente ve en su celular que el mozo ya va.

## Precuenta y cobro

- **Precuenta:** imprime el ticket con lo consumido y el total (no es factura) y marca que la mesa pidió la cuenta. Si después agregan algo, la mesa vuelve a quedar abierta sola. También podés tocar **Reabrir**.
- **Cobrar:** abre el Mostrador con toda la mesa. Lo que se pidió en varias rondas aparece junto (por ejemplo, 3 cafés en una sola línea). Elegí el comprobante y los medios de pago y confirmá como en cualquier venta. Al terminar, la mesa queda libre y volvés al salón.

Para cobrar tiene que estar abierto el turno de la caja, igual que en el Mostrador.

## Cambiar, unir o liberar una mesa

Desde el detalle de la mesa:

- **Mozo / cubiertos:** cambia el mozo, la cantidad de personas o la observación. El cubierto se ajusta solo.
- **Cambiar mesa:** pasa todo a una mesa libre (por ejemplo, si se mudan a la terraza).
- **Unir con…:** junta la cuenta con otra mesa ocupada. Todo pasa a la mesa elegida y esta queda libre.
- **Liberar sin cobrar:** deja la mesa libre y anula lo cargado. Si algo ya había salido a la cocina, te pide el motivo. Queda registrado.

## Editar el plano

1. Tocá **Editar plano**.
2. Arrastrá cada mesa a su lugar, con el mouse o con el dedo.
3. Tocá **Guardar plano**. **Cancelar** deja todo como estaba.

Las mesas nuevas, que todavía no tienen lugar, aparecen ordenadas en fila hasta que las acomodes.

## Pantalla de cocina y barra

Abrila con el botón **Cocina** del salón, en la tablet o en la PC de la cocina.

- Arriba elegí qué mostrar: **Todo**, **Cocina**, **Barra** o **Barra 2**. Cada dispositivo recuerda lo que elegiste.
- Cada tarjeta es una comanda: la mesa, el número, el destino, el mozo y los minutos desde que llegó. El borde se pone amarillo a los 10 minutos y rojo a los 20.
- Tocá un plato para pasarlo de **Nuevo** a **En marcha**, y otra vez para **Listo**. Si te equivocás, tocalo de nuevo y vuelve a En marcha.
- **Empezar** pasa toda la comanda a En marcha. **Todo listo** marca todos los platos como listos. Cuando la comanda está lista, **Entregado** la saca de la pantalla.
- Las aclaraciones del cliente (por ejemplo, *sin cebolla*) aparecen debajo del plato.
- Cuando entra una comanda nueva suena un aviso. El navegador solo deja sonar después de que tocaste la pantalla una vez.

Cuando la cocina marca un plato como listo, la mesa se pone verde en el salón y el cliente lo ve en su celular.

## Comandera (imprimir comandas)

Si en la cocina se trabaja con papel, en la PC que tiene la impresora de comandas abrí la pantalla de **Cocina** y tildá **Comandera**. Desde ese momento, cada comanda nueva se imprime sola en la impresora predeterminada de esa PC.

- Podés dejar tildados **Pantalla** y **Comandera** a la vez, o destildar Pantalla si solo querés que imprima.
- Elegí el destino (Cocina o Barra) para que esa comandera imprima solo lo suyo.
- Para que imprima sin mostrar el cuadro de impresión, la PC tiene que abrir el navegador con la opción `--kiosk-printing` (la misma configuración que la caja del Mostrador). Pedíselo a soporte.
- Si hay dos comanderas para el mismo destino, cada comanda sale una sola vez.
- El botón de la impresora en cada tarjeta **reimprime** esa comanda (sale marcada como reimpresión).
- La comandera imprime las comandas de la última hora que todavía no salieron. Si estuvo apagada más tiempo, reimprimí desde la pantalla las que hagan falta.

## Platos agotados

En la pantalla de cocina, tocá **Agotados**:

- Buscá el plato o la bebida y tocá **Agotado**. Desde ese momento no se puede pedir (ni el mozo ni desde el celular) y desaparece de la carta QR.
- Cuando vuelva a haber, tocá **Hay de nuevo**.

## Carta QR: configuración

En el salón, tocá **Carta QR**:

- **Uso de la carta:**
  - *Desactivada:* el QR muestra un aviso de que la carta no está disponible.
  - *Solo ver la carta y precios:* el cliente mira la carta pero pide al mozo.
  - *Ver, pedir, llamar al mozo y pedir la cuenta:* la carta completa.
- **Enviar a cocina sin que el mozo confirme:** si lo tildás, lo que piden desde el celular sale directo a la cocina. Si no, el mozo lo acepta con un toque (recomendado al empezar).
- **Mensaje de bienvenida:** aparece arriba de la carta (por ejemplo, *La cocina cierra a las 23:30*).
- **Familias de la carta:** marcá las familias de artículos que forman la carta. Marcar una familia incluye sus subfamilias. Sin ninguna marcada se muestran todas las familias con artículos con precio, así que conviene elegirlas.

Los precios de la carta son los mismos que usa el Mostrador. Los cambios de carta pueden tardar hasta un minuto en verse en los celulares; los agotados se ven al instante.

## Imprimir los QR de las mesas

En **Carta QR** tocá **Imprimir QR de las mesas**. Sale una tarjeta por mesa con el nombre del local, el nombre de la mesa y su QR. Tocá **Imprimir**, recortalas y pegá cada una en su mesa.

Cada QR es fijo para su mesa: no hace falta reimprimirlo aunque cambies la carta. Si agregás mesas nuevas, volvé a entrar e imprimí las nuevas.

## Qué ve el cliente en el celular

1. Escanea el QR de la mesa y se abre la carta, con el nombre del local y de la mesa.
2. Si la mesa está libre, toca **Abrir la mesa** y le aparece el **código de mesa** de 4 números para pasarle a sus acompañantes.
3. Si la mesa ya está abierta, escribe el código (se lo pasa quien la abrió o el mozo) y queda en la misma cuenta.
4. Elige por categoría o busca, suma con **+** y toca **Ver pedido**. Ahí puede agregar aclaraciones (por ejemplo, *sin azúcar*) y toca **Enviar pedido**.
5. En **Mi mesa** ve todo lo pedido por la mesa y el estado de cada plato: *Por confirmar*, *En cocina*, *En marcha*, *¡Listo!* y *Servido*, con el total.
6. Puede tocar **Llamar al mozo** o **Pedir la cuenta**. Paga como siempre, al mozo o en la caja.

Cuando la mesa se cobra, el código deja de servir y el celular muestra que la mesa se cerró.

## Preguntas frecuentes

**¿Puede alguien pedir desde su casa con una foto del QR?** No, porque para pedir hace falta el código de la mesa abierta, que cambia cada vez que se abre y deja de servir al cobrar. Además, por defecto el mozo confirma cada pedido antes de que salga a la cocina.

**¿El cliente puede cambiar los precios?** No. El precio siempre lo pone el sistema, igual que en el Mostrador.

**Un plato no sale en la pantalla de cocina.** Revisá el destino del artículo (C, B o R). Sin destino, el artículo no pasa por la cocina y queda servido al enviar.

**La comandera no imprime.** Verificá que en esa PC esté tildado **Comandera**, que el destino elegido sea el correcto y que la impresora de comandas sea la predeterminada de Windows. Si aparece el cuadro de impresión, falta la opción `--kiosk-printing` en el navegador.

**No suena el aviso.** El navegador solo permite sonidos después de que alguien tocó la pantalla. Tocá cualquier parte de la pantalla una vez al abrirla.

**¿Puedo usar el salón de Alfa Gestión y el de AlfaCore a la vez?** No conviene: son registros distintos y una mesa abierta en uno no se ve en el otro. Elegí uno de los dos para el local.

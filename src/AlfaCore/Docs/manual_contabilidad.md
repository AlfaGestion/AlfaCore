# Contabilidad

Las pantallas de **Gestión Contable** consultan la base activa. Los reportes no modifican asientos: solo los muestran y los exportan. La única pantalla que graba es **Asientos**, y solo si tu usuario tiene permiso.

## Cómo usar los reportes contables

**Libro diario**, **Subdiarios**, **Mayor**, **Sumas y saldos** y **Balance general** se usan de la misma forma:

1. Abrí el embudo de la barra superior. A la izquierda está la columna **Período** y a la derecha, **Opciones**.
2. En **Período** elegí un rango rápido (por ejemplo **Mes anterior** o **Trimestre actual**) o escribí **Desde** y **Hasta**. Al hacer clic en una fecha se abre el calendario. El rango elegido queda marcado en azul.
3. **Ejercicio** carga las fechas de ese ejercicio contable. Si elegís un rango rápido, el ejercicio se ajusta solo al que contiene la fecha Hasta.
4. En **Opciones** elegí la unidad de negocio y, según el reporte, subdiario, cuenta o casillas.
5. Pulsá **Aplicar** o **Generar**. Mientras no generes, los resultados que ves siguen siendo los del filtro anterior.

El chip del buscador resume el período aplicado, por ejemplo «Año actual» o «01/10/2026 a 31/10/2026».

**PDF** y **Excel** se habilitan cuando hay resultados y exportan exactamente lo generado. **Incluir proformas** aparece solo si tu usuario puede ver proformas.

En cada movimiento que tiene comprobante aparece **Ver**. Abre el detalle del comprobante encima del reporte. Al cerrarlo seguís en el mismo lugar, con los mismos filtros.

## Plan de cuentas

Es la lista ordenada de las cuentas de la contabilidad. Los **títulos** agrupan cuentas (por ejemplo, Activo → Disponibilidades → Caja); en las cuentas **imputables** se registran los movimientos.

- Arriba se ve cuántas cuentas, títulos, imputables y cajas o bancos tiene el plan.
- Al entrar, el plan se muestra resumido hasta los rubros. Tocá un grupo (o su flecha) para abrirlo o cerrarlo; al lado del grupo cerrado se ve cuántas cuentas tiene. **Expandir todo** y **Contraer todo** abren o cierran todos los grupos.
- La columna **Tipo** indica con una etiqueta si es Título, Imputable, Caja/Banco, Cliente o Proveedor, y si está dada de baja.
- **Tocá una cuenta imputable para abrir su Mayor** con el ejercicio actual.
- Escribí parte del código o de la descripción en el buscador: la lista se filtra mientras escribís, sin pulsar Enter. Por ejemplo, «caja» muestra las cuentas de caja y los títulos que las agrupan, con todo abierto. La **X** borra la búsqueda.
- En el embudo, **Opciones del plan** permite incluir las cuentas de clientes y proveedores y las dadas de baja. El chip indica qué incluye: «Cuentas activas», «Con clientes y proveedores», «Con dadas de baja» o «Con terceros y bajas».

**Excel** y **PDF** respetan la búsqueda y las opciones aplicadas.

## Libro diario

Lista los asientos del período en orden cronológico. Cada asiento empieza con una fila de encabezado: fecha, número de asiento y comprobante de origen, con su botón **Ver**. Debajo están sus renglones: cuenta, descripción, detalle, debe y haber.

- Las tarjetas superiores muestran la cantidad de asientos y los totales del debe y del haber. Si no son iguales, se indica la **Diferencia**.
- En **Opciones → Subdiario** elegí **Todos los asientos** o **Diario general (sin subdiarios)**.
- La fila **Totales** queda fija al pie de la tabla mientras te desplazás.

Si hay diferencia entre debe y haber, revisá **Sumas y saldos**: ahí se listan los asientos que no balancean.

## Subdiarios

Funciona igual que el Libro diario, pero muestra un solo subdiario por vez (cobranzas, pagos, ventas, compras u otros).

- Elegí el subdiario en **Opciones → Subdiario**. Al entrar se usa el primero disponible.
- **Resumido: totales por cuenta** reemplaza el detalle de asientos por un total por cuenta.

## Mayor de cuentas

Muestra los movimientos de una cuenta con su saldo acumulado.

1. Al entrar se listan las cuentas con saldo o movimientos del período, con su debe, haber y saldo. Las de clientes y proveedores aparecen agrupadas por rubro.
2. Escribí en el buscador de arriba parte del código o del nombre (por ejemplo «caja»): la lista se filtra mientras escribís. Si queda una sola cuenta, Enter la abre.
3. Tocá una cuenta para ver su mayor. Si tocás un grupo de clientes o proveedores, se ve el mayor de todas sus cuentas juntas.
4. **Cuentas**, arriba a la izquierda, vuelve a la lista. Escribir de nuevo en el buscador también vuelve a la lista.

El chip del buscador muestra la cuenta y el período, por ejemplo «111010001 · Año actual». En el embudo podés cambiar el período y, en **Opciones**, elegir un rango de cuentas con **Cuenta** y **Hasta la cuenta**.

Cada cuenta empieza con su **saldo anterior** (movimientos del ejercicio anteriores a la fecha Desde, incluida la apertura). Después siguen los movimientos con su comprobante y **Ver**, y al final el total de la cuenta. Un saldo positivo es deudor y uno negativo, acreedor.

## Sumas y saldos

Muestra, para cada cuenta, saldo anterior, debe y haber del período y saldo final.

- **Agrupar clientes y proveedores** junta todas las cuentas de terceros en una sola fila por rubro.
- **Mostrar títulos** agrega las filas de títulos con sus totales.
- **Cuentas sin movimientos** incluye cuentas que no tuvieron movimientos en el período.
- Tocá una cuenta para abrir su **Mayor** con el mismo período.

Si el período tiene asientos que no balancean, aparece un aviso amarillo con la diferencia total. Tocalo para ver qué asientos son.

## Balance general

Muestra los saldos acumulados del ejercicio, con la apertura incluida, hasta una fecha.

- En **Fecha de corte → Saldos al** elegí la fecha. El ejercicio se toma de esa fecha.
- **Detalle hasta el nivel** limita cuántos niveles del plan se muestran.
- **Comparativo con el ejercicio anterior** agrega una columna con el saldo a la misma fecha del ejercicio anterior.

Si los saldos no suman cero, el sistema avisa la diferencia: hay asientos desbalanceados. El detalle está en **Sumas y saldos**.

## Asientos

Permite consultar asientos y, con permiso, cargar o modificar asientos manuales.

**Buscar**

- Escribí en la barra superior un número de asiento, un comprobante, una cuenta o un usuario: la lista se actualiza mientras escribís.
- En el embudo, columna **Período**: al tocar un rango rápido la lista se actualiza enseguida. Si escribís las fechas a mano, pulsá **Aplicar**.
- Columna **Asientos**: **Mostrar** elige entre **Asientos manuales**, **De comprobantes** (compras, ventas, cobranzas, pagos) o **Todos**. También podés filtrar por **Unidad** y **Con la cuenta**. Estos cambios se aplican al momento.

**Abrir y modificar**

- Hacé clic en un asiento para abrirlo. Arriba aparecen **Grabar**, **Anular**, **Guardar como modelo**, **Controlar con ARCA** y **Volver** (o **Cancelar** si hiciste cambios).
- **Nuevo asiento** solo se habilita si tu usuario puede cargar asientos.
- Muchos asientos se abren en **solo lectura** y la pantalla dice por qué. Por ejemplo: es de un comprobante del libro de IVA, es un asiento resumen o de cierre de ejercicio, tiene movimientos conciliados con el banco, o lo generó otro módulo (cobranzas, pagos, sueldos). En esos casos se corrige desde el comprobante o módulo de origen.
- En los asientos generados por comprobantes, **Ver** abre el comprobante de origen.

**Modelos**

**Guardar como modelo** guarda las cuentas del asiento, con o sin importes, para reutilizarlas. Al cargar un asiento nuevo podés elegir el modelo.

## Retenciones y percepciones

Lista las retenciones y percepciones del período y arma los archivos para presentar.

1. Elegí **Desde** y **Hasta**, o usá **Mes anterior** o **Este mes**.
2. En **Ver** elegí **Sufridas** (las que te practicaron), **Practicadas** (las que practicaste como agente) o **Todas**.
3. Opcional: filtrá por **Unidad**.
4. Pulsá **Consultar**.

Arriba se resume el importe por impuesto. En la grilla, **Ver** abre el comprobante que originó cada movimiento (por ejemplo, la cobranza donde te retuvieron).

**Archivos para presentar** (sufridas): SIFERE de retenciones y percepciones de IIBB, y F.2002 de retenciones y percepciones de IVA.

**Archivos de agente** (practicadas): SICORE (Ganancias, sujetos retenidos y percepción de IVA), ARBA, ARCIBA y SIRCAR. Salen de las retenciones grabadas en órdenes de pago y de las percepciones del libro de IVA ventas.

Los avisos **Revisar** señalan datos que conviene completar antes de presentar, por ejemplo una cuenta de IIBB sin provincia o una retención sin número de certificado.

**Configurar** abre los códigos que usan los archivos: régimen de ARCA, jurisdicción por defecto y presentación mensual o quincenal.

## Libros de IVA

Entrá en **Ventas → Libro de IVA Ventas** o **Compras → Libro de IVA Compras**.

Al entrar se genera el libro del mes anterior. El chip del buscador muestra el mes y la unidad, por ejemplo «Octubre 2026 · 1».

1. Abrí el embudo de la barra superior.
2. En **Período** tocá uno de los últimos seis meses, o usá **Otro mes**: escribí el mes o usá las flechas para ir al anterior o al siguiente.
3. En **Libro** elegí la **Unidad de negocio (CUIT)**: cada CUIT presenta su propio libro.

Cada cambio de mes o de unidad genera el libro enseguida. **Generar**, arriba a la izquierda, lo vuelve a consultar con los mismos datos.

Las tarjetas muestran la cantidad de comprobantes, el neto gravado, el IVA y el total. En la grilla, **Ver** abre cada comprobante.

Cuando el libro está presentado, usá **Cerrar ventas hasta** (o compras) para que no se carguen más comprobantes con esas fechas. El aviso indica hasta qué fecha está cerrado.

**PDF** y **Excel** exportan el libro generado. **TXT ARCA** arma el archivo para presentar el libro en ARCA.

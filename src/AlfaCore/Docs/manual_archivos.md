# Archivos: maestros, tablas y precios

Archivos reúne los datos base que usan todos los módulos: clientes, proveedores, artículos, técnicos, contactos, tablas y listas de precios. Sigue la misma organización que Alfa Gestión: **Maestros**, **Tablas**, **Precios**, **Seguridad** e **Interfaces**.

## Cómo se trabaja en los maestros

Clientes, Proveedores, Artículos, Técnicos y Contactos tienen la misma pantalla.

Debajo del menú de arriba hay una barra:

- **A la izquierda**: el botón azul para crear (por ejemplo [[bi:plus-circle]] **Nuevo cliente**), [[bi:arrow-clockwise]] **Recargar** y **…** (más opciones, como [[bi:sliders]] **Configurar vista**).
- **En el medio**: el buscador. Escribí nombre, código, CUIT, email o teléfono y pulsá Enter. El embudo abre los **filtros** y la flecha de la derecha también. Cada filtro aplicado queda como una etiqueta al lado del buscador (por ejemplo **Activos**); tocá su [[bi:x]] para sacarlo.
- **A la derecha**: la cantidad de registros, las flechas [[bi:chevron-left]] [[bi:chevron-right]] para pasar de página y la forma de ver: [[bi:table]] **Listado** o [[bi:kanban]] **Kanban** (tarjetas).

Para trabajar:

- **Abrir una ficha**: en el listado, tocá el **…** de la columna **Acciones** (a la derecha de cada fila) y elegí [[bi:pencil-square]] **Editar**.
- En la ficha, la barra de arriba cambia: [[bi:floppy]] **Guardar** graba los cambios y [[bi:x-circle]] **Cancelar** los descarta. Si salís con cambios sin guardar, el sistema avisa: elegí **Seguir editando** o **Descartar**.
- **Configurar vista** (en **…**): elegí qué columnas ver y en qué orden (con [[bi:arrow-up]] y [[bi:arrow-down]]) y cómo **agrupar**. [[bi:arrow-counterclockwise]] **Restablecer anchos** vuelve las columnas a su ancho original. Tocá [[bi:floppy]] **Guardar vista** para que quede así la próxima vez.
- **Dar de baja** ([[bi:person-dash]] en la barra de la ficha) es una baja lógica: el registro queda inactivo, no se borra, y se puede seguir consultando sacando el filtro **Activos**.
- **Varios registros a la vez**: marcá las casillas de la izquierda (la del título marca todos). La barra de arriba muestra cuántos hay seleccionados y los botones [[bi:lightning-charge]] **Acción por lote** y [[bi:person-dash]] **Dar de baja**. **…** → [[bi:x-lg]] **Cancelar selección** desmarca todo.

## Clientes

Ficha completa del cliente: datos generales, domicilio, datos fiscales y condiciones comerciales.

Arriba de la ficha se ve un resumen del cliente y tres recuadros: **Contactos**, **Notas** y **Ver Cuenta corriente** (abre su cuenta corriente). Debajo están las solapas **Carga de datos**, **Condiciones**, **Contactos**, **Notas** y **Facturación automática**.

### Dar de alta un cliente

1. Tocá [[bi:plus-circle]] **Nuevo cliente**, el botón azul de arriba a la izquierda.
2. En la solapa **Carga de datos** completá **Razón social** y **Contacto**.
3. En **Domicilio**: **Calle**, **Altura**, **Código postal**, **Localidad**, **Provincia** y **País**.
4. En **Medios de contacto**: **Teléfono** y **Email** (el email se usa para enviar comprobantes y reclamos de saldo).
5. En **Datos fiscales**: tipo y número de documento (CUIT) y la condición de IVA. Con [[bi:cloud-search]] al lado del CUIT se consultan los datos en ARCA.
6. En la solapa **Condiciones**: **Condiciones comerciales** (lista de precios, clase de precio, condición de pago, vendedor, zona, límite de crédito), **Imputación contable**, **Entrega y pago** y **Estado e impuestos**.
7. Tocá [[bi:floppy]] **Guardar**, arriba a la izquierda.

### Portal Cliente

En la solapa **Carga de datos**, sección **Portal Cliente**: cargá la **Clave Portal** (con [[bi:eye]] la ves), tocá [[bi:send]] para mandarle la invitación al cliente y [[bi:box-arrow-up-right]] para entrar al portal como si fueras el cliente.

### Cambiar condiciones a varios clientes

Marcá los clientes y tocá [[bi:lightning-charge]] **Acción por lote** en la barra de arriba. Podés cambiar en un solo paso la lista de precios, la condición de pago, la clase, la clasificación, el vendedor, la zona, el límite de crédito o el bloqueo. Tocá [[bi:lightning-charge]] **Aplicar a N cliente(s)**.

### Conceptos que se facturan automáticamente

En la solapa **Facturación automática** de la ficha se carga lo que se le factura todos los períodos (por ejemplo, un abono mensual). Tocá [[bi:plus-circle]] **Agregar concepto** y completá **Concepto**, **Importe**, **Periodicidad**, **Tipo de comprobante** y **Unidad de negocio**. [[bi:trash3]] quita un concepto.

Para varios clientes a la vez, marcalos en el listado: arriba aparecen [[bi:plus-circle]] **Agregar concepto**, [[bi:dash-circle]] **Quitar concepto** y [[bi:percent]] **Aumentar / redondear** (sube los importes por porcentaje o monto; con [[bi:eye]] **Ver cómo queda** lo ves antes de [[bi:check2-circle]] **Aplicar**).

### Contactos del cliente

En la solapa **Contactos** de la ficha:

- [[bi:person-plus]] **Nuevo contacto** crea uno.
- [[bi:link-45deg]] **Vincular existente**: buscalo por nombre, email o celular y tocá [[bi:link-45deg]] al lado del que corresponde.
- En cada contacto: [[bi:box-arrow-up-right]] abre su ficha completa, [[bi:send]] **Invitar al portal** y [[bi:person-dash]] **Desvincular** (saca la relación sin borrar el contacto).

### Notas del cliente

En la solapa **Notas** (**Manual / notas internas**) se escriben observaciones internas del cliente, que el cliente no ve.

### Bloquear un cliente

Marcá los clientes y tocá [[bi:lightning-charge]] **Acción por lote** → **Bloqueo**. En los filtros podés ver solo los **Bloqueados** o los **Sin bloquear**.

## Proveedores

Funciona igual que Clientes: buscador, filtros, ficha con solapas, edición y baja lógica.

### Dar de alta un proveedor

Tocá [[bi:plus-circle]] **Nuevo proveedor** (arriba a la izquierda), completá razón social, CUIT, condición IVA, domicilio y email en **Carga de datos**, y las condiciones de compra en **Condiciones**. Tocá [[bi:floppy]] **Guardar**. También se puede crear sin salir de la carga de una compra (ver el manual de Compras).

### Descuentos por condición de compra

En la solapa **Condiciones** de la ficha del proveedor, sección **Descuentos por condición de compra**: [[bi:plus-circle]] **Agregar descuento** suma uno y [[bi:trash3]] lo quita. Si no tiene, aparece «Este proveedor no tiene descuentos por condición de compra».

## Artículos

Maestro de artículos y servicios: código, descripción, costos, precios, IVA e imagen. Arriba están las pestañas **Artículos**, **Listas de precios** y **Configuración**.

### Dar de alta un artículo

1. Tocá [[bi:plus-circle]] **Nuevo artículo**, el botón azul de arriba a la izquierda.
2. Completá **Código de artículo**, **Código de barras** (opcional) y **Descripción**.
3. Elegí **Rubro**, **Marca** y **Familia** (podés buscar o crear desde el mismo campo).
4. En **Costo y configuración** cargá el **Costo**, la **Alícuota de IVA** y la **Política de precios** (por ejemplo, calcular el precio con la **Utilidad %**). [[bi:eye]] **Ver cómo quedan los precios** muestra el resultado antes de guardar.
5. En **Precios de venta** revisá el precio de cada clase. Podés cargar una **oferta** con fechas **Oferta desde / hasta** y precios por cantidad.
6. Tocá [[bi:floppy]] **Guardar**.

### Imagen del artículo

En **Imagen del artículo**:

- [[bi:file-earmark-arrow-up]] **Seleccionar imagen** la sube desde tu equipo.
- [[bi:clipboard-image]] **Pegar** pega una imagen copiada.
- [[bi:images]] **Buscar imágenes** busca en internet; [[bi:google]] **Buscar en Google** abre la búsqueda en otra pestaña.
- [[bi:download]] **Descargar** la baja y [[bi:trash]] **Borrar** la quita.

Para varios artículos sin foto, marcalos y tocá [[bi:search]] **Buscar imágenes** en la barra de arriba.

### Aumentar o redondear precios de varios artículos

Marcá los artículos y tocá [[bi:lightning-charge]] **Acción por lote** en la barra de arriba:

- **Porcentaje**: escribí, por ejemplo, 10 para subir o -5 para bajar.
- **Aplicar a**: elegí sobre qué precios o costos aplicar.
- **Redondear precios a**: sin redondeo, $1, $10 o $100.

También podés cambiar el rubro, la marca, la familia o la utilidad de todos los marcados. Tocá [[bi:lightning-charge]] **Aplicar a N artículo(s)**.

## Listas de precios

Está en **Artículos → Listas de precios** (pestaña de arriba). Cada lista tiene sus artículos y precios propios, y se asigna a los clientes.

### Crear una lista

Tocá [[bi:plus-lg]] **Nueva lista** en la barra de arriba, completá **Código**, **Nombre** y la vigencia (**Vigencia desde / hasta**). Podés **Copiar artículos y precios de** otra lista para empezar. Tocá [[bi:check2]] **Grabar**.

### Trabajar una lista

Con la lista elegida, arriba de los artículos están los botones:

- [[bi:pencil]] **Datos de la lista**: cambia el nombre o la vigencia.
- [[bi:plus-square]] **Agregar artículos**: por **Rubro**, **Marca**, **Familia** (incluye sus subfamilias), **Código del proveedor** o una lista de **Códigos de artículo**. También se puede copiar de otra lista. Tocá [[bi:plus-lg]] **Agregar**.
- [[bi:percent]] **Aumento %**: elegí el **Porcentaje** (negativo para bajar) y sobre qué calcularlo, y [[bi:check2]] **Aplicar**.
- [[bi:123]] **Redondeo**: elegí al múltiplo de qué valor subir (por ejemplo 0,50, 1, 10 o 100), y [[bi:check2]] **Aplicar**.
- [[bi:x-square]] **Quitar**: saca los artículos marcados. Queda registrado en el histórico de precios.

En cada fila, [[bi:sliders]] abre **Todas las clases, oferta y precios por cantidad** de ese artículo.

### Borrar una lista

[[bi:trash]] **Borrar lista** (en los datos de la lista) solo funciona si ningún cliente la tiene asignada. Queda registrado en el histórico de precios.

## Configuración de artículos

En **Artículos → Configuración** (pestaña de arriba) se definen los parámetros generales de artículos y stock: códigos, códigos de barra de balanza, ruta de imágenes y si se usan **productos perecederos** (vencimientos). Tocá [[bi:floppy]] **Guardar** para aplicar o [[bi:x-lg]] **Cancelar** para volver a leer lo grabado.

## Técnicos

Técnicos son las personas que atienden servicios, tickets y partes de horas.

### Dar de alta un técnico

Tocá [[bi:plus-circle]] **Nuevo técnico** (arriba a la izquierda), completá **Código**, **Nombre**, **Cargo**, **Teléfono** y **Costo/hora**, y tocá **Guardar**. Si el técnico tiene que entrar al sistema, usá **Crear usuario del sistema**: cargá el **Nombre de usuario**, el **Email** y la **Contraseña** (con [[bi:eye]] la ves) y tocá [[bi:person-plus]] **Crear usuario**.

## Contactos

Personas de contacto: de clientes, proveedores o de las conversaciones de WhatsApp.

### Crear o editar un contacto

Tocá [[bi:plus-circle]] **Nuevo contacto** (arriba a la izquierda), completá **Nombre**, **Celular**, **Email**, **Cargo** y localidad, y tocá **Guardar**. Para cambiar uno, abrilo y tocá [[bi:pencil-square]] **Editar**. Desde la ficha, [[bi:whatsapp]] **Conversaciones** abre sus conversaciones.

### Unificar contactos repetidos

Abrí uno de los contactos repetidos y tocá [[bi:intersect]] **Unificar** en la barra de arriba. Buscá el duplicado, tocá [[bi:intersect]] al lado de él y después [[bi:intersect]] **Confirmar unificación**: quedan en uno solo con sus relaciones.

### Enviar una plantilla de WhatsApp a varios contactos

Marcá los contactos en el listado y tocá [[bi:lightning-charge]] **Enviar plantilla** en la barra de arriba. Elegí la plantilla aprobada, revisá y tocá [[bi:send]] **Confirmar envío**. Al terminar se muestra el resultado del envío masivo.

## Tablas de referencia

En **Archivos → Tablas → Tablas de referencia** se mantienen las tablas simples del sistema (código y descripción). Elegí la tabla, cargá **Código** y **Descripción** y tocá [[bi:floppy]] para grabar. En cada fila, [[bi:pencil]] **Editar** la modifica y [[bi:trash]] **Eliminar** la borra. [[bi:eraser]] **Limpiar** vacía el formulario para cargar otra.

## Chequeras

En **Archivos → Tablas → Chequeras** se cargan las chequeras de cheques propios: el número de cada cheque emitido tiene que estar dentro de una chequera.

Tocá [[bi:plus-lg]] **Nueva chequera** (arriba a la izquierda) y completá **Banco**, **Serie**, **Nro de control**, **Número desde**, **Número hasta**, la **Cuenta del banco** (cheques comunes) y la **Cuenta de cheques diferidos**. Tocá [[bi:check2]] **Grabar**. [[bi:trash]] **Borrar** elimina una chequera.

## Tabla de comprobantes

En **Archivos → Tablas → Tabla de comprobantes** se configuran los comprobantes (factura, nota de crédito, remito...) y sus talonarios. Cada comprobante puede tener varios talonarios, por ejemplo uno por punto de venta.

Para agregar un talonario a un comprobante que ya existe, abrilo y tocá [[bi:plus-lg]] **Nuevo talonario de…**: se proponen el número de talonario siguiente y los datos del comprobante. Para un comprobante nuevo tocá [[bi:plus-lg]] **Nuevo comprobante** (arriba a la izquierda) y escribí el código.

Completá **Descripción**, **Unidad de negocio**, **Punto de venta**, las **Letras habilitadas** (y cuál es la letra por defecto) y, para cada letra, **Desde**, **Hasta** y **Próximo** (el número que va a usar el próximo comprobante). Tocá [[bi:check2]] **Grabar**.

- Con el mismo comprobante y punto de venta, el rango de una letra no puede superponerse con el de otro talonario.
- **Stock** (entrada o salida) y **Cuenta** (debe o haber) dependen del sistema. Si el comprobante ya tiene movimientos, no se pueden cambiar. Algunos comprobantes del sistema (factura, nota de crédito, recibo...) tienen esos valores fijos.
- Un comprobante **externo** no valida la numeración y tiene un solo talonario.
- [[bi:trash]] **Borrar** no deja eliminar el único talonario de un comprobante con movimientos, ni uno de ventas con comprobantes ya emitidos en su punto de venta.

## Bancos

Bancos que usan los cheques y las chequeras. Tocá [[bi:plus-lg]] **Nuevo banco** (arriba a la izquierda), cargá **Código** y **Nombre** y tocá [[bi:check2]] **Grabar**. [[bi:trash]] **Borrar** lo elimina.

## Actualización de costos

En **Actualización de costos** se actualizan los costos de los artículos importando la lista de precios de un proveedor (Excel o CSV).

### Importar una lista de costos

1. Tocá [[bi:upload]] **Nueva importación** (arriba a la izquierda).
2. En **Perfil**, buscá y elegí el perfil del proveedor (dice qué columna es el código, cuál el costo, etc.). [[bi:x-circle]] **Cambiar** elige otro.
3. En **Archivo**, elegí el archivo. Si tiene varias columnas con precios, el sistema pregunta **¿Cuál columna usar como costo?**.
4. Si hace falta, escribí en **Notas / Prompt** una indicación para esta corrida (por ejemplo, «usar PRECIO NETO con 10% de descuento»).
5. Tocá [[bi:upload]] **Iniciar importación**. Aparece la **Vista previa**; tocá [[bi:box-arrow-right]] **Abrir lote** para revisarlo.
6. En el lote, revisá cada fila. Las que no encontraron su artículo tienen [[bi:search]] **Buscar**: elegí el artículo con [[bi:check2-circle]] **Elegir artículo** o [[bi:x-circle]] **Descartar fila**.
7. Marcá las filas que querés aplicar y tocá [[bi:check2-square]] **Aplicar chequeadas**.

Si te equivocaste, en el lote está [[bi:arrow-counterclockwise]] **Deshacer aplicación**. [[bi:file-earmark-arrow-down]] **Archivo original** baja el archivo que se importó.

### Perfiles de importación

Cada proveedor manda su lista con un formato distinto. En **Perfiles de importación** se guarda cómo leer el archivo de cada uno, para no configurarlo cada vez. Tocá [[bi:plus-circle]] **Nuevo perfil** (arriba a la izquierda), completalo y guardalo con [[bi:save]]. [[bi:copy]] **Duplicar como nuevo** sirve para un proveedor con un formato parecido; [[bi:trash]] **Eliminar** lo borra.

### Historial de actualizaciones

**Historial** muestra las corridas anteriores: cuándo, quién, con qué archivo y cuántos artículos cambiaron.

# Archivos: maestros, tablas y precios

Archivos reúne los datos base que usan todos los módulos: clientes, proveedores, artículos, técnicos, contactos, tablas y listas de precios. Sigue la misma organización que Alfa Gestión: **Maestros**, **Tablas**, **Precios**, **Seguridad** e **Interfaces**.

## Cómo se trabaja en los maestros

Clientes, Proveedores, Artículos, Técnicos y Contactos funcionan igual:

- Arriba está el **buscador**: escribí nombre, código, CUIT, email o teléfono y pulsá Enter. El ícono del embudo abre los **filtros**; cada filtro aplicado queda como un chip que se quita con la **x**.
- **Listado** o **Kanban**: cambiá la forma de ver los registros desde los botones de vista.
- **Configurar vista**: elegí qué columnas ver, en qué orden y cómo **agrupar**. Pulsá **Guardar vista** para que quede así la próxima vez.
- Hacé clic en un registro para abrir su ficha. **Editar** habilita los cambios; **Guardar** los graba y **Cancelar** los descarta.
- Si salís con cambios sin guardar, el sistema te avisa: elegí **Seguir editando** o **Descartar**.
- **Dar de baja** es una baja lógica: el registro queda inactivo, no se borra, y se puede seguir consultando con el filtro de inactivos.
- Para varios registros a la vez, marcá las casillas de la izquierda y usá **Acción por lote**. **Cancelar selección** desmarca todo.

## Clientes

Ficha completa del cliente: datos generales, domicilio, datos fiscales y condiciones comerciales.

### Dar de alta un cliente

1. Pulsá **Nuevo cliente** en la barra superior.
2. Completá **Razón social**, **Tipo documento** y **CUIT / Nro. documento**, y la **Condición IVA**.
3. Cargá domicilio (calle, altura, localidad, provincia), teléfono y **Email**: el email se usa para enviar comprobantes y reclamos de saldo.
4. En condiciones comerciales elegí **Lista de precios**, **Clase de precio**, **Condición de pago**, **Vendedor**, **Zona** y, si corresponde, **Límite de crédito**.
5. Pulsá **Guardar**.

### Cambiar condiciones a varios clientes

Marcá los clientes y elegí **Acción por lote**. Podés cambiar en un solo paso la lista de precios, la condición de pago, la clase, la clasificación, el vendedor, la zona, el límite de crédito o el bloqueo.

### Conceptos que se facturan automáticamente

En la ficha, la sección de conceptos guarda lo que se le factura todos los períodos (por ejemplo, un abono mensual): **Concepto**, **Importe**, **Periodicidad**, **Tipo de comprobante** y **Unidad de negocio**. Con varios clientes marcados podés **Agregar concepto**, **Quitar concepto** o **Aumentar o redondear importes** por porcentaje o monto.

### Contactos del cliente

En la sección de contactos de la ficha podés crear un **Nuevo contacto** o **Vincular contacto existente** (buscalo por nombre, email o celular). **Desvincular** saca la relación sin borrar la ficha del contacto.

### Bloquear un cliente

Marcá los clientes y elegí **Acción por lote** → **Bloqueo**. En los filtros podés ver solo los **Bloqueados** o los **Sin bloquear**.

## Proveedores

Funciona igual que Clientes: buscador, filtros, ficha, edición y baja lógica.

### Dar de alta un proveedor

Pulsá **Nuevo proveedor**, completá razón social, CUIT, condición IVA, domicilio y email, y las condiciones de compra. Pulsá **Guardar**. También se puede crear sin salir de la carga de una compra (ver el manual de Compras).

### Descuentos por condición de compra

En la ficha del proveedor se ven los descuentos que tiene según la condición de compra. Si no tiene, aparece «Este proveedor no tiene descuentos por condición de compra».

## Artículos

Maestro de artículos y servicios: código, descripción, costos, precios, IVA e imagen.

### Dar de alta un artículo

1. Pulsá **Nuevo artículo**.
2. Completá **Código de artículo**, **Código de barras** (opcional) y **Descripción**.
3. Elegí **Rubro**, **Marca** y **Familia** (podés buscar o crear desde el mismo campo).
4. En **Costo y configuración** cargá el **Costo**, la **Alícuota de IVA** y la **Política de precios** (por ejemplo, calcular el precio con la **Utilidad %**).
5. En **Precios de venta** revisá el precio de cada clase. Podés cargar una **oferta** con fechas **Oferta desde / hasta** y precios por cantidad.
6. Pulsá **Guardar**.

### Imagen del artículo

En **Imagen del artículo** podés **Seleccionar imagen** desde tu equipo, **Pegar** una imagen copiada, o **Buscar imágenes** en internet. **Borrar imagen** la quita.

### Aumentar o redondear precios de varios artículos

Marcá los artículos y elegí **Acción por lote**:

- **Porcentaje**: escribí, por ejemplo, 10 para subir o -5 para bajar.
- **Aplicar a**: elegí sobre qué precios o costos aplicar.
- **Redondear precios a**: sin redondeo, $1, $10 o $100.

También podés cambiar el rubro, la marca, la familia o la utilidad de todos los marcados.

## Listas de precios

Está en **Archivos → Precios → Listas de precios**. Cada lista tiene sus artículos y precios propios, y se asigna a los clientes.

### Crear una lista

Pulsá **Nueva lista**, completá **Código**, **Nombre** y la vigencia (**Vigencia desde / hasta**). Podés **Copiar artículos y precios de** otra lista para empezar. Pulsá **Grabar**.

### Agregar o quitar artículos

**Agregar artículos** permite sumar por **Rubro**, **Marca**, **Familia** (incluye sus subfamilias), **Código del proveedor** o una lista de **Códigos de artículo**. También se puede copiar de otra lista. Para sacar artículos usá **Quitar**: queda registrado en el histórico de precios.

### Aumentar o redondear una lista

- **Aumento %**: elegí el **Porcentaje** (negativo para bajar) y sobre qué calcularlo.
- **Redondeo**: elegí al múltiplo de qué valor subir (por ejemplo 0,50, 1, 10 o 100).

### Borrar una lista

**Borrar lista** solo funciona si ningún cliente la tiene asignada. Queda registrado en el histórico de precios.

## Configuración de artículos

En **Artículos → Configuración** se definen los parámetros generales de artículos y stock: códigos, códigos de barra de balanza, ruta de imágenes y si se usan **productos perecederos** (vencimientos). Pulsá **Guardar** para aplicar o **Cancelar** para volver a leer lo grabado.

## Técnicos

Técnicos son las personas que atienden servicios, tickets y partes de horas.

### Dar de alta un técnico

Pulsá **Nuevo técnico**, completá **Código**, **Nombre**, **Cargo**, **Teléfono** y **Costo/hora**, y **Guardar**. Si el técnico tiene que entrar al sistema, usá **Crear usuario del sistema**: cargá el **Nombre de usuario**, el **Email** y la **Contraseña**.

## Contactos

Personas de contacto: de clientes, proveedores o de las conversaciones de WhatsApp.

### Crear o editar un contacto

Pulsá **Nuevo contacto**, completá **Nombre**, **Celular**, **Email**, **Cargo** y localidad, y **Guardar**. Desde la ficha podés ver sus **Conversaciones**.

### Unificar contactos repetidos

Marcá los contactos duplicados y pulsá **Unificar**: quedan en uno solo con sus relaciones.

### Enviar una plantilla de WhatsApp a varios contactos

Marcá los contactos y pulsá **Enviar plantilla**. Elegí la plantilla aprobada y confirmá; al terminar se muestra el resultado del envío masivo.

## Tablas de referencia

En **Archivos → Tablas → Tablas de referencia** se mantienen las tablas simples del sistema (código y descripción). Elegí la tabla, cargá **Código** y **Descripción** y grabá. **Editar** modifica una fila y **Eliminar** la borra. **Limpiar** vacía el formulario para cargar otra.

## Chequeras

En **Archivos → Tablas → Chequeras** se cargan las chequeras de cheques propios: el número de cada cheque emitido tiene que estar dentro de una chequera.

Pulsá **Nueva chequera** y completá **Banco**, **Serie**, **Nro de control**, **Número desde**, **Número hasta**, la **Cuenta del banco** (cheques comunes) y la **Cuenta de cheques diferidos**. Pulsá **Grabar**. **Borrar** elimina una chequera.

## Tabla de comprobantes

En **Archivos → Tablas → Tabla de comprobantes** se configuran los comprobantes (factura, nota de crédito, remito...) y sus talonarios. Cada comprobante puede tener varios talonarios, por ejemplo uno por punto de venta.

Para agregar un talonario a un comprobante que ya existe, abrilo y pulsá **Nuevo talonario**: se proponen el número de talonario siguiente y los datos del comprobante. Para un comprobante nuevo pulsá **Nuevo comprobante** y escribí el código.

Completá **Descripción**, **Unidad de negocio**, **Punto de venta**, las **Letras habilitadas** (y cuál es la letra por defecto) y, para cada letra, **Desde**, **Hasta** y **Próximo** (el número que va a usar el próximo comprobante). Pulsá **Grabar**.

- Con el mismo comprobante y punto de venta, el rango de una letra no puede superponerse con el de otro talonario.
- **Stock** (entrada o salida) y **Cuenta** (debe o haber) dependen del sistema. Si el comprobante ya tiene movimientos, no se pueden cambiar. Algunos comprobantes del sistema (factura, nota de crédito, recibo...) tienen esos valores fijos.
- Un comprobante **externo** no valida la numeración y tiene un solo talonario.
- **Borrar** no deja eliminar el único talonario de un comprobante con movimientos, ni uno de ventas con comprobantes ya emitidos en su punto de venta.

## Bancos

Bancos que usan los cheques y las chequeras. Pulsá **Nuevo banco**, cargá **Código** y **Nombre** y **Grabar**.

## Actualización de costos

En **Actualización de costos** se actualizan los costos de los artículos importando la lista de precios de un proveedor (Excel o CSV).

### Importar una lista de costos

1. Pulsá **Nueva importación**.
2. Elegí el archivo y el **perfil de importación** (qué columna es el código, cuál el costo, etc.).
3. Revisá la **Vista previa**: muestra qué artículos cambian y cuánto.
4. Confirmá para aplicar los costos nuevos.

### Perfiles de importación

Cada proveedor manda su lista con un formato distinto. En **Perfiles de importación** se guarda, con **Nuevo perfil**, cómo leer el archivo de cada uno, para no configurarlo cada vez.

### Historial de actualizaciones

**Historial** muestra las corridas anteriores: cuándo, quién, con qué archivo y cuántos artículos cambiaron.

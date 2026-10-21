# Conciliación bancaria inteligente

Ingresá a **Caja y Bancos → Conciliación bancaria inteligente** con la empresa y base donde querés trabajar.

1. Elegí el archivo descargado del banco en **Archivo Excel o CSV**. Se admiten `.xlsx`, `.xls` y `.csv`, de hasta 20 MB.
2. El sistema detecta Provincia, Santander o Mercado Pago por el contenido. El selector **Formato del extracto** aparece solo si no se reconoce el formato y se oculta al procesarlo correctamente; el archivo permanece seleccionado.
3. El sistema propone la cuenta bancaria cuando hay una coincidencia clara con el banco detectado. Si hay varias cuentas posibles, seleccioná la que corresponde al extracto. Podés corregir la cuenta sugerida; se muestran las cuentas activas del plan de cuentas con medio de pago `DB`.
4. Revisá la cantidad de movimientos, ingresos, egresos y total neto.
5. Presioná **Importar extracto** en la barra superior.

Al terminar se abre **Revisión de movimientos** con la importación recién procesada. Se muestran fecha, descripción original, referencia, importe, clasificación, cliente probable, propuesta, confianza y estado.

Usá la fila superior para **buscar movimientos** y **filtrar por clasificación**. La búsqueda se aplica con **Buscar** o Enter; al elegir una clasificación se actualiza el resultado. **Limpiar** quita ambos filtros. Buscá por descripción, referencia, cliente, propuesta, fecha o importe.

Hacé clic en el encabezado de cualquier columna de datos para ordenar; otro clic invierte el sentido. Las flechas indican el orden activo. La búsqueda, clasificación y orden abarcan todos los movimientos del alcance seleccionado y se conservan al pasar de página, abrir otra importación o usar **Ver todas**. Al importar un archivo nuevo, la revisión comienza sin filtros de búsqueda ni clasificación.

Usá **Detalle** para ver los comprobantes sugeridos y los importes de aplicación en una ventana flotante sobre la grilla. Cerrala con la cruz, **Cerrar** o **Esc** para continuar revisando con los mismos filtros, orden y página. La confianza ayuda a priorizar la revisión; no garantiza que el cliente ni la aplicación sean correctos. La indicación “Ya registrada” requiere comprobar la cobranza existente en Alfa.

### Asignar un cliente y recordarlo

Si reconocés un ingreso que figura **No identificado**, pulsá **Asignar cliente** en la fila o abrí **Detalle**. En **Asignar cliente**, buscá por nombre, código o CUIT y elegí un resultado.

Dejá marcada **Recordar este nombre bancario para próximas importaciones** y revisá **Nombre del pagador en el extracto**. Por ejemplo, para «Maderas San Blas Srl · Transferencia recibida», conservá «Maderas San Blas Srl». Pulsá **Asignar cliente**: se actualizan la propuesta, los comprobantes y la cuenta corriente del detalle. Después podés **Cobrar** o revisar si ya existe un recibo.

La asociación se conserva en esta base y sirve para proponer el cliente en futuras importaciones, incluso cuando el pagador aparece solo en la descripción. No depende de que el importe sea igual ni cobra automáticamente. Si la asignación vale solo para ese movimiento, desmarcá **Recordar**. Las importaciones anteriores mantienen sus propuestas hasta que las revises; guardar un nombre no modifica cobranzas ya registradas.

Si ese mismo nombre ya está asociado a otro cliente con distinto CUIT, aparece un aviso y no se reemplaza la asociación. Podés desmarcar **Recordar** para hacer una asignación individual. No se permite cambiar el cliente de un movimiento cobrado o conciliado.

### Clientes distintos con el mismo CUIT

El CUIT se compara usando únicamente sus dígitos: con guiones, puntos o espacios identifica al mismo titular. Si hay varias cuentas del cliente para ese CUIT, el sistema compara sus comprobantes pendientes con el importe bancario, dentro de la tolerancia configurada. Propone el cliente solo si una de esas cuentas tiene una coincidencia de importe. Si ninguna o varias coinciden, muestra **Varios clientes posibles** y requiere asignación manual; el importe por sí solo no busca clientes de todo el padrón.

Para aplicar esta revisión a un movimiento ya importado, abrí **Detalle** y pulsá **Reevaluar propuesta con los comprobantes actuales**. No se modifica una cobranza registrada ni un movimiento conciliado. Si ya asignaste el cliente manualmente, esa elección se conserva. La nueva propuesta debe revisarse antes de cobrar.

Podés recordar un mismo nombre bancario para dos cuentas con el mismo CUIT; en futuras importaciones se consideran ambas. Una asociación recordada no fuerza siempre la primera cuenta del titular. Si los CUIT son distintos, se conserva el aviso de conflicto y podés asignar solo el movimiento desmarcando Recordar.

### Formato del archivo y cuenta contable

El formato bancario sirve para interpretar columnas, fechas e importes; la **Cuenta bancaria en Alfa** es la cuenta contable contra la que vas a conciliar. Cuando el sistema reconoce el archivo, solo necesitás revisar o elegir esa cuenta. El formato no crea otra cuenta bancaria.

Se admiten extractos Excel modernos (`.xlsx`), Excel anteriores (`.xls`) y CSV de los formatos bancarios compatibles. Los CSV pueden usar punto y coma, coma, tabulación o barra vertical; se respetan comillas, acentos y decimales. Si un CSV tiene solo columnas genéricas, puede ser necesario elegir **Formato del extracto** porque esas columnas no identifican por sí solas al banco. Seleccioná el formato que corresponde al archivo; después se oculta ese selector.

La variante de Provincia con **Detalle de Movimientos**, datos de cuenta y encabezados **Fecha / Descripción / Importe / Saldo** se detecta automáticamente, aunque la tabla comience en la fila 6 y no tenga Número Secuencia.

En CSV o XLS de Mercado Pago con “Hoy”, “Ayer” o fechas sin año, confirmá la fecha de referencia de la descarga. La conversión del archivo no se usa como prueba de esa fecha.

### Corregir la clasificación de un movimiento

Abrí **Detalle**, elegí la **Clasificación del movimiento** y pulsá **Guardar clasificación**. Podés agregar un motivo. Por ejemplo, una transferencia entre cuentas propias puede marcarse como **Transferencia Interna**; dejará de aparecer bajo el filtro **Cobranza** y la corrección quedará guardada al volver a abrir la revisión.

La corrección corresponde a ese movimiento; no configura reglas para futuras importaciones. No se permite reclasificar movimientos que ya generaron una cobranza. Un ingreso sin factura propuesta puede requerir revisión; no significa automáticamente que no sea un cobro.

### Verificar una cobranza ya registrada

**Detalle** abre primero la pestaña **Cuenta corriente**. Muestra los últimos **30 movimientos** del cliente; podés elegir **50 o 100**, cambiar **Hasta la fecha** y pulsar **Consultar**. El corte inicial es 15 días después del movimiento bancario, para incluir recibos cargados unos días más tarde. Los saldos incluyen toda la historia anterior al corte, separados por moneda.

Si hay recibos con el mismo cliente e importe dentro de los 15 días anteriores o posteriores, aparecen en **Posibles cobranzas ya registradas**, aunque queden fuera del límite o corte de la grilla. Se abre la información del primero: fecha y número, detalle, usuario, caja, unidad, cuentas de banco/retenciones y comprobantes aplicados. **Ver recibo** permite comparar los demás; **Ver** en la cuenta corriente consulta cualquier otro asiento. Incluye las cobranzas ingresadas desde Alfa Gestión.

Una coincidencia de importe no confirma que sea la misma transferencia: compará moneda, banco y aplicaciones. La consulta no registra cobranzas ni marca el movimiento como conciliado. Si no hay cliente identificado o movimientos en las unidades autorizadas, no se muestra una cuenta corriente ajena.

La pestaña **Comprobantes** conserva la comparación de facturas y su botón **Cobrar**. La cuenta corriente sirve para consultar y verificar.

### Comparar la factura propuesta

Dentro de **Detalle → Comprobantes**, **Comprobantes del cliente para comparar** muestra la propuesta y hasta 40 comprobantes cercanos a la fecha del movimiento. Revisá emisión, vencimiento, moneda, total y saldo actual. **Ver** muestra cliente/CUIT, unidad, comentarios, conceptos y pedidos/remitos de origen si están vinculados.

Podés consultar facturas anteriores o posteriores antes de decidir. Los importes de la propuesta reflejan la importación; la columna **Saldo actual** se consulta nuevamente al abrir el detalle. **Sin pendiente informado** indica que la vista actual no informa deuda para ese comprobante.

Al pulsar **Cobrar** desde ese detalle, se propone el comprobante que estás viendo, siempre que conserve saldo y esté habilitado en pesos. El saldo vuelve a validarse al confirmar.

### Registrar la cobranza rápida

En un ingreso clasificado como **Cobranza**, pulsá **Cobrar** desde la fila o el detalle. Se necesita un cliente identificado y una cuenta bancaria asociada a la importación.

1. Elegí el comprobante pendiente del cliente. La ventana muestra su saldo y unidad de negocio.
2. Revisá fecha y talonario **CB X**. Se propone el de la unidad de la factura o uno general si no hay uno específico. El número se asigna al confirmar.
   En **Detalle de la cobranza** se propone **Cob. [nombre del cliente]**. Podés cambiarlo por un comentario de hasta 100 caracteres; se guarda como detalle del asiento y comentario del recibo.
3. La cuenta y el importe bancario se completan con el movimiento importado. Si queda una diferencia, se propone la última cuenta de retención usada en una cobranza del mismo cliente y unidad de negocio, por el importe que falta cubrir. Se indica el recibo de referencia. Revisá la propuesta: podés corregir cuenta e importe o **Quitar** la retención si corresponde dejar un pago parcial. Si no hay antecedentes, usá **Agregar retención** y elegí la cuenta. Podés agregar también el número de certificado; no se copia el de la cobranza anterior.
4. Revisá el total y el saldo restante, y pulsá **Confirmar cobranza CB**. Banco más retenciones no puede superar el saldo pendiente. Si es menor, queda un pago parcial.

La cuenta de retención que efectivamente confirmes queda como referencia para la próxima cobranza de ese cliente y unidad, incluso al volver a ingresar al sistema. Cancelar no cambia esa referencia. Si una cobranza posterior no tiene retenciones, se conserva la última conocida. También se consideran los recibos de Alfa Gestión; se excluyen los anulados y las cuentas inactivas.

Ejemplo: transferencia de $97.000 y retención de $3.000 generan una CB por $100.000 aplicada al comprobante. El asiento debita banco y retenciones, y acredita al cliente.

Al confirmar, se muestra el número de CB y el movimiento queda **Cobrado**. No vuelvas a registrar la misma transferencia por otro circuito. Una importación con cobranzas generadas no puede eliminarse desde esta pantalla. Ante error, conservá los datos y reintentá; el sistema controla operaciones repetidas.

Este circuito admite facturas, proformas y notas de débito en pesos, con una aplicación por recibo. Si no hay cliente identificado, saldo autorizado o talonario válido, se informa el motivo. Anticipos, varias facturas por recibo y otros medios como cheques/tarjetas quedan para el módulo de Cobranzas.

En **Importación**, el listado de importaciones recientes permite abrir cualquier archivo ya procesado mediante **Revisar**. En revisión, **Ver todas** reúne los movimientos de todas las importaciones. La barra superior permite cambiar de página y actualizar.

El mismo archivo no vuelve a importarse por cambiarle el nombre. Los movimientos identificados como repetidos en otros extractos de la misma cuenta se omiten y se informa su cantidad.

**Eliminar** pide confirmación y quita los movimientos, clasificaciones y propuestas de la importación. No elimina cobranzas ni asientos de Alfa. Si el archivo ya dio origen a registraciones, la eliminación se bloquea.

Para Mercado Pago, un nombre como `mercadopago_2026-10-19.xlsx` permite resolver fechas relativas automáticamente. Si lo renombraste (por ejemplo, `mercadopago 102026.xlsx`), no necesitás cambiarlo otra vez: se pide **Fecha de referencia de la descarga**. Se propone la fecha de creación del Excel cuando está disponible; revisala, corregila si hace falta y pulsá **Procesar archivo**. Recién entonces se habilita la importación, una vez elegida la cuenta bancaria.

La fecha de referencia es la fecha a la que correspondían “Hoy”, “Ayer” o los días sin año al descargar el extracto. No es necesariamente el día de importación. Un nombre con mes y año no alcanza para deducir el día. Si cambiás la fecha, debés volver a procesar antes de importar. Los archivos con fechas completas no requieren este dato.

Los archivos pueden ser `.xlsx`, `.xls` o `.csv` y pesar hasta 20 MB. Si aparece un error, el archivo queda disponible para corregir banco/cuenta y reintentar. Al cambiar de empresa, base o usuario hay que volver a elegirlo.

La importación prepara propuestas para revisión. La acción **Confirmar cobranza CB** sí registra el recibo, el asiento y su aplicación. La comparación y confirmación de vínculos se gestionan en **Conciliar cuenta**. El cierre por saldos, los asientos por concepto y otros circuitos de cobro se incorporarán por separado.

### Conciliar la cuenta bancaria

1. Abrí la pestaña **Conciliar cuenta** y elegí la cuenta bancaria en pesos.
2. Indicá **Desde**, **Hasta**, el estado (**Pendientes**, **Conciliados** o **Todos**) y, si hace falta, un texto de búsqueda. Pulsá **Comparar**.
3. A la izquierda aparecen los movimientos de los extractos importados; a la derecha, los de esa cuenta bancaria registrados en Alfa. Las fechas se pueden ampliar para incluir acreditaciones o débitos posteriores.
4. Marcá uno o varios movimientos de cada lado. Podés cambiar de página sin perder la selección. Los totales deben coincidir exactamente y todos los movimientos deben ser ingresos o todos egresos.
5. **Conciliar selección** abre la revisión de todos los movimientos elegidos. **Confirmar conciliación** guarda el vínculo y marca los registros como conciliados.

**Proponer** prepara una selección por importe y fecha cercana, mostrando cuántos candidatos encontró. Revisá siempre las referencias y comprobantes antes de confirmar. La selección propuesta puede incluir un movimiento de Alfa que está en otra página.

Los totales superiores corresponden a movimientos del período, sin saldos iniciales; no son un cierre de conciliación. Si el usuario tiene unidades restringidas, solo verá los registros de Alfa autorizados para esas unidades. Esta etapa trabaja en pesos y no registra automáticamente asientos por diferencias.

**Registración** indica si se cargó una cobranza; **Conciliación** indica si se vinculó el movimiento con la cuenta bancaria contable. Son controles diferentes. Los movimientos conciliados anteriormente en Alfa Gestión se identifican y quedan fuera de los pendientes.

Para revisar o corregir un vínculo, elegí **Historial** en el movimiento del banco o **Ver vínculo** en Alfa. Podés **Desconciliar este vínculo** con un motivo: se libera todo el grupo y se conserva quién lo confirmó y quién lo deshizo. La cobranza o el asiento permanecen registrados. Si los registros cambiaron o tienen una conciliación legacy, se informa el impedimento para evitar liberar una marca incorrecta.

Una importación con historial de conciliación no puede eliminarse. Un movimiento conciliado tampoco permite generar otra cobranza rápida. Los asientos faltantes por comisiones, impuestos y otros conceptos se incorporarán en la siguiente etapa.

### Cobrar dos o más facturas con un mismo depósito

En **Cobrar**, la sección **Comprobantes a cobrar** permite marcar varias facturas del mismo cliente y unidad de negocio. Si la conciliación propone **Múltiple**, se precargan los comprobantes que siguen vigentes y con saldo; revisá la selección. Podés agregar o quitar facturas y corregir el importe de cada aplicación. Si la propuesta ya no coincide con los saldos disponibles, elegí manualmente los comprobantes.

El banco conserva el importe del depósito. Podés agregar retenciones; **Total CB** debe coincidir con **Aplicado**, dejando **Diferencia por aplicar** en cero. Al confirmar se emite **un solo recibo CB**, se aplica el importe indicado a cada factura y se registra un solo asiento. Las facturas de distintas unidades requieren recibos separados para respetar sus talonarios. La selección automática no graba nada hasta confirmar.

### Marcar un cobro ya ingresado en Alfa Gestión

El botón **Marcar como cobrado** está siempre al pie del detalle, junto a **Cerrar**, para los ingresos pendientes clasificados Cobranza. Al pulsarlo se abre Cuenta corriente y el recibo para revisar la confirmación. Si no hay un recibo válido, el aviso explica qué revisar; tener saldo cero no alcanza para confirmar que ese ingreso bancario fue cobrado.

Después de revisar el recibo y marcar **Confirmo que este recibo corresponde al movimiento bancario**, podés completar el reconocimiento desde el mismo botón del pie o desde el recibo. Si los importes difieren, primero completá la observación obligatoria. Hasta ese momento, pulsar el botón abre la revisión y conserva tus datos.

Al abrir **Detalle**, arriba se muestran el cliente, su **saldo al corte** y el importe del banco en bloques separados. La fecha junto al saldo indica hasta cuándo se calcularon los movimientos (inicialmente 15 días después de la fecha bancaria); podés cambiarla en **Cuenta corriente → Hasta la fecha → Consultar**. El saldo incluye los movimientos anteriores a los últimos 30/50/100 mostrados y se presenta separado por moneda.

Las solapas **Cuenta corriente** y **Comprobantes** sirven para revisar el cobro y las facturas. **Clasificación y cliente** reúne las opciones para reclasificar o asignar/cambiar el cliente. El detalle de un recibo comienza plegado; elegí **Ver recibo** o desplegá su encabezado para consultar medios de pago, aplicaciones y confirmación de cobro.

Si existe un recibo del mismo cliente con fecha cercana e importe dentro de la tolerancia configurada, el listado y el detalle muestran **Posible duplicado**, con número de recibo, fecha, importe y diferencia. El aviso se consulta al actualizar el listado, incluso cuando la propuesta original dice Anticipo. Por ejemplo, un ingreso de $1.119.185,00 puede advertir un recibo de $1.119.185,81 cargado una semana después, con tolerancia de $1,00. **Revisar posible duplicado** abre el detalle para compararlo en Cuenta corriente. El aviso no marca el cobro ni concilia automáticamente.

Todo ingreso pendiente clasificado como **Cobranza** ofrece **Marcar como cobrado**, también cuando la propuesta dice **Anticipo** o no quedan facturas pendientes. Abrí esa acción o **Detalle → Cuenta corriente**, revisá el recibo existente, sus cuentas y aplicaciones. Si hay varios candidatos, elegí **Ver recibo** en el que corresponda.

Marcá **Confirmo que este recibo corresponde al movimiento bancario** y pulsá **Marcar como cobrado**. Se verifica nuevamente cliente, importe del recibo que estás viendo, pesos, fecha cercana y autorización. Si el recibo tiene un importe distinto del banco, la pantalla muestra ambos importes y su diferencia; exige una observación de al menos 3 caracteres antes de confirmar. Esa diferencia queda pendiente de revisar y no se ajusta automáticamente. El movimiento queda **Cobrado**, sin generar otro recibo, asiento ni aplicación. La conciliación bancaria conserva su propio estado, incluso si el recibo se cargó en otra cuenta bancaria que debés revisar después.

El reconocimiento guarda recibo, usuario y fecha; impide reconocer el mismo recibo para otro movimiento. Las importaciones con esta confirmación conservan su historial y no se pueden eliminar.

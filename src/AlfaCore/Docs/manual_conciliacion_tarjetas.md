# Conciliación de tarjetas asistida con IA

Desde **Caja y Bancos → Conciliación de tarjetas asistida con IA**, compará las tarjetas presentadas en Alfa con las operaciones incluidas en los resúmenes de liquidación. Esta etapa no genera LT, asientos ni libro de IVA. La acreditación bancaria efectiva se verifica por separado con los extractos bancarios.

## Importar el documento testigo

1. Elegí un PDF mensual de Nación, Patagonia o Hipotecario, de hasta 20 MB.
2. Revisá banco, comercio, bruto, neto y avisos de lectura.
3. Confirmá la cuenta del plan cuyo medio de pago sea **TJ**, activa, imputable y en pesos. La sugerencia por nombre de tarjeta es orientativa.
4. Si corresponde, indicá la sucursal de Alfa que querés proponer como filtro. No es la sucursal impresa por el banco. No existe una relación fija entre 0001/Nación o 0002/Patagonia.
5. Podés recordar cuenta y sucursal para ese banco, comercio y tarjeta.
6. Presioná **Importar liquidación**. El PDF original se conserva completo en la base y se descarga con **Original**. El respaldo de la base incluye el testigo.

Un archivo ya importado para la misma cuenta se rechaza aunque cambie el nombre. Los documentos que contienen operaciones ya importadas se rechazan completos para no duplicar ni importar parcialmente un resumen superpuesto.

Podés leer el PDF y revisar los totales aunque todavía no exista su cuenta contable en esta base. Para guardarlo, elegí una cuenta TJ válida de la base correspondiente. No se crean cuentas automáticamente ni se trasladan asociaciones entre bases.

Los PDF de Hipotecario de agosto y septiembre de 2026 son resúmenes de liquidaciones. El archivo «CTA CTE» es un extracto bancario y no corresponde importarlo aquí; ese PDF de cuenta corriente se importa desde Conciliación bancaria.

## Corregir la cuenta elegida al importar

Si elegiste una cuenta equivocada, pulsá **Cambiar cuenta** junto a **Documento original** en Conciliar tarjetas, o desde la fila de la importación. Elegí la cuenta contable de tarjetas **TJ en pesos**, indicá el motivo y pulsá **Guardar cuenta**. Opcionalmente marcá **Recordar esta cuenta** para el mismo banco, comercio y tarjeta; se mantiene la sucursal recordada.

El PDF, los importes y las operaciones se conservan. Se vuelve a comparar contra la cuenta nueva y se limpian las selecciones y propuestas anteriores. La corrección queda anotada con usuario, fecha y motivo. Si el archivo o sus operaciones ya existen en destino, el cambio se rechaza completo.

Solo se permite mientras la importación no tenga historial de conciliación, incluso si los vínculos anteriores fueron anulados. No cambia cuentas del plan ni asientos contables.

## Comparar y confirmar

Abrí una importación con **Conciliar**. Las fechas iniciales corresponden a operaciones extraídas y pueden incluir ventas de meses anteriores. Cambiá período, sucursal, texto y estado desde el buscador superior y aplicá con **Comparar**.

La izquierda muestra operaciones de la liquidación; la derecha, tarjetas presentadas de Alfa de esa cuenta y unidades autorizadas. Las cobranzas del circuito Ventas, como CB y CBCT, también se muestran cuando el sistema desktop no completó el número de presentación. Se informa esa ausencia para revisar fecha, cupón e importe; no significa que la cobranza no exista. Los asientos internos sin identificación de presentación y cupón quedan excluidos para no duplicar importes. Sucursal y período filtran Alfa; el detalle del resumen permanece completo. Cada lista tiene 50 registros por página, scroll propio y encabezado fijo. Ordená por fecha o importe desde el título.

Nación puede detallar cupones o lotes, según el formato del documento. El resumen mensual con encabezado **PAGADOR: BANCO DE LA NACION ARGENTINA**, como el de abril de 2026, informa lotes y cantidades; también se admite. Patagonia e Hipotecario informan lotes y cantidades. Las ventas en cuotas conservan el importe presentado: no se multiplica por la cantidad de cuotas. El resumen final de planes no se vuelve a contar como ventas. Un lote no prueba la identidad de cada cupón. El número de cupón "1" de algunos históricos de Alfa tampoco sirve como identificador individual.

El **Resumen de cuenta BNA** es un extracto bancario, no una liquidación de tarjetas. Se importa desde **Conciliación bancaria**, que admite ese formato PDF; se rechaza en esta pantalla de tarjetas.

**Proponer selección** busca pares únicos por fecha e importe en todo el resultado consultado, incluidas otras páginas, hasta 2.000. No confirma automáticamente ni selecciona coincidencias ambiguas. También podés seleccionar varios registros de cada lado y comparar sus totales brutos.

Revisá el original y presioná **Conciliar selección**. Una diferencia dentro de la tolerancia requiere motivo y no modifica importes. Si el detalle extraído no coincide con el bruto del PDF, el documento queda disponible para revisar pero se bloquea la confirmación. No se ajustan ventas ni impuestos artificialmente para balancear.

Los movimientos sin liquidación encontrada no son automáticamente impagos: revisá fechas, sucursal y plazos. Las marcas de liquidación existentes del desktop se respetan. Esta pantalla no modifica esos registros.

## Herramientas de selección

Cada lista tiene **Seleccionar todo**, **Invertir** y **Limpiar**. Seleccionar todo incluye los pendientes de todas las páginas del resultado consultado. Los registros conciliados, liquidados en desktop o modificados quedan excluidos. La casilla del encabezado selecciona o desmarca únicamente los pendientes de la página visible; una marca intermedia indica selección parcial.

Usá **Solo seleccionados** para revisar lo marcado. Podés elegir 50 o 100 filas por página sin perder la selección. **Limpiar ambos lados** quita todas las marcas y vuelve a mostrar el resultado. Si se supera el máximo de 2.000 registros por lado, se conserva la selección anterior y podés continuar por página.

Los indicadores distinguen los totales consultados de la diferencia de selección. Para confirmar, ambos lados deben tener registros y la diferencia debe estar dentro de la tolerancia. La selección manual completa se guarda como **un único vínculo**; revisá los registros antes de confirmar. Para generar vínculos separados, podés revisar las propuestas de IA individualmente o seleccionarlas y confirmarlas juntas desde el cuadro de IA.

## Qué hace cada acción

- **Comparar**: vuelve a consultar las listas con los filtros elegidos. Actualiza la comparación y limpia la selección anterior; no propone ni guarda conciliaciones.
- **Proponer selección**: marca pares únicos con la misma fecha e importe entre los pendientes consultados, incluidas otras páginas. No usa IA. Revisá las marcas antes de confirmar.
- **Asistir con IA**: analiza los pendientes y propone coincidencias o agrupaciones, con el margen de fechas y tolerancia configurados. Presenta la evidencia para revisar cada propuesta.
- **Conciliar selección**: abre la confirmación para guardar el vínculo de lo seleccionado. Las otras tres acciones no lo guardan automáticamente.

Mientras se ejecuta la IA aparece **Analizando con IA…**, un indicador animado y un mensaje persistente. Primero prepara los pendientes; después informa las cantidades de ambos lados que se están analizando. Puede demorar unos minutos. El aviso permanece visible hasta terminar o informar un error, y los botones quedan bloqueados para evitar ejecutar dos operaciones a la vez.

## Asistir con IA

En **Conciliar tarjetas**, elegí **Asistir con IA** en la barra superior, revisá los criterios y pulsá **Aplicar y analizar**. Se consultan nuevamente los pendientes con las fechas, sucursal y búsqueda actuales, incluyendo otras páginas. Primero se buscan agrupaciones locales y solo se envían a la IA los casos restantes cuando está habilitada esa opción; el PDF original no se envía.

La IA puede proponer pares individuales y grupos de ventas que suman un lote, comparando importes brutos, fechas y referencias de cupón/presentación. La fecha de pago es informativa. No se compara el neto con ventas brutas ni se descuentan comisiones para forzar una coincidencia. Los casos ambiguos pueden quedar pendientes.

Cada propuesta muestra confianza orientativa, explicación, registros de ambos lados y diferencia. Podés marcar varias, usar **Seleccionar todas** o **Solo sin diferencias**, y pulsar **Revisar y confirmar N propuestas**. También se conserva **Revisar esta propuesta** para tratar una sola. El motivo precargado identifica la asistencia IA y podés ampliarlo; una diferencia aceptada conserva los importes originales. La confirmación vuelve a controlar permisos, huellas, pendientes y tolerancia. El cuadro solo se cierra con la X o **Cerrar**; al cerrarlo no se confirma nada.

El margen inicial es de 3 días, con hasta 2.000 pendientes por lado en cada petición. Si el período tiene más registros, el sistema organiza lotes por fechas y muestra **lote X de Y**. Cada lote incluye todos los candidatos dentro del margen, sin recortar una muestra. Si un solo día supera ese límite se indica la fecha y se pide filtrar por sucursal o conciliar parte de ese día. La consulta general conserva su límite de 10.000 movimientos. El administrador puede configurar los límites de cada petición en los parámetros de tarjetas. Si la IA no está configurada, falla o no propone coincidencias verificables, podés seguir con la selección manual y **Proponer selección**. Un error de IA conserva la selección previa. No se generan LT, asientos ni libro de IVA.

## Corregir una conciliación

Pulsá **Ver conciliados** junto a **Cambiar cuenta** y **Documento original**. También podés elegir **Conciliados** o **Todos** en el filtro general. En cualquier fila del grupo, pulsá **Deshacer #…**, escribí un motivo y confirmá **Deshacer conciliación completa**. Se libera todo ese vínculo (todos sus registros de ambos lados), no solo la fila donde hiciste clic. Después se muestran los pendientes para seleccionarlos en otra conciliación. Se consulta el vínculo completo sin exigir ampliar las fechas o sucursales de la pantalla; necesitás permisos sobre todas sus unidades. La reversión mantiene el historial; no borra el PDF ni modifica asientos. Los movimientos alterados después de confirmar se señalan para revisión.

Para corregir varias juntas, pulsá **Deshacer varias**. Se abre una ventana con todas las conciliaciones activas de la importación, incluso las que los filtros generales o rápidos no muestran. Allí sí podés tildarlas, usar **Seleccionar todas** o limpiar la selección. La selección abarca todas las páginas y admite hasta 1.000 conciliaciones por operación. Las casillas de las listas principales siguen reservadas para conciliar movimientos pendientes.

La ventana se abre mientras consulta y muestra el progreso. Si no puede terminar, el mensaje y el
código quedan visibles allí; pulsá **Volver a consultar** para reintentar. **Seleccionar todas** elige
solo las conciliaciones habilitadas. Las que requieran revisar permisos o identificar movimientos
aparecen deshabilitadas con su estado; no se liberan sin superar esos controles.

Revisá los números de vínculo, los registros e importes, escribí un motivo, marcá la confirmación y pulsá **Deshacer N conciliaciones**. Cada grupo se libera completo. Si alguna conciliación cambió, ya fue anulada o no se pueden identificar y autorizar todos sus movimientos, se rechaza la operación entera: no se deshace una parte del lote. Los grupos con problemas aparecen deshabilitados para revisión. La acción solo anula los vínculos propios de tarjetas, no cambia `MV_ASIENTOS` ni las marcas de liquidación del escritorio.

## Requisitos

El administrador debe ejecutar las actualizaciones. El servidor de AlfaCore necesita Python con `pypdf`. Python se configura una sola vez en **Administrar → Servidor**, con acceso de superadministrador, y se comparte entre todas las bases de esa instalación. Sin configuración común se conserva la ruta anterior de Recepción documental. Solo si necesitás otra instalación, indicá su ruta completa en `TARJETAS-PYTHON`. Vacío o `python` mantienen la configuración compartida del servidor; si tampoco existe esa configuración, se utiliza el Python disponible para el servicio. En esta etapa los PDF se procesan localmente, sin enviarlos a servicios de IA.


## Filtros rápidos por lista

Sobre las filas de **Liquidación** podés elegir una fecha de operación cargada. En **Alfa** podés elegir una fecha y una sucursal, o todas. Los controles son independientes: cambian la vista al instante, sin consultar otra vez la base ni ampliar el período o las sucursales del filtro general. Las fechas y sucursales disponibles se obtienen del resultado cargado; si falta alguna, ampliá primero el filtro general y pulsá **Comparar**. En Liquidación se trabaja sobre el detalle que ya muestra esa importación.

**Seleccionar todo**, **Invertir** y **Proponer selección** respetan los filtros rápidos, incluyendo otras páginas. Las marcas anteriores se conservan al filtrar; si alguna queda oculta aparece un aviso y sigue incluida en los totales y en la confirmación. Elegí todas las fechas/sucursales para volver a verla, o usá **Limpiar ambos lados** para quitar todas las marcas. **Comparar** restablece las vistas locales y la selección. **Asistir con IA** usa los filtros generales, no los rápidos.


## Detección de cuenta al seleccionar el PDF

Al seleccionar el archivo se muestra **Leyendo archivo y detectando cuenta…**. El sistema obtiene el período de las operaciones (no las fechas de pago) y consulta las cuentas TJ activas, imputables y en pesos, con los movimientos autorizados del usuario. Amplía la consulta con el margen de días configurado para tarjetas.

La vista previa muestra el período, las cuentas analizadas y las candidatas con evidencia: coincidencias individuales por cupón e importe dentro del margen de fechas; pares únicos de fecha e importe; y días con lotes cuyo bruto y cantidad son compatibles. No reutiliza una venta para explicar dos operaciones. El cupón genérico 1 no identifica una venta, ni se ignoran cupones informativos contradictorios. Los totales diarios de lotes son orientativos y no seleccionan una cuenta por sí solos.

Si hay una única candidata con coincidencias individuales, se precarga la cuenta; la sucursal solo se sugiere si las coincidencias pertenecen a una misma sucursal. Si hay varias, elegí **Usar cuenta** en la candidata correcta. Las asociaciones recordadas se validan contra el catálogo; si no tienen coincidencias y aparecen en otra cuenta, se pide revisión. La sugerencia por nombre se distingue expresamente de una verificación por movimientos. Podés corregir cuenta y sucursal antes de importar.

**Importar y preparar** conserva el PDF, abre la comparación con el período más el margen y marca pares únicos por fecha e importe para revisar. No confirma conciliaciones. Podés desactivar **Preparar coincidencias para revisar al importar**. Los lotes y los cupones con fechas diferentes pueden revisarse con **Asistir con IA**. La detección inicial es local, sin consumo de IA; las propuestas de IA siguen siendo explícitas y requieren confirmación.

Si el período supera 10.000 movimientos entre las cuentas TJ, no se sugiere a partir de una muestra parcial: elegí la cuenta manualmente. La detección de pertenencia puede encontrar operaciones ya liquidadas; la preparación posterior solo marca las pendientes habilitadas.


## Anular un archivo cargado por error

Si todavía estás en la vista previa, **Descartar archivo seleccionado** quita esa selección sin afectar las importaciones existentes.

Para un archivo ya importado, en **Importación** buscá su fila y pulsá **Anular**. Revisá el nombre y la cuenta, escribí un motivo de al menos tres caracteres y confirmá **Anular importación**. Se conservan el PDF original, las operaciones y el historial como testigo. La importación deja de aparecer entre las activas y podés importar nuevamente el archivo correcto.

Se permite cuando no quedan conciliaciones activas, aunque haya vínculos ya deshechos. Si todavía tiene conciliaciones, usá primero **Deshacer varias**. No modifica asientos, cuentas contables ni asociaciones recordadas. La anulación queda auditada con usuario, fecha y motivo.

**Mostrar también importaciones anuladas** permite consultar su estado y descargar el original. No pueden conciliarse ni cambiar de cuenta. Para trabajar nuevamente, importá el archivo como una nueva importación.

Requiere aplicar la actualización **2026-10-30-004** desde **Actualizaciones** en la base activa. El motivo de anulación, los motivos de desconciliación y los filtros rápidos conservan los valores ingresados al redibujarse la pantalla.

### Resultado visible de las propuestas

**Proponer selección** muestra un aviso persistente en la página: informa cuántos pendientes hay en cada lado, si faltan registros, si no hay coincidencias únicas o cuántos pares marcó. El botón de la barra superior actualiza ese aviso inmediatamente.

**Asistir con IA** informa el progreso por lotes cuando el período supera el tamaño de una petición. Al terminar muestra cuántos registros distintos envió, cuántos lotes analizó y cuántas propuestas resultaron válidas. Los movimientos sin candidatos dentro de las ventanas de fechas se cuentan aparte y siguen pendientes. Se descartan propuestas de distintos lotes que compiten por un mismo movimiento. Las agrupaciones que cruzan límites entre lotes pueden necesitar revisión manual; una propuesta no prueba que todos los pendientes estén liquidados. Si una petición falla, no se presentan resultados parciales como un análisis completo ni se confirma ninguna conciliación.


### Si aparece un error de formato de IA

El análisis exige ahora una respuesta estructurada con los campos y tipos esperados. Si aun así no puede interpretarse, se muestra un error con código de soporte y se conserva la selección anterior; no se confirman propuestas ni se modifican movimientos. El administrador puede consultar ese código en el registro para distinguir un problema de formato de una respuesta sin coincidencias. El modelo configurado debe admitir respuestas con esquema JSON.


### Aceptar varias propuestas de IA juntas

1. En el cuadro de IA, marcá las propuestas o pulsá **Seleccionar todas**. **Solo sin diferencias** elige únicamente las que tienen importes exactamente iguales.
2. Pulsá **Revisar y confirmar N propuestas**. El resumen muestra los importes y la diferencia de cada grupo. Cada propuesta genera su propio vínculo, aunque se confirmen juntas.
3. Si hay diferencias, marcá **Acepto las diferencias indicadas sin modificar los importes originales** y escribí el motivo. La tolerancia se comprueba por propuesta: diferencias de signo contrario no se compensan para el control.
4. Pulsá **Confirmar N propuestas**. La evidencia de IA y el motivo quedan guardados en cada conciliación. Si algún movimiento cambió, ya fue conciliado o no cumple los controles, no se guarda ninguna de las elegidas.

**Volver a las propuestas** conserva las marcas para corregir la selección. La selección masiva solo confirma vínculos de conciliación: no genera LT, asientos ni IVA.


## Exportar la conciliación a Excel

En **Conciliar tarjetas**, pulsá **Exportar Excel**. Elegí **Comparación actual**, **Solo pendientes** o **Solo conciliados** y confirmá **Descargar Excel**. Las dos últimas opciones reemplazan solo el estado general. Se respetan el período de Alfa, la búsqueda, las sucursales, las fechas locales y **Solo seleccionados** si está activo. Se incluyen todas las páginas del alcance elegido; no hace falta seleccionar todo para exportar la consulta completa.

El archivo tiene cuatro hojas:

- **Resumen**: base, usuario, fecha UTC, documento, cuenta, filtros y totales. Distingue el bruto/neto del archivo de los importes exportados.
- **Liquidacion**: operaciones del PDF con fechas, lotes, cupones, terminal, cantidades, bruto, estado y número de conciliación.
- **Alfa**: movimientos contables autorizados, comprobantes, sucursal, presentación, cupón, importe y estado. Identifica liquidaciones del sistema anterior y movimientos modificados después de conciliar.
- **Conciliaciones**: una fila por vínculo, cantidades y totales exportados de ambos lados. Para revisar sus movimientos, filtrá ese número en las hojas Liquidacion y Alfa. Los totales confirmados, usuario, fecha UTC y motivo se incluyen solo cuando el grupo completo de esta importación está disponible y no tiene movimientos modificados. Los vínculos parciales se indican expresamente.

Las fechas e importes son valores de Excel; los códigos conservan sus ceros iniciales. Los encabezados quedan inmovilizados y los detalles tienen filtros. La exportación vuelve a consultar los datos con los permisos vigentes y no cambia asientos ni conciliaciones. Un alcance vacío produce un archivo con encabezados y totales en cero.

**Este informe compara presentaciones con liquidaciones de tarjetas: no confirma por sí solo la acreditación bancaria.**


## Cambiar límites y probar agrupaciones

Al pulsar **Asistir con IA** se abre **Criterios de búsqueda**. Modificá los valores y pulsá **Aplicar y analizar**. Podés repetir el análisis cambiando los límites; no se confirma nada automáticamente. Los valores elegidos quedan para esa pantalla, sin modificar los de otros usuarios ni los predeterminados de la base. **Valores de la base** recupera los predeterminados.

| Criterio | Inicial | Rango | Uso |
|---|---:|---:|---|
| Margen entre fechas | 3 días | 0–31 | Diferencia entre presentación del PDF y operación de Alfa. |
| Máximo de movimientos por día/grupo | 200 | 1–500 | Limita tanto los días que se exploran como el tamaño de cada grupo propuesto. |
| Conceptos PDF combinados | 4 | 1–6 | Combina filas del mismo lote, presentación y terminal, por ejemplo crédito y débito. |
| Intentos de agrupación (modo anterior) | 100.000 | 100–1.000.000 | Acota la búsqueda; se informa si no pudo agotarse. |
| Filas por petición a la IA | 2.000 | 50–2.000 | Cantidad por lado en cada lote enviado al modelo. |

Para el caso de los siete conceptos de Nación, en modo anterior usá **3 días, 200 movimientos, 4 conceptos y 100.000 intentos**; en modo automático podés conservar los valores predeterminados. Se pueden proponer así grupos de 28, 30, 32 o 43 movimientos. Las agrupaciones grandes se calculan localmente; el modelo mantiene su máximo de 20 índices por propuesta y atiende los casos que quedan.

La búsqueda prueba primero el total de un día y sucursal; después, si está activado **Probar subconjuntos**, explora combinaciones dentro de ese día con la misma cantidad y total. No mezcla sucursales ni usa cuotas para multiplicar importes. Descarta candidatos que compiten por los mismos registros. Una coincidencia de cantidad e importe es evidencia para revisar, no prueba la identidad individual del cupón.

El margen no amplía automáticamente un período general que cambiaste manualmente. **Usar período del PDF con margen** carga ese período y actualiza la comparación. Al abrir una importación también se incluye el margen predeterminado.

Podés desactivar **Consultar IA para los casos que queden** para ensayar únicamente agrupaciones locales, sin enviar información al modelo. Si todo queda propuesto por el motor local tampoco se hacen peticiones de IA. Las propuestas distinguen **Agrupación local** de **IA**.

Si alcanza el máximo de intentos, avisa y conserva solo las coincidencias diarias cuya exploración terminó. No presenta una búsqueda interrumpida como exhaustiva. Si falla el modelo y ya había agrupaciones locales, las conserva y muestra un aviso con código; no muestra propuestas parciales del modelo como completas.

La tolerancia de importes sigue siendo la configurada para la base; no se amplía al cambiar estos límites. Toda diferencia requiere aceptación y motivo al confirmar. Las propuestas grandes pueden confirmarse juntas, con un máximo de 500 registros por lado y grupo, respetando además los límites totales de la tanda. No se modifican los asientos existentes.


## Avance del análisis

Mientras se procesa, se muestra **Paso 1 de 4** (consultar movimientos), **2 de 4** (buscar agrupaciones), **3 de 4** (analizar los casos restantes con IA) y **4 de 4** (preparar resultados). La barra representa etapas completadas, no porcentaje de tiempo.

Durante las consultas de IA se indica el lote actual, el total, cuántos terminaron y cuántos quedan por finalizar. Una consulta se cuenta como completada después de recibir y validar la respuesta. También se muestra el **tiempo transcurrido**. No se estima un tiempo restante: cada respuesta puede tardar distinto. Si todo se resuelve localmente, se informa que no hacen falta consultas al modelo.


## Recuperar un análisis al volver a entrar

Al terminar **Aplicar y analizar**, el resultado se guarda automáticamente en la base, asociado a esa liquidación y a tu usuario. Conserva las propuestas, evidencias, modelo, filtros y límites de búsqueda, incluso si no encontró coincidencias. No guarda casillas seleccionadas ni confirma conciliaciones.

Al abrir nuevamente la liquidación aparece **Análisis guardado**, con fecha y hora, y **Ver análisis guardado**. Este botón recupera el último análisis de tu usuario, restaura su período y filtros y verifica los movimientos actuales, sin volver a consultar la IA. Se informa cuántas propuestas siguen disponibles y cuántas se omitieron. Los conteos y el resumen de búsqueda corresponden al análisis original.

Si un movimiento cambió, ya fue conciliado o dejó de estar disponible con tus permisos, se omite toda la propuesta afectada. Si cambió la cuenta de la liquidación, hay que generar un análisis nuevo. Al confirmar se mantienen los controles transaccionales habituales. Podés confirmar las propuestas vigentes y volver a recuperar las restantes.

Un nuevo análisis terminado pasa a ser el último disponible. Si falla un nuevo intento, se conserva el anterior. Para utilizar el guardado hay que ejecutar las actualizaciones de base que incluye esta versión antes de analizar.

Esta etapa guarda resultados terminados. Cerrar la pantalla durante un análisis todavía puede interrumpirlo; continuar el trabajo sin la pantalla abierta corresponde a la futura integración con el worker de Recepción documental.


## Recibir liquidaciones y analizarlas sin mantener la pantalla abierta

En **Recepción documental → Nuevo** elegí **Liquidación de tarjetas** y adjuntá un PDF de hasta 20 MB. Para varios archivos usá **Alta por lote**: cada PDF de tarjetas genera una recepción independiente, aunque los nombres parezcan páginas de un mismo documento. En esta versión el tipo se elige explícitamente; no se clasifican automáticamente archivos de compras como liquidaciones.

Al guardar se conserva el original y se encola su lectura. Si la conexión se interrumpió al encolar, abrí el documento y pulsá **Preparar análisis de tarjetas**. El alta documental ya guardada se conserva si falla el encolado.

La ficha muestra **En cola → Leyendo PDF → Analizando → Listo para revisar**. Se actualiza cada cinco segundos durante el procesamiento y también tiene **Actualizar estado**. Podés cerrar el navegador: el worker del servidor continúa. La grilla y las tarjetas del listado muestran el estado del análisis de tus recepciones al actualizar la lista.

Si no hay una cuenta identificable queda **Esperando cuenta**. Elegí una cuenta TJ activa, imputable y en pesos; podés indicar una sucursal de Alfa o dejarla vacía para incluir todas. Pulsá **Aplicar cuenta y continuar**. La ficha muestra las cuentas candidatas cuando la lectura las identificó.

El worker usa los límites predeterminados de la base para buscar agrupaciones y consultar la IA para los casos restantes. Guarda las propuestas y su evidencia; no las confirma. Cuando termina, **Revisar en Conciliación de tarjetas** abre esa importación y ese análisis, restaurando su alcance y verificando los movimientos actuales. El análisis sigue asociado al usuario que encoló el documento; necesitás ingresar con ese usuario. En acceso restringido por `directo=1` se oculta el acceso al otro módulo.

Si falla, reintenta hasta tres veces, con cinco minutos de espera. Después queda **Requiere revisión**, con código de incidente y **Reintentar**. El original y la importación ya creada se conservan. Un reinicio normal del servidor devuelve el trabajo a la cola; ante una interrupción brusca, otro ciclo lo recupera al vencer su reserva. El PDF idéntico de la misma cuenta se reutiliza si ya estaba importado: no se duplican sus movimientos.

### Preparación del servidor

- Publicar la aplicación y ejecutar las actualizaciones hasta **2026-10-31-003** (incluye los límites y el análisis persistente).
- En **Administrar → Bases**, marcar **Incluir** en **Recepción documental · compras y tarjetas** para la base correspondiente. Se reutiliza la selección de bases del worker documental existente. El cliente debe tener **Comprobantes con IA** activo o en prueba vigente en la central.
- Ejecutar los workers en la instancia asignada por Administrar; `ProcesosSegundoPlano:Deshabilitados=true` impide ejecutarlos en esa instalación.
- Configurar Python con pypdf en **Administrar → Servidor** y la conexión de IA de esa instalación. Se reutiliza la configuración existente; no se cambia appsettings.json ni .env automáticamente.
- El usuario que carga y prepara debe estar autorizado en esa base y tener permiso de Conciliación de tarjetas. El worker consulta movimientos con ese usuario y sus unidades; no utiliza un administrador genérico.

`TARJETAS-DOCUMENTAL-HABILITADO` habilita este procesamiento en la base (inicial SI). `TARJETAS-DOCUMENTAL-MINUTOS` limita cada intento (inicial 20, de 1 a 120 minutos). Los límites de agrupación y de IA siguen siendo los de tarjetas. La confirmación final sigue siendo manual; no se generan LT, IVA ni asientos.

Una recepción preparada conserva el PDF con el que se encoló. Para corregir el archivo creá otra recepción: el worker verifica que el adjunto siga siendo el original y no reemplaza silenciosamente un análisis por el de otro documento.


## Búsqueda automática guiada por IA

En **Asistir con IA → Aplicar y analizar**, **Búsqueda automática** queda activada inicialmente. El sistema elige fechas y sucursales dentro del filtro general: no hace falta ir probándolas manualmente para cada lote. Combina primero las filas pendientes del mismo lote, fecha y terminal; compara días completos y ventanas de días consecutivos, y busca selecciones o exclusiones dentro de un día. No mezcla sucursales.

Ordena las búsquedas por costo para evitar que un lote difícil consuma todo el presupuesto antes de revisar los demás. En automático el presupuesto inicial es **1.000.000 de intentos por pasada**, y se permiten **hasta 3 días consecutivos por grupo**. Ambos se pueden ajustar en los criterios, con máximos de 1.000.000 y 7 respectivamente. El margen entre fecha PDF y Alfa, el máximo de movimientos y los demás límites siguen vigentes. La tolerancia de dinero no se aumenta.

Si quedan lotes explorables y está habilitada la consulta al modelo, la IA recibe objetivos y resúmenes de fechas/sucursales y elige alcances para una segunda búsqueda. Ese plan no confirma importes: el motor vuelve a comprobar cantidades, sumas, fechas, sucursal y alternativas sobre los registros reales. Puede proponer así grupos de más de 20 movimientos, hasta el máximo autorizado, sin pedir al modelo que enumere todos los comprobantes. Después se utiliza la asistencia de movimientos existente para lo que siga pendiente. No hay un ciclo ilimitado de llamadas: hay como máximo un plan y una segunda pasada local, además de los lotes de IA del período.

La búsqueda puede devolver una propuesta válida dentro de una fecha/sucursal sin haber agotado cualquier combinación del período. En ese caso lo dice expresamente en **Diagnóstico y alcances de la búsqueda**. Las propuestas conservan sus importes y requieren revisión y confirmación; una suma coincidente no prueba la identidad individual de los cupones.

Los avisos **Ambiguo** y **YaConciliado** aparecen directamente en la revisión. Se muestran comprobantes alternativos cuando están disponibles y vínculos que podrían explicar un faltante. Los casos ambiguos no se mandan otra vez al modelo para que elija uno por importe. No se desconcilia ni se mueve un registro de otro vínculo automáticamente.

El caso de Patagonia analizado se puede resolver con cinco grupos candidatos para ocho filas PDF y 119 movimientos de Alfa, incluyendo el fin de semana y un centavo de diferencia. Los otros tres registros quedan señalados para revisar la identidad y el vínculo anterior. Es un resultado del caso probado, no una garantía de resolver cualquier liquidación.

Para volver al comportamiento anterior desactivá **Búsqueda automática**. En ese modo vuelve a usarse el máximo de intentos de agrupación anterior. La actualización `2026-10-31-004` registra los predeterminados por base; los ajustes de esta ventana siguen siendo temporales para el análisis. La misma estrategia se usa al procesar desde Recepción documental.

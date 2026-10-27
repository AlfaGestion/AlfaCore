# Conciliación de tarjetas

Desde **Caja y Bancos → Conciliación de tarjetas**, compará las tarjetas presentadas en Alfa con las operaciones incluidas en los resúmenes de liquidación. Esta etapa no genera LT, asientos ni libro de IVA. La acreditación bancaria efectiva se verifica por separado con los extractos bancarios.

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

Los indicadores distinguen los totales consultados de la diferencia de selección. Para confirmar, ambos lados deben tener registros y la diferencia debe estar dentro de la tolerancia. La selección manual completa se guarda como **un único vínculo**; revisá los registros antes de confirmar. Para generar vínculos separados, revisá las propuestas de IA individualmente.

## Qué hace cada acción

- **Comparar**: vuelve a consultar las listas con los filtros elegidos. Actualiza la comparación y limpia la selección anterior; no propone ni guarda conciliaciones.
- **Proponer selección**: marca pares únicos con la misma fecha e importe entre los pendientes consultados, incluidas otras páginas. No usa IA. Revisá las marcas antes de confirmar.
- **Asistir con IA**: analiza los pendientes y propone coincidencias o agrupaciones, con el margen de fechas y tolerancia configurados. Presenta la evidencia para revisar cada propuesta.
- **Conciliar selección**: abre la confirmación para guardar el vínculo de lo seleccionado. Las otras tres acciones no lo guardan automáticamente.

Mientras se ejecuta la IA aparece **Analizando con IA…**, un indicador animado y un mensaje persistente. Primero prepara los pendientes; después informa las cantidades de ambos lados que se están analizando. Puede demorar unos minutos. El aviso permanece visible hasta terminar o informar un error, y los botones quedan bloqueados para evitar ejecutar dos operaciones a la vez.

## Asistir con IA

En **Conciliar tarjetas**, elegí **Asistir con IA** en la barra superior. Se consultan nuevamente los pendientes con las fechas, sucursal y búsqueda actuales, incluyendo otras páginas. Se envía a la IA el detalle necesario de las operaciones; el PDF original no se envía.

La IA puede proponer pares individuales y grupos de ventas que suman un lote, comparando importes brutos, fechas y referencias de cupón/presentación. La fecha de pago es informativa. No se compara el neto con ventas brutas ni se descuentan comisiones para forzar una coincidencia. Los casos ambiguos pueden quedar pendientes.

Cada propuesta muestra confianza orientativa, explicación, registros de ambos lados y diferencia. Pulsá **Revisar esta propuesta** y verificá la selección antes de **Confirmar**. El motivo precargado identifica la asistencia IA y podés ampliarlo; una diferencia aceptada conserva los importes originales. La confirmación vuelve a controlar permisos, huellas, pendientes y tolerancia. El cuadro solo se cierra con la X o **Cerrar**; al cerrarlo no se confirma nada.

El margen inicial es de 3 días, con hasta 2.000 pendientes por lado. El administrador puede configurarlos en los parámetros de tarjetas. Si hay más registros no se analiza una muestra parcial. Si la IA no está configurada, falla o no propone coincidencias verificables, podés seguir con la selección manual y **Proponer selección**. Un error de IA conserva la selección previa. No se generan LT, asientos ni libro de IVA.

## Corregir una conciliación

Pulsá **Ver conciliados** junto a **Cambiar cuenta** y **Documento original**. También podés elegir **Conciliados** o **Todos** en el filtro general. En cualquier fila del grupo, pulsá **Deshacer #…**, escribí un motivo y confirmá **Deshacer conciliación completa**. Se libera todo ese vínculo (todos sus registros de ambos lados), no solo la fila donde hiciste clic. Después se muestran los pendientes para seleccionarlos en otra conciliación. Consultá todas las fechas y sucursales del vínculo y conservá permisos sobre sus unidades. La reversión mantiene el historial; no borra el PDF ni modifica asientos. Los movimientos alterados después de confirmar se señalan para revisión.

## Requisitos

El administrador debe ejecutar las actualizaciones. El servidor de AlfaCore necesita Python con `pypdf`. Tarjetas reutiliza el ejecutable configurado en Recepción documental; no hace falta cargar la ruta dos veces. Solo si necesitás otra instalación, indicá su ruta completa en `TARJETAS-PYTHON`. Vacío o `python` mantienen la configuración compartida; si tampoco existe esa configuración, se utiliza el Python disponible para el servicio. En esta etapa los PDF se procesan localmente, sin enviarlos a servicios de IA.


## Filtros rápidos por lista

Sobre las filas de **Liquidación** podés elegir una fecha de operación cargada. En **Alfa** podés elegir una fecha y una sucursal, o todas. Los controles son independientes: cambian la vista al instante, sin consultar otra vez la base ni ampliar el período o las sucursales del filtro general. Las fechas y sucursales disponibles se obtienen del resultado cargado; si falta alguna, ampliá primero el filtro general y pulsá **Comparar**. En Liquidación se trabaja sobre el detalle que ya muestra esa importación.

**Seleccionar todo**, **Invertir** y **Proponer selección** respetan los filtros rápidos, incluyendo otras páginas. Las marcas anteriores se conservan al filtrar; si alguna queda oculta aparece un aviso y sigue incluida en los totales y en la confirmación. Elegí todas las fechas/sucursales para volver a verla, o usá **Limpiar ambos lados** para quitar todas las marcas. **Comparar** restablece las vistas locales y la selección. **Asistir con IA** usa los filtros generales, no los rápidos.


## Detección de cuenta al seleccionar el PDF

Al seleccionar el archivo se muestra **Leyendo archivo y detectando cuenta…**. El sistema obtiene el período de las operaciones (no las fechas de pago) y consulta las cuentas TJ activas, imputables y en pesos, con los movimientos autorizados del usuario. Amplía la consulta con el margen de días configurado para tarjetas.

La vista previa muestra el período, las cuentas analizadas y las candidatas con evidencia: coincidencias individuales por cupón e importe dentro del margen de fechas; pares únicos de fecha e importe; y días con lotes cuyo bruto y cantidad son compatibles. No reutiliza una venta para explicar dos operaciones. El cupón genérico 1 no identifica una venta, ni se ignoran cupones informativos contradictorios. Los totales diarios de lotes son orientativos y no seleccionan una cuenta por sí solos.

Si hay una única candidata con coincidencias individuales, se precarga la cuenta; la sucursal solo se sugiere si las coincidencias pertenecen a una misma sucursal. Si hay varias, elegí **Usar cuenta** en la candidata correcta. Las asociaciones recordadas se validan contra el catálogo; si no tienen coincidencias y aparecen en otra cuenta, se pide revisión. La sugerencia por nombre se distingue expresamente de una verificación por movimientos. Podés corregir cuenta y sucursal antes de importar.

**Importar y preparar** conserva el PDF, abre la comparación con el período más el margen y marca pares únicos por fecha e importe para revisar. No confirma conciliaciones. Podés desactivar **Preparar coincidencias para revisar al importar**. Los lotes y los cupones con fechas diferentes pueden revisarse con **Asistir con IA**. La detección inicial es local, sin consumo de IA; las propuestas de IA siguen siendo explícitas y requieren confirmación.

Si el período supera 10.000 movimientos entre las cuentas TJ, no se sugiere a partir de una muestra parcial: elegí la cuenta manualmente. La detección de pertenencia puede encontrar operaciones ya liquidadas; la preparación posterior solo marca las pendientes habilitadas.


## Eliminar un archivo cargado por error

Si todavía estás en la vista previa, **Descartar archivo seleccionado** quita esa selección sin afectar las importaciones existentes.

Para un archivo ya importado, en **Importación** buscá su fila y pulsá **Eliminar**. Revisá el nombre y la cuenta, escribí un motivo y confirmá **Eliminar importación**. Se eliminan el PDF guardado y sus operaciones importadas; después podés importar nuevamente el archivo correcto. No modifica asientos contables, el archivo de tu computadora ni las asociaciones de cuenta recordadas.

Solo se permite si la importación nunca tuvo conciliaciones. Tener todos los movimientos pendientes no garantiza esa condición: puede haber vínculos anulados en el historial. Si tiene historia, se conserva y podés revisar/deshacer sus vínculos desde **Ver conciliados**. La eliminación requiere confirmación explícita y queda auditada.

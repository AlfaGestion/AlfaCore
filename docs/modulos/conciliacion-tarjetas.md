# Conciliación de tarjetas: etapa inicial

## Consulta de desconciliación: rendimiento y errores visibles (30/10/2026)

**Deshacer varias** abre el diálogo antes de consultar, con progreso dentro de la ventana. Si falla,
conserva el mensaje y código aun si se descarta la notificación exterior, y ofrece **Volver a
consultar**. Las cancelaciones esperadas de sesión/navegación siguen siendo silenciosas; una
interrupción inesperada se registra mediante el servicio central y muestra su código. La vista de
conciliados aclara que las casillas principales son de pendientes y remite a la selección del diálogo.

La autorización recibe claves estructuradas XML generadas en el servidor desde los vínculos
almacenados. Busca por cuenta, secuencia, mes, asiento, período y tipo, más comprobante cuando la
clave es completa. Conserva la comprobación de la clave textual y de la huella en claves antiguas,
los permisos por unidad y el rechazo de identidades ambiguas. Para CUENTA admite los blancos
iniciales legacy del campo de 15 caracteres mediante igualdades indexables; no recorre el historial
comparando CONCAT por cada vínculo. Los miembros faltantes o con clave inválida permanecen contados
y bloquean el grupo. No modifica MV_ASIENTOS ni requiere migración.

Verificación: prueba de lectura con 100.000 movimientos temporales y presupuesto de lecturas
lógicas, identidades actuales/antiguas, cuentas con relleno, movimientos ausentes y permisos. La
prueba de anulación múltiple mantiene auditoría, idempotencia de confirmación y rollback del ensayo.

## Desconciliación individual y por lote (29/10/2026)

`ConciliacionTarjetasService.Desconciliar.cs` sustituye la dependencia anterior de `Comparar` al
desconciliar. Consulta el vínculo completo de la importación sin filtros de fecha, sucursal, búsqueda,
estado ni elegibilidad de nuevos candidatos. Los permisos sobre las unidades de **todos** los
renglones se verifican directamente contra `MV_ASIENTOS` y `TA_USUARIOS`, con el mismo criterio de
AdminCentral/unidad. La clave contable completa, la clave antigua y la huella se comparten con el SQL
de comparación; una clave antigua exige huella exacta y una identidad ambigua o ausente bloquea la
anulación. No se relajan permisos para eludir un filtro.

`VinculosParaDeshacerAsync` devuelve cabeceras, cantidades, importes y habilitación por integridad y
permisos. **Deshacer varias** abre una selección propia con páginas de 50, selección entre páginas y
confirmación explícita con motivo. No reutiliza las casillas de pendientes de las listas principales.
`DesconciliarVariasAsync` acepta hasta 1.000 IDs únicos de una misma importación/cuenta; en una
transacción Serializable bloquea las cabeceras activas, relee todos los miembros y valida todos los
grupos antes del primer UPDATE. La API individual reutiliza el mismo procedimiento. Solo cambia
Activo y auditoría de anulación en `TJ_Vinculo`, `TJ_VinculoDetalle` y `TJ_VinculoAlfa`. Conserva PDF,
contabilidad y marcas legacy. No requiere migración. Un fallo revierte el lote completo.

Ruta `/caja-bancos/conciliacion-tarjetas`, clave `D75-TARJETAS-IA`, ModuleKey `caja-bancos`, ya incluido en MainLayout. Reutiliza AlfaDesign/Smart Search/Tabs/Dialog/Checkbox/Notification. Diálogo sin cierre por backdrop; tablas con scroll y encabezados fijos. Es una operación de conciliación, no un ABM; no se incluye configuración de columnas. Paginadores independientes por lista, como el comparador bancario. Sin accesos a otros módulos en modo directo.

## Originales bancarios

La actualización `2026-10-27-001` agrega `BK_ImportacionExtracto.ArchivoOriginal varbinary(max) NULL`. El testigo se guarda en la transacción de importación; sin migración no se permite importar sin conservarlo. Descargar requiere sesión/base autorizada y permiso del módulo y verifica SHA-256 contra HashArchivo. Los históricos siguen visibles con Sin original; no se reconstruyen sus archivos. La eliminación existente, con sus guardas, elimina también el original.

Recepción documental usa carpeta/FTP e INT_COMPROBANTE_RECIBIDO_ADJUNTO. Se reutiliza su criterio de conservar original/nombre/huella y su descarga de blobs. Conciliación guarda los documentos financieros de hasta 20 MB en la base para garantizar atomicidad y respaldo conjunto sin depender de permisos/configuración de Interfaces. No se publican rutas de archivos ni endpoints de descarga sin autorización.

## PDF

`App_Data/tarjetas/leer_pdf.py` recibe bytes por stdin y entrega JSON UTF-8. Usa pypdf, sin escribir documentos ni llamar a IA. Se aprovechan los formatos relevados en IA_ProcesarDocumentos, evitando ejecutar lectores externos con posibles efectos de red. `TarjetasPdfLector` controla firma PDF, tamaño, hasta 150 páginas, plazo de dos minutos y cancelación del subproceso. El script se distribuye en build/publish. El ejecutable se resuelve por base: una ruta explícita en `TARJETAS-PYTHON` tiene prioridad; vacío o el valor inicial `python` reutilizan `InterfacesIaComprasPythonExe` de Recepción documental, y solo sin ambas configuraciones se recurre a `python` del PATH del servicio. Se comparte el intérprete (incluido su entorno virtual), no el script de procesamiento. `pypdf` debe estar instalado en ese mismo intérprete, accesible a la identidad del servicio. El lector captura stderr aunque Python cierre antes de recibir el PDF; el diagnóstico se registra en AUX_ERR y la pantalla informa problemas de instalación, permisos o publicación sin mostrar el traceback.

Nación: cupones, terminal, lote, fechas de operación/pago y liquidación. Se respeta signo negativo final en anulaciones. Patagonia e Hipotecario: lotes, cantidades y fechas de presentación/pago. Hipotecario se reconoce por PAGADOR dentro del documento; no por su nombre de archivo. El cierre «Total del día» limpia el contexto del lote para no volver a sumar el resumen posterior de planes. Las cuotas no multiplican el importe presentado. La suma del detalle debe coincidir con el bruto; de otro modo se bloquea confirmar. Nunca se recalculan ventas/IVA ni se completa OTROS para cerrar. Tarjeta inferida del nombre de archivo: orientativa, requiere confirmar cuenta. Solo se admiten cuentas TJ activas, imputables y en pesos.

La lectura no depende de cuentas existentes; la importación sí exige una cuenta TJ válida. Las asociaciones banco/comercio/tarjeta se guardan en la base activa. El PDF Hipotecario de cuenta corriente se rechaza como documento bancario, no como liquidación de tarjetas; el lector bancario todavía no admite ese PDF.

Se admite además Nación en formato mensual por lotes, detectado por `PAGADOR: 511 BANCO DE LA NACION ARGENTINA` (la sucursal y el código de pagador no se fijan en el lector). Reutiliza el formato de resumen por lotes de Patagonia/Hipotecario. Se conserva la lectura Nación anterior por cupones. Verificado el PDF de abril de 2026: 50 renglones, bruto 16.704.737,86 y neto 15.753.244,73; detalle igual al bruto. Las comisiones de columnas laterales no se suman a las ventas. El resumen de cuenta corriente BNA se identifica como documento bancario y se rechaza como liquidación; no implica soporte PDF en el lector bancario.

## Persistencia

La actualización `2026-10-27-002` crea auxiliares nuevos TJ_Importacion (PDF original, hash, cuenta/banco/comercio, fechas, totales y avisos), TJ_Detalle, TJ_Vinculo, TJ_VinculoDetalle y TJ_VinculoAlfa. Instalación transaccional e idempotente; índices filtrados impiden reutilizar detalles o líneas contables en vínculos activos. `003` agrega menú y permisos con el patrón de conciliación bancaria, sin cambiar las opciones legacy.

MV_ASIENTOS es la fuente oficial. Se leen débitos positivos de tarjetas de comprobantes de Ventas según V_TA_Cpte, aunque TJ_Presentacion sea cero/NULL. En bases legacy las CB/CBCT/CBFP pueden no numerar ese campo. Para líneas de otros circuitos se mantiene la exigencia de TJ_Presentacion>0 y cupón no vacío. Se excluyen resúmenes, aperturas, cierres y asientos internos CC/LT sin esos datos. Se aplican permisos por unidad de TA_USUARIOS/AdminCentral. Los cobros de Ventas sin número de cupón se incluyen con aviso, sin confundirlos con las líneas internas de presentación. Las marcas TJ_Liquidacion>0 son de solo lectura y no se reutilizan. Un cero histórico no prueba impago. No se escriben MV_ASIENTOS, V_MV_CPTE, MV_APLICACION ni marcas legacy. Clave completa por cuenta/período/mes/asiento/tipo/secuencia/TC/sucursal/número/letra, verificada contra la PK real de MV_ASIENTOS, y huella de contenido para detectar cambios. La cuenta forma parte del índice de vínculos. Los vínculos anteriores con clave abreviada se reconocen por huella; si ya no se puede identificar su renglón, se bloquean los candidatos como modificados para revisión.

Confirmación N:M serializable: relee registros autorizados y huellas, controla pendientes, diferencias y motivo. Se verifica sesión antes de commit. Reversión exige pertenencia a la importación y visibilidad de todas las líneas, conservando usuario/fecha/motivo. Logs mediante IAppEventService/AUX_ERR.

Hash de archivo/cuenta rechaza reimportaciones renombradas; huellas de operaciones por banco/comercio/cuenta/referencias y ordinal conservan multiplicidad y rechazan solapamientos sin importar parcialmente. Propuestas conservadoras únicas por fecha e importe, hasta 2.000; no equivalen a identificación del cupón ni se confirman solas. Listado de hasta 500 importaciones recientes; comparación limitada a 10.000 líneas y 2.000 seleccionadas por lado; grillas de 50. El PDF original y todos los detalles se conservan.

## Asistencia IA

La acción **Asistir con IA** relee pendientes de la importación y movimientos autorizados por unidad/cuenta/moneda/sucursal/período. `ConciliacionTarjetasService.Ia.cs` reutiliza `IConciliacionIaModelo` con contexto de tarjetas y prompt específico: brutos, cupones, cantidades y agrupaciones, sin netear gastos, sin PDF ni operaciones contables. Se usa `OPENAI_MODEL` y consumo CONCILIACION existentes. `2026-10-27-004` incorpora los parámetros TARJETAS-IA-DIAS (3, 0..31) y TARJETAS-IA-MAX-FILAS (2000, 50..2000). No se envían muestras parciales. Las fechas de pago no sustituyen operación/presentación.

`TarjetasIaPropuestas` adapta el contexto al transporte existente y reutiliza su validación de JSON/índices/límites/signos/tolerancia/fechas y descarte de todos los grupos superpuestos. Además rechaza cupones identificables contradictorios. No toma decisiones contables ni acepta orientaciones de cuentas/clasificación para tarjetas. La UI muestra evidencia de cada grupo y permite revisión individual o selección múltiple con confirmación conjunta; nunca combina las propuestas en un único vínculo. El motivo identifica la asistencia y puede editarse. Se conserva el snapshot de huellas de la comparación y se revalidan estado/tolerancia/permisos en la confirmación existente. Un fallo conserva la selección previa; cambiar comparación/base invalida el análisis. El diálogo no cierra por backdrop.

## Validación

El transporte compartido `ConciliacionIaModelo` envía filas compactas con un esquema de columnas único, índices originales y Unicode literal; conserva todos los registros y evita repetir nombres de campos por miles. Control de regresión de tamaño para 845/1.354 filas. No modifica los criterios ni el validador de respuestas. Los rechazos HTTP conservan modelo, cantidades y error/code/type/param del proveedor en la excepción interna registrada en AUX_ERR, con credenciales redactadas. La pantalla distingue contexto excedido, modelo inexistente, autenticación y cuota sin mostrar la respuesta técnica cruda. Referencia de contrato: [Chat Completions de OpenAI](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).

`python -m unittest discover -s tests/tarjetas -v`: anulaciones, diferencias sin compensación, lotes sin cupones inventados y formato desconocido. C#: tolerancias/motivos, propuestas no ambiguas, firma PDF y prueba SQL con TARJETAS_TEST_SQL/PDF sobre base autorizada: original idéntico, duplicados, sucursal, huellas obsoletas, doble confirmación, reversión auditada, permisos y sin asientos nuevos. Auxiliares de prueba se limpian por IDs; no se borran movimientos contables ni se versionan PDF reales/credenciales.

Hipotecario: pruebas sintéticas de lectura sin cuenta, planes en cuotas, exclusión del resumen de planes, detección por contenido y rechazo del extracto de cuenta corriente. Verificación local de los PDF de agosto y septiembre de 2026: detalle igual al bruto, junto con regresión de los siete PDF originales Nación/Patagonia.

Pendientes para otras etapas: propuestas por presentación/lote, más operadores, lectura asistida, generación LT/IVA y acreditación contra extractos bancarios. [Manual de usuario](../../src/AlfaCore/Docs/manual_conciliacion_tarjetas.md).

## Herramientas de selección e interfaz

`ConciliacionTarjetas.razor.Seleccion.cs` concentra selección masiva por lado, página, inversión y limpieza. El máximo es el mismo de ConfirmarAsync (2.000 por lado), sin truncado silencioso. Se omiten vínculos activos, marcas legacy y movimientos alterados. La casilla del encabezado representa selección parcial con AlfaCheckbox nullable. Vista local Solo seleccionados y tamaños 50/100 conservan las marcas y ajustan páginas al quitar registros. Los totales de resultado se separan de la diferencia seleccionada; confirmar exige selección bilateral y tolerancia. La selección completa genera un solo vínculo, indicado antes de grabar. Los avisos extensos se agrupan en un bloque desplegable; cabeceras y herramientas permanecen fuera del scroll de filas, con colores del sistema para filas seleccionadas.

## Corrección de la cuenta de importación

`ConciliacionTarjetasService.Cuenta.cs` permite corregir únicamente una importación sin ningún TJ_Vinculo (incluidos inactivos). La operación relee y bloquea la cabecera bajo Serializable, exige cuenta anterior vigente, motivo y destino TJ imputable/activo/pesos. Verifica la huella de cada detalle y rechaza PDF u operaciones existentes en destino. Recalcula las huellas con el mismo contrato histórico de ImportarAsync y los ordinales de ocurrencia en orden de Id. No altera original, hash del PDF, importes, sucursal ni datos contables. La cabecera y todas las huellas se actualizan en una transacción; el control de sesión se repite antes de commit.

El historial de corrección se añade a TJ_Importacion.Avisos con origen, destino, usuario, fecha UTC y motivo. Recordar es optativo y actualiza solo la asociación de banco/comercio/tarjeta, conservando su sucursal. La pantalla ofrece Cambiar cuenta desde ambas pestañas y, al guardar, vuelve a comparar e invalida selección/IA previa. El diálogo no cierra por backdrop. No requiere migración nueva.

Pruebas: TarjetasCambioCuentaTests cubre contrato de huellas, multiplicidad, destino inexistente, solicitud obsoleta, PDF repetido, operaciones superpuestas e historial aun anulado. El ensayo SQL es exclusivo de ELBOLSON2011_PRUEBAS, habilitado mediante TARJETAS_CUENTA_TEST_SQL, y revierte íntegramente su lote sintético y asociaciones.

## Cobranzas legacy sin presentación numerada (2026-10-27)

El SQL de lectura se centraliza en ConciliacionTarjetasService.Movimientos.cs y lo reutilizan comparación, confirmación y asistencia IA mediante el flujo existente. La ausencia de TJ_Presentacion ya no oculta cobranzas del circuito Ventas en la cuenta consultada. Se informa un aviso para revisar cupón, fecha e importe. Se conservan autorización por unidad, filtros de sucursal/período/cuenta/moneda, control de cupón para líneas internas y marcas legacy de liquidación. No se modifican datos de MV_ASIENTOS ni su numeración de presentación.

Verificación de lectura en ELBOLSON2011_PRUEBAS, cuenta 111010004, del 31/03/2026 al 29/04/2026: 250 movimientos positivos de Ventas (237 CBCT y 13 CB) que el requisito anterior descartaba por TJ_Presentacion=0. Las 72 líneas internas CC del mismo período permanecen excluidas. TarjetasPresentacionesTests valida el SQL real con esos registros y con tablas temporales para distinguir presentación explícita, unidades, sucursal, cuenta, moneda, aperturas/cierres/resúmenes y salidas de fondos. TARJETAS_LECTURA_TEST_SQL habilita el ensayo exclusivamente en la base autorizada; no escribe movimientos del cliente.

## Feedback durante operaciones (2026-10-27)

Operar publica un AlfaFeedbackPanel con AppUiMessage.InProgress y fuerza el render antes de esperar la operación. El panel persistente no es descartable y usa el indicador animado compartido y role=status/aria-live=polite. Durante Asistir con IA distingue preparación y análisis (cantidades consultadas); el botón muestra Analizando con IA y Busy=true. Comparar informa actualización de las listas. No muestra porcentajes ni tiempos ficticios. El finally retira el progreso y restaura acciones tanto en éxito como error/cancelación; el resultado o error conserva su feedback habitual. Cambiar sesión también limpia el progreso.

Los tooltips distinguen consulta, selección determinista por fecha/importe exactos y propuestas IA revisables. TarjetasProgresoUiTests mantiene una tarea pendiente controlada y verifica HTML de espera visible antes de completarla, acciones bloqueadas y limpieza posterior en éxito, error y cancelación, sin SQL ni llamadas al modelo.


### Vistas locales y reversión de vínculos

Las listas permiten fecha local de operación en Liquidación y fecha/sucursal local en Alfa, obtenidas exclusivamente de la comparación cargada. No cambian TarjetasFiltro ni ejecutan SQL. Los filtros se aplican antes de ordenar/paginar; selección masiva e inversión consideran el subconjunto local y preservan selecciones ocultas con aviso. Proponer selección usa ese subconjunto; la IA conserva el alcance general, indicado en pantalla. Una recarga general restablece filtros locales.

Ver conciliados cambia únicamente el estado del filtro general para hacer accesible Deshacer #. La confirmación identifica el vínculo completo, exige motivo y conserva el contrato transaccional de DesconciliarAsync: anulación lógica de TJ_Vinculo y sus miembros de ambos lados, auditoría de usuario/fecha/motivo y control de pertenencia, permisos y alcance completo. No elimina contabilidad ni toca marcas desktop. Después de confirmar vuelve a Pendientes. Los filtros locales nunca reducen el grupo que se revierte.


### Detección de cuenta basada en movimientos

LeerAsync conserva la extracción local y llama a DetectarCuenta antes de devolver la vista previa. Reutiliza el catálogo TJ activo, imputable, moneda 1 y una sola consulta multi-cuenta derivada de MovimientosAlfaSql, con exactamente las exclusiones y autorización por unidad de la comparación. La proyección incorpora Cuenta para evaluar cada candidata por separado. El límite global es 10.000 movimientos; si se excede no se evalúa un subconjunto ni se precarga una asociación como si hubiera sido verificada. La lectura no modifica datos.

TarjetasDeteccionCuentas evalúa cupones normalizados únicos (excluye 0/1), mismo importe y cercanía configurada; después pares únicos de fecha/importe, sin reutilizar filas ni aceptar cupones informativos contradictorios. Los lotes solo generan evidencia diaria cuando coinciden bruto y cantidad; nunca bastan para elegir cuenta. Múltiples candidatas requieren elección; una sola con evidencia individual puede precargarse. Una asociación válida conserva prioridad salvo evidencia en otras cuentas y ninguna en la recordada. El fallback por nombre se identifica como tal. La sucursal inferida requiere unanimidad entre las filas individuales coincidentes; una asociación explícita conserva la suya.

Se reutilizan TARJETAS-IA-DIAS y TARJETAS-TOLERANCIA, sin nuevas claves ni migraciones. Esta detección no llama al proveedor IA ni envía el PDF. Al importar se aplica el margen de fechas y, si la opción está activa, se ejecuta el selector determinista existente sobre los pendientes autorizados de la cuenta elegida. Las sugerencias no son autorización de escritura: ImportarAsync relee el PDF y valida la cuenta; ConfirmarAsync sigue comprobando pendientes, huellas, permisos y tolerancia en su propia transacción.


### Anulación de importaciones sin conciliaciones activas (2026-10-30)

La numeración corregida usa `2026-10-30-004` para anulación y `2026-10-30-003` para nombres del menú: ambos habían circulado como `2026-10-30-001`. Se conservan los alias de nombres en ActualizacionesService, que solo omite una copia vieja coexistente si su SQL coincide con el reemplazo. Las nuevas versiones permiten completar ambos cambios en bases que ya habían consumido el número anterior. Los scripts siguen siendo idempotentes.

La interfaz usa `AnularAsync` en lugar de la eliminación física. `2026-10-30-004__caja_tarjetas_anular_importacion.sql` agrega Anulada, UsuarioAnulacion, FechaAnulacion y MotivoAnulacion a TJ_Importacion, e ImportacionActiva a TJ_Detalle. Sustituye las restricciones globales de Hash/Cuenta y Huella por índices únicos filtrados para importaciones activas, permitiendo reimportar sin perder el original ni la historia anterior.

La anulación transaccional Serializable bloquea la cabecera y los rangos de vínculos, valida motivo y cuenta esperada y rechaza cualquier vínculo o miembro aún activo, incluso inconsistencias entre sus marcas. Los vínculos anulados no impiden anular. WriteAuditAsync comparte la transacción; si falla, no se anula. No escribe MV_ASIENTOS, maestros ni asociaciones recordadas.

Las listas excluyen anuladas por defecto; Mostrar también importaciones anuladas habilita consultar el estado y descargar el original. Comparar/confirmar y cambiar cuenta rechazan importaciones anuladas. Importar y cambiar cuenta consideran solo duplicados activos. Las bases anteriores siguen pudiendo listar/importar, pero Anular indica aplicar la nueva actualización. El método físico EliminarAsync queda disponible internamente con sus guardas anteriores; la pantalla ya no lo invoca.

Los parámetros string de AlfaInput y AlfaSelect usan expresiones Razor explícitas: sin `@`, el valor del campo se interpretaba como un literal y el render descartaba lo escrito. La prueba de UI reproduce escritura y redibujado del motivo. El ensayo SQL prueba migración idempotente, bloqueos, auditoría, conservación de PDF/historia y reimportación del mismo hash/huella, con rollback completo en una base de pruebas autorizada.

### Propuestas visibles y análisis IA por lotes

Proponer selección utiliza un resultado inline persistente y llama explícitamente a InvokeAsync(StateHasChanged), porque su callback pertenece a la barra global y el render automático de ese componente no actualiza la página consumidora. Se distinguen ausencia de comparación, ausencia de pendientes, ausencia de pares únicos y selección propuesta, con cantidades del subconjunto local. La preparación posterior a importar reutiliza ProponerCore y no depende de un render de la barra.

TarjetasIaLotes.Preparar reemplaza el rechazo global por tamaño con un plan previo de fechas completas. Cada lote respeta TARJETAS-IA-MAX-FILAS por lado e incluye todos los candidatos de Alfa desde la primera operación menos TARJETAS-IA-DIAS hasta la última más ese margen, siempre dentro de la consulta autorizada. Las ventanas pueden superponerse para conservar los candidatos cercanos a sus límites. Si un día completo no cabe, se aborta antes de llamar al modelo con fecha y recomendación de filtro; nunca se usa Take para recortar candidatos. El límite general de comparación sigue siendo 10.000.

El servicio llama secuencialmente al modelo, valida cada respuesta contra el subconjunto exacto que generó sus índices y comprueba sesión/cancelación antes y después de cada petición. El progreso asíncrono se comunica a la página como lote/total. La unión deduplica propuestas idénticas y elimina todos los grupos que compiten por un mismo id de detalle o clave contable entre lotes. Se cuentan los registros distintos enviados y los pendientes fuera de las ventanas. Una excepción no devuelve un resultado parcial como completo. El agrupamiento entre detalles de lotes de análisis diferentes puede requerir revisión manual; no se garantiza exhaustividad del matching ni se altera la confirmación transaccional.


### Contrato estructurado de respuesta IA

El transporte compartido de bancos y tarjetas solicita `response_format.type=json_schema` con `strict=true`, campos obligatorios e índices enteros. Sustituye el antiguo `json_object`, que solo aseguraba JSON sintácticamente válido, sin garantizar los campos y tipos que espera ConciliacionIaSalida. Contrato oficial: [Structured Outputs de OpenAI](https://developers.openai.com/api/docs/guides/structured-outputs). No cambia el modelo configurado, las credenciales ni los datos enviados.

Se manejan explícitamente respuestas incompletas y rechazos del modelo. El servidor sigue validando índices, cupón, grupos competidores, signos, fecha, moneda y tolerancia. Los campos raíz grupos/orientaciones son obligatorios también al deserializar; una respuesta con otra estructura no se convierte silenciosamente en un resultado vacío. Un formato incompatible registra en AUX_ERR longitud, ruta JSON, posición y SHA-256, sin el contenido de la respuesta. El incidente 2081 confirmó el punto de fallo en la deserialización, pero el código anterior no conservaba esos diagnósticos, por lo que no permite reconstruir el campo concreto del intento histórico.


### Confirmación conjunta de propuestas IA

ConciliacionTarjetas.razor.IaSeleccion.cs conserva índices de propuestas elegidas, selección total, selección de diferencias cero y resumen previo. Cualquier diferencia exige aceptación explícita y motivo humano; se muestra la suma absoluta además del detalle, para no ocultar diferencias de signo contrario. Un nuevo análisis, comparación o cambio de sesión invalida las marcas.

ConfirmarVariasAsync usa el permiso/sesión común y una única transacción Serializable. Relee la comparación autorizada una vez y prepara todos los grupos antes del primer INSERT. Reutiliza ResolverConfirmacion e InsertarVinculo con la confirmación individual, conservando huellas, permisos, pendientes, marcas legacy, integridad del PDF y tolerancia. Rechaza repeticiones entre grupos, más de 20 miembros por lado de una propuesta IA y solicitudes que excedan los 10.000 registros por lado de la comparación. Cada propuesta crea un TJ_Vinculo distinto, con usuario, fecha, evidencia y motivo; no hay compensación de diferencias entre propuestas. Si falla cualquier validación o escritura, no se confirma parcialmente. No cambia la contabilidad ni requiere migración.


## Exportación Excel de la comparación

`ConciliacionTarjetasService.Exportar.cs` agrega ExportarExcelAsync, dentro del control común de sesión, permiso de módulo y logging AUX_ERR. Reutiliza Comparar y su consulta oficial de MV_ASIENTOS con permisos por unidad. Aplica después filtros locales y selección, sin paginar; usa el filtro aplicado de la página, no cambios aún sin aplicar. Pendientes/Conciliados sustituyen únicamente Estado. La transacción RepeatableRead es de lectura y termina antes de generar el libro. Los metadatos de vínculos activos se consultan en lotes de 500 ids.

`TarjetasExcelExporter` genera Resumen, Liquidacion, Alfa y Conciliaciones con ClosedXML existente, sin archivos temporales ni IA. Los códigos e identificadores se graban como texto; los importes y fechas como valores nativos. Los textos externos no se escriben como fórmulas. Hay filtros, encabezados fijos y totales exportados, diferenciados del bruto/neto del original. El informe incluye la advertencia de que no verifica acreditación bancaria.

Un vínculo se considera completo para mostrar importes confirmados y auditoría solo si pertenece a esta importación, están incluidos todos sus miembros activos de ambos lados y no hay movimientos alterados. En grupos parciales se muestran exclusivamente cantidades y totales visibles, sin filtrar datos de miembros fuera del alcance; el detalle se consulta por id en las otras hojas. Se distingue LegacyLiquidado de conciliaciones AlfaCore. No se generan LT, asientos, IVA ni marcas de conciliación.

La página usa un AlfaDialog que no cierra por clic afuera y un botón de la barra del módulo; el progreso permanece visible hasta la descarga mediante downloadBlob existente. El servicio revalida la sesión al terminar y la descarga se cancela al cambiar de base. No requiere migración SQL. Pruebas en TarjetasExcelTests verifican alcance, más de 50 registros, tipos Excel, códigos, inyección de fórmulas, grupos completos/parciales/alterados, estados legacy, permisos, consulta vacía y cancelación.


## Búsqueda agrupada previa al modelo (2026-10-31)

TarjetasAgrupaciones enumera subconjuntos de conceptos positivos Nivel=Lote dentro de la misma fecha, lote y terminal. Prueba la cantidad total y el importe contra un día/sucursal de Alfa; excluye vínculos activos, liquidación legacy y huellas alteradas. Ante duplicados o registros que compiten se descartan las propuestas afectadas. Los totales diarios terminados tienen prioridad sobre búsquedas parciales posteriores.

Después explora subconjuntos positivos dentro de cada día y sucursal, con cantidad exacta y poda por sumas mínimas/máximas. La búsqueda está acotada por MaxIntentos, MaxLotesCombinados y MaxMovimientosGrupo; además hay topes de 10000 objetivos y 20000 candidatos para limitar memoria. Si no termina la fase diaria no devuelve sus candidatos incompletos; si se interrumpe la fase de subconjuntos descarta esa fase y conserva solo totales diarios completos. Se informa el límite, candidatos ambiguos y días excedidos. No garantiza una solución exhaustiva ni identidad individual.

AnalizarIaAsync aplica criterios validados (predeterminados de TA_CONFIGURACION u opciones temporales), ejecuta el motor en Task.Run cancelable y envía solo los restos a TarjetasIaLotes y al validador IA existente de 20 índices. Sin restos no invoca el modelo. Si falla IA pero existen propuestas locales, registra AUX_ERR y conserva únicamente esas propuestas con advertencia explícita. Los contadores distinguen registros revisados e información enviada al modelo. No se registran operaciones durante el análisis.

El AlfaDialog de criterios se abre desde Asistir con IA y permite ajustar límites y activar/desactivar subconjuntos o consulta al modelo. El período general sigue siendo el límite de datos: un botón explícito lo lleva al PDF más margen; al reabrir importaciones también se aplica el margen. Los controles usan enlaces Razor explícitos para no perder lo tipeado. La pantalla muestra origen Agrupación local/IA y conserva la revisión y confirmación explícitas. ConfirmarVarias admite hasta 500 miembros por lado/grupo (manual ya admitía 2000), conserva el máximo de 10000 filas por lado/tanda y revalida todo antes de insertar. La tolerancia y la aceptación/motivo de diferencias siguen vigentes.

TarjetasAgrupacionesTests cubre los siete conceptos/133 movimientos agrupados en cuatro propuestas, límites, subconjuntos únicos/ambiguos, sucursales, exclusiones y cancelación. TarjetasAgrupacionesSqlTests es optativa por variables de entorno, exclusiva de PRUEBAS02122025: consulta el caso real, verifica las cuatro propuestas y simula la validación sin INSERT; no invoca IA ni modifica movimientos/conciliaciones. La actualización 2026-10-31-001 solo agrega valores iniciales de parámetros.


### Progreso del análisis

`AnalizarIaAsync` admite un callback asíncrono opcional `avance` con `TarjetasProgresoAnalisis`: cuatro etapas reales, intentos locales y lotes iniciados/completados. El motor local publica avisos por fase y cada 400 ms como máximo entre controles de 512 intentos; un canal acotado conserva el aviso más reciente y se consume antes de pasar a la etapa siguiente. El callback anterior por lote se conserva.

La pantalla renderiza barra de etapas, contadores y reloj transcurrido de un segundo. El reloj se cancela y espera al terminar, fallar o cancelar la operación; no calcula ETA ni porcentaje temporal. El estado final se muestra solo después de regresar del servicio y sus validaciones de sesión. No se modifica información contable.


### Análisis persistente (2026-10-31)

`2026-10-31-002__caja_tarjetas_analisis_persistente.sql` agrega `TJ_Analisis`: cabecera con importación, usuario, fecha UTC y snapshot JSON versionado de criterios, filtro y resultado completo. Índice por importación/usuario/id descendente y FK con borrado en cascada para compatibilidad con la eliminación física histórica; la anulación lógica conserva el análisis. No escribe contabilidad ni tablas legacy. Conserva ejecuciones anteriores; la interfaz ofrece la última de cada usuario.

`AnalizarIaAsync` verifica la existencia del almacenamiento antes de las operaciones costosas, guarda al terminar (incluido resultado vacío o local con aviso IA) y valida sesión/cuenta/importación activa antes del commit. `guardar:false` se reserva para diagnósticos de solo lectura; la pantalla siempre usa el guardado predeterminado. Fallar o cancelar no sustituye el último análisis terminado.

`UltimoAnalisisAsync` y `RecuperarAnalisisAsync` usan la autorización común de módulo/base y restringen por usuario actual. Recuperar no llama al modelo: compara de nuevo con el alcance persistido y los permisos actuales, exige cuenta/detalle vigentes y devuelve objetos actuales. Descarta grupos completos por huellas distintas, vínculos activos, liquidación legacy, alteraciones, detalles modificados, falta de visibilidad o tolerancia actual. No restaura selecciones ni autorizaciones para diferencias; confirmar sigue releyendo y validando bajo transacción.

La recuperación restaura explícitamente los filtros guardados y comunica las omisiones. El resumen y los contadores históricos se identifican como tales. Guardar al finalizar no equivale a un worker: el procesamiento no continúa si la navegación cancela la operación. Integración documental y ejecución desatendida quedan pendientes.


### Integración con Recepción documental

La actualización `2026-10-31-003__interfaces_tarjetas_recepcion.sql` registra `LIQUIDACION_TARJETAS` en el catálogo documental y crea `TJ_Recepcion`. La cola es independiente de `IA_Compras_CAB`: un PDF por recepción, original/hash, autor/sistema reales, cuenta/sucursal, candidatos, estado/mensaje, intentos/reserva y referencias a importación/análisis. Las FK conservan la trazabilidad: una recepción con trabajo de tarjetas no se elimina físicamente como un archivo suelto. No hay cambios en menú/permisos legacy ni en contabilidad.

`InterfacesService.Tarjetas.cs` reutiliza el almacenamiento local/compartido/FTP y verifica el hash contra el adjunto documental. Encolar requiere sesión autorizada y permiso D75-TARJETAS-IA; el usuario no proviene del request. El hook posterior al alta conserva documento/archivos aun si encolar falla y permite reintentar desde la ficha. En alta por lote se evita la heurística de fusión por nombres para tarjetas; compras conserva su agrupación existente. Se corrigieron literales mojibake preexistentes al modificar InterfacesService.

`TarjetasRecepcionWorkerHostedService` usa la asignación central de workers y la selección explícita de bases de recepción ya existente (ICentralCompraIaService); nunca recorre bases ajenas a esa lista. También conserva la habilitación central COMPROBANTES_IA activa o prueba vigente; no amplía permisos/licencias al registrar el nuevo tipo. Respeta el apagado global de procesos y aplica espera de cinco minutos ante errores de conexión a una base. No depende de que haya un circuito web abierto. Su cola se habilita por base con TARJETAS-DOCUMENTAL-HABILITADO; TARJETAS-DOCUMENTAL-MINUTOS acota un intento. Se procesa un trabajo por base/ciclo, con reserva mayor al plazo máximo y hasta tres intentos. La detención normal reencola sin consumir intento; una caída brusca permite recuperación al vencer la reserva. El reclamo UPDATE/OUTPUT con UPDLOCK/READPAST/ROWLOCK y el token de reserva protegen frente a ejecuciones simultáneas.

El worker valida al autor activo/no-grupo y su sistema contra TA_USUARIOS, y crea una identidad interna acotada a ese trabajo/sesión, sin token de login ni privilegios SuperAdmin. PermissionService y las consultas oficiales mantienen los permisos por módulo/unidad. Se reutilizan LeerAsync, detección TJ, ImportarAsync y AnalizarIaAsync; no se llama a Confirmar ni se tocan MV_ASIENTOS o marcas desktop. El hash exacto+cuenta permite retomar una importación existente, incluida una importación manual idéntica. El resultado persistente devuelve su Id, que se enlaza al trabajo listo. Reintentar y elegir cuenta se auditan transaccionalmente.

La ficha muestra estado, progreso, candidatos, selección de cuenta/sucursal y reintento; refresca solo trabajos activos y cancela el polling al salir. El listado obtiene estados en una consulta por página, solo para el usuario vigente. El enlace a tarjetas incluye importación/análisis; la lectura vuelve a validar autor, base y huellas antes de ofrecer confirmación. No se muestra acceso a otro módulo con directo=1. El catálogo de tipos es explícito: clasificación automática de documentos no implementada en esta etapa.

Pruebas: TarjetasRecepcionTests (PDF/límites/identidad), RecepcionTarjetasPanelTests (acciones/estados/directo), TarjetasRecepcionSqlTests optativo (migración idempotente, reserva, recuperación y protección por token con DDL/datos bajo rollback, sin tocar contabilidad), más regresiones de tarjetas/recepción.


### Búsqueda automática, planes IA y diagnóstico de pendientes

`TarjetasBusquedaAutomatica` conserva objetivos máximos por fecha/lote/terminal (sin competir con subconceptos del mismo lote), genera alcances de días consecutivos de una sola sucursal y ordena las búsquedas por costo combinatorio. Los subconjuntos se buscan solo dentro de un día; las ventanas de varios días usan todos sus movimientos y cantidad exacta. Usa el complemento cuando excluir es más barato que seleccionar. Cada alcance se verifica hasta agotar sus combinaciones o probar ambigüedad; los más costosos que exceden el saldo del presupuesto se omiten con diagnóstico. Se acotan 25.000 tareas de búsqueda, se verifica cancelación y se informa progreso. Una coincidencia en un alcance no se presenta como unicidad sobre alcances no agotados. Los grupos que compiten por movimientos se excluyen.

`TarjetasPlanIa` envía objetivos y alcances resumidos usando `EsPlanTarjetas`, a través del proveedor/credencial/modelo/consumo existentes. Conserva el formato JSON estructurado de grupos y aplica su propio validador de índices y alcance; el plan no es una conciliación ni se valida como igualdad de sus totales resumidos. Los alcances del plan se recalculan con los datos autorizados y se verifica nuevamente la coincidencia matemática antes de crear una propuesta. El modo no cambia la configuración compartida, tolerancias, cuenta, permisos ni filtro general. Máximo un plan por ejecución y dos pasadas locales con presupuesto propio, además de los lotes de movimientos ya existentes. Se rechaza un contexto excesivo en vez de recortarlo. Referencia del formato utilizado: [Structured Outputs, OpenAI](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat).

Los lotes con alternativas indistinguibles quedan protegidos del fallback IA. `DetectarYaConciliados` consulta la comparación autorizada en estado Todos y detecta casos donde falta un movimiento cuyo importe coincide con uno ya ligado; informa los vínculos sin anularlos. No afirma que todos los faltantes tengan esa causa. La respuesta persistente agrega `Diagnosticos` con defaults compatibles con snapshots anteriores. Los avisos pendientes se reevalúan al analizar de nuevo; al recuperar un snapshot representan el diagnóstico histórico de esa ejecución.

La IA de movimientos conserva sus 20 índices por grupo. Se valida cantidad para lotes con cantidad positiva y, en modo automático, una sola sucursal, máximo de días/movimientos y agrupación de un mismo lote. Confirmar sigue releyendo las huellas y pendientes bajo su transacción habitual. No hay escrituras de contabilidad ni confirmaciones/desconciliaciones desde la búsqueda.

`2026-10-31-004__caja_tarjetas_busqueda_automatica.sql` incorpora tres claves CAJA sin sustituir valores existentes: TARJETAS-BUSQUEDA-AUTOMATICA=SI, TARJETAS-AUTO-MAX-DIAS=3 (1..7), TARJETAS-AUTO-MAX-INTENTOS=1000000 (100..1000000 por pasada). El modo anterior conserva TARJETAS-AGRUPA-MAX-INTENTOS y el motor original.

Pruebas: TarjetasAutomaticasTests cubre complementos, fines de semana, agrupación de conceptos, repetidos, presupuestos/cancelación y planes de índices falsos. TarjetasAutomaticasSqlTests es optativo, de lectura, sobre el caso Patagonia en PRUEBAS02122025: cinco grupos, ocho filas PDF, 119 movimientos, diagnóstico de ambigüedad y vínculo #13; no llama al proveedor ni guarda análisis/vínculos. El plan de IA se prueba con respuestas simuladas; no se publicaron cambios ni se configuraron credenciales en las pruebas.

El fallback de cupones en modo automático también rechaza una elección del modelo si quedan movimientos alternativos compatibles y no hay un cupón identificable que justifique la selección. El texto del modelo no reemplaza esa validación.


## Vista previa LT (02/11/2026)

`PrepararLtAsync` relee el original validando SHA-256, con la sesión y permiso de tarjetas vigentes. No utiliza movimientos seleccionados ni altera la conciliación. El lector agrega `LiquidacionesLt` sin cambiar el contrato del detalle conciliable. Nación se segmenta por cierre de pago/número de liquidación; los formatos mensuales compatibles exponen el resumen fiscal como `EsResumenMensual`, sin distribuirlo entre liquidaciones. La constancia AFIP no se suma nuevamente al resumen. Los conceptos desconocidos se conservan con `SinClasificar`; faltantes y diferencias son diagnósticos, nunca ajustes.

`TarjetasLtPreparacion` construye un borrador sin persistencia contable: banco neto y descuentos al Debe, bruto de tarjeta al Haber, invirtiendo el sentido en devoluciones. No recalcula IVA. Se muestran advertencias por resúmenes compartidos, conceptos desconocidos, cuentas faltantes y desbalance. No se afirma aptitud para registrar: faltan control de duplicidad legacy, unidad, numeración, datos fiscales y transacción LT/libro IVA.

`GuardarLtCuentasAsync` es la única escritura de esta etapa, sobre `TA_CONFIGURACION`, clave `TARJETAS-LT-CUENTAS`, grupo CAJA, JSON en ValorAux. Se identifica banco/comercio/tarjeta/cuenta TJ. Se valida contra MA_CUENTAS activo, imputable y pesos, excluyendo clientes y proveedores oficiales. Banco usa DB; otros conceptos medios vacíos o RI/RB/RG/RO. Las sugerencias por nombre solo se asignan si son únicas. No se escriben MV_ASIENTOS, libros, numeradores ni tablas de conciliación. No requiere migración de esquema; la clave se crea al guardar la configuración.

Validación: pruebas del armado/signos/cuentas, separación de liquidaciones Nación, no duplicidad arancel/base/constancia y lectura local de los PDF de muestra. Los PDF privados permanecen fuera del repositorio.

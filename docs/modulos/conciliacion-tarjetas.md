# Conciliación de tarjetas: etapa inicial

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

`TarjetasIaPropuestas` adapta el contexto al transporte existente y reutiliza su validación de JSON/índices/límites/signos/tolerancia/fechas y descarte de todos los grupos superpuestos. Además rechaza cupones identificables contradictorios. No toma decisiones contables ni acepta orientaciones de cuentas/clasificación para tarjetas. La UI muestra evidencia de cada grupo y permite revisar uno por vez en la confirmación normal; nunca combina todas las propuestas en un único vínculo. El motivo identifica la asistencia y puede editarse. Se conserva el snapshot de huellas de la comparación y se revalidan estado/tolerancia/permisos en la confirmación existente. Un fallo conserva la selección previa; cambiar comparación/base invalida el análisis. El diálogo no cierra por backdrop.

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


### Eliminación de importaciones sin historial

EliminarAsync utiliza el control común de sesión y permiso del módulo, y una transacción Serializable. Bloquea la cabecera y el rango de vínculos con UPDLOCK/HOLDLOCK; valida la cuenta esperada para rechazar solicitudes obsoletas. Rechaza cualquier TJ_Vinculo o miembro TJ_VinculoDetalle asociado, activo o anulado. Solo entonces elimina TJ_Detalle y TJ_Importacion, incluido su original; las huellas quedan libres para reimportar. No toca MV_ASIENTOS, maestros ni asociaciones recordadas.

La auditoría central WriteAuditAsync usa la misma conexión/transacción y conserva id, nombre, cuenta y motivo, sin copiar el PDF. Si falla, la eliminación no se confirma. La UI exige motivo y confirmación, conserva el diálogo ante errores y actualiza listas/estado al completar. No requiere migración propia; utiliza el esquema de auditoría central vigente.

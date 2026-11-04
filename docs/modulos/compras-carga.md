# Carga de compras

Ruta `/compras/carga`, con variante SaaS y query `documento=<IdComprobanteRecibido>`. Shell AlfaDesign, ModuleKey compras (ya incluido en AlfaDesignManagedModules); acciones en Context Toolbar y disposición responsive con original documental. Clave web D50-CARGA-COMPRAS-WEB bajo D50; script 2026-11-03-002 solo escribe ALFACORE_MENU_WEB/ALFACORE_TAREAS_WEB. No modifica permisos ni menú legacy.

## Contrato de seguridad y persistencia

CompraCargaService aplica sesión autorizada, base esperada, permiso propio y revalidación de sesión/usuario al devolver resultados. La carga documental exige además D016004 y obtiene el documento mediante IInterfacesService. Los errores relevantes se registran en AUX_ERR a través de IAppEventService. Contexto y preparación solo consultan. RegistrarAsync incorpora el alta comercial transaccional descrita abajo. No escribe V_MV_STOCK, MV_APLICACION, IA_Compras_* ni estados documentales. El script de menú se distribuye para aplicar por Actualizaciones, no se ejecuta desde la preparación.

El alta rápida sí crea un **maestro de proveedor**, sin registrar compras. ProveedorAltaRapidaService exige D010110 y D50-CARGA-COMPRAS-WEB, sesión/base/actor estables y delega en CrearProveedorEnSesionAsync del servicio común de cuentas comerciales. Esta entrada solo admite códigos automáticos: valida CUIT, tipo documental CUIT y duplicidad en VT_PROVEEDORES dentro de la transacción, con sp_getapplock para serializar altas rápidas. Revalida sesión antes de confirmar. Reutiliza el validador y la persistencia existentes; no incorpora un segundo esquema de proveedor. La búsqueda incluye inactivos/bloqueados, que no pueden seleccionarse en la compra. El motivo habitual se valida contra V_TA_MOTIVOCPRA y se conserva al reabrir/editar la ficha completa. La consulta ARCA usa IArcaPadronService existente.

La UI cancela y borra estado al cambiar de sesión; directo=1 limita el encabezado al módulo y oculta el enlace cruzado desde Recepción documental. Los adjuntos usan el endpoint autorizado existente con base y token de sesión; no se publican rutas de disco ni credenciales SQL.

## Fuentes confirmadas

VT_PROVEEDORES para proveedor; V_TA_UNIDADNEGOCIO y TA_USUARIOS para unidades autorizadas; MA_CUENTAS para catálogo contable; V_TA_MOTIVOCPRA, V_TA_INPUT_COMPRAS y TA_CUENTASIVA para imputaciones. Configuración TA_CONFIGURACION: PIVA/PIVADIF/PIVADIF1/PIVADIF2, MotivoComprasDefault, FECHAOPERDESDE/FECHAOPERHASTA y CIERRECOMPRAS. Consultas compatibles con nivel 100, verificado en ALFANET/SERVER-ALFAVB6. Sin funciones SQL modernas ni cambios de compatibilidad.

## Cálculo y alcance

El catálogo de tipos proviene de V_TA_CPTE con SISTEMA=COMPRAS, respetando unidad, talonario, letras y Externo. Ante reglas generales y específicas, prevalece la unidad específica. Se muestran también OC/RMC/NCC/NDC y demás tipos configurados, con alcance pendiente explícito; nunca se calculan como FCC por compartir signo o movimiento de stock. La vista exige coincidencia de tipo/talonario/unidad/letra. Los comprobantes externos muestran número de proveedor y los internos pendientes no reservan numeración. Cambiar de tipo conserva importes y recupera la identidad previa al volver. Recepción documental identifica NC/ND/remitos/OC sin reinterpretarlos como factura, aunque su circuito aún no esté implementado.

CompraCargaPlanificador calcula un borrador de FCC A/C en pesos. Neto e IVA por concepto, con importes decimales redondeados a dos decimales (AwayFromZero); IVA explícito se conserva y se avisa si difiere de la tasa. No se aplica una cuenta de redondeo automáticamente. Imputación explícita por concepto, luego particular del proveedor si elegible, luego cuenta del motivo. IVA al Debe, percepciones al Debe y total del proveedor al Haber. Marca una sola cabecera fiscal propuesta para factura A. En C no hay crédito fiscal, pero se conserva una cabecera fiscal y se respeta MonotributoAlIVA. La grabación mantiene TC/CUENTA sin relleno y códigos maestros al ancho habitual.

La vista verifica identidad 4+8+letra sin truncar, unidades autorizadas, fechas, tasas y cuentas. Consulta C_MV_CPTE por proveedor/FCC/identidad, excluyendo usuario IMPORTACION del chequeo operativo. Advierte separadamente sobre MV_ASIENTOS con ese tipo/número; no afirma que un asiento importado permita registrar o reemplazar una compra. Antes de grabar habrá que validar equivalencias contables, ejercicio/subdiario, moneda/cotización, duplicidad transaccional y reglas completas de IVA/proveedor.

CompraCargaOrigenLectura usa totales persistidos de la lectura IA. Si hay una tasa utiliza el neto gravado y el IVA explícito; con varias tasas conserva ambos totales en una línea que requiere separación manual (tasa cero), nunca deduce bases dividiendo IVA. No interpreta artículos/bultos ni reejecuta IA. El operador confirma alcance de servicios/gastos. Importes desconocidos y lecturas incompletas permanecen señalados.

## Verificación del alcance inicial

Pruebas del planificador: referencia legacy $10 + $2,10, cuentas por prioridad, tasas mixtas, percepciones, C, fechas/unidad y desbalances. Prueba SQL optativa COMPRA_CARGA_TEST_SQL, limitada a ALFANET/SERVER-ALFAVB6: solo lectura, comprueba referencia, duplicados y que los conteos no cambian. No contiene credenciales en el repositorio.

El alcance inicial de servicios/gastos se amplía con el contrato vigente descrito al final. Permanecen los límites explícitos de fiscalidad y mercadería especial; pagos está excluido por pedido del usuario. Los triggers y el ejemplo relevado no son una plantilla ciega: la referencia usaba 21209 (Anticipos de Clientes) por imputación particular y hay dos entradas independientes de stock para remito/factura.

## Registro transaccional de servicios/gastos

CompraCargaService.Registro reutiliza el planificador, contexto y proveedor oficiales. RegistrarAsync copia la solicitud antes del primer await y abre transacción Serializable. Exige sesión, base, actor y permiso D50-CARGA-COMPRAS-WEB; los revalida antes del commit. La huella SHA-256 incluye borrador, proveedor, contexto, bases y asiento revisados. Si cambian, obliga a revisar nuevamente. Total obligatorio, importes de dos decimales, FCC externo al Haber, A/IVA 1 o C/IVA 5, proveedor en pesos y FCC_Sin_OC; se bloquean cuentas usaCCostos hasta implementar distribución.

La actualización 2026-11-03-003 crea ALFACORE_COMPRA_OPERACION para idempotencia, resultado, solicitud y origen documental opcional único. No es configuración ni sustituye tablas oficiales. Se serializan altas web con sp_getapplock, y los rangos comerciales/contables con UPDLOCK/HOLDLOCK; la asignación contable reutiliza NumeracionAsiento y comparte su contador con el resto de AlfaCore/legacy. Mismo GUID y solicitud/actor devuelve el resultado anterior. Otro GUID con la misma factura del proveedor queda bloqueado, también para importadas/anuladas, sin borrar ni reemplazar.

Se resuelve EQU_FCC, subdiario de MA_SUBDIARIOSCOMPROBANTES/MA_SUBDIARIOS (vacío si no hay asociación, igual al legacy) y un único MV_EJERCICIOS abierto por fecha. Se insertan C_MV_CPTE y C_MV_CPTE_OBSERV de tipo OC para conceptos sin stock, MV_ASIENTOS con deuda del proveedor al Haber y contrapartidas al Debe, V_MV_CPTEACCIONES y operación auxiliar. Asiento completo/balance y sesión se verifican antes de confirmar. Todo se revierte si falla. No se actualizan contadores externos, no se generan pagos/aplicaciones ni se desactivan triggers.

Solo secuencia 1 contiene CAB* y LIVA*. Las posiciones de tasas se conservan según PIVA/PIVADIF/PIVADIF1/PIVADIF2, incluyendo posiciones vacías. C no discrimina IVA y mantiene su base; MonotributoAlIVA decide LIVA_TIPO. Exentos/no gravados van a LIVA_ImpNetoNGrav como en el legacy relevado; LIVA_EXENTO correspondía a impuestos internos no soportados. C_MV_CPTE conserva sus campos disponibles de IVA; MV_ASIENTOS conserva las cuatro posiciones fiscales. Percepciones se conservan contable y fiscalmente. CAE/CAI y vencimiento opcionales se guardan en NroCAI/FhVtoCAI, sin validación remota; CodCpteDGI toma el código configurado del talonario, sin inventar uno si está vacío.

La confirmación modal impide cierre por fondo, muestra guardado en curso y conserva errores/borrador. Tras éxito bloquea edición del comprobante registrado y ofrece Nueva compra. La referencia documental evita repetición desde AlfaCore y se completa con el circuito transaccional de recepción descrito abajo.

Ensayo SQL optativo COMPRA_REGISTRO_TEST_SQL: restringido a ALFANET/SERVER-ALFAVB6, inserta A y C con transacción externa y ROLLBACK obligatorio, compara libro/contabilidad, idempotencia, duplicados y ausencia de stock/aplicaciones. No conserva comprobantes ni asientos de prueba; SQL Server puede avanzar las identidades aunque se reviertan las filas.

En cuentas sin moneda informada se conserva el valor por defecto en pesos observado en el legacy; las monedas explícitas distintas de 1 quedan excluidas. El alta rápida permite elegir FCC_Sin_OC y lo conserva al reabrir la ficha. La actualización del maestro solo modifica esa marca cuando el pedido la informa; los guardados previos sin el campo no la borran.

## Recepción documental y consulta de la compra

La actualización 2026-11-03-004 incorpora INT_ESTADO.REGISTRADO (final, no editable) y recupera el estado de recepciones con ALFACORE_COMPRA_OPERACION anterior y compra existente. Solo actualiza recepción/historial; conserva lecturas históricas y contabilidad.

El borrador conserva ID de IA_Compras_CAB y SHA-256 del JSON leído. Registrar bloquea el documento y la última lectura con UPDLOCK/HOLDLOCK en su misma conexión/transacción. Exige COMPROBANTE_COMPRA, lectura PROCESADO/SIN_PROVEEDOR y versión vigente; no aprueba otra lectura nueva ni una en ejecución. Después de insertar factura/asiento/operación, cambia IA a APROBADO, recepción a REGISTRADO e inserta CAMBIO_ESTADO en INT_COMPROBANTE_RECIBIDO_HIST (Proceso COMPRA_REGISTRADA en el detalle JSON). Un fallo revierte también esos cambios. Se conservan JSON, adjuntos y archivos originales.

Interfaces muestra Registrado por vínculo web o IA aprobada legacy. El filtro RegistracionCompra aplica a tipos de compra, en SQL antes de paginar y con el mismo predicado en el conteo. El auxiliar es opcional antes de instalar Compras; se detecta y se carga mediante SQL dinámico a una temporal. Las recepciones registradas no pueden reabrirse manualmente, reencolarse o borrarse; los predicados de UPDATE y el bloqueo de borrado cierran la carrera con una grabación concurrente. Worker y cola omiten documentos REGISTRADO.

CompraDocumentoDialog consulta mediante CompraCargaService con sesión/base/actor y permisos de Compras/Recepción, además de unidades autorizadas de TA_USUARIOS. Obtiene C_MV_CPTE por ID del vínculo, conceptos por proveedor y asiento por clave completa guardada (ejercicio, mes, número, subdiario); nunca agrupa proveedores por el mismo número de factura. Reutiliza ComprobanteViewer con DatosConsulta precargados: no ejecuta consultas generales por TC/número, navegación a ventas ni PDF de ventas. Permite el modal de asiento completo y conserva el modo directo dentro de Interfaces.

Las pruebas SQL con rollback verifican recepción pendiente ante un duplicado, confirmación conjunta, filtros/conteo y consulta completa con cuenta del proveedor. No dejan comprobantes o documentos de ensayo persistidos; pueden avanzar identidades SQL. La prueba del visor verifica que el modo precargado no haga lecturas fuera del alcance autorizado.

## Ampliación del 03/11/2026: contrato vigente

Se conserva el circuito transaccional anterior y se agregan:

- Consulta independiente en CompraConsultaDialog, accesible desde el toolbar de carga. C_MV_CPTE por ID y unidades autorizadas; filtros SQL de fechas/texto/tipo/estado, máximo 501 para detectar truncamiento y mostrar 500. Consulta completa con artículos, proveedor, asiento por clave contable y auditoría. Las compras legacy no se identifican solo por número: se exige cuenta, TC/equivalencia y una referencia contable no ambigua.
- ALFACORE_COMPRA_BORRADOR (007), por usuario/sistema en cada base, JSON de edición con números crudos para no perder campos incompletos. Versión optimista, bloqueo Serializable y comprobación de sesión/actor antes del commit. Guardado explícito, recuperación y nueva revisión obligatoria; no configura nada ni registra contabilidad.
- NCC/NDC A/C: positivos de entrada, sentido de V_TA_CPTE verificado y EQU_<TC>. NCC invierte D/H y los importes LIVA (incluido total y tributos), sin invertir alícuotas ni duplicar cabeceras. Cabecera comercial conserva sus importes positivos. Las notas internas usan los contadores <LETRA>_ULTIMO_NRO y <LETRA>_SUC_DEFAULT, incluyendo máximo comercial y revalidación en la transacción; no se consume el contador en vista previa.
- Aplicación opcional NCC→FCC del mismo proveedor/unidad. Lee CO_CPTES_SALDOS_UNEG, exige importe positivo en centavos, no mayor que nota ni saldo, y fecha de factura no posterior. Escribe MV_APLICACION con referencias contables y TC/CUENTA sin relleno; no emite pagos. Sin selección, el crédito queda sin aplicar.
- TA_CCOSTO imputable y MA_CUENTAS.usaCCostos: distribución explícita por cuenta al 100 %, sin duplicados. MV_ASIENTOSCCOSTO se graba por renglón contable, sentido y clave del asiento en su misma transacción. Redondeo con residual final exacto; no se autoseleccionan centros.
- Impuestos internos: cuenta por V_TA_INPUT_COMPRAS.C_CFI_IMPINT → TA_CUENTASIVA.C_CFI_IMPINT → CuentaImpuestoInterno. Partida propia en asiento, ImporteImpuestosInternos comercial y LIVA_EXENTO fiscal con signo; no integra IVA crédito. El libro web separa este importe de Exento únicamente cuando la suma fiscal demuestra que LIVA_EXENTO ya lo contiene; evita duplicarlo en la exportación sin alterar lectores que separaron ambos conceptos.
- Mercadería: PrepararDesdeOrigenAsync permite FCC de una OC/RMC completa sin aplicaciones previas. Lee C_MV_CPTEINSUMOS, V_MA_ARTICULOS y stock oficial. Exige unidad simple, ausencia de series/lotes/talles/equivalencias/cuenta especial, depósito válido y stock de remito consistente. Huella del origen y sus movimientos, revalidada con locks al registrar. OC sin recepción: nueva entrada V_MV_STOCK; RMC: conserva stock sin duplicar. Inserta detalle y MV_APLICACION, referencia y Finalizado del origen. Costos de adquisición en movimientos; no actualiza costos/precios maestros sin incorporar su política legacy.
- ALFACORE_COMPRA_OPERACION.AsientoHuella y ALFACORE_COMPRA_ANULACION (007): anulación exclusiva de compras web con huella conservada y sin dependencias. Revalida cuenta/unidad, ejercicio/rangos/cierre, cambios externos, contabilizado/conciliado y aplicaciones/stock. Guarda snapshot de ledger y centros, marca cabecera anulada y retira su asiento/libro en una transacción, como el circuito legacy de compras (FrmAnulCpteVta.frm). No borra originales ni cabeceras; no permite reutilizar IDs ni anular documentos legacy sin evidencia suficiente. La recepción conserva su vínculo histórico registrado.

Los seis ejes tienen avances concretos, pero no equivalen a paridad completa del desktop. Permanecen pendientes creación directa de OC/RMC y edición comercial integral, recepciones parciales/múltiples, costos maestros bajo sus políticas, mercadería especial, moneda extranjera y fiscalidad fuera de A/C. Pagos está expresamente excluido.

### Integración y versiones de actualizaciones

Se integró origin/main hasta e7851f0a. Las versiones 002 y 003 habían sido publicadas con nombres diferentes por trabajos paralelos. Se retiraron ambos nombres de cada colisión y se agregaron aliases oficiales en ActualizacionesService, conservando exactamente el SQL anterior: carga 002→005, operaciones 003→006, IVA Ventas 002→008 e IVA Compras 003→009. El historial identifica scripts ya aplicados y el actualizador incremental puede ignorar copias antiguas idénticas. No se cambia la prueba de unicidad ni se agregan excepciones históricas. El script nuevo de borradores/anulación es 007.

### Evidencia legacy

V_MV_CPRA.vbp / V_mv_Cpra.frm: decisión de stock 8597–8660, crédito 9180, cabecera fiscal 9996–10091, aplicación comercial 10550–10595. ClsCCosto.cls: inserción MV_ASIENTOSCCOSTO 190–212. Stuff.bas: prioridad de impuesto interno 2550–2625. FrmAnulCpteVta.frm: validaciones y eliminación contable de compras 2223–2238 y ProcesoAnulaCPTE_Compras. Se leen como CP1252; no se modifica código legacy.

### Verificación final

03/11/2026: compilación y tanda focalizada con 79 pruebas correctas, 1 omitida (ensayo de Libro IVA Ventas que requiere otro entorno). Incluye compras, visor documental, unicidad/compatibilidad de actualizaciones, rutas SaaS, navegación por tenant, recepción de tarjetas y libros de IVA. Ensayo SQL en ALFANET/SERVER-ALFAVB6 con transacción externa y rollback: A/C, idempotencia, recepción, NCC aplicada, anulación auditada y bloqueada ante aplicaciones, distribución contable, borrador/versiones y facturación completa desde OC/RMC. El caso de mercadería usa cantidad 2 para verificar precio unitario frente a total de renglón; desde remito no suma otro movimiento, desde OC suma una entrada. Los conteos iniciales/finales coinciden. No quedan datos de ensayo persistidos; las identidades SQL pueden avanzar al revertir.

Distribución por redondeo acumulado: se prueban 100 centros sobre 0,50 para impedir residuales negativos. Impuestos internos: prueba de separación en el libro para factura y crédito, conservando datos cuando ya venían separados. Catálogo: 107 rutinas, cero advertencias/errores. Fuentes UTF-8 sin BOM, sin mojibake frecuente; git diff --check correcto. Falta revisión visual interactiva por el operador; no se desplegó ni se ejecutaron actualizaciones permanentes en las bases.

## Grilla de artículos y búsqueda de proveedores (03/11/2026)

Se adopta el manejo de VentasCargaDirecta: proveedor primero, código → Enter → cantidad → costo → Enter, búsqueda modal y navegación por flechas. Se reutilizan AlfaLookup/AlfaDialog/inputs y cargaDirecta.focus/scrollIntoView; la búsqueda conserva la semántica de costos de compras y los permisos propios, sin llamar al servicio de Ventas para precios/clientes. El módulo JS de compras conserva handlers por editor, para que disponer una página anterior no elimine atajos de la nueva.

Correcciones de proveedor: OnParametersSetAsync ya no reinicia el borrador cuando el shell vuelve a pasar los mismos parámetros. La notificación null del lookup durante la escritura no borra motivos ni actualiza el header sin necesidad. Al elegir, se muestra código/nombre. La consulta oficial VT_PROVEEDORES normaliza CUIT, compara descripción sin acentos, escapa comodines y prioriza coincidencias exactas. F2 muestra búsqueda explícita con resultados iniciales y feedback.

CompraCargaService.Articulos usa V_MA_ARTICULOS, depósito y unidad autorizados. Lee costo, tasa, unidad, marca NoStock e imputación particular; stock informativo desde V_MV_STOCK con signo existente. Huella de datos maestros (excluye stock informativo) y revalidación en la misma transacción de registro. Artículos especiales permanecen identificados/bloqueados; no se infieren equivalencias o impuestos. El proveedor sigue resolviéndose por VT_PROVEEDORES.

La carga directa ya no requiere OC/RMC cuando el proveedor permite facturación sin OC. Se admiten renglones mixtos: C_MV_CPTE.ImporteInsumos e ImporteOtrosConceptos se separan, artículos en C_MV_CPTEINSUMOS, otros conceptos en C_MV_CPTE_OBSERV y un asiento/IVA común. GrabarArticuloAsync comparte la escritura de detalle/stock entre carga directa y facturación de orígenes; un remito no repite stock. NCC usa cantidades negativas en stock, manteniendo importes positivos del detalle y signo fiscal negativo. NoStock no genera existencias. Se conserva el maestro de costos/precios. FCC_Sin_OC no bloquea notas, conforme a V_mv_Cpra.frm 11525.

Edición persistente conserva las cantidades/costos como texto, incluso incompletos. El servidor compara cantidad × costo redondeado con el neto informado y valida nuevamente artículo, unidad y huella antes de grabar. No se permite confirmar un formulario que cambió desde la revisión.

Las referencias Git incorporaron en paralelo CRM 004 y Contabilidad 007/008/009. Se renombraron los auxiliares aún pendientes a 010 (recepción), 011 (borradores/anulación), 012 (menú IVA Ventas), 013 (menú IVA Compras). Por una nueva colisión con Contabilidad 011 y CRM 012, borradores/anulación pasó a 015 y menú IVA Ventas a 016 (alias conservados en ScriptsRenombrados), conservando SQL y aliases por nombre completo para instalaciones que hubieran aplicado los anteriores. Se mantienen carga 005 y operaciones 006; no se modifican pruebas ni scripts de esos otros módulos.

Verificación de esta ampliación: 63 pruebas correctas, cero fallos/omisiones. La prueba SQL de lectura verifica proveedor por código, nombre, CUIT sin guiones y comodines literales; consulta artículos reales. El ensayo con rollback verifica carga directa mixta, separación de netos en cabecera, salida por NCC, NoStock y rechazo de una huella de artículo obsoleta sin crear cabecera. Los tests de UI verifican que teclear no reinicie motivo y que repetir parámetros del shell no borre la búsqueda ni haga otra consulta. Node verifica F2/F5, captura de flechas y disposición independiente de handlers. Catálogo 107/0/0, UTF-8 sin BOM en 46 archivos pendientes, git diff --check correcto. La validación visual interactiva queda para la revisión del operador. No se publicaron cambios.


## Facturación parcial de un origen (03/11/2026)

Ampliación incremental sobre el contrato anterior: Preparar factura desde una OC/RMC precarga solo artículos con saldo, consultando las mismas vistas que V_mv_Cpra.frm (`CO_AplicacionOC`, líneas 5750–5770; `VT_C_MV_INSUMOS_APLICACION`, 12385–12400). Se verificaron sus definiciones en ALFANET/SERVER-ALFAVB6. Permite reducir cantidades o quitar artículos; cada cantidad positiva y de hasta cuatro decimales se valida contra el pendiente en la transacción de registro. El hash incluye cantidades originales/aplicadas, stock y la configuración de recepción: una aplicación concurrente invalida la preparación anterior.

Cada factura guarda su detalle y MV_APLICACION con un solo origen. Finalizado se marca únicamente si se agotaron todos los artículos; el saldo se conserva para otra factura. RMC no genera nuevo stock; OC ingresa solo lo facturado y permite aplicaciones sucesivas de FCC. La regla legacy IR_Oblig=SI impide facturar una OC sin pasar por recepción; ese circuito IR completo sigue pendiente. El cierre voluntario del remanente se ofrece solo para OC (CerrarSaldoOrigen, equivalente a la pregunta de V_mv_Cpra.frm ~8974) y se rechaza en remitos; el ensayo SQL con rollback lo verifica (stock solo por lo facturado, Finalizado=1).

Las vistas históricas acumulan todos los artículos del destino para cada origen vinculado. Para no asignar cantidades de otra orden/remito, se bloquea una aplicación existente que reúna más de un origen o que corresponda a otro circuito. Continúan excluidos artículos repetidos dentro del origen, unidades especiales y diferencias de stock en remitos. Crear OC/RMC, recepción parcial de mercadería y consolidación de varios orígenes todavía no están implementados; esta ampliación cubre facturación parcial de documentos existentes, no todo el circuito comercial.


Verificación de versiones: el historial remoto incorporó 010 de Contabilidad. Se renombró el script de recepción pendiente de 010 a 014 sin cambiar su SQL, conservando los aliases 004 y 010 por nombre completo. No se modificó el script remoto ni las pruebas de unicidad.


Verificación de facturación parcial: 63 pruebas correctas, ninguna omitida. El ensayo SQL en ALFANET/SERVER-ALFAVB6 registra dos facturas sucesivas de una unidad sobre un origen de dos, verifica saldo/finalización, rechazo del exceso y bloqueo por IR_Oblig. Desde RMC no se agrega stock; desde OC se agregan dos entradas parciales. Los fixtures, cambios de configuración y movimientos de ensayo se revierten mediante rollback, con comparación de conteos. Catálogo 107 rutinas/0 advertencias/0 errores, git diff --check correcto y fuentes UTF-8 sin BOM. La revisión visual interactiva sigue pendiente; no hubo publicación ni cambios permanentes de comprobantes en la base.

## OC y remito directos (2026-11-03)

Se habilitó la carga directa de OC y RMC (CompraCargaService.Comercial.cs, CompraCargaPlanificador.PrepararComercial). Equivale a grabar una FCC de artículos sin tocar MV_ASIENTOS, libro de IVA, centros de costos ni cuenta corriente. Referencia legacy: V_mv_Cpra.frm, GeneraAsiento=False para OC/RMC; OC con Aprobado=1, Lugar_Entrega/FECHA_A_VENCER e IDDEPOSITODESTINO (~8011), ActualizaStock=PEsGarantias (solo OCG); RMC con ActualizaStock=True (~8649).

- OC: letra X, numeración interna con el contador A de V_TA_CPTE (la C se reserva a monotributo, como en las facturas), Aprobado=1, sin stock.
- RMC: numeración externa (el número del proveedor, único por proveedor), letra R, ingresa stock salvo artículos NO_CONTROLA_STOCK. Rechazado si el proveedor no tiene FCC_Sin_OC (regla VerificaOCenRMC), hasta implementar el remito aplicado a una OC.
- Persistencia: C_MV_CPTE, C_MV_CPTEINSUMOS, V_MV_STOCK (RMC), V_MV_CPTEACCIONES y ALFACORE_COMPRA_OPERACION (Periodo/TipoRegistro vacíos, Mes/Asiento 0; sin AsientoHuella, por lo que no se anulan desde AlfaCore).
- Verificación: ensayo SQL con rollback en ALFANET/SERVER-ALFAVB6 (OC aprobada sin stock ni asiento, RMC con stock, duplicado rechazado, ambos preparables para facturar desde su origen) y pruebas unitarias del planificador. Pendiente: remito aplicado a una OC (recepción parcial), revisión visual.

## Mail de OC, pagos y certificados de retención (2026-11-03)

Reemplaza la impresión diferida del VB6 (FrmRp_Diferida: OPG, PG y GAN/IVA/IBR/SUSS). Servicio `CompraDocumentoMailService` (PDF por `IDocumentPdfService`, envío por `CorreoSalienteService`, registro en `V_MA_EMAILMOV` con `TC` = tipo del documento o código del impuesto). Pantallas: acciones Mail/PDF y selección en `/compras/carga/visor` (OC y RMC) y solapa `/compras/carga/pagos` (PG/OPG de `V_MV_CPTE`, columnas `RETGAN_*`, `RETIVA_*`, `RETIBR_*`, `RETSUSS_*`).

- Alcance y permisos: unidades autorizadas del usuario y el permiso de la carga de compras (`D50-CARGA-COMPRAS-WEB`).
- El mail del proveedor sale de `VT_PROVEEDORES.MAIL`; el modo prueba usa `ALFACORE_MAIL_PRUEBA` como en Ventas.
- Textos en `TA_CONFIGURACION`: `COMPRAS-MAIL-ASUNTO-{OC|PAGO|RETENCION}` y `COMPRAS-MAIL-MENSAJE-{OC|PAGO|RETENCION}` (con valor por defecto si faltan).
- La alícuota de la retención no se guarda en el pago (queda en 0): el certificado usa la del régimen (`V_TA_RETENCIONES.PORCENTAJE`) o la deduce de lo retenido sobre la base.
- Los PDF son un diseño propio simple (no reproducen LiqOPG.rpt ni RETIGAN de Crystal); hay que revisarlos visualmente con datos reales y ajustarlos al formato que usa la empresa.
- Pendiente: certificados de Ingresos Brutos y SUSS no se probaron con datos reales (en ALFANET solo hay pagos con Ganancias e IVA).

# Preparación de compras: primera etapa

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

## Verificación y siguiente etapa

Pruebas del planificador: referencia legacy $10 + $2,10, cuentas por prioridad, tasas mixtas, percepciones, C, fechas/unidad y desbalances. Prueba SQL optativa COMPRA_CARGA_TEST_SQL, limitada a ALFANET/SERVER-ALFAVB6: solo lectura, comprueba referencia, duplicados y que los conteos no cambian. No contiene credenciales en el repositorio.

Pendiente: actualización del estado documental y acceso directo al comprobante definitivo desde la bandeja, fiscalidad fuera de A/C, anulación/edición comercial, persistencia del borrador y circuitos de mercadería/stock/costos/remitos/OC/pagos. Los triggers y el ejemplo relevado no son una plantilla ciega: la referencia usaba 21209 (Anticipos de Clientes) por imputación particular y hay dos entradas independientes de stock para remito/factura.

## Registro transaccional de servicios/gastos

CompraCargaService.Registro reutiliza el planificador, contexto y proveedor oficiales. RegistrarAsync copia la solicitud antes del primer await y abre transacción Serializable. Exige sesión, base, actor y permiso D50-CARGA-COMPRAS-WEB; los revalida antes del commit. La huella SHA-256 incluye borrador, proveedor, contexto, bases y asiento revisados. Si cambian, obliga a revisar nuevamente. Total obligatorio, importes de dos decimales, FCC externo al Haber, A/IVA 1 o C/IVA 5, proveedor en pesos y FCC_Sin_OC; se bloquean cuentas usaCCostos hasta implementar distribución.

La actualización 2026-11-03-003 crea ALFACORE_COMPRA_OPERACION para idempotencia, resultado, solicitud y origen documental opcional único. No es configuración ni sustituye tablas oficiales. Se serializan altas web con sp_getapplock, y los rangos comerciales/contables con UPDLOCK/HOLDLOCK; la asignación contable reutiliza NumeracionAsiento y comparte su contador con el resto de AlfaCore/legacy. Mismo GUID y solicitud/actor devuelve el resultado anterior. Otro GUID con la misma factura del proveedor queda bloqueado, también para importadas/anuladas, sin borrar ni reemplazar.

Se resuelve EQU_FCC, subdiario de MA_SUBDIARIOSCOMPROBANTES/MA_SUBDIARIOS (vacío si no hay asociación, igual al legacy) y un único MV_EJERCICIOS abierto por fecha. Se insertan C_MV_CPTE y C_MV_CPTE_OBSERV de tipo OC para conceptos sin stock, MV_ASIENTOS con deuda del proveedor al Haber y contrapartidas al Debe, V_MV_CPTEACCIONES y operación auxiliar. Asiento completo/balance y sesión se verifican antes de confirmar. Todo se revierte si falla. No se actualizan contadores externos, no se generan pagos/aplicaciones ni se desactivan triggers.

Solo secuencia 1 contiene CAB* y LIVA*. Las posiciones de tasas se conservan según PIVA/PIVADIF/PIVADIF1/PIVADIF2, incluyendo posiciones vacías. C no discrimina IVA y mantiene su base; MonotributoAlIVA decide LIVA_TIPO. Exentos/no gravados van a LIVA_ImpNetoNGrav como en el legacy relevado; LIVA_EXENTO correspondía a impuestos internos no soportados. C_MV_CPTE conserva sus campos disponibles de IVA; MV_ASIENTOS conserva las cuatro posiciones fiscales. Percepciones se conservan contable y fiscalmente. CAE/CAI y vencimiento opcionales se guardan en NroCAI/FhVtoCAI, sin validación remota; CodCpteDGI toma el código configurado del talonario, sin inventar uno si está vacío.

La confirmación modal impide cierre por fondo, muestra guardado en curso y conserva errores/borrador. Tras éxito bloquea edición del comprobante registrado y ofrece Nueva compra. La referencia documental evita repetición desde AlfaCore, pero no marca aprobada la recepción ni modifica la lectura IA en esta etapa.

Ensayo SQL optativo COMPRA_REGISTRO_TEST_SQL: restringido a ALFANET/SERVER-ALFAVB6, inserta A y C con transacción externa y ROLLBACK obligatorio, compara libro/contabilidad, idempotencia, duplicados y ausencia de stock/aplicaciones. No conserva comprobantes ni asientos de prueba; SQL Server puede avanzar las identidades aunque se reviertan las filas.

En cuentas sin moneda informada se conserva el valor por defecto en pesos observado en el legacy; las monedas explícitas distintas de 1 quedan excluidas. El alta rápida permite elegir FCC_Sin_OC y lo conserva al reabrir la ficha. La actualización del maestro solo modifica esa marca cuando el pedido la informa; los guardados previos sin el campo no la borran.

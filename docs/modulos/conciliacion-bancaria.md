# Conciliación bancaria inteligente

Cobranza rápida CB y relevamiento de Ventas/Cobranzas legacy: [base compartida de Cobranzas](cobranzas.md). Requiere actualización 004. Cobrar está disponible desde la fila o desde el detalle para ingresos clasificados como Cobranza; valida cliente, saldo, unidad y banco nuevamente al confirmar.

Primera integración en AlfaCore, 19/10/2026. Ruta `/caja-bancos/conciliacion-bancaria`, también con prefijo de empresa/base. Clave web propia `D75-CONCILIA-IA`, hija de Caja y Bancos (`D75`). Se conservan las opciones legacy de conciliación.

## Base reutilizada

Se incorporan los parsers Excel, entidades y motores determinísticos de `C:/dev/Alfa_Conciliaciones_Bancarias_IA`, bajo `src/AlfaCore/ConciliacionBancaria/`. El WinForms y su conexión independiente se sustituyen por una página Blazor, servicios de sesión de AlfaCore y un repositorio Dapper. No se requiere que ese repositorio externo esté instalado para compilar o ejecutar AlfaCore.

El usuario elige primero el archivo. La detección utiliza contenido y encabezados; el selector de banco se oculta cuando se reconoce el archivo. Si no hay un origen único o falla la lectura, conserva el archivo y permite elegir banco. No confía en el nombre para determinar el banco. La fecha del nombre original de Mercado Pago se utiliza exclusivamente para resolver fechas relativas o sin año del scraping.

Se mantiene el recorrido importar → normalizar → clasificar → proponer aplicaciones → mostrar revisión y detalle. La selección de cuenta bancaria se valida en servidor contra `MA_CUENTAS`, `MEDIODEPAGO='DB'`, activa y sin título. Una única coincidencia del nombre del banco puede sugerirse, priorizando la descripción completa (por ejemplo, BANCO PROVINCIA) frente a referencias auxiliares a ese banco; varias cuentas del mismo banco requieren selección manual. No se infiere un número de cuenta bancaria que no esté mapeado en el maestro.

## Datos y transacciones

Las tablas existentes `BK_*` se conservan. El primer script crea únicamente las faltantes y agrega:

- `BK_ImportacionExtracto.CuentaBanco`: cuenta contable asociada al extracto; las importaciones históricas quedan sin asignar hasta definir su cuenta.
- `BK_ExtractoMovimiento.HuellaWeb`: huella de banco/cuenta/fecha/importe/referencia. Sin referencia agrega descripción y ordinal para conservar operaciones idénticas repetidas dentro del archivo.

La importación, las clasificaciones, propuestas y sus aplicaciones se confirman en una transacción serializable. El hash de contenido evita reimportar el mismo archivo renombrado para una cuenta. Las huellas omiten movimientos ya presentes en otros extractos de esa cuenta. Si todos son duplicados, no queda una nueva cabecera. Las importaciones legacy sin cuenta/huella no se vinculan automáticamente a una cuenta supuesta; la deduplicación por cuenta comienza con las importaciones web.

Sin identificador externo único, una coincidencia por descripción/fecha/importe puede ser ambigua en archivos parcialmente superpuestos. El ordinal preserva multiplicidad, pero no prueba identidad bancaria. La revisión del usuario sigue siendo necesaria.

Deuda: `VE_CPTES_SALDOS_VENTAS` sin recalcular saldos. Esquema confirmado en pruebas: incluye `CUOTA`, `NOMBRE`, `CUENTA`, `TC`, `SUCURSAL`, `NUMERO`, `LETRA`, `SALDO` y `VENCIMIENTO`. Clientes: `VT_CLIENTES`. Posible cobro previo: `VE_COBRANZAS_REALIZADAS`, que toma secuencia 1 de `MV_ASIENTOS` para cobranzas. Es una advertencia por cliente/importe/fecha, no un vínculo contable confirmado.

Las propuestas son sugerencias independientes, no reservan saldo ni constituyen aplicaciones reales. Dos ingresos pueden sugerir el mismo comprobante. Antes de una futura generación habrá que revalidar saldos y compatibilidad de todas las aplicaciones. La confianza es una puntuación de reglas, no una probabilidad estadística ni una evaluación de IA.

## Seguridad, errores y shell

El servicio exige usuario autorizado para la sesión activa y permiso `D75-CONCILIA-IA`. Captura la conexión al comenzar; no usa conexión alternativa. La página cancela y descarta archivo y resultados ante cambio de sesión o usuario. Los errores pasan por `IAppEventService` hacia `AUX_ERR`; importación y eliminación registran además su resultado en `BK_GeneracionAlfaLog`.

AlfaDesign: `ModuleKey=caja-bancos`, módulo ya presente en `AlfaDesignManagedModules`; utiliza barra superior y Context Toolbar. Conserva `directo=1` en navegación del módulo. Listados paginados en SQL, 50 filas por página. La eliminación requiere confirmación y rechaza importaciones que ya tengan acciones registradas sobre sus movimientos.

## Configuración

### Origen de categorías y corrección manual (20/10/2026)

El catálogo del filtro continúa definido en `MovementCategory` (enum). La asignación automática utiliza `BK_ReglaInterpretacion`: banco, patrón de texto normalizado, signo, prioridad y categoría objetivo. Se aplica la primera regla coincidente; SIGN_CREDITO asigna Cobranza a ingresos sin coincidencia específica y SIGN_DEBITO asigna Gasto a egresos. El fallback del motor también usa signo si no hay reglas. Las categorías, reglas existentes y su configuración no se modificaron en esta entrega; no se agregó un administrador de reglas ni aprendizaje automático.

En Detalle se puede reclasificar un movimiento aún no cobrado y agregar un motivo opcional. Se inserta una nueva versión en BK_MovimientoClasificado con ReglaCodigo=MANUAL y se audita usuario, categoría anterior/nueva y motivo en BK_GeneracionAlfaLog (RECLASIFICAR). Las propuestas previas quedan con EstadoPropuesta=RECLASIFICADO como historial; no aparecen como propuestas activas ni cliente probable cuando el movimiento deja de ser Cobranza. La corrección persiste al volver a abrir la revisión; no crea reglas para movimientos nuevos de futuras importaciones.

Si se vuelve a Cobranza, se ejecuta el motor existente para ese movimiento, con el saldo actual y la tolerancia configurada, sin reprocesar otros movimientos. ConciliacionRepository admite ese alcance opcional y toma la clasificación más reciente. Los movimientos con cobranza registrada no pueden reclasificarse. La eliminación de importaciones sigue bloqueada por acciones contables; una corrección de clasificación sin generación contable no la bloquea.

### Contexto de comprobantes en el detalle

Se muestran hasta 40 FC/FP/ND del cliente identificado, priorizando los propuestos y luego los más próximos a la fecha del movimiento; se ordenan por fecha para comparar anteriores/posteriores. Incluye fecha, vencimiento informado en la vista oficial de saldos, moneda, total, saldo actual, unidad, documento/CUIT, comentarios, observaciones y descripción de los primeros cinco renglones (hasta 500 caracteres). Los saldos guardados de la propuesta se conservan separados de la consulta actual. Si la vista no informa deuda, se muestra Sin pendiente informado, sin asumir un saldo histórico ni reconstruir vencimientos ausentes.

MV_APLICACION permite mostrar NP/RM vinculados como origen, con fecha y comentarios de cabecera si existen. Sin vínculo se informa expresamente. Se aplican permisos por unidad mediante TA_USUARIOS y AdminCentral. No hay grabaciones en las tablas contables para estas consultas. Al seleccionar Ver se cambia el comprobante mostrado; Cobrar propone ese comprobante si sigue autorizado y pendiente en pesos, y rechaza el cambio si ya no está disponible (no sustituye silenciosamente por otro).

Archivos: nuevo `Services/ConciliacionBancariaService.Revision.cs`; modificados `ConciliacionBancariaService.cs`, `ConciliacionRepository.cs`, `Models/ConciliacionBancariaModels.cs`, página/CSS de Conciliación y pruebas de ConciliacionBancariaTests. La página conserva el shell y limpia el contexto al cambiar de sesión. No requiere migración SQL.

Validación del 20/10: compilación y 27 pruebas aprobadas, ninguna omitida. Se verificó persistencia de la reclasificación, salida del filtro Cobranza, conservación de historial, regeneración exclusiva del movimiento al volver a Cobranza, rechazo de movimientos cobrados y checksum de reglas sin cambios. Las importaciones de ensayo se eliminan al finalizar. La consulta contextual se ejecutó sobre propuestas reales en ALFANET2007_PRUEBAS; el test de pantalla verifica clasificación, fechas anterior/posterior, saldo, conceptos y origen mediante datos de representación controlados. Chromium con HTML renderizado y CSS real a 2048/1440/1024/390 px: modal y clasificación visibles, comparación contenida y sin overflow horizontal global; el desplazamiento de la tabla es interno. No equivale a un recorrido de extremo a extremo con sesión Blazor activa. Catálogo sin errores ni advertencias. No se reclasificaron los movimientos reales señalados en la imagen ni se cambiaron reglas/configuración.

`TA_CONFIGURACION.CLAVE=CONCILIACION-TOLERANCIA`, grupo `CAJA`: margen en pesos para propuestas exactas y múltiples. Valor inicial `1.00`, preservado si ya existe. Lectura con fallback de `VALOR` vacío a `ValorAux`. No autoriza diferencias contables ni ajusta importes automáticamente.

## Alcance y validación

Solo archivos `.xlsx`, máximo 20 MB. Los scrapers continúan funcionando fuera de AlfaCore. Importar y revisar no generan cobranzas ni asientos. La acción explícita Confirmar cobranza CB registra recibo, asiento y aplicación mediante CobranzasService; no emite facturas proforma ni marca conciliación contable definitiva.

Scripts: `2026-10-19-001__caja_conciliacion_bancaria.sql` y `2026-10-19-002__caja_conciliacion_bancaria_menu_web.sql`. Aplicados dos veces, sin errores, exclusivamente en `10.8.0.31 / ALFANET2007_PRUEBAS`. Las credenciales no forman parte del código ni de esta documentación.

Como alta operativa adicional, se registró esa conexión en `ALFA_CENTRAL.dbo.bases` con el patrón de `CentralAdminService.CreateBaseAsync`: **IdBase 4283**, nombre **ALFA NET - PRUEBAS**, cliente **112010001** (el mismo que la base original `ALFANET2007`, IdBase 106). Se conservó la conexión original. El `idweb` existente del cliente es `ALFANET`; la ruta de pruebas es `/ALFANET/4283/caja-bancos/conciliacion-bancaria`. Esta alta no forma parte de las migraciones distribuibles del módulo. No se modificaron usuarios ni datos operativos de la base original.

Pruebas: `ConciliacionBancariaTests`. Para integración definir `CONCILIACION_TEST_SQL` y `ALFACORE_TEST_SQL` sobre la base autorizada. Para lectura de extractos locales definir `CONCILIACION_EXTRACTOS` con la carpeta que contiene subcarpetas por banco. Las pruebas del importador crean importaciones propias y las eliminan; la prueba de cobranza escribe recibos/asientos/aplicaciones y un talonario de ensayo dentro de una transacción que siempre revierte. Puede consumir identities de SQL Server; no se reseedean.

Manual: [uso de conciliación bancaria](../../src/AlfaCore/Docs/manual_conciliacion_bancaria.md).

## Archivos de integración

| Archivo | Cambio |
|---|---|
| `Components/Pages/ConciliacionBancaria.razor` y `.razor.css` | Pantalla de importación y revisión, selección de archivo, banco y cuenta, detalle, confirmación de eliminación, paginación y cancelación por sesión. |
| `Models/ConciliacionBancariaModels.cs` | Contratos propios de la pantalla y resultados del proceso. |
| `Services/ConciliacionExtractoLector.cs` | Detección por contenido y coordinación de los parsers reutilizados. |
| `Services/ConciliacionBancariaService.cs` | Sesión, permiso, importación atómica, deduplicación, consultas y eliminación. |
| `ConciliacionBancaria/Infrastructure/ConciliacionRepository.cs` | Adaptación de clasificación y matching a Dapper con una conexión y transacción. |
| `ConciliacionBancaria/Application/Services/DeterministicClassificationService.cs` | Motor de clasificación existente por reglas. |
| `ConciliacionBancaria/Application/Services/DeterministicMatchingService.cs` | Motor de propuestas existente; tolerancia configurable, identificación pendiente ante clientes ambiguos y advertencia de cobro previo. |
| `ConciliacionBancaria/Infrastructure/Excel/*.cs` | Parsers existentes; correcciones de decimales, fechas relativas de MP y rechazo de fechas inválidas con importe. |
| `ConciliacionBancaria/Domain/*` y `Application/DTOs`, `Application/Abstractions` | Contratos y entidades reutilizados sin dependencia de WinForms. |
| `Program.cs` | Registro de los dos servicios del módulo. |
| `Services/ModuleTopNavPresets.cs` | Navegación de Resumen/Cierre de caja, separada de la navegación propia de Conciliación. |
| `App_Data/updates/2026-10-19-001__caja_conciliacion_bancaria.sql` | Estructura compatible con staging existente, índices, altas de reglas faltantes y clave de tolerancia. |
| `App_Data/updates/2026-10-19-002__caja_conciliacion_bancaria_menu_web.sql` | Entrada de menú propia, permisos legacy y permisos web materializados. |
| `tests/AlfaCore.Tests/ConciliacionBancariaTests.cs` | Pruebas de parsers, detección, importación SQL, rollback, duplicados y flujo de pantalla. |
| `Docs/manual_conciliacion_bancaria.md` | Manual funcional. |

Las rutas de la tabla son relativas a `src/AlfaCore/`, salvo la de pruebas. Se actualizaron además `docs/README.md`, `docs/CONFIGURACION_GLOBAL.md` y `docs/CATALOGO_RUTINAS.md`. No se modificó el proyecto WinForms ni el scraping.

## Evidencia de esta entrega

Las 14 pruebas del módulo pasaron contra la base de pruebas autorizada. El archivo `provincia_2026-04-01 (1).xlsx` de la imagen produce 20 movimientos y total 210.583,31. Los extractos locales más recientes de Provincia, Santander y Mercado Pago también se detectaron y leyeron. Las importaciones sintéticas de las pruebas se eliminaron al finalizar; los históricos existentes se conservaron.

Se verificaron rechazo de archivo renombrado, movimientos superpuestos omitidos, rollback ante un error de persistencia después de la primera fila y ausencia de asientos nuevos. La prueba de pantalla verifica archivo primero, banco detectado, selección de cuenta DB y apertura de revisión desde la acción del header.

AlfaDesign: **Cumple: 11/13** dentro de la validación realizada. Se verificó el contenido renderizado en Chromium a 2048/1440/1024, sin overflow horizontal global. Excepciones: `InputFile` nativo con estilo del módulo; no aplica redimensionamiento de columnas en esta etapa; el filtro operativo es la importación seleccionada. Pendientes de validación visual integral: shell completo en una sesión interactiva real y teclado/foco del diálogo de eliminación (categorías 1 y 9). Se reutilizan ambos componentes compartidos sin cambiar su implementación. No se detectaron regresiones en las pruebas del módulo; las otras funcionalidades del repositorio no se sometieron a una regresión completa.

Las capturas de contenido de validación quedan en `artifacts/conciliacion-preview/`; no representan una sesión completa del shell.

## Búsqueda, clasificación y orden de revisión

La fila de filtros utiliza `AlfaInput`, `AlfaSelect` y `AlfaButton`, con búsqueda por Enter/Buscar, clasificación inmediata y Limpiar. Es una fila compacta visible; no agrega un popover ni infraestructura JavaScript. El banco ya detectado queda interno; la cuenta propuesta permanece editable.

`ConciliacionMovimientoFiltros` transporta importación, búsqueda, clasificación, columna/sentido y página. Los filtros se aplican en SQL antes de `OFFSET/FETCH`, incluyendo el mismo conjunto en el conteo. La búsqueda literal escapa `%`, `_` y `[` y abarca descripción, referencia, cliente, propuesta, banco, categoría, estado, fecha e importes. El orden utiliza una lista compilada de diez columnas, con tipos numéricos/fecha y desempate estable por ID; nunca concatena identificadores enviados por el usuario. Cada encabezado expone `aria-sort`.

Se conservan búsqueda, clasificación y orden al paginar y cambiar el alcance de importaciones. Un archivo recién importado abre su revisión sin búsqueda/clasificación. El cambio de sesión limpia esos criterios junto con los datos. No se necesitan migraciones SQL adicionales.

Archivos modificados en esta mejora: `Components/Pages/ConciliacionBancaria.razor` y `.razor.css`, `Models/ConciliacionBancariaModels.cs`, `Services/ConciliacionBancariaService.cs`, `Services/ConciliacionExtractoLector.cs`, pruebas del módulo y manual funcional. Se reutilizan lectores y clasificador; no cambia la registración.

Validación de esta mejora: 16 pruebas aprobadas en ALFANET2007_PRUEBAS, incluida una importación sintética de 62 movimientos con paginación, clasificación, búsqueda literal y orden por las diez columnas. Se eliminaron esas importaciones al finalizar. Contenido verificado en Chromium a 2048/1440/1024, con filtros visibles y sin overflow horizontal global. Catálogo: 0 errores y 0 advertencias. Se mantiene la salvedad de validación integral del shell y foco del diálogo indicada anteriormente (11/13).

## Corrección de clientes probables en propuestas

Se detectó en la importación 43 que varios alias bancarios tenían `AliasTexto='.'`: el motor identificaba correctamente el código por CUIT pero usaba ese marcador como nombre. El repositorio ahora obtiene `ClientName` desde `VT_CLIENTES` y conserva `AliasText` solo como evidencia de búsqueda. La revisión y el detalle consultan además la razón social oficial por código, con fallback al nombre guardado o al código; no ocultan la identidad cuando falta un nombre válido.

`2026-10-19-003__caja_conciliacion_clientes_propuestas.sql` repara nombres vacíos/punto/guion en propuestas auxiliares con cliente identificado y sustituye esos marcadores en el resumen. Es idempotente y se ejecutó dos veces en ALFANET2007_PRUEBAS. No modifica clientes maestros, importes, asignaciones, aplicaciones ni confianza. Las propuestas existentes de Tires Ensenada, Legion, Legionmat, Productos El Molinero y Hotel Cordillera ya muestran su razón social sin reimportar.

`ContraparteBancaria` centraliza la extracción de CUIT de once dígitos y nombre, eliminando prefijos operativos y segmentos de factura/billetera. Se reutiliza en Santander y en el motor para los demás orígenes. El importe por sí solo no identifica un cliente. Si no hay coincidencia suficiente en Alfa, el movimiento continúa sin identificar.

Archivos modificados: `Application/DTOs/ClientAliasDto.cs`, `Infrastructure/ConciliacionRepository.cs`, `Application/Services/DeterministicMatchingService.cs`, `Infrastructure/Excel/SantanderExcelStatementParser.cs` bajo `ConciliacionBancaria/`, y `Services/ConciliacionBancariaService.cs`. Archivos nuevos: `ConciliacionBancaria/Application/Services/ContraparteBancaria.cs` y el script 003. Se agregaron regresiones en `ConciliacionBancariaTests.cs`.

Validación de la corrección de clientes: 22 pruebas aprobadas, incluida extracción de los textos de la imagen, alias con punto y razón social oficial, lectura de la importación 43 y su detalle. Tras la reparación no quedan propuestas con código asignado y nombre vacío/punto en esa importación. Catálogo sin errores ni advertencias.

## Navegación propia de conciliación

Conciliación mantiene su entrada `D75-CONCILIA-IA` dentro del menú general de Caja y Bancos, pero su menú superior contiene únicamente Conciliación bancaria. Importación y Revisión de movimientos son sus pestañas internas. No comparte el preset superior de Resumen/Cierre de caja; ese preset tampoco agrega el acceso a Conciliación.

La URL y `ModuleKey=caja-bancos` se conservan por pertenecer al área Caja y Bancos; la navegación propia utiliza `IRouteContextService` para conservar empresa/base y mantiene `directo=1`. No requiere cambios de datos ni migración SQL.

Archivos de la corrección: `Components/Pages/ConciliacionBancaria.razor`, `Services/ModuleTopNavPresets.cs` y la regresión de pantalla en `tests/AlfaCore.Tests/ConciliacionBancariaTests.cs`. La regresión comprueba una sola entrada propia, navegación con prefijo ALFANET/4283 y modo directo, y ausencia de Conciliación en el preset de Resumen/Cierre. Esta corrección mantiene la evaluación visual 11/13, con las salvedades de shell integral y foco del diálogo ya registradas.

Validación de la separación del menú: compilación correcta y regresión `PantallaArchivoPrimeroDetectaBancoYAbreRevisionDesdeElHeader` aprobada en ALFANET2007_PRUEBAS, incluyendo navegación propia, prefijo SaaS y `directo=1`. Catálogo sin errores ni advertencias.

## Detalle en ventana flotante

El botón Detalle abre el componente compartido `AlfaDialog` de AlfaDesign, tamaño Lg, sobre la grilla de movimientos. Conserva la descripción, observaciones y propuesta de aplicación existentes; reemplaza el panel que se mostraba debajo de la lista. La cruz, Cerrar, Esc y el fondo cierran el diálogo sin consultar nuevamente la grilla ni modificar filtros, orden o página. El componente compartido administra el foco y su retorno al elemento de origen.

Archivos: `Components/Pages/ConciliacionBancaria.razor`, `Docs/manual_conciliacion_bancaria.md` y la regresión existente de pantalla en `tests/AlfaCore.Tests/ConciliacionBancariaTests.cs`. Sin cambios en servicios, datos ni scripts SQL.

Validación: compilación y regresión de pantalla aprobadas en ALFANET2007_PRUEBAS (apertura con semántica modal, cierre y conservación de grilla/orden). Vista renderizada comprobada con Chromium a 1440, 1024 y 390 px: diálogo visible dentro del viewport y fondo fijo. Esta comprobación visual estática no ejecuta la interacción Blazor ni prueba Esc/foco de extremo a extremo. Catálogo sin errores ni advertencias.

## Cuenta corriente dentro del detalle (2026-10-20)

El diálogo abre en **Cuenta corriente**, con la comparación existente en **Comprobantes**. El componente `ConciliacionCuentaCorrientePanel` permite consultar 30/50/100 movimientos con corte editable (fecha bancaria +15 días inicialmente). Conserva sus filtros al cambiar entre pestañas; al cerrar/cambiar movimiento se cancela la consulta. Cobrar desde el detalle utiliza la selección de la pestaña Comprobantes.

`ConciliacionBancariaService.CuentaCorriente.cs` resuelve el cliente desde la última propuesta vigente del movimiento, con conexión de sesión, permiso del módulo y restricción de unidades de TA_USUARIOS. Lee MV_ASIENTOS con EsResumen=0, igual que el reporte comercial, incluyendo recibos legacy sin exigir cabecera V_MV_CPTE. Calcula el acumulado de toda la historia hasta el corte antes de tomar los últimos N renglones, con partición por moneda y orden contable estable.

Los candidatos CB/CBFP/CBCT de secuencia 1 utilizan el criterio del aviso existente (cliente, importe con diferencia <=0,01, fecha ±15 días). Se muestran aparte del límite/corte de la grilla y se identifican como posibles coincidencias: no se confirma banco, moneda o aplicación por ese criterio. No se modifica el motor ni el estado del movimiento.

El detalle valida nuevamente el cliente y las unidades autorizadas mediante la clave contable compuesta (período, mes operativo, número de asiento, tipo de registro y secuencia). Consulta contrapartidas del mismo asiento y unidad, cuentas/medios de pago desde MA_CUENTAS, comentarios opcionales de V_MV_CPTE y aplicaciones de MV_APLICACION al comprobante de origen. Normaliza unidades NULL/vacías para que los recibos legacy sean visibles sin depender de cabeceras comerciales. Una aplicación legacy sin unidad solo se vincula si el cliente/TC/número no existe en otra unidad contable; los datos de la factura origen conservan su propia restricción de unidad. Las aplicaciones ausentes no se infieren.

Archivos: modelos `ConciliacionCuentaCorrienteModels.cs`, servicio parcial y panel compartido con CSS aislado; integración en `ConciliacionBancaria.razor`. Lectura solamente; sin migraciones. Errores por el ejecutor centralizado del módulo (AUX_ERR) y feedback inline.

Validación: compilación correcta; 28 pruebas de conciliación aprobadas en ALFANET2007_PRUEBAS, sin omisiones. La regresión nueva verifica el movimiento 548 (KARPYZA), CB 000100007539X del 10/10/2026, contrapartida Santander Daniel y aplicación a FC 000200003771A; además contrasta saldos con una suma independiente de toda la historia, el corte, los límites 30/50/100 y el rechazo de claves/clientes/unidades no autorizados. La prueba de esta consulta es de lectura; las pruebas previas de grabación conservan sus transacciones de rollback y limpieza de importaciones propias.

AlfaDesign checklist — **Cumple: 11/13** dentro del alcance comprobado. Render real de AlfaDialog con el panel y datos de pruebas, y comparación de facturas, revisados en Chromium a 2048/1440/1024/390 px: ventanas dentro del viewport, tablas con scroll interno y sin overflow horizontal global. Excepciones: no aplica redimensionamiento de columnas en este detalle; se conserva InputFile nativo en importación. Deuda de validación: shell completo y teclado/foco en una sesión Blazor autenticada de extremo a extremo (categorías 1 y 9), no cubiertos por el render estático. Regresiones: ninguna detectada en las 28 pruebas del módulo; no se ejecutó la suite completa del repositorio. Legacy restante: se consulta el circuito contable existente de Alfa Gestión sin migrarlo ni modificarlo. Capturas locales en artifacts/conciliacion-preview, excluidas del repositorio.

## Comparación y vínculos de conciliación (2026-10-20)

Se incorpora **Conciliar cuenta** como tercera pestaña del mismo módulo, sin nuevas opciones de menú. Esta entrega implementa la primera etapa acordada: comparar banco contra contabilidad, confirmar vínculos y desconciliar con auditoría. La generación de asientos por concepto y el cierre/control integral de saldos quedan para las siguientes etapas; Tarjetas de Crédito será un módulo posterior (cuentas MedioDePago TJ/TJT).

### Fuente, alcance y seguridad

La comparación utiliza los extractos BK asociados a la cuenta bancaria y MV_ASIENTOS de esa misma cuenta. Se habilitan cuentas activas de MA_CUENTAS con MedioDePago DB y moneda 1 (pesos); los extractos actuales se identifican como ARS. No se convierten monedas. Otras monedas contables pueden verse en la lista con su código, pero no son seleccionables ni entran en el total en pesos.

MV_ASIENTOS es la fuente contable oficial: se excluyen EsResumen, aperturas y cierres de la lista de movimientos conciliables. Se aplican las unidades de TA_USUARIOS/AdminCentral a los registros de Alfa; los extractos importados no tienen unidad y se consultan por permiso del módulo y cuenta. Por eso los totales son movimientos importados frente a movimientos contables autorizados, sin asumir cobertura bancaria completa ni saldo inicial o cierre de período. La diferencia no demuestra por sí sola que la cuenta esté conciliada.

Se respetan tanto MV_ASIENTOS.Conciliado como los detalles conciliados de MV_CONCILIACION. Las marcas legacy se muestran como conciliadas en Alfa Gestión y no se reutilizan ni se deshacen desde AlfaCore. Los vínculos nuevos marcan exclusivamente Conciliado en el renglón bancario de MV_ASIENTOS, sin cambiar importe, fecha, comprobante o registración comercial. Los triggers y cierres existentes continúan vigentes.

### Propuestas y confirmación

Filtros de cuenta, período (hasta 367 días), pendiente/conciliado/todos y texto literal. Cada lista pagina en SQL con 50 registros y navegación propia, para permitir comparar páginas diferentes; es una excepción deliberada al paginador único del Context Toolbar porque existen dos colecciones independientes. La selección se conserva entre páginas y se limpia tras una consulta nueva o actualización exitosa. Con filtros modificados, las selecciones y confirmación quedan bloqueadas hasta aplicar.

Se propone un candidato por cada movimiento bancario visible: misma cuenta, pesos, importe firmado exacto y fecha próxima. El margen lo define TA_CONFIGURACION, clave CONCILIACION-DIAS-BANCO (inicial 3; 0..31). Se prioriza referencia bancaria exacta y luego cercanía de fecha; se informa la cantidad de candidatos. No hay confirmaciones automáticas, aunque solo exista uno. El candidato puede pertenecer a otra página de Alfa y aparece completo en la confirmación.

Se admiten vínculos 1:1, 1:N, N:1 y N:M de movimientos completos (hasta 100 por lado). Deben tener el mismo sentido, total distinto de cero e igualdad exacta a cuatro decimales. No se toleran diferencias, no se compensan débitos/créditos y no se generan ajustes ni asignaciones parciales dentro de un renglón.

La confirmación relee y valida ambas selecciones en servidor. Compara huellas de fecha, importe, cuenta/moneda, referencia, detalle y datos del comprobante/unidad contable; rechaza cambios, marcas previas y registros no autorizados. Utiliza transacción Serializable, bloqueo transaccional por cuenta, índices únicos de vínculos activos e idempotencia por operación/solicitud. La conexión siempre es la de la sesión autorizada; los errores se registran mediante IAppEventService/AUX_ERR.

### Historial y desconciliación

La actualización 2026-10-20-001__caja_conciliacion_vinculos.sql agrega BK_Conciliacion, BK_ConciliacionBanco y BK_ConciliacionAlfa. Conservan cabecera, clave contable compuesta, copias de los movimientos, huellas, usuario y fecha. La desconciliación libera el grupo entero, exige motivo de 3..300 caracteres y guarda usuario/fecha/motivo, sin borrar registros. El historial del movimiento bancario permite consultar vínculos activos y anteriores.

No se libera una marca si el asiento desapareció, cambió o tiene un vínculo legacy conciliado. Los cambios detectados se señalan en la comparación; requieren revisión. Una importación con historial de conciliación no puede eliminarse, incluso después de desconciliar. Un banco ya vinculado no puede generar otra cobranza desde el circuito rápido.

**Registración** y **Conciliación** son columnas separadas en Revisión de movimientos. Cobrado no implica Conciliado; confirmar un vínculo con una cobranza existente no genera una CB nueva.

### Archivos

- Services/ConciliacionBancariaService.Vinculos.cs: consultas, sugerencias, confirmación, historial y desconciliación; núcleos internos aceptan transacción para pruebas con rollback y futura coordinación de registraciones.
- Models/ConciliacionVinculosModels.cs: filtros, filas, selecciones y auditoría.
- Components/Shared/ConciliacionComparacionPanel.razor y CSS: filtros, dos listas, selección, confirmación e historial con controles AlfaDesign.
- Components/Pages/ConciliacionBancaria.razor, Models/ConciliacionBancariaModels.cs y Services/ConciliacionBancariaService.cs: tercera pestaña, estado separado, protección del historial y errores funcionales.
- Services/CobranzasService.cs: impide cobrar nuevamente un movimiento conciliado.
- App_Data/updates/2026-10-20-001__caja_conciliacion_vinculos.sql: actualización idempotente, aplicada dos veces en ALFANET2007_PRUEBAS; no cambia menús ni datos legacy.
- tests/AlfaCore.Tests/ConciliacionVinculosTests.cs: comparación real de lectura y pruebas de persistencia en transacción con rollback.

Compatibilidad: la base de pruebas conserva compatibility_level=100. Se usan parámetros XML para las claves compuestas y validación de configuración en .NET, sin OPENJSON/TRY_CONVERT ni cambios de compatibilidad.

### Validación de esta etapa

Compilación correcta y 30 pruebas de conciliación aprobadas, sin omisiones, sobre ALFANET2007_PRUEBAS. Incluyen las 28 regresiones de importación/cobranzas/cuenta corriente y dos pruebas nuevas de comparación/vínculos. Los ensayos de persistencia crean importación y renglones propios dentro de un ejercicio válido, ejecutan el mismo núcleo transaccional del servicio y revierten la transacción completa. Verifican selección múltiple, igualdad de importes, duplicados, huellas, idempotencia, marcas previas, cambios posteriores, desconciliación auditada, nueva conciliación tras anulación, propuestas ambiguas, egresos, rechazo de moneda extranjera, filtros, autorización y rollback de las filas creadas. Las identidades pueden avanzar por el rollback; no quedan conciliaciones de ensayo confirmadas.

La prueba de lectura invoca los handlers de la pantalla para comparar, revisar una propuesta y abrir el historial; no confirma datos reales. El script de actualización final se ejecutó dos veces en pruebas sin errores. Catálogo: 101 rutinas, cero errores/advertencias. git diff --check sin errores en los cambios y archivos fuente UTF-8 sin BOM.

**AlfaDesign checklist — Cumple: 11/13** dentro del alcance validado. Comparación y ventana de confirmación comprobadas con Chromium a 2048/1440/1024/390 px, sin overflow horizontal global y con scroll de tablas. Excepciones: paginación independiente en el pie de cada colección; sin redimensionamiento de columnas en esta etapa. Deuda de validación: shell completo y foco/teclado en una sesión Blazor autenticada de extremo a extremo (categorías 1 y 9); el render estático y los handlers no acreditan esa integración completa. Legacy restante: datos, marcas y cierres existentes, respetados por el servicio y triggers. No se detectaron regresiones en las 30 pruebas del módulo; no se ejecutó la suite completa del repositorio.

## Reconocer cobros existentes (20/10/2026)

Se reemplaza Cobrar por Marcar cobrado en propuestas CobranzaYaRegistrada. La acción abre el detalle de cuenta corriente; el recibo se selecciona allí y requiere confirmación explícita antes de marcar Cobrado. No genera registros comerciales/contables, ni modifica marcas o vínculos de conciliación bancaria. La cuenta bancaria puede diferir y se mantiene visible en las contrapartidas para revisión posterior.

Services/ConciliacionBancariaService.CobroExistente.cs relee el movimiento con bloqueo y el recibo autorizado mediante la clave completa de MV_ASIENTOS, dentro de una transacción Serializable. Resuelve el cliente desde la propuesta vigente; exige clasificación Cobranza, ARS/moneda 1, crédito del cliente, importe exacto y fecha ±15 días. Valida el importe y comprobante vistos en la pantalla para rechazar cambios. La actualización 2026-10-20-002__caja_cobro_existente.sql crea BK_CobroExistente con recibo, importe, usuario, fecha y unicidad por movimiento y clave del recibo. Repetir la misma confirmación es idempotente; no se permite reutilizar un recibo ya reconocido o generado para otro movimiento. Se audita además COBRO_EXISTENTE en BK_GeneracionAlfaLog, con lo que la eliminación del extracto queda protegida.

Components/Shared/ConciliacionCuentaCorrientePanel.razor agrega la confirmación, acción y callback de actualización. Components/Pages/ConciliacionBancaria.razor actualiza Registración sin cerrar el detalle ni modificar Conciliación. Models/ConciliacionBancariaModels.cs y Services/ConciliacionBancariaService.cs incorporan TipoMatch para decidir la acción por su tipo real. Las categorías y reglas no cambian. Tests en ConciliacionCobroExistenteTests.cs y regresión del render del detalle en ConciliacionBancariaTests.cs; operaciones de prueba con rollback.

Validación del reconocimiento: 31 pruebas de conciliación aprobadas sin omisiones, incluyendo reconocimiento de un recibo legacy con rollback, idempotencia, rechazo de importe/comprobante alterado, reutilización del recibo y usuario no autorizado. La prueba no marca como cobrado el movimiento real 548; usa copias transaccionales y comprueba que no crece MV_ASIENTOS ni se crean vínculos de conciliación. Actualización 002 aplicada dos veces en ALFANET2007_PRUEBAS. Detalle y comparación revisados en Chromium a 2048/1440/1024/390 sin overflow global. Se mantiene AlfaDesign 11/13 con las mismas limitaciones de validación interactiva integral de shell/foco documentadas anteriormente.

## Reconocimiento con diferencias y acceso desde cualquier cobranza pendiente

Marcar como cobrado se ofrece en todas las filas pendientes clasificadas como Cobranza con importe positivo, independientemente del TipoMatch o del estado de conciliación. Cobrar sigue disponible como acción separada para generar una CB cuando corresponda y conserva la validación de deuda vigente. No tener deuda no demuestra por sí solo que haya una cobranza registrada.

Caso relevado: movimiento 559, Productos El Molinero, banco 389.332,86 el 08/10/2026; CB 000100007531X del 10/10/2026 por 397.428,87. Su asiento tiene banco por el mismo importe del recibo, sin renglones de retenciones que expliquen los 8.096,01. Por eso la advertencia anterior por importe exacto no lo identificaba y la propuesta importada quedó como Anticipo. No se infiere la causa de la diferencia ni se corrige el asiento.

La cuenta corriente muestra recibos cercanos del cliente en pesos, priorizando los importes exactos; identifica Importe distinto en los restantes. La confirmación de un recibo diferente exige observación de 3..300 caracteres y conserva el checkbox explícito. El servicio valida el importe y comprobante vistos, cliente, moneda, tipo/fecha/clave/autorización, unicidad y observación. La actualización 2026-10-20-003 agrega ImporteBanco y Observacion a BK_CobroExistente, dejando Importe como total del recibo. La acción solo cambia Registración a Cobrado; no genera una CB, retenciones, asiento o conciliación por esa diferencia.

Archivos: ConciliacionBancaria.razor (acciones), ConciliacionCuentaCorrientePanel.razor (revisión/observación), ConciliacionCuentaCorrienteModels.cs (importe bancario y recibo cercano), ConciliacionBancariaService.CuentaCorriente.cs (candidatos), ConciliacionBancariaService.CobroExistente.cs (validación y auditoría), actualización 003 y las regresiones existentes de reconocimiento/pantalla. El criterio del motor de propuestas y la clasificación permanecen iguales; la confirmación es manual.

Validación de diferencias: 31 pruebas del módulo aprobadas sin omisiones. El ensayo de reconocimiento clona también los renglones del recibo dentro de la transacción para no depender de si el usuario ya reconoció el recibo real; revierte tanto copias bancarias como contables al terminar. Se comprueba rechazo sin observación y reconocimiento con observación, conservando banco 389.332,86 y recibo 397.428,87 por separado, sin nuevos asientos por la acción ni conciliación. El render de Molinero verifica importes, observación obligatoria y botón de confirmación; Chromium revisado a 2048/1440/1024/390 sin overflow global. AlfaDesign: 11/13, con validación interactiva de shell/foco aún pendiente. Actualización 003 aplicada dos veces en la base autorizada de pruebas; catálogo sin errores/advertencias.

## Mercado Pago con archivos renombrados y fechas relativas

El archivo C:/dev/Extractos/MercadoPago/mercadopago 102026.xlsx contiene Hoy y días escritos sin año. Sus propiedades de creación indican 19/10/2026; el nombre solo contiene mes/año. El lector anterior exigía una fecha ISO en el nombre, lo que generaba un error y hacía aparecer incorrectamente el selector de banco.

ConciliacionArchivo admite FechaReferencia opcional. ConciliacionLectura puede solicitarla como estado esperado, conservando MercadoPago como banco detectado, sin movimientos procesados ni importación habilitada. ConciliacionExtractoLector detecta fechas relativas/sin año en la columna Fecha y propone Properties.Created si es informativa (año >=2000), para confirmación explícita. No usa la fecha actual, el mes del nombre ni una fecha de modificación como prueba del día de descarga. Los nombres con fecha ISO siguen funcionando automáticamente; las fechas completas no requieren referencia. Una fecha elegida por el usuario prevalece sobre el nombre.

La página muestra Fecha de referencia de la descarga y Procesar archivo. Cambiar la fecha invalida la lectura previa y la disponibilidad de Importar. El servidor relee con esa misma referencia al importar, rechaza archivos aún pendientes de confirmación y guarda la referencia manual en BK_ImportacionExtracto.Observaciones. Se mantienen hash de archivo y huellas de movimientos para evitar duplicados. Se cancela y limpia la referencia al cambiar archivo/sesión. No hay migración SQL ni cambios en el archivo original.

Archivos: Models/ConciliacionBancariaModels.cs (contrato), Services/ConciliacionExtractoLector.cs (estado esperado y sugerencia), Infrastructure/Excel/MercadoPagoExcelStatementParser.cs bajo ConciliacionBancaria (detección compartida de fechas relativas), Components/Pages/ConciliacionBancaria.razor (campo/confirmación y mensajes), Services/ConciliacionBancariaService.cs (revalidación/auditoría). Pruebas ampliadas en ConciliacionBancariaTests.cs para creación sugerida, confirmación/corrección, cambio de año, fechas absolutas, flujo de pantalla y archivo real sin renombrar. La prueba del archivo real solo lo lee; no lo importa en la base.

Validación de fechas relativas: compilación y 33 pruebas de conciliación aprobadas sin omisiones. El archivo real renombrado se reconoce como MercadoPago, propone 19/10/2026 desde sus propiedades y, tras confirmarla, produce 49 movimientos del 01 al 19/10/2026 con neto -128.534,00. Se verifica cambio de año, referencia elegida/corregida, fechas absolutas y bloqueo de importación antes de confirmar/reprocesar. Flujo de referencia renderizado en Chromium a 2048/1440/1024/390 sin overflow global; AlfaDesign 11/13 con las mismas limitaciones de validación interactiva integral del shell/foco. No se importó el archivo real en la base. Catálogo sin errores ni advertencias.


## Asignación manual de cliente y memoria bancaria (20/10/2026)

Se agrega `ConciliacionBancariaService.Clientes.cs`: búsqueda acotada (30 resultados, mínimo dos caracteres) por código, razón social o documento desde VT_CLIENTES, con exclusión de bajas/títulos y escapes LIKE; asignación transaccional de ingresos clasificados Cobranza aún no cobrados ni conciliados, con sesión/permisos y usuario válido. El cliente seleccionado se valida en el servidor. Los errores se registran mediante Ejecutar/IAppEventService en AUX_ERR.

Asignar inserta una clasificación versionada CLIENTE_MANUAL con código/nombre, invalida las propuestas anteriores sin borrar su historia, regenera solo el movimiento y audita usuario/memoria en BK_GeneracionAlfaLog (ASIGNAR_CLIENTE). CobranzaCandidateDto transporta la elección explícita, que tiene prioridad sobre las inferencias y aliases. No genera asientos, recibos ni aplicaciones contables.

La opción Recordar usa BK_AliasClienteBanco existente: alias de 5 a 200 caracteres, con letras, presente en la descripción o contraparte y sin los conceptos genéricos de transferencia/pago. Rechaza un alias normalizado activo ya asociado a otro cliente; guardar otra vez el mismo alias/cliente no lo duplica. Permite asignar otro cliente solo al movimiento desmarcando Recordar, sin cambiar la memoria compartida. Los aliases se conservan en la base activa, sirven entre bancos y usuarios y no dependen del importe. No hay migración SQL ni parámetros nuevos.

El motor ahora busca aliases también en la descripción cuando ContraparteNombre está vacío, caso habitual de Mercado Pago. Una descripción con aliases de distintos clientes queda ambigua y sin asignación automática. El repositorio obtiene únicamente aliases de clientes activos y no títulos. Las propuestas de otros movimientos ya importados no se reescriben; las futuras importaciones toman la asociación.

La pantalla reutiliza AlfaLookup, AlfaCheckbox, AlfaInput y AlfaButton. Un ingreso sin cliente ofrece Asignar cliente en la fila; en el detalle se muestra el nombre bancario editable, la opción Recordar y el cliente elegido. Guardar refresca propuestas, comprobantes y panel de cuenta corriente sin cerrar el diálogo. El estado se limpia al cambiar sesión. Se conservan correcciones ante errores.

Archivos: Services/ConciliacionBancariaService.Clientes.cs (nuevo), ConciliacionBancaria/Application/DTOs/CobranzaCandidateDto.cs, Infrastructure/ConciliacionRepository.cs y Application/Services/DeterministicMatchingService.cs (elección explícita y memoria), Components/Pages/ConciliacionBancaria.razor y su CSS (interfaz), tests/AlfaCore.Tests/ConciliacionClientesTests.cs (nuevo) y ampliación de ConciliacionBancariaTests.cs (render).


La eliminación de importaciones considera ASIGNAR_CLIENTE como una revisión (igual que RECLASIFICAR), sin bloquearla por ese motivo; se mantiene la prohibición cuando hay registraciones contables o historial de conciliación. Los aliases recordados sobreviven a eliminar la importación y permanecen como memoria operativa.

Validación: compilación correcta y 43 pruebas de conciliación aprobadas, ninguna omitida. Las nuevas pruebas cubren descripción sin contraparte, prioridad manual, aliases ambiguos, búsqueda con escapes, guardar sin duplicar, recuerdo en una segunda importación, asignación individual sin cambiar memoria, conflicto con otro cliente, cliente inexistente, concepto genérico y movimiento ya cobrado. El ensayo público en ALFANET2007_PRUEBAS confirma que no cambia MV_ASIENTOS y elimina todas sus importaciones y aliases sintéticos al finalizar. No se asignaron clientes a movimientos operativos reales.

Chromium con HTML real del componente y CSS del módulo a 2048/1440/1024/390 px: buscador habilitado, Recordar marcado, alias editable, Asignar deshabilitado sin selección y Cerrar visible; modal dentro del viewport, sin overflow global. UTF-8 sin BOM verificado en los doce archivos. Catálogo validado (101 rutinas, cero errores/advertencias). AlfaDesign: Cumple 11/13; continúan pendientes el shell autenticado completo y el recorrido integrado de teclado/foco con Blazor. No se incorporaron componentes legacy ni deuda visual nueva. No equivale a una prueba de extremo a extremo en una sesión de usuario.


## Selección de cliente y titulares con CUIT compartido (20/10/2026)

AlfaLookup identifica las opciones por ItemKey y ejecuta selección en mousedown, antes de focusout, conservando click/Enter como alternativas y evitando notificaciones duplicadas. Cancela búsquedas pendientes al seleccionar y respeta Disabled. La página deja la invalidación de selección al propio lookup y, al recibir el cliente, muestra código y razón social en el campo; se prueba el callback real desde el componente compartido al estado de la página.

ContraparteBancaria.NormalizarCuit elimina cualquier carácter distinto de los dígitos ASCII. Para buscar por documento, se usa un patrón LIKE formado solo por los dígitos en orden como prefiltro SQL, compatible con nivel 100; el repositorio aplica luego igualdad exacta de CUIT normalizado para resolver identidad. La búsqueda manual permite documentos parciales con esos separadores. Se usan VT_CLIENTES, cuentas activas e imputables; no se alteran documentos fiscales de los maestros.

La resolución automática agrupa todos los clientes candidatos que comparten CUIT, también cuando un alias/nombre apunta inicialmente a una sola cuenta. ClientAliasDto distingue CUIT del pagador y ClientTaxId del cliente real. Para varias cuentas, se consultan saldos pendientes en VE_CPTES_SALDOS_VENTAS y se propone solo una cuenta con coincidencia individual de saldo/importe bancario dentro de CONCILIACION-TOLERANCIA. Si no hay coincidencias o las hay en más de una cuenta, deja el cliente sin resolver y explica la ambigüedad. No busca globalmente por importe, no desempata por orden y no da prioridad al alias sobre el grupo fiscal. Una asignación manual explícita sigue teniendo prioridad. No se interpreta una pequeña diferencia como retención durante este desempate.

Recordar admite el mismo alias en varias cuentas cuando sus CUIT oficiales normalizados coinciden y tienen once dígitos, manteniendo la prohibición entre titulares distintos. No reemplaza asociaciones previas y no duplica la combinación alias/cliente. La futura resolución evalúa las cuentas del titular por importe, en lugar de fijar siempre la última seleccionada.

ReevaluarPropuestaAsync permite actualizar un movimiento pendiente ya importado: valida usuario/sesión, clasificación Cobranza/importe positivo y ausencia de cobranza/conciliación bajo bloqueo; invalida propuestas anteriores conservando historial y regenera solo ese movimiento. Audita REEVALUAR_PROPUESTA. La elección manual se conserva. Se reutilizan validación y generación con AsignarClienteAsync; la tolerancia usa VALOR o ValorAux. Eliminar admite esta acción de revisión, sin liberar las guardias de registración ni de conciliación. El detalle refresca cliente, tipo, confianza, comprobantes y cuenta corriente sin cerrarse.

Archivos: Components/Shared/AlfaDesign/AlfaLookup.razor; Components/Pages/ConciliacionBancaria.razor; Services/ConciliacionBancariaService.Clientes.cs y ConciliacionBancariaService.cs; ConciliacionBancaria/Application/Services/ContraparteBancaria.cs y DeterministicMatchingService.cs; DTOs/ClientAliasDto.cs; Infrastructure/ConciliacionRepository.cs; pruebas ConciliacionClientesTests.cs y ConciliacionBancariaTests.cs, manual y catálogo. No requiere actualización SQL ni modifica movimientos operativos existentes automáticamente.


La prueba con Blazor activo reprodujo un cierre pendiente de focusout que podía ocultar una nueva búsqueda después de limpiar. Se corrige comprobando el foco actual dentro del lookup mediante AlfaLookupPopover.containsFocus antes de cerrar, con guardia de componente dispuesto/desconexión. Se actualiza la versión del script en Components/App.razor para invalidar caché. Archivos adicionales: wwwroot/js/alfa-lookup-popover.js y Components/App.razor.

Validación final: compilación y 53 pruebas de conciliación aprobadas sin omisiones. Incluye CUIT normalizado con cualquier separador, lectura real de las cuentas 112012764/112010786 del mismo titular en ALFANET2007_PRUEBAS, selección por importe para una u otra cuenta, ausencia/duplicidad de coincidencias, alias que no fija una sola empresa, memoria compartida entre cuentas con el mismo CUIT, callback de selección a la página, reevaluación individual y preservación de asignación manual. Los ensayos crean y retiran exclusivamente importaciones/aliases sintéticos; no modifican cobranzas operativas.

Prueba interactiva local del componente compartido dentro de AlfaDialog, con circuito Blazor Server y datos de selección simulados: Chromium a 2048/1440/1024/390 px verifica selección por mousedown antes de mouseup, una única notificación, campo con código/nombre, botón habilitado, limpiar, nueva búsqueda y selección con Enter. Todo aprobado. La revisión estática del detalle completo con CSS real también pasa en esos tamaños, sin overflow global. El host de ensayo se detuvo al terminar. Los artefactos de prueba están excluidos del repositorio. No equivale al recorrido completo del módulo con una sesión autenticada del usuario.

UTF-8 sin BOM y ausencia de mojibake verificados en los quince archivos de esta corrección; catálogo validado (101 rutinas, cero errores/advertencias). AlfaDesign: Cumple 11/13 para el módulo completo; se añade evidencia real de teclado/foco para el lookup, pero siguen pendientes el shell autenticado y el recorrido integrado de todo el módulo. Sin deuda visual nueva identificada.


## Provincia con cabecera en fila 6 y formatos XLS/CSV (20/10/2026)

El archivo real C:/dev/Extractos/MercadoPago/Provincia 102026.xlsx tiene metadata y cabecera en la fila 6 (Fecha, Descripción, Importe, Saldo), sin Número Secuencia. El parser solo buscaba hasta la fila 5 y el detector dependía de la secuencia/título del banco. Se amplía la búsqueda de cabecera Provincia a 40 filas y se reconoce esa variante por su tabla y las señales Detalle de Movimientos y Cuenta: en las primeras cinco filas. Los formatos explícitos y títulos de los otros bancos tienen prioridad; las cuatro columnas genéricas solas no alcanzan para decidir Provincia.

Se agrega ConciliacionArchivoTabular para normalizar XLS binario y CSV a un libro temporal compatible con los parsers existentes. XLSX conserva su lectura original con ClosedXML. Se incorpora la dependencia ExcelDataReader 3.9.0 porque el proyecto no tenía lector BIFF; también aporta CSV con delimitador automático (; , tabulación |), comillas, campos multilínea y fallback Windows-1252 cuando no es UTF-8. No se agrega DataSet ni una segunda lógica de interpretación bancaria. Referencia primaria: [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader). Los temporales privados se eliminan al terminar y el archivo fuente se mantiene intacto.

Los números de XLS se conservan numéricos; las referencias CSV se mantienen como texto, incluidos ceros iniciales. Las fechas nativas de Excel se leen directamente en Provincia/base y Santander, para no intercambiar día/mes por cultura. Un importe o saldo textual inválido en el parser base aborta la lectura con fila y mensaje; no desaparece como importe cero. Los archivos convertidos no reciben fecha de descarga ficticia: Created=1970 en el libro intermedio, por lo que Mercado Pago solicita referencia si contiene fechas relativas y no hay una fecha ISO en el nombre ni una referencia confirmada.

La pantalla admite .xlsx/.xls/.csv y conserva el límite de 20 MB. La selección de formato se muestra solo si la detección resulta incierta y se oculta tras una lectura correcta, incluso si el usuario tuvo que elegirlo manualmente. Se denomina Formato del extracto para distinguirlo de la cuenta contable, que sigue siendo una MA_CUENTAS activa con MedioDePago=DB. No cambia grabación de cobranzas, tablas ni menús.

Archivos nuevos: Services/ConciliacionArchivoTabular.cs y tests/AlfaCore.Tests/ConciliacionFormatoTests.cs. Modificados: AlfaCore.csproj (dependencia); Services/ConciliacionExtractoLector.cs (formatos y detección); ConciliacionBancaria/Infrastructure/Excel/ProvinciaExcelStatementParser.cs (cabecera), BaseExcelStatementParser.cs (fechas nativas y validación) y SantanderExcelStatementParser.cs (fecha nativa); Components/Pages/ConciliacionBancaria.razor (archivo y selector); pruebas ConciliacionBancariaTests.cs, manual y catálogo.

Los archivos reales Provincia 102026.xls y Provincia 102026.xlsx se usan solo para lectura: ambos producen 34 movimientos, total -729.577,61, con iguales fechas, descripciones, importes y saldos. No se importaron a la base durante esta tarea.


Validación final: compilación correcta y 65 pruebas de conciliación aprobadas, ninguna omitida. Las nuevas pruebas cubren los dos archivos reales Provincia sin escribirlos/importarlos, CSV separado por coma/punto y coma/tabulación, UTF-8/Windows-1252, comillas y campos multilínea, acentos y centavos, referencias con ceros iniciales, fechas nativas sin inversión de día/mes, Mercado Pago CSV con referencia explícita, Santander CSV, rechazo de importes inválidos/extensiones no admitidas y formato genérico sin inferencia falsa de banco. La prueba de pantalla verifica que elegir un formato desconocido lo oculta al terminar correctamente y que el archivo real Provincia muestra 34 movimientos y solo la cuenta contable. Se mantiene la suite SQL exclusivamente en ALFANET2007_PRUEBAS con limpieza de sus datos sintéticos.

Chromium con HTML real del componente y CSS del proyecto, a 2048/1440/1024/390 px: campo Cuenta bancaria visible, selector Formato ausente, accept=.xlsx,.xls,.csv, resumen de 34 movimientos y -729.577,61 correcto; sin overflow horizontal global. UTF-8 sin BOM comprobado en doce archivos y catálogo con 101 rutinas, cero errores/advertencias. Persisten avisos de compilación existentes y de la dependencia previa System.Security.Cryptography.Pkcs 8.0.1; no son de ExcelDataReader. AlfaDesign: Cumple 11/13; se conserva la deuda de validación del shell autenticado y recorrido integrado de teclado/foco del módulo completo. No se incorporaron componentes legacy ni cambios de navegación. La revisión estática no reemplaza una sesión Blazor autenticada completa.


## Sincronización con main remoto (20/10/2026)

La integración con los cambios de Conversaciones detectó dos scripts con versión 2026-10-19-001. Se conserva la secuencia 001 a 004 de Caja del 19/10 por sus dependencias y se renumera encuestas de satisfacción a 2026-10-20-004, posterior a los scripts de conciliación del 20/10. El SQL de encuestas no se modifica. ActualizacionesService reconoce el nombre anterior como alias solo cuando existe el archivo nuevo con SQL idéntico, conservando compatibilidad con publicaciones incrementales; se actualiza la referencia en ConversacionesEncuestaSatisfaccionTests. No se ejecutan migraciones ni se modifican versiones guardadas en bases durante esta sincronización.

Validación de sincronización: compilación correcta, 43 pruebas enfocadas de versiones SQL, errores del actualizador, encuestas y formatos de extractos aprobadas sin omisiones; catálogo válido y diff sin errores de whitespace.

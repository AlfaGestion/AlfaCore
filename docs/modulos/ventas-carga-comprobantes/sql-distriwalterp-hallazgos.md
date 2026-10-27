# Relevamiento SQL — DISTRIWALTERP

Fecha: 2026-10-14. Servidor: `10.8.0.10`. Base: `DISTRIWALTERP`.

## Alcance ejecutado

Se consultaron catálogos, definiciones SQL, una lista cerrada de configuraciones comerciales, tipos de comprobante y una muestra limitada de pedidos. No se ejecutaron SP comerciales, DDL, altas, modificaciones o anulaciones. No se realizó ninguna emisión fiscal. La conexión recibida se usó para lectura; esto no confirma que la base sea una copia aislada apta para pruebas de escritura.

Las credenciales no se guardaron en archivos. La evidencia exportada no contiene contraseñas, cadenas de conexión, nombres de clientes, identificadores de comprobantes ni los textos completos de las definiciones instaladas.

| Evidencia | Contenido |
|---|---|
| [Metadatos](sql-distriwalterp-metadatos.json) | Tipos, campos, PK, índices, FK, defaults, triggers y parámetros |
| [Contexto](sql-distriwalterp-contexto.json) | Configuraciones comerciales consultadas, talonarios y resúmenes sin documentos identificables |
| [Definiciones](sql-distriwalterp-definiciones.json) | Huellas SHA-256 e identificación de 36 módulos leídos |

El snapshot inicial cubre 44 nombres solicitados: 43 presentes (36 tablas, 6 procedimientos y 1 vista) y un procedimiento ausente. Incluye 1.357 campos, 138 índices distintos, 51 FK distintas y 40 triggers activos en los objetos relevados. Son cifras del conjunto seleccionado, no un inventario de toda la base. Los 36 módulos leídos también incluyen dependencias encontradas en una segunda consulta.

El motor informó `13.0.4001.0`, compatibilidad `100` y collation `Latin1_General_CI_AI`. El usuario de conexión tiene visibilidad de definiciones en esta base. Ninguna escritura o rendimiento comercial ha sido probado.

## Tipos y claves confirmados

| Objeto | Tipo confirmado | Identidad relevante |
|---|---|---|
| `V_MV_CPTE` | Tabla | PK `TC + IDCOMPROBANTE + IDCOMPLEMENTO`; `ID` tiene índice único adicional |
| `V_MV_CPTEINSUMOS` | Tabla | PK `TC + IDCOMPROBANTE + IDCOMPLEMENTO + IDARTICULO + SECUENCIA` |
| `MV_APLICACION` | Tabla | PK comercial compuesta, con documento destino, origen, cuenta, complemento y secuencia |
| `V_MV_STOCK` | Tabla | Movimiento de stock; existen triggers de fechas, equivalencias, depósito y validación |
| `V_TA_CPTE` | Tabla | **PK `CODIGO + Talonario`**, no solo `CODIGO` |
| `VT_CLIENTES` | Vista | Fuente oficial de clientes, con dependencias en cuentas y datos adicionales |
| `V_TA_INPUT_VENTAS` | Tabla | Configuración de imputaciones por motivo/condición; no es metadata visual de inputs |
| `V_MA_ARTICULOS`, `V_MA_PRECIOS`, `V_MA_PRECIOSCAB` | Tablas | Maestros/precios reales; el prefijo `V_` no significa vista |

Los anchos del catálogo están expresados en bytes. Por ejemplo, `nvarchar` de 26 bytes representa 13 caracteres. `V_MV_CPTE.IDCOMPROBANTE` y el detalle usan 13 caracteres, mientras algunos campos de aplicaciones/origen admiten 15. No uniformar anchos sin conservar el formato comercial y el complemento correspondiente.

Importes comerciales son principalmente `money` (cuatro decimales) y cantidades son `float`. El contrato de cálculo debe distinguir precisión de persistencia, redondeos VB6 y comparación de cantidades; no convertir indiscriminadamente toda cantidad o dinero a dos decimales.

Hay FK activas sin validación histórica (`is_not_trusted`) y otras deshabilitadas. No habilitarlas ni inferir que todos los datos históricos cumplen las relaciones durante esta migración.

## Configuración efectiva relevante

Valores observados en esta base, sin modificarlos:

| Configuración | Valor | Implicación para el primer circuito |
|---|---|---|
| `EntregaParcialRemito` | SI | Finalizar origen solo cuando el circuito determine que no quedan pendientes |
| `DescargaStockRemitos` | SI | La factura de remito debe considerar el movimiento ya hecho |
| `DescargaStockxReparto` | NO | El camino normal no difiere la descarga al reparto por esta clave |
| `FacturaNPPreparadas` | NO | No exige el camino de pedidos preparados por esta clave |
| `V_SoloPedidosPreparados` | NO | No limita globalmente la carga a preparados por esta clave |
| `RevalorizaRemitos` | NO | Investigar precio histórico/mantenido al traer remito, sin revalorizar por defecto |
| `PreciosFinales` | SI | Respetar cómo la pantalla y el totalizador manejan precios finales |
| `PreciosFinalesNeto` | NO | No aplicar el camino de precio final neto |
| `CantidadDecimales` | 2 | Default encontrado para formato; no reemplaza precisión intermedia |
| `CantidadDecimalesVentas` | Vacío | El lector VB6 no sustituye el default global con un valor positivo |
| `UsaMultilesMonedas` | NO | Primer piloto en moneda local; motor debe permitir otros contextos después |
| `USAFCELECTRONICA` | SI | El piloto operativo requiere electrónica/recuperación antes de emitir |
| `NoFacturaSinStock` | NO | No agregar bloqueo de stock negativo que no existe en este contexto |
| `NoFacturaVencidos` | NO | No agregar restricción de vencidos por esta clave |
| `USACCOSTO` | SI | Evaluar por cuenta el alcance diferido; no desactivar esta clave |

Las fechas operativas consultadas cubren 2026; stock/asientos convierten algunas claves de cierre desde texto `dd/MM/yyyy`. Hay que establecer formato de sesión SQL compatible o usar el mecanismo confirmado por objeto. No cambiar las fechas del cliente para facilitar las pruebas.

## Pendientes, aplicaciones y finalización

Se confirmó la cadena instalada:

```text
V_MV_CPTEINSUMOS + MV_APLICACION
             ↓
VT_V_MV_INSUMOS_APLICACION
             ↓
VT_V_MV_INSUMOS_SALDOSRM
             ↓
FMT_MV_APLICACION_IDCOMPROBANTE
             ↓
V_MV_CPTE.FINALIZADA del origen
```

`FMT_MV_APLICACION_IDCOMPROBANTE` se ejecuta en INSERT/UPDATE de aplicaciones. En el camino FC/FP/TK/TKFC hacia RM/NP/PR, y RM hacia NP/PR, consulta `EntregaParcialRemito`. Con `SI`, finaliza el origen si no encuentra saldo en `VT_V_MV_INSUMOS_SALDOSRM`. Sin ese valor, puede finalizarlo directamente. No implementa el mismo camino al borrar una aplicación: la reversión requiere análisis separado.

`VT_V_MV_INSUMOS_SALDOSRM` agrupa cantidades originales por TC/documento/artículo/complemento/depósito y resta cantidades aplicadas. Excluye cabeceras anuladas o bloqueadas y admite RM, NP, OT y PR. No incluye por sí sola todas las restricciones de consulta del formulario: el servicio debe aplicar contexto, cliente, finalización y políticas reales del circuito.

`VT_V_MV_INSUMOS_APLICACION` suma el detalle del comprobante destino unido a cada fila de aplicación por TC/documento. No distribuye explícitamente una línea entre varios documentos de origen ni normaliza cantidades por unidad. Por lo tanto:

- Es candidata para reproducir el pendiente legacy, pero no una API universal de pendientes certificados.
- En una factura con varios remitos y artículos repetidos, una misma cantidad destino podría atribuirse a más de un origen. Es una inferencia de la definición, no una inconsistencia demostrada en datos.
- No agregar trazabilidad nueva y suponer que el trigger actual la utiliza: habría que definir cómo interoperará con VB6 y con la vista instalada.
- Mantener precio/unidad/origen por línea en el modelo aunque la UI ofrezca consolidación; aprobar por separado las diferencias intencionales de agrupación.

El recuento leído no encontró FC/FP/TK/TKFC con más de un remito de origen distinto en `MV_APLICACION`. Es un resultado de esa consulta y ese momento, no prueba de que ningún cliente use consolidación ni de que el circuito múltiple sea correcto.

### Actualizaciones adicionales en el fuente VB6

`ActualizaInsumosComprobante`, líneas 12547–12589 del formulario principal, actualiza el detalle del origen, además de insertar la factura:

- En ciertos caminos de remito, descuenta `CANTIDADAFACTURAR` y `CANTIDADAFACTURARUD` y evita valores negativos.
- En caminos de pedido/presupuesto con secuencia original, incrementa `CANTIDADPREPARADA`/`CANTIDADPREPARADAUD`, pone a cero otros campos y asigna `RECEPCION = -999`.

Estos campos comerciales del detalle no son la tabla de reservas ni una integración de balanza. Hay que conservar su significado donde el circuito lo requiere, incluso con esos módulos diferidos. No identificar la exclusión de reservas con omitir cualquier campo llamado «preparada».

`ControladoPor` del detalle puede guardar bultos en el camino revisado (12510–12512). No usarlo como nueva referencia de documento/usuario/origen sin revisar ese significado.

## Identidad de usuario y unidad de negocio

El login SQL utilizado no tiene una fila coincidente en `TA_USUARIOS.NOMBRE` (recuento observado: 0). Varios triggers utilizan `SYSTEM_USER` para buscar usuario o unidad de negocio cuando faltan campos.

Ejemplo: `FMT_V_MV_CPTE_INS` puede entrar si usuario, unidad de negocio o unidad destino están vacíos. Algunas asignaciones utilizan el usuario SQL aun si el documento se originó en una sesión web de otra persona. `FMT_MV_APLICACION_IDCOMPROBANTE` intenta completar unidad de negocio en aplicaciones incompletas.

Consecuencia: el motor web debe resolver y enviar el contexto comercial completo desde la sesión autorizada y verificar qué triggers lo preservan. No crear una fila de usuario, cambiar triggers, impersonar usuarios o usar el login de conexión como operador comercial para esquivar el problema.

`FMT_VALIDA_DEPOSITO` valida la compatibilidad entre depósito y unidad de negocio. Los triggers contables también verifican caja, ejercicio, fechas y permisos de cuentas según configuración. El contexto explícito no permite ignorar esos controles.

## Talonarios y numeración

Existen varias filas del mismo código de comprobante, diferenciadas por `Talonario`, con letras y unidades de negocio distintas. Elegir la primera fila por TC puede mezclar series o condiciones de diferentes contextos.

Definir un contrato que identifique talonario, unidad de negocio, punto de venta y letra antes de numerar. Hay que relevar las restricciones del usuario para seleccionarlo. `FN_OBTIENE_PROXIMO_NUMERO_CPTE` y el alta web instalada usan variantes de máximo + 1; leer el próximo número no lo reserva. La concurrencia debe resolverse dentro de la operación confirmada, y los números fiscales deben respetar la autoridad externa.

## Brechas respecto del POS actual

`sp_web_CreaAsientoFactura` **no existe en esta base** al momento del relevamiento. No se instaló el script de AlfaCore que lo crea. El hallazgo confirma que no se puede reutilizar automáticamente el flujo POS para Carga de Comprobantes.

Los otros SP web principales sí existen, pero tienen contratos limitados y transacciones propias:

- Alta: inserta cabecera y puede numerar desde el máximo local.
- Insumos: inserta una línea y luego actualiza totales de cabecera fuera del bloque principal; hay lógica propia de precios/unidades.
- Cobranza y aplicación: caminos específicos de cobro por factura.

No certifican por sí solos tareas, otros conceptos, stock completo, origen parcial/consolidado y asientos equivalentes a VB6. Antes de envolverlos en una transacción común hay que estudiar sus COMMIT/ROLLBACK, cambios de formato de fechas y efectos de triggers.

## Capacidades diferidas y muestra observada

`sys.partitions` informó aproximadamente 171.388 filas en reservas, 30.620 cabeceras, 243.962 detalles y 0 filas en `MV_ASIENTOSCCOSTO`. Son aproximaciones del catálogo y no distinguen datos activos, históricos o anulados.

Se leyeron 30 pedidos ordenados por `IdComprobante` descendente. En esa muestra, ninguno tenía filas de reserva asociadas ni fecha de preparación. La muestra demuestra que hay candidatos para relevar el camino simple, pero no certifica todos los pedidos ni los más recientes globalmente por fecha.

Aunque `USACCOSTO = SI`, la tabla de movimientos CC estaba vacía según el catálogo. Eso no prueba que ninguna cuenta exija centros de costo. Mantener la exclusión del módulo CC y verificar las cuentas del primer circuito. No desactivar triggers ni modificar datos históricos para hacer pasar pruebas.

## Contrato del próximo incremento

Implementar primero un servicio de **consulta y simulación** para el camino individual NP/RM → FC, con:

1. Contexto explícito de usuario, unidad de negocio, depósito y talonario.
2. Búsqueda server-side de documentos admisibles sin mezclar clientes/contextos.
3. Detalle editable conservando unidad, equivalencia, precio, descuento y secuencia original.
4. Pendiente obtenido del camino legacy aplicable y sus campos auxiliares; detectar los casos que el modelo agregado no representa.
5. Plan de efectos que diferencie factura directa, factura de pedido y factura de remito que ya descargó stock.
6. Declaración visible de reservas/CC/autorización de emisión diferidos, sin alterar configuraciones globales.

El camino múltiple y las unidades distintas necesitan cerrar el reparto de cantidades a orígenes antes de habilitar grabación. Para comparar resultados de escritura, preparar dos copias aisladas del mismo estado; no utilizar esta conexión para emitir comprobantes de prueba en una base sin confirmar su aislamiento.

Pendientes: casos reales completos con operador, precedencia de talonarios/usuario, detalle de imputaciones contables, origen parcial con unidades repetidas, equivalencia fiscal y recuperación. No hay todavía certificación de grabación ni evidencia de mejora de rendimiento.

# Resumen de tablas y vistas SQL - Alfa Gestión

## Objetivo

Este documento resume las tablas y vistas principales detectadas en el script SQL y en la documentación provista.
Está pensado para que Codex/GPT entienda qué representa cada objeto antes de modificar código, consultas o pantallas.

## Reglas generales de lectura

- `MA_`: tablas maestras.
- `MV_`: tablas de movimientos.
- `TA_`: tablas de referencia/configuración.
- `V_`: vistas o módulo Ventas según el contexto del nombre.
- `C_`: compras.
- `P_`: personal/RRHH.
- `AC_`: acumuladores/saldos.
- `AUX_`: auxiliares.
- Si existe una vista oficial, priorizar la vista antes que consultar tablas base.
- `MV_ASIENTOS` es la fuente contable principal.
- `V_MV_STOCK` es la fuente principal de movimientos de stock.

> Nota: las descripciones marcadas como genéricas fueron inferidas por prefijo/nombre del objeto. Las descripciones más confiables son las documentadas en el Excel y en las reglas funcionales indicadas por Alberto.

## Objetos documentados manualmente

### Escritura de códigos: maestros frente a movimientos

`CodigoPk.Format(codigo, ancho)` se utiliza para PK/códigos de maestros y referencias que requieren ancho fijo (unidad de negocio, moneda, vendedor, depósito, etc.). **No se utiliza para `TC` ni `CUENTA` en tablas de movimientos.**

En `V_MV_CPTE`, `MV_ASIENTOS`, `MV_APLICACION` y demás movimientos, grabar `.Trim()` para `TC`, sus variantes de origen (`TCORIGEN`, `TC_ORIGEN`, `TCO_ORIGEN`) y `TC_PRINT`, así como para la cuenta de cliente, proveedor o cuenta contable. No usar `PadRight`, `PadLeft` ni rellenar a la longitud declarada del campo: `TC='CB'` y `CUENTA='112010681'` deben conservar la representación de Alfa Gestión. Los campos maestros asociados, por ejemplo `UNEGOCIO='   1'`, conservan su formato estándar.

Ante dudas, relevar un movimiento equivalente grabado por Alfa Gestión y comparar `DATALENGTH` además del contenido. En `nvarchar`, `DATALENGTH(N'CB')=4`; el relleno aumenta esa longitud. SQL Server ignora espacios finales en muchas comparaciones con `=` y `LEN` excluye los espacios finales, pero concatenaciones, claves construidas en código y exportaciones pueden conservarlos y romper la compatibilidad (por ejemplo, Ver asiento).

# Plan de Cuentas, Clientes y Proveedores

## Descripción general

Este conjunto de tablas define el núcleo de datos para:

- Plan de cuentas
- Clientes
- Proveedores
- Bancos
- Medios de pago
- Centros de costo

En el sistema Alfa, **clientes y proveedores son cuentas contables**, no entidades separadas.

---

## Tablas principales

### MA_CUENTAS
Tabla principal del sistema de cuentas.

Contiene:
- Plan de cuentas completo
- Clientes
- Proveedores
- Bancos
- Medios de pago

Uso:
- Es la base para toda la contabilidad
- Se utiliza junto con configuraciones para determinar comportamiento
- Campo clave: `TipoVista` (define tipo de cuenta)

---

### MA_CUENTASADIC
Datos adicionales de las cuentas.

Contiene:
- Domicilio
- Información fiscal
- Datos complementarios de clientes/proveedores

Uso:
- Extiende la información de MA_CUENTAS
- Se usa en vistas como `VT_CLIENTES` y `VT_PROVEEDORES`

---

### MA_CUENTASSUC
Sucursales de cuentas.

Contiene:
- Datos de sucursales de clientes/proveedores

Uso:
- Permite manejar múltiples sucursales por cuenta
- Se utiliza en operaciones comerciales y logísticas

---

### MA_CUENTAS_CCDIST
Distribución de centros de costo.

Contiene:
- Máscara o estructura de asignación de centros de costo

Uso:
- Define cómo se distribuyen los costos automáticamente
- Se utiliza en procesos contables

---

### MA_CUENTASCCOSTO
Centro de costo por defecto.

Contiene:
- Centro de costo asignado a la cuenta

Uso:
- Define comportamiento por defecto en imputaciones contables

---

### MA_CUENTASCCOSTO_UNEG
Centro de costo por unidad de negocio.

Contiene:
- Centro de costo por cuenta y unidad de negocio

Uso:
- Permite comportamiento diferenciado según sucursal o empresa
- Fundamental en estructuras multiempresa

---

## Reglas importantes

### Clientes
- NO se obtienen directamente desde MA_CUENTAS
- Usar siempre:
  - `VT_CLIENTES`
- Condición:
  - `TipoVista = 'CL'`

---

### Proveedores
- Usar:
  - `VT_PROVEEDORES`
- Mismo criterio que clientes

---

### Bancos
- Identificados por:
  - `TipoVista = 'BC'`

---

### Medios de pago
Campo: `MA_CUENTAS.MediodePago`

Valores posibles:
- EF → Efectivo
- DB → Débitos / Créditos bancarios
- CH → Cheques
- TJ → Tarjetas
- RB → Retención Ingresos Brutos
- RI → Retención IVA
- RG → Retención Ganancias

---

## Regla clave del sistema

👉 Toda entidad comercial (cliente, proveedor, banco, etc.) es una cuenta contable.

Esto implica:

- No separar lógica de clientes/proveedores fuera del plan de cuentas
- Siempre validar tipo de cuenta antes de usarla
- No hardcodear comportamientos

---

## Uso recomendado en desarrollo

- Usar vistas (`VT_CLIENTES`, `VT_PROVEEDORES`)
- No consultar MA_CUENTAS directamente para lógica funcional
- Validar siempre:
  - TipoVista
  - configuraciones en TA_CONFIGURACION
  
  
 

### Plan de Cuentas-Clientes-Provee

| Objeto | Descripción |
|---|---|
| `MA_CUENTASADIC` | Datos Adicionales de la cuenta (Domicilio y otros datos de los clientes y proveedores) |
| `MA_CUENTASSUC` | Para los clientes y/o proveedores con sucursales, se registra aquí los datos de la sucursal) |
| `MA_CUENTAS_CCDIST` | Distribucion por default de los Centros de Costos (mascara para asignar los centros de costo) |
| `MA_CUENTASCCOSTO` | Asignacion del centro de costo por default a la cuenta |
| `MA_CUENTASCCOSTO_UNEG` | Asignacion del centro de costo por default a la cuenta según Unidad de negocio |
| `TA_PAISES` | Paises |
| `V_TA_UnidadNegocio` | Unidades de Negocio |
| `V_TA_RETENCIONES` | Retenciones Nota: tiene Identity |
| `V_TA_MotivoVta` | Motivo de Venta Nota: tiene identity |
| `V_TA_MotivoCpra` | Motivo de compra Nota: tiene identity |
| `V_TA_Cpra_Vta` | Condicion de Compra/Venta Nota: tiene identity |
| `V_TA_Categoria` | Categorias de Clientes/Proveedores Nota: tiene identity |
| `TA_TIPODOCUMENTO` | Tipos de Documento (cuit, dni,etc...) |
| `TA_ESTADOS` | Provincias |
| `TA_CONDIVA` | Condicion de IVA |
| `TA_CCOSTO` | Centros de Costos |
| `V_TA_VENDEDORES` | Vendedores Nota: tiene identity |
| `V_TA_VendedoresCm` | Comision de Vendedores según descuento otorgado Nota: * tiene identity, cambiar PK (dejar idvendedor + descuento) |
| `V_TA_DEPOSITOS` | Depositos |
| `V_TA_DEPOSITOSMM` | Depositos minimos y maximos |
| `V_MA_PreciosCab` | Listas de precios |

### Articulos-Precios

| Objeto | Descripción |
|---|---|
| `MA_CUENTAS` | Plan de cuentas, para obtener datos del proveedor |
| `TA_MONEDAS` | monedas |
| `V_TA_Percepcion` | percepciones |
| `V_MA_ARTICULOS` | Maestro de Articulos Nota: Tiene identity |
| `V_MA_PRECIOSCAB` | Listas de Precios Cabecera Nota: Tiene identity, cambiar PK dejar IdLista + TipoLista |
| `V_MA_PRECIOS` | Listas de Precios Detalle Nota: Tiene identity, cambiar PK dejar IdLista + IdArticulo + TipoLista |
| `V_TA_Unidad` | unidade de medida Nota: tiene Identity |
| `V_TA_Rubros` | rubros Nota: tiene Identity |
| `V_TA_TipoArticulo` | tipos de articulos o marcas Nota: tiene Identity |
| `V_TA_PoliticaPrecios` | politicas de precios Nota: tiene Identity |
| `V_TA_FAMILIAS` | familias |
| `V_TA_TarifaFlete` | tarifas de flete |
| `S_TA_Equiv` | Equivalencias |
| `V_MA_ArtCatRel` | Categoria de Articulos Relacionadas |
| `V_TA_CategoriaArticulo` | Categoria de Articulos |

### V_MV_Cpte

| Objeto | Descripción |
|---|---|
| `V_MV_CPTE_OBSERV` | Detalle de Observaciones |
| `V_MV_CpteTerceros` | Detalle de Comprobantes a terceros (solo para Ord. De Trab.) |
| `V_MV_CpteEnComision` | Detalle de Trabajos en Comision (solo para Ord. De Trab.) |
| `V_MV_CpteDoc` | Detalle de Documentos relacionados |

### Solicitud Compra

| Objeto | Descripción |
|---|---|
| `V_MV_Cpte` | Cab Mov.Comprobantes, aquí se registra el comprobante SCOT Solicitudes procesadas |
| `V_MV_CpteInsumos` | Det. Mov.Comprobantes Articulos |
| `V_MV_CpteTareas` | Det. Mov.Comprobantes Servicios |
| `SolicitudCpra` | Cabecera de Solicitudes de Compras |
| `SolicitudCpraDet` | Detalle |
| `V_MV_Cpte_Observ` | Det. Mov.Comprobantes Observaciones |

### C_MV_Cpte

| Objeto | Descripción |
|---|---|
| `C_MV_Cpte` | Cabecera y totales |
| `C_MV_CPTE_OBSERV` | Detalle de Observaciones |
| `C_MV_CpteInsumos` | Detalle de Insumos/articulos |
| `C_MV_CpteTareas` | Detalle de Tareas/Servicios |

## Inventario agrupado de tablas y vistas detectadas en el SQL

### Contabilidad / Cuentas corrientes

Total de objetos: 48

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `AC_CuentasDiario` | Tabla/vista acumuladora o de saldos. | Empresa (nvarchar(15)), UnidadNegocio (nvarchar(4)), Fecha (datetime), Cuenta (nvarchar(15)), Debe (money), Haber (money) |
| TABLE | `AC_CuentasDiario_CCOSTO` | Tabla/vista acumuladora o de saldos. | Empresa (nvarchar(15)), UnidadNegocio (nvarchar(4)), CCosto (nvarchar(4)), Fecha (datetime), Cuenta (nvarchar(15)), Debe (money), Haber (money) |
| TABLE | `AC_OCPENDIENTES` | Tabla/vista acumuladora o de saldos. | IdArticulo (nvarchar(25)), Pendiente (float) |
| TABLE | `Ac_SaldosLayout` | Tabla/vista acumuladora o de saldos. | Id (int), IdDeposito (nvarchar(4)), IdPosicion (nvarchar(25)), IdArticulo (nvarchar(25)), NroLote (nvarchar(25)), IdUnidadArticulo (nvarchar(4)), SaldoArticulo (float), IdUnidadPosicion (nvarchar(4)) |
| TABLE | `MA_CONTACTOS_CUENTAS` | Tabla maestra del sistema. | Id (int), IdContacto (float), Cuenta (nvarchar(15)) |
| TABLE | `MA_CUENTAS` | Plan de cuentas, para obtener datos del proveedor | CODIGO (nvarchar(15)), DV (smallint), DESCRIPCION (nvarchar(50)), TITULO (bit), AJUSTE (bit), INDICE (smallint), BLOQUEO (bit), MANUAL (ntext) |
| TABLE | `MA_CUENTASADIC` | Datos Adicionales de la cuenta (Domicilio y otros datos de los clientes y proveedores) | CODIGO (nvarchar(15)), CONTACTO (nvarchar(70)), CALLE (nvarchar(50)), NUMERO (nvarchar(6)), PISO (nvarchar(2)), DEPARTAMENTO (nvarchar(2)), CPOSTAL (nvarchar(10)), LOCALIDAD (nvarchar(50)) |
| TABLE | `MA_CUENTASCCOSTO` | Asignacion del centro de costo por default a la cuenta | CODIGO (nvarchar(15)), CCOSTO (nvarchar(4)), PORCENTAJE (float), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), PideConfirmacion (bit), VariablePorUD (bit) |
| TABLE | `MA_CUENTASCCOSTO_UNEG` | Asignacion del centro de costo por default a la cuenta según Unidad de negocio | CODIGO (nvarchar(15)), CCOSTO (nvarchar(4)), UNEGOCIO (nvarchar(4)), PORCENTAJE (float) |
| TABLE | `MA_CUENTASOBS` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | Codigo (nvarchar(15)), Sucursal (int), Nota (ntext), SituacionIB (nvarchar(2)), Convenio (nvarchar(2)), Concepto (nvarchar(2)), NroConstancia (nvarchar(13)) |
| TABLE | `MA_CUENTASSUC` | Para los clientes y/o proveedores con sucursales, se registra aquí los datos de la sucursal) | CODIGO (nvarchar(15)), SUCURSAL (int), RAZON_SOCIAL (nvarchar(50)), CONTACTO (nvarchar(70)), CALLE (nvarchar(50)), NUMERO (nvarchar(6)), PISO (nvarchar(2)), DEPARTAMENTO (nvarchar(2)) |
| TABLE | `MA_CUENTAS_ARCHIVOS_RELACIONADOS` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | ID (int), CUENTA (nvarchar(15)), RUTA_ARCHIVO (nvarchar(300)), OBSERVACIONES (nvarchar(500)), IMAGEN (image), EsContrato (bit), PeriodoDesde (date), PeriodoHasta (date) |
| TABLE | `MA_CUENTAS_AUTOCPTES` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | Cuenta (nvarchar(15)), Concepto (nvarchar(500)), Periodicidad (nvarchar(25)), TC (nvarchar(4)), Activo (bit), FechaUltMov (datetime), SucursalCuenta (int), UNEGOCIO (nvarchar(4)) |
| TABLE | `MA_CUENTAS_AUTOCPTESDET` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | ID (int), Cuenta (nvarchar(15)), Detalle (nvarchar(500)), Importe (money), NroUltimaCuota (float), Cuotas (float), SucursalCuenta (int), ImporteOld (money) |
| TABLE | `MA_CUENTAS_CCDIST` | Distribucion por default de los Centros de Costos (mascara para asignar los centros de costo) | ID (int), Codigo (nvarchar(4)), Nombre (nvarchar(50)), Concepto (nvarchar(250)), CCosto (nvarchar(4)), Porcentaje (money), PideConfirmacion (bit) |
| TABLE | `MA_CUENTAS_SALDOS` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | CUENTA (nvarchar(15)), IDVENDEDOR (nvarchar(4)), SALDO (money), SALDO_CPTESVENCIDOS (money) |
| TABLE | `MA_CUENTAS_SERVICIOS` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | ID (int), Codigo (nchar(15)), IdTarea (nchar(4)), Importe (money) |
| TABLE | `MA_CUENTAS_USUARIOS` | Tabla maestra vinculada al plan de cuentas, clientes, proveedores, bancos o atributos asociados. | ID (int), Cuenta (nvarchar(15)), GrupoUser (nvarchar(50)) |
| TABLE | `MV_APLICACION` | Tabla clave para aplicación entre comprobantes, pagos/cobranzas y cancelaciones. Zona sensible por posibles inconsistencias. | ID (int), CUENTA (nvarchar(15)), TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (nvarchar(1)), TCO_ORIGEN (nvarchar(4)), SUCURSAL_ORIGEN (nvarchar(4)) |
| TABLE | `MV_APLICACION_CTROL` | Tabla de movimientos del sistema. | ID (int), CUENTA (nvarchar(15)), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), TC_ORIGEN (nvarchar(4)), IDCOMPROBANTE_ORIGEN (nvarchar(13)), IMPORTE (money), USUARIO (nvarchar(200)) |
| TABLE | `MV_ASIENTOS` | Tabla central contable. Fuente principal para cuenta corriente, contabilidad, libros de IVA y movimientos que generan asiento. | CUENTA (nvarchar(15)), SECUENCIA (int), MES_OPERATIVO (tinyint), NUMERO ASIENTO (int), FECHA (datetime), DETALLE (nvarchar(200)), TC (nvarchar(4)), SUCURSAL (nvarchar(4)) |
| TABLE | `MV_ASIENTOSCCOSTO` | Tabla de movimientos del sistema. | PERIODO (nvarchar(6)), MES_OPERATIVO (tinyint), NUMERO ASIENTO (int), CUENTA (nvarchar(15)), SECUENCIA (int), DEBE-HABER (nvarchar(1)), MONEDA (nvarchar(4)), CCOSTO (nvarchar(4)) |
| TABLE | `TA_AGRUPCUENTAS` | Tabla o vista de referencia/configuración. | IdAgrupacion (nvarchar(4)), Descripcion (nvarchar(200)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `TA_AGRUPCUENTAS_DETALLE` | Tabla o vista de referencia/configuración. | id (int), IdAgrupacion (nvarchar(4)), Cuenta (nvarchar(15)) |
| TABLE | `TA_CUENTASIVA` | Tabla o vista de referencia/configuración. | CFI_IVARI (nvarchar(15)), CFI_IVARI1 (nvarchar(15)), CFI_IVARI2 (nvarchar(15)), CFI_IVARI3 (nvarchar(15)), CFI_IVARI4 (nvarchar(15)), CFI_IVARI5 (nvarchar(15)), CFI_RETPERC (nvarchar(15)), CFI_RETIGAN (nvarchar(15)) |
| TABLE | `TA_CUENTAS_COMPROBANTES` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), CUENTA (nvarchar(15)), CUENTA-DESDE (nvarchar(15)), CUENTA-HASTA (nvarchar(15)), DEBE-HABER (nvarchar(1)), SECUENCIA (int), FechaHora_Grabacion (datetime) |
| TABLE | `TA_IMPORTACUENTAS` | Tabla o vista de referencia/configuración. | NOMBRE (nvarchar(100)), CUENTA (nvarchar(15)), TIPO_ARCHIVO (nvarchar(1)), SEPARADOR (nvarchar(2)), CALIFICADOR (nvarchar(1)), CAMPO_DESCRIPCION (nvarchar(100)), CAMPO_CONDIVA (nvarchar(100)), CAMPO_CUIT (nvarchar(100)) |
| TABLE | `TA_IMPORTACUENTASDETALLE` | Tabla o vista de referencia/configuración. | NOMBRE (nvarchar(100)), NOMBRE_CAMPO (nvarchar(100)), LONGITUD (nvarchar(5)), SECUENCIA (int) |
| TABLE | `V_TA_CuentasComision` | Tabla o vista de referencia/configuración. | Id (int), Tipo (nvarchar(2)), CUENTA (nvarchar(15)), DESCRIPCION (nvarchar(100)) |
| VIEW | `AC_Saldos` | Tabla/vista acumuladora o de saldos. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `AC_SaldosCta` | Tabla/vista acumuladora o de saldos. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `AC_SaldosCtaCC` | Tabla/vista acumuladora o de saldos. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `AC_SALDOSCTAConsolidado` | Tabla/vista acumuladora o de saldos. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `AC_SaldosDiario` | Tabla/vista acumuladora o de saldos. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `MV_ASIENTOS_AUX` | Tabla de movimientos del sistema. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_AC_CuentasDiario_CCosto` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_AC_Cuentas_Diario` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_MA_CUENTAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_FILTROPORCUENTA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP__FILTROPORCUENTA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_Clientes` | Vista oficial para obtener clientes. No consultar cuentas base directamente para clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CUENTAS_CONC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_ma_cuentasadic` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_CUENTASCC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_ma_cuentassuc` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_CUENTAS_CONTACTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PREPARACION_CUENTA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_Proveedores` | Vista oficial para obtener proveedores. No consultar cuentas base directamente para proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Compras

Total de objetos: 76

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `AC_ComprasDiaria` | Tabla/vista acumuladora o de saldos. | ID (int), UNegocio (nvarchar(4)), Fecha (datetime), IdArticulo (nvarchar(25)), Consumo (float), ValorCosto (money), ValorVenta (money), ValorVentaSinDto (money) |
| TABLE | `AC_ComprasMes` | Tabla/vista acumuladora o de saldos. | ID (int), UNEGOCIO (nvarchar(4)), Fecha (nvarchar(7)), IdArticulo (nvarchar(25)), Consumo (float), ValorCosto (money), ValorVenta (money), ValorVentaSinDto (money) |
| TABLE | `C_MV_Cpte` | Cabecera y totales | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), CUENTA (nvarchar(15)), FECHA (datetime), VENCIMIENTO (datetime), NOMBRE (nvarchar(50)), DOMICILIO (nvarchar(50)) |
| TABLE | `C_MV_CpteDoc` | Movimiento o detalle de comprobantes de compras. | ID (int), CUENTA (nvarchar(15)), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), DOCUMENTO (nvarchar(250)), Transmision (int), Recepcion (int) |
| TABLE | `C_MV_CpteInsumos` | Detalle de Insumos/articulos | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), CUENTA (nvarchar(15)), IDARTICULO (nvarchar(25)), DESCRIPCION (nvarchar(100)), IDUNIDAD (nvarchar(4)), CANTIDADUD (float) |
| TABLE | `C_MV_CpteTareas` | Detalle de Tareas/Servicios | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), CUENTA (nvarchar(15)), IDTAREA (nvarchar(4)), DESCRIPCION (nvarchar(250)), HORAS (float), VALORHORA (money) |
| TABLE | `C_MV_CPTE_ADICIONALES` | Movimiento o detalle de comprobantes de compras. | id (int), ruca (nvarchar(150)), caracter (nvarchar(150)), renspa (nvarchar(150)), dte (nvarchar(150)), guia (nvarchar(150)), tc (nvarchar(4)), idcomprobante (nvarchar(13)) |
| TABLE | `C_MV_CPTE_LOTE` | Movimiento o detalle de comprobantes de compras. | FechaInicio (datetime), NroLote (int), Usuario (nvarchar(50)), Observaciones (nvarchar(500)) |
| TABLE | `C_MV_CPTE_LOTEDET` | Movimiento o detalle de comprobantes de compras. | NroLote (int), CUENTA (nvarchar(15)), TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (nvarchar(1)) |
| TABLE | `C_MV_CPTE_OBSERV` | Detalle de Observaciones | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), CUENTA (nvarchar(15)), TIPO_OBS (nvarchar(2)), OBSERVACION (ntext), IMPORTE (money), IMPORTE_S_IVA (money) |
| TABLE | `C_MV_CPTE_PERCEPCIONES` | Movimiento o detalle de comprobantes de compras. | ID (int), CUENTA (nvarchar(15)), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), PROVINCIA (nvarchar(4)), PERCEPCION (money) |
| TABLE | `C_Proveedores_CCompra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdProveedor (nvarchar(15)), IdCond_Cpra_Vta (nvarchar(4)), PorDto1 (float), PorDto2 (float), PorDto3 (float), PorDto4 (float), PorDto5 (float) |
| TABLE | `IA_Compras_CAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), Estado (nvarchar(20)), FechaHora_Proceso (datetime), FechaHora_Modificacion (datetime), Usuario_Proceso (nvarchar(50)), Observaciones_Rev (nvarchar(500)), Archivo_RutaOriginal (nvarchar(500)), Archivo_NombreOriginal (nvarchar(260)) |
| TABLE | `IA_Compras_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), ID_CAB (int), NroRenglon (int), Cantidad (nvarchar(20)), Codigo_Articulo (nvarchar(50)), Descripcion (nvarchar(200)), UD (nvarchar(10)), Importe_Lista (money) |
| TABLE | `SolicitudCpra` | Cabecera de Solicitudes de Compras | NroSolicitud (nvarchar(13)), FechaCarga (datetime), FechaComprometido (datetime), Usuario (nvarchar(50)), Comentario (nvarchar(250)), FechaOrdenCpra (datetime), ProveedorSug (nvarchar(15)), UNegocio (nvarchar(4)) |
| TABLE | `SolicitudCpraDet` | Detalle | ID (int), NroSolicitud (nvarchar(13)), IdArticulo (nvarchar(25)), Descripcion (nvarchar(100)), UniMed (nvarchar(4)), Cantidad (float), Precio (money), Estado (int) |
| TABLE | `V_TA_COMPRADORES` | Tabla o vista de referencia/configuración. | Codigo (nvarchar(15)), Nombre (nvarchar(250)), Cuit (nvarchar(15)), idDeposito (nvarchar(4)), Onca (nvarchar(50)), uNegocio (nvarchar(4)) |
| TABLE | `V_TA_Cpra_Vta` | Condicion de Compra/Venta Nota: tiene identity | ID (int), IDCond_Cpra_Vta (nvarchar(4)), Descripcion (nvarchar(50)), CantidadDias (int), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), PorcRecargo (money), Transmision (int) |
| TABLE | `V_TA_Cpra_VtaDias` | Tabla o vista de referencia/configuración. | IDCOND_CPRA_VTA (nvarchar(4)), Hasta (int), Vence (int) |
| TABLE | `V_TA_Input_Compras` | Tabla o vista de referencia/configuración. | Id (int), IdMotivoCpra (nvarchar(4)), CondIva (nvarchar(4)), Repuestos (nvarchar(15)), Servicios (nvarchar(15)), Otros (nvarchar(15)), V_DFI_CTAVTA (nvarchar(15)), V_DFI_IMPINT (nvarchar(15)) |
| TABLE | `V_TA_MotivoCpra` | Motivo de compra Nota: tiene identity | ID (int), IdMotivoCpra (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int), HabilitadoPara (nvarchar(10)) |
| VIEW | `Aux_Ac_ComprasDiaria` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Aux_Ac_ComprasMes` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Aux_ComprasMes` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_AplicacionOC` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_CREDITOS_PENDIENTES` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_IMPAGOS` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_IMPAGOS_2026` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_SALDOS` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_SALDOS_TODOS` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_SALDOS_TODOS_UNEG` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_CPTES_SALDOS_UNEG` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_FaltantesOC` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_FCCore_Pendientes` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OCompraPendientes` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OCompraPendientes_Detalle` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OCompras_Pendientes_Detalle_Deposito` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OCompras_Pend_Detalle_Dep` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OC_RecibidasPendienteFac` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OC_RecibidasPendienteFac2` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OPG_PENDIENTES` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_OPG_PENDIENTES_UNEG` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `CO_PAGOS_REALIZADOS` | Vista/proceso asociado a compras, pagos o saldos de proveedores. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_BusquedaPreciosProvDtoCpra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_BusquedaPreciosProveedorDtoCpra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_CONSOLIDADO_RMC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_CONSOLIDADO_RMC_FCC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_CONSOLIDADO_RMC_FCC_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_CONSOLIDADO_RMC_FCC_PENDIENTE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_MV_CPTE_DETALLE` | Movimiento o detalle de comprobantes de compras. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_RemitosConCobranzas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_RemitosFacturados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `c_RemitosPendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_RemitosPendientesSinProv` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_RemitosPendientesSinProveedor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `C_RemitosSinAplicacion` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaCompras` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaCompras_AFIP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaCompras_Contadores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Libro_ComprasConFP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_ComprasDiaMesAnio` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_UltimaCompra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CTROL_SALDOAPLICACION_CPRA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_AFIP_CITI_Compras` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_COMPRASTOTALES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEIVAPROFORMA_CPRA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLExLIBROIVA_CPRA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_Planilla_Compras` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumoCpra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumoCpra_Cliente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumoCpra_Grupos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumoCpra_GruposMes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumoCpra_Vendedor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vw_compras_cabecera_dashboard` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vw_compras_detalle_dashboard` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ArticulosUltimaCompra` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Ventas / Movimientos comerciales

Total de objetos: 225

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `AC_VentasDiaria` | Tabla/vista acumuladora o de saldos. | ID (int), UNegocio (nvarchar(4)), Fecha (datetime), IdArticulo (nvarchar(25)), Consumo (float), ValorCosto (money), ValorVenta (money), ValorVentaSinDto (money) |
| TABLE | `AC_VentasFormTStock` | Tabla/vista acumuladora o de saldos. | Fecha (datetime), IdArticulo (nvarchar(25)), ConsumoG1 (float), ConsumoG2 (float), ConsumoG3 (float), ConsumoG4 (float), ConsumoMesAnt (float), ConsumoMes (float) |
| TABLE | `AC_VentasMes` | Tabla/vista acumuladora o de saldos. | ID (int), UNEGOCIO (nvarchar(4)), Fecha (nvarchar(7)), IdArticulo (nvarchar(25)), Consumo (float), ValorCosto (money), ValorVenta (money), ValorVentaSinDto (money) |
| TABLE | `AUX_ALCANCE_VENTAS` | Objeto auxiliar de soporte. | id (int), idVendedor (nvarchar(4)), diasTrabajados (int), usuario (nvarchar(100)), ImporteSIva (money) |
| TABLE | `AUX_INTERFACE_INVENTARIO` | Objeto auxiliar de soporte. | ID (int), CodigoBarra (nvarchar(50)), Linea (nvarchar(250)), Interface (nvarchar(50)), Usuario (nvarchar(150)) |
| TABLE | `MV_INVENTARIOS` | Tabla de movimientos del sistema. | ID (int), IdInventario (int), IdArticulo (nvarchar(25)), IdUnidad (nvarchar(4)), Stock (float), Conteo1 (float), Conteo2 (float), Diferencia (float) |
| TABLE | `MV_INVENTARIOSCAB` | Tabla de movimientos del sistema. | IdInventario (int), Fecha (datetime), IdDeposito (nvarchar(4)), Usuario (nvarchar(50)), Observaciones (nvarchar(255)), Finalizada (bit), Filtros (ntext), Consolidado (int) |
| TABLE | `V_COMISIONES_VENDEDOR_APL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idLiquidacion (int), CbTc (nvarchar(4)), IdCobranza (nvarchar(13)), Tc (nvarchar(4)), IDComprobante (nvarchar(13)), Comision (money), Observaciones (nvarchar(250)) |
| TABLE | `V_COMISIONES_VENDEDOR_CAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idLiquidacion (int), fecha_desde (datetime), fecha_hasta (datetime), usuario (nvarchar(100)) |
| TABLE | `V_COMISIONES_VENDEDOR_COB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdLiquidacion (int), CbTc (nvarchar(4)), IdCobranza (nvarchar(13)), FechaCob (date), TotalCobrado (money), Comision (money), Observaciones (nvarchar(250)) |
| TABLE | `V_COMISIONES_VENDEDOR_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdLiquidacion (int), Tc (nvarchar(4)), IDComprobante (nvarchar(13)), IdArticulo (nvarchar(25)), Cantidad (money), ValorVtaConIVA (money), AlicuotaIVA (money) |
| TABLE | `V_CPTES_MODELO_CAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdCpteModelo (int), NOMBRE (nvarchar(50)), DETALLE (nvarchar(50)), TCDefault (nvarchar(4)), ModificaTC (bit), ModificaRegistros (bit), AgregaRegistros (bit), DepositoOrigen (nvarchar(4)) |
| TABLE | `V_CPTES_MODELO_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdCpteModelo (int), IdArticulo (nvarchar(25)), Cantidad (float), IdUnidad (nvarchar(4)), FECHACOMP (datetime), CantCumplida (float) |
| TABLE | `V_ITEMS_PERFORMANCE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Codigo (nvarchar(4)), Descripcion (nvarchar(250)), Objetivo (int), Remuneracion (money), CCC (bit), Activo (bit), IdArticulo (nvarchar(max)) |
| TABLE | `V_MV_AdicionalesAFacturar` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), Detalle (nvarchar(max)), Minutos (int), FechaHoraAlta (datetime), Facturado (bit), Observaciones (nvarchar(50)), Periodo (nvarchar(6)), Cuenta (nchar(15)) |
| TABLE | `V_MV_ANALISISHARINA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), FECHA (datetime), RESPONSABLE (nvarchar(100)), TIPO_HARINA (nvarchar(25)), HORARIO (nvarchar(8)), HUMEDO (money), SECO (money), CARACT (nvarchar(100)) |
| TABLE | `V_MV_BONOS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDBONO (int), IDTIPOBONO (nvarchar(4)), CUENTA (nvarchar(25)), DESCRIPCIONCUENTA (nvarchar(100)), DESDE (int), HASTA (int), UNITARIO (money) |
| TABLE | `V_MV_BONOS_DETALLE` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IDBONO (int), IDTIPOBONO (nvarchar(4)), NUMERO (int), IMPRESO (bit), ANULADO (bit), FECHARECEPCION (datetime), IdNC (nvarchar(15)), Ean13 (nvarchar(13)) |
| TABLE | `V_MV_BONOS_DETALLE_DUP` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDBONO (int), IDTIPOBONO (nvarchar(4)), NUMERO (int), FECHADUPLICACION (datetime) |
| TABLE | `V_mv_CertificadosBajayDesarme` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdCertificado (int), Certificado (nvarchar(10)), Dominio (nvarchar(7)), IdTipo_MarcaVehiculo (nvarchar(4)), IdRubro_TipoVehiculo (nvarchar(4)), Modelo (nvarchar(50)), IdTipoMarca_Motor (nvarchar(4)), NumeroMotor (nvarchar(50)) |
| TABLE | `V_MV_CierreCaja` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdCierre (int), FechaOperativa (datetime), FechaCierre (datetime), Usuario (nvarchar(150)), Unegocio (nvarchar(4)), IdCaja (nvarchar(4)), CantidadZ (int), CantidadX (int) |
| TABLE | `V_MV_CierreCaja00` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IdCierre (int), CuentaMoneda (char(15)), Denominacion (float), Cantidad (float), Valor (float), Cotizacion (float), CambioInicial (float) |
| TABLE | `V_MV_CierreCaja01` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IdCierre (int), TipoMensaje (nvarchar(20)), Mensaje (nvarchar(250)) |
| TABLE | `V_MV_CierreCaja02` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), IdCierre (int), Cuenta (nvarchar(15)), Importe (money), ImporteP (money), Tipo (nvarchar(4)), Moneda (nvarchar(4)), Cotizacion (money) |
| TABLE | `V_MV_Comanda` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Estado (nvarchar(1)), FhOperativa (datetime), Planilla (int), Comanda (int), FhAlta (datetime), FhCierre (datetime), Mesa (nvarchar(6)), Mozo (nvarchar(6)) |
| TABLE | `V_MV_COMANDACAB` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Fecha (datetime), Cerrada (bit), Usuario (nvarchar(150)), FechaHora (datetime) |
| TABLE | `V_MV_COMPULSA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDCOMPULSA (int), FECHACARGA (datetime), IDLISTA (nvarchar(4)), FINALIZADO (bit), USUARIO (nvarchar(100)), GRUPO (nvarchar(50)), Observaciones (nvarchar(250)) |
| TABLE | `V_MV_Cortes` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Pedido (nvarchar(13)), IdArticulo (nvarchar(25)), Plancha (float), Corte (float), Cantidad (float), Ancho (float), Largo (money) |
| TABLE | `V_MV_Cotizar` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdArticulo (nvarchar(25)), Cot_Bolsas (float), Cot_Material (nvarchar(25)), Cot_Ancho (float), Cot_Largo (float), Cot_Micrones (float), Cot_PesoEsp (float), Cot_CodImpresion (nvarchar(25)) |
| TABLE | `V_MV_Cpte` | Cab Mov.Comprobantes, aquí se registra el comprobante SCOT Solicitudes procesadas | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), FECHA (datetime), FECHAESTINICIO (datetime), FECHAESTFIN (datetime), CUENTA (nvarchar(15)) |
| TABLE | `V_MV_CpteAcciones` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), TIPO_ACCION (nvarchar(2)), FECHAHORA (smalldatetime), USUARIO (nvarchar(255)), PC (nvarchar(255)) |
| TABLE | `V_MV_CpteDoc` | Detalle de Documentos relacionados | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), DOCUMENTO (nvarchar(250)), Transmision (int), Recepcion (int) |
| TABLE | `V_MV_CpteEnComision` | Detalle de Trabajos en Comision (solo para Ord. De Trab.) | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), TRABAJOS (ntext), TECNICOS (nvarchar(500)), FECHAINICIO (datetime), FECHAFIN (datetime) |
| TABLE | `V_MV_CpteInsumos` | Det. Mov.Comprobantes Articulos | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), IDARTICULO (nvarchar(25)), DESCRIPCION (nvarchar(100)), IDUNIDAD (nvarchar(4)), CANTIDADUD (float) |
| TABLE | `V_MV_CpteLlamadas` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdCpteLlamadas (int), Tc (nvarchar(50)), IdComprobante (nvarchar(50)), IdComplemento (int), FechaHora (datetime), Estado (nvarchar(1)), FechaHoraRespuesta (datetime), MensajeOperador (nvarchar(max)) |
| TABLE | `V_MV_CpteTareas` | Det. Mov.Comprobantes Servicios | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), IDTAREA (nvarchar(4)), DESCRIPCION (nvarchar(250)), HORAS (float), VALORHORA (money) |
| TABLE | `V_MV_CpteTerceros` | Detalle de Comprobantes a terceros (solo para Ord. De Trab.) | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), DETALLE (ntext), FECHA (datetime), CUENTA (nvarchar(15)), MATRICULA (nvarchar(20)) |
| TABLE | `V_MV_CPTE_CONSTRUCTORA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), tc (nvarchar(4)), idcomprobante (nvarchar(13)), obra_publica (bit), gastos_financieros (float), gastos (float), beneficio (float), impuestos (float) |
| TABLE | `V_MV_CPTE_COT` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), FECHA (datetime), NROCOT (nvarchar(250)) |
| TABLE | `V_MV_CPTE_ELECTRONICOS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdRequerimiento (nvarchar(100)), CantReg (int), Presta_Serv (bit), TC (nvarchar(4)), IdComprobante (nvarchar(13)), Tipo_Doc (int), Nro_Doc (nvarchar(50)), Tipo_Cpte (int) |
| TABLE | `V_MV_CPTE_ELECTRONICOS_EXPORTACION` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), TC (nvarchar(4)), IdComprobante (nvarchar(13)), Pais_Destino (nvarchar(50)), CUIT_Pais_Destino (nvarchar(50)), Tipo_Exportacion (nvarchar(50)), Intercom (nvarchar(5)), IDImpositivo (nvarchar(50)) |
| TABLE | `V_MV_CPTE_OBSERV` | Detalle de Observaciones | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), TIPO_OBS (nvarchar(2)), OBSERVACION (ntext), IMPORTE (money), IMPORTE_S_IVA (money) |
| TABLE | `v_mv_creales` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), Fecha (datetime), Cuenta (nvarchar(15)), Matricula (nvarchar(20)), Concepto (nvarchar(200)), Responsable (nvarchar(100)), Importe (money), Debe_Haber (nvarchar(1)) |
| TABLE | `v_mv_creales_Aplicacion` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), IdCargo (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), IMPORTE (money) |
| TABLE | `V_MV_CRONOGRAMA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Cuenta (nvarchar(15)), CodigoMant (nvarchar(25)), IdComprobante (nvarchar(13)), Medidas (nvarchar(1)), Frecuencia (nvarchar(1)), Cada (int), Fecha_Inicio (datetime) |
| TABLE | `V_MV_CtrolCierreCaja` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Fecha (datetime), CuentaMoneda (char(15)), Denominacion (float), Cantidad (float), Valor (float), Cotizacion (float) |
| TABLE | `V_MV_CTROL_CPTE` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (nvarchar(1)), FechaEnvioMail (datetime), Usuario (nvarchar(100)), idComplemento (int) |
| TABLE | `V_MV_Diarios` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDDiario (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), IDTecnico (nvarchar(4)), FECHA (datetime), IDTarea (nvarchar(4)) |
| TABLE | `V_MV_ENTRATRIGO` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Fecha (datetime), Responsable (nvarchar(50)), Toneladas (money), Vendedor (nvarchar(100)), Procedencia (nvarchar(100)), Camionero (nvarchar(100)), Humedo (money) |
| TABLE | `V_MV_ETIQUETAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDEtiqueta (int), FECHACARGA (datetime), USUARIO (nvarchar(100)), Observaciones (nvarchar(250)), IdArticulo (nvarchar(25)), DescripcionArticulo (nvarchar(100)), Presentacion (nvarchar(100)) |
| TABLE | `V_MV_ETIQUETAS_CTROL` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IDLISTA (nvarchar(4)), IDARTICULO (nvarchar(25)), Precio (money), Usuario (nvarchar(100)), FechaHora (datetime) |
| TABLE | `V_MV_Faltantes` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), FechaHora (datetime), IdArticulo (nvarchar(25)), Descripcion (nvarchar(100)), Cantidad (float), Usuario (nvarchar(50)) |
| TABLE | `V_MV_FORMPEDIDOS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDFormPedido (int), FECHACARGA (datetime), USUARIO (nvarchar(100)), DepositoLocal (nvarchar(4)), DepositoCD (nvarchar(4)), Observaciones (nvarchar(250)), IdArticulo (nvarchar(25)) |
| TABLE | `V_MV_FORMPEDIDOSEXC` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdLista (nvarchar(4)), IdArticulo (nvarchar(25)), Id (int) |
| TABLE | `V_MV_FORMTOMASTOCK` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDFORMTOMASTOCK (int), FECHACARGA (datetime), USUARIO (nvarchar(100)), DepositoCD (nvarchar(4)), Observaciones (nvarchar(250)), IdArticulo (nvarchar(25)), DescripcionArticulo (nvarchar(100)) |
| TABLE | `V_MV_INSERT` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDINSERT (int), FECHACARGA (datetime), VigenciaDesde (datetime), VigenciaHasta (datetime), IDLISTA (nvarchar(4)), USUARIO (nvarchar(100)), GRUPO (nvarchar(50)) |
| TABLE | `V_MV_INSERT_UNEG` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdInsert (int), IdLista (nvarchar(4)), UNegocio (nvarchar(100)) |
| TABLE | `V_MV_LiqUNeg` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Liquidacion (int), Fecha (datetime), Observaciones (nvarchar(250)), Usuario (nvarchar(50)), Anulado (bit), Fecha_Anulacion (datetime), UsuarioAnulacion (nvarchar(50)), Periodo (nvarchar(6)) |
| TABLE | `V_MV_LISTADISTRIBUCION` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Idlista (int), NombreLista (nvarchar(150)), IdCatalogo (int) |
| TABLE | `V_MV_LISTADISTRIBUCION_DET` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), IdLista (int), Cuenta (nvarchar(15)), Email (nvarchar(250)), nombre (nvarchar(250)) |
| TABLE | `V_MV_MUTUALES` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), Cuenta (nvarchar(15)), IdMutual (nvarchar(13)), FechaComprobante (datetime), FechaRecepcion (datetime), Importe (money), Observaciones (nvarchar(250)), Usuario (nvarchar(50)) |
| TABLE | `V_MV_OPHARINA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), DiaMolienda (datetime), TipoDeHarina (nvarchar(50)), Cantidad (float), CliDestino1 (nvarchar(15)), CliDestino2 (nvarchar(15)), CliDestino3 (nvarchar(15)), CliDestino4 (nvarchar(15)) |
| TABLE | `V_MV_PreciosHis` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), FechaHora (datetime), IdArticulo (nvarchar(25)), IdUnidad (nvarchar(4)), IdLista (nvarchar(4)), Observaciones (nvarchar(250)), Costo (money), Impuestos (money) |
| TABLE | `V_MV_PreciosProvHis` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Tipo (nvarchar(1)), FechaHora (datetime), IdArticulo (nvarchar(25)), PrecioProv (money), PrecioProvAnt (money), Transmision (int) |
| TABLE | `V_MV_PUNTOS_IMPRESOS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), id_padre (int), fh_impreso (datetime), sucursal (int), numero_control (int), codigobarra (nvarchar(13)), fh_recepcion (datetime), caja_emision (nvarchar(4)) |
| TABLE | `V_MV_PUNTOS_IMPRESOS_PRUEBAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), id_padre (int), fh_impreso (datetime), sucursal (int), numero_control (int), codigobarra (nvarchar(13)), fh_recepcion (datetime), caja_emision (nvarchar(4)) |
| TABLE | `V_MV_Reparto` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDReparto (int), IDComplemento (int), Fecha (datetime), Vehiculo (nvarchar(7)), Chofer (nvarchar(6)), Clase (int), Total_CantidadUD (float) |
| TABLE | `V_MV_RepartoDet` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), IdReparto (int), IdArticulo (nvarchar(25)), Descripcion (nvarchar(50)), IdUnidad (nvarchar(4)), CantidadUd (float), Cantidad (float), Rec_CantidadUd (float) |
| TABLE | `V_MV_RESERVAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), IDDEPOSITO (nvarchar(4)), IDPREPARACION (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDARTICULO (nvarchar(25)), IDUNIDAD (char(4)), CANTIDAD (float) |
| TABLE | `V_MV_SENIAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IdSenia (int), Recepcion (bit), Importe (money), Observaciones (nvarchar(255)), TC_Recepcion (nvarchar(4)), IdComprobante_Recepcion (nvarchar(13)), TC_Devolucion (nvarchar(4)), IdComprobante_Devolucion (nvarchar(13)) |
| TABLE | `V_MV_STATUS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | NroMov (int), UNegocio (nvarchar(4)), TC (nvarchar(4)), IdComprobante (nvarchar(13)), IdTarea (nvarchar(4)), IdEstado (nvarchar(4)), FechaHora (datetime), Usuario (nvarchar(100)) |
| TABLE | `V_MV_Stock` | Vista central de movimientos de stock. La cantidad ya viene con signo: positivo para ingreso, negativo para egreso. | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), SECUENCIA (int), FECHA (datetime), IDArticulo (nvarchar(25)), Descripcion (nvarchar(100)) |
| TABLE | `V_MV_STOCKHIS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), SECUENCIA (int), FECHA (datetime), IDArticulo (nvarchar(25)), Descripcion (nvarchar(100)) |
| TABLE | `V_MV_STOCK_LOCATOR` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), FECHA (datetime), TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (char(1)), IDARTICULO (nvarchar(25)), IDUNIDADBASE (nvarchar(4)) |
| TABLE | `V_MV_TARJETAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Cuenta (nvarchar(15)), FechaHora (datetime), NroSocio (nvarchar(25)), NroCupon (nvarchar(25)), Autorizo (nvarchar(50)), NroLote (nvarchar(15)), Cuotas (int) |
| TABLE | `V_MV_TARJETASCON_DET` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | IDCONC (int), SECUENCIA (int), IZQUIERDA (nvarchar(250)), DERECHA (nvarchar(250)), ID (int) |
| TABLE | `V_MV_TARJETAS_CONC` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), CUENTA (nvarchar(15)), FECHA (datetime), USUARIO (nvarchar(50)), OBSERVACIONES (nvarchar(250)) |
| TABLE | `V_MV_TPVScriptBaja` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | ID (int), Codigo (nvarchar(4)), FechaHora (datetime), Tabla (nvarchar(50)), Script (ntext), Ejecutado (datetime) |
| TABLE | `V_MV_TROPA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), IdTropa (int), Dte (nvarchar(15)), FechaDte (date), FechaVto (date), Comprador (nvarchar(15)), CuitComprador (nvarchar(50)), Vendedor (nvarchar(15)) |
| TABLE | `V_MV_TROPA_AUX` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idTropa (int), idPlanilla (int), usuario (nvarchar(250)), corral (nvarchar(10)), bultos (float), secuencia (int), usuarioPlanilla (nvarchar(150)) |
| TABLE | `V_MV_TROPA_AUXTOT` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idTropa (int), idPlanilla (int), categoria (nvarchar(10)), blD (float), blI (float), kg (float), usuario (nvarchar(250)) |
| TABLE | `V_MV_TROPA_CUARTEODESP` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idPlanilla (int), idTropa (int), Orden (int), Secuencia (int), kg (float), IdArticulo (nvarchar(25)), Bultos (float) |
| TABLE | `V_MV_TROPA_CUARTEO_CAB` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Id (int), IdPreparacion (int), FECHA_INICIO (datetime), FECHA_FINALIZACION (datetime), USUARIO (nvarchar(50)), CONTROLADOPOR (nvarchar(50)), OBSERVACIONES (nvarchar(250)), DEPOSITO (nvarchar(4)) |
| TABLE | `V_MV_TROPA_MENUDENCIAS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idPlanilla (int), idTropa (int), Orden (int), Secuencia (int), kg (float), IdArticulo (nvarchar(25)), Bultos (float) |
| TABLE | `V_MV_TROPA_PLANILLA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idPlanillaMatanza (int), fechahora (datetime), usuario (nvarchar(150)), finalizada (bit) |
| TABLE | `V_MV_TROPA_ROMANEO` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idPlanilla (int), idTropa (int), Orden (int), Secuencia (int), kgD (float), kgI (float), categoria (nvarchar(10)) |
| TABLE | `V_MV_TROPA_STOCK` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | id (int), idPlanilla (int), idTropa (int), comprador (nvarchar(15)), categoria (nvarchar(25)), secuencia (int), lado (nvarchar(1)), kg (float) |
| TABLE | `V_TA_Input_Ventas` | Tabla o vista de referencia/configuración. | Id (int), IdMotivoVta (nvarchar(4)), CondIva (nvarchar(4)), Repuestos (nvarchar(15)), Servicios (nvarchar(15)), Otros (nvarchar(15)), V_DFI_CTAVTA (nvarchar(15)), V_DFI_IMPINT (nvarchar(15)) |
| TABLE | `V_VENDEDOR_FAMILIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idVendedor (nvarchar(4)), idFamilia (nvarchar(15)) |
| TABLE | `V_VENDEDOR_ITEMS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idVendedor (nvarchar(4)), codigo (nvarchar(4)), tipo (nvarchar(1)) |
| VIEW | `Aux_AC_VentasDiaria` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Aux_Ac_VentasDiariaFPSIVA` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Aux_AC_VentasDiariaHis` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Aux_Ac_VentasMes` | Objeto auxiliar de soporte. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaVentas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIVAVentas_AFIP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaVentas_Cobranzas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `LibroIvaVentas_Contadores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Libro_VentasConFP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Libro_VentasPorArticulo` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `PL_VENTASDIARIASDETALLE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_VentasDIAMESANIO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_VentasDIAMESANIO_new` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_VentasMes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_COBRANZAS_REALIZADAS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_CREDITOS_PENDIENTES` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_CREDITOS_PENDIENTES_SINREMITOS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_CREDITOS_PENDIENTES_SINREMITOS_SALDO` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_CREDITOS_PENDIENTES_SINREMITOS_UNEG` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_CREDITOS_PEND_SINRM` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_IMPAGOS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_IMPAGOS_2026` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_HABER` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_TODOS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_TODOS_MONEDA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_TODOS_SALDO` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_TODOS_UNEG` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_UNEG` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_VENTAS` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_VENTASAM` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_VENTASV` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CPTES_SALDOS_VENTASV2` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CTACTE` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CTROL_SALDOAPLICACION` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CuentaFechaAux` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CuentaFechaAuxCC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_CUENTAS_FECHA_UNIDAD_AUX` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOAPLICACION` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOAPLICACION_HABER` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Ve_SaldosCtaAC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaAC_CCosto` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTACC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTACCAux` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTAFH` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaFhAcum` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaFhAcum2` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Ve_SaldosCtaFhAcumCC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Ve_SaldosCtaFhAcumCC2` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaFhAcumSPROF` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaFhAcumUnidades` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SaldosCtaFhAcumUnidades2` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTAFH_SPROFORMA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTAFH_UNEG_SPROFORMA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_DEBE` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_DEBECC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_HABER` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_HABERCC` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_SPROFORMA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_UNEG` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSCTA_UNEG_SPROFORMA` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSVDOR` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSVDOR_DEBE` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VE_SALDOSVDOR_HABER` | Vista/proceso asociado a ventas, cobranzas o saldos de clientes. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_Clientes_C_VENTASPORCLIENTE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_AC_VentasMes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_AC_VENTASMES_CATEGORIAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEVENTAS_COSTOS_COMPLETO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_mv_inventarios` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_Planilla_Ventas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VentaDiaria_TK_Unegocio` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VentaDiaria_Unegocio` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VentaDiaria_UnegocioFamilia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VentaDiaria_UnegocioFamiliaDesc` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VentaDiaria_UnegocioProveedor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_Aplicacion_OrdenDePago` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ArticulosPreciosHis` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ArticulosUltimoCosto` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_AsientosCCosto` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_Bonos_Recepcion` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ChequesConciliados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ChequesEnCartera` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ChequesEntregados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ChequesFirmados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ChequesRecibidos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_Cobranzas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_COBRANZASPORUSUARIO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_COBRANZAS_VDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_COBRANZAS_VDOR_FC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ConsDeRubroTipoProvArt` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ConsultaDeFamilia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ConsultaDeRubroTipoProveedor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ConsultaDeRubroTipoProveerdorArticulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_Diferencia03` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_DiferenciaCpte` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_DiferenciaCpte01` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_DiferenciaCpte02` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_EstadoBancario` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_ImportesAplicados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_MV_APLICACIONES_SIN_REMITOS_ASOCIADOS` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_MV_APLICACIONES_SIN_RM_ASOC` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `v_mv_CargosAplicados` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_MV_CPTE_DETALLE` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_MV_CPTE_DETALLE_SINIVA` | Movimiento o detalle de comprobantes de ventas / movimientos comerciales. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_NP_Pendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_OC_Pendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_OT_ANALISIS01` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_OT_Pendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_OT_PENDIENTES_SEGUNTAREAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_PR_Pendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosConCobranzas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosFacturados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosPendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosPendientes2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosPendientes2Saldos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosPendientesEnComodato` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosPendientesRPT` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `v_RemitosPendientesSinClientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_RemitosSinAplicacion` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TJCtaCte` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TJCupones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TJLiquidacionAsientos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TJLiquidacionCta` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjLiquidados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjLiquidadosDet` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjLiquidadosRes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjLotesRes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjParcialAux` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjParciales` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjPendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_TjPresentadas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_VENTAS_COBRANZAS_VDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_VENTAS_COBRANZAS_VDOR_CBFP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `V_VENTAS_VDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Artículos / Precios

Total de objetos: 42

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `Aux_Articulos` | Objeto auxiliar de soporte. | Id (int), IdArticulo (nvarchar(25)), IdDeposito (nvarchar(4)), Fecha (datetime), Col1 (money), Col2 (money), Col3 (money), Col4 (money) |
| TABLE | `AUX_BORRADOR_PRECIOS` | Objeto auxiliar de soporte. | ID (int), IdArticulo (nvarchar(25)), Descripcion (nvarchar(50)), EAN (nvarchar(25)), Presentacion (nvarchar(50)), CodigoProveedor (nvarchar(25)), Marca (nvarchar(50)), GrupoLista1 (money) |
| TABLE | `b_articulos_equivalencias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idarticulo1 (nvarchar(25)), idarticulo2 (nvarchar(25)) |
| TABLE | `Mv_NovedadesPrecios` | Tabla de movimientos del sistema. | Id (int), Codigo (int), IdLista (nvarchar(4)), Nombre (nvarchar(50)), Fecha (datetime), TipoMov (nvarchar(1)), CodInterno (nvarchar(9)), Descripcion (nvarchar(30)) |
| TABLE | `S_TA_EQUIV` | Equivalencias | ID (int), IDARTICULO (nvarchar(25)), IDUNIDAD (nvarchar(4)), IDUNIDAD_EQUIV (nvarchar(4)), COEFICIENTE (float), ParaTodos (bit), TRANSMISION (int) |
| TABLE | `TMP_BusquedaPrecio` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), Usuario (nvarchar(200)), IdArticulo (nvarchar(25)), Cantidad (float), IdLista (nvarchar(4)), NombreLista (nvarchar(100)), TipoLista (nvarchar(1)), Precio (money) |
| TABLE | `V_MA_ArtCatRel` | Categoria de Articulos Relacionadas | Id (int), Idarticulo (nvarchar(25)), IdCategoria (nvarchar(4)) |
| TABLE | `V_MA_ARTICULOS` | Maestro de Articulos Nota: Tiene identity | ID (int), IDARTICULO (nvarchar(25)), CODIGOBARRA (nvarchar(25)), DESCRIPCION (nvarchar(100)), CUENTAPROVEEDOR (nvarchar(15)), IDUNIDAD (nvarchar(4)), IDRUBRO (nvarchar(4)), IDTIPO (nvarchar(4)) |
| TABLE | `V_MA_ARTICULOS_REL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IDARTICULO (nvarchar(15)), RUTA_ARCHIVO (nvarchar(300)), OBSERVACIONES (nvarchar(500)), IMAGEN (image) |
| TABLE | `V_MA_Precios` | Listas de Precios Detalle Nota: Tiene identity, cambiar PK dejar IdLista + IdArticulo + TipoLista | IdLista (nvarchar(4)), Nombre (nvarchar(50)), IdArticulo (nvarchar(25)), ConIVA (bit), Precio1 (money), Precio2 (money), Precio3 (money), Precio4 (money) |
| TABLE | `V_MA_PreciosCab` | Listas de precios | Id (int), IdLista (nvarchar(4)), Nombre (nvarchar(50)), Grupo (nvarchar(10)), VigenciaDesde (datetime), VigenciaHasta (datetime), TipoLista (nvarchar(1)), Transmision (int) |
| TABLE | `V_MA_Precios_Borrador` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdLista (nvarchar(4)), Nombre (nvarchar(50)), IdArticulo (nvarchar(25)), ConIVA (bit), Precio1 (money), Precio2 (money), Precio3 (money), Precio4 (money) |
| TABLE | `V_MA_Precios_Comisiones_Vendedor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idLista (nvarchar(4)), idArticulo (nvarchar(25)), idVendedor (nvarchar(4)), comision (float) |
| TABLE | `V_TA_CategoriaArticulo` | Categoria de Articulos | IdCategoria (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `V_TA_PoliticaPrecios` | politicas de precios Nota: tiene Identity | Id (int), Codigo (nvarchar(4)), Clase1 (money), Clase2 (money), Clase3 (money), Clase4 (money), Clase5 (money), Descripcion (nvarchar(50)) |
| TABLE | `V_TA_TipoArticulo` | tipos de articulos o marcas Nota: tiene Identity | ID (int), IdTipo (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| VIEW | `SS_V_MA_ARTICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_V_MA_PRECIOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_V_MA_PRECIOS_BORRADOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_Articulos_Depositos_OC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Ma_Articulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_MA_ARTICULOS_DEPOSITOS_MM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Ma_Articulos_OLD` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Ma_Articulos_Padre_AgrupaSexoTalleColor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Ma_Articulos_Posiciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_MA_PRECIOS_DEPOSITOS_MM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_SaldoPrecios` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_CambiosPreciosProv` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CERTIFICADOS_ARTICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_INS_ARTICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_Articulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_PRECIOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_PRECIOS2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_PRECIOS_ARTICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PEDIDOSREPOSICION_ARTICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_PRECIOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_PRECIOS_RESUMIDO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_PRECIOS_RESUMIDOSINLISTA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_Pl_Articulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RubroTipoArticulosDescripciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ULTIMOCAMBIOPRECIO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `wsSysMobileStockComprometidoArticulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Stock / Depósitos

Total de objetos: 21

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `AUX_STOCK_TALLECOLORSEXO` | Objeto auxiliar de soporte. | IDDEPOSITO (nvarchar(4)), IDARTICULO (nvarchar(25)), TALLE (nvarchar(50)), COLOR (nvarchar(50)), SEXO (nvarchar(50)), STOCK (float) |
| TABLE | `Aux_V_MV_STOCK_LOCATOR` | Objeto auxiliar de soporte. | TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (nvarchar(1)), IDARTICULO (nvarchar(25)), DESCRIPCION (nvarchar(200)), NROLOTE (nvarchar(200)), IDUNIDADBASE (nvarchar(4)) |
| TABLE | `TMP_STOCK_FRANQUICIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdArticulo (nvarchar(25)), Descripcion (nvarchar(150)), Unidad (nvarchar(20)), Rubro (nvarchar(50)), CodigoTpv (nvarchar(4)), Franquicia (nvarchar(150)), Stock (float) |
| TABLE | `V_TA_ALERTAS_STOCK` | Tabla o vista de referencia/configuración. | ID (int), USUARIO (nvarchar(50)), IDARTICULO_DESDE (nvarchar(25)), IDARTICULO_HASTA (nvarchar(25)), IDRUBRO_DESDE (nvarchar(4)), IDRUBRO_HASTA (nvarchar(4)), IDTIPO_DESDE (nvarchar(4)), IDTIPO_HASTA (nvarchar(4)) |
| TABLE | `V_TA_DEPOSITO` | Tabla o vista de referencia/configuración. | ID (int), IdDeposito (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Capacidad (float), Unidad (nvarchar(4)), Empresa (nvarchar(100)) |
| TABLE | `V_TA_DEPOSITO_MM` | Tabla o vista de referencia/configuración. | Id (int), IdDeposito (nvarchar(4)), IdArticulo (nvarchar(25)), Minimo (float), Maximo (float), DDMM_DESDE (nvarchar(5)), DDMM_HASTA (nvarchar(5)), MINIMO_DDMM (float) |
| TABLE | `V_TA_MotivoStock` | Tabla o vista de referencia/configuración. | ID (int), IdMotivoStock (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), CuentaContable (nvarchar(15)), Transmision (int) |
| VIEW | `SS_V_TA_DEPOSITO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Stock_Equiv` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Stock_UDKG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `View_Stock` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DEPOSITO_MM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DEPOSITO_MM_STOCK` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEFRANQUICIA_STOCK` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_ART_sin_stock` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_TROPA_STOCK` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_FORMTOMASTOCK` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_STOCK_CodigoPadre_TalleColor` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Stock_Cons` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Stock_Cons3` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_STOCK_CONS4` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Tablas de referencia / Configuración

Total de objetos: 75

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `Ta_ArchivoLegal` | Tabla o vista de referencia/configuración. | ID (int), PERIODO (nvarchar(7)), TIPO (nvarchar(2)), TIPOREG (nvarchar(4)), TIPOARCHIVOLEGAL (nvarchar(1)), ARCHIVOGENERADO (nvarchar(255)), ARCHIVOINTERNO (ntext), EJERCICIO (int) |
| TABLE | `TA_CAI` | Tabla o vista de referencia/configuración. | ID (int), Cuenta (nvarchar(15)), CAI (nvarchar(50)), Vencimiento (datetime), TC (nvarchar(4)), Sucursal (nvarchar(4)), Letra (nvarchar(1)) |
| TABLE | `TA_CCOSTO` | Centros de Costos | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int), Recepcion (int), TITULO (bit) |
| TABLE | `TA_CLASIFICACIONES` | Tabla o vista de referencia/configuración. | Codigo (nvarchar(4)), Descripcion (nvarchar(50)), Color (int), fechahora_grabacion (datetime), fechahora_modificacion (datetime), Transmision (int), Id (int) |
| TABLE | `TA_CLASIFICACION_SERVICIOS` | Tabla o vista de referencia/configuración. | id (int), IdClasificacion (nvarchar(4)), IdTarea (nvarchar(4)) |
| TABLE | `TA_CODIGOSPOSTALES` | Tabla o vista de referencia/configuración. | Id (int), CP (nchar(13)), IDPROVINCIA (nchar(4)), LOCALIDAD (nchar(250)) |
| TABLE | `TA_COMPROBANTES` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), SISTEMA (nvarchar(20)), DEBE-HABER (nvarchar(1)), ES (nvarchar(1)), PIDEVENCIMIENTO (bit), ModificaNumeracion (bit), A_ULTIMO_NRO (int) |
| TABLE | `TA_CONDIVA` | Condicion de IVA | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `TA_CONFIGURACION` | Configuraciones dinámicas del sistema. Campos clave: CLAVE, VALOR, VALOR_AUX y GRUPO. | ID (int), GRUPO (nvarchar(50)), CLAVE (nvarchar(50)), VALOR (nvarchar(150)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `TA_CONFIG_FORM` | Tabla o vista de referencia/configuración. | ID (int), Formulario (nvarchar(100)), Usuario (nvarchar(50)), TipoObjeto (nvarchar(50)), Nombre (nvarchar(50)), DataField (nvarchar(50)), Propiedad (nvarchar(50)), Valor (nvarchar(500)) |
| TABLE | `TA_CONFIG_IMP_CPTE` | Tabla o vista de referencia/configuración. | ID (int), TC (nvarchar(4)), Descripcion (nvarchar(50)), Sistema (nvarchar(50)), Letra (nvarchar(1)), Impresora (nvarchar(500)), CantCopias (int), CantLineas (int) |
| TABLE | `TA_CONFIG_RPTS` | Tabla o vista de referencia/configuración. | ID (int), CODIGO (nvarchar(4)), NOMBRE (nvarchar(50)), PROYECTO (nvarchar(50)), CONFIGURACION (ntext), AlMenu (bit) |
| TABLE | `TA_COTIZACION` | Tabla o vista de referencia/configuración. | ID (int), FECHA_HORA (datetime), MONEDA1 (money), MONEDA2 (money), MONEDA3 (money), MONEDA4 (money), MONEDA5 (money) |
| TABLE | `TA_DistLocalidades` | Tabla o vista de referencia/configuración. | id (int), Localidad (nvarchar(100)), Distancia (float) |
| TABLE | `TA_ESTADOS` | Provincias | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int), CodJurisdiccion (nvarchar(10)) |
| TABLE | `TA_INTERFACES` | Tabla o vista de referencia/configuración. | NOMBRE (nvarchar(100)), SUBDIARIO (nvarchar(4)), TIPO_ARCHIVO (nvarchar(1)), SEPARADOR (nvarchar(3)), NOMBRE_ARCHIVO (nvarchar(200)), CALIFICADOR (nvarchar(1)), FECHA_BASICOS (nvarchar(100)), TC_BASICOS (nvarchar(100)) |
| TABLE | `TA_INTERFACES_DETALLE` | Tabla o vista de referencia/configuración. | INTERFACE (nvarchar(100)), NOMBRE_CAMPO (nvarchar(100)), TIPO_DATO (nvarchar(10)), LONGITUD (nvarchar(5)), FORMATO (nvarchar(50)), SECUENCIA (int) |
| TABLE | `TA_LOGOS` | Tabla o vista de referencia/configuración. | IDint (int), IDLOGO (char(50)), RUTA (char(255)), IMAGEN (image) |
| TABLE | `TA_MARCAVEHICULO` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)) |
| TABLE | `TA_MENU` | Tabla o vista de referencia/configuración. | id (int), Menu (nvarchar(50)), Titulo (nvarchar(50)), Clave (nvarchar(50)), Nombre (nvarchar(50)), Imagen (nvarchar(50)), Proceso (nvarchar(250)), Habilitado (bit) |
| TABLE | `TA_MONEDAS` | monedas | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `TA_MONEDAS_BILLETE` | Tabla o vista de referencia/configuración. | CUENTA (nvarchar(15)), Denominacion (float), Recargo (float) |
| TABLE | `Ta_NumArchivoLegal` | Tabla o vista de referencia/configuración. | ID (int), TIPO (nvarchar(2)), TIPOREG (nvarchar(4)), PERIODO (nvarchar(7)), PaginaDesde (int), PaginaHasta (int) |
| TABLE | `TA_PAISES` | Paises | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `TA_PUBLICACIONES` | Tabla o vista de referencia/configuración. | Archivo (nvarchar(255)), Referencia (nvarchar(255)), EJERCICIO (int) |
| TABLE | `TA_RANGOS_DATOS_ADIC` | Tabla o vista de referencia/configuración. | DESCRIPCION (nvarchar(50)), CUENTA-DESDE (nvarchar(15)), CUENTA-HASTA (nvarchar(15)), Vista (nvarchar(255)), PideVencimiento (bit), PideVencimientoHaber (bit), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `TA_REPORTES` | Tabla o vista de referencia/configuración. | Id (int), Nombre (nvarchar(50)), Archivo (nvarchar(255)), formula_Seleccion (ntext), formula_Ordenamiento (nvarchar(500)), PedirParametros (bit), SISTEMA (bit), Archivo2 (nvarchar(255)) |
| TABLE | `TA_TAREAS` | Tabla o vista de referencia/configuración. | USUARIO (nvarchar(50)), SISTEMA (nvarchar(50)), TAREA (nvarchar(50)) |
| TABLE | `TA_TARJETAS` | Tabla o vista de referencia/configuración. | Id (int), IdCliente (nchar(15)), Cuenta (nchar(15)), Emisor (nchar(10)) |
| TABLE | `TA_TIPICOS` | Tabla o vista de referencia/configuración. | Codigo (nvarchar(4)), Descripcion (nvarchar(150)), Campo (ntext), fechahora_grabacion (datetime), fechahora_modificacion (datetime) |
| TABLE | `TA_TIPODOCUMENTO` | Tipos de Documento (cuit, dni,etc...) | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `TA_TIPOVEHICULO` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)) |
| TABLE | `TA_TITCOPIADORES` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(100)), DESDE (nvarchar(15)), HASTA (nvarchar(15)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), TC (nvarchar(150)) |
| TABLE | `TA_USUARIOS` | Tabla o vista de referencia/configuración. | NOMBRE (nvarchar(50)), SISTEMA (nvarchar(50)), PASSWORD (nvarchar(40)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), CambiarProximoInicio (bit), V_ModificaPrecio (bit), V_CantidadNegativa (bit) |
| TABLE | `TA_VISTAS` | Tabla o vista de referencia/configuración. | ID (int), Nombre (nvarchar(255)), Campo (nvarchar(255)), TipoVista (nvarchar(2)) |
| TABLE | `V_TA_Bancos` | Tabla o vista de referencia/configuración. | ID (int), IdBanco (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `V_Ta_Cajas` | Tabla o vista de referencia/configuración. | Id (int), IdCajas (nvarchar(4)), Descripcion (nvarchar(50)), UNIDADNEGOCIO (nvarchar(4)) |
| TABLE | `V_TA_Categoria` | Categorias de Clientes/Proveedores Nota: tiene identity | ID (int), IdCategoria (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `V_TA_Colores` | Tabla o vista de referencia/configuración. | Color (nvarchar(100)), Porcentaje (int), Valor (money) |
| TABLE | `V_TA_Cpte` | Tipos de comprobantes. Define comportamiento por sistema, Debe/Haber y Entrada/Salida de stock. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), SISTEMA (nvarchar(20)), DEBEHABER (nvarchar(1)), ES (nvarchar(1)), PIDEVENCIMIENTO (bit), ModificaNumeracion (bit), A_ULTIMO_NRO (int) |
| TABLE | `V_TA_CTRL_CHEQUERAS` | Tabla o vista de referencia/configuración. | ID (int), IDBANCO (nvarchar(4)), SERIE (nvarchar(1)), NUMERO_DESDE (float), NUMERO_HASTA (float), CuentaBanco (nvarchar(15)), CuentaBancoDiferido (nvarchar(15)), Talonario (int) |
| TABLE | `V_TA_DESTINO` | Tabla o vista de referencia/configuración. | ID (int), IdDestino (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `V_TA_ESCALARETENCIONES` | Tabla o vista de referencia/configuración. | Id (int), IdRetencion (nvarchar(4)), Desde (money), Hasta (money), Importe (money), Porcentaje (money), SobreExcedente (money) |
| TABLE | `V_TA_FAMILIAS` | familias | IdFamilia (nvarchar(15)), Descripcion (nvarchar(100)), Transmision (int), MKBase (money), MkReal (money), IdPolitica (nvarchar(4)) |
| TABLE | `V_TA_FAMILIAS_USUARIOS` | Tabla o vista de referencia/configuración. | ID (int), IdFamilia (nvarchar(15)), GrupoUser (nvarchar(50)) |
| TABLE | `V_TA_FRECUENCIA_VDOR` | Tabla o vista de referencia/configuración. | idVendedor (nvarchar(4)), Cliente (nvarchar(15)), frecuencia (nvarchar(7)), observaciones (ntext), Orden (nvarchar(150)) |
| TABLE | `V_TA_INTERFACES` | Tabla o vista de referencia/configuración. | NOMBRE (nvarchar(100)), TIPO_ARCHIVO (nvarchar(1)), SEPARADOR (nvarchar(3)), NOMBRE_ARCHIVO (nvarchar(200)), CALIFICADOR (nvarchar(1)), TABLAS_QUE_ACTUALIZA (nvarchar(200)), Exporta (bit), SQLAvanzado (ntext) |
| TABLE | `V_TA_Interfaces_asignacionCampos` | Tabla o vista de referencia/configuración. | INTERFACE (nvarchar(100)), CAMPO_ORIGEN (nvarchar(100)), CAMPO_TABLA (nvarchar(100)), TABLA_ORIGEN (nvarchar(50)), CAMPO (nvarchar(100)), OPERADOR (nvarchar(4)), VALOR (nvarchar(200)), Secuencia (int) |
| TABLE | `V_TA_Interfaces_Detalle` | Tabla o vista de referencia/configuración. | INTERFACE (nvarchar(100)), NOMBRE_CAMPO (nvarchar(100)), TIPO_DATO (nvarchar(10)), LONGITUD (nvarchar(5)), FORMATO (nvarchar(50)), SECUENCIA (int) |
| TABLE | `V_TA_Interfaces_EquiSueldo` | Tabla o vista de referencia/configuración. | Tipo (nvarchar(1)), Secuencia (int), Codigo (nvarchar(15)), Cuenta (nvarchar(15)), DebeHaber (nvarchar(1)), Aportes (bit) |
| TABLE | `V_TA_Interfaces_Equivalencia` | Tabla o vista de referencia/configuración. | INTERFACE (nvarchar(100)), CAMPO_ORIGEN (nvarchar(100)), NUEVO_VALOR (nvarchar(200)), CAMPO (nvarchar(100)), OPERADOR (nvarchar(4)), VALOR (nvarchar(200)), Secuencia (int) |
| TABLE | `V_TA_INTERFACES_FILTROS` | Tabla o vista de referencia/configuración. | ID (int), NOMBRE_INTERFACE (nvarchar(100)), CAMPO (nvarchar(50)), OPERADOR (nvarchar(2)), VALOR (nvarchar(200)), OPERADOR_LOGICO (nvarchar(25)) |
| TABLE | `V_Ta_InterODBC` | Tabla o vista de referencia/configuración. | Id (int), Proveedor (nvarchar(50)), Odbc (nvarchar(250)), Sentencia (ntext), CuentaProveedor (nvarchar(15)), PoliticaPrecios (nvarchar(4)), FHACTUALIZACION (datetime), tabla (nvarchar(250)) |
| TABLE | `V_TA_MOTIVOSFALLA` | Tabla o vista de referencia/configuración. | id (int), codigo (nvarchar(4)), descripcion (nvarchar(150)) |
| TABLE | `V_TA_MotivoVta` | Motivo de Venta Nota: tiene identity | id (int), IdMotivoVta (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `V_TA_Origen` | Tabla o vista de referencia/configuración. | ID (int), IdOrigen (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `V_TA_Percepcion` | percepciones | idPercepcion (nvarchar(4)), Descripcion (nvarchar(100)), MinimoNoImponible (money), Percepcion (money), CIVA_Excluir1 (nvarchar(4)), CIVA_Excluir2 (nvarchar(4)), CIVA_Excluir3 (nvarchar(4)), IVA (bit) |
| TABLE | `V_TA_ProvEventuales` | Tabla o vista de referencia/configuración. | ID (int), NOMBRE (nvarchar(50)), CONDIVA (nvarchar(4)), CUIT (nvarchar(15)) |
| TABLE | `V_TA_RETENCIONES` | Retenciones Nota: tiene Identity | Id (int), IdRetencion (nvarchar(4)), Descripcion (nvarchar(100)), MinimoNoImponible (money), MinimoExcluido (money), PORCENTAJE (money), CUENTA (nvarchar(15)), NoAcumulativa (bit) |
| TABLE | `V_TA_Rubros` | rubros Nota: tiene Identity | ID (int), IdRubro (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int), COLOR (int), Pesable (bit) |
| TABLE | `V_TA_SCRIPT` | Tabla o vista de referencia/configuración. | ID (int), IdLista (nvarchar(4)), RutaArchivo (nvarchar(100)), Password (nvarchar(50)), SQL (ntext), Usuario (nvarchar(50)), Tipo (nvarchar(50)), Marca (nvarchar(15)) |
| TABLE | `V_TA_SCRIPT_CFG` | Tabla o vista de referencia/configuración. | ID (int), IdScript (int), CampoSel (nvarchar(100)), EsParametro (bit), TablaConsulta (nvarchar(100)), CampoRetorno (nvarchar(100)) |
| TABLE | `V_TA_STATUS` | Tabla o vista de referencia/configuración. | CodigoEstado (nvarchar(4)), Descripcion (nvarchar(100)), Color (float), Tipo (char(1)), PosiblesEstadoPrevio (ntext) |
| TABLE | `V_TA_Tareas` | Tabla o vista de referencia/configuración. | ID (int), IdTarea (nvarchar(4)), Descripcion (nvarchar(250)), HorasEstimadas (float), ValorHora (money), ModificaValor (bit), Exento (bit), IDTecnico (nvarchar(4)) |
| TABLE | `V_TA_TarifaFlete` | tarifas de flete | IdTarifaFlete (nvarchar(4)), Descripcion (nvarchar(50)), Importe (money), Tipo (nvarchar(1)), Transmision (int) |
| TABLE | `V_TA_TarifasEnco` | Tabla o vista de referencia/configuración. | ID (int), IdTarea (nvarchar(4)), UnidadMedida (nvarchar(4)), IdUnegOrigen (nvarchar(4)), IdUnegDestino (nvarchar(4)), Cantidad (money), Banda (money), Valor (money) |
| TABLE | `V_TA_Tecnicos` | Tabla o vista de referencia/configuración. | ID (int), IdTecnico (nvarchar(4)), Nombre (nvarchar(100)), Cargo (nvarchar(100)), Domicilio (nvarchar(100)), Localidad (nvarchar(100)), IdProvincia (nvarchar(4)), Telefono (nvarchar(50)) |
| TABLE | `V_TA_TIPOACEITES` | Tabla o vista de referencia/configuración. | Codigo (nvarchar(4)), Descripcion (nvarchar(50)), fechahora_grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `V_TA_TPV` | Tabla o vista de referencia/configuración. | CODIGO (nvarchar(4)), SUCURSAL (nvarchar(4)), DESCRIPCION (nvarchar(100)), SERVER (nvarchar(100)), DBNAME (nvarchar(100)), USUARIO (nvarchar(100)), PASSWORD (nvarchar(50)), CONECTADO (bit) |
| TABLE | `V_TA_Unidad` | unidade de medida Nota: tiene Identity | ID (int), IdUnidad (nvarchar(4)), Descripcion (nvarchar(50)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Transmision (int) |
| TABLE | `V_TA_UnidadNegocio` | Unidades de Negocio | Codigo (nvarchar(4)), Descripcion (nvarchar(50)), DepositosAsociados (nvarchar(250)), SUCURSALCUENTA (int), GRUPO (nvarchar(50)), IdDepositoDif (nvarchar(4)), MotivoParaDiferencias (nvarchar(4)), ModificaDiferencias (bit) |
| TABLE | `V_TA_VENDEDORES` | Vendedores Nota: tiene identity | Id (int), IdVendedor (nvarchar(4)), Nombre (nvarchar(100)), Domicilio (nvarchar(100)), Localidad (nvarchar(100)), IdProvincia (nvarchar(4)), CodigoPostal (nvarchar(50)), IdTipoDocumento (nvarchar(4)) |
| TABLE | `V_TA_VendedoresCFam` | Tabla o vista de referencia/configuración. | idVendedor (nvarchar(4)), idFamilia (nvarchar(15)), PorcentajeComision (numeric(10, 5)), ImporteComision (money) |
| TABLE | `V_TA_VendedoresCm` | Comision de Vendedores según descuento otorgado Nota: * tiene identity, cambiar PK (dejar idvendedor + descuento) | Id (int), IdVendedor (nvarchar(4)), Descuento (float), CmVta (float), CmCob (float) |
| TABLE | `V_TA_VendedoresCMarc` | Tabla o vista de referencia/configuración. | IdVendedor (nvarchar(4)), IdTipo (nvarchar(4)), PorcentajeComision (numeric(10, 5)), ImporteComision (money) |

### Personal / RRHH

Total de objetos: 84

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `P_MA_Clientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdCliente (nvarchar(15)), Razon_Social (nvarchar(100)), Domicilio (nvarchar(100)), Localidad (nvarchar(50)), CodPostal (nvarchar(9)), IdProvincia (nvarchar(4)), IdPais (nvarchar(4)) |
| TABLE | `P_MA_CONCEPTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Concepto (nvarchar(3)), Descripcion (nvarchar(50)), Prioridad (int), Periodicidad (nvarchar(2)), Importe_Fijo (nvarchar(15)), Porcentaje_Fijo (float), Cantidad (nvarchar(10)), Baja (bit) |
| TABLE | `P_MA_CONCEPTOSAFIP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Concepto (nchar(3)), ConceptoAFIP (nchar(6)), Descripcion (nchar(150)), Repeticion (bit), A_SIPA (bit), C_SIPA (bit), A_INSSJYP (bit) |
| TABLE | `P_MA_Empresas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEmpresa (nvarchar(15)), NombreEmpresa (nvarchar(100)), Calle (nvarchar(50)), Numero (nvarchar(50)), Piso (nvarchar(8)), Dpto (nvarchar(8)), Localidad (nvarchar(50)) |
| TABLE | `P_MA_Familiar` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEmpresa (nvarchar(15)), Legajo (nvarchar(15)), Apellido (nvarchar(50)), Nombre (nvarchar(50)), ApellidoCasada (nvarchar(50)), IdTipoDocumento (nvarchar(4)), NroDocumento (nvarchar(15)) |
| TABLE | `P_MA_Legajos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEmpresa (nvarchar(15)), Legajo (nvarchar(15)), Apellido (nvarchar(50)), ApellidoCasada (nvarchar(50)), Nombre (nvarchar(100)), Calle (nvarchar(50)), Nro (int) |
| TABLE | `P_MA_LEGAJOS_ARCHIVOS_RELACIONADOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), EMPRESA (nvarchar(15)), LEGAJO (nvarchar(15)), ETIQUETA (nvarchar(100)), RUTA_ARCHIVO (nvarchar(254)) |
| TABLE | `P_MA_LEGAJOS_INDUMENTARIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), EMPRESA (nvarchar(15)), LEGAJO (nvarchar(15)), IDINDUMENTARIA (nvarchar(4)), TALLE (nvarchar(15)), OBSERVACIONES (nvarchar(200)) |
| TABLE | `P_MA_PostulanteAuxiliar` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), USUARIO (nvarchar(100)), Secuencia (int), IdPostulante (int), IdIdiomas (nvarchar(4)), IdFunciones (nvarchar(4)), Tiempo (int), IdTitulos (nvarchar(4)) |
| TABLE | `P_MA_Postulantes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdPostulante (int), IdTipoDocumento (nvarchar(4)), NroDocumento (nvarchar(15)), Nombre (nvarchar(100)), FechaAlta (datetime), SueldoDesde (money), SueldoHasta (money), Sexo (nvarchar(1)) |
| TABLE | `P_MA_Postulantes_Conocimientos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), IdConocimiento (nvarchar(4)), Observaciones (nvarchar(100)) |
| TABLE | `P_MA_Postulantes_Experiencia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), IdFunciones (nvarchar(4)), Tiempo (int), IdAreas (nvarchar(4)), PDesde (datetime), PHasta (datetime) |
| TABLE | `P_MA_Postulantes_Idiomas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), IdIdiomas (nvarchar(4)) |
| TABLE | `P_MA_Postulantes_Obs` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), Fecha (datetime), Observaciones (nvarchar(500)), Usuario (nvarchar(100)) |
| TABLE | `P_MA_Postulantes_Titulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), IdTitulos (nvarchar(4)), IdNivelEstudio (nvarchar(4)) |
| TABLE | `P_MV_Busquedas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdBusqueda (int), IdPostulante (int), FechaProceso (datetime), Seleccionado (bit), FechaHoraEntrevista (datetime), Usuario (nvarchar(100)) |
| TABLE | `P_MV_CtrolHorarios` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdEmpresa (nvarchar(15)), Legajo (nvarchar(15)), FechaOperativa (datetime), Ingreso (datetime), Egreso (datetime), Observaciones (nvarchar(100)), ApellidoNombre (nvarchar(150)) |
| TABLE | `P_MV_CtrolLiq` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Empresa (nvarchar(15)), Nro_Liq (int), Periodo_Liq (nvarchar(7)), Descripcion (nvarchar(50)), Fecha_Pago (datetime), Lugar_Pago (nvarchar(50)), Fecha_Deposito (datetime), Banco_Depositado (nvarchar(4)) |
| TABLE | `P_MV_Deudores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), Fecha (datetime), IdCliente (nvarchar(15)), IdBusqueda (int), ImporteFacturar (money), ImportePagado (money), FechaPago (datetime), Observaciones (nvarchar(255)) |
| TABLE | `P_MV_Entrevistas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), TipoEntrevista (nvarchar(1)), IdPostulante (int), FechaHora (datetime), Responsable (nvarchar(100)), Resultado (nvarchar(500)), Procesado (bit), Aceptado (bit) |
| TABLE | `P_MV_LegExc` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Empresa (nvarchar(15)), Nro_Liq (int), Legajo (nvarchar(15)) |
| TABLE | `P_MV_Liquidaciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | EMPRESA (nvarchar(15)), NRO_LIQ (int), LEGAJO (nvarchar(15)), PRIORIDAD (int), CONCEPTO (nvarchar(3)), CANTIDAD (float), VALOR_UNITARIO (money), IMPORTE (money) |
| TABLE | `P_MV_LIQUIDACIONESCAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | EMPRESA (nvarchar(15)), NRO_LIQ (int), LEGAJO (nvarchar(15)), JORNALIZADO (bit), IDSINDICATO (nvarchar(4)), IDSIND_CATEGORIA (nvarchar(4)), IDSIND_CARGO (nvarchar(4)), SUELDOBASICO (money) |
| TABLE | `P_MV_NovedadesGcias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Empresa (nvarchar(15)), Legajo (nvarchar(15)), Periodo (nvarchar(7)), GastosSepelio (money), PrimasSeguro (money), Donaciones (money), Deducciones (money) |
| TABLE | `P_MV_NOVEDADES_DIASTRAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Empresa (nvarchar(15)), Legajo (nvarchar(15)), Nro_Liq (int), Dias (int) |
| TABLE | `P_MV_NOVEDADES_VARIABLES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Empresa (nvarchar(15)), Legajo (nvarchar(15)), NRO_LIQ (int), Concepto (nvarchar(3)), Cantidad (float), Importe (money) |
| TABLE | `P_MV_Referencias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), Fecha (datetime), Empresa (nvarchar(100)), Comentarios (nvarchar(255)), Telefonos (nvarchar(100)), Usuario (nvarchar(100)) |
| TABLE | `P_MV_RETGciasAcumuladas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Empresa (nvarchar(15)), Legajo (nvarchar(15)), Periodo (nvarchar(7)), Nro_Liq (int), CargasFamilia (money), HaberesDelMes (money), ImpuestoResultante (money) |
| TABLE | `P_MV_Seguimiento` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdPostulante (int), IdCliente (nvarchar(15)), Fecha (datetime), Contratado (bit), IdProcesoBusqueda (int) |
| TABLE | `P_MV_SolicitudRRHH` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdBusqueda (int), FechaSolicitud (datetime), IdCliente (nvarchar(15)), IdFunciones (nvarchar(4)), IdAreas (nvarchar(4)), SueldoDesde (money), SueldoHasta (money) |
| TABLE | `P_MV_SolicitudRRHH_Conocimientos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdBusqueda (int), IdConocimiento (nvarchar(4)) |
| TABLE | `P_MV_SolicitudRRHH_Idiomas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdBusqueda (int), IdIdiomas (nvarchar(4)) |
| TABLE | `P_MV_SolicitudRRHH_Titulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdBusqueda (int), IdTitulos (nvarchar(4)) |
| TABLE | `P_TA_Areas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdAreas (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_Categorizacion` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdCategorizacion (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_CentroMedico` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdCentroMedico (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_CONC_LEGAJO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), Empresa (nvarchar(15)), Legajo (nvarchar(15)), Concepto (nvarchar(3)), Cantidad (float), Importe (float), FechaDesde (datetime), FechaHasta (datetime) |
| TABLE | `P_Ta_Conocimientos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdConocimiento (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_EscalaRET_Ganancias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), Periodo (nvarchar(7)), Desde (money), Hasta (money), Importe (money), Porcentaje (int), SobreExcedente (money) |
| TABLE | `P_TA_EstadoBusqueda` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEstadoBusqueda (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_EstadoCivil` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEstadoCivil (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_EstadoPostulante` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdEstadoPostulante (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_FormasDePago` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdFormasDePago (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_FORMULAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Codigo (nvarchar(30)), Descripcion (nvarchar(50)), Formula (ntext) |
| TABLE | `P_TA_Funciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdFunciones (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_Grupos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdGrupo (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_Idiomas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdIdiomas (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_INDUMENTARIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), IdIndumentaria (nvarchar(4)), Descripcion (nvarchar(200)) |
| TABLE | `P_TA_INTERFACES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | TIPO (nvarchar(4)), CLAVE (nvarchar(50)), DESDE (int), LONGITUD (int), TABLA (nvarchar(50)), CAMPO (nvarchar(50)), VALOR (nvarchar(500)), EDITABLE (bit) |
| TABLE | `P_TA_MotivoEgreso` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdMotivoEgreso (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_NivelEstudio` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdNivelEstudio (nvarchar(4)), Descripcion (nvarchar(100)) |
| TABLE | `P_TA_ObraSocial` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdObraSocial (nvarchar(3)), Descripcion (nvarchar(50)), SIJP_CODIGO (nvarchar(50)) |
| TABLE | `P_TA_Parentesco` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdParentesco (nvarchar(4)), Descripcion (nvarchar(50)), Imagen (image) |
| TABLE | `P_TA_RET_Ganancias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), Periodo (nvarchar(7)), Descripcion (nvarchar(200)), FormulaAdicOS (nvarchar(3)), MinimoNoImponible (money), DeduccionEspecial (money), Conyuge (money), Hijos (money) |
| TABLE | `P_TA_Sindicatos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdSindicato (nvarchar(3)), IdSind_Categoria (nvarchar(4)), IdSind_Cargo (nvarchar(4)), Descripcion (nvarchar(50)), DescripcionCargo (nvarchar(50)), HorasConvenio (int), Basico (money) |
| TABLE | `P_TA_TipoPuesto` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdTipoPuesto (nvarchar(4)), Descripcion (nvarchar(50)) |
| TABLE | `P_TA_Titulos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdTitulos (nvarchar(4)), Descripcion (nvarchar(100)) |
| TABLE | `P_TA_VARIOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), tabla (nvarchar(50)), descripcion (nvarchar(250)), valor (nvarchar(25)) |
| TABLE | `P_TA_ZonaResidencia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), IdZonaResidencia (nvarchar(4)), Descripcion (nvarchar(50)) |
| VIEW | `P_AUX_LEGAJOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_Entrevistas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_ENTREVISTASEVALUACIONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_EntrevistasFH` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_EntrevistasPendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_EntrevistasPendientesFh` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_Evaluaciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_EvaluacionesPendientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_SaldosClientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_SaldosClientesBusquedas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_SindCatCargos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_Sindicatos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_Sind_Categorias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_Solucitudes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_SolucitudesAFinalizar` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_SolucitudesPostulantes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_TodasLasSolicitudes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_TodasLasSolicitudesPost` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_TodasLasSolicitudesPostulantes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_CONCEPTOSACUM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_RESUMENCONC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_RESUMENCONCLEG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_RESUMENCONCLEG2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_RESUMENLEG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `P_VT_RESUMENLEG_CC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Centros de costo

Total de objetos: 7

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| VIEW | `VT_ASIENTOS_CCOSTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_CCOSTO1` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_CCOSTOCUBE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_CCOSTO_CTRL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CCOSTOS_GASTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CCOSTO_ASIENTOS_CON` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CCOSTO_ASIENTOS_SIN` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Movimientos generales

Total de objetos: 40

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `MV_Agenda` | Tabla de movimientos del sistema. | Id (int), NombreAgenda (nvarchar(50)), Fecha (datetime), Mensaje (nvarchar(255)), FechaAviso (datetime), NotasDelDia (nvarchar(255)), IdProcesoBusqueda (int), IdEntrevista (int) |
| TABLE | `MV_AGENDAMEMO` | Tabla de movimientos del sistema. | ID (int), NombreAgenda (nvarchar(50)), Fecha (datetime), Asunto (nvarchar(100)), Mensaje (nvarchar(255)), De (nvarchar(50)), Para (nvarchar(50)), FechaLeido (datetime) |
| TABLE | `Mv_AgendaNovedades` | Tabla de movimientos del sistema. | Id (int), FechaHora (datetime), Usuario (nvarchar(50)), IdTarea (nvarchar(4)), Cuenta (nvarchar(15)), Detalle (ntext), Adjunto (ntext), Inicio (datetime) |
| TABLE | `Mv_AgendasCompartidas` | Tabla de movimientos del sistema. | ID (int), Usuario (nvarchar(50)), UsuarioCompartido (nvarchar(50)) |
| TABLE | `MV_ALERTA` | Tabla de movimientos del sistema. | ID (int), FechaHora (datetime), Cuenta (nvarchar(15)), Activo (int), Mensaje (nvarchar(250)), Usuario (nvarchar(50)), FhDesde (datetime), FhHasta (datetime) |
| TABLE | `MV_CALCULO_BIENES` | Tabla de movimientos del sistema. | CODIGO (float), ID (int), CUENTA (nvarchar(15)), PERIODO (int), FECHA_CIERRE (datetime), PERIODO_DE_CALCULO (nvarchar(50)), COSTO (money), COEFICIENTE (float) |
| TABLE | `MV_CASHFLOW_CAB` | Tabla de movimientos del sistema. | id (int), fecha_desde (date), fecha_hasta (date), cuenta (nvarchar(20)), descCuenta (nvarchar(150)), saldo (float), comentarios (nvarchar(250)), periodo (nvarchar(1)) |
| TABLE | `MV_CASHFLOW_DET` | Tabla de movimientos del sistema. | idCash (int), saldoAnt (float), fecha (date), pagos (float), cartera (float), egresos (float), saldo (float), supuestos (float) |
| TABLE | `MV_CONCILIACION` | Tabla de movimientos del sistema. | ID (int), IdConciliacion (nvarchar(15)), Cuenta (char(15)), Secuencia (int), Mes_Operativo (tinyint), Numero Asiento (int), Periodo (nvarchar(6)), Tipo_Reg (nvarchar(4)) |
| TABLE | `MV_CONCILIACION_AUX` | Tabla de movimientos del sistema. | ID (int), IdConciliacion (nvarchar(15)), Cuenta (nvarchar(15)), Secuencia (int), Mes_Operativo (tinyint), Numero Asiento (int), Periodo (nvarchar(6)), Tipo_reg (nvarchar(4)) |
| TABLE | `MV_CONCILIACION_CAB` | Tabla de movimientos del sistema. | IdConciliacion (nvarchar(15)), Fecha (datetime), Cuenta (nchar(15)), Observaciones (nchar(255)), Usuario (nchar(50)), FechaDesde (datetime), FechaHasta (datetime), UNegocio (nvarchar(4)) |
| TABLE | `MV_CONCILIACION_Cpte_AUX` | Tabla de movimientos del sistema. | Id (int), IdCpte (int), IdCpteAux (int) |
| TABLE | `MV_CONCILIACION_OPEN` | Tabla de movimientos del sistema. | ID (int), IDCONCILIACION (nvarchar(15)), MOTIVO (nvarchar(500)), FECHA (datetime), USUARIO (nvarchar(250)), ESTADO (bit) |
| TABLE | `MV_CONTENEDORES` | Tabla de movimientos del sistema. | id (int), tc (nvarchar(4)), idcomprobante (nvarchar(13)), idarticulo (nvarchar(25)), cantidad (float), idcontenedor (int) |
| TABLE | `MV_CONTENEDOR_ETIQUETAS` | Tabla de movimientos del sistema. | Id (int), IDPREPARACION (int), CUENTA (nvarchar(15)), ETIQUETAS (int), BULTOS (int), Impreso (bit) |
| TABLE | `MV_CONTROL_AS_RES` | Tabla de movimientos del sistema. | PERIODO (int), MES_OPERATIVO (tinyint), ID (int), SUBDIARIO (nvarchar(4)), FECHA_DESDE (datetime), FECHA_HASTA (datetime), ANULADO (bit), RESPONSABLE (nvarchar(50)) |
| TABLE | `MV_DETALLEIVAPROFORMA_COMPLETO` | Tabla de movimientos del sistema. | id (int), FECHA (date), FechaEmision (date), HoraEmision (nvarchar(50)), DiaDelaSemana (nvarchar(50)), DiaDelMes (nvarchar(10)), Hora (nvarchar(5)), TC (nvarchar(4)) |
| TABLE | `MV_EJERCICIOS` | Tabla de movimientos del sistema. | PERIODO (int), DESCRIPCION (nvarchar(50)), FECHA DESDE (datetime), FECHA HASTA (datetime), CERRADO (bit), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime), Cancelacion (bit) |
| TABLE | `MV_Eventos` | Tabla de movimientos del sistema. | id (int), Nombre (nvarchar(100)), Lugar (nvarchar(100)), FhDesde (nvarchar(10)), FhHasta (nvarchar(10)), Confirmado (bit), Convocante (nvarchar(100)), Organizador (nvarchar(100)) |
| TABLE | `MV_EventosEP` | Tabla de movimientos del sistema. | IdEvento (int), IdEmpresa (nvarchar(15)), Contacto (nvarchar(100)), Porcentaje (money) |
| TABLE | `MV_Ingresos` | Tabla de movimientos del sistema. | Id (int), ApellidoNombre (varchar(255)), Ingreso (datetime), Egreso (datetime), Observaciones (text), Adultos (int), PrecioAdultos (money), Menores (int) |
| TABLE | `MV_Ingresos_Clientes` | Tabla de movimientos del sistema. | Id (int), ApellidoNombre (varchar(255)), Dni (varchar(20)), Nacionalidad (varchar(100)), Direccion (varchar(100)), ModeloVehiculo (varchar(100)), Ciudad (varchar(100)), Patente (varchar(20)) |
| TABLE | `MV_LIBROIVA_EXENTOLEY` | Tabla de movimientos del sistema. | id (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), COND_IVA (nvarchar(4)), NETO21 (money), NETO10 (money), FECHA (datetime) |
| TABLE | `MV_PEDIDOS` | Tabla de movimientos del sistema. | IdComprobante (nvarchar(13)), Empresa (nvarchar(100)), Fecha (nvarchar(10)), IdEmpresa (nvarchar(15)), Contacto (nvarchar(100)), IdEvento (int), Medidas_Stand (nvarchar(50)), Produccion (nvarchar(50)) |
| TABLE | `MV_PEDIDOS_ARCH` | Tabla de movimientos del sistema. | Id (int), IdComprobante (nvarchar(15)), NroArchivo (nvarchar(10)), Fecha (nvarchar(10)), Ruta (nvarchar(500)), TipoArchivo (nvarchar(10)) |
| TABLE | `MV_PREPARACION` | Tabla de movimientos del sistema. | ID (int), IDPREPARACION (int), FECHA_INICIO (datetime), FECHA_FINALIZACION (datetime), IDTECNICO (nvarchar(4)), USUARIO (nvarchar(50)), CONTROLADOPOR (nvarchar(50)), OBSERVACIONES (nvarchar(250)) |
| TABLE | `MV_PREPARACION_DETALLES` | Tabla de movimientos del sistema. | ID (int), IDPREPARACION (int), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDARTICULO (nvarchar(25)), IDUNIDAD (char(4)), CANTIDAD (float), CANTIDADPREPARADA (float) |
| TABLE | `MV_PROYECCIONES` | Tabla de movimientos del sistema. | Cuenta (nvarchar(15)), Vencimiento (datetime), Usuario (nvarchar(250)), Importe (money), Unegocio (nvarchar(4)), Conciliado (bit), Tipo (nvarchar(1)), Anulado (bit) |
| TABLE | `MV_SALDOS` | Tabla de movimientos del sistema. | CUENTA (nvarchar(15)), PERIODO (nvarchar(6)), SALDO (money), MONEDA (nvarchar(4)), MES-A¸O (nvarchar(6)), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `MV_TASK` | Tabla de movimientos del sistema. | ID (int), Descripcion (nvarchar(250)), IdTecnico (nvarchar(4)), FhHsAlta (datetime), FhHsFin (datetime), IDTarea (int), IDRepeticion (int), ORDEN (int) |
| TABLE | `MV_Transmisiones` | Tabla de movimientos del sistema. | Tipo (nvarchar(1)), Transmision (int), Fecha (datetime), Hora (nvarchar(8)), Fecha_Desde (datetime), Fecha_Hasta (datetime), Registros_enviados (float), Registros_Recibidos (float) |
| TABLE | `MV_TransmisionesCFG` | Tabla de movimientos del sistema. | Nombre (nvarchar(50)), UNegocio (nvarchar(4)), TC (nvarchar(4)), TMaestros (ntext), TMovimientos (ntext), RutaArchivos (nvarchar(500)), EnviaMail (bit), RetransMaestros (bit) |
| TABLE | `MV_TransmisionesCodigos` | Tabla de movimientos del sistema. | id (int), Codigo (nvarchar(25)), FechaHora (datetime2(7)), Zona (nvarchar(25)) |
| TABLE | `MV_TransmisionesCTROL` | Tabla de movimientos del sistema. | Tipo (nchar(1)), Transmision (int), Unegocio (nvarchar(4)), Tabla (nvarchar(50)), Fecha (datetime), Codigo (nvarchar(15)), Sucursal (nvarchar(4)), Importe (float) |
| TABLE | `MV_TransmisionesERR` | Tabla de movimientos del sistema. | ID (int), Tipo (nchar(1)), Transmision (int), Tabla (nvarchar(50)), Error (int), Descripcion (nvarchar(250)), Sentencia (ntext) |
| TABLE | `MV_TransmisionesLOG` | Tabla de movimientos del sistema. | ID (int), Nro (nvarchar(50)), NroRecep (nvarchar(50)), Tipo (nvarchar(10)), Tabla (nvarchar(50)), TC (nvarchar(4)), Fecha (nvarchar(10)), Registros (float) |
| TABLE | `MV_VIAJES` | Tabla de movimientos del sistema. | ID (int), TC (char(4)), IDCOMPROBANTE (nvarchar(13)), FECHA (datetime), VEHICULO (nvarchar(7)), CHOFER (nvarchar(4)), NOMBRE_CHOFER (nvarchar(100)), PRECINTO (nvarchar(250)) |
| TABLE | `MV_VIAJES_TROPA_AUX` | Tabla de movimientos del sistema. | id (int), usuario (nvarchar(50)), nroViaje (nvarchar(13)), chofer (nvarchar(4)), destino (nvarchar(150)), idcomprobante (nvarchar(13)), tc (nvarchar(4)), medias (int) |
| VIEW | `MV_COT_CAB` | Tabla de movimientos del sistema. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `MV_COT_DET` | Tabla de movimientos del sistema. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

### Auxiliares / Temporales

Total de objetos: 32

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `AUX_ANALISISCOSTOS` | Objeto auxiliar de soporte. | ID (int), IdArticulo (nvarchar(25)), L1 (nvarchar(4)), L1_Utl (float), L1_PVenta (float), L1_Iva (float), L1_FhOferta_Desde (datetime), L1_FhOferta_Hasta (datetime) |
| TABLE | `AUX_ANEXOA` | Objeto auxiliar de soporte. | USUARIO (nvarchar(50)), CUENTA_TITULO (nvarchar(15)), CUENTA (nvarchar(15)), CODIGO (int), ID (int), COLUMNA2 (money), COLUMNA3 (money), COLUMNA4 (money) |
| TABLE | `AUX_BALANCEGRAL` | Objeto auxiliar de soporte. | CUENTA (nvarchar(15)), SUBCUENTA_D (money), SUBCUENTA_H (money), CUENTA_D (money), CUENTA_H (money), SUBRUBRO_D (money), SUBRUBRO_H (money), RUBRO_D (money) |
| TABLE | `AUX_ERR` | Objeto auxiliar de soporte. | ID (int), Proceso (nvarchar(150)), Fecha (datetime), Error (float), Descripcion (nvarchar(250)), Sql (ntext), Pc (nvarchar(150)), Usuario (nvarchar(50)) |
| TABLE | `AUX_EstadisticaCta` | Objeto auxiliar de soporte. | ID (int), USUARIO (nvarchar(50)), NOMBRE (nvarchar(100)), GRUPO (nvarchar(100)), FECHAD (datetime), FECHAH (datetime), IMPORTEG1 (money), IMPORTEG2 (money) |
| TABLE | `AUX_ESTADISTICAFH` | Objeto auxiliar de soporte. | Tipo (nvarchar(4)), Fecha (datetime) |
| TABLE | `AUX_ESTADISTICAMES` | Objeto auxiliar de soporte. | TIPO (nvarchar(4)), FECHA (nvarchar(7)) |
| TABLE | `AUX_GARANTIA` | Objeto auxiliar de soporte. | id (int), modelo (nvarchar(50)), serie (nvarchar(50)), nroTransmision (int), eca1 (nvarchar(50)), activacion (nvarchar(50)) |
| TABLE | `AUX_IMPRESION` | Objeto auxiliar de soporte. | ID (int), USUARIO (nvarchar(150)), CUENTA (nvarchar(15)), NOMBRE (nvarchar(250)), DETALLE (nvarchar(250)), FECHA (datetime), VENCIMIENTO (datetime), IMPORTE (money) |
| TABLE | `aux_Maps` | Objeto auxiliar de soporte. | id (int), Nombre (nvarchar(50)), Razon_Social (nvarchar(50)), Contenido (nvarchar(250)), USUARIO (nchar(100)), x (bigint), y (bigint) |
| TABLE | `AUX_MAYORES` | Objeto auxiliar de soporte. | USUARIO (nvarchar(50)), ID (int), CUENTA (nvarchar(15)), CCOSTO (nvarchar(4)), SALDO_ANTERIOR (money), SALDO_PERIODO (money), SALDO_ACTUAL (money), NUMERO ASIENTO (int) |
| TABLE | `AUX_MA_PUNTOS` | Objeto auxiliar de soporte. | id (int), id_padre (int), id_barra (int), cbarra (nvarchar(13)), usuario (nvarchar(50)), es_pruebas (int) |
| TABLE | `AUX_MV_APLICACION_TR` | Objeto auxiliar de soporte. | IdAplicacion (numeric(18, 0)), Transmision (numeric(18, 0)) |
| TABLE | `Aux_MV_Cpte` | Objeto auxiliar de soporte. | ID (int), TC (nvarchar(4)), USUARIO (nvarchar(100)), TIPO (nvarchar(2)), Seccion (nchar(100)), IDCOMPROBANTE (nvarchar(13)), IDCOMPLEMENTO (int), COLUMNA1 (nvarchar(50)) |
| TABLE | `Aux_MV_CpteQR` | Objeto auxiliar de soporte. | ID_AUX_MV_CPTEQR (int), USUARIO (nvarchar(100)), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), QR_AFIP (image), IMG_AFIP (image), LOGO_EMPRESA (image), MUESTRA_LOGO (nvarchar(2)) |
| TABLE | `AUX_MV_CTROL_CPTE` | Objeto auxiliar de soporte. | id (int), TC (nvarchar(4)), SUCURSAL (nvarchar(4)), NUMERO (nvarchar(8)), LETRA (nvarchar(1)), IMPORTE (money), CUENTA (nvarchar(15)), NOMBRE (nvarchar(150)) |
| TABLE | `AUX_NETPALM` | Objeto auxiliar de soporte. | ID (int), IdReparto (int), TcGenerado (nvarchar(4)), IdCpteGenerado (nvarchar(13)), NroPedido (nvarchar(15)), Fecha (datetime), Hora (nvarchar(6)), IdCliente (nvarchar(15)) |
| TABLE | `AUX_SALDOSMENSUALES` | Objeto auxiliar de soporte. | CUENTA (nvarchar(15)), Mes (smallint), Anio (smallint), Saldo (money), SaldoAjustado (money), Diferencia (money), ID (int), USUARIO (nvarchar(50)) |
| TABLE | `AUX_SUBDIARIOS` | Objeto auxiliar de soporte. | USUARIO (nvarchar(50)), ID (int), PERIODO (int), MES_OPERATIVO (int), NUMERO ASIENTO (int), FECHA (datetime), TC (nvarchar(4)), SUCURSAL (nvarchar(4)) |
| TABLE | `Aux_Tareas` | Objeto auxiliar de soporte. | ID (int), Descripcion (nvarchar(250)), IdTecnico (nvarchar(4)), FhHsAlta (datetime), FhHsFin (datetime), IDTarea (int) |
| TABLE | `AUX_TMP` | Objeto auxiliar de soporte. | USUARIO (nvarchar(255)), Cuenta (nvarchar(15)) |
| TABLE | `Aux_totales` | Objeto auxiliar de soporte. | Descripcion (nvarchar(100)), Saldo (money) |
| TABLE | `AUX_TOTALES_DET` | Objeto auxiliar de soporte. | Nombre_Grupo (nvarchar(100)), Cuenta (nvarchar(15)), Saldo (money) |
| TABLE | `AUX_VENDEDORZONA` | Objeto auxiliar de soporte. | id (int), usuario (nvarchar(100)), idvendedor (nvarchar(4)), zona (nvarchar(50)), pedido (float), altasCliente (float), kgs1 (float), kgs2 (float) |
| TABLE | `AUX_VENDEDORZONA_COMISION` | Objeto auxiliar de soporte. | id (int), usuario (nvarchar(50)), idvendedor (nvarchar(4)), idfamilia (nvarchar(15)), comision (float), importe (float), grupo (int) |
| TABLE | `AUX_VIAJES_CHOFER` | Objeto auxiliar de soporte. | id (int), usuario (nvarchar(50)), fecha (datetime), idvendedor (nvarchar(4)), cuenta (nvarchar(50)), descripcion (nvarchar(50)), FP (float), PAGO (float) |
| TABLE | `AUX_V_TA_CPTE` | Objeto auxiliar de soporte. | Usuario (nvarchar(80)), Codigo (nvarchar(4)), Descripcion (nvarchar(50)), UNegocio (nvarchar(4)), PtoVta (nvarchar(4)), Letra (nvarchar(2)), Nro_Desde (nvarchar(10)), Nro_Hasta (nvarchar(10)) |
| TABLE | `AUX_WEB_CARRITO` | Objeto auxiliar de soporte. | id (int), idarticulo (nvarchar(25)), descripcion (nvarchar(100)), precio (float), cantidad (float), descuento (float), presentacion (varchar(5)), idCliente (nvarchar(9)) |
| TABLE | `TMP_CAJA_FRANQUICIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Cuenta (nvarchar(15)), Descripcion (nvarchar(50)), idCajas (nvarchar(4)), Saldo (float), Moneda (nvarchar(4)), Cotizacion (float), Inicial (float), Cobranzas (float) |
| TABLE | `TMP_FRANQUICIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IDArticulo (nvarchar(25)), Descripcion (nvarchar(150)), Ingresos (float), Tickets (float), Proforma (float), Ajustes (float), StockInicio (float), StockFin (float) |
| TABLE | `TMP_POSICIONES_Asistente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IDDEPOSITO (nvarchar(4)), IDPOSICION (nvarchar(25)), DESCRIPCION (nvarchar(50)), TITULO (bit), HABILITADO (bit), RESERVADO (bit), IDUNIDAD (nvarchar(4)), CAPACIDAD (float) |
| TABLE | `Tmp_V_MV_TARJETASCON_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IDCONC (int), SECUENCIA (int), IZQUIERDA (nvarchar(250)), DERECHA (nvarchar(250)), ID (int) |

### Otros objetos

Total de objetos: 288

| Tipo | Nombre | Descripción | Columnas relevantes / primeras columnas |
|---|---|---|---|
| TABLE | `BSC_Objetivos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdTablero (int), IdPerspectiva (int), IdObjetivo (int), Nombre (nvarchar(150)), Descripcion (nvarchar(500)) |
| TABLE | `BSC_Perspectivas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdTablero (int), IdPerspectiva (int), Nombre (nvarchar(150)), Descripcion (nvarchar(500)) |
| TABLE | `BSC_Tableros` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdTablero (int), Nombre (nvarchar(150)), Descripcion (nvarchar(500)), Vision (ntext), Mision (ntext) |
| TABLE | `CalendarEvents` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | EventID (int), StartDateTime (datetime), EndDateTime (datetime), RecurrenceState (int), Subject (nvarchar(255)), Location (nvarchar(255)), Body (nvarchar(255)), BusyStatus (int) |
| TABLE | `CalendarRecurrencePatterns` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | RecurrencePatternID (int), MasterEventID (int), PatternStartDate (datetime), PatternEndMethod (int), PatternEndDate (datetime), PatternEndAfterOccurrences (int), EventStartTime (datetime), EventDuration (int) |
| TABLE | `COMANDA_RESERVAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Cliente (nvarchar(9)), NombreCliente (nvarchar(250)), FechaHora (datetime), Telefono (nvarchar(150)), Confirmado (bit), Cancelado (bit), ID (int), IdMesa (int) |
| TABLE | `CONTROL_ACCESO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | MAQUINA (nvarchar(150)), USUARIO (nvarchar(150)), FORMULARIO (nvarchar(150)), TAREA (nvarchar(150)), INGRESO (datetime), EGRESO (datetime), NombreUsuario (nvarchar(50)), ID (int) |
| TABLE | `Control_MV_Calculo` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Fecha (datetime), ID (int), RESPONSABLE (nvarchar(255)), PERIODO (nvarchar(6)), CUENTA (nvarchar(15)), MES_OPERATIVO (tinyint), NUMERO ASIENTO (int), Anulado (bit) |
| TABLE | `CTROL_ESCANEO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Cuenta (nvarchar(15)), Cpte (nvarchar(20)), Fecha (datetime), Usuario (nvarchar(150)), RutaArchivo (nvarchar(250)), NombreArchivo (nvarchar(250)), Eliminado (bit), ActualizadoPor (nvarchar(250)) |
| TABLE | `EM_C15` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Periodo (nvarchar(7)), P_Existencia (money), P_Ingresos (money), P_Elaboracion (money), P_Otros (money), SP000_Existencia (money), SP0000_Existencia (money), SPSemo_Existencia (money) |
| TABLE | `EM_LibroRojo` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Periodo (nvarchar(7)), Cuenta (nvarchar(15)), dia (int), Entradas (money), Salidas (money), Observaciones (nvarchar(100)) |
| TABLE | `EM_MV_PESADAS_TRIGO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IDPESADA (int), TIPO_PESADA (char(1)), FECHA_OPERATIVA (datetime), FH_INGRESO (datetime), FH_EGRESO (datetime), PATENTE (nvarchar(7)), PATENTE_ACOPLADO (nvarchar(7)), CANTIDAD_EJES (int) |
| TABLE | `EQ_CONDIVA_IMPORTACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | NOMBRE (nvarchar(100)), CODIGO_ORIGEN (nvarchar(100)), CODIGO_SISTEMA (nvarchar(4)) |
| TABLE | `EQ_COND_INTERFACES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | INTERFACE (nvarchar(100)), VALOR_CAMPO (nvarchar(100)), ASIGNADO_A_CUENTA (nvarchar(15)), AL_DEBE_O_HABER (nvarchar(1)), CAMPO_ORIGEN (nvarchar(100)), OPERADOR (nvarchar(10)), VALOR_CAMPO_ORIGEN (nvarchar(100)), SECUENCIA (int) |
| TABLE | `EQ_CTA_INTERFACE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | INTERFACE (nvarchar(100)), CODIGO_ORIGEN (nvarchar(100)), CODIGO_SISTEMA (nvarchar(15)) |
| TABLE | `EQ_TC_INTERFACE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | INTERFACE (nvarchar(100)), CODIGO_ORIGEN (nvarchar(100)), CODIGO_SISTEMA (nvarchar(4)) |
| TABLE | `H_HISTORIAL_ESTADOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), idEstado (nvarchar(4)), Usuario (nvarchar(50)), FechaHs (datetime), idHabitacion (nvarchar(4)) |
| TABLE | `H_HUESPED` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), NroDocumento (nvarchar(15)), Nombre (nvarchar(50)), Domicilio (nvarchar(100)), Localidad (nvarchar(50)), CPostal (nvarchar(10)), IdProvincia (nvarchar(4)), IdCondIVA (nvarchar(4)) |
| TABLE | `H_MA_HABITACIONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), idHabitacion (nvarchar(4)), Nombre (nvarchar(150)), idEstado (nvarchar(4)), idTipo (nvarchar(4)), capacidad (float) |
| TABLE | `H_MV_HABITACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Estado (nvarchar(1)), FhOperativa (datetime), Planilla (int), Comanda (int), FhAlta (datetime), FhCierre (datetime), IdHabitacion (nvarchar(4)), Mozo (nvarchar(6)) |
| TABLE | `H_RESERVAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idReserva (int), FechaHsReserva (datetime), FechaHoraDesde (datetime), FechaHoraHasta (datetime), NroDocumento (nvarchar(15)), Estado (nvarchar(2)), Observaciones (ntext) |
| TABLE | `H_RESERVAS_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idReserva (int), idHabitacion (nvarchar(4)), Cantidad (int), Finalizada (bit) |
| TABLE | `H_RESERVAS_HUESPED` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdReserva (int), NroDocumento (nvarchar(15)) |
| TABLE | `H_TA_TIPOHABITACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idTipo (nvarchar(4)), Descripcion (nvarchar(250)) |
| TABLE | `IA_Costos_Actualizacion_Hist` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), FechaHora (datetime), ImportacionID (int), ImportacionDetID (int), Usuario (nvarchar(50)), Proveedor (nvarchar(50)), CuentaProveedor (nvarchar(15)), ArchivoOrigen (nvarchar(500)) |
| TABLE | `IA_Costos_Importacion_CAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), FechaHora_Alta (datetime), FechaHora_InicioProceso (datetime), FechaHora_FinProceso (datetime), Estado (nvarchar(20)), Usuario (nvarchar(50)), IdInterODBC (int), Proveedor (nvarchar(50)) |
| TABLE | `IA_Costos_Importacion_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), ID_CAB (int), FilaOrigen (int), Estado (nvarchar(20)), CodigoProveedorLeido (nvarchar(100)), DescripcionLeida (nvarchar(250)), PrecioCostoLeido (money), MonedaLeida (nvarchar(4)) |
| TABLE | `MAPINFO_CALLES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Street (char(40)), FromLeft (int), ToLeft (int), FromRight (int), ToRight (int), Codigo (int), NSCode (smallint), Boundary (int) |
| TABLE | `MAPINFO_LOCALIDADES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Localidad (char(100)), Cod_loc (decimal(9, 0)), Partido (char(17)), Copart (decimal(8, 0)), Cod_postal (char(13)), Id (decimal(4, 0)), MI_SQL_X (float), MI_SQL_Y (float) |
| TABLE | `MAPINFO_MAPCATALOG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | SPATIALTYPE (float), TABLENAME (char(32)), OWNERNAME (char(32)), SPATIALCOLUMN (char(32)), DB_X_LL (float), DB_Y_LL (float), DB_X_UR (float), DB_Y_UR (float) |
| TABLE | `MA_ASISTENTES` | Tabla maestra del sistema. | id (int), Nombre_y_Apellido (nvarchar(100)), Domicilio (nvarchar(100)), Localidad (nvarchar(100)), Provincia (nvarchar(4)), C_Postal (nvarchar(20)), Telefono (nvarchar(100)), Fax (nvarchar(100)) |
| TABLE | `MA_BIENES` | Tabla maestra del sistema. | CODIGO (float), CUENTA_CONTABLE (nvarchar(15)), CUENTA_EJERCICIO (nvarchar(15)), CUENTA_ACUMULADA (nvarchar(15)), FECHA_COMPRA (datetime), COSTO (money), VALOR_RESIDUAL (money), VALOR_RECUPERO (money) |
| TABLE | `MA_BIENES_NOVEDADES` | Tabla maestra del sistema. | ID (int), CODIGO (float), FECHA (datetime), COSTO (float), VALOR_RECUPERO (money), DESCRIPCION (nvarchar(250)), PERIODO (int), PERIODO_REVALUACION (int) |
| TABLE | `MA_CALENDARIO` | Tabla maestra del sistema. | Fecha (datetime), NoLaborable (bit), Temporada (nvarchar(2)) |
| TABLE | `MA_CASH-DETALLE` | Tabla maestra del sistema. | ID (int), CODIGO_CASH (nvarchar(6)), NOMBRE_GRUPO (nvarchar(50)), NOMBRE_SUBGRUPO (nvarchar(50)), CUENTA (nvarchar(15)), SaldoPeriodo1 (money), SaldoPeriodo2 (money), SaldoPeriodo3 (money) |
| TABLE | `MA_CASH-FLOW` | Tabla maestra del sistema. | CODIGO (nvarchar(6)), NOMBRE (nvarchar(50)), MODIFICADO (bit), PERIODO 1 (nvarchar(7)), PERIODO 2 (nvarchar(7)), PERIODO 3 (nvarchar(7)), PERIODO 4 (nvarchar(7)), PERIODO 5 (nvarchar(7)) |
| TABLE | `MA_CASH-GRUPO` | Tabla maestra del sistema. | CODIGO_CASH (nvarchar(6)), NOMBRE (nvarchar(50)) |
| TABLE | `MA_CASH_CONTROL` | Tabla maestra del sistema. | ID (int), CODIGO_CASH (nvarchar(6)), NOMBRE_GRUPO (nvarchar(50)), NOMBRE_SUBGRUPO (nvarchar(50)), PERIODO (nvarchar(7)), CUENTA (nvarchar(15)), MES_OPERATIVO (tinyint), NUMERO ASIENTO (int) |
| TABLE | `MA_CASH_CTAS_PRINCIPALES` | Tabla maestra del sistema. | CODIGO_CASH (nvarchar(6)), CUENTA (nvarchar(15)) |
| TABLE | `MA_CASH_PROYECTADO` | Tabla maestra del sistema. | ID (int), NOMBRE (nvarchar(50)), CODIGO_CASH (nvarchar(6)), PERIODO_FIN (nvarchar(50)), COLUMNA1 (nvarchar(30)), COLUMNA2 (nvarchar(30)), COLUMNA3 (nvarchar(30)), COLUMNA4 (nvarchar(30)) |
| TABLE | `MA_CHOFERES` | Tabla maestra del sistema. | CODIGO (nvarchar(4)), APELLIDO (nvarchar(50)), NOMBRES (nvarchar(50)), TIPO_DOC (nvarchar(4)), NRO_DOCUMENTO (nvarchar(15)), LIBRETA_SANITARIA (nvarchar(20)), REGISTRO (nvarchar(20)), VENCIMIENTO_REGISTRO (smalldatetime) |
| TABLE | `MA_ClavePIN` | Tabla maestra del sistema. | ID (int), FECHASOLICITUD (datetime), HORASOLICITUD (datetime), MOTIVO (nvarchar(4)), CUENTA (nvarchar(15)), TC (nvarchar(4)), IDCOMPROBANTE (nvarchar(13)), IDARTICULO (nvarchar(25)) |
| TABLE | `MA_CONTACTOS` | Tabla maestra del sistema. | id (int), Nombre_y_Apellido (nvarchar(100)), Domicilio (nvarchar(100)), Localidad (nvarchar(100)), Provincia (nvarchar(4)), C_Postal (nvarchar(20)), Telefono (nvarchar(100)), Fax (nvarchar(100)) |
| TABLE | `MA_CONTACTOS_ADIC` | Tabla maestra del sistema. | id (int), IdContacto (int), TipoAdic (nvarchar(2)), DescrAdic (ntext) |
| TABLE | `MA_Destinatarios` | Tabla maestra del sistema. | Cliente (nvarchar(4)), Codigo (nvarchar(16)), Sucursal (nvarchar(6)), Nombre (nvarchar(50)), Domicilio (nvarchar(100)), Localidad (nvarchar(50)), CPostal (nvarchar(10)), Zona (nvarchar(7)) |
| TABLE | `MA_ESTADISTICACTA` | Tabla maestra del sistema. | ID (int), NOMBRE (nvarchar(100)), GRUPO (nvarchar(100)), CUENTA (nvarchar(15)), DEBE (bit), HABER (bit), TipoGrafico (int), FECHAD (datetime) |
| TABLE | `MA_IMAGEN` | Tabla maestra del sistema. | Idimagen (int), rutaImagen (nvarchar(250)), imagen (image), detalle (ntext) |
| TABLE | `MA_INDAJUSTE` | Tabla maestra del sistema. | ANIO (smallint), MES (smallint), INDICE1 (float), INDICE2 (float), INDICE3 (float), INDICE4 (float), FechaHora_Grabacion (datetime), FechaHora_Modificacion (datetime) |
| TABLE | `MA_MODELOS` | Tabla maestra del sistema. | TIPO_REG (nvarchar(4)), NOMBRE (nvarchar(50)), CUENTA (nvarchar(15)), DEBE-HABER (nvarchar(1)), IMPORTE (money), DETALLE (nvarchar(50)), SECUENCIA (int), FechaHora_Grabacion (datetime) |
| TABLE | `MA_POSICIONES` | Tabla maestra del sistema. | ID (int), IDDEPOSITO (nvarchar(4)), IDPOSICION (nvarchar(25)), DESCRIPCION (nvarchar(50)), TITULO (bit), HABILITADO (bit), RESERVADO (bit), IDUNIDAD (nvarchar(4)) |
| TABLE | `MA_POSICIONES_NOTAS` | Tabla maestra del sistema. | IDDEPOSITO (nvarchar(4)), IDPOSICION (nvarchar(25)), NOTA (nvarchar(200)), posX (float), posY (float), Width (float), Height (float), Color (nvarchar(10)) |
| TABLE | `MA_PROGRAMACION_MANTENIMIENTO` | Tabla maestra del sistema. | Codigo (int), HsDesde (nvarchar(50)), HsHasta (nvarchar(50)), Mantenimiento (nvarchar(250)), CadaHs (nvarchar(50)), Cadames (nvarchar(50)), Condicion (int), Tabla (nvarchar(150)) |
| TABLE | `MA_SUBDIARIOS` | Tabla maestra del sistema. | CODIGO (nvarchar(4)), DESCRIPCION (nvarchar(50)), CUENTA (nvarchar(15)), CUENTA-DESDE (nvarchar(15)), CUENTA-HASTA (nvarchar(15)), DEBE-HABER (nvarchar(1)), SECUENCIA (int), FechaHora_Grabacion (datetime) |
| TABLE | `MA_SUBDIARIOSCOMPROBANTES` | Tabla maestra del sistema. | Subdiario (nvarchar(4)), TC (nvarchar(4)) |
| TABLE | `MA_SUBDIARIOSCOMPROBANTES2` | Tabla maestra del sistema. | SubDiario (nvarchar(4)), TC (nvarchar(4)) |
| TABLE | `MA_VEHICULOS` | Tabla maestra del sistema. | PATENTE (nvarchar(7)), TIPO_VEHICULO (nvarchar(4)), MARCA_VEHICULO (nvarchar(4)), MODELO (nvarchar(50)), ANIO (int), VTV (nvarchar(50)), VTO_VTV (smalldatetime), C_VERDE (nvarchar(50)) |
| TABLE | `Resultados` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | CODIGO (nvarchar(15)), DV (smallint), DESCRIPCION (nvarchar(50)), TITULO (bit), AJUSTE (bit), INDICE (smallint), BLOQUEO (bit), MANUAL (nvarchar(500)) |
| TABLE | `RSARDI` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdArticulo (nvarchar(25)), DESCRIPCION (nvarchar(100)), COSTO (money), STOCK_ALFANETSolucionesInf (money), VALOR_ALFANETSolucionesInf (money), TOT_ALFANETSolucionesInf (money), VALOR_ALFANETSolucionesInf_1 (money), TOT_ALFANETSolucionesInf_1 (money) |
| TABLE | `SolicitudPnl` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | NroSolicitud (nvarchar(13)), FechaCarga (datetime), FechaComprometido (datetime), Usuario (nvarchar(50)), Comentario (nvarchar(250)), FechaOrdenCpra (datetime), ProveedorSug (nvarchar(15)), UNegocio (nvarchar(4)) |
| TABLE | `SolicitudPnlDet` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), NroSolicitud (nvarchar(13)), IdArticulo (nvarchar(25)), Descripcion (nvarchar(100)), UniMed (nvarchar(4)), Cantidad (float), Precio (money), Estado (int) |
| TABLE | `S_TA_UBICACIONES_VENDEDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), lat (nvarchar(15)), long (nvarchar(15)), idvendedor (nvarchar(4)), fechahora (datetime), idcomprobante (nvarchar(13)) |
| TABLE | `TESTIGO_EXPORTACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), CODIGO_ORIGEN (nvarchar(200)), CODIGO_SISTEMA (nvarchar(15)), CUENTA (nvarchar(15)), FH_GRABACION (datetime) |
| TABLE | `TESTIGO_RECEPCION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), FECHA_HORA (datetime), USUARIO (nvarchar(50)), ARCHIVO (nvarchar(200)), REGISTROS_LEIDOS (int), REGISTROS_IGNORADOS (int), TOTAL_LINEAS (int), ANULADA (bit) |
| TABLE | `V_MA_eMail` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), Evento (nvarchar(150)), De (nvarchar(250)), Para (nvarchar(250)), Asunto (nvarchar(250)), Mensaje (ntext), Adjunto (nvarchar(250)), Baja (bit) |
| TABLE | `V_MA_eMailMov` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Id (int), De (nchar(150)), Para (nvarchar(250)), Asunto (nvarchar(250)), Mensaje (ntext), Adjunto (nvarchar(250)), CodigoActivador (nvarchar(4)), FechaHora (datetime) |
| TABLE | `V_MA_INSUMOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdArticulo (nvarchar(25)), IdUnidadInsumo (nvarchar(4)), IdArticuloInsumo (nvarchar(25)), Cantidad (float), Transmision (int), ClasePr (int), CostoInsumo (float) |
| TABLE | `V_MA_INSUMOSTAREAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | ID (int), IdArticulo (nvarchar(25)), IdTarea (nvarchar(4)), Minutos (float), Segundos (int), DESCRIPCION (nvarchar(100)), Transmision (int) |
| TABLE | `V_MA_MANTENIMIENTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Codigo (nvarchar(25)), Matricula (nvarchar(20)), Modelo (nvarchar(50)), Serie (nvarchar(20)), Descripcion (nvarchar(150)), Ubicacion (nvarchar(150)), Caracteristicas (ntext), Imagen (image) |
| TABLE | `V_MA_MANTENIMIENTOS_AUX` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Codigo (nvarchar(25)), Matricula (nvarchar(20)), Modelo (nvarchar(50)), Serie (nvarchar(20)), Descripcion (nvarchar(150)), Ubicacion (nvarchar(150)), Caracteristicas (ntext), Imagen (image) |
| TABLE | `V_MA_Planchas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdArticulo (nvarchar(25)), Descripcion (nvarchar(50)), Ancho (float), Largo (float), ValorM2 (money), TieneBeta (bit), Medida (float), GeneraSobrantes (bit) |
| TABLE | `V_MA_PLANCHAS_CORTES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | IdSobrante (nchar(25)), IdCorte (int), IdPosicion (int), Area (float), Perimetro (float), Ancho (float), Largo (float), ALoLargo (bit) |
| TABLE | `V_MA_PUNTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), nombre (nvarchar(100)), fh_desde (date), fh_hasta (date), baja (bit), opcion (int), valor (float), condicion (int) |
| TABLE | `V_MA_PUNTOS_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | id (int), id_padre (int), desde (float), hasta (float), cantidad (int) |
| TABLE | `WEB_ACTUALIZACIONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idArticulo (nvarchar(25)), fhUltimaActualizacion (datetime) |
| TABLE | `wsSysMobilePedidosCabecera` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idPedido (int), idCliente (nvarchar(15)), idVendedor (nvarchar(4)), fecha (datetime), totalNeto (money), totalFinal (money) |
| TABLE | `wsSysMobilePedidosDetalle` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | idPedido (int), idPedidoItem (int), idArticulo (nvarchar(25)), cantidad (float), importeUnitario (money), porcDescuento (float), total (money) |
| VIEW | `AlfaOnLineVendedores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `EM_CTROL_APLICA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Gb_Asientos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `GB_AsientosCC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `H_VT_HABITACIONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Libro_CobranzasConFP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `MA_VEHICULOS_VENCIMIENTO` | Tabla maestra del sistema. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `NW_DOCUMENTADOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `PL_RANKINGCONSUMO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Productos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `ss_auditoria` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_MV_ASIENTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_V_TA_CAJAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `SS_V_TA_FAMILIAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_DESPACHOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_MovDiario` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_MovDiario_Posiciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_SaldoDiario` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_SaldoDiario_Posiciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_Sin_Asignar` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_Sin_Asignar_Cpte` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Locator_Sin_Asignar_IR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_Loc_MovDiario_Posiciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_MovDiario1` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_PuntoReposicion` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_RESERVAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_RESERVAS_SL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SaldoDiario` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Stk_SaldosMes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SALDOS_SL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SALDOS_Unidades` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SALDOS_Unidades_Lotes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SALDOS_UNIDADES_NVA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SERIES_Y_LOTES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `STK_SL_AGRUPADO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VIW` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_Pagos_Comprobantes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_ALBERTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_ALBERTO2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_marcos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_MESACTUAL` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_PRUEBA1` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_RP_Acceso_PRUEBA2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_Clientes_C_CLIENTESDADOSDEBAJA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_CLIENTES_C_CLIENTESMARCOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_Clientes_C_PRUEBA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_Clientes_C_PRUEBAMARCOS2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_CLIENTES_C_Vendedores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_CLIENTES_C_zonalunes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VTP_V_MA_CLIENTES_P_PROVEEDORESDADOSDEBAJA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_AFIP_01` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_AFIP_02` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_APLICACION_AGRUPADA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_APLICACION_FH` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_APLICACION_SIN_VJ` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_DEBE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_HABER` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_MONEDA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_SALDO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ASIENTOS_SALDOS_DH` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_AuxComisionesFamilias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_AuxComisionesMarcas` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CBNP` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_Chequeras` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CHEQUE_TERCERO_SALDO_FECHA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_ClientesVs` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_COMISIONES_LISTA_CB_DESCUENTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONSOLIDADO_CAJA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONSOLIDADO_CAJA02` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONSOLIDADO_CAJA_UNEGOCIO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONTENEDORES_ETIQUETAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONTROL_COSTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CONTROL_COSTOS_DET` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CPTES_COMISION_FAMCB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CPTE_Diferido` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CPTE_ZONA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_CtaUltimoMov` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CYB_IMPUT` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_CYB_MPAGOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_C_MV_INSUMOS_APLICACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEFRANQUICIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEFRANQUICIA_RUBRO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEIVAPROFORMA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEIVAPROFORMA_COMPLETO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLEIVAPROFORMA_COSTOANT` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLExLIBROIVA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_DETALLExLIBROIVA2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_EstadisticaClientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_e_mail` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_FaltanteMercaderiaNew` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_FaltantesMercaderia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_FAMILIAS_NIVELES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_FRECUENCIA_VDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_GUIADECARGA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_INS_INSUMOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_IRs_OCs` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ITEMS_VENDEDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MANTENIMIENTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MANTENIMIENTOS_PRO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MAPINFO_CALLES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_EMAILMOV` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MA_SUBDIARIOCOMPROBANTES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MP_PAGOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MVREPARTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_APLICACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_ASIENTOS_CTACABECERA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_ASIENTOS_RETPERC_REALIZADAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_COMPULSA_ABIERTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_COMPULSA_DISPONIBLES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_CONTENEDORES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_CONTENEDORES_CONS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_CPTE_ELECTRONICOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_Diarios_Descrip` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_FORMPEDIDOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_INGRESOS_PENDIENTES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_INSERT_ABIERTO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_INSERT_DISPONIBLE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_LIQUIDACIONESCAB` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_PEDIDOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_SALDOSRM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_TROPA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_MV_Viajes_Cptes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_OCPendiente_New` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_OTPR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PARA_MAPAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PEDIDOSREPOSICION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PEDIDOSREPOSICION_SINPED` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PEDIDOSREPOSICION_SINPEDIDOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vt_planillaMatanza` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_ART` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PLANILLA_ART_BORRADOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_Planilla_Retenciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PL_Clientes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PL_CONTACTOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_PL_Proveedores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_P_MA_LEGAJOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_P_MV_NOVEDADES_VARIABLES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_Cliente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_Cliente_old` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_Grupos` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_RankingConsumo_GruposMes` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_Grupos_Old` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_old` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RankingConsumo_oldNvo` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RANKINGCONSUMO_VENDEDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RANKINGCONSUMO_VENDEDOR_OLD` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RANKING_AVANCE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RANKING_AVANCE_ART` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_REMITOSTRANSF` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_REMITOSTRANSFRES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RESERVAS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RubroTipoArtDesc` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_RubroTipoDescripciones` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SALDOSCTA_MONEDA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SALDOSCTA_MONEDA_UNEG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SALDOSCTA_MONEDA_VDOR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SALDOSCUPONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIOS_01PCIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_00` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_01` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_02` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_03` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_RES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_SUBDIARIO_RESDIA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_TAREASAFACTURAR` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_TARIFASENCO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_TA_CPTE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `Vt_TitCopiadores` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_TRANSFERENCIASCAJA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_ULTIMO_ESTADO_OT` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VEHICULOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VENDEDORES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_VIAJESCTA` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MA_EMAILMOV` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Cpte` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS2` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS3` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS3P` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS4` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS4UD` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS5` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_CONS6` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTEINSUMOS_TRIM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTE_INSUMOS_COMISIONES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTE_INSUMOS_CRM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTE_PEDIDOS_X_UNEG` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_CPTE_PEDIDOS_X_UNEGSV` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_DIARIOS_UNSOLOTECNICO` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_FORMPEDIDOS` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_FORMPEDIDOSEXC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_INSUMOSOP_PENDIENTE` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_INSUMOS_APLICACION` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_NPPendiente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_NPPendienteUd` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_NPPendiente_ConDep` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_NPPend_ConDep` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OC` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCCons` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCPendiente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCPendienteDet` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCPendiente_ConDep` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCPend_ConDep` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_OCPend_RES` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_RepartoPend` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_Insumos_RepartoPendiente` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `VT_V_MV_INSUMOS_SALDOSRM` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vw_estadisticas_ingresos_diarias` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |
| VIEW | `vw_familias_jerarquia` | Objeto detectado en el script SQL. Revisar definición antes de usar en desarrollo. | Vista: revisar SELECT de definición si se necesitan campos exactos. |

## Notas funcionales críticas

### Clientes y proveedores

- Los clientes se obtienen desde `VT_CLIENTES`.
- Los proveedores se obtienen desde `VT_PROVEEDORES`.
- Ambos surgen conceptualmente de `MA_CUENTAS` y `MA_CUENTASADIC`.
- No hardcodear cuentas de clientes/proveedores; revisar `TA_CONFIGURACION`.

### Configuración

- `TA_CONFIGURACION` usa principalmente `CLAVE`, `VALOR`, `VALOR_AUX` y `GRUPO`.
- `GRUPO` hoy puede estar poco usado, pero debe respetarse para evolución futura.

### Códigos

- `IDARTICULO` es string de 25 caracteres.
- En artículos y tablas `TA_`, los códigos numéricos se alinean a la derecha y los alfanuméricos a la izquierda.
- Excepción: plan de cuentas (`MA_CUENTAS`), donde `CODIGO`/`CUENTA` se alinea siempre a la izquierda por jerarquía contable.

### Comprobantes

- `V_TA_Cpte` define comportamiento del comprobante.
- Si `DEBEHABER = D` y `SISTEMA <> Compras`, suma; si el sistema es Compras, la lógica se invierte.
- Si `DEBEHABER = H`, aplicar la lógica inversa.
- Para comprobantes de stock, usar `ES`: `E` suma stock, `S` resta stock, salvo ajustes `AJP` / `AJN`.
- Para stock operativo, priorizar `V_MV_STOCK`, donde `Cantidad` ya viene con signo.

### Contabilidad y cuenta corriente

- `MV_ASIENTOS` es el corazón contable.
- Las tablas de comprobantes (`V_MV_Cpte`, `C_MV_Cpte`) son auxiliares para impresión/formularios y operación.
- Para libros y cuenta corriente usar vistas oficiales como `Libro_VentasConFP`, `Libro_ComprasConFP`, `VE_COBRANZAS_REALIZADAS`, `CO_PAGOS_REALIZADOS`, `VE_CPTES_SALDOS_VENTAS` y `CO_CPTES_IMPAGOS_2026` cuando correspondan.
- `MV_APLICACION` es zona sensible: registra aplicaciones entre facturas, pagos/cobranzas y puede quedar inconsistente.

### Sucursales, unidades de negocio y depósitos

- El campo `UNegocio` se usa como unidad de negocio, sucursal o empresa dentro de la misma base.
- Las unidades de negocio están en `V_TA_UnidadNegocio`.
- Los depósitos están en `V_TA_DEPOSITOS` / `V_TA_DEPOSITOS` según disponibilidad.
- `V_TA_TPV` registra terminales/puntos de venta y datos de conexión/sincronización de bases locales.

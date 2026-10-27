# OBJETOS_SQL_PRIORITARIOS.md

## Conciliación de tarjetas y documentos originales (2026-10-27)

MV_ASIENTOS continúa como fuente contable oficial de tarjetas presentadas. Los nuevos auxiliares TJ_Importacion, TJ_Detalle, TJ_Vinculo, TJ_VinculoDetalle y TJ_VinculoAlfa conservan PDF, detalle y auditoría; no reemplazan ni escriben contabilidad o marcas legacy. Se crean mediante `2026-10-27-002`; contrato en [Conciliación de tarjetas](modulos/conciliacion-tarjetas.md). `BK_ImportacionExtracto.ArchivoOriginal` se incorpora por `2026-10-27-001` para testigos bancarios transaccionales; históricos NULL.

## Objetivo

Este archivo define objetos SQL importantes de Alfa Gestión y aclara su **tipo real o uso recomendado**.

Regla crítica:

> En Alfa Gestión, el prefijo `V_` no garantiza que el objeto sea una vista SQL.  
> Puede indicar el sistema/módulo de **Ventas** o puede formar parte del nombre histórico del objeto.

Antes de modificar queries o generar nuevas rutinas, Codex debe verificar el tipo real del objeto en SQL Server:
- `TABLE`
- `VIEW`
- `FUNCTION`
- `PROCEDURE`

No asumir tipo técnico por nombre.

---

## Regla sobre prefijos

| Prefijo / patrón | Significado habitual | Advertencia |
|---|---|---|
| `MA_` | Maestro | Normalmente tablas maestras |
| `MV_` | Movimiento | Normalmente tablas de movimientos |
| `TA_` | Tabla auxiliar / referencia / configuración | No siempre son simples catálogos |
| `C_` | Compras | Puede ser tabla, vista o proceso según el objeto |
| `V_` | Ventas o Vista, según contexto | NO asumir que es vista SQL |
| `P_` | Personal / RRHH o proceso, según contexto | Verificar tipo real |
| `AUX_` | Auxiliar | Uso técnico o complementario |
| `AC_` | Acumulador | Usar con cuidado |

---

## Objetos prioritarios del sistema

| Objeto | Tipo real esperado | Módulo / área | Uso recomendado | Observaciones |
|---|---|---|---|---|
| `MV_ASIENTOS` | TABLE | Contabilidad | Fuente contable principal | Es el corazón contable del sistema. Facturas, créditos, débitos y asientos impactan aquí cuando afectan contabilidad/cuenta corriente/libros. |
| `V_MV_STOCK` | Verificar en SQL Server | Stock | Fuente principal de movimientos de stock | Usar como fuente de stock. El campo `Cantidad` ya viene con signo: positivo suma, negativo resta. No recalcular signo por TC salvo casos específicos de análisis. |
| `V_TA_Cpte` | Verificar en SQL Server | Configuración de comprobantes | Define comportamiento de comprobantes | Se relaciona por campo `Codigo`. Campos clave: `DEBEHABER`, `SISTEMA`, `ES`. |
| `VT_CLIENTES` | VIEW esperada | Clientes | Fuente oficial para clientes | No consultar clientes directamente desde `MA_CUENTAS` salvo mantenimiento técnico. |
| `VT_PROVEEDORES` | VIEW esperada | Proveedores | Fuente oficial para proveedores | No consultar proveedores directamente desde `MA_CUENTAS` salvo mantenimiento técnico. |
| `MA_CUENTAS` | TABLE | Plan de cuentas | Tabla maestra de cuentas | Incluye cuentas contables, clientes, proveedores, bancos y medios de pago. |
| `MA_CUENTASADIC` | TABLE | Plan de cuentas | Datos adicionales de cuentas | Domicilio, datos fiscales y datos complementarios. |
| `MA_CUENTASSUC` | TABLE | Plan de cuentas | Sucursales de clientes/proveedores | Usar cuando la cuenta tiene múltiples sucursales. |
| `MA_CUENTAS_CCDIST` | TABLE | Centros de costo | Distribución por defecto | Define máscaras o reglas de distribución de centros de costo. |
| `MA_CUENTASCCOSTO` | TABLE | Centros de costo | Centro de costo por defecto | Asignación por cuenta. |
| `MA_CUENTASCCOSTO_UNEG` | TABLE | Centros de costo / unidades de negocio | Centro de costo por cuenta y unidad de negocio | Importante en escenarios multiempresa/sucursales. |
| `TA_CONFIGURACION` | TABLE | Configuración | Configuración dinámica del sistema | Campos principales: `CLAVE`, `VALOR`, `VALOR_AUX`, `GRUPO`. No hardcodear configuraciones si pueden venir de aquí. |
| `V_MV_Cpte` | Verificar en SQL Server | Ventas / comprobantes | Cabecera y totales de comprobantes de ventas | Puede contener datos auxiliares para impresión y gestión operativa. No usar como fuente contable principal. |
| `V_MV_CPTE_OBSERV` | Verificar en SQL Server | Ventas / comprobantes | Observaciones de comprobantes | Detalle textual u observaciones. |
| `V_MV_CpteInsumos` | Verificar en SQL Server | Ventas / comprobantes | Artículos / insumos del comprobante | Detalle operativo de artículos. |
| `V_MV_CpteTareas` | Verificar en SQL Server | Ventas / servicios | Tareas o servicios | Usado en comprobantes con servicios/tareas. |
| `V_MV_CpteTerceros` | Verificar en SQL Server | Ordenes de trabajo | Comprobantes a terceros | Solo aplica a ciertos circuitos. |
| `V_MV_CpteEnComision` | Verificar en SQL Server | Trabajos en comisión | Detalle de comisión | Solo aplica a ciertos circuitos. |
| `V_MV_CpteDoc` | Verificar en SQL Server | Documentos asociados | Documentos relacionados | Links o referencias documentales. |
| `C_MV_Cpte` | TABLE esperada | Compras / comprobantes | Cabecera y totales de comprobantes de compras | Auxiliar operativo. Si el comprobante afecta contabilidad, la fuente principal es `MV_ASIENTOS`. |
| `C_MV_CPTE_OBSERV` | TABLE esperada | Compras / comprobantes | Observaciones | Detalle de observaciones. |
| `C_MV_CpteInsumos` | TABLE esperada | Compras / artículos | Artículos / insumos | Detalle operativo de artículos. |
| `C_MV_CpteTareas` | TABLE esperada | Compras / servicios | Tareas o servicios | Detalle operativo de servicios. |
| `C_MV_CpteDoc` | TABLE esperada | Compras / documentos | Documentos asociados | Guarda links de imágenes o documentos asociados al comprobante. |
| `SolicitudCpra` | TABLE esperada | Solicitud de compra | Cabecera de solicitudes de compra | Cabecera del circuito de solicitudes. |
| `SolicitudCpraDet` | TABLE esperada | Solicitud de compra | Detalle de solicitudes de compra | Detalle del circuito de solicitudes. |
| `V_MA_ARTICULOS` | Verificar en SQL Server | Artículos | Maestro de artículos | No asumir tipo técnico por `V_`. Usar como objeto oficial de artículos si está vigente. |
| `V_MA_PRECIOSCAB` | Verificar en SQL Server | Precios | Cabecera de listas de precios | Revisar PK recomendada: `IdLista + TipoLista`. |
| `V_MA_PRECIOS` | Verificar en SQL Server | Precios | Detalle de listas de precios | Revisar PK recomendada: `IdLista + IdArticulo + TipoLista`. |
| `V_TA_Unidad` | Verificar en SQL Server | Artículos | Unidades de medida | Tabla/vista de referencia. |
| `V_TA_Rubros` | Verificar en SQL Server | Artículos | Rubros | Tabla/vista de referencia. |
| `V_TA_TipoArticulo` | Verificar en SQL Server | Artículos | Tipos de artículo o marcas | Tabla/vista de referencia. |
| `V_TA_PoliticaPrecios` | Verificar en SQL Server | Precios | Políticas de precios | Tabla/vista de referencia. |
| `TA_MONEDAS` | TABLE esperada | General | Monedas | Tabla de referencia. |
| `V_TA_Percepcion` | Verificar en SQL Server | Impuestos / percepciones | Percepciones | Tabla/vista de referencia. |
| `V_TA_FAMILIAS` | Verificar en SQL Server | Artículos | Familias | Tabla/vista de referencia. |
| `V_TA_TarifaFlete` | Verificar en SQL Server | Logística | Tarifas de flete | Tabla/vista de referencia. |
| `S_TA_Equiv` | Verificar en SQL Server | Artículos | Equivalencias | Verificar uso antes de modificar. |
| `V_MA_ArtCatRel` | Verificar en SQL Server | Artículos | Categorías relacionadas | Relación de categorías de artículos. |
| `V_TA_CategoriaArticulo` | Verificar en SQL Server | Artículos | Categorías de artículos | Tabla/vista de referencia. |
| `TA_USUARIOS` | TABLE esperada | Seguridad / usuarios | Usuarios del sistema | El usuario que graba un movimiento suele quedar registrado en el registro del movimiento. |
| `TA_MENU` | TABLE esperada | Seguridad / menú | Opciones de menú | Catálogo de opciones. |
| `TA_TAREA` | TABLE esperada | Seguridad / permisos | Autorización de menú/tareas | Registra usuario, sistema y código de menú autorizado. |
| `V_TA_UnidadNegocio` | Verificar en SQL Server | Unidades de negocio | Sucursales / empresas / unidades | `UNegocio` aparece en muchas tablas de movimientos. |
| `V_TA_TPV` | Verificar en SQL Server | Terminal punto de venta / sincronización | Datos de conexión y sincronización de locales | Usar cuando la unidad de negocio representa un local físico con base propia. |
| `V_TA_Deposito` | Verificar en SQL Server | Stock | Depósitos | Referencia para movimientos de stock. |
| `MV_APLICACION` | TABLE esperada | Cuentas corrientes / aplicaciones | Aplicación de comprobantes | Zona sensible. Puede tener inconsistencias. Se usa para aplicar facturas contra pagos/cobranzas. |

---

## Vistas/consultas funcionales recomendadas

| Objeto | Área | Uso recomendado |
|---|---|---|
| `Libro_VentasConFP` | Ventas / IVA / cuenta corriente | Movimientos de ventas con proformas. Proforma = afecta cuenta corriente/contabilidad pero no libro IVA. |
| `Libro_ComprasConFP` | Compras / IVA / cuenta corriente | Movimientos de compras con proformas. |
| `Libro_Ventas` | Ventas / IVA / libro de IVA Ventas. |
| `Libro_Compras` | Compras / IVA / Libro de IVA Compras |
| `VE_COBRANZAS_REALIZADAS` | Ventas / cobranzas | Consultar cobranzas realizadas. |
| `CO_PAGOS_REALIZADOS` | Compras / pagos | Consultar pagos realizados. |
| `CO_CPTES_IMPAGOS_2026` | Compras / deuda | Comprobantes impagos de compras. Verificar si es vista/script vigente por año. |
| `VE_CPTES_SALDOS_VENTAS` | Ventas / deuda | Saldos o comprobantes impagos de ventas. |

---

## Reglas de comportamiento de comprobantes

### Fuente de configuración

El comportamiento se obtiene de:

- `V_TA_Cpte`

Se relaciona con comprobantes por:

- `Codigo`

### Campos importantes

- `DEBEHABER`
- `SISTEMA`
- `ES`

### Regla de suma/resta contable

- Si `DEBEHABER = 'D'` y `SISTEMA <> 'Compras'`, suma.
- Si `DEBEHABER = 'D'` y `SISTEMA = 'Compras'`, invierte el criterio.
- Si `DEBEHABER = 'H'`, aplicar criterio inverso.
- No hardcodear TC sin consultar configuración.

### Stock

Para comprobantes del sistema de stock:

- Pueden no tener debe/haber.
- El campo `ES` indica entrada/salida:
  - `E` = entrada / suma stock
  - `S` = salida / resta stock

Excepción:
- comprobantes de ajuste `AJP` y `AJN`

Regla práctica:
- Para consultar stock real, usar `V_MV_STOCK`.
- En `V_MV_STOCK`, la cantidad ya está grabada con signo.
- No recalcular el signo desde el comprobante si se está consultando `V_MV_STOCK`.

---


## Regla final para Codex

Antes de usar un objeto SQL, Codex debe:

1. Verificar si el objeto es tabla o vista real.
2. Identificar el módulo funcional.
3. Verificar si existe una fuente oficial de lectura.
4. No asumir que `V_` significa vista.
5. No usar comprobantes como fuente contable principal si corresponde usar `MV_ASIENTOS`.
6. No usar TC hardcodeados sin revisar `V_TA_Cpte`.

## Registro centralizado de errores

### Tabla oficial

`AUX_ERR`

Esta tabla es la bitácora central de errores del sistema Alfa Gestión.

Debe utilizarse para registrar errores generados por:

- procesos
- opciones de menú
- aplicaciones
- rutinas automáticas
- consultas SQL
- integraciones
- servicios

---

### Campos principales

| Campo | Uso |
|---|---|
| `ID` | Identificador interno autoincremental |
| `Proceso` | Nombre del proceso, opción de menú, módulo o aplicación donde ocurrió el error |
| `Fecha` | Fecha y hora del error |
| `Error` | Código numérico del error, si existe |
| `Descripcion` | Descripción resumida del error |
| `Sql` | Sentencia SQL relacionada o detalle técnico extendido |
| `Pc` | Nombre de PC, IP o equipo desde donde se ejecutó |
| `Usuario` | Usuario asociado al error |

---

### Regla obligatoria

Todo error relevante del sistema debe registrarse en `AUX_ERR`.

Codex debe considerar `AUX_ERR` como el destino estándar para logging de errores técnicos cuando se trabaje con:

- acceso a datos
- procesos automáticos
- importaciones/exportaciones
- integraciones
- ejecución de SQL
- operaciones críticas

---

### Reglas de uso

- `Proceso` debe identificar claramente dónde ocurrió el error.
- `Fecha` debe grabarse con fecha y hora actual.
- `Descripcion` debe ser clara y útil para soporte.
- `Sql` puede contener:
  - sentencia SQL que falló
  - detalle técnico extendido
  - contexto adicional
- `Pc` debe registrar el equipo, IP o nombre de host si está disponible.
- `Usuario` debe registrar el usuario logueado o usuario de base si aplica.

---

### Regla para desarrollo web

En Alfa Gestión Web, los errores deben capturarse y enviarse a `AUX_ERR` desde una capa común, no repetidos manualmente en cada pantalla.

Implementación recomendada:

- crear un servicio central:
  - `ErrorLogService`
  - o `RegistroErroresService`

- crear un repositorio:
  - `ErrorLogRepository`
  - o `AuxErrRepository`

- usar Dapper para insertar el error

---

### Ejemplo conceptual

```sql
INSERT INTO AUX_ERR
(
    Proceso,
    Fecha,
    Error,
    Descripcion,
    Sql,
    Pc,
    Usuario
)
VALUES
(
    @Proceso,
    GETDATE(),
    @Error,
    @Descripcion,
    @Sql,
    @Pc,
    @Usuario
)
## Journal de Cobranzas (19/10/2026)

`ALFACORE_COBRANZA_OPERACION` es una tabla auxiliar nueva creada por la actualización `2026-10-19-004__caja_cobranzas_operacion.sql`. Guarda operación, hash de solicitud y resultado CB; el índice único filtrado de MovimientoId evita generar dos recibos desde un mismo movimiento bancario. No reemplaza `V_MV_CPTE`, `MV_ASIENTOS` ni `MV_APLICACION`, que se graban en la misma transacción. La lectura de saldo continúa usando `VE_CPTES_SALDOS_VENTAS`. Ver [Cobranzas](modulos/cobranzas.md).

## Trazabilidad de Carga de Comprobantes (17/10/2026)

`ALFACORE_VENTAS_CARGA_ORIGEN_LINEA` es una tabla auxiliar creada por actualización para consolidaciones nuevas. Relaciona `V_MV_CPTEINSUMOS.ID` destino con `V_MV_CPTE.ID` y `V_MV_CPTEINSUMOS.ID` de origen; no duplica cantidades ni reemplaza `MV_APLICACION`. La vista oficial `VT_V_MV_INSUMOS_APLICACION` usa esas relaciones solo para los detalles trazados y conserva su cálculo para los históricos. Esto evita descontar todos los artículos de una factura de cada origen relacionado. Ver [contrato y migración protegida](modulos/ventas-carga-comprobantes/consolidacion-iva-unidades.md).

## Vínculos de conciliación bancaria (20/10/2026)

BK_Conciliacion, BK_ConciliacionBanco y BK_ConciliacionAlfa son auxiliares de la actualización 2026-10-20-001. Guardan los vínculos entre BK_ExtractoMovimiento y la clave completa de MV_ASIENTOS, sus copias, huellas y auditoría de confirmación/desconciliación. No reemplazan la contabilidad. Se respeta Conciliado de MV_ASIENTOS y MV_CONCILIACION legacy. Solo las marcas nuevas de AlfaCore pueden liberarse desde este módulo. Ver [Conciliación bancaria](modulos/conciliacion-bancaria.md).

## Reconocimiento de cobros existentes (20/10/2026)

BK_CobroExistente (actualización 2026-10-20-002) vincula un movimiento importado con la clave completa del recibo existente en MV_ASIENTOS, guardando usuario/fecha/importe. No reemplaza ni graba comprobantes, asientos, aplicaciones o conciliaciones. Tiene unicidad por movimiento y recibo; el servicio audita COBRO_EXISTENTE y actualiza exclusivamente EstadoRevision a Cobrado. Ver [Conciliación bancaria](modulos/conciliacion-bancaria.md).

La actualización 2026-10-20-003 agrega ImporteBanco y Observacion a BK_CobroExistente para reconocer manualmente cobros con diferencias justificadas por el usuario. Importe conserva el total del recibo; ambos importes y la observación se auditan sin ajustar MV_ASIENTOS ni conciliar la diferencia.

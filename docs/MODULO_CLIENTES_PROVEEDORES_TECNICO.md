# Módulo Clientes y Proveedores

## Objetivo

Este módulo implementa el ABM web de cuentas comerciales de `Clientes` y `Proveedores` sobre la base actual de Alfa, reutilizando la lógica funcional relevada desde la versión VB6.

No es un desarrollo “nuevo desde cero”: traduce a AlfaCore el comportamiento principal del maestro comercial existente.

## Alcance actual

### Incluye

- listado paginado con configuración de vista por usuario
- búsqueda compacta tipo `smart search`
- alta
- edición
- baja lógica
- lectura oficial desde:
  - `VT_CLIENTES`
  - `VT_PROVEEDORES`
- grabación principal en:
  - `MA_CUENTAS`
  - `MA_CUENTASADIC`
- observaciones ampliadas en:
  - `MA_CUENTASOBS`
- descuentos por condición para proveedores en:
  - `C_PROVEEDORES_CCOMPRA`

### No incluye todavía

- sucursales `MA_CUENTASSUC`
- contratos
- comprobantes automáticos
- archivos relacionados
- edición ABM de tablas de referencia
- saldo / cuenta corriente

## Rutas actuales

- `Clientes`: `/ventas/clientes`
- `Proveedores`: `/compras/proveedores`

En navegación interna, ambos quedaron desacoplados del menú de dashboard para evitar mezclar tableros analíticos con ABMs operativos.

## Archivos principales

### UI

- `src/AlfaCore/Components/Pages/VentasClientes.razor`
- `src/AlfaCore/Components/Pages/Proveedores.razor`
- `src/AlfaCore/Components/Shared/CuentasComercialesPage.razor`

### Modelos

- `src/AlfaCore/Models/CuentasComercialesModels.cs`

### Servicios

- `src/AlfaCore/Services/ICuentasComercialesService.cs`
- `src/AlfaCore/Services/CuentasComercialesService.cs`
- `src/AlfaCore/Services/ICuentasComercialesValidator.cs`
- `src/AlfaCore/Services/CuentasComercialesValidator.cs`

### Registro DI

- `src/AlfaCore/Program.cs`

## Objetos SQL oficiales usados

### Lectura principal

- `VT_CLIENTES`
- `VT_PROVEEDORES`

### Escritura principal

- `MA_CUENTAS`
- `MA_CUENTASADIC`

### Escritura complementaria

- `MA_CUENTASOBS`
- `C_PROVEEDORES_CCOMPRA`

### Configuración

- `TA_CONFIGURACION`
  - `TituloClientes`
  - `TituloProveedores`
  - `UsaListasDePrecios`
  - `UsaListasDePreciosProveedores`

### Referencias de solo lectura

- `TA_PAISES`
- `TA_ESTADOS`
- `TA_CONDIVA`
- `TA_TIPODOCUMENTO`
- `V_TA_Cpra_Vta`
- `V_TA_VENDEDORES`
- `V_MA_PreciosCab`
- `TA_MONEDAS`
- `TA_CLASIFICACIONES`
- `TA_RANGOS_DATOS_ADIC`
- `TA_VISTAS`

## Reglas funcionales relevantes

### Generación de código

- se toma la cuenta título desde `TA_CONFIGURACION`
- clientes:
  - `TituloClientes`
- proveedores:
  - `TituloProveedores`
- el siguiente código se calcula usando `MAX(CODIGO)` sobre la vista oficial correspondiente

### TipoVista

- primero se intenta resolver por rango con:
  - `TA_RANGOS_DATOS_ADIC`
  - `TA_VISTAS`
- si no se resuelve, se usa fallback:
  - clientes: `CL`
  - proveedores: `PR`

### Baja lógica

- se realiza sobre `MA_CUENTAS.Dada_De_Baja`

### Validaciones relevantes

- razón social obligatoria
- calle obligatoria
- localidad obligatoria
- provincia obligatoria
- país obligatorio
- CUIT / número de documento obligatorio
- validación de CUIT
- validación de duplicado en `MA_CUENTASADIC.NUMERO_DOCUMENTO`
- validación de referencias:
  - documento
  - IVA
  - provincia
  - país
  - vendedor
  - lista
  - moneda
  - clasificación
  - condición comercial
- validación de `CodigoImputacion`
- en proveedores:
  - control de lista repetida
  - control de descuentos por condición

## Estructura visual actual

### Búsqueda

Se migró a un formato compacto tipo `Contactos`:

- barra de búsqueda principal
- chips con filtros activos
- panel desplegable para:
  - estado
  - bloqueo
  - agrupación
  - provincia
  - país

### Ficha

Se reorganizó en solapas inspiradas en el VB6:

- `Carga de datos`
- `Condiciones`
- `Notas`

## Correspondencia aproximada con VB6

Fuente relevada:

- `C:\dev\ALFAVB6\Clientes y Proveedores\v_ma_clientes.frm`

Solapas VB6 detectadas:

- `Carga de Datos`
- `Condiciones`
- `Saldos`
- `Notas`
- `Alerts`
- `Cptes-Automaticos`
- `Contratos`

Dentro de `Carga de Datos`, el VB6 usa subsolapas como:

- `Datos Generales`
- `Más Datos`
- `Datos de Contactos`

La versión AlfaCore actual toma esa idea, pero por ahora implementa una versión más compacta.

## Diferencias actuales contra VB6

- no existe todavía la parte de saldos
- no existe todavía contratos
- no existe todavía comprobantes automáticos
- no existe todavía sucursales
- no existe todavía archivos relacionados
- la UI web prioriza una carga más limpia y menos saturada visualmente

## Próximos pasos sugeridos

1. incorporar `MA_CUENTASSUC`
2. separar aún más la carga en subsolapas si hace falta
3. sumar contratos y adjuntos relacionados
4. agregar lookup/selector para cuentas relacionadas en vez de input libre
5. revisar si conviene desacoplar completamente las rutas de `clientes` y `proveedores` de `/ventas` y `/compras`

## Notas para otro modelo/agente

Si otro modelo tiene que continuar este módulo, debería:

1. respetar que la lectura oficial es `VT_CLIENTES` / `VT_PROVEEDORES`
2. no inventar tablas paralelas
3. mantener la escritura real en `MA_CUENTAS` + `MA_CUENTASADIC`
4. tratar `MA_CUENTASOBS` y `C_PROVEEDORES_CCOMPRA` como complementos del maestro
5. revisar primero `CuentasComercialesService` y `CuentasComercialesValidator`
6. usar el VB6 solo como referencia funcional, no como copia literal de UI

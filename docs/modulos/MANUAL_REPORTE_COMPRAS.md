# Manual técnico — Módulo Reporte de Compras

> Fecha: 2026-06-08  
> Módulo: `ReporteCompras`  
> Ruta web: `/compras/reportes`  
> Clave TA_MENU: `D5005`

---

## 1. Descripción general

El módulo **Reporte de Compras** es un generador de informes del área de compras de Alfa Gestión.  
Permite consultar y analizar comprobantes de compra, agrupados o en detalle, y ver la cuenta corriente de proveedores.

El usuario elige el tipo de reporte, configura los filtros y genera el resultado. No hay formularios de carga ni ABM: es un módulo 100 % de consulta.

---

## 2. Acceso y permisos

| Ítem | Valor |
|---|---|
| URL | `/compras/reportes` |
| Clave legacy | `D5005` (Menu: ALFA, Título: D5099) |
| Icono | `bi-bar-chart-line` |
| Habilitado web | Sí |
| Orden menú | 50050 |

La opción ya existía en `TA_MENU` (árbol legacy). El script de actualización `2026-06-08-001__compras_reporte_compras_menu_web.sql` solo registra la ruta en `ALFACORE_MENU_WEB` y propaga permisos en `TA_TAREAS` para usuarios con restricciones activas.

---

## 3. Tipos de reporte

### 3.1 Resumen de Compras

Muestra un resumen agregado del período filtrado.

**KPIs mostrados:**
- Total comprado (importe signado según tipo de comprobante)
- Neto total
- IVA total
- Cantidad de comprobantes
- Cantidad de proveedores distintos

**Tabla por proveedor** (sin paginación, ordenada por total descendente):

| Columna | Descripción |
|---|---|
| Cuenta | Código de cuenta del proveedor |
| Proveedor | Razón social (o cuenta si no tiene razón social) |
| Comprobantes | Cantidad de comprobantes en el período |
| Neto | Suma de neto gravado signado |
| IVA | Suma de IVA signado |
| Total | Total comprado signado |
| % Part. | Participación sobre el total general del período |

**Fuente SQL:** `vw_compras_cabecera_dashboard` — dos queries: una de totales globales y una agrupada por proveedor.

---

### 3.2 Detalle de Compras

Lista todos los comprobantes individualmente, con paginación (50 por página, server-side con OFFSET/FETCH).

**Columnas:**

| Columna | Descripción |
|---|---|
| Fecha | Fecha del comprobante (dd/MM/yyyy) |
| TC | Tipo de comprobante (FCC, NCC, NDC, etc.) |
| Comprobante | ID del comprobante |
| Cuenta | Código del proveedor |
| Proveedor | Razón social |
| Neto | Neto gravado signado |
| IVA | IVA signado |
| Total | Total signado |
| Estado | Estado del comprobante (badge visual) |
| Usuario | Usuario que registró |

**Ordenamiento:** fecha descendente, luego ID de comprobante descendente.

**Fuente SQL:** `vw_compras_cabecera_dashboard`.

---

### 3.3 Cuenta Corriente

Muestra los movimientos contables de proveedores desde `MV_ASIENTOS`, con cálculo de saldo signado.  
**Recomendación:** siempre filtrar por proveedor; sin filtro se devuelven todos los asientos del período.

**KPIs:**
- Saldo total del período (positivo = deuda al proveedor, negativo = a favor del cliente)
- Cantidad de movimientos

**Tabla de movimientos** (paginada, 50 por página):

| Columna | Descripción |
|---|---|
| Cuenta | Código de cuenta |
| Proveedor | Nombre (de MA_CUENTAS) |
| Fecha | Fecha del asiento |
| TC | Tipo de comprobante |
| Comprobante | ID del comprobante |
| D/H | Debe (D) o Haber (H) — badge visual |
| Importe | Importe bruto |
| Signado | Importe con signo: H suma, D resta |

**Convención de signo:**
- `DEBEHABER = 'H'` → el proveedor facturó → suma (deuda)
- `DEBEHABER = 'D'` → pagamos / nota de crédito → resta

**Fuente SQL:** `MV_ASIENTOS` + `MA_CUENTAS` (join por CUENTA).

---

## 4. Filtros disponibles

| Filtro | Tipo | Descripción |
|---|---|---|
| Fecha desde | Date picker | Límite inferior del período (inclusivo) |
| Fecha hasta | Date picker | Límite superior (inclusivo, se agrega +1 día en SQL) |
| Proveedor | Texto libre | Busca por código de cuenta o razón social (LIKE) |
| TC | Combo | Tipos de comprobante disponibles en la vista; se carga al iniciar |

**Accesos rápidos de período:**
- Hoy
- Este mes (1° del mes a hoy)
- Mes anterior
- Año actual

**Inicio por defecto:** primer día del mes actual hasta hoy.

---

## 5. Arquitectura técnica

### 5.1 Archivos del módulo

| Archivo | Rol |
|---|---|
| [ReporteCompras.razor](../src/AlfaCore/Components/Pages/ReporteCompras.razor) | Página Blazor — UI y lógica de presentación |
| [ReporteComprasModels.cs](../src/AlfaCore/Models/ReporteComprasModels.cs) | Modelos, DTOs, filtros y enum de tipo de reporte |
| [IReporteComprasService.cs](../src/AlfaCore/Services/IReporteComprasService.cs) | Interfaz del servicio |
| [ReporteComprasService.cs](../src/AlfaCore/Services/ReporteComprasService.cs) | Implementación: acceso a datos con ADO.NET directo |
| [2026-06-08-001__compras_reporte_compras_menu_web.sql](../src/AlfaCore/App_Data/updates/2026-06-08-001__compras_reporte_compras_menu_web.sql) | Script de menú, mapeo web y permisos |

### 5.2 Vistas SQL utilizadas

| Vista | Rol |
|---|---|
| `vw_compras_cabecera_dashboard` | Base de Resumen y Detalle. Combina `C_MV_Cpte` (con detalle) y `LibroIvaCompras` (registros contables sin comprobante). Aplica signo según TC. |

La vista excluye comprobantes anulados (`ANULADA = 0`). Los registros de `LibroIvaCompras` que ya tienen comprobante correspondiente en `C_MV_Cpte` no se duplican (filtro `NOT EXISTS`).

**Tipos de comprobante y su efecto en el signo:**

| TC | Signo | Tipo |
|---|---|---|
| FCC, NDC, LIQC, FPC | +1 | Compra / Proforma |
| NCC, NCPC | -1 | Nota de crédito |
| Otros | 0 | Sin efecto en totales |

### 5.3 Tecnología de acceso a datos

El servicio usa **ADO.NET directo** (`SqlConnection` / `SqlCommand` / `SqlDataReader`), sin Dapper ni EF. Motivo: las queries comparten una cláusula WHERE parametrizada (`CabeceraWhere`) que se interpola por `string template` y se parametriza con `SqlParameter`.

La cadena de conexión se toma de `ISessionService` (sesión activa del usuario) o del fallback `ConnectionStrings:AlfaGestion`.

---

## 6. Manejo de errores

- Los errores al cargar opciones (TC) se absorben silenciosamente — el combo queda vacío.
- Los errores al generar un reporte se muestran al usuario con `AppUiMessage` (mensaje, sugerencia, código de incidente).
- Los errores técnicos se registran en el logger (hacia `AUX_ERR` según la capa de logging centralizada).

---

## 7. Paginación

Los reportes de **Detalle** y **Cuenta Corriente** usan paginación server-side:
- 50 registros por página (default)
- `OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY` en SQL
- Controles: botones Anterior / Siguiente + indicador de página / total

El **Resumen** no pagina: siempre devuelve todos los proveedores del período (sin límite de filas).

---

## 8. Pendientes y mejoras identificadas

Los siguientes ítems **no están implementados** en la versión actual.

### 8.1 Exportación de datos _(prioridad alta)_
No existe ningún mecanismo de exportación. El usuario solo puede ver los datos en pantalla.

**Pendiente:**
- Exportar a Excel (`.xlsx`) para Resumen y Detalle
- Exportar a PDF para vista imprimible
- Opcionalmente: CSV como formato simple

---

### 8.2 Estados de comprobante incompletos _(prioridad media)_
La vista `vw_compras_cabecera_dashboard` expone los campos `Aprobado`, `Finalizado`, `Bloqueado`, `Cerrada` provenientes de `C_MV_Cpte`, pero el campo `EstadoComprobante` que construye la vista solo distingue `'Anulada'` / `'Activo'`.

**Pendiente:**
- Enriquecer la vista (o el servicio) para mapear `Aprobado`, `Finalizado`, `Bloqueado`, `Cerrada` a un estado funcional útil
- El badge visual de la UI tiene lógica para `"Cerrada"`, `"Finalizada"`, `"Aprobada"`, `"Pendiente"` pero la fuente nunca los devuelve en el estado actual

---

### 8.3 Filtros adicionales _(prioridad media)_
La vista tiene campos disponibles que no están expuestos como filtros:

| Campo en vista | Filtro en UI |
|---|---|
| `SUCURSAL` | No existe |
| `USUARIO` | No existe |
| `OrigenRegistro` (CPTE / LIVA) | No existe |
| `TipoMovimiento` | No existe |

**Pendiente:** agregar al panel de parámetros los filtros Sucursal, Usuario y opcionalmente Origen de registro.

---

### 8.4 Paginación en Resumen de Compras _(prioridad baja)_
El Resumen de Compras trae todos los proveedores del período sin límite. Con bases grandes o períodos amplios esto puede ser lento.

**Pendiente:** evaluar si necesita paginación o si alcanza con un `TOP 200` con advertencia al usuario.

---

### 8.5 Cuenta Corriente — restricción de TC _(prioridad media)_
`MV_ASIENTOS` es una tabla contable general (no exclusiva de compras). El filtro por TC está presente pero no hay validación de que los TC ingresados sean específicos de compras.

**Pendiente:**
- Agregar filtro o aviso que indique qué TCs son relevantes para compras
- Opcionalmente: filtrar por defecto solo los asientos de proveedores (`MA_CUENTAS` con tipo proveedor)

---

### 8.6 Soporte `?directo=1` _(prioridad media)_
La regla de AGENTS.md establece que si una URL entra con `?directo=1`, AlfaCore debe encerrarse en ese módulo (sin mostrar menú ni accesos a otros módulos). La página `ReporteCompras.razor` no implementa esta lógica.

**Pendiente:** agregar detección del parámetro `directo` en la URL y condicionar la visibilidad del layout principal según corresponda (consistente con otros módulos que ya lo implementen).

---

### 8.7 Drill-down a comprobante individual _(prioridad baja)_
El Detalle de Compras muestra el ID del comprobante como texto plano. No hay navegación hacia el detalle del comprobante ni a sus líneas de artículos.

**Pendiente:** evaluar si existe una página de detalle de comprobante (o de la pantalla de Compras legada) a la que enlazar desde el campo `IdComprobante`.

---

### 8.8 Gráficos y análisis visual _(prioridad baja — evolución futura)_
El roadmap original del módulo (en `docs/prompt_dashboard_compras.md`) preveía:
- Evolución mensual de compras
- Top proveedores (gráfico)
- Top rubros / familias / artículos
- Página de estado de recepción (pendiente, parcial, aprobado, finalizado)

Ninguno de estos está implementado en la pantalla actual. Son mejoras de análisis BI para una etapa posterior.

---

## 9. Checklist de implementación actual

| Ítem | Estado |
|---|---|
| Página Razor con los 3 tipos de reporte | ✅ Implementado |
| Filtros básicos (fecha, proveedor, TC) | ✅ Implementado |
| Accesos rápidos de período | ✅ Implementado |
| KPIs en Resumen | ✅ Implementado |
| Tabla de Resumen por proveedor | ✅ Implementado |
| Detalle paginado (OFFSET/FETCH) | ✅ Implementado |
| Cuenta Corriente paginada con saldo | ✅ Implementado |
| Manejo de errores con AppUiMessage | ✅ Implementado |
| Registro en ALFACORE_MENU_WEB | ✅ Implementado |
| Permisos en TA_TAREAS | ✅ Implementado |
| Exportación (Excel / PDF) | ❌ Pendiente |
| Estados de comprobante completos | ❌ Pendiente |
| Filtros de sucursal y usuario | ❌ Pendiente |
| Soporte `?directo=1` | ❌ Pendiente |
| Drill-down a comprobante individual | ❌ Pendiente |
| Gráficos y análisis visual | ❌ Pendiente (etapa futura) |

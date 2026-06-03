# Proyecto: Dashboard de Compras - Alfa Gestión

## Objetivo

Desarrollar una plataforma de Business Intelligence para Alfa Gestión que permita visualizar, analizar y explorar información operativa desde múltiples módulos (Compras, Ventas, Stock, etc.), con acceso web desde cualquier PC de la red.

---

## Arquitectura general

```
[Alfa Gestión SQL Server]
        ↓
[AlfaCore.exe — ASP.NET Core / Kestrel]
        ↓
[Blazor Server — Interactive Server rendering]
        ↓
[Navegador web — cualquier PC de la red]
```

- **Sin IIS.** El servidor web es Kestrel standalone.
- **Sin WebView2.** Los clientes usan cualquier navegador.
- **Servicio de Windows.** La app arranca automáticamente con el servidor.
- **Puerto configurable.** Por defecto `5055`.

---

## Tecnología

| Componente | Tecnología |
|---|---|
| Runtime | .NET 8 / ASP.NET Core |
| UI | Blazor Server (Interactive Server rendering) |
| Base de datos | SQL Server (`Microsoft.Data.SqlClient`) |
| Gráficos | SVG custom (sin dependencias externas) |
| Hosting | Kestrel standalone + Windows Service |
| Cultura | es-AR |
| Tema | Claro / Oscuro (CSS variables + localStorage) |

---

## Estructura de rutas

```
/                                      → Launcher (selector de módulos)
/compras                               → Inicio de Compras
/compras/proveedores                   → Proveedores
/compras/comprobantes                  → Comprobantes
/compras/rubros                        → Rubros
/compras/familias                      → Familias
/compras/articulos                     → Artículos
/compras/actividad                     → Actividad
/compras/informesia                    → InformesIA
/compras/informesia/resultado/{guid}   → Resultado de informe IA
/ayuda                                 → Manual de usuario
```

---

## Módulos planificados

| Módulo | Estado |
|---|---|
| Compras | ✅ Implementado |
| Ventas | 🔜 Próximamente |
| Stock | 🔜 Próximamente |
| Caja y Bancos | 🔜 Próximamente |
| Contabilidad | 🔜 Próximamente |
| Diseñador de consultas | 🔜 Próximamente |

---

## Origen de datos

### Tablas principales
- `C_MV_Cpte` — Cabecera de comprobantes
- `C_MV_CpteInsumos` — Detalle (ítems)

### Tablas maestras
- `Vt_Proveedores`
- `V_MA_ARTICULOS`
- `V_TA_Rubros`
- `V_TA_FAMILIAS`

### Tipos de comprobantes

| Tipo | Signo | Descripción |
|---|---|---|
| FCC, NDC, LIQC, FPC | +1 | Suman (compras) |
| NCC, NCPC | -1 | Restan (créditos) |

---

## Vistas SQL

### `vw_compras_cabecera_dashboard`
- Una fila por comprobante
- Campos: `TC`, `IDCOMPROBANTE`, `NUMERO`, `FECHA`, `CUENTA`, `RAZON_SOCIAL`, `SUCURSAL`, `IdDeposito`, `USUARIO`, `EstadoComprobante`
- Importes con signo: `ImporteDashboard`, `NetoDashboard`, `IvaDashboard`
- Flags derivados: `TieneDetalle`, `EsContable`, `IvaEnCero`, `AlertaOperativa`

### `vw_compras_detalle_dashboard`
- Una fila por ítem de comprobante
- Datos del artículo: `IDARTICULO`, `DESCRIPCION_ARTICULO`, `RUBRO`, `FAMILIA`
- Cantidades y totales con signo: `CantidadDashboard`, `TotalDashboard`
- Hereda campos de cabecera: `TC`, `IDCOMPROBANTE`, `CUENTA`, `FECHA`, `USUARIO`, `SUCURSAL`, `IdDeposito`

---

## Lógica de negocio

### Importes calculados
- `ImporteDashboard` — importe total con signo
- `NetoDashboard` — neto sin IVA con signo
- `IvaDashboard` — monto IVA con signo
- `CantidadDashboard` — cantidad de ítems con signo
- `TotalDashboard` — importe final para análisis

### Alertas operativas automáticas
- Comprobante con importe = 0
- Comprobante sin detalle de artículos
- Comprobante con IVA = 0 en importe significativo

---

## Pantallas — Módulo Compras

### Launcher (`/`)
- Grilla de módulos con cards visuales
- Compras activo, resto con badge "Próximamente"
- Fondo y estilo adaptado a modo claro/oscuro

### Inicio (`/compras`)
**KPIs:** Total comprado, Neto, IVA, Comprobantes, Artículos distintos, Proveedores activos, Ticket promedio

**Gráficos:**
- Evolución mensual (LineChart — 12 meses)
- Top 7 proveedores (HorizontalBarChart)
- Top 7 rubros (HorizontalBarChart)

**Tabla:** Top 10 artículos con link a historial

### Proveedores (`/compras/proveedores`)
**KPIs:** Total, proveedores activos, principal, concentración Top 5, variación, mayor crecimiento, mayor caída

**Gráficos:** Ranking Top 10, concentración Top 5 vs resto

**Tabla:** Cuenta, razón social, total, participación %, comprobantes, ticket promedio, última compra, variación

**Ficha:** Top artículos, últimos comprobantes, evolución mensual

### Comprobantes (`/compras/comprobantes`)
**KPIs:** Importe total, cantidad, proveedores, ticket promedio, alertas (en cero, sin detalle, IVA cero), máximo, TC predominante

**Filtros rápidos:** Críticos, con detalle, contables, valor cero, IVA cero, sin detalle

**Tabla paginada (20/página):** Fecha, TC, número, proveedor, neto, IVA, total, estado, detalle, alertas, usuario

**Modal de detalle:** Líneas del comprobante con artículo, rubro, familia, cantidad, costo, total

### Rubros (`/compras/rubros`)
**KPIs:** Total, rubros activos, principal y participación, variación, concentración Top 3, mayor crecimiento/caída

**Gráficos:** Top rubros, distribución %, concentración, crecimiento vs caída

**Tabla:** Rubro, total, participación %, comprobantes, artículos, variación, ticket promedio

**Ficha:** Evolución mensual, top artículos, top proveedores, últimos comprobantes

### Familias (`/compras/familias`)
**KPIs:** Total, familias activas, principal, variación, concentración Top 5, mayor crecimiento/caída

**Tabla:** Familia, descripción, padre, nivel, total, participación %, artículos, proveedores, variación

**Ficha:** Composición interna, artículos, proveedores, evolución mensual, últimos comprobantes

### Artículos (`/compras/articulos`)
**KPIs:** Total, artículos distintos, total ítems, costo promedio, artículos con aumento/baja, mayor aumento/baja

**Gráficos:** Top por impacto económico, top por cantidad, top aumentos, top bajas

**Tabla:** Artículo, cantidad, total, costo promedio, precio anterior/actual, variación %, última compra, proveedor principal

**Ficha:** Evolución de costo mensual, desglose por proveedor, historial de comprobantes

### Actividad (`/compras/actividad`)
**KPIs:** Facturas cargadas, ítems, usuarios activos, promedio ítems/factura, promedio facturas/usuario, día mayor actividad, usuario más activo

**Gráficos diarios:** Facturas por día (LineChart), ítems por día (LineChart), segmentación tipo de carga

**Tabla de usuarios:** Facturas, ítems, ítems/factura, importe, última actividad, días activos, % detalle/contable

**Ficha de usuario:** Serie diaria, comprobantes recientes

### InformesIA (`/compras/informesia`)
- Consulta en lenguaje natural sobre las vistas autorizadas
- Opción de incluir gráfico en el resultado
- Dictado por voz
- Sugerencias rápidas predefinidas
- Historial de últimas 20 consultas (cargar, repetir, abrir, borrar)
- Cada informe abre en nueva pestaña con URL propia

**Resultado (`/compras/informesia/resultado/{guid}`):**
- Tabla de datos + resumen estadístico
- Gráfico SVG cuando aplica
- Exportar PDF

---

## Filtros globales

Persisten entre páginas via `FilterStateService` (Singleton).

| Filtro | Tipo |
|---|---|
| FechaDesde / FechaHasta | Fecha |
| Proveedor | Texto (búsqueda parcial) |
| ArticuloCodigo | Texto (coincidencia exacta) |
| ArticuloDescripcion | Texto (búsqueda parcial) |
| Rubro | Selector |
| Familia | Selector |
| Usuario | Selector |
| Sucursal | Selector |
| Deposito | Selector |
| Estado | Selector |
| TipoComprobante | Selector |

**Accesos rápidos:** Hoy / Esta semana / Este mes / Mes anterior / Últimos 3 meses / Año actual

---

## Componentes compartidos

| Componente | Descripción |
|---|---|
| `GlobalFiltersBar` | Barra de filtros + accesos rápidos + resumen de activos |
| `KpiCard` | Tarjeta de indicador con ícono, título, valor y descripción |
| `LineChart` | Gráfico SVG con curvas Catmull-Rom, área, etiquetas inteligentes |
| `HorizontalBarChart` | Barras horizontales SVG con etiquetas, valores y links opcionales |
| `DataTable` | Grilla genérica con headers configurables y toolbar |
| `DetailCard` | Panel lateral de detalle con filas clave/valor |

---

## Servicios

### `ComprasDashboardService` (clase parcial)

| Archivo | Responsabilidad |
|---|---|
| `ComprasDashboardService.cs` | Configuración, conexión, helpers SQL, logging |
| `ComprasDashboardService.Dashboard.cs` | KPIs del resumen general |
| `ComprasDashboardService.Comprobantes.cs` | Listado paginado, overview y detalle |
| `ComprasDashboardService.Entities.cs` | Proveedores, rubros, familias, artículos |
| `ComprasDashboardService.Activity.cs` | Actividad por usuario y por día |
| `ComprasDashboardService.Shared.cs` | Métodos utilitarios compartidos |

### `InformesIaService`
Genera, valida y almacena resultados de consultas en lenguaje natural. Usa plantillas SQL seguras de solo lectura.

### `FilterStateService`
Singleton que mantiene `DashboardFilters` entre navegaciones.

### `StartupConnectionResolver`
Resuelve la cadena de conexión al iniciar. Si `appsettings.Production.json` no tiene datos completos, solicita credenciales por consola y las guarda para usos futuros.

### `ServerStartupHostedService`
Tarea de inicio que abre el navegador automáticamente (configurable).

---

## UI — Layout y tema

### Layout (`MainLayout.razor`)
- Sidebar colapsable (estado persistido en localStorage)
- Context-aware: muestra nav del módulo activo o el launcher
- Botón "← Aplicaciones" al entrar en un módulo
- Toggle claro/oscuro (luna/sol) en el header
- Exportar PDF de la página actual
- Logo de Alfa Gestión en el sidebar

### Modo claro/oscuro
- Implementado con `data-theme` en `<html>` y CSS custom properties
- Persistido en localStorage
- Aplicado antes de renderizar (sin flash)
- Default: **modo claro**

---

## Scripts

| Script | Descripción |
|---|---|
| `scripts\publicar_release.bat` | Publica la app en `publish\AlfaCoreLAN` |
| `scripts\publicar_instalador.bat [version]` | Genera el Setup.exe completo con Inno Setup |
| `scripts\actualizar_instalacion.bat` | Actualiza una instalación existente sin pisar JSON ni configuraciones |
| `scripts\instalar_servicio.bat` | Instala el servicio de Windows (requiere admin) |
| `scripts\desinstalar_servicio.bat` | Desinstala el servicio de Windows (requiere admin) |

---

## Deploy

### Instalador (recomendado)
1. `scripts\publicar_instalador.bat 1.0.0`
2. Distribuir `publish\AlfaCoreInstaller\Output\AlfaCoreSetup_X.X.X.exe`
3. El setup instala .NET 8 Hosting Bundle si falta
4. Instala el servicio de Windows automáticamente
5. En el primer inicio la app pide credenciales SQL y las guarda en `appsettings.Production.json`

### Manual
1. `scripts\publicar_release.bat`
2. Copiar `publish\AlfaCoreLAN` al servidor
3. Editar `appsettings.Production.json`
4. `abrir_firewall.bat` como admin
5. `instalar_servicio.bat` como admin

### Actualización
```batch
robocopy publish\AlfaCoreLAN "C:\ruta\instalacion" /MIR /XF appsettings.Production.json *.log
sc stop AlfaCore && sc start AlfaCore
```

Opción recomendada para actualizar sin reinstalar:
```batch
scripts\actualizar_instalacion.bat
```

---

## Configuración (`appsettings.Production.json`)

```json
{
  "ConnectionStrings": {
    "AlfaGestion": "Server=AGSERVER\\ALFANET;Database=ALFANET;User ID=...;Password=...;TrustServerCertificate=True;"
  },
  "ServidorWeb": {
    "Puerto": 5055,
    "EscucharEnRed": true
  }
}
```

---

## Flujos de navegación

```
/  (Launcher)
└── /compras  (Inicio)
    ├── /compras/proveedores → ficha proveedor → comprobantes / artículos
    ├── /compras/comprobantes → detalle comprobante → líneas de ítems
    ├── /compras/rubros → ficha rubro → artículos / comprobantes
    ├── /compras/familias → ficha familia → subfamilias / artículos
    ├── /compras/articulos → ficha artículo → evolución / proveedores
    ├── /compras/actividad → ficha usuario → serie diaria / comprobantes
    └── /compras/informesia → resultado en nueva pestaña
```

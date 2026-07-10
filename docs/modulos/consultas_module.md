# Módulo: Diseñador de Consultas — Alfa Gestión Dashboard

**Versión del documento:** 1.0  
**Fecha:** 2026-04-21  
**Estado:** Planificación — Etapa 1 aprobada para desarrollo

---

## 1. Objetivo

Migrar el sistema de consultas guardadas del ejecutable `MA_CONSULTAS.exe` (VB6) a una interfaz web moderna integrada en el Dashboard de Alfa Gestión.

El módulo permite a los usuarios:
- Ver y ejecutar las **consultas ya guardadas** en el sistema (listas, reportes, controles operativos)
- **Exportar resultados** a Excel y PDF desde el navegador
- (futuro) Crear y modificar consultas visualmente sin depender del VB6

El valor central está en los datos: las consultas guardadas en `V_TA_SCRIPT` representan años de configuración por parte de los usuarios. El módulo las expone de forma moderna sin necesidad de migrar ni tocar esos datos.

---

## 2. Contexto del sistema

### 2.1 Sistema origen (VB6 — MA_CONSULTAS.exe)

| Característica | Detalle |
|---|---|
| Tecnología | VB6 + ADO + Crystal Reports |
| Base de datos | SQL Server `ALFANET` en `AGSERVER\ALFANET` |
| Interfaz | Windows Forms con controles OCX legacy |
| Usuarios | Red interna, instalado localmente en cada PC |
| Limitación principal | Solo funciona en Windows con dependencias OCX instaladas |

### 2.2 Sistema destino (Blazor Server — Dashboard)

| Característica | Detalle |
|---|---|
| Tecnología | ASP.NET Core 8 / Blazor Server |
| Base de datos | Misma — SQL Server `ALFANET` (misma cadena de conexión) |
| Interfaz | Navegador web — cualquier PC de la red |
| Acceso | `http://SERVIDOR:5055/consultas` |
| Ventaja | Un solo punto de acceso, sin instalación en clientes |

---

## 3. Tablas de base de datos

Las consultas guardadas viven en tres tablas del sistema Alfa Gestión. **No se modifican ni migran.** El módulo las lee directamente.

### 3.1 `V_TA_SCRIPT` — Definición de consultas

Cada fila es una consulta guardada. Estructura real verificada en producción.

| Columna | Tipo | Descripción |
|---|---|---|
| `ID` | int IDENTITY (PK) | Identificador único |
| `CLAVE` | nvarchar(20) | Código visible de la consulta (ej. `"1010"`) |
| `GRUPO` | nvarchar(100) | Agrupación (ej. `"10 - CONTROLES ALFA"`) |
| `DESCRIPCION` | nvarchar(255) | Nombre visible de la consulta |
| `Marca` | nvarchar(15) | Filtro de módulo: `'CL'` = Consultas/Listados |
| `SQL` | ntext | Sentencia SQL completa; puede contener tokens `<P>` para parámetros |
| `IdLista` | nvarchar(4) | Identificador de lista secundario |
| `Tipo` | nvarchar(50) | Tipo interno de la consulta |
| `Tabla` | nvarchar(100) | Tabla principal de la consulta |
| `CamposTotaliza` | nvarchar(250) | Campos de agregado (SUM, etc.) |
| `CamposGrupo` | nvarchar(250) | Campos de GROUP BY |
| `CamposOrdenar` | nvarchar(250) | Campos de ORDER BY |
| `AlMenu` | bit | Si aparece en el menú de reportes |
| `Usuario` | nvarchar(50) | Usuario que la creó |

> **Filtro activo:** `WHERE Marca = 'CL'` selecciona las consultas del módulo Consultas-Listados del VB6. Otros módulos usan otros valores de `Marca`.

### 3.2 `V_TA_SCRIPT_CFG` — Campos y parámetros de cada consulta

Cada consulta puede tener cero o más filas. Cada fila representa un campo seleccionado o un parámetro de ejecución.

| Columna | Tipo | Descripción |
|---|---|---|
| `ID` | int IDENTITY (PK) | Identificador único |
| `IdScript` | int (FK) | Referencia a `V_TA_SCRIPT.ID` |
| `CampoSel` | nvarchar(100) | Nombre del campo (usado como etiqueta del parámetro) |
| `EsParametro` | bit | `1` = el usuario debe ingresar un valor en ejecución |
| `TablaConsulta` | nvarchar(100) | Tabla de lookup para el campo |
| `CampoRetorno` | nvarchar(100) | Campo de retorno del lookup |

> **Importante:** No existen columnas `OPERADOR`, `VALOR` ni `DESCRIPCION` en esta tabla. El operador y el valor predeterminado no se almacenan — el SQL en `V_TA_SCRIPT.SQL` contiene los tokens `<P>` que se sustituyen en orden con los valores ingresados por el usuario.

### 3.3 `TA_CONFIGURACION` — Configuración general del sistema

Almacén clave-valor del sistema Alfa Gestión. Acceso solo lectura desde el módulo.

| Columna | Tipo | Descripción |
|---|---|---|
| `ID` | int IDENTITY | Identificador interno |
| `GRUPO` | nvarchar(50) (PK1) | Grupo lógico |
| `CLAVE` | nvarchar(50) (PK2) | Clave de la configuración |
| `VALOR` | nvarchar(150) | Valor principal |
| `DESCRIPCION` | nvarchar(50) | Descripción |
| `ValorAux` | ntext | Valor auxiliar extendido |

---

## 4. Arquitectura del módulo dentro del Dashboard

```
/consultas                          → Lista de consultas agrupadas (árbol lateral + cards)
/consultas/{id}                     → Detalle + ejecución de una consulta
/consultas/{id}/exportar/excel      → Descarga directa de Excel
```

### 4.1 Encaje en el sistema existente

```
Launcher (/)
└── Diseñador de Consultas (/consultas)   ← NUEVO MÓDULO
    ├── ConsultasService                  ← Nuevo servicio
    ├── ConsultasModels                   ← Nuevos modelos
    └── Páginas Razor                     ← Nuevas páginas
```

El módulo **comparte** con el resto del dashboard:
- La cadena de conexión `AlfaGestion` (ya configurada en `appsettings`)
- El `MainLayout.razor` (sidebar context-aware, toggle claro/oscuro, PDF print)
- El patrón de servicios `Scoped` inyectados en páginas Razor
- Los componentes `DataTable`, `KpiCard`, `DetailCard`

El módulo **no comparte**:
- `FilterStateService` — tiene sus propios filtros de ejecución
- Las vistas SQL de Compras (`vw_compras_*`) — consulta directamente las tablas configuradas en cada query

---

## 5. Modelos de datos (C#)

```csharp
// Consulta guardada (de V_TA_SCRIPT)
public sealed class ConsultaGuardadaDto
{
    public int Id { get; init; }
    public string Clave { get; init; } = string.Empty;
    public string Grupo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Comentarios { get; init; } = string.Empty;
    public string Sql { get; init; } = string.Empty;
    public bool TieneParametros { get; init; }
    public IReadOnlyList<ParametroConsultaDto> Parametros { get; init; } = [];
}

// Parámetro de ejecución (de V_TA_SCRIPT_CFG donde EsParametro = 1)
// CampoSel se usa como etiqueta; no hay ValorDefault ni Operador almacenados.
public sealed class ParametroConsultaDto
{
    public int Orden { get; init; }
    public string Campo { get; init; } = string.Empty;   // viene de CampoSel
    public TipoParametro Tipo { get; init; }             // detectado por heurística del nombre
}

// Grupo de consultas para el árbol lateral
public sealed class GrupoConsultasDto
{
    public string Nombre { get; init; } = string.Empty;
    public IReadOnlyList<ConsultaGuardadaDto> Consultas { get; init; } = [];
}

// Resultado de ejecución
public sealed class ConsultaResultadoDto
{
    public bool Exitoso { get; init; }
    public string? MensajeError { get; init; }
    public IReadOnlyList<string> Columnas { get; init; } = [];
    public IReadOnlyList<string[]> Filas { get; init; } = [];
    public int TotalFilas { get; init; }
    public TimeSpan TiempoEjecucion { get; init; }
    public DateTime EjecutadoEn { get; init; }
}

// Request de ejecución (enviado desde la UI)
public sealed class EjecutarConsultaRequest
{
    public int ConsultaId { get; init; }
    public Dictionary<string, string> ValoresParametros { get; init; } = [];
}
```

---

## 6. Servicio `ConsultasService`

Clase única (`Scoped`) responsable de:
1. Leer grupos y consultas desde `V_TA_SCRIPT`
2. Leer parámetros desde `V_TA_SCRIPT_CFG`
3. Validar SQL antes de ejecutar (solo lectura)
4. Ejecutar consultas con parámetros sustituidos
5. Formatear resultados

### 6.1 Interfaz

```csharp
public interface IConsultasService
{
    Task<IReadOnlyList<GrupoConsultasDto>> GetGruposAsync(CancellationToken ct = default);
    Task<ConsultaGuardadaDto?> GetConsultaAsync(int id, CancellationToken ct = default);
    Task<ConsultaResultadoDto> EjecutarAsync(EjecutarConsultaRequest request, CancellationToken ct = default);
}
```

### 6.2 SQL de carga de consultas

```sql
-- Grupos y consultas
SELECT s.ID, s.CLAVE, s.GRUPO, s.DESCRIPCION, s.COMENTARIOS, s.SQL
FROM V_TA_SCRIPT s
WHERE s.MARCA = 'CL'
ORDER BY s.GRUPO, s.CLAVE

-- Parámetros de una consulta
SELECT cfg.ID, cfg.CAMPO, cfg.OPERADOR, cfg.VALOR, cfg.DESCRIPCION
FROM V_TA_SCRIPT_CFG cfg
WHERE cfg.IDSCRIPT = @Id AND cfg.ESPARAMETRO = 1
ORDER BY cfg.ID
```

### 6.3 Sustitución de parámetros

El SQL de cada consulta puede contener tokens `<P>` que representan valores en tiempo de ejecución. El servicio los sustituye antes de ejecutar:

```
SQL original:  WHERE Fecha >= <P> AND Fecha <= <P>
Con params:    WHERE Fecha >= '2026-01-01' AND Fecha <= '2026-04-21'
```

El orden de los `<P>` coincide con el orden de los parámetros en `V_TA_SCRIPT_CFG`.

> **Seguridad:** Antes de sustituir y ejecutar, el SQL pasa por el validador. No se permiten sentencias que no sean `SELECT`. La sustitución usa parámetros SQL (`SqlParameter`) cuando el tipo lo permite; en caso contrario aplica escape seguro de valores.

### 6.4 Validación de SQL (reutiliza `InformesIaSqlValidator`)

Se bloquean:
- `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `EXEC`, `EXECUTE`
- Múltiples sentencias separadas por `;`
- Comentarios que oculten comandos (`--`, `/* */`)

Se permite:
- `SELECT` con cualquier complejidad (JOIN, subqueries, CTE, UNION)
- `WITH ... AS (SELECT ...)`

### 6.5 Límite de filas

- Por defecto: máximo **500 filas** devueltas al navegador
- Si el resultado excede el límite: se muestra aviso y se ofrece exportar a Excel para ver todo
- El Excel exporta sin límite de filas

---

## 7. Pantallas del módulo

### 7.1 Lista de consultas — `/consultas`

**Propósito:** punto de entrada al módulo. Muestra todas las consultas agrupadas.

#### Layout
```
┌─────────────────────────────────────────────────────────────────┐
│  [Buscador de consultas]                            [Buscar]    │
├──────────────────┬──────────────────────────────────────────────┤
│  10 - CONTROLES  │  [Card]  1001 - Cambios de precios del día   │
│  ├ 1001          │          Se muestran los artículos con...    │
│  ├ 1002          │          [Ver y ejecutar →]                  │
│  └ 1010          │                                              │
│                  │  [Card]  1002 - Altas Diarias                │
│  50 - CONSULTAS  │          Lista de altas del día actual.      │
│  ├ 5001          │          [Ver y ejecutar →]                  │
│  └ 5002          │                                              │
│                  │  [Card]  1010 - Comprobantes cargados...     │
│                  │          Comprobantes de todo el mes en...   │
│                  │          [Ver y ejecutar →]                  │
└──────────────────┴──────────────────────────────────────────────┘
```

#### Componentes
- **Panel izquierdo (árbol):** grupos colapsables; al hacer click en un grupo, el panel derecho muestra sus consultas. En mobile: dropdown en lugar de árbol.
- **Panel derecho (cards):** una card por consulta con: código, nombre, descripción y botón "Ver y ejecutar".
- **Buscador superior:** filtra por nombre o código en tiempo real (sin ir al servidor). Solo busca en las consultas ya cargadas en memoria.

#### Datos cargados
- Al iniciar la página: carga todos los grupos con sus consultas en una sola llamada (`GetGruposAsync`).
- Los parámetros se cargan lazily al abrir una consulta.

---

### 7.2 Detalle y ejecución — `/consultas/{id}`

**Propósito:** ver la definición de una consulta, completar parámetros y ejecutarla.

#### Layout
```
┌─────────────────────────────────────────────────────────────────┐
│  ← Volver a consultas                                           │
│                                                                 │
│  1010 — Comprobantes cargados por sistemas                      │
│  Se muestran los comprobantes cargados de todo el mes en curso  │
│                                                                 │
│  ┌─ Parámetros ──────────────────────────────────────────────┐  │
│  │  Fecha desde:  [2026-04-01]                               │  │
│  │  Fecha hasta:  [2026-04-21]                               │  │
│  │  Proveedor:    [_____________]                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  [Ejecutar consulta]       [Exportar Excel]   [Exportar PDF]   │
│                                                                 │
│  ┌─ Resultado ────────────────────────────────────────────────┐ │
│  │  507 filas — Se muestran las primeras 500 [ver todo →]    │ │
│  │  Tiempo: 0.42 seg                                         │ │
│  │                                                           │ │
│  │  Fh_Alta    Fecha      tc   Nro.Cpte   CUENTA   DESCR...  │ │
│  │  17-04-26   17-04-26   FCC  000300..   2110100  CASPANI   │ │
│  │  ...                                                      │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

#### Flujo de ejecución
1. Usuario llega a `/consultas/{id}` — se carga la consulta y sus parámetros
2. Si hay parámetros: se muestra el formulario con valores por defecto (de `V_TA_SCRIPT_CFG`)
3. Usuario completa valores y pulsa **Ejecutar consulta**
4. El servicio sustituye los `<P>` con los valores ingresados, valida el SQL y lo ejecuta
5. Se muestran columnas y filas en la tabla
6. Si hay más de 500 filas: aviso + botón "Exportar Excel (sin límite)"
7. Si la consulta no tiene parámetros: el botón Ejecutar está disponible inmediatamente; en OnInitializedAsync se auto-ejecuta

#### Comportamiento de parámetros

| Tipo detectado | Control mostrado |
|---|---|
| Fecha (nombre contiene "fecha", "desde", "hasta") | `<input type="date">` |
| Número (nombre contiene "id", "codigo", "importe") | `<input type="number">` |
| Texto (default) | `<input type="text">` |

> La detección de tipo es heurística basada en el nombre del campo y el operador. En Etapa 2 se mejora con lectura del tipo real de la columna desde `INFORMATION_SCHEMA`.

#### Tabla de resultados
- Componente `DataTable` existente adaptado para columnas dinámicas
- Columnas con ancho automático
- Sin paginación en el primer prototipo (solo límite de 500 filas)
- Posibilidad de ordenar por columna (click en header) — Etapa 2

---

### 7.3 Exportar a Excel — `/consultas/{id}/exportar/excel`

No es una página visible. Es un endpoint HTTP que:
1. Recibe `id` de consulta + parámetros en query string o POST body
2. Ejecuta la consulta **sin límite de filas**
3. Genera el archivo `.xlsx` con `ClosedXML`
4. Retorna `FileStreamResult` con `Content-Disposition: attachment`

#### Formato del Excel generado
- **Fila 1:** Nombre de la consulta + fecha de exportación (celdas fusionadas, fondo gris oscuro, texto blanco)
- **Fila 2:** Encabezados de columna (fondo azul, texto blanco, negrita)
- **Filas 3+:** Datos (filas alternas con fondo blanco / gris muy claro)
- **Pie:** Total de filas en última celda de la columna A
- Ajuste automático de ancho de columna (`AutoFitColumns`)
- Nombre de archivo: `{CLAVE}_{DESCRIPCION}_{yyyyMMdd}.xlsx`

#### Librería
- **ClosedXML** — NuGet package `ClosedXML` (gratuita, MIT)
- Sin dependencias de Excel instalado en el servidor

---

### 7.4 Exportar a PDF

Se reutiliza el mecanismo ya existente en el Dashboard: `window.print()` con CSS de impresión.

Al hacer click en "Exportar PDF":
- Se llama `dashboardExport.printCurrentPage(titulo)` (JS ya implementado)
- El CSS `@media print` oculta sidebar, header, botones y muestra solo la tabla

No requiere código adicional. La tabla de resultados ya es imprimible con el CSS actual.

---

## 8. Sidebar — integración con MainLayout

El módulo se integra al sidebar context-aware existente. Se agrega la rama `"consultas"` en `MainLayout.razor`:

```razor
else if (_currentModule == "consultas")
{
    <a class="menu__back" href="/">
        <i class="bi bi-grid-3x3-gap-fill"></i>
        <span>Aplicaciones</span>
    </a>
    <p class="menu__section">Diseñador de Consultas</p>
    <nav class="menu__nav">
        <NavLink class="menu__link" href="/consultas" Match="NavLinkMatch.All">
            <span class="menu__icon"><i class="bi bi-table"></i></span>
            <span>Mis consultas</span>
        </NavLink>
    </nav>
}
```

En el Launcher (`/`), la card de "Diseñador de Consultas" pasa de badge "Próximamente" a link activo a `/consultas`.

`GetCurrentPageTitle()` en MainLayout se actualiza para reconocer:
```csharp
"consultas" => "Consultas",
_ when path.StartsWith("consultas/") => "Consulta",
```

---

## 9. Seguridad

### 9.1 Solo lectura garantizada

- El `ConsultasService` usa la misma cadena de conexión que el resto del dashboard
- Antes de cada ejecución, el SQL pasa por el validador (bloquea toda escritura)
- No se expone ningún endpoint que permita modificar `V_TA_SCRIPT` o `V_TA_SCRIPT_CFG`

### 9.2 Inyección SQL en parámetros

Los valores ingresados por el usuario **no se concatenan directamente** al SQL. Se usan dos estrategias:

1. **`SqlParameter` tipado:** cuando el tipo del campo es conocido (fecha, número)
2. **Escape seguro de string:** cuando el campo es texto — se aplica `Replace("'", "''")` y se envuelve en comillas simples

El token `<P>` se reemplaza por `@param0`, `@param1`, etc., y los valores se pasan como `SqlParameter[]`.

### 9.3 Timeout de ejecución

- Cada consulta tiene un timeout de **30 segundos**
- Si se excede: se muestra error con mensaje amigable
- El timeout es configurable por `appsettings` (`Consultas:TimeoutSegundos`)

### 9.4 Acceso por red

El módulo no tiene autenticación propia (igual que el resto del dashboard). El acceso está limitado por la red interna. Si en el futuro se requiere autenticación, se implementa a nivel de middleware de ASP.NET Core.

---

## 10. Archivos a crear

### 10.1 Nuevos archivos

| Archivo | Tipo | Descripción |
|---|---|---|
| `src/.../Models/ConsultasModels.cs` | C# | DTOs: `ConsultaGuardadaDto`, `GrupoConsultasDto`, `ParametroConsultaDto`, `ConsultaResultadoDto`, `EjecutarConsultaRequest` |
| `src/.../Services/IConsultasService.cs` | C# | Interfaz del servicio |
| `src/.../Services/ConsultasService.cs` | C# | Implementación del servicio (SQL, validación, ejecución) |
| `src/.../Services/ConsultasExcelExporter.cs` | C# | Exportación a Excel con ClosedXML |
| `src/.../Components/Pages/Consultas.razor` | Razor | Lista de consultas (`/consultas`) |
| `src/.../Components/Pages/ConsultaDetalle.razor` | Razor | Detalle y ejecución (`/consultas/{id}`) |

### 10.2 Archivos modificados

| Archivo | Cambio |
|---|---|
| `Program.cs` | Registrar `IConsultasService` + `ConsultasExcelExporter` |
| `MainLayout.razor` | Agregar rama `consultas` al sidebar |
| `Launcher.razor` | Activar card "Diseñador de Consultas" |
| `docs/CHANGELOG.md` | Agregar entrada v1.2.0 al completar Etapa 1 |
| `docs/arquitectura/AlfaCore.md` | Agregar rutas y módulo |

### 10.3 Nuevo paquete NuGet

```
ClosedXML — versión 0.102.x o superior
```

Instalación:
```bash
dotnet add src/AlfaCore/AlfaCore.csproj package ClosedXML
```

---

## 11. Plan de implementación

### Etapa 1 — Visor de consultas guardadas (2-3 semanas)

**Objetivo:** Los usuarios pueden ver, ejecutar y exportar las consultas del VB6 desde el navegador.

| Paso | Tarea | Estimación |
|---|---|---|
| 1.1 | Verificar estructura real de `V_TA_SCRIPT` y `V_TA_SCRIPT_CFG` contra SQL Server de producción | 1 día |
| 1.2 | Crear `ConsultasModels.cs` con todos los DTOs | 0.5 día |
| 1.3 | Crear `IConsultasService.cs` y `ConsultasService.cs` con lectura de grupos/consultas | 1 día |
| 1.4 | Crear `Consultas.razor` — lista con árbol lateral + cards | 2 días |
| 1.5 | Crear `ConsultaDetalle.razor` — formulario de parámetros + ejecución + tabla resultado | 3 días |
| 1.6 | Agregar `ConsultasExcelExporter.cs` con ClosedXML | 1 día |
| 1.7 | Integrar en Launcher y sidebar | 0.5 día |
| 1.8 | Pruebas con consultas reales de Alfa Gestión | 2 días |

**Criterio de aceptación Etapa 1:**
- [ ] Las consultas del VB6 aparecen agrupadas en la lista
- [ ] Se puede ejecutar una consulta sin parámetros y ver el resultado
- [ ] Se puede ejecutar una consulta con parámetros completando el formulario
- [ ] El resultado se puede exportar a Excel
- [ ] El resultado se puede imprimir/exportar a PDF
- [ ] Consultas con más de 500 filas muestran aviso y ofrecen Excel completo
- [ ] El módulo funciona en modo claro y oscuro
- [ ] El módulo aparece activo en el Launcher

---

### Etapa 2 — Constructor visual de consultas (3-4 semanas, futuro)

**Objetivo:** Crear nuevas consultas o modificar existentes desde el navegador.

Funcionalidades:
- Selector de fuente de datos (vistas y tablas autorizadas)
- Selector de campos con drag & drop de orden
- Constructor de filtros (campo / operador / valor / AND-OR)
- Configuración de ORDER BY y GROUP BY
- Funciones de agregado (SUM, AVG, COUNT, MIN, MAX)
- Vista previa del SQL generado (modo lectura)
- Guardar consulta en `V_TA_SCRIPT` / `V_TA_SCRIPT_CFG`

Nuevas vistas autorizadas (configurables en `appsettings`):
```json
"Consultas": {
  "VistasAutorizadas": [
    "vw_compras_cabecera_dashboard",
    "vw_compras_detalle_dashboard",
    "V_MA_ARTICULOS",
    "VT_Proveedores"
  ]
}
```

---

### Etapa 3 — Permisos y administración (futuro)

**Objetivo:** Control de acceso por grupo o usuario.

- Cada consulta puede marcarse como pública o privada
- Grupos de usuarios con acceso a grupos de consultas
- Historial de ejecuciones por usuario
- Administración de consultas (alta, baja, clonado)

---

## 12. Preguntas abiertas a resolver antes de arrancar

Antes de comenzar Etapa 1, verificar en producción:

1. **Estructura exacta de tablas:** ejecutar `SELECT TOP 1 * FROM V_TA_SCRIPT` y `SELECT TOP 1 * FROM V_TA_SCRIPT_CFG` para confirmar nombres de columnas reales (pueden diferir de los documentados en el VB6).

2. **Valor de MARCA:** confirmar que `'CL'` es el único valor para las consultas del módulo Consultas, o si hay otros valores de interés.

3. **Formato de parámetros `<P>`:** confirmar si el token es exactamente `<P>` o puede variar (ej. `<P1>`, `?`, etc.).

4. **Vistas vs tablas:** confirmar si `V_TA_SCRIPT` es una vista o tabla física, y si hay restricciones de acceso.

5. **Cantidad de consultas guardadas:** `SELECT COUNT(*), GRUPO FROM V_TA_SCRIPT WHERE MARCA='CL' GROUP BY GRUPO` — para dimensionar la UI del árbol lateral.

---

## 13. Consideraciones de UX

- El módulo es "de datos": los usuarios son operativos que ya conocen las consultas del VB6. No necesitan aprender nada nuevo.
- Los nombres de grupos y consultas se muestran **exactamente como están en la base de datos** sin transformar.
- Los errores de SQL se muestran con mensaje amigable, sin exponer el SQL interno ni el stack trace.
- Las consultas lentas (> 5 seg) muestran un spinner con mensaje "Ejecutando consulta…" para no generar ansiedad.
- La tabla de resultados soporta scroll horizontal para consultas con muchas columnas.

---

## 14. Glosario

| Término | Significado en este contexto |
|---|---|
| Consulta guardada | Una sentencia SQL pre-configurada, almacenada en `V_TA_SCRIPT`, con nombre y descripción para el usuario |
| Parámetro `<P>` | Token en el SQL que será sustituido por un valor ingresado por el usuario antes de ejecutar |
| MARCA `'CL'` | Valor del campo `MARCA` en `V_TA_SCRIPT` que identifica las consultas del módulo Consultas-Listados |
| Grupo | Agrupación lógica de consultas (campo `GRUPO` en `V_TA_SCRIPT`), ej. `"10 - CONTROLES ALFA"` |
| ClosedXML | Librería .NET open-source para generar archivos `.xlsx` sin depender de Excel instalado |
| Etapa 1 | Visor y ejecutor de consultas guardadas — alcance inicial del módulo |
| Etapa 2 | Constructor visual de consultas nuevas — alcance futuro |

# AlfaCore.Mcp — consulta de la base en lenguaje natural (MCP)

Servidor MCP (ASP.NET Core, SDK oficial `ModelContextProtocol.AspNetCore`, Streamable HTTP, **stateless**) que deja a Claude /
ChatGPT hacer preguntas como "ventas del mes", "¿qué artículo me dejó más ganancia?" o "compará este mes con el anterior".
Solo lectura. Toda la lógica de negocio vive una vez en las vistas SQL `dir.vw_*`
([scripts](../../docs/base-datos/sql-referencia/dir-mcp/README.md)); acá solo hay herramientas parametrizadas.

> **Estado: etapa 3-4 (uso local).** Escucha solo en `127.0.0.1` y **no tiene autenticación**: no publicarlo ni abrir el puerto
> hasta hacer la etapa 5 (OAuth + HTTPS).

## 1. Requisitos en la base (una sola vez)
Correr en la base elegida, en orden, los scripts de `docs/base-datos/sql-referencia/dir-mcp/`: `001`, `002`, `003`, `004` y `005`.
El **005 crea el login de solo lectura** (`mcp_dir_ro`). En SSMS hay que activar **Consulta → Modo SQLCMD** y completar los `:setvar`;
si no, el script aborta con un mensaje (las variables `$(...)` no se reemplazan). Alternativa por consola:

```
sqlcmd -S "10.8.0.20\ALFANET" -f i:65001 -i 005_login_solo_lectura.sql -v MCP_DB="<base>" MCP_LOGIN="mcp_dir_ro" MCP_PASSWORD="<contraseña fuerte>"
```

## 2. Cadena de conexión (nunca en el repo)
Usar **el login `mcp_dir_ro`**, no uno con más permisos. Cualquiera de las dos formas:

```
# user-secrets (recomendado para desarrollo; se carga porque launchSettings usa el entorno Development)
dotnet user-secrets set "ConnectionStrings:AlfaMcp" "Server=10.8.0.20\ALFANET;Database=<base>;User Id=mcp_dir_ro;Password=<contraseña>;TrustServerCertificate=True;Encrypt=False" --project src/AlfaCore.Mcp

# o variable de entorno (la tiene prioridad sobre user-secrets)
set ALFACORE_MCP_CONNECTION=Server=...;Database=...;User Id=mcp_dir_ro;Password=...
```

Sin cadena, el servidor no arranca y lo dice. Los parámetros de negocio (`IA_MCP_*`) se leen de `TA_CONFIGURACION` y se
refrescan cada 30 s sin reiniciar.

## 3. Ejecutar
```
dotnet run --project src/AlfaCore.Mcp
```
Queda en `http://127.0.0.1:5290/mcp` (puerto en `appsettings.json`, clave `Urls`). `GET /health` responde `{"estado":"ok"}`.

## 4. Probar con MCP Inspector
```
npx @modelcontextprotocol/inspector
```
En la pantalla: **Transport Type = Streamable HTTP**, **URL = `http://127.0.0.1:5290/mcp`**, *Connect*, pestaña **Tools → List Tools**.
Probar por ejemplo `ventas_resumen` con `desde=2026-10-01`, `hasta=2026-10-31`, `agrupar_por=mes`, y `describir_esquema`.

## 5. Registrarlo en Claude Code
```
claude mcp add --transport http alfacore-dir http://127.0.0.1:5290/mcp
claude mcp list            # debe figurar alfacore-dir conectado
```
Después, dentro de Claude Code: *"¿cuánto vendí en octubre de 2026 y cuál fue el margen?"*, *"top 10 artículos por ganancia del
último trimestre"*, *"comparame octubre con septiembre"*, *"¿quién me debe más de 500.000?"*. Para quitarlo:
`claude mcp remove alfacore-dir`.

## Herramientas
| Herramienta | Para qué |
|---|---|
| `ventas_resumen(desde, hasta, agrupar_por: dia\|mes\|vendedor\|rubro\|cliente)` | ventas netas, costo, margen y comprobantes, con totales del período |
| `ranking_articulos(desde, hasta, criterio: ganancia\|cantidad\|facturacion, top)` | ranking de artículos |
| `comparar_periodos(desde_a, hasta_a, desde_b, hasta_b)` | A vs B: totales, variación y detalle por rubro |
| `clientes_top(desde, hasta, top)` | mejores clientes |
| `deuda_clientes(minimo, top)` | saldos deudores, vencido y vencido a más de 90 días |
| `stock_critico(top)` | artículos en o bajo su punto de pedido |
| `describir_esquema()` | vistas y columnas de `dir` (llamarla antes de `consultar_sql`) |
| `consultar_sql(sql, max_filas)` | SELECT libre, una sentencia, solo `dir.*`, con tope de filas |

Fechas siempre `AAAA-MM-DD`. Toda respuesta incluye `Criterio` (cómo se calcularon los importes), `Totales` del período completo y
`Advertencias` (recortes, topes, líneas sin costo).

## Parámetros en TA_CONFIGURACION (grupo `IA_MCP`)
`IA_MCP_HABILITADO`, `IA_MCP_MAX_FILAS`, `IA_MCP_TOP_MAXIMO`, `IA_MCP_TIMEOUT_SEG`, `IA_MCP_TC_PROFORMA`,
`IA_MCP_INCLUIR_PROFORMAS`, `IA_MCP_CONSULTAR_SQL_HABILITADO`, `IA_MCP_LOG_PARAMETROS`, `IA_MCP_ADVERTIR_SIN_COSTO_PCT`.
Si una clave falta, lo riesgoso queda apagado (`HABILITADO` y `CONSULTAR_SQL_HABILITADO` por defecto en NO).

## Seguridad (capas)
1. **Permisos SQL (la barrera real):** el login solo tiene `SELECT` sobre el esquema `dir` e `INSERT` sobre `IA_LOG_CONSULTAS`. Las vistas
   son de `dbo`, así que leen las tablas base por ownership chaining sin que el login tenga acceso directo.
2. **Validador de `consultar_sql`** (ScriptDom): una sola sentencia SELECT, solo objetos `dir.*`, sin variables, `SELECT INTO`,
   funciones de tabla, `OPENROWSET`/`OPENQUERY`, nombres de servidor/base ni funciones con esquema. Es secundario: no sustituye al punto 1.
3. **Tope de filas y timeout** (`IA_MCP_MAX_FILAS`, `IA_MCP_TIMEOUT_SEG`); herramientas con SQL parametrizado: lo único interpolado en el
   SQL sale de listas cerradas, nunca del texto del modelo.
4. **Auditoría** de cada llamada en `dbo.IA_LOG_CONSULTAS` (fecha, usuario, herramienta, parámetros, duración, filas, error). Si la
   auditoría falla, la consulta sigue y el error queda en el log de la aplicación (se priorizó disponibilidad).
5. Escucha solo en loopback y valida el header `Host` (`AllowedHosts`).

Los errores internos nunca devuelven detalles al modelo: se responde con un código de incidente y el detalle queda en `IA_LOG_CONSULTAS`.

## Preparado para la etapa 5 (OAuth + HTTPS + claude.ai / ChatGPT)
- `IConexionResolver` (hoy: una base fija) → resolver la base por tenant desde `ALFA_CENTRAL` según el token.
- `IMcpUserContext` (hoy: usuario fijo `mcp-local`) → usuario desde las claims del token; ya se audita en cada llamada.
- `Program.cs` tiene marcado dónde van `UseAuthentication/UseAuthorization` y `MapMcp(...).RequireAuthorization()`.
- Es **stateless**, listo para publicar detrás de un proxy/HTTPS y escalar sin sesiones pegajosas.

## Pruebas
```
dotnet test tests/AlfaCore.Mcp.Tests
```
Unitarias (validador SQL, parámetros, configuración) corren siempre. Las 9 de integración se saltean salvo que se defina
`ALFACORE_MCP_TEST_CONNECTION` apuntando a una base con `dir` instalado (no escriben nada: usan una auditoría en memoria).

## Trampas conocidas
- En estas bases el literal `'2026-10-01'` se lee año-día-mes; el MCP antepone `SET DATEFORMAT ymd` y usa parámetros tipados.
- Las bases a compatibilidad 80/100 no soportan `LAG/LEAD`, `IIF`, `CONCAT`, `FORMAT` ni `OFFSET/FETCH`; la descripción de `consultar_sql` se lo advierte al modelo.
- Cuenta corriente y stock tardan 10-15 s en bases grandes (por eso el timeout por defecto es 60 s).

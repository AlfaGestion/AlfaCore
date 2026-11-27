# AlfaCore.Mcp — consulta de la base en lenguaje natural (MCP)

Servidor MCP (ASP.NET Core, SDK oficial `ModelContextProtocol.AspNetCore`, Streamable HTTP, **stateless**) que deja a Claude /
ChatGPT hacer preguntas como "ventas del mes", "¿qué artículo me dejó más ganancia?" o "compará este mes con el anterior".
Solo lectura. Toda la lógica de negocio vive una vez en las vistas SQL `dir.vw_*`
([scripts](../../docs/base-datos/sql-referencia/dir-mcp/README.md)); acá solo hay herramientas parametrizadas.

Dos modos de uso:

| Modo | Autenticación | Para qué | Quién |
|---|---|---|---|
| **Local simple** (`Development`, sin configurar `Mcp:Auth:Enabled`) | ninguna, solo `127.0.0.1` | probar con Claude Code en tu PC | por defecto con `dotnet run` |
| **OAuth** (etapa 5) | OAuth 2.1 propio (usuario + clave, PKCE, tokens de vida corta) | claude.ai, ChatGPT y Claude Code por HTTPS | producción — ver [guía de IIS](../../docs/modulos/mcp-publicacion-iis.md) |

> **Nunca** expongas el modo local a internet. En cualquier entorno que no sea `Development`, el servidor **exige** OAuth y se
> niega a arrancar si `Mcp:Auth:Enabled=false`.

## 1. Requisitos en la base de datos (una sola vez)
Scripts de `docs/base-datos/sql-referencia/dir-mcp/`, en orden: `001`–`004` (esquema `dir`, vistas, auditoría, claves) y `005` (login
de datos `mcp_dir_ro`, solo lectura). Para OAuth además: `010` (tablas `MCP_*`), `011` (login del almacén OAuth) y `012` (claves
`IA_MCP_OAUTH_*`). En SSMS activar **Consulta → Modo SQLCMD** para los que llevan variables (005 y 011).

## 2. Modo local simple (el que ya venís usando)
```
dotnet user-secrets set "ConnectionStrings:AlfaMcp" "Server=<servidor>;Database=<base>;User Id=mcp_dir_ro;Password=<contraseña>;TrustServerCertificate=True;Encrypt=False" --project src/AlfaCore.Mcp
dotnet run --project src/AlfaCore.Mcp
claude.cmd mcp add --scope user --transport http alfacore-dir http://127.0.0.1:5290/mcp
```
Queda en `http://127.0.0.1:5290/mcp` (`GET /health` → `{"estado":"ok"}`). Probar con `npx @modelcontextprotocol/inspector`
(*Streamable HTTP*, esa URL) o directamente preguntando en `claude`.

## 3. Probar el flujo OAuth en tu PC (antes de publicar)
Usa un almacén **en memoria** con un usuario de prueba, así no hace falta crear tablas. Requiere haber corrido el script **012** (la lista
blanca de redirects vive en `TA_CONFIGURACION`: sin ella el registro se rechaza a propósito). En PowerShell:

```powershell
$env:Mcp__Auth__Enabled = "true"
$env:Mcp__Auth__Store = "Memory"                       # solo permitido en Development
$env:Mcp__Auth__Issuer = "http://127.0.0.1:5290"
$env:Mcp__Auth__DevUser = "alberto"
$env:Mcp__Auth__DevPassword = "una-clave-larga-123"    # cualquiera de 12+ caracteres; no se guarda en ningún archivo
$env:Mcp__Auth__DevTotp = "true"                       # segundo factor de prueba (ver abajo)
dotnet run --project src/AlfaCore.Mcp
```
Con `DevTotp` el servidor imprime al arrancar una línea `TOTP de PRUEBA ... Clave para la app autenticadora: XXXX...`. En el celular, en
Google/Microsoft Authenticator: *Agregar cuenta → Ingresar clave de configuración* (tipo **basado en tiempo**), pegá esa clave. El login pedirá usuario, clave
y el código de 6 dígitos de la app. (Sin `DevTotp`, y con `IA_MCP_OAUTH_REQUIERE_TOTP=SI` — lo que deja el script 012 — el usuario de prueba **no podría entrar**;
para probar sin segundo factor poné esa clave en `NO` en `TA_CONFIGURACION`.)
En otra ventana: `claude.cmd mcp remove alfacore-dir` y `claude.cmd mcp add --scope user --transport http alfacore-dir http://127.0.0.1:5290/mcp`.
En `claude`: `/mcp` → `alfacore-dir` → **Authenticate**. Se abre el navegador con la pantalla de login; entrá con `alberto` y la clave de arriba.
Al volver, debe decir *connected*. Para volver al modo simple, cerrá la ventana (las variables `$env:` mueren con ella).

## 4. Qué hace el servidor OAuth
| Endpoint | Función |
|---|---|
| `/.well-known/oauth-protected-resource/mcp` | metadatos del recurso (RFC 9728); el `401` apunta acá con `WWW-Authenticate: Bearer resource_metadata=...` |
| `/.well-known/oauth-authorization-server`, `/.well-known/openid-configuration` (+ variantes con ruta) | metadatos del servidor de autorización (RFC 8414) |
| `POST /oauth/register` | registro dinámico de clientes públicos (RFC 7591), solo con redirect URIs de la lista blanca |
| `GET/POST /oauth/authorize` | pantalla de login y consentimiento; `authorization code` + **PKCE S256** obligatorio |
| `POST /oauth/token` | canje del código y renovación; *refresh tokens* con rotación y detección de reuso |
| `GET /oauth/jwks` | clave pública de firma (RS256) |

Login con usuario + clave + código TOTP. Un solo permiso: `mcp:read`. Los *access tokens* son JWT RS256 con `audience` = URL del recurso, vida por defecto 60 min.

### Administración de usuarios y segundo factor (por consola, nunca por HTTP)
```
dotnet AlfaCore.Mcp.dll crear-usuario <usuario> "Nombre a mostrar" [base] [--qr archivo.png]    # pide la clave 2 veces (mínimo 12 caracteres)
dotnet AlfaCore.Mcp.dll configurar-totp <usuario> [--qr archivo.png]                             # teléfono nuevo o perdido; corta sus sesiones
```
(en desarrollo: `dotnet run --project src/AlfaCore.Mcp -- crear-usuario ...`). La clave se guarda como hash PBKDF2-SHA256 (600.000 iteraciones).

**Segundo factor (TOTP, RFC 6238):** `crear-usuario` genera el secreto y lo muestra **una sola vez** (clave para tipear + URI `otpauth`; con `--qr` guarda un PNG para escanear
con la app: borrarlo después). La inscripción la hace el administrador a propósito: si fuera por web, quien robe una clave podría inscribir su propio teléfono.
El secreto se guarda **cifrado** (AES-256-GCM) con una clave que vive junto a la clave de firma (archivo `*.totp-key`, fuera de la base y del repo: **incluirla en el respaldo**,
porque sin ella los secretos guardados no se pueden leer y hay que reinscribir a todos). Cada código sirve **una sola vez** (anti-replay) y los códigos erróneos cuentan para el bloqueo.
Con `IA_MCP_OAUTH_REQUIERE_TOTP = SI` (por defecto) un usuario sin segundo factor no puede ingresar.

Para desactivar o desbloquear a alguien, en el almacén OAuth:
```sql
UPDATE dbo.MCP_USUARIO SET Activo = 0 WHERE Usuario = 'fulano';                 -- deja de poder renovar al vencer su token
UPDATE dbo.MCP_USUARIO SET Intentos = 0, BloqueadoHasta = NULL WHERE Usuario = 'fulano';   -- desbloquear
UPDATE dbo.MCP_REFRESH_TOKEN SET Revocado = 1 WHERE UsuarioId = (SELECT Id FROM dbo.MCP_USUARIO WHERE Usuario = 'fulano');  -- cortar sesiones ya
```

### Parámetros (TA_CONFIGURACION de la base de datos, grupo `IA_MCP`; se refrescan cada 30 s)
Negocio: `IA_MCP_HABILITADO` (interruptor general, también corta el login), `IA_MCP_MAX_FILAS`, `IA_MCP_TOP_MAXIMO`, `IA_MCP_TIMEOUT_SEG`,
`IA_MCP_TC_PROFORMA`, `IA_MCP_INCLUIR_PROFORMAS`, `IA_MCP_CONSULTAR_SQL_HABILITADO`, `IA_MCP_LOG_PARAMETROS`, `IA_MCP_ADVERTIR_SIN_COSTO_PCT`, `IA_MCP_LECTURA_SIN_BLOQUEOS`.
OAuth: `IA_MCP_OAUTH_ACCESS_TOKEN_MIN`, `_REFRESH_DIAS`, `_CODIGO_SEG`, `_MAX_INTENTOS`, `_BLOQUEO_MIN`, `_MAX_CLIENTES`, `_REQUIERE_TOTP` y
**`IA_MCP_OAUTH_REDIRECTS`** (lista blanca de aplicaciones que pueden conectarse). Si una clave falta, lo riesgoso queda apagado.

### Configuración de infraestructura (appsettings / variables de entorno; nunca en el repo)
| Variable | Qué es |
|---|---|
| `ALFACORE_MCP_CONNECTION` o secret `ConnectionStrings:AlfaMcp` | login de **datos** (`mcp_dir_ro`) |
| `ALFACORE_MCP_AUTH_CONNECTION` o secret `ConnectionStrings:AlfaMcpAuth` | login del **almacén OAuth** (solo tablas `MCP_*`) |
| `Mcp__Auth__Issuer` | URL pública https de la aplicación (ej. `https://alfacore-mcp.ddns.net`) |
| `Mcp__Auth__EndpointPath` | ruta del recurso MCP (por defecto `/mcp`) |
| `Mcp__Auth__SigningKeyPath` | PEM de la clave privada de firma, **fuera** de la carpeta publicada (se crea sola si no existe) |
| `Mcp__Auth__LimiteLoginPorMin` / `LimiteTokenPorMin` / `LimiteRegistroPorMin` | límites por IP (10 / 60 / 10) |

## Herramientas
| Herramienta | Para qué |
|---|---|
| `buscar_clientes(texto, top)` / `buscar_articulos(texto, top)` | código exacto a partir de parte del nombre, código o CUIT (todas las palabras, sin tildes) |
| `ficha_cliente(cliente)` | datos, deuda de hoy (vencida, +90, límite y disponible), compras 12 meses y comprobantes abiertos (~12 s: la vista de saldos calcula todo) |
| `ficha_articulo(articulo)` | costo y precios vigentes, punto de pedido, stock por depósito y ventas 12 meses |
| `ventas_resumen(desde, hasta, agrupar_por: dia\|mes\|vendedor\|rubro\|cliente\|articulo\|comprobante, cliente, articulo)` | ventas netas, costo, margen y comprobantes, con totales del período; opcionalmente de un cliente o artículo |
| `ranking_articulos(desde, hasta, criterio: ganancia\|cantidad\|facturacion, top, cliente)` | ranking de artículos (opcional: lo que compra un cliente) |
| `comparar_periodos(desde_a, hasta_a, desde_b, hasta_b)` | A vs B: totales, variación y detalle por rubro |
| `clientes_top(desde, hasta, top, articulo)` | mejores clientes (opcional: de un artículo); nombre del maestro y aviso si el código junta varios compradores (Consumidor Final) |
| `deuda_clientes(minimo, top)` | saldos deudores, vencido y vencido a más de 90 días |
| `stock_critico(top)` | artículos en o bajo su punto de pedido |
| `describir_esquema()` | vistas y columnas de `dir` (llamarla antes de `consultar_sql`) |
| `consultar_sql(sql, max_filas)` | SELECT libre, una sentencia, solo `dir.*`, con tope de filas |

Fechas siempre `AAAA-MM-DD`. Toda respuesta incluye `Criterio`, `Totales` del período completo y `Advertencias`.

## Seguridad (capas)
1. **Permisos SQL (la barrera real):** el login de datos solo tiene `SELECT` sobre `dir` e `INSERT` sobre `IA_LOG_CONSULTAS`; el del almacén OAuth
   solo toca tablas `MCP_*`. Son logins **distintos**: comprometer uno no da acceso a lo del otro.
2. **Autenticación OAuth 2.1:** PKCE S256 obligatorio; redirect URIs en lista blanca y comparación exacta; clientes solo públicos; códigos de un solo uso
   (60 s); *refresh tokens* rotativos con revocación de toda la cadena si se reusa uno viejo; códigos y refresh tokens **solo se guardan como hash**;
   tokens con `issuer`/`audience`/firma/vencimiento/`scope` validados (se rechaza `alg=none` y firmas ajenas).
3. **Login:** claves PBKDF2 + segundo factor TOTP de un solo uso, mismo mensaje y tiempo para usuario inexistente o clave mala, bloqueo tras N intentos, protección CSRF (antiforgery),
   límite de pedidos por IP, pantalla con `X-Frame-Options: DENY`, CSP y nombre de cliente escapado.
4. **Validador de `consultar_sql`** (ScriptDom): una sola sentencia SELECT, solo objetos `dir.*`, sin variables, `SELECT INTO`, funciones de tabla, `OPENROWSET`,
   servidores vinculados ni funciones con esquema. Secundario respecto del punto 1.
5. **Protección de la base de producción:** cada consulta corre con `DEADLOCK_PRIORITY LOW` (en un bloqueo mutuo siempre pierde el MCP) y, por defecto, `READ UNCOMMITTED` (`IA_MCP_LECTURA_SIN_BLOQUEOS`): no toma bloqueos compartidos, así no frena a quien graba una venta o un cobro. Costo: en un instante muy raro puede verse un comprobante a medio grabar.
6. **Tope de filas y timeout**; SQL parametrizado (lo interpolado sale de listas cerradas).
7. **Auditoría:** cada consulta en `dbo.IA_LOG_CONSULTAS` (con el usuario del token) y cada evento de login/token en `dbo.MCP_EVENTO`
   (`login_ok`, `login_fallido`, `usuario_bloqueado`, `codigo_reutilizado`, `refresh_reutilizado`...).
8. Errores internos: al usuario solo un código de incidente; el detalle queda en el log y en `IA_LOG_CONSULTAS`.

**Límites conocidos:** el acceso de un usuario desactivado cae al vencer su *access token* (60 min por defecto), salvo que revoques sus *refresh tokens* (SQL de arriba);
no hay códigos de recuperación del segundo factor (si alguien pierde el teléfono: `configurar-totp`); una sola clave de firma (para rotarla: cambiar el PEM; las sesiones se renuevan al volver a iniciar sesión).

## Pruebas
```
dotnet test tests/AlfaCore.Mcp.Tests
```
Corren siempre: validador SQL, parámetros, configuración y el **flujo OAuth completo** contra el servidor real en memoria (registro, login, PKCE, rotación, reuso,
bloqueo, tokens falsificados/vencidos, interruptor, base caída). Opcionales (se saltean sin su variable de entorno):
`ALFACORE_MCP_TEST_CONNECTION` (herramientas contra una base con `dir`) y `ALFACORE_MCP_TEST_AUTH_CONNECTION` (almacén SQL contra tablas `MCP_*`; limpia lo que crea).

## Trampas conocidas
- En estas bases el literal `'2026-10-01'` se lee año-día-mes; el MCP antepone `SET DATEFORMAT ymd` y usa parámetros tipados.
- Compatibilidad 80/100: sin `LAG/LEAD`, `IIF`, `CONCAT`, `FORMAT` ni `OFFSET/FETCH` (la descripción de `consultar_sql` se lo advierte al modelo).
- Cuenta corriente y stock tardan 10-15 s en bases grandes (timeout por defecto 60 s).
- Si ya tenés `dotnet run` abierto, `dotnet build/test` no puede reemplazar los archivos: cerralo antes (Ctrl+C).

# Publicación del servidor MCP en el IIS de ALFACENTRAL (etapa 5b)

> **Estado (27/11/2026): publicado y funcionando.** claude.ai y Claude Code se conectan por `https://alfacore-mcp.ddns.net/mcp` con login OAuth + segundo factor (TOTP).
> Por ahora apunta a la base de **pruebas**. ChatGPT: pendiente de probar (ver paso 6).

## Decisión de diseño: sitio IIS propio, con nombre propio, en 443
| Dato | Valor |
|---|---|
| Sitio / *app pool* | `AlfaCoreMcp` (pool propio, sin código administrado, `ApplicationPoolIdentity`) |
| Carpeta publicada | `C:\inetpub\AlfaCoreMcp` |
| Carpeta de secretos y claves | `C:\ProgramData\AlfaCoreMcp` (solo administradores, SYSTEM y la identidad del pool) |
| Nombre público | `alfacore-mcp.ddns.net` (No-IP, misma IP pública que `alfacentral.ddns.net`) |
| URL del MCP | `https://alfacore-mcp.ddns.net/mcp` |
| `Mcp__Auth__Issuer` | `https://alfacore-mcp.ddns.net` (sin barra final, sin puerto) |
| Binding | `https` puerto **443**, host `alfacore-mcp.ddns.net`, **SNI** (`sslFlags=1`) |
| Certificado | Let's Encrypt propio del nombre, pedido y renovado por **Certify The Web** |

Por qué así:
- **Sitio propio, no sub-aplicación:** no se toca nada del sitio de AlfaCore (ni su `web.config` ni su *app pool*; ya hubo un incidente de producción con ese archivo).
  Los metadatos OAuth (`/.well-known/...`) quedan en la raíz del sitio, sin reglas de reescritura. Si algo falla, se detiene este sitio y producción sigue igual.
- **443 con nombre propio, no un puerto aparte:** primero se publicó en `alfacentral.ddns.net:8443`. Desde afuera respondía, pero **el backend de claude.ai nunca llegó al 8443**
  (el log de IIS no mostraba ningún pedido suyo). Con SNI, el 443 se comparte con AlfaCore sin tocarlo: IIS elige el sitio por el nombre.

## 0. Antes de empezar
1. **SQL** (scripts de `docs/base-datos/sql-referencia/dir-mcp/`): `001`–`005` y `012` en la base de gestión; `010` y `011` en la base del almacén OAuth (`ALFA_CENTRAL`, 10.8.0.31).
   Las contraseñas de `mcp_dir_ro` y `mcp_auth_rw` no van en ningún archivo del repo.
2. **DNS:** crear el nombre en No-IP (registro A con la IP pública) y mantenerlo actualizado igual que `alfacentral.ddns.net`.
3. **Red:** el 443 ya está abierto hacia `10.8.0.53` por AlfaCore. No hace falta abrir nada más.
4. **Hosting Bundle:** AlfaCore ya corre sobre .NET 8 ahí. Verificar con `dotnet --list-runtimes` (debe aparecer `Microsoft.AspNetCore.App 8.x`). **No reiniciar IIS.**
5. **Carga sobre SQL:** el MCP lee con `READ UNCOMMITTED` y baja prioridad ante deadlocks (`IA_MCP_LECTURA_SIN_BLOQUEOS`, por defecto `SI`). La primera semana, mirar `DuracionMs` en `IA_LOG_CONSULTAS`.

## 1. Publicar
En la PC de desarrollo:
```
dotnet publish src/AlfaCore.Mcp -c Release -o C:\temp\AlfaCoreMcp
copy tools\mcp\instalar_mcp_iis.ps1 C:\temp\AlfaCoreMcp\
copy tools\mcp\usuarios_mcp.ps1 C:\temp\AlfaCoreMcp\
```
Copiar el contenido de `C:\temp\AlfaCoreMcp` a `C:\inetpub\AlfaCoreMcp` en el servidor. Verificar que estén `AlfaCore.Mcp.dll`, `web.config` y los dos `.ps1`.

## 2. Certificado (Certify The Web) — ANTES del instalador
El instalador toma el certificado del binding `443 + alfacore-mcp.ddns.net`, así que ese binding tiene que existir con su certificado.

1. Si el sitio `AlfaCoreMcp` todavía no existe, crearlo vacío para que Certify pueda validar (http-01 usa el puerto 80):
   `New-Website -Name AlfaCoreMcp -PhysicalPath C:\inetpub\AlfaCoreMcp -Port 80 -HostHeader alfacore-mcp.ddns.net`
2. En Certify: *New Certificate* → sitio `AlfaCoreMcp` → dominio `alfacore-mcp.ddns.net` → validación **http-01** → *Test* → *Request certificate*.
3. **Trampa:** con *Deployment = Auto*, Certify solo **actualiza** bindings https que ya existen; **no los crea**. Si no había binding 443, el certificado queda instalado
   pero sin asociar (y el navegador da error de TLS / conexión reiniciada). Crearlo a mano una vez:
   ```powershell
   Import-Module WebAdministration
   $c = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=alfacore-mcp.ddns.net" } | Sort-Object NotAfter -Descending | Select-Object -First 1
   New-WebBinding -Name AlfaCoreMcp -Protocol https -Port 443 -HostHeader alfacore-mcp.ddns.net -SslFlags 1
   New-Item "IIS:\SslBindings\!443!alfacore-mcp.ddns.net" -Thumbprint $c.Thumbprint -SSLFlags 1
   ```
   Desde ahí, las renovaciones de Certify actualizan ese binding solas.
4. **Verificar:** `Get-ChildItem IIS:\SslBindings | Where-Object Host -eq alfacore-mcp.ddns.net` muestra la huella. El binding 443 de AlfaCore sigue igual.

## 3. Instalador (pool, permisos, variables y sitio)
En el servidor, PowerShell **como administrador**, todo en la **misma** consola:
```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
C:\inetpub\AlfaCoreMcp\instalar_mcp_iis.ps1
```
`tools/mcp/instalar_mcp_iis.ps1` hace, en este orden, y no reinicia IIS ni toca otros sitios:
1. Pide servidor y base de datos (obligatorios), la clave de `mcp_dir_ro`, servidor y base del almacén (Enter = `10.8.0.31` / `ALFA_CENTRAL`) y la clave de `mcp_auth_rw`.
   Las claves se escriben ocultas.
2. **Prueba las dos conexiones** con los logins reales. Si alguna falla, se detiene sin cambiar nada y dice qué servidor, base y login falló.
3. Toma el certificado del binding del paso 2 (si no existe, se detiene y lo indica).
4. Crea el pool si falta, la carpeta de secretos con permisos por SID (el Windows está en castellano) y las **variables de entorno del pool**
   (`ASPNETCORE_ENVIRONMENT`, `ALFACORE_MCP_CONNECTION`, `ALFACORE_MCP_AUTH_CONNECTION`, `Mcp__Auth__Issuer`, `Mcp__Auth__SigningKeyPath`).
   Quedan en `applicationHost.config`, no en la carpeta publicada: **republicar no las pisa**.
5. Crea el sitio si falta, asocia el certificado al binding SNI y recicla el pool.
6. Verifica en local `/health` (200) y `/mcp` sin login (401).

Para cambiar una clave SQL o la base, volver a correr el instalador: reemplaza las variables.

## 4. Crear los usuarios (hasta 10)
PowerShell como administrador en el servidor:
```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
C:\inetpub\AlfaCoreMcp\usuarios_mcp.ps1 crear-usuario fulano "Nombre Apellido" --qr C:\ProgramData\AlfaCoreMcp\fulano.png
```
`usuarios_mcp.ps1` lee las variables del pool, así el comando usa exactamente la misma base, almacén e `Issuer` que el sitio, y **la misma `SigningKeyPath`**.
Al lado de esa ruta queda `*.totp-key`, la clave que cifra los segundos factores: con otra ruta, el sitio no los puede leer y nadie entra.
En producción el programa se niega a arrancar si `SigningKeyPath` no es una ruta absoluta.

- Pide la clave dos veces (mínimo 12 caracteres, una distinta por persona). **Anotarla en un gestor de contraseñas antes de escribirla.**
- Muestra **una sola vez** la clave del segundo factor y guarda el QR. Escanearlo con Google/Microsoft Authenticator con la persona delante y **borrar el PNG**.
- Si el usuario ya existe, avisa y no pide nada. El usuario es sensible a mayúsculas como se escribió.
- Teléfono nuevo o perdido: `usuarios_mcp.ps1 configurar-totp fulano --qr ...` (corta sus sesiones activas).
- Olvidó la clave: borrar y recrear el usuario (ver Operación).

**Verificar:** `SELECT Usuario, Activo FROM dbo.MCP_USUARIO` en `ALFA_CENTRAL`.

## 5. Verificar antes de conectar nada
```powershell
curl.exe -i https://alfacore-mcp.ddns.net/health                                   # 200 {"estado":"ok"}
curl.exe -i -X POST https://alfacore-mcp.ddns.net/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d "{}"
#   -> 401 con:  WWW-Authenticate: Bearer resource_metadata="https://alfacore-mcp.ddns.net/.well-known/oauth-protected-resource/mcp"
curl.exe https://alfacore-mcp.ddns.net/.well-known/oauth-protected-resource/mcp   # "resource" y "authorization_servers" con https://alfacore-mcp.ddns.net
curl.exe https://alfacore-mcp.ddns.net/.well-known/oauth-authorization-server      # authorization_endpoint, token_endpoint, registration_endpoint
```
Probar también **desde afuera** (celular con datos móviles): `https://alfacore-mcp.ddns.net/health`. Y confirmar que AlfaCore sigue respondiendo igual.
Si algo falla: log de IIS (`C:\inetpub\logs\LogFiles\W3SVC<id del sitio>\`) para ver si el pedido llega; `C:\inetpub\AlfaCoreMcp\logs\`
(activar `stdoutLogEnabled="true"` en el `web.config` solo mientras se diagnostica) y el Visor de eventos.

## 6. Conectar los clientes
**claude.ai** (probado): *Settings → Connectors → Add custom connector* → URL `https://alfacore-mcp.ddns.net/mcp`, registro de cliente automático (DCR).
Se abre el login de AlfaCore: usuario, clave y código de 6 dígitos. La *redirect URI* de claude.ai (`https://claude.ai/api/mcp/auth_callback`) ya está en la lista blanca.
En cada chat, verificar que el conector esté **activado** (menú de herramientas); si no, Claude responde de memoria. Para estar seguro, pedir
*"Usando el conector AlfaCore: ..."* y comprobar que aparece la llamada a la herramienta.

**Claude Code** (probado): `claude.cmd mcp add --scope user --transport http alfacore-dir-prod https://alfacore-mcp.ddns.net/mcp` → `/mcp` → *Authenticate*.

**ChatGPT** (pendiente): *Settings → Apps & Connectors* → modo desarrollador → crear conector con la URL y autenticación OAuth. **Ver la redirect URI que muestra ChatGPT**
y, si no está en `IA_MCP_OAUTH_REDIRECTS`, agregarla (se aplica en ≤30 s, sin reiniciar):
```sql
SELECT CAST(ValorAux AS nvarchar(max)) FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'IA_MCP_OAUTH_REDIRECTS';
UPDATE dbo.TA_CONFIGURACION SET ValorAux = CAST(ValorAux AS nvarchar(max)) + CHAR(10) + 'https://chatgpt.com/<ruta-que-muestre>' WHERE CLAVE = 'IA_MCP_OAUTH_REDIRECTS';
```

**Después de conectar:** `SELECT TOP 20 * FROM dbo.MCP_EVENTO ORDER BY Id DESC` (logins y tokens, en `ALFA_CENTRAL`) y
`SELECT TOP 20 * FROM dbo.IA_LOG_CONSULTAS ORDER BY Id DESC` (consultas con el usuario real, en la base de datos).

## Diagnóstico del login
`SELECT TOP 10 DATEADD(HOUR,-3,Fecha) Hora, Evento, Usuario, Detalle FROM dbo.MCP_EVENTO ORDER BY Id DESC` en `ALFA_CENTRAL`:
| Evento | Significa |
|---|---|
| `login_usuario_inexistente` | el usuario no existe (revisar mayúsculas) |
| `login_fallido` | clave incorrecta |
| `totp_fallido` | código de 6 dígitos incorrecto (hora del celular, o QR viejo en Authenticator) |
| `totp_ilegible` | el sitio no puede descifrar el segundo factor: el usuario se creó con otra `SigningKeyPath` |
| `login_rechazado` | usuario inactivo o bloqueado |
| `login_sin_totp` | clave correcta pero el usuario no tiene segundo factor: `configurar-totp` |
| `usuario_bloqueado` | 5 intentos fallidos: esperar 15 minutos (`IA_MCP_OAUTH_MAX_INTENTOS` / `_BLOQUEO_MIN`) |

## Operación
| Necesidad | Cómo |
|---|---|
| **Cortar todo ya** | `UPDATE dbo.TA_CONFIGURACION SET VALOR = 'NO' WHERE CLAVE = 'IA_MCP_HABILITADO'` (≤30 s; corta consultas, login, registro y tokens) o `Stop-Website AlfaCoreMcp` |
| Dar de baja a alguien | SQL del README del proyecto (`Activo = 0` + revocar sus refresh tokens) |
| Olvidó la clave | `DELETE dbo.MCP_REFRESH_TOKEN WHERE UsuarioId IN (SELECT Id FROM dbo.MCP_USUARIO WHERE Usuario='x'); DELETE dbo.MCP_USUARIO WHERE Usuario='x'` y volver a crearlo (paso 4) |
| Cambiar de base (pruebas → real) | Correr los scripts SQL en la base real y volver a correr `instalar_mcp_iis.ps1` con la nueva base |
| Renovar el certificado | Automático (Certify, binding del paso 2). Revisar en Certify que la renovación no falle |
| Actualizar la aplicación | `Stop-Website AlfaCoreMcp` → copiar la publicación (sin tocar `C:\ProgramData\AlfaCoreMcp`) → `Start-Website AlfaCoreMcp` |
| **Respaldo** | Incluir `C:\ProgramData\AlfaCoreMcp\` (clave de firma y `*.totp-key`). Sin la clave de cifrado hay que reinscribir el segundo factor de todos |
| Rotar la clave de firma | Detener el sitio, borrar `signing-key.pem`, iniciar (se genera una nueva). Todos vuelven a iniciar sesión |
| Marcha atrás completa | `Stop-Website AlfaCoreMcp`; opcional `Remove-Website` / `Remove-WebAppPool` y baja del nombre en No-IP. AlfaCore no se ve afectado |

## Antes de abrirlo al equipo
- El segundo factor (TOTP) ya viene exigido (`IA_MCP_OAUTH_REQUIERE_TOTP = SI`): nadie entra sin su app autenticadora.
- Claves únicas y largas por persona; no reutilizar las de otros sistemas.
- Revisar `MCP_EVENTO` la primera semana (intentos fallidos, bloqueos).
- Dar de baja a quien deje el equipo el mismo día.
- Pasar de la base de pruebas a la real cuando se haya validado con datos conocidos.

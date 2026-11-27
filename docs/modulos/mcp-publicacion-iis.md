# Publicación del servidor MCP en el IIS de ALFACENTRAL (etapa 5b)

> **Estado:** guía escrita y revisada contra el código; **no se ejecutó todavía en el servidor**. Cada paso lleva su verificación. Hacerlo en una
> ventana de mantenimiento corta y sin tocar el sitio de AlfaCore existente.

Objetivo: que `claude.ai`, ChatGPT y Claude Code se conecten por HTTPS a `https://alfacentral.ddns.net:8443/mcp` con login OAuth.

## Decisión de diseño: sitio IIS propio, no sub-aplicación
Se publica como un **sitio nuevo en un puerto propio (8443)**, con el certificado que ya tiene `alfacentral.ddns.net`. Motivos:
- **No se toca nada del sitio de AlfaCore** (ni su `web.config`, ni su *app pool*). Una sub-aplicación hereda la configuración del padre y obliga a editar su `web.config`; ya hubo un incidente de producción con ese archivo.
- Los metadatos OAuth (`/.well-known/...`) quedan en la **raíz del sitio**, donde los clientes los buscan, sin reglas de reescritura.
- Si algo falla, se detiene el sitio nuevo y producción sigue igual.

| Dato | Valor |
|---|---|
| Sitio / *app pool* | `AlfaCoreMcp` (pool propio, sin código administrado, identidad propia) |
| Carpeta publicada | `C:\inetpub\AlfaCoreMcp` |
| Carpeta de secretos y claves | `C:\ProgramData\AlfaCoreMcp` (solo lectura/escritura para la identidad del pool) |
| URL pública | `https://alfacentral.ddns.net:8443` · MCP en `/mcp` |
| `Mcp__Auth__Issuer` | `https://alfacentral.ddns.net:8443` (sin barra final) |

## 0. Antes de empezar
1. **SQL** (en las bases, con los scripts de `docs/base-datos/sql-referencia/dir-mcp/`): `001`–`005` en la base de datos de gestión; `010` y `011` en la base del almacén OAuth
   (recomendado `ALFA_CENTRAL`); `012` en la base de gestión. Anotar las contraseñas de `mcp_dir_ro` y `mcp_auth_rw` (no van en ningún archivo del repo).
2. **Red:** abrir el puerto **8443/TCP** hacia `10.8.0.53` en el router/firewall (reenvío de puertos). Es la única apertura nueva.
3. **Certificado:** verificar que el certificado de `alfacentral.ddns.net` está en el almacén *Local Computer → Personal* y **no vence pronto**.
4. **Hosting Bundle:** AlfaCore ya corre sobre .NET 8 en ese servidor, asi que el .NET 8 Hosting Bundle casi seguro **ya esta instalado**: verificarlo (`dotnet --list-runtimes` debe mostrar `Microsoft.AspNetCore.App 8.x`) y **no reiniciar IIS**. Solo si faltara, instalarlo **fuera de horario**: requiere `iisreset`, que tira todos los sitios (AlfaCore incluido) unos segundos. Crear el sitio y el pool nuevos NO requiere reiniciar nada.
5. **Carga sobre SQL Server:** el MCP lee con `READ UNCOMMITTED` y prioridad baja ante deadlocks (clave `IA_MCP_LECTURA_SIN_BLOQUEOS`, por defecto `SI`), asi que no frena las escrituras de Alfa Gestion/AlfaCore. Aun asi, la primera semana conviene mirar la duracion de las consultas en `IA_LOG_CONSULTAS` (`DuracionMs`).

## 1. Publicar
En tu PC:
```
dotnet publish src/AlfaCore.Mcp -c Release -o C:\temp\AlfaCoreMcp
```
Copiar el contenido de `C:\temp\AlfaCoreMcp` a `C:\inetpub\AlfaCoreMcp` en el servidor. **Verificar** que existe `AlfaCore.Mcp.dll` y `web.config`.

## 2. Pool, sitio y permisos (PowerShell como administrador, en el servidor)
```powershell
Import-Module WebAdministration
New-Item C:\ProgramData\AlfaCoreMcp -ItemType Directory -Force | Out-Null
New-WebAppPool -Name AlfaCoreMcp
Set-ItemProperty IIS:\AppPools\AlfaCoreMcp -Name managedRuntimeVersion -Value ""          # sin código administrado
Set-ItemProperty IIS:\AppPools\AlfaCoreMcp -Name processModel.identityType -Value ApplicationPoolIdentity

New-Website -Name AlfaCoreMcp -PhysicalPath C:\inetpub\AlfaCoreMcp -ApplicationPool AlfaCoreMcp -Port 8443 -Ssl -HostHeader alfacentral.ddns.net
# Asociar el certificado existente (reemplazar <HUELLA> por la huella del certificado de alfacentral.ddns.net):
$cert = Get-Item Cert:\LocalMachine\My\<HUELLA>
New-Item IIS:\SslBindings\!8443!alfacentral.ddns.net -Value $cert -SSLFlags 1 -ErrorAction SilentlyContinue   # 1 = SNI

# Permisos: el pool lee la app y escribe SOLO en la carpeta de secretos
icacls C:\inetpub\AlfaCoreMcp /grant "IIS AppPool\AlfaCoreMcp:(OI)(CI)RX"
icacls C:\ProgramData\AlfaCoreMcp /inheritance:r /grant "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "IIS AppPool\AlfaCoreMcp:(OI)(CI)M"
```
(Si el sitio ya usa SNI con otros certificados, ajustar el `-SSLFlags` a lo que use el resto de los bindings del servidor.)

## 3. Variables de entorno **del pool** (no van en `web.config`)
Se guardan en la configuración de IIS, no en la carpeta publicada: **republicar no las pisa** y nadie con acceso a la carpeta las lee.
```powershell
$a = "$env:windir\system32\inetsrv\appcmd.exe"
function Var($n, $v) { & $a set config -section:system.applicationHost/applicationPools "/+[name='AlfaCoreMcp'].environmentVariables.[name='$n',value='$v']" /commit:apphost }
Var ASPNETCORE_ENVIRONMENT Production
Var ALFACORE_MCP_CONNECTION       "Server=<sql>;Database=<base>;User Id=mcp_dir_ro;Password=<clave>;TrustServerCertificate=True;Encrypt=False"
Var ALFACORE_MCP_AUTH_CONNECTION  "Server=<sql>;Database=<base_almacen>;User Id=mcp_auth_rw;Password=<clave>;TrustServerCertificate=True;Encrypt=False"
Var Mcp__Auth__Issuer             "https://alfacentral.ddns.net:8443"
Var Mcp__Auth__SigningKeyPath     "C:\ProgramData\AlfaCoreMcp\signing-key.pem"
```
Reemplazar `<...>` por los valores reales **a mano en la consola**; no guardar este bloque completado en ningún archivo ni chat.

## 4. Crear los usuarios (hasta 10)
Con las mismas variables definidas en la sesión de PowerShell (`$env:ALFACORE_MCP_CONNECTION`, `$env:ALFACORE_MCP_AUTH_CONNECTION`, `$env:Mcp__Auth__Issuer`, `$env:ASPNETCORE_ENVIRONMENT="Production"`
y **`$env:Mcp__Auth__SigningKeyPath="C:\ProgramData\AlfaCoreMcp\signing-key.pem"`**, la MISMA ruta que el pool: ahí al lado queda la clave que cifra los segundos factores;
con otra ruta el sitio no puede leerlos y nadie entra. En producción el programa se niega a arrancar si falta):
```powershell
cd C:\inetpub\AlfaCoreMcp
dotnet AlfaCore.Mcp.dll crear-usuario alberto "Alberto"
```
Pide la clave dos veces (mínimo 12 caracteres; usar una distinta por persona) y muestra **una sola vez** la clave del segundo factor. Con `--qr C:\temp\alberto.png`
genera además el código QR para escanear con Google/Microsoft Authenticator (**borrar el PNG después**). Hacerlo con cada persona delante, para que lo cargue en su teléfono en ese momento.
Repetir por cada usuario. **Verificar:** `SELECT Usuario, Activo FROM dbo.MCP_USUARIO` en el almacén.

## 5. Verificar antes de conectar nada
```powershell
Start-Website AlfaCoreMcp
curl.exe -i https://alfacentral.ddns.net:8443/health                                   # 200 {"estado":"ok"}
curl.exe -i -X POST https://alfacentral.ddns.net:8443/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d "{}"
#   -> 401 con:  WWW-Authenticate: Bearer resource_metadata="https://alfacentral.ddns.net:8443/.well-known/oauth-protected-resource/mcp"
curl.exe https://alfacentral.ddns.net:8443/.well-known/oauth-protected-resource/mcp   # "resource" y "authorization_servers" con https://alfacentral.ddns.net:8443
curl.exe https://alfacentral.ddns.net:8443/.well-known/oauth-authorization-server      # authorization_endpoint, token_endpoint, registration_endpoint
```
Probar también **desde afuera de la red** (por ejemplo, desde el celular con datos móviles): `https://alfacentral.ddns.net:8443/health`.
Si algo falla: `C:\inetpub\AlfaCoreMcp\logs\` (activar `stdoutLogEnabled="true"` en el `web.config` solo mientras se diagnostica) y el Visor de eventos.

## 6. Conectar los clientes
**Claude Code (para probar primero):**
`claude.cmd mcp add --scope user --transport http alfacore-dir-prod https://alfacentral.ddns.net:8443/mcp` → `/mcp` → *Authenticate* → login.

**claude.ai:** *Settings → Connectors → Add custom connector* → URL `https://alfacentral.ddns.net:8443/mcp`. Dejar el registro de cliente en automático (DCR).
Se abre el login; entrar con el usuario y clave del paso 4. La *redirect URI* de claude.ai (`https://claude.ai/api/mcp/auth_callback`) ya viene en la lista blanca.

**ChatGPT:** *Settings → Connectors* (modo desarrollador) → agregar servidor MCP con esa URL y autenticación OAuth. **Verificar la redirect URI que muestra ChatGPT**
y, si no es una de las de `IA_MCP_OAUTH_REDIRECTS`, agregarla (sin reiniciar nada; se aplica en ≤30 s):
```sql
-- ver lo que hay
SELECT CAST(ValorAux AS nvarchar(max)) FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'IA_MCP_OAUTH_REDIRECTS';
-- agregar una línea (reemplazar la URI por la que muestre ChatGPT)
UPDATE dbo.TA_CONFIGURACION SET ValorAux = CAST(ValorAux AS nvarchar(max)) + CHAR(10) + 'https://chatgpt.com/<ruta-que-muestre>' WHERE CLAVE = 'IA_MCP_OAUTH_REDIRECTS';
```
Si ChatGPT no encuentra los metadatos, avisar: hay un plan B (reglas de reescritura) que no hace falta mientras el sitio sea propio.

**Después de conectar:** `SELECT TOP 20 * FROM dbo.MCP_EVENTO ORDER BY Id DESC` (logins y tokens) y `SELECT TOP 20 * FROM dbo.IA_LOG_CONSULTAS ORDER BY Id DESC` (consultas, con el usuario real).

## Operación
| Necesidad | Cómo |
|---|---|
| **Cortar todo ya** | `UPDATE dbo.TA_CONFIGURACION SET VALOR = 'NO' WHERE CLAVE = 'IA_MCP_HABILITADO'` (≤30 s; corta consultas, login, registro y tokens) o `Stop-Website AlfaCoreMcp` |
| Dar de baja a alguien | SQL del README del proyecto (`Activo = 0` + revocar sus refresh tokens) |
| Renovar el certificado | Se renueva el del sitio de AlfaCore; el binding 8443 usa el mismo: volver a asociarlo (paso 2) |
| Actualizar la aplicación | `Stop-Website AlfaCoreMcp` → copiar la publicación (sin tocar la carpeta de secretos) → `Start-Website AlfaCoreMcp` |
| Un teléfono nuevo o perdido | `dotnet AlfaCore.Mcp.dll configurar-totp <usuario> --qr archivo.png` (corta sus sesiones activas) |
| **Respaldo** | Incluir `C:\ProgramData\AlfaCoreMcp\` (clave de firma y `*.totp-key`) en el respaldo. Sin la clave de cifrado hay que reinscribir el segundo factor de todos |
| Rotar la clave de firma | Detener el sitio, borrar `signing-key.pem`, iniciar (se genera una nueva). Todos los usuarios deben volver a iniciar sesión |
| Marcha atrás completa | `Stop-Website AlfaCoreMcp`; cerrar el puerto 8443; opcional: `Remove-Website`/`Remove-WebAppPool`. AlfaCore no se ve afectado |

## Antes de abrirlo al equipo
- El segundo factor (TOTP) ya viene exigido (`IA_MCP_OAUTH_REQUIERE_TOTP = SI`): nadie entra sin su app autenticadora.
- Usar claves únicas y largas por persona; no reutilizar la de otros sistemas.
- Revisar `MCP_EVENTO` la primera semana (intentos fallidos, bloqueos).
- Dar de baja a quien deje el equipo el mismo día.

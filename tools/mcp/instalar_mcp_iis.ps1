# Instala / actualiza el sitio IIS del MCP de consulta (AlfaCore.Mcp). Ver docs/modulos/mcp-publicacion-iis.md.
# Correr en el servidor web, PowerShell COMO ADMINISTRADOR:
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   C:\inetpub\AlfaCoreMcp\instalar_mcp_iis.ps1
#   (en la MISMA consola, no con "powershell -File": asi las variables quedan para correr crear-usuario despues)
# - NO reinicia IIS ni toca otros sitios / pools. Crea (si faltan) el pool y el sitio AlfaCoreMcp.
# - Las contrasenas se piden por teclado (no quedan en el historial ni en archivos del repo). Se guardan como
#   variables de entorno DEL POOL en la configuracion de IIS (applicationHost.config), no en la carpeta publicada.
# - Antes de crear nada prueba las dos conexiones SQL con los logins reales; si alguna falla, no cambia nada.
param(
    [string]$Sitio = "AlfaCoreMcp",
    [string]$Carpeta = "C:\inetpub\AlfaCoreMcp",
    [string]$Secretos = "C:\ProgramData\AlfaCoreMcp",
    [string]$HostPublico = "alfacentral.ddns.net",
    [int]$Puerto = 8443,
    [int]$PuertoCertificado = 443   # se reutiliza el certificado de este binding (lo renueva Certify)
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

function Leer-Clave([string]$texto) {
    $s = Read-Host $texto -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}
function Leer-Obligatorio([string]$texto) {
    do { $v = (Read-Host $texto).Trim(); if (-not $v) { Write-Host "  (obligatorio: escribirlo, no solo Enter)" -ForegroundColor Yellow } } while (-not $v)
    $v
}
function Probar-Sql([string]$cs, [string]$sql) {
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    try { $cn.Open(); $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 30; return $cmd.ExecuteScalar() }
    finally { $cn.Dispose() }
}

if (-not (Test-Path (Join-Path $Carpeta "AlfaCore.Mcp.dll"))) { throw "No esta $Carpeta\AlfaCore.Mcp.dll: copiar primero la carpeta publicada." }

Write-Host "`n=== 1. Datos de conexion (las claves no se muestran) ===" -ForegroundColor Cyan
$sqlDatos  = Leer-Obligatorio "Servidor SQL de DATOS, con la instancia (ej. 10.8.0.20\ALFANET)"
$baseDatos = Leer-Obligatorio "Base de DATOS (la que tiene el esquema dir)"
$claveDatos = Leer-Clave "Clave del login mcp_dir_ro"
$sqlAuth   = Read-Host "Servidor SQL del almacen OAuth (Enter = 10.8.0.31)"; if (-not $sqlAuth) { $sqlAuth = "10.8.0.31" }
$baseAuth  = Read-Host "Base del almacen OAuth (Enter = ALFA_CENTRAL)";       if (-not $baseAuth) { $baseAuth = "ALFA_CENTRAL" }
$claveAuth = Leer-Clave "Clave del login mcp_auth_rw"

$csDatos = "Server=$sqlDatos;Database=$baseDatos;User Id=mcp_dir_ro;Password=$claveDatos;TrustServerCertificate=True;Encrypt=False"
$csAuth  = "Server=$sqlAuth;Database=$baseAuth;User Id=mcp_auth_rw;Password=$claveAuth;TrustServerCertificate=True;Encrypt=False"

Write-Host "`n=== 2. Probando conexiones ===" -ForegroundColor Cyan
try { $n = Probar-Sql $csDatos "SELECT COUNT(*) FROM dir.vw_Config"; Write-Host "  DATOS ok ($n claves en dir.vw_Config)" -ForegroundColor Green }
catch { throw "  DATOS fallo (servidor $sqlDatos, base $baseDatos, login mcp_dir_ro): $($_.Exception.Message)" }
try { $n = Probar-Sql $csAuth "SELECT COUNT(*) FROM dbo.MCP_USUARIO"; Write-Host "  ALMACEN OAuth ok ($n usuarios)" -ForegroundColor Green }
catch { throw "  ALMACEN OAuth fallo (servidor $sqlAuth, base $baseAuth, login mcp_auth_rw): $($_.Exception.Message)" }

Write-Host "`n=== 3. Certificado (el mismo del puerto $PuertoCertificado) ===" -ForegroundColor Cyan
$b443 = Get-ChildItem IIS:\SslBindings | Where-Object { $_.Port -eq $PuertoCertificado -and $_.Host -eq $HostPublico } | Select-Object -First 1
if (-not $b443) { throw "No hay binding https en el puerto $PuertoCertificado para $HostPublico." }
$huella = $b443.Thumbprint
$cert = Get-Item "Cert:\LocalMachine\My\$huella"
Write-Host "  $($cert.Subject) vence $($cert.NotAfter)"

Write-Host "`n=== 4. Pool, carpeta de secretos y variables ===" -ForegroundColor Cyan
if (-not (Test-Path "IIS:\AppPools\$Sitio")) { New-WebAppPool -Name $Sitio | Out-Null; Write-Host "  pool creado" }
Set-ItemProperty "IIS:\AppPools\$Sitio" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$Sitio" -Name processModel.identityType -Value ApplicationPoolIdentity

New-Item $Secretos -ItemType Directory -Force | Out-Null
# SIDs en vez de nombres: el Windows esta en castellano ("Administradores").
& icacls $Secretos /inheritance:r /grant "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "IIS AppPool\$($Sitio):(OI)(CI)M" | Out-Null
& icacls $Carpeta /grant "IIS AppPool\$($Sitio):(OI)(CI)RX" | Out-Null

$variables = [ordered]@{
    ASPNETCORE_ENVIRONMENT       = "Production"
    ALFACORE_MCP_CONNECTION      = $csDatos
    ALFACORE_MCP_AUTH_CONNECTION = $csAuth
    Mcp__Auth__Issuer            = "https://$($HostPublico):$Puerto"
    Mcp__Auth__SigningKeyPath    = (Join-Path $Secretos "signing-key.pem")
}
$filtro = "system.applicationHost/applicationPools/add[@name='$Sitio']/environmentVariables"
foreach ($k in $variables.Keys) {
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." -AtElement @{name = $k } -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." -Value @{name = $k; value = $variables[$k] }
}
Write-Host "  variables del pool: $($variables.Keys -join ', ')"

Write-Host "`n=== 5. Sitio https://$($HostPublico):$Puerto ===" -ForegroundColor Cyan
if (-not (Get-Website -Name $Sitio)) {
    New-Website -Name $Sitio -PhysicalPath $Carpeta -ApplicationPool $Sitio -Port $Puerto -Ssl -SslFlags 1 -HostHeader $HostPublico | Out-Null
    Write-Host "  sitio creado"
}
$ruta = "IIS:\SslBindings\!$Puerto!$HostPublico"
if (Test-Path $ruta) { Remove-Item $ruta }
New-Item $ruta -Thumbprint $huella -SSLFlags 1 | Out-Null
Restart-WebAppPool -Name $Sitio -ErrorAction SilentlyContinue
Start-Website -Name $Sitio -ErrorAction SilentlyContinue

Write-Host "`n=== 6. Verificacion local ===" -ForegroundColor Cyan
Start-Sleep 3
& curl.exe -s -o NUL -w "  /health -> HTTP %{http_code}`n" --resolve "$($HostPublico):$($Puerto):127.0.0.1" "https://$($HostPublico):$Puerto/health"
& curl.exe -s -o NUL -w "  /mcp sin login -> HTTP %{http_code} (tiene que ser 401)`n" --resolve "$($HostPublico):$($Puerto):127.0.0.1" -X POST "https://$($HostPublico):$Puerto/mcp" -H "Content-Type: application/json" -d "{}"

# Deja las variables en ESTA consola para poder crear usuarios a continuacion con la misma configuracion que el sitio.
foreach ($k in $variables.Keys) { Set-Item "env:$k" $variables[$k] }
Write-Host "`nListo. Para crear un usuario, en ESTA MISMA ventana:" -ForegroundColor Cyan
Write-Host "  cd $Carpeta"
Write-Host "  dotnet AlfaCore.Mcp.dll crear-usuario alberto `"Alberto`" --qr $Secretos\alberto.png"
Write-Host "  (escanear el QR con Authenticator y BORRAR el png)"

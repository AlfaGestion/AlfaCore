# Administra usuarios del MCP en el servidor, con la MISMA configuracion que el sitio IIS (lee las variables del pool).
# Correr en el servidor web, PowerShell COMO ADMINISTRADOR:
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   C:\inetpub\AlfaCoreMcp\usuarios_mcp.ps1 crear-usuario fulano "Nombre Apellido" --qr C:\ProgramData\AlfaCoreMcp\fulano.png
#   C:\inetpub\AlfaCoreMcp\usuarios_mcp.ps1 configurar-totp fulano --qr C:\ProgramData\AlfaCoreMcp\fulano.png
# Las variables (cadenas de conexion, Issuer, ruta de claves) se cargan solo en este proceso; no se muestran ni se guardan.
# Sin bloque param(): todos los argumentos van tal cual al programa (si no, "crear-usuario" se tomaria como nombre de sitio).
$Sitio = "AlfaCoreMcp"
$Carpeta = "C:\inetpub\AlfaCoreMcp"
$ErrorActionPreference = "Stop"
if ($args.Count -eq 0) { throw "Indicar el comando: crear-usuario <usuario> ""Nombre"" [--qr archivo.png]  |  configurar-totp <usuario> [--qr archivo.png]" }

Add-Type -Path "$env:windir\system32\inetsrv\Microsoft.Web.Administration.dll"
$sm = New-Object Microsoft.Web.Administration.ServerManager
$pool = $sm.ApplicationPools[$Sitio]
if (-not $pool) { throw "No existe el pool ${Sitio}: correr primero instalar_mcp_iis.ps1." }
$n = 0
foreach ($v in $pool.GetChildElement("environmentVariables").GetCollection()) {
    [Environment]::SetEnvironmentVariable([string]$v["name"], [string]$v["value"], "Process"); $n++
}
if ($n -eq 0) { throw "El pool ${Sitio} no tiene variables: correr primero instalar_mcp_iis.ps1." }

Push-Location $Carpeta
try { & dotnet AlfaCore.Mcp.dll @args } finally { Pop-Location }

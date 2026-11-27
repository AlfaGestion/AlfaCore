# Configura el SMTP del MCP (codigos de ingreso por mail, avisos e invitaciones) como variables del pool de IIS.
# Correr en el servidor web, PowerShell COMO ADMINISTRADOR:
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   C:\inetpub\AlfaCoreMcp\configurar_email_mcp.ps1
# - La contrasena se pide oculta y queda solo en la configuracion de IIS (applicationHost.config), nunca en archivos del repo.
# - Ofrece mandar un mail de prueba antes de guardar. NO reinicia IIS: solo recicla el pool AlfaCoreMcp.
$Sitio = "AlfaCoreMcp"
$ErrorActionPreference = "Stop"
Import-Module WebAdministration
if (-not (Test-Path "IIS:\AppPools\$Sitio")) { throw "No existe el pool ${Sitio}: correr primero instalar_mcp_iis.ps1." }

function Leer-Clave([string]$texto) {
    $s = Read-Host $texto -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}
function Leer-ConDefecto([string]$texto, [string]$defecto) {
    $v = (Read-Host "$texto (Enter = $defecto)").Trim(); if ($v) { $v } else { $defecto }
}

Write-Host "`n=== SMTP del MCP (mismos datos que RegistroPublico__Email* de AlfaCore) ===" -ForegroundColor Cyan
$server  = Leer-ConDefecto "Servidor SMTP" "mail.alfagestion.com.ar"
$port    = Leer-ConDefecto "Puerto" "587"
$account = Leer-ConDefecto "Cuenta (remitente)" "envios@alfagestion.com.ar"
$clave   = Leer-Clave "Clave de esa cuenta"
$ssl     = (Leer-ConDefecto "Usar SSL (S/N)" "N").ToUpper() -eq "S"

$prueba = (Read-Host "Mail para mandar una prueba ahora (Enter = no probar)").Trim()
if ($prueba) {
    $msg = New-Object System.Net.Mail.MailMessage
    $msg.From = New-Object System.Net.Mail.MailAddress($account, "AlfaCore")
    $msg.To.Add($prueba)
    $msg.Subject = "Prueba del envio de mails del MCP de AlfaCore"
    $msg.Body = "Si recibiste este mail, el MCP puede mandar los codigos de ingreso."
    $smtp = New-Object System.Net.Mail.SmtpClient($server, [int]$port)
    $smtp.EnableSsl = $ssl
    $smtp.Credentials = New-Object System.Net.NetworkCredential($account, $clave)
    try { $smtp.Send($msg); Write-Host "  Prueba enviada a $prueba. Revisar la casilla (y correo no deseado)." -ForegroundColor Green }
    catch { throw "  No se pudo enviar la prueba (no se guardo nada): $($_.Exception.Message)" }
    finally { $msg.Dispose(); $smtp.Dispose() }
}

$variables = [ordered]@{
    Mcp__Email__Server   = $server
    Mcp__Email__Port     = $port
    Mcp__Email__Account  = $account
    Mcp__Email__Password = $clave
    Mcp__Email__Ssl      = $(if ($ssl) { "true" } else { "false" })
}
$filtro = "system.applicationHost/applicationPools/add[@name='$Sitio']/environmentVariables"
foreach ($k in $variables.Keys) {
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." -AtElement @{name = $k } -ErrorAction SilentlyContinue -WarningAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." -Value @{name = $k; value = $variables[$k] }
}
Restart-WebAppPool -Name $Sitio
Write-Host "`nListo: SMTP guardado en el pool $Sitio y pool reciclado. La pantalla de conexion ya pide el mail." -ForegroundColor Cyan

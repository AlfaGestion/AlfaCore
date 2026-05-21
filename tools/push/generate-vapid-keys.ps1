param(
    [string]$Subject = "mailto:admin@alfagestion.com"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
    throw "No se encontró npx. Instalá Node.js o ejecutá: dotnet tool install --global web-push-cli si el equipo usa esa alternativa."
}

$output = npx web-push generate-vapid-keys

Write-Host ""
Write-Host "Claves VAPID generadas para AlfaCore"
Write-Host "Subject: $Subject"
Write-Host ""
$output
Write-Host ""
Write-Host "Configurar en producción como variables de entorno:"
Write-Host "PushNotifications__Subject=$Subject"
Write-Host "PushNotifications__PublicKey=<Public Key generada>"
Write-Host "PushNotifications__PrivateKey=<Private Key generada>"
Write-Host ""
Write-Host "No pegues la PrivateKey en código fuente ni la envíes al navegador."

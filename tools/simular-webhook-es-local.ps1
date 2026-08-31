param(
    [ValidateSet("inbound-text", "status-delivered")]
    [string]$Fixture = "inbound-text",
    [ValidatePattern('^\d+$')]
    [string]$PhoneNumberId = "900000000000084"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repositoryRoot "tools\AlfaCore.EsLocalTool\AlfaCore.EsLocalTool.csproj"
$fixturePath = Join-Path $repositoryRoot "tools\es-local\fixtures\$Fixture.json"

function Stop-Friendly([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

try {
    if (-not (Test-Path -LiteralPath $fixturePath)) { Stop-Friendly "No existe la fixture $Fixture." }
    try { $health = Invoke-WebRequest -Uri "https://localhost:7055/manifest.webmanifest" -UseBasicParsing -TimeoutSec 3 } catch { Stop-Friendly "AlfaCore no esta respondiendo en https://localhost:7055." }
    if ($health.StatusCode -ne 200) { Stop-Friendly "AlfaCore local no supero el health HTTPS." }

    $output = @(& dotnet run --project $toolProject -c Release -- prepare-simulation $PhoneNumberId)
    if ($LASTEXITCODE -ne 0) { Stop-Friendly "No se pudo preparar el routing ficticio local." }
    $base84Token = (($output | Where-Object { $_ -like 'BASE84_TOKEN=*' }) -split '=', 2)[1]
    $crossToken = (($output | Where-Object { $_ -like 'CROSS_TENANT_TOKEN=*' }) -split '=', 2)[1]
    if ([string]::IsNullOrWhiteSpace($base84Token) -or [string]::IsNullOrWhiteSpace($crossToken)) { Stop-Friendly "No se obtuvieron tokens locales de simulacion." }

    $payload = (Get-Content -Raw -LiteralPath $fixturePath).Replace('{{PHONE_NUMBER_ID}}', $PhoneNumberId).Replace('{{EVENT_ID}}', "es_local_$([Guid]::NewGuid().ToString('N'))")
    $headers = @{ "X-AlfaCore-ES-Local-Fixture" = "true" }
    $base84Url = "https://localhost:7055/api/conversaciones/whatsapp/webhook/$base84Token"
    $crossUrl = "https://localhost:7055/api/conversaciones/whatsapp/webhook/$crossToken"

    try {
        $accepted = Invoke-WebRequest -Uri $base84Url -Method Post -ContentType "application/json" -Headers $headers -Body $payload -UseBasicParsing -TimeoutSec 20
        if ($accepted.StatusCode -lt 200 -or $accepted.StatusCode -ge 300) { Stop-Friendly "El callback Base 84 no fue procesado." }
        Write-Host "callback Base84 + ownership Base84 -> PROCESADO" -ForegroundColor Green
    } catch {
        Stop-Friendly "El callback Base 84 fallo. Verifica que el tenant DEV termino sus actualizaciones locales."
    }

    $blocked = $false
    try {
        $unexpected = Invoke-WebRequest -Uri $crossUrl -Method Post -ContentType "application/json" -Headers $headers -Body $payload -UseBasicParsing -TimeoutSec 20
    } catch {
        if ($null -ne $_.Exception.Response) { $blocked = $true }
    }
    if (-not $blocked) { Stop-Friendly "La prueba cross-tenant no fue bloqueada." }
    Write-Host "callback ficticia + ownership Base84 -> BLOQUEADO" -ForegroundColor Green
    Write-Host "No se utilizaron Base 106 ni datos reales."
}
catch {
    Stop-Friendly $_.Exception.Message
}

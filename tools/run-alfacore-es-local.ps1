param(
    [switch]$PrepareOnly,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\AlfaCore\AlfaCore.csproj"
$toolProject = Join-Path $repositoryRoot "tools\AlfaCore.EsLocalTool\AlfaCore.EsLocalTool.csproj"
$bootstrapPath = Join-Path $repositoryRoot "docs\base-datos\sql-test\bootstrap_alfa_central_dev_embedded_signup.sql"
$centralConnection = "Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFA_CENTRAL_DEV;Integrated Security=True;TrustServerCertificate=True"
$tenantConnection = "Server=(localdb)\MSSQLLocalDB;Initial Catalog=ALFACORE_ES_TENANT_DEV;Integrated Security=True;TrustServerCertificate=True"
$keyRingPath = Join-Path $env:LOCALAPPDATA "AlfaCore\DataProtectionKeys\WhatsAppEmbeddedSignup"
$testUrl = "https://localhost:7055/ALFANET/84/conversaciones/configuracion?seccion=canales&subseccion=whatsapp-api"

function Stop-Friendly([string]$message) {
    Write-Host ""
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

function Get-PortPid([int]$port) {
    $connection = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $connection) { return $connection.OwningProcess }
    return $null
}

Clear-Host
Write-Host "========================================"
Write-Host "AlfaCore - WhatsApp ES Local"
Write-Host "========================================"
Write-Host ""
Write-Host "ENTORNO: LOCAL"
Write-Host "CENTRAL ES: ALFA_CENTRAL_DEV"
Write-Host "WORKER: DESHABILITADO"
Write-Host "BASE PERMITIDA: 84"
Write-Host ""

try {
    Write-Host "[1/7] Verificando entorno..."
    if (-not (Test-Path -LiteralPath $projectPath) -or -not (Test-Path -LiteralPath $bootstrapPath)) {
        Stop-Friendly "No se encontraron el proyecto o el bootstrap ES local. Ejecuta el launcher desde un checkout completo."
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { Stop-Friendly "Falta el SDK de .NET requerido por AlfaCore." }
    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $targetFramework = [string]$project.Project.PropertyGroup.TargetFramework | Select-Object -First 1
    if ($targetFramework -notmatch '^net(?<major>\d+)\.0$') { Stop-Friendly "No se pudo detectar el TargetFramework de AlfaCore." }
    $requiredMajor = [int]$Matches.major
    $installedMajors = @(dotnet --list-sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
    if ($installedMajors -notcontains $requiredMajor) { Stop-Friendly "Falta el SDK .NET $requiredMajor requerido por $targetFramework." }
    $localDb = Get-Command SqlLocalDB -ErrorAction SilentlyContinue
    if ($null -eq $localDb) { Stop-Friendly "Falta SQL Server LocalDB. Instalalo y volve a ejecutar este archivo." }
    if ($centralConnection -notmatch 'Initial Catalog=ALFA_CENTRAL_DEV(?:;|$)' -or $centralConnection -match '10\.8\.0\.31|Initial Catalog=ALFA_CENTRAL(?:;|$)') {
        Stop-Friendly "La conexion ES local no supero la guardia de seguridad."
    }

    Write-Host "[2/7] Preparando SQL local..."
    & SqlLocalDB start MSSQLLocalDB | Out-Null
    & dotnet run --project $toolProject -c Release -- bootstrap
    if ($LASTEXITCODE -ne 0) { Stop-Friendly "No se pudo preparar ALFA_CENTRAL_DEV en LocalDB." }

    Write-Host "[3/7] Preparando Embedded Signup..."
    $secretLines = @(& dotnet user-secrets list --project $projectPath 2>$null)
    $hasUserSecret = $secretLines | Where-Object { $_ -match '^WhatsAppEmbeddedSignup:AppSecret\s*=' } | Select-Object -First 1
    $hasProcessSecret = -not [string]::IsNullOrWhiteSpace($env:WhatsAppEmbeddedSignup__AppSecret)
    if ($null -eq $hasUserSecret -and -not $hasProcessSecret) {
        Stop-Friendly "Embedded Signup necesita el App Secret local. Consulta docs/gestion/whatsapp_embedded_signup_local_testing.md para configurarlo."
    }

    Write-Host "[4/7] Preparando Data Protection..."
    New-Item -ItemType Directory -Path $keyRingPath -Force | Out-Null

    Write-Host "[5/7] Verificando HTTPS..."
    & dotnet dev-certs https --check --trust *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Windows puede pedir confirmacion para confiar en el certificado de desarrollo."
        & dotnet dev-certs https --trust
        if ($LASTEXITCODE -ne 0) { Stop-Friendly "No se pudo preparar el certificado HTTPS de desarrollo." }
    }
    $portPid = Get-PortPid 7055
    if ($null -ne $portPid -and -not $PrepareOnly) {
        Stop-Friendly "El puerto 7055 esta siendo utilizado por PID $portPid. Cerra la instancia anterior y volve a intentar."
    }

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ConnectionStrings__AlfaCentral = $centralConnection
    $env:ConnectionStrings__AlfaGestion = $tenantConnection
    $env:WhatsAppEmbeddedSignup__CentralConnectionString = $centralConnection
    $env:WhatsAppEmbeddedSignup__Enabled = "true"
    $env:WhatsAppEmbeddedSignup__WorkerEnabled = "false"
    $env:WhatsAppEmbeddedSignup__AllowedBaseIds__0 = "84"
    $env:WhatsAppEmbeddedSignup__DataProtectionKeysPath = $keyRingPath
    $env:WhatsAppEmbeddedSignup__CallbackBaseUrl = "https://localhost:7055"
    $env:ServidorWeb__EscucharEnRed = "false"
    $env:ServidorWeb__AbrirNavegadorAlIniciar = "false"

    if ($PrepareOnly) {
        Write-Host "[6/7] Inicio omitido por validacion PrepareOnly."
        Write-Host "[7/7] Navegador omitido."
        Write-Host ""
        Write-Host "Preparacion ES local completada correctamente." -ForegroundColor Green
        exit 0
    }

    Write-Host "[6/7] Iniciando AlfaCore..."
    $process = Start-Process -FilePath $dotnet.Source -ArgumentList @("run", "--project", $projectPath, "--launch-profile", "https", "--no-restore") -WorkingDirectory $repositoryRoot -NoNewWindow -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $process.HasExited; $attempt++) {
        Start-Sleep -Milliseconds 500
        try {
            $response = Invoke-WebRequest -Uri "https://localhost:7055/manifest.webmanifest" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }
    if (-not $ready) {
        if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null }
        Stop-Friendly "AlfaCore no respondio por HTTPS a tiempo. Revisa los mensajes anteriores."
    }

    Write-Host "[7/7] Abriendo navegador..."
    if (-not $NoBrowser) { Start-Process $testUrl }
    Write-Host "AlfaCore listo en https://localhost:7055" -ForegroundColor Green
    Write-Host "Ruta de prueba: $testUrl"
    Wait-Process -Id $process.Id
    exit $process.ExitCode
}
catch {
    Stop-Friendly $_.Exception.Message
}

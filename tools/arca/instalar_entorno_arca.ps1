# Instala el entorno Python aislado para la descarga de Mis Comprobantes (ARCA) de AlfaCore.
# Se ejecuta UNA VEZ en el servidor donde corre AlfaCore, con PowerShell como administrador:
#   powershell -ExecutionPolicy Bypass -File instalar_entorno_arca.ps1
# No modifica el Python base ni el que usa recepcion documental: crea un venv aparte.
# Al final muestra la ruta de python.exe para pegar en Administrar > Servidor.
param(
    [string]$PythonBase = 'C:\Python314\python.exe',
    [string]$Destino = 'C:\AlfaCore_ARCA'
)
$ErrorActionPreference = 'Stop'

function Paso($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Falla($t) { Write-Host "`nERROR: $t" -ForegroundColor Red; exit 1 }

Paso "1/5 Python base: $PythonBase"
if (-not (Test-Path $PythonBase)) { Falla "No existe $PythonBase. Pasalo con -PythonBase 'C:\ruta\python.exe'." }
& $PythonBase --version

Paso "2/5 Entorno virtual en $Destino\venv"
New-Item -ItemType Directory -Force $Destino | Out-Null
$venv = Join-Path $Destino 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) { & $PythonBase -m venv $venv; if ($LASTEXITCODE -ne 0) { Falla 'No se pudo crear el venv.' } }
$py = Join-Path $venv 'Scripts\python.exe'

Paso '3/5 Instalando playwright y pywin32 (solo ruedas binarias, sin compilar)'
& $py -m pip install --upgrade pip --only-binary=:all:
& $py -m pip install --only-binary=:all: playwright pywin32
if ($LASTEXITCODE -ne 0) {
    Falla ("pip no pudo instalar playwright/pywin32 para este Python (" + (& $py --version) + ").`n" +
           "Si dice 'No matching distribution', ese Python es demasiado nuevo: instala Python 3.12 o 3.13 (64 bits) y volve a correr con -PythonBase 'C:\Python313\python.exe'.")
}

Paso '4/5 Instalando Chromium dentro del paquete (PLAYWRIGHT_BROWSERS_PATH=0, como espera el script)'
$env:PLAYWRIGHT_BROWSERS_PATH = '0'
& $py -m playwright install chromium
if ($LASTEXITCODE -ne 0) { Falla 'No se pudo bajar Chromium (revisa salida a internet / proxy del servidor).' }

Paso '5/5 Verificando'
& $py -c "import sys,playwright,win32com.client; print('OK Python '+sys.version.split()[0]+' con playwright y pywin32')"
if ($LASTEXITCODE -ne 0) { Falla 'La verificacion de imports fallo.' }

Write-Host "`nListo. En AlfaCore: Administrar > Servidor > 'Python con playwright y pywin32' pegar:" -ForegroundColor Green
Write-Host "  $py" -ForegroundColor Yellow
Write-Host "Despues: Guardar ARCA y Probar descarga." -ForegroundColor Green
Write-Host "Nota: el servicio de AlfaCore debe poder leer $Destino y, para el .xls, abrir Excel (Office 2003 sirve)." -ForegroundColor DarkGray

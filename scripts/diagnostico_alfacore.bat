@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."

set "SERVICE_NAME=AlfaCore"
set "EXE_NAME=AlfaCore.exe"

echo.
echo ===============================================
echo AlfaCore - Diagnostico rapido
echo ===============================================
echo.

echo [1/5] Servicio de Windows:
sc query "%SERVICE_NAME%"
echo.

set "SERVICE_PATH="
for /f "tokens=1,* delims==" %%A in ('wmic service where "name='AlfaCore'" get PathName /value ^| find "="') do set "SERVICE_PATH=%%B"

if defined SERVICE_PATH (
  set "SERVICE_PATH=!SERVICE_PATH:"=!"
  for %%I in ("!SERVICE_PATH!") do set "INSTALL_DIR=%%~dpI"
) else (
  set "INSTALL_DIR="
)

echo [1b/5] Ruta real del servicio:
if defined SERVICE_PATH (
  echo   PathName : !SERVICE_PATH!
  echo   Carpeta  : !INSTALL_DIR!
) else (
  echo   No se pudo resolver la ruta del servicio.
)
echo.

echo [2/5] Proceso activo:
wmic process where "name='%EXE_NAME%'" get ProcessId,ExecutablePath,CommandLine 2>nul
echo.

echo [3/5] PowerShell - ruta del proceso:
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process AlfaCore -ErrorAction SilentlyContinue | Select-Object Id,Path | Format-Table -AutoSize"
echo.

echo [4/5] Archivos esperados en la carpeta de publicacion:
if defined INSTALL_DIR (
  if exist "!INSTALL_DIR!%EXE_NAME%" (
    echo   OK  !INSTALL_DIR!%EXE_NAME%
  ) else (
    echo   FALTA !INSTALL_DIR!%EXE_NAME%
  )
  if exist "!INSTALL_DIR!Dapper.dll" (
    echo   OK  !INSTALL_DIR!Dapper.dll
  ) else (
    echo   FALTA !INSTALL_DIR!Dapper.dll
  )
  set "DEPS_FOUND="
  for /f "delims=" %%F in ('dir /b "!INSTALL_DIR!*.deps.json" 2^>nul') do set "DEPS_FOUND=1"
  if defined DEPS_FOUND (
    echo   OK  deps.json presente
  ) else (
    echo   FALTA deps.json
  )
  set "RUNTIME_FOUND="
  for /f "delims=" %%F in ('dir /b "!INSTALL_DIR!*.runtimeconfig.json" 2^>nul') do set "RUNTIME_FOUND=1"
  if defined RUNTIME_FOUND (
    echo   OK  runtimeconfig presente
  ) else (
    echo   FALTA runtimeconfig
  )
) else (
  echo   No se pudo revisar la instalacion real porque no se resolvio la ruta.
)
echo.

echo [5/5] Confirmacion manual de carpeta real:
echo   Si el servicio existe, la carpeta real es la que contiene AlfaCore.exe y Dapper.dll.
echo   La carpeta publish solo sirve como origen para copiar, no como destino de produccion.
echo.
pause
endlocal

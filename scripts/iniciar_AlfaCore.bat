@echo off
setlocal EnableExtensions

cd /d "%~dp0..\src\AlfaCore"
if errorlevel 1 goto :fail

set "CONFIG_FILE="
if exist "appsettings.Production.json" set "CONFIG_FILE=appsettings.Production.json"
if not defined CONFIG_FILE if exist "appsettings.json" set "CONFIG_FILE=appsettings.json"

set "PUERTO=5055"
if defined CONFIG_FILE (
  for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-Content '%CONFIG_FILE%' -Raw | ConvertFrom-Json).ServidorWeb.Puerto" 2^>nul`) do (
    if not "%%P"=="" set "PUERTO=%%P"
  )
)
set "URL_LOCAL=http://localhost:%PUERTO%"

echo ===============================================
echo AlfaCore - Alfa Gestion
echo Carpeta de trabajo: %CD%
if defined CONFIG_FILE (
  echo Configuracion detectada: %CONFIG_FILE%
) else (
  echo Configuracion detectada: no se encontro appsettings, se usaran valores por defecto.
)
echo URL local esperada: %URL_LOCAL%
echo Si la app escucha en LAN, otras PCs podran entrar por:
echo http://NOMBRE-PC:%PUERTO%
echo ===============================================

set "SERVICE_STATUS="
for /f "usebackq delims=" %%S in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$svc = Get-Service AlfaCore -ErrorAction SilentlyContinue; if ($svc) { $svc.Status }" 2^>nul`) do set "SERVICE_STATUS=%%S"
if /i "%SERVICE_STATUS%"=="Running" goto :service_running

echo Cerrando instancia anterior si existe...
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe = Join-Path (Get-Location) 'bin\\Release\\net8.0\\AlfaCore.exe'; $p = Get-Process AlfaCore -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -ExpandProperty Id; foreach ($id in $p) { Write-Output $id }" 2^>nul`) do (
  echo Deteniendo AlfaCore PID %%I...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Stop-Process -Id %%I -Force"
)

set "PORT_PID="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = Get-NetTCPConnection -LocalPort %PUERTO% -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p) { $p }" 2^>nul`) do set "PORT_PID=%%I"
if defined PORT_PID goto :port_busy

dotnet --list-sdks 2>nul | findstr /r "^8\." >nul
if errorlevel 1 goto :sdk_missing

echo Iniciando servidor...
echo.

dotnet run --configuration Release --no-launch-profile
set "EXITCODE=%ERRORLEVEL%"

if "%EXITCODE%"=="0" goto :end

echo.
echo ===============================================
echo La aplicacion finalizo con error. Codigo: %EXITCODE%
echo Revisa el mensaje anterior para ver la causa.
echo ===============================================
pause
goto :end

:fail
echo.
echo ===============================================
echo No se pudo abrir la carpeta del proyecto.
echo Ruta esperada: %~dp0..\src\AlfaCore
echo ===============================================
pause
goto :end

:service_running
echo AlfaCore ya esta corriendo como servicio de Windows.
echo Abriendo %URL_LOCAL% ...
start "" "%URL_LOCAL%"
timeout /t 2 /nobreak >nul
goto :end

:port_busy
echo.
echo ===============================================
echo No se inicia otra instancia porque el puerto %PUERTO% ya esta en uso.
echo Proceso que escucha en el puerto: PID %PORT_PID%
echo Si AlfaCore ya esta abierto como servicio, usa:
echo %URL_LOCAL%
echo ===============================================
start "" "%URL_LOCAL%"
pause
goto :end

:sdk_missing
echo.
echo ===============================================
echo No se encontro el SDK de .NET 8.
echo AlfaCore necesita el SDK para compilar y ejecutar con dotnet run.
echo.
echo Instala ".NET 8 SDK" x64 y volve a ejecutar este script.
echo Descarga oficial:
echo https://dotnet.microsoft.com/download/dotnet/8.0
echo.
echo Estado actual detectado:
dotnet --info
echo ===============================================
pause

:end
endlocal

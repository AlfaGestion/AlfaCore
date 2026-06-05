@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."

set "SOURCE_DIR=.\publish\AlfaCoreLAN"
for %%I in ("%SOURCE_DIR%") do set "SOURCE_DIR_FULL=%%~fI"

if not exist "%SOURCE_DIR_FULL%" (
  echo.
  echo ===============================================
  echo No existe la carpeta de publicacion:
  echo %SOURCE_DIR_FULL%
  echo Ejecuta primero scripts\publicar_release.bat
  echo ===============================================
  pause
  exit /b 1
)

echo.
echo ===============================================
echo AlfaCore - Actualizacion manual sin reinstalar
echo ===============================================
echo Origen : %SOURCE_DIR_FULL%
echo.
set "DEST_DIR_FULL=%~1"
if defined DEST_DIR_FULL goto :got_destination

set /p "DEST_DIR_FULL=Destino de instalacion: "
set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"

:got_destination
set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"

if not defined DEST_DIR_FULL (
  echo.
  echo No se ingreso un destino.
  pause
  exit /b 1
)

if /i "%DEST_DIR_FULL%"=="%SOURCE_DIR_FULL%" (
  echo.
  echo El destino no puede ser la misma carpeta de publicacion.
  pause
  exit /b 1
)

if not exist "%DEST_DIR_FULL%" (
  echo Creando carpeta destino...
  mkdir "%DEST_DIR_FULL%" >nul 2>&1
)

set "REMOTE_HOST="
for /f "tokens=1,2,3 delims=\" %%A in ("%DEST_DIR_FULL%") do (
  if "%%A"=="" if "%%B"=="" set "REMOTE_HOST=%%C"
)

if defined REMOTE_HOST (
  for /f "tokens=3 delims=: " %%S in ('sc \\%REMOTE_HOST% query "AlfaCore" ^| findstr /R /C:"STATE" 2^>nul') do set "REMOTE_SERVICE_STATE=%%S"
  if /i "%REMOTE_SERVICE_STATE%"=="RUNNING" (
    echo.
    echo ===============================================
    echo El servicio AlfaCore esta activo en %REMOTE_HOST%.
    echo Detenelo antes de actualizar para liberar AlfaCore.dll:
    echo   sc \\%REMOTE_HOST% stop AlfaCore
    echo Cuando finalice la copia, puedes iniciarlo con:
    echo   sc \\%REMOTE_HOST% start AlfaCore
    echo ===============================================
    pause
    exit /b 1
  )
)

echo.
echo Copiando archivos necesarios...
echo - Se preservan JSON y configuraciones existentes.
echo - Se copia todo lo demas desde la publicacion.
echo.
echo Destino detectado: %DEST_DIR_FULL%
echo.

robocopy "%SOURCE_DIR_FULL%" "%DEST_DIR_FULL%" /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF *.json *.log
set "ROBOCODE=%ERRORLEVEL%"

if %ROBOCODE% GEQ 8 (
  echo.
  echo La copia fallo con codigo %ROBOCODE%.
  echo Revisa permisos, archivos bloqueados o la ruta destino.
  echo Importante: la instalacion puede haber quedado con archivos mezclados
  echo (por ejemplo DLL nuevas con .deps.json viejos).
  echo No inicies AlfaCore hasta repetir la actualizacion completa.
  pause
  exit /b %ROBOCODE%
)

echo.
echo Actualizacion completada.
echo Ahora reinicia el servicio o la aplicacion:
echo   sc stop AlfaCore
echo   sc start AlfaCore
echo.
echo Si este destino corresponde a una instalacion en uso, deten la app o el servicio antes de ejecutar el nuevo binario.
echo.
pause
exit /b 0

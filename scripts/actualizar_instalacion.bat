@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."

set "SOURCE_DIR=.\publish\AlfaCoreLAN"
for %%I in ("%SOURCE_DIR%") do set "SOURCE_DIR_FULL=%%~fI"
set "ROOT_COPY_EXIT=0"
set "UPDATES_COPY_EXIT=0"

if /i "%~1"=="/?" goto :show_help
if /i "%~1"=="-h" goto :show_help
if /i "%~1"=="--help" goto :show_help
goto :main

:show_help
echo.
echo ===============================================
echo AlfaCore - actualizar_instalacion.bat
echo ===============================================
echo.
echo Uso:
echo   scripts\actualizar_instalacion.bat "C:\Ruta\De\Instalacion"
echo.
echo Si no informas destino, el script lo pide por pantalla.
echo.
echo Este script:
echo   - toma el publish de .\publish\AlfaCoreLAN
echo   - actualiza una instalacion existente sin reinstalar
echo   - preserva appsettings.json (actual), appsettings.Production.json (legacy) y .env
echo   - preserva App_Data local y wwwroot\uploads
echo   - sincroniza App_Data\updates como espejo exacto del origen (borra scripts viejos/renumerados que ya no esten)
echo.
echo Recomendaciones:
echo   - ejecutar scripts\publicar_release.bat antes de usarlo
echo   - detener el servicio AlfaCore antes de copiar
echo   - hacer backup de carpeta y base de datos
echo.
exit /b 0

:main

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
echo Este script:
echo   - actualiza la instalacion existente usando el publish actual
echo   - copia binarios, DLL, EXE y JSON del runtime
echo   - actualiza App_Data\updates
echo.
echo Preserva en el servidor:
echo   - appsettings.json
echo   - appsettings.Production.json ^(legacy^)
echo   - .env
echo   - App_Data local ^(uploads, sesiones, historicos, diagnosticos, etc.^)
echo   - wwwroot\uploads
echo.
echo Recomendado antes de continuar:
echo   - detener el servicio AlfaCore
echo   - hacer backup de la carpeta y de la base
echo.

set "DEST_DIR_FULL=%~1"
if defined DEST_DIR_FULL goto :got_destination

set /p "DEST_DIR_FULL=Destino de instalacion: "
set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"

:got_destination
set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"

rem quita la barra final (si la hay) para evitar que "%DEST_DIR_FULL%" escape la comilla de cierre en robocopy
if "%DEST_DIR_FULL:~-1%"=="\" if not "%DEST_DIR_FULL:~-2,1%"==":" set "DEST_DIR_FULL=%DEST_DIR_FULL:~0,-1%"

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
if "%DEST_DIR_FULL:~0,2%"=="\\" (
  for /f "tokens=1 delims=\" %%C in ("%DEST_DIR_FULL:~2%") do set "REMOTE_HOST=%%C"
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

set "CONFIRM="
set /p "CONFIRM=Confirma la actualizacion de [%DEST_DIR_FULL%]? [S/N]: "
if /i not "%CONFIRM%"=="S" if /i not "%CONFIRM%"=="SI" (
  echo.
  echo Actualizacion cancelada por el usuario.
  pause
  exit /b 0
)

echo.
echo Copiando archivos necesarios...
echo - Se preservan appsettings.json ^(actual^), appsettings.Production.json ^(legacy^) y .env
echo - Se preservan App_Data y wwwroot\uploads del servidor
echo - Se actualizan los binarios y JSON del runtime
echo - Se actualiza App_Data\updates
echo.
echo Destino detectado: %DEST_DIR_FULL%
echo.

robocopy "%SOURCE_DIR_FULL%" "%DEST_DIR_FULL%" /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XD "App_Data" "wwwroot\uploads" /XF "appsettings.json" "appsettings.Production.json" ".env" "*.log"
set "ROOT_COPY_EXIT=%ERRORLEVEL%"

if %ROOT_COPY_EXIT% GEQ 8 (
  echo.
  echo La copia principal fallo con codigo %ROOT_COPY_EXIT%.
  echo Revisa permisos, archivos bloqueados o la ruta destino.
  echo Importante: la instalacion puede haber quedado con archivos mezclados.
  echo No inicies AlfaCore hasta repetir la actualizacion completa.
  pause
  exit /b %ROOT_COPY_EXIT%
)

if exist "%SOURCE_DIR_FULL%\App_Data\updates" (
  robocopy "%SOURCE_DIR_FULL%\App_Data\updates" "%DEST_DIR_FULL%\App_Data\updates" /E /PURGE /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
  set "UPDATES_COPY_EXIT=%ERRORLEVEL%"
)

if %UPDATES_COPY_EXIT% GEQ 8 (
  echo.
  echo La copia de App_Data\updates fallo con codigo %UPDATES_COPY_EXIT%.
  echo Revisa permisos, archivos bloqueados o la ruta destino.
  echo No inicies AlfaCore hasta repetir la actualizacion completa.
  pause
  exit /b %UPDATES_COPY_EXIT%
)

if exist "%DEST_DIR_FULL%\iniciar_dashboard.bat" (
  del /q "%DEST_DIR_FULL%\iniciar_dashboard.bat" >nul 2>&1
)

echo.
echo Actualizacion completada.
echo.
echo Resumen:
echo   - Binarios actualizados desde publish\AlfaCoreLAN
echo   - Configuracion local preservada ^(appsettings.json actual / appsettings.Production.json legacy / .env^)
echo   - Datos locales preservados ^(App_Data y wwwroot\uploads^)
echo   - Scripts SQL publicados en App_Data\updates
echo   - Launcher actualizado a iniciar_AlfaCore.bat
echo.
echo Ahora reinicia el servicio o la aplicacion:
echo   sc stop AlfaCore
echo   sc start AlfaCore
echo.
echo Si la aplicacion usa actualizaciones de base autom?ticas, se ejecutaran al iniciar.
echo.
pause
exit /b 0


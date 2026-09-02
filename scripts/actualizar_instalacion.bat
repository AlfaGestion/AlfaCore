@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem OJO: "shift" mas abajo (parseo de argumentos) corrompe %~dp0 una vez que se
rem consumen todos los argumentos (bug conocido de cmd.exe) - por eso se guarda
rem ANTES de tocar shift, y de aca en mas se usa SCRIPT_DIR en vez de %~dp0.
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%.."

set "SOURCE_DIR=.\publish\AlfaCoreLAN"
set "DEFAULT_DEST_2=\\10.8.0.53\c\inetpub\wwwroot\AlfaCore"
for %%I in ("%SOURCE_DIR%") do set "SOURCE_DIR_FULL=%%~fI"
set "ROOT_COPY_EXIT=0"
set "UPDATES_COPY_EXIT=0"
set "SKIP_PUBLISH=0"
set "DEST_ARG="

if /i "%~1"=="/?" goto :show_help
if /i "%~1"=="-h" goto :show_help
if /i "%~1"=="--help" goto :show_help

rem -- separa el flag /nopublish del argumento de destino, en cualquier orden
:parse_args
if "%~1"=="" goto :main
if /i "%~1"=="/nopublish" (
  set "SKIP_PUBLISH=1"
  shift
  goto :parse_args
)
set "DEST_ARG=%~1"
shift
goto :parse_args

:show_help
echo.
echo ===============================================
echo AlfaCore - actualizar_instalacion.bat
echo ===============================================
echo.
echo Uso:
echo   scripts\actualizar_instalacion.bat ["C:\Ruta\De\Instalacion"] [/nopublish]
echo.
echo Si no informas destino, el script lo pide por pantalla.
echo Destinos sugeridos:
echo   1^) %DEFAULT_DEST_2% ^(SERVER-ALFACENTRAL^)
echo   2^) Otra ruta
echo.
echo Este script hace TODO el ciclo de despliegue de punta a punta:
echo   1^) publica el release ^(dotnet publish, via publicar_release.bat^)
echo      - se puede saltear este paso con /nopublish si ya publicaste antes
echo   2^) si el destino es una ruta de red ^(\\servidor\...^), te pide que
echo      detengas vos el sitio/servicio en el servidor antes de continuar
echo      ^(los binarios quedan bloqueados si el proceso sigue corriendo^)
echo   3^) copia los binarios actualizados ^(preservando config y datos locales^)
echo   4^) te avisa para que vuelvas a iniciar el sitio/servicio en el servidor
echo.
echo Preserva en el servidor:
echo   - appsettings.json ^(actual^), appsettings.Production.json ^(legacy^), .env, web.config
echo   - App_Data local ^(uploads, sesiones, historicos, diagnosticos, etc.^) y wwwroot\uploads
echo.
echo Recomendacion: hacer backup de carpeta y base de datos antes de actualizar.
echo.
exit /b 0

:main

echo.
echo ===============================================
echo AlfaCore - Publicar y actualizar instalacion ^(todo en uno^)
echo ===============================================

if "%SKIP_PUBLISH%"=="1" (
  echo.
  echo Paso 1/4: publicacion salteada ^(/nopublish^).
) else (
  echo.
  echo Paso 1/4: publicando release...
  call "%SCRIPT_DIR%publicar_release.bat"
  if errorlevel 1 (
    echo.
    echo ===============================================
    echo La publicacion fallo. Se aborta la actualizacion.
    echo ===============================================
    pause
    exit /b 1
  )
)

if not exist "%SOURCE_DIR_FULL%" (
  echo.
  echo ===============================================
  echo No existe la carpeta de publicacion:
  echo %SOURCE_DIR_FULL%
  echo Ejecuta primero scripts\publicar_release.bat ^(o no uses /nopublish^)
  echo ===============================================
  pause
  exit /b 1
)

echo.
echo Origen : %SOURCE_DIR_FULL%
echo.
echo Preserva en el servidor:
echo   - appsettings.json
echo   - appsettings.Production.json ^(legacy^)
echo   - .env
echo   - web.config ^(bindings/puerto propios de esta instalacion^)
echo   - App_Data local ^(uploads, sesiones, historicos, diagnosticos, etc.^)
echo   - wwwroot\uploads
echo.

set "DEST_DIR_FULL=%DEST_ARG%"
if defined DEST_DIR_FULL goto :got_destination

echo Destinos sugeridos:
echo   1^) %DEFAULT_DEST_2% ^(SERVER-ALFACENTRAL^)
echo   2^) Otra ruta
echo.
set /p "DEST_OPTION=Elige destino [1/2]: "
rem OJO: %VAR:"=% con VAR vacia/indefinida corrompe la variable (deja basura
rem tipo "= en vez de vacio) -- por eso el "if defined" guarda en cada uso.
if defined DEST_OPTION set "DEST_OPTION=%DEST_OPTION:"=%"

if /i "%DEST_OPTION%"=="1" (
  set "DEST_DIR_FULL=%DEFAULT_DEST_2%"
) else (
  set /p "DEST_DIR_FULL=Destino de instalacion: "
  if defined DEST_DIR_FULL set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"
)

:got_destination
if defined DEST_DIR_FULL set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"

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

set "CONFIRM="
set /p "CONFIRM=Confirma la actualizacion de [%DEST_DIR_FULL%]? [S/N]: "
if /i not "%CONFIRM%"=="S" if /i not "%CONFIRM%"=="SI" (
  echo.
  echo Actualizacion cancelada por el usuario.
  pause
  exit /b 0
)

if defined REMOTE_HOST (
  echo.
  echo Paso 2/4: destino remoto en %REMOTE_HOST%.
  echo.
  echo Antes de continuar, DETENE vos en el servidor:
  echo   - El sitio / Application Pool de IIS que sirve AlfaCore
  echo     ^(o el servicio "AlfaCore" si corre standalone^)
  echo Si el proceso sigue corriendo, los binarios quedan bloqueados y falla la copia.
  echo.
  pause
) else (
  echo.
  echo Paso 2/4: destino local, no hace falta detener nada.
)

echo.
echo Paso 3/4: copiando archivos actualizados...
echo - Se preservan appsettings.json ^(actual^), appsettings.Production.json ^(legacy^), .env y web.config
echo - Se preservan App_Data y wwwroot\uploads del servidor
echo - Se actualizan los binarios y JSON del runtime
echo - Se actualiza App_Data\updates
echo.
echo Destino detectado: %DEST_DIR_FULL%
echo.

robocopy "%SOURCE_DIR_FULL%" "%DEST_DIR_FULL%" /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XD "App_Data" "wwwroot\uploads" /XF "appsettings.json" "appsettings.Production.json" ".env" "web.config" "*.log"
set "ROOT_COPY_EXIT=%ERRORLEVEL%"

if %ROOT_COPY_EXIT% GEQ 8 (
  echo.
  echo La copia principal fallo con codigo %ROOT_COPY_EXIT%.
  echo Revisa permisos, archivos bloqueados o la ruta destino.
  echo Importante: la instalacion puede haber quedado con archivos mezclados.
  echo No inicies el sitio/servicio hasta repetir la actualizacion completa.
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
  echo No inicies el sitio/servicio hasta repetir la actualizacion completa.
  pause
  exit /b %UPDATES_COPY_EXIT%
)

if exist "%DEST_DIR_FULL%\iniciar_dashboard.bat" (
  del /q "%DEST_DIR_FULL%\iniciar_dashboard.bat" >nul 2>&1
)

echo.
echo Paso 4/4: archivos actualizados.
if defined REMOTE_HOST (
  echo Ahora INICIA vos en el servidor:
  echo   - El sitio / Application Pool de IIS que sirve AlfaCore
  echo     ^(o el servicio "AlfaCore" si corre standalone^)
) else (
  echo Destino local: inicia la app/servicio manualmente si corresponde.
)

echo.
echo ===============================================
echo Actualizacion completada de punta a punta.
echo ===============================================
echo.
echo Resumen:
echo   - Binarios actualizados desde publish\AlfaCoreLAN
echo   - Configuracion local preservada ^(appsettings.json actual / appsettings.Production.json legacy / .env / web.config^)
echo   - Datos locales preservados ^(App_Data y wwwroot\uploads^)
echo   - Scripts SQL publicados en App_Data\updates
echo   - Launcher actualizado a iniciar_AlfaCore.bat
if defined REMOTE_HOST (
  echo   - Recorda iniciar el sitio/servicio en %REMOTE_HOST% si todavia no lo hiciste
) else (
  echo   - Destino local: inicia la app/servicio manualmente si corresponde
)
echo.
echo Si la aplicacion usa actualizaciones de base automaticas, se ejecutaran al iniciar.
echo.
pause
exit /b 0

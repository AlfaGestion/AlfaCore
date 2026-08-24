@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."

set "SOURCE_DIR=.\publish\AlfaCoreLAN"
set "DEFAULT_DEST_1=\\10.8.0.32\c\Program Files\Alfa Gestion\AlfaCore"
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
echo   1^) %DEFAULT_DEST_1% ^(SERVER-ALFAWEB^)
echo   2^) %DEFAULT_DEST_2% ^(SERVER-ALFACENTRAL^)
echo.
echo Este script hace TODO el ciclo de despliegue de punta a punta:
echo   1^) publica el release ^(dotnet publish, via publicar_release.bat^)
echo      - se puede saltear este paso con /nopublish si ya publicaste antes
echo   2^) si el destino es una ruta de red ^(\\servidor\...^), detiene los
echo      servicios remotos que puedan tener los binarios bloqueados
echo      ^(el servicio "AlfaCore" y, si esta activo, IIS/W3SVC^)
echo   3^) copia los binarios actualizados ^(preservando config y datos locales^)
echo   4^) vuelve a iniciar en el servidor solo los servicios que estaban
echo      corriendo antes de este despliegue
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
  call "%~dp0publicar_release.bat"
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
echo   1^) %DEFAULT_DEST_1% ^(SERVER-ALFAWEB^)
echo   2^) %DEFAULT_DEST_2% ^(SERVER-ALFACENTRAL^)
echo   3^) Otra ruta
echo.
set /p "DEST_OPTION=Elige destino [1/2/3]: "
set "DEST_OPTION=%DEST_OPTION:"=%"

if /i "%DEST_OPTION%"=="1" (
  set "DEST_DIR_FULL=%DEFAULT_DEST_1%"
) else if /i "%DEST_OPTION%"=="2" (
  set "DEST_DIR_FULL=%DEFAULT_DEST_2%"
) else (
  set /p "DEST_DIR_FULL=Destino de instalacion: "
  set "DEST_DIR_FULL=%DEST_DIR_FULL:"=%"
)

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

set "CONFIRM="
set /p "CONFIRM=Confirma la actualizacion de [%DEST_DIR_FULL%]? [S/N]: "
if /i not "%CONFIRM%"=="S" if /i not "%CONFIRM%"=="SI" (
  echo.
  echo Actualizacion cancelada por el usuario.
  pause
  exit /b 0
)

set "REMOTE_ALFACORE_WAS_RUNNING=0"
set "REMOTE_W3SVC_WAS_RUNNING=0"

if defined REMOTE_HOST (
  echo.
  echo Paso 2/4: deteniendo servicios remotos en %REMOTE_HOST% ^(si estan activos^)...

  call :estado_servicio "%REMOTE_HOST%" "AlfaCore"
  if /i "!ESTADO_SERVICIO!"=="RUNNING" (
    set "REMOTE_ALFACORE_WAS_RUNNING=1"
    echo   - Deteniendo servicio AlfaCore...
    sc \\%REMOTE_HOST% stop AlfaCore >nul 2>&1
    call :esperar_estado "%REMOTE_HOST%" "AlfaCore" "STOPPED"
    if errorlevel 1 (
      echo.
      echo ===============================================
      echo El servicio AlfaCore en %REMOTE_HOST% no llego a detenerse a tiempo.
      echo Revisalo a mano antes de reintentar ^(sc \\%REMOTE_HOST% query AlfaCore^).
      echo ===============================================
      pause
      exit /b 1
    )
  )

  rem -- IIS (W3SVC): en instalaciones donde AlfaCore corre alojado bajo IIS,
  rem    el proceso w3wp.exe puede mantener los binarios bloqueados aunque el
  rem    servicio "AlfaCore" ya este detenido. Solo se toca si esta corriendo,
  rem    y solo se reinicia despues si estaba corriendo antes.
  call :estado_servicio "%REMOTE_HOST%" "W3SVC"
  if /i "!ESTADO_SERVICIO!"=="RUNNING" (
    set "REMOTE_W3SVC_WAS_RUNNING=1"
    echo   - Deteniendo IIS ^(W3SVC^)...
    sc \\%REMOTE_HOST% stop W3SVC >nul 2>&1
    call :esperar_estado "%REMOTE_HOST%" "W3SVC" "STOPPED"
    if errorlevel 1 (
      echo.
      echo ===============================================
      echo IIS ^(W3SVC^) en %REMOTE_HOST% no llego a detenerse a tiempo.
      echo Revisalo a mano antes de reintentar ^(sc \\%REMOTE_HOST% query W3SVC^).
      echo ===============================================
      pause
      exit /b 1
    )
  )
) else (
  echo.
  echo Paso 2/4: destino local, no hay servicios remotos que detener.
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
  echo No inicies AlfaCore hasta repetir la actualizacion completa.
  call :reiniciar_servicios_remotos
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
  call :reiniciar_servicios_remotos
  pause
  exit /b %UPDATES_COPY_EXIT%
)

if exist "%DEST_DIR_FULL%\iniciar_dashboard.bat" (
  del /q "%DEST_DIR_FULL%\iniciar_dashboard.bat" >nul 2>&1
)

echo.
echo Paso 4/4: reiniciando servicios remotos ^(los que estaban activos antes^)...
call :reiniciar_servicios_remotos

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
  echo   - Servicios remotos reiniciados en %REMOTE_HOST% ^(los que estaban activos^)
) else (
  echo   - Destino local: inicia la app/servicio manualmente si corresponde
)
echo.
echo Si la aplicacion usa actualizaciones de base automaticas, se ejecutaran al iniciar.
echo.
pause
exit /b 0

rem ===================================================================
rem  Subrutinas
rem ===================================================================

rem -- vuelve a iniciar, en el servidor remoto, unicamente los servicios que
rem    este mismo script detuvo (segun REMOTE_*_WAS_RUNNING). IIS antes que
rem    el servicio AlfaCore, porque AlfaCore puede depender de IIS activo.
:reiniciar_servicios_remotos
if not defined REMOTE_HOST exit /b 0
if "%REMOTE_W3SVC_WAS_RUNNING%"=="1" (
  echo   - Iniciando IIS ^(W3SVC^)...
  sc \\%REMOTE_HOST% start W3SVC >nul 2>&1
  call :esperar_estado "%REMOTE_HOST%" "W3SVC" "RUNNING"
)
if "%REMOTE_ALFACORE_WAS_RUNNING%"=="1" (
  echo   - Iniciando servicio AlfaCore...
  sc \\%REMOTE_HOST% start AlfaCore >nul 2>&1
  call :esperar_estado "%REMOTE_HOST%" "AlfaCore" "RUNNING"
)
exit /b 0

rem -- deja el estado actual (RUNNING/STOPPED/START_PENDING/STOP_PENDING/etc.)
rem    del servicio %2 en el host %1 en la variable ESTADO_SERVICIO. La
rem    etiqueta del campo cambia segun el idioma de Windows (ESTADO/STATE),
rem    por eso se busca cualquiera de las dos; el VALOR del estado siempre
rem    queda en ingles.
:estado_servicio
set "ESTADO_SERVICIO="
for /f "tokens=3 delims=: " %%S in ('sc \\%~1 query "%~2" 2^>nul ^| findstr /C:"ESTADO" /C:"STATE"') do set "ESTADO_SERVICIO=%%S"
exit /b 0

rem -- espera hasta ~30s a que el servicio %2 en el host %1 llegue al estado
rem    %3. Devuelve errorlevel 1 si se agoto el tiempo de espera.
:esperar_estado
set "_ESP_HOST=%~1"
set "_ESP_SVC=%~2"
set "_ESP_TARGET=%~3"
set "_ESP_INTENTOS=0"
:esperar_estado_loop
call :estado_servicio "%_ESP_HOST%" "%_ESP_SVC%"
if /i "!ESTADO_SERVICIO!"=="%_ESP_TARGET%" exit /b 0
set /a "_ESP_INTENTOS+=1"
if %_ESP_INTENTOS% GEQ 30 exit /b 1
timeout /t 1 /nobreak >nul
goto :esperar_estado_loop

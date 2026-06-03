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
set /p "DEST_DIR=Destino de instalacion: "

set "DEST_DIR=%DEST_DIR:"=%"
if not defined DEST_DIR (
  echo.
  echo No se ingreso un destino.
  pause
  exit /b 1
)

for %%I in ("%DEST_DIR%") do set "DEST_DIR_FULL=%%~fI"

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

echo.
echo Copiando archivos necesarios...
echo - Se preservan JSON y configuraciones existentes.
echo - Se copia todo lo demas desde la publicacion.
echo.

robocopy "%SOURCE_DIR_FULL%" "%DEST_DIR_FULL%" /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF *.json *.log
set "ROBOCODE=%ERRORLEVEL%"

if %ROBOCODE% GEQ 8 (
  echo.
  echo La copia fallo con codigo %ROBOCODE%.
  echo Revisa permisos, archivos bloqueados o la ruta destino.
  pause
  exit /b %ROBOCODE%
)

echo.
echo Actualizacion completada.
echo Ahora reinicia el servicio o la aplicacion:
echo   sc stop AlfaCore
echo   sc start AlfaCore
echo.
pause
exit /b 0

@echo off
setlocal

set "SERVICE_NAME=AlfaCore"
set "SILENT="
if /i "%~1"=="/silent" set "SILENT=1"

net session >nul 2>&1
if errorlevel 1 (
    echo Este script requiere permisos de administrador.
    echo Ejecutalo con clic derecho ^> Ejecutar como administrador.
    if not defined SILENT pause
    exit /b 1
)

echo ================================================
echo AlfaCore - Detener servicio Windows
echo ================================================

sc query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo ERROR: El servicio "%SERVICE_NAME%" no existe.
    if not defined SILENT pause
    exit /b 1
)

echo Deteniendo servicio...
sc stop "%SERVICE_NAME%"
if errorlevel 1 (
    echo ERROR: No se pudo detener el servicio.
    if not defined SILENT pause
    exit /b 1
)

echo ================================================
echo Servicio detenido correctamente.
echo ================================================
if not defined SILENT pause

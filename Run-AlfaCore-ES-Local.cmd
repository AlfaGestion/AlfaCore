@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run-alfacore-es-local.ps1"
if errorlevel 1 pause

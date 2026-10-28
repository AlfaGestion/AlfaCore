@echo off
setlocal EnableExtensions
rem Reutiliza publish\AlfaCoreLAN. Admite el destino opcional del actualizador.
call "%~dp0actualizar_instalacion.bat" %* /nopublish
exit /b %ERRORLEVEL%

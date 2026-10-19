@echo off
setlocal
set "PATH=%ProgramFiles%\dotnet;%PATH%"
set "ProcesosSegundoPlano__Deshabilitados=true"
set "ServidorWeb__EscucharEnRed=false"
call "%~dp0scripts\iniciar_AlfaCore.bat"
endlocal

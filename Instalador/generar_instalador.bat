@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0.."
set "ROOT=%CD%"
set "BUILD_WORKSPACE=%TEMP%\AlfaCoreInstallerBuild"
set "PUBLISH_SCRIPT=%BUILD_WORKSPACE%\scripts\publicar_release.bat"
set "PUBLISH_DIR=%BUILD_WORKSPACE%\publish\AlfaCoreLAN"
set "INSTALLER_ROOT=%ROOT%\instalador\AlfaCore"
set "STAGE_DIR=%INSTALLER_ROOT%\_input"
set "PREREQ_DIR=%INSTALLER_ROOT%\prerequisitos"
set "SCRIPTS_DIR=%INSTALLER_ROOT%\scripts"
set "ISS_FILE=%ROOT%\installer\AlfaCore.iss"
set "README_SRC=%ROOT%\instalador\README_INSTALACION.txt"
set "HOSTING_BUNDLE_URL=https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe"
set "HOSTING_BUNDLE=%PREREQ_DIR%\dotnet-hosting-win.exe"
set "ISCC_EXE="

echo ===============================================
echo AlfaCore - Generar instalador
echo ===============================================

echo [1/5] Preparando workspace temporal...
if exist "%BUILD_WORKSPACE%" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item -LiteralPath '%BUILD_WORKSPACE%' -Recurse -Force -ErrorAction SilentlyContinue"
)
mkdir "%BUILD_WORKSPACE%" >nul 2>&1
echo Copiando repositorio al workspace temporal...
robocopy "%ROOT%" "%BUILD_WORKSPACE%" /MIR /R:2 /W:1 /XD ".git" ".vs" "publish" "instalador\AlfaCore" "src\AlfaCore\bin" "src\AlfaCore\obj" "src\AlfaCoreShell\bin" "src\AlfaCoreShell\obj" >nul
set "ROBOCOPY_EXIT=%ERRORLEVEL%"
if %ROBOCOPY_EXIT% GEQ 8 (
  echo Robocopy devolvio error %ROBOCOPY_EXIT% al preparar el workspace temporal.
  exit /b %ROBOCOPY_EXIT%
)

if not exist "%PUBLISH_SCRIPT%" (
  echo No se encontro el script base de publicacion en el workspace temporal:
  echo   %PUBLISH_SCRIPT%
  exit /b 1
)

echo [1/5] Publicando aplicacion base en workspace temporal...
call "%PUBLISH_SCRIPT%"
if errorlevel 1 (
  if not exist "%PUBLISH_DIR%\AlfaCore.exe" (
    echo Fallo la publicacion y no se encontro una salida valida.
    exit /b 1
  )
)

if not exist "%INSTALLER_ROOT%" mkdir "%INSTALLER_ROOT%"
if not exist "%STAGE_DIR%" mkdir "%STAGE_DIR%"
if not exist "%PREREQ_DIR%" mkdir "%PREREQ_DIR%"
if not exist "%SCRIPTS_DIR%" mkdir "%SCRIPTS_DIR%"

echo [2/5] Limpiando carpeta de armado...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%STAGE_DIR%' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%PREREQ_DIR%' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%SCRIPTS_DIR%' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
del /q "%INSTALLER_ROOT%\AlfaCoreSetup*.exe" >nul 2>&1
del /q "%INSTALLER_ROOT%\README_INSTALACION.txt" >nul 2>&1
del /q "%INSTALLER_ROOT%\AlfaCore.iss" >nul 2>&1

echo [3/5] Armando carpeta de instalacion...
robocopy "%PUBLISH_DIR%" "%STAGE_DIR%" /MIR /R:2 /W:1 /XF "appsettings.Development.json" "appsettings.Production.json" "appsettings.Production.sample.json" "appsettings.Server.sample.json" "*.pdb" "*.log" >nul
set "ROBOCOPY_EXIT=%ERRORLEVEL%"
if %ROBOCOPY_EXIT% GEQ 8 (
  echo Robocopy devolvio error %ROBOCOPY_EXIT% al copiar la aplicacion.
  exit /b %ROBOCOPY_EXIT%
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$f = '%STAGE_DIR%\appsettings.json'; if (Test-Path $f) { $j = Get-Content $f -Raw | ConvertFrom-Json; $j.ModoSaaS = $false; if ($j.ConnectionStrings) { $j.ConnectionStrings.PSObject.Properties.Remove('AlfaCentral') | Out-Null }; $j | ConvertTo-Json -Depth 10 | Set-Content $f -Encoding UTF8 }"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -LiteralPath '%STAGE_DIR%' -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'appsettings.json' } | Remove-Item -Force -ErrorAction SilentlyContinue"

if exist "%PUBLISH_DIR%\App_Data\updates" (
  if not exist "%STAGE_DIR%\App_Data\updates" mkdir "%STAGE_DIR%\App_Data\updates" >nul 2>&1
  robocopy "%PUBLISH_DIR%\App_Data\updates" "%STAGE_DIR%\App_Data\updates" /MIR /R:2 /W:1 >nul
  set "ROBOCOPY_EXIT=%ERRORLEVEL%"
  if %ROBOCOPY_EXIT% GEQ 8 (
    echo Robocopy devolvio error %ROBOCOPY_EXIT% al copiar App_Data\updates.
    exit /b %ROBOCOPY_EXIT%
  )
)

if exist "%README_SRC%" copy /Y "%README_SRC%" "%INSTALLER_ROOT%\README_INSTALACION.txt" >nul
copy /Y "%ROOT%\scripts\instalar_servicio.bat" "%SCRIPTS_DIR%\instalar_servicio.bat" >nul
copy /Y "%ROOT%\scripts\desinstalar_servicio.bat" "%SCRIPTS_DIR%\desinstalar_servicio.bat" >nul
copy /Y "%ROOT%\scripts\detener_servicio.bat" "%SCRIPTS_DIR%\detener_servicio.bat" >nul
copy /Y "%ROOT%\scripts\abrir_firewall.bat" "%SCRIPTS_DIR%\abrir_firewall.bat" >nul
copy /Y "%ROOT%\scripts\Abrir-Firewall.ps1" "%SCRIPTS_DIR%\Abrir-Firewall.ps1" >nul
copy /Y "%ROOT%\scripts\iniciar_AlfaCore.bat" "%SCRIPTS_DIR%\iniciar_AlfaCore.bat" >nul
copy /Y "%ROOT%\scripts\actualizar_instalacion.bat" "%SCRIPTS_DIR%\actualizar_instalacion.bat" >nul

echo [4/5] Preparando .NET 8 Hosting Bundle...
if not exist "%HOSTING_BUNDLE%" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop';" ^
    "$out = '%HOSTING_BUNDLE%';" ^
    "try { Invoke-WebRequest -Uri '%HOSTING_BUNDLE_URL%' -OutFile $out -UseBasicParsing } catch { exit 1 }"
  if errorlevel 1 (
    echo Aviso: no se pudo descargar el Hosting Bundle. El instalador seguira sin incluirlo.
  )
)

echo [5/5] Compilando instalador con Inno Setup...
for %%I in ("%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe") do if exist "%%~fI" set "ISCC_EXE=%%~fI"
if not defined ISCC_EXE for %%I in ("%ProgramFiles%\Inno Setup 7\ISCC.exe") do if exist "%%~fI" set "ISCC_EXE=%%~fI"
if not defined ISCC_EXE for %%I in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe") do if exist "%%~fI" set "ISCC_EXE=%%~fI"
if not defined ISCC_EXE for %%I in ("%ProgramFiles%\Inno Setup 6\ISCC.exe") do if exist "%%~fI" set "ISCC_EXE=%%~fI"

if not defined ISCC_EXE (
  echo.
  echo Inno Setup no esta instalado en esta PC.
  echo Se dejo preparada la carpeta:
  echo   %INSTALLER_ROOT%
  echo.
  echo Instalalo desde https://jrsoftware.org/isinfo.php y compila:
  echo   %ISS_FILE%
  exit /b 0
)

set "INNO_DEFINES=-DSourceDir=%STAGE_DIR% -DOutputDir=%INSTALLER_ROOT%"
if exist "%HOSTING_BUNDLE%" set "INNO_DEFINES=%INNO_DEFINES% -DIncludeHostingBundle=1"
"%ISCC_EXE%" /Qp %INNO_DEFINES% "%ISS_FILE%"
if errorlevel 1 (
  echo La compilacion del instalador fallo.
  exit /b 1
)

if exist "%INSTALLER_ROOT%\AlfaCoreSetup.exe" (
  echo Instalador generado correctamente:
  echo   %INSTALLER_ROOT%\AlfaCoreSetup.exe
) else (
  echo La compilacion termino sin error pero no se encontro AlfaCoreSetup.exe.
  exit /b 1
)

exit /b 0

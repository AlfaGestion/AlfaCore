@echo off
setlocal
cd /d "%~dp0.."

set "APP_VERSION=%~1"
if not defined APP_VERSION set "APP_VERSION=1.0.0"

set "RELEASE_SCRIPT=.\scripts\publicar_release.bat"
set "SOURCE_DIR=.\publish\AlfaCoreLAN"
set "INSTALLER_ROOT=.\publish\AlfaCoreInstaller"
set "INPUT_DIR=%INSTALLER_ROOT%\Input"
set "OUTPUT_DIR=%INSTALLER_ROOT%\Output"
set "ISS_FILE=.\installer\AlfaCore.iss"
set "PREREQS_CACHE=.\installer\prereqs"
set "HOSTING_BUNDLE_URL=https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe"
set "ISCC_EXE="
set "BUNDLE_FLAG="

echo ===============================================
echo AlfaCore - Publicacion de instalador
echo Version: %APP_VERSION%
echo ===============================================

if not exist "%RELEASE_SCRIPT%" (
  echo No se encontro %RELEASE_SCRIPT%.
  exit /b 1
)

echo [1/5] Generando publicacion base...
call "%RELEASE_SCRIPT%"
if errorlevel 1 (
  if exist "%SOURCE_DIR%\AlfaCore.exe" (
    echo La publicacion automatica fallo, pero se encontro una publicacion previa valida.
    echo Se continuara usando la carpeta existente:
    echo   %SOURCE_DIR%
  ) else (
    goto :error
  )
)

for %%I in ("%SOURCE_DIR%") do set "SOURCE_DIR_FULL=%%~fI"
for %%I in ("%INPUT_DIR%") do set "INPUT_DIR_FULL=%%~fI"
for %%I in ("%OUTPUT_DIR%") do set "OUTPUT_DIR_FULL=%%~fI"
for %%I in ("%ISS_FILE%") do set "ISS_FILE_FULL=%%~fI"

echo [2/5] Preparando carpeta limpia para el setup...
if exist "%INPUT_DIR%" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%INPUT_DIR_FULL%' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
)
if not exist "%INPUT_DIR%" mkdir "%INPUT_DIR%"
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

robocopy "%SOURCE_DIR_FULL%" "%INPUT_DIR_FULL%" /MIR /XF "*.pdb" "*.log" "appsettings.Development.json" /XD "App_Data" >nul
set "ROBOCODE=%ERRORLEVEL%"
if %ROBOCODE% GEQ 8 (
  echo Robocopy devolvio error %ROBOCODE%.
  goto :error
)

if exist "%SOURCE_DIR_FULL%\App_Data\updates" (
  if not exist "%INPUT_DIR_FULL%\App_Data\updates" mkdir "%INPUT_DIR_FULL%\App_Data\updates"
  robocopy "%SOURCE_DIR_FULL%\App_Data\updates" "%INPUT_DIR_FULL%\App_Data\updates" /MIR >nul
  set "ROBOCODE=%ERRORLEVEL%"
  if %ROBOCODE% GEQ 8 (
    echo Robocopy devolvio error %ROBOCODE% al copiar App_Data\updates.
    goto :error
  )
)

echo [2.5/5] Limpiando datos de conexion del instalador...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$f = '%INPUT_DIR_FULL%\appsettings.json'; $j = Get-Content $f -Raw | ConvertFrom-Json; $j.ConnectionStrings.AlfaGestion = ''; if ($j.ConnectionStrings) { $j.ConnectionStrings.PSObject.Properties.Remove('AlfaCentral') | Out-Null }; $j | ConvertTo-Json -Depth 10 | Set-Content $f -Encoding UTF8"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$f = '%INPUT_DIR_FULL%\appsettings.json'; if (Test-Path $f) { $j = Get-Content $f -Raw | ConvertFrom-Json; $j.ModoSaaS = $false; if ($j.ConnectionStrings) { $j.ConnectionStrings.PSObject.Properties.Remove('AlfaCentral') | Out-Null }; $j | ConvertTo-Json -Depth 10 | Set-Content $f -Encoding UTF8 }"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -LiteralPath '%INPUT_DIR_FULL%' -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'appsettings.json' } | Remove-Item -Force -ErrorAction SilentlyContinue"

echo [2.6/5] Copiando scripts de servicio...
copy /Y ".\scripts\instalar_servicio.bat" "%INPUT_DIR_FULL%\instalar_servicio.bat" >nul
copy /Y ".\scripts\desinstalar_servicio.bat" "%INPUT_DIR_FULL%\desinstalar_servicio.bat" >nul

echo [3/5] Verificando .NET 8 Hosting Bundle...
if not exist "%PREREQS_CACHE%" mkdir "%PREREQS_CACHE%"
set "CACHED_BUNDLE=%PREREQS_CACHE%\dotnet-hosting-win.exe"

if not exist "%CACHED_BUNDLE%" (
  echo Descargando .NET 8 Hosting Bundle, puede tardar unos minutos...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%HOSTING_BUNDLE_URL%' -OutFile '%CACHED_BUNDLE%' -UseBasicParsing" 2>nul
  if not exist "%CACHED_BUNDLE%" (
    echo Advertencia: no se pudo descargar el Hosting Bundle. El instalador no lo incluira.
  ) else (
    echo Hosting Bundle descargado correctamente.
  )
) else (
  echo Hosting Bundle encontrado en cache.
)

if exist "%CACHED_BUNDLE%" (
  if not exist "%INPUT_DIR_FULL%\prereqs" mkdir "%INPUT_DIR_FULL%\prereqs"
  copy /Y "%CACHED_BUNDLE%" "%INPUT_DIR_FULL%\prereqs\dotnet-hosting-win.exe" >nul
  set "BUNDLE_FLAG=/DIncludeHostingBundle=1"
)

echo [4/5] Buscando Inno Setup...
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC_EXE=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined ISCC_EXE for /f "delims=" %%I in ('where iscc.exe 2^>nul') do if not defined ISCC_EXE set "ISCC_EXE=%%I"

if not defined ISCC_EXE (
  echo Inno Setup no esta instalado en esta PC.
  echo La carpeta para el instalador quedo preparada en:
  echo   %INPUT_DIR_FULL%
  echo El script del setup esta en:
  echo   %ISS_FILE_FULL%
  echo Instala Inno Setup y compila ese archivo para obtener el Setup.exe.
  goto :prepared
)

echo [5/5] Compilando instalador...
"%ISCC_EXE%" /DAppVersion=%APP_VERSION% /DSourceDir="%INPUT_DIR_FULL%" /DOutputDir="%OUTPUT_DIR_FULL%" %BUNDLE_FLAG% "%ISS_FILE_FULL%"
if errorlevel 1 goto :error

echo ===============================================
echo Instalador generado correctamente.
echo Salida: %OUTPUT_DIR_FULL%
echo ===============================================
goto :eof

:prepared
echo ===============================================
echo Publicacion preparada para instalador.
echo Falta solo compilar con Inno Setup.
echo ===============================================
goto :eof

:error
echo ===============================================
echo No se pudo generar el instalador.
echo Revisa los mensajes anteriores.
echo ===============================================
PAUSE
exit /b 1

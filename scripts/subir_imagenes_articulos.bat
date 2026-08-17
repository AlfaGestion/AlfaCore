@echo off
REM Sube al FTP de AlfaNet las imagenes de una carpeta local que esten
REM nombradas por codigo de articulo (ej: 526.jpg), a la carpeta
REM Clientes/IDCLIENTE/IDBASE/imagenes. Tambien sube la subcarpeta
REM "thumbs4" (miniaturas, mismo nombre de archivo que el original) a
REM Clientes/IDCLIENTE/IDBASE/thumbs4. Ignora cualquier archivo cuyo
REM nombre empiece con "TMP_" (fotos temporales sin relacionar a un
REM articulo).
REM
REM Usa curl.exe (viene de fabrica en Windows 10/11, en System32) en vez
REM del cliente ftp.exe clasico, porque ftp.exe usa modo activo por
REM defecto y eso falla detras de la mayoria de los routers/firewalls.
REM curl usa modo pasivo automaticamente y funciona sin configurar nada.
REM
REM ATENCION: este archivo tiene credenciales de FTP en texto plano.
REM No lo compartas fuera del equipo ni lo subas a repos publicos.
REM
REM Por defecto ya queda configurado para Fernandez Deposito:
REM   Carpeta:   C:\Alfa Gestion\imagenes\ImagenesWeb2\  (y su subcarpeta thumbs4\)
REM   IDCLIENTE: 112010760
REM   IDBASE:    115
REM
REM Doble clic y listo. Si alguna vez queres usarlo para otro cliente o
REM carpeta, le podes pasar los parametros y pisan estos valores:
REM   subir_imagenes_articulos.bat "C:\ruta\a\carpeta\imagenes" IDCLIENTE IDBASE

setlocal enabledelayedexpansion

set "CARPETA_ORIGEN=C:\Alfa Gestion\imagenes\ImagenesWeb2\"
set "IDCLIENTE=112010760"
set "IDBASE=115"

if not "%~1"=="" set "CARPETA_ORIGEN=%~1"
if not "%~2"=="" set "IDCLIENTE=%~2"
if not "%~3"=="" set "IDBASE=%~3"

if "%CARPETA_ORIGEN:~-1%"=="\" set "CARPETA_ORIGEN=%CARPETA_ORIGEN:~0,-1%"
set "CARPETA_THUMBS=%CARPETA_ORIGEN%\thumbs4"

if not exist "%CARPETA_ORIGEN%\*" (
    echo No se encontro la carpeta: %CARPETA_ORIGEN%
    pause
    exit /b 1
)

where curl >nul 2>&1
if errorlevel 1 (
    echo No se encontro curl.exe en este equipo.
    echo Windows 10/11 lo trae de fabrica en System32; si no esta, instalalo aparte.
    pause
    exit /b 1
)

set "FTP_HOST=alfanet.ddns.net"
set "FTP_USER=ftpalfa"
set "FTP_PASS=24681012"
set "FTP_CARPETA_BASE=Clientes/%IDCLIENTE%/%IDBASE%"

echo ===============================================
echo Subida de imagenes de articulos
echo Cliente: %IDCLIENTE%
echo Base   : %IDBASE%
echo Origen : %CARPETA_ORIGEN%
echo Destino: ftp://%FTP_HOST%/%FTP_CARPETA_BASE%/
echo ===============================================
echo.

call :SubirCarpeta "%CARPETA_ORIGEN%" "%FTP_CARPETA_BASE%/imagenes" "originales"
echo.

if exist "%CARPETA_THUMBS%\*" (
    call :SubirCarpeta "%CARPETA_THUMBS%" "%FTP_CARPETA_BASE%/thumbs4" "miniaturas (thumbs4)"
) else (
    echo No se encontro la carpeta de miniaturas: %CARPETA_THUMBS%
    echo Se omite la subida de thumbs4.
)

echo.
pause
exit /b 0

:SubirCarpeta
set "ORIGEN=%~1"
set "DESTINO=%~2"
set "ETIQUETA=%~3"

set /a TOTAL=0
set /a OMITIDOS=0
for %%F in ("%ORIGEN%\*.jpg" "%ORIGEN%\*.jpeg" "%ORIGEN%\*.png" "%ORIGEN%\*.webp" "%ORIGEN%\*.bmp") do (
    set "base=%%~nF"
    if /i "!base:~0,4!"=="TMP_" (
        set /a OMITIDOS+=1
    ) else (
        set /a TOTAL+=1
    )
)

echo --- Subiendo %ETIQUETA% ---
echo Archivos a subir: %TOTAL% (omitidos TMP_: %OMITIDOS%)

if %TOTAL%==0 (
    echo No hay archivos para subir en esta carpeta.
    exit /b 0
)

set /a SUBIDOS=0
set /a ERRORES=0
set /a ACTUAL=0

for %%F in ("%ORIGEN%\*.jpg" "%ORIGEN%\*.jpeg" "%ORIGEN%\*.png" "%ORIGEN%\*.webp" "%ORIGEN%\*.bmp") do (
    set "base=%%~nF"
    if /i "!base:~0,4!"=="TMP_" (
        rem se omite, ya se conto arriba
    ) else (
        set /a ACTUAL+=1
        echo [!ACTUAL!/%TOTAL%] Subiendo %%~nxF ...
        curl -s -S --ftp-create-dirs -T "%%F" "ftp://%FTP_USER%:%FTP_PASS%@%FTP_HOST%/%DESTINO%/%%~nxF"
        if errorlevel 1 (
            echo     ERROR subiendo %%~nxF
            set /a ERRORES+=1
        ) else (
            set /a SUBIDOS+=1
        )
    )
)

echo Subidos correctamente: %SUBIDOS% ^| Con error: %ERRORES%
exit /b 0

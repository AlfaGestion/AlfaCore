@echo off
REM Sube al FTP de AlfaNet las imagenes de una carpeta local que esten
REM nombradas por codigo de articulo (ej: 526.jpg), a la carpeta
REM Clientes/IDCLIENTE/imagenes. Ignora cualquier archivo cuyo nombre
REM empiece con "TMP_" (fotos temporales sin relacionar a un articulo).
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
REM   Carpeta:   C:\Alfa Gestion\imagenes\ImagenesWeb2\
REM   IDCLIENTE: 112010760
REM
REM Doble clic y listo. Si alguna vez queres usarlo para otro cliente o
REM carpeta, le podes pasar los parametros y pisan estos valores:
REM   subir_imagenes_articulos.bat "C:\ruta\a\carpeta\imagenes" IDCLIENTE

setlocal enabledelayedexpansion

set "CARPETA_ORIGEN=C:\Alfa Gestion\imagenes\ImagenesWeb2\"
set "IDCLIENTE=112010760"

if not "%~1"=="" set "CARPETA_ORIGEN=%~1"
if not "%~2"=="" set "IDCLIENTE=%~2"

if "%CARPETA_ORIGEN:~-1%"=="\" set "CARPETA_ORIGEN=%CARPETA_ORIGEN:~0,-1%"

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
set "FTP_CARPETA_DESTINO=Clientes/%IDCLIENTE%/imagenes"

set /a TOTAL=0
set /a OMITIDOS=0

for %%F in ("%CARPETA_ORIGEN%\*.jpg" "%CARPETA_ORIGEN%\*.jpeg" "%CARPETA_ORIGEN%\*.png" "%CARPETA_ORIGEN%\*.webp") do (
    set "base=%%~nF"
    if /i "!base:~0,4!"=="TMP_" (
        set /a OMITIDOS+=1
    ) else (
        set /a TOTAL+=1
    )
)

echo ===============================================
echo Subida de imagenes de articulos
echo Cliente: %IDCLIENTE%
echo Origen : %CARPETA_ORIGEN%
echo Destino: ftp://%FTP_HOST%/%FTP_CARPETA_DESTINO%/
echo Archivos a subir: %TOTAL%
echo Omitidos (empiezan con TMP_): %OMITIDOS%
echo ===============================================
echo.

if %TOTAL%==0 (
    echo No hay archivos para subir despues de excluir los TMP_.
    pause
    exit /b 0
)

set /a SUBIDOS=0
set /a ERRORES=0
set /a ACTUAL=0

for %%F in ("%CARPETA_ORIGEN%\*.jpg" "%CARPETA_ORIGEN%\*.jpeg" "%CARPETA_ORIGEN%\*.png" "%CARPETA_ORIGEN%\*.webp") do (
    set "base=%%~nF"
    if /i "!base:~0,4!"=="TMP_" (
        rem se omite, ya se conto arriba
    ) else (
        set /a ACTUAL+=1
        echo [!ACTUAL!/%TOTAL%] Subiendo %%~nxF ...
        curl -s -S --ftp-create-dirs -T "%%F" "ftp://%FTP_USER%:%FTP_PASS%@%FTP_HOST%/%FTP_CARPETA_DESTINO%/%%~nxF"
        if errorlevel 1 (
            echo     ERROR subiendo %%~nxF
            set /a ERRORES+=1
        ) else (
            set /a SUBIDOS+=1
        )
    )
)

echo.
echo ===============================================
echo Listo.
echo Subidos correctamente: %SUBIDOS%
echo Con error: %ERRORES%
echo Omitidos (TMP_): %OMITIDOS%
echo ===============================================
pause

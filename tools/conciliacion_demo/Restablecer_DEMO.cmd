@echo off
chcp 65001 >nul
python "%~dp0restablecer_demo.py" --restaurar
if errorlevel 1 (
    echo No se completo la restauracion. Revisar el mensaje y la carpeta Logs.
) else (
    echo Demostracion restablecida.
)
pause

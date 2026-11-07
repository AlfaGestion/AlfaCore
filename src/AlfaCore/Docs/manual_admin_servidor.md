# Configuración del servidor de AlfaCore

Ingresá como superadministrador y abrí **Administrar → Servidor**. Esta configuración corresponde a la instalación de AlfaCore, se comparte entre sus bases y se conserva durante las actualizaciones.

1. Indicá la ruta completa del ejecutable, por ejemplo `C:\Python314\python.exe`. Debe existir en el servidor donde funciona AlfaCore y tener instalada la biblioteca `pypdf`.
2. Si usás Recepción documental, podés indicar también su script de compras y carpeta de trabajo comunes. Si los dejás vacíos se conservan los valores anteriores de cada base. Los lectores de extractos y liquidaciones ya están incluidos en AlfaCore.
3. Pulsá **Probar lector**. Se comprueban Python y pypdf con los permisos del servicio que ejecuta AlfaCore. No importa documentos ni modifica asientos.
4. Pulsá **Guardar**. La configuración comienza a utilizarse sin reiniciar AlfaCore ni repetir la operación por cada base.

La ruta común reemplaza la ruta anterior de Recepción documental cuando está informada. Una ruta propia configurada en `TARJETAS-PYTHON` sigue teniendo prioridad para esa base; vacío o `python` heredan la instalación común. No hace falta modificar esa clave en las bases nuevas.

En Recepción documental quedan las opciones de operación de cada base. Los usuarios ya no editan ejecutables ni rutas de los lectores desde allí.

Los cambios quedan auditados. Se conservan localmente en `App_Data/servidor/lectores.json`; no se modifican `appsettings.json` ni `.env`. Si existen varias instalaciones de AlfaCore en distintos servidores, se configura cada una.

## Python para Mis Comprobantes de ARCA

ARCA usa un entorno separado de los lectores documentales: requiere `playwright`, `pywin32`, Chromium y Excel instalado en el servidor. **Probar descarga** comprueba los módulos de Python; no ingresa a ARCA.

Si aparece «No se encuentra python.exe», ejecutá el instalador incluido. Para desarrollo local, desde la raíz del repositorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/arca/instalar_entorno_arca.ps1 -PythonBase C:\Python314\python.exe -Destino C:\dev\AlfaCore\src\AlfaCore\App_Data\scripts\arca
```

Con el campo **Python con playwright y pywin32** vacío, AlfaCore reconoce primero `App_Data\scripts\arca\python\python.exe` (portable) y, si no existe, `App_Data\scripts\arca\venv\Scripts\python.exe` (instalador). Una ruta explícita configurada conserva prioridad. Los entornos se instalan en cada servidor; no se versionan ni se copian desde el entorno de desarrollo durante la publicación.

Si instalaste en otra carpeta, copiá la ruta que muestra el instalador al campo Python de ARCA, pulsá **Guardar ARCA** y después **Probar descarga**. No cambies el Python de lectores documentales para resolver un error de ARCA.

# Configuración del servidor de AlfaCore

Ingresá como superadministrador y abrí **Administrar → Servidor**. Esta configuración corresponde a la instalación de AlfaCore, se comparte entre sus bases y se conserva durante las actualizaciones.

1. Indicá la ruta completa del ejecutable, por ejemplo `C:\Python314\python.exe`. Debe existir en el servidor donde funciona AlfaCore y tener instalada la biblioteca `pypdf`.
2. Si usás Recepción documental, podés indicar también su script de compras y carpeta de trabajo comunes. Si los dejás vacíos se conservan los valores anteriores de cada base. Los lectores de extractos y liquidaciones ya están incluidos en AlfaCore.
3. Tocá [[bi:play-circle]] **Probar lector**, en la barra de arriba. Se comprueban Python y pypdf con los permisos del servicio que ejecuta AlfaCore. No importa documentos ni modifica asientos.
4. Tocá [[bi:save]] **Guardar** (arriba a la izquierda). La configuración comienza a utilizarse sin reiniciar AlfaCore ni repetir la operación por cada base.

La ruta común reemplaza la ruta anterior de Recepción documental cuando está informada. Una ruta propia configurada en `TARJETAS-PYTHON` sigue teniendo prioridad para esa base; vacío o `python` heredan la instalación común. No hace falta modificar esa clave en las bases nuevas.

En Recepción documental quedan las opciones de operación de cada base. Los usuarios ya no editan ejecutables ni rutas de los lectores desde allí.

Los cambios quedan auditados. Se conservan localmente en `App_Data/servidor/lectores.json`; no se modifican `appsettings.json` ni `.env`. Si existen varias instalaciones de AlfaCore en distintos servidores, se configura cada una.

## Python para Mis Comprobantes de ARCA

ARCA usa un entorno separado de los lectores documentales: requiere `playwright`, `pywin32`, Chromium y Excel instalado en el servidor. [[bi:play-circle]] **Probar descarga** comprueba los módulos de Python; no ingresa a ARCA.

Si aparece «No se encuentra python.exe», ejecutá el instalador incluido. Para desarrollo local, desde la raíz del repositorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/arca/instalar_entorno_arca.ps1 -PythonBase C:\Python314\python.exe -Destino C:\dev\AlfaCore\src\AlfaCore\App_Data\scripts\arca
```

Con el campo **Python con playwright y pywin32** vacío, AlfaCore reconoce primero `App_Data\scripts\arca\python\python.exe` (portable) y, si no existe, `App_Data\scripts\arca\venv\Scripts\python.exe` (instalador). Una ruta explícita configurada conserva prioridad. Los entornos se instalan en cada servidor; no se versionan ni se copian desde el entorno de desarrollo durante la publicación.

Si instalaste en otra carpeta, copiá la ruta que muestra el instalador al campo Python de ARCA, tocá [[bi:save]] **Guardar ARCA** y después [[bi:play-circle]] **Probar descarga**. No cambies el Python de lectores documentales para resolver un error de ARCA.

## Liquidaciones de tarjetas en segundo plano

- Publicar la aplicación y ejecutar las actualizaciones hasta **2026-10-31-003** (incluye los límites y el análisis persistente).
- En **Administrar → Bases**, marcar **Incluir** en **Recepción documental · compras y tarjetas** para la base correspondiente. Se reutiliza la selección de bases del worker documental existente. El cliente debe tener **Comprobantes con IA** activo o en prueba vigente en la central.
- Ejecutar los workers en la instancia asignada por Administrar; `ProcesosSegundoPlano:Deshabilitados=true` impide ejecutarlos en esa instalación.
- Configurar Python con pypdf en **Administrar → Servidor** y la conexión de IA de esa instalación. Se reutiliza la configuración existente; no se cambia appsettings.json ni .env automáticamente.
- El usuario que carga y prepara debe estar autorizado en esa base y tener permiso de Conciliación de tarjetas. El worker consulta movimientos con ese usuario y sus unidades; no utiliza un administrador genérico.

`TARJETAS-DOCUMENTAL-HABILITADO` habilita este procesamiento en la base (inicial SI). `TARJETAS-DOCUMENTAL-MINUTOS` limita cada intento (inicial 20, de 1 a 120 minutos). Los límites de agrupación y de IA siguen siendo los de tarjetas. La confirmación final sigue siendo manual; no se generan LT, IVA ni asientos.

Una recepción preparada conserva el PDF con el que se encoló. Para corregir el archivo creá otra recepción: el worker verifica que el adjunto siga siendo el original y no reemplaza silenciosamente un análisis por el de otro documento.

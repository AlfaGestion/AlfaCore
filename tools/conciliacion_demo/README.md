# Lote reproducible de Conciliación y Cobranzas

Guía del presentador: [hoja de ruta](../../src/AlfaCore/Docs/manual_demo_conciliacion_bancaria.md).

## Preparación realizada

**Actualización 26/10/2026:** se creó **ALFANET_DEMO** como copia independiente de ALFANET. Desde ahora las demostraciones se realizan en ALFANET_DEMO; ALFANET conserva sus otros usos. La carpeta vigente es `C:\dev\Demos\Conciliacion\`, con Extractos, Guia, Scripts, Versiones y Logs. La copia conserva información histórica; el paquete de extractos y el lote sí son ficticios. No presentar la base completa como anonimizada.

Se retiraron únicamente de la copia los tokens de pagos/mensajería, credenciales de correo y rutas de certificados fiscales heredados. Dos procedimientos que referían explícitamente a ALFANET fueron redirigidos a ALFANET_DEMO; el publicador de estadísticas externo quedó deshabilitado en la copia. Las referencias comprobadas quedan limitadas a la propia base y a la lectura de `master..spt_values`. Esto no es una auditoría general de todos los módulos o de credenciales configuradas globalmente en el servidor web.

El 25/10/2026 se sembró `DEMO-CONCILIACION-202610` en **SERVER-ALFAVB6 / ALFANET**, conservando la información anterior. No se creó una base vacía ni se anonimizó la base completa. El paquete de archivos generado sí contiene exclusivamente datos ficticios.

Antes de modificar se ejecutó un respaldo `COPY_ONLY` con `CHECKSUM`, validado mediante `RESTORE VERIFYONLY`; el respaldo queda en el directorio de backups del servidor y su ruta está registrada en el archivo administrativo `base/respaldo_previo.txt`. No se distribuye en el ZIP público.

Se verificaron los objetos reales, columnas, claves foráneas, triggers, unidad 1 y ejercicio abierto. La siembra utiliza `MA_CUENTAS`, `MA_CUENTASADIC`, `V_MV_CPTE`, `MV_ASIENTOS`, `MV_APLICACION` y claves concretas de `TA_CONFIGURACION`. No desactiva constraints ni triggers y no borra información.

La validación con rollback se ejecutó antes de confirmar. Después del commit se comprobaron 60 clientes DEMO, 333 comprobantes, 679 renglones, 27 proformas pendientes, cero asientos desbalanceados y cero rellenos indebidos en TC/CUENTA. Se instalaron las tres actualizaciones oficiales idempotentes de criterios, IA y asientos, preservando la configuración existente; no se adelantó manualmente el marcador global de actualizaciones.

## Regenerar archivos ficticios

Requiere Python con openpyxl. Estos comandos no acceden a SQL:

```text
python -m pip install -r tools/conciliacion_demo/requirements.txt
python tools/conciliacion_demo/generar_lotes.py --salida C:/dev/Extractos/DEMO_Conciliacion
python tools/conciliacion_demo/preparar_siembra.py --manifiesto C:/dev/Extractos/DEMO_Conciliacion/manifiesto_demo.json --salida C:/dev/Extractos/DEMO_Conciliacion/base
python tools/conciliacion_demo/documentar_lote.py --salida C:/dev/Extractos/DEMO_Conciliacion
```

El generador utiliza los formatos observados de los tres bancos; no lee sus filas ni copia archivos privados. Produce tres XLSX mensuales de 61 movimientos y tres CSV semanales de 44 filas, con 42 repeticiones exactas y dos novedades por banco. El manifiesto define también comprobantes y aplicaciones coherentes.

`documentar_lote.py` genera el Excel de seguimiento y reconstruye el ZIP público con una lista cerrada: seis extractos, manifiesto ficticio, guion Excel y hoja de ruta. Excluye respaldo, credenciales y archivos administrativos de la carpeta `base`.

## SQL y credenciales

`consultar_base.py` invoca SQLCMD usando una conexión ya guardada del servidor solicitado. La credencial solo se transmite en el entorno del proceso SQLCMD, nunca en argumentos, archivos nuevos ni salidas. No usar una conexión de otra base/servidor por conveniencia.

Los scripts de respaldo, siembra y verificación exigen explícitamente `SERVER-ALFAVB6 / ALFANET`. `validar_siembra.sql` revierte la transacción; `aplicar_siembra.sql` confirma. Ambos se detienen si el lote ya existe o si sus códigos están ocupados. No son herramientas para reiniciar una base ni se deben agregar al actualizador general de todos los clientes.

```text
python tools/conciliacion_demo/consultar_base.py --sql tools/conciliacion_demo/verificar_siembra.sql
```

El archivo `base/resultado_siembra.txt` confirma la carga original y `base/verificacion.json` registra las comprobaciones posteriores. La copia ALFANET_DEMO conserva los datos previos: para una presentación pública sin posibilidad de mostrarlos se necesita un trabajo adicional de anonimización. No restaurar ni limpiar ALFANET.

Las doce reglas de asientos se acotan a las cuentas bancarias DEMO. No se cambian las reglas anteriores. Los extractos no se importan automáticamente: hacerlo es el primer paso de la demostración.

## Creación y punto inicial independiente

Los scripts administrativos siguientes no deben agregarse al actualizador de clientes:

1. `crear_base_demo.sql`, conectado a master: respalda ALFANET con COPY_ONLY/CHECKSUM, verifica y restaura una base nueva con sus propios MDF/LDF. Falla si ALFANET_DEMO o sus archivos ya existen; no contiene REPLACE.
2. `aislar_base_demo.sql`, conectado a ALFANET_DEMO: marca la base y realiza los ajustes de aislamiento mencionados.
3. `crear_respaldo_inicial.sql`, conectado a ALFANET_DEMO: comprueba que no haya extractos DEMO importados y guarda un respaldo versionado más `E:\MSSQL\BACKUP\ALFANET_DEMO.bak`. Ambos se verifican con CHECKSUM. Si el nombre fijo ya existe, se detiene para que un operador archive y revise su versión anterior.

Los archivos físicos de la copia son `E:\MSSQL\DATA\ALFANET_DEMO.mdf` y `E:\MSSQL\DATA\ALFANET_DEMO_log.ldf`. Los nombres lógicos heredados no se utilizan para decidir el destino: la herramienta valida los caminos físicos de la copia.

## Restablecer después de una demostración

Ejecutar `C:\dev\Demos\Conciliacion\Scripts\Restablecer_DEMO.cmd`. Solicita usuario/clave SQL y confirmación escrita **ALFANET_DEMO**. La clave permanece en memoria del proceso y no se guarda en la carpeta. Requiere Python y SQLCMD; para restaurar no necesita openpyxl.

Sin `--restaurar`, `restablecer_demo.py` solo verifica el plan. Antes de restaurar valida:

- servidor SERVER-ALFAVB6 y base destino disponible con su marca DEMO;
- un único respaldo completo, con nombre interno ALFANET_DEMO y checksum;
- dos archivos del respaldo y del destino con caminos propios de ALFANET_DEMO;
- ausencia de uso de esos caminos por otra base;
- RESTORE VERIFYONLY con CHECKSUM.

Solo después de la confirmación cierra conexiones de ALFANET_DEMO, restaura con REPLACE y vuelve a MULTI_USER. No admite parámetros para cambiar base, servidor o archivo .bak. Verifica el lote esperado y cero importaciones DEMO; ante fallos deja registro local y registra el error en AUX_ERR de la demo cuando es posible. Si SQL no está disponible, se conserva el registro local. La aplicación debe reconectarse/recargarse para descartar el estado de sus pantallas.

La prueba real introdujo una importación y una marca temporal solo en ALFANET_DEMO; después de restaurar ambas desaparecieron y se recuperó el lote inicial. ALFANET mantuvo sus cantidades y sus archivos físicos.

## Entregas y correcciones

```text
python tools/conciliacion_demo/organizar_demo.py --destino C:/dev/Demos/Conciliacion --version 2026-10-26-inicial
```

El organizador regenera los formatos para ALFANET_DEMO, copia las guías y herramientas y conserva una entrega inmutable en Versiones. Se detiene si ese nombre ya existe; elegir un nombre distinto para cada corrección. Las versiones del .bak permanecen en la carpeta de respaldos del servidor. Actualizar el material local no modifica por sí mismo el respaldo inicial.

Para renovar el respaldo: restaurar el punto inicial, aplicar actualizaciones/correcciones, validar, archivar el `.bak` fijo anterior en el servidor, ejecutar el creador del respaldo y guardar una nueva entrega del material. No generar un punto inicial desde una demostración a medio terminar.

Las credenciales de operación pueden suministrarse mediante `ALFACORE_DEMO_SQL_USER` y `ALFACORE_DEMO_SQL_PASSWORD`. Las herramientas ejecutadas desde el repositorio también admiten la conexión guardada existente del servidor. Nunca guardar credenciales dentro de Scripts o Versiones.

## Validación automatizada

`ConciliacionDemoTests` genera el lote en una carpeta temporal y lo procesa con el lector real de AlfaCore: valida detección de los tres bancos, 61/44 filas, 42 repeticiones y dos novedades, propiedades de autor ficticias, ausencia de comentarios y coherencia entre recibos/aplicaciones. Requiere Python y openpyxl, igual que el generador. La verificación SQL adicional comprueba saldos mediante `VE_CPTES_SALDOS_VENTAS` dentro de la misma transacción de la siembra.

`python -m unittest discover -s tools/conciliacion_demo -p test_*.py` verifica las guardas de nombre interno del respaldo, checksum, distribución de archivos, servidor y SQL de destino, usando respuestas controladas. Estas pruebas no restauran bases.

# Actualizaciones: colisión de versiones tras integración — 2026-10-15

## Incidencia

El usuario informó un error sin detalle visible y un panel que permanecía en «Cargando» en `/ALFANET/106/actualizaciones`.

La integración del remoto incorporó tres scripts de Conversaciones con versiones 2026-10-15-001, 002 y 003, ya utilizadas localmente por tres scripts de Ventas. `GetScripts` exige unicidad global de fecha/correlativo y rechaza la lista antes de devolver el dashboard. Es un conflicto de los archivos distribuidos, independiente de las tablas comerciales de una base concreta. La verificación anterior no incluyó el test existente de unicidad de actualizaciones; se incorpora expresamente al cierre de esta corrección.

## Corrección

Se conservan las versiones de Conversaciones recibidas del remoto (004/005/006). Los scripts de Ventas mantienen su SQL y quedan con los siguientes nombres vigentes:

| Anterior | Nuevo |
|---|---|
| 2026-10-15-001 — ventas_carga_directa_operaciones | 2026-10-15-007 |
| 2026-10-15-002 — ventas_tipos_carga_directa | 2026-10-15-008 |
| 2026-10-15-003 — ventas_reporte_ventas_menu_web | 2026-10-15-009 |

Son scripts idempotentes; no se reescribe historial ni FECHAUPDATE_CORE, no se marca una migración como aplicada y no se elimina ninguna protección del actualizador. Se actualizaron referencias documentales al nombre vigente.

Para publicaciones incrementales que conserven el archivo antiguo junto al nuevo, se reconoce únicamente cada pareja explícita anterior/nueva y se omite la copia vieja si el contenido SQL coincide (normalizando fin de línea y blancos exteriores). Si cambia el contenido o se trata de cualquier otro duplicado, la ejecución sigue bloqueada. El error identifica versiones/archivos y pasa por `IAppEventService` para AUX_ERR, conservando detalle y código para el operador.

La pantalla muestra un estado de error persistente con detalle, código y Reintentar. Al cerrar el aviso ya no recrea una notificación vacía. La prueba de recuperación verifica que Recargar vuelve a mostrar el dashboard.

`DatabaseUpdatesHostedService` ahora respeta la bandera de infraestructura ya existente `ProcesosSegundoPlano:Deshabilitados`, igual que los demás procesos. El comportamiento normal no cambia; el preview de diagnóstico se inicia con esa bandera activa para evitar aplicar pendientes al arrancar.

## Verificación y límites

- Compilación final de aplicación y tests correcta, sin errores; tres advertencias existentes ajenas a esta corrección.
- **6 pruebas aprobadas, 0 omitidas y 0 fallidas**: unicidad de todas las versiones del repositorio, compatibilidad SQL 2008, alias idéntico admitido, alias distinto rechazado, duplicado real con registro central, error persistente con detalle/código, recuperación y bloqueo de ejecución tras un fallo de recarga.
- No se ejecutaron pruebas de emisión ni migraciones SQL sobre la base 106. La prueba del logger utiliza el contrato central simulado y verifica una sola llamada.
- Preview reiniciado en localhost:5055, proceso lanzador 15044, con `ProcesosSegundoPlano:Deshabilitados=true`; el hosted service de actualizaciones no se registra en ese modo. La ruta `/ALFANET/106/actualizaciones` respondió HTTP 200; es disponibilidad HTTP y no validación autenticada del panel.
- `python tools/catalogo/check_catalogo.py`: **99 rutinas, 0 advertencias, 0 errores**. `git diff --check`, referencias a los nuevos nombres y UTF-8 sin BOM/mojibake revisados.

Las pruebas de duplicados, logging y pantalla usan archivos temporales/servicios simulados; no ejecutan SQL de migración ni escriben en la base 106. La conexión directa de diagnóstico a esa base no pudo completarse desde esta sesión. Queda pendiente la comprobación del panel con la sesión autenticada del usuario. No se afirma haber revisado su historial ni aplicado sus actualizaciones.

Para futuras integraciones, incluir `ActualizacionesScriptsTests` en la regresión, además de los tests del módulo modificado.


## Integración del remoto antes del push

El commit remoto `904180a` también corrigió la colisión, moviendo los tres scripts de Conversaciones a 004/005/006. Al integrar ambas soluciones, el siguiente bloque libre de Ventas pasa a **007/008/009**. La corrección local inicial había usado 004/005/006; la tabla anterior ya refleja la numeración final integrada. Los alias de Ventas de 001/002/003 y de la corrección intermedia 004/005/006 apuntan a 007/008/009, y se siguen omitiendo únicamente al comprobar igualdad de SQL con la copia vigente. No se cambia ningún contenido de migración ni el historial de las bases.

Verificación final de integración: compilación correcta, **8 pruebas aprobadas, sin omitidas ni fallas**, incluyendo alias inicial e intermedio tanto idéntico como distinto y unicidad global de scripts. Catálogo: 99 rutinas sin errores.

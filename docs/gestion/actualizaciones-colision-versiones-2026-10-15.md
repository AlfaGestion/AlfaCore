# Actualizaciones: colisión de versiones tras integración — 2026-10-15

## Incidencia

El usuario informó un error sin detalle visible y un panel que permanecía en «Cargando» en `/ALFANET/106/actualizaciones`.

La integración del remoto incorporó tres scripts de Conversaciones con versiones 2026-10-15-001, 002 y 003, ya utilizadas localmente por tres scripts de Ventas. `GetScripts` exige unicidad global de fecha/correlativo y rechaza la lista antes de devolver el dashboard. Es un conflicto de los archivos distribuidos, independiente de las tablas comerciales de una base concreta. La verificación anterior no incluyó el test existente de unicidad de actualizaciones; se incorpora expresamente al cierre de esta corrección.

## Corrección

Se conservan las versiones de Conversaciones. Los scripts de Ventas mantienen su SQL y cambian de nombre:

| Anterior | Nuevo |
|---|---|
| 2026-10-15-001 — ventas_carga_directa_operaciones | 2026-10-15-004 |
| 2026-10-15-002 — ventas_tipos_carga_directa | 2026-10-15-005 |
| 2026-10-15-003 — ventas_reporte_ventas_menu_web | 2026-10-15-006 |

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

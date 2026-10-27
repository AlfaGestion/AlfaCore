# Reporte de Ventas ausente del menú — 2026-10-16

## Diagnóstico confirmado

El usuario confirmó la base ALFANET/106. La opción D60RPTWEB estaba instalada, habilitada y vinculada a /ventas/reportes, pero ALFACORE_MENU_WEB.PadreClave era NULL. El campo Titulo de TA_MENU sí indicaba D60: ese dato legacy no lo usa MenuService para construir la jerarquía web. Alberto tenía permiso explícito en ALFACORE_TAREAS_WEB. Se comprobó el mismo defecto en DISTRIWALTERP (ALFANET/153).

MenuService carga la navegación desde SQL y BuildSections exige que la opción sea descendiente de D60. Reiniciar el proceso no completa un vínculo ausente; una recarga del navegador vuelve a consultar el menú corregido.

## Corrección

- El instalador 2026-10-15-009__ventas_reporte_ventas_menu_web.sql establece ahora PadreClave=D60 cuando la columna está disponible.
- Nueva actualización idempotente 2026-10-16-001__ventas_reporte_ventas_padre_menu_web.sql para instalaciones anteriores. Solo corrige el padre de la fila ALFA/D60RPTWEB si corresponde al componente/ruta de Reporte de Ventas y existe el módulo D60.
- Aplicada esa reparación acotada, dentro de transacción y con verificación de resultado, a las bases 153 y 106. En ambas quedó PadreClave=D60.
- No se modificaron TA_MENU, permisos, datos comerciales ni parámetros. No se ejecutó el conjunto de migraciones pendientes ni se adelantó FECHAUPDATE_CORE; la nueva actualización puede aparecer pendiente y ejecutarse nuevamente sin duplicar datos.

## Verificación

9 pruebas aprobadas, sin omitidas ni fallas: test SQL de jerarquía/idempotencia y tests de unicidad, compatibilidad y manejo de errores del actualizador. El test reproduce la invisibilidad con padre NULL, ejecuta dos veces la reparación, comprueba que BuildSections incluye una sola opción bajo D60 y que TA_MENU queda igual; todo el ensayo se revierte.

Queda pendiente que el usuario recargue el navegador y confirme la presentación con su sesión. No se necesita reiniciar AlfaCore para el cambio de datos del menú.

Compilación final de aplicación correcta, sin errores ni advertencias, tras detener la instancia local que bloqueaba la copia del ejecutable. Preview reiniciado con procesos de fondo deshabilitados. No fue necesario cambiar código del menú ni permisos: la causa funcional era el padre faltante en SQL.

# Dashboard de ventas: vendedores y unidades de negocio

Las pestañas Vendedores (`/ventas/vendedores`) y U. de negocio (`/ventas/unidades-negocio`) amplían el dashboard existente. Comparten permisos, shell AlfaDesign y filtros de Ventas; no agregan módulos ni claves al menú legacy, por lo que no requieren altas en `ALFACORE_MENU_WEB` o `TA_MENU`.

`GestionDashboardService.AnalisisVentas.cs` consulta mediante Dapper la vista oficial `Libro_VentasConFP`. Usa los importes con su signo y el `IdVendedor` histórico del movimiento, no el vendedor actual del cliente. El esquema se contrastó con la definición disponible en `docs/legacy/sql-dumps/COMPLETO_042026.sql`; validar también en la base de destino antes del despliegue.

Los filtros de período, cliente, usuario, unidades y tipo se aplican al libro. Depósito usa existencia de cabecera en `V_MV_Cpte`, sin multiplicar importes. Las unidades se describen desde `V_TA_UnidadNegocio`, agrupando el catálogo por código normalizado. Los códigos vacíos se muestran como Sin vendedor/Sin unidad de negocio y admiten drilldown. El porcentaje se calcula sobre el total neto del período; si es cero se muestra un guion.

La comparación gráfica muestra hasta 15 participantes; la tabla incluye todos. Ver detalle abre la evolución mensual y comprobantes paginados de a 50, hasta el visor existente. Ranking, evolución y documentos provienen de una misma tabla temporal filtrada. No se recalculan signos ni se graban movimientos. Se conserva `directo=1` y el retorno desde el visor incluye selección y página.

Los errores de consulta pasan por el servicio central de eventos (`AUX_ERR`). La pantalla conserva filtros y datos en memoria ante fallos y ofrece reintento. Pruebas de UI cubren ambas dimensiones, selección sin asignar, paginación, filtros y navegación directa. Falta la validación visual y SQL en una base real.

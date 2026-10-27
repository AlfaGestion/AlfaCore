# Filtros y selección de documentos pendientes

Actualización: 2026-10-14. Ruta indicada por el usuario para la base: `/ALFANET/153/ventas/carga-comprobantes`.

## Corrección de disponibilidad

El listado inicial filtraba NP/RM por estado de cabecera (abierta, no anulada, no bloqueada, no cerrada y sin comodato), pero no exigía saldo. Por eso podía mostrar documentos totalmente aplicados cuyo indicador `FINALIZADA` seguía en cero.

Ahora listado, recuento paginado y carga directa requieren una fila con `SALDO>0` en la vista vigente `VT_V_MV_INSUMOS_SALDOSRM`, vinculada por TC y comprobante. Se conserva la validación del pendiente de cada artículo al simular; la búsqueda no reserva cantidades. No se modifican estados históricos ni aplicaciones para corregir la presentación.

En DISTRIWALTERP se detectaron 2.148 cabeceras NP que cumplían los estados anteriores, fechadas en 2024. De ellas, 2.145 tienen saldo positivo en la vista y tres no lo tienen. Los tres documentos sin saldo se excluyen. No se encontraron cabeceras RM normales que cumplieran esos estados en este diagnóstico. Esta evidencia no prueba el estado contable de todos los comprobantes ni convierte una factura parcialmente aplicada en una totalmente aplicada.

El módulo consulta únicamente orígenes NP/RM; FC no se incorpora al listado. Un origen con una aplicación parcial continúa disponible cuando la vista conserva cantidad pendiente. La antigüedad de la fecha no permite clasificarlo como facturado.

## Filtros incorporados

| Filtro | Fuente y comportamiento |
|---|---|
| Origen | NP o RM; conserva el circuito individual implementado |
| Cliente/comprobante | Búsqueda literal por nombre, cuenta o número, escapando caracteres de LIKE |
| Fecha desde/hasta | `V_MV_CPTE.FECHA`; ambas opcionales, hasta incluye todo el día mediante límite superior exclusivo |
| Vendedor | `V_MV_CPTE.IdVendedor`, histórico del documento; opciones desde `V_TA_VENDEDORES` |
| Zona | Zona actual de la cuenta en la vista oficial `VT_CLIENTES`; opciones distintas de su campo `ZONA`, sin inventar una tabla de zonas |
| Unidad de negocio | `V_MV_CPTE.UNEGOCIO`; opciones de `V_TA_UnidadNegocio`, respetando el alcance del usuario |
| Depósito | `V_MV_CPTE.IdDeposito`, depósito de cabecera; opciones de `V_TA_Deposito`. No es una búsqueda por todos los depósitos posibles del detalle |

Se confirmaron objetos y columnas antes de usarlos. Los PK de texto se comparan con trim; esta entrega no escribe códigos de maestros ni configuraciones. Los filtros no amplían los permisos de unidad de negocio establecidos por el servicio.

**Buscar** o Enter aplica una copia de los criterios y vuelve a la página uno. Cambiar campos sin aplicar no altera la consulta de la siguiente página: la paginación conserva los criterios aplicados. **Limpiar filtros** vacía las restricciones adicionales, conserva el tipo de origen y vuelve a consultar. Un cambio de sesión limpia los criterios y sus listas para evitar reutilizar selecciones de otra base.

No se impone una fecha inicial que oculte el historial. Sin fechas se muestran todos los pendientes admitidos; la pantalla lo indica. Se rechazan rangos invertidos y fechas fuera del rango de los campos datetime de SQL Server.

## Archivos y validación

- `Models/CargaComprobantesModels.cs`: criterios de búsqueda y opciones de filtros en el contexto.
- `Services/CargaComprobantesRepository.cs`: saldo obligatorio y predicados SQL parametrizados comunes a listado/recuento; catálogos de filtros.
- `Components/Pages/VentasCargaComprobantes.razor`: campos, aplicar/limpiar, copia de filtros aplicados y limpieza al cambiar sesión.
- `Components/Pages/VentasCargaComprobantes.razor.css`: distribución adaptable del panel de filtros.
- `tests/AlfaCore.Tests/CargaComprobantesFiltrosTests.cs`: SQL real, exclusión de documentos sin saldo por listado/carga directa, fecha inclusiva y combinación de vendedor/zona/unidad/depósito.
- `tests/AlfaCore.Tests/CargaComprobantesUiTests.cs`: handlers de aplicación/limpieza/paginación, conservación de carga y layout en Chromium.
- `src/AlfaCore/Docs/manual_carga_comprobantes.md`: instrucciones funcionales y ruta por empresa/base.

La suite del módulo pasó 21 pruebas, sin omisiones, con SQL y Chromium. La comparación fiscal histórica sigue independiente de esta corrección: sus diferencias no se consideran resueltas al filtrar los orígenes. Catálogo sin errores ni advertencias y fuentes UTF-8 sin BOM.

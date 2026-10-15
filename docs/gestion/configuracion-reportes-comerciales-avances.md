# Configuración y reportes comerciales — 2026-10-15

Se dejó en espera el desarrollo funcional de Carga de Comprobantes para atender esta pasada.

## Implementado

- Barra Smart Search de Configuración en Context Toolbar, búsqueda sin acentos, filtro por área y accesos puntuales a secciones de Ventas. Búsqueda de campos en Configuración → Ventas → Carga de comprobantes.
- Reparación de ReporteCompras frente al esquema real: columnas de libro, cuenta corriente, signos, vista de proveedores, sesión activa y logging central.
- Reporte de Ventas con Resumen, Detalle y Cuenta corriente, filtros, paginación, visor, Excel completo y PDF visible. Panel y servicio compartidos con Compras.
- Opción D60RPTWEB bajo Ventas y actualización idempotente. Aplicada dos veces en DISTRIWALTERP: una fila TA_MENU y un mapeo web; sin duplicados. Se agregaron solo permisos requeridos por la actualización, sin escribir movimientos comerciales ni configuraciones.

## Evidencia y cierre

- Compilación de aplicación y tests: correcta, sin errores; tres advertencias existentes ajenas al cambio.
- Verificación final: **24 pruebas aprobadas, 0 omitidas, 0 fallidas**, filtro `ReportesComercialesTests|ConfiguracionCatalogoTests|CargaComprobantesConfiguracionTests`, con SQL de DISTRIWALTERP (22 s).
- Comparación independiente de cantidades, importes e IVA contra libros; filtro por cuenta, paginación, NC no vacías con total/IVA negativos y cuenta corriente limitada a cuentas oficiales de proveedores/clientes. Sumas de cuenta corriente coinciden con consulta independiente de MV_ASIENTOS.
- Archivos Excel de resumen, detalle y cuenta corriente abiertos con ClosedXML y encabezados verificados para ambos circuitos. El render genera las tres vistas desde acciones del header. Con `directo=1` no ofrece enlace al visor de otro módulo.
- Chrome/Playwright revisó el HTML renderizado de la cuenta corriente de ambos reportes en **2048/1440/1024**: indicador visible y sin desborde global. No equivale a una sesión de aceptación interactiva del operador.
- Configuración: búsqueda desde toolbar por MaxDto, destino correcto, exclusión de opciones no coincidentes y filtro por área; catálogo de rutas, palabras sin acentos y editor compartido sin regresiones.
- Script final de menú reejecutado dos veces dentro de transacción revertida: mapeo único; la instalación efectiva anterior quedó conservada.
- Preview reiniciado en localhost:5055, proceso lanzador **28140**; las rutas SaaS de Configuración, Compras/Reportes y Ventas/Reportes respondieron HTTP 200. La disponibilidad HTTP no prueba login ni sustituye aceptación del usuario.
- Catálogo: **98 rutinas, sin advertencias ni errores**. Revisados diff, UTF-8 sin BOM, mojibake y enlaces de los documentos de esta pasada.

No se emitieron comprobantes, no se modificaron movimientos comerciales ni se dejaron cambios de configuración en la base.

## Pendientes

- Aceptación del operador con sus rangos y cuentas habituales; verificar impresión PDF en su navegador.
- Si se requiere un estado de deuda pendiente con apertura/aplicaciones, implementarlo como alcance explícito; esta cuenta corriente muestra movimientos y neto del período, como se indica en pantalla.
- Conversión o separación multimoneda y certificación de todos los históricos no forman parte de esta pasada.
- Se conservan los pendientes de Carga de Comprobantes del [registro existente](ventas-carga-comprobantes-avances-y-pendientes.md).

[Detalle técnico](../modulos/reportes-comerciales.md) · [Manual](../../src/AlfaCore/Docs/manual_reportes_comerciales.md).

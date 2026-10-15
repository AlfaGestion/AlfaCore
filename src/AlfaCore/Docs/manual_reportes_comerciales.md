# Reportes de Compras y Ventas

Entrá en **Compras → Reportes** o **Ventas → Reporte de Ventas**. Ambas pantallas tienen la misma forma de trabajo y consultan la base activa.

## Buscar y generar

Usá la barra superior para buscar por nombre o cuenta. Abrí el embudo para seleccionar período y tipo de comprobante. Hay rangos habituales y fechas desde/hasta. Presioná **Generar reporte** o Enter para aplicar los filtros.

- **Resumen**: importes del período agrupados por cuenta, neto, IVA, total y participación.
- **Detalle**: comprobantes con fecha, cuenta, motivo, neto, IVA, total y usuario. El botón del ojo abre el visor conservando la base; no se ofrece al entrar en modo de módulo aislado (`directo=1`).
- **Cuenta corriente**: movimientos contables de proveedores en Compras y de clientes en Ventas. En ventas, Debe suma deuda y Haber la resta; en compras ocurre al revés.

El indicador de cuenta corriente es el **movimiento neto del período filtrado**, sin saldo anterior al período ni cálculo de deuda pendiente por aplicación. Un valor positivo es a cobrar en Ventas o a pagar en Compras.

Los informes incluyen proformas según las vistas del sistema. El IVA corresponde a importes registrados fiscalmente; una proforma no genera libro IVA. Los totales pueden incluir impuestos distintos del IVA. Los importes son los registrados en la fuente; este informe no convierte monedas.

## Páginas y archivos

Para Detalle y Cuenta corriente, usá las flechas superiores y seleccioná 25, 50 o 100 registros por página en el pie. La paginación y Excel usan los filtros del último reporte generado. Cambiar un campo del buscador requiere generar nuevamente para aplicarlo.

**Excel** descarga todos los registros del filtro aplicado, incluyendo Cuenta corriente. **PDF** abre la impresión de los resultados visibles; en una vista paginada imprime la página actual. No crea comprobantes ni cambia movimientos.

Si cambiás de base o cerrás sesión durante una consulta, el resultado se descarta. Volvé a generar en la base activa. Si aparece un error con código, conservalo para soporte.

## Búsqueda de opciones de Configuración

Entrá en **Configuración** y escribí en la barra superior. Podés buscar por nombre, descripción, módulo o palabra clave, sin distinguir mayúsculas ni acentos. El embudo permite limitar por área; Limpiar quita texto y área.

Hay accesos a Carga de comprobantes, Punto de Venta, Facturación electrónica y Padrón ARCA. Podés buscar una clave como **MaxDto** para localizar su sección. Dentro de Configuración → Ventas → Carga de comprobantes también hay búsqueda de campos por nombre, clave, grupo o alcance; filtrar no borra cambios ni guarda parámetros.

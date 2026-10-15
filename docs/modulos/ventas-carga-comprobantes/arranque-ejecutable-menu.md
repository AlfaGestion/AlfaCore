# Ejecutable instalado y formas de iniciar Ventas

Relevamiento: 2026-10-14. Se accedió exclusivamente en lectura a la instalación legacy y sus fuentes. No se ejecutaron los binarios ni scripts SQL del menú. La consulta a `TA_MENU` de DISTRIWALTERP también fue de lectura.

## Identidad de los binarios

| Referencia | Versión | Última modificación | Tamaño |
|---|---|---|---|
| `NMC_CONT\v_mv_ventas.exe` indicado por el usuario | 2.02.0409 | 2026-10-09 20:26:21 | 13.037.568 bytes |
| `NMC_CONT_DEV\V_MV_VENTAS.EXE` | 2.02.0412 | 2026-10-13 17:49:48 | 13.041.664 bytes |
| `NMC_CONT_DEV\V_MV_VENTAS.vbp` | revisión declarada 413 | — | — |

El proyecto tiene `AutoIncrementVer=1`: la revisión declarada puede representar la siguiente compilación. Por eso 413 en el proyecto y 412 en el EXE de desarrollo no prueban una divergencia de lógica. El EXE instalado 409 y el de desarrollo 412 son archivos diferentes, con hashes distintos. No se certificó correspondencia exacta de ninguno con los fuentes actuales ni la versión que generó cada factura histórica.

Se registraron metadatos y huellas en [ejecutables-vb6.json](ejecutables-vb6.json). Las diferencias de un centavo del cálculo retrospectivo siguen pendientes; la diferencia de versiones es una hipótesis a investigar, no una explicación demostrada.

## Fuentes del menú

- Instalación: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT\SQL\Menu.Sql` → [llamadas del script instalado](llamadas-menu-vb6.json).
- Desarrollo: `Z:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT_DEV\Menu.Sql` → [llamadas del script de desarrollo](llamadas-menu-vb6-dev.json).
- Menú efectivo en la base de pruebas: `dbo.TA_MENU` → [llamadas de DISTRIWALTERP](llamadas-menu-distriwalterp.json).

Los scripts contienen 26 INSERT de opciones que llaman a la familia `V_MV_VENTAS`; 22 corresponden al EXE principal y cuatro a variantes de punto de venta/gráfico/reparto. La base contiene 29 registros de esa familia, 25 del EXE principal; 26 registros tienen `HABILITADO=1` y tres están deshabilitados. Las entradas generales/electrónicas se verificaron habilitadas. Los permisos por usuario pueden restringir adicionalmente lo que muestra el menú. No se copiaron argumentos de conexión ni el campo PROCESO completo.

El archivo de referencia tiene `D6001` como carga general. En DISTRIWALTERP esa entrada se desdobla en `D600101` y `D600102`; además aparecen `D4025` y `D4026` para envíos de repuestos/mantenimiento. El script por sí solo no identifica el menú usado por un cliente: la referencia operativa es su `TA_MENU` activo.

## Entradas relevantes

| Clave | Parámetro adicional | Comportamiento localizado en fuentes | Aplicación al producto |
|---|---|---|---|
| D600101 (base) / D6001 (script) | vacío | Carga general, TC habilitados por contexto/configuración | Editor común con capacidades explícitas |
| D600102 (base) | FacturaElectronica | Indicador de modo electrónico; interviene en selección de opciones | Primera comparación de factura electrónica desde NP/RM |
| D6A98 | FC | Fija Factura en el selector, sin cambio de TC | Factura como destino explícito |
| D6A99 | FP | Fija Proforma | Circuito posterior; no equivalente a FC |
| D6A95 / D6A94 | NC / ND | Fija nota de crédito/débito | Circuitos posteriores con signos y efectos propios |
| D6002 | SoloNotasPedido | Activa modo de notas de pedido | Carga de NP, distinta de facturación de NP existente |
| D6A55 / D8055 | SOLOREMITOS | Activa modo de remitos | Carga de RM, distinta de factura desde RM |
| D6A58 | RMPR | Ofrece remitos/presupuestos | Capacidad posterior del editor |
| D6096 | PR | Fija Presupuesto | Circuito posterior |
| D6036 / D6038 / D8073 / D989009 | ModificacionPedidos / ModificacionPresupuestos / ModificacionRemitos / ModificacionFacturas | Activa banderas de modificación específicas | Acciones separadas; no reutilizar alta sin reversión completa |
| D6090 / D6091 | EnComodato / DevolucionEnComodato | Comodatos y devoluciones | Fuera del primer circuito de factura individual |
| D8040 / D8045 / D6037 | SoloNotasPedidoCD / RemitoTransferencia / REMITOTR | Reposición, transferencias y transporte | No habilitar como NP/RM simples |
| D8710 / D8712 | Variante V_MV_VENTAS_SOLOREPARTO | Otro ejecutable | No certificarlo a partir del EXE principal |

`Option Compare Text` está declarado en `V_mv_vta.frm:3867` y `ModGlobal.bas:29`. Los parámetros de modo se comparan sin distinguir mayúsculas/minúsculas; `SOLOREMITOS` del menú coincide con `SoloRemitos` del formulario. La lectura de las banderas está en `V_mv_vta.frm:5602-5658`; la selección restringida de TC en `6188-6232`.

## Facturar un origen existente

La consulta `FrmFCRmPendientes.frm:521-556` construye un contexto que incluye TC de origen, sucursal, número, letra, cliente y modo fiscal/electrónico. Para electrónica agrega `FacturaElectronica`. Para proforma y ticket usa prefijos `*` y `#` sobre el TC de origen.

El formulario principal interpreta esos prefijos en `V_mv_vta.frm:5679-5688`. En `5691-5698` exige el conjunto de datos del origen para activar `EsFacturacionDeRemitosPendientes`; admite NP, RM, PR, OT y FP en ese camino. Esto no implica que todos estén certificados por el módulo web.

La consulta no ejecuta un segundo EXE en el camino revisado: establece `FrmMvVentas.pLlamadaInterna=True`, entrega `gLineaDeComando` y muestra el formulario. Por eso abrir el EXE solamente con `@ParametroAdicional=FC` no reproduce todo el contexto de una factura desde pedido/remito.

Para AlfaCore se conservará esta separación con datos tipados: destino FC, modo de emisión, identidad del origen y contexto de usuario/unidad de negocio. Los argumentos legacy de conexión no se trasladan a URLs; se reutiliza la sesión autorizada de AlfaCore.

## Siguiente comparación

1. Tomar como referencia operativa el EXE instalado 409 y el menú efectivo de DISTRIWALTERP.
2. Preparar estados iniciales equivalentes para NP simple → FC y RM ya descargado → FC, total y parcial. El remito requiere una muestra creada específicamente, dado que el relevamiento previo no encontró un RM normal pendiente apto en la base.
3. Entrar por el camino de pendientes con su contexto completo, comparando además carga general y entrada electrónica cuando cambien la lógica.
4. Capturar cálculo antes de grabación y efectos locales después: cabecera, detalle, origen, stock, asientos, aplicación y auditoría.
5. Determinar cuál de las diferencias históricas proviene del cálculo, configuración, intervención manual o versión. No ajustar fórmulas por aproximación estadística.

Todavía no se ejecutó la comparación funcional contra el EXE. Ejecutarlo directamente desde la carpeta compartida puede escribir archivos auxiliares allí; la autorización de esta carpeta fue solo de lectura. El ensayo deberá aislar también los archivos auxiliares y resolver el ambiente electrónico de prueba antes de ejecutar ese camino.

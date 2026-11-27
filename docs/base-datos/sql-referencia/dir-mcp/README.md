# MCP de consulta: donde esta cada parte de la base

Capa semantica para consultar la base en lenguaje natural desde Claude / ChatGPT (proyecto `AlfaCore.Mcp`).
Desde el 2026-11-27 sigue el mismo circuito que el resto de AlfaCore:

| Que | Donde | Como se aplica |
|---|---|---|
| Esquema `dir`, sus 10 vistas, `IA_LOG_CONSULTAS` y claves `IA_MCP_*` (antes 001-004, 012, 013, 015) | `src/AlfaCore/App_Data/updates/2026-11-27-040__ia_mcp_esquema_dir.sql` | **Solo**, en cada base, como cualquier actualizacion de AlfaCore. Si una vista no se puede crear (base vieja), se saltea sin frenar la actualizacion y el motivo queda en la clave `IA_MCP_ESQUEMA_ERRORES` |
| Tablas `MCP_*` de `ALFA_CENTRAL` (antes 010 y 014) | `docs/base-datos/sql-referencia/2026-11-27-041__alfa_central_mcp_oauth.sql` y `-042__alfa_central_mcp_multibase.sql` | A mano, como los demas scripts `alfa_central` |
| Logins de solo lectura (llevan contrasena y permisos de servidor) | esta carpeta: `005_login_solo_lectura.sql` (por base de datos) y `011_oauth_login_almacen.sql` (`ALFA_CENTRAL`) | A mano, una vez por servidor / base |
| Verificacion | esta carpeta: `099_verificacion.sql` | A mano, antes de invitar a nadie |

**Cambiar o agregar una vista**: un script NUEVO en `App_Data/updates/` (mismo patron: `BEGIN TRY` + `EXEC(N'CREATE VIEW ...')`
+ registro en `#mcp_errores` / `IA_MCP_ESQUEMA_ERRORES`), nunca editar el `040` ya publicado. Compatibilidad 80/100.

```
sqlcmd -S <servidor> -f i:65001 -i 005_login_solo_lectura.sql -v MCP_DB="<base>" MCP_LOGIN="mcp_dir_ro" MCP_PASSWORD="<contrasena fuerte>"
sqlcmd -S <servidor> -d <base> -f i:65001 -i 099_verificacion.sql -v MCP_LOGIN="mcp_dir_ro"
```

La contrasena **no esta en ningun script**: se pasa por `-v` y despues va a la variable de entorno / user-secrets del MCP.

## Reglas de negocio que resuelven las vistas (verificadas en ALFANET, 2026-11-25)

- **Venta** = comprobante con `V_TA_Cpte.SISTEMA='VENTAS'` y debe/haber, con lineas de articulos, no anulado, y que figure
  en el libro de IVA (`MV_ASIENTOS.LIVA_TIPO='VENTAS'`) o sea proforma (`IA_MCP_TC_PROFORMA`, hoy `FP,NCFP`).
  Quedan afuera cobranzas (`CB`, `CBCT`, `CBFP`), remitos, pedidos y presupuestos.
- **Signo**: `V_TA_Cpte.DEBEHABER='H'` resta (notas de credito). No hay tipos de comprobante hardcodeados.
- **Neto sin IVA de la linea** = `CANTIDAD * IMPORTE_S_IVA * (1 - PorcDto/100)`. Cuadra al centavo con la cabecera y con
  `Libro_VentasConFP` (octubre 2026: FC 210.885.203,59; FP 33.032.343,39; NC -365.522,51; NCFP -101.744,27).
- **Costo**: `V_MV_CpteInsumos.COSTO` es un snapshot del momento de la venta (difiere del costo actual en el 96 % de las
  lineas de hace un ano). Margen historico = neto - cantidad * costo registrado. `SinCosto=1` marca las pocas lineas sin costo.
- **Proformas**: FC y FP son independientes (ninguna FC nace de una FP), no se duplican. `EsProforma` permite separarlas.
- **Cuenta corriente**: fuente oficial `VE_CPTES_SALDOS_VENTAS` (creditos pendientes con signo negativo).
- **Stock**: `V_MV_Stock.Cantidad` (con signo, unidad base). `CantidadUD` NO sirve para stock. `PuntoPedido` es por articulo:
  para stock critico sumar `Stock` de todos los depositos.

- **Compra** = comprobante del libro de IVA compras (`MV_ASIENTOS.LIVA_TIPO='COMPRAS'`, una fila por comprobante, importes `LIVA_*`
  ya con signo) + proformas de compra de la cabecera (`IA_MCP_TC_PROFORMA_COMPRAS`, hoy `FPC,NCCP`). Cuadra mes a mes al centavo
  con `Libro_ComprasConFP` (2026). La cabecera `C_MV_Cpte` NO sirve como fuente fiscal: su neto difiere del libro en algunos meses
  y 280 de 1998 comprobantes de 2026 no tienen cabecera (liquidaciones de tarjeta `LT`, gastos bancarios `GB`).
- **Neto de linea de compra** = `CANTIDAD * IMPORTE_S_IVA * (1 - PorcDto/100)`; cuadra con `C_MV_Cpte.ImporteInsumos`.
- **Saldos con proveedores**: `CO_CPTES_SALDOS` (facturas) + `CO_CPTES_CREDITOS_PENDIENTES` (pagos a cuenta y NC sin aplicar,
  en negativo). En la base de pruebas los creditos suman 46,8 M: sin restarlos la deuda queda inflada.

## Trampas conocidas

- **Fechas**: en estas bases el literal `'2026-10-01'` se interpreta como a-d-m (10 de enero). Usar siempre parametros tipados
  (`DateTime`) o `yyyymmdd`. El MCP nunca debe concatenar fechas en el SQL.
- Claves de texto con relleno (`CodigoPk`): las vistas exponen codigos ya recortados; no volver a unir contra tablas base por texto recortado.
- Tiempos medidos sobre ALFANET: ventas de un mes ~2 s, stock ~10 s, cuenta corriente ~13 s (vista legacy). De ahi `IA_MCP_TIMEOUT_SEG=60`.
- 602 articulos tienen stock negativo en ALFANET: es dato real, no un error de la vista.
- Compatibilidad: sin `APPLY`, ventanas, `OFFSET/FETCH`, `THROW`, `STRING_SPLIT` ni `OPENJSON` (bases a compat 80/100).
- La tabla de auditoria esta en `dbo` a proposito: asi `consultar_sql` (limitado a `dir.*`) no la puede leer.

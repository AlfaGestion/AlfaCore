# Scripts SQL del servidor MCP de consulta (esquema `dir`)

Capa semantica para consultar la base en lenguaje natural desde Claude / ChatGPT (proyecto `AlfaCore.Mcp`, etapa 3).
**Estos scripts NO se aplican solos**: no estan en `App_Data/updates/`. Se revisan y se corren a mano, en este orden:

| # | Script | Que hace | Requiere |
|---|---|---|---|
| 1 | `001_esquema_dir.sql` | `CREATE SCHEMA dir AUTHORIZATION dbo` | db_owner |
| 2 | `002_vistas_dir.sql` | `dir.vw_Ventas`, `vw_Articulos`, `vw_Clientes`, `vw_CuentaCorriente`, `vw_Stock`, `vw_Config` | db_owner |
| 3 | `003_ia_log_consultas.sql` | tabla `dbo.IA_LOG_CONSULTAS` (auditoria) | db_owner |
| 4 | `004_config_ia_mcp.sql` | claves `IA_MCP_*` en `TA_CONFIGURACION` (no pisa existentes) | db_owner |
| 5 | `005_login_solo_lectura.sql` | login/usuario dedicado: SELECT en `dir` + INSERT en el log | sysadmin / securityadmin |
| 10 | `010_oauth_tablas.sql` | tablas del servidor OAuth: `MCP_USUARIO`, `MCP_CLIENTE_OAUTH`, `MCP_CODIGO`, `MCP_REFRESH_TOKEN`, `MCP_EVENTO` (en la base del almacén, p. ej. `ALFA_CENTRAL`) | db_owner |
| 11 | `011_oauth_login_almacen.sql` | login **distinto** (`mcp_auth_rw`) con permisos solo sobre las tablas `MCP_*` (Modo SQLCMD) | sysadmin / securityadmin |
| 12 | `012_config_oauth.sql` | claves `IA_MCP_OAUTH_*` y la lista blanca de redirects (en la base de **datos**) | db_owner |
| 13 | `013_vistas_compras.sql` | `dir.vw_Proveedores`, `vw_Compras`, `vw_ComprasDetalle`, `vw_CuentaCorrienteProveedores` + clave `IA_MCP_TC_PROFORMA_COMPRAS` (no pisa) | db_owner |
| 99 | `099_verificacion.sql` | calidad de datos, cuadre contra el libro y prueba de permisos | el login de 5 |

Todos son idempotentes. UTF-8 sin BOM: correrlos con `sqlcmd -f i:65001`. Los scripts 10-12 son de la etapa 5 (OAuth); ver [README del servidor](../../../../src/AlfaCore.Mcp/README.md).

```
sqlcmd -S <servidor> -d <base> -f i:65001 -i 001_esquema_dir.sql
sqlcmd -S <servidor> -d <base> -f i:65001 -i 002_vistas_dir.sql
sqlcmd -S <servidor> -d <base> -f i:65001 -i 003_ia_log_consultas.sql
sqlcmd -S <servidor> -d <base> -f i:65001 -i 004_config_ia_mcp.sql
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

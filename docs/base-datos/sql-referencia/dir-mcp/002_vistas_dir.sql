-- MCP de consulta en lenguaje natural - Etapa 2 / script 2: capa semantica `dir.vw_*`.
-- Solo SELECT. Sin APPLY, sin funciones de ventana, sin OFFSET/FETCH: corre en compatibilidad 80/100.
-- Verificado contra ALFANET (compat 100, Latin1_General_CI_AI) el 2026-11-25:
--   * V_MV_Cpte / V_MV_CpteInsumos / V_MA_ARTICULOS / V_TA_Cpte / V_MV_Stock son TABLAS.
--   * COSTO de la linea es un snapshot historico (difiere del costo actual en lo viejo).
--   * Neto de linea = CANTIDAD * IMPORTE_S_IVA * (1 - PorcDto/100); cuadra con la cabecera.
--   * Las claves de texto llevan relleno (CodigoPk): se unen crudas y se recortan solo al exponerlas.
SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Config: unica puerta del MCP a TA_CONFIGURACION (solo claves IA_MCP_*).
-- Normaliza VALOR / ValorAux (ntext) para que el MCP no repita esa logica.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Config', N'V') IS NOT NULL DROP VIEW dir.vw_Config;
GO
CREATE VIEW dir.vw_Config
AS
SELECT
    LTRIM(RTRIM(CLAVE)) AS Clave,
    CASE WHEN LTRIM(RTRIM(ISNULL(VALOR, N''))) <> N'' THEN LTRIM(RTRIM(VALOR))
         ELSE LTRIM(RTRIM(ISNULL(CAST(ValorAux AS nvarchar(max)), N''))) END AS Valor,
    LTRIM(RTRIM(GRUPO)) AS Grupo
FROM dbo.TA_CONFIGURACION
WHERE CLAVE LIKE N'IA[_]MCP[_]%'
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Ventas: UNA FILA POR LINEA VENDIDA, con signo aplicado.
--   Venta = comprobante de V_TA_Cpte.SISTEMA='VENTAS' con debe/haber, con lineas de articulos, no anulado, y
--           (figura en el libro de IVA: MV_ASIENTOS.LIVA_TIPO='VENTAS')  o  (es proforma segun IA_MCP_TC_PROFORMA).
--   Quedan afuera cobranzas (sin lineas), remitos, pedidos, presupuestos y anulados.
--   Signo: V_TA_Cpte.DEBEHABER 'H' => -1 (notas de credito), si no +1. Sin codigos de TC hardcodeados.
--   Cantidad, NetoSinIva, CostoTotal y Margen ya vienen con signo.
--   SinCosto=1 marca lineas sin costo registrado (su margen no es confiable).
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Ventas', N'V') IS NOT NULL DROP VIEW dir.vw_Ventas;
GO
CREATE VIEW dir.vw_Ventas
AS
SELECT
    x.Tc, x.TipoComprobante, x.Comprobante, x.Fecha, x.Anio, x.Mes, x.AnioMes,
    x.UnidadNegocio, x.ClienteCodigo, x.ClienteNombre, x.VendedorCodigo, x.VendedorNombre,
    x.ArticuloCodigo, x.ArticuloDescripcion, x.RubroCodigo, x.RubroNombre,
    x.EsNotaCredito, x.EsProforma, x.Signo, x.Moneda, x.Unidad,
    CAST(x.Signo * x.CantidadRaw AS decimal(18,4)) AS Cantidad,
    CAST(x.Signo * x.CantidadRaw * x.PrecioNetoUnit * (1 - x.PorcDto / 100.0) AS decimal(18,2)) AS NetoSinIva,
    CAST(x.Signo * x.CantidadRaw * x.CostoUnit AS decimal(18,2)) AS CostoTotal,
    CAST(x.Signo * x.CantidadRaw * (x.PrecioNetoUnit * (1 - x.PorcDto / 100.0) - x.CostoUnit) AS decimal(18,2)) AS Margen,
    x.SinCosto
FROM (
    SELECT
        LTRIM(RTRIM(c.TC))                              AS Tc,
        LTRIM(RTRIM(t.Descripcion))                     AS TipoComprobante,
        LTRIM(RTRIM(c.IDCOMPROBANTE))                   AS Comprobante,
        c.FECHA                                         AS Fecha,
        YEAR(c.FECHA)                                   AS Anio,
        MONTH(c.FECHA)                                  AS Mes,
        CONVERT(char(7), c.FECHA, 120)                  AS AnioMes,
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO, N'')))           AS UnidadNegocio,
        LTRIM(RTRIM(c.CUENTA))                          AS ClienteCodigo,
        LTRIM(RTRIM(c.NOMBRE))                          AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(c.IdVendedor, N'')))         AS VendedorCodigo,
        ISNULL(LTRIM(RTRIM(v.Nombre)), N'Sin vendedor') AS VendedorNombre,
        LTRIM(RTRIM(i.IDARTICULO))                      AS ArticuloCodigo,
        LTRIM(RTRIM(i.DESCRIPCION))                     AS ArticuloDescripcion,
        LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'')))            AS RubroCodigo,
        ISNULL(LTRIM(RTRIM(r.Descripcion)), N'Sin rubro') AS RubroNombre,
        CASE WHEN t.DebeHaber = N'H' THEN 1 ELSE 0 END  AS EsNotaCredito,
        CASE WHEN CHARINDEX(N',' + LTRIM(RTRIM(c.TC)) + N',', cfg.Lista) > 0 THEN 1 ELSE 0 END AS EsProforma,
        CASE WHEN t.DebeHaber = N'H' THEN -1 ELSE 1 END AS Signo,
        LTRIM(RTRIM(ISNULL(c.Moneda, N'')))             AS Moneda,
        LTRIM(RTRIM(ISNULL(i.IDUNIDAD, N'')))           AS Unidad,
        ISNULL(i.CANTIDAD, 0)                           AS CantidadRaw,
        ISNULL(i.IMPORTE_S_IVA, 0)                      AS PrecioNetoUnit,
        ISNULL(i.PorcDto, 0)                            AS PorcDto,
        ISNULL(i.COSTO, 0)                              AS CostoUnit,
        CASE WHEN ISNULL(i.COSTO, 0) > 0 THEN 0 ELSE 1 END AS SinCosto
    FROM dbo.V_MV_Cpte c
    INNER JOIN dbo.V_MV_CpteInsumos i
            ON i.TC = c.TC AND i.IDCOMPROBANTE = c.IDCOMPROBANTE AND i.IDCOMPLEMENTO = c.IDCOMPLEMENTO
    INNER JOIN (SELECT CODIGO, MAX(DEBEHABER) AS DebeHaber, MAX(DESCRIPCION) AS Descripcion
                FROM dbo.V_TA_Cpte
                WHERE UPPER(SISTEMA) = N'VENTAS' AND DEBEHABER IN (N'D', N'H')
                GROUP BY CODIGO) t
            ON t.CODIGO = c.TC
    LEFT JOIN (SELECT TOP 1 N',' + REPLACE(ISNULL(VALOR, N''), N' ', N'') + N',' AS Lista
               FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_TC_PROFORMA') cfg
            ON 1 = 1
    LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = i.IDARTICULO
    LEFT JOIN dbo.V_TA_Rubros r    ON r.IdRubro = a.IDRUBRO
    LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = c.IdVendedor
    WHERE c.ANULADA = 0
      AND ( CHARINDEX(N',' + LTRIM(RTRIM(c.TC)) + N',', cfg.Lista) > 0
            OR EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS m
                       WHERE m.TC = c.TC AND m.CUENTA = c.CUENTA
                         AND m.SUCURSAL = c.SUCURSAL AND m.NUMERO = c.NUMERO AND m.LETRA = c.LETRA
                         AND m.LIVA_TIPO = N'VENTAS') )
) x
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Articulos: maestro de articulos. CostoActual es el costo vigente HOY (no el historico de venta).
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Articulos', N'V') IS NOT NULL DROP VIEW dir.vw_Articulos;
GO
CREATE VIEW dir.vw_Articulos
AS
SELECT
    LTRIM(RTRIM(a.IDARTICULO))                        AS ArticuloCodigo,
    LTRIM(RTRIM(a.DESCRIPCION))                       AS Descripcion,
    LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'')))              AS RubroCodigo,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N'Sin rubro') AS RubroNombre,
    LTRIM(RTRIM(ISNULL(a.IdFamilia, N'')))            AS FamiliaCodigo,
    LTRIM(RTRIM(ISNULL(a.IDUNIDAD, N'')))             AS Unidad,
    LTRIM(RTRIM(ISNULL(a.Moneda, N'')))               AS Moneda,
    ISNULL(a.COSTO, 0)                                AS CostoActual,
    ISNULL(a.PRECIO1, 0)                              AS Precio1,
    ISNULL(a.PRECIO2, 0)                              AS Precio2,
    ISNULL(a.PRECIO3, 0)                              AS Precio3,
    ISNULL(a.PUNTOPEDIDO, 0)                          AS PuntoPedido,
    ISNULL(a.PUNTOMAXIMO, 0)                          AS PuntoMaximo,
    a.SUSPENDIDO                                      AS Suspendido
FROM dbo.V_MA_ARTICULOS a
LEFT JOIN dbo.V_TA_Rubros r ON r.IdRubro = a.IDRUBRO
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Clientes: clientes (y particulares). Sin telefono ni mail a proposito (no hacen falta para analisis).
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Clientes', N'V') IS NOT NULL DROP VIEW dir.vw_Clientes;
GO
CREATE VIEW dir.vw_Clientes
AS
SELECT
    LTRIM(RTRIM(cl.CODIGO))                           AS ClienteCodigo,
    LTRIM(RTRIM(cl.RAZON_SOCIAL))                     AS RazonSocial,
    LTRIM(RTRIM(ISNULL(cl.NUMERO_DOCUMENTO, N'')))    AS Documento,
    LTRIM(RTRIM(ISNULL(cl.IVA, N'')))                 AS CondicionIva,
    LTRIM(RTRIM(ISNULL(cl.LOCALIDAD, N'')))           AS Localidad,
    LTRIM(RTRIM(ISNULL(cl.PROVINCIA, N'')))           AS Provincia,
    LTRIM(RTRIM(ISNULL(cl.IdVendedor, N'')))          AS VendedorCodigo,
    ISNULL(LTRIM(RTRIM(v.Nombre)), N'Sin vendedor')   AS VendedorNombre,
    ISNULL(cl.Limite_Credito, 0)                      AS LimiteCredito,
    cl.Dada_De_Baja                                   AS DadoDeBaja
FROM dbo.Vt_Clientes cl
LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = cl.IdVendedor
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_CuentaCorriente: saldos abiertos por comprobante (fuente oficial VE_CPTES_SALDOS_VENTAS).
-- Saldo > 0 = el cliente debe; creditos pendientes (NC, anticipos) vienen con signo negativo.
-- DiasVencido > 0 = vencido hace esos dias; <= 0 = todavia no vence.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_CuentaCorriente', N'V') IS NOT NULL DROP VIEW dir.vw_CuentaCorriente;
GO
CREATE VIEW dir.vw_CuentaCorriente
AS
SELECT
    y.ClienteCodigo, y.ClienteNombre, y.VendedorCodigo, y.Tc, y.Comprobante, y.Fecha, y.Vencimiento,
    y.Saldo, y.DiasVencido,
    CASE WHEN y.DiasVencido <= 0  THEN N'A vencer'
         WHEN y.DiasVencido <= 30 THEN N'Vencido 1-30'
         WHEN y.DiasVencido <= 60 THEN N'Vencido 31-60'
         WHEN y.DiasVencido <= 90 THEN N'Vencido 61-90'
         ELSE N'Vencido mas de 90' END AS Tramo
FROM (
    SELECT
        LTRIM(RTRIM(s.CUENTA))                              AS ClienteCodigo,
        ISNULL(LTRIM(RTRIM(cl.RAZON_SOCIAL)), N'')          AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(cl.IdVendedor, N'')))            AS VendedorCodigo,
        LTRIM(RTRIM(s.TC))                                  AS Tc,
        LTRIM(RTRIM(s.SUCURSAL)) + N'-' + LTRIM(RTRIM(s.NUMERO)) + LTRIM(RTRIM(s.LETRA)) AS Comprobante,
        s.FECHA                                             AS Fecha,
        s.VENCIMIENTO                                       AS Vencimiento,
        CAST(s.SALDO AS decimal(18,2))                      AS Saldo,
        DATEDIFF(day, s.VENCIMIENTO, GETDATE())             AS DiasVencido
    FROM dbo.VE_CPTES_SALDOS_VENTAS s
    LEFT JOIN dbo.Vt_Clientes cl ON cl.CODIGO = s.CUENTA
) y
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Stock: existencia por articulo y deposito (V_MV_Stock.Cantidad ya viene con signo, en unidad base).
-- PuntoPedido es por articulo: para "stock critico" hay que sumar el Stock de todos los depositos del articulo.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Stock', N'V') IS NOT NULL DROP VIEW dir.vw_Stock;
GO
CREATE VIEW dir.vw_Stock
AS
SELECT
    LTRIM(RTRIM(s.IDArticulo))                          AS ArticuloCodigo,
    LTRIM(RTRIM(a.DESCRIPCION))                         AS Descripcion,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N'Sin rubro')   AS RubroNombre,
    LTRIM(RTRIM(ISNULL(s.IdDeposito, N'')))             AS DepositoCodigo,
    ISNULL(LTRIM(RTRIM(d.Descripcion)), N'')            AS DepositoNombre,
    CAST(SUM(ISNULL(s.Cantidad, 0)) AS decimal(18,4))   AS Stock,
    ISNULL(a.PUNTOPEDIDO, 0)                            AS PuntoPedido,
    ISNULL(a.PUNTOMAXIMO, 0)                            AS PuntoMaximo,
    ISNULL(a.COSTO, 0)                                  AS CostoActual,
    a.SUSPENDIDO                                        AS Suspendido
FROM dbo.V_MV_Stock s
INNER JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = s.IDArticulo
LEFT JOIN dbo.V_TA_Rubros r     ON r.IdRubro = a.IDRUBRO
LEFT JOIN dbo.V_TA_Deposito d   ON d.IdDeposito = s.IdDeposito
WHERE s.Anulado = 0
GROUP BY s.IDArticulo, a.DESCRIPCION, r.Descripcion, s.IdDeposito, d.Descripcion,
         a.PUNTOPEDIDO, a.PUNTOMAXIMO, a.COSTO, a.SUSPENDIDO
GO

-- Consultas con IA (MCP): dir.vw_Ventas incluye los comprobantes SIN lineas de articulos.
--
-- Problema (hallado en una base real): muchas facturas se cargan solo con importe y motivo (ej. abonos, servicios),
-- sin filas en V_MV_CpteInsumos. La vista del 040 exigia lineas de articulos, asi que esas ventas no aparecian
-- (en un mes, 3 de 73 comprobantes).
--
-- Ahora: UNION ALL de
--   A) lo de antes: una fila por linea de articulo (sin cambios);
--   B) una fila por comprobante del libro de ventas (dbo.Libro_VentasConFP, la misma fuente del Reporte de Ventas de AlfaCore)
--      que NO tiene lineas de articulos: ArticuloCodigo = '', Articulo/Rubro = 'Sin detalle de artículos', Cantidad 0,
--      NetoSinIva = neto del libro de IVA (proformas: IMPORTE_SINIVA), sin costo (SinCosto = 1).
-- Columnas nuevas al final: SinDetalle (1 = fila B) y Motivo (motivo de venta, solo en filas B).
-- Mismo criterio de venta que A: en el libro de IVA (LIVA_TIPO = 'VENTAS') o proforma segun IA_MCP_TC_PROFORMA; no anulados.
--
-- Seguro: DROP + CREATE dentro de una transaccion; si la vista nueva no compila en esta base, queda la anterior
-- y el error se agrega a IA_MCP_ESQUEMA_ERRORES. Idempotente.

DECLARE @error nvarchar(max);
IF SCHEMA_ID(N'dir') IS NOT NULL   -- sin esquema dir no hay nada que corregir (lo crea 2026-11-27-040)
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dir.vw_Ventas', N'V') IS NOT NULL DROP VIEW dir.vw_Ventas;
    EXEC(N'CREATE VIEW dir.vw_Ventas
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
    x.SinCosto,
    0 AS SinDetalle,
    CAST(NULL AS nvarchar(100)) AS Motivo
FROM (
    SELECT
        LTRIM(RTRIM(c.TC))                              AS Tc,
        LTRIM(RTRIM(t.Descripcion))                     AS TipoComprobante,
        LTRIM(RTRIM(c.IDCOMPROBANTE))                   AS Comprobante,
        c.FECHA                                         AS Fecha,
        YEAR(c.FECHA)                                   AS Anio,
        MONTH(c.FECHA)                                  AS Mes,
        CONVERT(char(7), c.FECHA, 120)                  AS AnioMes,
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO, N'''')))           AS UnidadNegocio,
        LTRIM(RTRIM(c.CUENTA))                          AS ClienteCodigo,
        LTRIM(RTRIM(c.NOMBRE))                          AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(c.IdVendedor, N'''')))         AS VendedorCodigo,
        ISNULL(LTRIM(RTRIM(v.Nombre)), N''Sin vendedor'') AS VendedorNombre,
        LTRIM(RTRIM(i.IDARTICULO))                      AS ArticuloCodigo,
        LTRIM(RTRIM(i.DESCRIPCION))                     AS ArticuloDescripcion,
        LTRIM(RTRIM(ISNULL(a.IDRUBRO, N'''')))            AS RubroCodigo,
        ISNULL(LTRIM(RTRIM(r.Descripcion)), N''Sin rubro'') AS RubroNombre,
        CASE WHEN t.DebeHaber = N''H'' THEN 1 ELSE 0 END  AS EsNotaCredito,
        CASE WHEN CHARINDEX(N'','' + LTRIM(RTRIM(c.TC)) + N'','', cfg.Lista) > 0 THEN 1 ELSE 0 END AS EsProforma,
        CASE WHEN t.DebeHaber = N''H'' THEN -1 ELSE 1 END AS Signo,
        LTRIM(RTRIM(ISNULL(c.Moneda, N'''')))             AS Moneda,
        LTRIM(RTRIM(ISNULL(i.IDUNIDAD, N'''')))           AS Unidad,
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
                WHERE UPPER(SISTEMA) = N''VENTAS'' AND DEBEHABER IN (N''D'', N''H'')
                GROUP BY CODIGO) t
            ON t.CODIGO = c.TC
    LEFT JOIN (SELECT TOP 1 N'','' + REPLACE(ISNULL(VALOR, N''''), N'' '', N'''') + N'','' AS Lista
               FROM dbo.TA_CONFIGURACION WHERE CLAVE = N''IA_MCP_TC_PROFORMA'') cfg
            ON 1 = 1
    LEFT JOIN dbo.V_MA_ARTICULOS a ON a.IDARTICULO = i.IDARTICULO
    LEFT JOIN dbo.V_TA_Rubros r    ON r.IdRubro = a.IDRUBRO
    LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = c.IdVendedor
    WHERE c.ANULADA = 0
      AND ( CHARINDEX(N'','' + LTRIM(RTRIM(c.TC)) + N'','', cfg.Lista) > 0
            OR EXISTS (SELECT 1 FROM dbo.MV_ASIENTOS m
                       WHERE m.TC = c.TC AND m.CUENTA = c.CUENTA
                         AND m.SUCURSAL = c.SUCURSAL AND m.NUMERO = c.NUMERO AND m.LETRA = c.LETRA
                         AND m.LIVA_TIPO = N''VENTAS'') )
) x
UNION ALL
SELECT
    y.Tc, y.TipoComprobante, y.Comprobante, y.Fecha, YEAR(y.Fecha), MONTH(y.Fecha), CONVERT(char(7), y.Fecha, 120),
    y.UnidadNegocio, y.ClienteCodigo, y.ClienteNombre, y.VendedorCodigo, y.VendedorNombre,
    N'''', N''Sin detalle de artículos'', N'''', N''Sin detalle de artículos'',
    y.EsNotaCredito, y.EsProforma, y.Signo, y.Moneda, N'''',
    CAST(0 AS decimal(18,4)),
    CAST(y.Signo * ABS(y.Neto) AS decimal(18,2)),
    CAST(0 AS decimal(18,2)),
    CAST(y.Signo * ABS(y.Neto) AS decimal(18,2)),
    1, 1, y.Motivo
FROM (
    SELECT
        LTRIM(RTRIM(l.TC))                              AS Tc,
        LTRIM(RTRIM(t.Descripcion))                     AS TipoComprobante,
        LTRIM(RTRIM(l.IdComprobante))                   AS Comprobante,
        l.FECHA                                         AS Fecha,
        LTRIM(RTRIM(ISNULL(l.UNEGOCIO, N'''')))           AS UnidadNegocio,
        LTRIM(RTRIM(l.CUENTA))                          AS ClienteCodigo,
        COALESCE(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)), N''''), LTRIM(RTRIM(l.CUENTA))) AS ClienteNombre,
        LTRIM(RTRIM(ISNULL(l.IdVendedor, N'''')))         AS VendedorCodigo,
        ISNULL(LTRIM(RTRIM(v.Nombre)), N''Sin vendedor'') AS VendedorNombre,
        CASE WHEN t.DebeHaber = N''H'' THEN 1 ELSE 0 END  AS EsNotaCredito,
        CASE WHEN CHARINDEX(N'','' + LTRIM(RTRIM(l.TC)) + N'','', cfg.Lista) > 0 THEN 1 ELSE 0 END AS EsProforma,
        CASE WHEN t.DebeHaber = N''H'' THEN -1 ELSE 1 END AS Signo,
        LTRIM(RTRIM(ISNULL(l.MONEDA, N'''')))             AS Moneda,
        CASE WHEN CHARINDEX(N'','' + LTRIM(RTRIM(l.TC)) + N'','', cfg.Lista) > 0
             THEN COALESCE(l.IMPORTE_SINIVA, ISNULL(l.LIVA_ImpNetoGrav, 0) + ISNULL(l.LIVA_ImpNetoNGrav, 0) + ISNULL(l.LIVA_EXENTO, 0))
             ELSE ISNULL(l.LIVA_ImpNetoGrav, 0) + ISNULL(l.LIVA_ImpNetoNGrav, 0) + ISNULL(l.LIVA_EXENTO, 0) END AS Neto,
        (SELECT TOP 1 LTRIM(RTRIM(mv.Descripcion)) FROM dbo.V_TA_MotivoVta mv
          WHERE LTRIM(RTRIM(mv.IdMotivoVta)) = LTRIM(RTRIM(l.IDMOTIVO))) AS Motivo
    FROM dbo.Libro_VentasConFP l
    INNER JOIN (SELECT CODIGO, MAX(DEBEHABER) AS DebeHaber, MAX(DESCRIPCION) AS Descripcion
                FROM dbo.V_TA_Cpte
                WHERE UPPER(SISTEMA) = N''VENTAS'' AND DEBEHABER IN (N''D'', N''H'')
                GROUP BY CODIGO) t
            ON t.CODIGO = l.TC
    LEFT JOIN (SELECT TOP 1 N'','' + REPLACE(ISNULL(VALOR, N''''), N'' '', N'''') + N'','' AS Lista
               FROM dbo.TA_CONFIGURACION WHERE CLAVE = N''IA_MCP_TC_PROFORMA'') cfg
            ON 1 = 1
    LEFT JOIN dbo.V_TA_VENDEDORES v ON v.IdVendedor = l.IdVendedor
    WHERE ( CHARINDEX(N'','' + LTRIM(RTRIM(l.TC)) + N'','', cfg.Lista) > 0
            OR UPPER(ISNULL(l.LIVA_TIPO, N'''')) = N''VENTAS'' )
      AND NOT EXISTS (SELECT 1 FROM dbo.V_MV_CpteInsumos i
                      WHERE i.TC = l.TC AND i.IDCOMPROBANTE = l.IdComprobante)
      AND NOT EXISTS (SELECT 1 FROM dbo.V_MV_Cpte c
                      WHERE c.TC = l.TC AND c.IDCOMPROBANTE = l.IdComprobante AND c.ANULADA <> 0)
) y');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    SET @error = N'dir.vw_Ventas (2026-11-28-001, quedo la version anterior): ' + ERROR_MESSAGE();
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
END CATCH

IF @error IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_ESQUEMA_ERRORES')
        UPDATE dbo.TA_CONFIGURACION
           SET ValorAux = CASE WHEN ISNULL(CAST(ValorAux AS nvarchar(max)), N'') = N'' THEN @error
                               ELSE CAST(ValorAux AS nvarchar(max)) + NCHAR(10) + @error END
         WHERE CLAVE = N'IA_MCP_ESQUEMA_ERRORES';
    ELSE
        INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, ValorAux, FechaHora_Grabacion)
        VALUES (N'IA_MCP', N'IA_MCP_ESQUEMA_ERRORES', N'', N'Vistas del esquema dir que no se pudieron crear en esta base (vacio = todo bien). Lo escribe la actualizacion del MCP.', @error, GETDATE());
END
GO

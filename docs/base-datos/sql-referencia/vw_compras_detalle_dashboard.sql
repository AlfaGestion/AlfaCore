 
/****** Object:  View [dbo].[vw_compras_detalle_dashboard]    Script Date: 16/4/2026 09:42:49 ******/
IF OBJECT_ID('dbo.vw_compras_detalle_dashboard', 'V') IS NOT NULL
BEGIN
    DROP VIEW [dbo].[vw_compras_detalle_dashboard];
END
GO

/****** Object:  View [dbo].[vw_compras_detalle_dashboard]    Script Date: 16/4/2026 09:42:49 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE VIEW [dbo].[vw_compras_detalle_dashboard]
AS
SELECT
c.TC,
c.IDCOMPROBANTE,
c.NUMERO,
c.FECHA,
c.CUENTA,
p.RAZON_SOCIAL,
c.SUCURSAL,
c.IdDeposito,
c.USUARIO,

 
d.IDARTICULO,
d.DESCRIPCION AS DESCRIPCION_ITEM,
d.CANTIDAD,
d.CANTIDADUD,
d.COSTO,
d.TOTAL,

a.DESCRIPCION AS DESCRIPCION_ARTICULO,
a.IDRUBRO,
r.Descripcion AS RUBRO,
a.IdFamilia,
f.Descripcion AS FAMILIA,
a.Presentacion,

CASE 
    WHEN c.TC IN ('FCC','NDC','LIQC','FPC') THEN 1
    WHEN c.TC IN ('NCC','NCPC','NCCP') THEN -1
    ELSE 0
END AS Signo,

d.CANTIDAD *
    CASE 
        WHEN c.TC IN ('FCC','NDC','LIQC','FPC') THEN 1
        WHEN c.TC IN ('NCC','NCPC','NCCP') THEN -1
        ELSE 0
    END AS CantidadDashboard,

d.TOTAL *
    CASE 
        WHEN c.TC IN ('FCC','NDC','LIQC','FPC') THEN 1
        WHEN c.TC IN ('NCC','NCPC','NCCP') THEN -1
        ELSE 0
    END AS TotalDashboard
 

FROM C_MV_Cpte c
INNER JOIN C_MV_CpteInsumos d
ON c.TC = d.TC
AND c.IDCOMPROBANTE = d.IDCOMPROBANTE
AND c.CUENTA = d.CUENTA
LEFT JOIN Vt_Proveedores p
ON c.CUENTA = p.CODIGO
LEFT JOIN V_MA_ARTICULOS a
ON d.IDARTICULO = a.IDARTICULO
LEFT JOIN V_TA_Rubros r
ON a.IDRUBRO = r.IdRubro
LEFT JOIN V_TA_FAMILIAS f
ON a.IdFamilia = f.IdFamilia
WHERE c.ANULADA = 0;
GO



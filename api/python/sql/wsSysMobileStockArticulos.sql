
GO

/****** Object:  View [dbo].[wsSysMobileStockArticulos]    Script Date: 16/05/2021 16:23:27 ******/
DROP VIEW [dbo].[wsSysMobileStockArticulos]
GO

/****** Object:  View [dbo].[wsSysMobileStockArticulos]    Script Date: 16/05/2021 16:23:27 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE VIEW [dbo].[wsSysMobileStockArticulos]
AS
SELECT     LTRIM(dbo.STK_SALDOS_Unidades.IDArticulo) AS idArticulo, SUM(dbo.STK_SALDOS_Unidades.Stock) AS Stock
FROM         dbo.STK_SALDOS_Unidades WITH (NOLOCK) INNER JOIN
                      dbo.V_MA_ARTICULOS ON dbo.STK_SALDOS_Unidades.IDArticulo = dbo.V_MA_ARTICULOS.IDARTICULO AND 
                      dbo.STK_SALDOS_Unidades.equivalencia = dbo.V_MA_ARTICULOS.IDUNIDAD
GROUP BY dbo.STK_SALDOS_Unidades.IDArticulo



GO



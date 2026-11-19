GO

/****** Object:  View [dbo].[wsSysMobileStockComprometidoArticulos]    Script Date: 19/05/2022 15:40:29 ******/
DROP VIEW [dbo].[wsSysMobileStockComprometidoArticulos]
GO

/****** Object:  View [dbo].[wsSysMobileStockComprometidoArticulos]    Script Date: 19/05/2022 15:40:29 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE VIEW [dbo].[wsSysMobileStockComprometidoArticulos]
AS
SELECT     dbo.V_MV_CpteInsumos.IDARTICULO, SUM(dbo.V_MV_CpteInsumos.CANTIDAD) AS Stock
FROM         dbo.V_NP_Pendientes LEFT OUTER JOIN
                      dbo.V_MV_CpteInsumos ON dbo.V_NP_Pendientes.TC = dbo.V_MV_CpteInsumos.TC AND 
                      dbo.V_NP_Pendientes.IDCOMPROBANTE = dbo.V_MV_CpteInsumos.IDCOMPROBANTE AND 
                      dbo.V_NP_Pendientes.IDCOMPLEMENTO = dbo.V_MV_CpteInsumos.IDCOMPLEMENTO
GROUP BY dbo.V_MV_CpteInsumos.IDARTICULO



GO



GO

/****** Object:  View [dbo].[wsSysMobileStockComprometidoArticulos]    Script Date: 11/12/2021 23:51:07 ******/
IF  EXISTS (SELECT * FROM dbo.sysobjects WHERE id = OBJECT_ID(N'[dbo].[wsSysMobileStockComprometidoArticulos]'))
DROP VIEW [dbo].[wsSysMobileStockComprometidoArticulos]
GO


GO

/****** Object:  View [dbo].[wsSysMobileStockComprometidoArticulos]    Script Date: 11/12/2021 23:51:08 ******/
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



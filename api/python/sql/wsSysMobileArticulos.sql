
GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPaneCount' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileArticulos'
GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPane1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileArticulos'
GO

/****** Object:  View [dbo].[wsSysMobileArticulos]    Script Date: 16/05/2021 16:30:12 ******/
DROP VIEW [dbo].[wsSysMobileArticulos]
GO

/****** Object:  View [dbo].[wsSysMobileArticulos]    Script Date: 16/05/2021 16:30:12 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

/*AND (SUSPENDIDOGM = 0)*/
CREATE VIEW [dbo].[wsSysMobileArticulos]
AS
SELECT        LTRIM(dbo.V_MA_ARTICULOS.IDARTICULO) AS idArticulo, dbo.V_MA_ARTICULOS.DESCRIPCION, LTRIM(ISNULL(dbo.V_MA_ARTICULOS.IDRUBRO, N'')) AS IdRubro, ISNULL(dbo.V_MA_ARTICULOS.IMPUESTOS, 0) 
                         AS Impuestos, dbo.V_MA_ARTICULOS.TasaIVA, dbo.V_MA_ARTICULOS.EXENTO, ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO1, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) 
                         AS PRECIO1, ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO2, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO2, ISNULL(CONVERT(DECIMAL(15, 5), 
                         ISNULL(dbo.V_MA_ARTICULOS.PRECIO3, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO3, ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO1, 0) 
                         * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO4, ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO5, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO5, 
                         ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO6, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO6, ISNULL(CONVERT(DECIMAL(15, 5), 
                         ISNULL(dbo.V_MA_ARTICULOS.PRECIO7, 0) * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO7, ISNULL(CONVERT(DECIMAL(15, 5), ISNULL(dbo.V_MA_ARTICULOS.PRECIO1, 0) 
                         * ISNULL(dbo.S_TA_EQUIV.COEFICIENTE, 1)), 0) AS PRECIO8, ISNULL(dbo.V_MA_ARTICULOS.PRECIO8, 0) * 0 AS PRECIO9, ISNULL(dbo.V_MA_ARTICULOS.PRECIO8, 0) * 0 AS PRECIO10, 
                         LTRIM(ISNULL(dbo.V_MA_ARTICULOS.IdFamilia, N'')) AS idfamilia
FROM            dbo.V_MA_ARTICULOS LEFT OUTER JOIN
                         dbo.S_TA_EQUIV ON dbo.V_MA_ARTICULOS.UD_TTE = dbo.S_TA_EQUIV.IDUNIDAD AND dbo.V_MA_ARTICULOS.IDARTICULO = dbo.S_TA_EQUIV.IDARTICULO
WHERE        (dbo.V_MA_ARTICULOS.SuspendidoC = 0) AND (dbo.V_MA_ARTICULOS.SuspendidoV = 0) AND (dbo.V_MA_ARTICULOS.SUSPENDIDO = 0)
GO

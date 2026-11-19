GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPaneCount' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileTotalRegistrosTablas'
GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPane1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileTotalRegistrosTablas'
GO

/****** Object:  View [dbo].[wsSysMobileTotalRegistrosTablas]    Script Date: 02/06/2021 20:50:41 ******/
DROP VIEW [dbo].[wsSysMobileTotalRegistrosTablas]
GO

/****** Object:  View [dbo].[wsSysMobileTotalRegistrosTablas]    Script Date: 02/06/2021 20:50:41 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE VIEW [dbo].[wsSysMobileTotalRegistrosTablas]
AS
SELECT        'wsSysMobileArticulos' AS TABLA, COUNT(IDARTICULO) AS CANTIDAD
FROM            wsSysMobileArticulos
UNION 
SELECT        'wsSysMobileClientes' AS TABLA, COUNT(CODIGO) AS CANTIDAD
FROM            wsSysMobileClientes
UNION
SELECT        'wsSysMobileRubros' AS TABLA, COUNT(IDRUBRO) AS CANTIDAD
FROM            wsSysMobileRubros
UNION
SELECT        'wsSysMobileVendedores' AS TABLA, COUNT(IDVENDEDOR) AS CANTIDAD
FROM            wsSysMobileVendedores
UNION
SELECT        'wsSysMobileDepositos' AS TABLA, COUNT(IDDEPOSITO) AS CANTIDAD
FROM            wsSysMobileDepositos
GO

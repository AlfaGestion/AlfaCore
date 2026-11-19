GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPaneCount' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileClientes'
GO

EXEC sys.sp_dropextendedproperty @name=N'MS_DiagramPane1' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'VIEW',@level1name=N'wsSysMobileClientes'
GO

/****** Object:  View [dbo].[wsSysMobileClientes]    Script Date: 02/06/2021 20:52:27 ******/
DROP VIEW [dbo].[wsSysMobileClientes]
GO

/****** Object:  View [dbo].[wsSysMobileClientes]    Script Date: 02/06/2021 20:52:27 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO


CREATE VIEW [dbo].[wsSysMobileClientes]
AS
SELECT     LTRIM(CODIGO) AS CODIGO, ISNULL(CodigoOpcional, CODIGO) AS codigoOpcional, RAZON_SOCIAL, ISNULL(CALLE, '') AS CALLE, ISNULL(NUMERO, '') AS NUMERO, 
                      ISNULL(PISO, '') AS PISO, ISNULL(DEPARTAMENTO, '') AS DEPARTAMENTO, ISNULL(LOCALIDAD, '') AS localidad, ISNULL(NUMERO_DOCUMENTO, '') 
                      AS numero_documento, ISNULL(IVA, '1') AS iva, CAST(ISNULL(CASE WHEN IdLista = 'MAY' THEN 6 ELSE 8 END, 6) AS smallint) AS Clase, ISNULL(Descuento, 0) 
                      AS descuento, 'FP' AS cpteDefault, LTRIM(ISNULL(IdVendedor, '')) AS idVendedor, ISNULL(TELEFONO, '') AS TELEFONO, ISNULL(MAIL, '') AS MAIL
FROM         dbo.Vt_Clientes
WHERE     (BLOQUEO = 0)

GO

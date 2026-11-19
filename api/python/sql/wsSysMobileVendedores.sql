
GO

/****** Object:  View [dbo].[wsSysMobileVendedores]    Script Date: 06/02/2021 20:54:56 ******/
IF  EXISTS (SELECT * FROM dbo.sysobjects WHERE id = OBJECT_ID(N'[dbo].[wsSysMobileVendedores]'))
DROP VIEW [dbo].[wsSysMobileVendedores]
GO


GO

/****** Object:  View [dbo].[wsSysMobileVendedores]    Script Date: 06/02/2021 20:54:56 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO


CREATE VIEW [dbo].[wsSysMobileVendedores]
AS
SELECT     LTRIM(IdVendedor) AS idVendedor, Nombre, '1' AS codigoValidacion
FROM         dbo.V_TA_VENDEDORES



GO



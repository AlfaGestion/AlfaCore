
GO

/****** Object:  View [dbo].[wsSysMobileRubros]    Script Date: 06/02/2021 20:53:47 ******/
IF  EXISTS (SELECT * FROM dbo.sysobjects WHERE id = OBJECT_ID(N'[dbo].[wsSysMobileRubros]'))
DROP VIEW [dbo].[wsSysMobileRubros]
GO

GO

/****** Object:  View [dbo].[wsSysMobileRubros]    Script Date: 06/02/2021 20:53:47 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE VIEW [dbo].[wsSysMobileRubros]
AS
SELECT     LTRIM(IdRubro) AS idRubro, Descripcion
FROM         dbo.V_TA_Rubros



GO


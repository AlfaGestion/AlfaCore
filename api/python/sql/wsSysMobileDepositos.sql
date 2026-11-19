
GO

/****** Object:  View [dbo].[wsSysMobileDepositos]    Script Date: 06/02/2021 20:55:53 ******/
IF  EXISTS (SELECT * FROM dbo.sysobjects WHERE id = OBJECT_ID(N'[dbo].[wsSysMobileDepositos]'))
DROP VIEW [dbo].[wsSysMobileDepositos]
GO


GO

/****** Object:  View [dbo].[wsSysMobileDepositos]    Script Date: 06/02/2021 20:55:53 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO


CREATE VIEW [dbo].[wsSysMobileDepositos]
AS
SELECT     LTRIM(IdDeposito) AS idDeposito, Descripcion
FROM         dbo.V_TA_DEPOSITO



GO
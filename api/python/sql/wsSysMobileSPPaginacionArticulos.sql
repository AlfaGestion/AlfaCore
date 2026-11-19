
GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPaginacionArticulos]    Script Date: 11/03/2020 10:30:01 ******/
IF  EXISTS (SELECT * FROM dbo.sysobjects WHERE id = OBJECT_ID(N'[dbo].[wsSysMobileSPPaginacionArticulos]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[wsSysMobileSPPaginacionArticulos]
GO

GO

/****** Object:  StoredProcedure [dbo].[wsSysMobileSPPaginacionArticulos]    Script Date: 11/03/2020 10:30:01 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO



CREATE PROCEDURE [dbo].[wsSysMobileSPPaginacionArticulos] 
	@PageNumber int,
	@PageSize int
AS
	DECLARE @PageN int
	DECLARE @tampag int
	
	IF @pageNumber <= 1 SET @PageN = 1
	IF @pageNumber > 1 SET @PageN = @pageNumber-1
	SET @tampag=@pageSize*@PageN
	DECLARE @ultimo nvarchar(25)
	SET ROWCOUNT @tampag
	SELECT @ultimo=idArticulo FROM wsSysMobileArticulos ORDER BY 1
	IF  @pageNumber=1
		BEGIN 
			SET ROWCOUNT @pageSize
			SELECT idArticulo,descripcion,idRubro,idfamilia,impuestos,tasaIva,exento,precio1,precio2,precio3,precio4,precio5,precio6,precio7,precio8,precio9,precio10
				FROM wsSysMobileArticulos
					ORDER BY 1
		END
	ELSE
		BEGIN
			SET ROWCOUNT @pageSize
			SELECT idArticulo,descripcion,idRubro,idfamilia,impuestos,tasaIva,exento,precio1,precio2,precio3,precio4,precio5,precio6,precio7,precio8,precio9,precio10
				FROM wsSysMobileArticulos
					WHERE idArticulo > @ultimo
						ORDER BY 1
		END
		SET ROWCOUNT 0



GO



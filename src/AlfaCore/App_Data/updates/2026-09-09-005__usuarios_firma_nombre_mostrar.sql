/*
    Firma digital de usuarios (MA_FIRMAS_USUARIO) -- agrega un texto editable a mostrar debajo de
    la firma en los PDF (ej. "Alberto Antúnez"), en vez de mostrar siempre el nombre de usuario de
    sistema tal cual (ej. "Alberto"). Si el usuario no carga ningún texto, se sigue mostrando el
    nombre de usuario como antes (fallback en CotizacionDocumentService.BuildDataAsync).
    Idempotente.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.MA_FIRMAS_USUARIO', N'U') IS NULL
    RETURN;
GO

IF COL_LENGTH(N'dbo.MA_FIRMAS_USUARIO', N'NombreMostrar') IS NULL
    ALTER TABLE dbo.MA_FIRMAS_USUARIO ADD NombreMostrar nvarchar(150) NULL;
GO

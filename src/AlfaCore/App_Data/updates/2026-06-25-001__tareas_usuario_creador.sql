SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS', N'U') IS NULL
    RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_TAREAS', N'UsuarioCreador') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_TAREAS
    ADD UsuarioCreador nvarchar(50) NULL;
END;

EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_TAREAS
SET UsuarioCreador = UsuarioAlta
WHERE UsuarioCreador IS NULL
   OR LTRIM(RTRIM(UsuarioCreador)) = N'''';
';

-- Compras: la pantalla pasa a llamarse "Carga de comprobantes de compras" (como Carga de Comprobantes de Ventas). Solo el nombre del menú web.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL RETURN;
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB SET NombreWeb=N''Carga de comprobantes de compras''
    WHERE Menu=N''ALFA'' AND Clave=N''D50-CARGA-COMPRAS-WEB'';';

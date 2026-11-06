-- Menú web: la opción de carga se llama "Carga de comprobantes" en Compras y en Ventas.
-- (2026-11-03-020 había vuelto a dejar "Preparar compra" en Compras.) Solo NombreWeb.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL RETURN;
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB SET NombreWeb=N''Carga de comprobantes''
    WHERE Menu=N''ALFA'' AND Clave IN (N''D50-CARGA-COMPRAS-WEB'',N''D60CCW'');';

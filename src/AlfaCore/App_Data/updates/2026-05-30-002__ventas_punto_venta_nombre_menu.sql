-- ============================================================
-- Punto de Venta — normalizacion de nombre visible en menu
-- Clave TA_MENU / ALFACORE_MENU_WEB: D6005
-- ============================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Nombre = N'Punto de Venta'
    WHERE Clave = N'D6005';
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    BEGIN
        ALTER TABLE dbo.ALFACORE_MENU_WEB
            ADD NombreWeb nvarchar(150) NULL;
    END;

    UPDATE dbo.ALFACORE_MENU_WEB
    SET NombreWeb = N'Punto de Venta'
    WHERE Clave = N'D6005';
END;
GO

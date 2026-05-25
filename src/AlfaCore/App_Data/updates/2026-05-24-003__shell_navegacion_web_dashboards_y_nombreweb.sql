IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

UPDATE w
SET
    RutaWeb = v.RutaWeb,
    NombreWeb = v.NombreWeb,
    Observacion = v.Observacion
FROM dbo.ALFACORE_MENU_WEB w
INNER JOIN
(
    SELECT N'D5001' AS Clave, N'/compras' AS RutaWeb, N'Dashboard de compras' AS NombreWeb, N'Dashboard web de compras' AS Observacion
    UNION ALL SELECT N'D6000', N'/ventas', N'Dashboard de ventas', N'Dashboard web de ventas'
    UNION ALL SELECT N'D7510', N'/caja-bancos', N'Dashboard de caja y bancos', N'Dashboard web de caja y bancos'
    UNION ALL SELECT N'D8079', N'/stock', N'Dashboard de stock', N'Dashboard web de stock'
    UNION ALL SELECT N'D9502', N'/contabilidad', N'Dashboard de contabilidad', N'Dashboard web de contabilidad'
) v
    ON v.Clave = w.Clave;
GO

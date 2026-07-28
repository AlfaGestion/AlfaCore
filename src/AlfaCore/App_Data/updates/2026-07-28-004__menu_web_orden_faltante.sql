SET NOCOUNT ON;

-- ============================================================
-- Menú web — completar OrdenWeb faltante
--
-- "Maestros" (D0101) y "Seguridad" (D0150) bajo Archivos (D01), y
-- "Utilidades Avanzadas de Bases de Datos" (D9890) bajo Utilidades
-- (D98), tenían OrdenWeb NULL. El orden entre hermanos con NULL no
-- está garantizado, así que podían aparecer en cualquier orden
-- (confirmado: Seguridad salía antes que Maestros). Se fija el
-- orden pedido: Maestros, Seguridad, Interfaces bajo Archivos; y
-- Utilidades Avanzadas de Bases de Datos, Calendario, Configuración,
-- Diseñador de Consultas bajo Utilidades.
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB SET OrdenWeb = 10100 WHERE Menu = N'ALFA' AND Clave = N'D0101' AND OrdenWeb IS NULL;
GO

UPDATE dbo.ALFACORE_MENU_WEB SET OrdenWeb = 15000 WHERE Menu = N'ALFA' AND Clave = N'D0150' AND OrdenWeb IS NULL;
GO

UPDATE dbo.ALFACORE_MENU_WEB SET OrdenWeb = 98070 WHERE Menu = N'ALFA' AND Clave = N'D9890' AND OrdenWeb IS NULL;
GO

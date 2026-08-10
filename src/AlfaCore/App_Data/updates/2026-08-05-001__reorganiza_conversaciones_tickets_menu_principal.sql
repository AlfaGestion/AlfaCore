SET NOCOUNT ON;

-- ============================================================
-- Menú web — Conversaciones y Tickets pasan a ser módulos
-- principales (hijos directos de "D"), en vez de colgar de CRM
-- (D010185). Partes de horas (D010186) pasa a colgar de Tickets
-- en vez de ser hermana suya bajo CRM — así queda cubierta por
-- el módulo Tickets del catálogo de módulos (ver
-- docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
--
-- Solo se toca PadreClave/OrdenWeb: la Clave no cambia, así que
-- no hace falta tocar ALFACORE_TAREAS_WEB (los permisos apuntan
-- a la Clave, no a PadreClave).
--
-- CRM (D010185) y CRM oportunidades (D010185-WEB-CRM) quedan
-- como están — CRM oportunidades todavía no está desarrollado.
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D', OrdenWeb = 96
WHERE Clave = N'D010185-WEB-CONVERSACIONES';
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D', OrdenWeb = 98
WHERE Clave = N'D010185-WEB-TICKETS';
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010185-WEB-TICKETS', OrdenWeb = 1
WHERE Clave = N'D010186';
GO

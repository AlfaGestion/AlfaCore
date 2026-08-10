SET NOCOUNT ON;

-- ============================================================
-- Menú web — Partes de horas pasa a ser módulo principal
--
-- El script 2026-08-05-001 la había puesto colgando de Tickets
-- para que quedara cubierta por ese módulo. Se revierte: a
-- futuro también se va a usar desde Órdenes de trabajo/
-- Producción, no solo desde Tickets — necesita ser su propio
-- nodo de primer nivel e independiente en el catálogo de
-- módulos (ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D', OrdenWeb = 99
WHERE Clave = N'D010186';
GO

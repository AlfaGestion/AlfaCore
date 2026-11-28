/*
    La entrada principal del módulo Ventas debe abrir el selector de Punto de Venta.
    El dashboard gerencial conserva su ruta /ventas-dashboard para no perder el acceso
    a la consulta histórica.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;

-- 2026-11-28: SIN EFECTO. Lo reemplazó 2026-10-13-001 (D60 -> /shell/D60, panel de opciones de Ventas). Se deja vacío
-- para que una base que lo corra tarde (scripts salteados) no vuelva a mandar "Ventas" directo a Punto de venta.
-- Ver 2026-11-28-003__ventas_menu_raiz_panel.sql.
PRINT N'2026-09-22-006: sin efecto (reemplazado por 2026-10-13-001).';

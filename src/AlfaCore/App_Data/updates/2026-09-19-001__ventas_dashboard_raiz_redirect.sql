/*
    Al clickear el módulo "Ventas" del menú principal, el usuario caía en el workspace /shell/D60,
    cuyo único tile activo hoy es Punto de Venta (D6054) -- el Dashboard de Ventas (D6000) se movió a
    Tableros en 2026-07-02-001, y Delivery (D6055-DELIVERY) quedó deshabilitado en 2026-07-23-008.
    Resultado: entrar a "Ventas" mostraba directo "Punto de Venta", que no corresponde.

    Fix: el módulo raíz D60 pasa a apuntar directo al dashboard real (/ventas, Ventas.razor) en vez
    del workspace ahora degenerado a un solo tile. Punto de Venta sigue accesible desde su propia
    pestaña en el menú superior de Ventas (ModuleTopNavPresets.BuildVentas). Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2026-11-28: SIN EFECTO. Este cambio lo reemplazó 2026-10-13-001 (D60 -> /shell/D60, panel de opciones de Ventas),
-- y /ventas ahora redirige a Punto de venta. En ALFANET2007 este script se aplicó tarde (26/11, por salteado) y volvió
-- a mandar "Ventas" directo a Punto de venta. Se deja vacío para que ninguna base que lo corra tarde vuelva a romperse.
-- Ver 2026-11-28-003__ventas_menu_raiz_panel.sql.
PRINT N'2026-09-19-001: sin efecto (reemplazado por 2026-10-13-001).';
GO

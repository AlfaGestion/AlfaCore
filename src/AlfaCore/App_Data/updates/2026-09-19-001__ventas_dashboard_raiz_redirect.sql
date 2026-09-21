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

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb    = N'/ventas',
    Componente = N'Ventas'
WHERE Menu = N'ALFA'
  AND Clave = N'D60';
GO

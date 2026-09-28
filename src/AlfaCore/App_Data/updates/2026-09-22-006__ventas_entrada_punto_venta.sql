/*
    La entrada principal del módulo Ventas debe abrir el selector de Punto de Venta.
    El dashboard gerencial conserva su ruta /ventas-dashboard para no perder el acceso
    a la consulta histórica.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/ventas/punto-venta',
    Componente = N'VentasPuntoVentaSelector'
WHERE Menu = N'ALFA'
  AND Clave = N'D60';

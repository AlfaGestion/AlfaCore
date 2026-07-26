SET NOCOUNT ON;

-- ============================================================
-- Punto de Venta — un solo punto de entrada en el menú
--
-- Antes: "Punto de venta" (Mostrador) y "Delivery / Take Away"
-- eran dos opciones separadas bajo Ventas.
-- Ahora: una sola opción "Punto de venta" (D6054) que muestra un
-- selector de los POS_PUNTOVENTA activos; al elegir uno, entra al
-- flujo según su modo (Mostrador -> /ventas/punto-venta/mostrador,
-- Delivery -> /ventas/delivery?idPuntoVenta=X).
-- "Delivery / Take Away" deja de ser una opción de menú aparte.
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET Componente = N'VentasPuntoVentaSelector',
    DescripcionWeb = N'Elegí en qué punto de venta querés trabajar (Mostrador, Salón o Delivery/Take Away).'
WHERE Menu = N'ALFA' AND Clave = N'D6054';
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET HabilitadoWeb = 0
WHERE Menu = N'ALFA' AND Clave = N'D6055-DELIVERY';
GO

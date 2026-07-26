SET NOCOUNT ON;

-- ============================================================
-- Delivery / Take Away — alta en menu web
--
-- Es una pantalla operativa (como "Punto de venta"), no de
-- configuracion: se agrega como hermana de D6054 bajo Ventas (D60),
-- no bajo la carpeta DCONFIGURACION de Utilidades.
-- No se toca TA_MENU (regla vigente del menu web autosuficiente).
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D6055-DELIVERY'
)
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb,
        EsFavoritoDefault, Observacion, NombreWeb, DescripcionWeb, PadreClave
    )
    VALUES
    (
        N'ALFA', N'D6055-DELIVERY', N'/ventas/delivery', N'VentasDelivery',
        N'bi-bicycle', 1, 60550, 0,
        N'Pedidos de delivery y take away, con cierre y facturación al entregar.',
        N'Delivery / Take Away', N'Pedidos de delivery y take away, con cierre y facturación al entregar.', N'D60'
    );
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/ventas/delivery',
    Componente = N'VentasDelivery',
    Icono = N'bi-bicycle',
    HabilitadoWeb = 1,
    PadreClave = N'D60',
    NombreWeb = N'Delivery / Take Away',
    DescripcionWeb = N'Pedidos de delivery y take away, con cierre y facturación al entregar.'
WHERE Menu = N'ALFA' AND Clave = N'D6055-DELIVERY';
GO

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT t.Usuario, t.Sistema, N'D6055-DELIVERY', GETDATE(), N'MIGRACION-POS-DELIVERY'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D6055-DELIVERY'
    );
END;
GO

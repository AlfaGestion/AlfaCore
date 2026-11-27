/* Agrupa las autorizaciones de comprobantes del Punto de Venta en un nodo visible. */
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D6054-COMPROBANTES')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
        (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    VALUES
        (N'ALFA', N'D6054-COMPROBANTES', NULL, N'Comprobantes habilitados', NULL, NULL, N'bi-receipt', 1, 1, 0);
END;

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = NULL, NombreWeb = N'Comprobantes habilitados', RutaWeb = NULL,
    Componente = NULL, Icono = N'bi-receipt', HabilitadoWeb = 1, OrdenWeb = 1
WHERE Menu = N'ALFA' AND Clave = N'D6054-COMPROBANTES';

INSERT INTO dbo.ALFACORE_MENU_WEB
    (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
SELECT v.Menu, v.Clave, N'D6054-COMPROBANTES', v.NombreWeb, NULL, NULL, N'bi-receipt', 1, v.OrdenWeb, 0
FROM (VALUES
    (N'ALFA', N'D6054-COMPROBANTE-FC', N'EFC · Factura electrónica', 2),
    (N'ALFA', N'D6054-COMPROBANTE-FP', N'FP · Factura proforma', 3),
    (N'ALFA', N'D6054-COMPROBANTE-NP', N'NP · Nota de pedido', 4),
    (N'ALFA', N'D6054-COMPROBANTE-RM', N'RM · Remito interno', 5)
) v(Menu, Clave, NombreWeb, OrdenWeb)
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB w
    WHERE w.Menu = v.Menu AND UPPER(LTRIM(RTRIM(w.Clave))) = v.Clave
);

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D6054-COMPROBANTES', RutaWeb = NULL, Componente = NULL, HabilitadoWeb = 1
WHERE Menu = N'ALFA' AND Clave IN
    (N'D6054-COMPROBANTE-FC', N'D6054-COMPROBANTE-FP', N'D6054-COMPROBANTE-NP', N'D6054-COMPROBANTE-RM');

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), N'D6054-COMPROBANTES', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(t.Clave, N'') <> N''
      AND NOT EXISTS
      (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
       WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
         AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
         AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D6054-COMPROBANTES');
END;

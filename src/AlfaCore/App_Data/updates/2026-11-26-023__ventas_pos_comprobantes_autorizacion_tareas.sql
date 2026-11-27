/*
    2026-11-26 · Autorización por usuario de los accesos rápidos del POS.
    Las cuatro opciones son acciones del módulo Punto de venta y se administran
    desde Autorización de tareas. No se agrega una columna a TA_USUARIOS.
    No escribe TA_MENU ni TA_TAREAS. Idempotente.
*/
SET NOCOUNT ON;

-- 1. Guardia
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

-- 2. Columnas necesarias en bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

-- 3. Alta de las acciones, solo si existe el módulo padre del Punto de venta
IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D6054')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
        (Menu, Clave, PadreClave, NombreWeb, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault)
    SELECT v.Menu, v.Clave, v.PadreClave, v.NombreWeb, NULL, NULL, N'bi-receipt', 1, v.OrdenWeb, 0
    FROM (VALUES
        (N'ALFA', N'D6054-COMPROBANTE-FC', N'D6054', N'EFC · Factura electrónica', 60541),
        (N'ALFA', N'D6054-COMPROBANTE-FP', N'D6054', N'FP · Factura proforma', 60542),
        (N'ALFA', N'D6054-COMPROBANTE-NP', N'D6054', N'NP · Nota de pedido', 60543),
        (N'ALFA', N'D6054-COMPROBANTE-RM', N'D6054', N'RM · Remito interno', 60544)
    ) v(Menu, Clave, PadreClave, NombreWeb, OrdenWeb)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.ALFACORE_MENU_WEB w
        WHERE w.Menu = v.Menu AND UPPER(LTRIM(RTRIM(w.Clave))) = v.Clave
    );
END;

-- 4. Mantener la definición visible y como acción, nunca como pantalla
UPDATE w
SET PadreClave = v.PadreClave,
    NombreWeb = v.NombreWeb,
    RutaWeb = NULL,
    Componente = NULL,
    Icono = N'bi-receipt',
    HabilitadoWeb = 1,
    OrdenWeb = v.OrdenWeb,
    EsFavoritoDefault = 0
FROM dbo.ALFACORE_MENU_WEB w
JOIN (VALUES
    (N'D6054-COMPROBANTE-FC', N'D6054', N'EFC · Factura electrónica', 60541),
    (N'D6054-COMPROBANTE-FP', N'D6054', N'FP · Factura proforma', 60542),
    (N'D6054-COMPROBANTE-NP', N'D6054', N'NP · Nota de pedido', 60543),
    (N'D6054-COMPROBANTE-RM', N'D6054', N'RM · Remito interno', 60544)
) v(Clave, PadreClave, NombreWeb, OrdenWeb) ON UPPER(LTRIM(RTRIM(w.Clave))) = v.Clave
WHERE w.Menu = N'ALFA';

-- 5. Los usuarios que ya tenían permisos web restringidos conservan acceso inicial.
-- Luego cada comprobante puede quitarse o agregarse desde Autorización de tareas.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), v.Clave, GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB t
    CROSS JOIN (VALUES
        (N'D6054-COMPROBANTE-FC'), (N'D6054-COMPROBANTE-FP'),
        (N'D6054-COMPROBANTE-NP'), (N'D6054-COMPROBANTE-RM')
    ) v(Clave)
    WHERE ISNULL(t.Clave, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave))) = v.Clave
      );
END;

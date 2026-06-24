-- ============================================================
-- Punto de venta - correccion de ubicacion en menu web
-- TA_MENU legacy:
--   D6000 = Comprobantes
--   D6054 = Facturador grafico
-- Objetivo:
--   - asegurar el nodo web D6000 bajo Ventas
--   - mover Punto de venta a la clave web D6054
--   - eliminar la entrada web incorrecta D6005
--   - conservar permisos explicitos migrando D6005 -> D6054
-- ============================================================

SET NOCOUNT ON;

-- 1. Guardia: ALFACORE_MENU_WEB debe existir
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- 2. Columna NombreWeb para bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

-- 3. INSERT del nodo web D6000 si no existe
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = N'ALFA' AND Clave = N'D6000')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D6000')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu,
        Clave,
        RutaWeb,
        Componente,
        Icono,
        HabilitadoWeb,
        OrdenWeb,
        EsFavoritoDefault,
        Observacion,
        NombreWeb
    )
    SELECT TOP (1)
        m.Menu,
        N'D6000',
        N'/shell/D6000',
        N'ShellWorkspacePage',
        N'bi-receipt',
        1,
        60000,
        0,
        N'Submenu web de comprobantes de ventas.',
        N'Comprobantes'
    FROM dbo.TA_MENU m
    WHERE m.Menu = N'ALFA'
      AND m.Clave = N'D6000';
END;
GO

-- 4. INSERT de la opcion web D6054 si no existe
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu = N'ALFA' AND Clave = N'D6054')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D6054')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu,
        Clave,
        RutaWeb,
        Componente,
        Icono,
        HabilitadoWeb,
        OrdenWeb,
        EsFavoritoDefault,
        Observacion,
        NombreWeb
    )
    SELECT TOP (1)
        m.Menu,
        N'D6054',
        N'/ventas/punto-venta',
        N'VentasPuntoVenta',
        N'bi-upc-scan',
        1,
        60540,
        0,
        N'Punto de venta web para operacion de mostrador con catalogo, carrito, cliente y cobro.',
        N'Punto de venta'
    FROM dbo.TA_MENU m
    WHERE m.Menu = N'ALFA'
      AND m.Clave = N'D6054'
      AND ISNULL(m.Habilitado, 1) = 1;
END;
GO

-- 5. UPDATE de D6000 y D6054 para corregir jerarquia visible y orden
UPDATE w
SET
    w.RutaWeb       = CASE WHEN w.Clave = N'D6000' THEN N'/shell/D6000' ELSE N'/ventas/punto-venta' END,
    w.Componente    = CASE WHEN w.Clave = N'D6000' THEN N'ShellWorkspacePage' ELSE N'VentasPuntoVenta' END,
    w.Icono         = CASE WHEN w.Clave = N'D6000' THEN N'bi-receipt' ELSE N'bi-upc-scan' END,
    w.HabilitadoWeb = 1,
    w.OrdenWeb      = CASE WHEN w.Clave = N'D6000' THEN 60000 ELSE 60540 END,
    w.EsFavoritoDefault = 0,
    w.Observacion   = CASE
                          WHEN w.Clave = N'D6000' THEN N'Submenu web de comprobantes de ventas.'
                          ELSE N'Punto de venta web para operacion de mostrador con catalogo, carrito, cliente y cobro.'
                      END,
    w.NombreWeb     = CASE WHEN w.Clave = N'D6000' THEN N'Comprobantes' ELSE N'Punto de venta' END
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu = N'ALFA'
  AND w.Clave IN (N'D6000', N'D6054');
GO

-- 6. Eliminar la entrada web incorrecta D6005
DELETE FROM dbo.ALFACORE_MENU_WEB
WHERE Menu = N'ALFA'
  AND Clave = N'D6005';
GO

-- 7. Mantener permisos explicitos: copiar D6005 -> D6054
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D6054'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(ISNULL(t.TAREA, N'')))) = N'D6005'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D6054'
      );
END;
GO

-- 8. Migrar favoritos D6005 -> D6054 y ~D6005 -> ~D6054
IF OBJECT_ID(N'dbo.ALFACORE_FAVORITOS', N'U') IS NOT NULL
BEGIN
    DELETE f
    FROM dbo.ALFACORE_FAVORITOS f
    WHERE UPPER(LTRIM(RTRIM(f.Clave))) = N'D6005'
      AND EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_FAVORITOS x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(f.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(f.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D6054'
      );

    UPDATE dbo.ALFACORE_FAVORITOS
    SET Clave = N'D6054'
    WHERE UPPER(LTRIM(RTRIM(Clave))) = N'D6005';

    DELETE f
    FROM dbo.ALFACORE_FAVORITOS f
    WHERE UPPER(LTRIM(RTRIM(f.Clave))) = N'~D6005'
      AND EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_FAVORITOS x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(f.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(f.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave))) = N'~D6054'
      );

    UPDATE dbo.ALFACORE_FAVORITOS
    SET Clave = N'~D6054'
    WHERE UPPER(LTRIM(RTRIM(Clave))) = N'~D6005';
END;
GO

-- 9. Migrar recientes D6005 -> D6054
IF OBJECT_ID(N'dbo.ALFACORE_RECIENTES', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.ALFACORE_RECIENTES
    SET Clave = N'D6054'
    WHERE UPPER(LTRIM(RTRIM(ISNULL(Clave, N'')))) = N'D6005';
END;
GO

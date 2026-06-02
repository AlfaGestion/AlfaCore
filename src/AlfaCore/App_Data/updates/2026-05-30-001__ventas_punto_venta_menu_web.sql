-- ============================================================
-- Punto de Venta — alta en menu web y permisos iniciales
-- Clave TA_MENU: D6005 (Menu: ALFA, Titulo: D60)
-- Se agrega como opcion hija del modulo Ventas.
-- ============================================================

SET NOCOUNT ON;

-- ── 1. Guardias basicas ──────────────────────────────────────
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- ── 2. Columna NombreWeb (puede no existir en bases antiguas) ─
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

-- ── 3. Alta en TA_MENU si no existia ─────────────────────────
IF EXISTS
(
    SELECT 1
    FROM dbo.TA_MENU
    WHERE Clave = N'D60'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_MENU
    WHERE Clave = N'D6005'
)
BEGIN
    INSERT INTO dbo.TA_MENU
    (
        Menu,
        Titulo,
        Clave,
        Nombre,
        Imagen,
        Proceso,
        Habilitado,
        ORDEN,
        DESCRIPCION
    )
    SELECT TOP (1)
        Menu,
        N'D60',
        N'D6005',
        N'Punto de venta',
        N'bi-upc-scan',
        N'PuntoVenta',
        1,
        N'60005',
        N'Punto de venta web para mostrador, carrito, cliente minimo, cobro y emision de comprobantes.'
    FROM dbo.TA_MENU
    WHERE Clave = N'D60';
END;
GO

-- ── 4. Mapeo web — INSERT si no existe ───────────────────────
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
SELECT
    m.Menu,
    N'D6005',
    N'/ventas/punto-venta',
    N'VentasPuntoVenta',
    N'bi-upc-scan',
    1,
    60005,
    0,
    N'Punto de venta web para operacion de mostrador con catalogo, carrito, cliente y cobro.',
    N'Punto de venta'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D6005'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D6005'
  );
GO

-- ── 5. Mapeo web — UPDATE si ya existia ──────────────────────
UPDATE w
SET
    w.RutaWeb       = N'/ventas/punto-venta',
    w.Componente    = N'VentasPuntoVenta',
    w.Icono         = N'bi-upc-scan',
    w.HabilitadoWeb = 1,
    w.OrdenWeb      = COALESCE(NULLIF(w.OrdenWeb, 0), 60005),
    w.Observacion   = COALESCE(NULLIF(w.Observacion, N''), N'Punto de venta web para operacion de mostrador con catalogo, carrito, cliente y cobro.'),
    w.NombreWeb     = COALESCE(NULLIF(w.NombreWeb, N''), N'Punto de venta')
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D6005';
GO

-- ── 6. Descripcion en TA_MENU si la columna existe ───────────
IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = COALESCE(NULLIF(CAST(Descripcion AS nvarchar(max)), N''), N'Punto de venta web para mostrador, carrito, cliente minimo, cobro y emision de comprobantes.')
    WHERE Clave = N'D6005';
END;
GO

-- ── 7. Permisos en TA_TAREAS ─────────────────────────────────
-- Solo se agregan permisos para usuarios que ya tienen filas
-- explicitas en TA_TAREAS (usuarios con restricciones activas).
-- Usuarios sin filas en TA_TAREAS tienen acceso irrestricto.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D6005'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D6005'
      );
END;
GO

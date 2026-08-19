-- ============================================================
-- Interfaces - Carrito de compras
-- Alta de menu web (administrativo), opcion legacy y permisos
-- iniciales. Pantalla base para administrar los carritos
-- derivados de catalogos y los carritos generales manuales.
-- Mantiene el carrito publico de catalogo (/carrito/{idcatalogo})
-- y agrega la administración de carritos generales.
--
-- Clave TA_MENU / ALFACORE_MENU_WEB: D016003
-- Padre legacy/web: D0160 (Interfaces)
-- Mismo patron que D016002 (Catalogos), ver:
--   2026-08-06-003__interfaces_catalogos_menu_web.sql
--   2026-08-11-004__interfaces_catalogos_reubicar_menu_web.sql
-- ============================================================

SET NOCOUNT ON;

-- ── 1. Guardia: la tabla web debe existir ────────────────────
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- ── 2. Columnas de soporte en bases antiguas ────────────────
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD NombreWeb nvarchar(150) NULL;
END;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
BEGIN
    ALTER TABLE dbo.ALFACORE_MENU_WEB
        ADD PadreClave nvarchar(50) NULL;
END;
GO

-- ── 3. Alta en TA_MENU si no existia ─────────────────────────
IF EXISTS
(
    SELECT 1
    FROM dbo.TA_MENU
    WHERE Clave = N'D0160'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_MENU
    WHERE Clave = N'D016003'
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
        N'D0160',
        N'D016003',
        N'Carrito de compras',
        N'bi-cart-check-fill',
        N'InterfacesCarritoCompras',
        1,
        N'16003',
        N'Administración de carritos derivados de catálogos y carritos generales.'
    FROM dbo.TA_MENU
    WHERE Clave = N'D0160';
END;
GO

-- ── 4. Mapeo web - INSERT si no existe ───────────────────────
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
    NombreWeb,
    PadreClave
)
SELECT
    m.Menu,
    N'D016003',
    N'/carrito-compras',
    N'InterfacesCarritoCompras',
    N'bi-cart-check-fill',
    1,
    16003,
    0,
    N'Administración de carritos derivados de catálogos y carritos generales.',
    N'Carrito de compras',
    N'D0160'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D016003'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D016003'
  );
GO

-- ── 5. Mapeo web - UPDATE si ya existia ──────────────────────
UPDATE w
SET
    w.RutaWeb         = N'/carrito-compras',
    w.Componente      = N'InterfacesCarritoCompras',
    w.Icono           = N'bi-cart-check-fill',
    w.HabilitadoWeb   = 1,
    w.OrdenWeb        = COALESCE(NULLIF(w.OrdenWeb, 0), 16003),
    w.Observacion     = COALESCE(NULLIF(w.Observacion, N''), N'Administración de carritos derivados de catálogos y carritos generales.'),
    w.NombreWeb       = COALESCE(NULLIF(w.NombreWeb, N''), N'Carrito de compras'),
    w.PadreClave      = N'D0160'
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D016003';
GO

-- ── 6. Descripcion en TA_MENU si la columna existe ───────────
IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Administración de carritos derivados de catálogos y carritos generales.'
    WHERE Clave = N'D016003'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
GO

-- ── 7. Permisos en TA_TAREAS ─────────────────────────────────
-- Solo se agregan permisos para usuarios que ya tienen filas
-- explícitas en TA_TAREAS (usuarios con restricciones activas).
-- Usuarios sin filas en TA_TAREAS tienen acceso irrestricto
-- por política del sistema y no necesitan este registro.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D016003'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D016003'
      );
END;
GO

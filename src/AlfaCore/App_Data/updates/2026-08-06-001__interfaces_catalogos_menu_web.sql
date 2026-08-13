-- ============================================================
-- Interfaces - Catalogos
-- Alta de menu web, opcion legacy y permisos iniciales
-- Clave TA_MENU / ALFACORE_MENU_WEB: D016002
-- Padre legacy: D0160
-- ============================================================

SET NOCOUNT ON;

-- ── 1. Guardia: la tabla web debe existir ────────────────────
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
    WHERE Clave = N'D0160'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.TA_MENU
    WHERE Clave = N'D016002'
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
        N'D016002',
        N'Catálogos',
        N'bi-journal-bookmark-fill',
        N'InterfacesCatalogos',
        1,
        N'16002',
        N'Catálogos web publicados desde AlfaCore.'
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
    NombreWeb
)
SELECT
    m.Menu,
    N'D016002',
    N'/catalogos',
    N'InterfacesCatalogos',
    N'bi-journal-bookmark-fill',
    1,
    16002,
    0,
    N'Catálogos web publicados desde AlfaCore.',
    N'Catálogos'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D016002'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D016002'
  );
GO

-- ── 5. Mapeo web - UPDATE si ya existia ──────────────────────
UPDATE w
SET
    w.RutaWeb         = N'/catalogos',
    w.Componente      = N'InterfacesCatalogos',
    w.Icono           = N'bi-journal-bookmark-fill',
    w.HabilitadoWeb   = 1,
    w.OrdenWeb        = COALESCE(NULLIF(w.OrdenWeb, 0), 16002),
    w.Observacion     = COALESCE(NULLIF(w.Observacion, N''), N'Catálogos web publicados desde AlfaCore.'),
    w.NombreWeb       = COALESCE(NULLIF(w.NombreWeb, N''), N'Catálogos')
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D016002';
GO

-- ── 6. Descripcion en TA_MENU si la columna existe ───────────
IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Catálogos web publicados desde AlfaCore.'
    WHERE Clave = N'D016002'
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
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D016002'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D016002'
      );
END;
GO

-- ============================================================
-- Interfaces / Catálogos - reubicación en el menú web
--
-- Objetivo:
-- - sacar Catálogos de la pantalla de Interfaces
-- - exponerlo como opción propia en el menú web
-- - mantener compatibilidad con el árbol legacy
--
-- Clave ALFACORE_MENU_WEB / TA_MENU: D016002
-- Nuevo padre web deseado: D0160 (Interfaces)
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

-- ── 3. Alta en ALFACORE_MENU_WEB si no existía ───────────────
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
    N'D016002',
    N'/catalogos',
    N'InterfacesCatalogos',
    N'bi-journal-bookmark-fill',
    1,
    16002,
    0,
    N'Catálogos web publicados desde AlfaCore.',
    N'Catálogos',
    N'D0160'
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

-- ── 4. Reubicación / actualización del nodo existente ─────────
UPDATE w
SET
    w.RutaWeb         = N'/catalogos',
    w.Componente      = N'InterfacesCatalogos',
    w.Icono           = N'bi-journal-bookmark-fill',
    w.HabilitadoWeb   = 1,
    w.OrdenWeb        = COALESCE(NULLIF(w.OrdenWeb, 0), 16002),
    w.Observacion     = COALESCE(NULLIF(w.Observacion, N''), N'Catálogos web publicados desde AlfaCore.'),
    w.NombreWeb       = COALESCE(NULLIF(w.NombreWeb, N''), N'Catálogos'),
    w.PadreClave      = N'D0160'
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D016002';
GO

-- ── 5. TA_MENU no se toca ────────────────────────────────────
-- La clave ya existe en el árbol legacy. Se respeta esa regla
-- para no alterar la navegación histórica del desktop.
GO

-- ── 6. Permisos en TA_TAREAS ─────────────────────────────────
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

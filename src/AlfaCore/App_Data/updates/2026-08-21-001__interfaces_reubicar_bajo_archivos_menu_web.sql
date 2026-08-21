-- ============================================================
-- Interfaces - reubicación bajo Archivos + "Recepción documental"
--
-- Objetivo:
-- - "Interfaces" (D0160) deja de ser categoría de primer nivel y
--   pasa a ser sub-opción de "Archivos" (D01). Deja de ser
--   clickeable por sí misma (HabilitadoWeb=0): pasa a ser solo
--   una etiqueta agrupadora, pero sigue existiendo la fila para
--   que ALFACORE_MENU_WEB.PadreClave/NombreWeb puedan referenciarla.
-- - Adentro de "Interfaces" aparece "Recepción documental"
--   (clave nueva D016004, solo web), apuntando a la pantalla que
--   hoy es /interfaces (sin tocar el componente).
-- - "Definición de interfaces" (D016001, /interfaces/configuracion)
--   se oculta del menú (HabilitadoWeb=0): sigue accesible desde el
--   botón "Configuración" que ya tiene la pantalla de Recepción
--   Documental.
--
-- Regla del proyecto: AlfaCore nunca escribe en TA_MENU (esa tabla
-- es exclusiva del árbol de menú del desktop VB6). D016004 se da de
-- alta solo en ALFACORE_MENU_WEB.
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

-- ── 3. "Interfaces" (D0160): pasa a ser sub-opción de Archivos (D01) ──
-- Deja de ser clickeable directo (HabilitadoWeb=0): queda solo como
-- etiqueta agrupadora para D016004. Solo actualiza si la fila existe
-- (si no existe, no hay nada que reubicar en esta base).
UPDATE w
SET
    w.PadreClave    = N'D01',
    w.HabilitadoWeb = 0,
    w.NombreWeb     = COALESCE(NULLIF(w.NombreWeb, N''), N'Interfaces')
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D0160';
GO

-- ── 4. "Definición de interfaces" (D016001): se oculta del menú ──
UPDATE w
SET
    w.PadreClave    = N'D0160',
    w.HabilitadoWeb = 0
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D016001';
GO

-- ── 5. Alta de "Recepción documental" (D016004), solo web ──────
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
    N'ALFA',
    N'D016004',
    N'/interfaces',
    N'Interfaces',
    N'bi-folder2-open',
    1,
    16000,
    1,
    N'Recepción documental web (antes bajo la clave D0160).',
    N'Recepción documental',
    N'D0160'
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB w
    WHERE w.Menu = N'ALFA'
      AND w.Clave = N'D016004'
);
GO

-- ── 6. Reafirma valores si la fila ya existía (reejecución idempotente) ──
UPDATE w
SET
    w.RutaWeb         = N'/interfaces',
    w.Componente      = N'Interfaces',
    w.Icono           = N'bi-folder2-open',
    w.HabilitadoWeb   = 1,
    w.OrdenWeb        = COALESCE(NULLIF(w.OrdenWeb, 0), 16000),
    w.Observacion     = COALESCE(NULLIF(w.Observacion, N''), N'Recepción documental web (antes bajo la clave D0160).'),
    w.NombreWeb       = COALESCE(NULLIF(w.NombreWeb, N''), N'Recepción documental'),
    w.PadreClave      = N'D0160'
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D016004';
GO

-- ── 7. TA_MENU no se toca ────────────────────────────────────
-- Ni D0160 ni D016001 (ya existen en el árbol legacy) ni D016004
-- (clave nueva, solo web) se agregan/modifican en TA_MENU. El menú
-- del desktop VB6 no se ve afectado por este script.
GO

-- ── 8. Permisos en TA_TAREAS para D016004 ───────────────────
-- Solo se agregan permisos para usuarios que ya tienen filas
-- explícitas en TA_TAREAS (usuarios con restricciones activas).
-- Usuarios sin filas en TA_TAREAS tienen acceso irrestricto por
-- política del sistema y no necesitan este registro.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D016004'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D016004'
      );
END;
GO

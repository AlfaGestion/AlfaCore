-- ============================================================
-- Reportes de Compras — mapeo web y permisos iniciales
-- Clave TA_MENU: D5005 (Menu: ALFA, Titulo: D5099)
-- No se toca TA_MENU: la opción ya existe en el árbol legacy.
-- ============================================================

SET NOCOUNT ON;

-- ── 1. Guardia: ALFACORE_MENU_WEB debe existir ───────────────
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

-- ── 3. Mapeo web — INSERT si no existe ───────────────────────
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
    N'D5005',
    N'/compras/reportes',
    N'ReporteCompras',
    N'bi-bar-chart-line',
    1,
    50050,
    0,
    N'Generador de informes y estadísticas de compras: resumen por proveedor, detalle de comprobantes y cuenta corriente.',
    N'Reporte de Compras'
FROM dbo.TA_MENU m
WHERE m.Clave = N'D5005'
  AND ISNULL(m.Habilitado, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ALFACORE_MENU_WEB w
      WHERE w.Menu = m.Menu
        AND w.Clave = N'D5005'
  );
GO

-- ── 4. Mapeo web — UPDATE si ya existía ──────────────────────
UPDATE w
SET
    w.RutaWeb       = N'/compras/reportes',
    w.Componente    = N'ReporteCompras',
    w.Icono         = N'bi-bar-chart-line',
    w.HabilitadoWeb = 1,
    w.OrdenWeb      = COALESCE(NULLIF(w.OrdenWeb, 0), 50050),
    w.Observacion   = COALESCE(NULLIF(w.Observacion, N''), N'Generador de informes y estadísticas de compras: resumen por proveedor, detalle de comprobantes y cuenta corriente.'),
    w.NombreWeb     = COALESCE(NULLIF(w.NombreWeb, N''), N'Reporte de Compras')
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Clave = N'D5005';
GO

-- ── 5. Descripción en TA_MENU (si la columna existe) ─────────
IF COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    UPDATE dbo.TA_MENU
    SET Descripcion = N'Generador de informes y estadísticas de compras: resumen por proveedor, detalle de comprobantes y cuenta corriente.'
    WHERE Clave = N'D5005'
      AND ISNULL(LTRIM(RTRIM(CAST(Descripcion AS nvarchar(max)))), N'') = N'';
END;
GO

-- ── 6. Permisos en TA_TAREAS ──────────────────────────────────
-- Solo se agregan permisos para usuarios que ya tienen filas
-- explícitas en TA_TAREAS (usuarios con restricciones activas).
-- Usuarios sin filas en TA_TAREAS tienen acceso irrestricto
-- por política del sistema y no necesitan este registro.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D5005'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D5005'
      );
END;
GO

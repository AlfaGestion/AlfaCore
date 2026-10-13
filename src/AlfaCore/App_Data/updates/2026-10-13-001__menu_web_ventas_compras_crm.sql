/*
    Ajustes del menú web (Inicio → Módulos), pedidos por los usuarios:

    1. Tableros → "Dashboard de Ventas" (D6000) abría el Punto de Venta: /ventas redirige al
       selector de puntos de venta desde 2026-09-22-006. Pasa a /ventas-dashboard (Ventas.razor).
    2. Compras (D50) no mostraba aplicaciones: falta el acceso web de "Reporte de Compras" (D5005)
       en algunas bases. Se agrega (o se corrige) colgado directamente de Compras.
    3. "Puntos de Venta" (DCONFIG-PUNTOSVENTA) pasa de Utilidades → Configuración a Ventas.
       Con eso Ventas (D60) vuelve a ser un módulo con su panel (Punto de venta + Puntos de Venta)
       en lugar de entrar directo al Punto de Venta.
    (Cotizaciones dentro de CRM: ver 2026-10-13-002.)

    No se toca TA_MENU (las claves ya existen en el árbol legacy). Idempotente.
*/

SET NOCOUNT ON;

-- ── 1. Guardia ───────────────────────────────────────────────
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;
GO

-- ── 2. Columnas que pueden faltar en bases antiguas ──────────
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO

-- ── 3. Dashboard de Ventas → dashboard real ──────────────────
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb    = N'/ventas-dashboard',
    Componente = N'Ventas'
WHERE Menu = N'ALFA'
  AND Clave = N'D6000'
  AND ISNULL(RutaWeb, N'') IN (N'', N'/ventas', N'/ventas/punto-venta');
GO

-- ── 4. Reporte de Compras dentro de Compras ──────────────────
IF EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D50')
   AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D5005')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
        (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion, NombreWeb)
    VALUES
        (N'ALFA', N'D5005', N'/compras/reportes', N'ReporteCompras', N'bi-bar-chart-line', 1, 50050, 0,
         N'Generador de informes y estadísticas de compras: resumen por proveedor, detalle de comprobantes y cuenta corriente.',
         N'Reporte de Compras');
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb       = N'/compras/reportes',
    Componente    = N'ReporteCompras',
    HabilitadoWeb = 1,
    NombreWeb     = COALESCE(NULLIF(NombreWeb, N''), N'Reporte de Compras')
WHERE Menu = N'ALFA'
  AND Clave = N'D5005';
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NOT NULL
    EXEC (N'
        UPDATE dbo.ALFACORE_MENU_WEB
        SET PadreClave = N''D50''
        WHERE Menu = N''ALFA''
          AND Clave = N''D5005''
          AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N''ALFA'' AND p.Clave = N''D50'');');
GO

-- ── 5. Puntos de Venta dentro de Ventas; Ventas vuelve a ser un módulo con panel ──
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NOT NULL
    EXEC (N'
        UPDATE dbo.ALFACORE_MENU_WEB
        SET PadreClave = N''D60'',
            OrdenWeb   = 60545
        WHERE Menu = N''ALFA''
          AND Clave = N''DCONFIG-PUNTOSVENTA''
          AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N''ALFA'' AND p.Clave = N''D60'');');
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb    = N'/shell/D60',
    Componente = N'ShellWorkspacePage'
WHERE Menu = N'ALFA'
  AND Clave = N'D60';
GO

-- ── 6. Permisos de Reporte de Compras (solo usuarios con restricciones activas) ──
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D5005'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))   = N'D5005'
      );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT t.Usuario, t.Sistema, N'D5005', GETDATE(), N'MIGRACION-MENU-2026-10-13'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D5005'
    );
END;
GO

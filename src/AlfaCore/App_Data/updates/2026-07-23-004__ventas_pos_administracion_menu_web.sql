SET NOCOUNT ON;

-- ============================================================
-- Administración de Puntos de Venta — alta en menú web
--
-- El menú web de AlfaCore es autosuficiente en ALFACORE_MENU_WEB
-- (columna PadreClave para la jerarquía) y no depende de TA_MENU.
-- No se toca TA_MENU en este script.
--
-- Se agrega:
--   1. Nodo agrupador "DCONFIGURACION" bajo Utilidades (D98). Por
--      ahora solo contendrá la administración de Puntos de Venta;
--      a futuro será la configuración general de AlfaCore.
--      IMPORTANTE: este nodo NO debe tener RutaWeb/Componente
--      propios. MenuService.IsShellModuleRow trata como "módulo
--      raíz" (barra lateral) a cualquier fila con
--      Componente='ShellWorkspacePage' y ruta '/shell/...',
--      SIN mirar su PadreClave. Si a este nodo se le pone esa
--      combinación, aparece como hermano de Utilidades en vez de
--      quedar anidado. Al dejarlo sin ruta, MenuService.BuildSections
--      lo usa solo como etiqueta de sección dentro del workspace de
--      Utilidades (D98), y sus hijos (ej. Puntos de Venta) aparecen
--      agrupados bajo ese título.
--   2. Nodo hoja "DCONFIG-PUNTOSVENTA" (Administración de Puntos
--      de Venta, Sectores y Mesas) bajo DCONFIGURACION.
--   3. Permisos en ALFACORE_TAREAS_WEB solo para usuarios que ya
--      tienen filas explícitas ahí (restricciones activas).
-- ============================================================

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- ── 1. Carpeta "Configuración" bajo Utilidades (D98) ─────────
IF NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'DCONFIGURACION'
)
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb,
        EsFavoritoDefault, Observacion, NombreWeb, DescripcionWeb, PadreClave
    )
    VALUES
    (
        N'ALFA', N'DCONFIGURACION', NULL, NULL,
        N'bi-gear-fill', 1, 98100, 0,
        N'Configuración general de AlfaCore (a futuro agrupará más secciones).',
        N'Configuración', N'Configuración general de AlfaCore.', N'D98'
    );
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = NULL,
    Componente = NULL,
    Icono = N'bi-gear-fill',
    HabilitadoWeb = 1,
    PadreClave = N'D98',
    NombreWeb = N'Configuración',
    DescripcionWeb = N'Configuración general de AlfaCore.'
WHERE Menu = N'ALFA' AND Clave = N'DCONFIGURACION';
GO

-- ── 2. Hoja "Puntos de Venta" bajo Configuración ─────────────
IF NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'DCONFIG-PUNTOSVENTA'
)
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb,
        EsFavoritoDefault, Observacion, NombreWeb, DescripcionWeb, PadreClave
    )
    VALUES
    (
        N'ALFA', N'DCONFIG-PUNTOSVENTA', N'/ventas/puntos-venta', N'PuntoVentaAdministracion',
        N'bi-shop-window', 1, 98110, 0,
        N'Administración de puntos de venta, sectores y mesas.',
        N'Puntos de Venta', N'Administración de puntos de venta (Mostrador/Salón/Delivery), sectores y mesas.', N'DCONFIGURACION'
    );
END;
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/ventas/puntos-venta',
    Componente = N'PuntoVentaAdministracion',
    Icono = N'bi-shop-window',
    HabilitadoWeb = 1,
    PadreClave = N'DCONFIGURACION',
    NombreWeb = N'Puntos de Venta',
    DescripcionWeb = N'Administración de puntos de venta (Mostrador/Salón/Delivery), sectores y mesas.'
WHERE Menu = N'ALFA' AND Clave = N'DCONFIG-PUNTOSVENTA';
GO

-- ── 3. Permisos: solo para usuarios con restricciones activas ─
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT t.Usuario, t.Sistema, N'DCONFIG-PUNTOSVENTA', GETDATE(), N'MIGRACION-POS-ADMIN'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'DCONFIG-PUNTOSVENTA'
    );
END;
GO

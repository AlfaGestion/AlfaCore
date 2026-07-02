-- ============================================================
-- Reestructuración del menú web
--
-- Cambios:
--   1. Nuevo nodo raíz DTABLEROS (Tableros) con los 5 dashboards
--   2. Logística → módulo raíz, orden 92 (antes de Gestión Contable)
--   3. Fix: D010181 tenía PadreClave=D0101, corrección a D010180
--   4. CRM → módulo raíz, orden 97 (después de Gestión Contable)
--   5. Conversaciones y Tickets pasan directamente bajo CRM
--   6. D010185WEB (agrupador vacío) → deshabilitado
--   7. D6000 pasa de workspace a Dashboard de Ventas en Tableros
--   8. D6054 (Punto de venta) pasa directamente bajo D60
--   9. D75 y D80 → deshabilitados (sin contenido)
-- ============================================================

SET NOCOUNT ON;

-- Guardia
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

-- ── 1. Nodo raíz Tableros ─────────────────────────────────────
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'DTABLEROS'
)
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
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'DTABLEROS',
        N'/shell/DTABLEROS',
        N'ShellWorkspacePage',
        N'bi-speedometer2',
        1,
        5,
        0,
        N'Vista consolidada de dashboards operativos del sistema.',
        N'Tableros',
        N'Vista consolidada de dashboards operativos del sistema.',
        N'D'
    );
END;
GO

-- UPDATE idempotente por si ya existía con valores distintos
UPDATE dbo.ALFACORE_MENU_WEB
SET
    RutaWeb         = N'/shell/DTABLEROS',
    Componente      = N'ShellWorkspacePage',
    Icono           = N'bi-speedometer2',
    HabilitadoWeb   = 1,
    OrdenWeb        = 5,
    EsFavoritoDefault = 0,
    NombreWeb       = N'Tableros',
    DescripcionWeb  = N'Vista consolidada de dashboards operativos del sistema.',
    PadreClave      = N'D'
WHERE Menu = N'ALFA'
  AND Clave = N'DTABLEROS';
GO

-- ── 2. Mover dashboards a Tableros ───────────────────────────
-- D5001 → Dashboard de Compras (antes bajo D50)
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'DTABLEROS',
    OrdenWeb   = 50
WHERE Menu = N'ALFA'
  AND Clave = N'D5001';

-- D6000 → Dashboard de Ventas (antes workspace bajo D60)
-- El Punto de venta (D6054) se mueve directamente bajo D60
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb    = N'/ventas',
    Componente = N'VentasComprobantes',
    NombreWeb  = N'Dashboard de Ventas',
    DescripcionWeb = N'Dashboard de ventas y acceso a comprobantes comerciales del circuito.',
    PadreClave = N'DTABLEROS',
    OrdenWeb   = 60
WHERE Menu = N'ALFA'
  AND Clave = N'D6000';

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D60'
WHERE Menu = N'ALFA'
  AND Clave = N'D6054';

-- D7510 → Dashboard de Caja y Bancos (antes bajo D75)
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'DTABLEROS',
    OrdenWeb   = 75
WHERE Menu = N'ALFA'
  AND Clave = N'D7510';

-- D8079 → Dashboard de Stock (antes bajo D80)
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'DTABLEROS',
    OrdenWeb   = 80
WHERE Menu = N'ALFA'
  AND Clave = N'D8079';

-- D9502 → Dashboard de Contabilidad (antes bajo D95)
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'DTABLEROS',
    OrdenWeb   = 95
WHERE Menu = N'ALFA'
  AND Clave = N'D9502';
GO

-- ── 3. Deshabilitar módulos que quedan vacíos ────────────────
-- D75 (Caja y Bancos) y D80 (Stock) quedan sin contenido
UPDATE dbo.ALFACORE_MENU_WEB
SET HabilitadoWeb = 0
WHERE Menu = N'ALFA'
  AND Clave IN (N'D75', N'D80');
GO

-- ── 4. Logística → módulo raíz (orden 92, antes de D95) ─────
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D',
    OrdenWeb   = 92
WHERE Menu = N'ALFA'
  AND Clave = N'D010180';

-- Fix: D010181 (Carga de viaje) tenía PadreClave=D0101 por error
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010180'
WHERE Menu = N'ALFA'
  AND Clave = N'D010181';
GO

-- ── 5. CRM → módulo raíz (orden 97, después de D95) ─────────
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D',
    OrdenWeb   = 97
WHERE Menu = N'ALFA'
  AND Clave = N'D010185';

-- Deshabilitar agrupador D010185WEB (ya no cumple función)
UPDATE dbo.ALFACORE_MENU_WEB
SET HabilitadoWeb = 0
WHERE Menu = N'ALFA'
  AND Clave = N'D010185WEB';

-- Conversaciones y Tickets pasan directamente bajo CRM
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010185',
    OrdenWeb   = 1
WHERE Menu = N'ALFA'
  AND Clave = N'D010185-WEB-CONVERSACIONES';

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010185',
    OrdenWeb   = 2
WHERE Menu = N'ALFA'
  AND Clave = N'D010185-WEB-TICKETS';

-- Partes de horas: ya tiene PadreClave=D010185, solo ajustar orden
UPDATE dbo.ALFACORE_MENU_WEB
SET OrdenWeb = 3
WHERE Menu = N'ALFA'
  AND Clave = N'D010186';
GO

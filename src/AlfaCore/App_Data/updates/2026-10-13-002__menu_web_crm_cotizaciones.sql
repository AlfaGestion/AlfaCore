/*
    CRM como módulo web en todas las bases, con Cotizaciones adentro.

    1. Módulo raíz CRM (D010185). 2026-05-25-003 lo creaba solo si la clave existía en TA_MENU,
       así que las bases sin esa clave legacy (ej. evelybn) quedaban sin módulo CRM aunque lo
       tuvieran contratado: "CRM oportunidades" quedaba colgando de un padre inexistente. El menú
       web es autosuficiente (ALFACORE_MENU_WEB.PadreClave), así que se crea sin depender de TA_MENU.
       Qué empresas lo ven lo sigue decidiendo el módulo contratado (Modulos.MenuKeyRaiz = D010185).
    2. Cotizaciones (DCOTIZACIONES) siempre dentro de CRM: sin CRM no hay Cotizaciones.

    No se toca TA_MENU. Idempotente.
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

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;
GO

-- ── 3. Módulo raíz CRM (INSERT si no existe) ─────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D010185')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
        (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion, NombreWeb, PadreClave)
    VALUES
        (N'ALFA', N'D010185', N'/shell/D010185', N'ShellWorkspacePage', N'bi-headset', 1, 97, 0,
         N'Módulo raíz CRM', N'CRM', N'D');
END;
GO

-- ── 4. UPDATE si ya existía: que sea un módulo con su panel ──
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb       = N'/shell/D010185',
    Componente    = N'ShellWorkspacePage',
    HabilitadoWeb = 1,
    PadreClave    = N'D',
    Icono         = COALESCE(NULLIF(Icono, N''), N'bi-headset'),
    NombreWeb     = COALESCE(NULLIF(NombreWeb, N''), N'CRM')
WHERE Menu = N'ALFA'
  AND Clave = N'D010185';
GO

-- ── 5. Cotizaciones siempre dentro de CRM ────────────────────
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010185',
    OrdenWeb   = 18600
WHERE Menu = N'ALFA'
  AND Clave = N'DCOTIZACIONES';
GO

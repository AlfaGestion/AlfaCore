/*
    Orden del menú web (Inicio → Módulos), acordado con los usuarios:

    1. Ventas reúne todo lo comercial: además de Punto de venta y Puntos de Venta, pasan
       "Carrito de compras" (D016003, pedidos de clientes; estaba en Archivos → Interfaces, grupo
       deshabilitado) y "Catálogos" (D016002, estaba en Archivos).
    2. Utilidades: la sección "Configuración" (DCONFIGURACION, sin ruta: es solo el título de la
       sección) agrupa Configuración General y Configuración Web / Portal.
    3. Calendario (D9808) pasa de Utilidades a CRM.
    4. Nombres: "Reportes" de Logística pasa a "Reportes de viajes"; "Actualizaciones" sale del
       grupo "Utilidades Avanzadas de Bases de Datos" (que solo la contenía) y queda directo en
       Utilidades. Sin acentos nuevos (los usuarios lo prefieren por los juegos de caracteres).

    No se tocan Seguridad, Tableros ni Conversaciones/Tickets. No se toca TA_MENU. Idempotente:
    cada cambio se aplica solo si el padre existe en esta base.
*/

SET NOCOUNT ON;

-- ── 1. Guardia ───────────────────────────────────────────────
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    RETURN;
GO

-- ── 2. Ventas: Carrito de compras y Catálogos ────────────────
UPDATE w
SET w.PadreClave = N'D60',
    w.OrdenWeb   = CASE w.Clave WHEN N'D016003' THEN 60560 ELSE 60570 END
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu = N'ALFA'
  AND w.Clave IN (N'D016003', N'D016002')
  AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N'ALFA' AND p.Clave = N'D60');
GO

-- ── 3. Utilidades: sección Configuración ─────────────────────
UPDATE w
SET w.PadreClave = N'DCONFIGURACION',
    w.OrdenWeb   = CASE w.Clave WHEN N'DCONFIGGENERAL' THEN 98101 ELSE 98102 END
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu = N'ALFA'
  AND w.Clave IN (N'DCONFIGGENERAL', N'DCONFIGWEBPORTAL')
  AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N'ALFA' AND p.Clave = N'DCONFIGURACION');
GO

-- ── 4. Calendario dentro de CRM ──────────────────────────────
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D010185',
    OrdenWeb   = 18700
WHERE Menu = N'ALFA'
  AND Clave = N'D9808'
  AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N'ALFA' AND p.Clave = N'D010185');
GO

-- ── 5. Nombres ───────────────────────────────────────────────
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NOT NULL
    UPDATE dbo.ALFACORE_MENU_WEB
    SET NombreWeb = N'Reportes de viajes'
    WHERE Menu = N'ALFA'
      AND Clave = N'D010182'
      AND ISNULL(NombreWeb, N'') IN (N'', N'Reportes');
GO

UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = N'D98'
WHERE Menu = N'ALFA'
  AND Clave = N'D989003'
  AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu = N'ALFA' AND p.Clave = N'D98');
GO

/*
    2026-11-28 · Ventas vuelve a abrir su panel de opciones desde el inicio.
    El módulo raíz D60 debe apuntar a /shell/D60 (ShellWorkspacePage), como dejó 2026-10-13-001. En ALFANET2007 el script
    viejo 2026-09-19-001 se aplicó tarde (26/11, por salteado) y lo volvió a /ventas, que redirige a Punto de venta: al tocar
    "Ventas" en el inicio se entraba directo al punto de venta. Solo corrige si D60 quedó en una de esas rutas viejas
    (no pisa una ruta puesta a mano). Idempotente. No escribe TA_MENU. Compatibility level 80/100.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;

UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/shell/D60',
    Componente = N'ShellWorkspacePage'
WHERE Menu = N'ALFA'
  AND Clave = N'D60'
  AND LTRIM(RTRIM(ISNULL(RutaWeb, N''))) IN (N'', N'/ventas', N'/ventas/', N'/ventas/punto-venta');

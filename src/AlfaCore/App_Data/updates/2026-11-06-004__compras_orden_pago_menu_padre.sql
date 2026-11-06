/*
    2026-11-06 · Orden de Pago (D509012): cuelga del menú web de Compras. La versión 2026-11-06-003 original dejó la opción sin PadreClave
    cuando el nodo «Órdenes de Pago» (D5090) no está en el menú web, y sin padre no aparece en el menú ni en la autorización de permisos.
    Este script la cuelga de D5090 si existe en el menú web y, si no, de Compras (D50). Solo toca la fila D509012 y solo si no tiene padre.
    No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL RETURN;

EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave = CASE WHEN EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D5090'') THEN N''D5090'' ELSE N''D50'' END
WHERE Clave = N''D509012'' AND ISNULL(LTRIM(RTRIM(PadreClave)), N'''') = N''''
  AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave IN (N''D5090'', N''D50''));';

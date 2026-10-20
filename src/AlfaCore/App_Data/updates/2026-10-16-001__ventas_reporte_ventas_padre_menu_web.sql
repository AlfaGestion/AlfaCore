-- Repara instalaciones que ya aplicaron el alta del reporte sin su padre web.
-- No modifica TA_MENU, permisos, configuración ni movimientos comerciales.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NULL RETURN;
EXEC sys.sp_executesql N'
UPDATE w SET PadreClave=N''D60''
FROM dbo.ALFACORE_MENU_WEB w
WHERE w.Menu=N''ALFA'' AND w.Clave=N''D60RPTWEB''
  AND w.Componente=N''ReporteVentas'' AND w.RutaWeb=N''/ventas/reportes''
  AND ISNULL(LTRIM(RTRIM(w.PadreClave)),N'''')<>N''D60''
  AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Menu=w.Menu AND p.Clave=N''D60'');';

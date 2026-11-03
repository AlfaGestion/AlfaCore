/*
    2026-11-03 · Reportes contables en AlfaCore: Mayor de cuentas (/contabilidad/mayor) y Balance de sumas y
    saldos (/contabilidad/sumas-y-saldos). Usan las opciones legacy D957005 «Reporte de Cuentas» y D957004
    «Balance de Sumas y Saldos» (Gestión Contable → Reportes): mismos permisos de TA_TAREAS, copiados a
    ALFACORE_TAREAS_WEB. No crea opciones en TA_MENU ni toca asientos. Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
EXEC sys.sp_executesql N'
DECLARE @o TABLE (Clave nvarchar(20), Ruta nvarchar(200), Componente nvarchar(100), Nombre nvarchar(150), Orden int);
INSERT INTO @o VALUES (N''D957005'', N''/contabilidad/mayor'', N''ContabilidadMayor'', N''Mayor de cuentas'', 95705),
                      (N''D957004'', N''/contabilidad/sumas-y-saldos'', N''ContabilidadSumasSaldos'', N''Balance de sumas y saldos'', 95704);
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
SELECT m.Menu, o.Clave, o.Ruta, o.Componente, N''bi-journal-richtext'', 1, o.Orden, 0, o.Nombre
FROM @o o JOIN dbo.TA_MENU m ON m.Clave = o.Clave
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu = m.Menu AND w.Clave = m.Clave);
UPDATE w SET RutaWeb = o.Ruta, Componente = o.Componente, NombreWeb = o.Nombre, HabilitadoWeb = 1
FROM dbo.ALFACORE_MENU_WEB w JOIN @o o ON o.Clave = w.Clave;';
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave = N''D95''
        WHERE Clave IN (N''D957005'', N''D957004'') AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D95'');';
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.USUARIO)), LTRIM(RTRIM(t.SISTEMA)), UPPER(LTRIM(RTRIM(t.TAREA))), GETDATE(), N'REPORTES-CONTABLES-2026-11-03'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D957005', N'D957004') AND ISNULL(LTRIM(RTRIM(t.USUARIO)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA))));

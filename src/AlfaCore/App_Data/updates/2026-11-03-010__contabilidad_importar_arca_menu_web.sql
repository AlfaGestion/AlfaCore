/*
    2026-11-03 · Control e importación contra ARCA (/contabilidad/control-arca). Usa la opción legacy D016023
    «Importar Comprobantes» (Recepción documental), con sus permisos de TA_TAREAS copiados a ALFACORE_TAREAS_WEB:
    habilita importar los recibidos que faltan y configurar la descarga de Mis Comprobantes.
    No crea opciones en TA_MENU ni modifica comprobantes. Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D016023') RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
SELECT m.Menu,N''D016023'',N''/contabilidad/control-arca'',N''ContabilidadControlArca'',N''bi-cloud-check'',1,160230,0,N''Control e importación ARCA''
FROM dbo.TA_MENU m WHERE m.Clave=N''D016023'' AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/contabilidad/control-arca'',Componente=N''ContabilidadControlArca'',Icono=N''bi-cloud-check'',
    NombreWeb=N''Control e importación ARCA'',HabilitadoWeb=1
WHERE Clave=N''D016023'';';
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D0160''
        WHERE Clave=N''D016023'' AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave=N''D0160'');';
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.USUARIO)), LTRIM(RTRIM(t.SISTEMA)), N'D016023', GETDATE(), N'IMPORTAR-ARCA-2026-11-03'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) = N'D016023' AND ISNULL(LTRIM(RTRIM(t.USUARIO)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D016023');

/*
    2026-11-26 · Tabla de comprobantes (/archivos/comprobantes): ABM de comprobantes y talonarios (V_TA_CPTE). Usa la opción
    legacy D010310 «Tabla de Comprobantes» como entrada del menú web y copia a ALFACORE_TAREAS_WEB sus permisos de TA_TAREAS.
    No crea opciones en TA_MENU ni modifica movimientos. Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D010310')
BEGIN
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
    SELECT m.Menu,N''D010310'',N''/archivos/comprobantes'',N''ArchivosComprobantes'',N''bi-receipt'',1,10310,0,N''Tabla de comprobantes''
    FROM dbo.TA_MENU m WHERE m.Clave=N''D010310'' AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
    UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/archivos/comprobantes'',Componente=N''ArchivosComprobantes'',Icono=N''bi-receipt'',
        NombreWeb=N''Tabla de comprobantes'',HabilitadoWeb=1
    WHERE Clave=N''D010310'';';
    IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
        EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D01''
            WHERE Clave=N''D010310'' AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave=N''D01'');';
END
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.USUARIO)), LTRIM(RTRIM(t.SISTEMA)), UPPER(LTRIM(RTRIM(t.TAREA))), GETDATE(), N'TABLA-COMPROBANTES-2026-11-26'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) = N'D010310'
      AND ISNULL(LTRIM(RTRIM(t.USUARIO)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA))));

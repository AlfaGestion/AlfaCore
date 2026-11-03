/*
    2026-11-03 · Retenciones y percepciones (/contabilidad/retenciones). Usa la opción legacy D016021 «SIFERE WEB IIBB»
    como entrada del menú web y copia a ALFACORE_TAREAS_WEB los permisos de TA_TAREAS de las exportaciones de
    retenciones/percepciones (SIFERE, SIRCAR, ARCIBA, ARBA, SICORE y F.2002 IVA sufridas).
    No crea opciones en TA_MENU ni modifica movimientos. Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D016021')
BEGIN
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
    SELECT m.Menu,N''D016021'',N''/contabilidad/retenciones'',N''ContabilidadRetenciones'',N''bi-percent'',1,160210,0,N''Retenciones y percepciones''
    FROM dbo.TA_MENU m WHERE m.Clave=N''D016021'' AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
    UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/contabilidad/retenciones'',Componente=N''ContabilidadRetenciones'',Icono=N''bi-percent'',
        NombreWeb=N''Retenciones y percepciones'',HabilitadoWeb=1
    WHERE Clave=N''D016021'';';
    IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
        EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D0160''
            WHERE Clave=N''D016021'' AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave=N''D0160'');';
END
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.USUARIO)), LTRIM(RTRIM(t.SISTEMA)), UPPER(LTRIM(RTRIM(t.TAREA))), GETDATE(), N'RETENCIONES-2026-11-03'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D016021',N'D016022',N'D016026',N'D016027',N'D016028',N'D016029',N'D016057')
      AND ISNULL(LTRIM(RTRIM(t.USUARIO)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA))));

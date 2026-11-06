/*
    2026-11-06 · Estudio contable (/contabilidad/estudio-contable): entrada en el menú web, colgada de Contabilidad (D0160),
    para que el administrador invite a su estudio. Clave web propia D016040 (no existe en TA_MENU). La pantalla exige además
    D015001 o D015002, así que el permiso del menú se da solo a quienes ya administran usuarios o autorizan tareas.
    No toca TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Clave=N'D016040')
    INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
    VALUES(COALESCE((SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave=N'D0160'),
                    (SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave=N'D01'),N'ALFA'),
           N'D016040',N'/contabilidad/estudio-contable',N'ContabilidadEstudioContable',N'bi-briefcase',1,160216,0,N'Estudio contable');
ELSE
    UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N'/contabilidad/estudio-contable',Componente=N'ContabilidadEstudioContable',
        Icono=N'bi-briefcase',NombreWeb=N'Estudio contable',HabilitadoWeb=1 WHERE Clave=N'D016040';
GO
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D0160''
        WHERE Clave=N''D016040'' AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave=N''D0160'');';
GO
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)), LTRIM(RTRIM(t.Sistema)), N'D016040', GETDATE(), N'ESTUDIO-CONTABLE-2026-11-06'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE UPPER(LTRIM(RTRIM(t.Clave))) IN (N'D015001', N'D015002')
      AND ISNULL(LTRIM(RTRIM(t.Usuario)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D016040');
GO

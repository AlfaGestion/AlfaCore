-- Administrar es una navegación propia, visible únicamente para SuperAdmin.
-- Esta ficha se registra oculta en el menú funcional; se abre desde BuildAdministrar.
-- Nunca se escriben TA_MENU ni TA_TAREAS.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(100) NULL;
EXEC(N'
IF NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N''ALFA'' AND Clave=N''ADMIN-SERVIDOR-WEB'')
    INSERT dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb,PadreClave)
    VALUES(N''ALFA'',N''ADMIN-SERVIDOR-WEB'',N''/admin/servidor'',N''AdminServidor'',N''bi-hdd-rack'',0,99998,0,N''Servidor'',N''ADMINISTRAR'');
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/admin/servidor'',Componente=N''AdminServidor'',Icono=N''bi-hdd-rack'',
    HabilitadoWeb=0,NombreWeb=N''Servidor'',PadreClave=N''ADMINISTRAR''
    WHERE Menu=N''ALFA'' AND Clave=N''ADMIN-SERVIDOR-WEB'';
');
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB(Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)),LTRIM(RTRIM(t.Sistema)),N'ADMIN-SERVIDOR-WEB',GETDATE(),N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(t.Clave,N'')<>N'' AND NOT EXISTS(
        SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N'ADMIN-SERVIDOR-WEB');

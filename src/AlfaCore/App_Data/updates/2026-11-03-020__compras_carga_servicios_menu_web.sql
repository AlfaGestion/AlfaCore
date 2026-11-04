-- Preparación de compras: vista previa sin escrituras contables. Solo menú y permisos web.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(100) NULL;
EXEC sys.sp_executesql N'
IF NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N''ALFA'' AND Clave=N''D50-CARGA-COMPRAS-WEB'')
    INSERT dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb,PadreClave)
    VALUES(N''ALFA'',N''D50-CARGA-COMPRAS-WEB'',N''/compras/carga'',N''ComprasCarga'',N''bi-journal-plus'',1,50045,0,N''Preparar compra'',N''D50'');
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/compras/carga'',Componente=N''ComprasCarga'',Icono=N''bi-journal-plus'',
    HabilitadoWeb=1,NombreWeb=N''Preparar compra'',PadreClave=N''D50'',OrdenWeb=50045
    WHERE Menu=N''ALFA'' AND Clave=N''D50-CARGA-COMPRAS-WEB'';';
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB(Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.Usuario)),LTRIM(RTRIM(t.Sistema)),N'D50-CARGA-COMPRAS-WEB',GETDATE(),N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(t.Clave,N'')<>N'' AND NOT EXISTS(
        SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N'D50-CARGA-COMPRAS-WEB');

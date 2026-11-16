-- Opción web "Cobranzas" dentro de Ventas, con la misma clave que el legacy (D6003).
-- Menú y permisos web propios: no escribe TA_MENU ni TA_TAREAS (esta última solo se lee).
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;

EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,PadreClave,NombreWeb,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault)
SELECT N''ALFA'',N''D6003'',N''D60'',N''Cobranzas'',N''/ventas/cobranzas'',N''Cobranzas'',N''bi-cash-coin'',1,60003,0
WHERE NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N''ALFA'' AND Clave=N''D6003'');
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave=N''D60'',NombreWeb=N''Cobranzas'',RutaWeb=N''/ventas/cobranzas'',Componente=N''Cobranzas'',
    Icono=N''bi-cash-coin'',HabilitadoWeb=1,OrdenWeb=60003,
    DescripcionWeb=N''Recibo CB de un cliente: comprobantes pendientes y medios de pago (banco, efectivo y retenciones).''
WHERE Menu=N''ALFA'' AND Clave=N''D6003'';';

-- Permiso inicial: quienes ya tienen Cobranzas (D6003) en el legacy. El resto se habilita
-- desde la administración de permisos. TA_TAREAS solo se consulta.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.ALFACORE_TAREAS_WEB(Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N''D6003'',GETDATE(),N''MIGRACION-COBRANZAS''
    FROM dbo.TA_TAREAS t WHERE UPPER(LTRIM(RTRIM(t.TAREA)))=N''D6003''
    AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N''D6003'');';
GO

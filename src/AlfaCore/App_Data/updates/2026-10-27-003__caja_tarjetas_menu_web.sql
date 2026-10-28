-- Opción web propia: no modifica las conciliaciones legacy D7543/D7545.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D75-TARJETAS-IA' AND ISNULL(Proceso,N'')<>N'ConciliacionTarjetas')
    BEGIN RAISERROR(N'La clave D75-TARJETAS-IA ya pertenece a otra opción. Revisar antes de instalar.', 16, 1); RETURN; END;
DECLARE @Creada bit=0;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D75-TARJETAS-IA')
BEGIN
    INSERT INTO dbo.TA_MENU(Menu,Titulo,Clave,Nombre,Imagen,Proceso,Habilitado,ORDEN)
    SELECT TOP (1) Menu,N'D75',N'D75-TARJETAS-IA',N'Conciliación de tarjetas',N'bi-credit-card',N'ConciliacionTarjetas',1,N'7546'
    FROM dbo.TA_MENU WHERE Clave=N'D75';
    IF @@ROWCOUNT>0 SET @Creada=1;
END;
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
SELECT m.Menu,m.Clave,N''/caja-bancos/conciliacion-tarjetas'',N''ConciliacionTarjetas'',N''bi-credit-card'',1,7546,0,N''Conciliación de tarjetas''
FROM dbo.TA_MENU m WHERE m.Clave=N''D75-TARJETAS-IA''
AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/caja-bancos/conciliacion-tarjetas'',Componente=N''ConciliacionTarjetas'',
Icono=N''bi-credit-card'',HabilitadoWeb=1,NombreWeb=N''Conciliación de tarjetas'' WHERE Clave=N''D75-TARJETAS-IA'';';
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D75'' WHERE Clave=N''D75-TARJETAS-IA'';';
-- Solo completa la descripción de una opción creada por este script.
IF @Creada=1 AND COL_LENGTH(N'dbo.TA_MENU',N'DESCRIPCION') IS NOT NULL
    UPDATE dbo.TA_MENU SET DESCRIPCION=N'Importación de liquidaciones y conciliación de tarjetas presentadas, sin registrar LT.'
    WHERE Clave=N'D75-TARJETAS-IA' AND ISNULL(CONVERT(nvarchar(max),DESCRIPCION),N'')=N'';
IF OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.TA_TAREAS(USUARIO,SISTEMA,TAREA)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N'D75-TARJETAS-IA' FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA,N'')<>N'' AND NOT EXISTS(
        SELECT 1 FROM dbo.TA_TAREAS x WHERE UPPER(LTRIM(RTRIM(x.USUARIO)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
        AND UPPER(LTRIM(RTRIM(x.SISTEMA)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
        AND UPPER(LTRIM(RTRIM(x.TAREA)))=N'D75-TARJETAS-IA');
-- El shell vigente usa además permisos web materializados, igual que Cierre de caja.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB(Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT t.Usuario,t.Sistema,N'D75-TARJETAS-IA',GETDATE(),N'MIGRACION-CONCILIACION'
    FROM dbo.ALFACORE_TAREAS_WEB t WHERE ISNULL(t.Clave,N'')<>N''
    AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N'D75-TARJETAS-IA');

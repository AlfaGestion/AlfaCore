-- Reporte de Ventas: clave web nueva confirmada como libre en DISTRIWALTERP.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D60RPTWEB' AND ISNULL(Proceso,N'')<>N'ReporteVentas')
BEGIN
    RAISERROR(N'La clave D60RPTWEB corresponde a otra opción. Revisar el menú antes de instalar Reporte de Ventas.',16,1);
    RETURN;
END;
DECLARE @Creada bit=0;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D60RPTWEB')
BEGIN
    INSERT INTO dbo.TA_MENU(Menu,Titulo,Clave,Nombre,Imagen,Proceso,Habilitado,ORDEN)
    SELECT TOP(1) Menu,N'D60',N'D60RPTWEB',N'Reporte de Ventas',N'bi-bar-chart-line',N'ReporteVentas',1,N'60007'
    FROM dbo.TA_MENU WHERE Clave=N'D60';
    IF @@ROWCOUNT>0 SET @Creada=1;
END;
-- Referencias dinámicas para bases que todavía no tenían NombreWeb.
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
SELECT m.Menu,N''D60RPTWEB'',N''/ventas/reportes'',N''ReporteVentas'',N''bi-bar-chart-line'',1,60007,0,N''Reporte de Ventas''
FROM dbo.TA_MENU m WHERE m.Clave=N''D60RPTWEB'' AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/ventas/reportes'',Componente=N''ReporteVentas'',Icono=N''bi-bar-chart-line'',NombreWeb=N''Reporte de Ventas'',HabilitadoWeb=1 WHERE Clave=N''D60RPTWEB'';';
-- Solo la fila que acaba de crearse: no altera opciones legacy existentes.
IF @Creada=1 AND COL_LENGTH(N'dbo.TA_MENU',N'DESCRIPCION') IS NOT NULL
    UPDATE dbo.TA_MENU SET DESCRIPCION=N'Resumen, detalle y cuenta corriente de ventas.'
    WHERE Clave=N'D60RPTWEB' AND ISNULL(CONVERT(nvarchar(max),DESCRIPCION),N'')=N'';
IF OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.TA_TAREAS(USUARIO,SISTEMA,TAREA)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N'D60RPTWEB' FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA,N'')<>N'' AND NOT EXISTS(
        SELECT 1 FROM dbo.TA_TAREAS x WHERE UPPER(LTRIM(RTRIM(x.USUARIO)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
        AND UPPER(LTRIM(RTRIM(x.SISTEMA)))=UPPER(LTRIM(RTRIM(t.SISTEMA))) AND UPPER(LTRIM(RTRIM(x.TAREA)))=N'D60RPTWEB');

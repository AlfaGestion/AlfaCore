-- Carga de Comprobantes: consulta y simulación de pedidos/remitos.
-- Clave nueva D60CCW. No modifica opciones legacy ajenas a este módulo.
SET NOCOUNT ON;

-- 1. Guardia. Todo el script pertenece a un único batch.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NULL RETURN;
IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu=N'ALFA' AND Clave=N'D60') RETURN;
IF EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D60CCW' AND ISNULL(Proceso,N'')<>N'CargaComprobantes')
BEGIN
    RAISERROR(N'La clave D60CCW está ocupada por una opción legacy. Revisar antes de instalar Carga de Comprobantes.',16,1);
    RETURN;
END;

-- 2. Nombre web para bases anteriores. Las referencias posteriores son dinámicas
-- para que SQL Server no intente resolver la columna antes de agregarla.
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu=N'ALFA' AND Clave=N'D60CCW')
BEGIN
    INSERT INTO dbo.TA_MENU (Menu,Titulo,Clave,Nombre,Imagen,Proceso,Habilitado,ORDEN)
    VALUES (N'ALFA',N'D60',N'D60CCW',N'Carga de Comprobantes',N'bi-file-earmark-plus',N'CargaComprobantes',1,N'60006');
END;

-- 3. INSERT idempotente de la opción web.
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB
    (Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,Observacion,NombreWeb)
SELECT N''ALFA'',N''D60CCW'',N''/ventas/carga-comprobantes'',N''VentasCargaComprobantes'',
    N''bi-file-earmark-plus'',1,60006,0,
    N''Consulta y simulación de facturas desde pedidos y remitos.'',N''Carga de Comprobantes''
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N''ALFA'' AND Clave=N''D60CCW'');';

-- 4. UPDATE del mapeo web; no cambia el árbol legacy existente.
EXEC sys.sp_executesql N'
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb=N''/ventas/carga-comprobantes'',Componente=N''VentasCargaComprobantes'',
    Icono=N''bi-file-earmark-plus'',HabilitadoWeb=1,NombreWeb=N''Carga de Comprobantes''
WHERE Menu=N''ALFA'' AND Clave=N''D60CCW'';';
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D60'' WHERE Menu=N''ALFA'' AND Clave=N''D60CCW'';';

-- 5. Descripción únicamente para la opción propia y si estaba vacía.
IF COL_LENGTH(N'dbo.TA_MENU',N'Descripcion') IS NOT NULL
    EXEC sys.sp_executesql N'
    UPDATE dbo.TA_MENU SET Descripcion=N''Consulta y simulación de facturas desde pedidos y remitos.''
    WHERE Menu=N''ALFA'' AND Clave=N''D60CCW'' AND Proceso=N''CargaComprobantes''
        AND LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(max),Descripcion),N'''')))=N'''';';

-- 6. Usuarios con restricciones explícitas. Sin filas se conserva la política global.
IF OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO,SISTEMA,TAREA)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N'D60CCW'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA,N'')<>N''
      AND NOT EXISTS (SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA)))=N'D60CCW');
END;

-- El shell actual consulta permisos web materializados; se agrega solo la clave
-- nueva para los usuarios que ya tienen restricciones explícitas en esa tabla.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (USUARIO,SISTEMA,Clave)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N'D60CCW'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(t.Clave,N'')<>N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))=N'D60CCW');
END;

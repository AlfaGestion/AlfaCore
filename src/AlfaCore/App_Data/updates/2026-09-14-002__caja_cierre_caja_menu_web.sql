-- 1. Guardia. No modifica opciones existentes de TA_MENU.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

-- 2. Compatibilidad con bases antiguas.
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;

-- 3. Alta de los nodos web. D75 fue deshabilitado al trasladar sus tableros.
INSERT INTO dbo.ALFACORE_MENU_WEB
    (Menu,Clave,PadreClave,NombreWeb,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault)
SELECT N'ALFA',N'D75',N'D',N'Caja y Bancos',N'/shell/D75',N'ShellWorkspacePage',N'bi-bank',1,61,0
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N'ALFA' AND Clave=N'D75');
INSERT INTO dbo.ALFACORE_MENU_WEB
    (Menu,Clave,PadreClave,NombreWeb,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault)
SELECT N'ALFA',N'D75-CIERRE',N'D75',N'Cierre de caja',N'/caja-bancos/cierre',N'CierreCaja',N'bi-cash-stack',1,10,0
WHERE NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N'ALFA' AND Clave=N'D75-CIERRE');

-- 4. Reejecución y posición inmediatamente después de Ventas.
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave=N'D', NombreWeb=N'Caja y Bancos', HabilitadoWeb=1,
    RutaWeb=N'/shell/D75', Componente=N'ShellWorkspacePage', Icono=N'bi-bank',
    OrdenWeb=ISNULL((SELECT TOP (1) OrdenWeb FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N'ALFA' AND Clave=N'D60'),60)+1
WHERE Menu=N'ALFA' AND Clave=N'D75';
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave=N'D75',NombreWeb=N'Cierre de caja',RutaWeb=N'/caja-bancos/cierre',Componente=N'CierreCaja',
    Icono=N'bi-cash-stack',HabilitadoWeb=1,OrdenWeb=10,
    DescripcionWeb=N'Consulta del cierre por fecha operativa y caja: saldos, cobranzas, ventas y movimientos.'
WHERE Menu=N'ALFA' AND Clave=N'D75-CIERRE';

-- 5. TA_MENU: solo alta de la nueva clave; las filas legacy existentes quedan intactas.
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NOT NULL
BEGIN
    DECLARE @Nuevos TABLE (Clave nvarchar(50));
    INSERT INTO dbo.TA_MENU (Menu,Titulo,Clave,Nombre,Habilitado,ORDEN)
    OUTPUT inserted.Clave INTO @Nuevos
    SELECT N'ALFA',N'D',N'D75',N'Caja y Bancos',1,N'61'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu=N'ALFA' AND Clave=N'D75');
    INSERT INTO dbo.TA_MENU (Menu,Titulo,Clave,Nombre,Habilitado,ORDEN)
    OUTPUT inserted.Clave INTO @Nuevos
    SELECT N'ALFA',N'D75',N'D75-CIERRE',N'Cierre de caja',1,N'10'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Menu=N'ALFA' AND Clave=N'D75-CIERRE');
    IF COL_LENGTH(N'dbo.TA_MENU',N'Descripcion') IS NOT NULL
        UPDATE m SET Descripcion=CASE WHEN m.Clave=N'D75' THEN N'Caja y Bancos'
            ELSE N'Consulta del cierre de caja por fecha operativa.' END
        FROM dbo.TA_MENU m INNER JOIN @Nuevos n ON n.Clave=m.Clave
        WHERE m.Menu=N'ALFA' AND ISNULL(CAST(m.Descripcion AS nvarchar(max)),N'')=N'';
END;

-- 6. Usuarios con permisos explícitos (legacy y catálogo web vigente).
IF OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.TA_TAREAS (USUARIO,SISTEMA,TAREA)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N'D75-CIERRE'
    FROM dbo.TA_TAREAS t WHERE ISNULL(t.TAREA,N'')<>N''
    AND NOT EXISTS (SELECT 1 FROM dbo.TA_TAREAS x
        WHERE UPPER(LTRIM(RTRIM(x.USUARIO)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
          AND UPPER(LTRIM(RTRIM(x.SISTEMA)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
          AND UPPER(LTRIM(RTRIM(x.TAREA)))=N'D75-CIERRE');
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT t.Usuario,t.Sistema,N'D75-CIERRE',GETDATE(),N'MIGRACION-CIERRE'
    FROM dbo.ALFACORE_TAREAS_WEB t WHERE ISNULL(t.Clave,N'')<>N''
    AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.Usuario)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.Sistema)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N'D75-CIERRE');
GO

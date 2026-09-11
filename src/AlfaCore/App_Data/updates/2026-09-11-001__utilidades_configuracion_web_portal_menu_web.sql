/* Configuración Web / Portal: identidad pública común de la empresa. */
SET NOCOUNT ON;

-- 1. Guardia
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
GO

-- 2. Compatibilidad con bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO

-- 3. Alta idempotente en ALFACORE_MENU_WEB, debajo de Utilidades (D98)
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'DCONFIGWEBPORTAL')
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
        (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion, NombreWeb, DescripcionWeb, PadreClave)
    VALUES
        (N'ALFA', N'DCONFIGWEBPORTAL', N'/configuracion-web-portal', N'ConfiguracionWebPortal', N'bi-globe2', 1, 98610, 0,
         N'Identidad comercial y visual para Portal Cliente, catálogos, carrito y emails públicos.',
         N'Configuración Web / Portal', N'Nombre, contacto, sitio web y logo público de la empresa.', N'D98');
END;
GO

-- 4. Actualización idempotente del mapeo web
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/configuracion-web-portal', Componente = N'ConfiguracionWebPortal', Icono = N'bi-globe2',
    HabilitadoWeb = 1, OrdenWeb = 98610, NombreWeb = N'Configuración Web / Portal',
    Observacion = N'Identidad comercial y visual para Portal Cliente, catálogos, carrito y emails públicos.'
WHERE Menu = N'ALFA' AND Clave = N'DCONFIGWEBPORTAL';
GO

-- 5. Descripción en TA_MENU (solo si el árbol legacy tiene la clave)
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.TA_MENU', N'Descripcion') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = N'DCONFIGWEBPORTAL')
       AND EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE Clave = N'D98')
    BEGIN
        INSERT INTO dbo.TA_MENU (Menu, Titulo, Clave, Nombre, Imagen, Proceso, Habilitado, ORDEN, DESCRIPCION)
        SELECT TOP (1) Menu, N'D98', N'DCONFIGWEBPORTAL', N'Configuración Web / Portal', N'bi-globe2',
               N'ConfiguracionWebPortal', 1, N'98610', N'Identidad comercial y visual para Portal Cliente, catálogos, carrito y emails públicos.'
        FROM dbo.TA_MENU WHERE Clave = N'D98';
    END;

    UPDATE dbo.TA_MENU
    SET DESCRIPCION = COALESCE(NULLIF(CAST(DESCRIPCION AS nvarchar(max)), N''), N'Identidad comercial y visual para Portal Cliente, catálogos, carrito y emails públicos.')
    WHERE Clave = N'DCONFIGWEBPORTAL';
END;
GO

-- 6. Permisos para usuarios con restricciones explícitas
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'DCONFIGWEBPORTAL'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.TA_TAREAS x
          WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
            AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
            AND UPPER(LTRIM(RTRIM(x.TAREA))) = N'DCONFIGWEBPORTAL');
END;
GO

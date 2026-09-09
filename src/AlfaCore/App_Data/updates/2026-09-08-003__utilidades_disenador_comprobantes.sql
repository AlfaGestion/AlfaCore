/*
   Etapa 1 - Diseñador de comprobantes.
   Tablas CORE_ aisladas de las estructuras legacy. Idempotente.
*/
SET NOCOUNT ON;
GO

-- 1. Guardia
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;
GO

-- 2. Compatibilidad de bases antiguas
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CORE_DocumentTemplate
    (
        IdTemplate int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CORE_DocumentTemplate PRIMARY KEY,
        UNegocio nvarchar(4) NULL,
        TipoDocumento nvarchar(30) NOT NULL,
        Nombre nvarchar(100) NOT NULL,
        TemplateJson nvarchar(max) NOT NULL,
        CssCustom nvarchar(max) NULL,
        EsSistema bit NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_EsSistema DEFAULT 0,
        EsPredeterminado bit NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_EsPredeterminado DEFAULT 0,
        Activo bit NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_Activo DEFAULT 1,
        FechaAlta datetime2 NOT NULL CONSTRAINT DF_CORE_DocumentTemplate_FechaAlta DEFAULT SYSDATETIME(),
        FechaModificacion datetime2 NULL,
        UsuarioModificacion nvarchar(50) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.CORE_DocumentTemplate') AND name = N'IX_CORE_DocumentTemplate_UNegocio_Tipo_Activo')
    CREATE INDEX IX_CORE_DocumentTemplate_UNegocio_Tipo_Activo ON dbo.CORE_DocumentTemplate(UNegocio, TipoDocumento, Activo);
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplateVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CORE_DocumentTemplateVersion
    (
        IdVersion bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CORE_DocumentTemplateVersion PRIMARY KEY,
        IdTemplate int NOT NULL,
        Version int NOT NULL,
        TemplateJson nvarchar(max) NOT NULL,
        CssCustom nvarchar(max) NULL,
        Fecha datetime2 NOT NULL CONSTRAINT DF_CORE_DocumentTemplateVersion_Fecha DEFAULT SYSDATETIME(),
        Usuario nvarchar(50) NULL,
        CONSTRAINT FK_CORE_DocumentTemplateVersion_Template FOREIGN KEY (IdTemplate) REFERENCES dbo.CORE_DocumentTemplate(IdTemplate)
    );
END;
GO

-- Seed: global, de sistema y sólo una vez.
IF NOT EXISTS (SELECT 1 FROM dbo.CORE_DocumentTemplate WHERE TipoDocumento = N'COTIZACION' AND UNegocio IS NULL AND EsSistema = 1)
BEGIN
    INSERT INTO dbo.CORE_DocumentTemplate (UNegocio, TipoDocumento, Nombre, TemplateJson, EsSistema, EsPredeterminado, Activo, UsuarioModificacion)
    VALUES (NULL, N'COTIZACION', N'Cotización estándar', N'{"schemaVersion":1,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"header-logo","type":"Logo","visible":true,"align":"right","width":120},{"id":"company","type":"Empresa","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":14,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":42,"align":"left"},{"field":"Cantidad","title":"Cant.","visible":true,"widthPercent":11,"align":"right"},{"field":"Precio","title":"Precio","visible":true,"widthPercent":16,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":17,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"observations","type":"Observaciones","visible":true}]}', 1, 1, 1, N'MIGRACION-DOCUMENTOS');
END;
GO

-- 3. Menú web
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N'ALFA' AND Clave = N'D98-DOCUMENTOS')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, Observacion, NombreWeb, DescripcionWeb, PadreClave)
    VALUES (N'ALFA', N'D98-DOCUMENTOS', N'/documentos/disenador', N'DisenadorComprobantes', N'bi-file-earmark-richtext', 1, 9825, 0, N'Diseño de plantillas de comprobantes.', N'Diseñador de comprobantes', N'Plantillas de Cotización y vista previa PDF beta.', N'D98');
GO

-- 4. Actualización idempotente del menú web
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N'/documentos/disenador', Componente = N'DisenadorComprobantes', Icono = N'bi-file-earmark-richtext',
    NombreWeb = N'Diseñador de comprobantes', DescripcionWeb = N'Plantillas de Cotización y vista previa PDF beta.', PadreClave = N'D98'
WHERE Menu = N'ALFA' AND Clave = N'D98-DOCUMENTOS';
GO

-- 5. Descripción legacy: se crea la clave sólo si aún no existe en TA_MENU.
IF OBJECT_ID(N'dbo.TA_MENU', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TA_MENU WHERE LTRIM(RTRIM(Clave)) = N'D98-DOCUMENTOS')
        INSERT INTO dbo.TA_MENU (Menu, Titulo, Clave, Nombre, Imagen, Proceso, Habilitado, ORDEN, DESCRIPCION)
        VALUES (N'ALFA', N'D98', N'D98-DOCUMENTOS', N'Diseñador de comprobantes', N'bi-file-earmark-richtext', N'', 1, N'D9825', N'Plantillas de Cotización y vista previa PDF beta.');
    UPDATE dbo.TA_MENU SET DESCRIPCION = N'Plantillas de Cotización y vista previa PDF beta.'
    WHERE LTRIM(RTRIM(Clave)) = N'D98-DOCUMENTOS' AND ISNULL(CAST(DESCRIPCION AS nvarchar(max)), N'') = N'';
END;
GO

-- 6. Permisos para usuarios con restricciones explícitas.
IF OBJECT_ID(N'dbo.TA_TAREAS', N'U') IS NOT NULL
    INSERT INTO dbo.TA_TAREAS (USUARIO, SISTEMA, TAREA)
    SELECT DISTINCT t.USUARIO, t.SISTEMA, N'D98-DOCUMENTOS'
    FROM dbo.TA_TAREAS t
    WHERE ISNULL(t.TAREA, N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.TA_TAREAS x WHERE UPPER(LTRIM(RTRIM(x.USUARIO))) = UPPER(LTRIM(RTRIM(t.USUARIO))) AND UPPER(LTRIM(RTRIM(x.SISTEMA))) = UPPER(LTRIM(RTRIM(t.SISTEMA))) AND UPPER(LTRIM(RTRIM(x.TAREA))) = N'D98-DOCUMENTOS');
GO

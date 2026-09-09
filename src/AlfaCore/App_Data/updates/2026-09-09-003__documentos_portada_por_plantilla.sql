/*
    Diseñador de comprobantes - la portada deja de ser una imagen global de Cotizaciones
    (TA_LOGOS, IDLOGO='COT_PORTADA') y pasa a ser parte de CADA PLANTILLA, igual que el resto del
    diseño -- así cada unidad de negocio puede tener su propia portada, junto con todo lo demás.
    Agrega la columna binaria y suma el bloque "Portada" a la plantilla de sistema (mismo criterio
    de no pisar plantillas ya personalizadas por un usuario real que ya vienen aplicando las
    migraciones anteriores de Documentos).
    Idempotente.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL
    RETURN;
GO

IF COL_LENGTH(N'dbo.CORE_DocumentTemplate', N'PortadaImagen') IS NULL
    ALTER TABLE dbo.CORE_DocumentTemplate ADD PortadaImagen varbinary(max) NULL;
GO

DECLARE @PortadaVieja varbinary(max) = NULL;
IF OBJECT_ID(N'dbo.TA_LOGOS', N'U') IS NOT NULL
    SELECT TOP (1) @PortadaVieja = IMAGEN FROM dbo.TA_LOGOS WHERE IDLOGO = N'COT_PORTADA';

UPDATE dbo.CORE_DocumentTemplate
SET TemplateJson = N'{"schemaVersion":1,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"cover","type":"Portada","visible":true},{"id":"header-logo","type":"Logo","visible":true,"align":"right","width":120},{"id":"company","type":"Empresa","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"proposal","type":"Propuesta","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":14,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":42,"align":"left"},{"field":"Cantidad","title":"Cant.","visible":true,"widthPercent":11,"align":"right"},{"field":"Precio","title":"Precio","visible":true,"widthPercent":16,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":17,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"signature","type":"Firma","visible":true}]}',
    -- Si TA_LOGOS todavía tiene la portada global vieja, se copia a la columna nueva de la
    -- plantilla de sistema para no perder lo que el usuario ya había subido.
    PortadaImagen = ISNULL(@PortadaVieja, PortadaImagen),
    FechaModificacion = SYSDATETIME(), UsuarioModificacion = N'MIGRACION-DOCUMENTOS-4'
WHERE TipoDocumento = N'COTIZACION' AND EsSistema = 1
  AND (UsuarioModificacion IS NULL OR UsuarioModificacion IN (N'MIGRACION-DOCUMENTOS', N'MIGRACION-DOCUMENTOS-2', N'MIGRACION-DOCUMENTOS-3'));
GO

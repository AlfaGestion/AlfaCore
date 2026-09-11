/*
    Diseñador de comprobantes - la Propuesta comercial debe aparecer ANTES del detalle de productos
    y servicios, no después. Reordena los bloques de la plantilla de sistema.

    OJO: solo toca la fila si sigue siendo el default sin tocar (UsuarioModificacion todavía es una
    de las marcas de migración, o nunca se modificó). Si un usuario real ya la personalizó desde el
    diseñador (UsuarioModificacion con su nombre de usuario), esta migración NO la pisa -- pisar una
    plantilla que alguien ya armó a mano sería peor que dejar el orden viejo. Ese caso se resuelve a
    mano, por base, fusionando el JSON en vez de sobrescribirlo.
    Idempotente.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL
    RETURN;
GO

UPDATE dbo.CORE_DocumentTemplate
SET TemplateJson = N'{"schemaVersion":1,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"header-logo","type":"Logo","visible":true,"align":"right","width":120},{"id":"company","type":"Empresa","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"proposal","type":"Propuesta","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":14,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":42,"align":"left"},{"field":"Cantidad","title":"Cant.","visible":true,"widthPercent":11,"align":"right"},{"field":"Precio","title":"Precio","visible":true,"widthPercent":16,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":17,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"signature","type":"Firma","visible":true}]}',
    FechaModificacion = SYSDATETIME(), UsuarioModificacion = N'MIGRACION-DOCUMENTOS-3'
WHERE TipoDocumento = N'COTIZACION' AND EsSistema = 1
  AND (UsuarioModificacion IS NULL OR UsuarioModificacion IN (N'MIGRACION-DOCUMENTOS', N'MIGRACION-DOCUMENTOS-2'));
GO

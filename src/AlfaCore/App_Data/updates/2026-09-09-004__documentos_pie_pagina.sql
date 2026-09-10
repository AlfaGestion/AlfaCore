/*
    Diseñador de comprobantes - agrega el bloque "Pie" (pie de página opcional: número de página /
    nombre de la empresa, generado con el mecanismo nativo de Playwright, no con CSS) a la plantilla
    de sistema. Arranca OCULTO (visible:false) -- es un agregado opcional, no debe aparecer en los
    PDF existentes sin que el usuario lo habilite a propósito desde el diseñador.

    Mismo criterio de las migraciones anteriores de Documentos: si un usuario real ya personalizó la
    plantilla desde el diseñador (UsuarioModificacion con su nombre), esta migración NO la toca --
    ese caso se resuelve a mano, por base, fusionando el JSON en vez de sobrescribirlo.
    Idempotente.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL
    RETURN;
GO

UPDATE dbo.CORE_DocumentTemplate
SET TemplateJson = N'{"schemaVersion":1,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"cover","type":"Portada","visible":true},{"id":"header-logo","type":"Logo","visible":true,"align":"right","width":120},{"id":"company","type":"Empresa","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"proposal","type":"Propuesta","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":14,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":42,"align":"left"},{"field":"Cantidad","title":"Cant.","visible":true,"widthPercent":11,"align":"right"},{"field":"Precio","title":"Precio","visible":true,"widthPercent":16,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":17,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"signature","type":"Firma","visible":true},{"id":"footer","type":"Pie","visible":false}]}',
    FechaModificacion = SYSDATETIME(), UsuarioModificacion = N'MIGRACION-DOCUMENTOS-5'
WHERE TipoDocumento = N'COTIZACION' AND EsSistema = 1
  AND (UsuarioModificacion IS NULL OR UsuarioModificacion IN (N'MIGRACION-DOCUMENTOS', N'MIGRACION-DOCUMENTOS-2', N'MIGRACION-DOCUMENTOS-3', N'MIGRACION-DOCUMENTOS-4'));
GO

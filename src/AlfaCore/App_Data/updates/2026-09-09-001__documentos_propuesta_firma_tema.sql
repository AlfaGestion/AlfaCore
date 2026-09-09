/*
    Diseñador de comprobantes - fix: el bloque "Observaciones" del documento leía las Observaciones
    INTERNAS de la cotización (notas de uso interno del vendedor), que terminaban impresas en el PDF
    que recibe el cliente. Se reemplaza por dos bloques nuevos: "Propuesta" (el texto de la solapa
    Propuesta, pensado para el cliente) y "Firma". El tipo de bloque "Observaciones" ya no es válido
    (ver TiposBloqueDocumento.Permitidos en DocumentosModels.cs), así que hay que migrar cualquier
    plantilla de sistema existente que todavía lo tenga, o el diseñador rompe al cargarla.
    Idempotente.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL
    RETURN;
GO

UPDATE dbo.CORE_DocumentTemplate
SET TemplateJson = N'{"schemaVersion":1,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"header-logo","type":"Logo","visible":true,"align":"right","width":120},{"id":"company","type":"Empresa","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":14,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":42,"align":"left"},{"field":"Cantidad","title":"Cant.","visible":true,"widthPercent":11,"align":"right"},{"field":"Precio","title":"Precio","visible":true,"widthPercent":16,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":17,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"proposal","type":"Propuesta","visible":true},{"id":"signature","type":"Firma","visible":true}]}',
    FechaModificacion = SYSDATETIME(), UsuarioModificacion = N'MIGRACION-DOCUMENTOS-2'
WHERE TipoDocumento = N'COTIZACION' AND EsSistema = 1
  AND TemplateJson LIKE N'%"type":"Observaciones"%';
GO

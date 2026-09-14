-- Plantillas de crédito y débito A/B/C. Módulo existente: no modifica menú ni permisos.
-- Idempotente: conserva diseños y predeterminadas existentes.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.CORE_DocumentTemplate', N'U') IS NULL RETURN;

DECLARE @Base nvarchar(max) = N'{"schemaVersion":1,"totalesAlPiePagina":false,"paper":{"size":"A4","orientation":"Portrait","marginTopMm":10,"marginBottomMm":10,"marginLeftMm":10,"marginRightMm":10},"blocks":[{"id":"header-logo","type":"Logo","visible":true,"align":"left","width":100,"combineWithCompany":true},{"id":"company","type":"Empresa","visible":true},{"id":"recuadro-tipo","type":"RecuadroTipo","visible":true},{"id":"document","type":"Comprobante","visible":true},{"id":"customer","type":"Cliente","visible":true},{"id":"items","type":"Items","visible":true,"columns":[{"field":"Codigo","title":"Código","visible":true,"widthPercent":10,"align":"left"},{"field":"Descripcion","title":"Descripción","visible":true,"widthPercent":40,"align":"left"},{"field":"Cantidad","title":"Cantidad","visible":true,"widthPercent":10,"align":"right"},{"field":"Precio","title":"P.Unitario","visible":true,"widthPercent":20,"align":"right"},{"field":"Total","title":"Total","visible":true,"widthPercent":20,"align":"right"}]},{"id":"totals","type":"Totales","visible":true},{"id":"cae","type":"Cae","visible":true},{"id":"qr","type":"QrAfip","visible":true,"align":"left","width":30},{"id":"footer","type":"Pie","visible":false}]}';
INSERT INTO dbo.CORE_DocumentTemplate
    (UNegocio, TipoDocumento, Nombre, TemplateJson, CssCustom, EsSistema, EsPredeterminado, Activo, UsuarioModificacion)
SELECT NULL, tipos.Tipo, tipos.Nombre,
       COALESCE(base.TemplateJson, @Base), base.CssCustom, 1,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.CORE_DocumentTemplate d
            WHERE d.UNegocio IS NULL AND d.TipoDocumento = tipos.Tipo AND d.Activo = 1 AND d.EsPredeterminado = 1)
            THEN 0 ELSE 1 END, 1, N'MIGRACION-DOCUMENTOS'
FROM (VALUES
    (N'CREDITO_A', N'Nota de crédito A estándar', N'FACTURA_A'),
    (N'CREDITO_B', N'Nota de crédito B estándar', N'FACTURA_B'),
    (N'CREDITO_C', N'Nota de crédito C estándar', N'FACTURA_C'),
    (N'DEBITO_A', N'Nota de débito A estándar', N'FACTURA_A'),
    (N'DEBITO_B', N'Nota de débito B estándar', N'FACTURA_B'),
    (N'DEBITO_C', N'Nota de débito C estándar', N'FACTURA_C')
) tipos(Tipo, Nombre, Base)
OUTER APPLY (
    SELECT TOP (1) TemplateJson, CssCustom FROM dbo.CORE_DocumentTemplate
    WHERE TipoDocumento = tipos.Base AND UNegocio IS NULL AND EsSistema = 1 AND Activo = 1
    ORDER BY EsPredeterminado DESC, IdTemplate
) base
WHERE NOT EXISTS (SELECT 1 FROM dbo.CORE_DocumentTemplate d
    WHERE d.TipoDocumento = tipos.Tipo AND d.UNegocio IS NULL AND d.EsSistema = 1);

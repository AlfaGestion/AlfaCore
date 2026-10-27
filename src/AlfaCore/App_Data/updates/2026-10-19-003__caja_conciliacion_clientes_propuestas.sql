-- Repara únicamente nombres vacíos o marcadores heredados de alias en propuestas auxiliares.
-- No cambia cliente asignado, confianza, aplicaciones ni registros contables.
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.BK_MovimientoMatch',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.VT_CLIENTES') IS NULL RETURN;
UPDATE p
SET ClienteNombre=LTRIM(RTRIM(c.RAZON_SOCIAL)),
    ResumenPropuesta=CASE
        WHEN CHARINDEX(N' — . — ',p.ResumenPropuesta)>0
            THEN REPLACE(p.ResumenPropuesta,N' — . — ',N' — '+LTRIM(RTRIM(c.RAZON_SOCIAL))+N' — ')
        WHEN CHARINDEX(N' — — — ',p.ResumenPropuesta)>0
            THEN REPLACE(p.ResumenPropuesta,N' — — — ',N' — '+LTRIM(RTRIM(c.RAZON_SOCIAL))+N' — ')
        ELSE p.ResumenPropuesta END
FROM dbo.BK_MovimientoMatch p
JOIN dbo.VT_CLIENTES c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(p.ClienteCodigo))
WHERE ISNULL(LTRIM(RTRIM(p.ClienteNombre)),N'') IN(N'',N'.',N'—')
AND NULLIF(LTRIM(RTRIM(c.RAZON_SOCIAL)),N'') IS NOT NULL
AND LTRIM(RTRIM(c.RAZON_SOCIAL)) NOT IN(N'.',N'—');

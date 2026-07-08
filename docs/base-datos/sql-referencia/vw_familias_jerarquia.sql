 

/****** Object:  View [dbo].[vw_familias_jerarquia]    Script Date: 16/4/2026 09:41:03 ******/
IF OBJECT_ID('dbo.vw_familias_jerarquia', 'V') IS NOT NULL
BEGIN
	DROP VIEW [dbo].[vw_familias_jerarquia]
END
GO

/****** Object:  View [dbo].[vw_familias_jerarquia]    Script Date: 16/4/2026 09:41:03 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE   VIEW [dbo].[vw_familias_jerarquia]
AS
WITH Base AS
(
    SELECT
        f.IdFamilia,
        f.Descripcion,
        PadreIdFamilia =
        (
            SELECT TOP 1 p.IdFamilia
            FROM dbo.V_TA_FAMILIAS p
            WHERE f.IdFamilia LIKE p.IdFamilia + '%'
              AND p.IdFamilia <> f.IdFamilia
            ORDER BY LEN(p.IdFamilia) DESC
        )
    FROM dbo.V_TA_FAMILIAS f
)
SELECT
    b.IdFamilia,
    b.Descripcion,
    b.PadreIdFamilia,
    LargoCodigo = LEN(b.IdFamilia),

    NivelJerarquico =
        (
            SELECT COUNT(*)
            FROM dbo.V_TA_FAMILIAS x
            WHERE b.IdFamilia LIKE x.IdFamilia + '%'
        ),

    TieneHijos =
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.V_TA_FAMILIAS h
                WHERE h.IdFamilia LIKE b.IdFamilia + '%'
                  AND h.IdFamilia <> b.IdFamilia
            ) THEN 1
            ELSE 0
        END
FROM Base b;
GO



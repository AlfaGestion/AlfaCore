-- Recupera vistas de Compras que antes se distribuían sin versión y el actualizador omitía.
-- Solo crea objetos faltantes; conserva las definiciones existentes de cada cliente.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.vw_compras_cabecera_dashboard', N'V') IS NULL
BEGIN
    EXEC(N'CREATE   VIEW [dbo].[vw_compras_cabecera_dashboard]
AS

WITH CpteBase AS
(
    SELECT
        c.TC,
        c.IDCOMPROBANTE,
        c.NUMERO,
        c.FECHA,
        c.CUENTA,
        p.RAZON_SOCIAL,
        c.SUCURSAL,
        c.IdDeposito,
        c.USUARIO,

        c.IMPORTE,
        c.NetoGravado,
        c.ImporteIva,
        c.NetoNoGravado,

        c.ANULADA,
        c.Aprobado,
        c.Finalizado,
        c.Bloqueado,
        c.Cerrada,

        SignoBase =
            CASE
                WHEN c.TC IN (''FCC'',''NDC'',''LIQC'',''FPC'') THEN 1
                WHEN c.TC IN (''NCC'',''NCPC'') THEN -1
                ELSE 0
            END,

        TipoMovimientoBase =
            CASE
                WHEN c.TC IN (''FCC'',''NDC'',''LIQC'') THEN ''Compra''
                WHEN c.TC IN (''NCC'') THEN ''Nota Crédito''
                WHEN c.TC IN (''FPC'',''NCPC'') THEN ''Proforma''
                ELSE ''Otro''
            END,

        PasaLibroIVABase =
            CASE
                WHEN c.TC IN (''FCC'',''NDC'',''LIQC'',''NCC'') THEN 1
                ELSE 0
            END,

        OrigenRegistro = ''CPTE'',
        TipoRegistro   = ''Con detalle''
    FROM dbo.C_MV_Cpte c
    LEFT JOIN dbo.Vt_Proveedores p
        ON c.CUENTA = p.CODIGO
    WHERE c.ANULADA = 0
),
LivaFaltantes AS
(
    SELECT
        l.TC,
        IDCOMPROBANTE = l.SUCURSAL + l.NUMERO + l.LETRA,
        l.NUMERO,
        l.FECHA,
        l.CUENTA,
        p.RAZON_SOCIAL,
        l.SUCURSAL,
        CAST(NULL AS nvarchar(15)) AS IdDeposito,
        l.USUARIO_LOGEADO AS USUARIO,

        IMPORTE       = ISNULL(l.LIVA_TOTAL, 0),
        NetoGravado   = ISNULL(l.LIVA_ImpNetoGrav, 0),
        ImporteIva    = ISNULL(l.LIVA_ImpIVA, 0)
                      + ISNULL(l.LIVA_ImpIVARec, 0)
                      + ISNULL(l.LIVA_ImpIva2, 0)
                      + ISNULL(l.LIVA_ImpIVA3, 0)
                      + ISNULL(l.LIVA_ImpIVA4, 0),
        NetoNoGravado = ISNULL(l.LIVA_ImpNetoNGrav, 0)
                      + ISNULL(l.LIVA_EXENTO, 0),

        CAST(0 AS bit) AS ANULADA,
        CAST(0 AS bit) AS Aprobado,
        CAST(0 AS bit) AS Finalizado,
        CAST(0 AS bit) AS Bloqueado,
        CAST(0 AS bit) AS Cerrada,

        SignoBase =
            CASE
                WHEN l.[DEBE-HABER] = ''H'' THEN 1
                WHEN l.[DEBE-HABER] = ''D'' THEN -1
                ELSE 0
            END,

        TipoMovimientoBase =
            CASE
                WHEN l.[DEBE-HABER] = ''H'' THEN ''Libro IVA Positivo''
                WHEN l.[DEBE-HABER] = ''D'' THEN ''Libro IVA Negativo''
                ELSE ''Libro IVA''
            END,

        PasaLibroIVABase = 1,

        OrigenRegistro = ''LIVA'',
        TipoRegistro   = ''Contable''
    FROM dbo.LibroIvaCompras l
    LEFT JOIN dbo.Vt_Proveedores p
        ON l.CUENTA = p.CODIGO
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.C_MV_Cpte c
        WHERE c.ANULADA = 0
          AND c.CUENTA = l.CUENTA
          AND c.TC = l.TC
          AND c.IDCOMPROBANTE = l.SUCURSAL + l.NUMERO + l.LETRA
    )
)

SELECT
    x.TC,
    x.IDCOMPROBANTE,
    x.NUMERO,
    x.FECHA,
    x.CUENTA,
    x.RAZON_SOCIAL,
    x.SUCURSAL,
    x.IdDeposito,
    x.USUARIO,

    x.IMPORTE,
    x.NetoGravado,
    x.ImporteIva,
    x.NetoNoGravado,

    x.ANULADA,
    x.Aprobado,
    x.Finalizado,
    x.Bloqueado,
    x.Cerrada,

    x.SignoBase AS Signo,
    x.TipoMovimientoBase AS TipoMovimiento,
    x.PasaLibroIVABase AS PasaLibroIVA,

    x.OrigenRegistro,
    x.TipoRegistro,

    x.IMPORTE * x.SignoBase     AS ImporteDashboard,
    x.NetoGravado * x.SignoBase AS NetoDashboard,
    x.ImporteIva * x.SignoBase  AS IvaDashboard,

    CASE
        WHEN x.ANULADA = 1 THEN ''Anulada''
        ELSE ''Activo''
    END AS EstadoComprobante

FROM
(
    SELECT * FROM CpteBase
    UNION ALL
    SELECT * FROM LivaFaltantes
) x;');
END;
GO

IF OBJECT_ID(N'dbo.vw_compras_detalle_dashboard', N'V') IS NULL
BEGIN
    EXEC(N'CREATE VIEW [dbo].[vw_compras_detalle_dashboard]
AS
SELECT
c.TC,
c.IDCOMPROBANTE,
c.NUMERO,
c.FECHA,
c.CUENTA,
p.RAZON_SOCIAL,
c.SUCURSAL,
c.IdDeposito,
c.USUARIO,


d.IDARTICULO,
d.DESCRIPCION AS DESCRIPCION_ITEM,
d.CANTIDAD,
d.CANTIDADUD,
d.COSTO,
d.TOTAL,

a.DESCRIPCION AS DESCRIPCION_ARTICULO,
a.IDRUBRO,
r.Descripcion AS RUBRO,
a.IdFamilia,
f.Descripcion AS FAMILIA,
a.Presentacion,

CASE
    WHEN c.TC IN (''FCC'',''NDC'',''LIQC'',''FPC'') THEN 1
    WHEN c.TC IN (''NCC'',''NCPC'') THEN -1
    ELSE 0
END AS Signo,

d.CANTIDAD *
    CASE
        WHEN c.TC IN (''FCC'',''NDC'',''LIQC'',''FPC'') THEN 1
        WHEN c.TC IN (''NCC'',''NCPC'') THEN -1
        ELSE 0
    END AS CantidadDashboard,

d.TOTAL *
    CASE
        WHEN c.TC IN (''FCC'',''NDC'',''LIQC'',''FPC'') THEN 1
        WHEN c.TC IN (''NCC'',''NCPC'') THEN -1
        ELSE 0
    END AS TotalDashboard


FROM C_MV_Cpte c
INNER JOIN C_MV_CpteInsumos d
ON c.TC = d.TC
AND c.IDCOMPROBANTE = d.IDCOMPROBANTE
AND c.CUENTA = d.CUENTA
LEFT JOIN Vt_Proveedores p
ON c.CUENTA = p.CODIGO
LEFT JOIN V_MA_ARTICULOS a
ON d.IDARTICULO = a.IDARTICULO
LEFT JOIN V_TA_Rubros r
ON a.IDRUBRO = r.IdRubro
LEFT JOIN V_TA_FAMILIAS f
ON a.IdFamilia = f.IdFamilia
WHERE c.ANULADA = 0;');
END;
GO

IF OBJECT_ID(N'dbo.vw_familias_jerarquia', N'V') IS NULL
BEGIN
    EXEC(N'CREATE   VIEW [dbo].[vw_familias_jerarquia]
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
            WHERE f.IdFamilia LIKE p.IdFamilia + ''%''
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
            WHERE b.IdFamilia LIKE x.IdFamilia + ''%''
        ),

    TieneHijos =
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.V_TA_FAMILIAS h
                WHERE h.IdFamilia LIKE b.IdFamilia + ''%''
                  AND h.IdFamilia <> b.IdFamilia
            ) THEN 1
            ELSE 0
        END
FROM Base b;');
END;
GO

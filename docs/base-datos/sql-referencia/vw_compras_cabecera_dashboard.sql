 
/****** Object:  View [dbo].[vw_compras_cabecera_dashboard]    Script Date: 16/4/2026 09:43:14 ******/
IF OBJECT_ID('dbo.vw_compras_cabecera_dashboard', 'V') IS NOT NULL
BEGIN
    DROP VIEW [dbo].[vw_compras_cabecera_dashboard];
END
GO
/****** Object:  View [dbo].[vw_compras_cabecera_dashboard]    Script Date: 16/4/2026 09:43:14 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO


CREATE   VIEW [dbo].[vw_compras_cabecera_dashboard]
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
                WHEN c.TC IN ('FCC','NDC','LIQC','FPC') THEN 1
                WHEN c.TC IN ('NCC','NCPC','NCCP') THEN -1
                ELSE 0
            END,

        TipoMovimientoBase =
            CASE
                WHEN c.TC IN ('FCC','NDC','LIQC') THEN 'Compra'
                WHEN c.TC IN ('NCC') THEN 'Nota Crédito'
                WHEN c.TC IN ('FPC','NCPC','NCCP') THEN 'Proforma'
                ELSE 'Otro'
            END,

        PasaLibroIVABase =
            CASE
                WHEN c.TC IN ('FCC','NDC','LIQC','NCC') THEN 1
                ELSE 0
            END,

        OrigenRegistro = 'CPTE',
        TipoRegistro   = 'Con detalle'
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
                WHEN l.[DEBE-HABER] = 'H' THEN 1
                WHEN l.[DEBE-HABER] = 'D' THEN -1
                ELSE 0
            END,

        TipoMovimientoBase =
            CASE
                WHEN l.[DEBE-HABER] = 'H' THEN 'Libro IVA Positivo'
                WHEN l.[DEBE-HABER] = 'D' THEN 'Libro IVA Negativo'
                ELSE 'Libro IVA'
            END,

        PasaLibroIVABase = 1,

        OrigenRegistro = 'LIVA',
        TipoRegistro   = 'Contable'
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
        WHEN x.ANULADA = 1 THEN 'Anulada'
        ELSE 'Activo'
    END AS EstadoComprobante

FROM
(
    SELECT * FROM CpteBase
    UNION ALL
    SELECT * FROM LivaFaltantes
) x;
GO



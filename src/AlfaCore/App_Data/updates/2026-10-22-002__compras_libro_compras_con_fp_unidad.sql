-- ============================================================
-- Libro_ComprasConFP: suma CABNOMBRE y UNEGOCIO (2026-10-22)
-- El Reporte de Compras (ReporteComercialService) lee l.CABNOMBRE (razón social) y
-- filtra por l.UNEGOCIO (unidades de negocio), igual que Libro_VentasConFP.
-- La vista no tiene la misma definición en todas las bases: en algunas (p. ej.
-- DISTRIWALTERP) ya trae esas columnas y NO se toca. Solo se recrea donde falta
-- alguna de las dos, con la definición de 2026-06-08-002 más esas columnas.
-- CABNOMBRE toma el nombre del asiento y, si está vacío, la razón social del proveedor.
-- Idempotente: si la vista ya tiene ambas columnas no hace nada.
-- ============================================================

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID(N'dbo.Libro_ComprasConFP', N'V') IS NOT NULL
   AND COL_LENGTH(N'dbo.Libro_ComprasConFP', N'UNEGOCIO') IS NOT NULL
   AND COL_LENGTH(N'dbo.Libro_ComprasConFP', N'CABNOMBRE') IS NOT NULL
    RETURN;

IF OBJECT_ID(N'dbo.Libro_ComprasConFP', N'V') IS NULL
    EXEC(N'CREATE VIEW dbo.Libro_ComprasConFP AS SELECT 1 AS Stub;');

EXEC(N'
ALTER VIEW [dbo].[Libro_ComprasConFP]
AS

-- ── Parte 1: Libro IVA Compras desde MV_ASIENTOS ─────────────
SELECT
     a.[CUENTA]
    ,a.[FECHA]
    ,a.[DETALLE]
    ,a.[TC]
    ,a.[SUCURSAL]
    ,a.[NUMERO]
    ,a.[LETRA]
    ,a.[DEBE-HABER]
    ,CASE WHEN a.[DEBE-HABER] = ''H'' THEN  a.IMPORTE ELSE a.IMPORTE * -1 END AS IMPORTE
    ,CASE WHEN a.[DEBE-HABER] = ''H'' THEN  ISNULL(c.NetoGravado, 0) ELSE ISNULL(c.NetoGravado, 0) * -1 END AS IMPORTE_SINIVA
    ,a.[MONEDA]
    ,a.[COTIZACION]
    ,a.[VENCIMIENTO]
    ,a.[FechaHora_Grabacion]
    ,a.[LIVA_ImpNetoGrav]
    ,a.[LIVA_ImpNetoNGrav]
    ,a.[LIVA_EXENTO]
    ,a.[LIVA_AlicIVA]
    ,a.[LIVA_AlicIVAREC]
    ,a.[LIVA_ImpIVA]
    ,a.[LIVA_ImpIVARec]
    ,a.[LIVA_Ret_Perc]
    ,a.[LIVA_Ret_IBtos]
    ,a.[LIVA_Ret_Ganancias]
    ,a.[LIVA_TOTAL]
    ,a.[USUARIO_LOGEADO]
    ,a.[LIVA_AlicIva2]
    ,a.[LIVA_ImpIva2]
    ,a.[LIVA_AlicIVA3]
    ,a.[LIVA_ImpIVA3]
    ,a.[LIVA_AlicIVA4]
    ,a.[LIVA_ImpIVA4]
    ,CAST(YEAR(a.FECHA) AS VARCHAR) + ''-'' + RIGHT(''0'' + CAST(MONTH(a.FECHA) AS VARCHAR), 2) AS ANIO_MES
    ,a.[SUCURSAL] + a.[NUMERO] + a.[LETRA] AS IdComprobante
    ,p.RAZON_SOCIAL
    ,ISNULL(m.Descripcion, '''') AS DESC_MOTIVO
    -- Campos signados listos para SUM() directas
    ,CASE WHEN a.[DEBE-HABER] = ''H''
          THEN  ISNULL(a.LIVA_ImpNetoGrav, 0)
          ELSE -ISNULL(a.LIVA_ImpNetoGrav, 0)
     END AS NetoSignado
    ,CASE WHEN a.[DEBE-HABER] = ''H''
          THEN   ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0)
               + ISNULL(a.LIVA_ImpIva2, 0) + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0)
          ELSE -(ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0)
               + ISNULL(a.LIVA_ImpIva2, 0) + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0))
     END AS IvaSignado
    ,COALESCE(NULLIF(LTRIM(RTRIM(a.[CABNOMBRE])), ''''), p.RAZON_SOCIAL) AS CABNOMBRE
    ,a.[UNEGOCIO]
FROM dbo.MV_ASIENTOS a
LEFT JOIN dbo.Vt_Proveedores p
    ON p.CODIGO = a.CUENTA
LEFT JOIN dbo.C_MV_Cpte c
    ON c.TC           = a.TC
   AND c.IDCOMPROBANTE = a.SUCURSAL + a.NUMERO + a.LETRA
LEFT JOIN dbo.V_TA_MotivoCpra m
    ON m.IdMotivoCpra = c.IDMOTIVOCPRAVTA
WHERE a.LIVA_TIPO = N''COMPRAS''
  AND a.IMPORTE > 0

UNION

-- ── Parte 2: Comprobantes de forma de pago compras (FPC / NCPC) ─
SELECT
     a.[CUENTA]
    ,a.[FECHA]
    ,a.[DETALLE]
    ,a.[TC]
    ,a.[SUCURSAL]
    ,a.[NUMERO]
    ,a.[LETRA]
    ,a.[DEBE-HABER]
    ,CASE WHEN a.[DEBE-HABER] = ''H'' THEN  a.IMPORTE ELSE a.IMPORTE * -1 END AS IMPORTE
    ,CASE WHEN a.[DEBE-HABER] = ''H'' THEN  ISNULL(c.NetoGravado, 0) ELSE ISNULL(c.NetoGravado, 0) * -1 END AS IMPORTE_SINIVA
    ,a.[MONEDA]
    ,a.[COTIZACION]
    ,a.[VENCIMIENTO]
    ,a.[FechaHora_Grabacion]
    ,a.[LIVA_ImpNetoGrav]
    ,a.[LIVA_ImpNetoNGrav]
    ,a.[LIVA_EXENTO]
    ,a.[LIVA_AlicIVA]
    ,a.[LIVA_AlicIVAREC]
    ,a.[LIVA_ImpIVA]
    ,a.[LIVA_ImpIVARec]
    ,a.[LIVA_Ret_Perc]
    ,a.[LIVA_Ret_IBtos]
    ,a.[LIVA_Ret_Ganancias]
    ,a.[LIVA_TOTAL]
    ,a.[USUARIO_LOGEADO]
    ,a.[LIVA_AlicIva2]
    ,a.[LIVA_ImpIva2]
    ,a.[LIVA_AlicIVA3]
    ,a.[LIVA_ImpIVA3]
    ,a.[LIVA_AlicIVA4]
    ,a.[LIVA_ImpIVA4]
    ,CAST(YEAR(a.FECHA) AS VARCHAR) + ''-'' + RIGHT(''0'' + CAST(MONTH(a.FECHA) AS VARCHAR), 2) AS ANIO_MES
    ,a.[SUCURSAL] + a.[NUMERO] + a.[LETRA] AS IdComprobante
    ,p.RAZON_SOCIAL
    ,ISNULL(m.Descripcion, '''') AS DESC_MOTIVO
    ,CASE WHEN a.[DEBE-HABER] = ''H''
          THEN  ISNULL(a.LIVA_ImpNetoGrav, 0)
          ELSE -ISNULL(a.LIVA_ImpNetoGrav, 0)
     END AS NetoSignado
    ,CASE WHEN a.[DEBE-HABER] = ''H''
          THEN   ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0)
               + ISNULL(a.LIVA_ImpIva2, 0) + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0)
          ELSE -(ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0)
               + ISNULL(a.LIVA_ImpIva2, 0) + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0))
     END AS IvaSignado
    ,COALESCE(NULLIF(LTRIM(RTRIM(a.[CABNOMBRE])), ''''), p.RAZON_SOCIAL) AS CABNOMBRE
    ,a.[UNEGOCIO]
FROM dbo.MV_ASIENTOS a
LEFT JOIN dbo.Vt_Proveedores p
    ON p.CODIGO = a.CUENTA
LEFT JOIN dbo.C_MV_Cpte c
    ON c.TC           = a.TC
   AND c.IDCOMPROBANTE = a.SUCURSAL + a.NUMERO + a.LETRA
LEFT JOIN dbo.V_TA_MotivoCpra m
    ON m.IdMotivoCpra = c.IDMOTIVOCPRAVTA
WHERE (a.TC = N''FPC'' OR a.TC = N''NCPC'')
  AND a.SECUENCIA = 1
  AND a.IMPORTE > 0
');
GO

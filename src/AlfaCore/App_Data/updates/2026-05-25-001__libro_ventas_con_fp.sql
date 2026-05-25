IF OBJECT_ID(N'dbo.Libro_VentasConFP', N'V') IS NULL
BEGIN
    EXEC(N'CREATE VIEW dbo.Libro_VentasConFP AS SELECT 1 AS Stub;');
END
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

ALTER VIEW [dbo].[Libro_VentasConFP]
AS
SELECT    
       a.[CUENTA]
      ,a.[FECHA]
      ,[DETALLE]
      ,a.[TC]
      ,a.[SUCURSAL]
      ,a.[NUMERO]
      ,a.[LETRA]
      ,[DEBE-HABER]
      ,CASE WHEN [DEBE-HABER] = 'D' THEN a.IMPORTE ELSE a.IMPORTE * -1 END AS IMPORTE
      ,CASE WHEN [DEBE-HABER] = 'D' THEN c.IMPORTE_S_IVA ELSE c.IMPORTE_S_IVA * -1 END AS IMPORTE_SINIVA
      ,a.[MONEDA]
      ,a.[COTIZACION]
      ,[CABIMPORTE]
      ,[VENCIMIENTO]
      ,a.[FechaHora_Grabacion]
      ,[CABCUENTA]
      ,[CABNOMBRE]
      ,[CABCUIT]
      ,[CABCONDIVA]
      ,[LIVA_TIPO]
      ,[LIVA_ImpNetoGrav]
      ,[LIVA_ImpNetoNGrav]
      ,[LIVA_EXENTO]
      ,[LIVA_AlicIVA]
      ,[LIVA_AlicIVAREC]
      ,[LIVA_ImpIVA]
      ,[LIVA_ImpIVARec]
      ,[LIVA_Ret_Perc]
      ,[LIVA_Ret_IBtos]
      ,[LIVA_Ret_Ganancias]
      ,[LIVA_TOTAL]
      ,ISNULL(A.[IdVendedor], '') AS IdVendedor
      ,ISNULL(FechaSubdiario, a.FECHA) AS Fecha_Subdiario
      ,[LIVA_AlicIva2]
      ,[LIVA_ImpIva2]
      ,[LIVA_AlicIVA3]
      ,[LIVA_ImpIVA3]
      ,[LIVA_AlicIVA4]
      ,[LIVA_ImpIVA4]
      ,[USUARIO_LOGEADO]
      ,[Tc_Origen]
      ,[Cpte_Origen]
      ,[Complemento_Origen]
      ,[IdCajas]
      ,a.[SucursalCuenta]
      ,a.[UNEGOCIO]
      ,[COD_DESTINO]
      ,[NroLiqUNeg]
      ,[IDMOTIVO]
      ,[CodCpteDGI]
      ,[NroCAI]
      ,[FhVtoCAI]
      ,B.Nombre AS NOMBRE_VENDEDOR
      ,CAST(YEAR(a.FECHA) AS VARCHAR) + '-' + RIGHT('0' + CAST(MONTH(a.FECHA) AS VARCHAR), 2) AS ANIO_MES
      ,CASE WHEN (a.[TC] = 'NC' OR a.[TC] = 'NCFP') THEN c.KG * -1 ELSE c.KG END AS KG
      ,a.[SUCURSAL] + a.[NUMERO] + a.[LETRA] AS IdComprobante
FROM dbo.MV_ASIENTOS A
LEFT OUTER JOIN dbo.V_TA_VENDEDORES B
    ON A.IdVendedor = B.IdVendedor
LEFT OUTER JOIN dbo.V_MV_Cpte C
    ON a.tc = c.tc
   AND a.sucursal + a.NUMERO + a.LETRA = c.IDCOMPROBANTE
WHERE (LIVA_TIPO = N'VENTAS')
  AND a.FECHA >= '20240101'
  AND a.importe > 0

UNION

SELECT    
       a.[CUENTA]
      ,a.[FECHA]
      ,[DETALLE]
      ,a.[TC]
      ,a.[SUCURSAL]
      ,a.[NUMERO]
      ,a.[LETRA]
      ,[DEBE-HABER]
      ,CASE WHEN [DEBE-HABER] = 'D' THEN a.IMPORTE ELSE a.IMPORTE * -1 END AS IMPORTE
      ,CASE WHEN [DEBE-HABER] = 'D' THEN c.IMPORTE_S_IVA ELSE c.IMPORTE_S_IVA * -1 END AS IMPORTE_SINIVA
      ,a.[MONEDA]
      ,a.[COTIZACION]
      ,[CABIMPORTE]
      ,[VENCIMIENTO]
      ,a.[FechaHora_Grabacion]
      ,[CABCUENTA]
      ,[CABNOMBRE]
      ,[CABCUIT]
      ,[CABCONDIVA]
      ,[LIVA_TIPO]
      ,[LIVA_ImpNetoGrav]
      ,[LIVA_ImpNetoNGrav]
      ,[LIVA_EXENTO]
      ,[LIVA_AlicIVA]
      ,[LIVA_AlicIVAREC]
      ,[LIVA_ImpIVA]
      ,[LIVA_ImpIVARec]
      ,[LIVA_Ret_Perc]
      ,[LIVA_Ret_IBtos]
      ,[LIVA_Ret_Ganancias]
      ,[LIVA_TOTAL]
      ,ISNULL(A.[IdVendedor], '') AS IdVendedor
      ,ISNULL(FechaSubdiario, a.FECHA) AS Fecha_Subdiario
      ,[LIVA_AlicIva2]
      ,[LIVA_ImpIva2]
      ,[LIVA_AlicIVA3]
      ,[LIVA_ImpIVA3]
      ,[LIVA_AlicIVA4]
      ,[LIVA_ImpIVA4]
      ,[USUARIO_LOGEADO]
      ,[Tc_Origen]
      ,[Cpte_Origen]
      ,[Complemento_Origen]
      ,[IdCajas]
      ,a.[SucursalCuenta]
      ,a.[UNEGOCIO]
      ,[COD_DESTINO]
      ,[NroLiqUNeg]
      ,[IDMOTIVO]
      ,[CodCpteDGI]
      ,[NroCAI]
      ,[FhVtoCAI]
      ,B.Nombre AS NOMBRE_VENDEDOR
      ,CAST(YEAR(a.FECHA) AS VARCHAR) + '-' + RIGHT('0' + CAST(MONTH(a.FECHA) AS VARCHAR), 2) AS ANIO_MES
      ,CASE WHEN A.tc = 'NC' OR A.TC = 'NCFP' THEN C.KG * -1 ELSE c.KG END AS KG
      ,a.[SUCURSAL] + a.[NUMERO] + a.[LETRA] AS IdComprobante
FROM dbo.MV_ASIENTOS A
LEFT OUTER JOIN dbo.V_TA_VENDEDORES B
    ON A.IdVendedor = B.IdVendedor
LEFT OUTER JOIN dbo.V_MV_Cpte C
    ON a.tc = c.tc
   AND a.sucursal + a.NUMERO + a.LETRA = c.IDCOMPROBANTE
WHERE (a.TC = 'FP' OR a.TC = 'NCFP')
  AND SECUENCIA = 1
  AND a.FECHA >= '20200101'
  AND a.IMPORTE > 0;
GO

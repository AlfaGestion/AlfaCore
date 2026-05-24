 
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================
   1) INDICE PARA TA_CONFIGURACION
   Evita scan/RID lookup al buscar CLAVE -> VALOR
   ========================================================= */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_TA_CONFIGURACION_CLAVE'
      AND object_id = OBJECT_ID('dbo.TA_CONFIGURACION')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_TA_CONFIGURACION_CLAVE
    ON dbo.TA_CONFIGURACION (CLAVE)
    INCLUDE (VALOR);
END
GO

/* =========================================================
   2) INDICE PARA MV_ASIENTOS
   Alineado con:
   - DEBE-HABER = 'D'
   - EsResumen = 0
   - TC IN (...)
   - JOIN por CUENTA, SUCURSAL, NUMERO, LETRA
   ========================================================= */
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_MV_ASIENTOS_CPTES_IMPAGOS'
      AND object_id = OBJECT_ID('dbo.MV_ASIENTOS')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_MV_ASIENTOS_CPTES_IMPAGOS
    ON dbo.MV_ASIENTOS
    (
        [DEBE-HABER],
        EsResumen,
        TC,
        CUENTA,
        SUCURSAL,
        NUMERO,
        LETRA
    )
    INCLUDE
    (
        FECHA,
        IMPORTE,
        VENCIMIENTO,
        CABNOMBRE,
        MONEDA,
        IdVendedor,
        UNEGOCIO
    );
END
GO

/* =========================================================
   3) RECREAR VISTA BASE
   ========================================================= */
IF OBJECT_ID('dbo.VE_CPTES_IMPAGOS', 'V') IS NOT NULL
    DROP VIEW dbo.VE_CPTES_IMPAGOS;
GO

CREATE VIEW dbo.VE_CPTES_IMPAGOS
AS
WITH BASE AS
(
    SELECT
        A.CUENTA,
        A.FECHA,
        A.TC,
        A.SUCURSAL,
        A.NUMERO,
        A.LETRA,
        A.[DEBE-HABER],
        A.IMPORTE,
        A.MONEDA,
        A.VENCIMIENTO,
        A.CABNOMBRE,
        A.EsResumen,
        A.IdVendedor,
        A.UNEGOCIO
    FROM dbo.MV_ASIENTOS A
    WHERE
        A.[DEBE-HABER] = N'D'
        AND A.EsResumen = 0
)
SELECT
    B.CUENTA,
    B.FECHA,
    B.TC,
    B.SUCURSAL,
    B.NUMERO,
    B.LETRA,
    B.[DEBE-HABER],
    B.IMPORTE,
    B.MONEDA,
    B.VENCIMIENTO,
    B.CABNOMBRE,
    M.TipoVista,
    M.CuentaPrincipal,
    ISNULL(SA.IMPORTE, 0) AS PAGO,
    B.IMPORTE - ISNULL(SA.IMPORTE, 0) AS SALDO,
    B.EsResumen,
    B.IdVendedor,
    B.SUCURSAL + B.NUMERO + B.LETRA AS IDCOMPROBANTE,
    B.UNEGOCIO
FROM BASE B
INNER JOIN dbo.MA_CUENTAS M
    ON B.CUENTA = M.CODIGO
LEFT JOIN dbo.VE_SALDOAPLICACION SA
    ON  B.CUENTA   = SA.CUENTA
    AND B.TC       = SA.TCO_ORIGEN
    AND B.SUCURSAL = SA.SUCURSAL_ORIGEN
    AND B.NUMERO   = SA.NUMERO_ORIGEN
    AND B.LETRA    = SA.LETRA_ORIGEN
WHERE
    M.TipoVista IN (N'CL', N'PE', N'CM', N'PR', N'CB')
    AND B.IMPORTE - ISNULL(SA.IMPORTE, 0) <> 0;
GO

/* =========================================================
   4) RECREAR VISTA FINAL 2026
   Sin usar la UDF fn_TraerConfiguracion en el WHERE
   ========================================================= */
IF OBJECT_ID('dbo.VE_CPTES_IMPAGOS_2026', 'V') IS NOT NULL
    DROP VIEW dbo.VE_CPTES_IMPAGOS_2026;
GO

CREATE VIEW dbo.VE_CPTES_IMPAGOS_2026
AS
WITH CFG AS
(
    SELECT TOP (1)
        VALOR AS CUENTA_CF
    FROM dbo.TA_CONFIGURACION
    WHERE CLAVE = N'CUENTACONSUMIDORFINAL'
)
SELECT
    COALESCE(V.VENCIMIENTO, V.FECHA) AS VENCIMIENTO,
    V.FECHA,
    V.CUENTA,
    COALESCE(NULLIF(LTRIM(RTRIM(V.CABNOMBRE)), N''), M.DESCRIPCION) AS NOMBRE,
    V.TC,
    V.SUCURSAL,
    V.NUMERO,
    V.LETRA,
    V.IMPORTE,
    V.PAGO,
    V.SALDO,
    V.UNEGOCIO,
    V.IDCOMPROBANTE
FROM dbo.VE_CPTES_IMPAGOS V
LEFT JOIN dbo.MA_CUENTAS M
    ON V.CUENTA = M.CODIGO
CROSS JOIN CFG
WHERE
    V.TC IN (N'FC', N'FP', N'ND')
    AND (CFG.CUENTA_CF IS NULL OR V.CUENTA <> CFG.CUENTA_CF);
GO

/* =========================================================
   5) ACTUALIZAR ESTADISTICAS DE LAS TABLAS MAS CRITICAS
   ========================================================= */
UPDATE STATISTICS dbo.MV_ASIENTOS WITH FULLSCAN;
GO
UPDATE STATISTICS dbo.MA_CUENTAS WITH FULLSCAN;
GO
UPDATE STATISTICS dbo.MV_APLICACION WITH FULLSCAN;
GO
UPDATE STATISTICS dbo.TA_CONFIGURACION WITH FULLSCAN;
GO
 
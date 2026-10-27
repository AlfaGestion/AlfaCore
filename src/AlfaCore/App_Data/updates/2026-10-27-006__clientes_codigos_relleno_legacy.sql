/*
    Clientes/Proveedores: códigos de tablas auxiliares guardados sin el relleno legacy (2026-10-27).

    La pantalla de Cuentas comerciales de AlfaCore grababa IVA, tipo de documento, país, provincia y
    condición comercial recortados ("1") en vez de como están en su tabla ("   1"). Eso rompe la FK
    FK_V_MV_Cpte_TA_CONDIVA al grabar comprobantes del cliente (pedidos web y del asistente): relevado en
    G3 (483 clientes), LANUEVA (24) y ALFA NET (23). El código ya se corrigió; esto repara lo grabado.

    Sólo reemplaza un valor por el código EXACTO de su tabla cuando, sin espacios, coincide con uno y
    sólo un código (no adivina). El trigger TRN_MA_CUENTASADIC marca esas cuentas para retransmitir.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MA_CUENTASADIC', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.TA_CONDIVA') IS NOT NULL
    UPDATE a SET IVA = t.CODIGO
    FROM dbo.MA_CUENTASADIC a
    INNER JOIN (SELECT MIN(CODIGO) AS CODIGO, LTRIM(RTRIM(CODIGO)) AS Clave FROM dbo.TA_CONDIVA GROUP BY LTRIM(RTRIM(CODIGO)) HAVING COUNT(*) = 1) t
        ON t.Clave = LTRIM(RTRIM(a.IVA))
    WHERE a.IVA <> t.CODIGO OR DATALENGTH(a.IVA) <> DATALENGTH(t.CODIGO);

IF OBJECT_ID(N'dbo.TA_TIPODOCUMENTO') IS NOT NULL
    UPDATE a SET DOCUMENTO_TIPO = t.CODIGO
    FROM dbo.MA_CUENTASADIC a
    INNER JOIN (SELECT MIN(CODIGO) AS CODIGO, LTRIM(RTRIM(CODIGO)) AS Clave FROM dbo.TA_TIPODOCUMENTO GROUP BY LTRIM(RTRIM(CODIGO)) HAVING COUNT(*) = 1) t
        ON t.Clave = LTRIM(RTRIM(a.DOCUMENTO_TIPO))
    WHERE a.DOCUMENTO_TIPO <> t.CODIGO OR DATALENGTH(a.DOCUMENTO_TIPO) <> DATALENGTH(t.CODIGO);

IF OBJECT_ID(N'dbo.TA_PAISES') IS NOT NULL
    UPDATE a SET PAIS = t.CODIGO
    FROM dbo.MA_CUENTASADIC a
    INNER JOIN (SELECT MIN(CODIGO) AS CODIGO, LTRIM(RTRIM(CODIGO)) AS Clave FROM dbo.TA_PAISES GROUP BY LTRIM(RTRIM(CODIGO)) HAVING COUNT(*) = 1) t
        ON t.Clave = LTRIM(RTRIM(a.PAIS))
    WHERE a.PAIS <> t.CODIGO OR DATALENGTH(a.PAIS) <> DATALENGTH(t.CODIGO);

IF OBJECT_ID(N'dbo.TA_ESTADOS') IS NOT NULL
    UPDATE a SET PROVINCIA = t.CODIGO
    FROM dbo.MA_CUENTASADIC a
    INNER JOIN (SELECT MIN(CODIGO) AS CODIGO, LTRIM(RTRIM(CODIGO)) AS Clave FROM dbo.TA_ESTADOS GROUP BY LTRIM(RTRIM(CODIGO)) HAVING COUNT(*) = 1) t
        ON t.Clave = LTRIM(RTRIM(a.PROVINCIA))
    WHERE a.PROVINCIA <> t.CODIGO OR DATALENGTH(a.PROVINCIA) <> DATALENGTH(t.CODIGO);

IF OBJECT_ID(N'dbo.V_TA_Cpra_Vta') IS NOT NULL
    UPDATE a SET idCond_Cpra_Vta = t.CODIGO
    FROM dbo.MA_CUENTASADIC a
    INNER JOIN (SELECT MIN(IDCond_Cpra_Vta) AS CODIGO, LTRIM(RTRIM(IDCond_Cpra_Vta)) AS Clave FROM dbo.V_TA_Cpra_Vta GROUP BY LTRIM(RTRIM(IDCond_Cpra_Vta)) HAVING COUNT(*) = 1) t
        ON t.Clave = LTRIM(RTRIM(a.idCond_Cpra_Vta))
    WHERE a.idCond_Cpra_Vta <> t.CODIGO OR DATALENGTH(a.idCond_Cpra_Vta) <> DATALENGTH(t.CODIGO);

/* Conciliacion bancaria: el asiento de gastos bancarios usa el comprobante contable GB (Gasto bancario) en lugar de CC.
   - Da de alta GB en la tabla de comprobantes (V_TA_Cpte) solo si no existe: si el cliente ya lo creo, no se toca.
     Letra X, numeracion del 1 al 99999999, proximo 1, codigo ARCA 039 (otros comprobantes "A" con IVA discriminado).
   - Clave de configuracion CONCILIACION-GASTOS-COMPROBANTE (grupo CAJA) con el comprobante a usar; si no existe en la tabla se usa CC.
   - Guarda el comprobante usado en BK_AsientoGastos.
   Idempotente. Compatible con compatibility level 80/100. */
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.V_TA_Cpte',N'U') IS NOT NULL
   AND NOT EXISTS(SELECT 1 FROM dbo.V_TA_Cpte WHERE UPPER(LTRIM(RTRIM(CODIGO)))=N'GB')
    INSERT dbo.V_TA_Cpte(CODIGO,DESCRIPCION,SISTEMA,PIDEVENCIMIENTO,ModificaNumeracion,
        A_ULTIMO_NRO,A_NRO_DESDE,A_NRO_HASTA,A_SUC_DEFAULT,B_ULTIMO_NRO,B_NRO_DESDE,B_NRO_HASTA,B_SUC_DEFAULT,
        C_ULTIMO_NRO,C_NRO_DESDE,C_NRO_HASTA,C_SUC_DEFAULT,E_ULTIMO_NRO,E_NRO_DESDE,E_NRO_HASTA,E_SUC_DEFAULT,
        X_ULTIMO_NRO,X_NRO_DESDE,X_NRO_HASTA,X_SUC_DEFAULT,LETRAS,FechaHora_Grabacion,FechaHora_Modificacion,
        R_ULTIMO_NRO,R_NRO_DESDE,R_NRO_HASTA,R_SUC_DEFAULT,Transmision,Recepcion,
        M_ULTIMO_NRO,M_NRO_DESDE,M_NRO_HASTA,M_SUC_DEFAULT,Talonario,Externo,CpteDGIX)
    VALUES(N'GB',N'GASTO BANCARIO',N'Contable',0,1,
        0,0,0,1,0,0,0,1,
        0,0,0,1,0,0,0,1,
        1,1,99999999,1,N'X',GETDATE(),GETDATE(),
        0,0,0,1,0,0,
        0,0,0,1,0,0,
        -- CpteDGIX es nvarchar(2) en el legacy: ' 039' solo entra si ya fue ampliada (ver 2026-11-12-001)
        CASE WHEN COL_LENGTH('dbo.V_TA_Cpte','CpteDGIX')>=8 AND COL_LENGTH('dbo.TA_COMPROBANTES','CpteDGIX')>=8 THEN N' 039' END);

IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-GASTOS-COMPROBANTE')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-GASTOS-COMPROBANTE',N'GB',N'Tipo de comprobante contable (letra X) del asiento de gastos bancarios de la conciliacion bancaria');

IF OBJECT_ID(N'dbo.BK_AsientoGastos',N'U') IS NOT NULL AND COL_LENGTH('dbo.BK_AsientoGastos','Tc') IS NULL
    ALTER TABLE dbo.BK_AsientoGastos ADD Tc nvarchar(8) NULL;

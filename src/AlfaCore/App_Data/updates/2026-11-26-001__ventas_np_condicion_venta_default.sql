SET NOCOUNT ON;

-- Alfa Gestión completa la condición de venta de la NP cuando el cliente no
-- tiene IDCOND_CPRA_VTA informado. El SP web histórico no aplicaba ese
-- comportamiento y provocaba la FK FK_V_MV_Cpte_V_TA_Cpra_Vta.
IF OBJECT_ID(N'dbo.sp_web_Alta_Comprobante', N'P') IS NULL
    RETURN;

DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_Alta_Comprobante'));
IF @def IS NULL OR CHARINDEX(N'ALFACORE_NP_CONDICION_VENTA_DEFAULT', @def) > 0
    RETURN;

DECLARE @insert nvarchar(max) = N'
IF NULLIF(LTRIM(RTRIM(@idCondCpraVta)), '''') IS NULL
BEGIN
    -- ALFACORE_NP_CONDICION_VENTA_DEFAULT: equivalente a CondicionVentaDefault
    -- de em_V_mv_vta_npfp.frm; si no está configurada, VB6 usa 1 (CONTADO).
    SELECT TOP (1) @idCondCpraVta = IDCond_Cpra_Vta
    FROM dbo.V_TA_Cpra_Vta
    WHERE LTRIM(RTRIM(IDCond_Cpra_Vta)) = COALESCE(
        NULLIF((SELECT TOP (1) LTRIM(RTRIM(VALOR))
               FROM dbo.TA_CONFIGURACION
               WHERE UPPER(LTRIM(RTRIM(CLAVE))) = ''CONDICIONVENTADEFAULT''), ''''), ''1'');

    IF @idCondCpraVta IS NULL
        SELECT TOP (1) @idCondCpraVta = IDCond_Cpra_Vta
        FROM dbo.V_TA_Cpra_Vta
        WHERE LTRIM(RTRIM(IDCond_Cpra_Vta)) = ''1'';
END';

DECLARE @insertPos int = CHARINDEX(N'SET @idComprobante', @def);
IF @insertPos = 0
BEGIN
    RAISERROR('No se pudo localizar SET @idComprobante en sp_web_Alta_Comprobante.', 16, 1);
    RETURN;
END;

SET @def = STUFF(@def, @insertPos, 0, @insert + CHAR(13) + CHAR(10));
DECLARE @createPos int = PATINDEX(N'%CREATE%PROCEDURE%', @def);
IF CHARINDEX(N'CREATE OR ALTER PROCEDURE', @def) > 0
    SET @def = REPLACE(@def, N'CREATE OR ALTER PROCEDURE', N'ALTER PROCEDURE');
ELSE IF CHARINDEX(N'ALTER PROCEDURE', @def) > 0 OR CHARINDEX(N'ALTER PROC', @def) > 0
    SET @createPos = 0;
ELSE IF @createPos > 0
BEGIN
    DECLARE @procedurePos int = CHARINDEX(N'PROCEDURE', @def, @createPos);
    SET @def = STUFF(@def, @createPos, @procedurePos + LEN(N'PROCEDURE') - @createPos, N'ALTER PROCEDURE');
END;
ELSE
BEGIN
    SET @createPos = CHARINDEX(N'CREATE PROC', @def);
    IF @createPos > 0
        SET @def = STUFF(@def, @createPos, LEN(N'CREATE PROC'), N'ALTER PROC');
END;

IF CHARINDEX(N'ALTER PROCEDURE', @def) = 0 AND CHARINDEX(N'ALTER PROC', @def) = 0
BEGIN
    RAISERROR('No se pudo preparar sp_web_Alta_Comprobante para su actualización.', 16, 1);
    RETURN;
END;

EXEC sys.sp_executesql @def;

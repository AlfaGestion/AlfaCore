SET NOCOUNT ON;

-- Alfa Gestión completa la condición de venta de la NP cuando el cliente no
-- tiene IDCOND_CPRA_VTA informado. El SP web histórico no aplicaba ese
-- comportamiento y provocaba la FK FK_V_MV_Cpte_V_TA_Cpra_Vta.
IF OBJECT_ID(N'dbo.sp_web_Alta_Comprobante', N'P') IS NULL
    RETURN;

DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_web_Alta_Comprobante'));
IF @def IS NULL OR CHARINDEX(N'ALFACORE_NP_CONDICION_VENTA_DEFAULT', @def) > 0
    RETURN;

DECLARE @needle nvarchar(max) = N'IF @condicionIva IS NULL SET @condicionIva = ''   1''';
IF CHARINDEX(@needle, @def) = 0
    THROW 51001, 'No se pudo localizar el punto seguro para homologar la condición de venta de la NP en sp_web_Alta_Comprobante.', 1;

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

SET @def = REPLACE(@def, @needle, @needle + @insert);
SET @def = REPLACE(@def,
    N'CREATE PROCEDURE [dbo].[sp_web_Alta_Comprobante]',
    N'ALTER PROCEDURE [dbo].[sp_web_Alta_Comprobante]');
EXEC sys.sp_executesql @def;

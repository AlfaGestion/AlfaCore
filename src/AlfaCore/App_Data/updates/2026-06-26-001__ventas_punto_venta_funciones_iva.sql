--- Punto de venta - funciones de calculo de IVA
--- Requeridas por sp_web_CpteInsumos para descomponer/recomponer precios con IVA.
--- Estas funciones forman parte del esquema base de AlfaNet; se incluyen aqui
--- para bases que no las tengan.

-- CREATE OR ALTER requiere SQL Server 2016 SP1+; hay bases de clientes en motores más viejos
-- (ej.: SQL Server 2008), así que se usa el patrón clásico DROP + CREATE.
IF OBJECT_ID(N'[dbo].[FN_PRECIO_CON_IVA]', N'FN') IS NOT NULL
    DROP FUNCTION [dbo].[FN_PRECIO_CON_IVA];
GO
CREATE FUNCTION [dbo].[FN_PRECIO_CON_IVA] (@PRECIO_SIN_IVA MONEY, @ALIC_IVA FLOAT)
RETURNS MONEY AS
BEGIN
    DECLARE @EL_PRECIO_CON_IVA MONEY
    DECLARE @IVA_DEFAULT NVARCHAR(50)
    IF (@ALIC_IVA = 0) OR (@ALIC_IVA IS NULL)
        BEGIN
            SELECT @IVA_DEFAULT = VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'PIVA'
            SET @ALIC_IVA = CONVERT(FLOAT, @IVA_DEFAULT)
        END
    SET @EL_PRECIO_CON_IVA = @PRECIO_SIN_IVA + (@ALIC_IVA * @PRECIO_SIN_IVA / 100)
    RETURN @EL_PRECIO_CON_IVA
END
GO

IF OBJECT_ID(N'[dbo].[FN_PRECIO_SIN_IVA]', N'FN') IS NOT NULL
    DROP FUNCTION [dbo].[FN_PRECIO_SIN_IVA];
GO
CREATE FUNCTION [dbo].[FN_PRECIO_SIN_IVA] (@PRECIO_CON_IVA MONEY, @ALIC_IVA FLOAT)
RETURNS MONEY AS
BEGIN
    DECLARE @EL_PRECIO_SIN_IVA MONEY
    DECLARE @IVA_DEFAULT NVARCHAR(50)
    IF (@ALIC_IVA = 0) OR (@ALIC_IVA IS NULL)
        BEGIN
            SELECT @IVA_DEFAULT = VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'PIVA'
            SET @ALIC_IVA = CONVERT(FLOAT, @IVA_DEFAULT)
        END
    SET @EL_PRECIO_SIN_IVA = @PRECIO_CON_IVA / (1 + (@ALIC_IVA / 100))
    RETURN @EL_PRECIO_SIN_IVA
END
GO

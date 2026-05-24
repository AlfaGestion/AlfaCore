DROP FUNCTION IF EXISTS [dbo].[fn_TraerConfiguracion];
GO

CREATE FUNCTION [dbo].[fn_TraerConfiguracion]
(
    @Clave NVARCHAR(100)
)
RETURNS NVARCHAR(255)
AS
BEGIN
    DECLARE @Valor NVARCHAR(255);

    SELECT TOP 1
        @Valor = VALOR
    FROM TA_CONFIGURACION
    WHERE CLAVE = @Clave;

    RETURN @Valor;
END
GO
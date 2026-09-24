/*
    URLs configurables de WSAA y WSFE para compatibilidad con la configuración
    histórica del Punto de Venta VB6. Se guardan con ?wsdl/?WSDL, pero AlfaCore
    normaliza la URL antes de enviar el POST SOAP.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL
    RETURN;

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'WSAA_URL')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'WSAA_URL', N'https://wsaahomo.afip.gov.ar/ws/services/LoginCms?wsdl', N'URL WSAA según ambiente ARCA', GETDATE(), GETDATE());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'WSFE_URL')
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion, FechaHora_Modificacion)
    VALUES (N'ARCA', N'WSFE_URL', N'https://wswhomo.afip.gov.ar/wsfev1/service.asmx?WSDL', N'URL WSFE según ambiente ARCA', GETDATE(), GETDATE());
END;

-- Ajustes exclusivos de la copia. No anonimiza toda la información histórica.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
IF DB_NAME()<>N'ALFANET_DEMO' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
 THROW 51000,'El aislamiento solo puede aplicarse en ALFANET_DEMO de SERVER-ALFAVB6.',1;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE=N'DEMO-BASE-REINICIABLE')
 INSERT TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION,FechaHora_Grabacion)
 VALUES(N'CAJA',N'DEMO-BASE-REINICIABLE',N'ALFANET_DEMO',N'Base exclusiva para demostraciones reiniciables',GETDATE());
ELSE IF NOT EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE=N'DEMO-BASE-REINICIABLE' AND VALOR=N'ALFANET_DEMO')
 THROW 51000,'La marca de base de demostración no coincide.',1;
UPDATE TA_CONFIGURACION SET VALOR=N'',ValorAux=NULL
 WHERE CLAVE IN(N'Point_Token',N'MERCADOPAGO_ACCESS_TOKEN',N'MERCADOPAGO_WEBHOOK_SECRET',
 N'CONV_WHATSAPP_VERIFY_TOKEN',N'CONV_WHATSAPP_PUBLIC_BASE_URL',N'ARCA_RUTA_CRT',N'ARCA_RUTA_KEY',
 N'EMAIL_SERVER',N'EMAIL_USUARIO',N'EMAIL_PASSWORD',N'EMAIL_DE',N'EMAIL_PARA',N'EMAIL_CTA',N'EMAIL_PASS',
 N'CIERRECAJAEMAIL',N'GOUR_EMAIL_CAJAS',N'WEB_EMAIL');
UPDATE TA_CONFIGURACION SET VALOR=N'NO'
 WHERE CLAVE IN(N'FCAUTOENVIAREMAIL',N'EMAIL_AUTENTICACION');
-- Los campos existen en el relevamiento de TA_USUARIOS y unidades de esta base.
UPDATE TA_USUARIOS SET email_server=NULL,email_usuario=NULL,email_password=NULL,email_de=NULL,email_autenticacion=0;
UPDATE V_TA_UnidadNegocio SET RUTA_CRT=NULL,RUTA_KEY=NULL,USAEFC=0;
-- Dos procedimientos legacy leen explícitamente ALFANET: redirigirlos a la copia local.
DECLARE @nombre sysname,@def nvarchar(max);
DECLARE modulos CURSOR LOCAL FAST_FORWARD FOR
 SELECT o.name,m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id
 WHERE SCHEMA_NAME(o.schema_id)='dbo' AND o.name IN(N'NW_RECONS_ESTADISTICAS_PASO2',N'NW_RECONS_ESTADISTICASCPRA_PASO2');
OPEN modulos;FETCH NEXT FROM modulos INTO @nombre,@def;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @def=REPLACE(REPLACE(@def,N'Alfanet.dbo.',N'ALFANET_DEMO.dbo.'),N'[ALFANET].',N'[ALFANET_DEMO].');
 SET @def=STUFF(@def,CHARINDEX(N'CREATE',UPPER(@def)),6,N'ALTER');
 EXEC sys.sp_executesql @def;
 FETCH NEXT FROM modulos INTO @nombre,@def;
END;
CLOSE modulos;DEALLOCATE modulos;
-- El proceso de publicación de estadísticas escribe en otra base: queda deshabilitado en la demo.
IF OBJECT_ID(N'dbo.NW_RECONS_ESTV',N'P') IS NOT NULL
 EXEC(N'ALTER PROCEDURE dbo.NW_RECONS_ESTV @FECHA nvarchar(7),@TABLA nvarchar(50) AS BEGIN THROW 51000,''La base de demostración no publica estadísticas externas.'',1; END;');
IF EXISTS(SELECT 1 FROM sys.sql_expression_dependencies WHERE referenced_database_name IS NOT NULL
 AND referenced_database_name NOT IN(N'ALFANET_DEMO',N'master'))
 THROW 51000,'Persisten referencias SQL externas: revisar antes de crear el punto inicial.',1;
COMMIT;
SELECT DB_NAME() base_demo,N'Integraciones heredadas retiradas de la copia y referencias revisadas' estado FOR JSON PATH;

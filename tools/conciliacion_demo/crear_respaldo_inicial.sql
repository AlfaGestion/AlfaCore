SET NOCOUNT ON;
IF DB_NAME()<>N'ALFANET_DEMO' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
 THROW 51000,'El respaldo inicial solo corresponde a ALFANET_DEMO de SERVER-ALFAVB6.',1;
IF NOT EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE=N'DEMO-BASE-REINICIABLE' AND VALOR=N'ALFANET_DEMO')
 THROW 51000,'La copia todavía no fue preparada como base de demostración.',1;
IF EXISTS(SELECT 1 FROM BK_ImportacionExtracto WHERE CuentaBanco IN('111020901','111020902','111010904'))
 THROW 51000,'Ya hay extractos DEMO importados; no guardar este estado como punto inicial.',1;
DECLARE @carpeta nvarchar(4000),@fijo nvarchar(4000),@version nvarchar(4000),@existe int;
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\Microsoft\MSSQLServer\MSSQLServer',N'BackupDirectory',@carpeta OUTPUT;
IF @carpeta IS NULL THROW 51000,'No se encontró la carpeta de respaldos.',1;
SET @fijo=@carpeta+N'\ALFANET_DEMO.bak';
EXEC master.dbo.xp_fileexist @fijo,@existe OUTPUT;
IF @existe=1 THROW 51000,'El respaldo inicial ya existe. Archivar y revisar la versión anterior antes de renovarlo.',1;
SET @version=@carpeta+N'\ALFANET_DEMO_INICIAL_'+REPLACE(REPLACE(REPLACE(CONVERT(nvarchar(23),GETDATE(),126),'-',''),':',''),'.','')+N'.bak';
BACKUP DATABASE [ALFANET_DEMO] TO DISK=@version WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=@version WITH CHECKSUM;
BACKUP DATABASE [ALFANET_DEMO] TO DISK=@fijo WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=@fijo WITH CHECKSUM;
SELECT DB_NAME() base_demo,@fijo respaldo_inicial,@version respaldo_versionado FOR JSON PATH;

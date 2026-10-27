SET NOCOUNT ON;
IF DB_NAME()<>N'ALFANET' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
    THROW 51000,'El respaldo de esta demostración solo corresponde a SERVER-ALFAVB6 / ALFANET.',1;
DECLARE @carpeta nvarchar(4000),@archivo nvarchar(4000);
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\Microsoft\MSSQLServer\MSSQLServer',N'BackupDirectory',@carpeta OUTPUT;
IF @carpeta IS NULL THROW 51000,'No se encontró la carpeta de respaldos del servidor.',1;
SET @archivo=@carpeta+N'\ALFANET_ANTES_DEMO_'+REPLACE(REPLACE(REPLACE(CONVERT(nvarchar(23),GETDATE(),126),N'-',N''),N':',N''),N'.',N'')+N'.bak';
BACKUP DATABASE [ALFANET] TO DISK=@archivo WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=@archivo WITH CHECKSUM;
SELECT @archivo respaldo,N'COPY_ONLY y VERIFYONLY con CHECKSUM completados' verificacion FOR JSON PATH;
